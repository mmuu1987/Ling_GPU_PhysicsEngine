using System;
using System.Collections.Generic;
using MassEngine.Game.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MassEngine.Game.Tests
{
    public sealed class WarSandboxDeploymentPlanTests
    {
        private readonly List<ScriptableObject> objects = new List<ScriptableObject>();
        private GameObject root;
        private MassEngineManager manager;
        private ScenarioConfig scenario;
        private SimulationConfig simulation;
        private RuntimeFlowConfig flow;
        private WarSandboxDeploymentPlan plan;
        private UnitTypeConfig[] roster;
        private string assetFolder;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("DeploymentPlanTests");
            root.SetActive(false);
            manager = root.AddComponent<MassEngineManager>();
            scenario = Create<ScenarioConfig>();
            manager.scenarioConfig = scenario;
            manager.systemConfig = Create<MassEngineSystemConfig>();
            simulation = Create<SimulationConfig>();
            flow = Create<RuntimeFlowConfig>();
            manager.systemConfig.simulationConfig = simulation;
            manager.systemConfig.runtimeFlowConfig = flow;
            plan = Create<WarSandboxDeploymentPlan>();
            roster = new UnitTypeConfig[6];
            for (int i = 0; i < roster.Length; i++)
            {
                UnitTypeConfig unit = Create<UnitTypeConfig>();
                unit.teamId = i / 2;
                unit.spawnConfig = Create<SpawnConfig>();
                unit.spawnConfig.unitCount = 120 + i * 30;
                unit.spawnConfig.spawnCenter = new Vector3(-60f + i * 25f, 2f, 10f + i * 12f);
                unit.spawnConfig.formationDensity = 0.4f + i * 0.1f;
                unit.spawnConfig.formationAspect = 1f + i * 0.5f;
                unit.spawnConfig.spawnSize = i == 0 ? new Vector3(20f, 0f, 30f) : Vector3.zero;
                roster[i] = unit;
            }
            scenario.unitTypes = (UnitTypeConfig[])roster.Clone();
            simulation.simulationWorldSize = new Vector2(720f, 640f);
            simulation.boundaryPadding = 3f;
            simulation.cellSize = 3f;
            simulation.maxAgentsPerCell = 96;
            flow.flowFieldResolution = 256;
            flow.flowFieldCellSize = 3f;
            flow.flowFieldOrigin = new Vector2(-384f, -384f);
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < objects.Count; i++)
            {
                if (objects[i] != null)
                    Undo.ClearUndo(objects[i]);
            }
            if (root != null)
                Object.DestroyImmediate(root);
            if (!string.IsNullOrEmpty(assetFolder))
                AssetDatabase.DeleteAsset(assetFolder);
            foreach (ScriptableObject obj in objects)
            {
                if (obj != null && !AssetDatabase.Contains(obj))
                    Object.DestroyImmediate(obj);
            }
            objects.Clear();
            assetFolder = null;
        }

        [Test]
        public void CaptureDoesNotChangeTheLiveDeployment()
        {
            string before = EditorJsonUtility.ToJson(scenario);
            string spawnBefore = EditorJsonUtility.ToJson(roster[0].spawnConfig);
            string simulationBefore = EditorJsonUtility.ToJson(simulation);
            Capture();
            Assert.That(plan.UnitTypeCount, Is.EqualTo(6));
            Assert.That(plan.EngagementGap, Is.EqualTo(75f));
            Assert.That(EditorJsonUtility.ToJson(scenario), Is.EqualTo(before));
            Assert.That(EditorJsonUtility.ToJson(roster[0].spawnConfig), Is.EqualTo(spawnBefore));
            Assert.That(EditorJsonUtility.ToJson(simulation), Is.EqualTo(simulationBefore));
        }

        [Test]
        public void LoadRestoresMixedArmyRanksAndExactSpatialDimensions()
        {
            var spawnValues = new string[roster.Length];
            for (int i = 0; i < roster.Length; i++)
                spawnValues[i] = EditorJsonUtility.ToJson(roster[i].spawnConfig);
            string simulationValues = EditorJsonUtility.ToJson(simulation);
            string flowValues = EditorJsonUtility.ToJson(flow);
            Capture();

            scenario.unitTypes = new[] { roster[5] };
            foreach (UnitTypeConfig unit in roster)
            {
                unit.teamId = 8;
                unit.spawnConfig.unitCount = 1;
                unit.spawnConfig.spawnCenter = Vector3.zero;
                unit.spawnConfig.formationDensity = 1f;
                unit.spawnConfig.formationAspect = 6f;
                unit.spawnConfig.spawnSize = new Vector3(5f, 0f, 5f);
            }
            simulation.simulationWorldSize = Vector2.one * 100f;
            simulation.boundaryPadding = 9f;
            simulation.cellSize = 5f;
            simulation.maxAgentsPerCell = 8;
            flow.flowFieldResolution = 32;
            flow.flowFieldCellSize = 10f;
            flow.flowFieldOrigin = Vector2.zero;

            Apply();
            CollectionAssert.AreEqual(roster, scenario.unitTypes);
            for (int i = 0; i < roster.Length; i++)
            {
                Assert.That(roster[i].teamId, Is.EqualTo(i / 2));
                Assert.That(EditorJsonUtility.ToJson(roster[i].spawnConfig), Is.EqualTo(spawnValues[i]));
            }
            Assert.That(EditorJsonUtility.ToJson(simulation), Is.EqualTo(simulationValues));
            Assert.That(EditorJsonUtility.ToJson(flow), Is.EqualTo(flowValues));
        }

        [Test]
        public void LoadRestoresSpawnReferencesButLeavesCombatTemplatesShared()
        {
            SpawnConfig savedSpawn = roster[0].spawnConfig;
            Capture();
            SpawnConfig replacement = Create<SpawnConfig>();
            replacement.unitCount = 17;
            roster[0].spawnConfig = replacement;
            CombatConfig combat = Create<CombatConfig>();
            roster[0].combatConfig = combat;

            Apply();
            Assert.That(roster[0].spawnConfig, Is.SameAs(savedSpawn));
            Assert.That(replacement.unitCount, Is.EqualTo(17));
            Assert.That(roster[0].combatConfig, Is.SameAs(combat));
        }

        [Test]
        public void PlansKeepIndependentValuesAcrossRepeatedLoads()
        {
            Capture();
            string originalPlan = EditorJsonUtility.ToJson(plan);
            WarSandboxDeploymentPlan second = Create<WarSandboxDeploymentPlan>();
            roster[0].spawnConfig.unitCount = 999;
            roster[0].spawnConfig.spawnCenter = new Vector3(120f, 0f, -45f);
            Assert.That(second.TryCapture(manager, 150f, out string error), Is.True, error);

            for (int i = 0; i < 3; i++)
            {
                Apply();
                Assert.That(roster[0].spawnConfig.unitCount, Is.EqualTo(120));
                Assert.That(second.TryApply(manager, out error), Is.True, error);
                Assert.That(roster[0].spawnConfig.unitCount, Is.EqualTo(999));
                Assert.That(roster[0].spawnConfig.spawnCenter, Is.EqualTo(new Vector3(120f, 0f, -45f)));
            }
            Assert.That(EditorJsonUtility.ToJson(plan), Is.EqualTo(originalPlan));
        }

        [Test]
        public void SharedSpawnAndRepeatedUnitReferencesRoundTrip()
        {
            roster[1].spawnConfig = roster[0].spawnConfig;
            scenario.unitTypes = new[] { roster[0], roster[1], roster[0] };
            Capture();
            roster[0].spawnConfig.unitCount = 1;
            scenario.unitTypes = new UnitTypeConfig[0];
            Apply();
            Assert.That(scenario.unitTypes, Is.EqualTo(new[] { roster[0], roster[1], roster[0] }));
            Assert.That(roster[1].spawnConfig, Is.SameAs(roster[0].spawnConfig));
            Assert.That(roster[1].spawnConfig.unitCount, Is.EqualTo(120));
        }

        [Test]
        public void MissingSavedReferenceRejectsLoadBeforeAnyWrite()
        {
            Capture();
            Object.DestroyImmediate(roster[5].spawnConfig);
            roster[0].spawnConfig.unitCount = 555;
            roster[0].teamId = 7;
            simulation.simulationWorldSize = Vector2.one * 100f;
            Assert.That(plan.TryApply(manager, out string error), Is.False);
            Assert.That(error, Does.Contain("entry 6"));
            Assert.That(roster[0].spawnConfig.unitCount, Is.EqualTo(555));
            Assert.That(roster[0].teamId, Is.EqualTo(7));
            Assert.That(simulation.simulationWorldSize, Is.EqualTo(Vector2.one * 100f));
        }

        [Test]
        public void MissingTargetConfigRejectsLoadWithoutTouchingTheRoster()
        {
            Capture();
            manager.systemConfig.runtimeFlowConfig = null;
            roster[0].spawnConfig.unitCount = 555;
            Assert.That(plan.TryApply(manager, out string error), Is.False);
            Assert.That(error, Does.Contain("runtime flow"));
            Assert.That(roster[0].spawnConfig.unitCount, Is.EqualTo(555));
        }

        [Test]
        public void InvalidCaptureLeavesThePreviousPlanIntact()
        {
            Capture();
            string before = EditorJsonUtility.ToJson(plan);
            scenario.unitTypes[5] = null;
            Assert.That(plan.TryCapture(manager, 10f, out string error), Is.False);
            Assert.That(error, Does.Contain("entry 6"));
            Assert.That(EditorJsonUtility.ToJson(plan), Is.EqualTo(before));
            Apply();
            CollectionAssert.AreEqual(roster, scenario.unitTypes);
        }

        [Test]
        public void EmptyOrUnsupportedPlanIsNotApplied()
        {
            Assert.That(plan.TryApply(manager, out _), Is.False);
            Capture();
            var serialized = new SerializedObject(plan);
            serialized.FindProperty("version").intValue = 999;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            roster[0].spawnConfig.unitCount = 555;
            Assert.That(plan.TryApply(manager, out _), Is.False);
            Assert.That(roster[0].spawnConfig.unitCount, Is.EqualTo(555));
        }

        [Test]
        public void LoadCanBeUndoneAndRedoneInOneOperation()
        {
            Capture();
            Undo.FlushUndoRecordObjects();
            roster[0].spawnConfig.unitCount = 777;
            roster[0].teamId = 5;
            scenario.unitTypes = new[] { roster[4] };
            simulation.simulationWorldSize = Vector2.one * 200f;
            flow.flowFieldOrigin = Vector2.zero;

            Apply();
            Undo.FlushUndoRecordObjects();
            Undo.PerformUndo();
            Assert.That(roster[0].spawnConfig.unitCount, Is.EqualTo(777));
            Assert.That(roster[0].teamId, Is.EqualTo(5));
            Assert.That(scenario.unitTypes, Is.EqualTo(new[] { roster[4] }));
            Assert.That(simulation.simulationWorldSize, Is.EqualTo(Vector2.one * 200f));
            Assert.That(flow.flowFieldOrigin, Is.EqualTo(Vector2.zero));

            Undo.PerformRedo();
            Assert.That(roster[0].spawnConfig.unitCount, Is.EqualTo(120));
            Assert.That(roster[0].teamId, Is.Zero);
            CollectionAssert.AreEqual(roster, scenario.unitTypes);
            Assert.That(simulation.simulationWorldSize, Is.EqualTo(new Vector2(720f, 640f)));
            Assert.That(flow.flowFieldOrigin, Is.EqualTo(new Vector2(-384f, -384f)));
        }

        [Test]
        public void SavedAssetReloadsValuesAndReferencesFromDisk()
        {
            string name = "__DeploymentPlanTest_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", name);
            assetFolder = "Assets/" + name;
            for (int i = 0; i < objects.Count; i++)
                AssetDatabase.CreateAsset(objects[i], assetFolder + "/Object" + i + ".asset");
            Capture();
            AssetDatabase.SaveAssets();
            string copyPath = assetFolder + "/ReloadedPlan.asset";
            Assert.That(AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(plan), copyPath), Is.True);
            WarSandboxDeploymentPlan loaded = AssetDatabase.LoadAssetAtPath<WarSandboxDeploymentPlan>(copyPath);
            Assert.That(loaded, Is.Not.Null);
            Assert.That(loaded, Is.Not.SameAs(plan));
            roster[0].spawnConfig.unitCount = 555;
            scenario.unitTypes = new UnitTypeConfig[0];
            Assert.That(loaded.TryApply(manager, out string error), Is.True, error);
            Assert.That(loaded.EngagementGap, Is.EqualTo(75f));
            Assert.That(roster[0].spawnConfig.unitCount, Is.EqualTo(120));
            CollectionAssert.AreEqual(roster, scenario.unitTypes);
        }

        [TestCase("SaveAs")]
        [TestCase("SaveActive")]
        [TestCase("FolderOpened Icon")]
        public void PlanToolbarIconsExist(string iconName)
        {
            Assert.That(EditorGUIUtility.IconContent(iconName).image, Is.Not.Null);
        }

        private T Create<T>() where T : ScriptableObject
        {
            T obj = ScriptableObject.CreateInstance<T>();
            objects.Add(obj);
            return obj;
        }

        private void Capture()
        {
            Assert.That(plan.TryCapture(manager, 75f, out string error), Is.True, error);
        }

        private void Apply()
        {
            Assert.That(plan.TryApply(manager, out string error), Is.True, error);
        }
    }
}
