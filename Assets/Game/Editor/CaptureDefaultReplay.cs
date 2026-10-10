using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MassEngine.Game.Editor
{
    [InitializeOnLoad]
    public static class CaptureDefaultReplay
    {
        private const string SessionKey = "CaptureDefaultReplay.Output";
        private const string Root = "Assets/Game/Scenes";
        private static readonly string[] Scenes = { "MainMenu", "Green", "Autumn", "Winter" };

        static CaptureDefaultReplay() { EditorApplication.update += Watch; }

        public static void Begin()
        {
            if (!Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Only an owned batch Editor may run this replay.");
            string argument = Environment.GetCommandLineArgs().Single(a => a.StartsWith("--terrain-output=", StringComparison.Ordinal));
            string output = argument.Substring("--terrain-output=".Length);
            if (Directory.Exists(output)) throw new IOException("Refuse to overwrite replay evidence.");
            SessionState.SetString(SessionKey, output);
            // The runner snapshots/restores EditorBuildSettings; no scene asset is saved.
            EditorBuildSettings.scenes = Scenes.Select(n => new EditorBuildSettingsScene(Root + "/" + n + ".unity", true)).ToArray();
            EditorSceneManager.OpenScene(Root + "/MainMenu.unity", OpenSceneMode.Single);
            EditorApplication.EnterPlaymode();
        }

        private static void Watch()
        {
            string output = SessionState.GetString(SessionKey, "");
            if (string.IsNullOrEmpty(output)) return;
            string path = Path.Combine(output, "receipt.json");
            if (!File.Exists(path)) return;
            var receipt = JsonUtility.FromJson<CaptureDefaultProbe.Receipt>(File.ReadAllText(path));
            SessionState.EraseString(SessionKey);
            EditorApplication.Exit(receipt.passed ? 0 : 1);
        }

        [Serializable]
        private sealed class BuildReceipt
        {
            public bool passed;
            public string buildGuid;
            public int errors, warnings;
        }

        public static void Build()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Batch build only.");
            const string directory = "Builds/CaptureDefault-20261006-37";
            if (Directory.Exists(directory)) throw new IOException("Refuse to overwrite any existing build.");
            Directory.CreateDirectory(directory);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes = Scenes.Select(n => Root + "/" + n + ".unity").ToArray(),
                locationPathName = directory + "/WarSandbox.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development
            });
            var receipt = new BuildReceipt {
                passed = report.summary.result == BuildResult.Succeeded,
                buildGuid = report.summary.guid.ToString(),
                errors = (int)report.summary.totalErrors,
                warnings = (int)report.summary.totalWarnings
            };
            File.WriteAllText("Logs/CaptureDefault-20261006/build/build.json", JsonUtility.ToJson(receipt, true));
            if (!receipt.passed) throw new InvalidOperationException("Capture default build failed.");
            File.WriteAllText(directory + "/Start-Game.cmd", "@echo off\r\ncd /d \"%~dp0\"\r\nstart \"\" \"WarSandbox.exe\" -screen-fullscreen 1 -window-mode exclusive -screen-width 1920 -screen-height 1080\r\n");
        }
    }
}
