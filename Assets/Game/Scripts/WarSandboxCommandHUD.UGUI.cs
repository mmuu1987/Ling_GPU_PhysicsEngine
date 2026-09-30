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
            if (IsTerminalPhase(controller.Phase)) awaitingMoveTarget = false;
            var ui = runtimeUi; ui.Begin();
            float w = ui.Width, h = ui.Height;
            bool narrow = w < 900;
            ui.Panel("battle-bar", new Rect(16, 16, w - 32, 62));
            ui.Panel("brand-mark", new Rect(16, 16, 4, 62), WarSandboxUGUI.Accent, false);
            ui.Label("battle-title", new Rect(30, 23, Mathf.Min(270, w - 478), 24), "战争沙盒  /  " + FormatPhase(controller.Phase), 19, null, true);
            ui.Label("battle-subtitle", new Rect(30, 49, Mathf.Min(310, w - 478), 22),
                (controller.gameMode == WarSandboxGameMode.ControlPoint ? "中央据点" : "歼灭会战") + "  ·  " + FormatBattleTime(controller.TelemetrySnapshot.battleSeconds), 13, WarSandboxUGUI.Muted);
            ui.Button("battle-toggle", new Rect(w - 424, 30, 184, 34), BattleActionLabel(), ToggleBattleFromUI, true,
                string.IsNullOrEmpty(controller.BattlefieldRuleError));
            ui.Button("battle-help", new Rect(w - 232, 30, 92, 34), "操作提示", () => { helpOpen = !helpOpen; if (helpOpen) diagnosticsOpen = false; nextUiRefresh = 0; }, false, true, helpOpen);
            ui.Button("battle-debug", new Rect(w - 132, 30, 100, 34), controller.TelemetrySnapshot.gridOverflowPerFrame > 0 ? "技术信息 !" : "技术信息", () => { diagnosticsOpen = !diagnosticsOpen; if (diagnosticsOpen) helpOpen = false; nextUiRefresh = 0; }, false, true, diagnosticsOpen);
            if (narrow)
                ui.Button("orders-toggle", new Rect(w - 164, 92, 148, 34), compactOrders ? "展开指挥面板" : "收起指挥面板", () => compactOrders = !compactOrders);
            float panelWidth = Mathf.Min(320, w - 32), left = w - panelWidth - 16;
            bool showPanel = !narrow || !compactOrders;
            var battlefield = WarSandboxSceneSession.Instance?.CurrentBattlefield;
            bool showBriefing = controller.Phase == WarSandboxBattlePhase.Setup && !awaitingMoveTarget &&
                !string.IsNullOrEmpty(battlefield?.briefing) && !helpOpen && !diagnosticsOpen;
            if (showPanel)
            {
                float top = narrow ? 136 : 94;
                ui.Panel("orders-bg", new Rect(left, top, panelWidth, h - top - 76));
                DrawSelectedOrder(ui, left + 12, top + 12, panelWidth - 24);
                float contentHeight = 470 + Mathf.Max(1, controller.ArmyCount) * 48 +
                    (string.IsNullOrEmpty(controller.BattlefieldRuleError) ? 0 : 110);
                ui.Scroll("orders-scroll", new Rect(left + 12, top + 122, panelWidth - 24, h - top - 210), contentHeight);
                float cw = panelWidth - 24, y = 0;
                ui.Label("army-heading", new Rect(0, y, cw, 28), "选择军团 · 数字键 1–9", 15, null, true); y += 34;
                for (int team = 0; team < controller.ArmyCount; team++)
                {
                    int id = team; var army = controller.GetArmy(team); if (army == null || army.initialUnitCount <= 0) continue;
                    ui.Button("army-" + id, new Rect(0, y, cw, 40), (controller.selectedTeam == id ? "已选 · " : "") + FormatTeamName(id) + "    " + controller.GetAliveUnitCount(id).ToString("N0") + " / " + army.initialUnitCount.ToString("N0"),
                        () => { controller.SelectArmy(id); nextUiRefresh = 0; }, false, true, controller.selectedTeam == id);
                    ui.Panel("army-color-" + id, new Rect(0, y, 4, 40), WarSandboxTeamPalette.Resolve(id), false); y += 48;
                }
                bool canOrder = !IsTerminalPhase(controller.Phase) && controller.SelectedArmy != null;
                float half = (cw - 8) / 2;
                ui.Button("attack", new Rect(0, y, half, 38), "进攻  A", IssueAttack, false, canOrder);
                ui.Button("move", new Rect(half + 8, y, half, 38), awaitingMoveTarget ? "选择落点…" : "移动  M", BeginMoveOrder, false, canOrder, awaitingMoveTarget); y += 46;
                ui.Button("hold", new Rect(0, y, half, 38), "防守  H", IssueHold, false, canOrder);
                ui.Button("retreat", new Rect(half + 8, y, half, 38), "撤退  R", IssueRetreat, false, canOrder); y += 54;
                bool setup = controller.Phase == WarSandboxBattlePhase.Setup;
                ui.Button("start", new Rect(0, y, cw, 44), BattleActionLabel(), ToggleBattleFromUI, true,
                    string.IsNullOrEmpty(controller.BattlefieldRuleError)); y += 54;
                var editor = controller.GetComponent<WarSandboxDeploymentHUD>();
                ui.Button("edit", new Rect(0, y, half, 36), setup ? "配兵布阵" : "返回布阵", () => { awaitingMoveTarget = false; editor.RequestEdit(); }, false, editor != null);
                ui.Button("reset", new Rect(half + 8, y, half, 36), "原样重开", () => { awaitingMoveTarget = false; controller.ResetBattle(); FocusBattlefield(); }); y += 46;
                if (controller.Phase == WarSandboxBattlePhase.Running || controller.Phase == WarSandboxBattlePhase.Paused)
                {
                    ui.Button("end-battle", new Rect(0, y, cw, 34), "结束本局（不判胜负）", () => { awaitingMoveTarget = false; controller.EndBattle(); });
                    y += 44;
                }
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
            if (!showPanel)
            {
                ui.Panel("compact-status", new Rect(16, 92, w - 196, 34));
                ui.Label("compact-status-text", new Rect(20, 94, w - 204, 30), SelectedArmyTitle() + " / " +
                    (controller.SelectedArmy != null && controller.SelectedArmy.hasOrder ? FormatOrder(controller.SelectedArmy.currentOrder.type) : "等待命令"), 14, null, true);
            }
            float infoTop = controller.gameMode == WarSandboxGameMode.ControlPoint || !showPanel ? 200 : 94;
            float infoHeight = helpOpen ? 208 : diagnosticsOpen ? 144 : showBriefing ? 140 : 0;
            float mapMinimumY = controller.gameMode == WarSandboxGameMode.ControlPoint ? 200 : !showPanel ? 138 : 94;
            DrawUGUIMinimap(ui, availableLeft, h, infoHeight > 0 ? Mathf.Max(mapMinimumY, infoTop + infoHeight + 12) : mapMinimumY);
            if (showBriefing)
            {
                float bw = Mathf.Min(420, availableLeft);
                ui.Panel("briefing-card", new Rect(16, infoTop, bw, 140));
                ui.Label("briefing-title", new Rect(24, infoTop + 6, bw - 16, 28), "本局目标 / 快速上手", 17, null, true);
                ui.Label("preset-briefing", new Rect(24, infoTop + 36, bw - 16, 96), battlefield.briefing, 14, WarSandboxUGUI.Muted);
            }
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
            var battleFeedback = controller.GetComponent<WarSandboxBattleFeedback>();
            bool hasBattleFeedback = battleFeedback != null && Time.unscaledTime < battleFeedback.MessageUntil;
            bool paused = controller.Phase == WarSandboxBattlePhase.Paused;
            if (awaitingMoveTarget || paused || Time.unscaledTime < feedbackUntil || !string.IsNullOrEmpty(controller.CommandError) || hasBattleFeedback)
            {
                float fw = Mathf.Min(620, availableLeft), fy = h - 158;
                ui.Panel("feedback", new Rect(16, fy, fw, 88));
                string title = awaitingMoveTarget ? "为 " + FormatTeamName(controller.selectedTeam) + " 选择目标" :
                    paused ? "战斗已暂停 · Space 继续" : "操作反馈";
                string feedback = awaitingMoveTarget ? MoveTargetHint() : Time.unscaledTime < feedbackUntil ? commandFeedback :
                    controller.CommandError ?? (paused ? "新下达活动命令会继续运行；追加已有航点不会" : hasBattleFeedback ? battleFeedback.Message : "");
                float textWidth = awaitingMoveTarget ? fw - 110 : fw - 8;
                ui.Label("feedback-title", new Rect(20, fy + 3, textWidth, 25), title, 15, WarSandboxUGUI.Accent, true);
                ui.Label("feedback-text", new Rect(20, fy + 27, textWidth, 58), feedback, 13,
                    string.IsNullOrEmpty(controller.CommandError) ? WarSandboxUGUI.Ink : WarSandboxUGUI.Danger);
                if (awaitingMoveTarget)
                    ui.Button("move-cancel", new Rect(16 + fw - 102, fy + 22, 94, 34), "取消 Esc", CancelMoveTarget);
            }
            if (helpOpen || diagnosticsOpen)
            {
                float dw = Mathf.Min(460, availableLeft);
                ui.Panel("details", new Rect(16, infoTop, dw, infoHeight));
                string text = helpOpen ? "操作提示\n1. 点击军团，或用数字键选择\n2. 按 M，再点击地面设置目标\n3. 再按 M，Shift + 点击追加航点\nEsc / 取消按钮只取消选点，不清除原命令\nSpace 暂停 / 继续；改令会继续运行\n右键 + WASD 观察 · 滚轮缩放\nF 跟随所选军团 · F3 全景\n小地图：左键定位，右键下令" :
                    "技术信息\n战斗时间 " + controller.TelemetrySnapshot.battleSeconds.ToString("F1") + " s\n网格溢出 " + controller.TelemetrySnapshot.gridOverflowPerFrame + " / 帧\n流场重建 " + controller.TelemetrySnapshot.attackerFlowRebuilds + " / " + controller.TelemetrySnapshot.defenderFlowRebuilds;
                ui.Label("details-text", new Rect(24, infoTop + 6, dw - 16, infoHeight - 12), text, 14, WarSandboxUGUI.Muted);
            }
            if (controller.BattleResult.valid) DrawUGUIResult(ui);
            ui.End();
        }
        private void DrawUGUIResult(WarSandboxUGUI ui)
        {
            var result = controller.BattleResult;
            float w = Mathf.Min(560, ui.Width - 40), x = (ui.Width - w) / 2, height = Mathf.Min(ui.Height - 120, 260 + result.ArmyCount * 38), y = (ui.Height - height) / 2;
            ui.Panel("result-shade", new Rect(0, 80, ui.Width, ui.Height - 144), new Color(0.86f, 0.90f, 0.92f, 0.8f), false);
            ui.Panel("result-card", new Rect(x, y, w, height));
            ui.Label("result-title", new Rect(x + 16, y + 18, w - 32, 42), FormatResultTitle(result), 28, WarSandboxUGUI.Accent, true);
            ui.Label("result-reason", new Rect(x + 16, y + 66, w - 32, 28), FormatVictoryReason(result.victoryReason) + "  /  " + FormatBattleTime(result.battleSeconds), 16, WarSandboxUGUI.Muted);
            float tableWidth = w - 48, column = tableWidth / 4;
            string[] headings = { "军团", "初始", "存活", "损失" };
            for (int i = 0; i < headings.Length; i++)
                ui.Label("result-heading-" + i, new Rect(x + 24 + column * i, y + 104, column, 30), headings[i], 14, WarSandboxUGUI.Muted);
            ui.Scroll("result-armies", new Rect(x + 24, y + 138, tableWidth, height - 246), result.ArmyCount * 38);
            for (int row = 0; row < result.ArmyCount; row++)
            {
                var army = result.GetArmy(row); string key = "result-army-" + army.teamId;
                ui.Label(key, new Rect(0, row * 38, column, 34), army.displayName, 15, null, army.teamId == result.winnerTeamId);
                ui.Label(key + "-initial", new Rect(column, row * 38, column, 34), army.initial.ToString("N0"), 15);
                ui.Label(key + "-alive", new Rect(column * 2, row * 38, column, 34), army.survivors.ToString("N0"), 15);
                ui.Label(key + "-losses", new Rect(column * 3, row * 38, column, 34), army.Casualties.ToString("N0"), 15);
            }
            ui.EndScroll();
            ui.Button("result-restart", new Rect(x + 24, y + height - 98, (w - 56) / 2, 38), "再来一局", StartOrRestartDefaultBattle, true);
            var resultEditor = controller.GetComponent<WarSandboxDeploymentHUD>();
            ui.Button("result-edit", new Rect(x + 32 + (w - 56) / 2, y + height - 98, (w - 56) / 2, 38), "返回布阵", () => resultEditor.RequestEdit(), false, resultEditor != null);
            var session = WarSandboxSceneSession.Instance;
            ui.Button("result-menu", new Rect(x + 24, y + height - 52, w - 48, 34), "选择其他战场", () => session.TryReturnToMenu(false, out _), false, session != null);
        }
        private void DrawUGUIMinimap(WarSandboxUGUI ui, float available, float screenHeight, float minimumY)
        {
            if (!showMinimap || !TryResolveSimulationWorldSize(out var world)) return;
            float size = Mathf.Min(200, available), y = screenHeight - size - 190;
            if (size < 120 || y < minimumY) return;
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
                if (action == WarSandboxMinimapAction.FocusCamera)
                {
                    if (!controller.TryResolveGroundPoint(target, out target, out string error)) { SetFeedback(error); return; }
                    cameraFocusMode = CameraFocusMode.None; cameraManager?.CenterTacticalPoint(target);
                }
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
