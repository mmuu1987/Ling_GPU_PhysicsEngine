using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MassEngine.Game.Editor
{
    /// <summary>Explicit navigation to the current game; never changes the user's scene on import.</summary>
    public static class GameEntryMenu
    {
        public const string MainMenuPath = "Assets/Game/Scenes/MainMenu.unity";
        public const string CatalogPath = "Assets/Game/Scenes/Catalog.asset";

        [MenuItem("Game/Start Here/Open Main Menu", false, 0)]
        public static void OpenMainMenu() => TryOpenMainMenu();

        [MenuItem("Game/Start Here/Play Main Menu", false, 1)]
        public static void PlayMainMenu()
        {
            if (TryOpenMainMenu()) EditorApplication.isPlaying = true;
        }

        [MenuItem("Game/Start Here/Open Main Menu", true)]
        [MenuItem("Game/Start Here/Play Main Menu", true)]
        private static bool CanOpen() => !EditorApplication.isPlayingOrWillChangePlaymode
            && !EditorApplication.isCompiling && !EditorApplication.isUpdating;

        private static bool TryOpenMainMenu()
        {
            if (!CanOpen()) return false;
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(MainMenuPath) == null)
            {
                Debug.LogError("Current game entry is missing: " + MainMenuPath);
                return false;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return false;
            EditorSceneManager.OpenScene(MainMenuPath, OpenSceneMode.Single);
            return true;
        }

        [MenuItem("Game/Start Here/Select Battlefield Catalog", false, 20)]
        public static void SelectCatalog()
        {
            var asset = AssetDatabase.LoadAssetAtPath<WarSandboxBattlefieldCatalog>(CatalogPath);
            Selection.activeObject = asset;
            if (asset != null) EditorGUIUtility.PingObject(asset);
        }

        [MenuItem("Game/Start Here/Select Project Guide", false, 21)]
        public static void SelectGuide()
        {
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Game/README.md");
            Selection.activeObject = asset;
            if (asset != null) EditorGUIUtility.PingObject(asset);
        }
    }
}
