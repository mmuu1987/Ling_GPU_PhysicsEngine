using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace MassEngine.Game.EditorTools
{
    /// <summary>兵种平衡 (2026-10-14): which assets the main catalog templates use and who else shares them.</summary>
    public static class BalanceInventory
    {
        [MenuItem("Tools/挑战模式/兵种资源清单")]
        public static void Run()
        {
            var users = new Dictionary<string, List<string>>();
            foreach (var g in AssetDatabase.FindAssets("t:UnitTypeConfig"))
            {
                string up = AssetDatabase.GUIDToAssetPath(g);
                var u = AssetDatabase.LoadAssetAtPath<UnitTypeConfig>(up);
                if (u == null) continue;
                foreach (Object sub in new Object[] { u.movementConfig, u.combatConfig, u.animationConfig })
                {
                    if (sub == null) continue;
                    string sp = AssetDatabase.GetAssetPath(sub);
                    if (!users.TryGetValue(sp, out var l)) users[sp] = l = new List<string>();
                    l.Add(up);
                }
            }
            var catalogUsers = new Dictionary<string, List<string>>();
            foreach (var g in AssetDatabase.FindAssets("t:WarSandboxBattlefieldCatalog"))
            {
                string cp = AssetDatabase.GUIDToAssetPath(g);
                var c = AssetDatabase.LoadAssetAtPath<WarSandboxBattlefieldCatalog>(cp);
                foreach (var t in c.templates)
                {
                    if (t == null || t.config == null) continue;
                    string up = AssetDatabase.GetAssetPath(t.config);
                    if (!catalogUsers.TryGetValue(up, out var l)) catalogUsers[up] = l = new List<string>();
                    if (!l.Contains(cp)) l.Add(cp);
                }
            }
            var catalog = AssetDatabase.LoadAssetAtPath<WarSandboxBattlefieldCatalog>("Assets/Game/Scenes/Catalog.asset");
            var sb = new StringBuilder();
            foreach (var t in catalog.templates)
            {
                if (t == null || t.config == null) continue;
                var u = t.config;
                bool player = !t.hiddenFromSelection && !u.unitTypeName.Contains("对照") && !u.unitTypeName.Contains("密集方阵");
                string up = AssetDatabase.GetAssetPath(u);
                string S(Object o) { if (o == null) return "null"; string p = AssetDatabase.GetAssetPath(o); int n = users.TryGetValue(p, out var l) ? l.Count : 0; return p + " [units=" + n + "]"; }
                sb.AppendLine((player ? "P " : "- ") + t.templateId + " | " + u.unitTypeName + " | unit=" + up + " [catalogs=" + (catalogUsers.TryGetValue(up, out var cl) ? cl.Count : 0) + "]");
                sb.AppendLine("    move=" + S(u.movementConfig) + " maxSpeed=" + (u.movementConfig ? u.movementConfig.maxSpeed : -1));
                sb.AppendLine("    combat=" + S(u.combatConfig) + (u.combatConfig ? " projSpeed=" + u.combatConfig.projectileSpeed + " grav=" + u.combatConfig.projectileGravity + " acq=" + u.combatConfig.targetAcquireRadius + " chargeMin=" + u.combatConfig.chargeMinSpeedFraction : ""));
                sb.AppendLine("    anim=" + S(u.animationConfig) + (u.animationConfig ? " moveRef=" + u.animationConfig.moveReferenceSpeed + " animMax=" + u.animationConfig.moveAnimationSpeedMax + " start=" + u.animationConfig.moveStartSpeed : ""));
            }
            Directory.CreateDirectory("Logs/Chal");
            File.WriteAllText("Logs/Chal/inventory.txt", sb.ToString(), new UTF8Encoding(false));
            Debug.Log("[BalanceInventory] Logs/Chal/inventory.txt");
        }
    }
}
