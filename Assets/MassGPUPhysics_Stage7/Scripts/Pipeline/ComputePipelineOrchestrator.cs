using UnityEngine;
using static MassGPUPhysics.Stage7.MassGpuShaderPropertyIds_Stage7;

namespace MassGPUPhysics.Stage7
{
    public sealed class ComputePipelineOrchestrator
    {
        private readonly MassGpuShaderSet_Stage7 shaders;
        private readonly MassGpuBufferManager_Stage7 buffers;

        public ComputePipelineOrchestrator(MassGpuShaderSet_Stage7 shaders, MassGpuBufferManager_Stage7 buffers)
        {
            this.shaders = shaders;
            this.buffers = buffers;
        }

        public void DispatchFrame(PipelineFrameContext frameContext)
        {
            if (buffers == null || !buffers.IsAllocated)
                return;

            UploadFrameConstants(frameContext);
            BindComputeBuffers();

            DispatchSpatialHash(frameContext);

            if (frameContext.rebuildAttackerFlow)
                DispatchRuntimeAttackerFlow(frameContext);
            if (frameContext.rebuildDefenderFlow)
                DispatchRuntimeDefenderFlow(frameContext);

            if (frameContext.rebuildDensityMap)
                DispatchDensityMap(frameContext);

            DispatchCombatSimulation(frameContext);
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
            Dispatch(shaders.RuntimeFlowShader, shaders.ClearRuntimeAttackerFlowResources, Mathf.Max(1, context.flowFieldThreadGroupsX), "ClearRuntimeAttackerFlowResources");
            Dispatch(shaders.RuntimeFlowShader, shaders.BuildRuntimeAttackerTargetDensity, Mathf.Max(1, context.agentThreadGroupsX), "BuildRuntimeAttackerTargetDensity");
            Dispatch(shaders.RuntimeFlowShader, shaders.SelectRuntimeAttackerFlowTargets, 1, "SelectRuntimeAttackerFlowTargets");
            Dispatch(shaders.RuntimeFlowShader, shaders.GenerateRuntimeAttackerFlowField, Mathf.Max(1, context.flowFieldThreadGroupsX), "GenerateRuntimeAttackerFlowField");
        }

        private void DispatchRuntimeDefenderFlow(PipelineFrameContext context)
        {
            Dispatch(shaders.RuntimeFlowShader, shaders.ClearRuntimeDefenderFlowResources, Mathf.Max(1, context.defenderFlowFieldThreadGroupsX), "ClearRuntimeDefenderFlowResources");
            Dispatch(shaders.RuntimeFlowShader, shaders.BuildRuntimeDefenderTargetDensity, Mathf.Max(1, context.agentThreadGroupsX), "BuildRuntimeDefenderTargetDensity");
            Dispatch(shaders.RuntimeFlowShader, shaders.SelectRuntimeDefenderFlowTargets, 1, "SelectRuntimeDefenderFlowTargets");
            Dispatch(shaders.RuntimeFlowShader, shaders.GenerateRuntimeDefenderFlowField, Mathf.Max(1, context.defenderFlowFieldThreadGroupsX), "GenerateRuntimeDefenderFlowField");
        }

        private void DispatchCombatSimulation(PipelineFrameContext context)
        {
            Dispatch(shaders.CombatSimulationShader, shaders.ClearPendingDamage, Mathf.Max(1, context.agentThreadGroupsX), "ClearPendingDamage");
            Dispatch(shaders.CombatSimulationShader, shaders.SimulateCombatAndAccumulateDamage, Mathf.Max(1, context.agentThreadGroupsX), "SimulateCombatAndAccumulateDamage");
        }

