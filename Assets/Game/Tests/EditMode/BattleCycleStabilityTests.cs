using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace MassEngine.Game.Tests
{
    // Stability contracts: repeat start/end/restart cycles and verify no state leakage.
    // All tests use inactive-root CPU-only fixtures; no GPU buffers are created.
    public sealed class BattleCycleStabilityTests
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
            root = new GameObject("Battle cycle stability");
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
        }

        private void SetArmies(int count)
        {
            if (scenario.unitTypes != null)
                foreach (var old in scenario.unitTypes)
                { Object.DestroyImmediate(old.spawnConfig); Object.DestroyImmediate(old); }
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
                if (scenario.unitTypes != null)
                    foreach (var unit in scenario.unitTypes)
                    { if (unit == null) continue; Object.DestroyImmediate(unit.spawnConfig); Object.DestroyImmediate(unit); }
                Object.DestroyImmediate(scenario);
            }
            if (system != null)
            { Object.DestroyImmediate(system.runtimeFlowConfig); Object.DestroyImmediate(system); }
        }

        private TeamStance Stance(int team) => (TeamStance)typeof(MassEngineManager)
            .GetMethod("ResolveTeamStance", PrivateInstance).Invoke(manager, new object[] { team });

        private TeamFlowFrameSettings Flow(int team) => (TeamFlowFrameSettings)typeof(MassEngineManager)
            .GetMethod("BuildTeamFlowSettings", PrivateInstance, null, new[] { typeof(int), typeof(int), typeof(int) }, null)
            .Invoke(manager, new object[] { team, 1, 256 });

        // After EndBattle: terminal state, engine paused, result set.
        // Orders/routes/frozen capture persist as visual state until ResetBattle.
        private void AssertEnded(int teamCount, string label)
        {
            Assert.That(manager.battleStarted, Is.False, label + "paused");
            Assert.That(controller.BattleResult.valid, Is.True, label + "has result");
        }

        // After RestartWithDefaultOrders (ResetBattle + StartDefaultBattle):
        // all state fully cleared, fresh defaults applied, running again.
        // This is the real "clean slate" check.
        private void AssertRestartedClean(int teamCount, WarSandboxGameMode mode, string label)
        {
            Assert.That(controller.Phase, Is.EqualTo(WarSandboxBattlePhase.Running), label + "running");
            for (int team = 0; team < teamCount; team++)
            {
                // ControlPoint StartDefaultBattle adds a Move route point to the control center.
                int expectedRoute = mode == WarSandboxGameMode.ControlPoint ? 1 : 0;
                Assert.That(controller.GetMoveRoutePointCount(team), Is.EqualTo(expectedRoute), label + "route " + team);
                Assert.That(Flow(team).targetMode, Is.EqualTo(mode == WarSandboxGameMode.ControlPoint ? 1 : 0), label + "override " + team);
                Assert.That(controller.GetArmy(team).hasOrder, Is.True, label + "order " + team);
                Assert.That(Stance(team), Is.EqualTo(TeamStance.Advance), label + "stance " + team);
            }
            Assert.That(controller.BattleResult.valid, Is.False, label + "no stale result");
            Assert.That(controller.ControlPointCaptureProgress, Is.Zero, label + "no progress");
            Assert.That(controller.ControlPointOwnerTeamId, Is.EqualTo(-1), label + "no owner");
        }

        [TestCase(2, 10)]
        [TestCase(3, 10)]
        public void DefaultCaptureCycleResetsAllStateConsistently(int teamCount, int cycles)
        {
            SetArmies(teamCount);
            Assert.That(controller.SetGameMode(WarSandboxGameMode.ControlPoint), Is.True);
            controller.controlPointCenter = new Vector3(5, 0, 10);
            // Cycle 0
            Assert.That(controller.StartDefaultBattle(), Is.True, "start 0");
            AssertRestartedClean(teamCount, WarSandboxGameMode.ControlPoint, "c0 ");
            Assert.That(controller.IssueMoveOrder(0, new Vector3(-10, 0, 15), false), Is.True, "c0 move");
            Assert.That(Stance(0), Is.EqualTo(TeamStance.MoveOnly), "c0 moved");
            Assert.That(controller.EndBattle(), Is.True, "c0 end");
            AssertEnded(teamCount, "c0 ");
            // Cycles 1+
            for (int i = 1; i < cycles; i++)
            {
                string l = "c" + i + " ";
                Assert.That(controller.RestartWithDefaultOrders(), Is.True, l + "restart");
                AssertRestartedClean(teamCount, WarSandboxGameMode.ControlPoint, l);
                if (i % 2 == 0)
                {
                    Assert.That(controller.IssueMoveOrder(0, new Vector3(-10, 0, 15), false), Is.True, l + "move");
                    Assert.That(Stance(0), Is.EqualTo(TeamStance.MoveOnly), l + "moved");
                }
                else
                {
                    Assert.That(controller.IssueOrder(ArmyOrder.Hold(0)), Is.True, l + "hold");
                    Assert.That(Stance(0), Is.EqualTo(TeamStance.HoldHere), l + "held");
                }
                Assert.That(controller.EndBattle(), Is.True, l + "end");
                AssertEnded(teamCount, l);
            }
        }

        [TestCase(2, 8)]
        [TestCase(3, 8)]
        public void AnnihilationCycleResetsAllStateConsistently(int teamCount, int cycles)
        {
            SetArmies(teamCount);
            Assert.That(controller.SetGameMode(WarSandboxGameMode.Annihilation), Is.True);
            Assert.That(controller.StartDefaultBattle(), Is.True, "start 0");
            AssertRestartedClean(teamCount, WarSandboxGameMode.Annihilation, "c0 ");
            Assert.That(controller.IssueOrder(ArmyOrder.Retreat(1)), Is.True, "c0 retreat");
            Assert.That(Stance(1), Is.EqualTo(TeamStance.MoveOnly), "c0 retreated");
            Assert.That(controller.EndBattle(), Is.True, "c0 end");
            AssertEnded(teamCount, "c0 ");
            for (int i = 1; i < cycles; i++)
            {
                string l = "c" + i + " ";
                Assert.That(controller.RestartWithDefaultOrders(), Is.True, l + "restart");
                AssertRestartedClean(teamCount, WarSandboxGameMode.Annihilation, l);
                if (i % 3 == 0)
                {
                    Assert.That(controller.IssueOrder(ArmyOrder.Retreat(1)), Is.True, l + "retreat");
                    Assert.That(Stance(1), Is.EqualTo(TeamStance.MoveOnly), l + "retreated");
                }
                else if (i % 3 == 1)
                {
                    Assert.That(controller.IssueOrder(ArmyOrder.Attack(1)), Is.True, l + "attack");
                    Assert.That(Stance(1), Is.EqualTo(TeamStance.Advance), l + "attacked");
                }
                Assert.That(controller.EndBattle(), Is.True, l + "end");
                AssertEnded(teamCount, l);
            }
        }

        [Test]
        public void ExplicitCommandsDoNotAccumulateAcrossCycles()
        {
            SetArmies(3);
            Assert.That(controller.SetGameMode(WarSandboxGameMode.ControlPoint), Is.True);
            controller.controlPointCenter = new Vector3(5, 0, 10);
            Assert.That(controller.StartDefaultBattle(), Is.True, "start 0");
            Assert.That(controller.IssueMoveOrder(0, new Vector3(-10, 0, 15), false), Is.True, "c0 move0");
            Assert.That(controller.IssueOrder(ArmyOrder.Hold(1)), Is.True, "c0 hold1");
            Assert.That(controller.IssueOrder(ArmyOrder.Retreat(2)), Is.True, "c0 retreat2");
            Assert.That(Stance(0), Is.EqualTo(TeamStance.MoveOnly));
            Assert.That(Stance(1), Is.EqualTo(TeamStance.HoldHere));
            Assert.That(Stance(2), Is.EqualTo(TeamStance.MoveOnly));
            Assert.That(controller.EndBattle(), Is.True, "c0 end");
            // Restart: all three teams' commands fully reset.
            Assert.That(controller.RestartWithDefaultOrders(), Is.True, "restart 1");
            AssertRestartedClean(3, WarSandboxGameMode.ControlPoint, "c1 ");
            // Route point from default Move order to control center.
            Assert.That(controller.GetMoveRoutePointCount(0), Is.EqualTo(1));
            Assert.That(controller.IssueOrder(ArmyOrder.Attack(0)), Is.True, "c1 attack0");
            Assert.That(controller.IssueMoveOrder(1, new Vector3(15, 0, -10), false), Is.True, "c1 move1");
            Assert.That(controller.IssueOrder(ArmyOrder.Hold(2)), Is.True, "c1 hold2");
            Assert.That(controller.EndBattle(), Is.True, "c1 end");
            Assert.That(controller.RestartWithDefaultOrders(), Is.True, "restart 2");
            AssertRestartedClean(3, WarSandboxGameMode.ControlPoint, "c2 ");
            Assert.That(controller.EndBattle(), Is.True, "c2 end");
        }

        [Test]
        public void InvalidCoordinatesDoNotAccumulateAcrossCycles()
        {
            SetArmies(3);
            Assert.That(controller.SetGameMode(WarSandboxGameMode.ControlPoint), Is.True);
            controller.controlPointCenter = new Vector3(5, 0, 10);
            Assert.That(controller.StartDefaultBattle(), Is.True, "start 0");
            controller.IssueMoveOrder(0, new Vector3(float.NaN, 0, 0), false);
            controller.IssueMoveOrder(0, new Vector3(0, float.NaN, 0), false);
            controller.IssueMoveOrder(0, new Vector3(0, 0, float.NaN), false);
            controller.IssueMoveOrder(1, new Vector3(float.PositiveInfinity, 0, 0), false);
            controller.IssueMoveOrder(2, new Vector3(0, 0, float.NegativeInfinity), false);
            Assert.That(controller.CommandError, Is.Not.Null, "error recorded");
            Assert.That(controller.IssueMoveOrder(1, new Vector3(10, 0, 5), false), Is.True, "valid after bad");
            Assert.That(controller.EndBattle(), Is.True, "end 0");
            for (int i = 1; i < 10; i++)
            {
                string l = "c" + i + " ";
                Assert.That(controller.RestartWithDefaultOrders(), Is.True, l + "restart");
                controller.IssueMoveOrder(0, new Vector3(float.NaN, 0, 0), false);
                controller.IssueMoveOrder(1, new Vector3(float.PositiveInfinity, 0, 0), false);
                controller.IssueMoveOrder(2, new Vector3(0, 0, float.NegativeInfinity), false);
                Assert.That(controller.CommandError, Is.Not.Null, l + "error");
                if (i % 2 == 0)
                    Assert.That(controller.IssueMoveOrder(1, new Vector3(10, 0, 5), false), Is.True, l + "valid");
                else
                    Assert.That(controller.IssueOrder(ArmyOrder.Attack(1)), Is.True, l + "attack");
                Assert.That(controller.EndBattle(), Is.True, l + "end");
            }
            // Engine still works after 10 cycles of abuse.
            Assert.That(controller.RestartWithDefaultOrders(), Is.True, "can still start");
            AssertRestartedClean(3, WarSandboxGameMode.ControlPoint, "final ");
            Assert.That(controller.CommandError, Is.Null, "error cleared by successful restart");
        }
    }
}