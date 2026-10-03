using System;
using UnityEngine;

namespace MassEngine.Game
{
    /// <summary>Candidate-rule navigation is CPU-only and cached, never installed on the live manager.</summary>
    public sealed class WarSandboxTerrainValidation
    {
        private TerrainNavigationGrid cachedNavigation;
        private TerrainNavigationGrid contextNavigation;
        private int cachedHash;
        private WarSandboxBattlefieldRules cachedRules;

        public bool TryValidate(MassEngineManager manager, WarSandboxDeploymentDraft candidate,
            out TerrainSurface surface, out string error)
        {
            surface = null; error = null;
            if (manager == null || candidate == null) { error = "地形部署上下文缺失。"; return false; }
            if (!manager.TryGetTerrainContext(out surface, out var navigation, out error)) return false;
            if (surface == null) return true; // Explicit legacy plane; never a failed terrain fallback.
            var rules = candidate.Rules;
            int hash = RulesHash(rules);
            if (cachedNavigation == null || contextNavigation != navigation || cachedHash != hash || !SameRules(cachedRules, rules))
            {
                try
                {
                    cachedNavigation = manager.CreateTerrainNavigation(
                        rules.staticObstaclesEnabled ? rules.staticObstacles : null, rules.staticObstacleClearance);
                }
                catch (ArgumentException ex) { error = "候选地形导航无效：" + ex.Message; return false; }
                catch (InvalidOperationException ex) { error = "候选地形导航无效：" + ex.Message; return false; }
                contextNavigation = navigation; cachedHash = hash; cachedRules = rules.Copy();
            }
            if (cachedNavigation == null) { error = "地形导航缺失，不能回退到平地。"; return false; }
            return ValidateFootprints(candidate, surface, cachedNavigation, out error);
        }

        public static bool ValidateFootprints(WarSandboxDeploymentDraft candidate, TerrainSurface surface,
            TerrainNavigationGrid navigation, out string error)
        {
            error = null;
            if (surface == null || navigation == null || navigation.Surface != surface)
            { error = "地表与导航快照不匹配。"; return false; }
            for (int i = 0; i < candidate.Count; i++)
            {
                var entry = candidate[i];
                var center = new Vector2(entry.center.x, entry.center.z);
                var size = entry.Size;
                if (!surface.TrySample(center, out _) ||
                    !navigation.IsFootprintWalkable(center, new Vector2(size.x, size.z)))
                { error = "编成 " + (i + 1) + "：完整部署脚印覆盖禁行地形、陡坡、障碍或边界留白。"; return false; }
            }
            return true;
        }

        private static int RulesHash(WarSandboxBattlefieldRules rules)
        {
            unchecked
            {
                int hash = rules.staticObstaclesEnabled ? 17 : 31;
                hash = hash * 397 ^ rules.staticObstacleClearance.GetHashCode();
                if (rules.staticObstaclesEnabled && rules.staticObstacles != null)
                    foreach (var obstacle in rules.staticObstacles)
                    { hash = hash * 397 ^ obstacle.center.GetHashCode(); hash = hash * 397 ^ obstacle.size.GetHashCode(); }
                return hash;
            }
        }

        private static bool SameRules(WarSandboxBattlefieldRules a, WarSandboxBattlefieldRules b)
        {
            if (a.staticObstaclesEnabled != b.staticObstaclesEnabled || a.staticObstacleClearance != b.staticObstacleClearance) return false;
            if (!a.staticObstaclesEnabled) return true;
            int count = a.staticObstacles?.Length ?? 0;
            if (count != (b.staticObstacles?.Length ?? 0)) return false;
            for (int i = 0; i < count; i++)
                if (!a.staticObstacles[i].Equals(b.staticObstacles[i])) return false;
            return true;
        }
    }
}
