using System.Collections.Generic;
using UnityEngine;
using static MassEngine.MassGpuShaderPropertyIds;

namespace MassEngine
{
    /// <summary>
    /// Test/diagnostics hook: receives the label of every kernel the orchestrator intends
    /// to dispatch, in dispatch order, BEFORE the null-shader guard. Lets EditMode tests
    /// assert the pipeline order without a GPU.
    /// </summary>
    public interface IDispatchListener
    {
        void OnDispatch(string kernelLabel);
    }

    /// <summary>
    /// GPU compute pipeline scheduler. Dispatch order (Requirement 9.1, density stage
    /// added in this engine): SpatialHash -> RuntimeFlow (conditional) -> DensityMap ->
    /// EngagementSlotOccupancy -> CombatSimulation -> ProjectileSimulation ->
    /// CollectActiveProjectiles -> LodClassification (once per unit type) -> buffer swap.
    /// </summary>
    public sealed class ComputePipelineOrchestrator
    {
        private readonly MassGpuShaderSet shaders;
        private readonly MassGpuBufferManager buffers;
        private readonly IDispatchListener dispatchListener;
        private readonly HashSet<string> reportedMissingKernels = new HashSet<string>();

        public ComputePipelineOrchestrator(MassGpuShaderSet shaders, MassGpuBufferManager buffers, IDispatchListener dispatchListener = null)
        {
            this.shaders = shaders;
            this.buffers = buffers;
            this.dispatchListener = dispatchListener;
        }

        public void DispatchFrame(PipelineFrameContext frameContext)
        {
            if (buffers == null || !buffers.IsAllocated)
                return;

            UploadFrameConstants(frameContext);
            BindComputeBuffers();

            DispatchSpatialHash(frameContext);

            if (frameContext.attackerFlow.rebuildThisFrame)
                DispatchRuntimeAttackerFlow(frameContext);
            if (frameContext.defenderFlow.rebuildThisFrame)
                DispatchRuntimeDefenderFlow(frameContext);

            if (frameContext.rebuildDensityMap)
                DispatchDensityMap(frameContext);

            DispatchCombatSimulation(frameContext);
            DispatchProjectileSimulation(frameContext);
            DispatchProjectileActiveList(frameContext);
            DispatchLodClassification(frameContext);
            buffers.SwapSimulationBuffers();
        }

        private void DispatchSpatialHash(PipelineFrameContext context)
        {
            Dispatch(shaders.SpatialHashShader, shaders.ClearGrid, Mathf.Max(1, context.gridThreadGroupsX), "ClearGrid");
            Dispatch(shaders.SpatialHashShader, shaders.BuildSpatialHash, Mathf.Max(1, context.agentThreadGroupsX), "BuildSpatialHash");
        }

        private void DispatchRuntimeAttackerFlow(PipelineFrameContext context)
        {
            int flowGroups = Mathf.Max(1, context.attackerFlow.threadGroupsX);
            Dispatch(shaders.RuntimeFlowShader, shaders.ClearRuntimeAttackerFlowResources, flowGroups, "ClearRuntimeAttackerFlowResources");
            Dispatch(shaders.RuntimeFlowShader, shaders.BuildRuntimeAttackerTargetDensity, Mathf.Max(1, context.agentThreadGroupsX), "BuildRuntimeAttackerTargetDensity");
            // One 64-thread group per sector (the kernel reduces its sector in groupshared memory).
            Dispatch(shaders.RuntimeFlowShader, shaders.SelectRuntimeAttackerFlowTargets, Mathf.Clamp(context.attackerFlow.sectorCount, 1, 8), "SelectRuntimeAttackerFlowTargets");
            Dispatch(shaders.RuntimeFlowShader, shaders.GenerateRuntimeAttackerFlowField, flowGroups, "GenerateRuntimeAttackerFlowField");
        }

        private void DispatchRuntimeDefenderFlow(PipelineFrameContext context)
        {
            int flowGroups = Mathf.Max(1, context.defenderFlow.threadGroupsX);
            Dispatch(shaders.RuntimeFlowShader, shaders.ClearRuntimeDefenderFlowResources, flowGroups, "ClearRuntimeDefenderFlowResources");
            Dispatch(shaders.RuntimeFlowShader, shaders.BuildRuntimeDefenderTargetDensity, Mathf.Max(1, context.agentThreadGroupsX), "BuildRuntimeDefenderTargetDensity");
            Dispatch(shaders.RuntimeFlowShader, shaders.SelectRuntimeDefenderFlowTargets, Mathf.Clamp(context.defenderFlow.sectorCount, 1, 8), "SelectRuntimeDefenderFlowTargets");
            Dispatch(shaders.RuntimeFlowShader, shaders.GenerateRuntimeDefenderFlowField, flowGroups, "GenerateRuntimeDefenderFlowField");
        }

