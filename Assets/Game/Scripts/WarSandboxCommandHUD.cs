using UnityEngine;

namespace MassEngine.Game
{
    /// <summary>
    /// Immediate-mode vertical-slice UI: army selection, orders, pause/speed/reset and
    /// victory display. Move orders consume the next ground click.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("MassEngine/War Sandbox Command HUD")]
    public sealed partial class WarSandboxCommandHUD : MonoBehaviour
    {
        public WarSandboxBattleController controller;
        public Camera commandCamera;
        public LayerMask groundMask = ~0;
        [Min(1f)] public float maxRayDistance = 2000f;
        [Min(240f)] public float panelWidth = 300f;
        public bool showHotkeys = true;
        [Min(0.1f)] public float cameraFollowSharpness = 5f;
        [Min(0f)] public float liveBoundsPadding = 12f;
        public bool showMinimap = true;
        [Min(96f)] public float minimapSize = 180f;

        // Follow target: one army, or every army at once. The old Attackers/Defenders pair had
        // no way to name a third army; the followed team travels in cameraFocusTeamId instead.
        private enum CameraFocusMode
        {
            None,
            Army,
            All
        }

        /// <summary>Armies per row in the selector and the force readout. Two keeps the old layout.</summary>
        private const int ArmyColumns = 2;

        private bool awaitingMoveTarget;
        private ClickFlowTargetSetter legacyClickSetter;
        private bool legacyClickSetterWasEnabled;
        private MyCameraManager cameraManager;
        private string commandFeedback;
        private float feedbackUntil;
        private CameraFocusMode cameraFocusMode;
        private int cameraFocusTeamId;
        private int lastArmyTapTeam = -1;
        private float lastArmyTapTime;
        private const float ArmyDoubleTapSeconds = 0.35f;
        // Bottom of the panel content measured at the last repaint. The panel box grows to
        // fit this instead of clipping whenever new controls push past the preferred height.
        private float panelContentHeight;

        private void Reset()
        {
            controller = GetComponent<WarSandboxBattleController>();
            commandCamera = Camera.main;
        }

        private void OnEnable()
        {
            ResolveReferences();
            legacyClickSetter = FindFirstObjectByType<ClickFlowTargetSetter>();
            if (legacyClickSetter != null)
            {
                legacyClickSetterWasEnabled = legacyClickSetter.enabled;
                legacyClickSetter.enabled = false;
            }
        }

        private void OnDisable()
        {
            CloseSelectionPreview();
            runtimeUi?.SetVisible(false);
            if (legacyClickSetter != null)
                legacyClickSetter.enabled = legacyClickSetterWasEnabled;
        }

        private void Update()
        {
            UpdateScopedOrdersP8();
            UpdateSelectionPreviewP7();
            RefreshRuntimeUI();
            if (BlockHotkeys26()) return;
            if (WarSandboxUGUI.IsTyping) return;
            if (WarSandboxSceneSession.Instance != null && WarSandboxSceneSession.Instance.InputBlocked) return;
            if (WarSandboxDeploymentHUD.BlocksInput(controller)) return;
            ResolveReferences();
            if (controller == null)
                return;

            bool cameraNavigation =
                Input.GetMouseButton(1) ||
                Input.GetMouseButton(2) ||
                Input.GetKey(KeyCode.LeftAlt) ||
                Input.GetKey(KeyCode.RightAlt) ||
                !Mathf.Approximately(Input.GetAxis("Mouse ScrollWheel"), 0f);

            if (cameraNavigation)
                cameraFocusMode = CameraFocusMode.None;

            // 1..9 (main row or keypad) select by roster position; a quick second tap of the same key also flies
            // the camera there (RTS convention), and Tab / Shift+Tab cycles armies with focus, so more armies never
            // need more F-keys (2026-10-10 feedback; F1/F2 removed).
            int armyCount = ResolveArmyCount();
            int hotkeyArmies = Mathf.Min(armyCount, 9);
            for (int teamId = 0; teamId < hotkeyArmies; teamId++)
            {
                if (!Input.GetKeyDown((KeyCode)((int)KeyCode.Alpha1 + teamId)) &&
                    !Input.GetKeyDown((KeyCode)((int)KeyCode.Keypad1 + teamId)))
                    continue;
                float now = Time.unscaledTime;
                if (teamId == lastArmyTapTeam && now - lastArmyTapTime <= ArmyDoubleTapSeconds)
                {
                    FocusArmy(teamId);
                    lastArmyTapTeam = -1;
                }
                else
                {
                    controller.SelectArmy(teamId);
                    lastArmyTapTeam = teamId;
                    lastArmyTapTime = now;
                }
            }
            if (armyCount > 0 && Input.GetKeyDown(KeyCode.Tab))
            {
                bool back = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                int current = controller.selectedTeam;
                int next = current < 0 || current >= armyCount ? 0 : (current + (back ? armyCount - 1 : 1)) % armyCount;
                controller.SelectArmy(next);
                FocusArmy(next);
                lastArmyTapTeam = -1;
            }
            if (!cameraNavigation)
            {
                if (Input.GetKeyDown(KeyCode.A))
                    PlayerOrderHotkeyP8(KeyCode.A);
                if (Input.GetKeyDown(KeyCode.M))
                    PlayerOrderHotkeyP8(KeyCode.M);
                if (Input.GetKeyDown(KeyCode.H))
                    PlayerOrderHotkeyP8(KeyCode.H);
                if (Input.GetKeyDown(KeyCode.R))
                    PlayerOrderHotkeyP8(KeyCode.R);
            }
            if (Input.GetKeyDown(KeyCode.Space))
                controller.TogglePause();
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
                StartOrRestartDefaultBattle();
            if (Input.GetKeyDown(KeyCode.Escape))
                CancelMoveTarget();
            if (Input.GetKeyDown(KeyCode.F3))
                FocusBattlefield();
            if (Input.GetKeyDown(KeyCode.F))
                FocusArmy(controller.selectedTeam);
            if (Input.GetKeyDown(KeyCode.O) && controller.Phase == WarSandboxBattlePhase.Setup)
                ToggleStaticObstacles();

            UpdateCameraFollow();

            if (!awaitingMoveTarget || !Input.GetMouseButtonDown(0) || IsMouseOverInterface())
                return;

            Camera targetCamera = commandCamera != null ? commandCamera : Camera.main;
            if (targetCamera == null)
                return;

            Ray ray = targetCamera.ScreenPointToRay(Input.mousePosition);
            GroundMoveClickP8(ray, Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift));
        }

        private void OnGUI()
        {
            if (WarSandboxSceneSession.Instance != null && WarSandboxSceneSession.Instance.InputBlocked) return;
            if (WarSandboxDeploymentHUD.BlocksInput(controller)) return;
            ResolveReferences();
            if (controller == null)
                return;

            DrawSelectionRectangleP7();
            DrawWorldOrderMarkers();
        }

