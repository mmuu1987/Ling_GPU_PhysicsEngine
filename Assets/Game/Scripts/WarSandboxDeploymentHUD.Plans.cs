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
                    // The UI hides the last "  /  " segment, so the map name stays visible and the id is last.
                    planLabels[i] = planSlots[i] + "  /  " + plan.displayName + "  /  " + MapName(plan.battlefieldId) +
                        (IsCurrentMap(plan.battlefieldId) ? "" : "（载入时切换地图）") +
                        (plan.statOverrides != null && plan.statOverrides.Length > 0 ? "  /  含兵种数值" : "") + "  /  " + plan.battlefieldId;
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
                GUILayout.Label(overwrite ? "旧文件将被替换。" : PlanLoadNote(), label);
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
            else if (PlanNeedsSwitch(pendingPlanSlot, out var foreign))
            {
                if (!TrySwitchForPlan(pendingPlanSlot, foreign, out var switchError)) { planMessage = switchError; planActionFailed28 = true; }
            }
            else if (deployment.TryLoadPlan(pendingPlanSlot, out var error))
            {
                selected = 0; plansOpen = false; ReadFields();
            }
            else { planMessage = error; planActionFailed28 = true; }
            clearFocusRequested = true;
        }

        // 2026-10-17: a plan belongs to its battlefield ("以方案为准"). Loading a plan saved on another battlefield
        // switches to that battlefield first and loads the plan once the new scene is ready. A missing or changed
        // battlefield (version / terrain) is reported up front and nothing is switched.
        private static string resumePlanSlot, resumePlanDirectory;
        private static float resumePlanDeadline;
        private string noteSlot, noteText;

        private static WarSandboxBattlefieldEntry FindMap(string id)
        {
            var entries = WarSandboxSceneSession.Instance != null && WarSandboxSceneSession.Instance.catalog != null
                ? WarSandboxSceneSession.Instance.catalog.entries : null;
            if (entries != null) foreach (var e in entries) if (e != null && e.id == id) return e;
            return null;
        }

        private static string MapName(string id)
        {
            var e = FindMap(id);
            return e != null && !string.IsNullOrEmpty(e.displayName) ? e.displayName : id;
        }

        private static bool IsCurrentMap(string id)
        {
            var session = WarSandboxSceneSession.Instance;
            return session == null || string.IsNullOrEmpty(session.CurrentEntryId) || session.CurrentEntryId == id;
        }

        private bool PlanNeedsSwitch(string slot, out WarSandboxPlanFile plan)
        {
            plan = null;
            return deployment != null && !string.IsNullOrEmpty(slot) && deployment.PlanStore.TryLoad(slot, out plan, out _) &&
                plan != null && !IsCurrentMap(plan.battlefieldId);
        }

        private string PlanLoadNote()
        {
            if (noteSlot != pendingPlanSlot || noteText == null)
            {
                noteSlot = pendingPlanSlot;
                noteText = !PlanNeedsSwitch(pendingPlanSlot, out var plan) ? "当前输入将被替换，已更新的布阵可撤销恢复。"
                    : SwitchProblem(plan) ?? "此方案属于地图「" + MapName(plan.battlefieldId) + "」：将切换到该地图并自动载入方案。当前布阵未保存的改动会丢失。";
            }
            return noteText;
        }

        private static string SwitchProblem(WarSandboxPlanFile plan)
        {
            var map = FindMap(plan.battlefieldId);
            if (map == null) return "方案的地图已不存在：" + plan.battlefieldId + "，无法载入。";
            if (map.contentVersion != plan.battlefieldVersion || map.terrainId != plan.terrainId || map.terrainVersion != plan.terrainVersion)
                return "地图「" + MapName(map.id) + "」已更新（版本或地形变化），此方案无法载入。";
            return null;
        }

        private bool TrySwitchForPlan(string slot, WarSandboxPlanFile plan, out string error)
        {
            if (!TryEnterPlanBattlefield(slot, deployment.PlanStore, true, out error)) return false;
            plansOpen = false;
            return true;
        }

        /// <summary>Map display name for plan lists (catalog name, else the id).</summary>
        public static string PlanMapName(string battlefieldId) => MapName(battlefieldId);

        /// <summary>
        /// Enters the plan's battlefield and loads the plan there once the scene is ready (also used by the menu's 方案库).
        /// A missing or changed battlefield is reported and nothing is switched.
        /// </summary>
        public static bool TryEnterPlanBattlefield(string slot, WarSandboxLocalPlanStore store, bool confirmEndBattle, out string error)
        {
            error = null;
            var session = WarSandboxSceneSession.Instance;
            if (session == null || store == null) { error = "当前无法切换地图。"; return false; }
            if (!store.TryLoad(slot, out var plan, out error) || plan == null) { error = error ?? "方案读取失败。"; return false; }
            if ((error = SwitchProblem(plan)) != null) return false;
            resumePlanSlot = slot; resumePlanDirectory = store.DirectoryPath;
            resumePlanDeadline = Time.realtimeSinceStartup + 180f;
            if (!session.TryEnterBattlefield(plan.battlefieldId, confirmEndBattle, out error))
            { resumePlanSlot = null; error = "切换到地图「" + MapName(plan.battlefieldId) + "」失败：" + error; return false; }
            return true;
        }

        // Runs on the HUD of the newly loaded battlefield (statics survive the scene change).
        private void ResumePendingPlan()
        {
            var session = WarSandboxSceneSession.Instance;
            if (session == null || session.State == WarSandboxEntryState.Failed || session.State == WarSandboxEntryState.Menu && !session.IsLoading ||
                Time.realtimeSinceStartup > resumePlanDeadline) { resumePlanSlot = null; return; }
            if (deployment == null || deployment.controller == null || session.InputBlocked || session.Controller != deployment.controller) return;
            string slot = resumePlanSlot; resumePlanSlot = null;
            if (!string.IsNullOrEmpty(resumePlanDirectory) && deployment.PlanStore.DirectoryPath != resumePlanDirectory)
                deployment.PlanStore = new WarSandboxLocalPlanStore(resumePlanDirectory);
            if (!deployment.IsEditing) Open(true);
            if (!deployment.IsEditing) { Debug.LogWarning("[方案] 切换地图后无法进入布阵：" + inputError); return; }
            if (deployment.TryLoadPlan(slot, out var error)) { selected = 0; plansOpen = false; planMessage = null; ReadFields(); }
            else { OpenPlanLibrary(); selectedPlanSlot = slot; planMessage = error; planActionFailed28 = true; }
            clearFocusRequested = true;
        }

        private void SavePlan(string slot, string name, bool overwrite)
        {
            if (deployment.TrySavePlan(slot, name, overwrite, out var error))
            { planActionFailed28 = false; planMessage = PlanState28.For(deployment.controller) != null ? "已保存到本地：" + slot + " · 未自动应用或开战" : "已保存：" + slot; RefreshPlans(); selectedPlanSlot = slot; }
            else { planMessage = error; planActionFailed28 = true; }
        }
    }
}

