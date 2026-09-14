using System;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MassEngine.Game.Tests
{
    public sealed class WarSandboxLocalPlanTests
    {
        private string directory;
        private WarSandboxLocalPlanStore store;
        private WarSandboxBattlefieldCatalog catalog;
        private WarSandboxBattlefieldEntry battlefield;
        private WarSandboxBattlefieldConfig config;
        private UnitTypeConfig a, b;
        private WarSandboxDeploymentEntry[] entries;
        private readonly Vector2 world = new Vector2(200, 200);

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), "WarSandboxPlans-" + Guid.NewGuid().ToString("N"));
            store = new WarSandboxLocalPlanStore(directory);
            a = Unit("Melee", 0, -40); b = Unit("Ranged", 1, 40);
            config = ScriptableObject.CreateInstance<WarSandboxBattlefieldConfig>();
            battlefield = new WarSandboxBattlefieldEntry { id = "test-field", displayName = "Test",
                scenePath = "Assets/Game/Scenes/WarSandbox.unity", rules = config };
            catalog = ScriptableObject.CreateInstance<WarSandboxBattlefieldCatalog>();
            catalog.entries = new[] { battlefield }; catalog.defaultEntryId = battlefield.id;
            catalog.templates = new[] {
                new WarSandboxUnitTemplateEntry { templateId = "melee-veteran", revision = 2, config = a },
                new WarSandboxUnitTemplateEntry { templateId = "archer", revision = 1, config = b } };
            entries = new[] { WarSandboxDeploymentEntry.From(a), WarSandboxDeploymentEntry.From(b) };
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(a.spawnConfig); Object.DestroyImmediate(a);
            Object.DestroyImmediate(b.spawnConfig); Object.DestroyImmediate(b);
            Object.DestroyImmediate(config); Object.DestroyImmediate(catalog);
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }

        private static UnitTypeConfig Unit(string name, int team, float x)
        {
            var unit = ScriptableObject.CreateInstance<UnitTypeConfig>(); unit.name = name; unit.unitTypeName = name; unit.teamId = team;
            unit.spawnConfig = ScriptableObject.CreateInstance<SpawnConfig>();
            unit.spawnConfig.unitCount = 100; unit.spawnConfig.spawnCenter = new Vector3(x, 2, 0);
            return unit;
        }

        private WarSandboxPlanFile Plan(string slot = "A")
        {
            var plan = WarSandboxLocalPlanStore.Create(slot, "混编方案 " + slot, battlefield.id, battlefield.contentVersion,
                battlefield.terrainId, battlefield.terrainVersion, world, 2, config.rules, entries, catalog, out var error);
            Assert.That(plan, Is.Not.Null, error); return plan;
        }

        private bool Resolve(WarSandboxPlanFile plan, out WarSandboxDeploymentDraft draft, out string error) =>
            WarSandboxLocalPlanStore.TryResolve(plan, battlefield, catalog, world, 2, out draft, out error);

        [Test]
        public void DiskRoundTripPreservesCoordinatesFormationRulesAndSourceReferences()
        {
            var rules = config.rules; rules.gameMode = WarSandboxGameMode.ControlPoint;
            rules.controlPointCenter = new Vector3(1.25f, 7, 3.5f); rules.controlPointCaptureSeconds = 33;
            rules.staticObstacles = new[] { new StaticObstacleRect(new Vector2(0, 70), new Vector2(10, 10)) };
            config.rules = rules;
            entries[0].manualSize = new Vector3(20, 5, 20); entries[0].teamId = 3;
            var expected = Plan();
            string source = JsonUtility.ToJson(a) + JsonUtility.ToJson(a.spawnConfig) + JsonUtility.ToJson(config);
            Assert.That(store.TrySave(expected, false, out var error), Is.True, error);
            var reopened = new WarSandboxLocalPlanStore(directory);
            Assert.That(reopened.TryLoad("A", out var read, out error), Is.True, error);
            Assert.That(Resolve(read, out var draft, out error), Is.True, error);
            Assert.That(draft.Snapshot(), Is.EqualTo(entries));
            Assert.That(JsonUtility.ToJson(draft.Rules), Is.EqualTo(JsonUtility.ToJson(rules)));
            Assert.That(draft[0].template, Is.SameAs(a));
            Assert.That(JsonUtility.ToJson(a) + JsonUtility.ToJson(a.spawnConfig) + JsonUtility.ToJson(config), Is.EqualTo(source));
        }

        [Test]
        public void TemplateReorderingAndRenamingDoNotChangeIdentity()
        {
            var plan = Plan(); Array.Reverse(catalog.templates); a.name = "Renamed asset"; a.unitTypeName = "Renamed label";
            Assert.That(Resolve(plan, out var draft, out var error), Is.True, error);
            Assert.That(draft[0].template, Is.SameAs(a)); Assert.That(draft[1].template, Is.SameAs(b));
        }

        [Test]
        public void LoadingIsOneUndoOperationIncludingRulesAndBranchesHistory()
        {
            var originalRules = config.rules;
            var draft = new WarSandboxDeploymentDraft(entries, originalRules);
            var changed = (WarSandboxDeploymentEntry[])entries.Clone(); changed[0].count = 50;
            var nextRules = originalRules.Copy(); nextRules.gameMode = WarSandboxGameMode.ControlPoint;
            draft.Replace(changed, nextRules);
            Assert.That(draft.Rules.gameMode, Is.EqualTo(WarSandboxGameMode.ControlPoint));
            Assert.That(draft.Undo(), Is.True); Assert.That(draft.Snapshot(), Is.EqualTo(entries));
            Assert.That(draft.Rules.gameMode, Is.EqualTo(originalRules.gameMode));
            Assert.That(draft.Redo(), Is.True); Assert.That(draft[0].count, Is.EqualTo(50));
            draft.Undo(); draft.Set(0, changed[0]); Assert.That(draft.CanRedo, Is.False);
        }

        [Test]
        public void SaveAsPreservesBothFilesAndOverwriteRequiresExplicitPermission()
        {
            Assert.That(store.TrySave(Plan(), false, out var error), Is.True, error);
            byte[] original = File.ReadAllBytes(Path.Combine(directory, "A.json"));
            var replacement = Plan(); replacement.entries[0].count = 50;
            Assert.That(store.TrySave(replacement, false, out error), Is.False);
            Assert.That(File.ReadAllBytes(Path.Combine(directory, "A.json")), Is.EqualTo(original));
            Assert.That(store.TrySave(Plan("B"), false, out error), Is.True, error);
            Assert.That(store.TrySave(replacement, true, out error), Is.True, error);
            Assert.That(store.TryLoad("A", out var aPlan, out error), Is.True, error);
            Assert.That(store.TryLoad("B", out var bPlan, out error), Is.True, error);
            Assert.That(aPlan.entries[0].count, Is.EqualTo(50)); Assert.That(bPlan.entries[0].count, Is.EqualTo(100));
            Assert.That(store.ListSlots(), Is.EqualTo(new[] { "A", "B" }));
            Assert.That(Directory.GetFiles(directory, "*.tmp-*"), Is.Empty);
        }

        [Test]
        public void FailedReplacementPreservesOldFileAndCleansTemporaryFile()
        {
            Assert.That(store.TrySave(Plan(), false, out var error), Is.True, error);
            string path = Path.Combine(directory, "A.json"); byte[] original = File.ReadAllBytes(path);
            using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
                Assert.That(store.TrySave(Plan(), true, out error), Is.False);
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(original));
            Assert.That(Directory.GetFiles(directory, "*.tmp-*"), Is.Empty);
        }

        [Test]
        public void InvalidReplacementAndMissingFileDoNotAlterExistingPlans()
        {
            Assert.That(store.TrySave(Plan(), false, out var error), Is.True, error);
            string path = Path.Combine(directory, "A.json"); byte[] original = File.ReadAllBytes(path);
            var invalid = Plan(); invalid.entries[0].count = 0;
            Assert.That(store.TrySave(invalid, true, out error), Is.False);
            Assert.That(store.TryLoad("Missing", out var missing, out error), Is.False); Assert.That(missing, Is.Null);
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(original));
        }

        [TestCase("../A")]
        [TestCase("A/B")]
        [TestCase("A\\B")]
        [TestCase("CON")]
        [TestCase("nul")]
        [TestCase("LPT1")]
        [TestCase("")]
        [TestCase("A.")]
        [TestCase("C:A")]
        public void UnsafeOrReservedSlotNamesAreRejected(string slot)
        {
            Assert.That(store.TryLoad(slot, out var plan, out var error), Is.False);
            Assert.That(plan, Is.Null); Assert.That(error, Is.Not.Empty); Assert.That(Directory.Exists(directory), Is.False);
        }

        [TestCase("truncated")]
        [TestCase("missing-zero")]
        [TestCase("missing-bool")]
        [TestCase("empty-object")]
        [TestCase("null-rules")]
        [TestCase("null-obstacle")]
        [TestCase("null-entry")]
        [TestCase("future-version")]
        [TestCase("bad-position")]
        [TestCase("oversized")]
        [TestCase("renamed")]
        public void CorruptAndIncompleteFilesReturnNoPartialPlan(string kind)
        {
            var plan = Plan();
            if (kind == "null-rules") plan.rules = null;
            if (kind == "null-obstacle") plan.rules.staticObstacles = new WarSandboxPlanObstacle[] { null };
            if (kind == "null-entry") plan.entries[0] = null;
            if (kind == "future-version") plan.schemaVersion++;
            if (kind == "bad-position") plan.entries[0].center.x = float.PositiveInfinity;
            if (kind == "renamed") plan.planId = "B";
            string text = JsonUtility.ToJson(plan);
            if (kind == "truncated") text = text.Substring(0, text.Length / 2);
            if (kind == "missing-zero") text = text.Replace("\"teamId\":0,", "");
            if (kind == "missing-bool") text = text.Replace("\"staticObstaclesEnabled\":false,", "");
            if (kind == "empty-object") text = "{}";
            if (kind == "oversized") text = new string(' ', WarSandboxLocalPlanStore.MaxPlanBytes + 1);
            Directory.CreateDirectory(directory); File.WriteAllText(Path.Combine(directory, "A.json"), text);
            Assert.That(store.TryLoad("A", out var loaded, out var error), Is.False, kind);
            Assert.That(loaded, Is.Null); Assert.That(error, Is.Not.Empty);
            Assert.That(store.ListSlots(), Is.EqualTo(new[] { "A" }));
        }

        [TestCase("battlefield")]
        [TestCase("battlefield-version")]
        [TestCase("terrain")]
        [TestCase("terrain-version")]
        [TestCase("template")]
        [TestCase("template-version")]
        [TestCase("world")]
        [TestCase("padding")]
        [TestCase("overlap")]
        public void IncompatibleDependenciesCannotProduceALoadableDraft(string kind)
        {
            var plan = Plan();
            switch (kind)
            {
                case "battlefield": plan.battlefieldId = "other"; break;
                case "battlefield-version": plan.battlefieldVersion++; break;
                case "terrain": plan.terrainId = "mountains"; break;
                case "terrain-version": plan.terrainVersion++; break;
                case "template": plan.entries[0].templateId = "removed"; break;
                case "template-version": plan.entries[0].templateRevision++; break;
                case "world": plan.worldWidth++; break;
                case "padding": plan.boundaryPadding++; break;
                case "overlap": plan.entries[0].center = plan.entries[1].center; break;
            }
            Assert.That(Resolve(plan, out var loaded, out var error), Is.False);
            Assert.That(loaded, Is.Null); Assert.That(error, Is.Not.Empty);
        }

        [Test]
        public void CompleteFirstDocumentIsPreservedWhenParserIgnoresTrailingContent()
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "A.json"), JsonUtility.ToJson(Plan()) + "{}");
            Assert.That(store.TryLoad("A", out var plan, out var error), Is.True, error);
            Assert.That(Resolve(plan, out var draft, out error), Is.True, error);
            Assert.That(draft.Snapshot(), Is.EqualTo(entries));
        }

        [Test]
        public void DirectoryErrorsAreNotReportedAsEmptySuccessfulLibraries()
        {
            Directory.CreateDirectory(directory); string blocked = Path.Combine(directory, "blocked"); File.WriteAllText(blocked, "file");
            var unavailable = new WarSandboxLocalPlanStore(blocked);
            Assert.That(unavailable.TrySave(Plan(), false, out var error), Is.False); Assert.That(error, Is.Not.Empty);
            Assert.That(unavailable.TryListSlots(out _, out error), Is.False); Assert.That(error, Is.Not.Empty);
        }

        [Test]
        public void ShippingTemplateCatalogCoversEveryScenarioTemplate()
        {
            var shipping = AssetDatabase.LoadAssetAtPath<WarSandboxBattlefieldCatalog>("Assets/Game/Settings/BattlefieldCatalog.asset");
            var scenario = AssetDatabase.LoadAssetAtPath<ScenarioConfig>("Assets/Game/Settings/ScenarioConfig.asset");
            Assert.That(shipping.TryValidateTemplates(out var error), Is.True, error);
            foreach (var unit in scenario.unitTypes)
            {
                Assert.That(shipping.TryGetTemplateId(unit, out var id, out var revision), Is.True, unit.name);
                Assert.That(shipping.TryResolveTemplate(id, revision, out var resolved, out error), Is.True, error);
                Assert.That(resolved, Is.SameAs(unit));
            }
        }
    }
}
