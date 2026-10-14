using System.Collections.Generic;
using Tk = MassEngine.Game.Ui.WarSandboxUiToolkit;

namespace MassEngine.Game
{
    /// <summary>
    /// Challenge mode (2026-10-14), new UI only: the header button cycles 自由 -> 只指挥 each army -> 自由.
    /// The rule lives in the draft (one Undo step, saved with plans); the draft itself rejects edits to locked armies.
    /// </summary>
    public sealed partial class WarSandboxDeploymentHUD
    {
        private void CycleChallenge()
        {
            var draft = deployment != null ? deployment.Draft : null;
            if (draft == null) return;
            var ids = ArmyIds(); ids.Sort();
            var rules = draft.Rules;
            if (!rules.challengeEnabled)
            {
                if (ids.Count == 0) return;
                int start = draft.Count > 0 ? draft[selected].teamId : ids[0];
                draft.SetChallenge(true, start);
            }
            else
            {
                int k = ids.IndexOf(rules.challengeTeam);
                if (k >= 0 && k + 1 < ids.Count) draft.SetChallenge(true, ids[k + 1]);
                else draft.SetChallenge(false, 0);
            }
            inputError = null;
            RefreshAfterUiChange();
            nextUiRefresh = 0;
        }

        private void UpdateChallengeButton()
        {
            var draft = deployment != null ? deployment.Draft : null;
            if (dpChallenge == null || draft == null) return;
            var rules = draft.Rules;
            dpChallenge.text = rules.challengeEnabled
                ? "挑战：只指挥" + WarSandboxBattleController.DefaultArmyName(rules.challengeTeam)
                : "指挥：自由";
            dpChallenge.tooltip = "点击切换：自由 → 只指挥某一军（挑战模式）→ 自由。\n挑战模式下其他军团由电脑按默认命令作战，编成锁定。";
            DpOnState(dpChallenge, rules.challengeEnabled);
            dpArmyAdd?.SetEnabled(!rules.challengeEnabled);
        }

        private string ChallengeSuffix(int team)
        {
            var draft = deployment != null ? deployment.Draft : null;
            if (draft == null || !draft.Rules.challengeEnabled) return "";
            return draft.IsTeamLocked(team) ? " · 电脑" : " · 你指挥";
        }
    }
}
