#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace MassEngine.Tests
{
    /// <summary>
    /// User decision 2026-10-01 ("3 加强"): make 2 charging cavalry beat the 16 knights of the cavalry battlefield, config only.
    /// Measurement on the shipped Cavalry/Prepared06 scene with runtime copies (no asset written), default Attack orders,
    /// fixed 1/30 s step. Output: Logs/AgentGiants/cavalry-strength*.json.
    /// </summary>
    public sealed class CavalryStrengthTests
    {
        public struct Case { public string name; public float hpMul; public int dmg; public float mult; public Case(string n, float h, int d, float m) { name = n; hpMul = h; dmg = d; mult = m; } }
        private static readonly Case[] Matrix =
        {
            new Case("shipped", 1f, 0, 0f),
            new Case("hp1.5", 1.5f, 0, 0f), new Case("hp2", 2f, 0, 0f), new Case("hp3", 3f, 0, 0f),
            new Case("dmg45", 1f, 45, 0f), new Case("dmg60", 1f, 60, 0f),
            new Case("hp1.5-dmg45", 1.5f, 45, 0f), new Case("hp2-dmg45", 2f, 45, 0f), new Case("hp2-dmg60", 2f, 60, 0f),
            new Case("x5", 1f, 0, 5f), new Case("hp2-x5", 2f, 0, 5f),
        };

        [UnityTearDown] public IEnumerator Cleanup()
        {
            Time.captureFramerate = 0;
            var empty = SceneManager.CreateScene("CavEmpty" + Time.frameCount);
            SceneManager.SetActiveScene(empty);
            for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene != empty && scene.isLoaded) yield return SceneManager.UnloadSceneAsync(scene);
            }
        }

        [UnityTest, Timeout(1500000)] public IEnumerator Sweep() { yield return Run("Assets/Game/Content/Characters/Cavalry/Prepared06/Integrated/Battlefield.unity", "cavalry-strength-sweep", Matrix, 1); }

        /// <summary>Shipped Prepared07 (damage 45) as authored, three runs: cavalry must win every time.</summary>
        [UnityTest, Timeout(900000)] public IEnumerator ShippedStrongCavalry()
        {
            yield return Run("Assets/Game/Content/Characters/Cavalry/Prepared07/Integrated/Battlefield.unity", "cavalry-strength-shipped07", new[] { new Case("shipped07", 1f, 0, 0f) }, 3);
            string json = File.ReadAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "AgentGiants", "cavalry-strength-shipped07.json")));
            Assert.AreEqual(3, System.Text.RegularExpressions.Regex.Matches(json, "\"winner\":\"cavalry\"").Count, json);
            StringAssert.Contains("\"cavDamage\":45", json);
        }

        public static IEnumerator Run(string scenePath, string output, Case[] matrix, int repeats)
        {
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(scenePath, new LoadSceneParameters(LoadSceneMode.Single));
            MassEngineManager manager = null;
            for (int i = 0; i < 300 && manager == null; i++) { manager = Object.FindFirstObjectByType<MassEngineManager>(); yield return null; }
            Assert.IsNotNull(manager);
            for (int i = 0; i < 30; i++) yield return null;
            ScenarioConfig source = manager.scenarioConfig;
            UnitTypeConfig cav = source.unitTypes[0], inf = source.unitTypes[1];
            Assert.Greater(cav.flockingConfig.agentRadius, 1f, "Cavalry slot first.");
            var rows = new List<string>();
            try
            {
                foreach (var c in matrix)
                for (int rep = 0; rep < repeats; rep++)
                {
                    var created = new List<Object>();
                    var scenario = ScriptableObject.CreateInstance<ScenarioConfig>(); created.Add(scenario);
                    var u = Object.Instantiate(cav); u.name = cav.name; created.Add(u);
                    u.combatConfig = Object.Instantiate(cav.combatConfig); created.Add(u.combatConfig);
                    u.combatConfig.maxHp = Mathf.RoundToInt(cav.combatConfig.maxHp * c.hpMul);
                    if (c.dmg > 0) u.combatConfig.attackDamage = c.dmg;
                    if (c.mult > 0) u.combatConfig.chargeDamageMultiplier = c.mult;
                    scenario.unitTypes = new[] { u, inf };
                    manager.scenarioConfig = scenario; manager.ResetScenario();
                    for (int i = 0; i < 20; i++) yield return null;
                    for (int team = 0; team < 2; team++) { manager.ClearFlowTargetOverride(team); manager.SetTeamNavigationOverride(team, true, true); }
                    int agents = manager.UnitTypes.TotalAgentCount;
                    var teams = new int[agents]; manager.Buffers.combatBuffers.teamIdBuffer.GetData(teams);
                    int c0 = 0, i0 = 0; foreach (int t in teams) if (t == 0) c0++; else i0++;
                    Time.captureFramerate = 30; manager.StartBattle();
                    float sim = 0f; int cl = c0, il = i0;
                    while (sim < 180f)
                    {
                        yield return null; sim += 1f / 30f;
                        var snap = manager.Telemetry.Snapshot;
                        if (!snap.valid || snap.TeamCount < 2) continue;
                        cl = snap.GetAliveCount(0); il = snap.GetAliveCount(1);
                        if (cl == 0 || il == 0 || !manager.IsBattleRunning) break;
                    }
                    Time.captureFramerate = 0;
                    for (int i = 0; i < 6; i++) yield return null;
                    var final = manager.Telemetry.Snapshot; cl = final.GetAliveCount(0); il = final.GetAliveCount(1);
                    var hp = new int[agents]; manager.Buffers.combatBuffers.hpReadBuffer.GetData(hp);
                    long cavHp = 0; for (int i = 0; i < agents; i++) if (teams[i] == 0) cavHp += Mathf.Max(0, hp[i]);
                    string row = string.Format(CultureInfo.InvariantCulture,
                        "{{\"case\":\"{0}\",\"rep\":{1},\"cavalry\":{2},\"infantry\":{3},\"cavHp\":{4},\"cavDamage\":{5},\"chargeMult\":{6},\"infHp\":{7},\"infDamage\":{8},\"winner\":\"{9}\",\"simSeconds\":{10},\"cavalryLeft\":{11},\"infantryLeft\":{12},\"cavHpLeft\":{13}}}",
                        c.name, rep, c0, i0, u.combatConfig.maxHp, u.combatConfig.attackDamage, u.combatConfig.chargeDamageMultiplier.ToString(CultureInfo.InvariantCulture), inf.combatConfig.maxHp, inf.combatConfig.attackDamage,
                        il == 0 ? "cavalry" : cl == 0 ? "infantry" : "timeout", sim.ToString("F1", CultureInfo.InvariantCulture), cl, il, cavHp);
                    Debug.Log("CAVALRY_STRENGTH " + row); rows.Add(row);
                    string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "AgentGiants"));
                    Directory.CreateDirectory(dir);
                    File.WriteAllText(Path.Combine(dir, output + ".json"), "[\n" + string.Join(",\n", rows) + "\n]\n", new UTF8Encoding(false));
                    manager.StopBattle(); manager.scenarioConfig = source;
                    foreach (var o in created) Object.Destroy(o);
                }
            }
            finally { Time.captureFramerate = 0; if (manager != null) manager.scenarioConfig = source; }
            Assert.AreEqual(matrix.Length * repeats, rows.Count);
        }
    }
}
#endif