        // Kept temporarily as reference while the uGUI presentation is validated.
        // Runtime controls are rendered exclusively by RefreshRuntimeUI.
        private void DrawLegacyControls()
        {
            bool compactLayout = Screen.height < 340f;
            float controlHeight = compactLayout ? 20f : 24f;
            Rect panel = ResolvePanelRect();
            GUI.Box(panel, GUIContent.none);
            GUILayout.BeginArea(new Rect(panel.x + 8f, panel.y + 6f, panel.width - 16f, panel.height - 12f));

            GUILayout.Label("战争沙盒", GUILayout.Height(compactLayout ? 17f : 20f));
            GUILayout.Label("阶段：" + FormatPhase(controller.Phase), GUILayout.Height(compactLayout ? 17f : 20f));
            var deploymentHud = controller.GetComponent<WarSandboxDeploymentHUD>();
            if (deploymentHud != null && GUILayout.Button(controller.Phase == WarSandboxBattlePhase.Setup ? "配兵布阵" : "返回布阵", GUILayout.Height(controlHeight)))
            {
                awaitingMoveTarget = false;
                deploymentHud.RequestEdit();
            }
            if (!string.IsNullOrEmpty(controller.BattlefieldRuleError))
            {
                GUILayout.Label("战场规则无效：\n" + controller.BattlefieldRuleError,
                    new GUIStyle(GUI.skin.label) { wordWrap = true });
                GUILayout.EndArea();
                return;
            }
            DrawForceSummary(compactLayout);

            if (controller.Phase == WarSandboxBattlePhase.Setup && !compactLayout)
            {
                GUILayout.BeginHorizontal();
                bool annihilation = controller.gameMode == WarSandboxGameMode.Annihilation;
                bool controlPoint = controller.gameMode == WarSandboxGameMode.ControlPoint;
                if (GUILayout.Toggle(annihilation, "歼灭战", GUI.skin.button, GUILayout.Height(controlHeight)) && !annihilation)
                    controller.SetGameMode(WarSandboxGameMode.Annihilation);
                if (GUILayout.Toggle(controlPoint, "据点战", GUI.skin.button, GUILayout.Height(controlHeight)) && !controlPoint)
                    controller.SetGameMode(WarSandboxGameMode.ControlPoint);
                GUILayout.EndHorizontal();

                bool obstacleEnabled = controller.staticObstaclesEnabled;
                string obstacleLabel = obstacleEnabled
                    ? "\u9759\u6001\u969c\u788d [O]  \u5df2\u5f00\u542f"
                    : "\u9759\u6001\u969c\u788d [O]  \u5df2\u5173\u95ed";
                bool requestedObstacleState = GUILayout.Toggle(
                    obstacleEnabled, obstacleLabel, GUI.skin.button, GUILayout.Height(controlHeight));
                if (requestedObstacleState != obstacleEnabled)
                    controller.SetStaticObstaclesEnabled(requestedObstacleState);
            }

            DrawArmySelector(controlHeight);

            ArmyRuntimeState selected = controller.SelectedArmy;
            if (selected != null)
            {
                string order = selected.hasOrder ? FormatOrder(selected.currentOrder.type) : "未下令";
                GUILayout.Label(
                    selected.displayName + "  " + selected.initialUnitCount + " 人  |  " + order,
                    GUILayout.Height(compactLayout ? 17f : 20f));
            }

            GUILayout.Space(compactLayout ? 1f : 4f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("进攻 [A]", GUILayout.Height(controlHeight)))
                IssueAttack();
            if (GUILayout.Button(awaitingMoveTarget ? "点击地面…" : "移动 [M]", GUILayout.Height(controlHeight)))
                BeginMoveOrder();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("原地防守 [H]", GUILayout.Height(controlHeight)))
                IssueHold();
            if (GUILayout.Button("撤回出生地 [R]", GUILayout.Height(controlHeight)))
                IssueRetreat();
            GUILayout.EndHorizontal();

            GUILayout.Space(compactLayout ? 1f : 4f);
            GUILayout.BeginHorizontal();
            if (IsPreOrPostBattle(controller.Phase))
            {
                string startLabel = controller.Phase == WarSandboxBattlePhase.Setup
                    ? "双方开战 [Enter]"
                    : "再来一局 [Enter]";
                if (GUILayout.Button(startLabel, GUILayout.Height(controlHeight)))
                    StartOrRestartDefaultBattle();
            }
            else
            {
                string pauseLabel = controller.Phase == WarSandboxBattlePhase.Running ? "暂停 [Space]" : "继续 [Space]";
                if (GUILayout.Button(pauseLabel, GUILayout.Height(controlHeight)))
                    controller.TogglePause();
            }
            if (GUILayout.Button("重开", GUILayout.Height(controlHeight)))
            {
                awaitingMoveTarget = false;
                controller.ResetBattle();
                FocusBattlefield();
                SetFeedback("战场已重置");
            }
            GUILayout.EndHorizontal();

            GUILayout.Label("速度", GUILayout.Height(compactLayout ? 17f : 20f));
            GUILayout.BeginHorizontal();
            DrawSpeedButton("0.5×", 0.5f, controlHeight);
            DrawSpeedButton("1×", 1f, controlHeight);
            DrawSpeedButton("2×", 2f, controlHeight);
            DrawSpeedButton("4×", 4f, controlHeight);
            GUILayout.EndHorizontal();

