using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace MassEngine.Game.Editor
{
    /// <summary>
    /// Lets the remote assistant run EditMode tests inside the open Editor (no second Unity / batch mode).
    /// Drop Logs/RemoteTests/run.txt (one group-name regex per line, optional "assembly=Name" lines, "menu=Path" runs a menu item,
    /// "mode=PlayMode" runs PlayMode tests instead of EditMode - the Editor enters and leaves Play mode by itself);
    /// the run starts when the Editor is idle (not playing / compiling) and writes progress.txt + result.txt.
    /// Only runs it starts itself are logged.
    /// </summary>
    [InitializeOnLoad]
    static class RemoteEditModeTests
    {
        const string Dir = "Logs/RemoteTests";
        static string Trigger => Path.Combine(Dir, "run.txt");
        static string Marker => Path.Combine(Dir, "running.txt");
        static string Progress => Path.Combine(Dir, "progress.txt");
        static string Result => Path.Combine(Dir, "result.txt");
        static double next;
        static TestRunnerApi api;

        static RemoteEditModeTests()
        {
            api = ScriptableObject.CreateInstance<TestRunnerApi>();
            api.hideFlags = HideFlags.HideAndDontSave;
            api.RegisterCallbacks(new Callbacks());
            EditorApplication.update += Poll;
        }

        static void Poll()
        {
            if (EditorApplication.timeSinceStartup < next) return;
            next = EditorApplication.timeSinceStartup + 2;
            if (!File.Exists(Trigger) || File.Exists(Marker)) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            var groups = new System.Collections.Generic.List<string>();
            var assemblies = new System.Collections.Generic.List<string>();
            var menus = new System.Collections.Generic.List<string>();
            var mode = TestMode.EditMode;
            foreach (var raw in File.ReadAllLines(Trigger))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                if (line.StartsWith("menu=")) menus.Add(line.Substring(5));
                else if (line.StartsWith("mode=")) mode = line.Substring(5).Trim().Equals("PlayMode", StringComparison.OrdinalIgnoreCase) ? TestMode.PlayMode : TestMode.EditMode;
                else if (line.StartsWith("assembly=")) assemblies.Add(line.Substring(9)); else groups.Add(line);
            }
            File.Delete(Trigger);
            if (menus.Count > 0)
            {
                var report = new StringBuilder();
                foreach (var menu in menus)
                {
                    try { report.AppendLine((EditorApplication.ExecuteMenuItem(menu) ? "menu ok " : "menu NOT FOUND ") + menu); }
                    catch (Exception e) { report.AppendLine("menu FAILED " + menu + "\n" + e); }
                }
                File.WriteAllText(Path.Combine(Dir, "menu.txt"), DateTime.Now.ToString("s") + "\n" + report);
                if (groups.Count == 0 && assemblies.Count == 0) return;
            }
            File.WriteAllText(Marker, DateTime.Now.ToString("s"));
            File.WriteAllText(Progress, "queued " + DateTime.Now.ToString("s") + " mode=" + mode + " groups=" + string.Join(" | ", groups) + "\n");
            if (File.Exists(Result)) File.Delete(Result);
            var filter = new Filter { testMode = mode };
            if (groups.Count > 0) filter.groupNames = groups.ToArray();
            if (assemblies.Count > 0) filter.assemblyNames = assemblies.ToArray();
            try { api.Execute(new ExecutionSettings(filter)); }
            catch (Exception e) { File.WriteAllText(Result, "EXECUTE FAILED " + e); File.Delete(Marker); }
        }

        sealed class Callbacks : ICallbacks
        {
            static bool Active => File.Exists(Marker);
            public void RunStarted(ITestAdaptor tests)
            {
                if (Active) File.AppendAllText(Progress, "started " + DateTime.Now.ToString("s") + " cases=" + tests.TestCaseCount + "\n");
            }
            public void TestStarted(ITestAdaptor test) { }
            public void TestFinished(ITestResultAdaptor result)
            {
                if (!Active || result.HasChildren) return;
                File.AppendAllText(Progress, result.TestStatus + " " + result.Test.FullName + " " + result.Duration.ToString("F2") + "s\n");
            }
            public void RunFinished(ITestResultAdaptor result)
            {
                if (!Active) return;
                var s = new StringBuilder();
                s.AppendLine("finished " + DateTime.Now.ToString("s") + " pass=" + result.PassCount + " fail=" + result.FailCount +
                             " skip=" + result.SkipCount + " inconclusive=" + result.InconclusiveCount + " duration=" + result.Duration.ToString("F1") + "s");
                Collect(result, s);
                File.WriteAllText(Result, s.ToString());
                File.Delete(Marker);
            }
            static void Collect(ITestResultAdaptor r, StringBuilder s)
            {
                if (r.HasChildren) { foreach (var c in r.Children) Collect(c, s); return; }
                if (r.TestStatus == TestStatus.Passed) return;
                s.AppendLine(r.TestStatus + " " + r.Test.FullName);
                if (!string.IsNullOrEmpty(r.Message)) s.AppendLine("  " + r.Message.Trim().Replace("\n", "\n  "));
                if (!string.IsNullOrEmpty(r.StackTrace))
                {
                    var lines = r.StackTrace.Split('\n');
                    for (int i = 0; i < Math.Min(8, lines.Length); i++) s.AppendLine("    " + lines[i].Trim());
                }
            }
        }
    }
}
