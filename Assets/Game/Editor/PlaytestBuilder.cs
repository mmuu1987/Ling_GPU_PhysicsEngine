using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace MassEngine.Game.Editor
{
    /// <summary>
    /// Reusable playtest player build. Scenes = MainMenu + every scene referenced by the battlefield catalog,
    /// so a selectable catalog entry can never point at a scene missing from the player.
    /// Unity -batchmode -quit -executeMethod MassEngine.Game.Editor.PlaytestBuilder.Build
    ///   --playtest-output=Builds/Name --playtest-receipt=path/build.json
    /// Refuses to overwrite an existing build. Does not modify EditorBuildSettings or any asset.
    /// </summary>
    public static class PlaytestBuilder
    {
        private const string MainMenu = "Assets/Game/Scenes/MainMenu.unity";
        private const string CatalogPath = "Assets/Game/Scenes/Catalog.asset";

        [Serializable]
        private sealed class BuildReceipt
        {
            public bool passed;
            public string buildGuid;
            public int errors, warnings;
            public string output;
            public string[] scenes;
            public string[] catalogEntries;
            public string[] validationOnlyScenes;
            public ulong totalSize;
            public double seconds;
        }

        private static string Arg(string name)
        {
            string prefix = "--" + name + "=";
            string value = Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith(prefix, StringComparison.Ordinal));
            return value?.Substring(prefix.Length);
        }

        public static void Build()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Batch build only.");
            string output = Arg("playtest-output");
            string receiptPath = Arg("playtest-receipt");
            if (string.IsNullOrEmpty(output) || string.IsNullOrEmpty(receiptPath))
                throw new ArgumentException("--playtest-output and --playtest-receipt are required.");
            output = output.Replace('\\', '/').TrimEnd('/');
            if (!output.StartsWith("Builds/", StringComparison.Ordinal) || output.Count(c => c == '/') != 1)
                throw new ArgumentException("Output must be a single folder under Builds/: " + output);
            if (Directory.Exists(output)) throw new IOException("Refuse to overwrite any existing build: " + output);

            var catalog = AssetDatabase.LoadAssetAtPath<WarSandboxBattlefieldCatalog>(CatalogPath);
            if (catalog == null) throw new FileNotFoundException("Battlefield catalog missing: " + CatalogPath);
            var scenes = new List<string> { MainMenu };
            foreach (var entry in catalog.entries)
            {
                if (string.IsNullOrEmpty(entry.scenePath) || !File.Exists(entry.scenePath))
                    throw new FileNotFoundException("Catalog entry " + entry.id + " scene missing: " + entry.scenePath);
                if (!scenes.Contains(entry.scenePath)) scenes.Add(entry.scenePath);
            }

            // WarSandboxCatalogBuildValidation checks every catalog reachable from the EditorBuildSettings scenes against
            // EditorBuildSettings. MainMenu's opt-in model-trial smoke (command-line only) references the legacy Version08
            // catalog. Register those legacy scenes for validation only (the state build 37 was validated under);
            // the player itself contains only `scenes`. EditorBuildSettings is restored afterwards.
            var originalSettings = EditorBuildSettings.scenes;
            var validation = new List<string>(scenes);
            for (int added = 1; added > 0;)
            {
                added = 0;
                foreach (string dep in AssetDatabase.GetDependencies(validation.ToArray(), true))
                {
                    var other = AssetDatabase.LoadAssetAtPath<WarSandboxBattlefieldCatalog>(dep);
                    if (other == null || other.entries == null) continue;
                    foreach (var e in other.entries)
                        if (!string.IsNullOrEmpty(e.scenePath) && File.Exists(e.scenePath) && !validation.Contains(e.scenePath)) { validation.Add(e.scenePath); added++; }
                }
            }
            BuildReport report;
            Directory.CreateDirectory(output);
            try
            {
                EditorBuildSettings.scenes = validation.Select(p => new EditorBuildSettingsScene(p, true)).ToArray();
                report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = scenes.ToArray(),
                    locationPathName = output + "/WarSandbox.exe",
                    target = BuildTarget.StandaloneWindows64,
                    options = BuildOptions.Development
                });
            }
            finally
            {
                EditorBuildSettings.scenes = originalSettings;
            }
            var receipt = new BuildReceipt
            {
                passed = report.summary.result == BuildResult.Succeeded,
                buildGuid = report.summary.guid.ToString(),
                errors = (int)report.summary.totalErrors,
                warnings = (int)report.summary.totalWarnings,
                output = output,
                scenes = scenes.ToArray(),
                catalogEntries = catalog.entries.Select(e => e.id).ToArray(),
                validationOnlyScenes = validation.Skip(scenes.Count).ToArray(),
                totalSize = report.summary.totalSize,
                seconds = report.summary.totalTime.TotalSeconds
            };
            string receiptDir = Path.GetDirectoryName(receiptPath);
            if (!string.IsNullOrEmpty(receiptDir)) Directory.CreateDirectory(receiptDir);
            File.WriteAllText(receiptPath, JsonUtility.ToJson(receipt, true));
            if (!receipt.passed) throw new InvalidOperationException("Playtest build failed: " + report.summary.result);
            File.WriteAllText(output + "/Start-Game.cmd",
                "@echo off\r\ncd /d \"%~dp0\"\r\nstart \"\" \"WarSandbox.exe\" -screen-fullscreen 1 -window-mode exclusive -screen-width 1920 -screen-height 1080\r\n");
        }
    }
}
