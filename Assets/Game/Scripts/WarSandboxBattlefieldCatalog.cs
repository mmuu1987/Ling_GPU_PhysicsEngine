using System;
using System.Collections.Generic;
using UnityEngine;

namespace MassEngine.Game
{
    [Serializable]
    public sealed class WarSandboxUnitTemplateEntry
    {
        public string templateId;
        public int revision = 1;
        public UnitTypeConfig config;

        public bool TryValidate(out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(templateId) || templateId != templateId.Trim())
                error = "Unit template id must be non-empty and have no surrounding whitespace.";
            else if (revision <= 0) error = "Unit template revision must be positive: " + templateId;
            else if (config == null) error = "Unit template config is missing: " + templateId;
            else
            {
                var validation = ConfigValidator.Validate(config);
                if (!validation.IsValid) error = templateId + ": " + string.Join("\n", validation.Errors);
            }
            return error == null;
        }
    }

    [Serializable]
    public sealed class WarSandboxBattlefieldEntry
    {
        public string id;
        public string displayName;
        public string scenePath;
        public int contentVersion = 1;
        public string terrainId = "flat-ground";
        public int terrainVersion = 1;
        public TerrainSurfaceAsset terrainSurface;
        public WarSandboxBattlefieldConfig rules;
        public Texture2D preview;
        [TextArea] public string description;
        [TextArea] public string briefing;

        public bool TryValidate(Func<string, bool> canLoadScene, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(id) || id != id.Trim()) error = "Battlefield id must be non-empty and have no surrounding whitespace.";
            else if (string.IsNullOrWhiteSpace(displayName)) error = "Battlefield displayName is missing: " + id;
            else if (!IsScenePath(scenePath)) error = "Battlefield scenePath must be an Assets/... .unity path: " + id;
            else if (contentVersion <= 0) error = "Battlefield contentVersion must be positive: " + id;
            else if (string.IsNullOrWhiteSpace(terrainId) || terrainId != terrainId.Trim()) error = "Battlefield terrainId is missing: " + id;
            else if (terrainVersion <= 0) error = "Battlefield terrainVersion must be positive: " + id;
            else if (!TryValidateTerrain(out error)) return false;
            else if (rules == null) error = "Battlefield rules are missing: " + id;
            else if (!rules.TryCreateSnapshot(out _, out string ruleError)) error = id + ": " + ruleError;
            else if (canLoadScene != null && !canLoadScene(scenePath)) error = "Battlefield scene is not included in the build: " + scenePath;
            return error == null;
        }

        public bool TryValidateTerrain(out string error)
        {
            error = null;
            if (terrainId == "flat-ground")
            {
                if (terrainVersion != 1 || terrainSurface != null)
                    error = "flat-ground@1 requires an explicit null terrainSurface provider.";
            }
            else if (terrainSurface == null) error = "Terrain provider is missing: " + terrainId;
            else if (terrainSurface.Id != terrainId || terrainSurface.Version != terrainVersion)
                error = "Terrain provider identity/version does not match the battlefield: " + terrainId;
            else if (!terrainSurface.TryCreateSurface(out _, out error)) return false;
            return error == null;
        }

        // Value copy freezes a load request; the provider must remain the exact authored asset.
        public WarSandboxBattlefieldEntry CopyIdentity() => new WarSandboxBattlefieldEntry
        {
            id = id, displayName = displayName, scenePath = scenePath, contentVersion = contentVersion,
            terrainId = terrainId, terrainVersion = terrainVersion, terrainSurface = terrainSurface,
            rules = rules, preview = preview, description = description, briefing = briefing
        };

        public static bool IsScenePath(string path)
        {
            return !string.IsNullOrWhiteSpace(path) && path.StartsWith("Assets/", StringComparison.Ordinal) &&
                path.EndsWith(".unity", StringComparison.Ordinal) && !path.Contains("..") &&
                !path.Contains("\\") && !path.Contains("//");
        }
    }

    [CreateAssetMenu(menuName = "MassEngine/War Sandbox Battlefield Catalog")]
    public sealed class WarSandboxBattlefieldCatalog : ScriptableObject
    {
        public string defaultEntryId;
        public WarSandboxBattlefieldEntry[] entries = new WarSandboxBattlefieldEntry[0];
        public WarSandboxUnitTemplateEntry[] templates = new WarSandboxUnitTemplateEntry[0];

        public bool TryValidate(Func<string, bool> canLoadScene, out string error)
        {
            error = null;
            if (entries == null || entries.Length == 0) { error = "The battlefield catalog is empty."; return false; }
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i] == null) { error = "Battlefield entry " + i + " is missing."; return false; }
                if (!entries[i].TryValidate(canLoadScene, out error)) return false;
                if (!ids.Add(entries[i].id)) { error = "Duplicate battlefield id: " + entries[i].id; return false; }
            }
            if (defaultEntryId == null || !ids.Contains(defaultEntryId))
            { error = "defaultEntryId does not name a battlefield in this catalog."; return false; }
            return true;
        }

        public bool TryValidateTemplates(out string error)
        {
            error = null;
            if (templates == null) { error = "The unit template catalog is missing."; return false; }
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var configs = new HashSet<UnitTypeConfig>();
            for (int i = 0; i < templates.Length; i++)
            {
                if (templates[i] == null) { error = "Unit template entry " + i + " is missing."; return false; }
                if (!templates[i].TryValidate(out error)) return false;
                if (!ids.Add(templates[i].templateId)) { error = "Duplicate unit template id: " + templates[i].templateId; return false; }
                if (!configs.Add(templates[i].config)) { error = "A unit template has more than one stable id: " + templates[i].templateId; return false; }
            }
            return true;
        }

        public bool TryResolveTemplate(string id, int revision, out UnitTypeConfig config, out string error)
        {
            config = null;
            if (!TryValidateTemplates(out error)) return false;
            foreach (var entry in templates)
                if (entry.templateId == id)
                {
                    if (entry.revision != revision) { error = "Unit template revision is not supported: " + id; return false; }
                    config = entry.config; return true;
                }
            error = "Unknown unit template id: " + id;
            return false;
        }

        public bool TryGetTemplateId(UnitTypeConfig config, out string id, out int revision)
        {
            id = null; revision = 0;
            if (templates == null) return false;
            foreach (var entry in templates)
                if (entry != null && entry.config == config) { id = entry.templateId; revision = entry.revision; return true; }
            return false;
        }

        public bool TryResolve(string id, Func<string, bool> canLoadScene,
            out WarSandboxBattlefieldEntry entry, out string error)
        {
            entry = null;
            if (!TryValidate(canLoadScene, out error)) return false;
            foreach (WarSandboxBattlefieldEntry candidate in entries)
                if (string.Equals(candidate.id, id, StringComparison.Ordinal)) { entry = candidate; return true; }
            error = "Unknown battlefield id: " + id;
            return false;
        }
    }
}
