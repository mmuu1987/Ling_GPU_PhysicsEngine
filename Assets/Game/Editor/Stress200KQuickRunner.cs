// Stress200KQuickRunner.cs  –  Runs the existing TightFormation17 smoke test at 100k.
// Uses the proven UI navigation from TightFormationPlayerSmoke.

using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace MassEngine.Game.Editor
{
    public static class Stress200KQuickRunner
    {
        [MenuItem("MassEngine/Stress Test/Run 100K Quick")]
        public static void Run()
        {
            Debug.Log("QuickRunner: Opening TightFormation17 LaunchMenu...");
            EditorSceneManager.OpenScene("Assets/Game/Content/Battlefields/FormationSetup/LaunchMenu.unity", OpenSceneMode.Single);
            Debug.Log("QuickRunner: Entering play mode (smoke test will auto-activate)...");
            EditorApplication.EnterPlaymode();
        }
    }
}