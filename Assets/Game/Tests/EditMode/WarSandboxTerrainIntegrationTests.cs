using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MassEngine.Game.Tests
{
    public sealed class WarSandboxTerrainIntegrationTests
    {
        private readonly List<Object> owned = new List<Object>();
        private GameObject root, sessionRoot;
        private MassEngineManager manager;
        private WarSandboxBattleController controller;
        private WarSandboxRuntimeDeployment deployment;
        private TerrainSurfaceAsset terrain;
        private WarSandboxBattlefieldEntry battlefield;
        private WarSandboxBattlefieldCatalog catalog;
        private ScenarioConfig source;
        private string directory;

        private T Own<T>() where T : ScriptableObject
        { var value = ScriptableObject.CreateInstance<T>(); owned.Add(value); return value; }

        [SetUp]
        public void SetUp()
        {
            terrain = Own<TerrainSurfaceAsset>(); InitializeTerrain(terrain, true);
            source = Own<ScenarioConfig>();
            source.unitTypes = new[] { Unit(0, -20), Unit(1, 20) };
            root = new GameObject("M62 CPU game tests"); root.SetActive(false);
            manager = root.AddComponent<MassEngineManager>(); manager.scenarioConfig = source; manager.terrainSurfaceAsset = terrain;
            var system = Own<MassEngineSystemConfig>(); system.simulationConfig = Own<SimulationConfig>();
            system.simulationConfig.simulationWorldSize = new Vector2(100, 100); system.simulationConfig.boundaryPadding = 0;
            system.runtimeFlowConfig = Own<RuntimeFlowConfig>(); system.runtimeFlowConfig.flowFieldOrigin = new Vector2(-50, -50);
            system.runtimeFlowConfig.flowFieldCellSize = 2; system.runtimeFlowConfig.flowFieldResolution = 50;
            manager.systemConfig = system;
            controller = root.AddComponent<WarSandboxBattleController>(); controller.manager = manager; controller.RebuildArmyStates();
            deployment = root.AddComponent<WarSandboxRuntimeDeployment>(); deployment.controller = controller;
            var rules = Own<WarSandboxBattlefieldConfig>();
            battlefield = new WarSandboxBattlefieldEntry { id = "m62-test", displayName = "M62", contentVersion = 3,
                scenePath = "Assets/Game/Experiments/LegacyScenes/M62Test.unity", terrainId = terrain.Id, terrainVersion = terrain.Version,
                terrainSurface = terrain, rules = rules };
            catalog = Own<WarSandboxBattlefieldCatalog>(); catalog.entries = new[] { battlefield }; catalog.defaultEntryId = battlefield.id;
            catalog.templates = new[] { new WarSandboxUnitTemplateEntry { templateId = "a", config = source.unitTypes[0] },
                new WarSandboxUnitTemplateEntry { templateId = "b", config = source.unitTypes[1] } };
            deployment.battlefieldCatalog = catalog;
            directory = Path.Combine(Path.GetTempPath(), "M62Game-" + Guid.NewGuid().ToString("N"));
            deployment.PlanStore = new WarSandboxLocalPlanStore(directory);
        }

        private UnitTypeConfig Unit(int team, float x)
        {
            var unit = Own<UnitTypeConfig>(); unit.name = "Unit " + team; unit.unitTypeName = unit.name; unit.teamId = team;
            unit.spawnConfig = Own<SpawnConfig>(); unit.spawnConfig.unitCount = 4;
            unit.spawnConfig.spawnCenter = new Vector3(x, 90, 0); unit.spawnConfig.spawnSize = new Vector3(4, 0, 4);
            return unit;
        }

        private static void InitializeTerrain(TerrainSurfaceAsset asset, bool barrier, int version = 1, Vector2? origin = null)
        {
            var heights = new float[21 * 21]; var blocked = new bool[20 * 20];
            for (int z = 0; z < 21; z++) for (int x = 0; x < 21; x++) heights[z * 21 + x] = 10 + x + z * .2f;
            if (barrier) for (int z = 0; z < 20; z++) blocked[z * 20 + 10] = true;
            asset.Initialize("mountain-test", version, 21, 21, origin ?? new Vector2(-50, -50), new Vector2(100, 100), 40, heights, blocked);
        }

        [TearDown]
        public void TearDown()
        {
            if (sessionRoot != null)
            {
                SetAutoProperty(typeof(WarSandboxSceneSession), null, "Instance", null);
                Object.DestroyImmediate(sessionRoot);
            }
            Object.DestroyImmediate(root);
            for (int i = owned.Count - 1; i >= 0; i--) if (owned[i] != null) Object.DestroyImmediate(owned[i]);
            owned.Clear();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
            Time.timeScale = 1;
        }

        private static void SetAutoProperty(Type type, object target, string name, object value) =>
            type.GetField("<" + name + ">k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static).SetValue(target, value);

        private void AttachSession()
        {
            Assert.That(WarSandboxSceneSession.Instance, Is.Null);
            sessionRoot = new GameObject("M62 CPU session"); sessionRoot.SetActive(false);
            var session = sessionRoot.AddComponent<WarSandboxSceneSession>(); session.catalog = catalog;
            SetAutoProperty(typeof(WarSandboxSceneSession), null, "Instance", session);
            SetAutoProperty(typeof(WarSandboxSceneSession), session, "State", WarSandboxEntryState.Battle);
            SetAutoProperty(typeof(WarSandboxSceneSession), session, "Controller", controller);
            SetAutoProperty(typeof(WarSandboxSceneSession), session, "CurrentEntryId", battlefield.id);
            typeof(WarSandboxSceneSession).GetField("activeEntry", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(session, battlefield.CopyIdentity());
        }

        [Test]
        public void CatalogRejectsStringOnlyWrongVersionAndFakeFlatProvider()
        {
            Assert.That(battlefield.TryValidate(null, out var error), Is.True, error);
            battlefield.terrainSurface = null;
            Assert.That(battlefield.TryValidate(null, out error), Is.False);
            battlefield.terrainSurface = terrain; battlefield.terrainVersion++;
            Assert.That(battlefield.TryValidate(null, out error), Is.False);
            battlefield.terrainId = "flat-ground"; battlefield.terrainVersion = 1;
            Assert.That(battlefield.TryValidate(null, out error), Is.False);
            battlefield.terrainSurface = null;
            Assert.That(battlefield.TryValidate(null, out error), Is.True, error);
            battlefield.terrainVersion = 2;
            Assert.That(battlefield.TryValidate(null, out error), Is.False);
        }

        [Test]
        public void SceneContractRejectsDifferentAssetWithSameStringsAndIncorrectSpace()
        {
            var request = battlefield.CopyIdentity();
            Assert.That(WarSandboxSceneSession.TryValidateTerrainProvider(manager, request, out var error), Is.True, error);
            var impostor = Own<TerrainSurfaceAsset>(); InitializeTerrain(impostor, true); manager.terrainSurfaceAsset = impostor;
            Assert.That(WarSandboxSceneSession.TryValidateTerrainProvider(manager, request, out error), Is.False);
            Assert.That(error, Does.Contain("asset"));
            manager.terrainSurfaceAsset = terrain;
            InitializeTerrain(terrain, true, 2, Vector2.zero); request.terrainVersion = 2;
            Assert.That(WarSandboxSceneSession.TryValidateTerrainProvider(manager, request, out error), Is.False);
            Assert.That(error, Does.Contain("origin/size"));
        }

        [Test]
        public void FrozenRequestSurvivesCatalogEditsButRejectsProviderRevisionChange()
        {
            var request = battlefield.CopyIdentity();
            battlefield.terrainId = "fake"; battlefield.terrainSurface = null; battlefield.contentVersion = 99;
            Assert.That(request.contentVersion, Is.EqualTo(3));
            Assert.That(WarSandboxSceneSession.TryValidateTerrainProvider(manager, request, out var error), Is.True, error);
            InitializeTerrain(terrain, true, 2);
            Assert.That(WarSandboxSceneSession.TryValidateTerrainProvider(manager, request, out error), Is.False);
            Assert.That(error, Does.Contain("identity/version"));
        }

        [Test]
        public void ExplicitFlatSceneRequiresBothNullProviderAndNullSurface()
        {
            var request = battlefield.CopyIdentity(); request.terrainId = "flat-ground"; request.terrainVersion = 1; request.terrainSurface = null;
            Assert.That(WarSandboxSceneSession.TryValidateTerrainProvider(manager, request, out _), Is.False);
            manager.terrainSurfaceAsset = null;
            Assert.That(WarSandboxSceneSession.TryValidateTerrainProvider(manager, request, out var error), Is.True, error);
            Assert.That(manager.TerrainSurface, Is.Null);
        }

        [Test]
        public void EntireFootprintIsCheckedAndFailedApplyLeavesDraftAndScenarioUntouched()
        {
            Assert.That(deployment.TryBeginEdit(false, out var error), Is.True, error);
            Assert.That(deployment.TryValidate(out error), Is.True, error);
            var entry = deployment.Draft[0]; entry.center.x = -8; entry.manualSize.x = 24;
            deployment.Draft.Set(0, entry);
            Assert.That(manager.TerrainNavigation.IsWalkable(new Vector2(entry.center.x, entry.center.z)), Is.True);
            int revision = deployment.Draft.Revision;
            Assert.That(deployment.TryApply(out error), Is.False); Assert.That(error, Does.Contain("脚印"));
            Assert.That(deployment.Draft.Revision, Is.EqualTo(revision));
            Assert.That(deployment.Draft[0], Is.EqualTo(entry));
            Assert.That(manager.scenarioConfig, Is.SameAs(source)); Assert.That(manager.Buffers, Is.Null);
        }

        [Test]
        public void CandidateObstacleNavigationRejectsReplacementWithoutTouchingHistoryOrLiveRules()
        {
            Assert.That(deployment.TryBeginEdit(false, out var error), Is.True, error);
            var before = deployment.Draft.Snapshot(); var rules = deployment.Draft.Rules;
            rules.staticObstaclesEnabled = true; rules.staticObstacleClearance = 0;
            rules.staticObstacles = new[] { new StaticObstacleRect(new Vector2(-17, 0), new Vector2(2, 2)) };
            var candidate = new WarSandboxDeploymentDraft(before, rules);
            Assert.That(candidate.TryValidate(deployment.WorldSize, rules, out error), Is.True, error);
            Assert.That(deployment.TryReplaceDraft(candidate, out error), Is.False);
            Assert.That(deployment.Draft.Snapshot(), Is.EqualTo(before)); Assert.That(deployment.Draft.CanUndo, Is.False);
            Assert.That(controller.staticObstaclesEnabled, Is.False); Assert.That(manager.scenarioConfig, Is.SameAs(source));
        }

        [Test]
        public void CandidateNavigationIsCachedAndRuleChangesInvalidateIt()
        {
            Assert.That(deployment.TryBeginEdit(false, out _), Is.True);
            var cache = new WarSandboxTerrainValidation();
            Assert.That(cache.TryValidate(manager, deployment.Draft, out _, out var error), Is.True, error);
            var field = typeof(WarSandboxTerrainValidation).GetField("cachedNavigation", BindingFlags.NonPublic | BindingFlags.Instance);
            var first = field.GetValue(cache);
            Assert.That(cache.TryValidate(manager, deployment.Draft, out _, out error), Is.True, error);
            Assert.That(field.GetValue(cache), Is.SameAs(first));
            var rules = deployment.Draft.Rules; rules.staticObstaclesEnabled = true;
            rules.staticObstacles = new[] { new StaticObstacleRect(new Vector2(-17, 0), new Vector2(2, 2)) };
            Assert.That(cache.TryValidate(manager, new WarSandboxDeploymentDraft(deployment.Draft.Snapshot(), rules), out _, out _), Is.False);
            Assert.That(field.GetValue(cache), Is.Not.SameAs(first));
        }

        [Test]
        public void RuntimeHeightIsSampledButAuthoredCoordinatesAndSourceArePreserved()
        {
            Assert.That(deployment.TryBeginEdit(false, out var error), Is.True, error);
            var before = deployment.Draft[0].center;
            Assert.That(deployment.TryApply(out error), Is.True, error);
            var actual = manager.scenarioConfig.unitTypes[0].spawnConfig.spawnCenter;
            Assert.That(manager.TryGetTerrainContext(out var surface, out _, out error), Is.True, error);
            Assert.That(surface.TrySample(new Vector2(before.x, before.z), out var sample), Is.True);
            Assert.That(actual, Is.EqualTo(sample.Position));
            Assert.That(source.unitTypes[0].spawnConfig.spawnCenter, Is.EqualTo(before));
            Assert.That(deployment.TryBeginEdit(false, out error), Is.True, error);
            Assert.That(deployment.Draft[0].center, Is.EqualTo(before));
        }

        [Test]
        public void BlockedUnreachableAndQueuedTargetsKeepPreviousOrderAndRoute()
        {
            Assert.That(controller.IssueMoveOrder(0, new Vector3(-30, -900, 0), false), Is.True, controller.CommandError);
            var before = controller.GetArmy(0).currentOrder;
            Assert.That(before.target.y, Is.Not.EqualTo(-900));
            Assert.That(controller.IssueMoveOrder(0, new Vector3(2, 0, 0), false), Is.False);
            Assert.That(controller.CommandError, Does.Contain("禁行"));
            Assert.That(controller.IssueMoveOrder(0, new Vector3(20, 0, 0), true), Is.False);
            Assert.That(controller.CommandError, Does.Contain("连通区"));
            Assert.That(controller.GetMoveRoutePointCount(0), Is.EqualTo(1));
            Assert.That(controller.GetArmy(0).currentOrder.target, Is.EqualTo(before.target));
            Assert.That(controller.IssueMoveOrder(0, new Vector3(-20, 400, 10), true), Is.True, controller.CommandError);
            Assert.That(controller.GetMoveRoutePointCount(0), Is.EqualTo(2));
            Assert.That(controller.GetArmy(0).currentOrder.target, Is.EqualTo(before.target));
        }

        [Test]
        public void UnreachableControlPointDefaultOrdersDoNotPartiallyStartBattle()
        {
            controller.gameMode = WarSandboxGameMode.ControlPoint; controller.controlPointCenter = new Vector3(-30, 0, 0);
            Assert.That(controller.StartDefaultBattle(), Is.False);
            Assert.That(controller.CommandError, Does.Contain("连通区"));
            Assert.That(controller.GetArmy(0).hasOrder, Is.False);
            Assert.That(controller.GetArmy(1).hasOrder, Is.False);
            Assert.That(manager.IsBattleRunning, Is.False);
        }

        [Test]
        public void EveryFormationNotJustArmyWeightedCenterMustReachTarget()
        {
            source.unitTypes[1].teamId = 0; controller.RebuildArmyStates();
            Assert.That(controller.IssueMoveOrder(0, new Vector3(-30, 0, 0), false), Is.False);
            Assert.That(controller.CommandError, Does.Contain("编成"));
            Assert.That(controller.GetArmy(0).hasOrder, Is.False);
        }

        [Test]
        public void TerrainClickAndGroundPresentationUseSurfaceWithoutAnyCollider()
        {
            var ray = new Ray(new Vector3(-20, 100, 0), Vector3.down);
            Assert.That(controller.TryRaycastGround(ray, 200, ~0, out var point, out var error), Is.True, error);
            Assert.That(controller.TryResolveGroundPoint(new Vector3(-20, -800, 0), out var ground, out error), Is.True, error);
            Assert.That(point, Is.EqualTo(ground)); Assert.That(point.y, Is.EqualTo(18).Within(1e-4));
            Assert.That(controller.TryRaycastGround(new Ray(new Vector3(200, 100, 0), Vector3.down), 200, ~0, out _, out error), Is.False);
        }

        [Test]
        public void FormatOneLoadValidatesCandidateTerrainBeforeReplacingDraftAndKeepsIdentityOnly()
        {
            AttachSession();
            Assert.That(deployment.TryBeginEdit(false, out var error), Is.True, error);
            Assert.That(deployment.TrySavePlan("valid", "有效山地", false, out error), Is.True, error);
            string json = File.ReadAllText(Path.Combine(directory, "valid.json"));
            Assert.That(json, Does.Contain("terrainId")); Assert.That(json, Does.Not.Contain("heights"));
            Assert.That(deployment.PlanStore.TryLoad("valid", out var plan, out error), Is.True, error);
            Assert.That(plan.schemaVersion, Is.EqualTo(1)); plan.planId = "blocked";
            plan.entries[0].center.x = 2;
            Assert.That(deployment.PlanStore.TrySave(plan, false, out error), Is.True, error);
            var before = deployment.Draft.Snapshot(); int revision = deployment.Draft.Revision;
            Assert.That(deployment.TryLoadPlan("blocked", out error), Is.False); Assert.That(error, Does.Contain("脚印"));
            Assert.That(deployment.Draft.Snapshot(), Is.EqualTo(before)); Assert.That(deployment.Draft.Revision, Is.EqualTo(revision));
            Assert.That(manager.scenarioConfig, Is.SameAs(source));
            // Editing the catalog after scene entry must not counterfeit the loaded identity.
            battlefield.terrainId = "fake"; battlefield.contentVersion++;
            Assert.That(deployment.TryLoadPlan("valid", out error), Is.True, error);
        }
    }
}
