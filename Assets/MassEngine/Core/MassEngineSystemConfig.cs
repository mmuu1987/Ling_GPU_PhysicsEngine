using UnityEngine;

namespace MassEngine
{
    [CreateAssetMenu(menuName = "MassEngine/System Config")]
    public sealed class MassEngineSystemConfig : ScriptableObject
    {
        public SimulationConfig simulationConfig;
        public LodConfig lodConfig;
        public RuntimeFlowConfig runtimeFlowConfig;
        public RuntimeCombatConfig runtimeCombatConfig;
    }
}
