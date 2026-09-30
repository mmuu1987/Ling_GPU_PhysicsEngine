using UnityEngine;

namespace MassEngine.Game
{
    // Presentation only. Reads existing orders; never infers per-agent movement from an army command.
    public sealed partial class WarSandboxCommandHUD
    {
        private string SelectedArmyTitle() => controller.SelectedArmy == null
            ? "尚未选择军团" : "已选 · " + FormatTeamName(controller.selectedTeam);

        private string SelectedOrderDetail()
        {
            var army = controller.SelectedArmy;
            if (army == null || !army.hasOrder) return "先选军团，再选择下方命令\n也可按 Enter 开始默认战斗";
            var order = army.currentOrder;
            if (order.type == ArmyOrderType.Move)
                return "目标 X " + order.target.x.ToString("F0") + " · Z " + order.target.z.ToString("F0") +
                    "\n后续航点 " + Mathf.Max(0, controller.GetMoveRoutePointCount(controller.selectedTeam) - 1) + " 个 · 依次接续";
            if (order.type == ArmyOrderType.Retreat)
                return "返回出生中心附近\n目标 X " + order.target.x.ToString("F0") + " · Z " + order.target.z.ToString("F0");
            if (order.type == ArmyOrderType.Hold) return "停止主动行军\n仍可攻击范围内的敌人";
            return "主动接敌\n命令只影响当前所选军团";
        }

        private string BattleActionLabel()
        {
            if (controller.Phase == WarSandboxBattlePhase.Setup) return "开始战斗  Enter";
            if (IsTerminalPhase(controller.Phase)) return "再来一局  Enter";
            return controller.Phase == WarSandboxBattlePhase.Running ? "暂停战斗  Space" : "继续战斗  Space";
        }
        private void ToggleBattleFromUI()
        {
            if (IsPreOrPostBattle(controller.Phase)) StartOrRestartDefaultBattle();
            else controller.TogglePause();
            nextUiRefresh = 0;
        }
        private void CancelMoveTarget()
        {
            if (!awaitingMoveTarget) return;
            awaitingMoveTarget = false;
            SetFeedback("已取消选点，原命令和航点不变");
            nextUiRefresh = 0;
        }
        private string MoveTargetHint()
        {
            if (!string.IsNullOrEmpty(controller.CommandError))
                return controller.CommandError.Replace("命令被拒绝：", "") + "\n原命令保留，可重新选点或取消";
            if (controller.Phase == WarSandboxBattlePhase.Paused)
            {
                var army = controller.SelectedArmy;
                bool hasRoute = army != null && army.hasOrder && army.currentOrder.type == ArmyOrderType.Move &&
                    controller.GetMoveRoutePointCount(controller.selectedTeam) > 0;
                return hasRoute ? "点击替换会继续运行\nShift + 点击只追加，不解除暂停" : "此军团尚无移动路线\n确认目标将继续战斗（含 Shift）";
            }
            return "点击地面替换目标 · Shift + 点击追加\n没有已有路线时，将建立新路线";
        }
        private void DrawSelectedOrder(WarSandboxUGUI ui, float x, float y, float width)
        {
            ui.Panel("selection-card", new Rect(x, y, width, 100), WarSandboxUGUI.Soft);
            ui.Panel("selection-color", new Rect(x, y, 4, 100), WarSandboxTeamPalette.Resolve(controller.selectedTeam), false);
            ui.Label("selected-army", new Rect(x + 6, y + 3, width - 12, 26), SelectedArmyTitle(), 18, null, true);
            var army = controller.SelectedArmy;
            string order = army != null && army.hasOrder ? FormatOrder(army.currentOrder.type) : "等待命令";
            ui.Label("order-status", new Rect(x + 6, y + 30, width - 12, 24), "军团命令 / " + order, 15, null, true);
            ui.Label("order-detail", new Rect(x + 6, y + 53, width - 12, 44), SelectedOrderDetail(), 13, WarSandboxUGUI.Muted);
        }
    }
}
