using UnityEngine;

namespace MassEngine.Game
{
    /// <summary>
    /// Converts an army footprint into a symmetric deployment position while keeping
    /// a stable edge-to-edge engagement gap as head count and formation shape change.
    /// Only the front line (teams 0 and 1) has such a position: they face each other along X,
    /// so each is placed at half its own depth plus half the gap. Any further army is returned
    /// unchanged - see ScenarioAutoFit for why extra armies are placed by hand.
    /// </summary>
    public static class WarSandboxFormationLayout
    {
        public const float DefaultEngagementGap = 50f;

        /// <summary>
        /// The centered position for a front-line team, or the spawn's current center for any
        /// other team (an extra army's placement is authored, not derived).
        /// </summary>
        public static Vector3 ResolveCenteredSpawnCenter(SpawnConfig spawn, int teamId, float engagementGap)
        {
            if (spawn == null || (teamId != 0 && teamId != 1))
                return spawn != null ? spawn.spawnCenter : Vector3.zero;

            Vector3 center = spawn.spawnCenter;
            float side = teamId == 0 ? -1f : 1f;
            float halfDepth = spawn.ResolveSpawnSize().x * 0.5f;
            center.x = side * (halfDepth + Mathf.Max(0f, engagementGap) * 0.5f);
            return center;
        }
    }
}
