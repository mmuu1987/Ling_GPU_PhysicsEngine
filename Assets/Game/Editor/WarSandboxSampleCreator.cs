using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MassEngine.Game.Editor
{
    /// <summary>
    /// Creates a runnable sample setup. Non-destructive: existing assets are loaded and
    /// left untouched; only NEWLY created assets receive sample values. The sample scene
    /// is written to its own path and the current scene is saved via the standard
    /// "save modified scenes?" prompt first.
    /// </summary>
    public static class WarSandboxSampleCreator
    {
        private const string SettingsDirectory = "Assets/Game/Settings";
        private const string SceneDirectory = "Assets/Game/Scenes";
        private const string SampleScenePath = SceneDirectory + "/WarSandboxSample.unity";

        [MenuItem("MassEngine/Create Sample Configs And Scene")]
        public static void CreateSampleConfigsAndScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            EnsureDirectory(SettingsDirectory);
            EnsureDirectory(SceneDirectory);

            UnitTypeConfig attacker = CreateUnitType("AttackerUnitConfig", "Attacker Sword", 0, new Vector3(-45f, 0f, 0f));
            UnitTypeConfig defender = CreateUnitType("DefenderUnitConfig", "Defender Sword", 1, new Vector3(35f, 0f, 0f));

            ScenarioConfig scenario = LoadOrCreate<ScenarioConfig>("ScenarioConfig", out bool scenarioCreated);
            if (scenarioCreated || scenario.unitTypes == null || scenario.unitTypes.Length == 0)
            {
                scenario.unitTypes = new[] { attacker, defender };
                EditorUtility.SetDirty(scenario);
            }

            MassEngineSystemConfig system = CreateSystemConfig();
            MassEngineShaderConfig shaders = CreateShaderConfig();

            CreateScene(scenario, system, shaders);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("MassEngine sample scene written to " + SampleScenePath + ". Assign VAT profiles / materials on the RenderConfig assets to see units rendered.");
        }

        private static UnitTypeConfig CreateUnitType(string assetName, string displayName, int teamId, Vector3 spawnCenter)
        {
            SpawnConfig spawn = LoadOrCreate<SpawnConfig>(assetName + "_Spawn", out bool spawnCreated);
            if (spawnCreated)
            {
                spawn.unitCount = 10000;
                spawn.spawnCenter = spawnCenter;
                // footprint auto-derives from unitCount / formationDensity
                EditorUtility.SetDirty(spawn);
            }

            MovementConfig movement = LoadOrCreate<MovementConfig>(assetName + "_Movement", out _);
            FlockingConfig flocking = LoadOrCreate<FlockingConfig>(assetName + "_Flocking", out _);
            AnimationConfig animation = LoadOrCreate<AnimationConfig>(assetName + "_Animation", out _);
            CombatConfig combat = LoadOrCreate<CombatConfig>(assetName + "_Combat", out _);
            RenderConfig render = LoadOrCreate<RenderConfig>(assetName + "_Render", out _);

            UnitTypeConfig unitType = LoadOrCreate<UnitTypeConfig>(assetName, out bool unitCreated);
            if (unitCreated)
            {
                unitType.unitTypeName = displayName;
                unitType.teamId = teamId;
                unitType.unitTypeClassName = "MassEngine.DefaultSwordUnit";
                unitType.spawnConfig = spawn;
                unitType.movementConfig = movement;
                unitType.flockingConfig = flocking;
                unitType.animationConfig = animation;
                unitType.combatConfig = combat;
                unitType.renderConfig = render;
                EditorUtility.SetDirty(unitType);
            }

            return unitType;
        }

        private static MassEngineSystemConfig CreateSystemConfig()
        {
            MassEngineSystemConfig system = LoadOrCreate<MassEngineSystemConfig>("MassEngineSystemConfig", out bool created);
            if (created || system.simulationConfig == null)
            {
                system.simulationConfig = LoadOrCreate<SimulationConfig>("SimulationConfig", out _);
                system.lodConfig = LoadOrCreate<LodConfig>("LodConfig", out _);
                system.runtimeFlowConfig = LoadOrCreate<RuntimeFlowConfig>("RuntimeFlowConfig", out _);
                system.runtimeCombatConfig = LoadOrCreate<RuntimeCombatConfig>("RuntimeCombatConfig", out _);
                EditorUtility.SetDirty(system);
            }

            return system;
        }

        private static MassEngineShaderConfig CreateShaderConfig()
        {
            MassEngineShaderConfig shaders = LoadOrCreate<MassEngineShaderConfig>("MassEngineShaderConfig", out bool created);
            if (created || shaders.spatialHashShader == null)
            {
                shaders.spatialHashShader = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/MassEngine/Spatial/Shaders/AgentSpatialHash.compute");
                shaders.runtimeFlowShader = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/MassEngine/FlowField/Shaders/AgentRuntimeFlow.compute");
                shaders.combatSimulationShader = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/MassEngine/Simulation/Shaders/AgentCombatSimulation.compute");
                shaders.lodClassificationShader = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/MassEngine/VatRender/Shaders/AgentLodClassification.compute");
                EditorUtility.SetDirty(shaders);
            }

            return shaders;
        }

        private static void CreateScene(ScenarioConfig scenario, MassEngineSystemConfig system, MassEngineShaderConfig shaders)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            GameObject systemObject = new GameObject("MassEngineSystem");
            MassEngineManager manager = systemObject.AddComponent<MassEngineManager>();
            manager.scenarioConfig = scenario;
            manager.systemConfig = system;
            manager.shaderConfig = shaders;
            manager.enableGpuDispatch = true;
            manager.battleStarted = true;

            ClickFlowTargetSetter clickSetter = systemObject.AddComponent<ClickFlowTargetSetter>();
            clickSetter.manager = manager;
            systemObject.AddComponent<BattleTelemetryHUD>().manager = manager;

            // Ground with a collider so click-to-set-target raycasts have something to hit.
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(20f, 1f, 20f);

            GameObject cameraObject = new GameObject("MassEngine Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            cameraObject.transform.position = new Vector3(0f, 80f, -90f);
            cameraObject.transform.rotation = Quaternion.Euler(60f, 0f, 0f);
            cameraObject.tag = "MainCamera";
            manager.cullingCamera = camera;
            manager.lodCenter = cameraObject.transform;
            clickSetter.raycastCamera = camera;

            EditorSceneManager.SaveScene(scene, SampleScenePath);
        }

        private static T LoadOrCreate<T>(string assetName, out bool created) where T : ScriptableObject
        {
            string path = SettingsDirectory + "/" + assetName + ".asset";
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null)
            {
                created = false;
                return asset;
            }

            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            created = true;
            return asset;
        }

        private static void EnsureDirectory(string assetDirectory)
        {
            // AssetDatabase-native creation: System.IO folders are invisible to
            // CreateAsset until the next refresh, which made the first run in a clean
            // project fail halfway and look random.
            if (AssetDatabase.IsValidFolder(assetDirectory))
                return;

            string parent = System.IO.Path.GetDirectoryName(assetDirectory).Replace('\\', '/');
            EnsureDirectory(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(assetDirectory));
        }
    }
}
