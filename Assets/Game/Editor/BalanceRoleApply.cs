using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace MassEngine.Game.EditorTools
{
    /// <summary>
    /// 兵种平衡 (2026-10-14, user approved role table v1): writes the role stats into the official unit assets
    /// (CombatConfig / MovementConfig; AnimationConfig.moveReferenceSpeed raised so run playback stays at or below 2x),
    /// and, separately and repeatably, the calibrated per-template price corrections into Catalog.asset.
    /// Every touched file is copied to outputs/Chal/bak/roles1 first. Assets shared with 对照 / 密集方阵 units are skipped.
    /// </summary>
    public static class BalanceRoleApply
    {
        const string CatalogPath = "Assets/Game/Scenes/Catalog.asset";
        const string Bak = "outputs/Chal/bak/roles1";
        const string Done = Bak + "/ROLES_DONE.txt";
        static readonly CultureInfo IC = CultureInfo.InvariantCulture;

        static readonly Dictionary<string, Dictionary<string, float>> Roles = new Dictionary<string, Dictionary<string, float>>
        {
            { "roster-knight", new Dictionary<string, float> { {"MaxHp", 260.0f}, {"AttackDamage", 12.0f}, {"AttackInterval", 1.0f}, {"MaxSpeed", 9.0f} } },
            { "roster-knight-legion", new Dictionary<string, float> { {"MaxHp", 260.0f}, {"AttackDamage", 12.0f}, {"AttackInterval", 1.0f}, {"MaxSpeed", 9.0f} } },
            { "roster-skeleton-warrior", new Dictionary<string, float> { {"MaxHp", 220.0f}, {"AttackDamage", 15.0f}, {"AttackInterval", 1.1f}, {"MaxSpeed", 9.0f} } },
            { "roster-yeti", new Dictionary<string, float> { {"MaxHp", 280.0f}, {"AttackDamage", 16.0f}, {"AttackInterval", 1.1f} } },
            { "roster-orcskull", new Dictionary<string, float> { {"MaxHp", 300.0f}, {"AttackDamage", 20.0f}, {"AttackInterval", 1.0f} } },
            { "roster-platformer-crab", new Dictionary<string, float> { {"MaxHp", 220.0f}, {"AttackDamage", 12.0f}, {"AttackInterval", 0.8f} } },
            { "roster-robot-expressive", new Dictionary<string, float> { {"MaxHp", 240.0f}, {"AttackDamage", 20.0f}, {"AttackInterval", 1.2f}, {"MaxSpeed", 9.0f} } },
            { "roster-skeleton-rogue", new Dictionary<string, float> { {"MaxHp", 80.0f}, {"AttackDamage", 9.0f}, {"AttackInterval", 0.55f}, {"MaxSpeed", 15.0f} } },
            { "roster-ninja", new Dictionary<string, float> { {"MaxHp", 70.0f}, {"AttackDamage", 12.0f}, {"AttackInterval", 0.6f}, {"MaxSpeed", 16.0f} } },
            { "roster-frog", new Dictionary<string, float> { {"MaxHp", 70.0f}, {"AttackDamage", 10.0f}, {"AttackInterval", 0.65f}, {"MaxSpeed", 14.0f} } },
            { "roster-monkroose", new Dictionary<string, float> { {"MaxHp", 100.0f}, {"AttackDamage", 11.0f}, {"AttackInterval", 0.7f}, {"MaxSpeed", 14.0f} } },
            { "roster-legacy-skeleton", new Dictionary<string, float> { {"MaxHp", 70.0f}, {"AttackDamage", 8.0f}, {"AttackInterval", 1.0f} } },
            { "roster-mushroom", new Dictionary<string, float> { {"MaxHp", 60.0f}, {"AttackDamage", 7.0f}, {"AttackInterval", 0.9f}, {"MaxSpeed", 8.0f} } },
            { "roster-orc", new Dictionary<string, float> { {"MaxHp", 160.0f}, {"AttackDamage", 18.0f}, {"AttackInterval", 0.9f}, {"MaxSpeed", 12.0f}, {"ChargeDamageMultiplier", 1.6f} } },
            { "roster-tribal", new Dictionary<string, float> { {"MaxHp", 120.0f}, {"AttackDamage", 14.0f}, {"AttackInterval", 0.85f}, {"MaxSpeed", 12.0f}, {"ChargeDamageMultiplier", 1.5f} } },
            { "launch-standard-unit-1", new Dictionary<string, float> { {"MaxHp", 80.0f}, {"ProjectileRange", 30.0f}, {"TargetAcquireRadius", 32.0f}, {"ProjectileSpeed", 30.0f} } },
            { "roster-female", new Dictionary<string, float> { {"MaxHp", 80.0f}, {"ProjectileRange", 30.0f}, {"TargetAcquireRadius", 32.0f}, {"ProjectileSpeed", 30.0f} } },
            { "roster-unitychan", new Dictionary<string, float> { {"MaxHp", 80.0f}, {"ProjectileRange", 30.0f}, {"TargetAcquireRadius", 32.0f}, {"ProjectileSpeed", 30.0f}, {"ProjectileHitRadius", 0.08f} } },
            { "launch-standard-unit-3", new Dictionary<string, float> { {"MaxHp", 80.0f}, {"AttackDamage", 9.0f}, {"AttackInterval", 1.2f}, {"ProjectileRange", 24.0f}, {"TargetAcquireRadius", 26.0f}, {"ProjectileSpeed", 22.0f}, {"ProjectileSplashRadius", 1.5f} } },
            { "roster-skeleton-mage", new Dictionary<string, float> { {"MaxHp", 80.0f}, {"AttackDamage", 9.0f}, {"AttackInterval", 1.2f}, {"ProjectileRange", 24.0f}, {"TargetAcquireRadius", 26.0f}, {"ProjectileSpeed", 22.0f}, {"ProjectileSplashRadius", 1.5f} } },
            { "roster-ranger", new Dictionary<string, float> { {"MaxHp", 80.0f}, {"AttackDamage", 16.0f}, {"AttackInterval", 1.5f}, {"ProjectileRange", 40.0f}, {"TargetAcquireRadius", 42.0f}, {"ProjectileSpeed", 38.0f} } },
            { "roster-cactoro", new Dictionary<string, float> { {"MaxHp", 100.0f}, {"AttackDamage", 14.0f}, {"AttackInterval", 0.7f}, {"ProjectileRange", 22.0f}, {"TargetAcquireRadius", 24.0f}, {"ProjectileSpeed", 45.0f}, {"MaxSpeed", 9.0f} } },
            { "roster-giant-alien", new Dictionary<string, float> { {"ProjectileRange", 18.0f}, {"TargetAcquireRadius", 22.0f}, {"ProjectileSpeed", 30.0f} } },
            { "roster-dragon", new Dictionary<string, float> { {"ProjectileSpeed", 30.0f} } },
            { "roster-dragon-evolved", new Dictionary<string, float> { {"ProjectileSpeed", 30.0f} } },
            { "roster-mounted-knight", new Dictionary<string, float> { {"MaxHp", 450.0f}, {"MaxSpeed", 18.0f} } },
            { "roster-large-wolf", new Dictionary<string, float> { {"MaxHp", 900.0f}, {"AttackDamage", 40.0f}, {"AttackInterval", 1.0f}, {"MaxSpeed", 14.0f}, {"ChargeDamageMultiplier", 1.5f} } },
            { "roster-large-bull", new Dictionary<string, float> { {"MaxHp", 1200.0f}, {"AttackDamage", 45.0f}, {"MaxSpeed", 10.0f}, {"ChargeDamageMultiplier", 3.0f} } },
            { "roster-triceratops", new Dictionary<string, float> { {"MaxHp", 1400.0f}, {"AttackDamage", 50.0f}, {"AttackInterval", 1.0f}, {"MaxSpeed", 8.0f}, {"ChargeDamageMultiplier", 2.5f} } },
            { "roster-stegosaurus", new Dictionary<string, float> { {"MaxHp", 1800.0f}, {"AttackDamage", 45.0f}, {"AttackInterval", 1.4f}, {"MaxSpeed", 6.0f} } },
            { "roster-spider", new Dictionary<string, float> { {"MaxHp", 900.0f}, {"AttackDamage", 35.0f}, {"MaxSpeed", 12.0f} } },
        };

        static string RoleKey(string id)
        {
            if (id == "skeleton-warrior") return "roster-skeleton-warrior";
            if (id.StartsWith("launch-") && id.EndsWith("-unit-1")) return "launch-standard-unit-1";
            if (id.StartsWith("launch-") && (id.EndsWith("-unit-3") || id.EndsWith("-unit-5"))) return "launch-standard-unit-3";
            return id;
        }

        static Dictionary<string, List<string>> Users()
        {
            var users = new Dictionary<string, List<string>>();
            foreach (var g in AssetDatabase.FindAssets("t:UnitTypeConfig"))
            {
                var u = AssetDatabase.LoadAssetAtPath<UnitTypeConfig>(AssetDatabase.GUIDToAssetPath(g));
                if (u == null) continue;
                foreach (Object sub in new Object[] { u.movementConfig, u.combatConfig, u.animationConfig })
                {
                    if (sub == null) continue;
                    string p = AssetDatabase.GetAssetPath(sub);
                    if (!users.TryGetValue(p, out var l)) users[p] = l = new List<string>();
                    l.Add(u.unitTypeName);
                }
            }
            return users;
        }

        static bool Backup(Object o, StringBuilder log, HashSet<string> done)
        {
            string p = AssetDatabase.GetAssetPath(o);
            if (string.IsNullOrEmpty(p)) return false;
            if (done.Add(p))
            {
                Directory.CreateDirectory(Bak);
                string dst = Path.Combine(Bak, p.Replace('/', '~') + ".bak");
                if (!File.Exists(dst)) File.Copy(p, dst);
                log.AppendLine("BAK " + p);
            }
            return true;
        }

        [MenuItem("Tools/挑战模式/写入兵种定位数值（先备份，可重复执行）")]
        public static void ApplyRoles()
        {
            var log = new StringBuilder();
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Write("roles.txt", "ERROR playing"); return; }
            // Idempotent (absolute values; backups keep the first original), so a re-run is safe.
            var catalog = AssetDatabase.LoadAssetAtPath<WarSandboxBattlefieldCatalog>(CatalogPath);
            var users = Users();
            var backed = new HashSet<string>();
            int written = 0;
            foreach (var t in catalog.templates)
            {
                if (!BalanceSpeedApply.IsPlayer(t)) continue;
                if (!Roles.TryGetValue(RoleKey(t.templateId), out var stats)) continue;
                var u = t.config; var c = u.combatConfig; var m = u.movementConfig; var a = u.animationConfig;
                bool bad = false;
                bool movesSpeed = stats.ContainsKey("MaxSpeed");
                foreach (Object sub in movesSpeed ? new Object[] { c, m, a } : new Object[] { c })
                {
                    if (sub == null) continue;
                    if (users.TryGetValue(AssetDatabase.GetAssetPath(sub), out var l))
                        foreach (var n in l) if (n.Contains("对照") || n.Contains("密集方阵")) { bad = true; log.AppendLine("CONFLICT " + t.templateId + " " + AssetDatabase.GetAssetPath(sub) + " shared with " + n); }
                }
                if (bad || c == null || m == null) { log.AppendLine("SKIP " + t.templateId); continue; }
                var sb = new StringBuilder(t.templateId + ":");
                foreach (var kv in stats)
                {
                    float v = kv.Value;
                    switch (kv.Key)
                    {
                        case "MaxHp": Backup(c, log, backed); sb.Append(" hp " + c.maxHp + "->" + Mathf.RoundToInt(v)); c.maxHp = Mathf.RoundToInt(v); break;
                        case "AttackDamage": Backup(c, log, backed); sb.Append(" dmg " + c.attackDamage + "->" + Mathf.RoundToInt(v)); c.attackDamage = Mathf.RoundToInt(v); break;
                        case "AttackInterval": Backup(c, log, backed); sb.Append(" int " + c.attackInterval + "->" + v); c.attackInterval = v; break;
                        case "ChargeDamageMultiplier": Backup(c, log, backed); sb.Append(" charge " + c.chargeDamageMultiplier + "->" + v); c.chargeDamageMultiplier = v; break;
                        case "ProjectileRange": Backup(c, log, backed); sb.Append(" range " + c.projectileRange + "->" + v); c.projectileRange = v; break;
                        case "ProjectileSpeed": Backup(c, log, backed); sb.Append(" projSpeed " + c.projectileSpeed + "->" + v); c.projectileSpeed = v; break;
                        case "ProjectileSplashRadius": Backup(c, log, backed); sb.Append(" splash " + c.projectileSplashRadius + "->" + v); c.projectileSplashRadius = v; break;
                        case "ProjectileHitRadius": Backup(c, log, backed); sb.Append(" hitRadius " + c.projectileHitRadius + "->" + v); c.projectileHitRadius = v; break;
                        case "TargetAcquireRadius": Backup(c, log, backed); sb.Append(" acquire " + c.targetAcquireRadius + "->" + v); c.targetAcquireRadius = v; break;
                        case "MaxSpeed":
                            Backup(m, log, backed); sb.Append(" speed " + m.maxSpeed + "->" + v);
                            if (a != null)
                            {
                                float reference = a.moveReferenceSpeed > 0f ? a.moveReferenceSpeed : m.maxSpeed;
                                float need = v / Mathf.Max(0.5f, a.moveAnimationSpeedMax);
                                if (need > reference + 1e-4f) { Backup(a, log, backed); sb.Append(" animRef " + a.moveReferenceSpeed + "->" + need); a.moveReferenceSpeed = need; EditorUtility.SetDirty(a); }
                            }
                            m.maxSpeed = v; EditorUtility.SetDirty(m); break;
                        default: log.AppendLine("WARN unknown stat " + kv.Key); break;
                    }
                }
                EditorUtility.SetDirty(c);
                written++;
                log.AppendLine(sb.ToString());
            }
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory(Bak);
            File.AppendAllText(Done, System.DateTime.Now.ToString("s") + " templates=" + written + "\n");
            log.AppendLine("DONE templates=" + written);
            Write("roles.txt", log.ToString());
        }

        /// <summary>Repeatable: Logs/Chal/price_scales.txt lines "templateId=scale" -> Catalog.asset priceScale.</summary>
        [MenuItem("Tools/挑战模式/写入单价修正系数（读 Logs/Chal/price_scales.txt）")]
        public static void ApplyScales()
        {
            var log = new StringBuilder();
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Write("scales.txt", "ERROR playing"); return; }
            string src = "Logs/Chal/price_scales.txt";
            if (!File.Exists(src)) { Write("scales.txt", "ERROR missing " + src); return; }
            var want = new Dictionary<string, float>();
            foreach (var raw in File.ReadAllLines(src))
            {
                int eq = raw.IndexOf('=');
                if (eq > 0 && float.TryParse(raw.Substring(eq + 1).Trim(), NumberStyles.Float, IC, out float v) && v >= 0.05f && v <= 20f)
                    want[raw.Substring(0, eq).Trim()] = v;
            }
            var catalog = AssetDatabase.LoadAssetAtPath<WarSandboxBattlefieldCatalog>(CatalogPath);
            Backup(catalog, log, new HashSet<string>());
            var resolver = new WarSandboxStatResolver(catalog, null, null);
            int n = 0;
            foreach (var t in catalog.templates)
            {
                if (t == null || !want.TryGetValue(t.templateId, out float s)) continue;
                int before = WarSandboxUnitCost.UnitPrice(resolver, t.config);
                float old = t.priceScale; t.priceScale = s; n++;
                log.AppendLine(t.templateId + " scale " + old.ToString(IC) + "->" + s.ToString(IC) + " price " + before + "->" + WarSandboxUnitCost.UnitPrice(resolver, t.config));
            }
            foreach (var id in want.Keys) { bool found = false; foreach (var t in catalog.templates) if (t != null && t.templateId == id) found = true; if (!found) log.AppendLine("WARN unknown id " + id); }
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            log.AppendLine("DONE scales=" + n);
            Write("scales.txt", log.ToString());
        }

        /// <summary>Repeatable: copy priceScale from the main catalog to every other catalog
        /// (same templateId first, otherwise same UnitTypeConfig). Each changed catalog is backed up once.</summary>
        [MenuItem("Tools/挑战模式/同步单价修正系数到其他目录（以主目录为准）")]
        public static void SyncScales()
        {
            var log = new StringBuilder();
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Write("scalesync.txt", "ERROR playing"); return; }
            var main = AssetDatabase.LoadAssetAtPath<WarSandboxBattlefieldCatalog>(CatalogPath);
            var byId = new Dictionary<string, float>();
            var byConfig = new Dictionary<Object, float>();
            foreach (var t in main.templates)
            {
                if (t == null) continue;
                byId[t.templateId] = t.priceScale;
                if (t.config != null && !byConfig.ContainsKey(t.config)) byConfig[t.config] = t.priceScale;
            }
            var backed = new HashSet<string>();
            int changed = 0, catalogs = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:WarSandboxBattlefieldCatalog"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path == CatalogPath) continue;
                var cat = AssetDatabase.LoadAssetAtPath<WarSandboxBattlefieldCatalog>(path);
                if (cat == null || cat.templates == null) continue;
                bool dirty = false;
                foreach (var t in cat.templates)
                {
                    if (t == null) continue;
                    float s; string how;
                    if (byId.TryGetValue(t.templateId, out s)) how = "id";
                    else if (t.config != null && byConfig.TryGetValue(t.config, out s)) how = "config";
                    else continue;
                    if (Mathf.Approximately(t.priceScale, s)) continue;
                    if (!dirty) { Backup(cat, log, backed); dirty = true; }
                    log.AppendLine(path + " " + t.templateId + " (" + how + ") " + t.priceScale.ToString(IC) + "->" + s.ToString(IC));
                    t.priceScale = s; changed++;
                }
                if (dirty) { EditorUtility.SetDirty(cat); catalogs++; }
            }
            AssetDatabase.SaveAssets();
            log.AppendLine("DONE catalogs=" + catalogs + " templates=" + changed);
            Write("scalesync.txt", log.ToString());
        }

        static void Write(string name, string s)
        {
            Directory.CreateDirectory("Logs/Chal");
            File.WriteAllText(Path.Combine("Logs/Chal", name), s, new UTF8Encoding(false));
            Debug.Log("[BalanceRoleApply] Logs/Chal/" + name);
        }
    }
}
