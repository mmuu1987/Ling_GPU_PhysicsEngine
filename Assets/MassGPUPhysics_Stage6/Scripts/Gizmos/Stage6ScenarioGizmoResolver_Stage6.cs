using UnityEngine;

public static class Stage6ScenarioGizmoResolver_Stage6
{
    public static bool TryResolveTeam(
        GPUInstancingManager_Stage6 manager,
        Stage6ScenarioConfig_Stage6 scenarioOverride,
        bool attacker,
        Color fallbackColor,
        out Stage6ScenarioGizmoTeam_Stage6 team)
    {
        Stage6ScenarioConfig_Stage6 scenario = scenarioOverride != null
            ? scenarioOverride
            : manager != null ? manager.scenarioConfig : null;

        Stage6TeamConfig_Stage6 teamConfig = null;
        if (scenario != null)
            teamConfig = attacker ? scenario.attackerTeamConfig : scenario.defenderTeamConfig;

        if (teamConfig == null && manager != null)
            teamConfig = attacker ? manager.attackerTeamConfig : manager.defenderTeamConfig;

        string roleName = attacker ? "Attacker" : "Defender";
        Vector3 fallbackCenter = Vector3.zero;
        Vector3 fallbackSize = new Vector3(0.01f, 0f, 0.01f);
        int fallbackCount = 0;

        if (manager != null)
        {
            GPUInstancingManager_Stage6.TeamCombatSettings settings = attacker
                ? manager.attackerSettings
                : manager.defenderSettings;
            fallbackCenter = settings.spawnCenter;
            fallbackSize = settings.spawnSize;
            fallbackCount = attacker
                ? Mathf.Clamp(manager.attackerCount, 0, manager.instanceCount)
                : Mathf.Max(0, manager.instanceCount - Mathf.Clamp(manager.attackerCount, 0, manager.instanceCount));
        }

        if (teamConfig == null)
        {
            if (manager == null)
            {
                team = default;
                return false;
            }

            team = new Stage6ScenarioGizmoTeam_Stage6(
                roleName,
                roleName,
                "Runtime Cache",
                fallbackCount,
                fallbackColor,
                null,
                null,
                fallbackCenter,
                fallbackSize);
            return true;
        }

        string unitName = teamConfig.unitConfig != null ? teamConfig.unitConfig.unitName : "No Unit";
        Color teamColor = teamConfig.teamColor.maxColorComponent > 0.001f ? teamConfig.teamColor : fallbackColor;
        team = new Stage6ScenarioGizmoTeam_Stage6(
            roleName,
            teamConfig.teamName,
            unitName,
            teamConfig.UnitCount,
            teamColor,
            teamConfig.spawnConfig,
            teamConfig.flowFieldConfig,
            fallbackCenter,
            fallbackSize);
        return true;
    }

    public static bool TryResolvePaintedFlowField(
        Stage6ScenarioGizmoTeam_Stage6 team,
        Color color,
        out Stage6ScenarioGizmoFlowField_Stage6 flowField)
    {
        if (!team.IsValid ||
            team.FlowFieldConfig == null ||
            team.FlowFieldConfig.paintedFlowFieldAsset == null)
        {
            flowField = default;
            return false;
        }

        PaintedFlowFieldAsset_Stage6 asset = team.FlowFieldConfig.paintedFlowFieldAsset;
        flowField = new Stage6ScenarioGizmoFlowField_Stage6(
            $"{team.RoleName} Flow: {asset.name}",
            asset.origin,
            asset.worldSize,
            asset.cellSize,
            asset.resolutionX,
            asset.resolutionZ,
            color);
        return true;
    }

    public static bool TryResolveEstimatedRuntimeFlowField(
        Stage6ScenarioGizmoTeam_Stage6 owner,
        Stage6ScenarioGizmoTeam_Stage6 other,
        Color color,
        out Stage6ScenarioGizmoFlowField_Stage6 flowField)
    {
        if (!owner.IsValid ||
            owner.FlowFieldConfig == null ||
            !owner.FlowFieldConfig.useRuntimeDynamicFlow ||
            !owner.FlowFieldConfig.autoSizeRuntimeFlowField)
        {
            flowField = default;
            return false;
        }

        Bounds bounds = CreateSpawnBounds(owner);
        if (other.IsValid)
            bounds.Encapsulate(CreateSpawnBounds(other));

        float padding = Mathf.Max(0f, owner.FlowFieldConfig.runtimePadding);
        Vector2 origin = new Vector2(bounds.min.x - padding, bounds.min.z - padding);
        Vector2 worldSize = new Vector2(
            Mathf.Max(0.25f, bounds.size.x + padding * 2f),
            Mathf.Max(0.25f, bounds.size.z + padding * 2f));
        float requestedCellSize = Mathf.Max(0.25f, owner.FlowFieldConfig.cellSize);
        int maxResolution = Mathf.Max(16, owner.FlowFieldConfig.runtimeMaxResolution);
        float resolvedCellSize = Mathf.Max(requestedCellSize, Mathf.Max(worldSize.x / maxResolution, worldSize.y / maxResolution));
        int resolutionX = Mathf.Max(1, Mathf.CeilToInt(worldSize.x / resolvedCellSize));
        int resolutionZ = Mathf.Max(1, Mathf.CeilToInt(worldSize.y / resolvedCellSize));

        flowField = new Stage6ScenarioGizmoFlowField_Stage6(
            $"{owner.RoleName} Runtime Flow (estimated)",
            origin,
            worldSize,
            resolvedCellSize,
            resolutionX,
            resolutionZ,
            color);
        return true;
    }

    private static Bounds CreateSpawnBounds(Stage6ScenarioGizmoTeam_Stage6 team)
    {
        Vector3 center = team.SpawnCenter;
        if (team.SpawnShape == Stage6SpawnShape.Circle)
        {
            float diameter = Mathf.Max(0.01f, team.SpawnRadius * 2f);
            return new Bounds(new Vector3(center.x, 0f, center.z), new Vector3(diameter, 1f, diameter));
        }

        Vector3 size = team.SpawnSize;
        return new Bounds(new Vector3(center.x, 0f, center.z), new Vector3(Mathf.Max(0.01f, size.x), 1f, Mathf.Max(0.01f, size.z)));
    }
}

