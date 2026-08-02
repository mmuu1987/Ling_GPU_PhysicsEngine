using UnityEngine;
using UnityEngine.Rendering;

namespace MassEngine
{
    [CreateAssetMenu(menuName = "MassEngine/Render Config")]
    public sealed class RenderConfig : ScriptableObject
    {
        public ScriptableObject vatProfile;
        public Mesh nearMesh;
        public Mesh midMesh;
        public Mesh farMesh;
        public Material nearMaterial;
        public Material midMaterial;
        public Material farMaterial;

        [Header("LOD Shadows")]
        public ShadowCastingMode nearShadowCasting = ShadowCastingMode.On;
        public bool nearReceiveShadows = true;
        public ShadowCastingMode midShadowCasting = ShadowCastingMode.Off;
        public bool midReceiveShadows;
        public ShadowCastingMode farShadowCasting = ShadowCastingMode.Off;
        public bool farReceiveShadows;
    }
}
