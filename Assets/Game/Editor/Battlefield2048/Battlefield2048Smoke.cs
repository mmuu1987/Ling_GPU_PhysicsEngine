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
    /// 程序化地图 2048 step 4 check (2026-10-14): enters Play from the menu scene, enters each 2048 map by id, logs the
    /// runtime scenario's formation centres (authored ±128 + outward offset → expected ±400), the edit draft, a freshly
    /// added formation, TryValidate, camera limits, then returns to the menu. Nothing is saved. Log: Logs/Battlefield2048/smoke.txt
    /// </summary>
    [InitializeOnLoad]
    public static class Battlefield2048Smoke
    {
        const string ActiveKey = "Battlefield2048Smoke.Active";
        const string LogPath = "Logs/Battlefield2048/smoke.txt";
        static IEnumerator routine; static double waitUntil, deadline;
        static readonly string[] Ids = { "ridge-pass-2048", "rolling-hills-2048", "canyon-mesa-2048", "cliff-plateau-2048", "plains-1024", "forest-hills-1024" };

        static Battlefield2048Smoke()
        {
            EditorApplication.playModeStateChanged += change =>
            {
                if (!SessionState.GetBool(ActiveKey, false)) return;
                if (change == PlayModeStateChange.EnteredPlayMode) Begin();
                else if (change == PlayModeStateChange.EnteredEditMode) { SessionState.SetBool(ActiveKey, false); Log("finished (left Play)"); }
            };
            if (SessionState.GetBool(ActiveKey, false) && EditorApplication.isPlaying) Begin();
        }

        [MenuItem(Battlefield2048Setup.MenuRoot + "接入检查（自动进 Play，自动退出）", false, 50)]
        static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath));
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
            foreach (var id in Ids)
            {
                if (!session.TryEnterBattlefield(id, true, out string error)) { Log(id + " ERROR enter " + error); continue; }
                yield return .5f;
                until = EditorApplication.timeSinceStartup + 180;
                while (session.IsLoading) { if (EditorApplication.timeSinceStartup > until) { Log(id + " ERROR load timeout"); yield break; } yield return .25f; }
                yield return 1.5f;
                if (session.State != WarSandboxEntryState.Battle) { Log(id + " state=" + session.State + " error=" + session.Error); if (!session.TryReturnToMenu(true, out _)) yield break; yield return 2f; continue; }
                var c = session.Controller; var d = c != null ? c.GetComponent<WarSandboxRuntimeDeployment>() : null;
                if (d == null) { Log(id + " ERROR no deployment"); continue; }
                var sb = new StringBuilder(id + " state=" + session.State + " error=" + session.Error + " statsWarning=" + session.StatsWarning + " help=" + session.HelpOpen + " phase=" + c.Phase + " offset=" + d.authoredSpawnOutwardOffset);
                var units = c.manager.scenarioConfig.unitTypes;
                sb.Append("\n  runtime scenario: " + string.Join(" ", units.Select(u => { var e = WarSandboxDeploymentEntry.From(u); return "t" + e.teamId + V(e.center) + "x" + e.count; })));
                sb.Append("\n  alive " + c.GetAliveUnitCount(0) + "/" + c.GetAliveUnitCount(1));
                if (session.HelpOpen) sb.Append("\n  (help open: edit skipped)");
                else if (d.TryBeginEdit(true, out error))
                {
                    var draft = d.Draft;
                    sb.Append("\n  draft: " + string.Join(" ", Enumerable.Range(0, draft.Count).Select(i => "t" + draft[i].teamId + V(draft[i].center) + " " + draft[i].Size.x.ToString("F0") + "x" + draft[i].Size.z.ToString("F0"))));
                    sb.Append("\n  validate: " + (d.TryValidate(out error) ? "OK" : "FAIL " + error));
                    var t = draft[0].template;
                    if (draft.Add(t, 1, d.UseTemplateSpawnDefaults)) sb.Append("\n  added: t1" + V(draft[draft.Count - 1].center));
                    else sb.Append("\n  add refused (roster limit?)");
                    d.CancelEditing();
                }
                else sb.Append("\n  edit ERROR " + error);
                var cam = Camera.main;
                var comfort = UnityEngine.Object.FindFirstObjectByType<BattlefieldCameraComfort24>();
                sb.Append("\n  camera " + (cam != null ? V(cam.transform.position) + " y=" + cam.transform.position.y.ToString("F0") + " far=" + cam.farClipPlane.ToString("F0") : "none") +
                    (comfort != null ? " horizontalLimit=" + comfort.horizontalLimit : ""));
                Log(sb.ToString());
                if (!session.TryReturnToMenu(true, out error)) { Log("ERROR menu " + error); yield break; }
                yield return .5f;
                until = EditorApplication.timeSinceStartup + 120;
                while (session.IsLoading) { if (EditorApplication.timeSinceStartup > until) yield break; yield return .25f; }
                yield return .5f;
            }
            // Let the menu screen draw before leaving Play, so the Game view does not keep the loading screen as its last frame.
            until = EditorApplication.timeSinceStartup + 30;
            while (session.State != WarSandboxEntryState.Menu && EditorApplication.timeSinceStartup < until) yield return .25f;
            yield return 2f;
            Log("ALL DONE");
        }

        static void Log(string line) { File.AppendAllText(LogPath, DateTime.Now.ToString("HH:mm:ss") + " " + line + "\n"); Debug.Log("[Battlefield2048Smoke] " + line); }
    }
}
