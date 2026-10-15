using System.Collections.Generic;
using UnityEngine;

namespace MassEngine.UiPrototype
{
    /// <summary>Mock data only. Nothing here is read from the game.</summary>
    static class ProtoData
    {
        public static readonly Color Atk = new Color(0.933f, 0.353f, 0.235f);
        public static readonly Color Def = new Color(0.239f, 0.557f, 0.949f);

        public sealed class Op
        {
            public string group, code, slug, name, mode, modeEn, terrain, terrainEn, desc;
            public int legions, troops, seed;
            public bool isNew;
            public string[] units, objectives;
        }

        public static readonly Op[] Ops =
        {
            new Op { group = "首发战役", code = "ANNIHILATION", slug = "FOREST-HILLS-1024", name = "森林丘陵 · 正面决战", mode = "歼灭战", modeEn = "ANNIHILATION", terrain = "丘陵林地", terrainEn = "FOREST HILLS", legions = 2, troops = 109618, seed = 11,
                desc = "两支主力在林间丘陵正面相遇。攻方需要穿过中央缓坡，守方依托高地和林线组织纵深防御。适合测试大规模近战接触与侧翼迂回。",
                units = new[] { "长剑步兵", "长弓手", "盾卫", "弩手" }, objectives = new[] { "歼灭全部敌军", "攻方损失低于 60%", "限时 15:00" } },
            new Op { group = "首发战役", code = "ENVELOPMENT", slug = "GREEN-PLAINS-1024", name = "翠绿平原 · 两翼包抄", mode = "歼灭战", modeEn = "ANNIHILATION", terrain = "开阔平原", terrainEn = "OPEN PLAINS", legions = 4, troops = 64000, seed = 5,
                desc = "开阔地带没有掩护，骑兵与远程的配合决定胜负。攻方分为中军与两翼，尝试在守方完成转向前完成合围。",
                units = new[] { "骑士", "长剑步兵", "长弓手", "盾卫" }, objectives = new[] { "歼灭全部敌军", "两翼同时接敌", "限时 12:00" } },
            new Op { group = "首发战役", code = "STRONGPOINT", slug = "AUTUMN-ROAD", name = "金秋林地 · 暖叶古道", mode = "据点战", modeEn = "STRONGPOINT", terrain = "林地古道", terrainEn = "WOODLAND ROAD", legions = 2, troops = 3048, seed = 19,
                desc = "小规模遭遇战。古道中段的据点是唯一的补给线，控制据点 120 秒即可获胜。",
                units = new[] { "长剑步兵", "弩手" }, objectives = new[] { "占领中央据点 120 秒", "或歼灭全部敌军" } },
            new Op { group = "首发战役", code = "PASS DEFENSE", slug = "MOUNTAIN-PASS", name = "山地隘口 · 据守", mode = "据点战", modeEn = "STRONGPOINT", terrain = "高山隘口", terrainEn = "MOUNTAIN PASS", legions = 3, troops = 24600, seed = 31,
                desc = "守方扼守狭窄隘口，攻方兵力占优但只能分批通过。测试狭窄地形下的拥挤与推挤表现。",
                units = new[] { "盾卫", "弩手", "长剑步兵", "雪原巨人" }, objectives = new[] { "守住隘口 10:00", "隘口失守即失败" } },
            new Op { group = "首发战役", code = "RIVER FORD", slug = "RIVER-FORD", name = "河谷渡口", mode = "歼灭战", modeEn = "ANNIHILATION", terrain = "河谷", terrainEn = "RIVER VALLEY", legions = 4, troops = 48000, seed = 43,
                desc = "两条浅滩是唯一的渡河点。双方需要在渡口两侧争夺立足点。",
                units = new[] { "长剑步兵", "长弓手", "骑士", "盾卫" }, objectives = new[] { "歼灭全部敌军", "限时 15:00" } },
            new Op { group = "新兵种", isNew = true, code = "DRAGON RAID", slug = "DRAGON-RAID", name = "巨龙来袭", mode = "歼灭战", modeEn = "ANNIHILATION", terrain = "开阔平原", terrainEn = "OPEN PLAINS", legions = 2, troops = 1212, seed = 53,
                desc = "12 头巨龙对阵千人方阵。大体型单位的占地、推挤与远程吐息同时出现。",
                units = new[] { "巨龙", "长弓手", "盾卫" }, objectives = new[] { "击落全部巨龙", "或守住阵线 08:00" } },
            new Op { group = "新兵种", isNew = true, code = "NEST", slug = "SPIDER-NEST", name = "蜘蛛巢穴", mode = "歼灭战", modeEn = "ANNIHILATION", terrain = "林地", terrainEn = "WOODLAND", legions = 2, troops = 6400, seed = 61,
                desc = "巨型蜘蛛从林地深处涌出。最大体型半径 4.8 m 的单位测试。",
                units = new[] { "巨型蜘蛛", "长剑步兵", "弩手" }, objectives = new[] { "歼灭全部敌军" } },
            new Op { group = "新兵种", isNew = true, code = "CHARGE", slug = "KNIGHT-CHARGE", name = "骑士冲锋", mode = "歼灭战", modeEn = "ANNIHILATION", terrain = "开阔平原", terrainEn = "OPEN PLAINS", legions = 2, troops = 12000, seed = 67,
                desc = "重骑兵对步兵方阵的正面冲锋。观察冲击、穿透和阵线回弹。",
                units = new[] { "骑士", "盾卫", "长矛手" }, objectives = new[] { "击溃敌方方阵" } },
            new Op { group = "新兵种", isNew = true, code = "HERD", slug = "DINO-HERD", name = "恐龙兽群", mode = "歼灭战", modeEn = "ANNIHILATION", terrain = "河谷", terrainEn = "RIVER VALLEY", legions = 3, troops = 4800, seed = 71,
                desc = "三角龙、剑龙与人类军团的三方混战。",
                units = new[] { "三角龙", "剑龙", "长剑步兵" }, objectives = new[] { "成为最后存活的一方" } },
            new Op { group = "自由编成", code = "SANDBOX", slug = "SANDBOX-1024", name = "自由沙盒 · 1024", mode = "自由", modeEn = "FREE PLAY", terrain = "平原", terrainEn = "PLAINS", legions = 2, troops = 2048, seed = 7,
                desc = "空白战场，自由配置军团、兵种和阵型。适合快速验证想法。",
                units = new[] { "任意" }, objectives = new[] { "无胜负条件" } },
            new Op { group = "自由编成", code = "STRESS", slug = "STRESS-100K", name = "十万人压测", mode = "歼灭战", modeEn = "ANNIHILATION", terrain = "平原", terrainEn = "PLAINS", legions = 2, troops = 100000, seed = 13,
                desc = "两军各 5 万人对冲，用于观察帧时间与 GPU 负载。",
                units = new[] { "长剑步兵", "盾卫" }, objectives = new[] { "歼灭全部敌军" } },
            new Op { group = "自由编成", code = "LIMIT", slug = "LIMIT-200K", name = "二十万人极限", mode = "歼灭战", modeEn = "ANNIHILATION", terrain = "丘陵林地", terrainEn = "FOREST HILLS", legions = 8, troops = 200000, seed = 17,
                desc = "八军团、二十万人的上限测试。未做大规模性能验收，仅供观察。",
                units = new[] { "全部常规兵种" }, objectives = new[] { "歼灭全部敌军" } },
        };

