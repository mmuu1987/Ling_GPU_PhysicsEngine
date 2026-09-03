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
    /// CombatSimulation -> LodClassification (once per unit type) -> buffer swap.
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

            // N-team runtime flow dispatch
            shaders.SetInt(TeamCountId, frameContext.teamCount);
            for (int teamIndex = 0; teamIndex < frameContext.teamCount; teamIndex++)
            {
                if (ShouldRebuildTeamFlow(frameContext, teamIndex))
                    DispatchRuntimeFlowForTeam(teamIndex, frameContext);
            }

            if (frameContext.rebuildDensityMap)
                DispatchDensityMap(frameContext);

            DispatchCombatSimulation(frameContext);
            DispatchLodClassification(frameContext);
            buffers.SwapSimulationBuffers();
        }

        private bool ShouldRebuildTeamFlow(PipelineFrameContext context, int teamIndex)
        {
            if (context.teamFlows != null && teamIndex < context.teamFlows.Length)
                return context.teamFlows[teamIndex].rebuildThisFrame;

            if (teamIndex == 0)
                return context.attackerFlow.rebuildThisFrame;
            if (teamIndex == 1)
                return context.defenderFlow.rebuildThisFrame;
            return false;
        }

        private void DispatchRuntimeFlowForTeam(int teamIndex, PipelineFrameContext context)
        {
            TeamFlowFrameSettings flow;
            if (context.teamFlows != null && teamIndex < context.teamFlows.Length)
                flow = context.teamFlows[teamIndex];
            else
                flow = teamIndex == 0 ? context.attackerFlow : context.defenderFlow;

            int flowGroups = Mathf.Max(1, flow.threadGroupsX);

            shaders.SetInt(ActiveTeamIndexId, teamIndex);

            // Bind per-team flow field directions buffer
            ComputeBuffer teamFlowBuffer;
            if (buffers.flowFieldDirectionsBuffers != null && teamIndex < buffers.flowFieldDirectionsBuffers.Length)
                teamFlowBuffer = buffers.flowFieldDirectionsBuffers[teamIndex];
            else
                teamFlowBuffer = teamIndex == 0 ? buffers.flowFieldDirectionsBuffer : buffers.defenderFlowFieldDirectionsBuffer;
            SetBuffer(shaders.RuntimeFlowShader, shaders.GenerateRuntimeFlowField, FlowFieldDirectionsId, teamFlowBuffer);

            // Bind per-team preview texture (fallback to legacy buffers for team 0/1)
            RenderTexture teamPreviewTexture;
            int previewPropertyId;
            if (buffers.runtimeFlowPreviewTextures != null && teamIndex < buffers.runtimeFlowPreviewTextures.Length)
            {
                teamPreviewTexture = buffers.runtimeFlowPreviewTextures[teamIndex];
                previewPropertyId = RuntimeFlowPreviewTextureId;
            }
            else
            {
                teamPreviewTexture = teamIndex == 0 ? buffers.runtimeAttackerFlowPreviewTexture : buffers.runtimeDefenderFlowPreviewTexture;
                previewPropertyId = teamIndex == 0 ? RuntimeAttackerFlowPreviewTextureId : RuntimeDefenderFlowPreviewTextureId;
            }
            SetTexture(shaders.RuntimeFlowShader, shaders.GenerateRuntimeFlowField, previewPropertyId, teamPreviewTexture);

            Dispatch(shaders.RuntimeFlowShader, shaders.ClearRuntimeFlowResources, flowGroups, "ClearRuntimeFlowResources[" + teamIndex + "]");
            Dispatch(shaders.RuntimeFlowShader, shaders.BuildRuntimeTargetDensity, Mathf.Max(1, context.agentThreadGroupsX), "BuildRuntimeTargetDensity[" + teamIndex + "]");
            Dispatch(shaders.RuntimeFlowShader, shaders.SelectRuntimeFlowTargets, Mathf.Clamp(flow.sectorCount, 1, 8), "SelectRuntimeFlowTargets[" + teamIndex + "]");
            Dispatch(shaders.RuntimeFlowShader, shaders.GenerateRuntimeFlowField, flowGroups, "GenerateRuntimeFlowField[" + teamIndex + "]");
        }

        private void DispatchSpatialHash(PipelineFrameContext context)
        {
            Dispatch(shaders.SpatialHashShader, shaders.ClearGrid, Mathf.Max(1, context.gridThreadGroupsX), "ClearGrid");
            Dispatch(shaders.SpatialHashShader, shaders.BuildSpatialHash, Mathf.Max(1, context.agentThreadGroupsX), "BuildSpatialHash");
        }

        private void DispatchDensityMap(PipelineFrameContext context)
        {
            if (buffers.densityMapTexture == null)
                return;

            Dispatch(shaders.CombatSimulationShader, shaders.ClearDensityMap, Mathf.Max(1, context.densityMapThreadGroupsX), Mathf.Max(1, context.densityMapThreadGroupsY), "ClearDensityMap");
            Dispatch(shaders.CombatSimulationShader, shaders.BuildDensityMap, Mathf.Max(1, context.agentThreadGroupsX), "BuildDensityMap");
        }

        private void DispatchCombatSimulation(PipelineFrameContext context)
        {
            Dispatch(shaders.CombatSimulationShader, shaders.ClearPendingDamage, Mathf.Max(1, context.agentThreadGroupsX), "ClearPendingDamage");
            Dispatch(shaders.CombatSimulationShader, shaders.BuildEngagementSlotOccupancy, Mathf.Max(1, context.agentThreadGroupsX), "BuildEngagementSlotOccupancy");
            Dispatch(shaders.CombatSimulationShader, shaders.SimulateCombatAndAccumulateDamage, Mathf.Max(1, context.agentThreadGroupsX), "SimulateCombatAndAccumulateDamage");
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
            shaders.SetInt(LocalTargetSearchCellRadiusId, Mathf.Max(1, context.localTargetSearchCellRadius));
            shaders.SetInt(DefenderMovementModeId, context.defenderMovementMode);
            shaders.SetFloat(DefenderGuardRadiusId, Mathf.Max(0f, context.defenderGuardRadius));

            shaders.SetInt(RuntimeFlowPreviewModeId, context.runtimeFlowPreviewMode);
            shaders.SetInt(FlowPreviewEnabledId, context.flowPreviewEnabled ? 1 : 0);
            shaders.SetInt(StaticObstacleCountId, Mathf.Clamp(context.staticObstacleCount, 0, StaticObstacleMath.MaxObstacleCount));
            shaders.SetFloat(StaticObstaclePaddingId, Mathf.Max(0f, context.staticObstaclePadding));
            if (context.staticObstacleRects != null && context.staticObstacleRects.Length > 0)
                shaders.SetVectorArray(StaticObstacleRectsId, context.staticObstacleRects);

            if (context.lod.frustumPlanes != null && context.lod.frustumPlanes.Length > 0)
                shaders.SetVectorArray(FrustumPlanesId, context.lod.frustumPlanes);
        }

        private void BindComputeBuffers()
        {
            BindSpatialHashBuffers();
            BindRuntimeFlowBuffers();
            BindCombatBuffers();
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

            SetBuffer(flow, shaders.ClearRuntimeFlowResources, AgentBufferId, buffers.agentBuffer);
            SetBuffer(flow, shaders.ClearRuntimeFlowResources, RuntimeTargetDensityId, buffers.runtimeTargetDensityBuffer);
            SetBuffer(flow, shaders.ClearRuntimeFlowResources, RuntimeFlowStatsId, buffers.runtimeFlowStatsBuffer);
            SetBuffer(flow, shaders.ClearRuntimeFlowResources, RuntimeFlowTargetsId, buffers.runtimeFlowTargetsBuffer);

            SetBuffer(flow, shaders.BuildRuntimeTargetDensity, AgentBufferId, buffers.agentBuffer);
            SetBuffer(flow, shaders.BuildRuntimeTargetDensity, AgentPositionReadBufferId, buffers.agentPositionReadBuffer);
            SetBuffer(flow, shaders.BuildRuntimeTargetDensity, HpReadBufferId, buffers.combatBuffers.hpReadBuffer);
            SetBuffer(flow, shaders.BuildRuntimeTargetDensity, TeamIdReadBufferId, buffers.combatBuffers.teamIdBuffer);
            SetBuffer(flow, shaders.BuildRuntimeTargetDensity, RuntimeTargetDensityId, buffers.runtimeTargetDensityBuffer);
            SetBuffer(flow, shaders.BuildRuntimeTargetDensity, RuntimeFlowStatsId, buffers.runtimeFlowStatsBuffer);

            SetBuffer(flow, shaders.SelectRuntimeFlowTargets, AgentBufferId, buffers.agentBuffer);
            SetBuffer(flow, shaders.SelectRuntimeFlowTargets, RuntimeTargetDensityId, buffers.runtimeTargetDensityBuffer);
            SetBuffer(flow, shaders.SelectRuntimeFlowTargets, RuntimeFlowStatsId, buffers.runtimeFlowStatsBuffer);
            SetBuffer(flow, shaders.SelectRuntimeFlowTargets, RuntimeFlowTargetsId, buffers.runtimeFlowTargetsBuffer);

            SetBuffer(flow, shaders.GenerateRuntimeFlowField, AgentBufferId, buffers.agentBuffer);
            SetBuffer(flow, shaders.GenerateRuntimeFlowField, RuntimeTargetDensityId, buffers.runtimeTargetDensityBuffer);
            SetBuffer(flow, shaders.GenerateRuntimeFlowField, RuntimeFlowStatsId, buffers.runtimeFlowStatsBuffer);
            SetBuffer(flow, shaders.GenerateRuntimeFlowField, RuntimeFlowTargetsId, buffers.runtimeFlowTargetsBuffer);
            // FlowFieldDirections and preview texture are bound per-team inside DispatchRuntimeFlowForTeam.
        }

        private void BindCombatBuffers()
        {
            ComputeShader combat = shaders.CombatSimulationShader;

            SetBuffer(combat, shaders.ClearPendingDamage, AgentBufferId, buffers.agentBuffer);
            SetBuffer(combat, shaders.ClearPendingDamage, PendingDamageBufferId, buffers.combatBuffers.pendingDamageWriteBuffer);

            SetTexture(combat, shaders.ClearDensityMap, DensityMapWriteId, buffers.densityMapTexture);

            SetBuffer(combat, shaders.BuildDensityMap, AgentBufferId, buffers.agentBuffer);
            SetBuffer(combat, shaders.BuildDensityMap, HpReadBufferId, buffers.combatBuffers.hpReadBuffer);
            SetTexture(combat, shaders.BuildDensityMap, DensityMapWriteId, buffers.densityMapTexture);

            int buildSlots = shaders.BuildEngagementSlotOccupancy;
            SetBuffer(combat, buildSlots, AgentBufferId, buffers.agentBuffer);
            SetBuffer(combat, buildSlots, TargetAgentIndexBufferId, buffers.combatBuffers.targetAgentIndexBuffer);
            SetBuffer(combat, buildSlots, EngagementSlotAssignmentBufferId, buffers.combatBuffers.engagementSlotAssignmentBuffer);
            SetBuffer(combat, buildSlots, EngagementSlotOccupancyBufferId, buffers.combatBuffers.engagementSlotOccupancyBuffer);
            SetBuffer(combat, buildSlots, HpReadBufferId, buffers.combatBuffers.hpReadBuffer);
            SetBuffer(combat, buildSlots, UnitTypeSettingsId, buffers.unitTypeSettingsBuffer);
            SetBuffer(combat, buildSlots, UnitTypeIndexReadBufferId, buffers.unitTypeIndexBuffer);

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
            SetBuffer(combat, simulate, FlowFieldDirectionsId, buffers.flowFieldDirectionsBuffer);
            SetBuffer(combat, simulate, DefenderFlowFieldDirectionsId, buffers.defenderFlowFieldDirectionsBuffer);
            SetBuffer(combat, simulate, UnitTypeSettingsId, buffers.unitTypeSettingsBuffer);
            SetBuffer(combat, simulate, UnitTypeIndexReadBufferId, buffers.unitTypeIndexBuffer);
            SetTexture(combat, simulate, DensityMapId, buffers.densityMapTexture);
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