        private void DispatchDensityMap(PipelineFrameContext context)
        {
            if (buffers.densityMapTexture == null ||
                buffers.attackerDensityMapTexture == null ||
                buffers.defenderDensityMapTexture == null)
                return;

            Dispatch(shaders.CombatSimulationShader, shaders.ClearDensityMap, Mathf.Max(1, context.densityMapThreadGroupsX), Mathf.Max(1, context.densityMapThreadGroupsY), "ClearDensityMap");
            Dispatch(shaders.CombatSimulationShader, shaders.BuildDensityMap, Mathf.Max(1, context.agentThreadGroupsX), "BuildDensityMap");
        }

        private void DispatchCombatSimulation(PipelineFrameContext context)
        {
            Dispatch(shaders.CombatSimulationShader, shaders.BuildEngagementSlotOccupancy, Mathf.Max(1, context.agentThreadGroupsX), "BuildEngagementSlotOccupancy");
            Dispatch(shaders.CombatSimulationShader, shaders.ClearPendingDamage, Mathf.Max(1, context.agentThreadGroupsX), "ClearPendingDamage");
            Dispatch(shaders.CombatSimulationShader, shaders.SimulateCombatAndAccumulateDamage, Mathf.Max(1, context.agentThreadGroupsX), "SimulateCombatAndAccumulateDamage");
        }

        private void DispatchProjectileSimulation(PipelineFrameContext context)
        {
            // Projectiles are part of the battle simulation and must freeze with it.
            // Their buffers are bound once by BindProjectileBuffers above.
            if (!context.battleStarted || context.projectileThreadGroupsX <= 0)
                return;

            Dispatch(
                shaders.ProjectileShader,
                shaders.SimulateProjectiles,
                Mathf.Max(1, context.projectileThreadGroupsX),
                "SimulateProjectiles");
        }

        /// <summary>
        /// Compresses the projectile pool into the append list the indirect draw reads,
        /// then publishes its count into the draw args. Runs AFTER SimulateProjectiles so
        /// slots released by this frame's hits and expiries are already excluded.
        /// </summary>
        private void DispatchProjectileActiveList(PipelineFrameContext context)
        {
            if (buffers.activeProjectileIndexBuffer == null || buffers.projectileDrawArgsBuffer == null)
                return;

            // Rebuilt every frame whether the battle runs or is paused: while paused the
            // pool contents are unchanged, so the list comes out identical and existing
            // trails stay on screen frozen instead of blinking out.
            buffers.ResetProjectileAppendCounter();

            if (context.projectileThreadGroupsX > 0)
            {
                SetBuffer(shaders.ProjectileShader, shaders.CollectActiveProjectiles, ProjectileBufferId, buffers.projectileBuffer);
                SetBuffer(shaders.ProjectileShader, shaders.CollectActiveProjectiles, ActiveProjectileIndicesId, buffers.activeProjectileIndexBuffer);
                DispatchOptional(shaders.ProjectileShader, shaders.CollectActiveProjectiles, context.projectileThreadGroupsX, "CollectActiveProjectiles");
            }

            // Always published, including on the skipped paths above: the args then carry
            // instance count 0 and the renderer draws nothing, rather than replaying a
            // stale count over a pool that has since been cleared.
            buffers.CopyProjectileCountToArgs();
        }

        /// <summary>
        /// Classification runs once per unit type: only that type's three append buffers
        /// are bound, keeping the UAV count flat regardless of how many unit types are
        /// registered (adding a unit type touches no shader code).
        /// </summary>
        private void DispatchLodClassification(PipelineFrameContext context)
        {
            for (int unitTypeIndex = 0; unitTypeIndex < context.unitTypeCount; unitTypeIndex++)
            {
                buffers.ResetAppendCounters(unitTypeIndex);

                ComputeShader shader = shaders.LodClassificationShader;
                int kernel = shaders.ClassifyVisibleAgentsForUnitType;
                if (shader != null)
                    shader.SetInt(ClassifyUnitTypeIndexId, unitTypeIndex);
                SetBuffer(shader, kernel, NearVisibleAgentIndicesId, buffers.GetVisibleIndexBuffer(unitTypeIndex, 0));
                SetBuffer(shader, kernel, MidVisibleAgentIndicesId, buffers.GetVisibleIndexBuffer(unitTypeIndex, 1));
                SetBuffer(shader, kernel, FarVisibleAgentIndicesId, buffers.GetVisibleIndexBuffer(unitTypeIndex, 2));

                Dispatch(shader, kernel, Mathf.Max(1, context.agentThreadGroupsX), "ClassifyVisibleAgentsForUnitType[" + unitTypeIndex + "]");
                buffers.CopyVisibleCountsToArgs(unitTypeIndex);
            }
        }

