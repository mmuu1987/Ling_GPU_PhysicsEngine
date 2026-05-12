using System.Runtime.InteropServices;
using UnityEngine;

using static MassGpuShaderPropertyIds_Stage6;
using AgentData = GPUInstancingManager_Stage6.AgentData;

public sealed partial class MassGpuRuntime_Stage6
{
    private void InitializeBuffers()
    {
        if (!TryApplyVatProfile(true))
        {
            owner.enabled = false;
            return;
        }

        if (instanceMesh == null || instanceMaterial == null)
        {
            Debug.LogError("[GPUInstancingManager_Stage6] Missing Mesh or Material.");
            owner.enabled = false;
            return;
        }

        if (!ValidateRequiredComputeShaders())
        {
            owner.enabled = false;
            return;
        }

        instanceCount = Mathf.Max(1, instanceCount);
        midLodRadius = Mathf.Max(midLodRadius, shadowCastingRadius + 0.01f);
        RecalculateGridSettings();

        runtimeAttackerNearMesh = instanceMesh;
        runtimeAttackerMidMesh = midInstanceMesh != null ? midInstanceMesh : instanceMesh;
        runtimeGeneratedFarMesh = farInstanceMesh == null ? MassGpuDrawUtility_Stage6.CreateBillboardQuadMesh() : null;
        runtimeAttackerFarMesh = farInstanceMesh != null ? farInstanceMesh : runtimeGeneratedFarMesh;
        runtimeDefenderNearMesh = defenderInstanceMesh != null ? defenderInstanceMesh : runtimeAttackerNearMesh;
        runtimeDefenderMidMesh = defenderMidInstanceMesh != null ? defenderMidInstanceMesh :
            (defenderVatProfile != null && defenderVatProfile.HasLowLod ? defenderVatProfile.lowLodMesh : runtimeAttackerMidMesh);
        runtimeDefenderFarMesh = defenderFarInstanceMesh != null ? defenderFarInstanceMesh : runtimeAttackerFarMesh;

        runtimeAttackerNearMaterial = instanceMaterial;
        runtimeAttackerMidMaterial = midInstanceMaterial != null ? midInstanceMaterial :
            (farInstanceMaterial != null ? farInstanceMaterial : instanceMaterial);
        runtimeAttackerFarMaterial = farInstanceMaterial != null ? farInstanceMaterial : runtimeAttackerMidMaterial;
        runtimeDefenderNearMaterial = defenderInstanceMaterial != null ? defenderInstanceMaterial : runtimeAttackerNearMaterial;
        runtimeDefenderMidMaterial = defenderMidInstanceMaterial != null ? defenderMidInstanceMaterial :
            (defenderFarInstanceMaterial != null ? defenderFarInstanceMaterial : runtimeDefenderNearMaterial);
        runtimeDefenderFarMaterial = defenderFarInstanceMaterial != null ? defenderFarInstanceMaterial : runtimeDefenderMidMaterial;

        EnableInstancing(runtimeAttackerNearMaterial);
        EnableInstancing(runtimeAttackerMidMaterial);
        EnableInstancing(runtimeAttackerFarMaterial);
        EnableInstancing(runtimeDefenderNearMaterial);
        EnableInstancing(runtimeDefenderMidMaterial);
        EnableInstancing(runtimeDefenderFarMaterial);

        agentBuffer = new ComputeBuffer(instanceCount, Marshal.SizeOf<AgentData>());
        agentPositionReadBuffer = new ComputeBuffer(instanceCount, sizeof(float) * 2);
        agentPositionWriteBuffer = new ComputeBuffer(instanceCount, sizeof(float) * 2);
        gridCountsBuffer = new ComputeBuffer(gridCellCount, sizeof(uint));
        gridAgentIndicesBuffer = new ComputeBuffer(gridCellCount * maxAgentsPerCell, sizeof(uint));
        teamIdBuffer = new ComputeBuffer(instanceCount, sizeof(int));
        hpBuffer = new ComputeBuffer(instanceCount, sizeof(int));
        targetAgentIndexBuffer = new ComputeBuffer(instanceCount, sizeof(int));
        attackCooldownBuffer = new ComputeBuffer(instanceCount, sizeof(float));
        homePositionBuffer = new ComputeBuffer(instanceCount, sizeof(float) * 3);
        pendingDamageReadBuffer = new ComputeBuffer(instanceCount, sizeof(int));
        pendingDamageWriteBuffer = new ComputeBuffer(instanceCount, sizeof(int));
        nearAttackerAgentIndexBuffer = MassGpuDrawUtility_Stage6.CreateAppendIndexBuffer(instanceCount);
        midAttackerAgentIndexBuffer = MassGpuDrawUtility_Stage6.CreateAppendIndexBuffer(instanceCount);
        farAttackerAgentIndexBuffer = MassGpuDrawUtility_Stage6.CreateAppendIndexBuffer(instanceCount);
        nearDefenderAgentIndexBuffer = MassGpuDrawUtility_Stage6.CreateAppendIndexBuffer(instanceCount);
        midDefenderAgentIndexBuffer = MassGpuDrawUtility_Stage6.CreateAppendIndexBuffer(instanceCount);
        farDefenderAgentIndexBuffer = MassGpuDrawUtility_Stage6.CreateAppendIndexBuffer(instanceCount);
        BuildAndUploadFlowField();
        CreateRuntimeDynamicFlowResources();
        nextDynamicFlowUpdateTime = Time.time + Mathf.Max(0.1f, dynamicFlowUpdateInterval);
        nextDefenderDynamicFlowUpdateTime = Time.time + Mathf.Max(0.1f, dynamicDefenderFlowUpdateInterval);

        UploadInitialAgents();

        nearAttackerArgsBuffer = MassGpuDrawUtility_Stage6.CreateArgsBuffer(runtimeAttackerNearMesh);
        midAttackerArgsBuffer = MassGpuDrawUtility_Stage6.CreateArgsBuffer(runtimeAttackerMidMesh);
        farAttackerArgsBuffer = MassGpuDrawUtility_Stage6.CreateArgsBuffer(runtimeAttackerFarMesh);
        nearDefenderArgsBuffer = MassGpuDrawUtility_Stage6.CreateArgsBuffer(runtimeDefenderNearMesh);
        midDefenderArgsBuffer = MassGpuDrawUtility_Stage6.CreateArgsBuffer(runtimeDefenderMidMesh);
        farDefenderArgsBuffer = MassGpuDrawUtility_Stage6.CreateArgsBuffer(runtimeDefenderFarMesh);

        kernels = MassGpuShaderSet_Stage6.Find(
            spatialHashShader,
            runtimeFlowShader,
            combatSimulationShader,
            lodClassificationShader);

        BindComputeBuffers(kernels.SpatialHashShader, kernels.ClearGrid);
        BindComputeBuffers(kernels.SpatialHashShader, kernels.BuildSpatialHash);
        BindComputeBuffers(kernels.RuntimeFlowShader, kernels.ClearRuntimeAttackerFlowResources);
        BindComputeBuffers(kernels.RuntimeFlowShader, kernels.BuildRuntimeAttackerTargetDensity);
        BindComputeBuffers(kernels.RuntimeFlowShader, kernels.SelectRuntimeAttackerFlowTargets);
        BindComputeBuffers(kernels.RuntimeFlowShader, kernels.GenerateRuntimeAttackerFlowField);
        BindComputeBuffers(kernels.RuntimeFlowShader, kernels.ClearRuntimeDefenderFlowResources);
        BindComputeBuffers(kernels.RuntimeFlowShader, kernels.BuildRuntimeDefenderTargetDensity);
        BindComputeBuffers(kernels.RuntimeFlowShader, kernels.SelectRuntimeDefenderFlowTargets);
        BindComputeBuffers(kernels.RuntimeFlowShader, kernels.GenerateRuntimeDefenderFlowField);
        BindComputeBuffers(kernels.CombatSimulationShader, kernels.ClearPendingDamage);
        BindComputeBuffers(kernels.CombatSimulationShader, kernels.SimulateCombatAndAccumulateDamage);
        BindComputeBuffers(kernels.LodClassificationShader, kernels.ClassifyVisibleAgentsByTeam);

        nearAttackerPropertyBlock = MassGpuDrawUtility_Stage6.CreatePropertyBlock(agentBuffer, nearAttackerAgentIndexBuffer, AgentBufferId, VisibleAgentIndicesId);
        midAttackerPropertyBlock = MassGpuDrawUtility_Stage6.CreatePropertyBlock(agentBuffer, midAttackerAgentIndexBuffer, AgentBufferId, VisibleAgentIndicesId);
        farAttackerPropertyBlock = MassGpuDrawUtility_Stage6.CreatePropertyBlock(agentBuffer, farAttackerAgentIndexBuffer, AgentBufferId, VisibleAgentIndicesId);
        nearDefenderPropertyBlock = MassGpuDrawUtility_Stage6.CreatePropertyBlock(agentBuffer, nearDefenderAgentIndexBuffer, AgentBufferId, VisibleAgentIndicesId);
        midDefenderPropertyBlock = MassGpuDrawUtility_Stage6.CreatePropertyBlock(agentBuffer, midDefenderAgentIndexBuffer, AgentBufferId, VisibleAgentIndicesId);
        farDefenderPropertyBlock = MassGpuDrawUtility_Stage6.CreatePropertyBlock(agentBuffer, farDefenderAgentIndexBuffer, AgentBufferId, VisibleAgentIndicesId);

        SyncRuntimeVatBindings();

        agentThreadGroupsX = Mathf.CeilToInt(instanceCount / 64f);
        gridThreadGroupsX = Mathf.CeilToInt(gridCellCount / 64f);

        Vector2 gridCenter = gridOrigin + activeWorldSize * 0.5f;
        renderBounds = new Bounds(new Vector3(gridCenter.x, 0f, gridCenter.y), new Vector3(
            activeWorldSize.x + 40f,
            Mathf.Max(120f, spawnArea.y * 2f + 20f),
            activeWorldSize.y + 40f));

        Debug.Log($"[GPUInstancingManager_Stage6] Initialized {instanceCount} instances, grid {gridResolutionX}x{gridResolutionZ}, max {maxAgentsPerCell}/cell.");
    }

