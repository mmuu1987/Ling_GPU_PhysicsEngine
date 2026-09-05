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

            UnitTypeConfig attacker = CreateUnitType("AttackerUnitConfig", "Attacker Sword", 0, null);
            UnitTypeConfig defender = CreateUnitType("DefenderUnitConfig", "Defender Sword", 1, null);
            // A third army so the sandbox's N-team support is visible on first Play. The HUD,
            // minimap and telemetry all iterate the roster; with only two teams every one of
            // those paths looked correct while still being hardcoded to attacker/defender.
            UnitTypeConfig thirdArmy = CreateUnitType("ThirdArmyUnitConfig", "Third Army Sword", 2, attacker.spawnConfig);

            ScenarioConfig scenario = LoadOrCreate<ScenarioConfig>("ScenarioConfig", out bool scenarioCreated);
            if (scenarioCreated || scenario.unitTypes == null || scenario.unitTypes.Length == 0)
            {
                scenario.unitTypes = new[] { attacker, defender, thirdArmy };
                EditorUtility.SetDirty(scenario);
            }

            MassEngineSystemConfig system = CreateSystemConfig(out SimulationConfig simulation, out bool simulationCreated, out RuntimeFlowConfig flowConfig, out bool flowCreated, out LodConfig lodConfig);
            MassEngineShaderConfig shaders = CreateShaderConfig();

            // A fresh sample must pass the engine's own physics ledger on first Play:
            // fit NEWLY created world/flow configs with the same suggestions the
            // Auto-Fit menu uses. Existing (user-tuned) assets stay untouched.
            if (simulationCreated || flowCreated)
            {
                ScenarioPhysicsReport fit = ScenarioPhysics.Evaluate(scenario.unitTypes, simulation, flowConfig, lodConfig);
                if (simulationCreated)
                {
                    simulation.simulationWorldSize = fit.SuggestedWorldSize;
                    simulation.cellSize = fit.SuggestedCellSize;
                    simulation.maxAgentsPerCell = fit.SuggestedMaxAgentsPerCell;
                    EditorUtility.SetDirty(simulation);
                }
                if (flowCreated && flowConfig != null)
                {
                    flowConfig.flowFieldResolution = fit.SuggestedFlowResolution;
                    flowConfig.flowFieldCellSize = fit.SuggestedFlowCellSize;
                    flowConfig.flowFieldOrigin = fit.SuggestedFlowOrigin;
                    EditorUtility.SetDirty(flowConfig);
                }

                ScenarioPhysicsReport check = ScenarioPhysics.Evaluate(scenario.unitTypes, simulation, flowConfig, lodConfig);
                if (check.HasIssues)
                    Debug.LogWarning("Sample scenario still flags ledger issues after auto-fit:\n" + check.Describe());
            }

            // The scene file is the only destructive write in this menu: overwriting a
            // scene the user has decorated must be an explicit choice.
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(SampleScenePath) != null && !Application.isBatchMode &&
                !EditorUtility.DisplayDialog(
                    "WarSandboxSample.unity already exists",
                    "Rebuilding will overwrite the sample scene and discard any manual edits in it. Config assets are never overwritten either way.",
                    "Rebuild Scene",
                    "Keep Existing Scene"))
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log("MassEngine sample configs ensured; existing scene kept at " + SampleScenePath);
                return;
            }

            CreateScene(scenario, system, shaders);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("MassEngine sample scene written to " + SampleScenePath + ". Assign VAT profiles / materials on the RenderConfig assets to see units rendered.");
        }

        /// <param name="mainArmySpawn">
        /// A main army's spawn, needed only by team 2 and up to clear the front line's Z extent.
        /// Null for teams 0 and 1, which are placed from their own footprint alone.
        /// </param>
        private static UnitTypeConfig CreateUnitType(string assetName, string displayName, int teamId, SpawnConfig mainArmySpawn)
        {
            SpawnConfig spawn = LoadOrCreate<SpawnConfig>(assetName + "_Spawn", out bool spawnCreated);
            if (spawnCreated)
            {
                ApplySampleDeployment(spawn, teamId, mainArmySpawn);
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

        /// <summary>
        /// Where one sample army starts. Teams 0/1 face each other along X with centers derived
        /// from the auto footprint (depth along X): half the formation depth plus an engagement
        /// gap keeps hostile spawn rects from overlapping - hardcoded centers violated the
        /// physics ledger the moment the footprint grew past them.
        ///
        /// Team 2 and up deploy along +Z instead, as a smaller force pressing in from the north.
        /// A ring layout would be the general answer, but spawn rects are axis-aligned: placed on
        /// a ring without rotating the footprint to face the middle they overlap each other.
        /// </summary>
        private static void ApplySampleDeployment(SpawnConfig spawn, int teamId, SpawnConfig mainArmySpawn)
        {
            const float engagementGap = 15f;
            const int mainArmyUnitCount = 10000;

            if (teamId <= 1 || mainArmySpawn == null)
            {
                spawn.unitCount = mainArmyUnitCount;
                float sideSign = teamId == 0 ? -1f : 1f;
                Vector3 footprint = spawn.ResolveSpawnSize();
                spawn.spawnCenter = new Vector3(sideSign * (footprint.x * 0.5f + engagementGap), 0f, 0f);
                return;
            }

            // Wide and shallow, so it reads as a flanking third force rather than a third block.
            spawn.unitCount = Mathf.Max(1, mainArmyUnitCount / 4);
            spawn.formationAspect = 4f;
            float ownDepth = spawn.ResolveSpawnSize().x;
            float frontLineHalf = mainArmySpawn.ResolveSpawnSize().z * 0.5f;
            // Stacked further out for each extra team so a fourth army does not land on the third.
            float lane = teamId - 2;
            spawn.spawnCenter = new Vector3(
                0f,
                0f,
                frontLineHalf + engagementGap + ownDepth * (0.5f + lane) + lane * engagementGap);
        }

        private static MassEngineSystemConfig CreateSystemConfig(out SimulationConfig simulation, out bool simulationCreated, out RuntimeFlowConfig flow, out bool flowCreated, out LodConfig lod)
        {
            MassEngineSystemConfig system = LoadOrCreate<MassEngineSystemConfig>("MassEngineSystemConfig", out bool created);
            simulation = LoadOrCreate<SimulationConfig>("SimulationConfig", out simulationCreated);
            flow = LoadOrCreate<RuntimeFlowConfig>("RuntimeFlowConfig", out flowCreated);
            lod = LoadOrCreate<LodConfig>("LodConfig", out _);
            if (created || system.simulationConfig == null)
            {
                system.simulationConfig = simulation;
                system.lodConfig = lod;
                system.runtimeFlowConfig = flow;
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
            manager.battleStarted = false;

            ClickFlowTargetSetter clickSetter = systemObject.AddComponent<ClickFlowTargetSetter>();
            clickSetter.manager = manager;
            systemObject.AddComponent<BattleTelemetryHUD>().manager = manager;
            WarSandboxBattleController battleController = systemObject.AddComponent<WarSandboxBattleController>();
            battleController.manager = manager;
            battleController.pauseOnStart = true;
            WarSandboxCommandHUD commandHud = systemObject.AddComponent<WarSandboxCommandHUD>();
            commandHud.controller = battleController;

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
            commandHud.commandCamera = camera;

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
