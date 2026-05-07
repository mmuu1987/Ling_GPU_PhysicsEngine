using UnityEngine;
using UnityEngine.Rendering;

using static MassGpuShaderPropertyIds_Stage6;
using DefenderMovementMode = GPUInstancingManager_Stage6.DefenderMovementMode;

public sealed partial class MassGpuRuntime_Stage6
{
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
        teamIdBuffer.SetData(initialData.TeamIds);
        hpBuffer.SetData(initialData.Hp);
        targetAgentIndexBuffer.SetData(initialData.TargetAgentIndices);
        attackCooldownBuffer.SetData(initialData.AttackCooldowns);
        homePositionBuffer.SetData(initialData.HomePositions);
        pendingDamageBuffer.SetData(initialData.PendingDamage);
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
            gridThreadGroupsX,
            agentThreadGroupsX,
            FlowFieldThreadGroupsX,
            DefenderFlowFieldThreadGroupsX,
            rebuildRuntimeAttackerFlow,
            rebuildRuntimeDefenderFlow);
        CopyVisibleCountsToArgs();
        DrawLods();
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

        kernels.SetFloat(DeltaTimeId, Time.deltaTime);
        kernels.SetFloat(AnimationDurationId, AnimationDuration);
        kernels.SetInt(FrameIndexId, Time.frameCount);

        kernels.SetVector(LodCenterId, center);
        kernels.SetFloat(NearLodRadiusSqrId, shadowCastingRadius * shadowCastingRadius);
        kernels.SetFloat(MidLodRadiusSqrId, midLodRadius * midLodRadius);
        kernels.SetInt(EnableFrustumCullingId, enableFrustumCulling ? 1 : 0);
        kernels.SetFloat(CullingRadiusId, cullingRadius);
        kernels.SetInt(NearAnimationIntervalId, nearAnimationInterval);
        kernels.SetInt(MidAnimationIntervalId, midAnimationInterval);
        kernels.SetInt(FarAnimationIntervalId, farAnimationInterval);

        kernels.SetInt(GridCellCountId, gridCellCount);
        kernels.SetInts(GridResolutionId, gridResolutionX, gridResolutionZ);
        kernels.SetVector(GridOriginId, new Vector4(gridOrigin.x, gridOrigin.y, 0f, 0f));
        kernels.SetVector(GridWorldSizeId, new Vector4(activeWorldSize.x, activeWorldSize.y, 0f, 0f));
        kernels.SetFloat(CellSizeId, cellSize);
        kernels.SetInt(MaxAgentsPerCellId, maxAgentsPerCell);
        kernels.SetFloat(AttackerAgentRadiusId, attackerSettings.agentRadius);
        kernels.SetFloat(DefenderAgentRadiusId, defenderSettings.agentRadius);
        kernels.SetFloat(AttackerSeparationStrengthId, attackerSettings.separationStrength);
        kernels.SetFloat(DefenderSeparationStrengthId, defenderSettings.separationStrength);
        kernels.SetFloat(AttackerVelocityDampingId, attackerSettings.velocityDamping);
        kernels.SetFloat(DefenderVelocityDampingId, defenderSettings.velocityDamping);
        kernels.SetFloat(AttackerMaxSpeedId, attackerSettings.maxSpeed);
        kernels.SetFloat(DefenderMaxSpeedId, defenderSettings.maxSpeed);
        kernels.SetFloat(BoundaryPaddingId, boundaryPadding);
        kernels.SetInt(FlowFieldEnabledId, enableFlowFieldNavigation && flowFieldDirectionsBuffer != null ? 1 : 0);
        kernels.SetInts(FlowFieldResolutionId, flowFieldResolutionX, flowFieldResolutionZ);
        kernels.SetVector(FlowFieldOriginId, new Vector4(flowFieldOrigin.x, flowFieldOrigin.y, 0f, 0f));
        kernels.SetFloat(FlowFieldCellSizeId, activeFlowFieldCellSize);
        kernels.SetFloat(FlowFieldWeightId, flowFieldWeight);
        kernels.SetFloat(FlowFieldResponsivenessId, flowFieldResponsiveness);
        kernels.SetInt(RuntimeFlowPreviewModeId, (int)runtimeFlowPreviewMode);
        kernels.SetInt(RuntimeDynamicAttackerFlowEnabledId, ShouldUseRuntimeDynamicAttackerFlowField() ? 1 : 0);
        kernels.SetInt(RuntimeDynamicDefenderFlowEnabledId, ShouldUseRuntimeDynamicDefenderFlowField() ? 1 : 0);
        kernels.SetInt(DynamicFlowSectorCountId, dynamicFlowSectorCount);
        kernels.SetFloat(DynamicFlowTargetStopRadiusId, dynamicFlowTargetStopRadius);
        kernels.SetInt(DynamicFlowMinDefendersPerTargetId, dynamicFlowMinDefendersPerTarget);
        kernels.SetInt(DynamicDefenderFlowSectorCountId, dynamicDefenderFlowSectorCount);
        kernels.SetFloat(DynamicDefenderFlowTargetStopRadiusId, dynamicDefenderFlowTargetStopRadius);
        kernels.SetInt(DynamicDefenderFlowMinAttackersPerTargetId, dynamicDefenderFlowMinAttackersPerTarget);
        bool defenderFlowEnabled = enableFlowFieldNavigation &&
                                   defenderMovementMode == DefenderMovementMode.UseDefenderFlowField &&
                                   defenderFlowFieldDirectionsBuffer != null;
        kernels.SetInt(DefenderMovementModeId, defenderFlowEnabled ? (int)DefenderMovementMode.UseDefenderFlowField : (int)DefenderMovementMode.HoldPositionNoSeparation);
        kernels.SetInt(DefenderFlowFieldEnabledId, defenderFlowEnabled ? 1 : 0);
        kernels.SetInts(DefenderFlowFieldResolutionId, defenderFlowFieldResolutionX, defenderFlowFieldResolutionZ);
        kernels.SetVector(DefenderFlowFieldOriginId, new Vector4(defenderFlowFieldOrigin.x, defenderFlowFieldOrigin.y, 0f, 0f));
        kernels.SetFloat(DefenderFlowFieldCellSizeId, activeDefenderFlowFieldCellSize);
        kernels.SetInt(EnableTwoTeamCombatId, enableTwoTeamCombat ? 1 : 0);
        kernels.SetInt(BattleStartedId, battleStarted ? 1 : 0);
        kernels.SetInt(AttackerCountId, Mathf.Clamp(attackerCount, 0, instanceCount));
        kernels.SetFloat(AttackerTargetAcquireRadiusId, attackerSettings.targetAcquireRadius);
        kernels.SetFloat(DefenderTargetAcquireRadiusId, defenderSettings.targetAcquireRadius);
        kernels.SetFloat(AttackerAttackRangeId, attackerSettings.attackRange);
        kernels.SetFloat(DefenderAttackRangeId, defenderSettings.attackRange);
        kernels.SetInt(AttackerAttackDamageId, attackerSettings.attackDamage);
        kernels.SetInt(DefenderAttackDamageId, defenderSettings.attackDamage);
        kernels.SetFloat(AttackerAttackIntervalId, attackerSettings.attackInterval);
        kernels.SetFloat(DefenderAttackIntervalId, defenderSettings.attackInterval);
        kernels.SetFloat(DefenderGuardRadiusId, defenderGuardRadius);
        kernels.SetFloat(DefenderMaxChaseDistanceId, defenderMaxChaseDistance);
        kernels.SetFloat(DeathClipDurationId, deathClipDuration);

        UpdateFrustumPlanes();
        kernels.SetVectorArray(FrustumPlanesId, frustumPlaneVectors);
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
        return cullingCamera != null ? cullingCamera : Camera.main;
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
