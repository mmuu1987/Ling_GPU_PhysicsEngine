using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace MassEngine.Game.Tests
{
    public sealed class ControlPointDefaultOrderTests
    {
        private GameObject root;
        private MassEngineManager manager;
        private WarSandboxBattleController controller;
        private ScenarioConfig scenario;
        private MassEngineSystemConfig system;
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("ControlPointDefaultOrderTests");
            root.SetActive(false);
            manager = root.AddComponent<MassEngineManager>();
            controller = root.AddComponent<WarSandboxBattleController>();
            controller.manager = manager;
            controller.staticObstaclesEnabled = false;
            scenario = ScriptableObject.CreateInstance<ScenarioConfig>();
            system = ScriptableObject.CreateInstance<MassEngineSystemConfig>();
            system.runtimeFlowConfig = ScriptableObject.CreateInstance<RuntimeFlowConfig>();
            manager.scenarioConfig = scenario;
            manager.systemConfig = system;
            SetArmies(3);
            controller.controlPointCenter = new Vector3(5, 0, 10);
            Assert.That(controller.SetGameMode(WarSandboxGameMode.ControlPoint), Is.True);
        }

        private void SetArmies(int count)
        {
            if (scenario.unitTypes != null)
                foreach (var old in scenario.unitTypes)
                {
                    Object.DestroyImmediate(old.spawnConfig);
                    Object.DestroyImmediate(old);
                }
            scenario.unitTypes = new UnitTypeConfig[count];
            for (int team = 0; team < count; team++)
            {
                var unit = ScriptableObject.CreateInstance<UnitTypeConfig>();
                unit.teamId = team;
                unit.spawnConfig = ScriptableObject.CreateInstance<SpawnConfig>();
                unit.spawnConfig.unitCount = 64;
                unit.spawnConfig.spawnCenter = new Vector3((team - 1) * 20, 0, team == 2 ? 30 : -10);
                scenario.unitTypes[team] = unit;
            }
            typeof(MassEngineManager).GetMethod("EnsureTeamFlowState", PrivateInstance).Invoke(manager, new object[] { count });
            controller.RebuildArmyStates();
        }

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1;
            if (root != null) Object.DestroyImmediate(root);
            if (scenario != null)
            {
                foreach (var unit in scenario.unitTypes)
                {
                    if (unit == null) continue;
                    Object.DestroyImmediate(unit.spawnConfig);
                    Object.DestroyImmediate(unit);
                }
                Object.DestroyImmediate(scenario);
            }
            if (system != null)
            {
                Object.DestroyImmediate(system.runtimeFlowConfig);
                Object.DestroyImmediate(system);
            }
        }

        private TeamStance Stance(int team) => (TeamStance)typeof(MassEngineManager)
            .GetMethod("ResolveTeamStance", PrivateInstance).Invoke(manager, new object[] { team });

        private TeamFlowFrameSettings Flow(int team) => (TeamFlowFrameSettings)typeof(MassEngineManager)
            .GetMethod("BuildTeamFlowSettings", PrivateInstance, null, new[] { typeof(int), typeof(int), typeof(int) }, null)
            .Invoke(manager, new object[] { team, 1, 256 });

        [TestCase(2)]
        [TestCase(3)]
        public void DefaultCaptureRetainsPointTargetButAllowsCombatForEveryArmy(int count)
        {
            SetArmies(count);
            string configBefore = JsonUtility.ToJson(system.runtimeFlowConfig);
            Assert.That(controller.StartDefaultBattle(), Is.True);
            for (int team = 0; team < count; team++)
            {
                Assert.That(Stance(team), Is.EqualTo(TeamStance.Advance), "Default capture must not use explicit MoveOnly.");
                var flow = Flow(team);
                Assert.That(flow.enabled, Is.True);
                Assert.That(flow.targetMode, Is.EqualTo(1), "Combat must not replace the point objective with enemy-density goals.");
                Assert.That(flow.targetPoint, Is.EqualTo(controller.controlPointCenter));
                Assert.That(controller.GetArmy(team).currentOrder.type, Is.EqualTo(ArmyOrderType.Move));
                Assert.That(controller.GetMoveRoutePointCount(team), Is.EqualTo(1));
            }
            Assert.That(JsonUtility.ToJson(system.runtimeFlowConfig), Is.EqualTo(configBefore), "Do not write configuration assets.");
        }

        [TestCase(0, ArmyOrderType.Move, TeamStance.MoveOnly)]
        [TestCase(1, ArmyOrderType.Move, TeamStance.MoveOnly)]
        [TestCase(2, ArmyOrderType.Move, TeamStance.MoveOnly)]
        [TestCase(0, ArmyOrderType.Retreat, TeamStance.MoveOnly)]
        [TestCase(1, ArmyOrderType.Retreat, TeamStance.MoveOnly)]
        [TestCase(2, ArmyOrderType.Retreat, TeamStance.MoveOnly)]
        [TestCase(0, ArmyOrderType.Hold, TeamStance.HoldHere)]
        [TestCase(1, ArmyOrderType.Hold, TeamStance.HoldHere)]
        [TestCase(2, ArmyOrderType.Hold, TeamStance.HoldHere)]
        [TestCase(0, ArmyOrderType.Attack, TeamStance.Advance)]
        [TestCase(1, ArmyOrderType.Attack, TeamStance.Advance)]
        [TestCase(2, ArmyOrderType.Attack, TeamStance.Advance)]
        public void ExplicitOrderReplacesOnlyItsArmiesDefaultDoctrine(int team, ArmyOrderType order, TeamStance expected)
        {
            Assert.That(controller.StartDefaultBattle(), Is.True);
            bool accepted;
            if (order == ArmyOrderType.Move) accepted = controller.IssueMoveOrder(team, new Vector3(-10, 0, 15), false);
            else if (order == ArmyOrderType.Retreat) accepted = controller.IssueOrder(ArmyOrder.Retreat(team));
            else if (order == ArmyOrderType.Hold) accepted = controller.IssueOrder(ArmyOrder.Hold(team));
            else accepted = controller.IssueOrder(ArmyOrder.Attack(team));
            Assert.That(accepted, Is.True);
            Assert.That(Stance(team), Is.EqualTo(expected));
            for (int other = 0; other < 3; other++)
                if (other != team) Assert.That(Stance(other), Is.EqualTo(TeamStance.Advance), "Do not change another army's doctrine.");
            var flow = Flow(team);
            if (order == ArmyOrderType.Move || order == ArmyOrderType.Retreat)
            {
                Assert.That(flow.targetMode, Is.EqualTo(1));
                Assert.That(flow.dynamicFlowEnabled, Is.False);
            }
            if (order == ArmyOrderType.Attack) Assert.That(flow.targetMode, Is.Zero);
            if (order == ArmyOrderType.Hold) Assert.That(flow.enabled, Is.False);
        }

        [Test]
        public void MissingTargetMoveDoesNotEraseTheDefaultObjectiveOrCombatDoctrine()
        {
            Assert.That(controller.StartDefaultBattle(), Is.True);
            Assert.That(controller.IssueOrder(new ArmyOrder { teamId = 0, type = ArmyOrderType.Move, hasTarget = false }), Is.False);
            Assert.That(Stance(0), Is.EqualTo(TeamStance.Advance));
            Assert.That(Flow(0).targetPoint, Is.EqualTo(controller.controlPointCenter));
            Assert.That(controller.GetMoveRoutePointCount(0), Is.EqualTo(1));
        }

        [Test]
        public void DefaultAnnihilationStillUsesDynamicAttackWithoutAPointOverride()
        {
            Assert.That(controller.SetGameMode(WarSandboxGameMode.Annihilation), Is.True);
            Assert.That(controller.StartDefaultBattle(), Is.True);
            for (int team = 0; team < 3; team++)
            {
                Assert.That(Stance(team), Is.EqualTo(TeamStance.Advance));
                Assert.That(Flow(team).targetMode, Is.Zero);
                Assert.That(Flow(team).dynamicFlowEnabled, Is.True);
                Assert.That(controller.GetArmy(team).currentOrder.type, Is.EqualTo(ArmyOrderType.Attack));
            }
        }
    }
}
