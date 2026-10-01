using System;
using UnityEngine;

namespace MassEngine.Game
{
    public sealed partial class WarSandboxDeploymentHUD
    {
        private bool plansOpen;
        private string planSlot = "A", planName = "方案 A", planMessage, selectedPlanSlot;
        private string pendingPlanAction, pendingPlanSlot, pendingPlanName;
        private string[] planSlots = new string[0], planLabels = new string[0];
        private Vector2 planScroll;

        public void OpenPlanLibrary()
        {
            if (deployment == null || !deployment.IsEditing) return;
            plansOpen = true; pendingPlanAction = null; planMessage = null;
            placing = templateMenu = armyMenu = false;
            RefreshPlans(); clearFocusRequested = true;
        }

        private void RefreshPlans()
        {
            if (!deployment.PlanStore.TryListSlots(out planSlots, out var error)) planMessage = error;
            planLabels = new string[planSlots.Length];
            for (int i = 0; i < planSlots.Length; i++)
            {
                if (deployment.PlanStore.TryLoad(planSlots[i], out var plan, out _))
                    planLabels[i] = planSlots[i] + "  /  " + plan.displayName + "  /  " + plan.battlefieldId +
                        (plan.statOverrides != null && plan.statOverrides.Length > 0 ? "  /  含兵种数值" : "");
                else planLabels[i] = planSlots[i] + "  /  文件损坏或版本不支持";
            }
            if (Array.IndexOf(planSlots, selectedPlanSlot) < 0) selectedPlanSlot = null;
        }

        private void DrawPlanLibrary()
        {
            float w = Mathf.Min(900, Screen.width - 32);
            var area = new Rect((Screen.width - w) * 0.5f, 12, w, Mathf.Max(180, Screen.height - 80));
            GUILayout.BeginArea(area);
            GUILayout.BeginHorizontal();
            GUILayout.Label("本地方案", heading, GUILayout.Height(32));
            if (GUILayout.Button(new GUIContent("↻", "刷新方案列表"), button, GUILayout.Width(40), GUILayout.Height(32)))
            { RefreshPlans(); GUIUtility.ExitGUI(); }
            if (GUILayout.Button("返回布阵", button, GUILayout.Width(100), GUILayout.Height(32)))
            { plansOpen = false; pendingPlanAction = null; GUI.FocusControl(null); GUIUtility.ExitGUI(); }
            GUILayout.EndHorizontal();
            if (pendingPlanAction != null)
            {
                GUILayout.Space(20);
                bool overwrite = pendingPlanAction == "save";
                GUILayout.Label((overwrite ? "覆盖方案 " : "载入方案 ") + pendingPlanSlot + "？", heading);
                GUILayout.Label(overwrite ? "旧文件将被替换。" : "当前输入将被替换，已更新的布阵可撤销恢复。", label);
                GUILayout.Space(16);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(overwrite ? "确认覆盖" : "确认载入", button, GUILayout.Height(36))) ExecutePlanAction();
                if (GUILayout.Button("取消", button, GUILayout.Height(36))) { pendingPlanAction = null; GUIUtility.ExitGUI(); }
                GUILayout.EndHorizontal();
            }
            else
            {
                GUILayout.Space(8);
                GUILayout.BeginHorizontal();
                GUILayout.Label("编号", label, GUILayout.Width(48));
                planSlot = GUILayout.TextField(planSlot, WarSandboxLocalPlanStore.MaxSlotLength, field, GUILayout.Width(110), GUILayout.Height(30));
                GUILayout.Label("名称", label, GUILayout.Width(48));
                planName = GUILayout.TextField(planName, 80, field, GUILayout.Height(30));
                GUILayout.EndHorizontal();
                if (GUILayout.Button(deployment.PlanStore.Exists(planSlot) ? "覆盖方案" : "另存方案", button, GUILayout.Height(34)))
                {
                    if (CommitFields())
                    {
                        if (deployment.PlanStore.Exists(planSlot))
                        { pendingPlanAction = "save"; pendingPlanSlot = planSlot; pendingPlanName = planName; }
                        else SavePlan(planSlot, planName, false);
                    }
                    else planMessage = inputError;
                    GUIUtility.ExitGUI();
                }
                GUILayout.Space(8);
                planScroll = GUILayout.BeginScrollView(planScroll);
                if (planSlots.Length == 0) GUILayout.Label("暂无本地方案", label);
                for (int i = 0; i < planSlots.Length; i++)
                {
                    Color before = GUI.backgroundColor;
                    if (planSlots[i] == selectedPlanSlot) GUI.backgroundColor = new Color(0.4f, 0.8f, 0.65f);
                    if (GUILayout.Button(planLabels[i], button, GUILayout.MinHeight(40)))
                    {
                        selectedPlanSlot = planSlots[i]; planSlot = planSlots[i];
                        if (deployment.PlanStore.TryLoad(selectedPlanSlot, out var plan, out _)) planName = plan.displayName;
                        planMessage = null;
                    }
                    GUI.backgroundColor = before;
                }
                GUILayout.EndScrollView();
                bool beforeEnabled = GUI.enabled; GUI.enabled = selectedPlanSlot != null;
                if (GUILayout.Button("载入选中方案", button, GUILayout.Height(36)))
                { pendingPlanAction = "load"; pendingPlanSlot = selectedPlanSlot; GUIUtility.ExitGUI(); }
                GUI.enabled = beforeEnabled;
            }
            if (!string.IsNullOrEmpty(planMessage)) GUILayout.Label(planMessage, note, GUILayout.MinHeight(36));
            GUILayout.EndArea();
        }

        private void ExecutePlanAction()
        {
            string action = pendingPlanAction; pendingPlanAction = null;
            if (action == "save") SavePlan(pendingPlanSlot, pendingPlanName, true);
            else if (deployment.TryLoadPlan(pendingPlanSlot, out var error))
            {
                selected = 0; plansOpen = false; ReadFields();
            }
            else planMessage = error;
            clearFocusRequested = true;
        }

        private void SavePlan(string slot, string name, bool overwrite)
        {
            if (deployment.TrySavePlan(slot, name, overwrite, out var error))
            { planMessage = "已保存：" + slot; RefreshPlans(); selectedPlanSlot = slot; }
            else planMessage = error;
        }
    }
}
