using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MassEngine.Editor;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MassEngine.Game.Editor
{
    /// <summary>
    /// 2026-10-17: fills the unit picker portrait (template.unitPreview) of every selectable template that has none
    /// (the four dragons, CC0 gunner / artillery showed 「真实预览待配置」). Same method as OfficialRosterQualityBuilder:
    /// the real in-game VAT model, full LOD, idle at 15 %, transparent background, uniform scale, never cropped.
    /// The PNG files stay local (derived from the model packs, git-ignored like the rest of the generated content);
    /// a clean clone reruns this menu (FourDragonsBuilder / CC0ArmsBuilder call it after a build).
    /// Also writes LOD 1 / LOD 2 check images to Logs/UnitPortraits for review.
    /// </summary>
    public static class UnitPortraitBackfill
    {
        public const string Folder = "Assets/Game/Content/Characters/OfficialRoster/Version08/UnitPreviews";
        static readonly string[] Catalogs = { "Assets/Game/Scenes/Catalog.asset", "Assets/Game/Content/Characters/OfficialRoster/Version08/Catalog.asset" };
        const string Logs = "Logs/UnitPortraits";

        [MenuItem("Tools/兵种/补齐缺失的兵种预览图")]
        public static void FillMissingMenu() => Debug.Log("[UnitPortraits] " + Run(null, false));

        [MenuItem("Tools/兵种/重拍四龙与机枪手炮车的预览图")]
        public static void RecaptureMenu() => Debug.Log("[UnitPortraits] " + Run(new[] { "roster-dragon-usurper", "roster-dragon-terrorbringer",
            "roster-dragon-souleater", "roster-dragon-nightmare", "roster-cc0-gunner", "roster-cc0-artillery" }, true));

        /// <summary>ids == null: every selectable template without a portrait. force: recapture even if the PNG exists.</summary>
        public static string Run(string[] ids, bool force)
        {
            var log = new StringBuilder();
            Directory.CreateDirectory(Folder); Directory.CreateDirectory(Logs);
            var made = new Dictionary<string, Texture2D>();
            foreach (string catalogPath in Catalogs)
            {
                var catalog = AssetDatabase.LoadAssetAtPath<WarSandboxBattlefieldCatalog>(catalogPath);
                if (catalog == null || catalog.templates == null) { log.AppendLine("skip (missing) " + catalogPath); continue; }
                bool dirty = false;
                foreach (var t in catalog.templates)
                {
                    if (t == null || t.config == null || t.hiddenFromSelection) continue;
                    bool wanted = ids == null ? t.unitPreview == null : Array.IndexOf(ids, t.templateId) >= 0;
                    if (!wanted) continue;
                    if (!made.TryGetValue(t.templateId, out var texture))
                    {
                        string path = Folder + "/" + t.templateId + ".png";
                        try
                        {
                            if (force || !File.Exists(path)) Capture(t, path);
                            texture = Import(path);
                            log.AppendLine("portrait " + t.templateId + " -> " + path);
                        }
                        catch (Exception e) { log.AppendLine("FAILED " + t.templateId + ": " + e.Message); Debug.LogException(e); }
                        made[t.templateId] = texture;
                    }
                    if (texture != null && t.unitPreview != texture) { t.unitPreview = texture; dirty = true; }
                }
                if (dirty) { EditorUtility.SetDirty(catalog); log.AppendLine("catalog updated " + catalogPath); }
            }
            AssetDatabase.SaveAssets();
            if (made.Count == 0) log.AppendLine("nothing missing");
            File.WriteAllText(Logs + "/run.txt", log.ToString(), new UTF8Encoding(false));
            return log.ToString();
        }

        static void Capture(WarSandboxUnitTemplateEntry template, string path)
        {
            var runtime = ResolvedUnitTypeRuntime.Resolve(template.config, 1f);
            if (runtime.nearMesh == null) throw new InvalidOperationException("no game mesh (build the unit first)");
            Bounds bounds = runtime.nearMesh.bounds;
            if (runtime.midMesh != null) bounds.Encapsulate(runtime.midMesh.bounds);
            if (runtime.farMesh != null) bounds.Encapsulate(runtime.farMesh.bounds);
            // VAT motion can leave the bind-pose bounds (wings); widen until nothing touches the frame edge.
            for (int attempt = 0; attempt < 4; attempt++)
            {
                using (var capture = new VatAppearanceRegressionCapture(bounds, 768))
                {
                    var pixels = capture.Capture(runtime, 0, AgentState.Idle, runtime.idleClipDuration * .15f);
                    if (!VatAppearanceRegressionImageChecks.IsVisibleAndNotPink(VatAppearanceRegressionImageChecks.Measure(pixels), out string error))
                        throw new InvalidOperationException(error);
                    if (!TryWritePortrait(path, capture.Size, pixels)) { bounds.Expand(bounds.size.magnitude * .5f); continue; }
                    SaveSquare(Logs + "/" + template.templateId + "-lod0.png", capture.Size, pixels);
                    for (int lod = 1; lod <= 2; lod++)
                        SaveSquare(Logs + "/" + template.templateId + "-lod" + lod + ".png", capture.Size,
                            capture.Capture(runtime, lod, AgentState.Idle, runtime.idleClipDuration * .15f));
                    return;
                }
            }
            throw new InvalidOperationException("model still touches the frame edge after widening");
        }

        /// <summary>OfficialRosterQualityBuilder.WritePortrait: crop to the alpha box, uniform scale into 448x560, transparent margin.</summary>
        static bool TryWritePortrait(string path, int size, Color32[] pixels)
        {
            int minX = size, minY = size, maxX = -1, maxY = -1;
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++) if (pixels[y * size + x].a >= 128)
            { minX = Math.Min(minX, x); minY = Math.Min(minY, y); maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y); }
            if (maxX < 0 || minX < 3 || minY < 3 || maxX >= size - 3 || maxY >= size - 3) return false;
            int width = maxX - minX + 1, height = maxY - minY + 1;
            const int w = 448, h = 560, pad = 28;
            float scale = Mathf.Min((w - pad * 2f) / width, (h - pad * 2f) / height);
            int targetW = Mathf.Max(1, Mathf.RoundToInt(width * scale)), targetH = Mathf.Max(1, Mathf.RoundToInt(height * scale));
            int left = (w - targetW) / 2, bottom = (h - targetH) / 2;
            var canvas = new Color32[w * h];
            for (int y = 0; y < targetH; y++) for (int x = 0; x < targetW; x++)
            {
                int sx = minX + Mathf.Clamp(Mathf.FloorToInt((x + .5f) / scale), 0, width - 1);
                int sy = minY + Mathf.Clamp(Mathf.FloorToInt((y + .5f) / scale), 0, height - 1);
                canvas[(bottom + y) * w + left + x] = pixels[sy * size + sx];
            }
            var texture = new Texture2D(w, h, TextureFormat.RGBA32, false);
            try { texture.SetPixels32(canvas); texture.Apply(false, false); File.WriteAllBytes(path, texture.EncodeToPNG()); }
            finally { Object.DestroyImmediate(texture); }
            return true;
        }

        static void SaveSquare(string path, int size, Color32[] pixels)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false);
            try { t.SetPixels32(pixels); t.Apply(false, false); File.WriteAllBytes(path, t.EncodeToPNG()); } finally { Object.DestroyImmediate(t); }
        }

        static Texture2D Import(string path)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.mipmapEnabled = false; importer.maxTextureSize = 1024; importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear; importer.sRGBTexture = true; importer.npotScale = TextureImporterNPOTScale.None;
            importer.textureCompression = TextureImporterCompression.Uncompressed; importer.alphaIsTransparency = true; importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}