        private void UploadFrameConstants(PipelineFrameContext context)
        {
            shaders.SetFloat(DeltaTimeId, context.deltaTime);
            shaders.SetInt(FrameIndexId, context.frameIndex);
            shaders.SetVector(LodCenterId, context.lod.lodCenterPosition);
            shaders.SetFloat(NearLodRadiusSqrId, Mathf.Max(0f, context.lod.nearLodRadius) * Mathf.Max(0f, context.lod.nearLodRadius));
            shaders.SetFloat(MidLodRadiusSqrId, Mathf.Max(0f, context.lod.midLodRadius) * Mathf.Max(0f, context.lod.midLodRadius));
            shaders.SetInt(EnableFrustumCullingId, context.lod.frustumPlanes != null && context.lod.frustumPlanes.Length >= 6 ? 1 : 0);
            shaders.SetFloat(CullingRadiusId, Mathf.Max(0f, context.lod.cullingRadius));
            float maxRender = Mathf.Max(0f, context.lod.maxRenderDistance);
            shaders.SetFloat(MaxRenderDistanceSqrId, maxRender * maxRender);
            shaders.SetInt(FarIncludeDeadId, context.lod.farIncludeDead ? 1 : 0);
            shaders.SetInt(NearAnimationIntervalId, Mathf.Max(1, context.lod.nearAnimationInterval));
            shaders.SetInt(MidAnimationIntervalId, Mathf.Max(1, context.lod.midAnimationInterval));
            shaders.SetInt(FarAnimationIntervalId, Mathf.Max(1, context.lod.farAnimationInterval));
            shaders.SetInt(NearSimulationIntervalId, Mathf.Max(1, context.lod.nearSimulationInterval));
            shaders.SetInt(MidSimulationIntervalId, Mathf.Max(1, context.lod.midSimulationInterval));
            shaders.SetInt(FarSimulationIntervalId, Mathf.Max(1, context.lod.farSimulationInterval));

            shaders.SetInt(GridCellCountId, Mathf.Max(1, context.grid.resolutionX * context.grid.resolutionZ));
            shaders.SetInts(GridResolutionId, Mathf.Max(1, context.grid.resolutionX), Mathf.Max(1, context.grid.resolutionZ));
            shaders.SetVector(GridOriginId, new Vector4(context.grid.origin.x, context.grid.origin.y, 0f, 0f));
            shaders.SetVector(GridWorldSizeId, new Vector4(context.grid.worldSize.x, context.grid.worldSize.y, 0f, 0f));
            shaders.SetFloat(CellSizeId, Mathf.Max(0.1f, context.grid.cellSize));
            shaders.SetInt(MaxAgentsPerCellId, Mathf.Max(1, context.grid.maxAgentsPerCell));
            shaders.SetFloat(BoundaryPaddingId, Mathf.Max(0f, context.grid.boundaryPadding));

            shaders.SetInt(EnableTwoTeamCombatId, context.combatEnabled ? 1 : 0);
            shaders.SetInt(BattleStartedId, context.battleStarted ? 1 : 0);
            shaders.SetInt(AttackerTeamIdId, context.attackerTeamId);
            shaders.SetInt(DefenderTeamIdId, context.defenderTeamId);
            // Read from the buffer manager, not the frame context: this bounds every teamId
            // the kernels index team-partitioned buffers with, so it must match what was
            // actually allocated or a stray id writes out of bounds.
            shaders.SetInt(TeamCountId, Mathf.Max(1, buffers.TeamCount));
            shaders.SetInt(LocalTargetSearchCellRadiusId, Mathf.Max(1, context.localTargetSearchCellRadius));
            shaders.SetInt(DefenderMovementModeId, context.defenderMovementMode);
            shaders.SetFloat(DefenderGuardRadiusId, Mathf.Max(0f, context.defenderGuardRadius));

            UploadTeamFlowConstants(context.attackerFlow, FlowFieldEnabledId, FlowFieldResolutionId, FlowFieldOriginId, FlowFieldCellSizeId,
                AttackerFlowTargetModeId, AttackerFlowTargetPointId, AttackerFlowTargetAreaId,
                RuntimeDynamicAttackerFlowEnabledId, DynamicFlowSectorCountId, DynamicFlowTargetStopRadiusId, DynamicFlowMinDefendersPerTargetId);
            UploadTeamFlowConstants(context.defenderFlow, DefenderFlowFieldEnabledId, DefenderFlowFieldResolutionId, DefenderFlowFieldOriginId, DefenderFlowFieldCellSizeId,
                DefenderFlowTargetModeId, DefenderFlowTargetPointId, DefenderFlowTargetAreaId,
                RuntimeDynamicDefenderFlowEnabledId, DynamicDefenderFlowSectorCountId, DynamicDefenderFlowTargetStopRadiusId, DynamicDefenderFlowMinAttackersPerTargetId);

            shaders.SetInt(RuntimeFlowPreviewModeId, context.runtimeFlowPreviewMode);
            shaders.SetInt(FlowPreviewEnabledId, context.flowPreviewEnabled ? 1 : 0);
            shaders.SetInt(StaticObstacleCountId, Mathf.Clamp(context.staticObstacleCount, 0, StaticObstacleMath.MaxObstacleCount));
            shaders.SetFloat(StaticObstaclePaddingId, Mathf.Max(0f, context.staticObstaclePadding));
            if (context.staticObstacleRects != null && context.staticObstacleRects.Length > 0)
                shaders.SetVectorArray(StaticObstacleRectsId, context.staticObstacleRects);

            if (context.lod.frustumPlanes != null && context.lod.frustumPlanes.Length > 0)
                shaders.SetVectorArray(FrustumPlanesId, context.lod.frustumPlanes);

            // 弹道系统常量
            shaders.SetInt(MaxProjectilesId, buffers.MaxProjectiles);
            shaders.SetFloat(CurrentTimeId, context.simulationTime);
        }

