using System;
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
    public static class WarSandboxTerrainPlayableBuilder
    {
        public const string Root = "Assets/Game/M62TerrainPlayable";
        public const string BattleScene = Root + "/WarSandboxTerrain.unity";
        public const string MenuScene = Root + "/TerrainMenu.unity";

        [MenuItem("MassEngine/Terrain Prototype/Create M6.2 Playable")]
        public static void Prepare()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode first.");
            if (Directory.Exists(Root)) throw new InvalidOperationException("Refusing to overwrite existing terrain content: " + Root);
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var setup = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                AssetDatabase.CreateFolder("Assets/Game", "M62TerrainPlayable");
                AssetDatabase.CreateFolder(Root, "Settings");
                var source = Load<ScenarioConfig>("Assets/Game/Settings/ScenarioConfig.asset");
                var scenario = ScriptableObject.CreateInstance<ScenarioConfig>();
                scenario.unitTypes = new UnitTypeConfig[source.unitTypes.Length];
                Vector3[] centers = { new Vector3(-282, 0, -12), new Vector3(-282, 0, 12),
                    new Vector3(-95, 0, -8), new Vector3(-95, 0, 8), new Vector3(132, 0, 0), new Vector3(150, 0, 0) };
                if (centers.Length != scenario.unitTypes.Length) throw new BuildFailedException("Expected six source formations.");
                for (int i = 0; i < scenario.unitTypes.Length; i++)
                {
                    var unit = Object.Instantiate(source.unitTypes[i]);
                    unit.name = "Terrain Formation " + i;
                    var spawn = Object.Instantiate(unit.spawnConfig);
                    spawn.unitCount = 64; spawn.spawnCenter = centers[i]; spawn.spawnSize = new Vector3(12, 0, 8);
                    AssetDatabase.CreateAsset(spawn, Root + "/Settings/Spawn" + i + ".asset");
                    unit.spawnConfig = spawn;
                    AssetDatabase.CreateAsset(unit, Root + "/Settings/Unit" + i + ".asset");
                    scenario.unitTypes[i] = unit;
                }
                AssetDatabase.CreateAsset(scenario, Root + "/Settings/Scenario.asset");
                var system = Object.Instantiate(Load<MassEngineSystemConfig>("Assets/Game/Settings/SystemConfig.asset"));
                var flow = Object.Instantiate(system.runtimeFlowConfig);
                flow.defenderFlowFieldEnabled = true; flow.runtimeDynamicDefenderFlowEnabled = true;
                AssetDatabase.CreateAsset(flow, Root + "/Settings/Flow.asset");
                system.runtimeFlowConfig = flow;
                AssetDatabase.CreateAsset(system, Root + "/Settings/System.asset");

                CopyNew("Assets/Game/Scenes/WarSandbox.unity", BattleScene);
                var battle = EditorSceneManager.OpenScene(BattleScene, OpenSceneMode.Single);
                var manager = Object.FindFirstObjectByType<MassEngineManager>();
                if (manager == null) throw new BuildFailedException("Battlefield manager missing.");
                manager.scenarioConfig = Load<ScenarioConfig>(Root + "/Settings/Scenario.asset");
                manager.systemConfig = Load<MassEngineSystemConfig>(Root + "/Settings/System.asset");
                manager.terrainSurfaceAsset = Load<TerrainSurfaceAsset>(WarSandboxTerrainPrototypeBuilder.Root + "/Surface.asset");
                manager.battleStarted = false;
                if (!manager.TryGetTerrainContext(out _, out var navigation, out var error)) throw new BuildFailedException(error);
                foreach (var unit in manager.scenarioConfig.unitTypes)
                {
                    Vector3 center = unit.spawnConfig.spawnCenter, size = unit.spawnConfig.ResolveSpawnSize();
                    if (!navigation.IsFootprintWalkable(new Vector2(center.x, center.z), new Vector2(size.x, size.z)))
                        throw new BuildFailedException("Terrain recipe has an invalid footprint: " + unit.name);
                }
                var plane = GameObject.Find("Plane");
                if (plane != null) plane.SetActive(false);
                var ground = new GameObject("Continuous Terrain");
                ground.AddComponent<MeshFilter>().sharedMesh = Load<Mesh>(WarSandboxTerrainPrototypeBuilder.Root + "/SurfaceMesh.asset");
                ground.AddComponent<MeshRenderer>().sharedMaterial = Load<Material>(WarSandboxTerrainPrototypeBuilder.Root + "/SurfaceMaterial.mat");
                ground.AddComponent<MeshCollider>().sharedMesh = ground.GetComponent<MeshFilter>().sharedMesh;
                var camera = manager.cullingCamera;
                camera.transform.position = new Vector3(-95, 235, -280);
                camera.transform.LookAt(new Vector3(-75, 24, 0));
                camera.farClipPlane = 2400;
                EditorSceneManager.SaveScene(battle);

                scenario = Load<ScenarioConfig>(Root + "/Settings/Scenario.asset");
                var surface = Load<TerrainSurfaceAsset>(WarSandboxTerrainPrototypeBuilder.Root + "/Surface.asset");
                var catalog = ScriptableObject.CreateInstance<WarSandboxBattlefieldCatalog>();
                catalog.defaultEntryId = "mountain-battle";
                catalog.entries = new[] {
                    new WarSandboxBattlefieldEntry { id = "mountain-battle", displayName = "山地三军 · 上下坡会战", scenePath = BattleScene,
                        terrainId = surface.Id, terrainVersion = surface.Version, terrainSurface = surface,
                        rules = Load<WarSandboxBattlefieldConfig>("Assets/Game/Settings/BattlefieldRules_A_Annihilation.asset") },
                    new WarSandboxBattlefieldEntry { id = "open-battle", displayName = "原始平面 · 11万单位", scenePath = "Assets/Game/Scenes/WarSandbox.unity",
                        rules = Load<WarSandboxBattlefieldConfig>("Assets/Game/Settings/BattlefieldRules_A_Annihilation.asset") }
                };
                catalog.templates = scenario.unitTypes.Select((unit, i) => new WarSandboxUnitTemplateEntry
                    { templateId = "m62-unit-" + i, revision = 1, config = unit }).ToArray();
                AssetDatabase.CreateAsset(catalog, Root + "/Settings/Catalog.asset");
                CopyNew(WarSandboxEntryBuilder.MenuScenePath, MenuScene);
                var menu = EditorSceneManager.OpenScene(MenuScene, OpenSceneMode.Single);
                var session = Object.FindFirstObjectByType<WarSandboxSceneSession>();
                session.catalog = Load<WarSandboxBattlefieldCatalog>(Root + "/Settings/Catalog.asset");
                session.enterDefaultOnStart = true;
                var smoke = new GameObject("M6.2 Terrain Smoke (opt-in)").AddComponent<WarSandboxTerrainPlayableSmoke>();
                smoke.session = session;
                EditorSceneManager.SaveScene(menu);
                Debug.Log("M62_PLAYABLE_READY " + MenuScene);
            }
            finally
            {
                if (setup.Length > 0) EditorSceneManager.RestoreSceneManagerSetup(setup);
                else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
        }

        [MenuItem("MassEngine/Terrain Prototype/Build M6.2 Windows Playable")]
        public static void BuildWindows() => BuildPlayer("Builds/M62TerrainPlayable");

        [MenuItem("MassEngine/Terrain Prototype/Build M6.3 Windows Validation")]
        public static void BuildM63Windows()
        {
            var setup = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                var menu = EditorSceneManager.OpenScene(MenuScene, OpenSceneMode.Single);
                var cycle = Object.FindFirstObjectByType<WarSandboxTerrainCycle>();
                if (cycle == null) cycle = new GameObject("M6.3 Terrain Cycle (opt-in)").AddComponent<WarSandboxTerrainCycle>();
                cycle.session = Object.FindFirstObjectByType<WarSandboxSceneSession>();
                EditorSceneManager.SaveScene(menu);
            }
            finally
            {
                if (setup.Length > 0) EditorSceneManager.RestoreSceneManagerSetup(setup);
                else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
            BuildPlayer("Builds/M63TerrainValidation");
        }

        private static void BuildPlayer(string outputDirectory)
        {
            var catalog = Load<WarSandboxBattlefieldCatalog>(Root + "/Settings/Catalog.asset");
            if (!catalog.TryValidate(p => Load<SceneAsset>(p) != null, out var error) || !catalog.TryValidateTemplates(out error))
                throw new BuildFailedException(error);
            string[] scenes = new[] { MenuScene, BattleScene, "Assets/Game/Scenes/WarSandbox.unity" };
            var savedScenes = EditorBuildSettings.scenes;
            string output = Path.GetFullPath(outputDirectory);
            Directory.CreateDirectory(output);
            try
            {
                EditorBuildSettings.scenes = scenes.Select(p => new EditorBuildSettingsScene(p, true)).ToArray();
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = scenes,
                    locationPathName = Path.Combine(output, "TerrainBattle.exe"), target = BuildTarget.StandaloneWindows64, options = BuildOptions.Development });
                if (report.summary.result != BuildResult.Succeeded) throw new BuildFailedException("M6.2 build failed.");
            }
            finally { EditorBuildSettings.scenes = savedScenes; }
            File.WriteAllText(Path.Combine(output, "Start-TerrainBattle.cmd"), "@echo off\r\ncd /d \"%~dp0\"\r\nstart \"\" \"TerrainBattle.exe\" -screen-fullscreen 0 -screen-width 1280 -screen-height 720\r\n", new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(output, "说明.txt"), "M6.2 山地可玩整合：三军各128人，复用现有近战/远程兵种。\r\n开战后红军由西坡上山，蓝军驻高地，绿军从东侧窄坡进入。可选军团并右键下达移动，Shift追加路点。\r\n红褐色为陡坡或禁行区，部署与命令会校验可通行性。支持原有布阵和本地方案界面。\r\n右键+WASD移动镜头，滚轮缩放。返回目录可进入原始11万单位平面战场。\r\nM6.3完整对局/换场/性能验收另行记录；本包不表示V1完成。\r\n", Encoding.UTF8);
            Debug.Log("M62_BUILD_READY " + output);
        }

        private static T Load<T>(string path) where T : Object => AssetDatabase.LoadAssetAtPath<T>(path) ?? throw new BuildFailedException("Missing asset: " + path);
        private static void CopyNew(string source, string target)
        { if (File.Exists(target) || !AssetDatabase.CopyAsset(source, target)) throw new BuildFailedException("Could not create: " + target); }
    }
}
