using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MassEngine.Game.Editor
{
    public static class WarSandboxEntryBuilder
    {
        public const string MenuScenePath = "Assets/Game/Scenes/WarSandboxMenu.unity";
        public const string CatalogPath = "Assets/Game/Settings/BattlefieldCatalog.asset";

        [MenuItem("MassEngine/Create Battlefield Entry")]
        public static void CreateEntry()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var catalog = AssetDatabase.LoadAssetAtPath<WarSandboxBattlefieldCatalog>(CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<WarSandboxBattlefieldCatalog>();
                catalog.defaultEntryId = "open-battle";
                catalog.entries = new[]
                {
                    Entry("open-battle", "开阔会战", "BattlefieldRules_A_Annihilation"),
                    Entry("walled-point", "双墙据点", "BattlefieldRules_B_ControlPoint")
                };
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(MenuScenePath) == null)
            {
                Scene menu = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var root = new GameObject("War Sandbox Entry");
                var session = root.AddComponent<WarSandboxSceneSession>();
                session.catalog = catalog;
                root.AddComponent<WarSandboxFrontEnd>();
                var camera = new GameObject("Menu Camera").AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.09f, 0.105f, 0.11f);
                camera.cullingMask = 0;
                camera.gameObject.AddComponent<AudioListener>();
                EditorSceneManager.SaveScene(menu, MenuScenePath);
            }
            AddBuildScenes(catalog);
            AssetDatabase.SaveAssets();
            Debug.Log("Battlefield entry ready: " + MenuScenePath);
        }

        private static WarSandboxBattlefieldEntry Entry(string id, string name, string ruleAsset)
        {
            return new WarSandboxBattlefieldEntry
            {
                id = id, displayName = name,
                scenePath = "Assets/Game/Scenes/WarSandbox.unity",
                rules = AssetDatabase.LoadAssetAtPath<WarSandboxBattlefieldConfig>("Assets/Game/Settings/" + ruleAsset + ".asset")
            };
        }

        public static void AddBuildScenes(WarSandboxBattlefieldCatalog catalog)
        {
            var paths = new List<string> { MenuScenePath };
            if (catalog.entries != null) foreach (var entry in catalog.entries)
                if (entry != null && WarSandboxBattlefieldEntry.IsScenePath(entry.scenePath) &&
                    AssetDatabase.LoadAssetAtPath<SceneAsset>(entry.scenePath) != null && !paths.Contains(entry.scenePath))
                    paths.Add(entry.scenePath);
            var scenes = paths.Select(path => new EditorBuildSettingsScene(path, true)).ToList();
            scenes.AddRange(EditorBuildSettings.scenes.Where(scene => !paths.Contains(scene.path)));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        public static bool IsIncludedScene(string path) => EditorBuildSettings.scenes.Any(scene => scene.enabled && scene.path == path) &&
            AssetDatabase.LoadAssetAtPath<SceneAsset>(path) != null;

        [MenuItem("MassEngine/Build Windows Sandbox")]
        public static void BuildWindowsMenu() => BuildWindows();

        public static void ImportPreviewsAndBuild()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<WarSandboxBattlefieldCatalog>(CatalogPath);
            AssetDatabase.Refresh();
            foreach (var entry in catalog.entries)
            {
                string path = "Assets/Game/Previews/" + entry.id + ".png";
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) throw new BuildFailedException("Missing battlefield preview: " + path);
                importer.mipmapEnabled = false;
                importer.maxTextureSize = 1024;
                importer.SaveAndReimport();
                entry.preview = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            }
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            BuildWindows();
        }

        public static void BuildWindows()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<WarSandboxBattlefieldCatalog>(CatalogPath);
            if (catalog == null || !catalog.TryValidate(IsIncludedScene, out _)) throw new BuildFailedException("Battlefield catalog/build scenes are invalid.");
            if (!catalog.TryValidateTemplates(out string templateError) || catalog.templates.Length == 0)
                throw new BuildFailedException(templateError ?? "The player unit template catalog is empty.");
            string directory = Path.GetFullPath("Builds/WarSandbox");
            Directory.CreateDirectory(directory);
            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray(),
                locationPathName = Path.Combine(directory, "WarSandbox.exe"),
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new BuildFailedException("Windows build failed: " + report.summary.result);
            Debug.Log("Windows sandbox build: " + directory);
        }
    }

    public sealed class WarSandboxCatalogBuildValidation : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;
        public void OnPreprocessBuild(BuildReport report)
        {
            string[] scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray();
            foreach (string path in AssetDatabase.GetDependencies(scenes, true))
            {
                var catalog = AssetDatabase.LoadAssetAtPath<WarSandboxBattlefieldCatalog>(path);
                if (catalog != null && !catalog.TryValidate(WarSandboxEntryBuilder.IsIncludedScene, out string error))
                    throw new BuildFailedException(path + ": " + error);
                if (catalog != null && (!catalog.TryValidateTemplates(out string templateError) || catalog.templates.Length == 0))
                    throw new BuildFailedException(path + ": " + (templateError ?? "The player unit template catalog is empty."));
            }
        }
    }
}