        private void UploadTeamFlowConstants(
            TeamFlowFrameSettings flow,
            int enabledId, int resolutionId, int originId, int cellSizeId,
            int targetModeId, int targetPointId, int targetAreaId,
            int dynamicEnabledId, int sectorCountId, int stopRadiusId, int minAgentsId)
        {
            shaders.SetInt(enabledId, flow.enabled ? 1 : 0);
            shaders.SetInts(resolutionId, Mathf.Max(1, flow.resolutionX), Mathf.Max(1, flow.resolutionZ));
            shaders.SetVector(originId, new Vector4(flow.origin.x, flow.origin.y, 0f, 0f));
            shaders.SetFloat(cellSizeId, Mathf.Max(0.1f, flow.cellSize));
            shaders.SetInt(targetModeId, flow.targetMode);
            shaders.SetVector(targetPointId, new Vector4(flow.targetPoint.x, flow.targetPoint.z, 0f, 0f));
            shaders.SetVector(targetAreaId, new Vector4(
                flow.targetAreaCenter.x,
                flow.targetAreaCenter.z,
                Mathf.Max(0f, flow.targetAreaSize.x),
                Mathf.Max(0f, flow.targetAreaSize.z)));
            shaders.SetInt(dynamicEnabledId, flow.dynamicFlowEnabled ? 1 : 0);
            shaders.SetInt(sectorCountId, Mathf.Clamp(flow.sectorCount, 1, 8));
            shaders.SetFloat(stopRadiusId, Mathf.Max(0f, flow.targetStopRadius));
            shaders.SetInt(minAgentsId, Mathf.Max(1, flow.minAgentsPerTarget));
        }

        private void BindComputeBuffers()
        {
            BindSpatialHashBuffers();
            BindRuntimeFlowBuffers();
            BindCombatBuffers();
            BindProjectileBuffers();
            BindLodBuffers();
        }

