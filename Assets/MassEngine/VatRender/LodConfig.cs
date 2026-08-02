using UnityEngine;
using UnityEngine.Serialization;

namespace MassEngine
{
    [CreateAssetMenu(menuName = "MassEngine/LOD Config")]
    public sealed class LodConfig : ScriptableObject
    {
        [FormerlySerializedAs("shadowCastingRadius")]
        [Min(0f)] public float nearLodRadius = 18f;
        [Min(0f)] public float midLodRadius = 75f;
        [Min(1)] public int nearAnimationInterval = 1;
        [Min(1)] public int midAnimationInterval = 2;
        [Min(1)] public int farAnimationInterval = 4;

        [Header("Simulation Cadence")]
        [Tooltip("Decision-pass frequency per LOD tier: agents run their expensive simulation step every N-th frame. Rate-dependent quantities are dt-compensated, so DPS and movement speed match full rate. Powers of two recommended.")]
        [Min(1)] public int nearSimulationInterval = 1;
        [Min(1)] public int midSimulationInterval = 2;
        [Min(1)] public int farSimulationInterval = 4;
        public bool enableFrustumCulling = true;
        [Min(0f)] public float cullingRadius = 2f;
        [Tooltip("Agents farther than this from lodCenter are not submitted for rendering (0 = unlimited). On km-scale battlefields units beyond ~900m are sub-pixel; this caps the worst-case visible instance count when the camera sees the whole field.")]
        [Min(0f)] public float maxRenderDistance = 0f;
        [Tooltip("Render corpses in the far LOD tier too. Off = bodies vanish beyond midLodRadius, forming a visible pop line as the camera moves.")]
        public bool farIncludeDead = true;
    }
}
