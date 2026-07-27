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
