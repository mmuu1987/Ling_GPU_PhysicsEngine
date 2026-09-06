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
        public void MoveRoutesReplaceAppendAndClearWithDoctrineChanges()
        {
            Vector3 first = new Vector3(-5f, 0f, 10f);
            Vector3 second = new Vector3(20f, 0f, 30f);
            Assert.That(controller.IssueMoveOrder(0, first, false), Is.True);
            Assert.That(controller.IssueMoveOrder(0, second, true), Is.True);

            Assert.That(controller.GetMoveRoutePointCount(0), Is.EqualTo(2));
            Assert.That(controller.GetArmy(0).currentOrder.target, Is.EqualTo(first));
            Assert.That(controller.TryGetMoveRoutePoint(0, 1, out Vector3 queued), Is.True);
            Assert.That(queued, Is.EqualTo(second));

            controller.IssueOrder(ArmyOrder.Attack(0));
            Assert.That(controller.GetMoveRoutePointCount(0), Is.Zero);
        }

        [Test]
        public void StaticObstacleToggleUploadsRuntimeWallsAndProjectsMoveTargets()
        {
            controller.useCustomStaticObstacleLayout = true;
            controller.staticObstacles = new[]
            {
                new StaticObstacleRect(Vector2.zero, new Vector2(10f, 20f))
            };
            controller.staticObstacleClearance = 2f;

            Assert.That(controller.SetStaticObstaclesEnabled(true), Is.True);
            Assert.That(manager.StaticObstacleCount, Is.EqualTo(1));
            Assert.That(manager.ResolvePointOutsideStaticObstacles(Vector3.zero), Is.Not.EqualTo(Vector3.zero));
            Assert.That(controller.SetStaticObstaclesEnabled(false), Is.True);
            Assert.That(manager.StaticObstacleCount, Is.Zero);

            Assert.That(controller.SetStaticObstaclesEnabled(true), Is.True);
            Assert.That(controller.IssueMoveOrder(0, Vector3.zero, false), Is.True);
            Assert.That(controller.GetArmy(0).currentOrder.target, Is.Not.EqualTo(Vector3.zero));
        }

        [Test]
        public void WaypointArrivalUsesGroundPlaneDistanceAndConfiguredRadius()
        {
            Assert.That(WarSandboxMoveRoute.HasReached(
                new Vector3(4f, 100f, 3f), Vector3.zero, 5f), Is.True);
            Assert.That(WarSandboxMoveRoute.HasReached(
                new Vector3(4.1f, 0f, 3f), Vector3.zero, 5f), Is.False);
        }

        [Test]
        public void ControlPointProgressCapturesOnlyWhenZoneIsUncontested()
        {
            Assert.That(WarSandboxControlPoint.ResolveProgress(0f, 10, 0, 2f, 20f), Is.EqualTo(0.1f));
            Assert.That(WarSandboxControlPoint.ResolveProgress(0.25f, 10, 3, 2f, 20f), Is.EqualTo(0.25f));
            Assert.That(WarSandboxControlPoint.ResolveProgress(-0.25f, 0, 0, 2f, 20f), Is.EqualTo(-0.2f));
            Assert.That(WarSandboxControlPoint.ResolveProgress(0f, 0, 4, 40f, 20f), Is.EqualTo(-1f));
        }

        [Test]
        public void GameModeCanOnlyChangeDuringDeployment()
        {
            Assert.That(controller.SetGameMode(WarSandboxGameMode.ControlPoint), Is.True);
            Assert.That(controller.gameMode, Is.EqualTo(WarSandboxGameMode.ControlPoint));
            controller.StartDefaultBattle();
            Assert.That(controller.SetGameMode(WarSandboxGameMode.Annihilation), Is.False);
            Assert.That(controller.gameMode, Is.EqualTo(WarSandboxGameMode.ControlPoint));
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

        [Test]
        public void ControlPointBattleOrdersBothArmiesToTheObjective()
        {
            controller.controlPointCenter = new Vector3(15f, 0f, -12f);
            controller.SetGameMode(WarSandboxGameMode.ControlPoint);

            Assert.That(controller.StartDefaultBattle(), Is.True);
            Assert.That(controller.GetArmy(0).currentOrder.type, Is.EqualTo(ArmyOrderType.Move));
            Assert.That(controller.GetArmy(1).currentOrder.type, Is.EqualTo(ArmyOrderType.Move));
            Assert.That(controller.GetArmy(0).currentOrder.target, Is.EqualTo(controller.controlPointCenter));
            Assert.That(controller.GetArmy(1).currentOrder.target, Is.EqualTo(controller.controlPointCenter));
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
        public void RankedDeploymentStacksATeamBackFromTheGapWithoutOverlap()
        {
            SpawnConfig melee = ScriptableObject.CreateInstance<SpawnConfig>();
            SpawnConfig archers = ScriptableObject.CreateInstance<SpawnConfig>();
            try
            {
                // The shipped attacker roster: a 30k melee screen with 20k archers behind it.
                melee.unitCount = 30000;
                melee.formationDensity = 0.5f;
                melee.formationAspect = 3.3333333f;
                archers.unitCount = 20000;
                archers.formationDensity = 0.5f;
                archers.formationAspect = 5f;

                const float requestedGap = 50f;
                float meleeDepth = melee.ResolveSpawnSize().x;
                melee.spawnCenter = WarSandboxFormationLayout.ResolveRankedSpawnCenter(melee, 0, requestedGap, 0f);
                archers.spawnCenter = WarSandboxFormationLayout.ResolveRankedSpawnCenter(archers, 0, requestedGap, meleeDepth);

                // The front rank owns the gap, exactly as a single-block team would.
                Assert.That(
                    melee.spawnCenter.x + meleeDepth * 0.5f,
                    Is.EqualTo(-requestedGap * 0.5f).Within(0.001f));
                // The rear rank begins where the front one ends: no overlap, no wasted lane.
                Assert.That(
                    archers.spawnCenter.x + archers.ResolveSpawnSize().x * 0.5f,
                    Is.EqualTo(melee.spawnCenter.x - meleeDepth * 0.5f).Within(0.001f));
                // Equal front widths are what make the two ranks read as one formation.
                Assert.That(
                    archers.ResolveSpawnSize().z,
                    Is.EqualTo(melee.ResolveSpawnSize().z).Within(0.01f));
            }
            finally
            {
                Object.DestroyImmediate(melee);
                Object.DestroyImmediate(archers);
            }
        }

        [Test]
        public void SplittingATeamIntoRanksKeepsItsTotalFootprint()
        {
            // Why the mixed-arms roster needed no world/grid/flow re-fit: 30k + 20k at the
            // authored aspects occupy the same rectangle the old single 50k block did.
            SpawnConfig single = ScriptableObject.CreateInstance<SpawnConfig>();
            SpawnConfig melee = ScriptableObject.CreateInstance<SpawnConfig>();
            SpawnConfig archers = ScriptableObject.CreateInstance<SpawnConfig>();
            try
            {
                single.unitCount = 50000;
                single.formationDensity = 0.5f;
                single.formationAspect = 2f;
                melee.unitCount = 30000;
                melee.formationDensity = 0.5f;
                melee.formationAspect = 3.3333333f;
                archers.unitCount = 20000;
                archers.formationDensity = 0.5f;
                archers.formationAspect = 5f;

                Assert.That(
                    melee.ResolveSpawnSize().x + archers.ResolveSpawnSize().x,
                    Is.EqualTo(single.ResolveSpawnSize().x).Within(0.01f));
                Assert.That(melee.ResolveSpawnSize().z, Is.EqualTo(single.ResolveSpawnSize().z).Within(0.01f));
                Assert.That(archers.ResolveSpawnSize().z, Is.EqualTo(single.ResolveSpawnSize().z).Within(0.01f));
            }
            finally
            {
                Object.DestroyImmediate(single);
                Object.DestroyImmediate(melee);
                Object.DestroyImmediate(archers);
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
        public void TeamPaletteGivesEveryArmyItsOwnStableColour()
        {
            // The HUD colours the selector, force readout, minimap and world markers from this one
            // table, so a repeat or a throw past its end would mislabel an army rather than fail loudly.
            var seen = new System.Collections.Generic.List<Color>();
            for (int teamId = 0; teamId < 24; teamId++)
            {
                Color colour = WarSandboxTeamPalette.Resolve(teamId);
                Assert.That(WarSandboxTeamPalette.Resolve(teamId), Is.EqualTo(colour), "team " + teamId + " must be stable");

                for (int i = 0; i < seen.Count; i++)
                {
                    float distance = Mathf.Abs(seen[i].r - colour.r) + Mathf.Abs(seen[i].g - colour.g) +
                                     Mathf.Abs(seen[i].b - colour.b);
                    Assert.That(distance, Is.GreaterThan(0.05f), "team " + teamId + " duplicates team " + i);
                }

                seen.Add(colour);
            }

            // Attacker red and defender blue are the colours the existing HUD screenshots use.
            Assert.That(WarSandboxTeamPalette.Resolve(0).r, Is.GreaterThan(WarSandboxTeamPalette.Resolve(0).b));
            Assert.That(WarSandboxTeamPalette.Resolve(1).b, Is.GreaterThan(WarSandboxTeamPalette.Resolve(1).r));
            Assert.That(WarSandboxTeamPalette.Resolve(-1), Is.EqualTo(Color.white));
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

        [TestCase(0, false, false, WarSandboxMinimapAction.FocusCamera)]
        [TestCase(0, true, false, WarSandboxMinimapAction.MoveSelectedArmy)]
        [TestCase(1, false, false, WarSandboxMinimapAction.MoveSelectedArmy)]
        [TestCase(1, false, true, WarSandboxMinimapAction.QueueMoveSelectedArmy)]
        [TestCase(0, true, true, WarSandboxMinimapAction.QueueMoveSelectedArmy)]
        [TestCase(2, false, false, WarSandboxMinimapAction.None)]
        public void MinimapPointerIntentSeparatesCameraAndOrders(
            int mouseButton,
            bool awaitingMoveTarget,
            bool appendModifier,
            WarSandboxMinimapAction expected)
        {
            Assert.That(
                WarSandboxMinimapProjection.ResolvePointerAction(mouseButton, awaitingMoveTarget, appendModifier),
                Is.EqualTo(expected));
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

        [Test]
        public void AnnihilationEndsOnlyWhenOneArmyIsLeftStanding()
        {
            // Three armies still fielding units: nobody has won anything yet.
            Assert.That(ResolveAnnihilation(
                new[] { 120, 80, 40 }, new[] { 3, 2, 1 },
                out WarSandboxBattlePhase phase, out int winner), Is.False);
            Assert.That(phase, Is.EqualTo(WarSandboxBattlePhase.Running));
            Assert.That(winner, Is.EqualTo(-1));

            // The third army outlasts both of the teams the old phases could name.
            Assert.That(ResolveAnnihilation(
                new[] { 120, 80, 40 }, new[] { 0, 0, 11 }, out phase, out winner), Is.True);
            Assert.That(phase, Is.EqualTo(WarSandboxBattlePhase.ArmyVictory));
            Assert.That(winner, Is.EqualTo(2));

            // Everyone emptied inside one sample.
            Assert.That(ResolveAnnihilation(
                new[] { 120, 80, 40 }, new[] { 0, 0, 0 }, out phase, out winner), Is.True);
            Assert.That(phase, Is.EqualTo(WarSandboxBattlePhase.Draw));
            Assert.That(winner, Is.EqualTo(-1));
        }

        [Test]
        public void TwoArmyBattlesKeepReportingTheAttackerAndDefenderPhases()
        {
            Assert.That(ResolveAnnihilation(
                new[] { 120, 80 }, new[] { 73, 0 },
                out WarSandboxBattlePhase phase, out int winner), Is.True);
            Assert.That(phase, Is.EqualTo(WarSandboxBattlePhase.AttackerVictory));
            Assert.That(winner, Is.EqualTo(0));

            Assert.That(ResolveAnnihilation(
                new[] { 120, 80 }, new[] { 0, 5 }, out phase, out winner), Is.True);
            Assert.That(phase, Is.EqualTo(WarSandboxBattlePhase.DefenderVictory));
            Assert.That(winner, Is.EqualTo(1));
        }

        [Test]
        public void AnArmyThatNeverTookTheFieldIsNotADefeatedOne()
        {
            // Team 1 is an empty slot between two real armies: it must not end the battle, and
            // it must not be mistaken for the loser once one of the real armies is wiped out.
            Assert.That(ResolveAnnihilation(
                new[] { 120, 0, 40 }, new[] { 120, 0, 40 },
                out WarSandboxBattlePhase phase, out int winner), Is.False);
            Assert.That(winner, Is.EqualTo(-1));

            Assert.That(ResolveAnnihilation(
                new[] { 120, 0, 40 }, new[] { 0, 0, 40 }, out phase, out winner), Is.True);
            Assert.That(phase, Is.EqualTo(WarSandboxBattlePhase.ArmyVictory));
            Assert.That(winner, Is.EqualTo(2));

            // One army alone on the field has no battle to win.
            Assert.That(ResolveAnnihilation(
                new[] { 120, 0 }, new[] { 120, 0 }, out phase, out winner), Is.False);
            Assert.That(phase, Is.EqualTo(WarSandboxBattlePhase.Running));
        }

        [Test]
        public void CaptureNamesTheWinnerEvenWhenThePhaseCannot()
        {
            BattleTelemetrySnapshot telemetry = new BattleTelemetrySnapshot { valid = true };

            Assert.That(WarSandboxBattleResult.Capture(
                WarSandboxBattlePhase.AttackerVictory, 120, 80, telemetry).winnerTeamId, Is.EqualTo(0));
            Assert.That(WarSandboxBattleResult.Capture(
                WarSandboxBattlePhase.DefenderVictory, 120, 80, telemetry).winnerTeamId, Is.EqualTo(1));
            Assert.That(WarSandboxBattleResult.Capture(
                WarSandboxBattlePhase.Draw, 120, 80, telemetry).winnerTeamId, Is.EqualTo(-1));
            Assert.That(WarSandboxBattleResult.Capture(
                WarSandboxBattlePhase.ArmyVictory, 120, 80, telemetry,
                WarSandboxVictoryReason.Annihilation, 2).winnerTeamId, Is.EqualTo(2));
        }

        private static bool ResolveAnnihilation(
            int[] initialCounts,
            int[] aliveCounts,
            out WarSandboxBattlePhase phase,
            out int winnerTeamId)
        {
            return WarSandboxVictory.TryResolveAnnihilation(initialCounts, aliveCounts, out phase, out winnerTeamId);
        }

        [Test]
        public void RebuildArmyStatesWidensToTheScenarioTeamCount()
        {
            Assert.That(controller.IssueOrder(ArmyOrder.Hold(1)), Is.True);
            AddThirdArmy(40, new Vector3(0f, 0f, 60f));

            controller.RebuildArmyStates();

            ArmyRuntimeState third = controller.GetArmy(2);
            Assert.That(controller.ArmyCount, Is.EqualTo(3));
            Assert.That(third, Is.Not.Null);
            Assert.That(third.teamId, Is.EqualTo(2));
            Assert.That(third.initialUnitCount, Is.EqualTo(40));
            Assert.That(third.spawnCenter, Is.EqualTo(new Vector3(0f, 0f, 60f)));
            Assert.That(third.displayName, Is.Not.EqualTo(controller.GetArmy(0).displayName));
            Assert.That(controller.SelectArmy(2), Is.True);
            Assert.That(controller.GetArmy(3), Is.Null);

            // Widening must not drop what the armies that already existed were doing.
            Assert.That(controller.GetArmy(1).currentOrder.type, Is.EqualTo(ArmyOrderType.Hold));
            Assert.That(controller.GetArmy(0).initialUnitCount, Is.EqualTo(120));
        }

        [Test]
        public void ThirdArmyTakesOrdersEvenWithoutAFlowFieldOfItsOwn()
        {
            AddThirdArmy(40, new Vector3(0f, 0f, 60f));
            controller.RebuildArmyStates();

            // The engine sizes its per-team flow state in Initialize, which an unstarted manager
            // has not run: orders must still be accepted for a team whose slice does not exist yet.
            Assert.That(manager.NavigableTeamCount, Is.EqualTo(2));

            Assert.That(controller.IssueOrder(ArmyOrder.Attack(2)), Is.True);
            Assert.That(controller.GetArmy(2).currentOrder.type, Is.EqualTo(ArmyOrderType.Attack));

            Assert.That(controller.IssueOrder(ArmyOrder.Retreat(2)), Is.True);
            Assert.That(controller.GetArmy(2).currentOrder.target, Is.EqualTo(new Vector3(0f, 0f, 60f)));

            Assert.That(controller.IssueMoveOrder(2, new Vector3(5f, 0f, 5f), false), Is.True);
            Assert.That(controller.GetMoveRoutePointCount(2), Is.EqualTo(1));
        }

        private void AddThirdArmy(int count, Vector3 center)
        {
            // Reuses the SetUp unit types so TearDown still destroys every instance it created.
            scenario.unitTypes = new[]
            {
                scenario.unitTypes[0],
                scenario.unitTypes[1],
                CreateUnitType("ThirdArmy", 2, count, center)
            };
        }

        private TeamFlowFrameSettings InvokeBuildTeamFlowSettings(int teamId)
        {
            // Signature spelled out because there are two overloads now: the single-team
            // builder this test drives, and the array builder that fans it out over every team.
            MethodInfo method = typeof(MassEngineManager).GetMethod(
                "BuildTeamFlowSettings",
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                new[] { typeof(int), typeof(int), typeof(int) },
                null);
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
