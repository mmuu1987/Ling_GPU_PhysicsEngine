using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace MassEngine.Game.EditorTools
{
    /// <summary>
    /// 远程覆盖射击 (2026-10-14, option B / 丙, user approved "开始做"): every player-facing official ranged unit
    /// except the dragons gets range 500 m, projectile speed 75 m/s and projectile lifetime 10 s.
    /// Writes CombatConfig assets referenced by the main Catalog (other catalogs share them).
    /// Assets shared with 对照 / 密集方阵 units are skipped. Every touched file is copied to outputs/Chal/bak/area first.
    /// Idempotent (absolute values; the first backup is kept).
    /// </summary>
    public static class BalanceAreaFireApply
    {
        const string CatalogPath = "Assets/Game/Scenes/Catalog.asset";
        const string Bak = "outputs/Chal/bak/area";
        const float Range = 500f, Speed = 75f, Lifetime = 10f;

        [MenuItem("Tools/挑战模式/远程覆盖射击：写入射程500（先备份，可重复执行）")]
        public static void Apply()
        {
            var log = new StringBuilder();
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Write("ERROR playing"); return; }
            var catalog = AssetDatabase.LoadAssetAtPath<WarSandboxBattlefieldCatalog>(CatalogPath);
            var users = new Dictionary<string, List<string>>();
            foreach (var g in AssetDatabase.FindAssets("t:UnitTypeConfig"))
            {
                var u = AssetDatabase.LoadAssetAtPath<UnitTypeConfig>(AssetDatabase.GUIDToAssetPath(g));
                if (u == null || u.combatConfig == null) continue;
                string p = AssetDatabase.GetAssetPath(u.combatConfig);
                if (!users.TryGetValue(p, out var l)) users[p] = l = new List<string>();
                l.Add(u.unitTypeName);
            }
            var done = new HashSet<string>();
            int written = 0;
            foreach (var t in catalog.templates)
            {
                if (!BalanceSpeedApply.IsPlayer(t)) continue;
                var c = t.config.combatConfig;
                if (c == null || c.projectileRange <= 0.01f) continue;
                if (t.templateId.Contains("dragon")) { log.AppendLine("KEEP " + t.templateId + " (dragon, not in the approved list)"); continue; }
                string path = AssetDatabase.GetAssetPath(c);
                bool shared = false;
                if (users.TryGetValue(path, out var names))
                    foreach (var n in names) if (n.Contains("对照") || n.Contains("密集方阵")) { shared = true; log.AppendLine("CONFLICT " + t.templateId + " " + path + " shared with " + n); }
                if (shared) { log.AppendLine("SKIP " + t.templateId); continue; }
                if (done.Add(path))
                {
                    Directory.CreateDirectory(Bak);
                    string dst = Path.Combine(Bak, path.Replace('/', '~') + ".bak");
                    if (!File.Exists(dst)) File.Copy(path, dst);
                }
                log.AppendLine(t.templateId + " (" + t.config.unitTypeName + "): range " + c.projectileRange + "->" + Range +
                    " speed " + c.projectileSpeed + "->" + Speed + " life " + c.projectileMaxLifetime + "->" + Lifetime + "  " + path);
                c.projectileRange = Range; c.projectileSpeed = Speed; c.projectileMaxLifetime = Lifetime;
                EditorUtility.SetDirty(c);
                written++;
            }
            AssetDatabase.SaveAssets();
            log.AppendLine("DONE templates=" + written + " assets=" + done.Count);
            Write(log.ToString());
        }

        static void Write(string s)
        {
            Directory.CreateDirectory("Logs/Chal");
            File.WriteAllText("Logs/Chal/areafire.txt", s);
        }
    }
}
