using UnityEngine.UIElements;
using Tk = MassEngine.Game.Ui.WarSandboxUiToolkit;

namespace MassEngine.Game
{
    /// <summary>
    /// Budget battle (2026-10-14), new UI only. Header button cycles 关 -> 5/10/20/50/100/200 万 per army -> 关.
    /// The army inspector steps that army's own budget (or follows the default). Totals use WarSandboxUnitCost with the
    /// draft's effective stats; over budget shows red and blocks apply/start through WarSandboxRuntimeDeployment.ValidateDraft.
    /// </summary>
    public sealed partial class WarSandboxDeploymentHUD
    {
        private Button dpBudget, dpBudgetDown, dpBudgetUp, dpBudgetReset;
        private VisualElement dpBudgetRow;

        private void BuildBudgetControls(VisualElement r)
        {
            dpBudget = DpOn("dp-budget", CycleBudget);
            dpBudgetRow = r.Q("dp-army-budget-row");
            dpBudgetDown = DpOn("dp-army-budget-down", () => StepArmyBudget(-1));
            dpBudgetUp = DpOn("dp-army-budget-up", () => StepArmyBudget(1));
            dpBudgetReset = DpOn("dp-army-budget-reset", ResetArmyBudget);
        }

        private bool BudgetOn => deployment != null && deployment.Draft != null && deployment.Draft.Rules.budgetEnabled;

        private void AfterBudgetEdit() { inputError = null; RefreshAfterUiChange(); nextUiRefresh = 0; }

        private void CycleBudget()
        {
            var draft = deployment != null ? deployment.Draft : null;
            if (draft == null) return;
            var rules = draft.Rules;
            if (!rules.budgetEnabled) draft.SetBudget(true, WarSandboxUnitCost.CycleSteps[0]);
            else
            {
                int current = WarSandboxUnitCost.DefaultBudgetOf(rules), next = -1;
                foreach (int s in WarSandboxUnitCost.CycleSteps) if (s > current) { next = s; break; }
                if (next > 0) draft.SetBudget(true, next); else draft.SetBudget(false, current);
            }
            AfterBudgetEdit();
        }

        private int SelectedTeam() => deployment.Draft.Count > 0 ? deployment.Draft[selected].teamId : -1;

        private void StepArmyBudget(int direction)
        {
            var draft = deployment != null ? deployment.Draft : null; int team = draft != null ? SelectedTeam() : -1;
            if (team < 0 || !draft.Rules.budgetEnabled) return;
            draft.SetTeamBudget(team, WarSandboxUnitCost.Step(WarSandboxUnitCost.BudgetOf(draft.Rules, team), direction));
            AfterBudgetEdit();
        }

        private void ResetArmyBudget()
        {
            var draft = deployment != null ? deployment.Draft : null; int team = draft != null ? SelectedTeam() : -1;
            if (team < 0) return;
            draft.SetTeamBudget(team, 0);
            AfterBudgetEdit();
        }

        private void UpdateBudgetButton()
        {
            var draft = deployment != null ? deployment.Draft : null;
            if (dpBudget == null || draft == null) return;
            var rules = draft.Rules;
            dpBudget.text = rules.budgetEnabled ? "预算：每军 " + WarSandboxUnitCost.Format(WarSandboxUnitCost.DefaultBudgetOf(rules)) + " 点" : "预算：关";
            dpBudget.tooltip = "点击切换默认预算：关 → 5 万 → 10 万 → 20 万 → 50 万 → 100 万 → 200 万 → 关。\n单价按兵种数值自动计算（标准剑兵 10 点）。\n某支军可在军团页单独调整预算。超预算标红，不能应用或开战。";
            DpOnState(dpBudget, rules.budgetEnabled);
        }

        private string BudgetLine(int team, out bool over)
        {
            over = false;
            if (!BudgetOn) return "";
            var rules = deployment.Draft.Rules;
            long cost = WarSandboxUnitCost.ArmyCost(deployment.Draft, deployment.DraftResolver(), team);
            int budget = WarSandboxUnitCost.BudgetOf(rules, team);
            over = cost > budget;
            return "\n" + cost.ToString("N0") + " / " + WarSandboxUnitCost.Format(budget) + " 点" + (over ? " · 超预算" : "");
        }

        private string BudgetArmyText(int team)
        {
            if (!BudgetOn) return "";
            var rules = deployment.Draft.Rules;
            long cost = WarSandboxUnitCost.ArmyCost(deployment.Draft, deployment.DraftResolver(), team);
            int budget = WarSandboxUnitCost.BudgetOf(rules, team);
            string source = WarSandboxUnitCost.HasTeamBudget(rules, team) ? "本军单独设定" : "跟随默认";
            string state = cost > budget ? "超出 " + (cost - budget).ToString("N0") + " 点" : "剩余 " + (budget - cost).ToString("N0") + " 点";
            return "\n预算：已用 " + cost.ToString("N0") + " / " + budget.ToString("N0") + " 点（" + source + "）\n" + state;
        }

        private void UpdateBudgetRow(int team)
        {
            bool on = BudgetOn;
            Tk.Show(dpBudgetRow, on);
            if (!on) return;
            dpBudgetReset?.SetEnabled(WarSandboxUnitCost.HasTeamBudget(deployment.Draft.Rules, team));
        }

        private string BudgetUnitText(WarSandboxDeploymentEntry e)
        {
            if (!BudgetOn) return "";
            int price = WarSandboxUnitCost.UnitPrice(deployment.DraftResolver(), e.template);
            return " · " + price.ToString("N0") + " 点/人 · 共 " + ((long)price * e.count).ToString("N0") + " 点";
        }
    }
}
