using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MassEngine.Game.Editor
{
    /// <summary>
    /// Rebuilds Build Settings from the main battlefield catalog: MainMenu + every catalog scene that exists.
    /// Some legacy PlayMode tests (e.g. PlatformerBatch6PlanBuildSetup) overwrite the scene list without
    /// restoring it; this puts the game's own list back.
    /// </summary>
    static class RestoreBuildScenes
    {
        const string MainMenu = "Assets/Game/Scenes/MainMenu.unity";

        [MenuItem(Battlefield1024Generator.MenuRoot + "修复 Build Settings 场景列表", false, 50)]
        public static void Restore()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<WarSandboxBattlefieldCatalog>(Battlefield1024Generator.CatalogPath);
            if (catalog == null || catalog.entries == null)
            {
                Debug.LogError("[BuildScenes] 找不到地图目录 " + Battlefield1024Generator.CatalogPath);
                return;
            }
            var paths = new List<string> { MainMenu };
            foreach (var e in catalog.entries)
                if (!string.IsNullOrEmpty(e.scenePath) && File.Exists(e.scenePath) && !paths.Contains(e.scenePath))
                    paths.Add(e.scenePath);
            var before = EditorBuildSettings.scenes.Select(s => s.path).ToArray();
            EditorBuildSettings.scenes = paths.Select(p => new EditorBuildSettingsScene(p, true)).ToArray();
            AssetDatabase.SaveAssets();
            Debug.Log("[BuildScenes] Build Settings 已重建：" + string.Join("，", paths.Select(Path.GetFileNameWithoutExtension)) +
                      "（原有 " + before.Length + " 个）");
        }
    }
}
