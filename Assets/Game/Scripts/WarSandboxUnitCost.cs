using System.Collections.Generic;
using UnityEngine;

namespace MassEngine.Game
{
    /// <summary>
    /// Budget battle (2026-10-14): unit price from effective stats, army totals and the budget check.
    /// Lanchester square law: an army of N units is worth about N^2 x HP x DPS, so one unit is worth sqrt(HP x DPS).
    /// Multipliers: ranged reach (+2% per metre beyond 2 m, max x3), splash (1 + 0.35 r^2, ranged or melee), melee charge (+15% per
    /// multiplier step), speed (0.8 + 0.2 x speed / 12). Scale: the launch swordsman (140 HP, 14 per 0.9 s, 12 m/s) = 10 points. (Speed reference 6 -> 12 when all move speeds were doubled, 2026-10-14.)
    /// First draft; coefficients are meant to be tuned with equal-budget sample battles.
    /// </summary>
    public static class WarSandboxUnitCost
    {
        public const int DefaultBudget = 200000;
        public const int MaxBudget = 100000000;
        public const float ReferencePrice = 10f;
        public static readonly float ReferencePower = Mathf.Sqrt(140f * 14f / 0.9f);
        /// <summary>Header button cycle: off -> these -> off.</summary>
        public static readonly int[] CycleSteps = { 50000, 100000, 200000, 500000, 1000000, 2000000 };
        /// <summary>Per-army -/+ steps.</summary>
        public static readonly int[] Steps = { 5000, 10000, 20000, 30000, 50000, 80000, 100000, 150000, 200000, 250000, 300000,
            400000, 500000, 700000, 1000000, 1500000, 2000000, 3000000, 5000000, 10000000 };

        private static float Value(WarSandboxStatResolver resolver, UnitTypeConfig template, WarSandboxUnitStat stat, float fallback)
        {
            var v = resolver.Resolve(template, WarSandboxUnitStats.Get(stat));
            return v.applies && !float.IsNaN(v.effective) && !float.IsInfinity(v.effective) ? v.effective : fallback;
        }

        public static float Power(WarSandboxStatResolver resolver, UnitTypeConfig template)
        {
            if (template == null) return 0;
            if (resolver == null) resolver = new WarSandboxStatResolver(null, null, null);
            float hp = Mathf.Max(1f, Value(resolver, template, WarSandboxUnitStat.MaxHp, 100f));
            float damage = Mathf.Max(0f, Value(resolver, template, WarSandboxUnitStat.AttackDamage, 10f));
            float interval = Mathf.Max(0.1f, Value(resolver, template, WarSandboxUnitStat.AttackInterval, 1f));
            float speed = Mathf.Max(0f, Value(resolver, template, WarSandboxUnitStat.MaxSpeed, 12f));
            float factor = 1f;
            if (WarSandboxUnitStats.IsRanged(template))
            {
                float range = Value(resolver, template, WarSandboxUnitStat.ProjectileRange, 10f);
                float splash = Mathf.Max(0f, Value(resolver, template, WarSandboxUnitStat.ProjectileSplashRadius, 0f));
                factor *= Mathf.Clamp(1f + 0.02f * (range - 2f), 1f, 3f);
                factor *= 1f + 0.35f * splash * splash;
            }
            else
            {
                float charge = Value(resolver, template, WarSandboxUnitStat.ChargeDamageMultiplier, 1f);
                factor *= 1f + 0.15f * Mathf.Max(0f, charge - 1f);
                // Kaiju B (2026-10-15): melee splash (stomp) uses the same area rule as projectile splash.
                float meleeSplash = template.combatConfig != null ? Mathf.Max(0f, template.combatConfig.meleeSplashRadius) : 0f;
                factor *= 1f + 0.35f * meleeSplash * meleeSplash;
                // Trample (2026-10-16, cavalry): +20% per damage-multiplier step (first draft, to be tuned).
                float trample = template.combatConfig != null ? Mathf.Max(0f, template.combatConfig.trampleDamageMultiplier) : 0f;
                factor *= 1f + 0.2f * trample;
                // Charge guard: +50% at no damage taken while charging, scaled by the share it removes (first draft).
                if (trample > 0f) factor *= 1f + 0.5f * (1f - Mathf.Clamp01(template.combatConfig.trampleDamageTaken));
            }
            factor *= Mathf.Clamp(0.8f + 0.2f * speed / 12f, 0.6f, 1.6f);
            return Mathf.Sqrt(hp * damage / interval) * factor;
        }

        public static int UnitPrice(WarSandboxStatResolver resolver, UnitTypeConfig template) =>
            template == null ? 0 : Mathf.Max(1, Mathf.RoundToInt(Power(resolver, template) / ReferencePower * ReferencePrice * PriceScale(resolver, template)));

        /// <summary>Per-template correction from the catalog (equal-budget battle calibration, 2026-10-14); 1 without a catalog.</summary>
        public static float PriceScale(WarSandboxStatResolver resolver, UnitTypeConfig template) =>
            resolver != null && resolver.Catalog != null ? resolver.Catalog.PriceScaleOf(template) : 1f;

        public static bool HasTeamBudget(WarSandboxBattlefieldRules rules, int teamId)
        {
            var a = rules.budgetTeamPoints;
            return a != null && teamId >= 0 && teamId < a.Length && a[teamId] > 0;
        }

        public static int DefaultBudgetOf(WarSandboxBattlefieldRules rules) => rules.budgetPoints > 0 ? rules.budgetPoints : DefaultBudget;

        public static int BudgetOf(WarSandboxBattlefieldRules rules, int teamId) =>
            HasTeamBudget(rules, teamId) ? rules.budgetTeamPoints[teamId] : DefaultBudgetOf(rules);

        public static long ArmyCost(WarSandboxDeploymentDraft draft, WarSandboxStatResolver resolver, int teamId)
        {
            long total = 0;
            if (draft == null) return 0;
            for (int i = 0; i < draft.Count; i++)
                if (draft[i].teamId == teamId) total += (long)UnitPrice(resolver, draft[i].template) * draft[i].count;
            return total;
        }

        public static bool TryCheck(WarSandboxDeploymentDraft draft, WarSandboxStatResolver resolver, out string error)
        {
            error = null;
            if (draft == null) return true;
            var rules = draft.Rules;
            if (!rules.budgetEnabled) return true;
            var teams = new List<int>();
            for (int i = 0; i < draft.Count; i++) if (!teams.Contains(draft[i].teamId)) teams.Add(draft[i].teamId);
            teams.Sort();
            foreach (int team in teams)
            {
                long cost = ArmyCost(draft, resolver, team); int budget = BudgetOf(rules, team);
                if (cost <= budget) continue;
                error = "预算：" + WarSandboxBattleController.DefaultArmyName(team) + "超出 " + (cost - budget).ToString("N0") +
                    " 点（已用 " + cost.ToString("N0") + " / 预算 " + budget.ToString("N0") + "）。";
                return false;
            }
            return true;
        }

        public static int Step(int current, int direction)
        {
            if (direction > 0) { foreach (int s in Steps) if (s > current) return s; return Steps[Steps.Length - 1]; }
            for (int i = Steps.Length - 1; i >= 0; i--) if (Steps[i] < current) return Steps[i];
            return Steps[0];
        }

        /// <summary>200000 -> "20 万"; other values with thousands separators.</summary>
        public static string Format(long points) =>
            points >= 10000 && points % 10000 == 0 ? (points / 10000).ToString("N0") + " 万" :
            points >= 10000 && points % 1000 == 0 ? (points / 10000f).ToString("0.#") + " 万" : points.ToString("N0");
    }
}
