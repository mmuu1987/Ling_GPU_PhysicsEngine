using UnityEngine;

namespace MassGPUPhysics.Stage7
{
    [CreateAssetMenu(menuName = "MassGPUPhysics/Stage7/Shader Config")]
    public sealed class Stage7ShaderConfig : ScriptableObject
    {
        public ComputeShader spatialHashShader;
        public ComputeShader runtimeFlowShader;
        public ComputeShader combatSimulationShader;
        public ComputeShader lodClassificationShader;
    }
}
