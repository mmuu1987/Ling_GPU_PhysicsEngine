using System.Collections.Generic;
using MassEngine.Game.Ui;
using UnityEngine;
using UnityEngine.UIElements;
using Tk = MassEngine.Game.Ui.WarSandboxUiToolkit;

namespace MassEngine.Game
{
    /// <summary>
    /// Phase 3 of the UI Toolkit migration: Q 版 battle HUD (title, clock and force bar, objective, member selection, selected
    /// army, army list, feedback, command bar, setup bar, speed/tools, minimap), the after-action report and the reset/end
    /// confirmation. View only — every button calls the same method as the uGUI HUD in WarSandboxCommandHUD.Toy/.UGUI, and every
    /// value shown is one the controller already exposes (no kills, morale or battle log). Hotkeys, world markers and the
    /// selection rectangle are unchanged.
    /// </summary>
    public sealed partial class WarSandboxCommandHUD
    {
        private static readonly float[] TkSpeeds = { .5f, 1f, 2f, 4f };

        private UIDocument tkDoc;
        private VisualElement tkRoot, tkTitle, tkCenter, tkObjective, tkP7, tkRight, tkLeft, tkCmd, tkSetup, tkMini, tkResult, tkDecision;
        private VisualElement tkPhaseDot, tkBar, tkBarLabels, tkDetails, tkSelStripe, tkSelNumbers, tkSelHp, tkFeedback, tkArmyList, tkResRows;
        private Label tkName, tkPhase, tkMode, tkSpeedTag, tkClock, tkObjectiveText, tkP7Title, tkP7State, tkResetText, tkMiniToggleText,
            tkDetailsText, tkSelOrder, tkSelName, tkSelCount, tkSelStart, tkSelPct, tkSelLoss, tkSelDetail, tkFeedbackText, tkStartText,
            tkStartKey, tkMoveText, tkSetupNote, tkRuleError, tkResOp, tkResTitle, tkResSub, tkResTotal, tkResDead, tkResTime, tkResNote,
            tkResEditText, tkResAgainText, tkDecTitle, tkDecBody, tkDecState;
        private Button tkWholeArmy, tkRedeploy, tkEnd, tkFollowS, tkOverviewS, tkHelp, tkTech, tkMiniToggle, tkMoveCancel, tkStart,
            tkAttack, tkMove, tkHold, tkRetreat, tkEdit, tkSetupStart, tkResCatalog, tkResEdit, tkDecConfirm;
        private TacIcon tkStartIcon;
        private TacSeg tkSpeed;
        private TkMinimap tkMinimap;
        private readonly List<int> tkArmyTeams = new List<int>();
        private readonly List<VisualElement> tkArmyCards = new List<VisualElement>();
        private readonly List<VisualElement> tkBarSegs = new List<VisualElement>();
        private readonly List<Label> tkBarLabelList = new List<Label>();
        private readonly List<int> tkBarTeams = new List<int>();
        private string tkArmyKey = "", tkBarKey = "";
        private bool tkResultBuilt;

        private void HideBattleToolkit()
        {
            if (tkRoot != null) Tk.Show(tkRoot, false);
            tkResultBuilt = false;
            TkCompactHidden();
        }

        private void DestroyBattleToolkit()
        {
            if (tkDoc != null) Tk.DestroyDocument(tkDoc);
            tkDoc = null; tkRoot = null;
            TkCompactHidden();
        }

