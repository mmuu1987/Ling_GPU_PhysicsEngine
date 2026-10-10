using UnityEngine;

namespace MassEngine.Game
{
    // B2 "tactical command" battle HUD. Every control of the previous HUD keeps its id and action; only placement and
    // styling changed (left: selection + armies + feedback, bottom: command bar, right: playback/system + preparation).
    public sealed partial class WarSandboxCommandHUD
    {
        private WarSandboxUGUI runtimeUi;
        private float nextUiRefresh;
        private bool diagnosticsOpen, helpOpen, compactOrders;
        private BattleTelemetryHUD telemetryHud;
        private FlowFieldPreviewHUD flowHud;
        private bool telemetryWasEnabled, flowWasEnabled, diagnosticsCaptured;
        private const float ColumnWidth = 300, RightWidth = 300;
        private static readonly Color CardFill = WarSandboxUGUI.Raised;
        private bool CustomStatsActive()
        {
            var deployment = controller != null ? controller.GetComponent<WarSandboxRuntimeDeployment>() : null;
            return deployment != null && deployment.HasCustomStats;
        }
        /// <summary>Conservative wrapped-text height (CJK counted full width) so panels grow before text is truncated.</summary>
        private static float EstimateTextHeight(string text, int size, float width)
        {
            if (string.IsNullOrEmpty(text)) return size * 1.45f;
            float usable = Mathf.Max(20, width - 20), lines = 0;
            foreach (var line in text.Split('\n'))
            {
                float w = 0; foreach (char c in line) w += c < 0x2E80 ? size * 0.56f : size * 1.02f;
                lines += Mathf.Max(1, Mathf.Ceil(w / usable));
            }
            return lines * size * 1.42f + 6;
        }
        private string HelpText() => "操作提示\n1. 点击军团，或用数字键选择\n2. 按 M，再点击地面设置目标\n3. 左键拖框当前军团；框空不指挥任何人\n4. 局部只支持单目标；整军团可Shift追加航点\nEsc先取消选点，再清选区；不清除原命令\nSpace 暂停 / 继续；改令会继续运行\n右键 + WASD 观察 · 滚轮缩放\nF 跟随所选军团 · F3 全景\n小地图：左键定位，右键下令";
        private string DiagnosticsText() => "技术信息\n战斗时间 " + controller.TelemetrySnapshot.battleSeconds.ToString("F1") + " s\n网格溢出 " + controller.TelemetrySnapshot.gridOverflowPerFrame + " / 帧\n流场重建 " +
            controller.TelemetrySnapshot.attackerFlowRebuilds + " / " + controller.TelemetrySnapshot.defenderFlowRebuilds;
        private string StartLabel()
        {
            if (controller.Phase == WarSandboxBattlePhase.Setup) return "开始战斗";
            if (IsTerminalPhase(controller.Phase)) return "再来一局";
            return controller.Phase == WarSandboxBattlePhase.Running ? "暂停" : "继续";
        }
        private void RefreshLegacyRuntimeUI()
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
            bool narrow = w < 900, setup = controller.Phase == WarSandboxBattlePhase.Setup, point = controller.gameMode == WarSandboxGameMode.ControlPoint;
            bool ruleOk = string.IsNullOrEmpty(controller.BattlefieldRuleError), custom = CustomStatsActive();
            var battlefield = WarSandboxSceneSession.Instance?.CurrentBattlefield;
            var editor = controller.GetComponent<WarSandboxDeploymentHUD>();
            string clock = FormatBattleTime(controller.TelemetrySnapshot.battleSeconds);
            float rightX = w - 16 - RightWidth;

