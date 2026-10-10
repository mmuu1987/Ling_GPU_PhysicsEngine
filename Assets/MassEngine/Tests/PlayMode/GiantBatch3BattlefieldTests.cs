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
    /// Giant batch 3 (GiantBatch3Builder.IntegrateYeti01): the giant Yeti on its shipped battlefield, default Attack orders,
    /// fixed 1/30 s step. Same functional gate as GiantBattlefieldTests (both sides deal damage, no grid overflow, the fight
    /// ends) plus a knight-count sweep that only measures. Screenshots and JSON go to Logs/AgentMonsters3.
    /// </summary>
    public sealed class GiantBatch3BattlefieldTests
    {
        private const string Folder = "Assets/Game/Content/Characters/Giants3/Prepared01/Integrated/", Folder2 = "Assets/Game/Content/Characters/Giants3/Prepared02/Integrated/";
        private static readonly string[] Keys = { "giant-yeti" }, Keys2 = { "giant-bluedemon", "giant-alien" };
        private static readonly int[] Sweep = { 24, 48, 96, 144 };
        private static string Dir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "AgentMonsters3"));

        [UnityTearDown] public IEnumerator Cleanup()
        {
            Time.captureFramerate = 0;
            var empty = SceneManager.CreateScene("Giant3Empty" + Time.frameCount);
            SceneManager.SetActiveScene(empty);
            for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene != empty && scene.isLoaded) yield return SceneManager.UnloadSceneAsync(scene);
            }
        }

        [UnityTest, Timeout(900000)] public IEnumerator Giant3BattlefieldsAsShipped() { yield return Run("giants3-shipped", new[] { 0 }, true, Folder, Keys); }
        [UnityTest, Timeout(1500000)] public IEnumerator Giant3KnightSweep() { yield return Run("giants3-sweep", Sweep, false, Folder, Keys); }
        // Batch 3b (IntegrateBlueDemonAlien02): blue demon (melee) and alien (ranged, direct shots, no splash).
        [UnityTest, Timeout(900000)] public IEnumerator Giant3BlueDemonAlienAsShipped() { yield return Run("giants3b-shipped", new[] { 0 }, true, Folder2, Keys2); }
        [UnityTest, Timeout(1500000)] public IEnumerator Giant3BlueDemonAlienKnightSweep() { yield return Run("giants3b-sweep", Sweep, false, Folder2, Keys2); }

        private static IEnumerator Run(string output, int[] counts, bool shipped, string folder, string[] keys)
        {
            Directory.CreateDirectory(Dir);
            var rows = new List<string>();
            foreach (string key in keys)
            {
                yield return EditorSceneManager.LoadSceneAsyncInPlayMode(folder + key + "Battlefield.unity", new LoadSceneParameters(LoadSceneMode.Single));
                MassEngineManager manager = null;
                for (int i = 0; i < 300 && manager == null; i++) { manager = Object.FindFirstObjectByType<MassEngineManager>(); yield return null; }
                Assert.IsNotNull(manager);
                for (int i = 0; i < 30; i++) yield return null;
                ScenarioConfig source = manager.scenarioConfig;
                Assert.AreEqual(2, source.unitTypes.Length);
                UnitTypeConfig giant = source.unitTypes[0], knight = source.unitTypes[1];
                bool ranged = key == "giant-alien";
                if (ranged) { Assert.Greater(giant.combatConfig.projectileRange, 10f, "The alien is ranged."); Assert.AreEqual(0f, giant.combatConfig.projectileSplashRadius, "Alien shots have no splash."); }
                else Assert.AreEqual(0f, giant.combatConfig.projectileRange, "Giants are melee.");
                Assert.AreEqual(2, giant.spawnConfig.unitCount); Assert.AreEqual(16, knight.spawnConfig.unitCount);
                try
                {
                    foreach (int n in counts)
                    {
                        var created = new List<Object>();
                        if (!shipped)
                        {
                            var scenario = ScriptableObject.CreateInstance<ScenarioConfig>(); created.Add(scenario);
                            var k = Object.Instantiate(knight); k.name = knight.name; created.Add(k);
                            k.spawnConfig = Object.Instantiate(knight.spawnConfig); created.Add(k.spawnConfig);
                            k.spawnConfig.unitCount = n;
                            scenario.unitTypes = new[] { giant, k };
                            manager.scenarioConfig = scenario;
                            manager.ResetScenario();
                            for (int i = 0; i < 20; i++) yield return null;
                        }
                        for (int team = 0; team < 2; team++) { manager.ClearFlowTargetOverride(team); manager.SetTeamNavigationOverride(team, true, true); }
                        int agents = manager.UnitTypes.TotalAgentCount;
                        var teams = new int[agents]; manager.Buffers.combatBuffers.teamIdBuffer.GetData(teams);
                        int g0 = 0, k0 = 0; foreach (int t in teams) if (t == 0) g0++; else k0++;
                        long gHp0 = Hp(manager, teams, agents, 0), kHp0 = Hp(manager, teams, agents, 1);
                        if (shipped) Shot(manager, teams, agents, key + "-start.png");
                        Time.captureFramerate = 30;
                        manager.StartBattle();
                        float sim = 0f, contact = -1f, firstKnightHit = -1f, firstGiantHit = -1f; int gl = g0, kl = k0; bool midShot = false;
                        while (sim < 180f)
                        {
                            yield return null; sim += 1f / 30f;
                            var snap = manager.Telemetry.Snapshot;
                            if (!snap.valid || snap.TeamCount < 2) continue;
                            gl = snap.GetAliveCount(0); kl = snap.GetAliveCount(1);
                            if ((firstKnightHit < 0 || firstGiantHit < 0) && Time.frameCount % 3 == 0)
                            {
                                if (firstKnightHit < 0 && Hp(manager, teams, agents, 1) < kHp0) firstKnightHit = sim;
                                if (firstGiantHit < 0 && Hp(manager, teams, agents, 0) < gHp0) firstGiantHit = sim;
                            }
                            if (contact < 0 && (gl < g0 || kl < k0 || Time.frameCount % 15 == 0 && Hp(manager, teams, agents, 0) < gHp0)) contact = sim;
                            if (shipped && !midShot && contact >= 0 && sim >= contact + 2.5f) { midShot = true; Time.captureFramerate = 0; Shot(manager, teams, agents, key + "-fight.png"); Time.captureFramerate = 30; }
                            if (gl == 0 || kl == 0 || !manager.IsBattleRunning) break;
                        }
                        Time.captureFramerate = 0;
                        for (int i = 0; i < 6; i++) yield return null;
                        var final = manager.Telemetry.Snapshot; gl = final.GetAliveCount(0); kl = final.GetAliveCount(1);
                        long gLost = gHp0 - Hp(manager, teams, agents, 0), kLost = kHp0 - Hp(manager, teams, agents, 1);
                        if (shipped) Shot(manager, teams, agents, key + "-end.png");
                        string winner = kl == 0 ? "giants" : gl == 0 ? "knights" : "timeout";
                        string row = string.Format(CultureInfo.InvariantCulture,
                            "{{\"giant\":\"{0}\",\"giants\":{1},\"knights\":{2},\"giantHp\":{3},\"giantDamage\":{4},\"giantRadius\":{5},\"giantRange\":{6},\"winner\":\"{7}\",\"simSeconds\":{8},\"giantsLeft\":{9},\"knightsLeft\":{10},\"giantHpLost\":{11},\"knightHpLost\":{12},\"contactAt\":{13},\"peakGridOverflow\":{14}}}",
                            key, g0, k0, giant.combatConfig.maxHp, giant.combatConfig.attackDamage, giant.flockingConfig.agentRadius.ToString("F2", CultureInfo.InvariantCulture), giant.combatConfig.attackRange.ToString("F2", CultureInfo.InvariantCulture),
                            winner, sim.ToString("F1", CultureInfo.InvariantCulture), gl, kl, gLost, kLost, contact.ToString("F1", CultureInfo.InvariantCulture), final.peakGridOverflowPerFrame);
                        row = row.Substring(0, row.Length - 1) + string.Format(CultureInfo.InvariantCulture, ",\"ranged\":{0},\"firstKnightHit\":{1},\"firstGiantHit\":{2}}}",
                            ranged ? "true" : "false", firstKnightHit.ToString("F1", CultureInfo.InvariantCulture), firstGiantHit.ToString("F1", CultureInfo.InvariantCulture));
                        Debug.Log("GIANT3_RESULT " + row); rows.Add(row);
                        File.WriteAllText(Path.Combine(Dir, output + ".json"), "[\n" + string.Join(",\n", rows) + "\n]\n", new UTF8Encoding(false));
                        if (shipped)
                        {
                            // A ranged giant may legitimately win the 16-knight showcase untouched; its reach is checked in the sweep instead.
                            if (!ranged) Assert.Greater(gLost, 0, "Knights must damage the giant (reach covers the giant radius).");
                            Assert.Greater(kLost, 0, "The giant must damage knights.");
                            Assert.AreNotEqual("timeout", winner, "Shipped giant fight must end.");
                            Assert.AreEqual(0, final.peakGridOverflowPerFrame, "Giant grid must not overflow.");
                            if (ranged) Assert.IsTrue(firstKnightHit >= 0 && (firstGiantHit < 0 || firstKnightHit < firstGiantHit),
                                "The ranged alien must hit knights before they reach it (knight " + firstKnightHit + "s, alien " + firstGiantHit + "s).");
                        }
                        if (!shipped && ranged && n >= 48) Assert.Greater(gLost, 0, "Knights must be able to reach and damage the ranged giant.");
                        manager.StopBattle();
                        manager.scenarioConfig = source;
                        foreach (var o in created) Object.Destroy(o);
                    }
                }
                finally { Time.captureFramerate = 0; if (manager != null) manager.scenarioConfig = source; }
            }
            Assert.AreEqual(counts.Length * keys.Length, rows.Count);
        }

        private static void Shot(MassEngineManager manager, int[] teams, int agents, string file)
        {
            var pos = new Vector2[agents]; manager.Buffers.agentPositionReadBuffer.GetData(pos);
            var hp = new int[agents]; manager.Buffers.combatBuffers.hpReadBuffer.GetData(hp);
            Vector2 c = Vector2.zero; int n = 0;
            for (int i = 0; i < agents; i++) if (teams[i] == 0 && hp[i] > 0) { c += pos[i]; n++; }
            if (n == 0) { for (int i = 0; i < agents; i++) c += pos[i]; n = agents; }
            c /= Mathf.Max(1, n);
            var cam = Camera.allCameras[0]; var p = cam.transform.position; var r = cam.transform.rotation; var old = cam.targetTexture;
            var rt = new RenderTexture(1280, 720, 24); var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            try
            {
                cam.transform.position = new Vector3(c.x - 10f, 9f, c.y - 22f); cam.transform.LookAt(new Vector3(c.x + 4f, 2.5f, c.y));
                cam.targetTexture = rt; cam.Render(); RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); tex.Apply();
                File.WriteAllBytes(Path.Combine(Dir, file), tex.EncodeToPNG());
            }
            finally { cam.targetTexture = old; RenderTexture.active = null; cam.transform.SetPositionAndRotation(p, r); Object.Destroy(rt); Object.Destroy(tex); }
        }

        private static long Hp(MassEngineManager manager, int[] teams, int agents, int team)
        {
            var hp = new int[agents]; manager.Buffers.combatBuffers.hpReadBuffer.GetData(hp);
            long sum = 0; for (int i = 0; i < agents; i++) if (teams[i] == team) sum += Mathf.Max(0, hp[i]);
            return sum;
        }
    }
}
#endif