        private void BuildBattleToolkit()
        {
            // Below the front end document (order 10) so the in-battle system strip and its confirmation stay on top.
            tkDoc = Tk.CreateDocument(transform, "Battle HUD UI Toolkit", TkLayoutName(), 0);
            if (tkDoc == null) return;
            var r = tkDoc.rootVisualElement;
            tkRoot = r;
            tkTitle = r.Q("bh-title"); tkCenter = r.Q("bh-center"); tkObjective = r.Q("bh-objective"); tkP7 = r.Q("bh-p7");
            tkRight = r.Q("bh-right"); tkLeft = r.Q("bh-left"); tkCmd = r.Q("bh-cmd"); tkSetup = r.Q("bh-setup"); tkMini = r.Q("bh-mini");
            tkResult = r.Q("bh-result"); tkDecision = r.Q("bh-decision");
            tkPhaseDot = r.Q("bh-phase-dot"); tkBar = r.Q("bh-bar"); tkBarLabels = r.Q("bh-bar-labels"); tkDetails = r.Q("bh-details");
            tkSelStripe = r.Q("bh-sel-stripe"); tkSelNumbers = r.Q("bh-sel-numbers"); tkSelHp = r.Q("bh-sel-hp"); tkFeedback = r.Q("bh-feedback");
            tkArmyList = r.Q<ScrollView>("bh-armies"); tkResRows = r.Q<ScrollView>("res-rows");
            tkName = r.Q<Label>("bh-name"); tkPhase = r.Q<Label>("bh-phase"); tkMode = r.Q<Label>("bh-mode");
            tkSpeedTag = r.Q<Label>("bh-speed-tag"); tkClock = r.Q<Label>("bh-clock"); tkObjectiveText = r.Q<Label>("bh-objective-text");
            tkP7Title = r.Q<Label>("bh-p7-title"); tkP7State = r.Q<Label>("bh-p7-state"); tkResetText = r.Q<Label>("bh-reset-text");
            tkMiniToggleText = r.Q<Label>("bh-mini-toggle-text"); tkDetailsText = r.Q<Label>("bh-details-text");
            tkSelOrder = r.Q<Label>("bh-sel-order"); tkSelName = r.Q<Label>("bh-sel-name"); tkSelCount = r.Q<Label>("bh-sel-count");
            tkSelStart = r.Q<Label>("bh-sel-start"); tkSelPct = r.Q<Label>("bh-sel-pct"); tkSelLoss = r.Q<Label>("bh-sel-loss");
            tkSelDetail = r.Q<Label>("bh-sel-detail"); tkFeedbackText = r.Q<Label>("bh-feedback-text");
            tkStartText = r.Q<Label>("bh-start-text"); tkStartKey = r.Q<Label>("bh-start-key"); tkMoveText = r.Q<Label>("bh-move-text");
            tkSetupNote = r.Q<Label>("bh-setup-note"); tkRuleError = r.Q<Label>("bh-rule-error");
            tkResOp = r.Q<Label>("res-op"); tkResTitle = r.Q<Label>("res-title"); tkResSub = r.Q<Label>("res-sub");
            tkResTotal = r.Q<Label>("res-k-total"); tkResDead = r.Q<Label>("res-k-dead"); tkResTime = r.Q<Label>("res-k-time");
            tkResNote = r.Q<Label>("res-note"); tkResEditText = r.Q<Label>("res-edit-text"); tkResAgainText = r.Q<Label>("res-again-text");
            tkDecTitle = r.Q<Label>("dec-title"); tkDecBody = r.Q<Label>("dec-body"); tkDecState = r.Q<Label>("dec-state");
            tkStartIcon = r.Q<TacIcon>("bh-start-icon"); tkSpeed = r.Q<TacSeg>("bh-speed");

            // Same actions as the uGUI HUD (WarSandboxCommandHUD.Toy / .UGUI / .Selection / .Details26).
            tkWholeArmy = TkOn("bh-whole-army", ClearPlayerSelectionP8);
            tkRedeploy = TkOn("bh-redeploy", () => { battleToolsOpen = false; TkDeployEditor()?.RequestEdit(); });
            TkOn("bh-reset", () => RequestDecision26(BattleDetails26.Decision.Reset));
            tkEnd = TkOn("bh-end", () => RequestDecision26(BattleDetails26.Decision.End));
            tkFollowS = TkOn("bh-follow-s", () => FocusArmy(controller.selectedTeam));
            tkOverviewS = TkOn("bh-overview-s", FocusBattlefield);
            tkHelp = TkOn("bh-help", () =>
            {
                var guide = Guide30.For(WarSandboxSceneSession.Instance);
                if (guide != null) guide.Open(); else { helpOpen = !helpOpen; diagnosticsOpen = false; }
            });
            tkTech = TkOn("bh-tech", () => { diagnosticsOpen = !diagnosticsOpen; helpOpen = false; });
            tkMiniToggle = TkOn("bh-mini-toggle", () => showMinimap = !showMinimap);
            tkMoveCancel = TkOn("bh-move-cancel", CancelMoveTarget);
            tkStart = TkOn("bh-start", ToggleBattleFromUI);
            tkAttack = TkOn("bh-attack", IssueAttack);
            tkMove = TkOn("bh-move", BeginMoveOrder);
            tkHold = TkOn("bh-hold", IssueHold);
            tkRetreat = TkOn("bh-retreat", IssueRetreat);
            TkOn("bh-follow", () => FocusArmy(controller.selectedTeam));
            TkOn("bh-overview", FocusBattlefield);
            tkEdit = TkOn("bh-edit", () => TkDeployEditor()?.RequestEdit());
            tkSetupStart = TkOn("bh-setup-start", ToggleBattleFromUI);
            tkResCatalog = TkOn("res-catalog", () => PlanState28.RequestCatalog(WarSandboxSceneSession.Instance));
            tkResEdit = TkOn("res-edit", () => TkDeployEditor()?.RequestEdit());
            TkOn("res-again", StartOrRestartDefaultBattle);
            TkOn("dec-cancel", CancelDecision26);
            tkDecConfirm = TkOn("dec-confirm", ConfirmDecision26);
            if (tkSpeed != null)
                tkSpeed.changed += k => { if (controller != null && k >= 0 && k < TkSpeeds.Length) controller.SetSimulationSpeed(TkSpeeds[k]); nextUiRefresh = 0; };

            tkMinimap = new TkMinimap(this);
            r.Q("bh-mini-host")?.Add(tkMinimap);

            TkCompactBind(r);
            Tk.HideScrollers(r);
            // Enter/Space are battle hotkeys; a focused button would also fire on them.
            r.Query<Button>().ForEach(b => b.focusable = false);
        }

        private Button TkOn(string name, System.Action action)
        {
            var b = tkRoot.Q<Button>(name);
            if (b != null) b.clicked += () => { action(); nextUiRefresh = 0; };
            return b;
        }

        private WarSandboxDeploymentHUD TkDeployEditor() => controller != null ? controller.GetComponent<WarSandboxDeploymentHUD>() : null;