            // ---- Right: help/debug beside the system strip, playback row, battle actions ----
            var nav = WarSandboxFrontEnd.NavigationLayout(w);
            ui.Button("battle-debug", new Rect(nav.x - 8 - 96, nav.y, 96, nav.height), controller.TelemetrySnapshot.gridOverflowPerFrame > 0 ? "技术信息 !" : "技术信息",
                () => { diagnosticsOpen = !diagnosticsOpen; if (diagnosticsOpen) helpOpen = false; nextUiRefresh = 0; }, false, true, diagnosticsOpen);
            ui.Button("battle-help", new Rect(nav.x - 8 - 96 - 4 - 86, nav.y, 86, nav.height), "操作提示",
                () => { helpOpen = !helpOpen; if (helpOpen) diagnosticsOpen = false; nextUiRefresh = 0; }, false, true, helpOpen);
            float playX = w - 16 - 394;
            ui.Button("battle-toggle", new Rect(playX, 50, 140, 32), BattleActionLabel(), ToggleBattleFromUI, true, ruleOk);
            bool canOrder = !IsTerminalPhase(controller.Phase) && controller.SelectedArmy != null;
            for (int i = 0; i < 4; i++)
            {
                float speed = i == 0 ? 0.5f : i == 1 ? 1 : i == 2 ? 2 : 4;
                ui.Button("speed-" + i, new Rect(playX + 146 + i * 62, 50, 58, 32), speed + "×", () => controller.SetSimulationSpeed(speed), false, canOrder, Mathf.Approximately(controller.SimulationSpeed, speed));
            }
            float rightBottom = 82;
            System.Action edit = () => { awaitingMoveTarget = false; editor.RequestEdit(); };
            System.Action reset = () => { awaitingMoveTarget = false; controller.ResetBattle(); FocusBattlefield(); };
            string details = helpOpen ? HelpText() : diagnosticsOpen ? DiagnosticsText() : null;
            if (setup)
            {
                // Preparation card: briefing (or the help/diagnostics text), rules, deployment.
                bool showBriefing = details == null && !string.IsNullOrEmpty(battlefield?.briefing);
                string body = details ?? (showBriefing ? battlefield.briefing : null);
                float textH = body == null ? 0 : EstimateTextHeight(body, 14, RightWidth - 16);
                float y = 92, cardH = 40 + textH + (body == null ? 0 : 8) + 38 * 3 + 10;
                var card = new Rect(rightX, y, RightWidth, cardH);
                ui.Panel("prep-card", card); ui.Brackets("prep-card-br", card, null, 10);
                ui.Code("prep-code", new Rect(rightX + 14, y + 8, RightWidth - 28, 22), details != null ? (helpOpen ? "HELP // 操作提示" : "DIAG // 技术信息") : "BRIEFING // 作战准备", 12, WarSandboxUGUI.Accent);
                if (custom) ui.Chip("prep-custom", new Rect(rightX + RightWidth - 88, y + 10, 74, 18), "自定义数值", new Color32(40, 30, 8, 255), WarSandboxUGUI.Amber, WarSandboxUGUI.Amber, 11, false);
                y += 36;
                if (details != null) ui.Label("details-text", new Rect(rightX + 4, y, RightWidth - 8, textH), details, 14, WarSandboxUGUI.Muted);
                else if (showBriefing) ui.Label("preset-briefing", new Rect(rightX + 4, y, RightWidth - 8, textH), battlefield.briefing, 14, new Color32(196, 214, 226, 255));
                if (body != null) y += textH + 8;
                float half = (RightWidth - 32) / 2, bx = rightX + 12;
                ui.Button("annihilation", new Rect(bx, y, half, 32), "歼灭战", () => controller.SetGameMode(WarSandboxGameMode.Annihilation), false, true, !point);
                ui.Button("point", new Rect(bx + half + 8, y, half, 32), "据点战", () => controller.SetGameMode(WarSandboxGameMode.ControlPoint), false, true, point); y += 38;
                ui.Button("obstacles", new Rect(bx, y, RightWidth - 24, 32), controller.staticObstaclesEnabled ? "静态障碍：开启  O" : "静态障碍：关闭  O", ToggleStaticObstacles, false, true, controller.staticObstaclesEnabled); y += 38;
                ui.Button("edit", new Rect(bx, y, half, 32), "配兵布阵", edit, false, editor != null);
                ui.Button("reset", new Rect(bx + half + 8, y, half, 32), "原样重开", reset);
                rightBottom = card.yMax;
            }
            else
            {
                bool live = controller.Phase == WarSandboxBattlePhase.Running || controller.Phase == WarSandboxBattlePhase.Paused;
                float x = w - 16 - (live ? 346 : 172);
                ui.Button("edit", new Rect(x, 90, 84, 30), "返回布阵", edit, false, editor != null);
                ui.Button("reset", new Rect(x + 88, 90, 84, 30), "原样重开", reset);
                if (live) ui.Button("end-battle", new Rect(x + 176, 90, 170, 30), "结束本局（不判胜负）", () => { awaitingMoveTarget = false; controller.EndBattle(); });
                rightBottom = 120;
                if (details != null)
                {
                    float textH = EstimateTextHeight(details, 14, RightWidth - 16);
                    var panel = new Rect(rightX, 130, RightWidth, textH + 40);
                    ui.Panel("details", panel); ui.Brackets("details-br", panel, null, 10);
                    ui.Code("details-code", new Rect(rightX + 14, 138, RightWidth - 28, 20), helpOpen ? "HELP // 操作提示" : "DIAG // 技术信息", 12, WarSandboxUGUI.Accent);
                    ui.Label("details-text", new Rect(rightX + 4, 164, RightWidth - 8, textH), details, 14, WarSandboxUGUI.Muted);
                    rightBottom = panel.yMax;
                }
            }
            if (!ruleOk)
            {
                float eh = EstimateTextHeight(controller.BattlefieldRuleError, 13, RightWidth - 16) + 8;
                ui.Panel("rule-error-bg", new Rect(rightX, rightBottom + 8, RightWidth, eh), null, true, WarSandboxUGUI.Danger);
                ui.Label("rule-error", new Rect(rightX + 4, rightBottom + 12, RightWidth - 8, eh - 8), controller.BattlefieldRuleError, 13, WarSandboxUGUI.Danger);
                rightBottom += eh + 8;
            }