        private void DispatchDensityMap(PipelineFrameContext context)
        {
            if (buffers.densityMapTexture == null)
                return;

            Dispatch(shaders.CombatSimulationShader, shaders.ClearDensityMap, Mathf.Max(1, context.densityMapThreadGroupsX), Mathf.Max(1, context.densityMapThreadGroupsY), "ClearDensityMap");
            Dispatch(shaders.CombatSimulationShader, shaders.BuildDensityMap, Mathf.Max(1, context.agentThreadGroupsX), "BuildDensityMap");
        }

        private void DispatchLodClassification(PipelineFrameContext context)
        {
            buffers.ResetAppendCounters();
            Dispatch(shaders.LodClassificationShader, shaders.ClassifyVisibleAgentsByTeam, Mathf.Max(1, context.agentThreadGroupsX), "ClassifyVisibleAgentsByTeam");
            buffers.CopyVisibleCountsToArgs();
        }

        private void UploadFrameConstants(PipelineFrameContext context)
        {
            shaders.SetFloat(DeltaTimeId, context.deltaTime);
            shaders.SetFloat(AnimationDurationId, Mathf.Max(0.0001f, context.animationDuration));
            shaders.SetInt(FrameIndexId, context.frameIndex);
            shaders.SetVector(LodCenterId, context.lodCenterPosition);
            shaders.SetFloat(NearLodRadiusSqrId, Mathf.Max(0f, context.nearLodRadius) * Mathf.Max(0f, context.nearLodRadius));
            shaders.SetFloat(MidLodRadiusSqrId, Mathf.Max(0f, context.midLodRadius) * Mathf.Max(0f, context.midLodRadius));
            shaders.SetInt(EnableFrustumCullingId, context.frustumPlanes != null && context.frustumPlanes.Length >= 6 ? 1 : 0);
            shaders.SetFloat(CullingRadiusId, Mathf.Max(0f, context.cullingRadius));
            shaders.SetInt(NearAnimationIntervalId, Mathf.Max(1, context.nearAnimationInterval));
            shaders.SetInt(MidAnimationIntervalId, Mathf.Max(1, context.midAnimationInterval));
            shaders.SetInt(FarAnimationIntervalId, Mathf.Max(1, context.farAnimationInterval));
            shaders.SetInt(GridCellCountId, Mathf.Max(1, context.gridResolutionX * context.gridResolutionZ));
            shaders.SetInts(GridResolutionId, Mathf.Max(1, context.gridResolutionX), Mathf.Max(1, context.gridResolutionZ));
            shaders.SetVector(GridOriginId, new Vector4(context.gridOrigin.x, context.gridOrigin.y, 0f, 0f));
            shaders.SetVector(GridWorldSizeId, new Vector4(context.gridWorldSize.x, context.gridWorldSize.y, 0f, 0f));
            shaders.SetFloat(CellSizeId, Mathf.Max(0.1f, context.cellSize));
            shaders.SetInt(MaxAgentsPerCellId, Mathf.Max(1, context.maxAgentsPerCell));
            shaders.SetFloat(AttackerAgentRadiusId, context.attackerSettings.agentRadius);
            shaders.SetFloat(DefenderAgentRadiusId, context.defenderSettings.agentRadius);
            shaders.SetFloat(AttackerSeparationStrengthId, context.attackerSettings.separationStrength);
            shaders.SetFloat(DefenderSeparationStrengthId, context.defenderSettings.separationStrength);
            shaders.SetInt(SeparationSkipIntervalId, Mathf.Max(1, context.attackerSettings.separationSkipInterval));
            float densityAvailable = buffers != null && buffers.densityMapTexture != null ? 1f : 0f;
            shaders.SetFloat(AttackerDensityAvoidanceStrengthId, Mathf.Max(0f, context.attackerSettings.densityAvoidanceStrength) * densityAvailable);
            shaders.SetFloat(DefenderDensityAvoidanceStrengthId, Mathf.Max(0f, context.defenderSettings.densityAvoidanceStrength) * densityAvailable);
            shaders.SetInt(AttackerDensityComfortCountId, Mathf.Max(0, context.attackerSettings.densityComfortCount));
            shaders.SetInt(DefenderDensityComfortCountId, Mathf.Max(0, context.defenderSettings.densityComfortCount));
            shaders.SetFloat(AttackerDensityPressureRangeId, Mathf.Max(0.01f, context.attackerSettings.densityPressureRange));
            shaders.SetFloat(DefenderDensityPressureRangeId, Mathf.Max(0.01f, context.defenderSettings.densityPressureRange));
            shaders.SetFloat(AttackerDensitySpeedPenaltyId, Mathf.Clamp01(context.attackerSettings.densitySpeedPenalty) * densityAvailable);
            shaders.SetFloat(DefenderDensitySpeedPenaltyId, Mathf.Clamp01(context.defenderSettings.densitySpeedPenalty) * densityAvailable);
            shaders.SetFloat(AttackerSpeedVariationId, Mathf.Clamp(context.attackerSettings.speedVariation, 0f, 0.5f));
            shaders.SetFloat(DefenderSpeedVariationId, Mathf.Clamp(context.defenderSettings.speedVariation, 0f, 0.5f));
            shaders.SetFloat(AttackerLaneBiasStrengthId, Mathf.Clamp01(context.attackerSettings.laneBiasStrength));
            shaders.SetFloat(DefenderLaneBiasStrengthId, Mathf.Clamp01(context.defenderSettings.laneBiasStrength));
            shaders.SetFloat(AttackerVelocityDampingId, context.attackerSettings.velocityDamping);
            shaders.SetFloat(DefenderVelocityDampingId, context.defenderSettings.velocityDamping);
            shaders.SetFloat(AttackerMaxSpeedId, context.attackerSettings.maxSpeed);
            shaders.SetFloat(DefenderMaxSpeedId, context.defenderSettings.maxSpeed);
            shaders.SetInt(AttackerFlowTargetModeId, context.attackerSettings.flowTargetMode);
            shaders.SetVector(AttackerFlowTargetPointId, new Vector4(context.attackerSettings.flowTargetPoint.x, context.attackerSettings.flowTargetPoint.z, 0f, 0f));
            shaders.SetVector(AttackerFlowTargetAreaId, new Vector4(
                context.attackerSettings.flowTargetAreaCenter.x,
                context.attackerSettings.flowTargetAreaCenter.z,
                Mathf.Max(0f, context.attackerSettings.flowTargetAreaSize.x),
                Mathf.Max(0f, context.attackerSettings.flowTargetAreaSize.z)));
            shaders.SetInt(DefenderFlowTargetModeId, context.defenderSettings.flowTargetMode);
            shaders.SetVector(DefenderFlowTargetPointId, new Vector4(context.defenderSettings.flowTargetPoint.x, context.defenderSettings.flowTargetPoint.z, 0f, 0f));
            shaders.SetVector(DefenderFlowTargetAreaId, new Vector4(
                context.defenderSettings.flowTargetAreaCenter.x,
                context.defenderSettings.flowTargetAreaCenter.z,
                Mathf.Max(0f, context.defenderSettings.flowTargetAreaSize.x),
                Mathf.Max(0f, context.defenderSettings.flowTargetAreaSize.z)));
            shaders.SetFloat(BoundaryPaddingId, Mathf.Max(0f, context.boundaryPadding));
            shaders.SetInt(FlowFieldEnabledId, context.flowFieldEnabled ? 1 : 0);
            shaders.SetInts(FlowFieldResolutionId, Mathf.Max(1, context.flowFieldResolutionX), Mathf.Max(1, context.flowFieldResolutionZ));
            shaders.SetVector(FlowFieldOriginId, new Vector4(context.flowFieldOrigin.x, context.flowFieldOrigin.y, 0f, 0f));
            shaders.SetFloat(FlowFieldCellSizeId, Mathf.Max(0.1f, context.flowFieldCellSize));
            shaders.SetFloat(FlowFieldWeightId, Mathf.Clamp01(context.flowFieldWeight));
            shaders.SetFloat(FlowFieldResponsivenessId, Mathf.Max(0f, context.flowFieldResponsiveness));
            shaders.SetInt(RuntimeFlowPreviewModeId, context.runtimeFlowPreviewMode);
            shaders.SetInt(RuntimeDynamicAttackerFlowEnabledId, context.runtimeDynamicAttackerFlowEnabled ? 1 : 0);
            shaders.SetInt(RuntimeDynamicDefenderFlowEnabledId, context.runtimeDynamicDefenderFlowEnabled ? 1 : 0);
            shaders.SetInt(DynamicFlowSectorCountId, Mathf.Clamp(context.dynamicFlowSectorCount, 1, 8));
            shaders.SetFloat(DynamicFlowTargetStopRadiusId, Mathf.Max(0f, context.dynamicFlowTargetStopRadius));
            shaders.SetInt(DynamicFlowMinDefendersPerTargetId, Mathf.Max(1, context.dynamicFlowMinDefendersPerTarget));
            shaders.SetInt(DynamicDefenderFlowSectorCountId, Mathf.Clamp(context.dynamicDefenderFlowSectorCount, 1, 8));
            shaders.SetFloat(DynamicDefenderFlowTargetStopRadiusId, Mathf.Max(0f, context.dynamicDefenderFlowTargetStopRadius));
            shaders.SetInt(DynamicDefenderFlowMinAttackersPerTargetId, Mathf.Max(1, context.dynamicDefenderFlowMinAttackersPerTarget));
            shaders.SetInt(DefenderMovementModeId, context.defenderMovementMode);
            shaders.SetInt(DefenderFlowFieldEnabledId, context.defenderFlowFieldEnabled ? 1 : 0);
            shaders.SetInts(DefenderFlowFieldResolutionId, Mathf.Max(1, context.defenderFlowFieldResolutionX), Mathf.Max(1, context.defenderFlowFieldResolutionZ));
            shaders.SetVector(DefenderFlowFieldOriginId, new Vector4(context.defenderFlowFieldOrigin.x, context.defenderFlowFieldOrigin.y, 0f, 0f));
            shaders.SetFloat(DefenderFlowFieldCellSizeId, Mathf.Max(0.1f, context.defenderFlowFieldCellSize));
            shaders.SetInt(EnableTwoTeamCombatId, 1);
            shaders.SetInt(BattleStartedId, context.battleStarted ? 1 : 0);
            shaders.SetInt(AttackerCountId, Mathf.Clamp(context.attackerCount, 0, context.totalAgentCount));
            shaders.SetFloat(AttackerTargetAcquireRadiusId, context.attackerSettings.targetAcquireRadius);
            shaders.SetFloat(DefenderTargetAcquireRadiusId, context.defenderSettings.targetAcquireRadius);
            shaders.SetFloat(AttackerAttackRangeId, context.attackerSettings.attackRange);
            shaders.SetFloat(DefenderAttackRangeId, context.defenderSettings.attackRange);
            shaders.SetInt(AttackerAttackDamageId, context.attackerSettings.attackDamage);
            shaders.SetInt(DefenderAttackDamageId, context.defenderSettings.attackDamage);
            shaders.SetFloat(AttackerAttackIntervalId, context.attackerSettings.attackInterval);
            shaders.SetFloat(DefenderAttackIntervalId, context.defenderSettings.attackInterval);
            shaders.SetFloat(DefenderGuardRadiusId, Mathf.Max(0f, context.defenderGuardRadius));
            shaders.SetFloat(DefenderMaxChaseDistanceId, Mathf.Max(0.1f, context.defenderMaxChaseDistance));
            shaders.SetFloat(DeathClipDurationId, Mathf.Max(0.01f, context.deathClipDuration));

            if (context.frustumPlanes != null && context.frustumPlanes.Length > 0)
                shaders.SetVectorArray(FrustumPlanesId, context.frustumPlanes);
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

            SetBuffer(shaders.SpatialHashShader, shaders.BuildSpatialHash, AgentBufferId, buffers.agentBuffer);
            SetBuffer(shaders.SpatialHashShader, shaders.BuildSpatialHash, AgentPositionReadBufferId, buffers.agentPositionReadBuffer);
            SetBuffer(shaders.SpatialHashShader, shaders.BuildSpatialHash, HpReadBufferId, buffers.combatBuffers.hpBuffer);
            SetBuffer(shaders.SpatialHashShader, shaders.BuildSpatialHash, GridCountsId, buffers.gridCountsBuffer);
            SetBuffer(shaders.SpatialHashShader, shaders.BuildSpatialHash, GridAgentIndicesId, buffers.gridAgentIndicesBuffer);
        }

