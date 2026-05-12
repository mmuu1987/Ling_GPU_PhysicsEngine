using UnityEngine;
using UnityEngine.Serialization;

namespace MassGPUPhysics.Stage7
{
    [CreateAssetMenu(menuName = "MassGPUPhysics/Stage7/LOD Config")]
    public sealed class LodConfig : ScriptableObject
    {
        [FormerlySerializedAs("shadowCastingRadius")]
        [Min(0f)] public float nearLodRadius = 18f;
        [Min(0f)] public float midLodRadius = 75f;
        [Min(1)] public int nearAnimationInterval = 1;
        [Min(1)] public int midAnimationInterval = 2;
        [Min(1)] public int farAnimationInterval = 4;
        public bool enableFrustumCulling = true;
        [Min(0f)] public float cullingRadius = 2f;
    }
}
