using NUnit.Framework;
using UnityEngine;

namespace MassEngine.Game.Tests
{
    public sealed class WarSandboxScenarioPresetTests
    {
        private GameObject root;
        private MassEngineManager manager;
        private WarSandboxBattleController controller;
        private WarSandboxScenarioPreset preset;
        private ScenarioConfig scenario;
        private MassEngineSystemConfig system;
        private UnitTypeConfig unitType;
        private SpawnConfig spawn;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("ScenarioPresetTests");
            root.SetActive(false);
            manager = root.AddComponent<MassEngineManager>();
            controller = root.AddComponent<WarSandboxBattleController>();
            controller.manager = manager;

            spawn = ScriptableObject.CreateInstance<SpawnConfig>();
            unitType = ScriptableObject.CreateInstance<UnitTypeConfig>();
            unitType.spawnConfig = spawn;
            scenario = ScriptableObject.CreateInstance<ScenarioConfig>();
            scenario.unitTypes = new[] { unitType };

            system = ScriptableObject.CreateInstance<MassEngineSystemConfig>();
            system.simulationConfig = ScriptableObject.CreateInstance<SimulationConfig>();
            system.runtimeFlowConfig = ScriptableObject.CreateInstance<RuntimeFlowConfig>();
            system.runtimeCombatConfig = ScriptableObject.CreateInstance<RuntimeCombatConfig>();
            manager.scenarioConfig = scenario;
            manager.systemConfig = system;
            preset = ScriptableObject.CreateInstance<WarSandboxScenarioPreset>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(preset);
            Object.DestroyImmediate(system.runtimeCombatConfig);
            Object.DestroyImmediate(system.runtimeFlowConfig);
            Object.DestroyImmediate(system.simulationConfig);
            Object.DestroyImmediate(system);
            Object.DestroyImmediate(scenario);
            Object.DestroyImmediate(unitType);
            Object.DestroyImmediate(spawn);
            Object.DestroyImmediate(root);
        }

        [Test]
        public void CaptureAndApplyRoundTripsCompleteBattlefieldIntent()
        {
            unitType.teamId = 1;
            spawn.unitCount = 54321;
            spawn.spawnCenter = new Vector3(25f, 0f, -18f);
            spawn.formationDensity = 0.7f;
            spawn.formationAspect = 3f;
            spawn.spawnSize = new Vector3(80f, 0f, 120f);

            system.simulationConfig.simulationWorldSize = new Vector2(900f, 700f);
            system.simulationConfig.cellSize = 3f;
            system.simulationConfig.maxAgentsPerCell = 96;
            system.runtimeFlowConfig.flowFieldResolution = 256;
            system.runtimeFlowConfig.flowFieldOrigin = new Vector2(-450f, -350f);
            system.runtimeFlowConfig.defenderFlowFieldEnabled = true;
            system.runtimeFlowConfig.dynamicDefenderFlowSectorCount = 7;
            system.runtimeCombatConfig.defenderGuardRadius = 12f;

            controller.pauseOnStart = false;
            controller.gameMode = WarSandboxGameMode.ControlPoint;
            controller.controlPointCenter = new Vector3(12f, 0f, 34f);
            controller.controlPointRadius = 45f;
            controller.controlPointCaptureSeconds = 33f;
            controller.staticObstaclesEnabled = true;
            controller.useCustomStaticObstacleLayout = true;
            controller.staticObstacleClearance = 4f;
            controller.staticObstacles = new[]
            {
                new StaticObstacleRect(new Vector2(10f, 20f), new Vector2(14f, 60f))
            };

            Assert.That(preset.CaptureFrom(manager, controller), Is.True);

            unitType.teamId = 0;
            spawn.unitCount = 1;
            spawn.spawnCenter = Vector3.zero;
            system.simulationConfig.simulationWorldSize = Vector2.one;
            system.simulationConfig.cellSize = 1f;
            system.runtimeFlowConfig.flowFieldResolution = 16;
            system.runtimeFlowConfig.defenderFlowFieldEnabled = false;
            system.runtimeCombatConfig.defenderGuardRadius = 0f;
            controller.gameMode = WarSandboxGameMode.Annihilation;
            controller.controlPointCenter = Vector3.zero;
            controller.staticObstacles[0] = default;

            Assert.That(preset.ApplyTo(manager, controller), Is.True);
            Assert.That(unitType.teamId, Is.EqualTo(1));
            Assert.That(spawn.unitCount, Is.EqualTo(54321));
            Assert.That(spawn.spawnCenter, Is.EqualTo(new Vector3(25f, 0f, -18f)));
            Assert.That(spawn.formationDensity, Is.EqualTo(0.7f));
            Assert.That(spawn.formationAspect, Is.EqualTo(3f));
            Assert.That(spawn.spawnSize, Is.EqualTo(new Vector3(80f, 0f, 120f)));
            Assert.That(system.simulationConfig.simulationWorldSize, Is.EqualTo(new Vector2(900f, 700f)));
            Assert.That(system.simulationConfig.cellSize, Is.EqualTo(3f));
            Assert.That(system.simulationConfig.maxAgentsPerCell, Is.EqualTo(96));
            Assert.That(system.runtimeFlowConfig.flowFieldResolution, Is.EqualTo(256));
            Assert.That(system.runtimeFlowConfig.flowFieldOrigin, Is.EqualTo(new Vector2(-450f, -350f)));
            Assert.That(system.runtimeFlowConfig.defenderFlowFieldEnabled, Is.True);
            Assert.That(system.runtimeFlowConfig.dynamicDefenderFlowSectorCount, Is.EqualTo(7));
            Assert.That(system.runtimeCombatConfig.defenderGuardRadius, Is.EqualTo(12f));
            Assert.That(controller.pauseOnStart, Is.False);
            Assert.That(controller.gameMode, Is.EqualTo(WarSandboxGameMode.ControlPoint));
            Assert.That(controller.controlPointCenter, Is.EqualTo(new Vector3(12f, 0f, 34f)));
            Assert.That(controller.controlPointRadius, Is.EqualTo(45f));
            Assert.That(controller.controlPointCaptureSeconds, Is.EqualTo(33f));
            Assert.That(controller.staticObstaclesEnabled, Is.True);
            Assert.That(controller.useCustomStaticObstacleLayout, Is.True);
            Assert.That(controller.staticObstacleClearance, Is.EqualTo(4f));
            Assert.That(controller.staticObstacles[0].center, Is.EqualTo(new Vector2(10f, 20f)));
            Assert.That(controller.staticObstacles[0].size, Is.EqualTo(new Vector2(14f, 60f)));
        }

        [Test]
        public void CaptureOwnsAnIndependentObstacleArray()
        {
            controller.staticObstacles = new[]
            {
                new StaticObstacleRect(Vector2.zero, new Vector2(10f, 20f))
            };

            Assert.That(preset.CaptureFrom(manager, controller), Is.True);
            controller.staticObstacles[0] = new StaticObstacleRect(Vector2.one, Vector2.one);

            Assert.That(preset.staticObstacles[0].center, Is.EqualTo(Vector2.zero));
            Assert.That(preset.staticObstacles[0].size, Is.EqualTo(new Vector2(10f, 20f)));
        }
    }
}
