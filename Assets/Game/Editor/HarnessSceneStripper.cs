using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MassEngine.Game.Editor
{
    /// <summary>
    /// Smoke/probe harness code lives in Assets/Game/Scripts/Harness (assembly Game.Harness, defineConstraints
    /// "UNITY_EDITOR || MASS_HARNESS"): always compiled in the Editor, compiled into a player only when the
    /// MASS_HARNESS scripting define is set for the build target. Some scenes (e.g. MainMenu) still carry harness
    /// components; in a production player they would load as "missing script" behaviours, so they are removed from
    /// the built copy of the scene. The source scenes are never modified. Audit 2026-10-12.
    /// </summary>
    public sealed class HarnessSceneStripper : IProcessSceneWithReport
    {
        public const string Define = "MASS_HARNESS";
        public const string AssemblyName = "Game.Harness";
        public int callbackOrder => -1000;

        public void OnProcessScene(Scene scene, BuildReport report)
        {
            if (report == null) return; // Entering Play Mode in the Editor: the harness is compiled, keep it.
            if (HarnessIncluded(report.summary.platform)) return;
            var stripped = new List<string>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (MonoBehaviour behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (behaviour == null || behaviour.GetType().Assembly.GetName().Name != AssemblyName) continue;
                    stripped.Add(behaviour.gameObject.name + "/" + behaviour.GetType().Name);
                    Object.DestroyImmediate(behaviour);
                }
            }
            if (stripped.Count > 0)
                Debug.Log("[Harness] " + scene.path + ": stripped " + stripped.Count + " harness component(s) from the player copy ("
                    + string.Join(", ", stripped) + "). Add the " + Define + " scripting define to keep smoke/probe code in a player.");
        }

        public static bool HarnessIncluded(BuildTarget target)
        {
            BuildTargetGroup group = BuildPipeline.GetBuildTargetGroup(target);
            string defines = PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.FromBuildTargetGroup(group));
            return defines.Split(';').Any(d => d.Trim() == Define);
        }
    }
}
