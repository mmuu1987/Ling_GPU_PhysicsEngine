#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace MassEngine.Tests
{
    /// <summary>
    /// Overnight task 6: opt-in melee charge (CombatConfig.chargeDamageMultiplier). One mounted knight charges a
    /// holding high-HP dummy from 30 m on the shipped cavalry battlefield (runtime copies only, no asset is written).
    /// Every HP drop of the dummy is one settled melee hit. Legacy (x1) must be bit-identical in damage and must not
    /// even allocate the charge table; x3 must triple only the first hit; an unreachable speed gate must disable it.
    /// </summary>
    public sealed class MeleeChargeTests
    {
        private const string Scene = "Assets/Game/Cavalry/Prepared05/Integrated/Battlefield.unity";
        private const string CombatShaderPath = "Assets/MassEngine/Simulation/Shaders/AgentCombatSimulation.compute";
        private static readonly FieldInfo ChargeField = typeof(MassEngineManager).GetField("meleeCharge", BindingFlags.Instance | BindingFlags.NonPublic);

        private struct Case
        {
            public string name; public float multiplier, minFraction, speed;
            public Case(string n, float m, float f, float s) { name = n; multiplier = m; minFraction = f; speed = s; }
        }

        // Only a test that loaded a battlefield may unload scenes: after a plain [Test] the active scene is the
        // runner's own init scene, and unloading it stalls the whole PlayMode run.
        private bool loadedBattlefield;

        [UnityTearDown] public IEnumerator Cleanup()
        {
            Time.captureFramerate = 0;
            if (!loadedBattlefield) yield break;
            loadedBattlefield = false;
            var empty = SceneManager.CreateScene("ChargeEmpty" + Time.frameCount);
            SceneManager.SetActiveScene(empty);
            for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene != empty && scene.isLoaded) yield return SceneManager.UnloadSceneAsync(scene);
            }
        }

        [Test] public void DefaultsKeepLegacyBehaviour()
        {
            Step("defaults-test");
            var c = ScriptableObject.CreateInstance<CombatConfig>();
            try { Assert.AreEqual(1f, c.chargeDamageMultiplier); Assert.AreEqual(0.6f, c.chargeMinSpeedFraction, 1e-6f); }
            finally { Object.DestroyImmediate(c); }
        }

        [UnityTest, Timeout(600000)] public IEnumerator FirstHitOnArrivalIsMultiplied()
        {
            var cases = new[]
            {
                new Case("legacy-x1", 1f, 0.6f, 3.5f),
                new Case("charge-x3", 3f, 0.6f, 6f),
                new Case("charge-x3-unreachable-gate", 3f, 2f, 6f),
            };
            var results = new Dictionary<string, List<int>>();
            var rows = new List<string>();
            Step(" load-scene");
            loadedBattlefield = true;
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(Scene, new LoadSceneParameters(LoadSceneMode.Single));
            Step(" scene-loaded");
            MassEngineManager manager = null;
            for (int i = 0; i < 300 && manager == null; i++) { manager = Object.FindFirstObjectByType<MassEngineManager>(); yield return null; }
            Assert.IsNotNull(manager);
            for (int i = 0; i < 30; i++) yield return null;
            Step(" manager-ready frame=" + Time.frameCount);
            ScenarioConfig source = manager.scenarioConfig;
            Assert.AreEqual(2, source.unitTypes.Length);
            UnitTypeConfig rider = source.unitTypes[0], foot = source.unitTypes[1];
            Assert.LessOrEqual(rider.combatConfig.projectileRange, 0.01f, "Mounted knight must be melee.");
            var combatShader = AssetDatabase.LoadAssetAtPath<ComputeShader>(CombatShaderPath);
            Assert.IsNotNull(combatShader);
            int damage = rider.combatConfig.attackDamage;
            Time.captureFramerate = 30;
            try
            {
                foreach (var c in cases)
                {
                    var created = new List<Object>();
                    var scenario = ScriptableObject.CreateInstance<ScenarioConfig>(); created.Add(scenario);
                    var r = Clone(rider, created); var d = Clone(foot, created);
                    r.spawnConfig.unitCount = 1; r.spawnConfig.spawnCenter = new Vector3(-25f, 0f, 0f); r.spawnConfig.spawnSize = new Vector3(8f, 0f, 8f);
                    r.combatConfig.chargeDamageMultiplier = c.multiplier; r.combatConfig.chargeMinSpeedFraction = c.minFraction;
                    r.movementConfig.maxSpeed = c.speed;
                    d.spawnConfig.unitCount = 1; d.spawnConfig.spawnCenter = new Vector3(5f, 0f, 0f); d.spawnConfig.spawnSize = new Vector3(8f, 0f, 8f);
                    d.combatConfig.maxHp = 100000;
                    scenario.unitTypes = new[] { r, d };
                    manager.scenarioConfig = scenario;
                    Step(" reset " + c.name);
                    manager.ResetScenario();
                    Step(" reset-done " + c.name);
                    Step("physics issues: " + string.Join(" | ", ScenarioPhysicsIssues(manager)));
                    manager.ClearFlowTargetOverride(0); manager.SetTeamNavigationOverride(0, true, true);
                    manager.ClearFlowTargetOverride(1); manager.SetTeamNavigationOverride(1, false, false); // dummy holds
                    for (int i = 0; i < 10; i++) yield return null;
                    int agents = manager.UnitTypes.TotalAgentCount;
                    Assert.AreEqual(2, agents);
                    var teams = new int[agents]; manager.Buffers.combatBuffers.teamIdBuffer.GetData(teams);
                    int dummy = teams[0] == 1 ? 0 : 1;
                    var hp = new int[agents]; manager.Buffers.combatBuffers.hpReadBuffer.GetData(hp);
                    int last = hp[dummy];
                    Assert.AreEqual(100000, last);
                    var drops = new List<int>(); float sim = 0f, firstHitAt = -1f;
                    manager.StartBattle();
                    Step(" started " + c.name);
                    while (sim < 25f && drops.Count < 3)
                    {
                        yield return null; sim += 1f / 30f;
                        manager.Buffers.combatBuffers.hpReadBuffer.GetData(hp);
                        if (Mathf.RoundToInt(sim * 30f) % 60 == 0) Step(" t=" + sim.ToString("F1", CultureInfo.InvariantCulture) + " dummyHp=" + hp[dummy]);
                        if (hp[dummy] < last) { drops.Add(last - hp[dummy]); if (firstHitAt < 0f) firstHitAt = sim; last = hp[dummy]; }
                        Assert.IsFalse(combatShader.IsKeywordEnabled(MeleeChargeParams.Keyword), "Charge keyword must never stay enabled on the shared compute asset.");
                    }
                    bool allocated = ChargeField != null && ChargeField.GetValue(manager) != null;
                    manager.battleStarted = false;
                    results[c.name] = drops;
                    string row = string.Format(CultureInfo.InvariantCulture,
                        "{{\"case\":\"{0}\",\"multiplier\":{1},\"minSpeedFraction\":{2},\"maxSpeed\":{3},\"attackDamage\":{4},\"hits\":[{5}],\"firstHitAt\":{6},\"chargeTableAllocated\":{7}}}",
                        c.name, c.multiplier, c.minFraction, c.speed, damage, string.Join(",", drops), firstHitAt.ToString("F2", CultureInfo.InvariantCulture), allocated ? "true" : "false");
                    Debug.Log("CHARGE " + row); Step(row);
                    rows.Add(row);
                    Assert.AreEqual(c.multiplier > 1f, allocated, c.name + ": charge table allocation");
                    foreach (var o in created) Object.Destroy(o);
                }
            }
            finally
            {
                Time.captureFramerate = 0;
                if (manager != null) manager.scenarioConfig = source;
            }
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "AgentCharge"));
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "charge-hits.json"), "[\n" + string.Join(",\n", rows) + "\n]\n", new UTF8Encoding(false));

            foreach (var c in cases) Assert.GreaterOrEqual(results[c.name].Count, 3, c.name + ": three hits expected");
            Assert.AreEqual(new[] { damage, damage, damage }, results["legacy-x1"].ToArray(), "Legacy melee damage must be unchanged.");
            Assert.AreEqual(new[] { damage * 3, damage, damage }, results["charge-x3"].ToArray(), "Only the first hit on arrival is a charge hit.");
            Assert.AreEqual(new[] { damage, damage, damage }, results["charge-x3-unreachable-gate"].ToArray(), "Below the speed gate there is no charge bonus.");
        }

        [UnityTest, Timeout(900000)] public IEnumerator ShippedChargeBattlefield()
        {
            const string Shipped = "Assets/Game/Cavalry/Prepared06/Integrated/Battlefield.unity";
            Step(" shipped load-scene");
            loadedBattlefield = true;
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(Shipped, new LoadSceneParameters(LoadSceneMode.Single));
            MassEngineManager manager = null;
            for (int i = 0; i < 300 && manager == null; i++) { manager = Object.FindFirstObjectByType<MassEngineManager>(); yield return null; }
            Assert.IsNotNull(manager);
            for (int i = 0; i < 30; i++) yield return null;
            ScenarioConfig source = manager.scenarioConfig;
            Assert.AreEqual(2, source.unitTypes.Length);
            UnitTypeConfig rider = source.unitTypes[0], foot = source.unitTypes[1];
            Assert.AreEqual(3f, rider.combatConfig.chargeDamageMultiplier, "Shipped cavalry charges x3.");
            Assert.AreEqual(6f, rider.movementConfig.maxSpeed, "Shipped cavalry gallops at 6 m/s.");
            Assert.AreEqual(1f, foot.combatConfig.chargeDamageMultiplier, "Infantry must not charge.");
            int damage = rider.combatConfig.attackDamage;
            var rows = new List<string>();
            Time.captureFramerate = 30;
            try
            {
                foreach (bool legacy in new[] { true, false })
                {
                    var created = new List<Object>();
                    var scenario = ScriptableObject.CreateInstance<ScenarioConfig>(); created.Add(scenario);
                    var r = Clone(rider, created);
                    if (legacy) { r.combatConfig.chargeDamageMultiplier = 1f; r.movementConfig.maxSpeed = 3.5f; }
                    scenario.unitTypes = new[] { r, foot };
                    manager.scenarioConfig = scenario;
                    manager.ResetScenario();
                    for (int team = 0; team < 2; team++) { manager.ClearFlowTargetOverride(team); manager.SetTeamNavigationOverride(team, true, true); }
                    for (int i = 0; i < 10; i++) yield return null;
                    int agents = manager.UnitTypes.TotalAgentCount;
                    var teams = new int[agents]; manager.Buffers.combatBuffers.teamIdBuffer.GetData(teams);
                    var hp = new int[agents]; var prev = new int[agents];
                    manager.Buffers.combatBuffers.hpReadBuffer.GetData(prev);
                    int chargeHits = 0; float sim = 0f, firstChargeAt = -1f; int riders = 0, knights = 0;
                    manager.StartBattle();
                    while (sim < 120f)
                    {
                        yield return null; sim += 1f / 30f;
                        manager.Buffers.combatBuffers.hpReadBuffer.GetData(hp);
                        riders = 0; knights = 0;
                        for (int i = 0; i < agents; i++)
                        {
                            if (teams[i] == 1 && prev[i] - hp[i] >= damage * 3) { chargeHits++; if (firstChargeAt < 0f) firstChargeAt = sim; }
                            if (hp[i] > 0) { if (teams[i] == 0) riders++; else knights++; }
                            prev[i] = hp[i];
                        }
                        if (riders == 0 || knights == 0) break;
                    }
                    manager.battleStarted = false;
                    string row = string.Format(CultureInfo.InvariantCulture,
                        "{{\"case\":\"{0}\",\"multiplier\":{1},\"maxSpeed\":{2},\"riders\":{3},\"knights\":{4},\"winner\":\"{5}\",\"simSeconds\":{6},\"ridersLeft\":{7},\"knightsLeft\":{8},\"chargeHits\":{9},\"firstChargeAt\":{10}}}",
                        legacy ? "legacy-no-charge-3.5" : "shipped-charge", r.combatConfig.chargeDamageMultiplier, r.movementConfig.maxSpeed, r.spawnConfig.unitCount, foot.spawnConfig.unitCount,
                        knights == 0 ? "cavalry" : riders == 0 ? "infantry" : "timeout", sim.ToString("F1", CultureInfo.InvariantCulture), riders, knights, chargeHits, firstChargeAt.ToString("F1", CultureInfo.InvariantCulture));
                    Debug.Log("CHARGE " + row); Step(row); rows.Add(row);
                    if (legacy) Assert.AreEqual(0, chargeHits, "Legacy cavalry never charges.");
                    else Assert.Greater(chargeHits, 0, "Shipped cavalry must land charge hits.");
                    foreach (var o in created) Object.Destroy(o);
                }
            }
            finally
            {
                Time.captureFramerate = 0;
                if (manager != null) manager.scenarioConfig = source;
            }
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "AgentCharge"));
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "charge-shipped.json"), "[\n" + string.Join(",\n", rows) + "\n]\n", new UTF8Encoding(false));
        }

        private static IEnumerable<string> ScenarioPhysicsIssues(MassEngineManager manager)
        {
            yield return "agents=" + (manager.UnitTypes != null ? manager.UnitTypes.TotalAgentCount : -1);
        }

        // Unity's batch log is buffered and lost on a watchdog kill; this trace is flushed per line.
        private static void Step(string message)
        {
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "AgentCharge"));
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "charge-trace.txt"), System.DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) + " " + message + "\n");
        }

        private static UnitTypeConfig Clone(UnitTypeConfig source, List<Object> created)
        {
            var u = Object.Instantiate(source); u.name = source.name; created.Add(u);
            u.spawnConfig = Object.Instantiate(source.spawnConfig); created.Add(u.spawnConfig);
            u.combatConfig = Object.Instantiate(source.combatConfig); created.Add(u.combatConfig);
            u.movementConfig = Object.Instantiate(source.movementConfig); created.Add(u.movementConfig);
            return u;
        }
    }
}
#endif
