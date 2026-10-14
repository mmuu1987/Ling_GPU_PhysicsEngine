using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace MassEngine.Game.Editor
{
    /// <summary>
    /// 2026-10-11  1024x1024 battlefield generator (menu: Tools/战场 1024).
    /// Every map is regenerated from code into its own self-contained folder under
    /// Assets/Game/Content/Battlefields/Maps1024/&lt;Map&gt;: Surface (heights + blocked cells),
    /// ground/backdrop meshes, baked control texture, ground material, rules, preview and scene.
    /// The scene starts as a copy of Map1024/Green1024.unity so every gameplay component
    /// (manager, deployment, HUDs, camera rig) stays identical; only terrain and scenery change.
    /// Trees and rocks are visual only and are placed exclusively on blocked cells, so what the
    /// player sees as forest is exactly what the flow field refuses to cross.
    /// Third-party-derived textures are written below Assets/ThirdParty/_Derived (not tracked).
    /// </summary>
    public static class Battlefield1024Generator
    {
        public const string MenuRoot = "Tools/战场 1024/";
        public const string Template = "Assets/Game/Content/Battlefields/Map1024/Green1024.unity";
        public const string Root = "Assets/Game/Content/Battlefields/Maps1024";
        public const string Derived = "Assets/ThirdParty/_Derived/Maps1024";
        public const string CatalogPath = "Assets/Game/Scenes/Catalog.asset";
        public const string GroundShader = "MassEngine/Battlefield Ground 1024";
        public const string LogDir = "Logs/Battlefield1024";
        public const int Vertices = 257;                       // 256 x 256 cells at 4 m
        public const float Half = 512f, WorldSize = 1024f, SlopeLimit = 38f;
        public const float EdgeBlock = 472f;                   // |x| or |z| beyond this is blocked
        public const float OpenCore = 300f;                    // kept open and gentle for big battles
        // Two 100k armies at density 0.7 / aspect 2.2 (same check as Map1024Builder).
        public static readonly Vector2 ArmyCenter = new Vector2(152.4f, 0f), ArmySize = new Vector2(254.8f, 560.7f);
        public const long DecorationTriangleBudget = 2000000;

        const string GroundName = "Plan B - Shared Render And Navigation Surface";
        const string BackdropName = "Visual Only Backdrop - No navigation or colliders";
        const string OldSceneryName = "22 Seasonal Woodland - visual only outside navigation";
        public const string SceneryName = "1024 Scenery - visual only, trees stand on blocked cells";

        [MenuItem(MenuRoot + "生成 翠绿平原", false, 1)] static void MenuPlains() => RunFromMenu(new ProcSkinMap(new PlainsMap())); // 2026-10-13: procedural props + ground
        [MenuItem(MenuRoot + "生成 森林丘陵", false, 2)] static void MenuForest() => RunFromMenu(new ProcSkinMap(new ForestHillsMap()));
        [MenuItem(MenuRoot + "生成全部（2 张）", false, 3)] static void MenuAll() => RunFromMenu(new ProcSkinMap(new PlainsMap()), new ProcSkinMap(new ForestHillsMap()));

        static void RunFromMenu(params MapSpec[] maps)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { EditorUtility.DisplayDialog("战场 1024", "请先退出 Play 模式。", "好"); return; }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var existing = maps.Where(m => AssetDatabase.IsValidFolder(m.Dir)).Select(m => m.DisplayName).ToArray();
            if (existing.Length > 0 && !EditorUtility.DisplayDialog("战场 1024", "以下地图已存在，会删除其目录后重新生成：\n" + string.Join("\n", existing), "重新生成", "取消")) return;
            var reports = new List<Report>();
            try { foreach (var m in maps) reports.Add(Generate(m)); }
            catch (Exception e) { Debug.LogException(e); EditorUtility.DisplayDialog("战场生成失败", e.Message, "好"); return; }
            finally { EditorUtility.ClearProgressBar(); RepairCatalogAndBuildSettings(); }
            string text = string.Join("\n\n", reports.Select(r => r.Summary()));
            Debug.Log("[Battlefield1024] 生成完成\n" + text);
            EditorUtility.DisplayDialog("战场生成完成", text + "\n\n详细报告：" + LogDir, "好");
        }

        [Serializable]
        public sealed class Report
        {
            public string id, displayName, scene, terrainId;
            public int blockedCells, walkableCells, props, renderers, materials, textures, shadowCasters;
            public long decorationTriangles;
            public bool connected, footprintsWalkable, catalogValid, terrainProviderValid;
            public float minHeight, maxHeight, coreMaxSlopeDegrees;
            public List<string> notes = new List<string>();
            public List<string> prefabs = new List<string>();
            public string Summary() => displayName + "（" + id + "）\n  场景：" + scene +
                "\n  可通行格 " + walkableCells + " / 禁行格 " + blockedCells + "，高度 " + minHeight.ToString("F0") + "–" + maxHeight.ToString("F0") + " m" +
                "\n  景物 " + props + " 个，三角形 " + decorationTriangles.ToString("N0") + "，投影 " + shadowCasters +
                (notes.Count > 0 ? "\n  注意：" + string.Join("；", notes.Take(4)) : "");
        }

        // ------------------------------------------------------------------ generation
        public static Report Generate(MapSpec spec)
        {
            // 2026-10-14 (2048 maps): the size-dependent constants come from spec.Size. These locals deliberately
            // shadow the class constants of the same name; for 1024 maps (Size = S1024) the values are identical.
            var size = spec.Size;
            int Vertices = size.Vertices; float Half = size.Half, WorldSize = size.WorldSize, EdgeBlock = size.EdgeBlock, OpenCore = size.OpenCore;
            Vector2 ArmyCenter = size.ArmyCenter, ArmySize = size.ArmySize; string LogDir = size.LogDir;
            Require(File.Exists(size.TemplateScene), "缺少模板场景 " + size.TemplateScene);
            Require(Shader.Find(GroundShader) != null, "缺少着色器 " + GroundShader + "（Maps1024/Shared/BattlefieldGround1024.shader）");
            var report = new Report { id = spec.Id, displayName = spec.DisplayName, scene = spec.ScenePath, terrainId = spec.TerrainId };
            var catalog = AssetDatabase.LoadAssetAtPath<WarSandboxBattlefieldCatalog>(CatalogPath);
            Require(catalog != null, "缺少地图目录 " + CatalogPath);
            var source = catalog.entries.FirstOrDefault(e => e.scenePath == Template);
            Require(source != null && !ReferenceEquals(source.rules, null), "地图目录里没有模板条目 " + Template);
            // Unity may unload assets on OpenScene/imports; keep PATHS and reload, never reuse wrappers.
            string templateRulesPath = AssetDatabase.GetAssetPath(source.rules.GetInstanceID());
            Require(!string.IsNullOrEmpty(templateRulesPath), "模板规则不是资源文件：" + Template);

            Progress(spec, "清理旧输出", .02f);
            if (catalog.entries.Any(e => e.id == spec.Id))
            {
                catalog.entries = catalog.entries.Where(e => e.id != spec.Id).ToArray();
                EditorUtility.SetDirty(catalog); AssetDatabase.SaveAssets();
            }
            if (SceneManager.GetActiveScene().path == spec.ScenePath) EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            if (AssetDatabase.IsValidFolder(spec.Dir)) Require(AssetDatabase.DeleteAsset(spec.Dir), "无法删除 " + spec.Dir);
            if (AssetDatabase.IsValidFolder(spec.DerivedDir)) AssetDatabase.DeleteAsset(spec.DerivedDir);
            EnsureFolder(spec.Dir + "/Textures"); EnsureFolder(spec.Dir + "/Materials"); EnsureFolder(spec.DerivedDir);

            // 1) Gameplay surface: 257^2 heights, 256^2 blocked cells, 4 m step, origin -512.
            Progress(spec, "地形与禁行格", .06f);
            var heights = new float[Vertices * Vertices];
            var blocked = new bool[(Vertices - 1) * (Vertices - 1)];
            for (int z = 0; z < Vertices; z++)
                for (int x = 0; x < Vertices; x++)
                    heights[z * Vertices + x] = spec.Height(-Half + x * 4f, -Half + z * 4f);
            for (int z = 0; z < Vertices - 1; z++)
                for (int x = 0; x < Vertices - 1; x++)
                {
                    float cx = -Half + (x + .5f) * 4f, cz = -Half + (z + .5f) * 4f;
                    blocked[z * (Vertices - 1) + x] = Mathf.Max(Mathf.Abs(cx), Mathf.Abs(cz)) > EdgeBlock || spec.Blocked(cx, cz);
                }
            var surfaceAsset = ScriptableObject.CreateInstance<TerrainSurfaceAsset>();
            surfaceAsset.name = spec.Folder + " Surface";
            surfaceAsset.Initialize(spec.TerrainId, spec.TerrainVersion, Vertices, Vertices, new Vector2(-Half, -Half),
                new Vector2(WorldSize, WorldSize), SlopeLimit, heights, blocked);
            AssetDatabase.CreateAsset(surfaceAsset, spec.Dir + "/Surface.asset");
            Require(surfaceAsset.TryCreateSurface(out var surface, out var error), "地形无效：" + error);
            report.minHeight = heights.Min(); report.maxHeight = heights.Max();
            for (int z = 0; z < Vertices - 1; z++)
                for (int x = 0; x < Vertices - 1; x++)
                {
                    if (surface.IsCellWalkable(x, z)) report.walkableCells++; else report.blockedCells++;
                    float cx = -Half + (x + .5f) * 4f, cz = -Half + (z + .5f) * 4f;
                    if (Mathf.Abs(cx) < OpenCore && Mathf.Abs(cz) < OpenCore && surface.TrySample(new Vector2(cx, cz), out var q))
                        report.coreMaxSlopeDegrees = Mathf.Max(report.coreMaxSlopeDegrees, Mathf.Atan(q.Gradient.magnitude) * Mathf.Rad2Deg);
                }

            // 2) Meshes.
            Progress(spec, "地面与远景网格", .12f);
            var ground = TerrainPrototype.CreateMesh(surface);
            ground.name = spec.Folder + " Ground";
            AssetDatabase.CreateAsset(ground, spec.Dir + "/GroundMesh.asset");
            var backdrop = new Backdrop(spec, surface, ground);
            AssetDatabase.CreateAsset(backdrop.Mesh, spec.Dir + "/BackdropMesh.asset");
            if (size.BakeCameraGround)
            {
                // Camera floor / pan anchor for this map (the 1024 maps share the template's VisualGround1024).
                var cameraGround = ScriptableObject.CreateInstance<CameraGround24>();
                cameraGround.name = spec.Folder + " CameraGround";
                cameraGround.coordinates = (float[])backdrop.Coords.Clone(); cameraGround.heights = (float[])backdrop.Heights.Clone();
                AssetDatabase.CreateAsset(cameraGround, spec.Dir + "/CameraGround.asset");
            }

            // 3) Control texture + ground material.
            Progress(spec, "烘焙地表控制图", .18f);
            var control = BakeControl(spec, surface);
            Progress(spec, "地面贴图", .26f);
            var look = spec.Look;
            var groundMaterial = new Material(Shader.Find(GroundShader)) { name = spec.Folder + " Ground" };
            groundMaterial.SetTexture("_Control", control);
            groundMaterial.SetVector("_ControlRect", new Vector4(-Half, -Half, WorldSize, WorldSize));
            groundMaterial.SetTexture("_GrassA", GroundTexture(look.GrassA, spec, "GrassA"));
            groundMaterial.SetTexture("_GrassB", GroundTexture(look.GrassB, spec, "GrassB"));
            groundMaterial.SetTexture("_Dirt", GroundTexture(look.Dirt, spec, "Dirt"));
            groundMaterial.SetTexture("_Rock", GroundTexture(look.Rock, spec, "Rock"));
            groundMaterial.SetTexture("_Forest", GroundTexture(look.Forest, spec, "Forest"));
            groundMaterial.SetVector("_Tiling", look.Tiling); groundMaterial.SetFloat("_ForestTiling", look.ForestTiling);
            groundMaterial.SetColor("_GrassATint", look.GrassATint); groundMaterial.SetColor("_GrassBTint", look.GrassBTint);
            groundMaterial.SetColor("_DirtTint", look.DirtTint); groundMaterial.SetColor("_RockTint", look.RockTint);
            groundMaterial.SetColor("_ForestTint", look.ForestTint);
            groundMaterial.SetVector("_PaletteDark", look.PaletteDark); groundMaterial.SetVector("_PaletteLight", look.PaletteLight);
            groundMaterial.SetFloat("_Stylize", look.Stylize); groundMaterial.SetVector("_SlopeRock", look.SlopeRock);
            groundMaterial.SetVector("_OutsideMix", look.OutsideMix);
            groundMaterial.SetVector("_HazeColor", look.Haze); groundMaterial.SetFloat("_HazeStart", look.HazeStart); groundMaterial.SetFloat("_HazeEnd", look.HazeEnd);
            AssetDatabase.CreateAsset(groundMaterial, spec.Dir + "/Materials/Ground.mat");

            // 4) Rules: own copy, terrain does the obstacles, control point at the map centre.
            var templateRules = AssetDatabase.LoadAssetAtPath<WarSandboxBattlefieldConfig>(templateRulesPath);
            Require(templateRules != null, "无法加载模板规则 " + templateRulesPath);
            var rules = ScriptableObject.CreateInstance<WarSandboxBattlefieldConfig>();
            rules.name = spec.Folder + " Rules";
            var r = templateRules.rules.Copy();
            r.staticObstaclesEnabled = false; r.staticObstacles = new StaticObstacleRect[0];
            surface.TrySample(spec.ControlPoint, out var cp);
            r.controlPointCenter = new Vector3(spec.ControlPoint.x, cp.Position.y, spec.ControlPoint.y);
            r.controlPointRadius = Mathf.Max(r.controlPointRadius, spec.ControlPointRadius);
            rules.rules = r;
            Require(rules.TryCreateSnapshot(out _, out error), "规则无效：" + error);
            AssetDatabase.CreateAsset(rules, spec.Dir + "/Rules.asset");
            AssetDatabase.SaveAssets();

            // 5) Scene from the 1024 template.
            Progress(spec, "场景", .32f);
            Require(AssetDatabase.CopyAsset(size.TemplateScene, spec.ScenePath), "复制模板场景失败");
            var scene = EditorSceneManager.OpenScene(spec.ScenePath, OpenSceneMode.Single);
            // OpenScene(Single) unloads assets referenced only from C# locals -> reload by path.
            surfaceAsset = Reload<TerrainSurfaceAsset>(spec.Dir + "/Surface.asset");
            ground = Reload<Mesh>(spec.Dir + "/GroundMesh.asset");
            var backdropMesh = Reload<Mesh>(spec.Dir + "/BackdropMesh.asset");
            groundMaterial = Reload<Material>(spec.Dir + "/Materials/Ground.mat");
            rules = Reload<WarSandboxBattlefieldConfig>(spec.Dir + "/Rules.asset");
            var manager = Find<MassEngineManager>(scene);
            manager.terrainSurfaceAsset = surfaceAsset;
            EditorUtility.SetDirty(manager);
            var groundGo = FindRoot(scene, GroundName); Require(groundGo != null, "模板里缺少 " + GroundName);
            ResetTransform(groundGo.transform);
            groundGo.GetComponent<MeshFilter>().sharedMesh = ground;
            var collider = groundGo.GetComponent<MeshCollider>(); if (collider != null) { collider.sharedMesh = null; collider.sharedMesh = ground; }
            var gr = groundGo.GetComponent<MeshRenderer>(); gr.sharedMaterial = groundMaterial; gr.shadowCastingMode = ShadowCastingMode.Off; gr.receiveShadows = true;
            var backGo = FindRoot(scene, BackdropName); Require(backGo != null, "模板里缺少 " + BackdropName);
            ResetTransform(backGo.transform);
            backGo.GetComponent<MeshFilter>().sharedMesh = backdropMesh;
            var br = backGo.GetComponent<MeshRenderer>(); br.sharedMaterial = groundMaterial; br.shadowCastingMode = ShadowCastingMode.Off; br.receiveShadows = true;
            var old = FindRoot(scene, OldSceneryName); if (old != null) Object.DestroyImmediate(old);
            if (size.BakeCameraGround)
            {
                var cameraGround = Reload<CameraGround24>(spec.Dir + "/CameraGround.asset");
                foreach (var g in scene.GetRootGameObjects())
                    foreach (var comfort in g.GetComponentsInChildren<BattlefieldCameraComfort24>(true)) { comfort.ground = cameraGround; EditorUtility.SetDirty(comfort); }
            }

            // 6) Scenery.
            Progress(spec, "摆放树木与岩石", .40f);
            var root = new GameObject(SceneryName);
            SceneManager.MoveGameObjectToScene(root, scene);
            var deco = new Decorator(spec, surface, backdrop, root.transform);
            spec.Decorate(deco);
            deco.Finish(report);

            // 7) Catalog entry (default entry untouched until the old scenes are cleaned up).
            Progress(spec, "地图目录", .86f);
            EditorSceneManager.MarkSceneDirty(scene);
            Require(EditorSceneManager.SaveScene(scene), "保存场景失败");
            AddToBuildSettings(spec.ScenePath);
            catalog = Reload<WarSandboxBattlefieldCatalog>(CatalogPath);
            source = catalog.entries.FirstOrDefault(e => e.scenePath == Template);
            Require(source != null, "地图目录里没有模板条目 " + Template);
            var entry = source.CopyIdentity();
            entry.id = spec.Id; entry.displayName = spec.DisplayName; entry.scenePath = spec.ScenePath;
            entry.contentVersion = 1; entry.terrainId = spec.TerrainId; entry.terrainVersion = spec.TerrainVersion;
            entry.terrainSurface = surfaceAsset; entry.rules = rules; entry.description = spec.Description;
            entry.briefing = spec.Briefing; entry.hiddenFromSelection = false; entry.selectionNote = "";
            entry.preview = CapturePreview(spec, scene);
            catalog.entries = catalog.entries.Concat(new[] { entry }).ToArray();
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();

            // 8) Validation (same contract Map1024Builder used for Green1024).
            Progress(spec, "校验", .94f);
            report.catalogValid = catalog.TryValidate(File.Exists, out error);
            if (!report.catalogValid) report.notes.Add("目录校验：" + error);
            report.terrainProviderValid = WarSandboxSceneSession.TryValidateTerrainProvider(manager, entry, out error);
            if (!report.terrainProviderValid) report.notes.Add("地形提供者：" + error);
            Require(manager.terrainSurfaceAsset != null, "场景管理器的地形资源为空");
            Require(manager.TryGetTerrainContext(out var s2, out var nav, out error) && nav != null, "导航构建失败：" + (error ?? "未生成导航（地形资源无效）"));
            report.connected = nav.AreConnected(new Vector2(-size.ConnectProbe, 0), new Vector2(size.ConnectProbe, 0));
            foreach (var p in spec.MustConnect)
                if (!nav.AreConnected(new Vector2(-size.ConnectProbe, 0), p)) { report.connected = false; report.notes.Add("不连通：" + p); }
            report.footprintsWalkable = nav.IsFootprintWalkable(new Vector2(-ArmyCenter.x, 0), ArmySize) && nav.IsFootprintWalkable(ArmyCenter, ArmySize);
            ActorMaterialBuildGuard.RequireValid(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory(LogDir);
            File.WriteAllText(LogDir + "/" + spec.Id + ".json", JsonUtility.ToJson(report, true));
            Require(report.connected, spec.DisplayName + "：左右部署区不连通（" + string.Join("；", report.notes) + "）");
            Require(report.footprintsWalkable, spec.DisplayName + "：20 万人部署矩形不可通行");
            Require(report.catalogValid && report.terrainProviderValid, spec.DisplayName + "：" + string.Join("；", report.notes));
            return report;
        }

        // ------------------------------------------------------------------ control texture
        const int ControlSize = 1024;
        static Texture2D BakeControl(MapSpec spec, TerrainSurface surface)
        {
            // Locals shadow the 1024 constants (same values for 1024 maps).
            int ControlSize = spec.Size.ControlSize; float WorldSize = spec.Size.WorldSize, Half = spec.Size.Half;
            var pixels = new Color32[ControlSize * ControlSize];
            float texel = WorldSize / ControlSize;
            for (int z = 0; z < ControlSize; z++)
                for (int x = 0; x < ControlSize; x++)
                {
                    float wx = -Half + (x + .5f) * texel, wz = -Half + (z + .5f) * texel;
                    surface.TrySample(new Vector2(wx, wz), out var q);
                    float slope = 1f - 1f / Mathf.Sqrt(1f + q.Gradient.sqrMagnitude);
                    pixels[z * ControlSize + x] = spec.Splat(wx, wz, q.Position.y, slope);
                }
            var t = new Texture2D(ControlSize, ControlSize, TextureFormat.RGBA32, false, true);
            t.SetPixels32(pixels); t.Apply();
            string path = spec.Dir + "/Textures/Control.png";
            File.WriteAllBytes(path, t.EncodeToPNG());
            Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(path);
            var i = (TextureImporter)AssetImporter.GetAtPath(path);
            i.sRGBTexture = false; i.alphaSource = TextureImporterAlphaSource.FromInput; i.alphaIsTransparency = false;
            i.maxTextureSize = ControlSize; i.mipmapEnabled = true; i.wrapMode = TextureWrapMode.Clamp;
            i.filterMode = FilterMode.Bilinear; i.textureCompression = TextureImporterCompression.Uncompressed; i.isReadable = false;
            i.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static Texture2D GroundTexture(string source, MapSpec spec, string name)
        {
            Require(File.Exists(source), "缺少地面素材 " + source);
            string dest = spec.DerivedDir + "/" + name + Path.GetExtension(source);
            Require(AssetDatabase.CopyAsset(source, dest), "复制地面素材失败 " + source);
            var imp = (TextureImporter)AssetImporter.GetAtPath(dest);
            imp.textureType = TextureImporterType.Default; imp.sRGBTexture = true;
            imp.maxTextureSize = 1024; imp.mipmapEnabled = true; imp.anisoLevel = 4; imp.wrapMode = TextureWrapMode.Repeat;
            imp.textureCompression = TextureImporterCompression.Compressed; imp.crunchedCompression = false; imp.isReadable = false;
            var p = imp.GetPlatformTextureSettings("Standalone");
            p.overridden = true; p.maxTextureSize = 1024; p.format = TextureImporterFormat.Automatic; p.textureCompression = TextureImporterCompression.Compressed;
            imp.SetPlatformTextureSettings(p);
            imp.SaveAndReimport();
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>(dest);
            Require(t != null && t.width <= 1024 && t.height <= 1024, "地面贴图超出 1024 上限 " + dest);
            return t;
        }

        // ------------------------------------------------------------------ preview
        static Texture2D CapturePreview(MapSpec spec, Scene scene)
        {
            string LogDir = spec.Size.LogDir;
            var camera = Camera.main;
            if (camera == null) return null;
            bool async = ShaderUtil.allowAsyncCompilation;
            var pos = camera.transform.position; var rot = camera.transform.rotation;
            var rt = new RenderTexture(1280, 720, 24);
            var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            var oldTarget = camera.targetTexture; var active = RenderTexture.active;
            string path = spec.Dir + "/Preview.png";
            try
            {
                ShaderUtil.allowAsyncCompilation = false;
                camera.transform.position = spec.PreviewCamera; camera.transform.LookAt(spec.PreviewTarget);
                camera.targetTexture = rt; camera.Render();
                RenderTexture.active = rt; image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); image.Apply();
                File.WriteAllBytes(path, image.EncodeToPNG());
                Directory.CreateDirectory(LogDir);
                File.WriteAllBytes(LogDir + "/" + spec.Id + "-overview.png", image.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = oldTarget; RenderTexture.active = active;
                camera.transform.SetPositionAndRotation(pos, rot);
                rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(image);
                ShaderUtil.allowAsyncCompilation = async;
            }
            AssetDatabase.ImportAsset(path);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.maxTextureSize = 512; imp.mipmapEnabled = false; imp.textureCompression = TextureImporterCompression.Compressed;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // ------------------------------------------------------------------ helpers
        static T Reload<T>(string path) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            Require(asset != null, "无法重新加载 " + path);
            return asset;
        }

        /// <summary>Default entry must exist and every Build Settings scene must exist, whatever happened before.</summary>
        public static void RepairCatalogAndBuildSettings()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<WarSandboxBattlefieldCatalog>(CatalogPath);
            if (catalog != null && catalog.entries != null && catalog.entries.Length > 0 &&
                catalog.entries.All(e => e.id != catalog.defaultEntryId || e.hiddenFromSelection))
            {
                var preferred = catalog.entries.FirstOrDefault(e => e.id == "plains-1024" && !e.hiddenFromSelection)
                                ?? catalog.entries.FirstOrDefault(e => !e.hiddenFromSelection) ?? catalog.entries[0];
                Debug.LogWarning("[Battlefield1024] 默认地图 " + catalog.defaultEntryId + " 不存在，改为 " + preferred.id);
                catalog.defaultEntryId = preferred.id;
                EditorUtility.SetDirty(catalog);
                AssetDatabase.SaveAssets();
            }
            var scenes = EditorBuildSettings.scenes;
            var kept = scenes.Where(s => File.Exists(s.path)).ToArray();
            if (kept.Length != scenes.Length)
            {
                Debug.LogWarning("[Battlefield1024] Build Settings 移除不存在的场景：" + string.Join("，", scenes.Where(s => !File.Exists(s.path)).Select(s => s.path)));
                EditorBuildSettings.scenes = kept;
            }
        }

        public static void AddToBuildSettings(string path)
        {
            var list = EditorBuildSettings.scenes.ToList();
            if (list.Any(s => s.path == path)) return;
            list.Add(new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = list.ToArray();
        }

        public static void EnsureFolder(string path)
        {
            path = path.TrimEnd('/');
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        static void ResetTransform(Transform t) { t.localPosition = Vector3.zero; t.localRotation = Quaternion.identity; t.localScale = Vector3.one; }

        static GameObject FindRoot(Scene scene, string name) => scene.GetRootGameObjects().FirstOrDefault(g => g.name == name);

        static T Find<T>(Scene scene) where T : Component
        {
            foreach (var g in scene.GetRootGameObjects()) { var t = g.GetComponentInChildren<T>(true); if (t != null) return t; }
            throw new Exception("场景缺少 " + typeof(T).Name);
        }

        static void Progress(MapSpec spec, string step, float t) => EditorUtility.DisplayProgressBar("生成 " + spec.DisplayName, step, t);

        public static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    }

    /// <summary>Backdrop apron out to ±Size.BackdropOuter (1024: ±3072 m, 2048: ±5120 m): rectilinear grid, no triangles
    /// over the playable square. 2026-10-14: parameterised by MapSpec.Size; the 1024 grid is identical to before.</summary>
    public sealed class Backdrop
    {
        public readonly Mesh Mesh;
        readonly float[] coords; readonly float[] heights; readonly int n; readonly float half;
        /// <summary>Grid lines (same on x and z) and n×n heights; used to bake the 2048 camera ground.</summary>
        public float[] Coords => coords;
        public float[] Heights => heights;

        public Backdrop(MapSpec spec, TerrainSurface surface, Mesh ground)
        {
            var size = spec.Size; float h = size.Half, outer = size.BackdropOuter; int w = size.Vertices;
            half = h;
            var c = new List<float>();
            for (float v = -outer; v < -2f * h; v += 64f) c.Add(v);
            for (float v = -2f * h; v < -h; v += 16f) c.Add(v);
            for (int i = 0; i < w; i++) c.Add(-h + i * 4f);
            for (float v = h + 16f; v <= 2f * h; v += 16f) c.Add(v);
            for (float v = 2f * h + 64f; v <= outer; v += 64f) c.Add(v);
            coords = c.ToArray(); n = coords.Length;
            heights = new float[n * n];
            var vertices = new Vector3[n * n];
            for (int z = 0; z < n; z++)
                for (int x = 0; x < n; x++)
                {
                    float y = Height(spec, surface, coords[x], coords[z]);
                    heights[z * n + x] = y; vertices[z * n + x] = new Vector3(coords[x], y, coords[z]);
                }
            var indices = new List<int>((n - 1) * (n - 1) * 6);
            for (int z = 0; z < n - 1; z++)
                for (int x = 0; x < n - 1; x++)
                {
                    if (coords[x] >= -h && coords[x + 1] <= h && coords[z] >= -h && coords[z + 1] <= h) continue;
                    int i = z * n + x;
                    indices.Add(i); indices.Add(i + n + 1); indices.Add(i + 1);
                    indices.Add(i); indices.Add(i + n); indices.Add(i + n + 1);
                }
            Mesh = new Mesh { name = spec.Folder + " Backdrop", indexFormat = IndexFormat.UInt32 };
            Mesh.vertices = vertices; Mesh.SetTriangles(indices, 0); Mesh.RecalculateNormals();
            // Seam: copy the gameplay mesh normals so the two meshes shade as one surface.
            var normals = Mesh.normals; var groundNormals = ground.normals;
            for (int z = 0; z < n; z++)
                for (int x = 0; x < n; x++)
                {
                    float wx = coords[x], wz = coords[z];
                    if (Mathf.Abs(wx) > h || Mathf.Abs(wz) > h) continue;
                    int gx = Mathf.RoundToInt((wx + h) / 4f), gz = Mathf.RoundToInt((wz + h) / 4f);
                    normals[z * n + x] = groundNormals[gz * w + gx];
                }
            Mesh.normals = normals; Mesh.RecalculateBounds();
        }

        public static float Height(MapSpec spec, TerrainSurface s, float x, float z)
        {
            float h = spec.Size.Half;
            float outside = Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)) - h;
            s.TrySample(new Vector2(Mathf.Clamp(x, -h, h), Mathf.Clamp(z, -h, h)), out var q);
            if (outside <= 0) return q.Position.y;
            float rise = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(16f, 300f, outside));
            float ridge = spec.Look.RidgeBase + 55f * Mathf.PerlinNoise(x * .0035f + 37f, z * .0035f + 71f) + 24f * Mathf.Sin(Mathf.Atan2(z, x) * 5f + .7f);
            float far = spec.Look.FarMountains * Mathf.SmoothStep(0, 1, Mathf.InverseLerp(650f, 1900f, outside)) *
                        (.45f + .55f * Mathf.PerlinNoise(x * .0012f + 11f, z * .0012f + 5f));
            return Mathf.Lerp(q.Position.y, ridge, rise) + far;
        }

        /// <summary>Height of the rendered surface (ground inside, backdrop triangles outside).</summary>
        public float Sample(TerrainSurface s, float x, float z)
        {
            if (Mathf.Abs(x) <= half && Mathf.Abs(z) <= half && s.TrySample(new Vector2(x, z), out var q)) return q.Position.y;
            int ix = Index(x), iz = Index(z);
            float tx = Mathf.InverseLerp(coords[ix], coords[ix + 1], x), tz = Mathf.InverseLerp(coords[iz], coords[iz + 1], z);
            float a = heights[iz * n + ix], b = heights[iz * n + ix + 1], c = heights[(iz + 1) * n + ix], d = heights[(iz + 1) * n + ix + 1];
            return tx >= tz ? a + (b - a) * tx + (d - b) * tz : a + (c - a) * tz + (d - c) * tx;
        }

        int Index(float v)
        {
            int lo = 0, hi = n - 2;
            v = Mathf.Clamp(v, coords[0], coords[n - 1]);
            while (lo < hi) { int mid = (lo + hi + 1) / 2; if (coords[mid] <= v) lo = mid; else hi = mid - 1; }
            return lo;
        }
    }
}
