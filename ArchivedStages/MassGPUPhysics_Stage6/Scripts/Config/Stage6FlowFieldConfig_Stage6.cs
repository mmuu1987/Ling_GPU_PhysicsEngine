using UnityEngine;

[CreateAssetMenu(fileName = "Stage6FlowFieldConfig", menuName = "MassGPUPhysics/Stage6/Config/Flow Field Config")]
public sealed class Stage6FlowFieldConfig_Stage6 : ScriptableObject
{
    [Header("Navigation")]
    public bool enableNavigation = true;
    public PaintedFlowFieldAsset_Stage6 paintedFlowFieldAsset;
    [Min(0.25f)] public float cellSize = 2f;
    [Range(0f, 1f)] public float weight = 1f;
    [Min(0f)] public float responsiveness = 6f;

    [Header("Runtime Dynamic Flow")]
    public bool useRuntimeDynamicFlow = true;
    public bool autoSizeRuntimeFlowField = true;
    [Min(0f)] public float runtimePadding = 40f;
    [Min(16)] public int runtimeMaxResolution = 256;
    [Min(0.1f)] public float updateInterval = 0.5f;
    [Range(1, 8)] public int sectorCount = 5;
    [Min(0f)] public float targetStopRadius = 2f;
    [Min(1)] public int minEnemiesPerTarget = 8;

    public bool RequestsNavigation => enableNavigation && (paintedFlowFieldAsset != null || useRuntimeDynamicFlow);

    private void OnValidate()
    {
        cellSize = Mathf.Max(0.25f, cellSize);
        weight = Mathf.Clamp01(weight);
        responsiveness = Mathf.Max(0f, responsiveness);
        runtimePadding = Mathf.Max(0f, runtimePadding);
        runtimeMaxResolution = Mathf.Max(16, runtimeMaxResolution);
        updateInterval = Mathf.Max(0.1f, updateInterval);
        sectorCount = Mathf.Clamp(sectorCount, 1, 8);
        targetStopRadius = Mathf.Max(0f, targetStopRadius);
        minEnemiesPerTarget = Mathf.Max(1, minEnemiesPerTarget);
    }
}
