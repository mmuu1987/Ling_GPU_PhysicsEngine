using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MassEngine.Game.Editor
{
    /// <summary>
    /// Official game content: the five M7.1 launch battlefields, unchanged, followed by the new-unit battlefields
    /// (cavalry, dragons, giants, non-human batch 2, unified roster; v2 adds the giant batch 3; v4 adds the regular-size batch 4).
    /// New authored content only: the M7.1 catalog,
    /// the unified-roster catalogs and every referenced scene are read, never written. Player-facing names and
    /// descriptions replace the internal trial labels; previews come from OfficialRosterPreviewTests.
    /// </summary>
    public static class OfficialRosterBuilder
    {
        public const string Parent = "Assets/Game/OfficialRoster";
        /// <summary>Current official content (v4 = v3 + new-unit batch 4: skull orc, ninja, tribal warrior). Earlier versions stay in the project, read-only.</summary>
        public const string Root = Parent + "/Version05";
        public const string Version01Root = Parent + "/Version01";
        public const string Version02Root = Parent + "/Version02";
        public const string Version03Root = Parent + "/Version03";
        public const string Version04Root = Parent + "/Version04";
        /// <summary>Earlier official versions, oldest first; their previews are reused byte for byte.</summary>
        public static readonly string[] PreviousRoots = { Version01Root, Version02Root, Version03Root, Version04Root };
        public const string CatalogPath = Root + "/Catalog.asset";
        public const string MenuScene = Root + "/LaunchMenu.unity";
        /// <summary>Batch-4 collection = the Giants3/Prepared02 collection (v3 source) + the three regular-size troops.</summary>
        public const string SourceCatalog = "Assets/Game/Troops5/Prepared01/Integrated/Catalog.asset";
        public const string PreviewSource = "Logs/OfficialRoster/previews";
        public const string Output01 = "Builds/OfficialRoster-20261001-01";
        public const string Output02 = "Builds/OfficialRoster-20261001-02";
        public const string Output03 = "Builds/OfficialRoster-20261001-03";
        public const string Output04 = "Builds/OfficialRoster-20261001-04";
        public const string Output05 = "Builds/OfficialRoster-20261001-05";
        public const string Output06 = "Builds/OfficialRoster-20261001-06";
        public const string Output07 = "Builds/OfficialRoster-20261001-07";
        public const string Output08 = "Builds/OfficialRoster-20261001-08";

        /// <summary>id in <see cref="SourceCatalog"/>, official display name, one-line flavour (no fixed winner).</summary>
        public static readonly string[][] NewEntries =
        {
            new[] { "cavalry-mounted-knight", "骑兵冲锋", "剑盾骑兵对阵步兵；骑兵首次接敌的冲锋造成三倍伤害。" },
            new[] { "dragons-dragon", "飞龙来袭", "飞龙低空盘旋喷吐火球，爆炸溅射可以一次波及多名敌人。" },
            new[] { "dragons-dragon-phalanx", "飞龙 vs 密集方阵", "密集方阵正面迎击飞龙，阵型越密，火球溅射越致命。" },
            new[] { "dragons-dragon-evolved", "进化巨龙", "体型更大、火力更强的进化巨龙，火球爆炸范围更广。" },
            new[] { "dragons-dragon-evolved-phalanx", "进化巨龙 vs 密集方阵", "150 名骑士组成方阵围攻进化巨龙。" },
            new[] { "giants-demon", "巨型恶魔", "两头巨型恶魔冲击骑士队列，生命与攻击远超常规兵种。" },
            new[] { "giants-dino", "巨型暴龙", "两头巨型暴龙撕开骑士阵线，体型巨大、难以合围。" },
            new[] { "giants-yeti", "巨型雪人", "两头巨型雪人挥拳横扫骑士队列，皮糙肉厚、力大无穷。" },
            new[] { "giants-bluedemon", "巨型蓝魔", "两头巨型蓝魔猛扑骑士队列，攻击凶猛、出手沉重。" },
            new[] { "giants-alien", "外星巨人", "两名外星巨人远距离发射能量弹，骑士必须顶着火力冲到近前。" },
            new[] { "troops4-orcskull", "骷髅兽人", "头戴骨盔的骷髅兽人步步压上，血厚力沉，以少打多硬撼骑士军团。" },
            new[] { "troops4-ninja", "忍者突袭", "持刀忍者比骑士跑得更快，出手迅捷但身板单薄，抢先接敌是关键。" },
            new[] { "troops4-tribal", "部落战士", "戴图腾面具的部落战士成群冲锋，攻守均衡，与骑士军团人数相当。" },
            new[] { "troops5-cactoro", "仙人掌枪手", "荒漠射手直射尖刺，抢先在接敌前压制骑士冲锋；无范围伤害，被近身后需防线支撑。" },
            new[] { "troops5-frog", "毒蛙群", "体型娇小动作敏捷，数量占优；轻装近战抢攻，前排承受主要冲击。" },
            new[] { "troops5-monkroose", "猴獴战士", "身手矫健兼具力量与速度，单体近战均衡；面对骑士军团寸土不让。" },
            new[] { "nonhuman2-triceratops", "三角龙冲阵", "体型庞大的三角龙投入战场。" },
            new[] { "nonhuman2-stegosaurus", "剑龙防线", "背负骨板的剑龙投入战场。" },
            new[] { "nonhuman2-spider", "巨型蜘蛛", "成群的巨型蜘蛛投入战场。" },
            new[] { "unified-regular", "自由编成 · 常规兵种", "12 种常规兵种，可在配兵布阵中自由组合双方军团。" },
            new[] { "unified-all-regular", "全兵种同场", "新旧兵种同场展示，适合挑选喜欢的兵种。" },
            new[] { "unified-large", "自由编成 · 大型兵种", "面向大型兵种的配兵战场，可编入体型较大的单位。" },
        };

        /// <summary>v1-v4 were authored on 2026-10-01 and are kept as is; the current content is <see cref="Prepare05"/>.</summary>
        public static void Prepare01() => throw new InvalidOperationException("Version01 is already authored and kept read-only; run Prepare05.");
        public static void Prepare02() => throw new InvalidOperationException("Version02 is already authored and kept read-only; run Prepare05.");
        public static void Prepare03() => throw new InvalidOperationException("Version03 is already authored and kept read-only; run Prepare05.");
        public static void Prepare04() => throw new InvalidOperationException("Version04 is already authored and kept read-only; run Prepare05.");

        public static void Prepare05()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode first.");
            if (Directory.Exists(Root)) throw new InvalidOperationException("Refusing to overwrite existing official content: " + Root);
            var setup = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                // Pass 1: read team/unit counts from each new scene before authoring (scene loads can unload wrappers).
                var source = Load<WarSandboxBattlefieldCatalog>(SourceCatalog);
                var paths = NewEntries.Select(n => Find(source, n[0]).scenePath).ToArray();
                var teams = new int[paths.Length]; var totals = new int[paths.Length];
                for (int i = 0; i < paths.Length; i++)
                {
                    EditorSceneManager.OpenScene(paths[i], OpenSceneMode.Single);
                    var manager = Object.FindFirstObjectByType<MassEngineManager>() ?? throw new BuildFailedException("No manager: " + paths[i]);
                    var units = manager.scenarioConfig.unitTypes;
                    teams[i] = units.Select(u => u.teamId).Distinct().Count();
                    totals[i] = units.Sum(u => u.spawnConfig.unitCount);
                }
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

                // Pass 2: author the catalog with fresh asset wrappers.
                if (!AssetDatabase.IsValidFolder(Parent)) AssetDatabase.CreateFolder("Assets/Game", "OfficialRoster");
                AssetDatabase.CreateFolder(Parent, Path.GetFileName(Root));
                AssetDatabase.CreateFolder(Root, "Previews");
                var launch = Load<WarSandboxBattlefieldCatalog>(WarSandboxLaunchPresetsBuilder.CatalogPath);
                source = Load<WarSandboxBattlefieldCatalog>(SourceCatalog);
                var entries = launch.entries.Select(e => e.CopyIdentity()).ToList();
                for (int i = 0; i < NewEntries.Length; i++)
                {
                    var entry = Find(source, NewEntries[i][0]).CopyIdentity();
                    string mode = entry.rules.rules.gameMode == WarSandboxGameMode.ControlPoint ? "据点战" : "歼灭战";
                    entry.displayName = NewEntries[i][1];
                    entry.description = teams[i] + " 支军团 · " + totals[i].ToString("N0", CultureInfo.InvariantCulture) + " 人 · " + mode + "\n" + NewEntries[i][2];
                    entry.briefing = (teams[i] > 2 ? "目标：成为最后存活的军团。" : "目标：消灭另一支军团。") +
                        "\n按 Enter 开战，Space 暂停；可在配兵布阵中调整兵种与人数，并保存自己的方案。";
                    entry.preview = ImportPreview(entry.id);
                    entries.Add(entry);
                }
                var catalog = ScriptableObject.CreateInstance<WarSandboxBattlefieldCatalog>();
                catalog.defaultEntryId = launch.defaultEntryId;
                catalog.entries = entries.ToArray();
                catalog.templates = MergeTemplates(launch.templates, source.templates);
                AssetDatabase.CreateAsset(catalog, CatalogPath);
                AssetDatabase.SaveAssets();
                catalog = Load<WarSandboxBattlefieldCatalog>(CatalogPath);
                if (!catalog.TryValidate(p => AssetDatabase.LoadAssetAtPath<SceneAsset>(p) != null, out var error) || !catalog.TryValidateTemplates(out error))
                    throw new BuildFailedException(error);

                // The official menu is the M7.1 launch menu pointed at the new catalog (same first-run default).
                if (!AssetDatabase.CopyAsset(WarSandboxLaunchPresetsBuilder.MenuScene, MenuScene)) throw new BuildFailedException("Menu copy failed.");
                var menu = EditorSceneManager.OpenScene(MenuScene, OpenSceneMode.Single);
                var session = Object.FindFirstObjectByType<WarSandboxSceneSession>() ?? throw new BuildFailedException("Menu has no session.");
                session.catalog = Load<WarSandboxBattlefieldCatalog>(CatalogPath);
                foreach (var cycle in Object.FindObjectsByType<WarSandboxTerrainCycle>(FindObjectsSortMode.None)) cycle.session = session;
                EditorSceneManager.SaveScene(menu);
                EditorBuildSettings.scenes = Scenes(Load<WarSandboxBattlefieldCatalog>(CatalogPath)).Select(p => new EditorBuildSettingsScene(p, true)).ToArray();
                AssetDatabase.SaveAssets();
                int previews = catalog.entries.Count(e => e.preview != null);
                Debug.Log("OFFICIAL_ROSTER_READY root=" + Root + " entries=" + catalog.entries.Length + " templates=" + catalog.templates.Length + " previews=" + previews);
            }
            finally
            {
                if (setup.Length > 0) EditorSceneManager.RestoreSceneManagerSetup(setup);
                else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
        }

        public static void Build01() => Build(Output01);
        /// <summary>Same content as 01 plus the opt-in official-catalog smoke in WarSandboxTerrainCycle (development build only).</summary>
        public static void Build02() => Build(Output02);
        /// <summary>v2 content (adds the giant Yeti), with the same opt-in official-catalog smoke as 02.</summary>
        public static void Build03() => Build(Output03);
        public static void Build04() => Build(Output04);
        /// <summary>v3 content unchanged; the runtime UI is unified on the B "tactical command" style.</summary>
        public static void Build05() => Build(Output05);
        /// <summary>05 plus a dark plate behind the battle title (readable over bright terrain) and a one-line move-target label.</summary>
        public static void Build06() => Build(Output06);
        /// <summary>v4 content: adds the regular-size batch 4 (skull orc, ninja, tribal warrior) under "新兵种".</summary>
        public static void Build07() => Build(Output07);
        public static void Build08() => Build(Output08);

        private static void Build(string outputPath)
        {
            if (Directory.Exists(outputPath) || File.Exists(outputPath)) throw new BuildFailedException("Refusing to replace " + outputPath);
            var catalog = Load<WarSandboxBattlefieldCatalog>(CatalogPath);
            if (!catalog.TryValidate(p => AssetDatabase.LoadAssetAtPath<SceneAsset>(p) != null, out var error) || !catalog.TryValidateTemplates(out error))
                throw new BuildFailedException(error);
            string[] scenes = Scenes(catalog);
            string output = Path.GetFullPath(outputPath); Directory.CreateDirectory(output);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = scenes, target = BuildTarget.StandaloneWindows64,
                locationPathName = Path.Combine(output, "WarSandbox.exe"), options = BuildOptions.Development });
            if (report.summary.result != BuildResult.Succeeded) throw new BuildFailedException("Official Windows build failed.");
            File.WriteAllText(Path.Combine(output, "Start-WarSandbox.cmd"), "@echo off\r\ncd /d \"%~dp0\"\r\nstart \"\" \"WarSandbox.exe\" -screen-fullscreen 0 -screen-width 1280 -screen-height 720\r\n", new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(output, "说明.txt"),
                "首次进入 512 人开阔对冲，按 Enter 开战、Space 暂停。配兵布阵可编辑并保存自己的方案。\r\n" +
                "返回战场目录可选择全部 " + catalog.entries.Length + " 个战场：M7.1 的 5 个首发战场，以及骑兵、飞龙、进化巨龙、巨型恶魔、巨型暴龙、" +
                "巨型雪人、巨型蓝魔、外星巨人、骷髅兽人、忍者、部落战士、三角龙、剑龙、巨型蜘蛛和自由编成等新兵种战场。\r\n", new UTF8Encoding(true));
            Debug.Log("OFFICIAL_ROSTER_BUILD_OK " + output + " scenes=" + scenes.Length);
        }

        public static string[] Scenes(WarSandboxBattlefieldCatalog catalog) =>
            new[] { MenuScene }.Concat(catalog.entries.Select(e => e.scenePath)).Distinct().ToArray();

        private static WarSandboxBattlefieldEntry Find(WarSandboxBattlefieldCatalog catalog, string id) =>
            catalog.entries.FirstOrDefault(e => e.id == id) ?? throw new BuildFailedException("Source battlefield missing: " + id);

        private static WarSandboxUnitTemplateEntry[] MergeTemplates(params WarSandboxUnitTemplateEntry[][] sets)
        {
            var merged = new List<WarSandboxUnitTemplateEntry>();
            foreach (var set in sets)
                foreach (var t in set)
                {
                    var same = merged.FirstOrDefault(m => m.templateId == t.templateId || m.config == t.config);
                    if (same == null) { merged.Add(new WarSandboxUnitTemplateEntry { templateId = t.templateId, revision = t.revision, config = t.config }); continue; }
                    if (same.templateId != t.templateId || same.config != t.config || same.revision != t.revision)
                        throw new BuildFailedException("Conflicting unit template: " + t.templateId + " / " + same.templateId);
                }
            return merged.ToArray();
        }

        private static Texture2D ImportPreview(string id)
        {
            // Earlier official previews are tracked assets: reuse them byte for byte; new battlefields come from the picked captures.
            string source = PreviousRoots.Select(r => r + "/Previews/" + id + ".png").FirstOrDefault(File.Exists) ?? Path.Combine(PreviewSource, id + ".png");
            if (!File.Exists(source)) { Debug.LogWarning("OFFICIAL_ROSTER no preview for " + id); return null; }
            string target = Root + "/Previews/" + id + ".png";
            if (File.Exists(target)) throw new BuildFailedException("Refusing to overwrite preview: " + target);
            File.Copy(source, target); AssetDatabase.ImportAsset(target);
            var importer = (TextureImporter)AssetImporter.GetAtPath(target);
            importer.mipmapEnabled = false; importer.maxTextureSize = 1024; importer.SaveAndReimport();
            return Load<Texture2D>(target);
        }

        private static T Load<T>(string path) where T : Object => AssetDatabase.LoadAssetAtPath<T>(path) ?? throw new BuildFailedException("Missing asset: " + path);
    }
}
