using UnityEngine;
using UnityEngine.Rendering;

using static MassGpuShaderPropertyIds_Stage6;
using DefenderMovementMode = GPUInstancingManager_Stage6.DefenderMovementMode;

public sealed partial class MassGpuRuntime_Stage6
{
    private Camera cachedMainCamera;

    private void UploadInitialAgents()
    {
        MassAgentSpawnUtility_Stage6.CombatSpawnData initialData = MassAgentSpawnUtility_Stage6.BuildInitialCombatData(
            instanceCount,
            enableTwoTeamCombat,
            attackerCount,
            spawnArea,
            spawnClusterForCollisionDemo,
            clusteredSpawnRadius,
            attackerSettings.spawnCenter,
            attackerSettings.spawnSize,
            defenderSettings.spawnCenter,
            defenderSettings.spawnSize,
            attackerSettings.maxHp,
            defenderSettings.maxHp,
            AnimationDuration);

        agentBuffer.SetData(initialData.Agents);
        agentPositionReadBuffer.SetData(initialData.AgentPositions);
        agentPositionWriteBuffer.SetData(initialData.AgentPositions);
        teamIdBuffer.SetData(initialData.TeamIds);
        hpBuffer.SetData(initialData.Hp);
        targetAgentIndexBuffer.SetData(initialData.TargetAgentIndices);
        attackCooldownBuffer.SetData(initialData.AttackCooldowns);
        homePositionBuffer.SetData(initialData.HomePositions);
        pendingDamageReadBuffer.SetData(initialData.PendingDamage);
        pendingDamageWriteBuffer.SetData(initialData.PendingDamage);
    }

    private void Update()
    {
        if (agentBuffer == null)
            return;

        ResetAppendCounters();
        UploadFrameParameters();
        bool rebuildRuntimeAttackerFlow = ConsumeRuntimeDynamicAttackerFlowRebuildRequest();
        bool rebuildRuntimeDefenderFlow = ConsumeRuntimeDynamicDefenderFlowRebuildRequest();
        dispatchScheduler.DispatchSimulation(
            kernels,
            agentPositionReadBuffer,
            agentPositionWriteBuffer,
            pendingDamageReadBuffer,
            pendingDamageWriteBuffer,
            gridThreadGroupsX,
            agentThreadGroupsX,
            FlowFieldThreadGroupsX,
            DefenderFlowFieldThreadGroupsX,
            rebuildRuntimeAttackerFlow,
            rebuildRuntimeDefenderFlow);
        buffers.SwapSimulationBuffers();
        CopyVisibleCountsToArgs();
        DrawLods();
        UpdateTelemetry();
    }

    private void ResetAppendCounters()
    {
        nearAttackerAgentIndexBuffer.SetCounterValue(0);
        midAttackerAgentIndexBuffer.SetCounterValue(0);
        farAttackerAgentIndexBuffer.SetCounterValue(0);
        nearDefenderAgentIndexBuffer.SetCounterValue(0);
        midDefenderAgentIndexBuffer.SetCounterValue(0);
        farDefenderAgentIndexBuffer.SetCounterValue(0);
    }

    private void UploadFrameParameters()
    {
        Vector3 center = GetLodCenter();
        float dt = Time.deltaTime;
        float activeAnimationDuration = AnimationDuration;
        int frameIndex = Time.frameCount;
        int activeAgentCount = Mathf.Max(1, instanceCount);
        int activeAttackerCount = Mathf.Clamp(attackerCount, 0, instanceCount);
        bool attackerRuntimeFlowEnabled = ShouldUseRuntimeDynamicAttackerFlowField();
        bool defenderRuntimeFlowEnabled = ShouldUseRuntimeDynamicDefenderFlowField();
        bool attackerFlowEnabled = enableFlowFieldNavigation && flowFieldDirectionsBuffer != null;
        bool defenderFlowEnabled = enableFlowFieldNavigation &&
                                   defenderMovementMode == DefenderMovementMode.UseDefenderFlowField &&
                                   defenderFlowFieldDirectionsBuffer != null;

        UpdateFrustumPlanes();

        UploadSpatialHashParameters(activeAgentCount);
        UploadRuntimeFlowParameters(activeAgentCount, activeAttackerCount, attackerRuntimeFlowEnabled, defenderRuntimeFlowEnabled);
        UploadCombatSimulationParameters(activeAgentCount, activeAttackerCount, attackerFlowEnabled, defenderFlowEnabled, dt);
        UploadLodClassificationParameters(activeAgentCount, center, dt, activeAnimationDuration, frameIndex);
    }

