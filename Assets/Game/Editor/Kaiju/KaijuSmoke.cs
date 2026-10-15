using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MassEngine.Game.Editor
{
    /// <summary>
    /// Kaiju B check (2026-10-15): enters Play, opens a 2048 map, replaces the deployment with
    /// 巨兽暴龙 ×6 (team 0) against the map's own team-1 infantry, starts the battle, logs alive counts / HP and
    /// renders a few shots from a temporary camera. Nothing is saved. Log + shots: Logs/Kaiju/
    /// </summary>
    [InitializeOnLoad]
    public static class KaijuSmoke
    {
        const string ActiveKey = "KaijuSmoke.Active";
        const string Dir = "Logs/Kaiju";
        const string LogPath = Dir + "/smoke.txt";
        const string MapId = "rolling-hills-2048";
        const string KaijuPath = "Assets/Game/Content/Characters/Kaiju/Dino/kaiju-dino.asset";
        static IEnumerator routine; static double waitUntil, deadline;

        static KaijuSmoke()
        {
            EditorApplication.playModeStateChanged += change =>
            {
                if (!SessionState.GetBool(ActiveKey, false)) return;
                if (change == PlayModeStateChange.EnteredPlayMode) Begin();
                else if (change == PlayModeStateChange.EnteredEditMode) { SessionState.SetBool(ActiveKey, false); Log("finished (left Play)"); }
            };
            if (SessionState.GetBool(ActiveKey, false) && EditorApplication.isPlaying) Begin();
        }

        [MenuItem("Tools/巨兽/对战检查（自动进 Play，自动退出）", false, 50)]
        static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            Directory.CreateDirectory(Dir);
            File.WriteAllText(LogPath, "");
            var menu = AssetDatabase.LoadAssetAtPath<SceneAsset>(EditorBuildSettings.scenes[0].path);
            EditorSceneManager.playModeStartScene = menu;
            SessionState.SetBool(ActiveKey, true);
            Application.runInBackground = true;
            Log("entering play");
            EditorApplication.EnterPlaymode();
        }

        static void Begin()
        {
            if (routine != null) return;
            EditorSceneManager.playModeStartScene = null;
            routine = Steps(); waitUntil = 0; deadline = EditorApplication.timeSinceStartup + 600;
            EditorApplication.update += Tick;
        }

        static void Tick()
        {
            if (routine == null) { EditorApplication.update -= Tick; return; }
            EditorApplication.QueuePlayerLoopUpdate();
            bool more = false;
            if (EditorApplication.timeSinceStartup > deadline) Log("ERROR timeout");
            else if (EditorApplication.timeSinceStartup < waitUntil) return;
            else
            {
                try { more = routine.MoveNext(); if (more && routine.Current is float s) waitUntil = EditorApplication.timeSinceStartup + s; }
                catch (Exception e) { Log("ERROR " + e); }
            }
            if (!more) { EditorApplication.update -= Tick; routine = null; EditorApplication.ExitPlaymode(); }
        }

        static string V(Vector3 v) => "(" + v.x.ToString("F0") + "," + v.z.ToString("F0") + ")";

        static IEnumerator Steps()
        {
            WarSandboxSceneSession session = null;
            double until = EditorApplication.timeSinceStartup + 120;
            while ((session = WarSandboxSceneSession.Instance) == null || session.IsLoading)
            { if (EditorApplication.timeSinceStartup > until) { Log("ERROR no session"); yield break; } yield return .25f; }
            if (!session.TryEnterBattlefield(MapId, true, out string error)) { Log("ERROR enter " + error); yield break; }
            yield return .5f;
            until = EditorApplication.timeSinceStartup + 180;
            while (session.IsLoading) { if (EditorApplication.timeSinceStartup > until) { Log("ERROR load timeout"); yield break; } yield return .25f; }
            yield return 1.5f;
            var c = session.Controller; var d = c != null ? c.GetComponent<WarSandboxRuntimeDeployment>() : null;
            if (d == null) { Log("ERROR no deployment"); yield break; }
            var kaiju = AssetDatabase.LoadAssetAtPath<UnitTypeConfig>(KaijuPath);
            Log("kaiju in roster: " + d.Templates.Contains(kaiju) + " choice: " + d.ChoiceTemplates.Contains(kaiju) + " cellSize " + c.manager.systemConfig.simulationConfig.cellSize);
            if (!d.TryBeginEdit(true, out error)) { Log("ERROR edit " + error); yield break; }
            var draft = d.Draft;
            var snap = draft.Snapshot();
            Log("original: " + string.Join(" ", snap.Select(e => "t" + e.teamId + V(e.center) + "x" + e.count + " " + e.template.unitTypeName)));
            var t0 = snap.First(e => e.teamId == 0); var t1 = snap.First(e => e.teamId == 1);
            for (int i = draft.Count - 1; i >= 0; i--) if (draft[i].teamId == 0) draft.Remove(i);
            if (!draft.Add(kaiju, 0, true)) { Log("ERROR add kaiju refused"); d.CancelEditing(); yield break; }
            int k = draft.Count - 1; var ke = draft[k]; ke.center = new Vector3(t0.center.x * .35f, t0.center.y, t0.center.z); draft.Set(k, ke);
            // Bring the defenders closer too so contact happens within the check window.
            for (int i = 0; i < draft.Count; i++) if (draft[i].teamId == 1) { var e = draft[i]; e.center = new Vector3(e.center.x * .35f, e.center.y, e.center.z); draft.Set(i, e); }
            var now = draft.Snapshot();
            Log("draft: " + string.Join(" ", now.Select(e => "t" + e.teamId + V(e.center) + "x" + e.count + " size " + e.Size.x.ToString("F0") + "x" + e.Size.z.ToString("F0") + " " + e.template.unitTypeName)));
            if (!d.TryValidate(out error)) { Log("validate FAIL " + error); d.CancelEditing(); yield break; }
            Log("validate OK");
            if (!d.TryApply(out error)) { Log("apply FAIL " + error); yield break; }
            Log("applied; agents " + c.manager.UnitTypes.TotalAgentCount);
            yield return 1f;
            Shot(c, "kaiju_0_deploy", 70f, 35f);
            c.StartDefaultBattle();
            float t = 0; int shot = 1;
            int kaijuHp0 = -1;
            while (t < 60f)
            {
                yield return 2.5f; t += 2.5f;
                var (agents, teams, hp) = Read(c.manager);
                int a0 = 0, a1 = 0, hp0 = 0; for (int i = 0; i < agents.Length; i++) { if (hp[i] <= 0) continue; if (teams[i] == 0) { a0++; hp0 += hp[i]; } else a1++; }
                if (kaijuHp0 < 0) kaijuHp0 = hp0;
                Log("t=" + t.ToString("F1") + " alive kaiju " + a0 + " infantry " + a1 + " kaijuHP " + hp0 + "/" + kaijuHp0 + " phase " + c.Phase);
                if (t == 10f || t == 20f || t == 35f) Shot(c, "kaiju_" + (shot++) + "_t" + t.ToString("F0"), 60f, 28f);
                if (t == 20f) Shot(c, "kaiju_closeup_t20", 30f, 12f);
                if (a0 == 0 || a1 == 0) { Shot(c, "kaiju_end", 60f, 28f); break; }
            }
            if (!session.TryReturnToMenu(true, out error)) { Log("ERROR menu " + error); yield break; }
            until = EditorApplication.timeSinceStartup + 60;
            while (session.State != WarSandboxEntryState.Menu && EditorApplication.timeSinceStartup < until) yield return .25f;
            yield return 1f;
            Log("ALL DONE");
        }

        static (AgentData[], int[], int[]) Read(MassEngineManager m)
        {
            int n = m.UnitTypes.TotalAgentCount; var agents = new AgentData[n]; var teams = new int[n]; var hp = new int[n];
            m.Buffers.agentBuffer.GetData(agents); m.Buffers.combatBuffers.teamIdBuffer.GetData(teams); m.Buffers.combatBuffers.hpReadBuffer.GetData(hp);
            return (agents, teams, hp);
        }

        /// <summary>Temporary camera aimed at the living kaiju centroid from the infantry side.</summary>
        static void Shot(WarSandboxBattleController c, string name, float distance, float height)
        {
            try
            {
                var (agents, teams, hp) = Read(c.manager);
                Vector3 k = Vector3.zero, e = Vector3.zero; int nk = 0, ne = 0;
                for (int i = 0; i < agents.Length; i++) { if (hp[i] <= 0) continue; if (teams[i] == 0) { k += agents[i].position; nk++; } else { e += agents[i].position; ne++; } }
                if (nk == 0) { Log("shot " + name + " skipped (no kaiju)"); return; }
                k /= nk; e = ne > 0 ? e / ne : k + Vector3.right * 100;
                Vector3 side = Vector3.Cross(Vector3.up, (e - k).normalized);
                Vector3 pos = k + side * distance + (e - k).normalized * distance * .35f + Vector3.up * height;
                var go = new GameObject("KaijuShotCam") { hideFlags = HideFlags.HideAndDontSave };
                var cam = go.AddComponent<Camera>();
                var main = Camera.main;
                if (main != null) cam.CopyFrom(main);
                cam.fieldOfView = 50; cam.nearClipPlane = .5f; cam.farClipPlane = 3000;
                go.transform.position = pos; go.transform.LookAt(k + Vector3.up * 6f);
                var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32);
                cam.targetTexture = rt; cam.Render();
                var prev = RenderTexture.active; RenderTexture.active = rt;
                var tex = new Texture2D(1600, 900, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); tex.Apply();
                RenderTexture.active = prev;
                File.WriteAllBytes(Path.Combine(Dir, name + ".jpg"), tex.EncodeToJPG(88));
                cam.targetTexture = null; UnityEngine.Object.DestroyImmediate(tex); rt.Release(); UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(go);
                Log("shot " + name + " at " + V(pos) + " kaiju " + V(k));
            }
            catch (Exception ex) { Log("shot " + name + " ERROR " + ex.Message); }
        }

        static void Log(string line) { File.AppendAllText(LogPath, DateTime.Now.ToString("HH:mm:ss") + " " + line + "\n"); Debug.Log("[KaijuSmoke] " + line); }
    }
}
