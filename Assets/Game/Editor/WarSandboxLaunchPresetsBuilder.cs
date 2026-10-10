using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MassEngine.Game.Editor
{
    /// <summary>New authored content only. Existing scenes, templates and player plans are preserved.</summary>
    public static class WarSandboxLaunchPresetsBuilder
    {
        public const string Root = "Assets/Game/Content/Battlefields/LaunchPresets";
        public const string MenuScene = Root + "/LaunchMenu.unity";
        public const string CatalogPath = Root + "/Catalog.asset";
        private const string FlatScene = "Assets/Game/Experiments/LegacyScenes/WarSandbox.unity";
        private static readonly string[] Ids = { "launch-open", "launch-mountain", "launch-point", "launch-three", "launch-standard" };
        private static readonly string[] Names = { "开阔对冲 · 快速上手", "山地绕行", "中央争夺", "三方混编", "开阔对冲 · 标准规模" };
        private static readonly string[] Descriptions = {
            "2 支军团 · 512 人 · 歼灭战\n近战在前、远程在后，适合第一次开战。",
            "3 支军团 · 384 人 · 歼灭战\n西侧宽坡、东侧隘口，绕过陡坡接敌。",
            "2 支军团 · 512 人 · 据点战\n抢先进入中央据点，独占 15 秒获胜。",
            "3 支军团 · 768 人 · 歼灭战\n多方向混编会战，可切换指挥任一军团。",
            "2 支军团 · 100,000 人 · 歼灭战\n50k 对 50k 标准档，运行负载较高。"
        };
        private static readonly string[] Briefings = {
            "目标：消灭另一支军团。\n按 Enter 开战，Space 暂停；先观战，也可选择军团下令。",
            "目标：消灭另外两支军团。\n西坡和东侧隘口可以通行；陡坡不可穿越。选中军团后按 M，再点击地面移动。",
            "目标：独占中央据点 15 秒，或消灭敌军。\n红军较近、蓝军从后方赶来；双方进入时争夺暂停。",
            "目标：成为最后存活的军团。\n三方相互敌对。选择不同军团，尝试进攻、防守或绕到侧面。",
            "目标：消灭另一支军团。\n这是 10 万人的标准规模选项；首次体验可返回目录选择快速上手。"
        };

        [MenuItem("MassEngine/Launch Presets/Create M7.1 Content")]
        public static void Prepare()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode first.");
            if (Directory.Exists(Root)) throw new InvalidOperationException("Refusing to overwrite existing launch content: " + Root);
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var setup = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                AssetDatabase.CreateFolder("Assets/Game/Content/Battlefields", "LaunchPresets");
                AssetDatabase.CreateFolder(Root, "Previews");
                var original = Load<ScenarioConfig>("Assets/Game/Settings/ScenarioConfig.asset");
                var mountain = Load<ScenarioConfig>(WarSandboxTerrainPlayableBuilder.Root + "/Settings/Scenario.asset");
                var catalog = ScriptableObject.CreateInstance<WarSandboxBattlefieldCatalog>();
                catalog.defaultEntryId = Ids[0]; catalog.entries = new WarSandboxBattlefieldEntry[Ids.Length];
                var templates = new List<WarSandboxUnitTemplateEntry>();
                for (int preset = 0; preset < Ids.Length; preset++)
                {
                    bool terrain = preset == 1, standard = preset == 4;
                    string folder = Root + "/" + Ids[preset]; AssetDatabase.CreateFolder(Root, Ids[preset]);
                    var source = terrain ? mountain : original;
                    int formations = terrain || preset == 3 ? 6 : 4;
                    var scenario = ScriptableObject.CreateInstance<ScenarioConfig>();
                    scenario.unitTypes = new UnitTypeConfig[formations];
                    for (int i = 0; i < formations; i++)
                    {
                        var unit = Object.Instantiate(source.unitTypes[i]);
                        var spawn = Object.Instantiate(unit.spawnConfig);
                        if (!terrain && !standard)
                        {
                            spawn.unitCount = i % 2 == 0 ? 192 : 64;
                            spawn.spawnSize = i % 2 == 0 ? new Vector3(16, 0, 24) : new Vector3(12, 0, 16);
                            spawn.spawnCenter = Center(preset, i);
                        }
                        AssetDatabase.CreateAsset(spawn, folder + "/Spawn" + i + ".asset");
                        unit.spawnConfig = spawn; AssetDatabase.CreateAsset(unit, folder + "/Unit" + i + ".asset");
                        scenario.unitTypes[i] = unit;
                        templates.Add(new WarSandboxUnitTemplateEntry { templateId = Ids[preset] + "-unit-" + i, revision = 1, config = unit });
                    }
                    AssetDatabase.CreateAsset(scenario, folder + "/Scenario.asset");
                    var rules = Object.Instantiate(Load<WarSandboxBattlefieldConfig>("Assets/Game/Settings/" +
                        (preset == 2 ? "BattlefieldRules_B_ControlPoint.asset" : "BattlefieldRules_A_Annihilation.asset")));
                    AssetDatabase.CreateAsset(rules, folder + "/Rules.asset");
                    string scenePath = folder + "/WarSandbox.unity";
                    if (!AssetDatabase.CopyAsset(terrain ? WarSandboxTerrainPlayableBuilder.BattleScene : FlatScene, scenePath))
                        throw new BuildFailedException("Scene copy failed: " + scenePath);
                    var surface = terrain ? Load<TerrainSurfaceAsset>(WarSandboxTerrainPrototypeBuilder.Root + "/Surface.asset") : null;
                    catalog.entries[preset] = new WarSandboxBattlefieldEntry { id = Ids[preset], displayName = Names[preset],
                        scenePath = scenePath, rules = rules, description = Descriptions[preset], briefing = Briefings[preset],
                        terrainId = surface != null ? surface.Id : "flat-ground", terrainVersion = surface != null ? surface.Version : 1, terrainSurface = surface };
                }
                catalog.templates = templates.ToArray(); AssetDatabase.CreateAsset(catalog, CatalogPath);
                AssetDatabase.SaveAssets();
                // Opening scenes can unload source assets. Finish authoring first, then reload
                // each serialized dependency after the scene transition instead of holding stale wrappers.
                for (int preset = 0; preset < Ids.Length; preset++)
                {
                    bool terrain = preset == 1, standard = preset == 4;
                    string folder = Root + "/" + Ids[preset], scenePath = folder + "/WarSandbox.unity";
                    var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                    var rules = Load<WarSandboxBattlefieldConfig>(folder + "/Rules.asset");
                    var manager = Object.FindFirstObjectByType<MassEngineManager>();
                    manager.scenarioConfig = Load<ScenarioConfig>(folder + "/Scenario.asset"); manager.battleStarted = false;
                    var controller = WarSandboxRuntimeBootstrap.EnsureControls(manager);
                    controller.pauseOnStart = true; controller.battlefieldConfig = rules;
                    var camera = manager.cullingCamera;
                    Vector3 position = terrain ? new Vector3(-95, 235, -280) : standard ? new Vector3(0, 260, -220) :
                        preset == 2 ? new Vector3(35, 165, -165) : new Vector3(0, 120, -130);
                    Vector3 target = terrain ? new Vector3(-75, 24, 0) : preset == 2 ? new Vector3(35, 0, 0) : new Vector3(0, 0, 20);
                    camera.transform.position = position; camera.transform.LookAt(target);
                    if (manager.lodCenter != null && manager.lodCenter != camera.transform) manager.lodCenter.position = position;
                    if (!WarSandboxDeploymentDraft.TryCapture(manager.scenarioConfig, out var draft, out string error)) throw new BuildFailedException(error);
                    var simulation = manager.systemConfig.simulationConfig;
                    if (!draft.TryValidate(simulation.simulationWorldSize - Vector2.one * simulation.boundaryPadding * 2,
                        rules.rules, out error)) throw new BuildFailedException(Ids[preset] + ": " + error);
                    if (terrain)
                    {
                        if (!manager.TryGetTerrainContext(out _, out var navigation, out error)) throw new BuildFailedException(error);
                        foreach (var unit in manager.scenarioConfig.unitTypes)
                        {
                            var p = unit.spawnConfig.spawnCenter; var size = unit.spawnConfig.ResolveSpawnSize();
                            if (!navigation.IsFootprintWalkable(new Vector2(p.x, p.z), new Vector2(size.x, size.z))) throw new BuildFailedException("Invalid mountain footprint.");
                        }
                    }
                    EditorSceneManager.SaveScene(scene);
                }
                catalog = Load<WarSandboxBattlefieldCatalog>(CatalogPath);
                if (!catalog.TryValidate(p => Load<SceneAsset>(p) != null, out var catalogError) || !catalog.TryValidateTemplates(out catalogError))
                    throw new BuildFailedException(catalogError);
                if (!AssetDatabase.CopyAsset(WarSandboxEntryBuilder.MenuScenePath, MenuScene)) throw new BuildFailedException("Menu copy failed.");
                var menu = EditorSceneManager.OpenScene(MenuScene, OpenSceneMode.Single);
                var session = Object.FindFirstObjectByType<WarSandboxSceneSession>();
                session.catalog = Load<WarSandboxBattlefieldCatalog>(CatalogPath); session.enterDefaultOnStart = true;
                new GameObject("Launch Preset Validation (opt-in)").AddComponent<WarSandboxTerrainCycle>().session = session;
                EditorSceneManager.SaveScene(menu); AssetDatabase.SaveAssets();
                Debug.Log("M71_CONTENT_READY " + CatalogPath);
            }
            finally
            {
                if (setup.Length > 0) EditorSceneManager.RestoreSceneManagerSetup(setup);
                else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
        }

        private static Vector3 Center(int preset, int i)
        {
            if (preset == 2) return new Vector3(i < 2 ? (i == 0 ? -55 : -75) : (i == 2 ? 150 : 170), 0, 0);
            if (preset == 3)
                return i >= 4 ? new Vector3(0, 0, i == 4 ? 58 : 86) : new Vector3(i < 2 ? (i == 0 ? -45 : -65) : (i == 2 ? 45 : 65), 0, -30);
            return new Vector3(i < 2 ? (i == 0 ? -48 : -68) : (i == 2 ? 48 : 68), 0, 0);
        }

        [MenuItem("MassEngine/Launch Presets/Build M7.1 Windows")]
        public static void BuildWindows() => BuildWindows("Builds/M71LaunchPresets");

        [MenuItem("MassEngine/Launch Presets/Build M7.2 Windows")]
        public static void BuildM72Windows() => BuildWindows("Builds/M72Playability");

        [MenuItem("MassEngine/Launch Presets/Build M7.3 Windows")]
        public static void BuildM73Windows() => BuildWindows("Builds/M73Stability");

        [MenuItem("MassEngine/Launch Presets/Build M7.3 Budget Windows")]
        public static void BuildM73BudgetWindows() => BuildWindows("Builds/M73Budget");

        [MenuItem("MassEngine/Launch Presets/Build M7.3 Desktop Measurement")]
        public static void BuildM73DesktopWindows() => BuildWindows("Builds/M73Desktop", true);

        [MenuItem("MassEngine/Launch Presets/Build M7.3 GPU Breakdown")]
        public static void BuildM73GpuBreakdownWindows() => BuildWindows("Builds/M73GpuBreakdown", true, "gpu-breakdown");

        [MenuItem("MassEngine/Launch Presets/Build M7.3 Far LOD Windows")]
        public static void BuildM73FarLodWindows() => BuildWindows("Builds/M73FarLod", true);

        // New reviewed binaries always use an unused path; never overwrite an earlier candidate.
        [MenuItem("MassEngine/Launch Presets/Build Reviewed Windows (new folder)")]
        public static void BuildReviewedWindows()
        {
            string label = Environment.GetEnvironmentVariable("WAR_SANDBOX_REVIEW_BUILD");
            if (string.IsNullOrEmpty(label)) label = "M7MotionReview-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            if (!System.Text.RegularExpressions.Regex.IsMatch(label, @"^M7MotionReview-[A-Za-z0-9-]+$"))
                throw new BuildFailedException("Invalid reviewed build folder name.");
            string output = Path.Combine("Builds", label);
            if (Directory.Exists(output) || File.Exists(output)) throw new BuildFailedException("Refusing to replace " + output);
            BuildWindows(output);
        }

        private static void BuildWindows(string outputPath, bool frameTiming = false, string measurementPhase = "desktop-performance")
        {
            var catalog = Load<WarSandboxBattlefieldCatalog>(CatalogPath);
            if (!catalog.TryValidate(p => Load<SceneAsset>(p) != null, out var error) || !catalog.TryValidateTemplates(out error)) throw new BuildFailedException(error);
            string[] scenes = new[] { MenuScene }.Concat(catalog.entries.Select(e => e.scenePath)).Distinct().ToArray();
            var previous = EditorBuildSettings.scenes;
            bool previousTiming = PlayerSettings.enableFrameTimingStats;
            string output = Path.GetFullPath(outputPath); Directory.CreateDirectory(output);
            try
            {
                EditorBuildSettings.scenes = scenes.Select(p => new EditorBuildSettingsScene(p, true)).ToArray();
                if (frameTiming) PlayerSettings.enableFrameTimingStats = true;
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = scenes, target = BuildTarget.StandaloneWindows64,
                    locationPathName = Path.Combine(output, "WarSandbox.exe"), options = BuildOptions.Development });
                if (report.summary.result != BuildResult.Succeeded) throw new BuildFailedException("Launch Windows build failed.");
            }
            finally { EditorBuildSettings.scenes = previous; PlayerSettings.enableFrameTimingStats = previousTiming; }
            File.WriteAllText(Path.Combine(output, "Start-WarSandbox.cmd"), "@echo off\r\ncd /d \"%~dp0\"\r\nstart \"\" \"WarSandbox.exe\" -screen-fullscreen 0 -screen-width 1280 -screen-height 720\r\n", new UTF8Encoding(false));
            if (frameTiming) File.WriteAllText(Path.Combine(output, measurementPhase == "gpu-breakdown" ? "Measure-GpuBreakdown.cmd" : "Measure-Desktop.cmd"),
                "@echo off\r\ncd /d \"%~dp0\"\r\nset \"RUN_ROOT=%~dp0DesktopMeasurements\\%RANDOM%-%RANDOM%\"\r\nmkdir \"%RUN_ROOT%\"\r\n" +
                "start \"\" /wait \"WarSandbox.exe\" -screen-fullscreen 0 -screen-width 1280 -screen-height 720 --terrain-cycle --terrain-cycle-phase=" + measurementPhase + " --terrain-cycle-output=\"%RUN_ROOT%\\report\" --war-sandbox-settings-file=\"%RUN_ROOT%\\preferences\\settings.json\" -logFile \"%RUN_ROOT%\\player.log\"\r\n" +
                "echo Report: %RUN_ROOT%\r\npause\r\n", new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(output, "说明.txt"), "首次进入512人开阔对冲，按Enter开战、Space暂停。配兵布阵可编辑并保存自己的方案。\r\n底部战场目录可选山地绕行、中央据点、三方混编，另有10万人标准规模选项。\r\n右键+WASD移动镜头，滚轮缩放；选择军团后A进攻、H防守、M再点击地面移动。\r\n设置可调整音效音量和静音，保存后下次启动保留；打开设置会暂停正在进行的战斗。\r\n结算显示全部参战军团的初始、存活和损失；可以主动结束本局，不判胜负。\r\n", Encoding.UTF8);
        }

        public static void PrepareAndBuild() { Prepare(); BuildWindows(); }
        public static void ImportPreviewsAndBuild()
        {
            var catalog = Load<WarSandboxBattlefieldCatalog>(CatalogPath);
            foreach (var entry in catalog.entries)
            {
                string source = Path.Combine("Logs/M71Validation/presets", entry.id + "-setup.png");
                string target = Root + "/Previews/" + entry.id + ".png";
                if (File.Exists(target)) throw new BuildFailedException("Refusing to overwrite preview: " + target);
                File.Copy(source, target); AssetDatabase.ImportAsset(target);
                var importer = (TextureImporter)AssetImporter.GetAtPath(target);
                importer.mipmapEnabled = false; importer.maxTextureSize = 1024; importer.SaveAndReimport();
                entry.preview = Load<Texture2D>(target);
            }
            EditorUtility.SetDirty(catalog); AssetDatabase.SaveAssets(); BuildWindows();
        }
        private static T Load<T>(string path) where T : Object => AssetDatabase.LoadAssetAtPath<T>(path) ?? throw new BuildFailedException("Missing asset: " + path);
    }
}