    private void UploadSpatialHashParameters(int activeAgentCount)
    {
        ComputeShader shader = kernels.SpatialHashShader;
        shader.SetInt(AgentCountId, activeAgentCount);
        shader.SetInt(GridCellCountId, gridCellCount);
        shader.SetInts(GridResolutionId, gridResolutionX, gridResolutionZ);
        shader.SetVector(GridOriginId, new Vector4(gridOrigin.x, gridOrigin.y, 0f, 0f));
        shader.SetFloat(CellSizeId, cellSize);
        shader.SetInt(MaxAgentsPerCellId, maxAgentsPerCell);
    }

    private void UploadRuntimeFlowParameters(
        int activeAgentCount,
        int activeAttackerCount,
        bool attackerRuntimeFlowEnabled,
        bool defenderRuntimeFlowEnabled)
    {
        ComputeShader shader = kernels.RuntimeFlowShader;
        shader.SetInt(AgentCountId, activeAgentCount);
        shader.SetInt(EnableTwoTeamCombatId, enableTwoTeamCombat ? 1 : 0);
        shader.SetInt(BattleStartedId, battleStarted ? 1 : 0);
        shader.SetInt(AttackerCountId, activeAttackerCount);
        shader.SetInts(FlowFieldResolutionId, flowFieldResolutionX, flowFieldResolutionZ);
        shader.SetVector(FlowFieldOriginId, new Vector4(flowFieldOrigin.x, flowFieldOrigin.y, 0f, 0f));
        shader.SetFloat(FlowFieldCellSizeId, activeFlowFieldCellSize);
        shader.SetInts(DefenderFlowFieldResolutionId, defenderFlowFieldResolutionX, defenderFlowFieldResolutionZ);
        shader.SetVector(DefenderFlowFieldOriginId, new Vector4(defenderFlowFieldOrigin.x, defenderFlowFieldOrigin.y, 0f, 0f));
        shader.SetFloat(DefenderFlowFieldCellSizeId, activeDefenderFlowFieldCellSize);
        shader.SetInt(RuntimeFlowPreviewModeId, (int)runtimeFlowPreviewMode);
        shader.SetInt(RuntimeDynamicAttackerFlowEnabledId, attackerRuntimeFlowEnabled ? 1 : 0);
        shader.SetInt(RuntimeDynamicDefenderFlowEnabledId, defenderRuntimeFlowEnabled ? 1 : 0);
        shader.SetInt(DynamicFlowSectorCountId, dynamicFlowSectorCount);
        shader.SetFloat(DynamicFlowTargetStopRadiusId, dynamicFlowTargetStopRadius);
        shader.SetInt(DynamicFlowMinDefendersPerTargetId, dynamicFlowMinDefendersPerTarget);
        shader.SetInt(DynamicDefenderFlowSectorCountId, dynamicDefenderFlowSectorCount);
        shader.SetFloat(DynamicDefenderFlowTargetStopRadiusId, dynamicDefenderFlowTargetStopRadius);
        shader.SetInt(DynamicDefenderFlowMinAttackersPerTargetId, dynamicDefenderFlowMinAttackersPerTarget);
    }

