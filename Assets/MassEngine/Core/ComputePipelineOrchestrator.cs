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
        /// <summary>Reused upload staging for the per-team flow records; resized only when the team count changes.</summary>
        private TeamFlowParams[] teamFlowParamsScratch = System.Array.Empty<TeamFlowParams>();

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

            DispatchRuntimeFlow(frameContext);

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

        /// <summary>
        /// Dispatch label for one team's flow kernel. The team is part of the label because
        /// the kernels themselves are shared now - without it, an order assertion or a
        /// missing-kernel report could not say which army failed to rebuild.
        /// </summary>
        public static string FlowDispatchLabel(string kernelName, int teamId)
        {
            return kernelName + "[team" + teamId + "]";
        }

        /// <summary>
        /// Rebuilds the flow field of every team that asked for one this frame, one team per
        /// dispatch group. Rebuilds are staggered across teams upstream, so in practice at
        /// most one team runs here on any given frame.
        /// </summary>
        private void DispatchRuntimeFlow(PipelineFrameContext context)
        {
            if (context.teamFlows == null)
                return;

            // Never dispatch past what the buffers were sized for: the flow buffers are
            // partitioned by team, so a stray team would write into another team's slice.
            int teamCount = Mathf.Min(context.teamFlows.Length, Mathf.Max(1, buffers.TeamCount));
            for (int teamId = 0; teamId < teamCount; teamId++)
            {
                if (!context.teamFlows[teamId].rebuildThisFrame)
                    continue;

                DispatchRuntimeFlowForTeam(context, teamId);
            }
        }

        private void DispatchRuntimeFlowForTeam(PipelineFrameContext context, int teamId)
        {
            TeamFlowFrameSettings flow = context.teamFlows[teamId];
            ComputeShader shader = shaders.RuntimeFlowShader;
            int flowGroups = Mathf.Max(1, flow.threadGroupsX);

            // Both of these are per dispatch, not per frame: the team travels in a uniform
            // and the preview texture is one per team.
            if (shader != null)
                shader.SetInt(FlowTeamIdId, teamId);
            SetTexture(shader, shaders.GenerateRuntimeFlowField, RuntimeFlowPreviewTextureId, buffers.GetFlowPreviewTexture(teamId));

            Dispatch(shader, shaders.ClearRuntimeFlowResources, flowGroups, FlowDispatchLabel("ClearRuntimeFlowResources", teamId));
            Dispatch(shader, shaders.BuildRuntimeFlowTargetDensity, Mathf.Max(1, context.agentThreadGroupsX), FlowDispatchLabel("BuildRuntimeFlowTargetDensity", teamId));
            // One 64-thread group per sector (the kernel reduces its sector in groupshared memory).
            Dispatch(shader, shaders.SelectRuntimeFlowTargets, Mathf.Clamp(flow.sectorCount, 1, MassGpuBufferManager.FlowTargetSlotsPerTeam), FlowDispatchLabel("SelectRuntimeFlowTargets", teamId));
            Dispatch(shader, shaders.GenerateRuntimeFlowField, flowGroups, FlowDispatchLabel("GenerateRuntimeFlowField", teamId));
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
            shaders.SetFloat(DefenderGuardRadiusId, Mathf.Max(0f, context.defenderGuardRadius));

            UploadFlowGridConstants(context);
            UploadTeamFlowParams(context);

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

        /// <summary>
        /// Uploads the one flow grid every team shares. Per-team grids are a non-goal: the flow
        /// buffers are partitioned as teamId * cellCount + cell, which only holds while the cell
        /// index itself is team-independent. Team 0's record defines the grid because the manager
        /// writes identical grid values into every record.
        /// </summary>
        private void UploadFlowGridConstants(PipelineFrameContext context)
        {
            TeamFlowFrameSettings grid = context.teamFlows != null && context.teamFlows.Length > 0
                ? context.teamFlows[0]
                : default(TeamFlowFrameSettings);

            shaders.SetInts(FlowFieldResolutionId, Mathf.Max(1, grid.resolutionX), Mathf.Max(1, grid.resolutionZ));
            shaders.SetVector(FlowFieldOriginId, new Vector4(grid.origin.x, grid.origin.y, 0f, 0f));
            shaders.SetFloat(FlowFieldCellSizeId, Mathf.Max(0.1f, grid.cellSize));
        }

        /// <summary>
        /// One TeamFlowParams record per allocated team, replacing the attacker/defender uniform
        /// pairs. These have to be a buffer rather than uniforms because the combat kernel reads
        /// each agent's own team record, while a uniform would only hold the last team uploaded.
        /// A team with no frame record stays zeroed, which reads as "no target, field off".
        /// </summary>
        private void UploadTeamFlowParams(PipelineFrameContext context)
        {
            if (buffers.teamFlowParamsBuffer == null)
                return;

            int teamCount = Mathf.Max(1, buffers.TeamCount);
            if (teamFlowParamsScratch.Length != teamCount)
                teamFlowParamsScratch = new TeamFlowParams[teamCount];

            int recordCount = context.teamFlows != null ? Mathf.Min(context.teamFlows.Length, teamCount) : 0;
            for (int teamId = 0; teamId < recordCount; teamId++)
                teamFlowParamsScratch[teamId] = TeamFlowParams.From(context.teamFlows[teamId]);
            for (int teamId = recordCount; teamId < teamCount; teamId++)
                teamFlowParamsScratch[teamId] = default(TeamFlowParams);

            buffers.teamFlowParamsBuffer.SetData(teamFlowParamsScratch);
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

            // Every team runs these same four kernels over team-partitioned buffers, so binding
            // happens once here. Only the preview texture and the flowTeamId uniform are per
            // team, and both are set per dispatch in DispatchRuntimeFlowForTeam.
            BindRuntimeFlowKernel(flow, shaders.ClearRuntimeFlowResources);

            BindRuntimeFlowKernel(flow, shaders.BuildRuntimeFlowTargetDensity);
            SetBuffer(flow, shaders.BuildRuntimeFlowTargetDensity, AgentPositionReadBufferId, buffers.agentPositionReadBuffer);
            SetBuffer(flow, shaders.BuildRuntimeFlowTargetDensity, HpReadBufferId, buffers.combatBuffers.hpReadBuffer);
            SetBuffer(flow, shaders.BuildRuntimeFlowTargetDensity, TeamIdReadBufferId, buffers.combatBuffers.teamIdBuffer);

            BindRuntimeFlowKernel(flow, shaders.SelectRuntimeFlowTargets);

            BindRuntimeFlowKernel(flow, shaders.GenerateRuntimeFlowField);
            SetBuffer(flow, shaders.GenerateRuntimeFlowField, FlowFieldDirectionsId, buffers.flowFieldDirectionsBuffer);
        }

        /// <summary>
        /// The scratch every runtime flow kernel reaches for, plus the per-team parameter records
        /// the kernels read their own configuration from.
        /// </summary>
        private void BindRuntimeFlowKernel(ComputeShader flow, int kernel)
        {
            SetBuffer(flow, kernel, AgentBufferId, buffers.agentBuffer);
            SetBuffer(flow, kernel, RuntimeFlowTargetDensityId, buffers.runtimeFlowTargetDensityBuffer);
            SetBuffer(flow, kernel, RuntimeFlowStatsId, buffers.runtimeFlowStatsBuffer);
            SetBuffer(flow, kernel, RuntimeFlowTargetsId, buffers.runtimeFlowTargetsBuffer);
            SetBuffer(flow, kernel, TeamFlowParamsReadBufferId, buffers.teamFlowParamsBuffer);
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
            // Only SimulateCombatAndAccumulateDamage reads stances: the locomotion branch and
            // the target-usability test both live there. Other kernels never ask.
            SetBuffer(combat, simulate, TeamStanceReadBufferId, buffers.teamStanceBuffer);
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
            // Each agent reads its own team's flow configuration here, which is precisely why
            // these parameters live in a buffer instead of uniforms.
            SetBuffer(combat, simulate, TeamFlowParamsReadBufferId, buffers.teamFlowParamsBuffer);
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
