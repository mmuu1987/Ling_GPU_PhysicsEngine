using UnityEngine;

namespace MassEngine.Game
{
    public sealed partial class WarSandboxCommandHUD
    {
        private bool battleToolsOpen, armyStatisticsOpen;
        private void RefreshRuntimeUI()
        {
            ResolveReferences();
            SynchronizeDetails26();
            ObserveSelection25();
            bool visible = controller != null && !WarSandboxDeploymentHUD.BlocksInput(controller) &&
                (WarSandboxSceneSession.Instance == null || !WarSandboxSceneSession.Instance.InputBlocked);
            if (!diagnosticsCaptured && controller != null && controller.manager != null)
            {
                telemetryHud = controller.manager.GetComponent<BattleTelemetryHUD>(); flowHud = controller.manager.GetComponent<FlowFieldPreviewHUD>();
                telemetryWasEnabled = telemetryHud != null && telemetryHud.enabled; flowWasEnabled = flowHud != null && flowHud.enabled; diagnosticsCaptured = true;
            }
            if (telemetryHud != null) telemetryHud.enabled = false;
            if (flowHud != null) flowHud.enabled = false;
            if (!visible) { runtimeUi?.SetVisible(false); return; }
            if (runtimeUi == null) runtimeUi = new WarSandboxUGUI(transform, "Battle HUD Canvas", 50);
            runtimeUi.SetVisible(true);
            if (Time.unscaledTime < nextUiRefresh) return;
            nextUiRefresh = Time.unscaledTime + .12f;
            var ui = runtimeUi; ui.Begin(); float w = ui.Width, h = ui.Height;
            var session = WarSandboxSceneSession.Instance;
            var editor = controller.GetComponent<WarSandboxDeploymentHUD>();
            bool setup = controller.Phase == WarSandboxBattlePhase.Setup;
            ui.Panel("battle-title-bg", new Rect(16, 12, 300, 54), WarSandboxUGUI.Surface);
            ui.Label("battle-title", new Rect(24, 14, 284, 32), session != null ? session.CurrentDisplayName : "战争沙盒", 19, WarSandboxUGUI.Ink, true);
            ui.Label("battle-subtitle", new Rect(24, 44, 284, 20), FormatPhase(controller.Phase), 12, WarSandboxUGUI.Muted);
            DrawSelectionPreviewP7(ui,w);
            if (controller.BattleResult.valid)
            { awaitingMoveTarget = false; DrawUGUIResult(ui); ui.End(); return; }
            float centerW = Mathf.Min(420, w - 580), centerX = (w - centerW) / 2;
            ui.Panel("clock-bg", new Rect(centerX, 12, centerW, 76), WarSandboxUGUI.Surface);
            ui.LabelAligned("battle-clock", new Rect(centerX + 12, 18, centerW - 24, 30), FormatBattleTime(controller.TelemetrySnapshot.battleSeconds), 24, WarSandboxUGUI.Ink, true, TextAnchor.MiddleCenter);
            DrawForceBar(ui, new Rect(centerX + 12, 52, centerW - 24, 5), 60);
            if (controller.gameMode == WarSandboxGameMode.ControlPoint)
            {
                string status = controller.IsControlPointContested ? "争夺中" : controller.ControlPointOwnerTeamId < 0 ? "尚未占领" : FormatTeamName(controller.ControlPointOwnerTeamId) + " · " + Mathf.RoundToInt(controller.ControlPointCaptureProgress * 100) + "%";
                ui.Panel("objective", new Rect(centerX, 100, centerW, 40), WarSandboxUGUI.Surface);
                ui.LabelAligned("objective-title", new Rect(centerX + 6, 102, centerW - 12, 36), "中央据点 / " + status, 16, WarSandboxUGUI.Ink, true, TextAnchor.MiddleCenter);
            }
            ui.Button("battle-tools", new Rect(16, 80, 126, 34), battleToolsOpen || helpOpen || diagnosticsOpen ? "收起工具" : "更多 / 帮助", () => { bool close = battleToolsOpen || helpOpen || diagnosticsOpen; battleToolsOpen = !close; if (close) helpOpen = diagnosticsOpen = false; });
            if (setup)
            {
                float bw = 500, bx = (w - bw) / 2;
                ui.Panel("setup-actions", new Rect(bx, h - 110, bw, 90), WarSandboxUGUI.Surface);
                ui.Label("setup-note", new Rect(bx + 14, h - 108, bw - 28, 28), PlanState28.SetupText(controller, FlowPolish23.Active(controller) ? "布阵已就绪 · 可继续修改，点击右侧开始战斗" : "战前准备 · 调整军团，或使用当前部署直接开战"), 14, WarSandboxUGUI.Muted);
                ui.Button("edit", new Rect(bx + 16, h - 68, 206, 40), "配兵与布阵", () => editor.RequestEdit(), false, editor != null);
                ui.Button("start", new Rect(bx + 238, h - 70, 246, 44), "开始战斗  Enter", ToggleBattleFromUI, true, string.IsNullOrEmpty(controller.BattlefieldRuleError));
            }
            else
            {
                float bw = Mathf.Min(760, w - 72), bx = (w - bw) / 2, by = h - 146;
                ui.Panel("command-bar", new Rect(bx, by, bw, 128), WarSandboxUGUI.Surface);
                int shown = 0; for (int i = 0; i < controller.ArmyCount; i++) if (controller.GetArmy(i) != null && controller.GetArmy(i).initialUnitCount > 0) shown++;
                // Two rows up to all eight existing engine army slots. No new grouping semantics.
                float tabW = (bw - 150) / Mathf.Min(4, Mathf.Max(1, shown)); int n = 0;
                for (int i = 0; i < controller.ArmyCount; i++)
                {
                    var army = controller.GetArmy(i); if (army == null || army.initialUnitCount <= 0) continue; int team = i;
                    ui.Button("team-select-" + i, new Rect(bx + 12 + n % 4 * tabW, by + 10 + n / 4 * 28, tabW - 8, 26), (Feedback25 != null && controller.selectedTeam == i ? "已选 · " : "") + FormatTeamName(i) + " " + controller.GetAliveUnitCount(i).ToString("N0"), () => controller.SelectArmy(team), false, true, controller.selectedTeam == team); n++;
                }
                ui.Button("army-statistics", new Rect(bx + bw - 132, by + 10, 118, 28), "军团统计", () => armyStatisticsOpen = !armyStatisticsOpen);
                float cy = by + (shown > 4 ? 66 : 48), b = (bw - 32) / 5;
                bool orders = (!selectionPreviewEnabled || playerScopeEnabled) && !PendingDecision26 && controller.SelectedArmy != null && controller.GetAliveUnitCount(controller.selectedTeam) > 0;
                ui.Button("start", new Rect(bx + 12, cy, b - 6, 40), BattleActionLabel(), ToggleBattleFromUI, true);
                ui.Button("attack", new Rect(bx + 12 + b, cy, b - 6, 40), "进攻  A", IssueAttack, false, orders);
                ui.Button("move", new Rect(bx + 12 + b * 2, cy, b - 6, 40), awaitingMoveTarget ? "选择落点…" : "移动  M", BeginMoveOrder, false, orders, awaitingMoveTarget);
                ui.Button("hold", new Rect(bx + 12 + b * 3, cy, b - 6, 40), "防守  H", IssueHold, false, orders);
                ui.Button("retreat", new Rect(bx + 12 + b * 4, cy, b - 6, 40), "撤退  R", IssueRetreat, false, orders);
                if (shown <= 4) ui.Label("order-status", new Rect(bx + 12, by + 94, bw - 24, 26), Selection25(SelectedArmyTitle() + " · " + (controller.SelectedArmy != null && controller.SelectedArmy.hasOrder ? FormatOrder(controller.SelectedArmy.currentOrder.type) : "等待命令")), 13, WarSandboxUGUI.Muted);
                string feedback = awaitingMoveTarget ? MoveTargetHint() : controller.CommandError ?? (Time.unscaledTime < feedbackUntil ? commandFeedback : "");
                if (FlowPolish23.Active(controller) && controller.Phase == WarSandboxBattlePhase.Paused && !awaitingMoveTarget && string.IsNullOrEmpty(controller.CommandError)) feedback = "战斗已暂停 · 下达命令会恢复战斗；只查看信息不会继续";
                feedback = FeedbackText26(FeedbackText25(feedback));
                if (!string.IsNullOrEmpty(feedback))
                {
                    ui.Panel("feedback", new Rect(bx, by - 80, bw, 68), WarSandboxUGUI.Surface);
                    ui.Label("feedback-text", new Rect(bx + 10, by - 76, bw - 130, 60), feedback, 15, (Feedback25 != null ? Feedback25.Rejected : !string.IsNullOrEmpty(controller.CommandError)) ? WarSandboxUGUI.Danger : WarSandboxUGUI.Ink);
                    if (awaitingMoveTarget) ui.Button("move-cancel", new Rect(bx + bw - 110, by - 64, 96, 34), "取消 Esc", CancelMoveTarget);
                }
            }
            if (battleToolsOpen || helpOpen || diagnosticsOpen)
            {
                ui.Panel("battle-tools-card", new Rect(16, 126, 268, 318));
                ui.Button("tools-edit", new Rect(30, 142, 116, 36), "返回布阵", () => { battleToolsOpen = false; editor.RequestEdit(); }, false, editor != null);
                ui.Button("reset", new Rect(156, 142, 114, 36), Details26 != null ? "重置本局" : "原样重开", () => RequestDecision26(BattleDetails26.Decision.Reset));
                ui.Button("end-battle", new Rect(30, 190, 240, 36), "结束本局（不判胜负）", () => RequestDecision26(BattleDetails26.Decision.End), false, !setup);
                ui.Button("follow", new Rect(30, 238, 116, 34), "跟随 F", () => FocusArmy(controller.selectedTeam));
                ui.Button("panorama", new Rect(156, 238, 114, 34), "全景 F3", FocusBattlefield);
                ui.Button("help", new Rect(30, 284, 116, 34), "操作帮助", () => { var guide = Guide30.For(WarSandboxSceneSession.Instance); if (guide != null) guide.Open(); else { helpOpen = !helpOpen; diagnosticsOpen = false; } });
                ui.Button("diagnostics", new Rect(156, 284, 114, 34), "技术信息", () => { diagnosticsOpen = !diagnosticsOpen; helpOpen = false; });
                float sy = 330; float[] speeds = { .5f, 1, 2, 4 };
                for (int i = 0; i < speeds.Length; i++) { float speed = speeds[i]; ui.Button("speed-" + i, new Rect(30 + i * 62, sy, 56, 32), speed + "×", () => controller.SetSimulationSpeed(speed), false, true, Mathf.Approximately(controller.SimulationSpeed, speed)); }
                ui.Button("minimap-toggle", new Rect(30, 376, 240, 34), showMinimap ? "收起战术地图" : "展开战术地图", () => showMinimap = !showMinimap);
                if (helpOpen || diagnosticsOpen)
                {
                    ui.Panel("details", new Rect(300, 156, 420, 296));
                    ui.Label("details-text", new Rect(312, 164, 396, 280), helpOpen ? HelpText() : DiagnosticsText(), 17, WarSandboxUGUI.Ink);
                }
            }
            if (armyStatisticsOpen && !setup)
            {
                float ah = Mathf.Min(350, 80 + controller.ArmyCount * 46);
                ui.Panel("army-statistics-panel", new Rect(w - 334, 150, 310, ah));
                ui.Label("army-statistics-title", new Rect(w - 324, 162, 200, 32), "全军团状态", 20, WarSandboxUGUI.Ink, true);
                ui.Button("army-statistics-close", new Rect(w - 100, 160, 62, 30), "关闭", () => armyStatisticsOpen = false);
                ui.Scroll("army-statistics-list", new Rect(w - 322, 204, 284, ah - 66), controller.ArmyCount * 46);
                for (int i = 0; i < controller.ArmyCount; i++) ui.Label("army-statistic-" + i, new Rect(0, i * 46, 280, 42), FormatTeamName(i) + "  " + controller.GetAliveUnitCount(i).ToString("N0") + " 存活", 17);
                ui.EndScroll();
            }
            if (showMinimap && battleToolsOpen) DrawUGUIMinimap(ui, w, h, 150, h - 170, w - 230);
            if (!string.IsNullOrEmpty(controller.BattlefieldRuleError)) ui.Label("rule-error", new Rect(30, h - 200, w - 60, 44), controller.BattlefieldRuleError, 16, WarSandboxUGUI.Danger);
            DrawDecision26(ui);
            ui.End();
        }
    }
}


