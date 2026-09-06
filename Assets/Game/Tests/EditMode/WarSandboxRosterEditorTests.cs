using System.Collections.Generic;
using MassEngine.Game.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MassEngine.Game.Tests
{
    public sealed class WarSandboxRosterEditorTests
    {
        private readonly List<ScriptableObject> transientObjects = new List<ScriptableObject>();
        private readonly List<Object> persistentObjects = new List<Object>();
        private GameObject root;
        private string assetFolder;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("RosterEditorTests");
            root.SetActive(false);
        }

        [TearDown]
        public void TearDown()
        {
            Undo.ClearAll();
            if (root != null)
                Object.DestroyImmediate(root);
            if (!string.IsNullOrEmpty(assetFolder))
                AssetDatabase.DeleteAsset(assetFolder);
            for (int i = 0; i < transientObjects.Count; i++)
            {
                if (transientObjects[i] != null)
                    Object.DestroyImmediate(transientObjects[i]);
            }
            for (int i = 0; i < persistentObjects.Count; i++)
            {
                if (persistentObjects[i] != null && !AssetDatabase.Contains(persistentObjects[i]))
                    Object.DestroyImmediate(persistentObjects[i]);
            }
            transientObjects.Clear();
            persistentObjects.Clear();
            assetFolder = null;
        }

        [Test]
        public void EmptyAndSparseScenariosGetTheNextUnusedTeamId()
        {
            ScenarioConfig empty = CreateTransient<ScenarioConfig>();
            Assert.That(WarSandboxRosterEditor.ResolveNextTeamId(empty), Is.Zero);

            empty.unitTypes = new[]
            {
                CreateUnit(0),
                CreateUnit(2),
                null,
                CreateUnit(4)
            };
            Assert.That(WarSandboxRosterEditor.ResolveNextTeamId(empty), Is.EqualTo(5));
        }

        [Test]
        public void AddRejectsMissingOrUnsavedInputsWithoutMutation()
        {
            ScenarioConfig scenario = CreateTransient<ScenarioConfig>();
            scenario.unitTypes = new[] { CreateUnit(0) };
            UnitTypeConfig added;
            Assert.That(
                WarSandboxRosterEditor.TryAddUnitType(scenario, null, 1, "Missing", out added, out string error),
                Is.False);
            Assert.That(error, Does.Contain("template"));
            Assert.That(added, Is.Null);
            Assert.That(scenario.unitTypes.Length, Is.EqualTo(1));

            UnitTypeConfig template = CreateUnit(0);
            Assert.That(
                WarSandboxRosterEditor.TryAddUnitType(scenario, template, 1, "Unsaved", out added, out error),
                Is.False);
            Assert.That(error, Does.Contain("Save the unit type template"));
            Assert.That(scenario.unitTypes.Length, Is.EqualTo(1));
        }

        [Test]
        public void RemoveDetachesOnlyTheSelectedRosterEntryAndCanBeUndone()
        {
            ScenarioConfig scenario = CreateTransient<ScenarioConfig>();
            UnitTypeConfig first = CreateUnit(0);
            UnitTypeConfig second = CreateUnit(1);
            scenario.unitTypes = new[] { first, second, first };

            Assert.That(WarSandboxRosterEditor.RemoveUnitType(scenario, 1, out string error), Is.True, error);
            Undo.FlushUndoRecordObjects();
            Assert.That(scenario.unitTypes, Is.EqualTo(new[] { first, first }));
            Assert.That(WarSandboxRosterEditor.RemoveUnitType(scenario, 99, out error), Is.False);
            Assert.That(error, Does.Contain("no longer exists"));

            Undo.PerformUndo();
            CollectionAssert.AreEqual(new[] { first, second, first }, scenario.unitTypes);
        }

        [Test]
        public void AddCreatesAnIndependentSpawnAndKeepsTheTemplateSharedSettings()
        {
            CreatePersistentFolder();
            ScenarioConfig scenario = CreatePersistent<ScenarioConfig>("Scenario.asset");
            SpawnConfig templateSpawn = CreatePersistent<SpawnConfig>("Template_Spawn.asset");
            templateSpawn.unitCount = 240;
            templateSpawn.spawnCenter = new Vector3(-20f, 0f, 18f);
            templateSpawn.formationDensity = 0.75f;
            templateSpawn.formationAspect = 3f;
            UnitTypeConfig template = CreatePersistent<UnitTypeConfig>("Template.asset");
            template.unitTypeName = "Line Infantry";
            template.teamId = 0;
            template.spawnConfig = templateSpawn;
            scenario.unitTypes = new[] { template };
            AssetDatabase.SaveAssets();

            Assert.That(
                WarSandboxRosterEditor.TryAddUnitType(
                    scenario, template, 2, "Team 3 Line Infantry", out UnitTypeConfig added, out string error),
                Is.True, error);
            Assert.That(added, Is.Not.Null);
            Assert.That(added, Is.Not.SameAs(template));
            Assert.That(added.spawnConfig, Is.Not.SameAs(templateSpawn));
            Assert.That(added.teamId, Is.EqualTo(2));
            Assert.That(added.unitTypeName, Is.EqualTo("Team 3 Line Infantry"));
            Assert.That(added.spawnConfig.unitCount, Is.EqualTo(240));
            Assert.That(added.spawnConfig.spawnCenter, Is.EqualTo(new Vector3(-20f, 0f, 18f)));
            Assert.That(AssetDatabase.Contains(added), Is.True);
            Assert.That(AssetDatabase.Contains(added.spawnConfig), Is.True);

            added.spawnConfig.unitCount = 7;
            added.spawnConfig.spawnCenter = Vector3.zero;
            Assert.That(templateSpawn.unitCount, Is.EqualTo(240));
            Assert.That(templateSpawn.spawnCenter, Is.EqualTo(new Vector3(-20f, 0f, 18f)));
            Assert.That(scenario.unitTypes, Is.EqualTo(new[] { template, added }));
        }

        [Test]
        public void RemovingAClonedEntryDoesNotDeleteItsReusableAssets()
        {
            CreatePersistentFolder();
            ScenarioConfig scenario = CreatePersistent<ScenarioConfig>("Scenario.asset");
            SpawnConfig spawn = CreatePersistent<SpawnConfig>("Template_Spawn.asset");
            UnitTypeConfig template = CreatePersistent<UnitTypeConfig>("Template.asset");
            template.spawnConfig = spawn;
            scenario.unitTypes = new[] { template };
            AssetDatabase.SaveAssets();

            Assert.That(
                WarSandboxRosterEditor.TryAddUnitType(
                    scenario, template, 1, "Defender Variant", out UnitTypeConfig added, out string error),
                Is.True, error);
            string unitPath = AssetDatabase.GetAssetPath(added);
            string spawnPath = AssetDatabase.GetAssetPath(added.spawnConfig);
            Assert.That(WarSandboxRosterEditor.RemoveUnitType(scenario, 1, out error), Is.True, error);
            Assert.That(scenario.unitTypes, Is.EqualTo(new[] { template }));
            Assert.That(AssetDatabase.LoadAssetAtPath<UnitTypeConfig>(unitPath), Is.Not.Null);
            Assert.That(AssetDatabase.LoadAssetAtPath<SpawnConfig>(spawnPath), Is.Not.Null);
        }

        [Test]
        public void AddRejectsNegativeTeamIdBeforeCreatingAssets()
        {
            CreatePersistentFolder();
            ScenarioConfig scenario = CreatePersistent<ScenarioConfig>("Scenario.asset");
            SpawnConfig spawn = CreatePersistent<SpawnConfig>("Template_Spawn.asset");
            UnitTypeConfig template = CreatePersistent<UnitTypeConfig>("Template.asset");
            template.spawnConfig = spawn;
            scenario.unitTypes = new[] { template };
            AssetDatabase.SaveAssets();
            int assetCountBefore = AssetDatabase.FindAssets("t:Object", new[] { assetFolder }).Length;

            Assert.That(
                WarSandboxRosterEditor.TryAddUnitType(
                    scenario, template, -1, "Invalid", out UnitTypeConfig added, out string error),
                Is.False);
            Assert.That(error, Does.Contain("zero or greater"));
            Assert.That(added, Is.Null);
            Assert.That(AssetDatabase.FindAssets("t:Object", new[] { assetFolder }).Length, Is.EqualTo(assetCountBefore));
            Assert.That(scenario.unitTypes, Is.EqualTo(new[] { template }));
        }

        private T CreateTransient<T>() where T : ScriptableObject
        {
            T value = ScriptableObject.CreateInstance<T>();
            transientObjects.Add(value);
            return value;
        }

        private UnitTypeConfig CreateUnit(int teamId)
        {
            UnitTypeConfig unit = CreateTransient<UnitTypeConfig>();
            unit.teamId = teamId;
            unit.spawnConfig = CreateTransient<SpawnConfig>();
            return unit;
        }

        private void CreatePersistentFolder()
        {
            string folderName = "__RosterEditorTest_" + System.Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", folderName);
            assetFolder = "Assets/" + folderName;
        }

        private T CreatePersistent<T>(string fileName) where T : ScriptableObject
        {
            T value = ScriptableObject.CreateInstance<T>();
            string path = assetFolder + "/" + fileName;
            AssetDatabase.CreateAsset(value, path);
            persistentObjects.Add(value);
            return value;
        }
    }
}