            // ---- Top left: operation title ----
            float titleW = Mathf.Min(400, Mathf.Min(narrow ? playX : w * 0.5f - 226, nav.x - 8 - 96 - 4 - 86) - 28);
            // Backing plate: the title block floats over bright terrain, so it needs the same dark field as the panels.
            ui.Panel("battle-title-bg", new Rect(10, 4, titleW + 8, 78), new Color32(7, 15, 22, 200), false);
            ui.Code("battle-code", new Rect(16, 8, titleW, 18), "MASS WAR SANDBOX // " + (point ? "CONTROL-POINT" : "ANNIHILATION"), 11, WarSandboxUGUI.Muted);
            ui.Panel("battle-title-mark", new Rect(16, 30, 3, 24), WarSandboxUGUI.Accent, false);
            ui.LabelAligned("battle-title", new Rect(14, 26, titleW, 32), battlefield != null && !string.IsNullOrEmpty(battlefield.displayName) ? battlefield.displayName : "战争沙盒",
                19, WarSandboxUGUI.Ink, true, TextAnchor.MiddleLeft);
            ui.LabelAligned("battle-subtitle", new Rect(14, 56, titleW, 24), FormatPhase(controller.Phase) + "  ·  " + (point ? "中央据点" : "歼灭会战") + (narrow ? "  ·  " + clock : "") +
                (custom ? "  ·  自定义数值" : ""), 13, custom ? WarSandboxUGUI.Amber : WarSandboxUGUI.Muted, false, TextAnchor.MiddleLeft);

            // ---- Top centre (wide only): clock, force bar, objective ----
            if (!narrow)
            {
                float cw = 420, cx = (w - cw) / 2;
                ui.Panel("clock-bg", new Rect(cx, 8, cw, 70), new Color32(7, 15, 22, 215), false, WarSandboxUGUI.Line);
                ui.Code("battle-clock", new Rect(cx, 10, cw, 30), clock, 24, WarSandboxUGUI.Ink, TextAnchor.MiddleCenter, true);
                DrawForceBar(ui, new Rect(cx + 14, 44, cw - 28, 6), 52);
            }
            if (point && !IsTerminalPhase(controller.Phase))
            {
                float ow = narrow ? Mathf.Min(300, playX - 32) : 420, ox = narrow ? 16 : (w - ow) / 2, oy = narrow ? 84 : 86;
                if (narrow) oy = 84;
                ui.Panel("objective", new Rect(ox, oy, ow, 62), new Color32(7, 15, 22, 225), false, WarSandboxUGUI.Amber);
                string status = controller.IsControlPointContested ? "争夺中" : controller.ControlPointOwnerTeamId < 0 ? "未占领" : FormatTeamName(controller.ControlPointOwnerTeamId) + "占领 " + Mathf.RoundToInt(controller.ControlPointCaptureProgress * 100) + "%";
                ui.LabelAligned("objective-title", new Rect(ox + 2, oy + 2, ow - 4, 24), "中央据点  /  " + status, 15, WarSandboxUGUI.Amber, true, TextAnchor.MiddleLeft);
                ui.LabelAligned("objective-counts", new Rect(ox + 2, oy + 24, ow - 4, 22), FormatControlPointCounts(), 12, WarSandboxUGUI.Muted, false, TextAnchor.MiddleLeft);
                ui.Panel("objective-track", new Rect(ox + 12, oy + 50, ow - 24, 4), WarSandboxUGUI.Line, false);
                ui.Panel("objective-progress", new Rect(ox + 12, oy + 50, (ow - 24) * Mathf.Clamp01(controller.ControlPointCaptureProgress), 4),
                    controller.ControlPointOwnerTeamId >= 0 ? WarSandboxTeamPalette.Resolve(controller.ControlPointOwnerTeamId) : WarSandboxUGUI.Amber, false);
            }