        private void BindSpatialHashBuffers()
        {
            SetBuffer(shaders.SpatialHashShader, shaders.ClearGrid, AgentBufferId, buffers.agentBuffer);
            SetBuffer(shaders.SpatialHashShader, shaders.ClearGrid, GridCountsId, buffers.gridCountsBuffer);
            SetBuffer(shaders.SpatialHashShader, shaders.ClearGrid, TeamGridCountsId, buffers.teamGridCountsBuffer);
            SetBuffer(shaders.SpatialHashShader, shaders.ClearGrid, SpatialHashStatsId, buffers.spatialHashStatsBuffer);

            SetBuffer(shaders.SpatialHashShader, shaders.BuildSpatialHash, AgentBufferId, buffers.agentBuffer);
            SetBuffer(shaders.SpatialHashShader, shaders.BuildSpatialHash, AgentPositionReadBufferId, buffers.agentPositionReadBuffer);
            SetBuffer(shaders.SpatialHashShader, shaders.BuildSpatialHash, HpReadBufferId, buffers.combatBuffers.hpReadBuffer);
            SetBuffer(shaders.SpatialHashShader, shaders.BuildSpatialHash, GridCountsId, buffers.gridCountsBuffer);
            SetBuffer(shaders.SpatialHashShader, shaders.BuildSpatialHash, GridAgentIndicesId, buffers.gridAgentIndicesBuffer);
            SetBuffer(shaders.SpatialHashShader, shaders.BuildSpatialHash, TeamGridCountsId, buffers.teamGridCountsBuffer);
            SetBuffer(shaders.SpatialHashShader, shaders.BuildSpatialHash, TeamGridAgentIndicesId, buffers.teamGridAgentIndicesBuffer);
            SetBuffer(shaders.SpatialHashShader, shaders.BuildSpatialHash, TeamIdReadBufferId, buffers.combatBuffers.teamIdBuffer);
            SetBuffer(shaders.SpatialHashShader, shaders.BuildSpatialHash, SpatialHashStatsId, buffers.spatialHashStatsBuffer);
        }