        public sealed class Unit
        {
            public string key, name, en, role, size, icon, tier;
            public float radius, speed, range, interval;
            public int hp, atk, def;
        }

        public static readonly Dictionary<string, Unit> Units = new Dictionary<string, Unit>
        {
            ["swordsman"] = new Unit { key = "swordsman", name = "长剑步兵", en = "SWORDSMAN", role = "近战", size = "中型", icon = "infantry", tier = "T2", radius = 0.45f, hp = 120, atk = 24, def = 18, speed = 3.2f, range = 1.5f, interval = 1.1f },
            ["archer"] = new Unit { key = "archer", name = "长弓手", en = "LONGBOWMAN", role = "远程", size = "中型", icon = "archer", tier = "T2", radius = 0.4f, hp = 80, atk = 16, def = 8, speed = 3.4f, range = 48f, interval = 2.4f },
            ["knight"] = new Unit { key = "knight", name = "骑士", en = "MOUNTED KNIGHT", role = "近战", size = "大型", icon = "cavalry", tier = "T4", radius = 3.3f, hp = 900, atk = 70, def = 45, speed = 7.5f, range = 4.35f, interval = 1.6f },
            ["dragon"] = new Unit { key = "dragon", name = "巨龙", en = "DRAGON", role = "远程", size = "巨型", icon = "beast", tier = "T5", radius = 3.4f, hp = 6000, atk = 180, def = 80, speed = 6f, range = 30f, interval = 3.2f },
            ["shield"] = new Unit { key = "shield", name = "盾卫", en = "SHIELD GUARD", role = "近战", size = "中型", icon = "infantry", tier = "T2", radius = 0.5f, hp = 160, atk = 14, def = 32, speed = 2.8f, range = 1.4f, interval = 1.2f },
            ["crossbow"] = new Unit { key = "crossbow", name = "弩手", en = "CROSSBOWMAN", role = "远程", size = "中型", icon = "archer", tier = "T2", radius = 0.4f, hp = 90, atk = 22, def = 10, speed = 3.0f, range = 40f, interval = 2.8f },
            ["yeti"] = new Unit { key = "yeti", name = "雪原巨人", en = "GIANT YETI", role = "近战", size = "巨型", icon = "beast", tier = "T5", radius = 4.1f, hp = 4200, atk = 140, def = 60, speed = 4.2f, range = 5.15f, interval = 2.2f },
        };

