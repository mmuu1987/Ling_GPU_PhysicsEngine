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

        /// <summary>
        /// Palette length uploaded to the tracer shader. Matches ConfigValidator.MaxTeamId + 1,
        /// so every team a scenario may legally field has a slot.
        /// </summary>
        public const int MaxTeamColors = 8;

        [Header("Team Colors")]
        [Tooltip("Tracer color per teamId: entry i colors team i's shots, so a third army stops " +
                 "borrowing the defender's. Channels above 1 are intentional - tracers are thin " +
                 "alpha-blended lines, and brightness is what makes them read against lit terrain " +
                 "and fog. A team past the end of the list reuses the last entry.")]
        [ColorUsage(true, true)]
        public Color[] teamColors =
        {
            new Color(1f, 0.82f, 0.35f, 0.9f),
            new Color(0.4f, 1f, 1.85f, 1f),
            new Color(1.4f, 0.5f, 1.7f, 1f)
        };

        /// <summary>
        /// The tracer color for a team: its own entry, the last authored entry when the palette is
        /// shorter than the roster, or white when it is empty - a missing palette must not black
        /// out every tracer, because an invisible projectile reads as a broken simulation.
        /// </summary>
        public Color ResolveTeamColor(int teamId)
        {
            if (teamColors == null || teamColors.Length == 0)
                return Color.white;

            return teamColors[Mathf.Clamp(teamId, 0, teamColors.Length - 1)];
        }

        [Header("Shadows")]
        [Tooltip("Tracers are small, numerous and additive; shadows cost far more than they read.")]
        public ShadowCastingMode shadowCasting = ShadowCastingMode.Off;
        public bool receiveShadows;
    }
}