    private void UploadCombatSimulationParameters(
        int activeAgentCount,
        int activeAttackerCount,
        bool attackerFlowEnabled,
        bool defenderFlowEnabled,
        float dt)
    {
        ComputeShader shader = kernels.CombatSimulationShader;
        shader.SetInt(AgentCountId, activeAgentCount);
        shader.SetFloat(DeltaTimeId, dt);
        shader.SetInt(GridCellCountId, gridCellCount);
        shader.SetInts(GridResolutionId, gridResolutionX, gridResolutionZ);
        shader.SetVector(GridOriginId, new Vector4(gridOrigin.x, gridOrigin.y, 0f, 0f));
        shader.SetVector(GridWorldSizeId, new Vector4(activeWorldSize.x, activeWorldSize.y, 0f, 0f));
        shader.SetFloat(CellSizeId, cellSize);
        shader.SetInt(MaxAgentsPerCellId, maxAgentsPerCell);
        shader.SetFloat(AttackerAgentRadiusId, attackerSettings.agentRadius);
        shader.SetFloat(DefenderAgentRadiusId, defenderSettings.agentRadius);
        shader.SetFloat(AttackerSeparationStrengthId, attackerSettings.separationStrength);
        shader.SetFloat(DefenderSeparationStrengthId, defenderSettings.separationStrength);
        shader.SetFloat(AttackerVelocityDampingId, attackerSettings.velocityDamping);
        shader.SetFloat(DefenderVelocityDampingId, defenderSettings.velocityDamping);
        shader.SetFloat(AttackerMaxSpeedId, attackerSettings.maxSpeed);
        shader.SetFloat(DefenderMaxSpeedId, defenderSettings.maxSpeed);
        shader.SetFloat(BoundaryPaddingId, boundaryPadding);
        shader.SetInt(FlowFieldEnabledId, attackerFlowEnabled ? 1 : 0);
        shader.SetInts(FlowFieldResolutionId, flowFieldResolutionX, flowFieldResolutionZ);
        shader.SetVector(FlowFieldOriginId, new Vector4(flowFieldOrigin.x, flowFieldOrigin.y, 0f, 0f));
        shader.SetFloat(FlowFieldCellSizeId, activeFlowFieldCellSize);
        shader.SetFloat(FlowFieldWeightId, flowFieldWeight);
        shader.SetFloat(FlowFieldResponsivenessId, flowFieldResponsiveness);
        shader.SetInt(DefenderMovementModeId, defenderFlowEnabled ? (int)DefenderMovementMode.UseDefenderFlowField : (int)DefenderMovementMode.HoldPositionNoSeparation);
        shader.SetInt(DefenderFlowFieldEnabledId, defenderFlowEnabled ? 1 : 0);
        shader.SetInts(DefenderFlowFieldResolutionId, defenderFlowFieldResolutionX, defenderFlowFieldResolutionZ);
        shader.SetVector(DefenderFlowFieldOriginId, new Vector4(defenderFlowFieldOrigin.x, defenderFlowFieldOrigin.y, 0f, 0f));
        shader.SetFloat(DefenderFlowFieldCellSizeId, activeDefenderFlowFieldCellSize);
        shader.SetInt(EnableTwoTeamCombatId, enableTwoTeamCombat ? 1 : 0);
        shader.SetInt(BattleStartedId, battleStarted ? 1 : 0);
        shader.SetInt(AttackerCountId, activeAttackerCount);
        shader.SetFloat(AttackerTargetAcquireRadiusId, attackerSettings.targetAcquireRadius);
        shader.SetFloat(DefenderTargetAcquireRadiusId, defenderSettings.targetAcquireRadius);
        shader.SetFloat(AttackerAttackRangeId, attackerSettings.attackRange);
        shader.SetFloat(DefenderAttackRangeId, defenderSettings.attackRange);
        shader.SetInt(AttackerAttackDamageId, attackerSettings.attackDamage);
        shader.SetInt(DefenderAttackDamageId, defenderSettings.attackDamage);
        shader.SetFloat(AttackerAttackIntervalId, attackerSettings.attackInterval);
        shader.SetFloat(DefenderAttackIntervalId, defenderSettings.attackInterval);
        shader.SetFloat(DefenderGuardRadiusId, defenderGuardRadius);
        shader.SetFloat(DefenderMaxChaseDistanceId, defenderMaxChaseDistance);
    }

    private void UploadLodClassificationParameters(
        int activeAgentCount,
        Vector3 center,
        float dt,
        float activeAnimationDuration,
        int frameIndex)
    {
        ComputeShader shader = kernels.LodClassificationShader;
        shader.SetInt(AgentCountId, activeAgentCount);
        shader.SetFloat(DeltaTimeId, dt);
        shader.SetFloat(AnimationDurationId, activeAnimationDuration);
        shader.SetInt(FrameIndexId, frameIndex);
        shader.SetVector(LodCenterId, center);
        shader.SetFloat(NearLodRadiusSqrId, shadowCastingRadius * shadowCastingRadius);
        shader.SetFloat(MidLodRadiusSqrId, midLodRadius * midLodRadius);
        shader.SetInt(EnableFrustumCullingId, enableFrustumCulling ? 1 : 0);
        shader.SetFloat(CullingRadiusId, cullingRadius);
        shader.SetInt(NearAnimationIntervalId, nearAnimationInterval);
        shader.SetInt(MidAnimationIntervalId, midAnimationInterval);
        shader.SetInt(FarAnimationIntervalId, farAnimationInterval);
        shader.SetInt(EnableTwoTeamCombatId, enableTwoTeamCombat ? 1 : 0);
        shader.SetFloat(DeathClipDurationId, deathClipDuration);
        shader.SetVectorArray(FrustumPlanesId, frustumPlaneVectors);
    }

