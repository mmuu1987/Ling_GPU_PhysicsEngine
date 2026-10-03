using System.Collections.Generic;
using MassEngine.Game.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MassEngine.Game.Tests
{
    public sealed class WarSandboxBattlefieldTests
    {
        private readonly List<Object> objects = new List<Object>();
        private GameObject root;
        private MassEngineManager manager;
        private WarSandboxBattleController controller;
        private string assetFolder;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("Battlefield Rule Tests"); root.SetActive(false);
            manager = root.AddComponent<MassEngineManager>();
            controller = root.AddComponent<WarSandboxBattleController>(); controller.manager = manager;
            manager.scenarioConfig = Create<ScenarioConfig>();
            manager.systemConfig = Create<MassEngineSystemConfig>();
            manager.systemConfig.simulationConfig = Create<SimulationConfig>();
            manager.systemConfig.runtimeFlowConfig = Create<RuntimeFlowConfig>();
            var a = Create<UnitTypeConfig>(); a.spawnConfig = Create<SpawnConfig>(); a.spawnConfig.unitCount = 100;
            var b = Create<UnitTypeConfig>(); b.spawnConfig = Create<SpawnConfig>(); b.spawnConfig.unitCount = 100; b.teamId = 1;
            a.spawnConfig.spawnCenter = new Vector3(-50, 0, 0); b.spawnConfig.spawnCenter = new Vector3(50, 0, 0);
            manager.scenarioConfig.unitTypes = new[] { a, b };
            controller.RebuildArmyStates();
        }

        [TearDown]
        public void TearDown()
        {
            Undo.ClearAll(); Time.timeScale = 1f;
            Object.DestroyImmediate(root);
            if (assetFolder != null) AssetDatabase.DeleteAsset(assetFolder);
            foreach (Object item in objects) if (item != null) Object.DestroyImmediate(item);
            objects.Clear(); assetFolder = null;
        }

        [Test]
        public void DefaultsAndBoundaryValuesAreValid()
        {
            var config = Create<WarSandboxBattlefieldConfig>();
            Assert.That(config.TryCreateSnapshot(out var snapshot, out var error), Is.True, error);
            Assert.That(snapshot.gameMode, Is.EqualTo(WarSandboxGameMode.Annihilation));
            Assert.That(snapshot.staticObstacles, Is.Empty);
            config.rules.controlPointRadius = 2;
            config.rules.controlPointCaptureSeconds = 5;
            config.rules.staticObstacleClearance = 0;
            config.rules.staticObstacles = new StaticObstacleRect[StaticObstacleMath.MaxObstacleCount];
            for (int i = 0; i < config.rules.staticObstacles.Length; i++) config.rules.staticObstacles[i] = Wall(i);
            Assert.That(config.TryCreateSnapshot(out _, out error), Is.True, error);
            config.rules.staticObstacles = null;
            Assert.That(config.TryCreateSnapshot(out snapshot, out error), Is.True, error);
            Assert.That(snapshot.staticObstacles, Is.Empty);
        }

        [TestCase("mode", "gameMode")]
        [TestCase("center-x", "controlPointCenter")]
        [TestCase("center-y", "controlPointCenter")]
        [TestCase("center-z", "controlPointCenter")]
        [TestCase("radius-min", "controlPointRadius")]
        [TestCase("radius-nan", "controlPointRadius")]
        [TestCase("radius-inf", "controlPointRadius")]
        [TestCase("capture-min", "controlPointCaptureSeconds")]
        [TestCase("capture-nan", "controlPointCaptureSeconds")]
        [TestCase("capture-inf", "controlPointCaptureSeconds")]
        [TestCase("clearance-min", "staticObstacleClearance")]
        [TestCase("clearance-nan", "staticObstacleClearance")]
        [TestCase("clearance-inf", "staticObstacleClearance")]
        [TestCase("count", "staticObstacles")]
        [TestCase("wall-center", "staticObstacles[0]")]
        [TestCase("wall-size", "staticObstacles[0]")]
        [TestCase("wall-min", "staticObstacles[0]")]
        public void InvalidFieldsAreReportedEvenWhenObstaclesAreDisabled(string field, string expected)
        {
            var config = Create<WarSandboxBattlefieldConfig>();
            config.rules.staticObstacles = new[] { Wall(0) };
            switch (field)
            {
                case "mode": config.rules.gameMode = (WarSandboxGameMode)99; break;
                case "center-x": config.rules.controlPointCenter.x = float.NaN; break;
                case "center-y": config.rules.controlPointCenter.y = float.PositiveInfinity; break;
                case "center-z": config.rules.controlPointCenter.z = float.NegativeInfinity; break;
                case "radius-min": config.rules.controlPointRadius = 1.99f; break;
                case "radius-nan": config.rules.controlPointRadius = float.NaN; break;
                case "radius-inf": config.rules.controlPointRadius = float.PositiveInfinity; break;
                case "capture-min": config.rules.controlPointCaptureSeconds = 4.99f; break;
                case "capture-nan": config.rules.controlPointCaptureSeconds = float.NaN; break;
                case "capture-inf": config.rules.controlPointCaptureSeconds = float.PositiveInfinity; break;
                case "clearance-min": config.rules.staticObstacleClearance = -1; break;
                case "clearance-nan": config.rules.staticObstacleClearance = float.NaN; break;
                case "clearance-inf": config.rules.staticObstacleClearance = float.PositiveInfinity; break;
                case "count": config.rules.staticObstacles = new StaticObstacleRect[9]; break;
                case "wall-center": config.rules.staticObstacles[0].center.y = float.NaN; break;
                case "wall-size": config.rules.staticObstacles[0].size.y = float.PositiveInfinity; break;
                case "wall-min": config.rules.staticObstacles[0].size.x = 0.01f; break;
            }
            Assert.That(config.TryCreateSnapshot(out _, out string error), Is.False);
            Assert.That(error, Does.Contain(expected));
        }

        [Test]
        public void CaptureResolvesBuiltInLayoutEvenWhenDisabledAndCopiesArrays()
        {
            controller.staticObstacles = new[] { Wall(1000) };
            controller.useCustomStaticObstacleLayout = false; controller.staticObstaclesEnabled = false;
            var captured = controller.CaptureBattlefieldRules();
            Assert.That(captured.staticObstacles.Length, Is.EqualTo(2));
            Assert.That(captured.staticObstacles[0].center, Is.EqualTo(new Vector2(0, -90)));
            captured.staticObstacles[0] = Wall(999);
            Assert.That(controller.CaptureBattlefieldRules().staticObstacles[0].center, Is.EqualTo(new Vector2(0, -90)));
            controller.useCustomStaticObstacleLayout = true;
            captured = controller.CaptureBattlefieldRules(); captured.staticObstacles[0] = Wall(-50);
            Assert.That(controller.staticObstacles[0].center.x, Is.EqualTo(1000));
        }

        [Test]
        public void DefinitionsSwitchWithoutSharingLiveOrRestartArrays()
        {
            var a = Create<WarSandboxBattlefieldConfig>(); var b = ControlPointRules();
            string originalA = JsonUtility.ToJson(a); string originalB = JsonUtility.ToJson(b);
            Assert.That(controller.TryApplyBattlefieldConfig(a, out var error), Is.True, error);
            Assert.That(manager.StaticObstacleCount, Is.Zero);
            Assert.That(controller.TryApplyBattlefieldConfig(b, out error), Is.True, error);
            Assert.That(controller.gameMode, Is.EqualTo(WarSandboxGameMode.ControlPoint));
            Assert.That(manager.StaticObstacleCount, Is.EqualTo(1));
            controller.staticObstacles[0] = Wall(999);
            controller.SetGameMode(WarSandboxGameMode.Annihilation);
            controller.SetStaticObstaclesEnabled(false);
            controller.StartDefaultBattle(); controller.SetSimulationSpeed(2);
            controller.ResetBattle();
            Assert.That(controller.Phase, Is.EqualTo(WarSandboxBattlePhase.Setup));
            Assert.That(controller.SimulationSpeed, Is.EqualTo(1));
            Assert.That(controller.GetArmy(0).hasOrder, Is.False);
            Assert.That(controller.gameMode, Is.EqualTo(b.rules.gameMode));
            Assert.That(controller.staticObstacles[0].center, Is.EqualTo(b.rules.staticObstacles[0].center));
            Assert.That(controller.controlPointRadius, Is.EqualTo(24));
            Assert.That(manager.StaticObstacleCount, Is.EqualTo(1));
            Assert.That(JsonUtility.ToJson(a), Is.EqualTo(originalA));
            Assert.That(JsonUtility.ToJson(b), Is.EqualTo(originalB));
            b.rules.staticObstacles[0] = Wall(600);
            controller.ResetBattle();
            Assert.That(controller.staticObstacles[0].center.x, Is.EqualTo(0), "Restart uses its captured definition, not a mutated source.");
            Assert.That(controller.TryApplyBattlefieldConfig(a, out error), Is.True, error);
            Assert.That(manager.StaticObstacleCount, Is.Zero);
            controller.ResetBattle(); Assert.That(controller.gameMode, Is.EqualTo(WarSandboxGameMode.Annihilation));
        }

        [Test]
        public void InvalidOrMidBattleApplicationLeavesAllLiveStateUnchanged()
        {
            var a = ControlPointRules(); var b = Create<WarSandboxBattlefieldConfig>();
            Assert.That(controller.TryApplyBattlefieldConfig(a, out _), Is.True);
            string before = JsonUtility.ToJson(controller);
            b.rules.staticObstacles = new[] { default(StaticObstacleRect) };
            Assert.That(controller.TryApplyBattlefieldConfig(b, out _), Is.False);
            Assert.That(controller.TryApplyBattlefieldConfig(null, out _), Is.False);
            Assert.That(JsonUtility.ToJson(controller), Is.EqualTo(before));
            b.rules = WarSandboxBattlefieldRules.Default;
            controller.StartDefaultBattle(); before = JsonUtility.ToJson(controller);
            Assert.That(controller.TryApplyBattlefieldConfig(b, out _), Is.False);
            Assert.That(JsonUtility.ToJson(controller), Is.EqualTo(before));
            controller.PauseBattle();
            Assert.That(controller.TryApplyBattlefieldConfig(b, out _), Is.False);
        }

        [Test]
        public void UnboundResetRetainsLegacySetupChoices()
        {
            controller.SetGameMode(WarSandboxGameMode.ControlPoint); controller.controlPointRadius = 43;
            controller.SetStaticObstaclesEnabled(true);
            controller.StartDefaultBattle(); controller.ResetBattle();
            Assert.That(controller.battlefieldConfig, Is.Null);
            Assert.That(controller.gameMode, Is.EqualTo(WarSandboxGameMode.ControlPoint));
            Assert.That(controller.controlPointRadius, Is.EqualTo(43));
            Assert.That(controller.useCustomStaticObstacleLayout, Is.False);
            Assert.That(controller.GetStaticObstacleCount(), Is.EqualTo(2));
        }

        [Test]
        public void InvalidAssignedRulesBlockEveryStartPathAndCanBeCorrected()
        {
            var config = ControlPointRules(); config.rules.controlPointRadius = float.NaN;
            controller.battlefieldConfig = config;
            Assert.That(controller.StartDefaultBattle(), Is.False);
            Assert.That(controller.IssueOrder(ArmyOrder.Attack(0)), Is.False);
            Assert.That(controller.IssueMoveOrder(0, Vector3.zero, false), Is.False);
            controller.StartOrResumeBattle();
            Assert.That(manager.IsBattleRunning, Is.False);
            Assert.That(controller.Phase, Is.EqualTo(WarSandboxBattlePhase.Setup));
            Assert.That(controller.GetArmy(0).hasOrder, Is.False);
            Assert.That(controller.GetMoveRoutePointCount(0), Is.Zero);
            Assert.That(controller.BattlefieldRuleError, Does.Contain("controlPointRadius"));
            config.rules.controlPointRadius = 24;
            Assert.That(controller.StartDefaultBattle(), Is.True);
            Assert.That(controller.BattlefieldRuleError, Is.Null);
        }

        [Test]
        public void EditorLoadAndCaptureEachUndoAsOneOperationWithoutTouchingDeployment()
        {
            var a = Create<WarSandboxBattlefieldConfig>(); var b = ControlPointRules(); Persist(a, "A"); Persist(b, "B");
            Assert.That(WarSandboxBattlefieldEditor.TryApply(controller, a, out _), Is.True);
            Undo.FlushUndoRecordObjects(); Undo.ClearAll();
            string before = EditorJsonUtility.ToJson(controller);
            string deployment = EditorJsonUtility.ToJson(manager.scenarioConfig);
            Assert.That(WarSandboxBattlefieldEditor.TryApply(controller, b, out _), Is.True);
            Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
            Assert.That(EditorJsonUtility.ToJson(controller), Is.EqualTo(before));
            Undo.PerformRedo(); Assert.That(controller.battlefieldConfig, Is.SameAs(b));
            Assert.That(controller.staticObstacles, Is.Not.SameAs(b.rules.staticObstacles));
            Undo.ClearAll(); before = EditorJsonUtility.ToJson(a);
            Assert.That(WarSandboxBattlefieldEditor.TryCapture(controller, a, out _), Is.True);
            Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
            Assert.That(controller.battlefieldConfig, Is.SameAs(b));
            Assert.That(EditorJsonUtility.ToJson(a), Is.EqualTo(before));
            Assert.That(EditorJsonUtility.ToJson(manager.scenarioConfig), Is.EqualTo(deployment));
            Undo.PerformRedo(); Assert.That(controller.battlefieldConfig, Is.SameAs(a));
            Assert.That(a.rules.gameMode, Is.EqualTo(WarSandboxGameMode.ControlPoint));
        }

        [Test]
        public void FailedEditorCaptureAndLoadDoNotPartiallyWrite()
        {
            var config = ControlPointRules(); Persist(config, "Rules");
            controller.controlPointCaptureSeconds = 2;
            string before = EditorJsonUtility.ToJson(controller) + EditorJsonUtility.ToJson(config);
            Assert.That(WarSandboxBattlefieldEditor.TryCapture(controller, config, out _), Is.False);
            Assert.That(EditorJsonUtility.ToJson(controller) + EditorJsonUtility.ToJson(config), Is.EqualTo(before));
            config.rules.staticObstacles[0].size = Vector2.zero;
            before = EditorJsonUtility.ToJson(controller);
            Assert.That(WarSandboxBattlefieldEditor.TryApply(controller, config, out _), Is.False);
            Assert.That(EditorJsonUtility.ToJson(controller), Is.EqualTo(before));
        }

        [Test]
        public void RulesRoundTripThroughDiskRetainsDisabledLayout()
        {
            var config = ControlPointRules(); Persist(config, "Rules");
            controller.useCustomStaticObstacleLayout = false;
            controller.staticObstaclesEnabled = false; controller.controlPointRadius = 42;
            Assert.That(WarSandboxBattlefieldEditor.TryCapture(controller, config, out _), Is.True);
            AssetDatabase.SaveAssets(); Undo.ClearAll();
            string path = AssetDatabase.GetAssetPath(config);
            controller.battlefieldConfig = null;
            Resources.UnloadAsset(config);
            var loaded = AssetDatabase.LoadAssetAtPath<WarSandboxBattlefieldConfig>(path);
            Assert.That(loaded.rules.controlPointRadius, Is.EqualTo(42));
            Assert.That(loaded.rules.staticObstaclesEnabled, Is.False);
            Assert.That(loaded.rules.staticObstacles.Length, Is.EqualTo(2));
            Assert.That(WarSandboxBattlefieldEditor.TryApply(controller, loaded, out _), Is.True);
            Assert.That(controller.staticObstacles[0].center, Is.EqualTo(new Vector2(0, -90)));
        }

        [Test]
        public void ExplicitSceneControlsCanBeCreatedUndoneAndRedone()
        {
            Object.DestroyImmediate(controller); controller = null;
            Assert.That(WarSandboxBattlefieldEditor.TryEnsureController(manager, out controller, out var error), Is.True, error);
            var hud = root.GetComponent<WarSandboxCommandHUD>();
            Assert.That(controller.manager, Is.SameAs(manager)); Assert.That(hud.controller, Is.SameAs(controller));
            Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
            Assert.That(root.GetComponent<WarSandboxBattleController>(), Is.Null);
            Assert.That(root.GetComponent<WarSandboxCommandHUD>(), Is.Null);
            Undo.PerformRedo();
            controller = root.GetComponent<WarSandboxBattleController>();
            Assert.That(controller.manager, Is.SameAs(manager));
            Assert.That(root.GetComponent<WarSandboxCommandHUD>().controller, Is.SameAs(controller));
        }

        private T Create<T>() where T : ScriptableObject
        {
            T item = ScriptableObject.CreateInstance<T>(); objects.Add(item); return item;
        }

        [TestCase("BattlefieldRules_A_Annihilation")]
        [TestCase("BattlefieldRules_B_ControlPoint")]
        public void ExamplesLeaveControlPointAndDefaultSpawnsClearInsideWorld(string assetName)
        {
            var config = AssetDatabase.LoadAssetAtPath<WarSandboxBattlefieldConfig>("Assets/Game/Settings/" + assetName + ".asset");
            var scenario = AssetDatabase.LoadAssetAtPath<ScenarioConfig>("Assets/Game/Settings/ScenarioConfig.asset");
            var simulation = AssetDatabase.LoadAssetAtPath<SimulationConfig>("Assets/Game/Settings/SimulationConfig.asset");
            Assert.That(config, Is.Not.Null); Assert.That(scenario, Is.Not.Null); Assert.That(simulation, Is.Not.Null);
            Assert.That(config.TryCreateSnapshot(out var rules, out var error), Is.True, error);
            Vector2 half = simulation.simulationWorldSize * 0.5f;
            Vector2 point = new Vector2(rules.controlPointCenter.x, rules.controlPointCenter.z);
            Assert.That(Mathf.Abs(point.x) + rules.controlPointRadius, Is.LessThan(half.x));
            Assert.That(Mathf.Abs(point.y) + rules.controlPointRadius, Is.LessThan(half.y));
            foreach (StaticObstacleRect obstacle in rules.staticObstacles)
            {
                Rect bounds = obstacle.Bounds;
                bounds.xMin -= rules.staticObstacleClearance; bounds.xMax += rules.staticObstacleClearance;
                bounds.yMin -= rules.staticObstacleClearance; bounds.yMax += rules.staticObstacleClearance;
                Assert.That(bounds.xMin, Is.GreaterThan(-half.x)); Assert.That(bounds.xMax, Is.LessThan(half.x));
                Assert.That(bounds.yMin, Is.GreaterThan(-half.y)); Assert.That(bounds.yMax, Is.LessThan(half.y));
                var nearest = new Vector2(Mathf.Clamp(point.x, bounds.xMin, bounds.xMax), Mathf.Clamp(point.y, bounds.yMin, bounds.yMax));
                Assert.That(Vector2.Distance(nearest, point), Is.GreaterThan(rules.controlPointRadius));
                foreach (UnitTypeConfig unit in scenario.unitTypes)
                {
                    var spawn = unit.spawnConfig; Vector3 size = spawn.ResolveSpawnSize();
                    Rect footprint = new Rect(new Vector2(spawn.spawnCenter.x - size.x * 0.5f, spawn.spawnCenter.z - size.z * 0.5f), new Vector2(size.x, size.z));
                    Assert.That(bounds.Overlaps(footprint), Is.False, unit.name + " spawns inside an obstacle.");
                }
            }
        }

        [Test]
        public void ExistingControllerCanReceiveAHudWithoutChangingRules()
        {
            controller.controlPointRadius = 52;
            Assert.That(WarSandboxBattlefieldEditor.TryEnsureController(manager, out var result, out var error), Is.True, error);
            Assert.That(result, Is.SameAs(controller)); Assert.That(result.controlPointRadius, Is.EqualTo(52));
            Assert.That(root.GetComponents<WarSandboxBattleController>().Length, Is.EqualTo(1));
            Assert.That(root.GetComponent<WarSandboxCommandHUD>().controller, Is.SameAs(controller));
            Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
            Assert.That(root.GetComponent<WarSandboxCommandHUD>(), Is.Null);
            Assert.That(root.GetComponent<WarSandboxBattleController>(), Is.SameAs(controller));
        }
        private void Persist(Object item, string name)
        {
            if (assetFolder == null)
            {
                string folderName = "__BattlefieldTest_" + System.Guid.NewGuid().ToString("N");
                AssetDatabase.CreateFolder("Assets", folderName); assetFolder = "Assets/" + folderName;
            }
            AssetDatabase.CreateAsset(item, assetFolder + "/" + name + ".asset");
        }
        private WarSandboxBattlefieldConfig ControlPointRules()
        {
            var config = Create<WarSandboxBattlefieldConfig>();
            config.rules.gameMode = WarSandboxGameMode.ControlPoint;
            config.rules.controlPointCenter = new Vector3(3, 0, 4);
            config.rules.controlPointRadius = 24; config.rules.controlPointCaptureSeconds = 15;
            config.rules.staticObstaclesEnabled = true; config.rules.staticObstacles = new[] { Wall(0) };
            return config;
        }
        private static StaticObstacleRect Wall(float x) => new StaticObstacleRect(new Vector2(x, 60), new Vector2(14, 30));
    }
}