            // ---- Bottom: command bar ----
            float barNeed = 676, barLeft = narrow ? 16 : 16 + ColumnWidth + 16, barRight = narrow ? w - 16 : w - 16 - 200 - 16;
            float unit = Mathf.Min(1, (barRight - barLeft) / barNeed), barW = barNeed * unit, barX = barLeft + (barRight - barLeft - barW) / 2, barY = h - 80;
            var bar = new Rect(barX, barY, barW, 64);
            ui.Panel("command-bar", bar); ui.Brackets("command-bar-br", bar, null, 10);
            float bx2 = barX + 8 * unit;
            string startKey = IsPreOrPostBattle(controller.Phase) ? "ENTER" : "SPACE";
            ui.Button("start", new Rect(bx2, barY + 8, 120 * unit, 48), StartLabel(), ToggleBattleFromUI, true, ruleOk);
            ui.Chip("start-key", new Rect(bx2 + 120 * unit - 44, barY + 10, 40, 12), startKey, new Color(0, 0, 0, 0), WarSandboxUGUI.Deep, null, 9);
            bx2 += 128 * unit;
            CommandButton(ui, "attack", ref bx2, barY, unit, "进攻", "A", IssueAttack, canOrder, false);
            CommandButton(ui, "move", ref bx2, barY, unit, awaitingMoveTarget ? "选点中…" : "移动", "M", BeginMoveOrder, canOrder, awaitingMoveTarget);
            CommandButton(ui, "hold", ref bx2, barY, unit, "防守", "H", IssueHold, canOrder, false);
            CommandButton(ui, "retreat", ref bx2, barY, unit, "撤退", "R", IssueRetreat, canOrder, false);
            ui.Panel("command-sep", new Rect(bx2 + 2 * unit, barY + 12, 1, 40), WarSandboxUGUI.Line, false); bx2 += 12 * unit;
            CommandButton(ui, "follow", ref bx2, barY, unit, "跟随", "F", () => FocusArmy(controller.selectedTeam), true, false);
            CommandButton(ui, "panorama", ref bx2, barY, unit, "全景", "F3", FocusBattlefield, true, false);

