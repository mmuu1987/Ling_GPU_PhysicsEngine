using UnityEngine;

[CreateAssetMenu(fileName = "Stage6TeamConfig", menuName = "MassGPUPhysics/Stage6/Config/Team Config")]
public sealed class Stage6TeamConfig_Stage6 : ScriptableObject
{
    [Header("Identity")]
    public string teamName = "Team";
    [Min(0)] public int teamId;
    public Color teamColor = Color.white;
    [Min(0)] public int defaultEnemyTeamId = 1;

    [Header("Behavior")]
    public Stage6TeamBehaviorMode behaviorMode = Stage6TeamBehaviorMode.Attack;
    [Min(0f)] public float guardRadius = 1.5f;
    [Min(0.1f)] public float maxChaseDistance = 24f;

    [Header("Config References")]
    public Stage6UnitConfig_Stage6 unitConfig;
    public Stage6SpawnConfig_Stage6 spawnConfig;
    public Stage6RenderConfig_Stage6 renderConfig;
    public Stage6FlowFieldConfig_Stage6 flowFieldConfig;

    public int UnitCount => spawnConfig != null ? Mathf.Max(0, spawnConfig.unitCount) : 0;

    public Stage6RenderConfig_Stage6 ResolvedRenderConfig =>
        renderConfig != null ? renderConfig : unitConfig != null ? unitConfig.renderConfig : null;

    public void ApplyTo(ref GPUInstancingManager_Stage6.TeamCombatSettings settings)
    {
        if (spawnConfig != null)
            spawnConfig.ApplyTo(ref settings);

        if (unitConfig != null)
            unitConfig.ApplyTo(ref settings);

        settings.Normalize();
    }

    private void OnValidate()
    {
        teamId = Mathf.Max(0, teamId);
        defaultEnemyTeamId = Mathf.Max(0, defaultEnemyTeamId);
        guardRadius = Mathf.Max(0f, guardRadius);
        maxChaseDistance = Mathf.Max(0.1f, maxChaseDistance);
    }
}
