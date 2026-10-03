using UnityEngine;
namespace MassEngine.Projectiles
{
    /// <summary>Bounded, config-derived capacity; do not size a sustained volley by agentCount/4 alone.</summary>
    public static class ProjectilePoolBudget
    {
        // 16 MiB for the 64-byte projectile records, excluding indices/staging/CPU leases.
        // Custom extreme rates still queue at this ceiling; this is not unlimited spawning.
        public const int MaxProjectiles = 262144;
        public const int MaxSlotsPerRangedAgent = 8;
        public static int Calculate(ScenarioConfig scenario, int agentCount)
        {
            if (agentCount <= 0) return 0;
            long wanted = 0;
            if (scenario != null && scenario.unitTypes != null)
                foreach (var unit in scenario.unitTypes)
                {
                    if (unit == null || unit.spawnConfig == null || unit.combatConfig == null) continue;
                    var combat = unit.combatConfig;
                    if (combat.projectileRange <= .01f || combat.projectileSpeed <= 0) continue;
                    // Max lifetime bounds lofts, terrain elevation and target scale without guessing
                    // an in-range fraction. One extra slot covers the asynchronous retirement delay.
                    float cycles = Mathf.Clamp(combat.projectileMaxLifetime / Mathf.Max(.01f, combat.attackInterval),0,MaxSlotsPerRangedAgent);
                    int perAgent = Mathf.Clamp(Mathf.CeilToInt(cycles)+1,1,MaxSlotsPerRangedAgent);
                    wanted += (long)Mathf.Max(0,unit.spawnConfig.unitCount)*perAgent;
                }
            return (int)System.Math.Min(MaxProjectiles,System.Math.Max(Mathf.Max(1,agentCount/4),wanted));
        }
    }
}
