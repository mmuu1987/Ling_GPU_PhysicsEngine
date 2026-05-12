using UnityEngine;

using DefenderMovementMode = GPUInstancingManager_Stage6.DefenderMovementMode;
using TeamCombatSettings = GPUInstancingManager_Stage6.TeamCombatSettings;

public sealed partial class MassGpuRuntime_Stage6
{
    public void ApplyScenarioConfig()
    {
        if (scenarioConfig == null)
            return;

        if (scenarioConfig.attackerTeamConfig != null)
            attackerTeamConfig = scenarioConfig.attackerTeamConfig;
        if (scenarioConfig.defenderTeamConfig != null)
            defenderTeamConfig = scenarioConfig.defenderTeamConfig;

        applyConfigUnitCounts = scenarioConfig.applyUnitCounts;
        enableTwoTeamCombat = scenarioConfig.enableTwoTeamCombat;

        ApplyConfigAssetsToManager();
    }

    public void ApplyConfigAssetsToManager()
    {
        MigrateLegacyTeamSettingsIfNeeded();

        bool hasAttackerConfig = attackerTeamConfig != null;
        bool hasDefenderConfig = defenderTeamConfig != null;
        if (!hasAttackerConfig && !hasDefenderConfig)
            return;

        if (hasAttackerConfig)
            ApplyTeamConfig(attackerTeamConfig, true);

        if (hasDefenderConfig)
            ApplyTeamConfig(defenderTeamConfig, false);

        ApplyConfiguredUnitCounts();
        ApplyConfiguredFlowNavigationFlag();
        enableTwoTeamCombat = hasAttackerConfig && hasDefenderConfig ? true : enableTwoTeamCombat;
        splitTeamSettingsInitialized = true;

        attackerSettings.Normalize();
        defenderSettings.Normalize();
        attackerCount = Mathf.Clamp(attackerCount, 0, instanceCount);
        TryApplyVatProfile(false);
    }

    private void ApplyTeamConfig(Stage6TeamConfig_Stage6 teamConfig, bool isAttacker)
    {
        TeamCombatSettings settings = isAttacker ? attackerSettings : defenderSettings;
        teamConfig.ApplyTo(ref settings);

        if (isAttacker)
            attackerSettings = settings;
        else
            defenderSettings = settings;

        ApplyRenderConfig(teamConfig.ResolvedRenderConfig, isAttacker);
        ApplyFlowFieldConfig(teamConfig.flowFieldConfig, isAttacker);

        if (!isAttacker)
        {
            defenderGuardRadius = teamConfig.guardRadius;
            defenderMaxChaseDistance = teamConfig.maxChaseDistance;
            defenderMovementMode = ShouldUseDefenderFlowField(teamConfig)
                ? DefenderMovementMode.UseDefenderFlowField
                : DefenderMovementMode.HoldPositionNoSeparation;
        }
    }

    private void ApplyRenderConfig(Stage6RenderConfig_Stage6 renderConfig, bool isAttacker)
    {
        if (renderConfig == null)
            return;

        if (isAttacker)
        {
            if (renderConfig.vatProfile != null)
                vatProfile = renderConfig.vatProfile;
            if (renderConfig.nearMesh != null)
                instanceMesh = renderConfig.nearMesh;
            if (renderConfig.midMesh != null)
                midInstanceMesh = renderConfig.midMesh;
            if (renderConfig.farMesh != null)
                farInstanceMesh = renderConfig.farMesh;
            if (renderConfig.nearMaterial != null)
                instanceMaterial = renderConfig.nearMaterial;
            if (renderConfig.midMaterial != null)
                midInstanceMaterial = renderConfig.midMaterial;
            if (renderConfig.farMaterial != null)
                farInstanceMaterial = renderConfig.farMaterial;
            return;
        }

        if (renderConfig.vatProfile != null)
            defenderVatProfile = renderConfig.vatProfile;
        if (renderConfig.nearMesh != null)
            defenderInstanceMesh = renderConfig.nearMesh;
        if (renderConfig.midMesh != null)
            defenderMidInstanceMesh = renderConfig.midMesh;
        if (renderConfig.farMesh != null)
            defenderFarInstanceMesh = renderConfig.farMesh;
        if (renderConfig.nearMaterial != null)
            defenderInstanceMaterial = renderConfig.nearMaterial;
        if (renderConfig.midMaterial != null)
            defenderMidInstanceMaterial = renderConfig.midMaterial;
        if (renderConfig.farMaterial != null)
            defenderFarInstanceMaterial = renderConfig.farMaterial;
    }