        /// <summary>Called from RefreshRuntimeUI instead of the uGUI drawing when the UI Toolkit switch is on and the HUD is visible.</summary>
        private void BattleToolkitUpdate()
        {
            TkCompactCheckLayout();
            if (tkDoc == null) BuildBattleToolkit();
            if (tkRoot == null) return;
            Tk.Show(tkRoot, true);
            // Minimap every frame (camera dot, badges); the rest of the HUD at the throttled rate.
            bool mini = showMinimap && !controller.BattleResult.valid && TryResolveSimulationWorldSize(out _); // the result screen hides it (was shown here every frame and hidden again at the throttled refresh: flicker)
            Tk.Show(tkMini, mini);
            if (mini) tkMinimap.Refresh(Time.unscaledTime >= nextUiRefresh);
            TkCompactFrame();
            if (Time.unscaledTime < nextUiRefresh) return;
            nextUiRefresh = Time.unscaledTime + .12f;

            var session = WarSandboxSceneSession.Instance;
            var editor = TkDeployEditor();
            bool setup = controller.Phase == WarSandboxBattlePhase.Setup;
            bool result = controller.BattleResult.valid;

            // ----- title -----
            Tk.SetText(tkName, session != null ? session.CurrentDisplayName : "战争沙盒");
            Tk.SetText(tkPhase, FormatPhase(controller.Phase));
            Tk.SetText(tkMode, controller.gameMode == WarSandboxGameMode.ControlPoint ? "· 据点模式" : "");
            bool running = controller.Phase == WarSandboxBattlePhase.Running, paused = controller.Phase == WarSandboxBattlePhase.Paused;
            tkPhaseDot.EnableInClassList("status-dot--live", running);
            tkPhaseDot.EnableInClassList("status-dot--paused", paused);
            tkPhaseDot.EnableInClassList("status-dot--ok", !running && !paused);

            // ----- member selection (P7/P8), shown on the result screen too, like the uGUI HUD -----
            Tk.Show(tkP7, selectionPreviewEnabled);
            if (selectionPreviewEnabled)
            {
                Tk.Show(tkWholeArmy, playerScopeEnabled);
                Tk.SetText(tkP7Title, playerScopeEnabled ? "左键拖框：当前军团存活成员 · 投影选取（可穿遮挡）" : "P7 开发验证 · 左键拖框 · 投影选取（可穿遮挡）");
                Tk.SetText(tkP7State, SelectionStatusP7() + (playerScopeEnabled ? "\n局部仅支持单目标；Esc取消选点/清选区，旧命令保留" : "\n玩家下令入口禁用；Esc清选区，旧命令保留"));
            }

            Tk.Show(tkResult, result);
            if (result)
            {
                awaitingMoveTarget = false;
                foreach (var e in new[] { tkTimer(), tkObjective, tkRight, tkLeft, tkCmd, tkSetup, tkMini, tkRuleError, tkDecision }) Tk.Show(e, false);
                TkResult(session, editor);
                TkCompactRefresh(true);
                return;
            }
            tkResultBuilt = false;
            Tk.Show(tkTimer(), true); Tk.Show(tkRight, true);

            // ----- clock, speed, force bar, objective -----
            Tk.SetText(tkClock, FormatBattleTime(controller.TelemetrySnapshot.battleSeconds));
            Tk.SetText(tkSpeedTag, "×" + controller.SimulationSpeed.ToString("0.##"));
            TkForceBar();
            bool cp = controller.gameMode == WarSandboxGameMode.ControlPoint;
            Tk.Show(tkObjective, cp);
            if (cp)
            {
                string status = controller.IsControlPointContested ? "争夺中" : controller.ControlPointOwnerTeamId < 0 ? "尚未占领" :
                    FormatTeamName(controller.ControlPointOwnerTeamId) + " · " + Mathf.RoundToInt(controller.ControlPointCaptureProgress * 100) + "%";
                Tk.SetText(tkObjectiveText, "中央据点 / " + status);
            }

            // ----- right: speed, ops, tools, details -----
            int speedIndex = -1;
            for (int i = 0; i < TkSpeeds.Length; i++) if (Mathf.Approximately(controller.SimulationSpeed, TkSpeeds[i])) speedIndex = i;
            if (tkSpeed != null && tkSpeed.index != speedIndex) tkSpeed.index = speedIndex;
            tkRedeploy?.SetEnabled(editor != null);
            Tk.SetText(tkResetText, Details26 != null ? "重置本局" : "原样重开");
            tkEnd?.SetEnabled(!setup);
            // Follow/overview live on the command bar during battle; in setup they sit with the tools, as in the uGUI tools card.
            Tk.Show(tkFollowS, setup); Tk.Show(tkOverviewS, setup);
            tkHelp?.EnableInClassList("btn--on", helpOpen);
            tkTech?.EnableInClassList("btn--on", diagnosticsOpen);
            tkMiniToggle?.EnableInClassList("btn--on", showMinimap);
            Tk.SetText(tkMiniToggleText, showMinimap ? "收起小地图" : "展开小地图");
            Tk.Show(tkDetails, helpOpen || diagnosticsOpen);
            if (helpOpen || diagnosticsOpen) Tk.SetText(tkDetailsText, helpOpen ? HelpText() : DiagnosticsText());

            // ----- left column (battle only, like the uGUI team tabs / order status / feedback) -----
            Tk.Show(tkLeft, !setup);
            Tk.Show(tkCmd, !setup);
            Tk.Show(tkSetup, setup);
            if (setup)
            {
                Tk.SetText(tkSetupNote, PlanState28.SetupText(controller, FlowPolish23.Active(controller) ? "布阵已就绪 · 可继续修改，点击右侧开始战斗" : "战前准备 · 调整军团，或使用当前部署直接开战"));
                tkEdit?.SetEnabled(editor != null);
                tkSetupStart?.SetEnabled(string.IsNullOrEmpty(controller.BattlefieldRuleError));
            }
            else
            {
                TkSelected();
                TkArmies();
                TkFeedback();
                TkCommands();
            }

            // ----- rule error, confirmation -----
            Tk.Show(tkRuleError, !string.IsNullOrEmpty(controller.BattlefieldRuleError));
            Tk.SetText(tkRuleError, controller.BattlefieldRuleError);
            TkDecision();
            TkCompactRefresh(false);
        }