            if (!compactLayout)
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("跟随选中 [F]", GUILayout.Height(controlHeight)))
                    FocusArmy(controller.selectedTeam);
                if (GUILayout.Button("全景 [F3]", GUILayout.Height(controlHeight)))
                    FocusBattlefield();
                GUILayout.EndHorizontal();
            }

            if (awaitingMoveTarget)
                GUILayout.Label(
                    "下一次左键点击地面将下达移动命令；Esc 取消。",
                    GUILayout.Height(compactLayout ? 17f : 36f));
            else if (Time.unscaledTime < feedbackUntil)
                GUILayout.Label(commandFeedback, GUILayout.Height(compactLayout ? 17f : 20f));
            else if (showHotkeys && !compactLayout)
                GUILayout.Label("数字键选军团(双击聚焦) · Tab轮换 · F跟随 · F3全景 · Enter开战 · A/M/H/R下令");

            // Measure what the layout actually used this repaint (area-local coordinates).
            // BeginArea silently clips everything below its fixed height, so the box must be
            // sized from real content, not from the drifting preferredHeight constant.
            if (Event.current.type == EventType.Repaint)
            {
                Rect content = GUILayoutUtility.GetLastRect();
                if (content.yMax > 100f)
                    panelContentHeight = content.yMax;
            }
            GUILayout.EndArea();
            DrawControlPointStatus();
            DrawBattleResultReport();
        }

        private void IssueAttack()
        {
            if (RejectSelectionOrderP7() || RejectInput26) return;
            if (RoutePlayerOrderP8(ArmyOrderType.Attack,Vector3.zero,false,out _)) return;
            awaitingMoveTarget = false;
            bool paused = controller.Phase == WarSandboxBattlePhase.Paused;
            bool success = controller.IssueOrder(ArmyOrder.Attack(controller.selectedTeam));
            if (success) SetFeedback(FormatTeamName(controller.selectedTeam) + "：进攻");
            else SetFeedback(controller.CommandError ?? "当前无法进攻。");
            RecordResult25(success, "进攻", paused);
        }

        private void BeginMoveOrder()
        {
            if (RejectSelectionOrderP7() || RejectInput26) return;
            if (!CaptureMoveIntentP8()) return;
            if (controller == null || controller.SelectedArmy == null || IsTerminalPhase(controller.Phase)) return;
            awaitingMoveTarget = true;
            nextUiRefresh = 0;
            SetFeedback(FormatTeamName(controller.selectedTeam) + "：请选择移动目标");
        }

        private bool IssueMoveTo(Vector3 target, bool append = false)
        {
            if (RejectSelectionOrderP7() || RejectInput26) return false;
            if (RoutePlayerOrderP8(ArmyOrderType.Move,target,append,out bool scopedAccepted)) return scopedAccepted;
            bool paused = controller.Phase == WarSandboxBattlePhase.Paused;
            ArmyRuntimeState selectedArmy = controller.SelectedArmy;
            bool actuallyAppending = append &&
                                     controller.GetMoveRoutePointCount(controller.selectedTeam) > 0 &&
                                     selectedArmy != null && selectedArmy.hasOrder &&
                                     selectedArmy.currentOrder.type == ArmyOrderType.Move;
            if (!controller.IssueMoveOrder(controller.selectedTeam, target, append))
            {
                SetFeedback(controller.CommandError ?? "当前无法下达移动命令。");
                RecordResult25(false, "移动", paused);
                return false;
            }

            awaitingMoveTarget = false;
            SetFeedback(FormatTeamName(controller.selectedTeam) + (actuallyAppending ? "：已追加路线航点" : "：移动目标已更新"));
            RecordResult25(true, "移动", paused, actuallyAppending);
            return true;
        }

        private void IssueHold()
        {
            if (RejectSelectionOrderP7() || RejectInput26) return;
            if (RoutePlayerOrderP8(ArmyOrderType.Hold,Vector3.zero,false,out _)) return;
            awaitingMoveTarget = false;
            bool paused = controller.Phase == WarSandboxBattlePhase.Paused;
            bool success = controller.IssueOrder(ArmyOrder.Hold(controller.selectedTeam));
            if (success) SetFeedback(FormatTeamName(controller.selectedTeam) + "：原地防守");
            else SetFeedback(controller.CommandError ?? "当前无法防守。");
            RecordResult25(success, "防守", paused);
        }

        private void IssueRetreat()
        {
            if (RejectSelectionOrderP7() || RejectInput26) return;
            if (RoutePlayerOrderP8(ArmyOrderType.Retreat,Vector3.zero,false,out _)) return;
            awaitingMoveTarget = false;
            bool paused = controller.Phase == WarSandboxBattlePhase.Paused;
            bool success = controller.IssueOrder(ArmyOrder.Retreat(controller.selectedTeam));
            if (success) SetFeedback(FormatTeamName(controller.selectedTeam) + "：撤回出生地");
            else SetFeedback(controller.CommandError ?? "当前无法撤退。");
            RecordResult25(success, "撤退", paused);
        }

        private void ToggleStaticObstacles()
        {
            bool enabled = !controller.staticObstaclesEnabled;
            if (controller.SetStaticObstaclesEnabled(enabled))
                SetFeedback(enabled ? "\u9759\u6001\u969c\u788d\u5df2\u5f00\u542f" : "\u9759\u6001\u969c\u788d\u5df2\u5173\u95ed");
            else SetFeedback(controller.CommandError ?? "当前无法修改障碍。");
        }

        private void StartOrRestartDefaultBattle()
        {
            awaitingMoveTarget = false;
            bool started = false;
            if (IsTerminalPhase(controller.Phase))
                started = controller.RestartWithDefaultOrders();
            else if (controller.Phase == WarSandboxBattlePhase.Setup)
                started = controller.StartDefaultBattle();
            if (started)
                SetFeedback("各军团已下达默认命令");
            else SetFeedback(controller.CommandError ?? controller.BattlefieldRuleError ?? "当前无法开始战斗。");
        }

        private void DrawSpeedButton(string label, float speed, float height)
        {
            bool selected = Mathf.Approximately(controller.SimulationSpeed, speed);
            if (GUILayout.Toggle(selected, label, GUI.skin.button, GUILayout.Height(height)) && !selected)
                controller.SetSimulationSpeed(speed);
        }

        private bool IsMouseOverInterface()
        {
            Vector2 guiMouse = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
            return WarSandboxFrontEnd.IsOverNavigation(guiMouse) || WarSandboxUGUI.PointerOverUI();
        }

        private Rect ResolvePanelRect()
        {
            float width = Mathf.Min(Mathf.Max(240f, panelWidth), Mathf.Max(240f, Screen.width - 16f));
            bool compactPanel = Screen.height < 380f;
            // Selector and force readout both lay out ArmyColumns per row, so every extra pair of
            // armies costs two rows. Two armies keep the historical height to the pixel.
            int rosterRows = Mathf.Max(1, (ResolveArmyCount() + ArmyColumns - 1) / ArmyColumns);
            float preferredHeight = (compactPanel ? 322f : 410f) + (rosterRows - 1) * 2f * (compactPanel ? 18f : 24f);
            // Grow to the measured content (6px area top margin + content + 8px bottom padding);
            // still capped by the screen so very short windows fall back to the compact layout
            // instead of drawing off-screen.
            float height = Mathf.Min(
                Mathf.Max(preferredHeight, panelContentHeight + 14f),
                Mathf.Max(200f, Screen.height - 16f));
            return new Rect(Mathf.Max(8f, Screen.width - width - 8f), 8f, width, height);
        }

        private Rect ResolveBattleResultRect()
        {
            return WarSandboxBattleReportLayout.ResolveRect(
                Screen.width, Screen.height, ResolvePanelRect().x, 8f);
        }

        private void DrawBattleResultReport()
        {
            WarSandboxBattleResult result = controller.BattleResult;
            if (!result.valid)
                return;

            Rect panel = ResolveBattleResultRect();
            GUI.Box(panel, GUIContent.none);
            GUILayout.BeginArea(new Rect(panel.x + 12f, panel.y + 8f, panel.width - 24f, panel.height - 16f));

            GUIStyle titleStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                fontSize = 18
            };
            GUILayout.Label(FormatResultTitle(result), titleStyle, GUILayout.Height(26f));
            GUILayout.Label(
                "胜因  " + FormatVictoryReason(result.victoryReason) +
                "    战斗时长  " + FormatBattleTime(result.battleSeconds));
            GUILayout.Label(
                "\u653b\u65b9  \u5e78\u5b58 " + result.attackerSurvivors + "/" + result.attackerInitial +
                "    \u4f24\u4ea1 " + result.AttackerCasualties);
            GUILayout.Label(
                "\u5b88\u65b9  \u5e78\u5b58 " + result.defenderSurvivors + "/" + result.defenderInitial +
                "    \u4f24\u4ea1 " + result.DefenderCasualties);
            GUILayout.Label(
                "\u6d41\u573a\u91cd\u5efa  \u653b " + result.attackerFlowRebuilds +
                "  |  \u5b88 " + result.defenderFlowRebuilds);

            Color previous = GUI.contentColor;
            GUI.contentColor = result.peakGridOverflowPerFrame > 0
                ? new Color(1f, 0.45f, 0.3f)
                : new Color(0.55f, 1f, 0.6f);
            GUILayout.Label("\u7f51\u683c\u6ea2\u51fa\u5cf0\u503c  " + result.peakGridOverflowPerFrame + "/\u5e27");
            GUI.contentColor = previous;

            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("\u518d\u6765\u4e00\u5c40", GUILayout.Height(28f)))
                StartOrRestartDefaultBattle();
            if (GUILayout.Button("\u8fd4\u56de\u90e8\u7f72", GUILayout.Height(28f)))
            {
                awaitingMoveTarget = false;
                controller.ResetBattle();
                FocusBattlefield();
                SetFeedback("\u5df2\u8fd4\u56de\u90e8\u7f72\u9636\u6bb5");
            }
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        // Not static any more: past two armies only winnerTeamId knows who won, and turning
        // that into a name needs the controller's roster.
        private string FormatResultTitle(WarSandboxBattleResult result)
        {
            if (result.phase == WarSandboxBattlePhase.Ended) return "手动结束";
            if (result.winnerTeamId >= 0 && result.TryGetArmy(result.winnerTeamId, out var winner))
                return winner.displayName + "胜利";
            switch (result.phase)
            {
                case WarSandboxBattlePhase.AttackerVictory: return "\u653b\u65b9\u80dc\u5229";
                case WarSandboxBattlePhase.DefenderVictory: return "\u5b88\u65b9\u80dc\u5229";
                case WarSandboxBattlePhase.ArmyVictory: return FormatTeamName(result.winnerTeamId) + "\u80dc\u5229";
                default: return "\u540c\u5f52\u4e8e\u5c3d";
            }
        }

        private static string FormatBattleTime(float seconds)
        {
            int totalSeconds = Mathf.Max(0, Mathf.FloorToInt(seconds));
            return (totalSeconds / 60).ToString("00") + ":" + (totalSeconds % 60).ToString("00");
        }

        private static string FormatVictoryReason(WarSandboxVictoryReason reason)
        {
            if (reason == WarSandboxVictoryReason.ManualEnd) return "主动结束 · 不判定胜负";
            return reason == WarSandboxVictoryReason.ControlPoint ? "占领据点" : "歼灭敌军";
        }

        private void DrawControlPointStatus()
        {
            if (controller.gameMode != WarSandboxGameMode.ControlPoint || IsTerminalPhase(controller.Phase))
                return;

            Rect commandPanel = ResolvePanelRect();
            float width = Mathf.Min(420f, Mathf.Max(220f, commandPanel.x - 16f));
            Rect panel = new Rect(8f, 92f, width, 78f);
            GUI.Box(panel, GUIContent.none);

            GUIStyle summaryStyle = new GUIStyle(GUI.skin.label)
            {
                wordWrap = true,
                fontSize = 11
            };
            GUI.Label(
                new Rect(panel.x + 8f, panel.y + 4f, panel.width - 16f, 28f),
                FormatControlPointCounts(),
                summaryStyle);

            string status = controller.IsControlPointContested
                ? "争夺中"
                : controller.ControlPointOwnerTeamId >= 0
                    ? FormatTeamName(controller.ControlPointOwnerTeamId) + "占领 " +
                      Mathf.RoundToInt(controller.ControlPointCaptureProgress * 100f) + "%"
                    : "未占领";
            GUI.Label(new Rect(panel.x + 8f, panel.y + 32f, panel.width - 16f, 18f), "中央据点  " + status);

            Rect bar = new Rect(panel.x + 8f, panel.y + 56f, panel.width - 16f, 14f);
            DrawSolidRect(bar, new Color(0.1f, 0.12f, 0.14f, 0.9f));
            float progress = Mathf.Clamp01(controller.ControlPointCaptureProgress);
            if (progress > 0f && controller.ControlPointOwnerTeamId >= 0)
                DrawSolidRect(
                    new Rect(bar.x, bar.y, bar.width * progress, bar.height),
                    WarSandboxTeamPalette.Resolve(controller.ControlPointOwnerTeamId));
        }

        private string FormatControlPointCounts()
        {
            string result = "据点兵力  ";
            bool hasArmy = false;
            for (int teamId = 0; teamId < ResolveArmyCount(); teamId++)
            {
                ArmyRuntimeState army = controller.GetArmy(teamId);
                if (army == null || army.initialUnitCount <= 0)
                    continue;

                if (hasArmy)
                    result += "  |  ";
                result += FormatTeamName(teamId) + " " + controller.GetControlPointUnitCount(teamId);
                hasArmy = true;
            }

            return hasArmy ? result : "据点兵力  无可用军团";
        }

        private void ResolveReferences()
        {
            if (controller == null)
                controller = GetComponent<WarSandboxBattleController>();
            if (commandCamera == null && controller != null && controller.manager != null)
                commandCamera = controller.manager.cullingCamera;
            if (cameraManager == null)
                cameraManager = FindFirstObjectByType<MyCameraManager>();
            if (cameraManager != null && controller != null) cameraManager.TerrainManager = controller.manager;
        }

        private void FocusArmy(int teamId)
        {
            ResolveReferences();
            if (cameraManager == null ||
                (!TryResolveLiveArmyBounds(teamId, out Bounds bounds) && !TryResolveArmyBounds(teamId, out bounds)))
                return;

            controller.SelectArmy(teamId);
            cameraFocusMode = CameraFocusMode.Army;
            cameraFocusTeamId = teamId;
            // Stand behind the army looking at the nearest enemy army; no enemy keeps the current heading.
            Vector3? toward = TryResolveNearestEnemyCenter(teamId, bounds.center, out Vector3 enemy) ? enemy : (Vector3?)null;
            cameraManager.FocusTacticalBounds(ExpandLiveBounds(bounds), toward);
            SetFeedback("镜头跟随：" + FormatTeamName(teamId));
        }

        private void FocusBattlefield()
        {
            ResolveReferences();
            if (cameraManager == null || controller.manager == null)
                return;

            Bounds bounds;
            if (!TryResolveLiveCombinedArmyBounds(out bounds) && !TryResolveCombinedArmyBounds(out bounds))
            {
                SimulationConfig simulation = controller.manager.systemConfig != null
                    ? controller.manager.systemConfig.simulationConfig
                    : null;
                if (simulation == null)
                    return;
                Vector2 size = simulation.simulationWorldSize;
                bounds = new Bounds(Vector3.zero, new Vector3(size.x, 40f, size.y));
            }

            cameraFocusMode = CameraFocusMode.All;
            cameraManager.FocusTacticalBounds(ExpandLiveBounds(bounds));
            SetFeedback("镜头跟随：全体战场");
        }

        private void UpdateCameraFollow()
        {
            if (cameraFocusMode == CameraFocusMode.None || cameraManager == null)
                return;

            bool resolved;
            Bounds bounds;
            if (cameraFocusMode == CameraFocusMode.All)
                resolved = TryResolveLiveCombinedArmyBounds(out bounds);
            else
                resolved = TryResolveLiveArmyBounds(cameraFocusTeamId, out bounds);

            if (resolved)
                cameraManager.FollowTacticalBounds(ExpandLiveBounds(bounds), cameraFollowSharpness);
        }

        private bool TryResolveNearestEnemyCenter(int teamId, Vector3 from, out Vector3 center)
        {
            center = default;
            float best = float.PositiveInfinity;
            int armyCount = ResolveArmyCount();
            // Live armies first (a wiped-out army has no live bounds); deployment bounds only without live telemetry.
            for (int pass = 0; pass < 2 && float.IsPositiveInfinity(best); pass++)
                for (int other = 0; other < armyCount; other++)
                {
                    if (other == teamId) continue;
                    Bounds enemy;
                    if (pass == 0 ? !TryResolveLiveArmyBounds(other, out enemy) : !TryResolveArmyBounds(other, out enemy)) continue;
                    Vector3 delta = enemy.center - from; delta.y = 0f;
                    float sqr = delta.sqrMagnitude;
                    if (sqr < best) { best = sqr; center = enemy.center; }
                }
            return !float.IsPositiveInfinity(best);
        }

        private bool TryResolveLiveArmyBounds(int teamId, out Bounds bounds)
        {
            bounds = default;
            if (controller == null)
                return false;

            BattleTelemetrySnapshot snapshot = controller.TelemetrySnapshot;
            // GetTeam covers every teamId in the sample; the attackers/defenders fields it mirrors
            // would report team 1's bounds for a third army.
            TeamSpatialTelemetry team = snapshot.GetTeam(teamId);
            if (!snapshot.valid || !team.valid)
                return false;

            bounds = team.bounds;
            return true;
        }

        /// <summary>
        /// Union of every army's live bounds. Iterating the roster is what pulls the panorama
        /// camera out far enough to hold a third army; merging teams 0 and 1 left it off frame.
        /// </summary>
        private bool TryResolveLiveCombinedArmyBounds(out Bounds bounds)
        {
            bounds = default;
            bool found = false;
            int armyCount = ResolveArmyCount();
            for (int teamId = 0; teamId < armyCount; teamId++)
            {
                if (!TryResolveLiveArmyBounds(teamId, out Bounds army))
                    continue;

                if (found)
                    bounds.Encapsulate(army);
                else
                    bounds = army;
                found = true;
            }

            return found;
        }

        private Bounds ExpandLiveBounds(Bounds bounds)
        {
            float padding = Mathf.Max(0f, liveBoundsPadding);
            bounds.Expand(new Vector3(padding * 2f, 0f, padding * 2f));
            return bounds;
        }

        private void DrawTacticalMinimap()
        {
            if (!showMinimap || cameraManager == null || !TryResolveSimulationWorldSize(out Vector2 worldSize))
                return;

            Rect outer = WarSandboxMinimapProjection.ResolveOuterRect(
                Screen.width, Screen.height, minimapSize, 8f);
            Rect map = WarSandboxMinimapProjection.ResolveContentRect(outer);
            GUI.Box(outer, GUIContent.none);
            GUI.Label(new Rect(outer.x + 8f, outer.y + 3f, outer.width - 16f, 20f), "左定位 右移动 Shift追加");

            Color previous = GUI.color;
            GUI.color = new Color(0.08f, 0.1f, 0.12f, 0.92f);
            GUI.DrawTexture(map, Texture2D.whiteTexture);
            GUI.color = previous;

            DrawStaticObstaclesMinimap(map, worldSize);
            DrawControlPointMinimap(map, worldSize);
            int minimapArmyCount = ResolveArmyCount();
            for (int teamId = 0; teamId < minimapArmyCount; teamId++)
                DrawMinimapTeam(map, worldSize, teamId, WarSandboxTeamPalette.Resolve(teamId));

            Camera targetCamera = commandCamera != null ? commandCamera : Camera.main;
            if (targetCamera != null)
            {
                Vector2 cameraPoint = WarSandboxMinimapProjection.WorldToMap(
                    targetCamera.transform.position, worldSize, map);
                DrawSolidRect(new Rect(cameraPoint.x - 2f, cameraPoint.y - 2f, 4f, 4f), Color.white);
            }

            Event current = Event.current;
            if (current.type == EventType.MouseDown && map.Contains(current.mousePosition))
            {
                Vector3 point = WarSandboxMinimapProjection.MapToWorld(current.mousePosition, worldSize, map);
                WarSandboxMinimapAction action = WarSandboxMinimapProjection.ResolvePointerAction(
                    current.button, awaitingMoveTarget, current.shift);
                if (action == WarSandboxMinimapAction.MoveSelectedArmy ||
                    action == WarSandboxMinimapAction.QueueMoveSelectedArmy)
                {
                    if (IssueMoveTo(point, action == WarSandboxMinimapAction.QueueMoveSelectedArmy))
                        current.Use();
                }
                else if (action == WarSandboxMinimapAction.FocusCamera)
                {
                    if (!controller.TryResolveGroundPoint(point, out point, out string error)) { SetFeedback(error); current.Use(); return; }
                    cameraFocusMode = CameraFocusMode.None;
                    cameraManager.CenterTacticalPoint(point);
                    SetFeedback("镜头定位：" + point.x.ToString("F0") + ", " + point.z.ToString("F0"));
                    current.Use();
                }
            }
        }

        private void DrawMinimapTeam(Rect map, Vector2 worldSize, int teamId, Color color)
        {
            bool hasBounds = TryResolveLiveArmyBounds(teamId, out Bounds bounds) ||
                             TryResolveArmyBounds(teamId, out bounds);
            if (!hasBounds)
                return;

            Vector2 min = WarSandboxMinimapProjection.WorldToMap(bounds.min, worldSize, map);
            Vector2 max = WarSandboxMinimapProjection.WorldToMap(bounds.max, worldSize, map);
            Rect teamRect = Rect.MinMaxRect(
                Mathf.Min(min.x, max.x),
                Mathf.Min(min.y, max.y),
                Mathf.Max(min.x, max.x),
                Mathf.Max(min.y, max.y));
            DrawRectOutline(teamRect, color, 1f);

            Vector2 center = WarSandboxMinimapProjection.WorldToMap(bounds.center, worldSize, map);
            int routePointCount = controller.GetMoveRoutePointCount(teamId);
            if (routePointCount > 0)
                DrawMinimapRoute(map, worldSize, teamId, center, color, routePointCount);

            float markerSize = controller.selectedTeam == teamId ? 10f : 7f;
            DrawSolidRect(
                new Rect(center.x - markerSize * 0.5f, center.y - markerSize * 0.5f, markerSize, markerSize),
                color);

            ArmyRuntimeState army = controller.GetArmy(teamId);
            if (routePointCount == 0 && army != null && army.hasOrder && army.currentOrder.hasTarget)
            {
                Vector2 target = WarSandboxMinimapProjection.WorldToMap(army.currentOrder.target, worldSize, map);
                DrawSolidRect(new Rect(target.x - 5f, target.y - 1f, 10f, 2f), color);
                DrawSolidRect(new Rect(target.x - 1f, target.y - 5f, 2f, 10f), color);
            }
        }

        private void DrawStaticObstaclesMinimap(Rect map, Vector2 worldSize)
        {
            int count = controller.GetStaticObstacleCount();
            for (int obstacleIndex = 0; obstacleIndex < count; obstacleIndex++)
            {
                if (!controller.TryGetStaticObstacle(obstacleIndex, out StaticObstacleRect obstacle))
                    continue;

                Rect bounds = obstacle.Bounds;
                Vector2 minimum = WarSandboxMinimapProjection.WorldToMap(
                    new Vector3(bounds.xMin, 0f, bounds.yMin), worldSize, map);
                Vector2 maximum = WarSandboxMinimapProjection.WorldToMap(
                    new Vector3(bounds.xMax, 0f, bounds.yMax), worldSize, map);
                Rect obstacleRect = Rect.MinMaxRect(
                    Mathf.Min(minimum.x, maximum.x),
                    Mathf.Min(minimum.y, maximum.y),
                    Mathf.Max(minimum.x, maximum.x),
                    Mathf.Max(minimum.y, maximum.y));
                DrawSolidRect(obstacleRect, new Color(0.48f, 0.52f, 0.55f, 0.9f));
                DrawRectOutline(obstacleRect, new Color(0.85f, 0.9f, 0.95f), 1f);
            }
        }

        private void DrawControlPointMinimap(Rect map, Vector2 worldSize)
        {
            if (controller.gameMode != WarSandboxGameMode.ControlPoint)
                return;

            Vector3 centerWorld = controller.controlPointCenter;
            float radius = Mathf.Max(2f, controller.controlPointRadius);
            Vector2 center = WarSandboxMinimapProjection.WorldToMap(centerWorld, worldSize, map);
            Vector2 edgeX = WarSandboxMinimapProjection.WorldToMap(centerWorld + Vector3.right * radius, worldSize, map);
            Vector2 edgeZ = WarSandboxMinimapProjection.WorldToMap(centerWorld + Vector3.forward * radius, worldSize, map);
            float radiusX = Mathf.Abs(edgeX.x - center.x);
            float radiusY = Mathf.Abs(edgeZ.y - center.y);
            DrawRectOutline(
                new Rect(center.x - radiusX, center.y - radiusY, radiusX * 2f, radiusY * 2f),
                new Color(1f, 0.85f, 0.2f),
                1f);
            DrawSolidRect(new Rect(center.x - 2f, center.y - 2f, 4f, 4f), new Color(1f, 0.85f, 0.2f));
        }

        private void DrawMinimapRoute(
            Rect map,
            Vector2 worldSize,
            int teamId,
            Vector2 armyCenter,
            Color color,
            int routePointCount)
        {
            Vector2 previous = armyCenter;
            Color lineColor = new Color(color.r, color.g, color.b, 0.65f);
            for (int routeIndex = 0; routeIndex < routePointCount; routeIndex++)
            {
                if (!controller.TryGetMoveRoutePoint(teamId, routeIndex, out Vector3 worldPoint))
                    continue;

                Vector2 point = WarSandboxMinimapProjection.WorldToMap(worldPoint, worldSize, map);
                DrawLine(previous, point, lineColor, 1f);
                if (routeIndex == 0)
                {
                    DrawSolidRect(new Rect(point.x - 5f, point.y - 1f, 10f, 2f), color);
                    DrawSolidRect(new Rect(point.x - 1f, point.y - 5f, 2f, 10f), color);
                }
                else
                {
                    DrawRectOutline(new Rect(point.x - 3f, point.y - 3f, 6f, 6f), color, 1f);
                }

                previous = point;
            }
        }

        private bool TryResolveSimulationWorldSize(out Vector2 worldSize)
        {
            worldSize = default;
            if (controller == null || controller.manager == null || controller.manager.systemConfig == null ||
                controller.manager.systemConfig.simulationConfig == null)
                return false;

            worldSize = controller.manager.systemConfig.simulationConfig.simulationWorldSize;
            return worldSize.x > 0f && worldSize.y > 0f;
        }

        private static void DrawRectOutline(Rect rect, Color color, float thickness)
        {
            if (rect.width <= 0f || rect.height <= 0f)
                return;

            DrawSolidRect(new Rect(rect.xMin, rect.yMin, rect.width, thickness), color);
            DrawSolidRect(new Rect(rect.xMin, rect.yMax - thickness, rect.width, thickness), color);
            DrawSolidRect(new Rect(rect.xMin, rect.yMin, thickness, rect.height), color);
            DrawSolidRect(new Rect(rect.xMax - thickness, rect.yMin, thickness, rect.height), color);
        }

        private static void DrawSolidRect(Rect rect, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        private static void DrawLine(Vector2 start, Vector2 end, Color color, float thickness)
        {
            Vector2 delta = end - start;
            float length = delta.magnitude;
            if (length <= 0.01f)
                return;

            Matrix4x4 previous = GUI.matrix;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg, start);
            DrawSolidRect(new Rect(start.x, start.y - thickness * 0.5f, length, thickness), color);
            GUI.matrix = previous;
        }

        private bool TryResolveArmyBounds(int teamId, out Bounds bounds)
        {
            bounds = default;
            if (controller == null || controller.manager == null ||
                controller.manager.scenarioConfig == null ||
                controller.manager.scenarioConfig.unitTypes == null)
                return false;

            bool found = false;
            UnitTypeConfig[] unitTypes = controller.manager.scenarioConfig.unitTypes;
            for (int i = 0; i < unitTypes.Length; i++)
            {
                UnitTypeConfig unitType = unitTypes[i];
                SpawnConfig spawn = unitType != null ? unitType.spawnConfig : null;
                if (spawn == null || unitType.teamId != teamId)
                    continue;

                Vector3 size = spawn.ResolveSpawnSize();
                if (!controller.TryResolveGroundPoint(spawn.spawnCenter, out var center, out _)) continue;
                Bounds spawnBounds = new Bounds(
                    center,
                    new Vector3(Mathf.Max(1f, size.x), 30f, Mathf.Max(1f, size.z)));
                if (!found)
                {
                    bounds = spawnBounds;
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(spawnBounds);
                }
            }

            return found;
        }

        /// <summary>Union of every army's configured deployment, for the pre-battle camera.</summary>
        private bool TryResolveCombinedArmyBounds(out Bounds bounds)
        {
            bounds = default;
            bool found = false;
            int armyCount = ResolveArmyCount();
            for (int teamId = 0; teamId < armyCount; teamId++)
            {
                if (!TryResolveArmyBounds(teamId, out Bounds army))
                    continue;

                if (found)
                    bounds.Encapsulate(army);
                else
                    bounds = army;
                found = true;
            }

            return found;
        }

        private void DrawWorldOrderMarkers()
        {
            Camera targetCamera = commandCamera != null ? commandCamera : Camera.main;
            if (targetCamera == null)
                return;

            if (Feedback25 != null) { DrawControlPointWorldMarker(targetCamera); return; }
            int armyCount = ResolveArmyCount();
            for (int teamId = 0; teamId < armyCount; teamId++)
                DrawArmyMarker(targetCamera, controller.GetArmy(teamId), WarSandboxTeamPalette.Resolve(teamId));
            DrawControlPointWorldMarker(targetCamera);
        }

        private void DrawControlPointWorldMarker(Camera targetCamera)
        {
            if (controller.gameMode != WarSandboxGameMode.ControlPoint)
                return;

            if (!controller.TryResolveGroundPoint(controller.controlPointCenter, out var centerWorld, out _) ||
                !controller.TryResolveGroundPoint(controller.controlPointCenter + Vector3.right * Mathf.Max(2f, controller.controlPointRadius),
                    out var edgeWorld, out _)) return;
            Vector3 centerScreen = targetCamera.WorldToScreenPoint(centerWorld);
            Vector3 edgeScreen = targetCamera.WorldToScreenPoint(edgeWorld);
            if (centerScreen.z <= 0f || edgeScreen.z <= 0f)
                return;

            Vector2 center = new Vector2(centerScreen.x, Screen.height - centerScreen.y);
            float radius = Mathf.Clamp(Mathf.Abs(edgeScreen.x - centerScreen.x), 8f, 240f);
            Color color = new Color(1f, 0.71f, 0.15f, 0.85f); // B amber (objective)
            const int segments = 24;
            Vector2 previous = center + Vector2.right * radius;
            bool terrain = controller.manager.terrainSurfaceAsset != null, previousVisible = true;
            if (terrain) previous = new Vector2(edgeScreen.x, Screen.height - edgeScreen.y);
            for (int i = 1; i <= segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                Vector2 next = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                bool visible = true;
                if (terrain)
                {
                    var ringPoint = centerWorld + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * controller.controlPointRadius;
                    visible = controller.TryResolveGroundPoint(ringPoint, out ringPoint, out _);
                    Vector3 projected = targetCamera.WorldToScreenPoint(ringPoint);
                    visible &= projected.z > 0;
                    next = new Vector2(projected.x, Screen.height - projected.y);
                }
                if (visible && previousVisible) DrawLine(previous, next, color, 1f);
                previous = next; previousVisible = visible;
            }

            Color labelColor = GUI.contentColor; GUI.contentColor = color;
            GUI.Label(new Rect(center.x + 8f, center.y - 12f, 100f, 22f), "中央据点");
            GUI.contentColor = labelColor;
        }

        private void DrawArmyMarker(Camera targetCamera, ArmyRuntimeState army, Color color)
        {
            if (army == null || !army.hasOrder || !army.currentOrder.hasTarget)
                return;

            if (!controller.TryResolveGroundPoint(army.currentOrder.target, out var target, out _)) return;
            Vector3 screen = targetCamera.WorldToScreenPoint(target);
            if (screen.z <= 0f)
                return;

            float x = screen.x;
            float y = Screen.height - screen.y;
            Color previous = GUI.color;
            GUI.color = color;
            GUI.Box(new Rect(x - 9f, y - 1f, 18f, 2f), GUIContent.none);
            GUI.Box(new Rect(x - 1f, y - 9f, 2f, 18f), GUIContent.none);
            GUI.Label(
                new Rect(x + 10f, y - 11f, 150f, 22f),
                army.displayName + " " + FormatOrder(army.currentOrder.type));
            GUI.color = previous;
        }

        private void SetFeedback(string text)
        {
            if (Feedback25 != null) { Feedback25.Notice(text); nextUiRefresh = 0; }
            commandFeedback = text;
            feedbackUntil = Time.unscaledTime + 2f;
        }

        // Reads the roster instead of assuming two armies, so a third army is named rather than
        // labelled "守方". Falls back to the old pair only when the controller has no such army.
        private string FormatTeamName(int teamId)
        {
            ArmyRuntimeState army = controller != null ? controller.GetArmy(teamId) : null;
            if (army != null && !string.IsNullOrEmpty(army.displayName))
                return army.displayName;

            // Same table the controller names armies from, so an out-of-range selection reads as
            // its own army instead of borrowing the defender's name.
            return WarSandboxBattleController.DefaultArmyName(teamId);
        }

        private static string FormatOrder(ArmyOrderType type)
        {
            switch (type)
            {
                case ArmyOrderType.Attack: return "进攻";
                case ArmyOrderType.Move: return "移动";
                case ArmyOrderType.Hold: return "原地防守";
                case ArmyOrderType.Retreat: return "撤退";
                default: return "未下令";
            }
        }

        /// <summary>
        /// One "name alive/initial" entry per army, ArmyColumns per row, tinted with the army's
        /// minimap colour so map and readout agree on who is who. The single line it replaces
        /// spelled out 攻/守 and simply had nowhere to put a third army.
        /// </summary>
        private void DrawForceSummary(bool compactLayout)
        {
            int armyCount = ResolveArmyCount();
            float lineHeight = compactLayout ? 17f : 20f;
            Color previousColor = GUI.contentColor;
            for (int teamId = 0; teamId < armyCount; teamId++)
            {
                if (teamId % ArmyColumns == 0)
                    GUILayout.BeginHorizontal();

                ArmyRuntimeState army = controller.GetArmy(teamId);
                GUI.contentColor = WarSandboxTeamPalette.Resolve(teamId);
                GUILayout.Label(
                    FormatTeamName(teamId) + " " + controller.GetAliveUnitCount(teamId) + "/" +
                    (army != null ? army.initialUnitCount : 0),
                    GUILayout.Height(lineHeight));

                if (teamId % ArmyColumns == ArmyColumns - 1 || teamId == armyCount - 1)
                    GUILayout.EndHorizontal();
            }

            GUI.contentColor = previousColor;
        }

        /// <summary>
        /// One toggle per army in the roster, ArmyColumns per row. Armies 1..9 advertise their
        /// digit hotkey in the label; past that the name stands alone.
        /// </summary>
        private void DrawArmySelector(float controlHeight)
        {
            int armyCount = ResolveArmyCount();
            for (int teamId = 0; teamId < armyCount; teamId++)
            {
                if (teamId % ArmyColumns == 0)
                    GUILayout.BeginHorizontal();

                string label = teamId < 9
                    ? FormatTeamName(teamId) + " [" + (teamId + 1) + "]"
                    : FormatTeamName(teamId);
                if (GUILayout.Toggle(controller.selectedTeam == teamId, label, GUI.skin.button, GUILayout.Height(controlHeight)))
                    controller.SelectArmy(teamId);

                if (teamId % ArmyColumns == ArmyColumns - 1 || teamId == armyCount - 1)
                    GUILayout.EndHorizontal();
            }
        }

        /// <summary>Roster length, or zero before the controller resolves — every per-army loop goes through here.</summary>
        private int ResolveArmyCount()
        {
            return controller != null ? controller.ArmyCount : 0;
        }

        private static bool IsPreOrPostBattle(WarSandboxBattlePhase value)
        {
            return value == WarSandboxBattlePhase.Setup || IsTerminalPhase(value);
        }

        private static bool IsTerminalPhase(WarSandboxBattlePhase value)
        {
            return value == WarSandboxBattlePhase.AttackerVictory ||
                   value == WarSandboxBattlePhase.DefenderVictory ||
                   value == WarSandboxBattlePhase.ArmyVictory ||
                   value == WarSandboxBattlePhase.Draw || value == WarSandboxBattlePhase.Ended;
        }

        private static string FormatPhase(WarSandboxBattlePhase value)
        {
            switch (value)
            {
                case WarSandboxBattlePhase.Running: return "交战中";
                case WarSandboxBattlePhase.Paused: return "已暂停";
                case WarSandboxBattlePhase.AttackerVictory: return "攻方胜利";
                case WarSandboxBattlePhase.DefenderVictory: return "守方胜利";
                case WarSandboxBattlePhase.ArmyVictory: return "战斗结束";
                case WarSandboxBattlePhase.Draw: return "同归于尽";
                case WarSandboxBattlePhase.Ended: return "手动结束";
                default: return "部署";
            }
        }
    }

    /// <summary>
    /// Per-army colours for the minimap, world markers and force readout. Teams 0 and 1 keep the
    /// red / blue the two-army HUD drew, so an ordinary battle looks unchanged; further armies get
    /// distinct hues, and past the table the hue is generated so the HUD never runs out.
    /// </summary>
    public static class WarSandboxTeamPalette
    {
        private static readonly Color[] Colors =
        {
            new Color(1f, 0.33f, 0.22f),
            new Color(0.27f, 0.57f, 1f),
            new Color(0.35f, 0.85f, 0.4f),
            new Color(0.95f, 0.8f, 0.2f),
            new Color(0.75f, 0.4f, 0.95f),
            new Color(0.2f, 0.85f, 0.85f),
            new Color(0.95f, 0.55f, 0.2f),
            new Color(0.72f, 0.72f, 0.74f)
        };

        public static Color Resolve(int teamId)
        {
            if (teamId < 0)
                return Color.white;
            if (teamId < Colors.Length)
                return Colors[teamId];

            // Golden-ratio hue walk: neighbours stay distinct for any army count, no bigger table.
            return Color.HSVToRGB(Mathf.Repeat(teamId * 0.618034f, 1f), 0.7f, 0.95f);
        }
    }

    public enum WarSandboxMinimapAction
    {
        None,
        FocusCamera,
        MoveSelectedArmy,
        QueueMoveSelectedArmy
    }

    public static class WarSandboxMinimapProjection
    {
        public static WarSandboxMinimapAction ResolvePointerAction(int mouseButton, bool awaitingMoveTarget, bool appendModifier)
        {
            if (mouseButton == 1 || (mouseButton == 0 && awaitingMoveTarget))
                return appendModifier
                    ? WarSandboxMinimapAction.QueueMoveSelectedArmy
                    : WarSandboxMinimapAction.MoveSelectedArmy;
            return mouseButton == 0 ? WarSandboxMinimapAction.FocusCamera : WarSandboxMinimapAction.None;
        }

        public static Rect ResolveOuterRect(float screenWidth, float screenHeight, float requestedSize, float margin)
        {
            screenWidth = Mathf.Max(1f, screenWidth);
            screenHeight = Mathf.Max(1f, screenHeight);
            margin = Mathf.Clamp(margin, 0f, Mathf.Min(screenWidth, screenHeight) * 0.1f);
            float available = Mathf.Max(1f, Mathf.Min(screenWidth - margin * 2f, screenHeight - margin * 2f));
            float minimum = Mathf.Min(96f, available);
            float maximum = Mathf.Clamp(Mathf.Min(screenWidth * 0.38f, screenHeight * 0.38f), minimum, available);
            float size = Mathf.Clamp(requestedSize, minimum, maximum);
            return new Rect(margin, Mathf.Max(margin, screenHeight - size - margin), size, size);
        }

        public static Rect ResolveContentRect(Rect outer)
        {
            return new Rect(outer.x + 7f, outer.y + 24f, Mathf.Max(1f, outer.width - 14f), Mathf.Max(1f, outer.height - 31f));
        }

        public static Vector2 WorldToMap(Vector3 world, Vector2 worldSize, Rect map)
        {
            float normalizedX = Mathf.InverseLerp(-worldSize.x * 0.5f, worldSize.x * 0.5f, world.x);
            float normalizedZ = Mathf.InverseLerp(-worldSize.y * 0.5f, worldSize.y * 0.5f, world.z);
            return new Vector2(
                Mathf.Lerp(map.xMin, map.xMax, normalizedX),
                Mathf.Lerp(map.yMax, map.yMin, normalizedZ));
        }

        public static Vector3 MapToWorld(Vector2 mapPoint, Vector2 worldSize, Rect map)
        {
            float normalizedX = Mathf.InverseLerp(map.xMin, map.xMax, mapPoint.x);
            float normalizedZ = Mathf.InverseLerp(map.yMax, map.yMin, mapPoint.y);
            return new Vector3(
                Mathf.Lerp(-worldSize.x * 0.5f, worldSize.x * 0.5f, normalizedX),
                0f,
                Mathf.Lerp(-worldSize.y * 0.5f, worldSize.y * 0.5f, normalizedZ));
        }
    }

    public static class WarSandboxBattleReportLayout
    {
        public static Rect ResolveRect(float screenWidth, float screenHeight, float commandPanelX, float margin)
        {
            screenWidth = Mathf.Max(1f, screenWidth);
            screenHeight = Mathf.Max(1f, screenHeight);
            margin = Mathf.Clamp(margin, 0f, Mathf.Min(screenWidth, screenHeight) * 0.1f);

            float availableLeftWidth = Mathf.Max(1f, commandPanelX - margin * 2f);
            float width = Mathf.Min(420f, Mathf.Max(240f, availableLeftWidth));
            width = Mathf.Min(width, Mathf.Max(1f, screenWidth - margin * 2f));
            float height = Mathf.Min(224f, Mathf.Max(1f, screenHeight - margin * 2f));
            float leftRegionCenter = margin + availableLeftWidth * 0.5f;
            float x = Mathf.Clamp(leftRegionCenter - width * 0.5f, margin, Mathf.Max(margin, screenWidth - width - margin));
            return new Rect(x, margin, width, height);
        }
    }
}


