using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MassEngine.Game.Tests
{
    public class UnitRadiusEditingTests
    {
        private UnitTypeConfig unit, other;
        private WarSandboxBattlefieldCatalog catalog;
        private GameObject root;
        private MassEngineManager manager;
        private MassEngineSystemConfig system;
        private string directory;
        private const WarSandboxUnitStat Radius = WarSandboxUnitStat.AgentRadius;
        private UnitTypeConfig Make(string name, int team, float x)
        {
            var u = ScriptableObject.CreateInstance<UnitTypeConfig>(); u.name = name; u.teamId = team;
            u.flockingConfig = ScriptableObject.CreateInstance<FlockingConfig>(); u.flockingConfig.agentRadius = .55f;
            u.spawnConfig = ScriptableObject.CreateInstance<SpawnConfig>(); u.spawnConfig.unitCount = 16;
            u.spawnConfig.spawnCenter = new Vector3(x, 0, 0); u.spawnConfig.formationDensity = .1f; u.spawnConfig.formationJitterFraction = .02f;
            u.movementConfig = ScriptableObject.CreateInstance<MovementConfig>(); u.combatConfig = ScriptableObject.CreateInstance<CombatConfig>();
            return u;
        }
        [SetUp] public void Setup()
        {
            unit = Make("Radius fixture", 0, -20); other = Make("Other template", 1, 20);
            catalog = ScriptableObject.CreateInstance<WarSandboxBattlefieldCatalog>();
            catalog.templates = new[] { new WarSandboxUnitTemplateEntry { templateId = "a", revision = 1, config = unit }, new WarSandboxUnitTemplateEntry { templateId = "b", revision = 1, config = other } };
            root = new GameObject("P2 CPU fixture"); root.SetActive(false); manager = root.AddComponent<MassEngineManager>();
            system = ScriptableObject.CreateInstance<MassEngineSystemConfig>(); system.simulationConfig = ScriptableObject.CreateInstance<SimulationConfig>(); system.simulationConfig.cellSize = 3; system.simulationConfig.simulationWorldSize = new Vector2(200, 200);
            system.runtimeFlowConfig = ScriptableObject.CreateInstance<RuntimeFlowConfig>(); manager.systemConfig = system;
            directory = Path.Combine(Path.GetTempPath(), "P2-radius-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        }
        [TearDown] public void Cleanup()
        {
            Object.DestroyImmediate(root);
            foreach (var u in new[] { unit, other })
            { Object.DestroyImmediate(u.flockingConfig); Object.DestroyImmediate(u.spawnConfig); Object.DestroyImmediate(u.combatConfig); Object.DestroyImmediate(u.movementConfig); Object.DestroyImmediate(u); }
            Object.DestroyImmediate(catalog); Object.DestroyImmediate(system.simulationConfig); Object.DestroyImmediate(system.runtimeFlowConfig); Object.DestroyImmediate(system);
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
        private WarSandboxGlobalStats Global(float value)
        { var g = new WarSandboxGlobalStats(); var s = new WarSandboxStatSet(); s.Set(Radius, value); g.Replace("a", 1, s); return g; }
        private WarSandboxDeploymentDraft Draft(WarSandboxStatOverrides local = null) => new WarSandboxDeploymentDraft(new[] { WarSandboxDeploymentEntry.From(unit), WarSandboxDeploymentEntry.From(other) }, null, local);
        [Test] public void KeyAndOrdinalAppendWithoutChangingOldFields()
        {
            string[] keys = { "maxHp", "attackDamage", "attackInterval", "attackRange", "targetAcquireRadius", "maxSpeed", "chargeDamageMultiplier", "chargeMinSpeedFraction", "projectileRange", "projectileSpeed", "projectileSplashRadius" };
            for (int i = 0; i < keys.Length; i++) Assert.That(WarSandboxUnitStats.Definitions[i].Key, Is.EqualTo(keys[i]));
            Assert.That((int)Radius, Is.EqualTo(11)); Assert.That(WarSandboxUnitStats.Get(Radius).Key, Is.EqualTo("agentRadius"));
            Assert.That(WarSandboxUnitOverrideFile.CurrentSchema, Is.EqualTo(1));
        }
        [TestCase(float.NaN)] [TestCase(float.PositiveInfinity)] [TestCase(float.NegativeInfinity)] [TestCase(-1)] [TestCase(0)] [TestCase(.049f)] [TestCase(4.801f)]
        public void InvalidRadiusIsRejectedWithoutDirtyingData(float value)
        {
            var set = new WarSandboxStatSet(); Assert.That(set.Set(Radius, value), Is.False); Assert.That(set.Count, Is.Zero);
            var local = new WarSandboxStatOverrides(); Assert.That(local.Set(unit, Radius, value), Is.False); Assert.That(local.IsEmpty, Is.True);
            var draft = Draft(); int version = draft.Revision; Assert.That(draft.SetStat(unit, Radius, value), Is.False); Assert.That(draft.Revision, Is.EqualTo(version));
            var read = WarSandboxStatSet.FromContract(new[] { new WarSandboxStatValue { stat = "agentRadius", value = value } }, out int skipped);
            Assert.That(skipped, Is.EqualTo(1)); Assert.That(read.Count, Is.Zero);
        }
        [TestCase(.05f)] [TestCase(.6f)] [TestCase(.555123f)] [TestCase(4.8f)]
        public void ValidRadiusRoundTripsWithoutPrecisionLoss(float value)
        {
            var d = WarSandboxUnitStats.Get(Radius); var set = new WarSandboxStatSet(); Assert.That(set.Set(Radius, value), Is.True);
            var read = WarSandboxStatSet.FromContract(set.ToContract(), out int skipped); Assert.That(skipped, Is.Zero); Assert.That(read.TryGet(Radius, out var actual), Is.True); Assert.That(actual, Is.EqualTo(value));
            Assert.That(float.Parse(d.Format(value), System.Globalization.CultureInfo.InvariantCulture), Is.EqualTo(value));
        }
        [Test] public void OldFilesAndMissingFieldRemainOfficialAndUnmodified()
        {
            var old = WarSandboxStatSet.FromContract(new[] { new WarSandboxStatValue { stat = "maxHp", value = 200 } }, out _);
            var global = new WarSandboxGlobalStats(); global.Replace("a", 1, old);
            var r = new WarSandboxStatResolver(catalog, global, null);
            Assert.That(r.Resolve(unit, WarSandboxUnitStats.Get(Radius)).source, Is.EqualTo(WarSandboxStatSource.Official));
            using (var instance = new WarSandboxDeploymentInstance(Draft().Snapshot(), r))
            { Assert.That(instance.Scenario.unitTypes[0].flockingConfig, Is.SameAs(unit.flockingConfig)); Assert.That(instance.HasRadiusChanges, Is.False); }
        }
        [Test] public void PriorityCloneIsolationRestorationAndUnrelatedStats()
        {
            var local = new WarSandboxStatOverrides(); local.Set(unit, Radius, .7f);
            var global = Global(.6f); var r = new WarSandboxStatResolver(catalog, global, local); var def = WarSandboxUnitStats.Get(Radius);
            Assert.That(r.Resolve(unit, def).source, Is.EqualTo(WarSandboxStatSource.Local));
            string before = JsonUtility.ToJson(unit.flockingConfig);
            using (var instance = new WarSandboxDeploymentInstance(Draft().Snapshot(), r))
            {
                var tuned = instance.Scenario.unitTypes[0]; Assert.That(tuned.flockingConfig, Is.Not.SameAs(unit.flockingConfig));
                Assert.That(new DefaultSwordUnit(tuned).BuildGpuSettings().agentRadius, Is.EqualTo(.7f));
                Assert.That(tuned.combatConfig, Is.SameAs(unit.combatConfig)); Assert.That(tuned.movementConfig, Is.SameAs(unit.movementConfig));
                Assert.That(tuned.spawnConfig.unitCount, Is.EqualTo(unit.spawnConfig.unitCount)); Assert.That(tuned.teamId, Is.EqualTo(unit.teamId));
                Assert.That(instance.Scenario.unitTypes[1].flockingConfig, Is.SameAs(other.flockingConfig));
            }
            Assert.That(JsonUtility.ToJson(unit.flockingConfig), Is.EqualTo(before));
            local.Remove(unit, Radius); Assert.That(r.Resolve(unit, def).effective, Is.EqualTo(.6f));
            local.Set(unit, Radius, .55f); Assert.That(r.Resolve(unit, def).effective, Is.EqualTo(.55f));
            using (var instance = new WarSandboxDeploymentInstance(Draft().Snapshot(), r)) Assert.That(instance.Scenario.unitTypes[0].flockingConfig, Is.SameAs(unit.flockingConfig));
        }
        [Test] public void SaveReloadUndoAndFailureKeepOriginalFile()
        {
            string path = Path.Combine(directory, "stats.json"); var store = new WarSandboxUnitStatStore(path);
            Assert.That(store.TrySave(Global(.6f), "radius", out var error), Is.True, error);
            Assert.That(new WarSandboxUnitStatStore(path).Current.TryGet("a", Radius, out float value) && value == .6f);
            byte[] original = File.ReadAllBytes(path);
            // A deterministic invalid backup destination rejects before replacing the original.
            File.Delete(store.BackupPath); Directory.CreateDirectory(store.BackupPath);
            Assert.That(store.TrySave(Global(.7f), "blocked", out error), Is.False);
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(original)); Directory.Delete(store.BackupPath);
            Assert.That(store.TrySave(Global(.7f), "next", out error), Is.True, error); Assert.That(store.TryUndo(out error), Is.True, error);
            Assert.That(store.Current.TryGet("a", Radius, out value) && value == .6f);
            Assert.That(store.TrySave(new WarSandboxGlobalStats(), "restore", out error), Is.True); Assert.That(store.Current.IsEmpty, Is.True);
        }
        [Test] public void DraftUndoRedoAndCancelAreNotPersistence()
        {
            var draft = Draft(); var original = draft.Snapshot(); draft.SetStat(unit, Radius, .7f);
            Assert.That(draft.Undo(), Is.True); Assert.That(draft.Stats.IsEmpty, Is.True); Assert.That(draft.Redo(), Is.True);
            Assert.That(draft.TryGetStat(unit, Radius, out float radius) && radius == .7f); Assert.That(original[0].count, Is.EqualTo(draft[0].count));
            Assert.That(Directory.GetFiles(directory), Is.Empty); Assert.That(unit.flockingConfig.agentRadius, Is.EqualTo(.55f));
        }
        [Test] public void LocalPlanRadiusRoundTripPreservesTemplateIdentity()
        {
            var battlefield = new WarSandboxBattlefieldEntry { id = "radius-test", scenePath = "Assets/Game/Scenes/Green.unity" };
            var rules = WarSandboxBattlefieldRules.Default; var local = new WarSandboxStatOverrides(); local.Set(unit, Radius, .7f);
            var plan = WarSandboxLocalPlanStore.Create("Radius", "P2 isolated local", battlefield.id, battlefield.contentVersion, battlefield.terrainId, battlefield.terrainVersion, new Vector2(200, 200), 0, rules, Draft().Snapshot(), catalog, local, out _);
            var store = new WarSandboxLocalPlanStore(Path.Combine(directory, "Plans")); Assert.That(store.TrySave(plan, false, out string error), Is.True, error);
            Assert.That(store.TryLoad("Radius", out var loaded, out error), Is.True, error);
            Assert.That(WarSandboxLocalPlanStore.TryResolve(loaded, battlefield, catalog, new Vector2(200, 200), 0, out var draft, out error), Is.True, error);
            Assert.That(draft.TryGetStat(unit, Radius, out float radius) && radius == .7f); Assert.That(draft.TryGetStat(other, Radius, out _), Is.False);
            StringAssert.Contains("agentRadius", File.ReadAllText(Path.Combine(directory, "Plans", "Radius.json")));
        }
        [Test] public void GridDensityAndFootprintGateWithoutAutomaticCorrections()
        {
            var draft = Draft(); var r = new WarSandboxStatResolver(catalog, Global(1.6f), draft.Stats);
            Assert.That(WarSandboxRadiusPolicy.TryValidate(manager, draft, r, out _, out string error), Is.False); StringAssert.Contains("网格", error);
            r = new WarSandboxStatResolver(catalog, Global(.8f), draft.Stats); var entry = draft[0]; entry.manualSize = new Vector3(1, 0, 1); draft.Set(0, entry);
            Assert.That(WarSandboxRadiusPolicy.TryValidate(manager, draft, r, out _, out error), Is.False); StringAssert.Contains("间距", error);
            Assert.That(draft[0].count, Is.EqualTo(16)); Assert.That(draft[0].manualSize, Is.EqualTo(entry.manualSize));
            draft = Draft(); r = new WarSandboxStatResolver(catalog, Global(.7f), draft.Stats);
            Assert.That(WarSandboxRadiusPolicy.TryValidate(manager, draft, r, out var clearance, out error), Is.True, error); Assert.That(clearance, Is.EqualTo(.7f));
        }
        [Test] public void ExpandedFootprintsRejectBoundaryAndOtherFormationOverlap()
        {
            var draft = Draft(); var resolver = new WarSandboxStatResolver(catalog, Global(.7f), draft.Stats);
            var entry = draft[0]; entry.center = new Vector3(99, 0, 0); draft.Set(0, entry);
            Assert.That(WarSandboxRadiusPolicy.TryValidate(manager, draft, resolver, out _, out string error), Is.False); StringAssert.Contains("越出", error);
            draft = Draft(); entry = draft[1]; entry.center = draft[0].center; draft.Set(1, entry);
            Assert.That(WarSandboxRadiusPolicy.TryValidate(manager, draft, resolver, out _, out error), Is.False); StringAssert.Contains("相交", error);
            Assert.That(draft[0].count + draft[1].count, Is.EqualTo(32));
        }
        [Test] public void RepeatedFormationsShareOneOwnedFlockingClonePerTemplate()
        {
            FlockingConfig copy;
            using (var instance = new WarSandboxDeploymentInstance(new[] { WarSandboxDeploymentEntry.From(unit), WarSandboxDeploymentEntry.From(unit) }, new WarSandboxStatResolver(catalog, Global(.7f), null)))
            {
                copy = instance.Scenario.unitTypes[0].flockingConfig;
                Assert.That(copy, Is.Not.SameAs(unit.flockingConfig)); Assert.That(instance.Scenario.unitTypes[1].flockingConfig, Is.SameAs(copy));
                Assert.That(instance.CustomTemplateCount, Is.EqualTo(1));
            }
            Assert.That(copy == null, Is.True); Assert.That(unit.flockingConfig.agentRadius, Is.EqualTo(.55f));
        }
        [Test] public void ExplicitClearanceRestorationIgnoresHistoricalHighWaterButLegacyDoesNot()
        {
            var resolve = typeof(MassEngineManager).GetMethod("ResolveTerrainClearance", BindingFlags.Instance | BindingFlags.NonPublic);
            typeof(MassEngineManager).GetField("terrainClearanceFloor", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(manager, 4f);
            Assert.That((float)resolve.Invoke(manager, null), Is.EqualTo(4));
            manager.SetDeploymentRadiusClearance(.7f); Assert.That((float)resolve.Invoke(manager, null), Is.EqualTo(.7f));
            manager.SetDeploymentRadiusClearance(.55f); Assert.That((float)resolve.Invoke(manager, null), Is.EqualTo(.55f));
            manager.SetDeploymentRadiusClearance(null); Assert.That((float)resolve.Invoke(manager, null), Is.EqualTo(4));
            Assert.Throws<ArgumentOutOfRangeException>(() => manager.SetDeploymentRadiusClearance(float.NaN));
        }
    }
}
