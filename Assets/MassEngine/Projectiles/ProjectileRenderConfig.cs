using UnityEngine;
using UnityEngine.Rendering;

namespace MassEngine.Projectiles
{
    /// <summary>
    /// Read-only projectile tracer render settings. Consumed by
    /// ProjectileGpuRenderDispatcher on the render path and never written back at
    /// runtime, so one asset can be shared by every scene that references it.
    /// A null mesh is not a misconfiguration: the dispatcher falls back to a
    /// procedural camera-facing quad, which is all the first pass needs.
    /// </summary>
    [CreateAssetMenu(menuName = "MassEngine/Projectile Render Config")]
    public sealed class ProjectileRenderConfig : ScriptableObject
    {
        [Tooltip("Master switch. Off keeps the simulation and its active-list pass intact and only skips the draw.")]
        public bool renderProjectiles = true;

        [Tooltip("Optional override mesh. Leave empty to use the built-in unit quad stretched along the flight direction.")]
        public Mesh mesh;

        [Tooltip("Required. Must be a shader that reads projectileBuffer via activeProjectileIndices (see ProjectileTrail.shader).")]
        public Material material;

        [Header("Tracer Shape")]
        [Tooltip("Tracer half-width in world metres, applied across the flight direction.")]
        public float trailWidth = 0.15f;

        [Tooltip("Multiplier on the per-projectile trailLength written at launch.")]
        public float trailLengthScale = 2f;

        [Tooltip("Floor on tracer length in metres, so slow or freshly launched shots stay visible.")]
        public float trailMinLength = 0.8f;

        [Header("Team Colors")]
        public Color attackerColor = new Color(1f, 0.82f, 0.35f, 0.9f);
        public Color defenderColor = new Color(0.45f, 0.78f, 1f, 0.9f);

        [Header("Shadows")]
        [Tooltip("Tracers are small, numerous and additive; shadows cost far more than they read.")]
        public ShadowCastingMode shadowCasting = ShadowCastingMode.Off;
        public bool receiveShadows;
    }
}
