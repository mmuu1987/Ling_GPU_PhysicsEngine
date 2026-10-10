using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MassEngine.Game.Editor
{
    // Resumes only this explicit, hash-checked migration; GUID identity is authoritative.
    public static class GameLayoutRecovery20261007
    {
        const string Root = "Logs/GameLayout/";
        const string Review = Root + "Review20261007/";
        [Serializable] public class Move { public string source, destination, guid; }
        [Serializable] public class Moves { public Move[] moves; }
        [Serializable] public class Audit { public bool passed; }
        [Serializable] public class Receipt
        {
            public bool passed;
            public string error;
            public int alreadyMoved, movedNow, patchesWritten, patchesAlreadyApplied;
            public string[] scenes;
        }
        static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        static string Hash(string path)
        {
            using (var algorithm = SHA256.Create())
            using (var stream = File.OpenRead(path))
                return BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            Require(!string.IsNullOrEmpty(parent), "Invalid parent: " + path);
            EnsureFolder(parent);
            Require(!string.IsNullOrEmpty(AssetDatabase.CreateFolder(parent, Path.GetFileName(path))), "Cannot create " + path);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }
        public static void Apply()
        {
            Require(Application.isBatchMode, "Owned batch editor only");
            Require(!File.Exists(Review + "recovery-apply.json"), "Prior recovery receipt exists; inspect first");
            var receipt = new Receipt();
            try
            {
                Require(JsonUtility.FromJson<Audit>(File.ReadAllText(Review + "recovery-audit.json")).passed, "Original-file audit failed");
                var plan = JsonUtility.FromJson<GameLayoutMigration.Plan>(File.ReadAllText(Root + "plan.json"));
                var moves = JsonUtility.FromJson<Moves>(File.ReadAllText(Review + "recovery-moves.json"));
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                foreach (var move in moves.moves)
                {
                    string current = AssetDatabase.GUIDToAssetPath(move.guid);
                    Require(!string.IsNullOrEmpty(current), "Missing original GUID: " + move.guid);
                    if (current == move.destination) { receipt.alreadyMoved++; continue; }
                    Require(current == move.source, "Unexpected location for " + move.guid + ": " + current);
                    Require(!File.Exists(move.destination) && !Directory.Exists(move.destination), "Occupied destination: " + move.destination);
                    EnsureFolder(Path.GetDirectoryName(move.destination).Replace('\\', '/'));
                    string error = AssetDatabase.MoveAsset(current, move.destination);
                    Require(string.IsNullOrEmpty(error), error);
                    // Move dependencies must be visible before the next move (folder, then contained scene).
                    AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                    Require(AssetDatabase.GUIDToAssetPath(move.guid) == move.destination, "Move GUID check failed: " + move.destination);
                    receipt.movedNow++;
                }
                foreach (var patch in plan.patches)
                {
                    string current = Hash(patch.destination);
                    Require(current == patch.before || current == Hash(patch.prepared), "Concurrent source change: " + patch.destination);
                    Require(Hash(patch.backup) == patch.before, "Invalid original backup: " + patch.source);
                }
                var changed = new List<GameLayoutMigration.Patch>();
                var oldBuild = EditorBuildSettings.scenes;
                AssetDatabase.DisallowAutoRefresh();
                AssetDatabase.StartAssetEditing();
                try
                {
                    foreach (var patch in plan.patches)
                    {
                        if (Hash(patch.destination) == Hash(patch.prepared)) { receipt.patchesAlreadyApplied++; continue; }
                        File.WriteAllBytes(patch.destination, File.ReadAllBytes(patch.prepared));
                        changed.Add(patch); receipt.patchesWritten++;
                    }
                    EditorBuildSettings.scenes = plan.scenes.Select(p => new EditorBuildSettingsScene(p, true)).ToArray();
                    receipt.scenes = plan.scenes; receipt.passed = true;
                }
                catch
                {
                    foreach (var patch in changed) File.WriteAllBytes(patch.destination, File.ReadAllBytes(patch.backup));
                    EditorBuildSettings.scenes = oldBuild;
                    throw;
                }
                finally
                {
                    AssetDatabase.StopAssetEditing(); AssetDatabase.AllowAutoRefresh();
                    AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                }
            }
            catch (Exception error) { receipt.error = error.ToString(); throw; }
            finally { File.WriteAllText(Review + "recovery-apply.json", JsonUtility.ToJson(receipt, true)); }
        }
    }
}