        private void BindRuntimeFlowBuffers()
        {
            SetBuffer(shaders.RuntimeFlowShader, shaders.ClearRuntimeAttackerFlowResources, AgentBufferId, buffers.agentBuffer);
            SetBuffer(shaders.RuntimeFlowShader, shaders.ClearRuntimeAttackerFlowResources, RuntimeAttackerTargetDensityId, buffers.runtimeAttackerTargetDensityBuffer);
            SetBuffer(shaders.RuntimeFlowShader, shaders.ClearRuntimeAttackerFlowResources, RuntimeAttackerFlowStatsId, buffers.runtimeAttackerFlowStatsBuffer);
            SetBuffer(shaders.RuntimeFlowShader, shaders.ClearRuntimeAttackerFlowResources, RuntimeAttackerFlowTargetsId, buffers.runtimeAttackerFlowTargetsBuffer);

            SetBuffer(shaders.RuntimeFlowShader, shaders.BuildRuntimeAttackerTargetDensity, AgentBufferId, buffers.agentBuffer);
            SetBuffer(shaders.RuntimeFlowShader, shaders.BuildRuntimeAttackerTargetDensity, AgentPositionReadBufferId, buffers.agentPositionReadBuffer);
            SetBuffer(shaders.RuntimeFlowShader, shaders.BuildRuntimeAttackerTargetDensity, HpReadBufferId, buffers.combatBuffers.hpBuffer);
            SetBuffer(shaders.RuntimeFlowShader, shaders.BuildRuntimeAttackerTargetDensity, RuntimeAttackerTargetDensityId, buffers.runtimeAttackerTargetDensityBuffer);
            SetBuffer(shaders.RuntimeFlowShader, shaders.BuildRuntimeAttackerTargetDensity, RuntimeAttackerFlowStatsId, buffers.runtimeAttackerFlowStatsBuffer);

            SetBuffer(shaders.RuntimeFlowShader, shaders.SelectRuntimeAttackerFlowTargets, AgentBufferId, buffers.agentBuffer);
            SetBuffer(shaders.RuntimeFlowShader, shaders.SelectRuntimeAttackerFlowTargets, RuntimeAttackerTargetDensityId, buffers.runtimeAttackerTargetDensityBuffer);
            SetBuffer(shaders.RuntimeFlowShader, shaders.SelectRuntimeAttackerFlowTargets, RuntimeAttackerFlowStatsId, buffers.runtimeAttackerFlowStatsBuffer);
            SetBuffer(shaders.RuntimeFlowShader, shaders.SelectRuntimeAttackerFlowTargets, RuntimeAttackerFlowTargetsId, buffers.runtimeAttackerFlowTargetsBuffer);

            SetBuffer(shaders.RuntimeFlowShader, shaders.GenerateRuntimeAttackerFlowField, AgentBufferId, buffers.agentBuffer);
            SetBuffer(shaders.RuntimeFlowShader, shaders.GenerateRuntimeAttackerFlowField, FlowFieldDirectionsId, buffers.flowFieldDirectionsBuffer);
            SetBuffer(shaders.RuntimeFlowShader, shaders.GenerateRuntimeAttackerFlowField, RuntimeAttackerTargetDensityId, buffers.runtimeAttackerTargetDensityBuffer);
            SetBuffer(shaders.RuntimeFlowShader, shaders.GenerateRuntimeAttackerFlowField, RuntimeAttackerFlowStatsId, buffers.runtimeAttackerFlowStatsBuffer);
            SetBuffer(shaders.RuntimeFlowShader, shaders.GenerateRuntimeAttackerFlowField, RuntimeAttackerFlowTargetsId, buffers.runtimeAttackerFlowTargetsBuffer);
            SetTexture(shaders.RuntimeFlowShader, shaders.GenerateRuntimeAttackerFlowField, RuntimeAttackerFlowPreviewTextureId, buffers.runtimeAttackerFlowPreviewTexture);

            SetBuffer(shaders.RuntimeFlowShader, shaders.ClearRuntimeDefenderFlowResources, AgentBufferId, buffers.agentBuffer);
            SetBuffer(shaders.RuntimeFlowShader, shaders.ClearRuntimeDefenderFlowResources, RuntimeDefenderTargetDensityId, buffers.runtimeDefenderTargetDensityBuffer);
            SetBuffer(shaders.RuntimeFlowShader, shaders.ClearRuntimeDefenderFlowResources, RuntimeDefenderFlowStatsId, buffers.runtimeDefenderFlowStatsBuffer);
            SetBuffer(shaders.RuntimeFlowShader, shaders.ClearRuntimeDefenderFlowResources, RuntimeDefenderFlowTargetsId, buffers.runtimeDefenderFlowTargetsBuffer);

            SetBuffer(shaders.RuntimeFlowShader, shaders.BuildRuntimeDefenderTargetDensity, AgentBufferId, buffers.agentBuffer);
            SetBuffer(shaders.RuntimeFlowShader, shaders.BuildRuntimeDefenderTargetDensity, AgentPositionReadBufferId, buffers.agentPositionReadBuffer);
            SetBuffer(shaders.RuntimeFlowShader, shaders.BuildRuntimeDefenderTargetDensity, HpReadBufferId, buffers.combatBuffers.hpBuffer);
            SetBuffer(shaders.RuntimeFlowShader, shaders.BuildRuntimeDefenderTargetDensity, RuntimeDefenderTargetDensityId, buffers.runtimeDefenderTargetDensityBuffer);
            SetBuffer(shaders.RuntimeFlowShader, shaders.BuildRuntimeDefenderTargetDensity, RuntimeDefenderFlowStatsId, buffers.runtimeDefenderFlowStatsBuffer);

            SetBuffer(shaders.RuntimeFlowShader, shaders.SelectRuntimeDefenderFlowTargets, AgentBufferId, buffers.agentBuffer);
            SetBuffer(shaders.RuntimeFlowShader, shaders.SelectRuntimeDefenderFlowTargets, RuntimeDefenderTargetDensityId, buffers.runtimeDefenderTargetDensityBuffer);
            SetBuffer(shaders.RuntimeFlowShader, shaders.SelectRuntimeDefenderFlowTargets, RuntimeDefenderFlowStatsId, buffers.runtimeDefenderFlowStatsBuffer);
            SetBuffer(shaders.RuntimeFlowShader, shaders.SelectRuntimeDefenderFlowTargets, RuntimeDefenderFlowTargetsId, buffers.runtimeDefenderFlowTargetsBuffer);

            SetBuffer(shaders.RuntimeFlowShader, shaders.GenerateRuntimeDefenderFlowField, AgentBufferId, buffers.agentBuffer);
            SetBuffer(shaders.RuntimeFlowShader, shaders.GenerateRuntimeDefenderFlowField, DefenderFlowFieldDirectionsId, buffers.defenderFlowFieldDirectionsBuffer);
            SetBuffer(shaders.RuntimeFlowShader, shaders.GenerateRuntimeDefenderFlowField, RuntimeDefenderTargetDensityId, buffers.runtimeDefenderTargetDensityBuffer);
            SetBuffer(shaders.RuntimeFlowShader, shaders.GenerateRuntimeDefenderFlowField, RuntimeDefenderFlowStatsId, buffers.runtimeDefenderFlowStatsBuffer);
            SetBuffer(shaders.RuntimeFlowShader, shaders.GenerateRuntimeDefenderFlowField, RuntimeDefenderFlowTargetsId, buffers.runtimeDefenderFlowTargetsBuffer);
            SetTexture(shaders.RuntimeFlowShader, shaders.GenerateRuntimeDefenderFlowField, RuntimeDefenderFlowPreviewTextureId, buffers.runtimeDefenderFlowPreviewTexture);
        }

