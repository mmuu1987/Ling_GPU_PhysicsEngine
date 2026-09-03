using UnityEngine;

namespace MassEngine
{
    [CreateAssetMenu(menuName = "MassEngine/Shader Config")]
    public sealed class MassEngineShaderConfig : ScriptableObject
    {
        public ComputeShader spatialHashShader;
        public ComputeShader runtimeFlowShader;
        public ComputeShader combatSimulationShader;
        public ComputeShader lodClassificationShader;
        public ComputeShader projectileShader;
    }
}
