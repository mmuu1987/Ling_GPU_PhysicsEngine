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
    /// New-unit batch 5 (Troops5Builder.Integrate01): cactoro gunner (ranged) / frog / monkroose regular troops on their shipped
    /// battlefields against the batch-4 80-knight legion, default Attack orders, fixed 1/30 s step. Gate: the troop damages the
    /// knights, the fight ends, no grid overflow; melee troops must also be damaged, the ranged cactoro must land the first hit
    /// (it may win untouched). Winner, duration and losses are measured only (first values, no balance claim).
    /// Screenshots and JSON go to Logs/AgentMonsters3 (runs 01/02 = tuning passes, run 03 = shipped values).
    /// </summary>
    public sealed class Troops5BattlefieldTests
    {
        private const string Folder = "Assets/Game/Content/Characters/Troops5/Prepared01/Integrated/";
        private static readonly bool[] Ranged = { true, false, false };
        private static readonly string[] Keys = { "cactoro", "frog", "monkroose" };
        private static string Dir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "AgentMonsters3"));
        private static string output = "troops5-shipped-01";

        [UnityTearDown] public IEnumerator Cleanup()
        {
            Time.captureFramerate = 0;
            var empty = SceneManager.CreateScene("Troops5Empty" + Time.frameCount);
            SceneManager.SetActiveScene(empty);
            for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene != empty && scene.isLoaded) yield return SceneManager.UnloadSceneAsync(scene);
            }
        }

        [UnityTest, Timeout(1200000)] public IEnumerator Troops5BattlefieldsAsShipped() { output = "troops5-shipped-01"; yield return Run(); }

        private static IEnumerator Run()
        {
            Directory.CreateDirectory(Dir);
            var rows = new List<string>();
            for (int ki = 0; ki < Keys.Length; ki++)
            {
                string key = Keys[ki]; bool ranged = Ranged[ki];
                yield return EditorSceneManager.LoadSceneAsyncInPlayMode(Folder + key + "Battlefield.unity", new LoadSceneParameters(LoadSceneMode.Single));
                MassEngineManager manager = null;
                for (int i = 0; i < 300 && manager == null; i++) { manager = Object.FindFirstObjectByType<MassEngineManager>(); yield return null; }
                Assert.IsNotNull(manager, key);
                for (int i = 0; i < 30; i++) yield return null;
                ScenarioConfig scenario = manager.scenarioConfig;
                Assert.AreEqual(2, scenario.unitTypes.Length);
                UnitTypeConfig troop = scenario.unitTypes[0], knight = scenario.unitTypes[1];
                Assert.AreEqual(0, troop.teamId); Assert.AreEqual(1, knight.teamId);
                Assert.AreEqual(ranged, troop.combatConfig.projectileRange > 0f, "Ranged profile: " + key);
                Assert.LessOrEqual(troop.flockingConfig.agentRadius, 0.8f, "Regular roster radius.");
                try
                {
                    for (int team = 0; team < 2; team++) { manager.ClearFlowTargetOverride(team); manager.SetTeamNavigationOverride(team, true, true); }
                    int agents = manager.UnitTypes.TotalAgentCount;
                    var teams = new int[agents]; manager.Buffers.combatBuffers.teamIdBuffer.GetData(teams);
                    int t0 = 0, k0 = 0; foreach (int t in teams) if (t == 0) t0++; else k0++;
                    Assert.AreEqual(troop.spawnConfig.unitCount, t0); Assert.AreEqual(knight.spawnConfig.unitCount, k0);
                    long tHp0 = Hp(manager, teams, agents, 0), kHp0 = Hp(manager, teams, agents, 1);
                    Shot(manager, teams, agents, output + "-" + key + "-start.png");
                    Time.captureFramerate = 30;
                    manager.StartBattle();
                    float sim = 0f, contact = -1f; int tl = t0, kl = k0; bool midShot = false; string firstHit = "none";
                    while (sim < 180f)
                    {
                        yield return null; sim += 1f / 30f;
                        var snap = manager.Telemetry.Snapshot;
                        if (!snap.valid || snap.TeamCount < 2) continue;
                        tl = snap.GetAliveCount(0); kl = snap.GetAliveCount(1);
                        if (contact < 0)
                        {
                            long tNow = Hp(manager, teams, agents, 0), kNow = Hp(manager, teams, agents, 1);
                            if (tNow < tHp0 || kNow < kHp0) { contact = sim; firstHit = kNow < kHp0 && tNow == tHp0 ? "troops" : tNow < tHp0 && kNow == kHp0 ? "knights" : "both"; }
                        }
                        if (!midShot && contact >= 0 && sim >= contact + 2.5f) { midShot = true; Time.captureFramerate = 0; Shot(manager, teams, agents, output + "-" + key + "-fight.png"); Time.captureFramerate = 30; }
                        if (tl == 0 || kl == 0 || !manager.IsBattleRunning) break;
                    }
                    Time.captureFramerate = 0;
                    for (int i = 0; i < 6; i++) yield return null;
                    var final = manager.Telemetry.Snapshot; tl = final.GetAliveCount(0); kl = final.GetAliveCount(1);
                    long tLost = tHp0 - Hp(manager, teams, agents, 0), kLost = kHp0 - Hp(manager, teams, agents, 1);
                    Shot(manager, teams, agents, output + "-" + key + "-end.png");
                    string winner = kl == 0 ? "troops" : tl == 0 ? "knights" : "timeout";
                    string row = string.Format(CultureInfo.InvariantCulture,
                        "{{\"troop\":\"{0}\",\"troops\":{1},\"knights\":{2},\"hp\":{3},\"damage\":{4},\"speed\":{5},\"radius\":{6},\"range\":{7},\"winner\":\"{8}\",\"simSeconds\":{9:F1},\"troopsLeft\":{10},\"knightsLeft\":{11},\"troopHpLost\":{12},\"knightHpLost\":{13},\"contactAt\":{14:F1},\"peakGridOverflow\":{15},\"projectileRange\":{16},\"firstHit\":\"{17}\"}}",
                        key, t0, k0, troop.combatConfig.maxHp, troop.combatConfig.attackDamage, troop.movementConfig.maxSpeed, troop.flockingConfig.agentRadius, troop.combatConfig.attackRange,
                        winner, sim, tl, kl, tLost, kLost, contact, final.peakGridOverflowPerFrame, troop.combatConfig.projectileRange, firstHit);
                    Debug.Log("TROOPS5_RESULT " + row); rows.Add(row);
                    File.WriteAllText(Path.Combine(Dir, output + ".json"), "[\n" + string.Join(",\n", rows) + "\n]\n", new UTF8Encoding(false));
                    if (ranged) Assert.AreEqual("troops", firstHit, "The ranged " + key + " must land the first hit.");
                    else Assert.Greater(tLost, 0, "Knights must damage the " + key + ".");
                    Assert.Greater(kLost, 0, "The " + key + " must damage knights.");
                    Assert.AreNotEqual("timeout", winner, "Shipped fight must end: " + key);
                    Assert.AreEqual(0, final.peakGridOverflowPerFrame, "Grid must not overflow: " + key);
                    manager.StopBattle();
                }
                finally { Time.captureFramerate = 0; }
            }
            Assert.AreEqual(Keys.Length, rows.Count);
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
                cam.transform.position = new Vector3(c.x - 6f, 7f, c.y - 16f); cam.transform.LookAt(new Vector3(c.x + 3f, 1f, c.y));
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
