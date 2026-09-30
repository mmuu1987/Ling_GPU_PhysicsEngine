using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MassEngine.Editor;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MassEngine.Game.Editor
{
    /// <summary>New launch-only render assets. Historical scenes and VAT data stay intact.</summary>
    public static class WarSandboxFarLodBuilder
    {
        public const string Root = "Assets/Game/M73RenderBudget";
        public const string ProfilePath = Root + "/MaleFar128.asset";
        private const string SourcePath = "Assets/VAT_Data/MaleCharacter_Stage5_MultiClip_Profile.asset";

        public static void CreateVerifyAndBuild()
        {
            CreateAndBind();
            var appearance = VatAppearanceRegression.Execute("Logs/M73FarLod/appearance", ProfilePath);
            if (!appearance.passed || appearance.maleComparisons.Any(c => c.lod != "far" && c.changedPixels != 0))
                throw new InvalidOperationException("Far variant failed GPU appearance checks or changed near/mid pixels.");
            WarSandboxLaunchPresetsBuilder.BuildM73FarLodWindows();
        }

        [MenuItem("MassEngine/Launch Presets/Create M7.3 Far LOD")]
        public static void CreateAndBind()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play Mode before changing launch render assets.");
            if (Directory.Exists(Root) || File.Exists(Root + ".meta"))
                throw new InvalidOperationException("Refusing to overwrite render content: " + Root);
            var source = AssetDatabase.LoadAssetAtPath<VATProfile>(SourcePath);
            var catalog = AssetDatabase.LoadAssetAtPath<WarSandboxBattlefieldCatalog>(WarSandboxLaunchPresetsBuilder.CatalogPath);
            var units = catalog.templates.Select(t => t.config).Distinct()
                .Where(u => u != null && u.renderConfig != null && u.renderConfig.vatProfile == source).ToArray();
            if (source == null || units.Length == 0 || units.Any(u => !AssetDatabase.GetAssetPath(u).StartsWith(WarSandboxLaunchPresetsBuilder.Root + "/", StringComparison.Ordinal)))
                throw new InvalidOperationException("Expected launch-owned Male templates.");
            var originals = units.ToDictionary(u => u, u => u.renderConfig);
            AssetDatabase.CreateFolder("Assets/Game", "M73RenderBudget");
            try
            {
                VATProfile profile = VatLodReducer.CreateFarVariant(source, ProfilePath, 128);
                var replacements = new Dictionary<RenderConfig, RenderConfig>();
                foreach (var original in originals.Values.Distinct())
                {
                    var replacement = Object.Instantiate(original);
                    replacement.name = original.name + "_Far128";
                    replacement.vatProfile = profile;
                    replacement.nearMesh = profile.cleanMesh;
                    replacement.midMesh = profile.midLodMesh;
                    replacement.farMesh = profile.lowLodMesh;
                    AssetDatabase.CreateAsset(replacement, Root + "/" + replacement.name + ".asset");
                    replacements.Add(original, replacement);
                }
                foreach (var unit in units)
                {
                    unit.renderConfig = replacements[originals[unit]];
                    EditorUtility.SetDirty(unit);
                    AssetDatabase.SaveAssetIfDirty(unit);
                }
                Debug.Log("[M7.3 Far LOD] Bound " + units.Length + " launch units; Male vertices near/mid/far=" +
                    profile.cleanMesh.vertexCount + "/" + profile.midLodMesh.vertexCount + "/" + profile.lowLodMesh.vertexCount);
            }
            catch
            {
                foreach (var pair in originals)
                {
                    pair.Key.renderConfig = pair.Value;
                    EditorUtility.SetDirty(pair.Key);
                    AssetDatabase.SaveAssetIfDirty(pair.Key);
                }
                AssetDatabase.DeleteAsset(Root); // This invocation created this exact directory.
                throw;
            }
        }
    }
}
