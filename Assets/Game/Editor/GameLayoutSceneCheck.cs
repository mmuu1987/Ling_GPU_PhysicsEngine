using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MassEngine.Game.Editor
{
    // One-shot read-only scene verification, only when the explicit local marker exists.
    [InitializeOnLoad]
    public static class GameLayoutSceneCheck
    {
        private const string DirectoryPath = "Logs/GameLayout/Review20261007/";
        [Serializable] private sealed class Receipt
        {
            public bool passed;
            public string error;
            public string[] scenes;
            public int checkedGameObjects;
            public int missingScripts;
            public bool menuHasSessionAndFrontEnd;
            public bool currentCatalogValid;
        }
        static GameLayoutSceneCheck() { EditorApplication.update += Tick; }
        private static void Tick()
        {
            string marker = DirectoryPath + "validate-scenes.request";
            if (!File.Exists(marker) || EditorApplication.isCompiling || EditorApplication.isUpdating
                || EditorApplication.isPlayingOrWillChangePlaymode) return;
            File.Delete(marker);
            var result = new Receipt();
            try
            {
                var plan = JsonUtility.FromJson<GameLayoutMigration.Plan>(File.ReadAllText("Logs/GameLayout/plan.json"));
                result.scenes = plan.scenes;
                Require(EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).SequenceEqual(plan.scenes), "Build scene list mismatch");
                var catalog = AssetDatabase.LoadAssetAtPath<WarSandboxBattlefieldCatalog>(GameEntryMenu.CatalogPath);
                Require(catalog != null, "Current catalog is missing");
                Require(catalog.TryValidate(p => plan.scenes.Contains(p), out var error), error);
                result.currentCatalogValid = true;
                for (int i = 0; i < plan.scenes.Length; i++)
                {
                    string path = plan.scenes[i];
                    Require(AssetDatabase.AssetPathToGUID(path) == plan.sceneGuids[i], "GUID changed: " + path);
                    var scene = EditorSceneManager.OpenPreviewScene(path);
                    try
                    {
                        var objects = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).Select(t => t.gameObject).ToArray();
                        result.checkedGameObjects += objects.Length;
                        result.missingScripts += objects.Sum(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount);
                        if (i == 0)
                        {
                            var session = objects.Select(g => g.GetComponent<WarSandboxSceneSession>()).FirstOrDefault(s => s != null);
                            var front = objects.Select(g => g.GetComponent<WarSandboxFrontEnd>()).FirstOrDefault(s => s != null);
                            Require(session != null && front != null && session.catalog == catalog, "Main menu session/front-end/catalog mismatch");
                            result.menuHasSessionAndFrontEnd = true;
                        }
                        else
                        {
                            var deployment = objects.Select(g => g.GetComponent<WarSandboxRuntimeDeployment>()).FirstOrDefault(s => s != null);
                            Require(deployment != null && deployment.battlefieldCatalog == catalog, "Battlefield deployment/catalog mismatch: " + path);
                        }
                    }
                    finally { EditorSceneManager.ClosePreviewScene(scene); }
                }
                Require(result.missingScripts == 0, "Missing MonoBehaviour scripts found");
                result.passed = true;
            }
            catch (Exception exception) { result.error = exception.ToString(); Debug.LogException(exception); }
            File.WriteAllText(DirectoryPath + "scene-check.json", JsonUtility.ToJson(result, true));
        }
        private static void Require(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message);
        }
    }
}