        private VisualElement tkTimerCache;
        private VisualElement tkTimer() => tkTimerCache ??= tkRoot.Q("bh-timer");

        private void TkForceBar()
        {
            int count = ResolveArmyCount();
            string key = "";
            for (int team = 0; team < count; team++) { var a = controller.GetArmy(team); if (a != null && a.initialUnitCount > 0) key += team + ","; }
            if (key != tkBarKey)
            {
                tkBarKey = key; tkBar.Clear(); tkBarLabels.Clear(); tkBarSegs.Clear(); tkBarLabelList.Clear(); tkBarTeams.Clear();
                for (int team = 0; team < count; team++)
                {
                    var a = controller.GetArmy(team); if (a == null || a.initialUnitCount <= 0) continue;
                    Color color = WarSandboxTeamPalette.Resolve(team);
                    var seg = Tk.E("bh-bar__seg"); seg.style.backgroundColor = color; seg.pickingMode = PickingMode.Ignore;
                    var label = Tk.L("", "bh-bar-label"); label.style.color = color;
                    tkBar.Add(seg); tkBarLabels.Add(label); tkBarSegs.Add(seg); tkBarLabelList.Add(label); tkBarTeams.Add(team);
                }
            }
            for (int i = 0; i < tkBarTeams.Count; i++)
            {
                int team = tkBarTeams[i], alive = Mathf.Max(0, controller.GetAliveUnitCount(team));
                tkBarSegs[i].style.flexGrow = alive;
                Tk.SetText(tkBarLabelList[i], FormatTeamName(team) + " " + alive.ToString("N0"));
            }
        }

        private static string TkOrderClass(ArmyOrderType type)
        {
            switch (type)
            {
                case ArmyOrderType.Move: return "chip--order-move";
                case ArmyOrderType.Hold: return "chip--order-hold";
                case ArmyOrderType.Retreat: return "chip--order-retreat";
                default: return null;
            }
        }

        private static void TkSetOrderChip(Label chip, ArmyRuntimeState army)
        {
            bool has = army != null && army.hasOrder;
            Tk.SetText(chip, has ? FormatOrder(army.currentOrder.type) : "等待命令");
            string cls = has ? TkOrderClass(army.currentOrder.type) : "chip--order-hold";
            chip.EnableInClassList("chip--order-move", cls == "chip--order-move");
            chip.EnableInClassList("chip--order-hold", cls == "chip--order-hold");
            chip.EnableInClassList("chip--order-retreat", cls == "chip--order-retreat");
        }

        private static void TkSetHp(VisualElement fill, float frac)
        {
            fill.style.width = Length.Percent(Mathf.Clamp01(frac) * 100f);
            fill.EnableInClassList("hp__fill--mid", frac < .6f && frac >= .3f);
            fill.EnableInClassList("hp__fill--low", frac < .3f);
        }

        private void TkSelected()
        {
            var army = controller.SelectedArmy;
            Tk.SetText(tkSelName, army != null ? FormatTeamName(controller.selectedTeam) : SelectedArmyTitle());
            tkSelStripe.style.backgroundColor = army != null ? WarSandboxTeamPalette.Resolve(controller.selectedTeam) : new Color(0, 0, 0, 0);
            Tk.Show(tkSelOrder, army != null);
            Tk.Show(tkSelNumbers, army != null);
            if (army != null)
            {
                TkSetOrderChip(tkSelOrder, army);
                int alive = Mathf.Max(0, controller.GetAliveUnitCount(controller.selectedTeam)), initial = Mathf.Max(0, army.initialUnitCount);
                float frac = initial > 0 ? (float)alive / initial : 0f;
                Tk.SetText(tkSelCount, alive.ToString("N0"));
                Tk.SetText(tkSelStart, "/ " + initial.ToString("N0"));
                Tk.SetText(tkSelPct, Mathf.RoundToInt(frac * 100) + "%");
                TkSetHp(tkSelHp, frac);
                Tk.SetText(tkSelLoss, Mathf.Max(0, initial - alive).ToString("N0"));
            }
            Tk.SetText(tkSelDetail, SelectedOrderDetail());
        }