        private void BindCombatBuffers()
        {
            SetBuffer(shaders.CombatSimulationShader, shaders.ClearPendingDamage, AgentBufferId, buffers.agentBuffer);
            SetBuffer(shaders.CombatSimulationShader, shaders.ClearPendingDamage, PendingDamageBufferId, buffers.combatBuffers.pendingDamageWriteBuffer);

            SetTexture(shaders.CombatSimulationShader, shaders.ClearDensityMap, DensityMapWriteId, buffers.densityMapTexture);

            SetBuffer(shaders.CombatSimulationShader, shaders.BuildDensityMap, AgentBufferId, buffers.agentBuffer);
            SetBuffer(shaders.CombatSimulationShader, shaders.BuildDensityMap, HpReadBufferId, buffers.combatBuffers.hpBuffer);
            SetTexture(shaders.CombatSimulationShader, shaders.BuildDensityMap, DensityMapWriteId, buffers.densityMapTexture);

            SetBuffer(shaders.CombatSimulationShader, shaders.SimulateCombatAndAccumulateDamage, AgentBufferId, buffers.agentBuffer);
            SetBuffer(shaders.CombatSimulationShader, shaders.SimulateCombatAndAccumulateDamage, AgentPositionReadBufferId, buffers.agentPositionReadBuffer);
            SetBuffer(shaders.CombatSimulationShader, shaders.SimulateCombatAndAccumulateDamage, AgentPositionBufferId, buffers.agentPositionWriteBuffer);
            SetBuffer(shaders.CombatSimulationShader, shaders.SimulateCombatAndAccumulateDamage, GridCountsReadBufferId, buffers.gridCountsBuffer);
            SetBuffer(shaders.CombatSimulationShader, shaders.SimulateCombatAndAccumulateDamage, GridAgentIndicesReadBufferId, buffers.gridAgentIndicesBuffer);
            SetBuffer(shaders.CombatSimulationShader, shaders.SimulateCombatAndAccumulateDamage, TeamIdReadBufferId, buffers.combatBuffers.teamIdBuffer);
            SetBuffer(shaders.CombatSimulationShader, shaders.SimulateCombatAndAccumulateDamage, HpBufferId, buffers.combatBuffers.hpBuffer);
            SetBuffer(shaders.CombatSimulationShader, shaders.SimulateCombatAndAccumulateDamage, TargetAgentIndexBufferId, buffers.combatBuffers.targetAgentIndexBuffer);
            SetBuffer(shaders.CombatSimulationShader, shaders.SimulateCombatAndAccumulateDamage, AttackCooldownBufferId, buffers.combatBuffers.attackCooldownBuffer);
            SetBuffer(shaders.CombatSimulationShader, shaders.SimulateCombatAndAccumulateDamage, HomePositionReadBufferId, buffers.combatBuffers.homePositionBuffer);
            SetBuffer(shaders.CombatSimulationShader, shaders.SimulateCombatAndAccumulateDamage, PendingDamageBufferId, buffers.combatBuffers.pendingDamageWriteBuffer);
            SetBuffer(shaders.CombatSimulationShader, shaders.SimulateCombatAndAccumulateDamage, PendingDamageReadBufferId, buffers.combatBuffers.pendingDamageReadBuffer);
            SetBuffer(shaders.CombatSimulationShader, shaders.SimulateCombatAndAccumulateDamage, FlowFieldDirectionsId, buffers.flowFieldDirectionsBuffer);
            SetBuffer(shaders.CombatSimulationShader, shaders.SimulateCombatAndAccumulateDamage, DefenderFlowFieldDirectionsId, buffers.defenderFlowFieldDirectionsBuffer);
            SetTexture(shaders.CombatSimulationShader, shaders.SimulateCombatAndAccumulateDamage, DensityMapId, buffers.densityMapTexture);
        }