            // ---- Left: feedback panel, selection card, army list ----
            float fy = narrow ? h - 202 : h - 126, fw = Mathf.Min(ColumnWidth, w - 32);
            var battleFeedback = controller.GetComponent<WarSandboxBattleFeedback>();
            bool hasBattleFeedback = battleFeedback != null && Time.unscaledTime < battleFeedback.MessageUntil;
            bool paused = controller.Phase == WarSandboxBattlePhase.Paused;
            bool hasFeedback = awaitingMoveTarget || paused || Time.unscaledTime < feedbackUntil || !string.IsNullOrEmpty(controller.CommandError) || hasBattleFeedback;
            var feedbackRect = new Rect(16, fy, fw, 110);
            ui.Panel("feedback", feedbackRect, null, true, awaitingMoveTarget ? WarSandboxUGUI.Accent : (Color?)null);
            {
                string title = awaitingMoveTarget ? "为 " + FormatTeamName(controller.selectedTeam) + " 选择目标" :
                    paused ? "战斗已暂停 · Space 继续" : "战况 · 操作反馈";
                string feedback = awaitingMoveTarget ? MoveTargetHint() : Time.unscaledTime < feedbackUntil ? commandFeedback :
                    controller.CommandError ?? (paused ? "新下达活动命令会继续运行；追加已有航点不会" : hasBattleFeedback ? battleFeedback.Message : "");
                float textWidth = awaitingMoveTarget ? fw - 104 : fw - 4;
                ui.LabelAligned("feedback-title", new Rect(18, fy + 6, textWidth, 26), title, 15, awaitingMoveTarget || paused ? WarSandboxUGUI.Accent : WarSandboxUGUI.Muted, true, TextAnchor.MiddleLeft);
                ui.Label("feedback-text", new Rect(18, fy + 36, fw - 4, 68), hasFeedback ? feedback : "等待指令", 13,
                    !string.IsNullOrEmpty(controller.CommandError) ? WarSandboxUGUI.Danger : hasFeedback ? WarSandboxUGUI.Ink : WarSandboxUGUI.Dim);
                if (awaitingMoveTarget)
                    ui.Button("move-cancel", new Rect(16 + fw - 98, fy + 6, 90, 28), "取消 Esc", CancelMoveTarget);
            }
            float listTop = narrow ? 132 : point ? 96 : 88;
            if (narrow)
                ui.Button("orders-toggle", new Rect(16, point ? 152 : 90, 148, 30), compactOrders ? "展开指挥面板" : "收起指挥面板", () => compactOrders = !compactOrders, false, true, compactOrders);
            if (narrow && point) listTop = 190;
            bool showPanel = !narrow || !compactOrders;
            if (showPanel)
            {
                DrawSelectedOrder(ui, 16, listTop, fw);
                float scrollTop = listTop + 108, scrollBottom = fy - 8;
                float rows = 0; for (int team = 0; team < controller.ArmyCount; team++) { var a = controller.GetArmy(team); if (a != null && a.initialUnitCount > 0) rows++; }
                if (scrollBottom - scrollTop >= 60)
                {
                    ui.Panel("orders-bg", new Rect(16, scrollTop, fw, scrollBottom - scrollTop));
                    ui.Scroll("orders-scroll", new Rect(17, scrollTop + 1, fw - 2, scrollBottom - scrollTop - 2), 34 + rows * 58 + 4);
                    float cw = fw - 2, y = 0;
                    ui.Code("army-heading", new Rect(4, y, cw - 8, 30), "FORCES // 军团  ·  1–9", 12, WarSandboxUGUI.Accent); y += 34;
                    for (int team = 0; team < controller.ArmyCount; team++)
                    {
                        int id = team; var army = controller.GetArmy(team); if (army == null || army.initialUnitCount <= 0) continue;
                        bool selected = controller.selectedTeam == id; int alive = controller.GetAliveUnitCount(id); Color color = WarSandboxTeamPalette.Resolve(id);
                        string k = "army-" + id; var row = new Rect(8, y, cw - 16, 52);
                        ui.TintButton(k, row, "", () => { controller.SelectArmy(id); nextUiRefresh = 0; }, selected ? WarSandboxUGUI.Soft : CardFill, WarSandboxUGUI.Ink);
                        ui.Panel("army-color-" + id, new Rect(row.x, row.y, 4, row.height), color, false);
                        if (selected) ui.Panel(k + "-sel", new Rect(row.xMax - 2, row.y, 2, row.height), WarSandboxUGUI.Accent, false);
                        ui.Code(k + "-key", new Rect(row.x + 6, row.y + 2, 28, 26), (id + 1).ToString(), 12, selected ? WarSandboxUGUI.Accent : WarSandboxUGUI.Dim);
                        ui.LabelAligned(k + "-name", new Rect(row.x + 20, row.y + 2, row.width - 120, 26), (selected ? "已选 · " : "") + FormatTeamName(id), 15, WarSandboxUGUI.Ink, selected, TextAnchor.MiddleLeft);
                        ui.Code(k + "-count", new Rect(row.xMax - 140, row.y + 2, 136, 26), alive.ToString("N0") + " / " + army.initialUnitCount.ToString("N0"), 12, selected ? WarSandboxUGUI.Accent : WarSandboxUGUI.Muted, TextAnchor.MiddleRight);
                        float ratio = Mathf.Clamp01(alive / (float)Mathf.Max(1, army.initialUnitCount)), track = row.width - 40;
                        ui.Panel(k + "-track", new Rect(row.x + 28, row.y + 36, track, 4), WarSandboxUGUI.Line, false);
                        ui.Panel(k + "-fill", new Rect(row.x + 28, row.y + 36, track * ratio, 4), color, false);
                        string order = army.hasOrder ? FormatOrder(army.currentOrder.type) : "待命";
                        ui.LabelAligned(k + "-order", new Rect(row.xMax - 120, row.y + 38, 116, 16), order, 10, WarSandboxUGUI.Dim, false, TextAnchor.MiddleRight);
                        y += 58;
                    }
                    ui.EndScroll();
                }
            }
            else
            {
                ui.Panel("compact-status", new Rect(16, (point ? 152 : 90) + 38, fw, 34));
                ui.LabelAligned("compact-status-text", new Rect(18, (point ? 152 : 90) + 40, fw - 4, 30), SelectedArmyTitle() + " / " +
                    (controller.SelectedArmy != null && controller.SelectedArmy.hasOrder ? FormatOrder(controller.SelectedArmy.currentOrder.type) : "等待命令"), 14, WarSandboxUGUI.Ink, true, TextAnchor.MiddleLeft);
            }