        private void TkArmies()
        {
            string key = "";
            for (int i = 0; i < controller.ArmyCount; i++) { var a = controller.GetArmy(i); if (a != null && a.initialUnitCount > 0) key += i + ","; }
            if (key != tkArmyKey)
            {
                tkArmyKey = key; tkArmyList.Clear(); tkArmyTeams.Clear(); tkArmyCards.Clear();
                for (int i = 0; i < controller.ArmyCount; i++)
                {
                    var a = controller.GetArmy(i); if (a == null || a.initialUnitCount <= 0) continue;
                    int team = i;
                    var card = Tk.E("army-card");
                    var stripe = Tk.E("army-card__stripe"); stripe.style.backgroundColor = WarSandboxTeamPalette.Resolve(team);
                    card.Add(stripe);
                    card.Add(Tk.L(team < 9 ? (team + 1).ToString() : "", "army-card__key"));
                    var body = Tk.E("army-card__body");
                    var row = Tk.E("row");
                    var name = Tk.L("", "army-card__name"); name.name = "name";
                    var chip = Tk.L("", "chip", "chip--order"); chip.name = "order";
                    row.Add(name); row.Add(Tk.E("spacer")); row.Add(chip);
                    var hp = Tk.E("hp"); var fill = Tk.E("hp__fill"); fill.name = "hp"; hp.Add(fill);
                    body.Add(row); body.Add(hp);
                    card.Add(body);
                    var count = Tk.L("", "army-card__count"); count.name = "count";
                    card.Add(count);
                    card.RegisterCallback<ClickEvent>(_ => { controller.SelectArmy(team); nextUiRefresh = 0; });
                    tkArmyList.Add(card); tkArmyTeams.Add(team); tkArmyCards.Add(card);
                }
            }
            for (int i = 0; i < tkArmyCards.Count; i++)
            {
                int team = tkArmyTeams[i]; var card = tkArmyCards[i]; var army = controller.GetArmy(team);
                int alive = Mathf.Max(0, controller.GetAliveUnitCount(team)), initial = army != null ? Mathf.Max(0, army.initialUnitCount) : 0;
                card.EnableInClassList("army-card--sel", controller.selectedTeam == team);
                card.EnableInClassList("army-card--dead", alive <= 0);
                Tk.SetText(card.Q<Label>("name"), FormatTeamName(team) + (controller.ChallengeActive && team == controller.ChallengeTeam ? " · 你" : ""));
                TkSetOrderChip(card.Q<Label>("order"), army);
                TkSetHp(card.Q("hp"), initial > 0 ? (float)alive / initial : 0f);
                Tk.SetText(card.Q<Label>("count"), alive.ToString("N0"));
            }
        }

        private void TkFeedback()
        {
            // Same text selection as the uGUI HUD.
            string feedback = awaitingMoveTarget ? MoveTargetHint() : controller.CommandError ?? (Time.unscaledTime < feedbackUntil ? commandFeedback : "");
            if (FlowPolish23.Active(controller) && controller.Phase == WarSandboxBattlePhase.Paused && !awaitingMoveTarget && string.IsNullOrEmpty(controller.CommandError))
                feedback = "战斗已暂停 · 下达命令会恢复战斗；只查看信息不会继续";
            if (!awaitingMoveTarget && string.IsNullOrEmpty(controller.CommandError) && controller.SelectedArmy != null && !controller.CanCommandTeam(controller.selectedTeam))
                feedback = "挑战模式 · 这是电脑指挥的军团，只能查看\n点军团卡切回你指挥的" + WarSandboxBattleController.DefaultArmyName(controller.ChallengeTeam);
            feedback = FeedbackText26(FeedbackText25(feedback));
            bool show = !string.IsNullOrEmpty(feedback);
            Tk.Show(tkFeedback, show);
            if (!show) return;
            Tk.SetText(tkFeedbackText, feedback);
            tkFeedbackText.EnableInClassList("bh-feedback__t--bad", Feedback25 != null ? Feedback25.Rejected : !string.IsNullOrEmpty(controller.CommandError));
            Tk.Show(tkMoveCancel, awaitingMoveTarget);
        }

        private void TkCommands()
        {
            string label = BattleActionLabel(); int split = label.IndexOf("  ", System.StringComparison.Ordinal);
            Tk.SetText(tkStartText, split > 0 ? label.Substring(0, split) : label);
            Tk.SetText(tkStartKey, split > 0 ? label.Substring(split).Trim() : "");
            bool running = controller.Phase == WarSandboxBattlePhase.Running;
            string icon = running ? "pause" : "play";
            if (tkStartIcon != null && tkStartIcon.icon != icon) tkStartIcon.icon = icon;
            tkStart.EnableInClassList("cmd--paused", controller.Phase == WarSandboxBattlePhase.Paused);
            bool orders = (!selectionPreviewEnabled || playerScopeEnabled) && !PendingDecision26 && controller.SelectedArmy != null && controller.GetAliveUnitCount(controller.selectedTeam) > 0 && controller.CanCommandTeam(controller.selectedTeam);
            tkAttack.SetEnabled(orders); tkMove.SetEnabled(orders); tkHold.SetEnabled(orders); tkRetreat.SetEnabled(orders);
            tkMove.EnableInClassList("cmd--on", awaitingMoveTarget);
            Tk.SetText(tkMoveText, awaitingMoveTarget ? "选择落点…" : "移动");
        }

        private void TkDecision()
        {
            Tk.Show(tkDecision, PendingDecision26);
            if (!PendingDecision26) return;
            bool reset = Details26.Pending == BattleDetails26.Decision.Reset;
            Tk.SetText(tkDecTitle, reset ? "确认重置本局？" : "确认结束本局？");
            Tk.SetText(tkDecBody, reset ? "清除当前命令、路线和战斗进度。\n恢复本局初始配置，回到战前准备。" : "立即结束本局，不判定胜负。\n这不是暂停，不能继续当前这一局。");
            Tk.SetText(tkDecState, controller.Phase == WarSandboxBattlePhase.Running ? "当前仍在交战，弹窗不会自动暂停" :
                controller.Phase == WarSandboxBattlePhase.Paused ? "当前保持暂停，取消不会恢复战斗" : "当前为战前准备，确认不会开始战斗");
            if (tkDecConfirm != null && tkDecConfirm.text != (reset ? "确认重置" : "确认结束")) tkDecConfirm.text = reset ? "确认重置" : "确认结束";
        }

