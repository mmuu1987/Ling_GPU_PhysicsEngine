using System.Collections.Generic;
using UnityEngine;

namespace MassEngine.Game
{
    /// <summary>Presentation-only deduplication. Never aliases IDs, rewrites a draft or broadens battlefield policy.</summary>
    public static class WarSandboxRosterChoices
    {
        // Audited canonical identities for the four reused visual families in Version08.
        private static bool Preferred(string id) => id == "roster-male" || id == "roster-female" ||
            id == "roster-knight" || id == "roster-skeleton-warrior";

        public static bool SameAppearance(UnitTypeConfig a, UnitTypeConfig b)
        {
            if (a == b) return true;
            if (a == null || b == null || a.renderConfig == null || b.renderConfig == null) return false;
            var x = a.renderConfig; var y = b.renderConfig;
            // A weapon/accessory is baked into the near mesh. Do not merge different materials or missing bindings.
            // Audited MaleFar128 is an LOD budget variant of the same near character, not a new character.
            return x.nearMesh != null && x.nearMaterial != null && x.nearMesh == y.nearMesh && x.nearMaterial == y.nearMaterial;
        }

        public static List<WarSandboxUnitTemplateEntry> Library(WarSandboxBattlefieldCatalog catalog)
        {
            var result = new List<WarSandboxUnitTemplateEntry>();
            if (catalog == null || catalog.templates == null) return result;
            foreach (var entry in catalog.templates)
            {
                if (entry == null || entry.hiddenFromSelection || entry.config == null || string.IsNullOrWhiteSpace(entry.templateId)) continue;
                int duplicate = result.FindIndex(x => SameAppearance(x.config, entry.config));
                if (duplicate < 0) result.Add(entry);
                else if (Preferred(entry.templateId) && !Preferred(result[duplicate].templateId)) result[duplicate] = entry;
            }
            return result;
        }

        public static List<UnitTypeConfig> Local(WarSandboxBattlefieldCatalog catalog, IEnumerable<UnitTypeConfig> allowed)
        {
            var result = new List<UnitTypeConfig>();
            foreach (var candidate in allowed)
            {
                if (candidate == null || (catalog != null && !catalog.IsSelectableTemplate(candidate))) continue;
                // Keep actual melee/ranged choices. Only the atlas represents one character per appearance.
                int duplicate = result.FindIndex(x => SameAppearance(x, candidate) && WarSandboxUnitStats.IsRanged(x) == WarSandboxUnitStats.IsRanged(candidate));
                if (duplicate < 0) result.Add(candidate);
                else if (catalog != null && catalog.TryGetTemplateId(candidate, out var id, out _) && Preferred(id) &&
                    catalog.TryGetTemplateId(result[duplicate], out var previous, out _) && !Preferred(previous)) result[duplicate] = candidate;
            }
            return result;
        }
    }
}