        private void BindLodBuffers()
        {
            SetBuffer(shaders.LodClassificationShader, shaders.ClassifyVisibleAgentsByTeam, AgentBufferId, buffers.agentBuffer);
            SetBuffer(shaders.LodClassificationShader, shaders.ClassifyVisibleAgentsByTeam, TeamIdReadBufferId, buffers.combatBuffers.teamIdBuffer);
            SetBuffer(shaders.LodClassificationShader, shaders.ClassifyVisibleAgentsByTeam, HpReadBufferId, buffers.combatBuffers.hpBuffer);
            SetBuffer(shaders.LodClassificationShader, shaders.ClassifyVisibleAgentsByTeam, NearAttackerAgentIndicesId, buffers.nearAttackerAgentIndexBuffer);
            SetBuffer(shaders.LodClassificationShader, shaders.ClassifyVisibleAgentsByTeam, MidAttackerAgentIndicesId, buffers.midAttackerAgentIndexBuffer);
            SetBuffer(shaders.LodClassificationShader, shaders.ClassifyVisibleAgentsByTeam, FarAttackerAgentIndicesId, buffers.farAttackerAgentIndexBuffer);
            SetBuffer(shaders.LodClassificationShader, shaders.ClassifyVisibleAgentsByTeam, NearDefenderAgentIndicesId, buffers.nearDefenderAgentIndexBuffer);
            SetBuffer(shaders.LodClassificationShader, shaders.ClassifyVisibleAgentsByTeam, MidDefenderAgentIndicesId, buffers.midDefenderAgentIndexBuffer);
            SetBuffer(shaders.LodClassificationShader, shaders.ClassifyVisibleAgentsByTeam, FarDefenderAgentIndicesId, buffers.farDefenderAgentIndexBuffer);
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

        private static void Dispatch(ComputeShader shader, int kernel, int groupsX, string label)
        {
            if (shader == null || kernel < 0)
            {
                Debug.LogError("Stage7 skipped GPU dispatch: " + label + " shader or kernel is missing.");
                return;
            }

            try
            {
                shader.Dispatch(kernel, Mathf.Max(1, groupsX), 1, 1);
            }
            catch (System.Exception exception)
            {
                Debug.LogError("Stage7 GPU dispatch failed at " + label + ": " + exception.Message);
            }
        }

        private static void Dispatch(ComputeShader shader, int kernel, int groupsX, int groupsY, string label)
        {
            if (shader == null || kernel < 0)
            {
                Debug.LogError("Stage7 skipped GPU dispatch: " + label + " shader or kernel is missing.");
                return;
            }

            try
            {
                shader.Dispatch(kernel, Mathf.Max(1, groupsX), Mathf.Max(1, groupsY), 1);
            }
            catch (System.Exception exception)
            {
                Debug.LogError("Stage7 GPU dispatch failed at " + label + ": " + exception.Message);
            }
        }
    }
}