        private void TkResult(WarSandboxSceneSession session, WarSandboxDeploymentHUD editor)
        {
            var result = controller.BattleResult;
            bool result27 = BattleResult27.Active(controller);
            Tk.SetText(tkResOp, session != null ? session.CurrentDisplayName : "");
            Tk.SetText(tkResTitle, FormatResultTitle(result));
            tkResTitle.EnableInClassList("result__title--draw", result.winnerTeamId < 0);
            Tk.SetText(tkResSub, FormatVictoryReason(result.victoryReason) + "  ·  " + FormatBattleTime(result.battleSeconds) + (CustomStatsActive() ? "  ·  自定义数值" : ""));
            int total = 0, dead = 0;
            for (int i = 0; i < result.ArmyCount; i++) { var a = result.GetArmy(i); total += a.initial; dead += a.Casualties; }
            Tk.SetText(tkResTotal, total.ToString("N0"));
            Tk.SetText(tkResDead, dead.ToString("N0"));
            Tk.SetText(tkResTime, FormatBattleTime(result.battleSeconds));
            Tk.Show(tkResNote, result27);
            Tk.SetText(tkResEditText, result27 ? "调整编成布阵" : "返回布阵");
            Tk.SetText(tkResAgainText, result27 ? "沿用配置再战" : "再来一局");
            tkResEdit?.SetEnabled(editor != null);
            tkResCatalog?.SetEnabled(session != null);
            if (tkResultBuilt) return;
            tkResultBuilt = true;
            tkResRows.Clear();
            for (int i = 0; i < result.ArmyCount; i++)
            {
                var army = result.GetArmy(i); bool winner = army.teamId == result.winnerTeamId;
                var row = Tk.E("bh-res-row"); row.EnableInClassList("bh-res-row--win", winner);
                var k = Tk.E("bh-res-row__k");
                var sw = Tk.E("bh-res-row__sw"); sw.style.backgroundColor = WarSandboxTeamPalette.Resolve(army.teamId);
                k.Add(sw); k.Add(Tk.L(army.displayName, "bh-res-row__name"));
                if (winner) k.Add(Tk.L("WIN", "chip", "chip--win"));
                row.Add(k);
                row.Add(Tk.L(army.initial.ToString("N0"), "scell"));
                row.Add(Tk.L(army.survivors.ToString("N0"), "scell", "scell--alive"));
                row.Add(Tk.L(army.Casualties.ToString("N0"), "scell", "scell--loss"));
                tkResRows.Add(row);
            }
        }

        /// <summary>
        /// Tactical minimap. Layers, bottom to top: surface/grid/obstacles (Painter2D), army shapes (GPU head-count grid as a
        /// texture, selected army outlined — GpuMinimapDensity), overlay (control point, route dots, camera
        /// dot), numbered army badges (number = army hotkey). Without compute/async readback the shapes layer is
        /// skipped and the badges sit on the army bounds centres, as the dots did. Input is unchanged: left click focuses the
        /// camera, right click (or left while picking a move target) orders a move, Shift queues — all through MinimapCommandP8.
        /// </summary>
        private sealed class TkMinimap : VisualElement
        {
            private static readonly Color Surface = new Color32(0x8F, 0xCB, 0x6A, 0xFF);
            private static readonly Color Grid = new Color(1, 1, 1, .28f);
            private static readonly Color Wall = new Color32(0x6E, 0x86, 0x58, 0xFF);
            private static readonly Color Objective = new Color32(0xFF, 0xA4, 0x1B, 0xFF);
            private static readonly Color Ink = new Color32(0x3B, 0x3F, 0x4C, 0xFF);
            private static readonly Color CameraDot = new Color32(0x2F, 0x9B, 0xEA, 0xFF);
            private readonly WarSandboxCommandHUD hud;
            private readonly VisualElement armyLayer, overlay, badgeLayer;
            private readonly List<Label> badges = new List<Label>();
            private readonly string[] badgeText = new string[GpuMinimapDensity.MaxTeams];
            private GpuMinimapDensity density;
            private bool densityFailed, shapes;
            private int downButton = -1;

            public TkMinimap(WarSandboxCommandHUD hud)
            {
                this.hud = hud;
                AddToClassList("bh-minimap");
                generateVisualContent += Draw;
                armyLayer = Layer("bh-minimap__armies");
                overlay = Layer("bh-minimap__overlay");
                overlay.generateVisualContent += DrawOverlay;
                badgeLayer = Layer("bh-minimap__badges");
                RegisterCallback<PointerDownEvent>(e => downButton = e.button);
                RegisterCallback<PointerUpEvent>(e =>
                {
                    int button = downButton; downButton = -1;
                    if (button != e.button || button < 0 || button > 1 || hud.controller == null) return;
                    if (!hud.TryResolveSimulationWorldSize(out var world)) return;
                    var map = new Rect(0, 0, contentRect.width, contentRect.height);
                    if (!map.Contains(e.localPosition)) return;
                    hud.MinimapCommandP8(WarSandboxMinimapProjection.MapToWorld(e.localPosition, world, map), button, e.shiftKey);
                    hud.nextUiRefresh = 0;
                });
                RegisterCallback<PointerLeaveEvent>(evt => downButton = -1);
                RegisterCallback<DetachFromPanelEvent>(evt => ReleaseDensity());
            }

