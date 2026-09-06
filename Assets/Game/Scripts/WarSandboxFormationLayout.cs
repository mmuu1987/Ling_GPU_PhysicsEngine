using UnityEngine;

namespace MassEngine.Game
{
    /// <summary>
    /// Converts an army footprint into a symmetric deployment position while keeping
    /// a stable edge-to-edge engagement gap as head count and formation shape change.
    /// Only the front line (teams 0 and 1) has such a position: they face each other along X,
    /// so each is placed at half its own depth plus half the gap. Any further army is returned
    /// unchanged - see ScenarioAutoFit for why extra armies are placed by hand.
    ///
    /// A team fielding several unit types deploys them as ranks: the block closest to the enemy
    /// takes the gap, every later one falls in behind the depth already spent. That is what lets
    /// swordsmen screen the archers standing behind them instead of both piling onto the gap.
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
            return ResolveRankedSpawnCenter(spawn, teamId, engagementGap, 0f);
        }

        /// <summary>
        /// The centered position for one rank of a front-line team. depthAlreadyDeployed is the
        /// summed X depth of that team's blocks standing between this one and the enemy, so 0
        /// means "front rank" and reproduces ResolveCenteredSpawnCenter exactly.
        /// </summary>
        public static Vector3 ResolveRankedSpawnCenter(SpawnConfig spawn, int teamId, float engagementGap, float depthAlreadyDeployed)
        {
            if (spawn == null || (teamId != 0 && teamId != 1))
                return spawn != null ? spawn.spawnCenter : Vector3.zero;

            Vector3 center = spawn.spawnCenter;
            float side = teamId == 0 ? -1f : 1f;
            float halfDepth = spawn.ResolveSpawnSize().x * 0.5f;
            center.x = side * (Mathf.Max(0f, engagementGap) * 0.5f + Mathf.Max(0f, depthAlreadyDeployed) + halfDepth);
            return center;
        }
    }
}
