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

            Rect panel = ResolvePanelRect();
            GUI.Box(panel, GUIContent.none);
            GUILayout.BeginArea(new Rect(panel.x + 10f, panel.y + 8f, panel.width - 20f, panel.height - 16f));

            GUILayout.Label("战争沙盒");
            GUILayout.Label("阶段：" + FormatPhase(controller.Phase));

            GUILayout.BeginHorizontal();
            if (GUILayout.Toggle(controller.selectedTeam == 0, "攻方 [1]", GUI.skin.button))
                controller.SelectArmy(0);
            if (GUILayout.Toggle(controller.selectedTeam == 1, "守方 [2]", GUI.skin.button))
                controller.SelectArmy(1);
            GUILayout.EndHorizontal();

            ArmyRuntimeState selected = controller.SelectedArmy;
            if (selected != null)
            {
                string order = selected.hasOrder ? FormatOrder(selected.currentOrder.type) : "未下令";
                GUILayout.Label(selected.displayName + "  " + selected.initialUnitCount + " 人  |  " + order);
            }

            GUILayout.Space(4f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("进攻 [A]"))
                IssueAttack();
            if (GUILayout.Button(awaitingMoveTarget ? "点击地面…" : "移动 [M]"))
                awaitingMoveTarget = true;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("原地防守 [H]"))
                IssueHold();
            if (GUILayout.Button("撤回出生地 [R]"))
                IssueRetreat();
            GUILayout.EndHorizontal();

            GUILayout.Space(4f);
            GUILayout.BeginHorizontal();
            string pauseLabel = controller.Phase == WarSandboxBattlePhase.Running ? "暂停 [Space]" : "继续 [Space]";
            if (GUILayout.Button(pauseLabel))
                controller.TogglePause();
            if (GUILayout.Button("重开"))
            {
                awaitingMoveTarget = false;
                controller.ResetBattle();
            }
            GUILayout.EndHorizontal();

            GUILayout.Label("速度");
            GUILayout.BeginHorizontal();
            DrawSpeedButton("0.5×", 0.5f);
            DrawSpeedButton("1×", 1f);
            DrawSpeedButton("2×", 2f);
            DrawSpeedButton("4×", 4f);
            GUILayout.EndHorizontal();

            if (awaitingMoveTarget)
                GUILayout.Label("下一次左键点击地面将下达移动命令；Esc 取消。");
            else if (showHotkeys)
                GUILayout.Label("1/2 选军团 · A进攻 · M移动 · H防守 · R撤退");

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

        private void DrawSpeedButton(string label, float speed)
        {
            bool selected = Mathf.Approximately(controller.SimulationSpeed, speed);
            if (GUILayout.Toggle(selected, label, GUI.skin.button) && !selected)
                controller.SetSimulationSpeed(speed);
        }

        private bool IsMouseOverPanel()
        {
            Vector2 guiMouse = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
            return ResolvePanelRect().Contains(guiMouse);
        }

        private Rect ResolvePanelRect()
        {
            float width = Mathf.Max(240f, panelWidth);
            return new Rect(Mathf.Max(8f, Screen.width - width - 8f), 8f, width, 252f);
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
