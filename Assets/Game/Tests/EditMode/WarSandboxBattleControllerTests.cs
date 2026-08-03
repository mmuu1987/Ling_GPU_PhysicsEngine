using System.Reflection;
using MassEngine.Game.Editor;
using NUnit.Framework;
using UnityEngine;

namespace MassEngine.Game.Tests
{
    public sealed class WarSandboxBattleControllerTests
    {
        private GameObject root;
        private MassEngineManager manager;
        private WarSandboxBattleController controller;
        private ScenarioConfig scenario;
        private MassEngineSystemConfig system;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("WarSandboxControllerTests");
            root.SetActive(false);
            manager = root.AddComponent<MassEngineManager>();
            controller = root.AddComponent<WarSandboxBattleController>();
            controller.manager = manager;

            scenario = ScriptableObject.CreateInstance<ScenarioConfig>();
            scenario.unitTypes = new[]
            {
                CreateUnitType("Attackers", 0, 120, new Vector3(-20f, 0f, 5f)),
                CreateUnitType("Defenders", 1, 80, new Vector3(30f, 0f, -5f))
            };
            manager.scenarioConfig = scenario;

            system = ScriptableObject.CreateInstance<MassEngineSystemConfig>();
            system.runtimeFlowConfig = ScriptableObject.CreateInstance<RuntimeFlowConfig>();
            manager.systemConfig = system;

