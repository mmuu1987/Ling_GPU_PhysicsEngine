using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MassEngine.Game.Tests
{
    public sealed class WarSandboxRuntimeDeploymentTests
    {
        private ScenarioConfig source;
        private UnitTypeConfig a, b;
        private GameObject root;
        private MassEngineManager manager;
        private WarSandboxBattleController controller;
        private WarSandboxRuntimeDeployment editor;
        private MassEngineSystemConfig system;

        [SetUp]
        public void SetUp()
        {
            source = ScriptableObject.CreateInstance<ScenarioConfig>();
            a = Unit("A", 0, new Vector3(-40, 0, 0)); b = Unit("B", 1, new Vector3(40, 0, 0));
            source.unitTypes = new[] { a, b };
            root = new GameObject("Runtime Deployment Tests"); root.SetActive(false);
            manager = root.AddComponent<MassEngineManager>(); manager.scenarioConfig = source; manager.enableGpuDispatch = true;
            system = ScriptableObject.CreateInstance<MassEngineSystemConfig>();
            system.simulationConfig = ScriptableObject.CreateInstance<SimulationConfig>();
            system.simulationConfig.simulationWorldSize = new Vector2(200, 200);
            system.runtimeFlowConfig = ScriptableObject.CreateInstance<RuntimeFlowConfig>();
            manager.systemConfig = system;
            controller = root.AddComponent<WarSandboxBattleController>(); controller.manager = manager; controller.RebuildArmyStates();
            editor = root.AddComponent<WarSandboxRuntimeDeployment>(); editor.controller = controller;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(root);
            foreach (var unit in source.unitTypes) { Object.DestroyImmediate(unit.spawnConfig); Object.DestroyImmediate(unit); }
            Object.DestroyImmediate(source);
            Object.DestroyImmediate(system.simulationConfig); Object.DestroyImmediate(system.runtimeFlowConfig); Object.DestroyImmediate(system);
            Time.timeScale = 1;
        }

        private static UnitTypeConfig Unit(string name, int team, Vector3 center)
        {
            var unit = ScriptableObject.CreateInstance<UnitTypeConfig>(); unit.name = name; unit.unitTypeName = name; unit.teamId = team;
            unit.spawnConfig = ScriptableObject.CreateInstance<SpawnConfig>(); unit.spawnConfig.unitCount = 100; unit.spawnConfig.spawnCenter = center;
            return unit;
        }

        private WarSandboxDeploymentDraft Draft()
        {
            Assert.That(WarSandboxDeploymentDraft.TryCapture(source, out var draft, out var error), Is.True, error);
            return draft;
        }

        [Test]
        public void SnapshotAndHistoryAreIndependentAndBranchingClearsRedo()
        {
            var draft = Draft(); var before = draft.Snapshot(); var e = draft[0]; e.count = 50;
            Assert.That(draft.Set(0, e), Is.True); before[1].count = 777;
            Assert.That(draft[1].count, Is.EqualTo(100)); Assert.That(a.spawnConfig.unitCount, Is.EqualTo(100));
            Assert.That(draft.Undo(), Is.True); Assert.That(draft[0].count, Is.EqualTo(100));
            Assert.That(draft.Redo(), Is.True); Assert.That(draft[0].count, Is.EqualTo(50));
            draft.Undo(); draft.RemoveArmy(1); Assert.That(draft.CanRedo, Is.False);
            draft.Undo(); Assert.That(draft.Count, Is.EqualTo(2));
        }

        [Test]
        public void AddAndRemoveArmiesKeepRawIdentityAndEnforceCompositionLimit()
        {
            var draft = Draft(); Assert.That(draft.NextArmyId(), Is.EqualTo(2));
            Assert.That(draft.Add(a, 2), Is.True); Assert.That(draft[2].count, Is.EqualTo(1000));
            Assert.That(draft.RemoveArmy(0), Is.True); Assert.That(draft[0].teamId, Is.EqualTo(1));
            Assert.That(draft.NextArmyId(), Is.Zero);
            while (draft.Count < WarSandboxDeploymentDraft.MaxCompositions) Assert.That(draft.Add(b, 3), Is.True);
            Assert.That(draft.Add(b, 3), Is.False);
        }

        [TestCase("overlap")]
        [TestCase("outside")]
        [TestCase("nan")]
        [TestCase("infinite")]
        [TestCase("count")]
        [TestCase("capacity")]
        [TestCase("density")]
        [TestCase("manual")]
        [TestCase("team")]
        public void InvalidDraftsAreRejectedWithoutMutatingAssets(string invalid)
        {
            var draft = Draft(); var e = draft[0];
            switch (invalid)
            {
                case "overlap": e.center = draft[1].center; break;
                case "outside": e.center.x = 99; break;
                case "nan": e.center.z = float.NaN; break;
                case "infinite": e.manualSize = new Vector3(float.PositiveInfinity, 0, 10); break;
                case "count": e.count = 0; break;
                case "capacity": e.count = WarSandboxDeploymentDraft.MaxTotalUnits + 1; break;
                case "density": e.density = 20; break;
                case "manual": e.manualSize = new Vector3(10, 0, 0); break;
                case "team": e.teamId = ConfigValidator.MaxTeamId + 1; break;
            }
            draft.Set(0, e);
            Assert.That(draft.TryValidate(new Vector2(200, 200), WarSandboxBattlefieldRules.Default, out var error), Is.False);
            Assert.That(error, Is.Not.Empty); Assert.That(a.spawnConfig.unitCount, Is.EqualTo(100));
            Assert.That(a.spawnConfig.spawnCenter.x, Is.EqualTo(-40));
        }

        [Test]
        public void ObstacleClearanceAndEmptyDeploymentAreValidated()
        {
            var draft = Draft(); var rules = WarSandboxBattlefieldRules.Default;
            rules.staticObstaclesEnabled = true; rules.staticObstacles = new[] { new StaticObstacleRect(new Vector2(-40, 0), new Vector2(2, 2)) };
            Assert.That(draft.TryValidate(new Vector2(200, 200), rules, out _), Is.False);
            rules.staticObstaclesEnabled = false;
            Assert.That(draft.TryValidate(new Vector2(200, 200), rules, out _), Is.True);
            draft.RemoveArmy(0); draft.RemoveArmy(1);
            Assert.That(draft.TryValidate(new Vector2(200, 200), rules, out _), Is.False);
        }

        [Test]
        public void InstancesOwnEveryMutableConfigEvenForRepeatedTemplates()
        {
            var entries = new[] { WarSandboxDeploymentEntry.From(a), WarSandboxDeploymentEntry.From(a) };
            entries[1].teamId = 2; entries[1].count = 50;
            var instance = new WarSandboxDeploymentInstance(entries);
            var clone = instance.Scenario; var first = clone.unitTypes[0]; var second = clone.unitTypes[1];
            Assert.That(clone, Is.Not.SameAs(source)); Assert.That(first, Is.Not.SameAs(a));
            Assert.That(first.spawnConfig, Is.Not.SameAs(a.spawnConfig)); Assert.That(first.spawnConfig, Is.Not.SameAs(second.spawnConfig));
            Assert.That(first.combatConfig, Is.SameAs(a.combatConfig));
            first.spawnConfig.unitCount = 1;
            Assert.That(second.spawnConfig.unitCount, Is.EqualTo(50)); Assert.That(a.spawnConfig.unitCount, Is.EqualTo(100));
            instance.Dispose(); instance.Dispose();
            Assert.That(clone == null, Is.True); Assert.That(first == null, Is.True); Assert.That(a != null, Is.True);
        }

        [Test]
        public void CancelAndInvalidApplyKeepTheLastDeploymentAndBlockBattleCommands()
        {
            Assert.That(editor.TryBeginEdit(false, out var error), Is.True, error);
            Assert.That(manager.enableGpuDispatch, Is.False);
            var e = editor.Draft[0]; e.center = editor.Draft[1].center; editor.Draft.Set(0, e);
            Assert.That(editor.TryApply(out error), Is.False); Assert.That(editor.IsEditing, Is.True);
            Assert.That(manager.scenarioConfig, Is.SameAs(source)); Assert.That(controller.StartDefaultBattle(), Is.False);
            Assert.That(controller.IssueOrder(ArmyOrder.Attack(0)), Is.False);
            controller.StartOrResumeBattle(); Assert.That(manager.IsBattleRunning, Is.False);
            Assert.That(editor.CancelEditing(), Is.True); Assert.That(manager.enableGpuDispatch, Is.True);
            Assert.That(manager.scenarioConfig, Is.SameAs(source)); Assert.That(a.spawnConfig.spawnCenter.x, Is.EqualTo(-40));
        }

        [Test]
        public void ApplyRestartAndReeditUseIndependentFullStrengthSnapshots()
        {
            editor.TryBeginEdit(false, out _); var e = editor.Draft[0]; e.count = 64; editor.Draft.Set(0, e);
            Assert.That(editor.TryApply(out var error), Is.True, error);
            var first = manager.scenarioConfig;
            Assert.That(first, Is.Not.SameAs(source)); Assert.That(first.unitTypes[0].spawnConfig.unitCount, Is.EqualTo(64));
            Assert.That(controller.StartDefaultBattle(), Is.True);
            Assert.That(editor.TryBeginEdit(false, out _), Is.False);
            controller.ResetBattle(); Assert.That(controller.GetArmy(0).initialUnitCount, Is.EqualTo(64));
            Assert.That(editor.TryBeginEdit(false, out error), Is.True, error);
            e = editor.Draft[0]; e.count = 32; editor.Draft.Set(0, e);
            Assert.That(editor.TryApply(out error), Is.True, error);
            Assert.That(first == null, Is.True); Assert.That(manager.scenarioConfig.unitTypes[0].spawnConfig.unitCount, Is.EqualTo(32));
            Assert.That(editor.Templates[0], Is.SameAs(a)); Assert.That(a.spawnConfig.unitCount, Is.EqualTo(100));
        }

        [Test]
        public void RuntimeValidationRespectsTheSimulationBoundaryPadding()
        {
            editor.TryBeginEdit(false, out _); var e = editor.Draft[0];
            e.center.x = 94; e.manualSize = new Vector3(10, 0, 20); editor.Draft.Set(0, e);
            Assert.That(editor.Draft.TryValidate(editor.WorldSize, controller.CaptureBattlefieldRules(), out _), Is.True);
            Assert.That(editor.TryValidate(out _), Is.False);
        }

        [Test]
        public void SharedSpawnGeometryMatchesRuntimePreview()
        {
            foreach (Vector3 manualSize in new[] { Vector3.zero, new Vector3(8, 2, 32) })
            {
                a.spawnConfig.spawnSize = manualSize;
                var entry = WarSandboxDeploymentEntry.From(a);
                Assert.That(entry.Size, Is.EqualTo(a.spawnConfig.ResolveSpawnSize()));
            }
        }

        [Test]
        public void ExistingMixedDeploymentPassesBothBattlefieldFootprintChecks()
        {
            var scenario = AssetDatabase.LoadAssetAtPath<ScenarioConfig>("Assets/Game/Settings/ScenarioConfig.asset");
            var simulation = AssetDatabase.LoadAssetAtPath<SimulationConfig>("Assets/Game/Settings/SimulationConfig.asset");
            Assert.That(WarSandboxDeploymentDraft.TryCapture(scenario, out var draft, out var error), Is.True, error);
            foreach (string name in new[] { "A_Annihilation", "B_ControlPoint" })
            {
                var config = AssetDatabase.LoadAssetAtPath<WarSandboxBattlefieldConfig>("Assets/Game/Settings/BattlefieldRules_" + name + ".asset");
                Assert.That(draft.TryValidate(simulation.simulationWorldSize, config.rules, out error), Is.True, error);
            }
        }
    }
}
