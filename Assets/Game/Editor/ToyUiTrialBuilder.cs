using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
namespace MassEngine.Game.Editor
{
    public static class ToyUiTrialBuilder
    {
        public const string Output = "Builds/ToyUI-20261004-11";
        public static void Build()
        {
            if (Directory.Exists(Output)) throw new InvalidOperationException("Never overwrite a build: " + Output);
            var catalog = AssetDatabase.LoadAssetAtPath<WarSandboxBattlefieldCatalog>("Assets/Game/Content/Characters/OfficialRoster/Version08/Catalog.asset");
            if (catalog == null || !catalog.TryValidate(File.Exists, out var error)) throw new InvalidOperationException("Invalid current catalog");
            if (!catalog.TryValidateTemplates(out error)) throw new InvalidOperationException(error);
            var scenes = new[] { "Assets/Game/Content/Characters/OfficialRoster/Version08/LaunchMenu.unity" }.Concat(catalog.entries.Select(x => x.scenePath)).Distinct().ToArray();
            Directory.CreateDirectory(Output);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = scenes, target = BuildTarget.StandaloneWindows64,
                locationPathName = Output + "/WarSandbox.exe", options = BuildOptions.Development });
            File.WriteAllText("Logs/ToyUIBuild-20261004-11/build-report.json", JsonUtility.ToJson(new Receipt {
                passed = report.summary.result == BuildResult.Succeeded, buildGuid = report.summary.guid.ToString(),
                result = report.summary.result.ToString(), bytes = report.summary.totalSize.ToString(), sceneCount = scenes.Length,
                warnings = (int)report.summary.totalWarnings, errors = (int)report.summary.totalErrors }, true));
            if (report.summary.result != BuildResult.Succeeded) throw new InvalidOperationException("Build failed: " + report.summary.result);
            File.WriteAllText(Output + "/Start-ToyUI.cmd", "@echo off\r\ncd /d \"%~dp0\"\r\nstart \"\" \"WarSandbox.exe\" -screen-fullscreen 1 -window-mode exclusive -screen-width 1920 -screen-height 1080\r\n");
            Debug.Log("TOY_UI_BUILD_READY " + Output);
        }
        [Serializable] private class Receipt { public bool passed; public string buildGuid, result, bytes; public int sceneCount, warnings, errors; }
    }
}
