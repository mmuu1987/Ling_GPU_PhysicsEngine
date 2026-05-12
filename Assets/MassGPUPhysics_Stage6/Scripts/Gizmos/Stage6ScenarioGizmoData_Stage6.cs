using UnityEngine;

public readonly struct Stage6ScenarioGizmoTeam_Stage6
{
    public readonly bool IsValid;
    public readonly string RoleName;
    public readonly string TeamName;
    public readonly string UnitName;
    public readonly int UnitCount;
    public readonly Color TeamColor;
    public readonly Stage6SpawnConfig_Stage6 SpawnConfig;
    public readonly Stage6FlowFieldConfig_Stage6 FlowFieldConfig;
    public readonly Vector3 FallbackSpawnCenter;
    public readonly Vector3 FallbackSpawnSize;

    public Stage6ScenarioGizmoTeam_Stage6(
        string roleName,
        string teamName,
        string unitName,
        int unitCount,
        Color teamColor,
        Stage6SpawnConfig_Stage6 spawnConfig,
        Stage6FlowFieldConfig_Stage6 flowFieldConfig,
        Vector3 fallbackSpawnCenter,
        Vector3 fallbackSpawnSize)
    {
        IsValid = true;
        RoleName = roleName;
        TeamName = string.IsNullOrWhiteSpace(teamName) ? roleName : teamName;
        UnitName = string.IsNullOrWhiteSpace(unitName) ? "No Unit" : unitName;
        UnitCount = Mathf.Max(0, unitCount);
        TeamColor = teamColor;
        SpawnConfig = spawnConfig;
        FlowFieldConfig = flowFieldConfig;
        FallbackSpawnCenter = fallbackSpawnCenter;
        FallbackSpawnSize = fallbackSpawnSize;
    }

    public Vector3 SpawnCenter => SpawnConfig != null ? SpawnConfig.center : FallbackSpawnCenter;

    public Vector3 SpawnSize => SpawnConfig != null ? SpawnConfig.EffectiveRectSize : SanitizeSize(FallbackSpawnSize);

    public Stage6SpawnShape SpawnShape => SpawnConfig != null ? SpawnConfig.shape : Stage6SpawnShape.Rectangle;

    public float SpawnRadius => SpawnConfig != null ? Mathf.Max(0.01f, SpawnConfig.radius) : Mathf.Max(SpawnSize.x, SpawnSize.z) * 0.5f;

    private static Vector3 SanitizeSize(Vector3 size)
    {
        return new Vector3(Mathf.Max(0.01f, size.x), Mathf.Max(0f, size.y), Mathf.Max(0.01f, size.z));
    }
}

public readonly struct Stage6ScenarioGizmoFlowField_Stage6
{
    public readonly bool IsValid;
    public readonly string Label;
    public readonly Vector2 Origin;
    public readonly Vector2 WorldSize;
    public readonly float CellSize;
    public readonly int ResolutionX;
    public readonly int ResolutionZ;
    public readonly Color Color;

    public Stage6ScenarioGizmoFlowField_Stage6(
        string label,
        Vector2 origin,
        Vector2 worldSize,
        float cellSize,
        int resolutionX,
        int resolutionZ,
        Color color)
    {
        IsValid = true;
        Label = string.IsNullOrWhiteSpace(label) ? "Flow Field" : label;
        Origin = origin;
        WorldSize = new Vector2(Mathf.Max(0.01f, worldSize.x), Mathf.Max(0.01f, worldSize.y));
        CellSize = Mathf.Max(0.01f, cellSize);
        ResolutionX = Mathf.Max(1, resolutionX);
        ResolutionZ = Mathf.Max(1, resolutionZ);
        Color = color;
    }

    public Vector3 Center => new Vector3(Origin.x + WorldSize.x * 0.5f, 0f, Origin.y + WorldSize.y * 0.5f);
    public Vector3 Size => new Vector3(WorldSize.x, 0f, WorldSize.y);
}

