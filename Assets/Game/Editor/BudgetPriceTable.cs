using System.IO;
using System.Text;
using MassEngine.Game;
using UnityEditor;
using UnityEngine;

namespace MassEngine.Game.EditorTools
{
    /// <summary>预算对战 (2026-10-14): dumps official stats of every catalog template (and, once present, its unit price).</summary>
    public static class BudgetPriceTable
    {
        [MenuItem("Tools/挑战模式/兵种数值与单价表")]
        public static void Run()
        {
            var sb = new StringBuilder();
            foreach (var guid in AssetDatabase.FindAssets("t:WarSandboxBattlefieldCatalog"))
            {
                var catalog = AssetDatabase.LoadAssetAtPath<WarSandboxBattlefieldCatalog>(AssetDatabase.GUIDToAssetPath(guid));
                if (catalog == null) continue;
                sb.AppendLine("# " + AssetDatabase.GUIDToAssetPath(guid) + " templates=" + catalog.templates.Length);
                sb.AppendLine("id\tname\thidden\tranged\thp\tdmg\tint\tatkR\tprojR\tsplash\tspeed\tcharge\tradius\tprice");
                var resolver = new WarSandboxStatResolver(catalog, null, null);
                foreach (var t in catalog.templates)
                {
                    if (t == null || t.config == null) continue;
                    string V(WarSandboxUnitStat s) { var v = resolver.Resolve(t.config, WarSandboxUnitStats.Get(s)); return v.applies ? v.effective.ToString("0.##") : "-"; }
                    sb.AppendLine(t.templateId + "\t" + t.config.unitTypeName + "\t" + (t.hiddenFromSelection ? 1 : 0) + "\t" + (WarSandboxUnitStats.IsRanged(t.config) ? 1 : 0) + "\t" +
                        V(WarSandboxUnitStat.MaxHp) + "\t" + V(WarSandboxUnitStat.AttackDamage) + "\t" + V(WarSandboxUnitStat.AttackInterval) + "\t" +
                        V(WarSandboxUnitStat.AttackRange) + "\t" + V(WarSandboxUnitStat.ProjectileRange) + "\t" + V(WarSandboxUnitStat.ProjectileSplashRadius) + "\t" +
                        V(WarSandboxUnitStat.MaxSpeed) + "\t" + V(WarSandboxUnitStat.ChargeDamageMultiplier) + "\t" + V(WarSandboxUnitStat.AgentRadius) + "\t" + Price(resolver, t.config));
                }
            }
            Directory.CreateDirectory("Logs/Chal");
            File.WriteAllText("Logs/Chal/prices.txt", sb.ToString(), new UTF8Encoding(false));
            Debug.Log("[BudgetPriceTable] written Logs/Chal/prices.txt");
        }

        private static string Price(WarSandboxStatResolver resolver, UnitTypeConfig config) =>
            WarSandboxUnitCost.UnitPrice(resolver, config) + " (" + WarSandboxUnitCost.Power(resolver, config).ToString("0.0") + ")";

        [MenuItem("Tools/挑战模式/预算逻辑自检（不进 Play）")]
        public static void SelfCheck()
        {
            var log = new StringBuilder(); int pass = 0, fail = 0;
            void Check(string name, bool ok) { log.AppendLine((ok ? "PASS " : "FAIL ") + name); if (ok) pass++; else fail++; }
            try
            {
                var catalog = AssetDatabase.LoadAssetAtPath<WarSandboxBattlefieldCatalog>("Assets/Game/Scenes/Catalog.asset");
                var sword = catalog.FindTemplate("launch-open-unit-0").config;
                var demon = catalog.FindTemplate("roster-giant-demon").config;
                var resolver = new WarSandboxStatResolver(catalog, null, null);
                Check("swordsman = 10", WarSandboxUnitCost.UnitPrice(resolver, sword) == 10);
                var rules = WarSandboxBattlefieldRules.Default;
                Check("default off, 20 万", !rules.budgetEnabled && WarSandboxUnitCost.BudgetOf(rules, 3) == 200000);
                var e0 = new WarSandboxDeploymentEntry { template = sword, teamId = 0, count = 20000, density = 1, aspect = 1 };
                var e1 = new WarSandboxDeploymentEntry { template = demon, teamId = 1, count = 100, density = 1, aspect = 1 };
                var d = new WarSandboxDeploymentDraft(new[] { e0, e1 }, rules);
                Check("off -> check ok", WarSandboxUnitCost.TryCheck(d, resolver, out _));
                Check("SetBudget on 20 万", d.SetBudget(true, 200000) && d.Rules.budgetEnabled);
                Check("cost team0 = 200000", WarSandboxUnitCost.ArmyCost(d, resolver, 0) == 200000);
                Check("exactly at budget ok", WarSandboxUnitCost.TryCheck(d, resolver, out string e));
                var more = e0; more.count = 20001; d.Set(0, more);
                bool over = !WarSandboxUnitCost.TryCheck(d, resolver, out e);
                Check("over by 10 -> " + e, over && e.StartsWith("预算") && e.Contains("超出 10 点"));
                Check("team budget 30 万", d.SetTeamBudget(0, 300000) && WarSandboxUnitCost.BudgetOf(d.Rules, 0) == 300000 && WarSandboxUnitCost.BudgetOf(d.Rules, 1) == 200000);
                Check("now ok", WarSandboxUnitCost.TryCheck(d, resolver, out _));
                Check("undo team budget", d.Undo() && !WarSandboxUnitCost.HasTeamBudget(d.Rules, 0));
                Check("rules copy deep", d.Rules.budgetTeamPoints == null || d.Rules.budgetTeamPoints != d.Rules.budgetTeamPoints);
                var on = WarSandboxBattlefieldRules.Default; on.budgetEnabled = true; on.budgetPoints = 50000; on.budgetTeamPoints = new int[8]; on.budgetTeamPoints[1] = 80000;
                var pr = WarSandboxPlanRules.From(on);
                Check("plan round trip", pr.TryToRuntime(out var back, out _) && back.budgetEnabled && back.budgetPoints == 50000 && back.budgetTeamPoints[1] == 80000);
                var bad = on; bad.budgetTeamPoints = new int[9];
                Check("validate rejects 9 teams", !bad.TryValidate(out _));
                Check("steps", WarSandboxUnitCost.Step(200000, 1) == 250000 && WarSandboxUnitCost.Step(200000, -1) == 150000);
                Check("format", WarSandboxUnitCost.Format(200000) == "20 万" && WarSandboxUnitCost.Format(250000) == "25 万" && WarSandboxUnitCost.Format(5000) == "5,000");
                var go = new GameObject("BudgetSelfCheck");
                var c = go.AddComponent<WarSandboxBattleController>();
                c.budgetEnabled = true; c.budgetPoints = 70000;
                var cap = c.CaptureBattlefieldRules();
                Check("controller capture", cap.budgetEnabled && cap.budgetPoints == 70000);
                Object.DestroyImmediate(go);
            }
            catch (System.Exception ex) { log.AppendLine("EXCEPTION " + ex); fail++; }
            log.Insert(0, "BudgetSelfCheck " + System.DateTime.Now.ToString("s") + " pass=" + pass + " fail=" + fail + "\n");
            Directory.CreateDirectory("Logs/Chal");
            File.WriteAllText("Logs/Chal/budgetcheck.txt", log.ToString(), new UTF8Encoding(false));
            Debug.Log("[BudgetSelfCheck] pass=" + pass + " fail=" + fail);
        }
    }
}
