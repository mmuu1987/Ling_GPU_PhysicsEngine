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
        /// <summary>Current official content: v7 curates v6 and adds truthful static unit previews. All earlier assets and IDs remain read-only.</summary>
        public const string Root = Parent + "/Version07";
        public const string Version01Root = Parent + "/Version01";
        public const string Version02Root = Parent + "/Version02";
        public const string Version03Root = Parent + "/Version03";
        public const string Version04Root = Parent + "/Version04";
        public const string Version05Root = Parent + "/Version05";
        public const string Version06Root = Parent + "/Version06";
        /// <summary>Earlier official versions, oldest first; their previews are reused byte for byte.</summary>
        public static readonly string[] PreviousRoots = { Version01Root, Version02Root, Version03Root, Version04Root, Version05Root, Version06Root };
        public const string CatalogPath = Root + "/Catalog.asset";
        public const string MenuScene = Root + "/LaunchMenu.unity";
        /// <summary>Collection extends batch 5 with the three author-official Platformer enemies.</summary>
        public const string SourceCatalog = "Assets/Game/PlatformerBatch6/Prepared01/Integrated/Catalog.asset";
        public const string PreviewSource = "Logs/OfficialRoster/previews";
        public const string Output01 = "Builds/OfficialRoster-20261001-01";
        public const string Output02 = "Builds/OfficialRoster-20261001-02";
        public const string Output03 = "Builds/OfficialRoster-20261001-03";
        public const string Output04 = "Builds/OfficialRoster-20261001-04";
        public const string Output05 = "Builds/OfficialRoster-20261001-05";
        public const string Output06 = "Builds/OfficialRoster-20261001-06";
        public const string Output07 = "Builds/OfficialRoster-20261001-07";
        public const string Output08 = "Builds/OfficialRoster-20261001-08";
        public const string Output09 = "Builds/OfficialRoster-20261002-01";
        public const string Output10 = "Builds/OfficialRoster-20261002-02";

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
            new[] { "troops6-crab", "蟹怪围攻", "低矮的蟹怪群以钳口近战围攻骑士；纯近战，没有额外技能。" },
            new[] { "troops6-enemy", "绿皮小怪", "绿皮小怪使用咬击与步行接敌，可在布阵中自由调整人数。" },
            new[] { "troops6-skull", "骷髅头军团", "骷髅头造型的新近战兵种，沿地面导航，不新增飞行或特殊伤害。" },
            new[] { "nonhuman2-triceratops", "三角龙冲阵", "体型庞大的三角龙投入战场。" },
            new[] { "nonhuman2-stegosaurus", "剑龙防线", "背负骨板的剑龙投入战场。" },
            new[] { "nonhuman2-spider", "巨型蜘蛛", "成群的巨型蜘蛛投入战场。" },
            new[] { "unified-regular", "自由编成 · 常规兵种", "12 种常规兵种，可在配兵布阵中自由组合双方军团。" },
            new[] { "unified-all-regular", "全兵种同场", "新旧兵种同场展示，适合挑选喜欢的兵种。" },
            new[] { "unified-large", "自由编成 · 大型兵种", "面向大型兵种的配兵战场，可编入体型较大的单位。" },
        };

        /// <summary>Earlier versions are preserved; current curation is <see cref="Prepare07"/>.</summary>
        public static void Prepare01() => throw new InvalidOperationException("Version01 is already authored and kept read-only; run Prepare07.");
        public static void Prepare02() => throw new InvalidOperationException("Version02 is already authored and kept read-only; run Prepare07.");
        public static void Prepare03() => throw new InvalidOperationException("Version03 is already authored and kept read-only; run Prepare07.");
        public static void Prepare04() => throw new InvalidOperationException("Version04 is already authored and kept read-only; run Prepare07.");

        public static void Prepare05() => throw new InvalidOperationException("Version05 is already authored and kept read-only; run Prepare07.");

        public static void Prepare06() => throw new InvalidOperationException("Version06 is kept read-only; run Prepare07.");
        public static void Prepare07() => OfficialRosterQualityBuilder.Prepare07();

        private static void RejectHistoricalBuild(string outputPath) =>
            throw new InvalidOperationException("Historical build target is read-only: " + outputPath + ". Run Build10 for the current Version07 quality package.");

        public static void Build01() => RejectHistoricalBuild(Output01);
        public static void Build02() => RejectHistoricalBuild(Output02);
        public static void Build03() => RejectHistoricalBuild(Output03);
        public static void Build04() => RejectHistoricalBuild(Output04);
        public static void Build05() => RejectHistoricalBuild(Output05);
        public static void Build06() => RejectHistoricalBuild(Output06);
        public static void Build07() => RejectHistoricalBuild(Output07);
        public static void Build08() => RejectHistoricalBuild(Output08);
        public static void Build09() => RejectHistoricalBuild(Output09);
        /// <summary>Fresh quality package only; cannot overwrite Version06 or an existing output.</summary>
        public static void Build10() => OfficialRosterQualityBuilder.Build07();

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

