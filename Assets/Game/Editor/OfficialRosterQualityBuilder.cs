using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MassEngine.Editor;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MassEngine.Game.Editor
{
    /// <summary>Curates Version06 without rebaking or editing any previous content. All identities stay resolvable.</summary>
    public static class OfficialRosterQualityBuilder
    {
        public const string Root = "Assets/Game/OfficialRoster/Version07";
        public const string CatalogPath = Root + "/Catalog.asset";
        public const string MenuScene = Root + "/LaunchMenu.unity";
        public const string Output = "Builds/OfficialRoster-20261002-02";
        public const string Previous = "Assets/Game/OfficialRoster/Version06";
        public const string SourceEvidence = "Logs/AgentContentQuality/source-03/source-review.json";
        public static readonly string[] WithdrawnTemplates = { "roster-platformer-enemy", "roster-platformer-skull" };
        public static readonly string[] WithdrawnBattlefields = { "troops6-enemy", "troops6-skull" };
        private const string Reason = "原始FBX本身为头部主导造型，并非丢失身体；当前地面兵团角色缺少可读的完整体态与步行/出手语义。退出新正式选择，资源与旧方案身份保留。";
        [Serializable] public sealed class QualityReport
        {
            public bool technicalPassed;
            public bool rebakedCharacters;
            public string sourceReview;
            public string humanAcceptance = "Paused by user; no new manual/balance/V1 acceptance.";
            public int identityBattlefields, selectableBattlefields, identityTemplates, selectableTemplates, uniqueRenderedModels;
            public string[] withdrawnTemplates, withdrawnBattlefields;
            public PortraitEvidence[] portraits;
        }
        [Serializable] public sealed class PortraitEvidence
        {
            public string templateId, config, profile, material, portrait;
            public int foregroundPixels;
            public int[] fullLodMotionChangedPixels;
            public string[] images;
            public string artAcceptance = "Technical image checks only; this is not automatic artistic approval.";
        }
        public static void Prepare07()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode first.");
            Require(!Directory.Exists(Root) && !File.Exists(Root), "Fresh version only: " + Root);
            Require(File.Exists(SourceEvidence), "Complete the original-geometry source review first.");
            var sourceReview = JsonUtility.FromJson<PlatformerSourceReview.SourceReport>(File.ReadAllText(SourceEvidence));
            Require(sourceReview != null && sourceReview.technicalCapturePassed && sourceReview.units.Length == 3 && sourceReview.units.All(u => u.allTriangleCountsMatch), "Original geometry evidence has not passed.");
            foreach (var row in sourceReview.units) Require(PlatformerSourceSha(row.source) == row.sourceSha256, "Source changed since visual review: " + row.source);
            string output = Environment.GetEnvironmentVariable("WAR_SANDBOX_QUALITY_OUTPUT");
            Require(!string.IsNullOrWhiteSpace(output), "A fresh isolated evidence directory is required.");
            var setup = EditorSceneManager.GetSceneManagerSetup(); var oldBuild = EditorBuildSettings.scenes;
            bool made = false;
            try
            {
                // Only read source scenes. Capture the actual authored units and battlefield-local selectable roster.
                var old = Load<WarSandboxBattlefieldCatalog>(Previous + "/Catalog.asset");
                var specs = old.entries.Select(e => new[] { e.id, e.scenePath }).ToArray();
                var features = new Dictionary<string, string[]>();
                foreach (var spec in specs)
                {
                    EditorSceneManager.OpenScene(spec[1], OpenSceneMode.Single);
                    var manager = Object.FindFirstObjectByType<MassEngineManager>(); Require(manager != null && manager.scenarioConfig != null, "No authored scenario: " + spec[0]);
                    old = Load<WarSandboxBattlefieldCatalog>(Previous + "/Catalog.asset");
                    var authored = manager.scenarioConfig.unitTypes;
                    // A formal scene must not quietly retain a withdrawn model in its default army.
                    if (!WithdrawnBattlefields.Contains(spec[0]))
                        foreach (var unit in authored)
                            Require(!old.templates.Any(t => WithdrawnTemplates.Contains(t.templateId) && t.config.renderConfig.vatProfile == unit.renderConfig.vatProfile), "Visible scenario still contains a withdrawn body: " + spec[0]);
                    var deployment = manager.GetComponent<WarSandboxRuntimeDeployment>();
                    var roster = deployment != null && deployment.rosterPolicy != null ? deployment.rosterPolicy.templates : Array.Empty<UnitTypeConfig>();
                    var candidates = authored.Concat(roster).Distinct();
                    var ids = new List<string>();
                    foreach (var unit in candidates)
                    {
                        Require(old.TryGetTemplateId(unit, out string id, out _), "Actual preview unit lacks a stable catalog identity: " + spec[0] + " / " + unit.name);
                        if (!WithdrawnTemplates.Contains(id)) ids.Add(id);
                    }
                    features.Add(spec[0], ids.Distinct().ToArray());
                }
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                Require(!string.IsNullOrEmpty(AssetDatabase.CreateFolder("Assets/Game/OfficialRoster", "Version07")), "Cannot create fresh quality root."); made = true;
                AssetDatabase.CreateFolder(Root, "Previews"); AssetDatabase.CreateFolder(Root, "UnitPreviews");
                old = Load<WarSandboxBattlefieldCatalog>(Previous + "/Catalog.asset");
                var catalog = ScriptableObject.CreateInstance<WarSandboxBattlefieldCatalog>(); catalog.defaultEntryId = old.defaultEntryId;
                catalog.entries = old.entries.Select(e => e.CopyIdentity()).ToArray();
                catalog.templates = old.templates.Select(t => new WarSandboxUnitTemplateEntry
                {
                    templateId = t.templateId, revision = t.revision, config = t.config,
                    hiddenFromSelection = WithdrawnTemplates.Contains(t.templateId),
                    selectionNote = WithdrawnTemplates.Contains(t.templateId) ? Reason : t.selectionNote
                }).ToArray();
                foreach (var entry in catalog.entries)
                {
                    entry.hiddenFromSelection = WithdrawnBattlefields.Contains(entry.id);
                    entry.selectionNote = entry.hiddenFromSelection ? Reason : entry.selectionNote;
                    entry.featuredTemplateIds = features[entry.id];
                    if (!entry.id.StartsWith("launch-", StringComparison.Ordinal))
                    {
                        string image = Root + "/Previews/" + entry.id + ".png";
                        WriteNew(image, File.ReadAllBytes(AssetDatabase.GetAssetPath(entry.preview))); entry.preview = ImportImage(image, 1024);
                    }
                }
                AssetDatabase.CreateAsset(catalog, CatalogPath); AssetDatabase.SaveAssets();
                var report = new QualityReport { sourceReview = SourceEvidence, identityBattlefields = catalog.entries.Length,
                    selectableBattlefields = catalog.SelectableEntryCount, identityTemplates = catalog.templates.Length,
                    selectableTemplates = catalog.templates.Count(t => !t.hiddenFromSelection), withdrawnTemplates = WithdrawnTemplates, withdrawnBattlefields = WithdrawnBattlefields };
                var unique = new Dictionary<string, PortraitEvidence>(); var rows = new List<PortraitEvidence>();
                foreach (var template in catalog.templates.Where(t => !t.hiddenFromSelection))
                {
                    var render = template.config.renderConfig;
                    string signature = AssetDatabase.GetAssetPath(render.vatProfile) + "|" + AssetDatabase.GetAssetPath(render.nearMesh) + "|" +
                        AssetDatabase.GetAssetPath(render.nearMaterial) + "|" + AssetDatabase.GetAssetPath(render.midMesh) + "|" +
                        AssetDatabase.GetAssetPath(render.midMaterial) + "|" + AssetDatabase.GetAssetPath(render.farMesh) + "|" + AssetDatabase.GetAssetPath(render.farMaterial);
                    if (!unique.TryGetValue(signature, out var evidence))
                    {
                        evidence = CaptureTemplate(template, output); unique.Add(signature, evidence);
                    }
                    template.unitPreview = Load<Texture2D>(evidence.portrait);
                    rows.Add(new PortraitEvidence { templateId = template.templateId, config = AssetDatabase.GetAssetPath(template.config),
                        profile = evidence.profile, material = evidence.material, portrait = evidence.portrait, foregroundPixels = evidence.foregroundPixels,
                        fullLodMotionChangedPixels = evidence.fullLodMotionChangedPixels, images = evidence.images });
                }
                report.uniqueRenderedModels = unique.Count; report.portraits = rows.ToArray(); report.technicalPassed = true;
                EditorUtility.SetDirty(catalog); AssetDatabase.SaveAssets();
                Require(catalog.TryValidate(p => AssetDatabase.LoadAssetAtPath<SceneAsset>(p) != null, out string error) && catalog.TryValidateTemplates(out error), error);
                Require(catalog.templates.Where(t => !t.hiddenFromSelection).All(t => t.unitPreview != null), "Every formal template needs a truthful portrait.");
                Require(catalog.entries.Where(e => !e.hiddenFromSelection).All(e => e.featuredTemplateIds.Length > 0), "Every formal entry needs its actual representative units.");
                Require(AssetDatabase.CopyAsset(Previous + "/LaunchMenu.unity", MenuScene), "Cannot clone the previous menu into the fresh version.");
                var menu = EditorSceneManager.OpenScene(MenuScene, OpenSceneMode.Single);
                var session = Object.FindFirstObjectByType<WarSandboxSceneSession>(); Require(session != null, "Cloned menu lacks a session.");
                session.catalog = Load<WarSandboxBattlefieldCatalog>(CatalogPath);
                foreach (var cycle in Object.FindObjectsByType<WarSandboxTerrainCycle>(FindObjectsSortMode.None)) cycle.session = session;
                EditorSceneManager.SaveScene(menu);
                EditorBuildSettings.scenes = Scenes(session.catalog).Select(p => new EditorBuildSettingsScene(p, true)).ToArray();
                AssetDatabase.SaveAssets();
                WriteNew(Path.Combine(output, "quality-report.json"), Encoding.UTF8.GetBytes(JsonUtility.ToJson(report, true)));
                Debug.Log("OFFICIAL_QUALITY_PREPARED identities=" + report.identityBattlefields + " selectable=" + report.selectableBattlefields + " unitImages=" + report.selectableTemplates + " unique=" + unique.Count);
            }
            catch
            {
                EditorBuildSettings.scenes = oldBuild;
                if (made) { AssetDatabase.DeleteAsset(Root); AssetDatabase.Refresh(); }
                throw;
            }
            finally
            {
                if (setup.Length > 0) EditorSceneManager.RestoreSceneManagerSetup(setup);
                else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
        }
        private static PortraitEvidence CaptureTemplate(WarSandboxUnitTemplateEntry template, string output)
        {
            var config = template.config; var runtime = ResolvedUnitTypeRuntime.Resolve(config, 1f);
            Require(runtime.nearMesh != null && runtime.midMesh != null && runtime.farMesh != null, "Missing configured game mesh: " + template.templateId);
            Bounds bounds = runtime.nearMesh.bounds; bounds.Encapsulate(runtime.midMesh.bounds); bounds.Encapsulate(runtime.farMesh.bounds);
            var row = new PortraitEvidence { templateId = template.templateId, config = AssetDatabase.GetAssetPath(config),
                profile = AssetDatabase.GetAssetPath(config.renderConfig.vatProfile), material = AssetDatabase.GetAssetPath(runtime.nearMaterial) };
            var files = new List<string>(); var changed = new List<int>();
            using (var capture = new VatAppearanceRegressionCapture(bounds, 768))
            {
                var states = new[] { AgentState.Idle, AgentState.Move, AgentState.Attack, AgentState.Dead };
                var durations = new[] { runtime.idleClipDuration, runtime.moveClipDuration, runtime.attackClipDuration, runtime.deathClipDuration };
                for (int c = 0; c < states.Length; c++)
                {
                    var samples = new List<Color32[]>();
                    for (int f = 0; f < 3; f++)
                    {
                        var pixels = capture.Capture(runtime, 0, states[c], durations[c] * new[] { .15f, .45f, .8f }[f]); CheckImage(pixels);
                        string name = template.templateId + "-full-" + c + "-" + f + ".png";
                        SaveImage(Path.Combine(output, name), capture.Size, pixels); files.Add(name); samples.Add(pixels);
                        if (c == 0 && f == 0)
                        {
                            row.foregroundPixels = VatAppearanceRegressionImageChecks.Measure(pixels).foregroundPixels;
                            row.portrait = Root + "/UnitPreviews/" + template.templateId + ".png";
                            WritePortrait(row.portrait, capture.Size, pixels); ImportImage(row.portrait, 1024);
                        }
                    }
                    Require(VatAppearanceRegressionImageChecks.HasMotion(samples, out int delta), "No visible " + states[c] + " motion: " + template.templateId); changed.Add(delta);
                }
                for (int lod = 1; lod <= 2; lod++)
                {
                    var pixels = capture.Capture(runtime, lod, AgentState.Idle, runtime.idleClipDuration * .15f); CheckImage(pixels);
                    string name = template.templateId + "-lod" + lod + ".png"; SaveImage(Path.Combine(output, name), capture.Size, pixels); files.Add(name);
                }
            }
            foreach (var view in new[] { new Vector3(0, 1, -5), new Vector3(5, 1, 0) })
                using (var capture = new VatAppearanceRegressionCapture(bounds, 512, view))
                {
                    var pixels = capture.Capture(runtime, 0, AgentState.Idle, runtime.idleClipDuration * .15f); CheckImage(pixels);
                    string name = template.templateId + (view.x == 0 ? "-front.png" : "-side.png"); SaveImage(Path.Combine(output, name), capture.Size, pixels); files.Add(name);
                }
            row.fullLodMotionChangedPixels = changed.ToArray(); row.images = files.ToArray();
            Debug.Log("QUALITY_UNIT_IMAGES " + template.templateId + " motions=" + string.Join(",", row.fullLodMotionChangedPixels)); return row;
        }
        private static void WritePortrait(string path, int size, Color32[] pixels)
        {
            int minX = size, minY = size, maxX = -1, maxY = -1;
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++) if (pixels[y * size + x].a >= 128)
            { minX = Math.Min(minX, x); minY = Math.Min(minY, y); maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y); }
            Require(minX >= 3 && minY >= 3 && maxX < size - 3 && maxY < size - 3, "Capture touches the edge: do not crop body parts to make a thumbnail.");
            int width = maxX - minX + 1, height = maxY - minY + 1;
            const int w = 448, h = 560, pad = 28;
            float scale = Mathf.Min((w - pad * 2f) / width, (h - pad * 2f) / height);
            int targetW = Mathf.Max(1, Mathf.RoundToInt(width * scale)), targetH = Mathf.Max(1, Mathf.RoundToInt(height * scale));
            int left = (w - targetW) / 2, bottom = (h - targetH) / 2;
            var canvas = new Color32[w * h];
            // Uniform scale only. Transparent margin, no repainting, no stretching, no concept-art replacement.
            for (int y = 0; y < targetH; y++) for (int x = 0; x < targetW; x++)
            {
                int sx = minX + Mathf.Clamp(Mathf.FloorToInt((x + .5f) / scale), 0, width - 1);
                int sy = minY + Mathf.Clamp(Mathf.FloorToInt((y + .5f) / scale), 0, height - 1);
                canvas[(bottom + y) * w + left + x] = pixels[sy * size + sx];
            }
            var texture = new Texture2D(w, h, TextureFormat.RGBA32, false);
            try { texture.SetPixels32(canvas); texture.Apply(false, false); WriteNew(path, texture.EncodeToPNG()); } finally { Object.DestroyImmediate(texture); }
        }
        private static Texture2D ImportImage(string path, int max)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path); importer.mipmapEnabled = false; importer.maxTextureSize = max;
            importer.wrapMode = TextureWrapMode.Clamp; importer.filterMode = FilterMode.Bilinear; importer.sRGBTexture = true;
            importer.npotScale = TextureImporterNPOTScale.None; importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.alphaIsTransparency = true; importer.SaveAndReimport(); return Load<Texture2D>(path);
        }
        public static string[] Scenes(WarSandboxBattlefieldCatalog catalog) => new[] { MenuScene }.Concat(catalog.entries.Select(e => e.scenePath)).Distinct().ToArray();
        public static void Build07()
        {
            Require(!Directory.Exists(Output) && !File.Exists(Output), "Refusing to replace an existing package: " + Output);
            var catalog = Load<WarSandboxBattlefieldCatalog>(CatalogPath);
            Require(catalog.TryValidate(p => AssetDatabase.LoadAssetAtPath<SceneAsset>(p) != null, out string error) && catalog.TryValidateTemplates(out error), error);
            Require(catalog.templates.Where(t => !t.hiddenFromSelection).All(t => t.unitPreview != null), "Formal unit portrait gate is not satisfied.");
            string output = Path.GetFullPath(Output); Directory.CreateDirectory(output);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = Scenes(catalog), target = BuildTarget.StandaloneWindows64,
                locationPathName = Path.Combine(output, "WarSandbox.exe"), options = BuildOptions.Development });
            if (report.summary.result != BuildResult.Succeeded) throw new BuildFailedException("New quality Windows build failed; preserve its failed evidence.");
            File.WriteAllText(Path.Combine(output, "Start-WarSandbox.cmd"), "@echo off\r\ncd /d \"%~dp0\"\r\nstart \"\" \"WarSandbox.exe\" -screen-fullscreen 0 -screen-width 1280 -screen-height 720\r\n", new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(output, "说明.txt"), "Version07 质量修正版。正式可选战场 " + catalog.SelectableEntryCount + " 个；原有 " + catalog.entries.Length + " 个身份保留兼容。\r\n" +
                "目录详情左侧为战场总览，右侧为实际游戏模型全身静态图；箭头切换代表兵种，＋放大。兵种库也可查看同一模型图。\r\n" +
                "绿皮小怪与骷髅头已退出新正式选择，不删资源或存档身份。蟹怪继续作为现有近战候选，无额外技能。\r\n" +
                "首次启动仍为512人准备战场。人工试玩暂停；技术验证不等于观感、平衡或V1签收。旧版包未覆盖。\r\n", new UTF8Encoding(true));
            Debug.Log("OFFICIAL_QUALITY_BUILD_OK " + output + " scenes=" + Scenes(catalog).Length);
        }
        private static string PlatformerSourceSha(string path)
        { using (var hash = System.Security.Cryptography.SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant(); }
        private static void SaveImage(string path, int size, Color32[] pixels)
        { var t = new Texture2D(size, size, TextureFormat.RGBA32, false); try { t.SetPixels32(pixels); t.Apply(false, false); WriteNew(path, t.EncodeToPNG()); } finally { Object.DestroyImmediate(t); } }
        private static void CheckImage(Color32[] pixels)
        { Require(VatAppearanceRegressionImageChecks.IsVisibleAndNotPink(VatAppearanceRegressionImageChecks.Measure(pixels), out string error), error); }
        private static T Load<T>(string path) where T : Object => AssetDatabase.LoadAssetAtPath<T>(path) ?? throw new InvalidOperationException("Missing asset " + path);
        private static void Require(bool value, string error) { if (!value) throw new InvalidOperationException(error); }
        private static void WriteNew(string path, byte[] bytes) { using (var file = new FileStream(path, FileMode.CreateNew)) file.Write(bytes, 0, bytes.Length); }
    }
}
