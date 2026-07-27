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

        private bool awaitingMoveTarget;
        private ClickFlowTargetSetter legacyClickSetter;
        private bool legacyClickSetterWasEnabled;

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

            if (Input.GetKeyDown(KeyCode.Alpha1))
                controller.SelectArmy(0);
            if (Input.GetKeyDown(KeyCode.Alpha2))
                controller.SelectArmy(1);
            if (Input.GetKeyDown(KeyCode.A))
                IssueAttack();
            if (Input.GetKeyDown(KeyCode.M))
                awaitingMoveTarget = true;
            if (Input.GetKeyDown(KeyCode.H))
                IssueHold();
            if (Input.GetKeyDown(KeyCode.R))
                IssueRetreat();
            if (Input.GetKeyDown(KeyCode.Space))
                controller.TogglePause();
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
                StartOrRestartDefaultBattle();
            if (Input.GetKeyDown(KeyCode.Escape))
                awaitingMoveTarget = false;

            if (!awaitingMoveTarget || !Input.GetMouseButtonDown(0) || IsMouseOverPanel())
                return;

            Camera targetCamera = commandCamera != null ? commandCamera : Camera.main;
            if (targetCamera == null)
                return;

            Ray ray = targetCamera.ScreenPointToRay(Input.mousePosition);
            if (!Physics.Raycast(ray, out RaycastHit hit, Mathf.Max(1f, maxRayDistance), groundMask, QueryTriggerInteraction.Ignore))
                return;

            if (controller.IssueOrder(ArmyOrder.Move(controller.selectedTeam, hit.point)))
                awaitingMoveTarget = false;
        }

        private void OnGUI()
        {
            ResolveReferences();
            if (controller == null)
                return;

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
                awaitingMoveTarget = true;
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
            }
            GUILayout.EndHorizontal();

            GUILayout.Label("速度", GUILayout.Height(compactLayout ? 17f : 20f));
            GUILayout.BeginHorizontal();
            DrawSpeedButton("0.5×", 0.5f, controlHeight);
            DrawSpeedButton("1×", 1f, controlHeight);
            DrawSpeedButton("2×", 2f, controlHeight);
            DrawSpeedButton("4×", 4f, controlHeight);
            GUILayout.EndHorizontal();

            if (awaitingMoveTarget)
                GUILayout.Label(
                    "下一次左键点击地面将下达移动命令；Esc 取消。",
                    GUILayout.Height(compactLayout ? 17f : 36f));
            else if (showHotkeys && !compactLayout)
                GUILayout.Label("Enter双方开战 · 1/2选军 · A进攻 · M移动 · H防守 · R撤退");

            GUILayout.EndArea();
        }

        private void IssueAttack()
        {
            awaitingMoveTarget = false;
            controller.IssueOrder(ArmyOrder.Attack(controller.selectedTeam));
        }

        private void IssueHold()
        {
            awaitingMoveTarget = false;
            controller.IssueOrder(ArmyOrder.Hold(controller.selectedTeam));
        }

        private void IssueRetreat()
        {
            awaitingMoveTarget = false;
            controller.IssueOrder(ArmyOrder.Retreat(controller.selectedTeam));
        }

        private void StartOrRestartDefaultBattle()
        {
            awaitingMoveTarget = false;
            if (IsTerminalPhase(controller.Phase))
                controller.RestartWithDefaultOrders();
            else if (controller.Phase == WarSandboxBattlePhase.Setup)
                controller.StartDefaultBattle();
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
            float preferredHeight = Screen.height < 340f ? 276f : 300f;
            float height = Mathf.Min(preferredHeight, Mathf.Max(200f, Screen.height - 16f));
            return new Rect(Mathf.Max(8f, Screen.width - width - 8f), 8f, width, height);
        }

        private void ResolveReferences()
        {
            if (controller == null)
                controller = GetComponent<WarSandboxBattleController>();
            if (commandCamera == null && controller != null && controller.manager != null)
                commandCamera = controller.manager.cullingCamera;
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
