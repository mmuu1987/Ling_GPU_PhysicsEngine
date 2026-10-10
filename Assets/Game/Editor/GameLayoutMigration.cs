using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MassEngine.Game.Editor
{
    // Explicit one-shot local maintenance request; never runs from an ordinary player build.
    [InitializeOnLoad]
    public static class GameLayoutMigration
    {
        const string Root = "Logs/GameLayout/";
        [Serializable] public class Move { public string source, destination; }
        [Serializable] public class Patch { public string source, destination, before, backup, prepared; }
        [Serializable] public class Plan { public Move[] moves; public Patch[] patches; public string[] scenes, sceneGuids; }
        [Serializable] class Receipt { public bool passed; public string stage, error; public string[] scenes, dirtyAssets; public int moves, patches; }
        static GameLayoutMigration() { EditorApplication.update += Tick; }
        static string Hash(string path)
        {
            using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
        }
        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        static bool Affected(string path, Plan plan) => plan.moves.Any(m => path == m.source || path.StartsWith(m.source + "/", StringComparison.Ordinal)) || plan.patches.Any(p => p.source == path);
        static void Write(string name, Receipt receipt) { Directory.CreateDirectory(Root); File.WriteAllText(Root + name + ".json", JsonUtility.ToJson(receipt, true)); }
        static void Tick()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
            string request = Root + "request.txt";
            if (!File.Exists(request)) return;
            string command = File.ReadAllText(request).Trim();
            File.Delete(request);
            try
            {
                var plan = JsonUtility.FromJson<Plan>(File.ReadAllText(Root + "plan.json"));
                if (command == "preflight") Preflight(plan);
                else if (command == "apply") Apply(plan);
                else if (command == "validate") Validate(plan);
            }
            catch (Exception error) { Write(command, new Receipt { stage = command, error = error.ToString() }); Debug.LogException(error); }
        }
        static void Preflight(Plan plan)
        {
            var dirty = Resources.FindObjectsOfTypeAll<UnityEngine.Object>()
                .Where(o => o != null && EditorUtility.IsPersistent(o) && EditorUtility.IsDirty(o))
                .Select(AssetDatabase.GetAssetPath).Where(p => !string.IsNullOrEmpty(p) && Affected(p, plan)).Distinct().ToArray();
            var scenes = Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt).ToArray();
            Write("preflight", new Receipt { stage = "preflight", passed = dirty.Length == 0 && scenes.All(s => !s.isDirty), dirtyAssets = dirty,
                scenes = scenes.Select(s => s.path + (s.isDirty ? " [DIRTY]" : "")).ToArray() });
        }
        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/'); EnsureFolder(parent);
            Require(!string.IsNullOrEmpty(AssetDatabase.CreateFolder(parent, Path.GetFileName(path))), "Cannot create " + path);
        }
        static void Apply(Plan plan)
        {
            Require(!File.Exists(Root + "apply.json"), "An apply receipt already exists; inspect before retrying.");
            Preflight(plan);
            Require(JsonUtility.FromJson<Receipt>(File.ReadAllText(Root + "preflight.json")).passed, "Unsaved editor content; no assets moved.");
            foreach (var patch in plan.patches) Require(Hash(patch.source) == patch.before, "Changed since planning: " + patch.source);
            var completed = new List<Move>();
            var patched = new List<Patch>();
            var oldBuild = EditorBuildSettings.scenes;
            AssetDatabase.DisallowAutoRefresh();
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var move in plan.moves)
                {
                    Require(move.source.StartsWith("Assets/Game/", StringComparison.Ordinal) && move.destination.StartsWith("Assets/Game/", StringComparison.Ordinal), "Move outside game directory");
                    Require(!File.Exists(move.destination) && !Directory.Exists(move.destination), "Destination already exists: " + move.destination);
                    Require(File.Exists(move.source) || Directory.Exists(move.source), "Source missing: " + move.source);
                    EnsureFolder(Path.GetDirectoryName(move.destination).Replace('\\', '/'));
                    string error = AssetDatabase.MoveAsset(move.source, move.destination);
                    Require(string.IsNullOrEmpty(error), error);
                    completed.Add(move);
                }
                foreach (var patch in plan.patches)
                {
                    Require(Hash(patch.destination) == patch.before, "Asset changed during move: " + patch.destination);
                    File.WriteAllBytes(patch.destination, File.ReadAllBytes(patch.prepared)); patched.Add(patch);
                }
                EditorBuildSettings.scenes = plan.scenes.Select(p => new EditorBuildSettingsScene(p, true)).ToArray();
                Write("apply", new Receipt { passed = true, stage = "apply", moves = completed.Count, patches = patched.Count, scenes = plan.scenes });
            }
            catch
            {
                foreach (var patch in patched) File.WriteAllBytes(patch.destination, File.ReadAllBytes(patch.backup));
                foreach (var move in completed.AsEnumerable().Reverse())
                {
                    string error = AssetDatabase.MoveAsset(move.destination, move.source);
                    if (!string.IsNullOrEmpty(error)) Debug.LogError("Migration rollback: " + error);
                }
                EditorBuildSettings.scenes = oldBuild;
                throw;
            }
            finally { AssetDatabase.StopAssetEditing(); AssetDatabase.AllowAutoRefresh(); AssetDatabase.Refresh(); }
        }
        static void Validate(Plan plan)
        {
            for (int i = 0; i < plan.scenes.Length; i++)
            {
                Require(AssetDatabase.AssetPathToGUID(plan.scenes[i]) == plan.sceneGuids[i], "Scene GUID mismatch: " + plan.scenes[i]);
                Require(AssetDatabase.LoadAssetAtPath<SceneAsset>(plan.scenes[i]) != null, "Scene import failed: " + plan.scenes[i]);
            }
            Require(EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).SequenceEqual(plan.scenes), "Build scene list mismatch");
            var catalog = AssetDatabase.LoadAssetAtPath<WarSandboxBattlefieldCatalog>("Assets/Game/Scenes/Catalog.asset");
            Require(catalog != null, "Missing current catalog");
            Require(catalog.TryValidate(p => plan.scenes.Contains(p), out var error), error);
            Write("validate", new Receipt { passed = true, stage = "compiled-and-catalog-validated", scenes = plan.scenes });
        }
    }
}