    private void RecalculateGridSettings()
    {
        cellSize = Mathf.Max(0.1f, cellSize);
        maxAgentsPerCell = Mathf.Max(1, maxAgentsPerCell);

        MassSpatialHashGridSettings_Stage6 grid;
        if (ShouldAutoSizeCombatSimulationWorld())
        {
            Bounds combatBounds = CalculateCombatSpawnBounds();
            float padding = Mathf.Max(0f, combatSimulationBoundsPadding) + Mathf.Max(0f, boundaryPadding);
            Vector2 min = new Vector2(combatBounds.min.x - padding, combatBounds.min.z - padding);
            Vector2 max = new Vector2(combatBounds.max.x + padding, combatBounds.max.z + padding);
            grid = MassSpatialHashGridSettings_Stage6.FromBounds(min, max, cellSize);
        }
        else
        {
            grid = MassSpatialHashGridSettings_Stage6.Calculate(
                simulationWorldSize,
                spawnArea,
                boundaryPadding,
                cellSize);
        }

        activeWorldSize = grid.WorldSize;
        gridResolutionX = grid.ResolutionX;
        gridResolutionZ = grid.ResolutionZ;
        gridCellCount = grid.CellCount;
        gridOrigin = grid.Origin;
    }

    private bool ShouldAutoSizeCombatSimulationWorld()
    {
        return enableTwoTeamCombat &&
               autoSizeSimulationWorldForTwoTeamCombat &&
               simulationWorldSize.x <= 0f &&
               simulationWorldSize.y <= 0f;
    }

