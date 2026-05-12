using UnityEngine;

namespace MassGPUPhysics.Stage7
{
    [CreateAssetMenu(menuName = "MassGPUPhysics/Stage7/System Config")]
    public sealed class Stage7SystemConfig : ScriptableObject
    {
        public SimulationConfig simulationConfig;
        public LodConfig lodConfig;
        public RuntimeFlowConfig runtimeFlowConfig;
        public RuntimeCombatConfig runtimeCombatConfig;
    }
}
