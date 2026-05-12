using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MassGPUPhysics.Stage7.Editor
{
    public static class Stage7SampleAssetCreator
    {
        private const string SettingsDirectory = "Assets/MassGPUPhysics_Stage7/Setting";
        private const string SceneDirectory = "Assets/MassGPUPhysics_Stage7/Scene";

        [MenuItem("MassGPUPhysics/Stage7/Create Sample Configs And Scene")]
        public static void CreateSampleConfigsAndScene()
        {
            EnsureDirectory(SettingsDirectory);
            EnsureDirectory(SceneDirectory);

            UnitTypeConfig attacker = CreateUnitType("Stage7AttackerUnitConfig", "Attacker Sword", 0, new Vector3(-45f, 0f, 0f));
            UnitTypeConfig defender = CreateUnitType("Stage7DefenderUnitConfig", "Defender Sword", 1, new Vector3(35f, 0f, 0f));
            ScenarioConfig_Stage7 scenario = LoadOrCreate<ScenarioConfig_Stage7>("Stage7ScenarioConfig");
            scenario.unitTypes = new[] { attacker, defender };
            EditorUtility.SetDirty(scenario);
            Stage7SystemConfig system = CreateSystemConfig();
            Stage7ShaderConfig shaders = CreateShaderConfig();

            CreateScene(scenario, system, shaders);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private static UnitTypeConfig CreateUnitType(string assetName, string displayName, int teamId, Vector3 spawnCenter)
        {
            SpawnConfig spawn = LoadOrCreate<SpawnConfig>(assetName + "_Spawn");
            spawn.unitCount = 50000;
            spawn.spawnCenter = spawnCenter;
            spawn.spawnSize = new Vector3(35f, 0f, 80f);

            MovementConfig movement = LoadOrCreate<MovementConfig>(assetName + "_Movement");
            FlockingConfig flocking = LoadOrCreate<FlockingConfig>(assetName + "_Flocking");
            AnimationConfig animation = LoadOrCreate<AnimationConfig>(assetName + "_Animation");
            CombatConfig combat = LoadOrCreate<CombatConfig>(assetName + "_Combat");
            RenderConfig render = LoadOrCreate<RenderConfig>(assetName + "_Render");

            UnitTypeConfig unitType = LoadOrCreate<UnitTypeConfig>(assetName);
            unitType.unitTypeName = displayName;
            unitType.teamId = teamId;
            unitType.unitTypeClassName = "MassGPUPhysics.Stage7.DefaultSwordUnit";
            unitType.spawnConfig = spawn;
            unitType.movementConfig = movement;
            unitType.flockingConfig = flocking;
            unitType.animationConfig = animation;
            unitType.combatConfig = combat;
            unitType.renderConfig = render;

            EditorUtility.SetDirty(spawn);
            EditorUtility.SetDirty(movement);
            EditorUtility.SetDirty(flocking);
            EditorUtility.SetDirty(animation);
            EditorUtility.SetDirty(combat);
            EditorUtility.SetDirty(render);
            EditorUtility.SetDirty(unitType);

            return unitType;
        }

        private static Stage7SystemConfig CreateSystemConfig()
        {
            Stage7SystemConfig system = LoadOrCreate<Stage7SystemConfig>("Stage7SystemConfig");
            system.simulationConfig = LoadOrCreate<SimulationConfig>("Stage7SimulationConfig");
            system.lodConfig = LoadOrCreate<LodConfig>("Stage7LodConfig");
            system.runtimeFlowConfig = LoadOrCreate<RuntimeFlowConfig>("Stage7RuntimeFlowConfig");
            system.runtimeCombatConfig = LoadOrCreate<RuntimeCombatConfig>("Stage7RuntimeCombatConfig");
            EditorUtility.SetDirty(system);
            EditorUtility.SetDirty(system.simulationConfig);
            EditorUtility.SetDirty(system.lodConfig);
            EditorUtility.SetDirty(system.runtimeFlowConfig);
            EditorUtility.SetDirty(system.runtimeCombatConfig);
            return system;
        }

        private static Stage7ShaderConfig CreateShaderConfig()
        {
            Stage7ShaderConfig shaders = LoadOrCreate<Stage7ShaderConfig>("Stage7ShaderConfig");
            shaders.spatialHashShader = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/MassGPUPhysics_Stage7/Shaders/AgentSpatialHash_Stage6.compute");
            shaders.runtimeFlowShader = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/MassGPUPhysics_Stage7/Shaders/AgentRuntimeFlow_Stage6.compute");
            shaders.combatSimulationShader = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/MassGPUPhysics_Stage7/Shaders/AgentCombatSimulation_Stage6.compute");
            shaders.lodClassificationShader = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/MassGPUPhysics_Stage7/Shaders/AgentLodClassification_Stage6.compute");
            EditorUtility.SetDirty(shaders);
            return shaders;
        }

        private static void CreateScene(ScenarioConfig_Stage7 scenario, Stage7SystemConfig system, Stage7ShaderConfig shaders)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject systemObject = new GameObject("MassGpuSystem_Stage7");
            MassGpuSystemManager_Stage7 manager = systemObject.AddComponent<MassGpuSystemManager_Stage7>();
            manager.scenarioConfig = scenario;
            manager.systemConfig = system;
            manager.shaderConfig = shaders;

            GameObject cameraObject = new GameObject("Stage7 Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            cameraObject.transform.position = new Vector3(0f, 80f, -90f);
            cameraObject.transform.rotation = Quaternion.Euler(60f, 0f, 0f);
            manager.cullingCamera = camera;
            manager.lodCenter = cameraObject.transform;

            EditorSceneManager.SaveScene(scene, SceneDirectory + "/Stage7_Test.unity");
        }

        private static T LoadOrCreate<T>(string assetName) where T : ScriptableObject
        {
            string path = SettingsDirectory + "/" + assetName + ".asset";
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null)
                return asset;

            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        private static void EnsureDirectory(string assetDirectory)
        {
            string fullPath = Path.Combine(Application.dataPath, assetDirectory.Substring("Assets/".Length));
            if (!Directory.Exists(fullPath))
                Directory.CreateDirectory(fullPath);
        }
    }
}