            private VisualElement Layer(string className)
            {
                var layer = new VisualElement { pickingMode = PickingMode.Ignore };
                layer.AddToClassList(className);
                layer.style.position = Position.Absolute;
                layer.style.left = 0; layer.style.top = 0; layer.style.right = 0; layer.style.bottom = 0;
                Add(layer);
                return layer;
            }

            /// <summary>Every frame while visible. <paramref name="full"/> also repaints the static base (throttled HUD tick).</summary>
            public void Refresh(bool full)
            {
                var controller = hud.controller;
                if (full) MarkDirtyRepaint();
                shapes = UpdateDensity(controller);
                armyLayer.style.display = shapes ? DisplayStyle.Flex : DisplayStyle.None;
                UpdateBadges(controller);
                overlay.MarkDirtyRepaint();
            }

            private bool UpdateDensity(WarSandboxBattleController controller)
            {
                if (controller == null || controller.manager == null || !hud.TryResolveSimulationWorldSize(out var world)) return false;
                if (density != null && !density.IsCurrent) ReleaseDensity(); // the engine reallocated its buffers (new battle)
                if (density == null)
                {
                    if (densityFailed || !GpuMinimapDensity.Supported) return false;
                    var buffers = controller.manager.Buffers;
                    if (buffers == null || !buffers.IsAllocated) return false;
                    try { density = new GpuMinimapDensity(controller.manager); }
                    catch (System.Exception ex) { densityFailed = true; Debug.LogWarning("[Minimap] army shapes unavailable, using dots: " + ex.Message); return false; }
                }
                density.Tick(world, controller.ArmyCount);
                if (density.Fault != null)
                {
                    Debug.LogWarning("[Minimap] army shapes stopped, using dots: " + density.Fault);
                    ReleaseDensity(); densityFailed = true; return false;
                }
                if (density.Build(controller.selectedTeam, WarSandboxTeamPalette.Resolve))
                {
                    armyLayer.style.backgroundImage = new StyleBackground(Background.FromTexture2D(density.Texture));
                    armyLayer.MarkDirtyRepaint();
                }
                return density.HasData;
            }

            private void ReleaseDensity()
            {
                if (density == null) return;
                armyLayer.style.backgroundImage = StyleKeyword.None;
                density.Dispose(); density = null;
            }

            /// <summary>Badge / route anchor: on the army's shape when shapes are on, else the army bounds centre.</summary>
            private bool TryArmyPoint(WarSandboxBattleController controller, int team, out Vector3 point)
            {
                if (shapes && density != null) return density.TryGetAnchor(team, out point);
                point = default;
                if (!hud.TryResolveLiveArmyBounds(team, out var bounds) && !hud.TryResolveArmyBounds(team, out bounds)) return false;
                point = bounds.center;
                return true;
            }

            private Label NextBadge(ref int shown)
            {
                if (shown == badges.Count)
                {
                    var label = new Label { pickingMode = PickingMode.Position };
                    label.AddToClassList("bh-mm-badge");
                    // 2026-10-17: left click on a composition badge selects that composition for command; right click falls through (move order).
                    label.RegisterCallback<PointerDownEvent>(e => { if (e.button == 0 && label.userData is int) e.StopPropagation(); });
                    label.RegisterCallback<PointerUpEvent>(e =>
                    {
                        if (e.button != 0 || !(label.userData is int g)) return;
                        e.StopPropagation(); downButton = -1;
                        if (density != null) hud.SelectMinimapComposition(density, g);
                    });
                    badgeLayer.Add(label); badges.Add(label);
                }
                return badges[shown++];
            }

            /// <summary>2026-10-17: one round badge per composition (number = its place in the army), on its live centroid.</summary>
            private bool UpdateCompositionBadges(WarSandboxBattleController controller, Rect map, Vector2 world, ref int shown)
            {
                if (!shapes || density == null || density.GroupCount == 0) return false;
                Label selectedBadge = null;
                for (int g = 0; g < density.GroupCount; g++)
                {
                    int team = density.GroupTeam(g);
                    if (team < 0 || team >= controller.ArmyCount || !density.TryGetGroupAnchor(g, out var point, out _)) continue;
                    Vector2 pos = WarSandboxMinimapProjection.WorldToMap(point, world, map);
                    var badge = NextBadge(ref shown);
                    bool armySelected = controller.selectedTeam == team, picked = hud.IsMinimapCompositionSelected(g);
                    Color color = WarSandboxTeamPalette.Resolve(team);
                    badge.userData = g;
                    badge.text = density.GroupOrdinal(g).ToString();
                    badge.EnableInClassList("bh-mm-badge--sel", picked);
                    badge.style.borderTopColor = badge.style.borderBottomColor = badge.style.borderLeftColor = badge.style.borderRightColor = picked ? Ink : armySelected ? color * .7f : color;
                    badge.style.backgroundColor = picked ? color : Color.white;
                    badge.style.color = picked ? Color.white : Ink;
                    badge.style.left = pos.x; badge.style.top = pos.y;
                    badge.style.display = DisplayStyle.Flex;
                    if (picked) selectedBadge = badge;
                }
                if (selectedBadge != null && badgeLayer.IndexOf(selectedBadge) != badgeLayer.childCount - 1) selectedBadge.BringToFront();
                return true;
            }

