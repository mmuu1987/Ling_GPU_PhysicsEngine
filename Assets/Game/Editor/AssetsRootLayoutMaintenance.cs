using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MassEngine.Game.Editor
{
    // Explicit batch maintenance only. No InitializeOnLoad hook or automatic resource moves.
    public static class AssetsRootLayoutMaintenance
    {
        const string Log = "Logs/AssetsLayout-20261007/";
        [Serializable] public class Move { public string source, destination, guid; }
        [Serializable] public class Patch { public string source, destination, before, after, backup, prepared; }
        [Serializable] public class Plan { public Move[] moves; public Patch[] patches; public string[] scenePaths, sceneGuids; }
        [Serializable] public class Identities { public Move[] assets; }
        [Serializable] public class ScriptRecord { public string guid, assembly; }
        [Serializable] public class ResourceRecord { public string guid, key; }
        [Serializable] public class DependencyRecord { public string guid; public string[] dependencies; }
        [Serializable] public class Baseline
        {
            public ScriptRecord[] scripts;
            public ResourceRecord[] resources;
            public DependencyRecord[] dependencies;
        }
        [Serializable] public class Receipt
        {
            public bool passed;
            public string stage, error;
            public int moved, alreadyMoved, patched, alreadyPatched, assetIdentities, scripts, resources, dependencyRoots, sceneObjects, missingScripts;
            public string[] scenes;
        }
        static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        static Plan ReadPlan() => JsonUtility.FromJson<Plan>(File.ReadAllText(Log + "plan.json"));
        static Identities ReadIdentities() => JsonUtility.FromJson<Identities>(File.ReadAllText(Log + "identities.before.json"));
        static void Write(string file, object value) => File.WriteAllText(Log + file, JsonUtility.ToJson(value, true));
        static string Hash(string path)
        {
            using (var hash = SHA256.Create())
            using (var file = File.OpenRead(path))
                return BitConverter.ToString(hash.ComputeHash(file)).Replace("-", "").ToLowerInvariant();
        }
        static void RequireBatch()
        {
            Require(Application.isBatchMode, "Run only in an owned batch editor; never close an interactive editor.");
            Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Play mode is not permitted.");
        }
        static string ResourceKey(string path)
        {
            const string token = "/Resources/";
            int at = path.LastIndexOf(token, StringComparison.Ordinal);
            return at < 0 ? null : Path.ChangeExtension(path.Substring(at + token.Length), null).Replace('\\', '/');
        }
        static string[] Dependencies(string path) => AssetDatabase.GetDependencies(path, true)
            .Select(p => { var g = AssetDatabase.AssetPathToGUID(p); return string.IsNullOrEmpty(g) ? "PATH:" + p : g; })
            .Distinct().OrderBy(p => p, StringComparer.Ordinal).ToArray();
        static void CaptureBaseline(Plan plan)
        {
            if (File.Exists(Log + "unity-before.json")) return;
            var ids = ReadIdentities().assets;
            foreach (var item in ids) Require(AssetDatabase.AssetPathToGUID(item.source) == item.guid, "Original GUID mismatch: " + item.source);
            var scripts = ids.Where(a => a.source.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                .Select(a => new ScriptRecord { guid = a.guid, assembly = CompilationPipeline.GetAssemblyNameFromScriptPath(a.source) }).ToArray();
            var resources = ids.Where(a => File.Exists(a.source) && ResourceKey(a.source) != null)
                .Select(a => new ResourceRecord { guid = a.guid, key = ResourceKey(a.source) }).ToArray();
            var roots = ids.Where(a => plan.scenePaths.Contains(a.source) || a.source.StartsWith("Assets/Settings/", StringComparison.Ordinal)
                || a.source.EndsWith(".inputactions", StringComparison.OrdinalIgnoreCase) || a.source == "Assets/Scenes/SampleScene.unity")
                .Where(a => File.Exists(a.source)).ToArray();
            var deps = roots.Select(a => new DependencyRecord { guid = a.guid, dependencies = Dependencies(a.source) }).ToArray();
            Write("unity-before.json", new Baseline { scripts = scripts, resources = resources, dependencies = deps });
        }
        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            Require(!string.IsNullOrEmpty(parent), "Invalid folder: " + path);
            EnsureFolder(parent);
            Require(!string.IsNullOrEmpty(AssetDatabase.CreateFolder(parent, Path.GetFileName(path))), "Cannot create " + path);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        }
        public static void Apply()
        {
            RequireBatch();
            if (File.Exists(Log + "apply.json"))
                Require(!JsonUtility.FromJson<Receipt>(File.ReadAllText(Log + "apply.json")).passed, "Already applied; do not repeat.");
            var receipt = new Receipt { stage = "apply" };
            var plan = ReadPlan();
            var changed = new List<Patch>();
            bool locked = false;
            try
            {
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                CaptureBaseline(plan);
                foreach (var patch in plan.patches)
                {
                    string current = File.Exists(patch.destination) ? patch.destination : patch.source;
                    Require(Hash(current) == patch.before || Hash(current) == patch.after, "Concurrent edit: " + current);
                    Require(Hash(patch.backup) == patch.before && Hash(patch.prepared) == patch.after, "Invalid patch backup: " + patch.source);
                }
                // Create and import ALL parent directories before moving. Do not batch dependent moves.
                foreach (var move in plan.moves) EnsureFolder(Path.GetDirectoryName(move.destination).Replace('\\', '/'));
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                EditorApplication.LockReloadAssemblies(); locked = true;
                foreach (var move in plan.moves)
                {
                    string current = AssetDatabase.GUIDToAssetPath(move.guid);
                    if (current == move.destination) { receipt.alreadyMoved++; continue; }
                    Require(current == move.source, "Unexpected GUID location: " + move.guid + " -> " + current);
                    Require(!File.Exists(move.destination) && !Directory.Exists(move.destination), "Target already exists: " + move.destination);
                    string error = AssetDatabase.MoveAsset(current, move.destination);
                    Require(string.IsNullOrEmpty(error), error);
                    if (AssetDatabase.GUIDToAssetPath(move.guid) != move.destination)
                        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                    Require(AssetDatabase.GUIDToAssetPath(move.guid) == move.destination, "Move GUID check failed: " + move.destination);
                    receipt.moved++;
                    Write("move-progress.json", receipt);
                }
                AssetDatabase.DisallowAutoRefresh();
                AssetDatabase.StartAssetEditing();
                try
                {
                    foreach (var patch in plan.patches)
                    {
                        string current = Hash(patch.destination);
                        if (current == patch.after) { receipt.alreadyPatched++; continue; }
                        Require(current == patch.before, "Changed during migration: " + patch.destination);
                        File.WriteAllBytes(patch.destination, File.ReadAllBytes(patch.prepared));
                        changed.Add(patch); receipt.patched++;
                    }
                }
                catch
                {
                    foreach (var patch in changed) File.WriteAllBytes(patch.destination, File.ReadAllBytes(patch.backup));
                    throw;
                }
                finally { AssetDatabase.StopAssetEditing(); AssetDatabase.AllowAutoRefresh(); }
                // Build Settings are intentionally not reassigned: the current game entry is unchanged.
                Require(Hash("ProjectSettings/EditorBuildSettings.asset") == Hash(Log + "build-settings.before"), "Build settings unexpectedly changed.");
                receipt.passed = true;
            }
            catch (Exception error) { receipt.error = error.ToString(); throw; }
            finally
            {
                Write("apply.json", receipt);
                if (locked) EditorApplication.UnlockReloadAssemblies();
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            }
        }
        public static void Validate()
        {
            RequireBatch();
            var result = new Receipt { stage = "validate" };
            try
            {
                var plan = ReadPlan(); var before = JsonUtility.FromJson<Baseline>(File.ReadAllText(Log + "unity-before.json"));
                foreach (var a in ReadIdentities().assets)
                {
                    Require(AssetDatabase.GUIDToAssetPath(a.guid) == a.destination, "Asset GUID/path mismatch: " + a.destination);
                    result.assetIdentities++;
                }
                foreach (var s in before.scripts)
                {
                    string path = AssetDatabase.GUIDToAssetPath(s.guid);
                    Require(CompilationPipeline.GetAssemblyNameFromScriptPath(path) == s.assembly, "Assembly boundary changed: " + path);
                    result.scripts++;
                }
                foreach (var r in before.resources)
                {
                    Require(ResourceKey(AssetDatabase.GUIDToAssetPath(r.guid)) == r.key, "Resources key changed: " + r.key);
                    result.resources++;
                }
                foreach (var d in before.dependencies)
                {
                    string path = AssetDatabase.GUIDToAssetPath(d.guid);
                    Require(Dependencies(path).SequenceEqual(d.dependencies), "Dependency identity set changed: " + path);
                    result.dependencyRoots++;
                }
                Require(Hash("ProjectSettings/EditorBuildSettings.asset") == Hash(Log + "build-settings.before"), "Build Settings bytes changed");
                Require(EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).SequenceEqual(plan.scenePaths), "Current build scenes changed");
                var input = AssetDatabase.LoadMainAssetAtPath("Assets/Project/Input/InputSystem_Actions.inputactions");
                Require(input != null && input.GetType().Name == "InputActionAsset", "Input actions failed to import");
                foreach (var p in Directory.GetFiles("Assets/Project/Settings", "*.asset"))
                    Require(AssetDatabase.LoadMainAssetAtPath(p.Replace('\\', '/')) != null, "Render setting failed to import: " + p);
                var catalog = AssetDatabase.LoadAssetAtPath<WarSandboxBattlefieldCatalog>(GameEntryMenu.CatalogPath);
                Require(catalog != null, "Current catalog missing");
                Require(catalog.TryValidate(p => plan.scenePaths.Contains(p), out var error), error);
                for (int i = 0; i < plan.scenePaths.Length; i++)
                {
                    var path = plan.scenePaths[i];
                    Require(AssetDatabase.AssetPathToGUID(path) == plan.sceneGuids[i], "Scene identity changed: " + path);
                    var scene = EditorSceneManager.OpenPreviewScene(path);
                    try
                    {
                        var objects = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).Select(t => t.gameObject).ToArray();
                        result.sceneObjects += objects.Length;
                        result.missingScripts += objects.Sum(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount);
                        if (i == 0)
                        {
                            var session = objects.Select(g => g.GetComponent<WarSandboxSceneSession>()).FirstOrDefault(s => s != null);
                            Require(session != null && session.catalog == catalog && objects.Any(g => g.GetComponent<WarSandboxFrontEnd>() != null), "Menu components/catalog mismatch");
                        }
                        else Require(objects.Any(g => { var d = g.GetComponent<WarSandboxRuntimeDeployment>(); return d != null && d.battlefieldCatalog == catalog; }), "Battlefield catalog mismatch: " + path);
                    }
                    finally { EditorSceneManager.ClosePreviewScene(scene); }
                }
                Require(result.missingScripts == 0, "Missing scene scripts");
                result.scenes = plan.scenePaths; result.passed = true;
            }
            catch (Exception error) { result.error = error.ToString(); throw; }
            finally { Write("validate.json", result); }
        }
    }
}