    private void ApplyFlowFieldConfig(Stage6FlowFieldConfig_Stage6 flowConfig, bool isAttacker)
    {
        if (flowConfig == null)
            return;

        if (isAttacker || attackerTeamConfig == null)
        {
            flowFieldCellSize = flowConfig.cellSize;
            flowFieldWeight = flowConfig.weight;
            flowFieldResponsiveness = flowConfig.responsiveness;
        }

        if (isAttacker)
        {
            paintedFlowFieldAsset = flowConfig.paintedFlowFieldAsset;
            enableRuntimeDynamicAttackerFlowField = flowConfig.useRuntimeDynamicFlow;
            autoSizeRuntimeAttackerFlowField = flowConfig.autoSizeRuntimeFlowField;
            runtimeFlowFieldPadding = flowConfig.runtimePadding;
            runtimeFlowFieldMaxResolution = flowConfig.runtimeMaxResolution;
            dynamicFlowUpdateInterval = flowConfig.updateInterval;
            dynamicFlowSectorCount = flowConfig.sectorCount;
            dynamicFlowTargetStopRadius = flowConfig.targetStopRadius;
            dynamicFlowMinDefendersPerTarget = flowConfig.minEnemiesPerTarget;
            return;
        }

        defenderPaintedFlowFieldAsset = flowConfig.paintedFlowFieldAsset;
        enableRuntimeDynamicDefenderFlowField = flowConfig.useRuntimeDynamicFlow;
        autoSizeRuntimeDefenderFlowField = flowConfig.autoSizeRuntimeFlowField;
        runtimeDefenderFlowFieldPadding = flowConfig.runtimePadding;
        runtimeDefenderFlowFieldMaxResolution = flowConfig.runtimeMaxResolution;
        dynamicDefenderFlowUpdateInterval = flowConfig.updateInterval;
        dynamicDefenderFlowSectorCount = flowConfig.sectorCount;
        dynamicDefenderFlowTargetStopRadius = flowConfig.targetStopRadius;
        dynamicDefenderFlowMinAttackersPerTarget = flowConfig.minEnemiesPerTarget;
    }

    private void ApplyConfiguredUnitCounts()
    {
        if (!applyConfigUnitCounts)
            return;

        bool hasAttackerCount = TryGetConfiguredUnitCount(attackerTeamConfig, out int configuredAttackerCount);
        bool hasDefenderCount = TryGetConfiguredUnitCount(defenderTeamConfig, out int configuredDefenderCount);

        if (hasAttackerCount)
            attackerCount = configuredAttackerCount;

        if (hasAttackerCount && hasDefenderCount)
            instanceCount = Mathf.Max(1, configuredAttackerCount + configuredDefenderCount);
        else if (hasAttackerCount)
            instanceCount = Mathf.Max(instanceCount, configuredAttackerCount);

        attackerCount = Mathf.Clamp(attackerCount, 0, instanceCount);
    }

    private void ApplyConfiguredFlowNavigationFlag()
    {
        bool hasAttackerFlowConfig = attackerTeamConfig != null && attackerTeamConfig.flowFieldConfig != null;
        bool hasDefenderFlowConfig = defenderTeamConfig != null && defenderTeamConfig.flowFieldConfig != null;
        if (!hasAttackerFlowConfig && !hasDefenderFlowConfig)
            return;

        enableFlowFieldNavigation =
            RequestsNavigation(attackerTeamConfig) ||
            RequestsNavigation(defenderTeamConfig);
    }

    private static bool TryGetConfiguredUnitCount(Stage6TeamConfig_Stage6 teamConfig, out int unitCount)
    {
        if (teamConfig != null && teamConfig.spawnConfig != null)
        {
            unitCount = Mathf.Max(0, teamConfig.spawnConfig.unitCount);
            return true;
        }

        unitCount = 0;
        return false;
    }

    private static bool RequestsNavigation(Stage6TeamConfig_Stage6 teamConfig)
    {
        return teamConfig != null &&
               teamConfig.flowFieldConfig != null &&
               teamConfig.flowFieldConfig.RequestsNavigation;
    }

    private static bool ShouldUseDefenderFlowField(Stage6TeamConfig_Stage6 teamConfig)
    {
        if (!RequestsNavigation(teamConfig))
            return false;

        return teamConfig.behaviorMode == Stage6TeamBehaviorMode.Attack ||
               teamConfig.behaviorMode == Stage6TeamBehaviorMode.FlowFieldAdvance ||
               teamConfig.behaviorMode == Stage6TeamBehaviorMode.Hybrid;
    }

    private bool HasRequiredComputeShaders()
    {
        return MassGpuShaderSet_Stage6.HasRequiredShaders(
            spatialHashShader,
            runtimeFlowShader,
            combatSimulationShader,
            lodClassificationShader);
    }

    private bool ValidateRequiredComputeShaders()
    {
        if (HasRequiredComputeShaders())
            return true;

        if (computeShader != null)
        {
            Debug.LogError("[GPUInstancingManager_Stage6] Legacy computeShader is assigned, but Stage6 now requires split compute assets: AgentSpatialHash_Stage6, AgentRuntimeFlow_Stage6, AgentCombatSimulation_Stage6, and AgentLodClassification_Stage6.");
        }
        else
        {
            Debug.LogError("[GPUInstancingManager_Stage6] Missing split ComputeShader references: spatialHashShader, runtimeFlowShader, combatSimulationShader, and lodClassificationShader.");
        }

        return false;
    }
}