            private void UpdateBadges(WarSandboxBattleController controller)
            {
                int shown = 0;
                var map = new Rect(0, 0, contentRect.width, contentRect.height);
                if (controller != null && map.width >= 4 && map.height >= 4 && hud.TryResolveSimulationWorldSize(out var world)
                    && !UpdateCompositionBadges(controller, map, world, ref shown))
                {
                    for (int team = 0; team < controller.ArmyCount && team < GpuMinimapDensity.MaxTeams; team++)
                    {
                        if (!TryArmyPoint(controller, team, out var point)) continue;
                        Vector2 pos = WarSandboxMinimapProjection.WorldToMap(point, world, map);
                        var badge = NextBadge(ref shown);
                        badge.userData = null;
                        bool selected = controller.selectedTeam == team;
                        Color color = WarSandboxTeamPalette.Resolve(team);
                        badge.text = badgeText[team] ??= (team + 1).ToString();
                        badge.EnableInClassList("bh-mm-badge--sel", selected);
                        badge.style.borderTopColor = badge.style.borderBottomColor = badge.style.borderLeftColor = badge.style.borderRightColor = selected ? Ink : color;
                        badge.style.backgroundColor = selected ? color : Color.white;
                        badge.style.color = selected ? Color.white : Ink;
                        badge.style.left = pos.x; badge.style.top = pos.y;
                        badge.style.display = DisplayStyle.Flex;
                        if (selected && badgeLayer.IndexOf(badge) != badgeLayer.childCount - 1) badge.BringToFront();
                    }
                }
                for (int i = shown; i < badges.Count; i++) badges[i].style.display = DisplayStyle.None;
            }

            private static void FillRect(Painter2D p, Rect r, Color c)
            {
                p.fillColor = c; p.BeginPath();
                p.MoveTo(new Vector2(r.xMin, r.yMin)); p.LineTo(new Vector2(r.xMax, r.yMin)); p.LineTo(new Vector2(r.xMax, r.yMax)); p.LineTo(new Vector2(r.xMin, r.yMax));
                p.ClosePath(); p.Fill();
            }

            private static void Dot(Painter2D p, Vector2 c, float radius, Color fill)
            {
                p.fillColor = fill; p.BeginPath(); p.Arc(c, radius, Angle.Degrees(0), Angle.Degrees(360)); p.ClosePath(); p.Fill();
            }

            private void Draw(MeshGenerationContext mgc)
            {
                var controller = hud.controller;
                var map = new Rect(0, 0, contentRect.width, contentRect.height);
                if (controller == null || map.width < 4 || map.height < 4 || !hud.TryResolveSimulationWorldSize(out var world)) return;
                var p = mgc.painter2D;
                FillRect(p, map, Surface);
                p.strokeColor = Grid; p.lineWidth = 1;
                for (int i = 1; i < 4; i++)
                {
                    float x = map.width * i / 4, y = map.height * i / 4;
                    p.BeginPath(); p.MoveTo(new Vector2(x, 0)); p.LineTo(new Vector2(x, map.height)); p.Stroke();
                    p.BeginPath(); p.MoveTo(new Vector2(0, y)); p.LineTo(new Vector2(map.width, y)); p.Stroke();
                }
                for (int i = 0; i < controller.GetStaticObstacleCount(); i++)
                {
                    if (!controller.TryGetStaticObstacle(i, out var obstacle)) continue;
                    var b = obstacle.Bounds;
                    Vector2 a = WarSandboxMinimapProjection.WorldToMap(new Vector3(b.xMin, 0, b.yMin), world, map), c = WarSandboxMinimapProjection.WorldToMap(new Vector3(b.xMax, 0, b.yMax), world, map);
                    FillRect(p, Rect.MinMaxRect(Mathf.Min(a.x, c.x), Mathf.Min(a.y, c.y), Mathf.Max(a.x, c.x), Mathf.Max(a.y, c.y)), Wall);
                }
            }

            private void DrawOverlay(MeshGenerationContext mgc)
            {
                var controller = hud.controller;
                var map = new Rect(0, 0, contentRect.width, contentRect.height);
                if (controller == null || map.width < 4 || map.height < 4 || !hud.TryResolveSimulationWorldSize(out var world)) return;
                var p = mgc.painter2D;
                var cam = hud.commandCamera;
                if (controller.gameMode == WarSandboxGameMode.ControlPoint)
                {
                    var o = WarSandboxMinimapProjection.WorldToMap(controller.controlPointCenter, world, map);
                    Dot(p, o, 6, Color.white); Dot(p, o, 4.5f, Objective);
                }
                for (int team = 0; team < controller.ArmyCount; team++)
                {
                    int count = controller.GetMoveRoutePointCount(team);
                    if (count <= 0 || !TryArmyPoint(controller, team, out var start)) continue;
                    Color color = WarSandboxTeamPalette.Resolve(team);
                    Vector2 previous = WarSandboxMinimapProjection.WorldToMap(start, world, map);
                    for (int i = 0; i < count; i++)
                    {
                        if (!controller.TryGetMoveRoutePoint(team, i, out var target)) continue;
                        var point = WarSandboxMinimapProjection.WorldToMap(target, world, map);
                        int dots = Mathf.Min(24, Mathf.CeilToInt(Vector2.Distance(previous, point) / 6));
                        for (int d = 0; d < dots; d++) Dot(p, Vector2.Lerp(previous, point, (d + 1f) / Mathf.Max(1, dots)), 1.4f, color);
                        previous = point;
                    }
                }
                if (cam != null)
                {
                    var cpos = WarSandboxMinimapProjection.WorldToMap(cam.transform.position, world, map);
                    Dot(p, cpos, 4f, Color.white); Dot(p, cpos, 2.6f, CameraDot);
                }
            }
        }
    }
}


