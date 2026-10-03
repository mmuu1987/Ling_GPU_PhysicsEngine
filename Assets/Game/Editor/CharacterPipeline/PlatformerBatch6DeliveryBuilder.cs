using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MassEngine.Game.Editor
{
    public static class PlatformerBatch6DeliveryBuilder
    {
        public const string MenuScene = PlatformerBatch6Builder.Integrated + "/Menu.unity";
        private sealed class PreviewPayload { public string target; public byte[] bytes; }

        public static void PrepareOfficial06()
        {
            string[] keys = { "crab", "enemy", "skull" };
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode first.");
            if (Directory.Exists(OfficialRosterBuilder.Root) || File.Exists(OfficialRosterBuilder.Root))
                throw new InvalidOperationException("Refusing to add previews to existing official content: " + OfficialRosterBuilder.Root);

            // Decode and validate every capture before creating any output file.
            var payloads = new List<PreviewPayload>();
            foreach (string key in keys)
            {
                string source = Path.Combine("Logs/AgentPlatformer6", "platformer6-battles-01-" + key + "-fight.png");
                string target = Path.Combine(OfficialRosterBuilder.PreviewSource, "troops6-" + key + ".png");
                if (!File.Exists(source) || File.Exists(target) || Directory.Exists(target))
                    throw new InvalidOperationException("Fresh real preview required: " + key);
                payloads.Add(new PreviewPayload { target = target, bytes = CreatePreviewPng(source, key) });
            }

            bool previewDirectoryExisted = Directory.Exists(OfficialRosterBuilder.PreviewSource);
            var createdTargets = new List<string>();
            try
            {
                Directory.CreateDirectory(OfficialRosterBuilder.PreviewSource);
                foreach (var payload in payloads)
                {
                    // CreateNew makes the no-overwrite check race-safe; track each file before writing its bytes.
                    using (var output = new FileStream(payload.target, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        createdTargets.Add(payload.target);
                        output.Write(payload.bytes, 0, payload.bytes.Length);
                    }
                }
                OfficialRosterBuilder.Prepare06();
            }
            catch
            {
                foreach (string target in createdTargets)
                {
                    try { if (File.Exists(target)) File.Delete(target); }
                    catch (Exception rollbackError) { Debug.LogError("Could not remove partial preview " + target + ": " + rollbackError); }
                }
                if (!previewDirectoryExisted)
                {
                    try
                    {
                        if (Directory.Exists(OfficialRosterBuilder.PreviewSource) && !Directory.EnumerateFileSystemEntries(OfficialRosterBuilder.PreviewSource).Any())
                            Directory.Delete(OfficialRosterBuilder.PreviewSource);
                    }
                    catch (Exception rollbackError) { Debug.LogError("Could not remove empty preview directory: " + rollbackError); }
                }
                throw;
            }
            Debug.Log("PLATFORMER6_OFFICIAL06_READY");
        }

        private static byte[] CreatePreviewPng(string source, string key)
        {
            Texture2D full = null, card = null;
            try
            {
                full = new Texture2D(2, 2, TextureFormat.RGB24, false);
                card = new Texture2D(1280, 592, TextureFormat.RGB24, false);
                if (!full.LoadImage(File.ReadAllBytes(source)) || full.width != 1280 || full.height != 720)
                    throw new InvalidOperationException("Unexpected source capture size: " + key);
                // Center crop only: real default-deployment battle captures; no invented units or stretched aspect ratio.
                card.SetPixels(full.GetPixels(0, 64, 1280, 592)); card.Apply();
                byte[] png = card.EncodeToPNG();
                if (png == null || png.Length == 0) throw new InvalidOperationException("Could not encode source capture: " + key);
                return png;
            }
            finally
            {
                if (full != null) Object.DestroyImmediate(full);
                if (card != null) Object.DestroyImmediate(card);
            }
        }

        public static void CreateMenu01()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode first.");
            if (File.Exists(MenuScene)) throw new InvalidOperationException("Never overwrite the owned menu.");
            var catalog = AssetDatabase.LoadAssetAtPath<WarSandboxBattlefieldCatalog>(PlatformerBatch6Builder.Integrated + "/Catalog.asset");
            if (catalog == null || !catalog.TryValidate(p => File.Exists(p), out string error) || !catalog.TryValidateTemplates(out error)) throw new InvalidOperationException("New collection is not valid.");
            var setup = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                if (!AssetDatabase.CopyAsset(WarSandboxLaunchPresetsBuilder.MenuScene, MenuScene)) throw new InvalidOperationException("Menu copy failed.");
                var menu = EditorSceneManager.OpenScene(MenuScene, OpenSceneMode.Single);
                var session = Object.FindFirstObjectByType<WarSandboxSceneSession>();
                if (session == null) throw new InvalidOperationException("Menu has no scene session.");
                session.catalog = AssetDatabase.LoadAssetAtPath<WarSandboxBattlefieldCatalog>(PlatformerBatch6Builder.Integrated + "/Catalog.asset");
                session.enterDefaultOnStart = false;
                foreach (var cycle in Object.FindObjectsByType<WarSandboxTerrainCycle>(FindObjectsSortMode.None)) cycle.session = session;
                EditorSceneManager.SaveScene(menu);
                AssetDatabase.SaveAssets();
                Debug.Log("PLATFORMER6_MENU_READY " + MenuScene + " entries=" + catalog.entries.Length);
            }
            finally
            {
                if (setup.Length > 0) EditorSceneManager.RestoreSceneManagerSetup(setup);
                else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
        }
    }
}