    private void BindComputeBuffers(ComputeShader shader, int kernel)
    {
        shader.SetBuffer(kernel, AgentBufferId, agentBuffer);

        if (shader == kernels.SpatialHashShader)
        {
            if (kernel == kernels.ClearGrid)
            {
                shader.SetBuffer(kernel, GridCountsId, gridCountsBuffer);
                return;
            }

            if (kernel == kernels.BuildSpatialHash)
            {
                shader.SetBuffer(kernel, AgentPositionReadBufferId, agentPositionReadBuffer);
                shader.SetBuffer(kernel, HpReadBufferId, hpBuffer);
                shader.SetBuffer(kernel, GridCountsId, gridCountsBuffer);
                shader.SetBuffer(kernel, GridAgentIndicesId, gridAgentIndicesBuffer);
                return;
            }
        }

        if (shader == kernels.RuntimeFlowShader)
        {
            if (kernel == kernels.ClearRuntimeAttackerFlowResources)
            {
                shader.SetBuffer(kernel, RuntimeAttackerTargetDensityId, runtimeAttackerTargetDensityBuffer);
                shader.SetBuffer(kernel, RuntimeAttackerFlowStatsId, runtimeAttackerFlowStatsBuffer);
                shader.SetBuffer(kernel, RuntimeAttackerFlowTargetsId, runtimeAttackerFlowTargetsBuffer);
                return;
            }

            if (kernel == kernels.BuildRuntimeAttackerTargetDensity)
            {
                shader.SetBuffer(kernel, AgentPositionReadBufferId, agentPositionReadBuffer);
                shader.SetBuffer(kernel, HpReadBufferId, hpBuffer);
                shader.SetBuffer(kernel, RuntimeAttackerTargetDensityId, runtimeAttackerTargetDensityBuffer);
                shader.SetBuffer(kernel, RuntimeAttackerFlowStatsId, runtimeAttackerFlowStatsBuffer);
                return;
            }

            if (kernel == kernels.SelectRuntimeAttackerFlowTargets)
            {
                shader.SetBuffer(kernel, RuntimeAttackerTargetDensityId, runtimeAttackerTargetDensityBuffer);
                shader.SetBuffer(kernel, RuntimeAttackerFlowStatsId, runtimeAttackerFlowStatsBuffer);
                shader.SetBuffer(kernel, RuntimeAttackerFlowTargetsId, runtimeAttackerFlowTargetsBuffer);
                return;
            }

            if (kernel == kernels.GenerateRuntimeAttackerFlowField)
            {
                shader.SetBuffer(kernel, FlowFieldDirectionsId, flowFieldDirectionsBuffer);
                shader.SetBuffer(kernel, RuntimeAttackerTargetDensityId, runtimeAttackerTargetDensityBuffer);
                shader.SetBuffer(kernel, RuntimeAttackerFlowStatsId, runtimeAttackerFlowStatsBuffer);
                shader.SetBuffer(kernel, RuntimeAttackerFlowTargetsId, runtimeAttackerFlowTargetsBuffer);
                shader.SetTexture(kernel, RuntimeAttackerFlowPreviewTextureId, runtimeAttackerFlowPreviewTexture);
                return;
            }

            if (kernel == kernels.ClearRuntimeDefenderFlowResources)
            {
                shader.SetBuffer(kernel, RuntimeDefenderTargetDensityId, runtimeDefenderTargetDensityBuffer);
                shader.SetBuffer(kernel, RuntimeDefenderFlowStatsId, runtimeDefenderFlowStatsBuffer);
                shader.SetBuffer(kernel, RuntimeDefenderFlowTargetsId, runtimeDefenderFlowTargetsBuffer);
                return;
            }

            if (kernel == kernels.BuildRuntimeDefenderTargetDensity)
            {
                shader.SetBuffer(kernel, AgentPositionReadBufferId, agentPositionReadBuffer);
                shader.SetBuffer(kernel, HpReadBufferId, hpBuffer);
                shader.SetBuffer(kernel, RuntimeDefenderTargetDensityId, runtimeDefenderTargetDensityBuffer);
                shader.SetBuffer(kernel, RuntimeDefenderFlowStatsId, runtimeDefenderFlowStatsBuffer);
                return;
            }

            if (kernel == kernels.SelectRuntimeDefenderFlowTargets)
            {
                shader.SetBuffer(kernel, RuntimeDefenderTargetDensityId, runtimeDefenderTargetDensityBuffer);
                shader.SetBuffer(kernel, RuntimeDefenderFlowStatsId, runtimeDefenderFlowStatsBuffer);
                shader.SetBuffer(kernel, RuntimeDefenderFlowTargetsId, runtimeDefenderFlowTargetsBuffer);
                return;
            }

            if (kernel == kernels.GenerateRuntimeDefenderFlowField)
            {
                shader.SetBuffer(kernel, DefenderFlowFieldDirectionsId, defenderFlowFieldDirectionsBuffer);
                shader.SetBuffer(kernel, RuntimeDefenderTargetDensityId, runtimeDefenderTargetDensityBuffer);
                shader.SetBuffer(kernel, RuntimeDefenderFlowStatsId, runtimeDefenderFlowStatsBuffer);
                shader.SetBuffer(kernel, RuntimeDefenderFlowTargetsId, runtimeDefenderFlowTargetsBuffer);
                shader.SetTexture(kernel, RuntimeDefenderFlowPreviewTextureId, runtimeDefenderFlowPreviewTexture);
                return;
            }
        }

        if (shader == kernels.CombatSimulationShader)
        {
            if (kernel == kernels.ClearPendingDamage)
            {
                shader.SetBuffer(kernel, PendingDamageBufferId, pendingDamageWriteBuffer);
                return;
            }

            if (kernel == kernels.SimulateCombatAndAccumulateDamage)
            {
                shader.SetBuffer(kernel, AgentPositionReadBufferId, agentPositionReadBuffer);
                shader.SetBuffer(kernel, AgentPositionBufferId, agentPositionWriteBuffer);
                shader.SetBuffer(kernel, GridCountsReadBufferId, gridCountsBuffer);
                shader.SetBuffer(kernel, GridAgentIndicesReadBufferId, gridAgentIndicesBuffer);
                shader.SetBuffer(kernel, TeamIdReadBufferId, teamIdBuffer);
                shader.SetBuffer(kernel, HpBufferId, hpBuffer);
                shader.SetBuffer(kernel, TargetAgentIndexBufferId, targetAgentIndexBuffer);
                shader.SetBuffer(kernel, AttackCooldownBufferId, attackCooldownBuffer);
                shader.SetBuffer(kernel, HomePositionReadBufferId, homePositionBuffer);
                shader.SetBuffer(kernel, PendingDamageBufferId, pendingDamageWriteBuffer);
                shader.SetBuffer(kernel, PendingDamageReadBufferId, pendingDamageReadBuffer);
                shader.SetBuffer(kernel, FlowFieldDirectionsId, flowFieldDirectionsBuffer);
                shader.SetBuffer(kernel, DefenderFlowFieldDirectionsId, defenderFlowFieldDirectionsBuffer);
                return;
            }
        }

        if (shader == kernels.LodClassificationShader && kernel == kernels.ClassifyVisibleAgentsByTeam)
        {
            shader.SetBuffer(kernel, TeamIdReadBufferId, teamIdBuffer);
            shader.SetBuffer(kernel, HpReadBufferId, hpBuffer);
            shader.SetBuffer(kernel, NearAttackerAgentIndicesId, nearAttackerAgentIndexBuffer);
            shader.SetBuffer(kernel, MidAttackerAgentIndicesId, midAttackerAgentIndexBuffer);
            shader.SetBuffer(kernel, FarAttackerAgentIndicesId, farAttackerAgentIndexBuffer);
            shader.SetBuffer(kernel, NearDefenderAgentIndicesId, nearDefenderAgentIndexBuffer);
            shader.SetBuffer(kernel, MidDefenderAgentIndicesId, midDefenderAgentIndexBuffer);
            shader.SetBuffer(kernel, FarDefenderAgentIndicesId, farDefenderAgentIndexBuffer);
        }
    }

    private void ReleaseBuffers()
    {
        runtimeDynamicAttackerFlowActive = false;
        runtimeDynamicDefenderFlowActive = false;

        buffers.ReleaseAll();
        ReleaseRuntimeFlowPreviewTextures();

        if (runtimeGeneratedFarMesh != null)
        {
            Object.Destroy(runtimeGeneratedFarMesh);
            runtimeGeneratedFarMesh = null;
        }
    }
}