        private void BindRuntimeFlowBuffers()
        {
            ComputeShader flow = shaders.RuntimeFlowShader;

            SetBuffer(flow, shaders.ClearRuntimeAttackerFlowResources, AgentBufferId, buffers.agentBuffer);
            SetBuffer(flow, shaders.ClearRuntimeAttackerFlowResources, RuntimeAttackerTargetDensityId, buffers.runtimeAttackerTargetDensityBuffer);
            SetBuffer(flow, shaders.ClearRuntimeAttackerFlowResources, RuntimeAttackerFlowStatsId, buffers.runtimeAttackerFlowStatsBuffer);
            SetBuffer(flow, shaders.ClearRuntimeAttackerFlowResources, RuntimeAttackerFlowTargetsId, buffers.runtimeAttackerFlowTargetsBuffer);

            SetBuffer(flow, shaders.BuildRuntimeAttackerTargetDensity, AgentBufferId, buffers.agentBuffer);
            SetBuffer(flow, shaders.BuildRuntimeAttackerTargetDensity, AgentPositionReadBufferId, buffers.agentPositionReadBuffer);
            SetBuffer(flow, shaders.BuildRuntimeAttackerTargetDensity, HpReadBufferId, buffers.combatBuffers.hpReadBuffer);
            SetBuffer(flow, shaders.BuildRuntimeAttackerTargetDensity, TeamIdReadBufferId, buffers.combatBuffers.teamIdBuffer);
            SetBuffer(flow, shaders.BuildRuntimeAttackerTargetDensity, RuntimeAttackerTargetDensityId, buffers.runtimeAttackerTargetDensityBuffer);
            SetBuffer(flow, shaders.BuildRuntimeAttackerTargetDensity, RuntimeAttackerFlowStatsId, buffers.runtimeAttackerFlowStatsBuffer);

            SetBuffer(flow, shaders.SelectRuntimeAttackerFlowTargets, AgentBufferId, buffers.agentBuffer);
            SetBuffer(flow, shaders.SelectRuntimeAttackerFlowTargets, RuntimeAttackerTargetDensityId, buffers.runtimeAttackerTargetDensityBuffer);
            SetBuffer(flow, shaders.SelectRuntimeAttackerFlowTargets, RuntimeAttackerFlowStatsId, buffers.runtimeAttackerFlowStatsBuffer);
            SetBuffer(flow, shaders.SelectRuntimeAttackerFlowTargets, RuntimeAttackerFlowTargetsId, buffers.runtimeAttackerFlowTargetsBuffer);

            SetBuffer(flow, shaders.GenerateRuntimeAttackerFlowField, AgentBufferId, buffers.agentBuffer);
            SetBuffer(flow, shaders.GenerateRuntimeAttackerFlowField, FlowFieldDirectionsId, buffers.flowFieldDirectionsBuffer);
            SetBuffer(flow, shaders.GenerateRuntimeAttackerFlowField, RuntimeAttackerTargetDensityId, buffers.runtimeAttackerTargetDensityBuffer);
            SetBuffer(flow, shaders.GenerateRuntimeAttackerFlowField, RuntimeAttackerFlowStatsId, buffers.runtimeAttackerFlowStatsBuffer);
            SetBuffer(flow, shaders.GenerateRuntimeAttackerFlowField, RuntimeAttackerFlowTargetsId, buffers.runtimeAttackerFlowTargetsBuffer);
            SetTexture(flow, shaders.GenerateRuntimeAttackerFlowField, RuntimeAttackerFlowPreviewTextureId, buffers.runtimeAttackerFlowPreviewTexture);

            SetBuffer(flow, shaders.ClearRuntimeDefenderFlowResources, AgentBufferId, buffers.agentBuffer);
            SetBuffer(flow, shaders.ClearRuntimeDefenderFlowResources, RuntimeDefenderTargetDensityId, buffers.runtimeDefenderTargetDensityBuffer);
            SetBuffer(flow, shaders.ClearRuntimeDefenderFlowResources, RuntimeDefenderFlowStatsId, buffers.runtimeDefenderFlowStatsBuffer);
            SetBuffer(flow, shaders.ClearRuntimeDefenderFlowResources, RuntimeDefenderFlowTargetsId, buffers.runtimeDefenderFlowTargetsBuffer);

            SetBuffer(flow, shaders.BuildRuntimeDefenderTargetDensity, AgentBufferId, buffers.agentBuffer);
            SetBuffer(flow, shaders.BuildRuntimeDefenderTargetDensity, AgentPositionReadBufferId, buffers.agentPositionReadBuffer);
            SetBuffer(flow, shaders.BuildRuntimeDefenderTargetDensity, HpReadBufferId, buffers.combatBuffers.hpReadBuffer);
            SetBuffer(flow, shaders.BuildRuntimeDefenderTargetDensity, TeamIdReadBufferId, buffers.combatBuffers.teamIdBuffer);
            SetBuffer(flow, shaders.BuildRuntimeDefenderTargetDensity, RuntimeDefenderTargetDensityId, buffers.runtimeDefenderTargetDensityBuffer);
            SetBuffer(flow, shaders.BuildRuntimeDefenderTargetDensity, RuntimeDefenderFlowStatsId, buffers.runtimeDefenderFlowStatsBuffer);

            SetBuffer(flow, shaders.SelectRuntimeDefenderFlowTargets, AgentBufferId, buffers.agentBuffer);
            SetBuffer(flow, shaders.SelectRuntimeDefenderFlowTargets, RuntimeDefenderTargetDensityId, buffers.runtimeDefenderTargetDensityBuffer);
            SetBuffer(flow, shaders.SelectRuntimeDefenderFlowTargets, RuntimeDefenderFlowStatsId, buffers.runtimeDefenderFlowStatsBuffer);
            SetBuffer(flow, shaders.SelectRuntimeDefenderFlowTargets, RuntimeDefenderFlowTargetsId, buffers.runtimeDefenderFlowTargetsBuffer);

            SetBuffer(flow, shaders.GenerateRuntimeDefenderFlowField, AgentBufferId, buffers.agentBuffer);
            SetBuffer(flow, shaders.GenerateRuntimeDefenderFlowField, DefenderFlowFieldDirectionsId, buffers.defenderFlowFieldDirectionsBuffer);
            SetBuffer(flow, shaders.GenerateRuntimeDefenderFlowField, RuntimeDefenderTargetDensityId, buffers.runtimeDefenderTargetDensityBuffer);
            SetBuffer(flow, shaders.GenerateRuntimeDefenderFlowField, RuntimeDefenderFlowStatsId, buffers.runtimeDefenderFlowStatsBuffer);
            SetBuffer(flow, shaders.GenerateRuntimeDefenderFlowField, RuntimeDefenderFlowTargetsId, buffers.runtimeDefenderFlowTargetsBuffer);
            SetTexture(flow, shaders.GenerateRuntimeDefenderFlowField, RuntimeDefenderFlowPreviewTextureId, buffers.runtimeDefenderFlowPreviewTexture);
        }

