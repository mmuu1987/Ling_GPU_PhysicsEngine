using UnityEngine;

[CreateAssetMenu(fileName = "Stage6SpawnConfig", menuName = "MassGPUPhysics/Stage6/Config/Spawn Config")]
public sealed class Stage6SpawnConfig_Stage6 : ScriptableObject
{
    [Header("Count")]
    [Min(0)] public int unitCount = 50000;

    [Header("Area")]
    public Stage6SpawnShape shape = Stage6SpawnShape.Rectangle;
    public Vector3 center = Vector3.zero;
    public Vector3 size = new Vector3(35f, 0f, 80f);
    [Min(0.01f)] public float radius = 40f;

    [Header("Formation")]
    public Stage6FormationType formation = Stage6FormationType.Grid;
    [Range(0f, 1f)] public float scatter = 0f;

    public Vector3 EffectiveRectSize
    {
        get
        {
            if (shape != Stage6SpawnShape.Circle)
                return new Vector3(Mathf.Max(0.01f, size.x), Mathf.Max(0f, size.y), Mathf.Max(0.01f, size.z));

            float diameter = Mathf.Max(0.01f, radius) * 2f;
            return new Vector3(diameter, Mathf.Max(0f, size.y), diameter);
        }
    }

    public void ApplyTo(ref GPUInstancingManager_Stage6.TeamCombatSettings settings)
    {
        settings.spawnCenter = center;
        settings.spawnSize = EffectiveRectSize;
        settings.Normalize();
    }

    private void OnValidate()
    {
        unitCount = Mathf.Max(0, unitCount);
        size.x = Mathf.Max(0.01f, size.x);
        size.y = Mathf.Max(0f, size.y);
        size.z = Mathf.Max(0.01f, size.z);
        radius = Mathf.Max(0.01f, radius);
        scatter = Mathf.Clamp01(scatter);
    }
}
