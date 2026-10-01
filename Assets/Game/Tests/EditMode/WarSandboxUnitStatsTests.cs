using System;
using System.IO;
using System.Linq;
using System.Text;
using MassEngine.Game.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MassEngine.Game.Tests
{
    /// <summary>Unit stat overrides: official &lt; global (template id) &lt; local (plan), clamped per field, never written to assets.</summary>
    public sealed class WarSandboxUnitStatsTests
    {
        private string directory;
        private UnitTypeConfig melee, ranged, stranger;
        private WarSandboxBattlefieldCatalog catalog;
        private WarSandboxBattlefieldEntry battlefield;
        private WarSandboxBattlefieldConfig rulesConfig;
        private ScenarioConfig source;
        private GameObject root;
        private MassEngineManager manager;
        private WarSandboxBattleController controller;
        private WarSandboxRuntimeDeployment editor;
        private MassEngineSystemConfig system;
        private readonly Vector2 world = new Vector2(200, 200);

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), "WarSandboxUnitStats-" + Guid.NewGuid().ToString("N"));
            melee = Unit("Knight", 0, -40, 0); ranged = Unit("Dragon", 1, 40, 14); stranger = Unit("Unlisted", 1, 0, 0);
            rulesConfig = ScriptableObject.CreateInstance<WarSandboxBattlefieldConfig>();
            battlefield = new WarSandboxBattlefieldEntry { id = "test-field", displayName = "Test", scenePath = "Assets/Game/Scenes/WarSandbox.unity", rules = rulesConfig };
            catalog = ScriptableObject.CreateInstance<WarSandboxBattlefieldCatalog>();
            catalog.entries = new[] { battlefield }; catalog.defaultEntryId = battlefield.id;
            catalog.templates = new[]
            {
                new WarSandboxUnitTemplateEntry { templateId = "knight", revision = 1, config = melee },
                new WarSandboxUnitTemplateEntry { templateId = "dragon", revision = 2, config = ranged }
            };
            source = ScriptableObject.CreateInstance<ScenarioConfig>(); source.unitTypes = new[] { melee, ranged };
            root = new GameObject("Unit Stat Tests"); root.SetActive(false);
            manager = root.AddComponent<MassEngineManager>(); manager.scenarioConfig = source; manager.enableGpuDispatch = true;
            system = ScriptableObject.CreateInstance<MassEngineSystemConfig>();
            system.simulationConfig = ScriptableObject.CreateInstance<SimulationConfig>();
            system.simulationConfig.simulationWorldSize = world;
            system.runtimeFlowConfig = ScriptableObject.CreateInstance<RuntimeFlowConfig>();
            manager.systemConfig = system;
            controller = root.AddComponent<WarSandboxBattleController>(); controller.manager = manager; controller.RebuildArmyStates();
            editor = root.AddComponent<WarSandboxRuntimeDeployment>(); editor.controller = controller; editor.battlefieldCatalog = catalog;
            editor.StatStore = new WarSandboxUnitStatStore(Path.Combine(directory, WarSandboxUnitStatStore.FileName));
            editor.PlanStore = new WarSandboxLocalPlanStore(Path.Combine(directory, "Plans"));
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(root);
            foreach (var unit in new[] { melee, ranged, stranger })
            {
                Object.DestroyImmediate(unit.spawnConfig); Object.DestroyImmediate(unit.combatConfig);
                Object.DestroyImmediate(unit.movementConfig); Object.DestroyImmediate(unit);
            }
            Object.DestroyImmediate(source); Object.DestroyImmediate(rulesConfig); Object.DestroyImmediate(catalog);
            Object.DestroyImmediate(system.simulationConfig); Object.DestroyImmediate(system.runtimeFlowConfig); Object.DestroyImmediate(system);
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
            Time.timeScale = 1;
        }

        private static UnitTypeConfig Unit(string name, int team, float x, float projectileRange)
        {
            var unit = ScriptableObject.CreateInstance<UnitTypeConfig>(); unit.name = name; unit.unitTypeName = name; unit.teamId = team;
            unit.spawnConfig = ScriptableObject.CreateInstance<SpawnConfig>(); unit.spawnConfig.unitCount = 100; unit.spawnConfig.spawnCenter = new Vector3(x, 0, 0);
            unit.combatConfig = ScriptableObject.CreateInstance<CombatConfig>();
            unit.combatConfig.maxHp = 100; unit.combatConfig.attackDamage = 10; unit.combatConfig.attackInterval = 1; unit.combatConfig.attackRange = 1.5f;
            unit.combatConfig.projectileRange = projectileRange; unit.combatConfig.chargeDamageMultiplier = 2;
            unit.movementConfig = ScriptableObject.CreateInstance<MovementConfig>(); unit.movementConfig.maxSpeed = 4;
            return unit;
        }

        private WarSandboxGlobalStats Global(string id, WarSandboxUnitStat stat, float value)
        {
            var global = new WarSandboxGlobalStats(); var set = new WarSandboxStatSet(); set.Set(stat, value);
            global.Replace(id, 1, set); return global;
        }

        private static WarSandboxDeploymentEntry[] Entries(params UnitTypeConfig[] units) => units.Select(WarSandboxDeploymentEntry.From).ToArray();

        // ---------- resolution ----------

        [Test]
        public void WithoutOverridesTheAuthoredSubConfigsAreUsedUnchanged()
        {
            string before = JsonUtility.ToJson(melee.combatConfig) + JsonUtility.ToJson(melee.movementConfig);
            foreach (var resolver in new[] { null, new WarSandboxStatResolver(catalog, new WarSandboxGlobalStats(), new WarSandboxStatOverrides()) })
                using (var instance = new WarSandboxDeploymentInstance(Entries(melee, ranged), resolver))
                {
                    Assert.That(instance.HasCustomStats, Is.False);
                    Assert.That(instance.Scenario.unitTypes[0].combatConfig, Is.SameAs(melee.combatConfig));
                    Assert.That(instance.Scenario.unitTypes[0].movementConfig, Is.SameAs(melee.movementConfig));
                    Assert.That(instance.Scenario.unitTypes[1].combatConfig, Is.SameAs(ranged.combatConfig));
                }
            Assert.That(JsonUtility.ToJson(melee.combatConfig) + JsonUtility.ToJson(melee.movementConfig), Is.EqualTo(before));
        }

        [Test]
        public void LocalBeatsGlobalBeatsOfficialFieldByField()
        {
            var global = Global("knight", WarSandboxUnitStat.MaxHp, 200);
            var set = global.Get("knight"); set.Set(WarSandboxUnitStat.AttackDamage, 20); global.Replace("knight", 1, set);
            var local = new WarSandboxStatOverrides(); local.Set(melee, WarSandboxUnitStat.MaxHp, 300);
            var resolver = new WarSandboxStatResolver(catalog, global, local);
            var hp = resolver.Resolve(melee, WarSandboxUnitStats.Get(WarSandboxUnitStat.MaxHp));
            var damage = resolver.Resolve(melee, WarSandboxUnitStats.Get(WarSandboxUnitStat.AttackDamage));
            var speed = resolver.Resolve(melee, WarSandboxUnitStats.Get(WarSandboxUnitStat.MaxSpeed));
            Assert.That((hp.effective, hp.source, hp.global, hp.official), Is.EqualTo((300f, WarSandboxStatSource.Local, 200f, 100f)));
            Assert.That((damage.effective, damage.source), Is.EqualTo((20f, WarSandboxStatSource.Global)));
            Assert.That((speed.effective, speed.source), Is.EqualTo((4f, WarSandboxStatSource.Official)));
            using (var instance = new WarSandboxDeploymentInstance(Entries(melee, ranged), resolver))
            {
                var tuned = instance.Scenario.unitTypes[0];
                Assert.That(tuned.combatConfig, Is.Not.SameAs(melee.combatConfig));
                Assert.That((tuned.combatConfig.maxHp, tuned.combatConfig.attackDamage), Is.EqualTo((300, 20)));
                Assert.That(tuned.combatConfig.attackRange, Is.EqualTo(1.5f), "Untouched fields are copied from the official asset.");
                Assert.That(tuned.movementConfig, Is.SameAs(melee.movementConfig), "Movement is cloned only when a movement field changes.");
                Assert.That(instance.Scenario.unitTypes[1].combatConfig, Is.SameAs(ranged.combatConfig));
                Assert.That(instance.CustomTemplateCount, Is.EqualTo(1));
            }
            Assert.That((melee.combatConfig.maxHp, melee.combatConfig.attackDamage), Is.EqualTo((100, 10)), "Assets are never written.");
        }

        [Test]
        public void GlobalLayerMatchesByTemplateIdOnly()
        {
            var resolver = new WarSandboxStatResolver(catalog, Global("knight", WarSandboxUnitStat.MaxSpeed, 9), null);
            Assert.That(resolver.Resolve(melee, WarSandboxUnitStats.Get(WarSandboxUnitStat.MaxSpeed)).effective, Is.EqualTo(9f));
            Assert.That(resolver.HasCustom(stranger), Is.False, "Templates without a catalog id cannot receive global values.");
            Assert.That(resolver.HasCustom(ranged), Is.False);
            using (var instance = new WarSandboxDeploymentInstance(Entries(melee), resolver))
                Assert.That(instance.Scenario.unitTypes[0].movementConfig.maxSpeed, Is.EqualTo(9f));
        }

        [Test]
        public void ValuesAreClampedAndRoundedPerField()
        {
            var set = new WarSandboxStatSet();
            set.Set(WarSandboxUnitStat.MaxHp, 1e9f); set.Set(WarSandboxUnitStat.AttackInterval, 0);
            set.Set(WarSandboxUnitStat.AttackDamage, 12.6f); set.Set(WarSandboxUnitStat.MaxSpeed, float.NaN);
            set.Set(WarSandboxUnitStat.ChargeDamageMultiplier, 0.2f);
            Assert.That(set.TryGet(WarSandboxUnitStat.MaxHp, out float hp) && hp == 100000);
            Assert.That(set.TryGet(WarSandboxUnitStat.AttackInterval, out float interval) && interval == 0.1f);
            Assert.That(set.TryGet(WarSandboxUnitStat.AttackDamage, out float damage) && damage == 13);
            Assert.That(set.TryGet(WarSandboxUnitStat.MaxSpeed, out float speed) && speed == WarSandboxUnitStats.Get(WarSandboxUnitStat.MaxSpeed).Min);
            Assert.That(set.TryGet(WarSandboxUnitStat.ChargeDamageMultiplier, out float charge) && charge == 1);
        }

        [Test]
        public void ChargeIsMeleeOnlyAndProjectileFieldsAreRangedOnly()
        {
            var charge = WarSandboxUnitStats.Get(WarSandboxUnitStat.ChargeDamageMultiplier);
            var splash = WarSandboxUnitStats.Get(WarSandboxUnitStat.ProjectileSplashRadius);
            Assert.That(WarSandboxUnitStats.Applies(melee, charge), Is.True); Assert.That(WarSandboxUnitStats.Applies(melee, splash), Is.False);
            Assert.That(WarSandboxUnitStats.Applies(ranged, charge), Is.False); Assert.That(WarSandboxUnitStats.Applies(ranged, splash), Is.True);
            var draft = new WarSandboxDeploymentDraft(Entries(melee, ranged));
            Assert.That(draft.SetStat(ranged, WarSandboxUnitStat.ChargeDamageMultiplier, 3), Is.False);
            Assert.That(draft.SetStat(melee, WarSandboxUnitStat.ProjectileSpeed, 30), Is.False);
            Assert.That(draft.SetStat(ranged, WarSandboxUnitStat.ProjectileSplashRadius, 5), Is.True);
        }

        [Test]
        public void SameTemplateInSeveralCompositionsSharesOneTunedCopy()
        {
            var local = new WarSandboxStatOverrides(); local.Set(melee, WarSandboxUnitStat.AttackDamage, 50);
            var entries = Entries(melee, ranged, melee); entries[2].center = new Vector3(-40, 0, 60);
            using (var instance = new WarSandboxDeploymentInstance(entries, new WarSandboxStatResolver(catalog, null, local)))
            {
                Assert.That(instance.Scenario.unitTypes[0].combatConfig, Is.SameAs(instance.Scenario.unitTypes[2].combatConfig));
                Assert.That(instance.Scenario.unitTypes[2].combatConfig.attackDamage, Is.EqualTo(50));
                Assert.That(instance.CustomTemplateCount, Is.EqualTo(1));
            }
        }

        // ---------- draft history ----------

        [Test]
        public void StatEditsAreUndoableAndAContinuousDragIsOneStep()
        {
            var draft = new WarSandboxDeploymentDraft(Entries(melee, ranged));
            Assert.That(draft.SetStat(melee, WarSandboxUnitStat.MaxHp, 150), Is.True);
            Assert.That(draft.SetStat(melee, WarSandboxUnitStat.MaxHp, 160), Is.True);
            Assert.That(draft.SetStat(melee, WarSandboxUnitStat.MaxHp, 170), Is.True);
            Assert.That(draft.SetStat(melee, WarSandboxUnitStat.MaxHp, 170), Is.False, "Unchanged values do not create history.");
            Assert.That(draft.SetStat(melee, WarSandboxUnitStat.AttackDamage, 30), Is.True);
            Assert.That(draft.Undo(), Is.True);
            Assert.That(draft.TryGetStat(melee, WarSandboxUnitStat.AttackDamage, out _), Is.False);
            Assert.That(draft.TryGetStat(melee, WarSandboxUnitStat.MaxHp, out float hp) && hp == 170);
            Assert.That(draft.Undo(), Is.True);
            Assert.That(draft.Stats.IsEmpty, Is.True, "The whole drag is undone at once.");
            Assert.That(draft.Redo(), Is.True); Assert.That(draft.TryGetStat(melee, WarSandboxUnitStat.MaxHp, out hp) && hp == 170);
            var e = draft[0]; e.count = 50; draft.Set(0, e);
            Assert.That(draft.SetStat(melee, WarSandboxUnitStat.MaxHp, 180), Is.True);
            Assert.That(draft.Undo(), Is.True); Assert.That(draft.TryGetStat(melee, WarSandboxUnitStat.MaxHp, out hp) && hp == 170,
                "An entry edit in between starts a new step.");
            Assert.That(draft.ClearTemplateStats(melee), Is.True); Assert.That(draft.Stats.IsEmpty, Is.True);
            Assert.That(draft.Undo(), Is.True); Assert.That(draft.Stats.IsEmpty, Is.False);
        }

        // ---------- plans ----------

        private WarSandboxPlanFile Plan(WarSandboxStatOverrides stats, string slot = "A") =>
            WarSandboxLocalPlanStore.Create(slot, "方案 " + slot, battlefield.id, battlefield.contentVersion, battlefield.terrainId,
                battlefield.terrainVersion, world, 0, rulesConfig.rules, Entries(melee, ranged), catalog, stats, out _);

        [Test]
        public void PlanRoundTripCarriesLocalOverridesByTemplateId()
        {
            var stats = new WarSandboxStatOverrides();
            stats.Set(melee, WarSandboxUnitStat.MaxHp, 321); stats.Set(ranged, WarSandboxUnitStat.ProjectileSplashRadius, 6);
            stats.Set(stranger, WarSandboxUnitStat.MaxHp, 999);
            var store = new WarSandboxLocalPlanStore(Path.Combine(directory, "Plans"));
            Assert.That(store.TrySave(Plan(stats), false, out string error), Is.True, error);
            string text = File.ReadAllText(Path.Combine(directory, "Plans", "A.json"));
            Assert.That(text, Does.Contain("\"statOverrides\"").And.Contain("\"knight\"").And.Contain("\"projectileSplashRadius\""));
            Assert.That(text, Does.Not.Contain("Unlisted"), "Templates not deployed by the plan are not saved.");
            Assert.That(store.TryLoad("A", out var loaded, out error), Is.True, error);
            Assert.That(WarSandboxLocalPlanStore.TryResolve(loaded, battlefield, catalog, world, 0, out var draft, out error), Is.True, error);
            Assert.That(draft.TryGetStat(melee, WarSandboxUnitStat.MaxHp, out float hp) && hp == 321);
            Assert.That(draft.TryGetStat(ranged, WarSandboxUnitStat.ProjectileSplashRadius, out float splash) && splash == 6);
        }

        [Test]
        public void PlansWithoutOverridesKeepTheLegacyFileShapeAndOldFilesStillLoad()
        {
            var store = new WarSandboxLocalPlanStore(Path.Combine(directory, "Plans"));
            Assert.That(store.TrySave(Plan(null, "Old"), false, out string error), Is.True, error);
            Assert.That(store.TrySave(Plan(new WarSandboxStatOverrides(), "Empty"), false, out error), Is.True, error);
            byte[] old = File.ReadAllBytes(Path.Combine(directory, "Plans", "Old.json"));
            string empty = File.ReadAllText(Path.Combine(directory, "Plans", "Empty.json"), Encoding.UTF8);
            Assert.That(Encoding.UTF8.GetString(old), Does.Not.Contain("statOverrides"));
            Assert.That(empty, Does.Not.Contain("statOverrides"));
            Assert.That(store.TryLoad("Old", out var loaded, out error), Is.True, error);
            Assert.That(loaded.statOverrides, Is.Null);
            Assert.That(WarSandboxLocalPlanStore.TryResolve(loaded, battlefield, catalog, world, 0, out var draft, out error), Is.True, error);
            Assert.That(draft.Stats.IsEmpty, Is.True);
        }

        [Test]
        public void UnknownOrInapplicableStatsInAPlanAreIgnoredButMalformedEntriesAreRejected()
        {
            var plan = Plan(null);
            plan.statOverrides = new[] { new WarSandboxTemplateStats { valuesVersion = 1, templateId = "knight", templateRevision = 1, stats = new[] {
                new WarSandboxStatValue { stat = "futureField", value = 5 },
                new WarSandboxStatValue { stat = "projectileSpeed", value = 50 },
                new WarSandboxStatValue { stat = "attackDamage", value = 77 } } } };
            Assert.That(WarSandboxLocalPlanStore.TryResolve(plan, battlefield, catalog, world, 0, out var draft, out string error), Is.True, error);
            var set = draft.Stats.Get(melee);
            Assert.That(set.Count, Is.EqualTo(1)); Assert.That(set.TryGet(WarSandboxUnitStat.AttackDamage, out float damage) && damage == 77);
            plan.statOverrides = new[] { plan.statOverrides[0], plan.statOverrides[0] };
            Assert.That(WarSandboxLocalPlanStore.TryValidatePlan(plan, out _), Is.False, "Duplicate template ids are ambiguous.");
            plan.statOverrides = new[] { new WarSandboxTemplateStats { valuesVersion = 1, templateId = " ", templateRevision = 1, stats = new WarSandboxStatValue[0] } };
            Assert.That(WarSandboxLocalPlanStore.TryValidatePlan(plan, out _), Is.False);
        }

        // ---------- global store ----------

        [Test]
        public void MissingGlobalFileMeansOfficialValuesAndCreatesNothing()
        {
            var store = new WarSandboxUnitStatStore(Path.Combine(directory, "none", WarSandboxUnitStatStore.FileName));
            Assert.That(store.Current.IsEmpty, Is.True); Assert.That(store.Warning, Is.Null); Assert.That(store.CanUndo, Is.False);
            Assert.That(Directory.Exists(Path.Combine(directory, "none")), Is.False);
        }

        [Test]
        public void SaveKeepsOneUndoLevelAndLeavesNoTemporaryFiles()
        {
            var store = editor.StatStore;
            Assert.That(store.TrySave(Global("knight", WarSandboxUnitStat.MaxHp, 111), "a", out string error), Is.True, error);
            Assert.That(store.TrySave(Global("knight", WarSandboxUnitStat.MaxHp, 222), "b", out error), Is.True, error);
            var reopened = new WarSandboxUnitStatStore(store.FilePath);
            Assert.That(reopened.Current.TryGet("knight", WarSandboxUnitStat.MaxHp, out float hp) && hp == 222);
            Assert.That(reopened.Current.LastChange, Is.EqualTo("b"));
            Assert.That(reopened.TryUndo(out error), Is.True, error);
            Assert.That(new WarSandboxUnitStatStore(store.FilePath).Current.TryGet("knight", WarSandboxUnitStat.MaxHp, out hp) && hp == 111);
            Assert.That(reopened.CanUndo, Is.False);
            Assert.That(Directory.GetFiles(directory).Any(f => f.Contains(".tmp-")), Is.False);
        }

        [Test]
        public void FirstSaveCanBeUndoneBackToNoOverrides()
        {
            var store = editor.StatStore;
            Assert.That(store.TrySave(Global("dragon", WarSandboxUnitStat.ProjectileSplashRadius, 7), "push", out string error), Is.True, error);
            Assert.That(store.TryUndo(out error), Is.True, error);
            Assert.That(new WarSandboxUnitStatStore(store.FilePath).Current.IsEmpty, Is.True);
        }

        [TestCase("{not json")]
        [TestCase("{\"schemaVersion\":99,\"templates\":[]}")]
        [TestCase("{\"schemaVersion\":1}")]
        [TestCase("")]
        public void CorruptGlobalFileIsQuarantinedAndIgnored(string content)
        {
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, WarSandboxUnitStatStore.FileName);
            File.WriteAllText(path, content);
            var store = new WarSandboxUnitStatStore(path);
            Assert.That(store.Current.IsEmpty, Is.True);
            Assert.That(store.Warning, Does.Contain("官方数值"));
            Assert.That(File.Exists(path), Is.False);
            Assert.That(Directory.GetFiles(directory, WarSandboxUnitStatStore.FileName + ".broken-*").Length, Is.EqualTo(1));
        }

        [Test]
        public void UnknownGlobalFieldsAreSkippedWithAWarning()
        {
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, WarSandboxUnitStatStore.FileName);
            File.WriteAllText(path, "{\"schemaVersion\":1,\"templates\":[{\"valuesVersion\":1,\"templateId\":\"knight\",\"templateRevision\":1," +
                "\"stats\":[{\"stat\":\"maxHp\",\"value\":250},{\"stat\":\"laserPower\",\"value\":9}]}]}");
            var store = new WarSandboxUnitStatStore(path);
            Assert.That(store.Current.TryGet("knight", WarSandboxUnitStat.MaxHp, out float hp) && hp == 250);
            Assert.That(store.Warning, Does.Contain("1 项"));
            Assert.That(File.Exists(path), Is.True);
        }

        // ---------- runtime integration ----------

        [Test]
        public void ApplyUsesDraftAndGlobalLayersAndReeditKeepsLocalValues()
        {
            Assert.That(editor.StatStore.TrySave(Global("dragon", WarSandboxUnitStat.AttackDamage, 90), "g", out string error), Is.True, error);
            Assert.That(editor.TryBeginEdit(false, out error), Is.True, error);
            Assert.That(editor.SetStat(melee, WarSandboxUnitStat.MaxSpeed, 7), Is.True);
            Assert.That(editor.SetStat(stranger, WarSandboxUnitStat.MaxSpeed, 7), Is.False, "Only templates of this roster are editable.");
            Assert.That(editor.TryApply(out error), Is.True, error);
            var units = manager.scenarioConfig.unitTypes;
            Assert.That(units[0].movementConfig.maxSpeed, Is.EqualTo(7f)); Assert.That(units[1].combatConfig.attackDamage, Is.EqualTo(90));
            Assert.That(melee.movementConfig.maxSpeed, Is.EqualTo(4f)); Assert.That(ranged.combatConfig.attackDamage, Is.EqualTo(10));
            Assert.That(editor.HasCustomStats, Is.True); Assert.That(editor.CustomTemplateCount, Is.EqualTo(2));
            Assert.That(editor.TryBeginEdit(false, out error), Is.True, error);
            Assert.That(editor.Draft.TryGetStat(melee, WarSandboxUnitStat.MaxSpeed, out float speed) && speed == 7);
            Assert.That(editor.CancelEditing(), Is.True);
        }

        [Test]
        public void PushWritesOnlyLocalFieldsByTemplateIdAndCanClearThemFromThePlan()
        {
            Assert.That(editor.StatStore.TrySave(Global("knight", WarSandboxUnitStat.AttackRange, 3), "seed", out string error), Is.True, error);
            Assert.That(editor.TryBeginEdit(false, out error), Is.True, error);
            editor.SetStat(melee, WarSandboxUnitStat.MaxHp, 400); editor.SetStat(melee, WarSandboxUnitStat.ChargeDamageMultiplier, 3);
            Assert.That(editor.TryPushStats(melee, true, out error), Is.True, error);
            var global = new WarSandboxUnitStatStore(editor.StatStore.FilePath).Current.Get("knight");
            Assert.That(global.Count, Is.EqualTo(3), "Existing global fields are kept.");
            Assert.That(global.TryGet(WarSandboxUnitStat.MaxHp, out float hp) && hp == 400);
            Assert.That(editor.Draft.Stats.IsEmpty, Is.True);
            Assert.That(editor.DraftStats.Resolve(melee, WarSandboxUnitStats.Get(WarSandboxUnitStat.MaxHp)).source, Is.EqualTo(WarSandboxStatSource.Global));
            Assert.That(editor.Draft.Undo(), Is.True, "Clearing local fields after a push is an ordinary undo step.");
            Assert.That(editor.TryUndoGlobalChange(out error), Is.True, error);
            Assert.That(new WarSandboxUnitStatStore(editor.StatStore.FilePath).Current.Get("knight").Count, Is.EqualTo(1));
            Assert.That(editor.TryPushStats(stranger, false, out _), Is.False);
            editor.CancelEditing();
        }

        [Test]
        public void LoadTimeGlobalLayerReplacesTheScenarioOnlyWhenItChangesSomething()
        {
            Assert.That(editor.TryApplyGlobalStatsOnLoad(out string warning), Is.False); Assert.That(warning, Is.Null);
            Assert.That(manager.scenarioConfig, Is.SameAs(source));
            Assert.That(editor.StatStore.TrySave(Global("knight", WarSandboxUnitStat.MaxHp, 100), "same as official", out string error), Is.True, error);
            editor.StatStore.Invalidate();
            Assert.That(editor.TryApplyGlobalStatsOnLoad(out warning), Is.False, "A value equal to the official one changes nothing.");
            Assert.That(manager.scenarioConfig, Is.SameAs(source));
            Assert.That(editor.StatStore.TrySave(Global("knight", WarSandboxUnitStat.MaxHp, 500), "buff", out error), Is.True, error);
            Assert.That(editor.TryApplyGlobalStatsOnLoad(out warning), Is.True, warning);
            Assert.That(manager.scenarioConfig, Is.Not.SameAs(source));
            Assert.That(manager.scenarioConfig.unitTypes[0].combatConfig.maxHp, Is.EqualTo(500));
            Assert.That(manager.scenarioConfig.unitTypes[0].spawnConfig.spawnCenter, Is.EqualTo(melee.spawnConfig.spawnCenter));
            Assert.That(editor.HasCustomStats, Is.True);
            Assert.That(editor.TryBeginEdit(false, out error), Is.True, error);
            Assert.That(editor.Templates[0], Is.SameAs(melee), "Editing still starts from the authored templates.");
            Assert.That(editor.Draft[0].template, Is.SameAs(melee));
            editor.CancelEditing();
            // The fixture root is inactive, so Unity would skip OnDestroy; invoke the teardown path directly.
            typeof(WarSandboxRuntimeDeployment).GetMethod("OnDestroy", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .Invoke(editor, null);
            Assert.That(manager.scenarioConfig, Is.SameAs(source), "Destroying the deployment restores the authored scenario.");
        }

        [Test]
        public void SavedPlanFromTheRuntimeIncludesDraftOverrides()
        {
            // The session-free path cannot save (no current battlefield); the plan file shape is covered above.
            Assert.That(editor.TryBeginEdit(false, out string error), Is.True, error);
            editor.SetStat(ranged, WarSandboxUnitStat.ProjectileRange, 20);
            Assert.That(editor.TrySavePlan("A", "x", false, out error), Is.False);
            Assert.That(error, Does.Contain("战场目录"));
            editor.CancelEditing();
        }

        // ---------- shipping content ----------

        [Test]
        public void EveryOfficialTemplateValueFitsTheEditableRange()
        {
            var official = AssetDatabase.LoadAssetAtPath<WarSandboxBattlefieldCatalog>(OfficialRosterBuilder.CatalogPath);
            Assert.That(official, Is.Not.Null);
            Assert.That(official.TryValidateTemplates(out string error), Is.True, error);
            int checkedValues = 0;
            foreach (var template in official.templates)
                foreach (var d in WarSandboxUnitStats.Definitions)
                {
                    if (!WarSandboxUnitStats.TryGetOfficial(template.config, d, out float value)) continue;
                    Assert.That(value, Is.InRange(d.Min, d.Max), template.templateId + "." + d.Key);
                    checkedValues++;
                }
            Assert.That(checkedValues, Is.GreaterThan(official.templates.Length * 5));
            Assert.That(official.TryGetTemplateId(official.templates.First(t => t.templateId == "roster-dragon-evolved").config, out _, out _), Is.True);
        }
    }
}
