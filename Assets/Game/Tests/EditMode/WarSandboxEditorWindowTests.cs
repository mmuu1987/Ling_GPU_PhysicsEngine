using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using MassEngine.Game.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace MassEngine.Game.Tests
{
    public sealed class WarSandboxEditorWindowTests
    {
        private readonly List<Object> objects = new List<Object>();
        private WarSandboxEditorWindow window;
        private MassEngineManager manager;
        private SpawnConfig firstSpawn;
        private SpawnConfig secondSpawn;
        private int previousFontSize;
        private string assetFolder;

        [SetUp]
        public void SetUp()
        {
            previousFontSize = EditorPrefs.GetInt("MassEngine.WarSandboxEditor.FontSize", 16);
            var go = new GameObject("Authoring UI Test"); objects.Add(go); go.SetActive(false);
            manager = go.AddComponent<MassEngineManager>();
            manager.scenarioConfig = Create<ScenarioConfig>();
            manager.systemConfig = Create<MassEngineSystemConfig>();
            manager.systemConfig.simulationConfig = Create<SimulationConfig>();
            manager.systemConfig.runtimeFlowConfig = Create<RuntimeFlowConfig>();
            var first = Create<UnitTypeConfig>(); var second = Create<UnitTypeConfig>();
            firstSpawn = first.spawnConfig = Create<SpawnConfig>(); secondSpawn = second.spawnConfig = Create<SpawnConfig>();
            first.unitTypeName = "前排近战"; second.unitTypeName = "远程部队"; second.teamId = 1;
            firstSpawn.unitCount = 100; secondSpawn.unitCount = 200;
            firstSpawn.spawnCenter = new Vector3(-50, 0, 0); secondSpawn.spawnCenter = new Vector3(50, 0, 0);
            manager.scenarioConfig.unitTypes = new[] { first, second };
            window = ScriptableObject.CreateInstance<WarSandboxEditorWindow>();
            SetField("manager", manager); SetField("page", 0);
            window.position = new Rect(80, 80, 1040, 820); window.Show(); window.CreateGUI();
        }

        [TearDown]
        public void TearDown()
        {
            if (window != null) window.Close();
            EditorPrefs.SetInt("MassEngine.WarSandboxEditor.FontSize", previousFontSize);
            Undo.ClearAll();
            if (!string.IsNullOrEmpty(assetFolder)) AssetDatabase.DeleteAsset(assetFolder);
            for (int i = objects.Count - 1; i >= 0; i--)
                if (objects[i] != null && !AssetDatabase.Contains(objects[i])) Object.DestroyImmediate(objects[i]);
            objects.Clear();
            assetFolder = null;
        }

        [Test]
        public void PagesOnlyExposeTheirTaskAndNavigationDoesNotWriteAssets()
        {
            string before = EditorJsonUtility.ToJson(manager.scenarioConfig) + EditorJsonUtility.ToJson(firstSpawn);
            Navigate(1);
            Assert.That(window.rootVisualElement.Q<IntegerField>("army-count"), Is.Not.Null);
            Assert.That(window.rootVisualElement.Q<Button>("auto-fit"), Is.Null);
            Navigate(2);
            Assert.That(window.rootVisualElement.Q<IntegerField>("army-count"), Is.Null);
            Assert.That(window.rootVisualElement.Q<Button>("auto-fit"), Is.Not.Null);
            Navigate(3);
            Assert.That(window.rootVisualElement.Q<Button>("load-plan").enabledSelf, Is.False);
            Navigate(4);
            window.rootVisualElement.Q<TextField>("help-search").value = "远程";
            Assert.That(EditorJsonUtility.ToJson(manager.scenarioConfig) + EditorJsonUtility.ToJson(firstSpawn), Is.EqualTo(before));
        }

        [Test]
        public void EditingSelectedArmyChangesOnlyItsSpawnAndCanUndo()
        {
            Navigate(1);
            window.rootVisualElement.Q<PopupField<string>>("army-selector").index = 1;
            window.rootVisualElement.Q<IntegerField>("army-count").value = 750;
            Assert.That(firstSpawn.unitCount, Is.EqualTo(100));
            Assert.That(secondSpawn.unitCount, Is.EqualTo(750));
            Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
            Assert.That(secondSpawn.unitCount, Is.EqualTo(200));
        }

        [Test]
        public void RecommendedFormationRestoresAutomaticFootprintWithoutMovingArmy()
        {
            firstSpawn.spawnSize = new Vector3(30, 0, 40); firstSpawn.formationDensity = 1f;
            Navigate(1);
            Click("recommended-formation");
            Assert.That(firstSpawn.HasManualFootprint, Is.False);
            Assert.That(firstSpawn.formationDensity, Is.EqualTo(0.5f));
            Assert.That(firstSpawn.unitCount, Is.EqualTo(100));
            Assert.That(firstSpawn.spawnCenter, Is.EqualTo(new Vector3(-50, 0, 0)));
        }

        [Test]
        public void RosterCanBePopulatedWhenUnitListIsNullAndMissingTemplatesExplainDisabledActions()
        {
            manager.scenarioConfig.unitTypes = null; SetField("addingArmy", true); Navigate(1);
            Assert.That(window.rootVisualElement.Q<PopupField<string>>("template-selector"), Is.Not.Null);
            Assert.That(window.rootVisualElement.Q<Button>("add-independent-army").enabledSelf, Is.False);
            Assert.That(window.rootVisualElement.Q<Button>("add-same-army").enabledSelf, Is.False);
        }

        [TestCase("MassEngine.Game.Tests.MissingWarSandboxUnit")]
        [TestCase("UnityEngine.GameObject")]
        public void TrialRejectsInvalidUnitImplementationWithoutChangingAssets(string className)
        {
            UnitTypeConfig unit = manager.scenarioConfig.unitTypes[0];
            unit.unitTypeClassName = className;
            string before = EditorJsonUtility.ToJson(unit) + EditorJsonUtility.ToJson(firstSpawn);

            Navigate(3);

            Assert.That(window.rootVisualElement.Q<Button>("start-play").enabledSelf, Is.False);
            Assert.That(window.rootVisualElement.Q<Button>("save-config").enabledSelf, Is.True);
            Assert.That(window.rootVisualElement.Q<Button>("save-plan").enabledSelf, Is.True);
            Assert.That(window.rootVisualElement.Query<Label>().ToList().Exists(label => label.text.Contains(className)), Is.True);
            Assert.That(EditorJsonUtility.ToJson(unit) + EditorJsonUtility.ToJson(firstSpawn), Is.EqualTo(before));
        }

        [Test]
        public void TrialAllowsDefaultImplementationAndOptionalConfigFallbacks()
        {
            manager.scenarioConfig.unitTypes[0].unitTypeClassName = string.Empty;
            Navigate(3);
            Assert.That(window.rootVisualElement.Q<Button>("start-play").enabledSelf, Is.True);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void RegainingFocusRefreshesTrialStatusAfterInspectorChanges(bool repaired)
        {
            UnitTypeConfig unit = manager.scenarioConfig.unitTypes[0];
            const string invalidClass = "MassEngine.Game.Tests.MissingWarSandboxUnit";
            unit.unitTypeClassName = repaired ? invalidClass : string.Empty;
            Navigate(3);
            Assert.That(window.rootVisualElement.Q<Button>("start-play").enabledSelf, Is.EqualTo(!repaired));

            unit.unitTypeClassName = repaired ? string.Empty : invalidClass;
            string before = EditorJsonUtility.ToJson(unit) + EditorJsonUtility.ToJson(firstSpawn);
            typeof(WarSandboxEditorWindow).GetMethod("OnFocus", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.Invoke(window, null);

            Assert.That(window.rootVisualElement.Q<Button>("start-play").enabledSelf, Is.EqualTo(repaired));
            Assert.That(EditorJsonUtility.ToJson(unit) + EditorJsonUtility.ToJson(firstSpawn), Is.EqualTo(before));
        }

        [Test]
        public void ConsecutiveAdditionsKeepTheTemplateAndUndoRestoresRosterAndStrengthTogether()
        {
            PersistConfigurations();
            UnitTypeConfig template = manager.scenarioConfig.unitTypes[0];
            SetField("rosterTemplate", template);
            Navigate(1); Click("new-roster"); Click("add-independent-army");
            UnitTypeConfig firstAdded = manager.scenarioConfig.unitTypes[2];
            Assert.That(firstAdded.teamId, Is.EqualTo(2));
            Assert.That(firstAdded.spawnConfig.unitCount, Is.EqualTo(1000));
            Assert.That(firstAdded.spawnConfig, Is.Not.SameAs(firstSpawn));
            Undo.FlushUndoRecordObjects();

            Click("new-roster");
            window.rootVisualElement.Q<IntegerField>("new-army-count").value = 350;
            Click("add-independent-army");
            UnitTypeConfig secondAdded = manager.scenarioConfig.unitTypes[3];
            Assert.That(secondAdded.teamId, Is.EqualTo(3));
            Assert.That(secondAdded.unitTypeName, Does.Not.Contain(firstAdded.unitTypeName));
            Assert.That(secondAdded.spawnConfig.unitCount, Is.EqualTo(350));
            Assert.That(secondAdded.spawnConfig, Is.Not.SameAs(firstAdded.spawnConfig));
            Assert.That(firstSpawn.unitCount, Is.EqualTo(100));
            Undo.FlushUndoRecordObjects(); Undo.PerformUndo();

            Assert.That(manager.scenarioConfig.unitTypes.Length, Is.EqualTo(3));
            Assert.That(firstAdded.spawnConfig.unitCount, Is.EqualTo(1000));
            Assert.That(AssetDatabase.Contains(secondAdded), Is.True);
            Assert.That(AssetDatabase.Contains(secondAdded.spawnConfig), Is.True);
            Undo.PerformRedo();
            Assert.That(manager.scenarioConfig.unitTypes[3], Is.SameAs(secondAdded));
            Assert.That(secondAdded.spawnConfig.unitCount, Is.EqualTo(350));
        }

        [Test]
        public void LoadingPlanThroughWindowUndoesGapRosterAndPositionsInOneStep()
        {
            var plan = Create<WarSandboxDeploymentPlan>();
            UnitTypeConfig[] originalRoster = (UnitTypeConfig[])manager.scenarioConfig.unitTypes.Clone();
            Assert.That(plan.TryCapture(manager, 80f, out string error), Is.True, error);
            Undo.FlushUndoRecordObjects(); Undo.ClearAll();
            firstSpawn.unitCount = 777;
            firstSpawn.spawnCenter = new Vector3(-120, 0, 35);
            manager.scenarioConfig.unitTypes = new[] { originalRoster[1] };
            SetField("engagementGap", 150f); SetField("deploymentPlan", plan);

            Navigate(3); Click("load-plan");
            // Finish the load's undo record before simulating the next UI event.
            Undo.FlushUndoRecordObjects();
            Assert.That(manager.scenarioConfig.unitTypes, Is.EqualTo(originalRoster));
            Assert.That(firstSpawn.unitCount, Is.EqualTo(100));
            Assert.That(firstSpawn.spawnCenter, Is.EqualTo(new Vector3(-50, 0, 0)));
            Navigate(2);
            Assert.That(window.rootVisualElement.Q<FloatField>("engagement-gap").value, Is.EqualTo(80f));
            Undo.FlushUndoRecordObjects(); Undo.PerformUndo();

            Assert.That(manager.scenarioConfig.unitTypes, Is.EqualTo(new[] { originalRoster[1] }));
            Assert.That(firstSpawn.unitCount, Is.EqualTo(777));
            Assert.That(firstSpawn.spawnCenter, Is.EqualTo(new Vector3(-120, 0, 35)));
            Assert.That(window.rootVisualElement.Q<FloatField>("engagement-gap").value, Is.EqualTo(150f));
            Undo.PerformRedo();
            Assert.That(manager.scenarioConfig.unitTypes, Is.EqualTo(originalRoster));
            Assert.That(firstSpawn.unitCount, Is.EqualTo(100));
            Assert.That(window.rootVisualElement.Q<FloatField>("engagement-gap").value, Is.EqualTo(80f));
        }

        [Test]
        public void MissingPlanDependencyLeavesWindowGapAndDeploymentUntouched()
        {
            var plan = Create<WarSandboxDeploymentPlan>();
            UnitTypeConfig first = manager.scenarioConfig.unitTypes[0];
            Assert.That(plan.TryCapture(manager, 80f, out string error), Is.True, error);
            Undo.FlushUndoRecordObjects(); Undo.ClearAll();
            Object.DestroyImmediate(secondSpawn);
            manager.scenarioConfig.unitTypes = new[] { first };
            firstSpawn.unitCount = 777;
            firstSpawn.spawnCenter = new Vector3(-120, 0, 35);
            SetField("engagementGap", 150f); SetField("deploymentPlan", plan);
            string before = EditorJsonUtility.ToJson(manager.scenarioConfig) + EditorJsonUtility.ToJson(firstSpawn);

            Navigate(3); Click("load-plan");

            Assert.That(EditorJsonUtility.ToJson(manager.scenarioConfig) + EditorJsonUtility.ToJson(firstSpawn), Is.EqualTo(before));
            Assert.That(window.rootVisualElement.Query<Label>().ToList().Exists(label => label.ClassListContains("error")), Is.True);
            Navigate(2);
            Assert.That(window.rootVisualElement.Q<FloatField>("engagement-gap").value, Is.EqualTo(150f));
        }

        [Test]
        public void AutoFitUsesExplicitManagerAndUndoRestoresItsDeployment()
        {
            Vector3 previousCenter = firstSpawn.spawnCenter;
            var otherObject = new GameObject("Other Manager"); objects.Add(otherObject); otherObject.SetActive(false);
            var other = otherObject.AddComponent<MassEngineManager>(); other.scenarioConfig = Create<ScenarioConfig>();
            var unrelated = Create<SpawnConfig>(); unrelated.spawnCenter = new Vector3(999, 0, 999);
            var unrelatedUnit = Create<UnitTypeConfig>(); unrelatedUnit.spawnConfig = unrelated;
            other.scenarioConfig.unitTypes = new[] { unrelatedUnit };
            Navigate(2); Click("auto-fit");
            Assert.That(firstSpawn.spawnCenter, Is.Not.EqualTo(previousCenter));
            Assert.That(unrelated.spawnCenter, Is.EqualTo(new Vector3(999, 0, 999)));
            Assert.That(secondSpawn.spawnCenter.x - secondSpawn.ResolveSpawnSize().x * 0.5f -
                (firstSpawn.spawnCenter.x + firstSpawn.ResolveSpawnSize().x * 0.5f), Is.EqualTo(50f).Within(0.001f));
            Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
            Assert.That(firstSpawn.spawnCenter, Is.EqualTo(previousCenter));
        }

        [UnityTest]
        public IEnumerator NarrowWindowKeepsLargeTextAndFitsFieldsWithinPage()
        {
            window.position = new Rect(80, 80, 560, 640);
            window.rootVisualElement.Q<PopupField<string>>("font-size").index = 2;
            Navigate(1);
            for (int i = 0; i < 5; i++) yield return null;
            Assert.That(window.rootVisualElement.ClassListContains("compact"), Is.True);
            var field = window.rootVisualElement.Q<IntegerField>("army-count");
            Assert.That(field.resolvedStyle.fontSize, Is.GreaterThanOrEqualTo(18));
            Assert.That(field.worldBound.width, Is.GreaterThan(200));
            Assert.That(field.worldBound.xMax, Is.LessThanOrEqualTo(window.rootVisualElement.worldBound.xMax + 1));
            Assert.That(window.rootVisualElement.Q<ScrollView>("page-scroll").resolvedStyle.height, Is.GreaterThan(200));
        }

        private T Create<T>() where T : ScriptableObject
        {
            T result = ScriptableObject.CreateInstance<T>(); objects.Add(result); return result;
        }

        [Test]
        public void RulesRemainAccessibleWithAnEmptyRosterAndCreationCanUndo()
        {
            manager.scenarioConfig.unitTypes = null;
            Navigate(2); Click("create-rule-controller");
            Assert.That(window.rootVisualElement.Q<FloatField>("rules-radius"), Is.Not.Null);
            Assert.That(manager.GetComponent<WarSandboxCommandHUD>(), Is.Not.Null);
            Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
            Assert.That(manager.GetComponent<WarSandboxBattleController>(), Is.Null);
            Assert.That(window.rootVisualElement.Q<Button>("create-rule-controller"), Is.Not.Null);
        }

        [Test]
        public void ChoosingRulesDoesNotLoadThemAndEditingDetachesWithoutChangingAsset()
        {
            var controller = manager.gameObject.AddComponent<WarSandboxBattleController>();
            controller.manager = manager;
            var rules = Create<WarSandboxBattlefieldConfig>(); rules.rules.controlPointRadius = 44;
            PersistConfigurations(); Navigate(2);
            var picker = window.rootVisualElement.Q<UnityEditor.UIElements.ObjectField>("rules-asset");
            picker.value = rules;
            Assert.That(controller.battlefieldConfig, Is.Null);
            Assert.That(controller.controlPointRadius, Is.EqualTo(30));
            Click("load-rules"); Undo.FlushUndoRecordObjects(); Undo.ClearAll();
            string original = EditorJsonUtility.ToJson(rules);
            window.rootVisualElement.Q<FloatField>("rules-radius").value = 55;
            Assert.That(controller.battlefieldConfig, Is.Null);
            Assert.That(controller.controlPointRadius, Is.EqualTo(55));
            Assert.That(EditorJsonUtility.ToJson(rules), Is.EqualTo(original));
            Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
            Assert.That(controller.battlefieldConfig, Is.SameAs(rules));
            Assert.That(controller.controlPointRadius, Is.EqualTo(44));
        }

        [Test]
        public void InvalidRulesBlockTrialButNotRuleRepairOrDeploymentSaving()
        {
            var controller = manager.gameObject.AddComponent<WarSandboxBattleController>(); controller.manager = manager;
            var rules = Create<WarSandboxBattlefieldConfig>(); rules.rules.controlPointRadius = 0;
            controller.battlefieldConfig = rules;
            Navigate(3);
            Assert.That(window.rootVisualElement.Q<Button>("start-play").enabledSelf, Is.False);
            Assert.That(window.rootVisualElement.Q<Button>("save-plan").enabledSelf, Is.True);
            Navigate(2);
            Assert.That(window.rootVisualElement.Q<FloatField>("rules-radius").enabledSelf, Is.True);
            window.rootVisualElement.Q<FloatField>("rules-radius").value = 36;
            Navigate(3);
            Assert.That(window.rootVisualElement.Q<Button>("start-play").enabledSelf, Is.True);
        }

        [Test]
        public void DisabledObstacleLayoutCanBeEditedAndUndoneWithoutEnablingIt()
        {
            var controller = manager.gameObject.AddComponent<WarSandboxBattleController>(); controller.manager = manager;
            Navigate(2);
            window.rootVisualElement.Q<Vector2Field>("obstacle-size-0").value = new Vector2(20, 60);
            Assert.That(controller.useCustomStaticObstacleLayout, Is.True);
            Assert.That(controller.staticObstaclesEnabled, Is.False);
            Assert.That(controller.staticObstacles[0].size, Is.EqualTo(new Vector2(20, 60)));
            Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
            Assert.That(controller.useCustomStaticObstacleLayout, Is.False);
            Assert.That(controller.CaptureBattlefieldRules().staticObstacles[0].size, Is.EqualTo(new Vector2(14, 110)));
        }

        [UnityTest]
        public IEnumerator NarrowRuleEditorKeepsItsInputsWithinPage()
        {
            var controller = manager.gameObject.AddComponent<WarSandboxBattleController>(); controller.manager = manager;
            window.position = new Rect(80, 80, 560, 640);
            window.rootVisualElement.Q<PopupField<string>>("font-size").index = 2;
            Navigate(2);
            for (int i = 0; i < 5; i++) yield return null;
            foreach (string fieldName in new[] { "rules-center", "rules-radius", "rules-asset" })
            {
                VisualElement field = window.rootVisualElement.Q(fieldName);
                Assert.That(field.worldBound.width, Is.GreaterThan(200), fieldName);
                Assert.That(field.worldBound.xMax, Is.LessThanOrEqualTo(window.rootVisualElement.worldBound.xMax + 1), fieldName);
            }
        }
        private void PersistConfigurations()
        {
            string folderName = "__WarSandboxWindowTest_" + System.Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", folderName);
            assetFolder = "Assets/" + folderName;
            for (int i = 0; i < objects.Count; i++)
                if (objects[i] is ScriptableObject) AssetDatabase.CreateAsset(objects[i], assetFolder + "/Config" + i + ".asset");
            AssetDatabase.SaveAssets();
        }
        private void SetField(string name, object value) => typeof(WarSandboxEditorWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(window, value);
        private void Navigate(int page) => typeof(WarSandboxEditorWindow).GetMethod("Navigate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(window, new object[] { page });
        private void Click(string name)
        {
            var button = window.rootVisualElement.Q<Button>(name);
            Assert.That(button.enabledInHierarchy, Is.True);
            // Editor panels translate keyboard submission through their host window. Invoke the
            // real Clickable action here so this test does not depend on operating-system focus.
            typeof(Clickable).GetMethod("Invoke", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(button.clickable, new object[] { null });
        }
    }
}
