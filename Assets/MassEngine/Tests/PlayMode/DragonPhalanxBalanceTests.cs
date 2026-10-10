#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using MassEngine.Projectiles;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace MassEngine.Tests
{
    /// <summary>
    /// Overnight task 3 (measurement only, no asset is written): dragons vs knight blocks of different size/density
    /// on the shipped dragon battlefield. Each case swaps a runtime ScenarioConfig copy, resets and fights to a natural
    /// end (or a cap) at a fixed 1/30 s step. Results: Logs/AgentPhalanx/&lt;matrix&gt;.json and PHALANX log lines.
    /// Hits per fireball = enemy HP lost / dragon attackDamage (includes the direct hit). Both armies get the HUD's default
    /// Attack order unless a case says the knights hold.
    /// </summary>
    public sealed class DragonPhalanxBalanceTests
    {
        private const string Folder = "Assets/Game/Content/Characters/Dragons/Prepared07/Integrated/";
        private static readonly FieldInfo FxField = typeof(MassEngineManager).GetField("projectileImpactFx", BindingFlags.Instance | BindingFlags.NonPublic);

        private struct Case
        {
            public string name; public int dragons, knights, cellCap; public float density; public bool sparse45, noSplash, knightsHold;
            public Case(string n, int d, int k, float rho, bool sparse = false, bool noSplash = false, int cap = 160) { name = n; dragons = d; knights = k; density = rho; sparse45 = sparse; this.noSplash = noSplash; cellCap = cap; knightsHold = false; }
            public Case Hold() { var c = this; c.knightsHold = true; c.name += "-knightsHold"; return c; }
        }

        private static readonly Case[] Matrix =
        {
            new Case("baseline-16-sparse45", 2, 16, 0f, sparse: true),
            new Case("16-dense", 2, 16, 0.8f),
            new Case("60-dense", 2, 60, 0.8f),
            new Case("120-dense", 2, 120, 0.8f),
            new Case("200-dense", 2, 200, 0.8f),
            new Case("120-open", 2, 120, 0.12f),
            new Case("200-open", 2, 200, 0.12f),
            new Case("200-dense-nosplash", 2, 200, 0.8f, noSplash: true),
            new Case("200-dense-cap64", 2, 200, 0.8f, cap: 64), // shipped per-cell capacity: reproduces the overflow
            new Case("120-dense-4dragons", 4, 120, 0.8f),
            new Case("300-dense", 2, 300, 0.8f),
            new Case("60-dense", 2, 60, 0.8f).Hold(), // knights ordered to hold: dragons bombard from 14 m, outside their reach
        };

        // Break-even search for the phalanx battlefields (2 dragons vs one dense 0.8/m2 knight block).
        private static readonly Case[] YoungEven = { new Case("even-80", 2, 80, 0.8f), new Case("even-90", 2, 90, 0.8f), new Case("even-100", 2, 100, 0.8f), new Case("even-110", 2, 110, 0.8f) };
        private static readonly Case[] EvolvedEven = { new Case("even-140", 2, 140, 0.8f), new Case("even-150", 2, 150, 0.8f), new Case("even-160", 2, 160, 0.8f), new Case("even-175", 2, 175, 0.8f) };
        private static readonly Case[] TraceMatrix = { new Case("trace-60-dense", 2, 60, 0.8f) };

        [UnityTearDown] public IEnumerator Cleanup()
        {
            Time.captureFramerate = 0;
            var empty = SceneManager.CreateScene("PhalanxEmpty" + Time.frameCount);
            SceneManager.SetActiveScene(empty);
            for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene != empty && scene.isLoaded) yield return SceneManager.UnloadSceneAsync(scene);
            }
        }

        [UnityTest, Timeout(1200000)] public IEnumerator YoungDragonMatrix() { yield return RunMatrix("dragon", "phalanx-dragon", Matrix); }
        [UnityTest, Timeout(1200000)] public IEnumerator EvolvedDragonMatrix() { yield return RunMatrix("dragon-evolved", "phalanx-dragon-evolved", Matrix); }
        [UnityTest, Timeout(1200000)] public IEnumerator YoungDragonBreakEven() { yield return RunMatrix("dragon", "even-dragon", YoungEven); }
        [UnityTest, Timeout(1200000)] public IEnumerator EvolvedDragonBreakEven() { yield return RunMatrix("dragon-evolved", "even-dragon-evolved", EvolvedEven); }
        [UnityTest, Timeout(1200000)] public IEnumerator YoungDragonTrace() { yield return RunMatrix("dragon", "trace-dragon", TraceMatrix, true); }

        /// <summary>The shipped phalanx battlefields (DragonBuilder.IntegrateFire08) exactly as authored, default Attack orders.</summary>
        [UnityTest, Timeout(600000)] public IEnumerator PhalanxBattlefieldsAsShipped()
        {
            var rows = new List<string>();
            foreach (string key in new[] { "dragon", "dragon-evolved" })
            {
                yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Game/Content/Characters/Dragons/Prepared11/Integrated/" + key + "PhalanxBattlefield.unity", new LoadSceneParameters(LoadSceneMode.Single));
                MassEngineManager manager = null;
                for (int i = 0; i < 300 && manager == null; i++) { manager = Object.FindFirstObjectByType<MassEngineManager>(); yield return null; }
                Assert.IsNotNull(manager);
                for (int i = 0; i < 30; i++) yield return null;
                var sim = manager.systemConfig.simulationConfig;
                int agents = manager.UnitTypes.TotalAgentCount;
                var teams = new int[agents]; manager.Buffers.combatBuffers.teamIdBuffer.GetData(teams);
                int d0 = 0, k0 = 0; foreach (int t in teams) if (t == 0) d0++; else k0++;
                Time.captureFramerate = 30;
                for (int team = 0; team < 2; team++) { manager.ClearFlowTargetOverride(team); manager.SetTeamNavigationOverride(team, true, true); }
                manager.StartBattle();
                float t0 = 0f; int dl = d0, kl = k0;
                while (t0 < 150f)
                {
                    yield return null; t0 += 1f / 30f;
                    var snap = manager.Telemetry.Snapshot;
                    if (!snap.valid || snap.TeamCount < 2) continue;
                    dl = snap.GetAliveCount(0); kl = snap.GetAliveCount(1);
                    if (dl == 0 || kl == 0) break;
                }
                Time.captureFramerate = 0;
                var fx = FxField.GetValue(manager) as ProjectileImpactFx; var counter = new uint[1]; if (fx != null) fx.Counter.GetData(counter);
                string row = string.Format(CultureInfo.InvariantCulture, "{{\"scene\":\"{0}Phalanx\",\"dragons\":{1},\"knights\":{2},\"cellCap\":{3},\"winner\":\"{4}\",\"simSeconds\":{5:F1},\"dragonsLeft\":{6},\"knightsLeft\":{7},\"impacts\":{8},\"peakGridOverflow\":{9}}}",
                    key, d0, k0, sim.maxAgentsPerCell, kl == 0 ? "dragons" : dl == 0 ? "knights" : "timeout", t0, dl, kl, counter[0], manager.Telemetry.Snapshot.peakGridOverflowPerFrame);
                Debug.Log("PHALANX_SCENE " + row); rows.Add(row);
                Assert.AreEqual(256, sim.maxAgentsPerCell); Assert.AreEqual(0, manager.Telemetry.Snapshot.peakGridOverflowPerFrame, "Phalanx grid must never overflow.");
                Assert.AreNotEqual(0, counter[0], "Impact effect is live on the phalanx battlefield.");
                manager.StopBattle();
            }
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "AgentPhalanx"));
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "phalanx-shipped.json"), "[\n" + string.Join(",\n", rows) + "\n]\n", new UTF8Encoding(false));
        }

        /// <summary>The shipped phalanx battlefields (DragonBuilder.IntegrateFire10: evolved phalanx 150 knights) exactly as authored, default Attack orders.</summary>
        [UnityTest, Timeout(600000)] public IEnumerator PhalanxBattlefieldsAsShipped13()
        {
            var rows = new List<string>();
            foreach (string key in new[] { "dragon-evolved", "dragon-evolved" })
            {
                yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Game/Content/Characters/Dragons/Prepared13/Integrated/" + key + "PhalanxBattlefield.unity", new LoadSceneParameters(LoadSceneMode.Single));
                MassEngineManager manager = null;
                for (int i = 0; i < 300 && manager == null; i++) { manager = Object.FindFirstObjectByType<MassEngineManager>(); yield return null; }
                Assert.IsNotNull(manager);
                for (int i = 0; i < 30; i++) yield return null;
                var sim = manager.systemConfig.simulationConfig;
                int agents = manager.UnitTypes.TotalAgentCount;
                var teams = new int[agents]; manager.Buffers.combatBuffers.teamIdBuffer.GetData(teams);
                int d0 = 0, k0 = 0; foreach (int t in teams) if (t == 0) d0++; else k0++;
                Time.captureFramerate = 30;
                for (int team = 0; team < 2; team++) { manager.ClearFlowTargetOverride(team); manager.SetTeamNavigationOverride(team, true, true); }
                manager.StartBattle();
                float t0 = 0f; int dl = d0, kl = k0;
                while (t0 < 150f)
                {
                    yield return null; t0 += 1f / 30f;
                    var snap = manager.Telemetry.Snapshot;
                    if (!snap.valid || snap.TeamCount < 2) continue;
                    dl = snap.GetAliveCount(0); kl = snap.GetAliveCount(1);
                    if (dl == 0 || kl == 0) break;
                }
                Time.captureFramerate = 0;
                var fx = FxField.GetValue(manager) as ProjectileImpactFx; var counter = new uint[1]; if (fx != null) fx.Counter.GetData(counter);
                string row = string.Format(CultureInfo.InvariantCulture, "{{\"scene\":\"{0}Phalanx\",\"dragons\":{1},\"knights\":{2},\"cellCap\":{3},\"winner\":\"{4}\",\"simSeconds\":{5:F1},\"dragonsLeft\":{6},\"knightsLeft\":{7},\"impacts\":{8},\"peakGridOverflow\":{9}}}",
                    key, d0, k0, sim.maxAgentsPerCell, kl == 0 ? "dragons" : dl == 0 ? "knights" : "timeout", t0, dl, kl, counter[0], manager.Telemetry.Snapshot.peakGridOverflowPerFrame);
                Debug.Log("PHALANX_SCENE " + row); rows.Add(row);
                Assert.AreEqual(256, sim.maxAgentsPerCell); Assert.AreEqual(0, manager.Telemetry.Snapshot.peakGridOverflowPerFrame, "Phalanx grid must never overflow.");
                Assert.AreNotEqual(0, counter[0], "Impact effect is live on the phalanx battlefield.");
                manager.StopBattle();
            }
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "AgentPhalanx"));
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "phalanx-shipped-13.json"), "[\n" + string.Join(",\n", rows) + "\n]\n", new UTF8Encoding(false));
        }

        private static IEnumerator RunMatrix(string key, string output, Case[] matrix, bool trace = false)
        {
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(Folder + key + "Battlefield.unity", new LoadSceneParameters(LoadSceneMode.Single));
            MassEngineManager manager = null;
            for (int i = 0; i < 300 && manager == null; i++) { manager = Object.FindFirstObjectByType<MassEngineManager>(); yield return null; }
            Assert.IsNotNull(manager);
            for (int i = 0; i < 30; i++) yield return null;
            ScenarioConfig source = manager.scenarioConfig;
            Assert.IsNotNull(source); Assert.AreEqual(2, source.unitTypes.Length);
            UnitTypeConfig dragon = source.unitTypes[0], knight = source.unitTypes[1];
            MassEngineSystemConfig sourceSystem = manager.systemConfig;
            Debug.Log("PHALANX scene cellSize=" + sourceSystem.simulationConfig.cellSize + " maxAgentsPerCell=" + sourceSystem.simulationConfig.maxAgentsPerCell);
            Assert.Greater(dragon.combatConfig.projectileSplashRadius, 0f, "Dragon slot first.");
            var rows = new List<string>();
            Time.captureFramerate = 30; // fixed 1/30 s simulation step, runs as fast as the GPU allows
            try
            {
                foreach (var c in matrix)
                {
                    var created = new List<Object>();
                    var scenario = ScriptableObject.CreateInstance<ScenarioConfig>(); created.Add(scenario);
                    var d = Clone(dragon, created); var k = Clone(knight, created);
                    d.spawnConfig.unitCount = c.dragons; d.spawnConfig.spawnCenter = new Vector3(-22f, 0f, 0f);
                    k.spawnConfig.unitCount = c.knights; k.spawnConfig.spawnCenter = new Vector3(18f, 0f, 0f);
                    if (c.sparse45) k.spawnConfig.spawnSize = new Vector3(45f, 0f, 45f);
                    else { k.spawnConfig.spawnSize = Vector3.zero; k.spawnConfig.formationDensity = c.density; k.spawnConfig.formationAspect = 2f; }
                    if (c.noSplash) d.combatConfig.projectileSplashRadius = 0f;
                                        scenario.unitTypes = new[] { d, k };
                    var system = Object.Instantiate(sourceSystem); created.Add(system);
                    system.simulationConfig = Object.Instantiate(sourceSystem.simulationConfig); created.Add(system.simulationConfig);
                    system.simulationConfig.maxAgentsPerCell = c.cellCap;
                    manager.systemConfig = system;
                    manager.scenarioConfig = scenario;
                    manager.ResetScenario();
                    // Same as the HUD's default start: ArmyOrder.Attack for every army (WarSandboxBattleController.IssueOrderInternal).
                    for (int team = 0; team < 2; team++)
                    {
                        manager.ClearFlowTargetOverride(team);
                        manager.SetTeamNavigationOverride(team, !(team == 1 && c.knightsHold), !(team == 1 && c.knightsHold));
                    }
                    for (int i = 0; i < 20; i++) yield return null;
                    int agents = manager.UnitTypes.TotalAgentCount;
                    var teams = new int[agents]; manager.Buffers.combatBuffers.teamIdBuffer.GetData(teams);
                    long hp0 = EnemyHp(manager, teams, agents); long dragonHp0 = EnemyHp(manager, teams, agents, 0);
                    manager.StartBattle();
                    float sim = 0f, firstKillAt = -1f; int dragonsAlive = c.dragons, knightsAlive = c.knights, killed10 = -1;
                    var counter = new uint[1];
                    while (sim < 150f)
                    {
                        yield return null;
                        sim += 1f / 30f;
                        var snap = manager.Telemetry != null ? manager.Telemetry.Snapshot : default;
                        if (!snap.valid || snap.TeamCount < 2) continue;
                        dragonsAlive = snap.GetAliveCount(0); knightsAlive = snap.GetAliveCount(1);
                        if (trace && Time.frameCount % 90 == 0) Trace(manager, teams, agents, sim, k.combatConfig.attackRange);
                        if (firstKillAt < 0 && knightsAlive < c.knights) firstKillAt = sim;
                        if (killed10 < 0 && firstKillAt >= 0 && sim >= firstKillAt + 10f) killed10 = c.knights - knightsAlive;
                        if (dragonsAlive == 0 || knightsAlive == 0 || !manager.IsBattleRunning) break;
                    }
                    for (int i = 0; i < 6; i++) yield return null; // let the async telemetry catch the final state
                    var final = manager.Telemetry.Snapshot;
                    dragonsAlive = final.GetAliveCount(0); knightsAlive = final.GetAliveCount(1);
                    var fx = FxField != null ? FxField.GetValue(manager) as ProjectileImpactFx : null;
                    uint impacts = 0; if (fx != null && fx.IsValid) { fx.Counter.GetData(counter); impacts = counter[0]; }
                    long lost = hp0 - EnemyHp(manager, teams, agents); long dragonLost = dragonHp0 - EnemyHp(manager, teams, agents, 0);
                    int launched = manager.TotalLaunchedProjectiles;
                    float hits = lost / Mathf.Max(1f, d.combatConfig.attackDamage);
                    float perShot = hits / Mathf.Max(1, c.noSplash ? launched : (int)impacts);
                    string winner = knightsAlive == 0 ? "dragons" : dragonsAlive == 0 ? "knights" : "timeout";
                    string row = string.Format(CultureInfo.InvariantCulture,
                        "{{\"case\":\"{0}\",\"dragon\":\"{1}\",\"dragons\":{2},\"knights\":{3},\"density\":{4},\"splash\":{5},\"winner\":\"{6}\",\"simSeconds\":{7:F1},\"dragonsLeft\":{8},\"knightsLeft\":{9},\"fireballs\":{10},\"impacts\":{11},\"hits\":{12:F0},\"hitsPerFireball\":{13:F2},\"firstKillAt\":{14:F1},\"killsFirst10s\":{15},\"cellCap\":{16},\"peakGridOverflow\":{17},\"dragonHpLostPct\":{18}}}",
                        c.name, key, c.dragons, c.knights, c.sparse45 ? (16f / (45f * 45f)) : c.density, d.combatConfig.projectileSplashRadius, winner, sim, dragonsAlive, knightsAlive, launched, impacts, hits, perShot, firstKillAt, killed10, c.cellCap, final.peakGridOverflowPerFrame, (100.0 * dragonLost / System.Math.Max(1L, dragonHp0)).ToString("F1", CultureInfo.InvariantCulture));
                    Debug.Log("PHALANX " + row);
                    rows.Add(row);
                    manager.StopBattle();
                    manager.scenarioConfig = source; manager.systemConfig = sourceSystem;
                    foreach (var o in created) Object.Destroy(o);
                }
            }
            finally
            {
                Time.captureFramerate = 0;
                if (manager != null) { manager.scenarioConfig = source; manager.systemConfig = sourceSystem; }
            }
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "AgentPhalanx"));
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, output + ".json"), "[\n" + string.Join(",\n", rows) + "\n]\n", new UTF8Encoding(false));
            Assert.AreEqual(matrix.Length, rows.Count);
        }

        private static void Trace(MassEngineManager manager, int[] teams, int agents, float sim, float knightRange)
        {
            var pos = new Vector2[agents]; manager.Buffers.agentPositionReadBuffer.GetData(pos);
            var hp = new int[agents]; manager.Buffers.combatBuffers.hpReadBuffer.GetData(hp);
            Vector2 c0 = Vector2.zero, c1 = Vector2.zero; int n0 = 0, n1 = 0; float best = float.MaxValue; int inRange = 0;
            for (int i = 0; i < agents; i++)
            {
                if (hp[i] <= 0) continue;
                if (teams[i] == 0) { c0 += pos[i]; n0++; } else { c1 += pos[i]; n1++; }
            }
            for (int i = 0; i < agents; i++)
            {
                if (hp[i] <= 0 || teams[i] != 1) continue;
                float nearest = float.MaxValue;
                for (int j = 0; j < agents; j++) if (hp[j] > 0 && teams[j] == 0) nearest = Mathf.Min(nearest, Vector2.Distance(pos[i], pos[j]));
                best = Mathf.Min(best, nearest); if (nearest <= knightRange) inRange++;
            }
            int dragonHp = 0; for (int i = 0; i < agents; i++) if (teams[i] == 0) dragonHp += Mathf.Max(0, hp[i]);
            Debug.Log(string.Format(CultureInfo.InvariantCulture, "PHALANX_TRACE t={0:F1} dragons={1} at={2} knights={3} at={4} nearestKnightToDragon={5:F2} knightsWithinAttackRange={6} (range {7:F2}) dragonHp={8}",
                sim, n0, n0 > 0 ? (c0 / n0).ToString("F1") : "-", n1, n1 > 0 ? (c1 / n1).ToString("F1") : "-", best, inRange, knightRange, dragonHp));
        }

        private static UnitTypeConfig Clone(UnitTypeConfig source, List<Object> created)
        {
            var u = Object.Instantiate(source); u.name = source.name; created.Add(u);
            u.spawnConfig = Object.Instantiate(source.spawnConfig); created.Add(u.spawnConfig);
            u.combatConfig = Object.Instantiate(source.combatConfig); created.Add(u.combatConfig);
            return u;
        }

        private static long EnemyHp(MassEngineManager manager, int[] teams, int agents, int team = 1)
        {
            var hp = new int[agents]; manager.Buffers.combatBuffers.hpReadBuffer.GetData(hp);
            long sum = 0; for (int i = 0; i < agents; i++) if (teams[i] == team) sum += Mathf.Max(0, hp[i]);
            return sum;
        }
    }
}
#endif