            controller.RebuildArmyStates();
        }

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
            if (system != null && system.runtimeFlowConfig != null)
                Object.DestroyImmediate(system.runtimeFlowConfig);
            if (system != null)
                Object.DestroyImmediate(system);
            if (scenario != null)
            {
                if (scenario.unitTypes != null)
                {
                    foreach (UnitTypeConfig unitType in scenario.unitTypes)
                    {
                        if (unitType != null && unitType.spawnConfig != null)
                            Object.DestroyImmediate(unitType.spawnConfig);
                        if (unitType != null)
                            Object.DestroyImmediate(unitType);
                    }
                }
                Object.DestroyImmediate(scenario);
            }
            if (root != null)
                Object.DestroyImmediate(root);
        }

        [Test]
        public void RebuildArmyStatesAggregatesIntentByTeam()
        {
            ArmyRuntimeState attackers = controller.GetArmy(0);
            ArmyRuntimeState defenders = controller.GetArmy(1);

            Assert.That(attackers.initialUnitCount, Is.EqualTo(120));
            Assert.That(attackers.spawnCenter, Is.EqualTo(new Vector3(-20f, 0f, 5f)));
            Assert.That(defenders.initialUnitCount, Is.EqualTo(80));
            Assert.That(defenders.spawnCenter, Is.EqualTo(new Vector3(30f, 0f, -5f)));
        }

        [Test]
        public void OrdersAreAppliedToTheLiveManagerAndRecorded()
        {
            Assert.That(controller.IssueOrder(ArmyOrder.Attack(1)), Is.True);
            Assert.That(manager.IsBattleRunning, Is.True);
            Assert.That(controller.GetArmy(1).currentOrder.type, Is.EqualTo(ArmyOrderType.Attack));

            Vector3 destination = new Vector3(12f, 0f, 40f);
            Assert.That(controller.IssueOrder(ArmyOrder.Move(1, destination)), Is.True);
            Assert.That(controller.GetArmy(1).currentOrder.target, Is.EqualTo(destination));

            Assert.That(controller.IssueOrder(ArmyOrder.Hold(1)), Is.True);
            Assert.That(controller.GetArmy(1).currentOrder.type, Is.EqualTo(ArmyOrderType.Hold));

            Assert.That(controller.IssueOrder(ArmyOrder.Retreat(1)), Is.True);
            Assert.That(controller.GetArmy(1).currentOrder.target, Is.EqualTo(new Vector3(30f, 0f, -5f)));
        }

        [Test]
        public void RuntimeNavigationDoctrineOverridesReadOnlyFlowConfig()
        {
            RuntimeFlowConfig flow = system.runtimeFlowConfig;
            flow.defenderFlowFieldEnabled = false;
            flow.runtimeDynamicDefenderFlowEnabled = false;

            manager.SetTeamNavigationOverride(1, true, true);
            TeamFlowFrameSettings attack = InvokeBuildTeamFlowSettings(1);
            Assert.That(attack.enabled, Is.True);
            Assert.That(attack.dynamicFlowEnabled, Is.True);
            Assert.That(flow.defenderFlowFieldEnabled, Is.False, "Runtime order must not write the config asset.");

            manager.SetTeamNavigationOverride(1, false, false);
            TeamFlowFrameSettings hold = InvokeBuildTeamFlowSettings(1);
            Assert.That(hold.enabled, Is.False);
            Assert.That(hold.dynamicFlowEnabled, Is.False);
        }

        [Test]
        public void PausePreservesOrderAndSpeedIsBounded()
        {
            controller.IssueOrder(ArmyOrder.Attack(0));
            controller.PauseBattle();

            Assert.That(manager.IsBattleRunning, Is.False);
            Assert.That(controller.GetArmy(0).currentOrder.type, Is.EqualTo(ArmyOrderType.Attack));

            controller.SetSimulationSpeed(99f);
            Assert.That(controller.SimulationSpeed, Is.EqualTo(4f));
            controller.SetSimulationSpeed(0f);
            Assert.That(controller.SimulationSpeed, Is.EqualTo(0.25f));
        }

        [Test]
        public void DefaultBattleOrdersBothArmiesToAttack()
        {
            Assert.That(controller.StartDefaultBattle(), Is.True);
            Assert.That(controller.Phase, Is.EqualTo(WarSandboxBattlePhase.Running));
            Assert.That(controller.GetArmy(0).currentOrder.type, Is.EqualTo(ArmyOrderType.Attack));
            Assert.That(controller.GetArmy(1).currentOrder.type, Is.EqualTo(ArmyOrderType.Attack));
            Assert.That(manager.IsBattleRunning, Is.True);
        }

        [TestCase(10000)]
        [TestCase(200000)]
        public void CenteredDeploymentKeepsRequestedEdgeGapAcrossArmySizes(int unitCount)
        {
            SpawnConfig attackers = ScriptableObject.CreateInstance<SpawnConfig>();
            SpawnConfig defenders = ScriptableObject.CreateInstance<SpawnConfig>();
            try
            {
                attackers.unitCount = unitCount;
                attackers.formationDensity = 0.5f;
                attackers.formationAspect = 2f;
                defenders.unitCount = unitCount;
                defenders.formationDensity = 0.5f;
                defenders.formationAspect = 2f;

                const float requestedGap = 50f;
                attackers.spawnCenter = WarSandboxFormationLayout.ResolveCenteredSpawnCenter(attackers, 0, requestedGap);
                defenders.spawnCenter = WarSandboxFormationLayout.ResolveCenteredSpawnCenter(defenders, 1, requestedGap);

                float attackerFrontEdge = attackers.spawnCenter.x + attackers.ResolveSpawnSize().x * 0.5f;
                float defenderFrontEdge = defenders.spawnCenter.x - defenders.ResolveSpawnSize().x * 0.5f;
                Assert.That(defenderFrontEdge - attackerFrontEdge, Is.EqualTo(requestedGap).Within(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(attackers);
                Object.DestroyImmediate(defenders);
            }
        }

        [Test]
        public void ScalePresetPreservesUnitTypeSharesAndExactTeamTotals()
        {
            ScenarioConfig presetScenario = ScriptableObject.CreateInstance<ScenarioConfig>();
            presetScenario.unitTypes = new[]
            {
                CreateUnitType("AttackersA", 0, 100, Vector3.zero),
                CreateUnitType("AttackersB", 0, 300, Vector3.zero),
                CreateUnitType("Defenders", 1, 80, Vector3.zero)
            };

            try
            {
                WarSandboxScenarioPresets.ApplyPerTeamUnitCount(presetScenario, 10000);

                Assert.That(presetScenario.unitTypes[0].spawnConfig.unitCount, Is.EqualTo(2500));
                Assert.That(presetScenario.unitTypes[1].spawnConfig.unitCount, Is.EqualTo(7500));
                Assert.That(presetScenario.unitTypes[2].spawnConfig.unitCount, Is.EqualTo(10000));
                Assert.That(WarSandboxScenarioPresets.ResolveTeamUnitCount(presetScenario, 0), Is.EqualTo(10000));
                Assert.That(WarSandboxScenarioPresets.ResolveTeamUnitCount(presetScenario, 1), Is.EqualTo(10000));
            }
            finally
            {
                foreach (UnitTypeConfig unitType in presetScenario.unitTypes)
                {
                    Object.DestroyImmediate(unitType.spawnConfig);
                    Object.DestroyImmediate(unitType);
                }
                Object.DestroyImmediate(presetScenario);
            }
        }

        [Test]
        public void CameraZoomRejectsInputSpikesAndAlwaysStaysBounded()
        {
            float distance = 100f;
            for (int i = 0; i < 1000; i++)
            {
                float wheelSpike = i % 2 == 0 ? float.MaxValue : float.MinValue;
                distance = CameraMotionSafety.ResolveZoomDistance(distance, wheelSpike, 10f, 2f, 2500f);
                Assert.That(float.IsNaN(distance) || float.IsInfinity(distance), Is.False);
                Assert.That(distance, Is.InRange(2f, 2500f));
            }
        }

        [Test]
        public void CameraMotionRejectsNonFiniteAndClampsSingleFrameTravel()
        {
            Vector3 invalid = new Vector3(float.NaN, float.PositiveInfinity, 1f);
            Assert.That(CameraMotionSafety.ClampStep(invalid, 200f), Is.EqualTo(Vector3.zero));
            Assert.That(CameraMotionSafety.ClampWorldPosition(invalid, 5000f), Is.EqualTo(Vector3.zero));

            Vector3 clamped = CameraMotionSafety.ClampStep(new Vector3(10000f, 0f, 0f), 200f);
            Assert.That(clamped.magnitude, Is.EqualTo(200f).Within(0.001f));
        }

        [TestCase(330f, -30f)]
        [TestCase(270f, -90f)]
        [TestCase(30f, 30f)]
        public void CameraPitchSynchronizesEulerAnglesToSignedRange(float unityEulerAngle, float expectedPitch)
        {
            Assert.That(
                CameraMotionSafety.NormalizeSignedAngle(unityEulerAngle),
                Is.EqualTo(expectedPitch).Within(0.001f));
        }

        [Test]
        public void CameraFollowIsFrameRateIndependentAndStepBounded()
        {
            Vector3 current = Vector3.zero;
            Vector3 target = new Vector3(1000f, 0f, 0f);
            Vector3 step = CameraMotionSafety.ResolveFollowStep(current, target, 5f, 0.016f, 20f);
            Assert.That(step.x, Is.GreaterThan(0f));
            Assert.That(step.magnitude, Is.LessThanOrEqualTo(20f));

            Vector3 stalledFrame = CameraMotionSafety.ResolveFollowStep(current, target, 5f, 10f, 20f);
            Assert.That(stalledFrame.magnitude, Is.EqualTo(20f).Within(0.001f));
            Assert.That(CameraMotionSafety.ResolveFollowStep(current, new Vector3(float.NaN, 0f, 0f), 5f, 0.016f, 20f), Is.EqualTo(Vector3.zero));
        }

        [Test]
        public void MinimapProjectionRoundTripsWorldCoordinates()
        {
            Rect map = new Rect(12f, 40f, 180f, 140f);
            Vector2 worldSize = new Vector2(840f, 620f);
            Vector3 world = new Vector3(123f, 0f, -77f);

            Vector2 projected = WarSandboxMinimapProjection.WorldToMap(world, worldSize, map);
            Vector3 restored = WarSandboxMinimapProjection.MapToWorld(projected, worldSize, map);

            Assert.That(restored.x, Is.EqualTo(world.x).Within(0.001f));
            Assert.That(restored.z, Is.EqualTo(world.z).Within(0.001f));
        }

        [Test]
        public void MinimapLayoutStaysOnScreenAtSmallResolutions()
        {
            Rect outer = WarSandboxMinimapProjection.ResolveOuterRect(320f, 240f, 180f, 8f);
            Assert.That(outer.xMin, Is.GreaterThanOrEqualTo(0f));
            Assert.That(outer.yMin, Is.GreaterThanOrEqualTo(0f));
            Assert.That(outer.xMax, Is.LessThanOrEqualTo(320f));
            Assert.That(outer.yMax, Is.LessThanOrEqualTo(240f));
            Assert.That(WarSandboxMinimapProjection.ResolveContentRect(outer).height, Is.GreaterThan(0f));
        }

        [Test]
        public void BattleResultCapturesAFrozenTerminalSummary()
        {
            BattleTelemetrySnapshot telemetry = new BattleTelemetrySnapshot
            {
                aliveAttackers = 73,
                aliveDefenders = 0,
                battleSeconds = 125.8f,
                attackerFlowRebuilds = 4,
                defenderFlowRebuilds = 7,
                peakGridOverflowPerFrame = 12,
                valid = true
            };

            WarSandboxBattleResult result = WarSandboxBattleResult.Capture(
                WarSandboxBattlePhase.AttackerVictory, 120, 80, telemetry);
            telemetry.aliveAttackers = 1;

            Assert.That(result.valid, Is.True);
            Assert.That(result.phase, Is.EqualTo(WarSandboxBattlePhase.AttackerVictory));
            Assert.That(result.attackerSurvivors, Is.EqualTo(73));
            Assert.That(result.AttackerCasualties, Is.EqualTo(47));
            Assert.That(result.DefenderCasualties, Is.EqualTo(80));
            Assert.That(result.battleSeconds, Is.EqualTo(125.8f));
            Assert.That(result.peakGridOverflowPerFrame, Is.EqualTo(12));
        }

        [TestCase(320f, 240f, 72f)]
        [TestCase(997f, 635f, 689f)]
        public void BattleReportLayoutStaysOnScreen(float screenWidth, float screenHeight, float commandPanelX)
        {
            Rect report = WarSandboxBattleReportLayout.ResolveRect(screenWidth, screenHeight, commandPanelX, 8f);
            Assert.That(report.xMin, Is.GreaterThanOrEqualTo(0f));
            Assert.That(report.yMin, Is.GreaterThanOrEqualTo(0f));
            Assert.That(report.xMax, Is.LessThanOrEqualTo(screenWidth));
            Assert.That(report.yMax, Is.LessThanOrEqualTo(screenHeight));
        }

        private TeamFlowFrameSettings InvokeBuildTeamFlowSettings(int teamId)
        {
            MethodInfo method = typeof(MassEngineManager).GetMethod(
                "BuildTeamFlowSettings",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            return (TeamFlowFrameSettings)method.Invoke(manager, new object[] { teamId, 1, 16 });
        }

        private static UnitTypeConfig CreateUnitType(string name, int teamId, int count, Vector3 center)
        {
            UnitTypeConfig unitType = ScriptableObject.CreateInstance<UnitTypeConfig>();
            unitType.name = name;
            unitType.teamId = teamId;
            unitType.spawnConfig = ScriptableObject.CreateInstance<SpawnConfig>();
            unitType.spawnConfig.unitCount = count;
            unitType.spawnConfig.spawnCenter = center;
            return unitType;
        }
    }
}
