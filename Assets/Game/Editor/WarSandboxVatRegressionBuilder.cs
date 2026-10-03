using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MassEngine.Game.Editor
{
    /// <summary>Builds an isolated M5.3 A/B player without rewriting shipped scenes, configs or build settings.</summary>
    public static class WarSandboxVatRegressionBuilder
    {
        public static void BuildWindows()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode before building.");
            string profilePath = Argument("--vat-performance-profile=");
            var rebaked = AssetDatabase.LoadAssetAtPath<VATProfile>(profilePath ?? "");
            var reference = AssetDatabase.LoadAssetAtPath<VATProfile>("Assets/VAT_Data/MaleCharacter_Stage5_MultiClip_Profile.asset");
            if (rebaked == null || reference == null || rebaked == reference)
                throw new BuildFailedException("Specify the distinct persisted Male rebake with --vat-performance-profile=Assets/...asset.");
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var setup = EditorSceneManager.GetSceneManagerSetup();
            string directory = "Assets/Game/M53Build_" + Guid.NewGuid().ToString("N");
            string scenePath = directory + "/VatPerformanceMenu.unity";
            string buildDirectory = Path.GetFullPath(Argument("--vat-performance-build=") ?? "Builds/M53Regression");
            try
            {
                AssetDatabase.CreateFolder("Assets/Game", Path.GetFileName(directory));
                if (!AssetDatabase.CopyAsset(WarSandboxEntryBuilder.MenuScenePath, scenePath))
                    throw new BuildFailedException("Cannot copy the menu into the temporary build folder.");
                var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                var session = UnityEngine.Object.FindFirstObjectByType<WarSandboxSceneSession>();
                if (session == null) throw new BuildFailedException("Temporary menu has no session.");
                session.enterDefaultOnStart = false;
                var probe = new GameObject("M5.3 VAT Performance").AddComponent<WarSandboxVatPerformance>();
                probe.referenceMale = reference;
                probe.rebakedMale = rebaked;
                EditorSceneManager.SaveScene(scene);
                Directory.CreateDirectory(buildDirectory);
                BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { scenePath, "Assets/Game/Scenes/WarSandbox.unity" },
                    locationPathName = Path.Combine(buildDirectory, "VatPerformance.exe"),
                    target = BuildTarget.StandaloneWindows64,
                    options = BuildOptions.Development
                });
                if (report.summary.result != BuildResult.Succeeded)
                    throw new BuildFailedException("M5.3 validation build failed: " + report.summary.result);
                File.WriteAllText(Path.Combine(buildDirectory, "vat-profile-path.txt"), profilePath + "\n");
                Debug.Log("M5.3 validation player: " + buildDirectory);
            }
            finally
            {
                // Unload our temporary scene before deleting it; restore the user's saved scene setup.
                if (setup.Length > 0) EditorSceneManager.RestoreSceneManagerSetup(setup);
                else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                AssetDatabase.DeleteAsset(directory);
            }
        }

        private static string Argument(string prefix)
        {
            foreach (string arg in Environment.GetCommandLineArgs())
                if (arg.StartsWith(prefix, StringComparison.Ordinal)) return arg.Substring(prefix.Length).Trim('"');
            return null;
        }
    }
}