            // ---- Bottom right: minimap ----
            DrawUGUIMinimap(ui, w, h, rightBottom + 12, narrow ? barY - 12 : h - 16, narrow ? feedbackRect.xMax + 16 : barX + barW + 16);
            if (controller.BattleResult.valid) DrawUGUIResult(ui);
            ui.End();
        }
        private void CommandButton(WarSandboxUGUI ui, string id, ref float x, float barY, float unit, string label, string key, System.Action click, bool enabled, bool selected)
        {
            float bw = 84 * unit;
            ui.Button(id, new Rect(x, barY + 8, bw, 48), label, click, false, enabled, selected);
            ui.Chip(id + "-key", new Rect(x + bw - 22, barY + 10, 20, 12), key, new Color(0, 0, 0, 0), enabled ? WarSandboxUGUI.Accent : WarSandboxUGUI.Dim, null, 9);
            x += bw + 4 * unit;
        }
        private void DrawForceBar(WarSandboxUGUI ui, Rect bar, float labelY)
        {
            int count = ResolveArmyCount(); float total = 0;
            for (int team = 0; team < count; team++) { var a = controller.GetArmy(team); if (a != null && a.initialUnitCount > 0) total += Mathf.Max(0, controller.GetAliveUnitCount(team)); }
            ui.Panel("force-track", bar, WarSandboxUGUI.Line, false);
            float x = bar.x; int shown = 0;
            for (int team = 0; team < count; team++) { var a = controller.GetArmy(team); if (a != null && a.initialUnitCount > 0) shown++; }
            float labelW = bar.width / Mathf.Max(1, shown); int slot = 0;
            for (int team = 0; team < count; team++)
            {
                var army = controller.GetArmy(team); if (army == null || army.initialUnitCount <= 0) continue;
                int alive = Mathf.Max(0, controller.GetAliveUnitCount(team)); float width = total > 0 ? bar.width * alive / total : 0; Color color = WarSandboxTeamPalette.Resolve(team);
                if (width > 0) ui.Panel("force-seg-" + team, new Rect(x, bar.y, Mathf.Max(1, width - 1), bar.height), color, false);
                x += width;
                var anchor = shown == 1 ? TextAnchor.MiddleCenter : slot == 0 ? TextAnchor.MiddleLeft : slot == shown - 1 ? TextAnchor.MiddleRight : TextAnchor.MiddleCenter;
                ui.Code("force-label-" + team, new Rect(bar.x + labelW * slot + (anchor == TextAnchor.MiddleLeft ? -2 : 0), labelY, labelW - (anchor == TextAnchor.MiddleRight ? 4 : 0), 20),
                    FormatTeamName(team) + " " + alive.ToString("N0"), 11, color, anchor);
                slot++;
            }
        }
        private void DrawUGUIResult(WarSandboxUGUI ui)
        {
            var result = controller.BattleResult;
            bool result27 = BattleResult27.Active(controller);
            int count = result.ArmyCount, rows = (count + 1) / 2, visibleRows = Mathf.Min(rows, 2);
            float w = Mathf.Min(640, ui.Width - 40), x = (ui.Width - w) / 2, cellH = 88;
            float height = Mathf.Min(ui.Height - 40, 130 + visibleRows * (cellH + 8) + 64 + (result27 ? 60 : 0)), y = (ui.Height - height) / 2;
            ui.Panel("result-shade", new Rect(0, 0, ui.Width, ui.Height), WarSandboxUGUI.Shade, true);
            var card = new Rect(x, y, w, height);
            ui.Panel("result-card", card); ui.Brackets("result-card-br", card);
            ui.Code("result-code", new Rect(x + 24, y + 16, w - 48, 20), "AFTER-ACTION // 战后报告", 12, WarSandboxUGUI.Accent);
            bool decided = result.winnerTeamId >= 0;
            ui.LabelAligned("result-title", new Rect(x + 14, y + 38, w - 28, 54), FormatResultTitle(result), 38, decided ? WarSandboxUGUI.Ink : WarSandboxUGUI.Amber, true, TextAnchor.MiddleLeft);
            ui.LabelAligned("result-reason", new Rect(x + 14, y + 94, w - 28, 26), FormatVictoryReason(result.victoryReason) + "  ·  " + FormatBattleTime(result.battleSeconds) +
                (CustomStatsActive() ? "  ·  自定义数值" : ""), 15, WarSandboxUGUI.Muted, false, TextAnchor.MiddleLeft);
            float gridTop = y + 130, gridH = height - 130 - 64 - (result27 ? 60 : 0), gw = w - 48, colW = (gw - 8) / 2;
            ui.Scroll("result-armies", new Rect(x + 24, gridTop, gw, gridH), rows * (cellH + 8));
            string[] headings = { "初始", "存活", "损失" };
            for (int i = 0; i < count; i++)
            {
                var army = result.GetArmy(i); string key = "result-army-" + army.teamId;
                float cx = (i % 2) * (colW + 8), cy = (i / 2) * (cellH + 8); bool winner = army.teamId == result.winnerTeamId; Color color = WarSandboxTeamPalette.Resolve(army.teamId);
                ui.Panel(key + "-cell", new Rect(cx, cy, colW, cellH), CardFill, false, winner ? WarSandboxUGUI.Amber : WarSandboxUGUI.Line);
                ui.Panel(key + "-color", new Rect(cx, cy, 4, cellH), color, false);
                ui.LabelAligned(key, new Rect(cx + 6, cy + 4, colW - 70, 28), army.displayName, 16, WarSandboxUGUI.Ink, winner, TextAnchor.MiddleLeft);
                if (winner) ui.Chip(key + "-win", new Rect(cx + colW - 52, cy + 10, 42, 16), "WIN", WarSandboxUGUI.Amber, WarSandboxUGUI.Deep, null, 10);
                float sw = (colW - 16) / 3;
                for (int s = 0; s < 3; s++)
                {
                    ui.LabelAligned(key + "-h" + s, new Rect(cx + 6 + s * sw, cy + 34, sw, 18), headings[s], 11, WarSandboxUGUI.Muted, false, TextAnchor.MiddleLeft);
                    string value = s == 0 ? army.initial.ToString("N0") : s == 1 ? army.survivors.ToString("N0") : army.Casualties.ToString("N0");
                    ui.Code(key + (s == 0 ? "-initial" : s == 1 ? "-alive" : "-losses"), new Rect(cx + 6 + s * sw, cy + 52, sw, 30), value, 19,
                        s == 2 ? WarSandboxUGUI.Danger : s == 1 ? WarSandboxUGUI.Ink : WarSandboxUGUI.Muted);
                }
            }
            ui.EndScroll();
            float by = y + height - 58, bw = (w - 48 - 16) / 3;
            if (result27) ui.Label("result-next-note", new Rect(x + 24, by - 56, w - 48, 52), "再来一局：沿用本局已应用编成和布阵，立即重新开战。\n返回布阵：保留配置继续修改；战斗进度和路线不保留。", 14, WarSandboxUGUI.Muted);
            ui.Button("result-restart", new Rect(x + 24, by, bw, 42), result27 ? "沿用配置再战" : "再来一局", StartOrRestartDefaultBattle, true);
            ui.Chip("result-restart-key", new Rect(x + 24 + bw - 46, by + 4, 42, 12), "ENTER", new Color(0, 0, 0, 0), WarSandboxUGUI.Deep, null, 9);
            var resultEditor = controller.GetComponent<WarSandboxDeploymentHUD>();
            ui.Button("result-edit", new Rect(x + 32 + bw, by, bw, 42), result27 ? "调整编成布阵" : "返回布阵", () => resultEditor.RequestEdit(), false, resultEditor != null);
            var session = WarSandboxSceneSession.Instance;
            ui.Button("result-menu", new Rect(x + 40 + bw * 2, by, bw, 42), "选择其他战场", () => PlanState28.RequestCatalog(session), false, session != null);
        }
        private void DrawUGUIMinimap(WarSandboxUGUI ui, float screenWidth, float screenHeight, float minimumY, float bottom, float minimumX)
        {
            if (!showMinimap || !TryResolveSimulationWorldSize(out var world)) return;
            float size = Mathf.Min(200, bottom - 28 - minimumY, screenWidth - 16 - minimumX), x = screenWidth - 16 - size, y = bottom - size - 28;
            if (size < 120) return;
            var frame = new Rect(x, y, size, size + 28);
            ui.Panel("mini-bg", frame); ui.Brackets("mini-br", frame, null, 10);
            ui.Code("mini-title", new Rect(x + 4, y + 2, size - 8, 24), "TACTICAL // 战术视图", 11, WarSandboxUGUI.Muted);
            Rect map = new Rect(x + 8, y + 26, size - 16, size - 8);
            ui.Panel("mini-surface", map, WarSandboxUGUI.FieldFill, false, WarSandboxUGUI.Line);
            for (int i = 1; i < 4; i++)
            {
                ui.Panel("mini-grid-v" + i, new Rect(map.x + map.width * i / 4, map.y, 1, map.height), new Color32(20, 38, 50, 255), false);
                ui.Panel("mini-grid-h" + i, new Rect(map.x, map.y + map.height * i / 4, map.width, 1), new Color32(20, 38, 50, 255), false);
            }
            for (int i = 0; i < controller.GetStaticObstacleCount(); i++)
            {
                if (!controller.TryGetStaticObstacle(i, out var obstacle)) continue;
                var b = obstacle.Bounds;
                Vector2 a = WarSandboxMinimapProjection.WorldToMap(new Vector3(b.xMin, 0, b.yMin), world, map), c = WarSandboxMinimapProjection.WorldToMap(new Vector3(b.xMax, 0, b.yMax), world, map);
                ui.Panel("mini-wall-" + i, Rect.MinMaxRect(Mathf.Min(a.x, c.x), Mathf.Min(a.y, c.y), Mathf.Max(a.x, c.x), Mathf.Max(a.y, c.y)), WarSandboxUGUI.Dim, false);
            }
            if (controller.gameMode == WarSandboxGameMode.ControlPoint)
            {
                var p = WarSandboxMinimapProjection.WorldToMap(controller.controlPointCenter, world, map);
                ui.Panel("mini-objective", new Rect(p.x - 4, p.y - 4, 8, 8), WarSandboxUGUI.Amber, false);
            }
            for (int team = 0; team < controller.ArmyCount; team++)
            {
                if (!TryResolveLiveArmyBounds(team, out var bounds) && !TryResolveArmyBounds(team, out bounds)) continue;
                Vector2 p = WarSandboxMinimapProjection.WorldToMap(bounds.center, world, map); Color color = WarSandboxTeamPalette.Resolve(team);
                ui.Panel("mini-army-" + team, new Rect(p.x - 4, p.y - 4, controller.selectedTeam == team ? 10 : 7, controller.selectedTeam == team ? 10 : 7), color, false,
                    controller.selectedTeam == team ? WarSandboxUGUI.Ink : (Color?)null);
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
                ui.Panel("mini-camera", new Rect(p.x - 2, p.y - 2, 5, 5), WarSandboxUGUI.Accent, false);
            }
            ui.PointerArea("mini-input", map, (point, button, shift) =>
            {
                var target = WarSandboxMinimapProjection.MapToWorld(new Vector2(map.x + point.x * map.width, map.y + point.y * map.height), world, map);
                MinimapCommandP8(target,button,shift);
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