        public sealed class CompSeed { public int side; public string unit; public int count; public float density, aspect; public Vector2 center; public Color color; }

        public static readonly CompSeed[] Comps =
        {
            new CompSeed { side = 0, unit = "swordsman", count = 4000, density = 0.5f, aspect = 2f, center = new Vector2(264, 470), color = Atk },
            new CompSeed { side = 0, unit = "archer", count = 2400, density = 0.4f, aspect = 3f, center = new Vector2(112, 470), color = new Color(0.95f, 0.55f, 0.24f) },
            new CompSeed { side = 0, unit = "knight", count = 600, density = 0.05f, aspect = 2.5f, center = new Vector2(304, 232), color = new Color(0.86f, 0.28f, 0.4f) },
            new CompSeed { side = 0, unit = "dragon", count = 12, density = 0.05f, aspect = 2f, center = new Vector2(352, 880), color = new Color(0.98f, 0.45f, 0.35f) },
            new CompSeed { side = 1, unit = "shield", count = 5000, density = 0.6f, aspect = 2.5f, center = new Vector2(704, 640), color = Def },
            new CompSeed { side = 1, unit = "crossbow", count = 2000, density = 0.4f, aspect = 3f, center = new Vector2(872, 640), color = new Color(0.31f, 0.7f, 0.91f) },
            new CompSeed { side = 1, unit = "yeti", count = 40, density = 0.05f, aspect = 2f, center = new Vector2(896, 296), color = new Color(0.42f, 0.49f, 0.94f) },
        };

        public static readonly string[,] Keymap =
        {
            { "进攻", "A" }, { "移动", "M" }, { "防守", "H" }, { "撤退", "R" },
            { "跟随", "F" }, { "全景", "F3" }, { "暂停 / 继续", "Space" }, { "选择军团", "1 – 9" },
            { "切换军团", "Tab" }, { "镜头平移", "WASD / 方向键" }, { "取消选择", "Esc" }, { "框选", "左键拖拽" },
        };

        public static string N(int v) => v.ToString("N0");
        public static string N(float v) => Mathf.RoundToInt(v).ToString("N0");
    }
}
