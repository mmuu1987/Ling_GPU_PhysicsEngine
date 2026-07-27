using UnityEngine;

namespace MassEngine
{
    /// <summary>
    /// Spawn intent for one unit type. The designer authors WHERE (spawnCenter),
    /// HOW MANY (unitCount) and the formation feel (density + aspect); the actual
    /// footprint is derived so it is always physically consistent with the head count.
    /// spawnSize acts as a manual override for intentional shapes (choke fills,
    /// wedge columns); leave it at zero for auto-derivation.
    /// </summary>
    [CreateAssetMenu(menuName = "MassEngine/Spawn Config")]
    public sealed class SpawnConfig : ScriptableObject
    {
        /// <summary>Physical packing limit for typical agent radii (~0.45m): beyond this they overlap.</summary>
        public const float PackingLimitPerSquareMeter = 1.5f;

        [Min(1)] public int unitCount = 50000;
        public Vector3 spawnCenter = Vector3.zero;

        [Header("Formation (auto footprint)")]
        [Tooltip("Agents per square meter. 0.5 ≈ marching density; ~1.5 is the physical packing limit for 0.45m-radius agents.")]
        [Range(0.05f, PackingLimitPerSquareMeter)] public float formationDensity = 0.5f;
        [Tooltip("Front width (Z) : depth (X) ratio. 2 = wide battle line, 0.5 = deep column. Armies face each other along X in the default scenario.")]
        [Range(0.1f, 10f)] public float formationAspect = 2f;

        [Header("Manual Override")]
        [Tooltip("If BOTH x and z are > 0 this exact footprint is used and density is ignored. Leave zero for auto.")]
        public Vector3 spawnSize = Vector3.zero;

        /// <summary>True when the manual spawnSize override is active.</summary>
        public bool HasManualFootprint
        {
            get { return spawnSize.x > 0f && spawnSize.z > 0f; }
        }

        /// <summary>
        /// The effective spawn footprint: manual override when set, otherwise derived
        /// from unitCount / formationDensity with the configured front:depth ratio
        /// (front along Z, depth along X).
        /// </summary>
        public Vector3 ResolveSpawnSize()
        {
            if (HasManualFootprint)
                return spawnSize;

            float density = Mathf.Clamp(formationDensity, 0.05f, PackingLimitPerSquareMeter);
            float aspect = Mathf.Clamp(formationAspect, 0.1f, 10f);
            float area = Mathf.Max(1, unitCount) / density;
            float depth = Mathf.Sqrt(area / aspect);   // X
            float front = aspect * depth;              // Z
            return new Vector3(depth, 0f, front);
        }

        /// <summary>Effective agents per square meter of the resolved footprint.</summary>
        public float ResolveDensity()
        {
            Vector3 size = ResolveSpawnSize();
            float area = Mathf.Max(1f, size.x * size.z);
            return Mathf.Max(0, unitCount) / area;
        }
    }
}
