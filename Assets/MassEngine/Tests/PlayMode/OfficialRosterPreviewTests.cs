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
    /// Captures catalog-card previews (1280x592, the card's ~2.17:1 aspect so RawImage does not distort them) for the
    /// new-unit battlefields joined into the official catalog (OfficialRosterBuilder). One setup frame and one frame
    /// ~2.5 s after first contact per battlefield, written to Logs/OfficialRoster/shots-03. Measures only; the functional
    /// gates live in the per-unit battlefield tests.
    /// </summary>
    public sealed class OfficialRosterPreviewTests
    {
        private const int W = 1280, H = 592;
        private static readonly string[][] Scenes =
        {
            new[] { "cavalry-mounted-knight", "Assets/Game/Cavalry/Prepared07/Integrated/Battlefield.unity" },
            new[] { "dragons-dragon", "Assets/Game/Dragons/Prepared13/Integrated/dragonBattlefield.unity" },
            new[] { "dragons-dragon-phalanx", "Assets/Game/Dragons/Prepared13/Integrated/dragonPhalanxBattlefield.unity" },
            new[] { "dragons-dragon-evolved", "Assets/Game/Dragons/Prepared13/Integrated/dragon-evolvedBattlefield.unity" },
            new[] { "dragons-dragon-evolved-phalanx", "Assets/Game/Dragons/Prepared13/Integrated/dragon-evolvedPhalanxBattlefield.unity" },
            new[] { "giants-demon", "Assets/Game/Giants/Prepared02/Integrated/giant-demonBattlefield.unity" },
            new[] { "giants-dino", "Assets/Game/Giants/Prepared02/Integrated/giant-dinoBattlefield.unity" },
            new[] { "nonhuman2-triceratops", "Assets/Game/NonhumanBatch2/Prepared01/Integrated/triceratopsBattlefield.unity" },
            new[] { "nonhuman2-stegosaurus", "Assets/Game/NonhumanBatch2/Prepared01/Integrated/stegosaurusBattlefield.unity" },
            new[] { "nonhuman2-spider", "Assets/Game/NonhumanBatch2/Prepared01/Integrated/spiderBattlefield.unity" },
            new[] { "unified-regular", "Assets/Game/UnifiedRoster/Version03/Scenes/RegularBattlefield.unity" },
            new[] { "unified-all-regular", "Assets/Game/UnifiedRoster/Version03/Scenes/AllRegularBattlefield.unity" },
            new[] { "unified-large", "Assets/Game/UnifiedRoster/Version03/Scenes/LargeBattlefield.unity" },
        };
        /// <summary>Battlefields added in official v2 (giant batch 3); captured on their own so v1 previews are not re-shot.</summary>
        private static readonly string[][] ScenesV2 =
        {
            new[] { "giants-yeti", "Assets/Game/Giants3/Prepared01/Integrated/giant-yetiBattlefield.unity" },
        };
        private static readonly string[][] ScenesV3 =
        {
            new[] { "giants-bluedemon", "Assets/Game/Giants3/Prepared02/Integrated/giant-bluedemonBattlefield.unity" },
            new[] { "giants-alien", "Assets/Game/Giants3/Prepared02/Integrated/giant-alienBattlefield.unity" },
        };
        /// <summary>Official v4 (new-unit batch 4: regular-size skull orc, ninja, tribal warrior vs the knight legion).</summary>
        private static readonly string[][] ScenesV4 =
        {
            new[] { "troops4-orcskull", "Assets/Game/Troops4/Prepared01/Integrated/orcskullBattlefield.unity" },
            new[] { "troops4-ninja", "Assets/Game/Troops4/Prepared01/Integrated/ninjaBattlefield.unity" },
            new[] { "troops4-tribal", "Assets/Game/Troops4/Prepared01/Integrated/tribalBattlefield.unity" },
        };
        private static string dir = "shots-03";
        private static string Dir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "OfficialRoster", dir));

        [UnityTearDown] public IEnumerator Cleanup()
        {
            Time.captureFramerate = 0;
            var empty = SceneManager.CreateScene("OfficialEmpty" + Time.frameCount);
            SceneManager.SetActiveScene(empty);
            for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene != empty && scene.isLoaded) yield return SceneManager.UnloadSceneAsync(scene);
            }
        }

        [UnityTest, Timeout(1500000)] public IEnumerator CaptureOfficialPreviews() { dir = "shots-03"; yield return Capture(Scenes); }
        [UnityTest, Timeout(900000)] public IEnumerator CaptureOfficialV2Previews() { dir = "shots-04"; yield return Capture(ScenesV2); }
        [UnityTest, Timeout(900000)] public IEnumerator CaptureOfficialV3Previews() { dir = "shots-05"; yield return Capture(ScenesV3); }
        [UnityTest, Timeout(900000)] public IEnumerator CaptureOfficialV4Previews() { dir = "shots-06"; yield return Capture(ScenesV4); }

        private static IEnumerator Capture(string[][] scenes)
        {
            Directory.CreateDirectory(Dir);
            var rows = new List<string>();
            foreach (var item in scenes)
            {
                string id = item[0];
                yield return EditorSceneManager.LoadSceneAsyncInPlayMode(item[1], new LoadSceneParameters(LoadSceneMode.Single));
                MassEngineManager manager = null;
                for (int i = 0; i < 300 && manager == null; i++) { manager = Object.FindFirstObjectByType<MassEngineManager>(); yield return null; }
                Assert.IsNotNull(manager, id);
                for (int i = 0; i < 30; i++) yield return null;
                int agents = manager.UnitTypes.TotalAgentCount;
                var teams = new int[agents]; manager.Buffers.combatBuffers.teamIdBuffer.GetData(teams);
                int teamCount = 0; foreach (int t in teams) teamCount = Mathf.Max(teamCount, t + 1);
                for (int team = 0; team < teamCount; team++) { manager.ClearFlowTargetOverride(team); manager.SetTeamNavigationOverride(team, true, true); }
                Shot(manager, agents, teams, id + "-setup.png");
                long hp0 = Hp(manager, agents);
                float sim = 0f, contact = -1f; bool shot = false;
                try
                {
                    Time.captureFramerate = 30;
                    manager.StartBattle();
                    while (sim < 45f)
                    {
                        yield return null; sim += 1f / 30f;
                        if (contact < 0 && Time.frameCount % 15 == 0 && Hp(manager, agents) < hp0) contact = sim;
                        if (contact >= 0 && sim >= contact + 2.5f)
                        { Time.captureFramerate = 0; Shot(manager, agents, teams, id + "-fight.png"); shot = true; break; }
                        if (!manager.IsBattleRunning) break;
                    }
                    Time.captureFramerate = 0;
                    if (!shot) Shot(manager, agents, teams, id + "-fight.png");
                    manager.StopBattle();
                }
                finally { Time.captureFramerate = 0; }
                rows.Add(string.Format(CultureInfo.InvariantCulture, "{{\"id\":\"{0}\",\"agents\":{1},\"teams\":{2},\"contact\":{3:F1},\"sim\":{4:F1} }}",
                    id, agents, teamCount, contact, sim));
                File.WriteAllText(Path.Combine(Dir, "previews.json"), "[\n" + string.Join(",\n", rows) + "\n]\n", new UTF8Encoding(false));
            }
            Assert.AreEqual(scenes.Length, rows.Count);
        }

        /// <summary>Frames team 0 (the featured unit) and the enemies nearest to it; steep pitch keeps the horizon out of the card.</summary>
        private static void Shot(MassEngineManager manager, int agents, int[] teams, string file)
        {
            var pos = new Vector2[agents]; manager.Buffers.agentPositionReadBuffer.GetData(pos);
            var hp = new int[agents]; manager.Buffers.combatBuffers.hpReadBuffer.GetData(hp);
            Vector2 own = Vector2.zero; int n = 0;
            for (int i = 0; i < agents; i++) if (teams[i] == 0 && hp[i] > 0) { own += pos[i]; n++; }
            own = n > 0 ? own / n : Vector2.zero;
            float best = float.MaxValue; for (int i = 0; i < agents; i++) if (teams[i] != 0 && hp[i] > 0) best = Mathf.Min(best, Vector2.Distance(pos[i], own));
            Vector2 foe = Vector2.zero; int m = 0;
            for (int i = 0; i < agents; i++) if (teams[i] != 0 && hp[i] > 0 && Vector2.Distance(pos[i], own) <= best + 14f) { foe += pos[i]; m++; }
            foe = m > 0 ? foe / m : own + new Vector2(20f, 0f);
            Vector2 c = (own + foe) * 0.5f; float gap = Vector2.Distance(own, foe);
            float d = Mathf.Clamp(gap * 1.1f + 16f, 20f, 80f);
            var cam = Camera.allCameras[0]; var p = cam.transform.position; var r = cam.transform.rotation; var old = cam.targetTexture;
            var rt = new RenderTexture(W, H, 24); var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            try
            {
                cam.transform.position = new Vector3(c.x - 0.25f * d, 0.72f * d, c.y - 0.78f * d);
                cam.transform.LookAt(new Vector3(c.x, 1.5f, c.y - 0.1f * d));
                cam.targetTexture = rt; cam.Render(); RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply();
                File.WriteAllBytes(Path.Combine(Dir, file), tex.EncodeToPNG());
            }
            finally { cam.targetTexture = old; RenderTexture.active = null; cam.transform.SetPositionAndRotation(p, r); Object.Destroy(rt); Object.Destroy(tex); }
        }

        private static long Hp(MassEngineManager manager, int agents)
        {
            var hp = new int[agents]; manager.Buffers.combatBuffers.hpReadBuffer.GetData(hp);
            long sum = 0; for (int i = 0; i < agents; i++) sum += Mathf.Max(0, hp[i]); return sum;
        }
    }
}
#endif
