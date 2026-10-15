using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MassEngine.Game.Editor
{
    /// <summary>
    /// 2026-10-11  Removes the old 512 battlefields once the 1024 maps exist (menu: Tools/战场 1024).
    ///  1. Old battle scenes = Assets/Game/Scenes/{Green,Autumn,Winter}.unity plus every .unity under
    ///     Content/Battlefields except Map1024 / Maps1024. They leave the catalog and Build Settings.
    ///  2. Every asset in the old battlefield folders that nothing else references any more
    ///     (dependency closure of all remaining scenes/prefabs/assets + exact path strings in code)
    ///     is deleted. FormationSetup and LaunchPresets keep all non-scene assets (tests load them by path).
    ///  3. Still-referenced leftovers move to Content/Battlefields/Shared/&lt;folder&gt; (GUIDs keep the links),
    ///     so the old folders disappear. A full list is written to Logs/Battlefield1024/cleanup.txt.
    /// </summary>
    public static class Battlefield1024Cleanup
    {
        const string Battlefields = "Assets/Game/Content/Battlefields";
        const string SharedDir = Battlefields + "/Shared";
        const string MainMenu = "Assets/Game/Scenes/MainMenu.unity";
        static readonly string[] OldSceneFiles = { "Assets/Game/Scenes/Green.unity", "Assets/Game/Scenes/Autumn.unity", "Assets/Game/Scenes/Winter.unity" };
        static readonly string[] KeepFolders = { Battlefields + "/Map1024", Battlefields + "/Maps1024", SharedDir };
        static readonly string[] ScenesOnlyFolders = { Battlefields + "/FormationSetup", Battlefields + "/LaunchPresets" };

        [MenuItem(Battlefield1024Generator.MenuRoot + "清理旧 512 场景…", false, 20)]
        static void Menu()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { EditorUtility.DisplayDialog("清理旧场景", "请先退出 Play 模式。", "好"); return; }
            var catalog = AssetDatabase.LoadAssetAtPath<WarSandboxBattlefieldCatalog>(Battlefield1024Generator.CatalogPath);
            if (catalog == null) { EditorUtility.DisplayDialog("清理旧场景", "找不到 " + Battlefield1024Generator.CatalogPath, "好"); return; }
            var newMaps = catalog.entries.Where(e => e.scenePath.StartsWith(Battlefield1024Generator.Root + "/") && File.Exists(e.scenePath)).ToArray();
            if (newMaps.Length == 0) { EditorUtility.DisplayDialog("清理旧场景", "还没有生成任何 1024 新地图，先运行“生成全部”。", "好"); return; }
            var oldScenes = OldScenes();
            var entries = catalog.entries.Where(e => oldScenes.Contains(e.scenePath)).Select(e => e.displayName + "（" + e.id + "）").ToArray();
            string msg = "将删除 " + oldScenes.Count + " 个旧场景文件，并从地图目录移除：\n" + string.Join("\n", entries) +
                         "\n\n保留：MainMenu、Green1024、" + string.Join("、", newMaps.Select(e => e.displayName)) +
                         "\n旧目录中不再被引用的资源会被删除，仍被引用的移到 Battlefields/Shared。\n此操作不可在编辑器内撤销（可用 git 恢复）。";
            if (!EditorUtility.DisplayDialog("清理旧 512 场景", msg, "删除", "取消")) return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            try
            {
                string log = Run(catalog, oldScenes);
                EditorUtility.DisplayDialog("清理完成", log, "好");
            }
            catch (Exception e) { Debug.LogException(e); EditorUtility.DisplayDialog("清理失败", e.Message, "好"); }
            finally { EditorUtility.ClearProgressBar(); }
        }

        public static HashSet<string> OldScenes()
        {
            var set = new HashSet<string>(OldSceneFiles.Where(File.Exists));
            foreach (var guid in AssetDatabase.FindAssets("t:Scene", new[] { Battlefields }))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (!KeepFolders.Any(k => p.StartsWith(k + "/"))) set.Add(p);
            }
            return set;
        }

        static string Run(WarSandboxBattlefieldCatalog catalog, HashSet<string> oldScenes)
        {
            var log = new StringBuilder();
            // 1) catalog + build settings
            EditorUtility.DisplayProgressBar("清理旧场景", "地图目录与 Build Settings", .05f);
            int before = catalog.entries.Length;
            catalog.entries = catalog.entries.Where(e => !oldScenes.Contains(e.scenePath)).ToArray();
            var preferred = catalog.entries.FirstOrDefault(e => e.id == "plains-1024" && !e.hiddenFromSelection)
                            ?? catalog.entries.FirstOrDefault(e => !e.hiddenFromSelection);
            Battlefield1024Generator.Require(preferred != null, "清理后地图目录为空");
            if (catalog.entries.All(e => e.id != catalog.defaultEntryId || e.hiddenFromSelection) || catalog.defaultEntryId != preferred.id)
                catalog.defaultEntryId = preferred.id;
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            log.AppendLine("地图目录：" + before + " → " + catalog.entries.Length + " 条，默认 " + catalog.defaultEntryId);
            var scenes = new List<EditorBuildSettingsScene> { new EditorBuildSettingsScene(MainMenu, true) };
            foreach (var e in catalog.entries)
                if (File.Exists(e.scenePath) && scenes.All(s => s.path != e.scenePath)) scenes.Add(new EditorBuildSettingsScene(e.scenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            log.AppendLine("Build Settings：" + string.Join("，", scenes.Select(s => Path.GetFileNameWithoutExtension(s.path))));

            if (oldScenes.Contains(SceneManager.GetActiveScene().path)) EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // 2) candidates = old scenes + every asset in the old battlefield folders (+ old per-scene configs)
            EditorUtility.DisplayProgressBar("清理旧场景", "计算依赖", .2f);
            var all = AssetDatabase.GetAllAssetPaths().Where(p => p.StartsWith("Assets/") && !AssetDatabase.IsValidFolder(p)).ToArray();
            var candidates = new HashSet<string>(oldScenes);
            foreach (var p in all)
            {
                if (p.StartsWith(Battlefields + "/"))
                {
                    if (KeepFolders.Any(k => p.StartsWith(k + "/"))) continue;
                    if (ScenesOnlyFolders.Any(k => p.StartsWith(k + "/"))) continue;   // scenes there are already in oldScenes
                    candidates.Add(p);
                }
                else if (p.StartsWith("Assets/Game/Scenes/") && OldSceneFiles.Any(s => Path.GetFileNameWithoutExtension(p).StartsWith(Path.GetFileNameWithoutExtension(s) + "-")))
                    candidates.Add(p);   // e.g. Green-Flow.asset, Green-System.asset
            }
            var roots = all.Where(p => !candidates.Contains(p) && !p.StartsWith("Assets/ThirdParty/") && !p.EndsWith(".cs")).ToArray();
            var keep = new HashSet<string>(AssetDatabase.GetDependencies(roots, true));
            // exact asset paths spelled out in code (tests, tools) stay where they are
            var code = new StringBuilder();
            foreach (var cs in Directory.GetFiles("Assets", "*.cs", SearchOption.AllDirectories))
                if (!cs.Replace('\\', '/').StartsWith("Assets/ThirdParty/")) code.Append(File.ReadAllText(cs));
            string codeText = code.ToString();
            var pinned = new HashSet<string>(candidates.Where(p => !oldScenes.Contains(p) && codeText.Contains("\"" + p + "\"")));

            var delete = candidates.Where(p => oldScenes.Contains(p) || (!keep.Contains(p) && !pinned.Contains(p))).OrderBy(p => p).ToList();
            var move = candidates.Where(p => !oldScenes.Contains(p) && keep.Contains(p) && !pinned.Contains(p) && p.StartsWith(Battlefields + "/")).OrderBy(p => p).ToList();

            // 3) delete
            EditorUtility.DisplayProgressBar("清理旧场景", "删除 " + delete.Count + " 个资源", .5f);
            var failed = new List<string>();
            AssetDatabase.DeleteAssets(delete.ToArray(), failed);
            // 4) move still-referenced leftovers (GUIDs and references survive MoveAsset)
            EditorUtility.DisplayProgressBar("清理旧场景", "整理 " + move.Count + " 个仍被引用的资源", .75f);
            var moveErrors = new List<string>();
            foreach (var p in move)
            {
                string rel = p.Substring(Battlefields.Length + 1);
                string dest = SharedDir + "/" + rel;
                Battlefield1024Generator.EnsureFolder(Path.GetDirectoryName(dest).Replace('\\', '/'));
                string err = AssetDatabase.MoveAsset(p, dest);
                if (!string.IsNullOrEmpty(err)) moveErrors.Add(p + "：" + err);
            }
            // 5) empty folders
            int removedFolders = 0;
            foreach (var dir in Directory.GetDirectories(Battlefields, "*", SearchOption.AllDirectories).Select(d => d.Replace('\\', '/')).OrderByDescending(d => d.Length))
            {
                if (KeepFolders.Any(k => dir == k || dir.StartsWith(k + "/"))) continue;
                if (Directory.Exists(dir) && Directory.GetFiles(dir).All(f => f.EndsWith(".meta")) && Directory.GetDirectories(dir).Length == 0)
                    if (AssetDatabase.DeleteAsset(dir)) removedFolders++;
            }
            AssetDatabase.SaveAssets(); AssetDatabase.Refresh();

            log.AppendLine("删除旧场景 " + oldScenes.Count + " 个，资源共 " + (delete.Count - failed.Count) + " 个" + (failed.Count > 0 ? "（失败 " + failed.Count + "）" : ""));
            log.AppendLine("移到 Battlefields/Shared：" + (move.Count - moveErrors.Count) + " 个；代码按路径引用而保留原处：" + pinned.Count + " 个；空目录 " + removedFolders);
            Directory.CreateDirectory(Battlefield1024Generator.LogDir);
            File.WriteAllText(Battlefield1024Generator.LogDir + "/cleanup.txt", log + "\n[deleted]\n" + string.Join("\n", delete) +
                "\n\n[failed]\n" + string.Join("\n", failed) + "\n\n[moved]\n" + string.Join("\n", move) + "\n\n[move errors]\n" + string.Join("\n", moveErrors) +
                "\n\n[pinned by code]\n" + string.Join("\n", pinned));
            Debug.Log("[Battlefield1024] 清理完成\n" + log);
            return log + "\n详细清单：" + Battlefield1024Generator.LogDir + "/cleanup.txt";
        }
    }
}
