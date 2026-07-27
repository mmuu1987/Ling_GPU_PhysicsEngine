using UnityEngine;

namespace MassEngine
{
    [CreateAssetMenu(menuName = "MassEngine/Simulation Config")]
    public sealed class SimulationConfig : ScriptableObject
    {
        public Vector2 simulationWorldSize = new Vector2(160f, 160f);
        [Min(0f)] public float boundaryPadding = 2f;
        [Min(0.1f)] public float cellSize = 2f;
        [Min(1)] public int maxAgentsPerCell = 64;
    }
}
