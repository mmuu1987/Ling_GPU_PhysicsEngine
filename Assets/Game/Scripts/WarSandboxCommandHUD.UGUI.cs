using UnityEngine;

namespace MassEngine.Game
{
    public sealed partial class WarSandboxCommandHUD
    {
        private WarSandboxUGUI runtimeUi;
        private float nextUiRefresh;
        private bool diagnosticsOpen, helpOpen, compactOrders;
        private BattleTelemetryHUD telemetryHud;
        private FlowFieldPreviewHUD flowHud;
        private bool telemetryWasEnabled, flowWasEnabled, diagnosticsCaptured;
        private void RefreshRuntimeUI()
        {
            ResolveReferences();
            bool visible = controller != null && !WarSandboxDeploymentHUD.BlocksInput(controller) &&
                (WarSandboxSceneSession.Instance == null || !WarSandboxSceneSession.Instance.InputBlocked);
            if (!diagnosticsCaptured && controller != null && controller.manager != null)
            {
                telemetryHud = controller.manager.GetComponent<BattleTelemetryHUD>(); flowHud = controller.manager.GetComponent<FlowFieldPreviewHUD>();
                telemetryWasEnabled = telemetryHud != null && telemetryHud.enabled; flowWasEnabled = flowHud != null && flowHud.enabled;
                diagnosticsCaptured = true;
            }
            if (telemetryHud != null) telemetryHud.enabled = false;
            if (flowHud != null) flowHud.enabled = false;
            if (!visible) { runtimeUi?.SetVisible(false); return; }
            if (runtimeUi == null) runtimeUi = new WarSandboxUGUI(transform, "Battle HUD Canvas", 50);
            runtimeUi.SetVisible(true);
            if (Time.unscaledTime < nextUiRefresh) return;
            nextUiRefresh = Time.unscaledTime + 0.15f;
            var ui = runtimeUi; ui.Begin();
            float w = ui.Width, h = ui.Height;
            bool narrow = w < 900;
            ui.Panel("battle-bar", new Rect(16, 16, w - 32, 62));
            ui.Panel("brand-mark", new Rect(16, 16, 4, 62), WarSandboxUGUI.Accent, false);
            ui.Label("battle-title", new Rect(30, 23, Mathf.Min(270, w - 256), 24), "战争沙盒  /  " + FormatPhase(controller.Phase), 19, null, true);
            ui.Label("battle-subtitle", new Rect(30, 49, Mathf.Min(310, w - 242), 22),
                (controller.gameMode == WarSandboxGameMode.ControlPoint ? "中央据点" : "歼灭会战") + "  ·  " + FormatBattleTime(controller.TelemetrySnapshot.battleSeconds), 13, WarSandboxUGUI.Muted);
            ui.Button("battle-help", new Rect(w - 232, 30, 92, 34), "操作提示", () => helpOpen = !helpOpen, false, true, helpOpen);
            ui.Button("battle-debug", new Rect(w - 132, 30, 100, 34), "技术信息", () => diagnosticsOpen = !diagnosticsOpen, false, true, diagnosticsOpen);
            if (narrow)
                ui.Button("orders-toggle", new Rect(w - 164, 92, 148, 34), compactOrders ? "展开指挥面板" : "收起指挥面板", () => compactOrders = !compactOrders);
            float panelWidth = Mathf.Min(320, w - 32), left = w - panelWidth - 16;
            bool showPanel = !narrow || !compactOrders;
            if (showPanel)
            {
                float top = narrow ? 136 : 94;
                ui.Panel("orders-bg", new Rect(left, top, panelWidth, h - top - 76));
                float contentHeight = 446 + Mathf.Max(1, controller.ArmyCount) * 48;
                ui.Scroll("orders-scroll", new Rect(left + 12, top + 12, panelWidth - 24, h - top - 100), contentHeight);
                float cw = panelWidth - 24, y = 0;
                ui.Label("army-heading", new Rect(0, y, cw, 28), "军团指挥", 20, null, true); y += 38;
                for (int team = 0; team < controller.ArmyCount; team++)
                {
                    int id = team; var army = controller.GetArmy(team); if (army == null || army.initialUnitCount <= 0) continue;
                    ui.Button("army-" + id, new Rect(0, y, cw, 40), FormatTeamName(id) + "    " + controller.GetAliveUnitCount(id).ToString("N0") + " / " + army.initialUnitCount.ToString("N0"),
                        () => controller.SelectArmy(id), false, true, controller.selectedTeam == id);
                    ui.Panel("army-color-" + id, new Rect(0, y, 4, 40), WarSandboxTeamPalette.Resolve(id), false); y += 48;
                }
                string order = controller.SelectedArmy != null && controller.SelectedArmy.hasOrder ? FormatOrder(controller.SelectedArmy.currentOrder.type) : "等待命令";
                ui.Label("order-status", new Rect(0, y, cw, 32), "当前指令  /  " + order, 14, WarSandboxUGUI.Muted); y += 36;
                bool canOrder = !IsTerminalPhase(controller.Phase);
                float half = (cw - 8) / 2;
                ui.Button("attack", new Rect(0, y, half, 38), "进攻  A", IssueAttack, false, canOrder);
                ui.Button("move", new Rect(half + 8, y, half, 38), awaitingMoveTarget ? "选择落点…" : "移动  M", BeginMoveOrder, false, canOrder, awaitingMoveTarget); y += 46;
                ui.Button("hold", new Rect(0, y, half, 38), "防守  H", IssueHold, false, canOrder);
                ui.Button("retreat", new Rect(half + 8, y, half, 38), "撤退  R", IssueRetreat, false, canOrder); y += 54;
                bool setup = controller.Phase == WarSandboxBattlePhase.Setup;
                string start = setup ? "开始战斗  Enter" : IsTerminalPhase(controller.Phase) ? "再来一局  Enter" : controller.Phase == WarSandboxBattlePhase.Running ? "暂停战斗  Space" : "继续战斗  Space";
                ui.Button("start", new Rect(0, y, cw, 44), start, () => { if (IsPreOrPostBattle(controller.Phase)) StartOrRestartDefaultBattle(); else controller.TogglePause(); }, true,
                    string.IsNullOrEmpty(controller.BattlefieldRuleError)); y += 54;
                var editor = controller.GetComponent<WarSandboxDeploymentHUD>();
                ui.Button("edit", new Rect(0, y, half, 36), setup ? "配兵布阵" : "返回布阵", () => { awaitingMoveTarget = false; editor.RequestEdit(); }, false, editor != null);
                ui.Button("reset", new Rect(half + 8, y, half, 36), "原样重开", () => { awaitingMoveTarget = false; controller.ResetBattle(); FocusBattlefield(); }); y += 46;
                float speedWidth = (cw - 18) / 4;
                for (int i = 0; i < 4; i++)
                {
                    float speed = i == 0 ? 0.5f : i == 1 ? 1 : i == 2 ? 2 : 4;
                    ui.Button("speed-" + i, new Rect(i * (speedWidth + 6), y, speedWidth, 32), speed + "×", () => controller.SetSimulationSpeed(speed), false, canOrder, Mathf.Approximately(controller.SimulationSpeed, speed));
                }
                y += 42;
                ui.Button("follow", new Rect(0, y, half, 34), "跟随  F", () => FocusArmy(controller.selectedTeam));
                ui.Button("panorama", new Rect(half + 8, y, half, 34), "全景  F3", FocusBattlefield); y += 44;
                if (setup)
                {
                    ui.Button("annihilation", new Rect(0, y, half, 32), "歼灭战", () => controller.SetGameMode(WarSandboxGameMode.Annihilation), false, true, controller.gameMode == WarSandboxGameMode.Annihilation);
                    ui.Button("point", new Rect(half + 8, y, half, 32), "据点战", () => controller.SetGameMode(WarSandboxGameMode.ControlPoint), false, true, controller.gameMode == WarSandboxGameMode.ControlPoint); y += 40;
                    ui.Button("obstacles", new Rect(0, y, cw, 32), controller.staticObstaclesEnabled ? "静态障碍：开启  O" : "静态障碍：关闭  O", ToggleStaticObstacles); y += 38;
                }
                if (!string.IsNullOrEmpty(controller.BattlefieldRuleError))
                    ui.Label("rule-error", new Rect(0, y, cw, 100), controller.BattlefieldRuleError, 14, WarSandboxUGUI.Danger);
                ui.EndScroll();
            }
            float availableLeft = showPanel ? left - 32 : w - 32;
            DrawUGUIMinimap(ui, availableLeft, h);
            if (controller.gameMode == WarSandboxGameMode.ControlPoint && !IsTerminalPhase(controller.Phase))
            {
                float ow = Mathf.Min(400, availableLeft);
                ui.Panel("objective", new Rect(16, 94, ow, 90));
                string status = controller.IsControlPointContested ? "争夺中" : controller.ControlPointOwnerTeamId < 0 ? "未占领" : FormatTeamName(controller.ControlPointOwnerTeamId) + "占领 " + Mathf.RoundToInt(controller.ControlPointCaptureProgress * 100) + "%";
                ui.Label("objective-title", new Rect(24, 100, ow - 16, 28), "中央据点  /  " + status, 17, null, true);
                ui.Label("objective-counts", new Rect(24, 132, ow - 16, 24), FormatControlPointCounts(), 12, WarSandboxUGUI.Muted);
                ui.Panel("objective-track", new Rect(34, 166, ow - 36, 5), WarSandboxUGUI.Line, false);
                ui.Panel("objective-progress", new Rect(34, 166, (ow - 36) * Mathf.Clamp01(controller.ControlPointCaptureProgress), 5), WarSandboxUGUI.Accent, false);
            }
            if (awaitingMoveTarget || Time.unscaledTime < feedbackUntil)
            {
                float fw = Mathf.Min(480, w - 32);
                ui.Panel("feedback", new Rect(16, h - 110, fw, 40));
                ui.Label("feedback-text", new Rect(20, h - 108, fw - 8, 36), awaitingMoveTarget ? "点击战场选择落点 · Shift 追加 · Esc 取消" : commandFeedback, 14, WarSandboxUGUI.Accent);
            }
            if (helpOpen || diagnosticsOpen)
            {
                float dw = Mathf.Min(420, availableLeft), dy = controller.gameMode == WarSandboxGameMode.ControlPoint ? 200 : 94;
                ui.Panel("details", new Rect(16, dy, dw, 144));
                string text = helpOpen ? "操作提示\n数字键选择军团 · A/M/H/R 下令\n右键自由观察 · 滚轮缩放\nF 跟随当前军团 · F3 全景\n点击小地图定位，右键下达移动命令" :
                    "技术信息\n战斗时间 " + controller.TelemetrySnapshot.battleSeconds.ToString("F1") + " s\n网格溢出 " + controller.TelemetrySnapshot.gridOverflowPerFrame + " / 帧\n流场重建 " + controller.TelemetrySnapshot.attackerFlowRebuilds + " / " + controller.TelemetrySnapshot.defenderFlowRebuilds;
                ui.Label("details-text", new Rect(24, dy + 8, dw - 16, 128), text, 14, WarSandboxUGUI.Muted);
            }
            if (controller.TelemetrySnapshot.gridOverflowPerFrame > 0 && !diagnosticsOpen)
                ui.Label("capacity-hint", new Rect(20, h - 140, Mathf.Min(260, availableLeft), 24), "局部拥挤  ·  技术信息可查看", 12, WarSandboxUGUI.Danger);
            if (controller.BattleResult.valid) DrawUGUIResult(ui);
            ui.End();
        }
        private void DrawUGUIResult(WarSandboxUGUI ui)
        {
            float w = Mathf.Min(520, ui.Width - 40), x = (ui.Width - w) / 2, height = Mathf.Min(ui.Height - 120, 224 + controller.ArmyCount * 34), y = (ui.Height - height) / 2;
            ui.Panel("result-shade", new Rect(0, 80, ui.Width, ui.Height - 144), new Color(0.86f, 0.90f, 0.92f, 0.8f), false);
            ui.Panel("result-card", new Rect(x, y, w, height));
            var result = controller.BattleResult;
            ui.Label("result-title", new Rect(x + 16, y + 18, w - 32, 42), FormatResultTitle(result), 28, WarSandboxUGUI.Accent, true);
            ui.Label("result-reason", new Rect(x + 16, y + 66, w - 32, 28), FormatVictoryReason(result.victoryReason) + "  /  " + FormatBattleTime(result.battleSeconds), 16, WarSandboxUGUI.Muted);
            ui.Scroll("result-armies", new Rect(x + 24, y + 110, w - 48, height - 180), controller.ArmyCount * 38);
            for (int team = 0; team < controller.ArmyCount; team++)
            {
                var army = controller.GetArmy(team); int alive = controller.GetAliveUnitCount(team);
                ui.Label("result-army-" + team, new Rect(0, team * 38, w - 48, 34), FormatTeamName(team) + "   存活 " + alive.ToString("N0") + "   损失 " + Mathf.Max(0, army.initialUnitCount - alive).ToString("N0"), 16);
            }
            ui.EndScroll();
            ui.Button("result-restart", new Rect(x + 24, y + height - 54, (w - 56) / 2, 38), "再来一局", StartOrRestartDefaultBattle, true);
            var resultEditor = controller.GetComponent<WarSandboxDeploymentHUD>();
            ui.Button("result-edit", new Rect(x + 32 + (w - 56) / 2, y + height - 54, (w - 56) / 2, 38), "返回布阵", () => resultEditor.RequestEdit(), false, resultEditor != null);
        }
        private void DrawUGUIMinimap(WarSandboxUGUI ui, float available, float screenHeight)
        {
            if (!showMinimap || !TryResolveSimulationWorldSize(out var world)) return;
            float size = Mathf.Min(200, available), y = screenHeight - size - 156;
            if (size < 120 || y < 200) return;
            ui.Panel("mini-bg", new Rect(16, y, size, size + 28));
            ui.Label("mini-title", new Rect(22, y, size - 12, 26), "战术视图", 13, WarSandboxUGUI.Muted);
            Rect map = new Rect(24, y + 28, size - 16, size - 12);
            ui.Panel("mini-surface", map, WarSandboxUGUI.Background, false);
            for (int i = 0; i < controller.GetStaticObstacleCount(); i++)
            {
                if (!controller.TryGetStaticObstacle(i, out var obstacle)) continue;
                var b = obstacle.Bounds;
                Vector2 a = WarSandboxMinimapProjection.WorldToMap(new Vector3(b.xMin, 0, b.yMin), world, map), c = WarSandboxMinimapProjection.WorldToMap(new Vector3(b.xMax, 0, b.yMax), world, map);
                ui.Panel("mini-wall-" + i, Rect.MinMaxRect(Mathf.Min(a.x, c.x), Mathf.Min(a.y, c.y), Mathf.Max(a.x, c.x), Mathf.Max(a.y, c.y)), WarSandboxUGUI.Muted, false);
            }
            if (controller.gameMode == WarSandboxGameMode.ControlPoint)
            {
                var p = WarSandboxMinimapProjection.WorldToMap(controller.controlPointCenter, world, map);
                ui.Panel("mini-objective", new Rect(p.x - 4, p.y - 4, 8, 8), new Color32(169, 128, 27, 255), false);
            }
            for (int team = 0; team < controller.ArmyCount; team++)
            {
                if (!TryResolveLiveArmyBounds(team, out var bounds) && !TryResolveArmyBounds(team, out bounds)) continue;
                Vector2 p = WarSandboxMinimapProjection.WorldToMap(bounds.center, world, map); Color color = WarSandboxTeamPalette.Resolve(team);
                ui.Panel("mini-army-" + team, new Rect(p.x - 4, p.y - 4, controller.selectedTeam == team ? 10 : 7, controller.selectedTeam == team ? 10 : 7), color, false);
                Vector2 previous = p;
                for (int i = 0; i < controller.GetMoveRoutePointCount(team); i++)
                {
                    if (!controller.TryGetMoveRoutePoint(team, i, out var target)) continue;
                    var point = WarSandboxMinimapProjection.WorldToMap(target, world, map);
                    // Small evenly spaced route dots avoid an extra shader/material or mesh allocation.
                    int dots = Mathf.Min(24, Mathf.CeilToInt(Vector2.Distance(previous, point) / 5));
                    for (int dot = 0; dot < dots; dot++)
                    {
                        var d = Vector2.Lerp(previous, point, (dot + 1f) / Mathf.Max(1, dots));
                        ui.Panel("mini-route-" + team + "-" + i + "-" + dot, new Rect(d.x - 1, d.y - 1, 2, 2), color, false);
                    }
                    previous = point;
                }
            }
            if (commandCamera != null)
            {
                var p = WarSandboxMinimapProjection.WorldToMap(commandCamera.transform.position, world, map);
                ui.Panel("mini-camera", new Rect(p.x - 2, p.y - 2, 5, 5), WarSandboxUGUI.Ink, false);
            }
            ui.PointerArea("mini-input", map, (point, button, shift) =>
            {
                var target = WarSandboxMinimapProjection.MapToWorld(new Vector2(map.x + point.x * map.width, map.y + point.y * map.height), world, map);
                var action = WarSandboxMinimapProjection.ResolvePointerAction(button, awaitingMoveTarget, shift);
                if (action == WarSandboxMinimapAction.FocusCamera) { cameraFocusMode = CameraFocusMode.None; cameraManager?.CenterTacticalPoint(target); }
                else if (action != WarSandboxMinimapAction.None) IssueMoveTo(target, action == WarSandboxMinimapAction.QueueMoveSelectedArmy);
            });
        }
        private void OnDestroy()
        {
            runtimeUi?.Dispose();
            if (telemetryHud != null) telemetryHud.enabled = telemetryWasEnabled;
            if (flowHud != null) flowHud.enabled = flowWasEnabled;
        }
    }
}