    private Vector3 GetLodCenter()
    {
        if (lodCenter != null)
            return lodCenter.position;

        Camera activeCamera = GetActiveCullingCamera();
        return activeCamera != null ? activeCamera.transform.position : Vector3.zero;
    }

    private Camera GetActiveCullingCamera()
    {
        if (cullingCamera != null)
            return cullingCamera;

        if (cachedMainCamera == null || !cachedMainCamera.isActiveAndEnabled)
            cachedMainCamera = Camera.main;

        return cachedMainCamera;
    }

    private void UpdateFrustumPlanes()
    {
        Camera activeCamera = GetActiveCullingCamera();
        if (!enableFrustumCulling || activeCamera == null)
        {
            for (int i = 0; i < frustumPlaneVectors.Length; i++)
                frustumPlaneVectors[i] = Vector4.zero;
            return;
        }

        GeometryUtility.CalculateFrustumPlanes(activeCamera, frustumPlanes);
        for (int i = 0; i < frustumPlanes.Length; i++)
        {
            Plane plane = frustumPlanes[i];
            Vector3 normal = plane.normal;
            frustumPlaneVectors[i] = new Vector4(normal.x, normal.y, normal.z, plane.distance);
        }
    }

    private void CopyVisibleCountsToArgs()
    {
        ComputeBuffer.CopyCount(nearAttackerAgentIndexBuffer, nearAttackerArgsBuffer, sizeof(uint));
        ComputeBuffer.CopyCount(midAttackerAgentIndexBuffer, midAttackerArgsBuffer, sizeof(uint));
        ComputeBuffer.CopyCount(farAttackerAgentIndexBuffer, farAttackerArgsBuffer, sizeof(uint));
        ComputeBuffer.CopyCount(nearDefenderAgentIndexBuffer, nearDefenderArgsBuffer, sizeof(uint));
        ComputeBuffer.CopyCount(midDefenderAgentIndexBuffer, midDefenderArgsBuffer, sizeof(uint));
        ComputeBuffer.CopyCount(farDefenderAgentIndexBuffer, farDefenderArgsBuffer, sizeof(uint));
    }

    private void DrawLods()
    {
        DrawLod(runtimeAttackerNearMesh, runtimeAttackerNearMaterial, nearAttackerArgsBuffer, nearAttackerPropertyBlock, ShadowCastingMode.On);
        DrawLod(runtimeAttackerMidMesh, runtimeAttackerMidMaterial, midAttackerArgsBuffer, midAttackerPropertyBlock, ShadowCastingMode.Off);
        DrawLod(runtimeAttackerFarMesh, runtimeAttackerFarMaterial, farAttackerArgsBuffer, farAttackerPropertyBlock, ShadowCastingMode.Off);
        DrawLod(runtimeDefenderNearMesh, runtimeDefenderNearMaterial, nearDefenderArgsBuffer, nearDefenderPropertyBlock, ShadowCastingMode.On);
        DrawLod(runtimeDefenderMidMesh, runtimeDefenderMidMaterial, midDefenderArgsBuffer, midDefenderPropertyBlock, ShadowCastingMode.Off);
        DrawLod(runtimeDefenderFarMesh, runtimeDefenderFarMaterial, farDefenderArgsBuffer, farDefenderPropertyBlock, ShadowCastingMode.Off);
    }

    private void DrawLod(Mesh mesh, Material material, ComputeBuffer argsBuffer, MaterialPropertyBlock propertyBlock, ShadowCastingMode shadowCastingMode)
    {
        if (mesh == null || material == null || argsBuffer == null)
            return;

        Graphics.DrawMeshInstancedIndirect(
            mesh, 0, material, renderBounds, argsBuffer, 0,
            propertyBlock, shadowCastingMode, true, owner.gameObject.layer);
    }
}
