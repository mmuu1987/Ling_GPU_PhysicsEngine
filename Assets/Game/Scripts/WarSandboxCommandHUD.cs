using UnityEngine;

namespace MassEngine.Game
{
    /// <summary>
    /// Immediate-mode vertical-slice UI: army selection, orders, pause/speed/reset and
    /// victory display. Move orders consume the next ground click.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("MassEngine/War Sandbox Command HUD")]
    public sealed class WarSandboxCommandHUD : MonoBehaviour
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

        private enum CameraFocusMode
        {
            None,
            Attackers,
            Defenders,
            Both
        }

        private bool awaitingMoveTarget;
        private ClickFlowTargetSetter legacyClickSetter;
        private bool legacyClickSetterWasEnabled;
        private MyCameraManager cameraManager;
        private string commandFeedback;
        private float feedbackUntil;
        private CameraFocusMode cameraFocusMode;

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
            if (legacyClickSetter != null)
                legacyClickSetter.enabled = legacyClickSetterWasEnabled;
        }

        private void Update()
        {
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

            if (Input.GetKeyDown(KeyCode.Alpha1))
                controller.SelectArmy(0);
            if (Input.GetKeyDown(KeyCode.Alpha2))
                controller.SelectArmy(1);
            if (!cameraNavigation)
            {
                if (Input.GetKeyDown(KeyCode.A))
                    IssueAttack();
                if (Input.GetKeyDown(KeyCode.M))
                    BeginMoveOrder();
                if (Input.GetKeyDown(KeyCode.H))
                    IssueHold();
                if (Input.GetKeyDown(KeyCode.R))
                    IssueRetreat();
            }
            if (Input.GetKeyDown(KeyCode.Space))
                controller.TogglePause();
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
                StartOrRestartDefaultBattle();
            if (Input.GetKeyDown(KeyCode.Escape))
                awaitingMoveTarget = false;
            if (Input.GetKeyDown(KeyCode.F1))
                FocusArmy(0);
            if (Input.GetKeyDown(KeyCode.F2))
                FocusArmy(1);
            if (Input.GetKeyDown(KeyCode.F3))
                FocusBattlefield();
            if (Input.GetKeyDown(KeyCode.F))
                FocusArmy(controller.selectedTeam);

            UpdateCameraFollow();

            if (!awaitingMoveTarget || !Input.GetMouseButtonDown(0) || IsMouseOverInterface())
                return;

            Camera targetCamera = commandCamera != null ? commandCamera : Camera.main;
            if (targetCamera == null)
                return;

            Ray ray = targetCamera.ScreenPointToRay(Input.mousePosition);
            if (!Physics.Raycast(ray, out RaycastHit hit, Mathf.Max(1f, maxRayDistance), groundMask, QueryTriggerInteraction.Ignore))
                return;

            IssueMoveTo(hit.point, Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift));
        }

        private void OnGUI()
        {
            ResolveReferences();
            if (controller == null)
                return;

            DrawWorldOrderMarkers();
            DrawTacticalMinimap();

            bool compactLayout = Screen.height < 340f;
            float controlHeight = compactLayout ? 20f : 24f;
            Rect panel = ResolvePanelRect();
            GUI.Box(panel, GUIContent.none);
            GUILayout.BeginArea(new Rect(panel.x + 8f, panel.y + 6f, panel.width - 16f, panel.height - 12f));

            GUILayout.Label("战争沙盒", GUILayout.Height(compactLayout ? 17f : 20f));
            GUILayout.Label("阶段：" + FormatPhase(controller.Phase), GUILayout.Height(compactLayout ? 17f : 20f));
            GUILayout.Label(FormatForceSummary(), GUILayout.Height(compactLayout ? 17f : 20f));

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
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Toggle(controller.selectedTeam == 0, "攻方 [1]", GUI.skin.button, GUILayout.Height(controlHeight)))
                controller.SelectArmy(0);
            if (GUILayout.Toggle(controller.selectedTeam == 1, "守方 [2]", GUI.skin.button, GUILayout.Height(controlHeight)))
                controller.SelectArmy(1);
            GUILayout.EndHorizontal();

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
                if (GUILayout.Button("攻方镜头 [F1]", GUILayout.Height(controlHeight)))
                    FocusArmy(0);
                if (GUILayout.Button("守方镜头 [F2]", GUILayout.Height(controlHeight)))
                    FocusArmy(1);
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
                GUILayout.Label("F当前/F1攻/F2守/F3双方跟随 · Enter开战 · A/M/H/R下令");

            GUILayout.EndArea();
            DrawControlPointStatus();
            DrawBattleResultReport();
        }

        private void IssueAttack()
        {
            awaitingMoveTarget = false;
            if (controller.IssueOrder(ArmyOrder.Attack(controller.selectedTeam)))
                SetFeedback(FormatTeamName(controller.selectedTeam) + "：进攻");
        }

        private void BeginMoveOrder()
        {
            awaitingMoveTarget = true;
            SetFeedback(FormatTeamName(controller.selectedTeam) + "：请选择移动目标");
        }

        private bool IssueMoveTo(Vector3 target, bool append = false)
        {
            ArmyRuntimeState selectedArmy = controller.SelectedArmy;
            bool actuallyAppending = append &&
                                     controller.GetMoveRoutePointCount(controller.selectedTeam) > 0 &&
                                     selectedArmy != null && selectedArmy.hasOrder &&
                                     selectedArmy.currentOrder.type == ArmyOrderType.Move;
            if (!controller.IssueMoveOrder(controller.selectedTeam, target, append))
            {
                if (actuallyAppending)
                    SetFeedback("路线已达到航点上限");
                return false;
            }

            awaitingMoveTarget = false;
            SetFeedback(FormatTeamName(controller.selectedTeam) + (actuallyAppending ? "：已追加路线航点" : "：移动目标已更新"));
            return true;
        }

        private void IssueHold()
        {
            awaitingMoveTarget = false;
            if (controller.IssueOrder(ArmyOrder.Hold(controller.selectedTeam)))
                SetFeedback(FormatTeamName(controller.selectedTeam) + "：原地防守");
        }

        private void IssueRetreat()
        {
            awaitingMoveTarget = false;
            if (controller.IssueOrder(ArmyOrder.Retreat(controller.selectedTeam)))
                SetFeedback(FormatTeamName(controller.selectedTeam) + "：撤回出生地");
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
                SetFeedback("双方已下达进攻命令");
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
            return ResolvePanelRect().Contains(guiMouse) ||
                   (controller.BattleResult.valid && ResolveBattleResultRect().Contains(guiMouse)) ||
                   (showMinimap && WarSandboxMinimapProjection.ResolveOuterRect(
                       Screen.width, Screen.height, minimapSize, 8f).Contains(guiMouse));
        }

        private Rect ResolvePanelRect()
        {
            float width = Mathf.Min(Mathf.Max(240f, panelWidth), Mathf.Max(240f, Screen.width - 16f));
            float preferredHeight = Screen.height < 340f ? 276f : 352f;
            float height = Mathf.Min(preferredHeight, Mathf.Max(200f, Screen.height - 16f));
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
            GUILayout.Label(FormatResultTitle(result.phase), titleStyle, GUILayout.Height(26f));
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

        private static string FormatResultTitle(WarSandboxBattlePhase value)
        {
            switch (value)
            {
                case WarSandboxBattlePhase.AttackerVictory: return "\u653b\u65b9\u80dc\u5229";
                case WarSandboxBattlePhase.DefenderVictory: return "\u5b88\u65b9\u80dc\u5229";
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
            return reason == WarSandboxVictoryReason.ControlPoint ? "占领据点" : "歼灭敌军";
        }

        private void DrawControlPointStatus()
        {
            if (controller.gameMode != WarSandboxGameMode.ControlPoint || IsTerminalPhase(controller.Phase))
                return;

            Rect commandPanel = ResolvePanelRect();
            float width = Mathf.Min(360f, Mathf.Max(180f, commandPanel.x - 16f));
            Rect panel = new Rect(8f, 92f, width, 58f);
            GUI.Box(panel, GUIContent.none);
            GUI.Label(
                new Rect(panel.x + 8f, panel.y + 4f, panel.width - 16f, 20f),
                "中央据点  攻 " + controller.AttackersInControlPoint + "  |  守 " + controller.DefendersInControlPoint);

            Rect bar = new Rect(panel.x + 8f, panel.y + 30f, panel.width - 16f, 16f);
            DrawSolidRect(bar, new Color(0.1f, 0.12f, 0.14f, 0.9f));
            float half = bar.width * 0.5f;
            float progress = Mathf.Clamp(controller.ControlPointProgress, -1f, 1f);
            if (progress > 0f)
                DrawSolidRect(new Rect(bar.center.x, bar.y, half * progress, bar.height), new Color(1f, 0.32f, 0.2f));
            else if (progress < 0f)
                DrawSolidRect(new Rect(bar.center.x + half * progress, bar.y, -half * progress, bar.height), new Color(0.25f, 0.55f, 1f));
            DrawSolidRect(new Rect(bar.center.x - 1f, bar.y, 2f, bar.height), Color.white);
        }

        private void ResolveReferences()
        {
            if (controller == null)
                controller = GetComponent<WarSandboxBattleController>();
            if (commandCamera == null && controller != null && controller.manager != null)
                commandCamera = controller.manager.cullingCamera;
            if (cameraManager == null)
                cameraManager = FindFirstObjectByType<MyCameraManager>();
        }

        private void FocusArmy(int teamId)
        {
            ResolveReferences();
            if (cameraManager == null ||
                (!TryResolveLiveArmyBounds(teamId, out Bounds bounds) && !TryResolveArmyBounds(teamId, out bounds)))
                return;

            controller.SelectArmy(teamId);
            cameraFocusMode = teamId == 0 ? CameraFocusMode.Attackers : CameraFocusMode.Defenders;
            cameraManager.FocusTacticalBounds(ExpandLiveBounds(bounds));
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

            cameraFocusMode = CameraFocusMode.Both;
            cameraManager.FocusTacticalBounds(ExpandLiveBounds(bounds));
            SetFeedback("镜头跟随：双方战场");
        }

        private void UpdateCameraFollow()
        {
            if (cameraFocusMode == CameraFocusMode.None || cameraManager == null)
                return;

            bool resolved;
            Bounds bounds;
            if (cameraFocusMode == CameraFocusMode.Both)
                resolved = TryResolveLiveCombinedArmyBounds(out bounds);
            else
                resolved = TryResolveLiveArmyBounds(cameraFocusMode == CameraFocusMode.Attackers ? 0 : 1, out bounds);

            if (resolved)
                cameraManager.FollowTacticalBounds(ExpandLiveBounds(bounds), cameraFollowSharpness);
        }

        private bool TryResolveLiveArmyBounds(int teamId, out Bounds bounds)
        {
            bounds = default;
            if (controller == null)
                return false;

            BattleTelemetrySnapshot snapshot = controller.TelemetrySnapshot;
            TeamSpatialTelemetry team = teamId == 0 ? snapshot.attackers : snapshot.defenders;
            if (!snapshot.valid || !team.valid)
                return false;

            bounds = team.bounds;
            return true;
        }

        private bool TryResolveLiveCombinedArmyBounds(out Bounds bounds)
        {
            bool hasAttackers = TryResolveLiveArmyBounds(0, out Bounds attackers);
            bool hasDefenders = TryResolveLiveArmyBounds(1, out Bounds defenders);
            bounds = hasAttackers ? attackers : defenders;
            if (hasAttackers && hasDefenders)
                bounds.Encapsulate(defenders);
            return hasAttackers || hasDefenders;
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

            DrawControlPointMinimap(map, worldSize);
            DrawMinimapTeam(map, worldSize, 0, new Color(1f, 0.3f, 0.2f));
            DrawMinimapTeam(map, worldSize, 1, new Color(0.25f, 0.55f, 1f));

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
                Bounds spawnBounds = new Bounds(
                    spawn.spawnCenter,
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

        private bool TryResolveCombinedArmyBounds(out Bounds bounds)
        {
            bool hasAttackers = TryResolveArmyBounds(0, out Bounds attackers);
            bool hasDefenders = TryResolveArmyBounds(1, out Bounds defenders);
            bounds = hasAttackers ? attackers : defenders;
            if (hasAttackers && hasDefenders)
                bounds.Encapsulate(defenders);
            return hasAttackers || hasDefenders;
        }

        private void DrawWorldOrderMarkers()
        {
            Camera targetCamera = commandCamera != null ? commandCamera : Camera.main;
            if (targetCamera == null)
                return;

            DrawArmyMarker(targetCamera, controller.GetArmy(0), new Color(1f, 0.35f, 0.25f));
            DrawArmyMarker(targetCamera, controller.GetArmy(1), new Color(0.3f, 0.6f, 1f));
            DrawControlPointWorldMarker(targetCamera);
        }

        private void DrawControlPointWorldMarker(Camera targetCamera)
        {
            if (controller.gameMode != WarSandboxGameMode.ControlPoint)
                return;

            Vector3 centerScreen = targetCamera.WorldToScreenPoint(controller.controlPointCenter);
            Vector3 edgeScreen = targetCamera.WorldToScreenPoint(
                controller.controlPointCenter + Vector3.right * Mathf.Max(2f, controller.controlPointRadius));
            if (centerScreen.z <= 0f || edgeScreen.z <= 0f)
                return;

            Vector2 center = new Vector2(centerScreen.x, Screen.height - centerScreen.y);
            float radius = Mathf.Clamp(Mathf.Abs(edgeScreen.x - centerScreen.x), 8f, 240f);
            Color color = new Color(1f, 0.85f, 0.2f, 0.8f);
            const int segments = 24;
            Vector2 previous = center + Vector2.right * radius;
            for (int i = 1; i <= segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                Vector2 next = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                DrawLine(previous, next, color, 1f);
                previous = next;
            }

            GUI.Label(new Rect(center.x + 8f, center.y - 12f, 100f, 22f), "中央据点");
        }

        private static void DrawArmyMarker(Camera targetCamera, ArmyRuntimeState army, Color color)
        {
            if (army == null || !army.hasOrder || !army.currentOrder.hasTarget)
                return;

            Vector3 screen = targetCamera.WorldToScreenPoint(army.currentOrder.target);
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
            commandFeedback = text;
            feedbackUntil = Time.unscaledTime + 2f;
        }

        private static string FormatTeamName(int teamId)
        {
            return teamId == 0 ? "攻方" : "守方";
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

        private string FormatForceSummary()
        {
            ArmyRuntimeState attackers = controller.GetArmy(0);
            ArmyRuntimeState defenders = controller.GetArmy(1);
            int attackerInitial = attackers != null ? attackers.initialUnitCount : 0;
            int defenderInitial = defenders != null ? defenders.initialUnitCount : 0;
            return "兵力  攻 " + controller.GetAliveUnitCount(0) + "/" + attackerInitial +
                   "  |  守 " + controller.GetAliveUnitCount(1) + "/" + defenderInitial;
        }

        private static bool IsPreOrPostBattle(WarSandboxBattlePhase value)
        {
            return value == WarSandboxBattlePhase.Setup || IsTerminalPhase(value);
        }

        private static bool IsTerminalPhase(WarSandboxBattlePhase value)
        {
            return value == WarSandboxBattlePhase.AttackerVictory ||
                   value == WarSandboxBattlePhase.DefenderVictory ||
                   value == WarSandboxBattlePhase.Draw;
        }

        private static string FormatPhase(WarSandboxBattlePhase value)
        {
            switch (value)
            {
                case WarSandboxBattlePhase.Running: return "交战中";
                case WarSandboxBattlePhase.Paused: return "已暂停";
                case WarSandboxBattlePhase.AttackerVictory: return "攻方胜利";
                case WarSandboxBattlePhase.DefenderVictory: return "守方胜利";
                case WarSandboxBattlePhase.Draw: return "同归于尽";
                default: return "部署";
            }
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
