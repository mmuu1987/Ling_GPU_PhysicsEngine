// Stress200KRunner.cs  –  Sets up 200k draft in Edit mode, then enters Play mode.
// The Stress200KPerfProbe will apply the draft and start the battle in Play mode.

using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace MassEngine.Game.Editor
{
    public static class Stress200KRunner
    {
        [MenuItem("MassEngine/Stress Test/Run 200K Probe")]
        public static void RunProbe()
        {
            Debug.Log("Stress200KRunner: Opening battle scene...");
            EditorSceneManager.OpenScene(Stress200KBuilder.Battle, OpenSceneMode.Single);

            var c = Object.FindFirstObjectByType<WarSandboxBattleController>();
            var d = Object.FindFirstObjectByType<WarSandboxRuntimeDeployment>();
            if (c == null || d == null)
            {
                Debug.LogError("Stress200KRunner: Missing controller or deployment");
                Application.Quit(1);
                return;
            }

            // Enter editing mode to initialize Draft
            Debug.Log("Stress200KRunner: Entering edit mode...");
            if (!d.TryBeginEdit(true, out var editErr))
            {
                Debug.LogError("Stress200KRunner: TryBeginEdit failed: " + editErr);
                Application.Quit(1);
                return;
            }

            // Set 100k per side with tight density
            Debug.Log("Stress200KRunner: Setting 200k deployment...");
            for (int i = 0; i < 2; i++)
            {
                var entry = d.Draft[i];
                entry.count = 100000;
                entry.density = 0.7f;
                entry.aspect = 2.2f;
                d.Draft.Set(i, entry);
            }

            if (!d.TryValidate(out var valErr))
            {
                Debug.LogError("Stress200KRunner: Validation failed: " + valErr);
                Application.Quit(1);
                return;
            }

            // Don't start the battle here — TryApply needs Play mode for GPU buffers.
            // The probe will handle Apply + Start in Play mode.
            Debug.Log("Stress200KRunner: Draft set. Entering play mode for deployment...");
            EditorApplication.EnterPlaymode();
        }
    }
}