using UnityEngine;

[ExecuteAlways]
[DisallowMultipleComponent]
[AddComponentMenu("MassGPUPhysics/Stage6/Scenario Gizmos")]
public sealed class Stage6ScenarioGizmos_Stage6 : MonoBehaviour
{
    [Header("Source")]
    public GPUInstancingManager_Stage6 manager;
    public Stage6ScenarioConfig_Stage6 scenarioOverride;

    [Header("Spawn Areas")]
    public bool drawSpawnAreas = true;
    public bool drawLabels = true;
    [Range(0f, 1f)] public float spawnFillAlpha = 0.12f;
    [Range(0f, 1f)] public float spawnOutlineAlpha = 0.9f;
    [Min(0f)] public float labelYOffset = 2f;
    public Color fallbackAttackerColor = new Color(0.95f, 0.22f, 0.16f, 1f);
    public Color fallbackDefenderColor = new Color(0.16f, 0.44f, 1f, 1f);

    [Header("Flow Field")]
    public bool drawFlowFieldBounds = true;
    public bool drawFlowFieldGrid;
    [Min(1)] public int flowFieldGridStride = 8;
    public bool preferPaintedFlowFieldBounds = true;
    public Color attackerFlowFieldColor = new Color(1f, 0.72f, 0.12f, 1f);
    public Color defenderFlowFieldColor = new Color(0.08f, 0.9f, 0.72f, 1f);

    private void Reset()
    {
        manager = GetComponent<GPUInstancingManager_Stage6>();
    }

    private void OnValidate()
    {
        if (manager == null)
            manager = GetComponent<GPUInstancingManager_Stage6>();

        spawnFillAlpha = Mathf.Clamp01(spawnFillAlpha);
        spawnOutlineAlpha = Mathf.Clamp01(spawnOutlineAlpha);
        labelYOffset = Mathf.Max(0f, labelYOffset);
        flowFieldGridStride = Mathf.Max(1, flowFieldGridStride);
    }

    private void OnDrawGizmos()
    {
        DrawScenarioGizmos();
    }

    private void DrawScenarioGizmos()
    {
        GPUInstancingManager_Stage6 sourceManager = manager != null ? manager : GetComponent<GPUInstancingManager_Stage6>();
        bool hasAttacker = Stage6ScenarioGizmoResolver_Stage6.TryResolveTeam(
            sourceManager,
            scenarioOverride,
            true,
            fallbackAttackerColor,
            out Stage6ScenarioGizmoTeam_Stage6 attacker);
        bool hasDefender = Stage6ScenarioGizmoResolver_Stage6.TryResolveTeam(
            sourceManager,
            scenarioOverride,
            false,
            fallbackDefenderColor,
            out Stage6ScenarioGizmoTeam_Stage6 defender);

        if (drawFlowFieldBounds)
        {
            DrawFlowField(attacker, defender, attackerFlowFieldColor);
            DrawFlowField(defender, attacker, defenderFlowFieldColor);
        }

        if (!drawSpawnAreas)
            return;

        if (hasAttacker)
            Stage6ScenarioGizmoDrawer_Stage6.DrawSpawnArea(attacker, spawnFillAlpha, spawnOutlineAlpha, drawLabels, labelYOffset);
        if (hasDefender)
            Stage6ScenarioGizmoDrawer_Stage6.DrawSpawnArea(defender, spawnFillAlpha, spawnOutlineAlpha, drawLabels, labelYOffset);
    }

    private void DrawFlowField(
        Stage6ScenarioGizmoTeam_Stage6 team,
        Stage6ScenarioGizmoTeam_Stage6 otherTeam,
        Color color)
    {
        if (!team.IsValid)
            return;

        bool resolved = false;
        Stage6ScenarioGizmoFlowField_Stage6 flowField;
        if (preferPaintedFlowFieldBounds)
            resolved = Stage6ScenarioGizmoResolver_Stage6.TryResolvePaintedFlowField(team, color, out flowField);
        else
            flowField = default;

        if (!resolved)
        {
            resolved = Stage6ScenarioGizmoResolver_Stage6.TryResolveEstimatedRuntimeFlowField(team, otherTeam, color, out flowField);
            if (!resolved && !preferPaintedFlowFieldBounds)
                resolved = Stage6ScenarioGizmoResolver_Stage6.TryResolvePaintedFlowField(team, color, out flowField);
        }

        if (resolved)
            Stage6ScenarioGizmoDrawer_Stage6.DrawFlowField(flowField, drawFlowFieldGrid, flowFieldGridStride, drawLabels, labelYOffset + 1f);
    }
}