        private void BindCombatBuffers()
        {
            ComputeShader combat = shaders.CombatSimulationShader;

            SetBuffer(combat, shaders.ClearPendingDamage, AgentBufferId, buffers.agentBuffer);
            SetBuffer(combat, shaders.ClearPendingDamage, PendingDamageBufferId, buffers.combatBuffers.pendingDamageWriteBuffer);

            SetTexture(combat, shaders.ClearDensityMap, DensityMapWriteId, buffers.densityMapTexture);
            SetTexture(combat, shaders.ClearDensityMap, AttackerDensityMapWriteId, buffers.attackerDensityMapTexture);
            SetTexture(combat, shaders.ClearDensityMap, DefenderDensityMapWriteId, buffers.defenderDensityMapTexture);

            SetBuffer(combat, shaders.BuildDensityMap, AgentBufferId, buffers.agentBuffer);
            SetBuffer(combat, shaders.BuildDensityMap, HpReadBufferId, buffers.combatBuffers.hpReadBuffer);
            SetBuffer(combat, shaders.BuildDensityMap, TeamIdReadBufferId, buffers.combatBuffers.teamIdBuffer);
            SetTexture(combat, shaders.BuildDensityMap, DensityMapWriteId, buffers.densityMapTexture);
            SetTexture(combat, shaders.BuildDensityMap, AttackerDensityMapWriteId, buffers.attackerDensityMapTexture);
            SetTexture(combat, shaders.BuildDensityMap, DefenderDensityMapWriteId, buffers.defenderDensityMapTexture);

            SetBuffer(combat, shaders.BuildEngagementSlotOccupancy, AgentBufferId, buffers.agentBuffer);
            SetBuffer(combat, shaders.BuildEngagementSlotOccupancy, HpReadBufferId, buffers.combatBuffers.hpReadBuffer);
            SetBuffer(combat, shaders.BuildEngagementSlotOccupancy, TeamIdReadBufferId, buffers.combatBuffers.teamIdBuffer);
            SetBuffer(combat, shaders.BuildEngagementSlotOccupancy, TargetAgentIndexBufferId, buffers.combatBuffers.targetAgentIndexBuffer);
            SetBuffer(combat, shaders.BuildEngagementSlotOccupancy, EngagementSlotAssignmentBufferId, buffers.combatBuffers.engagementSlotAssignmentBuffer);
            SetBuffer(combat, shaders.BuildEngagementSlotOccupancy, EngagementSlotOccupancyBufferId, buffers.combatBuffers.engagementSlotOccupancyBuffer);

            int simulate = shaders.SimulateCombatAndAccumulateDamage;
            SetBuffer(combat, simulate, AgentBufferId, buffers.agentBuffer);
            SetBuffer(combat, simulate, AgentPositionReadBufferId, buffers.agentPositionReadBuffer);
            SetBuffer(combat, simulate, AgentPositionBufferId, buffers.agentPositionWriteBuffer);
            SetBuffer(combat, simulate, GridCountsReadBufferId, buffers.gridCountsBuffer);
            SetBuffer(combat, simulate, GridAgentIndicesReadBufferId, buffers.gridAgentIndicesBuffer);
            SetBuffer(combat, simulate, TeamGridCountsReadBufferId, buffers.teamGridCountsBuffer);
            SetBuffer(combat, simulate, TeamGridAgentIndicesReadBufferId, buffers.teamGridAgentIndicesBuffer);
            SetBuffer(combat, simulate, TeamIdReadBufferId, buffers.combatBuffers.teamIdBuffer);
            SetBuffer(combat, simulate, HpBufferId, buffers.combatBuffers.hpWriteBuffer);
            SetBuffer(combat, simulate, HpReadBufferId, buffers.combatBuffers.hpReadBuffer);
            SetBuffer(combat, simulate, TargetAgentIndexBufferId, buffers.combatBuffers.targetAgentIndexBuffer);
            SetBuffer(combat, simulate, EngagementSlotAssignmentBufferId, buffers.combatBuffers.engagementSlotAssignmentBuffer);
            SetBuffer(combat, simulate, EngagementSlotOccupancyReadBufferId, buffers.combatBuffers.engagementSlotOccupancyBuffer);
            SetBuffer(combat, simulate, AttackCooldownBufferId, buffers.combatBuffers.attackCooldownBuffer);
            SetBuffer(combat, simulate, HomePositionReadBufferId, buffers.combatBuffers.homePositionBuffer);
            SetBuffer(combat, simulate, PendingDamageBufferId, buffers.combatBuffers.pendingDamageWriteBuffer);
            SetBuffer(combat, simulate, PendingDamageReadBufferId, buffers.combatBuffers.pendingDamageReadBuffer);
            SetBuffer(combat, simulate, FlowFieldDirectionsReadBufferId, buffers.flowFieldDirectionsBuffer);
            SetBuffer(combat, simulate, DefenderFlowFieldDirectionsReadBufferId, buffers.defenderFlowFieldDirectionsBuffer);
            SetBuffer(combat, simulate, UnitTypeSettingsId, buffers.unitTypeSettingsBuffer);
            SetBuffer(combat, simulate, UnitTypeIndexReadBufferId, buffers.unitTypeIndexBuffer);
            SetBuffer(combat, simulate, LaunchRequestBufferId, buffers.combatBuffers.launchRequestBuffer);
            SetTexture(combat, simulate, DensityMapId, buffers.densityMapTexture);
            SetTexture(combat, simulate, AttackerDensityMapId, buffers.attackerDensityMapTexture);
            SetTexture(combat, simulate, DefenderDensityMapId, buffers.defenderDensityMapTexture);
        }

