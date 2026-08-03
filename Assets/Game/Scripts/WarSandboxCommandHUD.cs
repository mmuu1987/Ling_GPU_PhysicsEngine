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

            if (!awaitingMoveTarget || !Input.GetMouseButtonDown(0) || IsMouseOverPanel())
                return;

            Camera targetCamera = commandCamera != null ? commandCamera : Camera.main;
            if (targetCamera == null)
                return;

            Ray ray = targetCamera.ScreenPointToRay(Input.mousePosition);
            if (!Physics.Raycast(ray, out RaycastHit hit, Mathf.Max(1f, maxRayDistance), groundMask, QueryTriggerInteraction.Ignore))
                return;

            if (controller.IssueOrder(ArmyOrder.Move(controller.selectedTeam, hit.point)))
            {
                awaitingMoveTarget = false;
                SetFeedback(FormatTeamName(controller.selectedTeam) + "：移动目标已更新");
            }
        }

        private void OnGUI()
        {
            ResolveReferences();
            if (controller == null)
                return;

            DrawWorldOrderMarkers();

            bool compactLayout = Screen.height < 340f;
            float controlHeight = compactLayout ? 20f : 24f;
            Rect panel = ResolvePanelRect();
            GUI.Box(panel, GUIContent.none);
            GUILayout.BeginArea(new Rect(panel.x + 8f, panel.y + 6f, panel.width - 16f, panel.height - 12f));

            GUILayout.Label("战争沙盒", GUILayout.Height(compactLayout ? 17f : 20f));
            GUILayout.Label("阶段：" + FormatPhase(controller.Phase), GUILayout.Height(compactLayout ? 17f : 20f));
            GUILayout.Label(FormatForceSummary(), GUILayout.Height(compactLayout ? 17f : 20f));

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

        private bool IsMouseOverPanel()
        {
            Vector2 guiMouse = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
            return ResolvePanelRect().Contains(guiMouse);
        }

        private Rect ResolvePanelRect()
        {
            float width = Mathf.Min(Mathf.Max(240f, panelWidth), Mathf.Max(240f, Screen.width - 16f));
            float preferredHeight = Screen.height < 340f ? 276f : 324f;
            float height = Mathf.Min(preferredHeight, Mathf.Max(200f, Screen.height - 16f));
            return new Rect(Mathf.Max(8f, Screen.width - width - 8f), 8f, width, height);
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
}
