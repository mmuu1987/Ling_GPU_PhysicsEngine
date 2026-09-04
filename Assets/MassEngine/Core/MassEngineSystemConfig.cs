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

        /// <summary>Optional: leave empty and projectiles simulate without any tracer visuals.</summary>
        public MassEngine.Projectiles.ProjectileRenderConfig projectileRenderConfig;
    }
}