        private void BindProjectileBuffers()
        {
            ComputeShader projectile = shaders.ProjectileShader;
            int simulate = shaders.SimulateProjectiles;

            SetBuffer(projectile, simulate, ProjectileBufferId, buffers.projectileBuffer);
            SetBuffer(projectile, simulate, AgentPositionReadBufferId, buffers.agentPositionReadBuffer);
            SetBuffer(projectile, simulate, HpReadBufferId, buffers.combatBuffers.hpReadBuffer);
            SetBuffer(projectile, simulate, TeamIdReadBufferId, buffers.combatBuffers.teamIdBuffer);
            SetBuffer(projectile, simulate, PendingDamageBufferId, buffers.combatBuffers.pendingDamageWriteBuffer);
        }

        private void BindLodBuffers()
        {
            ComputeShader lod = shaders.LodClassificationShader;
            int classify = shaders.ClassifyVisibleAgentsForUnitType;

            SetBuffer(lod, classify, AgentBufferId, buffers.agentBuffer);
            SetBuffer(lod, classify, HpReadBufferId, buffers.combatBuffers.hpReadBuffer);
            SetBuffer(lod, classify, UnitTypeSettingsId, buffers.unitTypeSettingsBuffer);
            SetBuffer(lod, classify, UnitTypeIndexReadBufferId, buffers.unitTypeIndexBuffer);
            // The three per-unit-type append buffers are bound inside DispatchLodClassification.
        }

        private static void SetBuffer(ComputeShader shader, int kernel, int propertyId, ComputeBuffer buffer)
        {
            if (shader != null && kernel >= 0 && buffer != null)
                shader.SetBuffer(kernel, propertyId, buffer);
        }

        private static void SetTexture(ComputeShader shader, int kernel, int propertyId, RenderTexture texture)
        {
            if (shader != null && kernel >= 0 && texture != null)
                shader.SetTexture(kernel, propertyId, texture);
        }

        private void Dispatch(ComputeShader shader, int kernel, int groupsX, string label)
        {
            Dispatch(shader, kernel, groupsX, 1, label);
        }

        /// <summary>
        /// Dispatch for kernels that only drive optional visuals: a missing one degrades to
        /// a single warning instead of the hard error the simulation kernels report, because
        /// losing it must not read as a broken pipeline.
        /// </summary>
        private void DispatchOptional(ComputeShader shader, int kernel, int groupsX, string label)
        {
            if (dispatchListener != null)
                dispatchListener.OnDispatch(label);

            if (shader == null || kernel < 0)
            {
                if (reportedMissingKernels.Add(label))
                    Debug.LogWarning("MassEngine skipped optional GPU dispatch: " + label + " shader or kernel is missing (reported once) - the visuals it feeds stay off, simulation is unaffected.");
                return;
            }

            shader.Dispatch(kernel, Mathf.Max(1, groupsX), 1, 1);
        }

        private void Dispatch(ComputeShader shader, int kernel, int groupsX, int groupsY, string label)
        {
            if (dispatchListener != null)
                dispatchListener.OnDispatch(label);

            if (shader == null || kernel < 0)
            {
                // One-time report per kernel; a missing shader would otherwise spam the
                // console with several messages every frame.
                if (reportedMissingKernels.Add(label))
                    Debug.LogError("MassEngine skipped GPU dispatch: " + label + " shader or kernel is missing (reported once).");
                return;
            }

            shader.Dispatch(kernel, Mathf.Max(1, groupsX), Mathf.Max(1, groupsY), 1);
        }
    }
}
