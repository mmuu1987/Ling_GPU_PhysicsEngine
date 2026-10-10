using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MassEngine.Game.Tests
{
    // CPU-only controller contracts. Never submit non-finite coordinates to a GPU simulation.
    public sealed class CommandInputSafetyTests
    {
        private GameObject root;
        private MassEngineManager manager;
        private WarSandboxBattleController controller;
        private ScenarioConfig scenario;
        private MassEngineSystemConfig system;
        private readonly List<WarSandboxSoundCue> feedback = new List<WarSandboxSoundCue>();
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("Command input safety CPU tests");
            root.SetActive(false);
            manager = root.AddComponent<MassEngineManager>();
            controller = root.AddComponent<WarSandboxBattleController>();
            controller.manager = manager;
            controller.staticObstaclesEnabled = false;
            scenario = ScriptableObject.CreateInstance<ScenarioConfig>();
            system = ScriptableObject.CreateInstance<MassEngineSystemConfig>();
            system.runtimeFlowConfig = ScriptableObject.CreateInstance<RuntimeFlowConfig>();
            scenario.unitTypes = new UnitTypeConfig[3];
            for (int team = 0; team < 3; team++)
            {
                var unit = ScriptableObject.CreateInstance<UnitTypeConfig>();
                unit.teamId = team;
                unit.spawnConfig = ScriptableObject.CreateInstance<SpawnConfig>();
                unit.spawnConfig.unitCount = 64;
                unit.spawnConfig.spawnCenter = new Vector3((team - 1) * 20, 0, team == 2 ? 30 : -10);
                scenario.unitTypes[team] = unit;
            }
            manager.scenarioConfig = scenario;
            manager.systemConfig = system;
            typeof(MassEngineManager).GetMethod("EnsureTeamFlowState", PrivateInstance).Invoke(manager, new object[] { 3 });
            controller.RebuildArmyStates();
            manager.PauseBattle();
            controller.FeedbackRequested += feedback.Add;
        }

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1;
            if (controller != null) controller.FeedbackRequested -= feedback.Add;
            if (root != null) Object.DestroyImmediate(root);
            foreach (var unit in scenario.unitTypes)
            {
                Object.DestroyImmediate(unit.spawnConfig);
                Object.DestroyImmediate(unit);
            }
            Object.DestroyImmediate(scenario);
            Object.DestroyImmediate(system.runtimeFlowConfig);
            Object.DestroyImmediate(system);
            feedback.Clear();
        }

        private static Vector3 BadTarget(int axis, int kind)
        {
            var point = new Vector3(5, 2, 10);
            point[axis] = kind == 0 ? float.NaN : kind == 1 ? float.PositiveInfinity : float.NegativeInfinity;
            return point;
        }

        private static IEnumerable<TestCaseData> InvalidTargets()
        {
            for (int axis = 0; axis < 3; axis++)
                for (int kind = 0; kind < 3; kind++) yield return new TestCaseData(axis, kind);
        }

        private static IEnumerable<TestCaseData> InvalidMoveCalls()
        {
            for (int axis = 0; axis < 3; axis++)
                for (int kind = 0; kind < 3; kind++)
                    for (int api = 0; api < 3; api++) yield return new TestCaseData(axis, kind, api);
        }

        private bool Move(int team, Vector3 target, int api)
        {
            if (api == 0) return controller.IssueOrder(ArmyOrder.Move(team, target));
            return controller.IssueMoveOrder(team, target, api == 2);
        }

        private TeamStance Stance(int team) => (TeamStance)typeof(MassEngineManager)
            .GetMethod("ResolveTeamStance", PrivateInstance).Invoke(manager, new object[] { team });

        private TeamFlowFrameSettings Flow(int team) => (TeamFlowFrameSettings)typeof(MassEngineManager)
            .GetMethod("BuildTeamFlowSettings", PrivateInstance, null, new[] { typeof(int), typeof(int), typeof(int) }, null)
            .Invoke(manager, new object[] { team, 1, 256 });

        [Serializable]
        private sealed class ArmyState
        {
            public bool hasOrder, flowEnabled, dynamicTargeting;
            public ArmyOrder order;
            public TeamStance stance;
            public int targetMode;
            public Vector3 target;
            public Vector3[] route;
        }

        [Serializable]
        private sealed class CommandState
        {
            public WarSandboxBattlePhase phase;
            public bool engineRunning;
            public int selectedTeam;
            public float speed;
            public ArmyState[] armies;
        }

        private string Snapshot()
        {
            var state = new CommandState {
                phase = controller.Phase, engineRunning = manager.battleStarted,
                selectedTeam = controller.selectedTeam, speed = controller.SimulationSpeed,
                armies = new ArmyState[3]
            };
            for (int team = 0; team < 3; team++)
            {
                var army = controller.GetArmy(team);
                var flow = Flow(team);
                var route = new Vector3[controller.GetMoveRoutePointCount(team)];
                for (int i = 0; i < route.Length; i++) Assert.That(controller.TryGetMoveRoutePoint(team, i, out route[i]), Is.True);
                state.armies[team] = new ArmyState {
                    hasOrder = army.hasOrder, order = army.currentOrder, stance = Stance(team),
                    flowEnabled = flow.enabled, dynamicTargeting = flow.dynamicFlowEnabled,
                    targetMode = flow.targetMode, target = flow.targetPoint, route = route
                };
            }
            return JsonUtility.ToJson(state);
        }

        private void RejectedWithoutStateChange(Func<bool> issue)
        {
            string before = Snapshot();
            feedback.Clear();
            Assert.That(issue(), Is.False, "Non-finite command input must be rejected, not accepted or normalized.");
            Assert.That(controller.CommandError, Is.Not.Null.And.Not.Empty);
            Assert.That(feedback, Is.EqualTo(new[] { WarSandboxSoundCue.Rejected }), "No successful command/start feedback on rejection.");
            Assert.That(Snapshot(), Is.EqualTo(before), "Rejected input must preserve every team's route/order/stance/target and battle state.");
            Assert.That(manager.Buffers, Is.Null, "These tests must never initialize or dispatch GPU buffers.");
        }

        [TestCaseSource(nameof(InvalidMoveCalls))]
        public void NonFiniteMoveCannotStartBattleOrReplaceAndResumeAPausedRoute(int axis, int kind, int api)
        {
            Vector3 target = BadTarget(axis, kind);
            Assert.That(controller.Phase, Is.EqualTo(WarSandboxBattlePhase.Setup));
            RejectedWithoutStateChange(() => Move(0, target, api));
            Assert.That(controller.IssueMoveOrder(0, new Vector3(5, 20, 10), false), Is.True);
            Assert.That(controller.IssueMoveOrder(0, new Vector3(15, -30, 25), true), Is.True);
            Assert.That(controller.CommandError, Is.Null, "A valid command clears the previous error.");
            controller.PauseBattle();
            Assert.That(controller.Phase, Is.EqualTo(WarSandboxBattlePhase.Paused));
            RejectedWithoutStateChange(() => Move(0, target, api));
        }

        [TestCaseSource(nameof(InvalidTargets))]
        public void InvalidThirdArmyMoveKeepsTheDefaultObjectiveAndOtherArmies(int axis, int kind)
        {
            Assert.That(controller.SetGameMode(WarSandboxGameMode.ControlPoint), Is.True);
            controller.controlPointCenter = new Vector3(5, 0, 10);
            Assert.That(controller.StartDefaultBattle(), Is.True);
            for (int team = 0; team < 3; team++) Assert.That(Stance(team), Is.EqualTo(TeamStance.Advance));
            for (int api = 0; api < 3; api++)
            {
                int entryPoint = api;
                RejectedWithoutStateChange(() => Move(2, BadTarget(axis, kind), entryPoint));
            }
        }

        [TestCaseSource(nameof(InvalidTargets))]
        public void InvalidDefaultCapturePointDoesNotPartiallyStartAnyArmy(int axis, int kind)
        {
            Assert.That(controller.SetGameMode(WarSandboxGameMode.ControlPoint), Is.True);
            controller.controlPointCenter = BadTarget(axis, kind);
            RejectedWithoutStateChange(() => controller.StartDefaultBattle());
            controller.controlPointCenter = new Vector3(5, 0, 10);
            Assert.That(controller.StartDefaultBattle(), Is.True, controller.CommandError);
            for (int team = 0; team < 3; team++) Assert.That(Stance(team), Is.EqualTo(TeamStance.Advance));
        }

        [TestCase(0, 0)]
        [TestCase(1, 1)]
        [TestCase(2, 2)]
        public void InvalidDerivedRetreatTargetKeepsTheAcceptedRoute(int axis, int kind)
        {
            Assert.That(controller.IssueMoveOrder(2, new Vector3(10, 0, 20), false), Is.True);
            controller.GetArmy(2).spawnCenter = BadTarget(axis, kind);
            RejectedWithoutStateChange(() => controller.IssueOrder(ArmyOrder.Retreat(2)));
        }

        [Test]
        public void FiniteCoordinatesAndExistingCommandSemanticsRemainSupported()
        {
            var first = new Vector3(5, 120, 10);
            var second = new Vector3(15, -300, 25);
            Assert.That(controller.IssueMoveOrder(0, first, false), Is.True);
            Assert.That(Stance(0), Is.EqualTo(TeamStance.MoveOnly));
            Assert.That(Flow(0).targetPoint, Is.EqualTo(first));
            Assert.That(controller.IssueMoveOrder(0, second, true), Is.True);
            Assert.That(controller.GetMoveRoutePointCount(0), Is.EqualTo(2));
            Assert.That(Flow(0).targetPoint, Is.EqualTo(first), "Appending a waypoint does not prematurely replace the active leg.");
            Assert.That(controller.IssueOrder(ArmyOrder.Hold(0)), Is.True);
            Assert.That(Stance(0), Is.EqualTo(TeamStance.HoldHere));
            Assert.That(controller.IssueOrder(ArmyOrder.Attack(0)), Is.True);
            Assert.That(Stance(0), Is.EqualTo(TeamStance.Advance));
            Assert.That(Flow(0).targetMode, Is.Zero);
            Assert.That(controller.IssueOrder(ArmyOrder.Retreat(0)), Is.True);
            Assert.That(Stance(0), Is.EqualTo(TeamStance.MoveOnly));
            Assert.That(Flow(0).targetPoint, Is.EqualTo(controller.GetArmy(0).spawnCenter));
            Assert.That(manager.Buffers, Is.Null);
        }
    }
}
