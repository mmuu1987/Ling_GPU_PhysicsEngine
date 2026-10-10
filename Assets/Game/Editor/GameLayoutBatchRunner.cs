using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace MassEngine.Game.Editor
{
    public static class GameLayoutBatchRunner
    {
        private static void RequireBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Owned batch editor only.");
        }
        private static void InvokeMigration(string method)
        {
            var plan = JsonUtility.FromJson<GameLayoutMigration.Plan>(File.ReadAllText("Logs/GameLayout/plan.json"));
            var target = typeof(GameLayoutMigration).GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic);
            if (target == null) throw new MissingMethodException(method);
            try { target.Invoke(null, new object[] { plan }); }
            catch (TargetInvocationException exception) { throw exception.InnerException ?? exception; }
        }
        public static void Apply()
        {
            RequireBatch();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            InvokeMigration("Apply");
        }
        [Serializable] private sealed class CheckReceipt { public bool passed; public string error; }
        public static void Validate()
        {
            RequireBatch();
            InvokeMigration("Validate");
            const string directory = "Logs/GameLayout/Review20261007/";
            if (File.Exists(directory + "scene-check.json"))
                throw new IOException("Refuse to reuse an earlier scene check receipt.");
            File.WriteAllText(directory + "validate-scenes.request", "validate");
            var method = typeof(GameLayoutSceneCheck).GetMethod("Tick", BindingFlags.Static | BindingFlags.NonPublic);
            method.Invoke(null, null);
            if (!File.Exists(directory + "scene-check.json"))
                throw new InvalidOperationException("Scene validation did not run.");
            var receipt = JsonUtility.FromJson<CheckReceipt>(File.ReadAllText(directory + "scene-check.json"));
            if (!receipt.passed) throw new InvalidOperationException(receipt.error);
            Debug.Log("Game directory migration and all four scene checks passed.");
        }
    }
}
