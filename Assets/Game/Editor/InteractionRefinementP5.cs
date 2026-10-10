#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MassEngine.Game.Editor
{
    // Explicit opt-in test isolation only. Does not run in an ordinary Editor session.
    [InitializeOnLoad]
    public static class InteractionRefinementP5
    {
        static InteractionRefinementP5()
        {
            if (!Application.isBatchMode) return;
            const string prefix = "--interaction-p5-output=";
            var argument = Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith(prefix, StringComparison.Ordinal));
            if (argument == null) return;
            try
            {
                string output = argument.Substring(prefix.Length);
                string allowed = Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/InteractionRefinement-20261007/P5")) + Path.DirectorySeparatorChar;
                if (!Path.IsPathFullyQualified(output) || !Path.GetFullPath(output).StartsWith(allowed, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(output))
                    throw new InvalidOperationException("P5 tests require an existing isolated log directory.");
                WarSandboxUnitStatStore.DefaultPathOverride = Path.Combine(output, "globals.json");
                Debug.Log("INTERACTION_P5_ISOLATED");
            }
            catch (Exception error)
            {
                Debug.LogError("P5 isolation failed: " + error.Message);
                EditorApplication.delayCall += () => EditorApplication.Exit(1);
            }
        }
    }
}
#endif




