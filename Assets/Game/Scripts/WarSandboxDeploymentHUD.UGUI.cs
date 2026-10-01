using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace MassEngine.Game
{
    public sealed partial class WarSandboxDeploymentHUD
    {
        private WarSandboxUGUI runtimeUi;
        private float nextUiRefresh;
        private void RefreshUGUI()
        {
            if (runtimeUi == null) runtimeUi = new WarSandboxUGUI(transform, "Deployment Canvas", 100);
            if (clearFocusRequested)
            {
                EventSystem.current?.SetSelectedGameObject(null); clearFocusRequested = false;
            }
            runtimeUi.SetVisible(true);
            if (Time.unscaledTime < nextUiRefresh) return;
            nextUiRefresh = Time.unscaledTime + 0.1f;
            var ui = runtimeUi; ui.Begin();
            float w = ui.Width, h = ui.Height;
            ui.Panel("deployment-bg", new Rect(0, 0, w, h), WarSandboxUGUI.Background);
            if (Confirming) DrawUGUIConfirmation(ui);
            else if (plansOpen) DrawUGUIPlans(ui);
            else if (statsOpen && deployment.Draft != null) DrawUGUIStats(ui);
            else if (deployment.Draft != null)
            {
                bool narrow = w < 900;
                // The battle system strip (设置 / 战场目录 / 退出) sits top right, so the plan library button goes below it.
                ui.Code("deployment-brand", new Rect(20, 10, w - 270, 22), "BATTLE PLANNER // 战前准备", 12, WarSandboxUGUI.Accent, TextAnchor.MiddleLeft, true);
                ui.Panel("deployment-title-mark", new Rect(20, 40, 3, 26), WarSandboxUGUI.Accent, false);
                ui.Label("deployment-title", new Rect(18, 34, w - 270, 40), "配兵与布阵", 28, null, true);
                ui.Button("deployment-plans", new Rect(w - 16 - 138, 48, 138, 34), "本地方案库", OpenPlanLibrary, true);
                float top = narrow ? 128 : 94;
                if (narrow)
                {
                    ui.Button("tab-roster", new Rect(24, 84, (w - 56) / 2, 34), "01  军团编成", () => narrowMap = false, false, true, !narrowMap);
                    ui.Button("tab-map", new Rect(32 + (w - 56) / 2, 84, (w - 56) / 2, 34), "02  部署预览", () => narrowMap = true, false, true, narrowMap);
                }
                float bodyHeight = Mathf.Max(180, h - top - 112);
                float sidebar = narrow ? w - 40 : 344;
                if (!narrow || !narrowMap) DrawUGUIRoster(ui, new Rect(20, top, sidebar, bodyHeight));
                if (!narrow || narrowMap) DrawUGUIMap(ui, new Rect(narrow ? 20 : 382, top, narrow ? w - 40 : w - 402, bodyHeight));
                bool valid = deployment.TryValidate(out string error);
                ui.Panel("deployment-footer", new Rect(20, h - 100, w - 40, 82));
                ui.Brackets("deployment-footer-br", new Rect(20, h - 100, w - 40, 82), null, 10);
                ui.Label("deployment-validation", new Rect(26, h - 98, w - 52, 32), inputError ?? error ?? (HasPendingInputs() ? "输入待更新 · 应用前将重新检查" : "布阵有效 · 可以开战"), 14,
                    inputError != null || !valid ? WarSandboxUGUI.Danger : WarSandboxUGUI.Accent);
                float bw = Mathf.Min(180, (w - 64) / 3), bx = w - 32 - 3 * bw - 16;
                ui.Button("deployment-cancel", new Rect(bx, h - 60, bw, 34), "取消修改", () => { deployment.CancelEditing(); ReleaseCamera(); });
                ui.Button("deployment-apply", new Rect(bx + bw + 8, h - 60, bw, 34), "应用布阵", () => Apply(false), false, valid);
                ui.Button("deployment-start", new Rect(bx + 2 * (bw + 8), h - 60, bw, 34), "应用并开战", () => Apply(true), true, valid);
            }
            ui.End();
        }
        private void DrawUGUIConfirmation(WarSandboxUGUI ui)
        {
            float w = Mathf.Min(480, ui.Width - 40), x = (ui.Width - w) / 2, y = Mathf.Max(20, (ui.Height - 270) / 2);
            ui.Panel("edit-confirm-card", new Rect(x, y, w, 240)); ui.Brackets("edit-confirm-br", new Rect(x, y, w, 240));
            ui.Code("edit-confirm-code", new Rect(x + 24, y + 8, w - 48, 18), "MISSION // REDEPLOY", 11, WarSandboxUGUI.Accent);
            ui.Label("edit-confirm-title", new Rect(x + 18, y + 26, w - 36, 42), "结束本局并返回布阵？", 24, null, true);
            ui.Label("edit-confirm-note", new Rect(x + 18, y + 76, w - 36, 48), "当前战斗进度不会保留，战前方案仍可继续修改。", 16, WarSandboxUGUI.Muted);
            ui.Button("edit-confirm-yes", new Rect(x + 28, y + 138, w - 56, 38), "结束并布阵", () => { Confirming = false; resumeAfterConfirmation = false; Open(true); }, true);
            ui.Button("edit-confirm-no", new Rect(x + 28, y + 186, w - 56, 34), "取消，回到战场", CancelConfirmation);
        }
        private void DrawUGUIRoster(WarSandboxUGUI ui, Rect area)
        {
            var draft = deployment.Draft;
            ui.Panel("roster-card", area); ui.Brackets("roster-card-br", area, null, 10);
            float x = area.x + 12, y = area.y + 10, width = area.width - 24;
            float toolWidth = (width - 18) / 4;
            ui.Button("roster-add", new Rect(x, y, toolWidth, 32), "新增", () =>
            {
                if (!CommitFields()) return;
                var template = draft.Count > 0 ? draft[selected].template : deployment.Templates[0];
                int team = draft.Count > 0 ? draft[selected].teamId : 0;
                if (draft.Add(template, team, deployment.UseTemplateSpawnDefaults)) { selected = draft.Count - 1; RefreshAfterUiChange(); }
            });
            ui.Button("roster-remove", new Rect(x + toolWidth + 6, y, toolWidth, 32), "移除", () => { draft.Remove(selected); ClampSelection(); RefreshAfterUiChange(); }, false, draft.Count > 0);
            ui.Button("roster-undo", new Rect(x + 2 * (toolWidth + 6), y, toolWidth, 32), "撤销", () => { draft.Undo(); ClampSelection(); RefreshAfterUiChange(); }, false, draft.CanUndo);
            ui.Button("roster-redo", new Rect(x + 3 * (toolWidth + 6), y, toolWidth, 32), "重做", () => { draft.Redo(); ClampSelection(); RefreshAfterUiChange(); }, false, draft.CanRedo);
            y += 42;
            float rosterHeight = Mathf.Min(150, area.height * 0.3f);
            var stats = deployment.DraftStats;
            ui.Scroll("roster-list", new Rect(x, y, width, rosterHeight), Mathf.Max(48, draft.Count * 48));
            if (draft.Count == 0) ui.Label("roster-empty", new Rect(0, 0, width, 44), "暂无编成 · 点击新增开始", 15, WarSandboxUGUI.Muted);
            for (int i = 0; i < draft.Count; i++)
            {
                int index = i; var entry = draft[i];
                ui.Button("roster-item-" + i, new Rect(0, i * 48, width, 42), WarSandboxBattleController.DefaultArmyName(entry.teamId) + "  /  " + entry.Name + "  " + entry.count.ToString("N0") +
                    (stats.HasCustom(entry.template) ? "  · 自定义" : ""), () =>
                { if (index != selected && CommitFields()) { selected = index; templateMenu = armyMenu = false; RefreshAfterUiChange(); } }, false, true, selected == i);
                ui.Panel("roster-stripe-" + i, new Rect(0, i * 48, 4, 42), WarSandboxTeamPalette.Resolve(entry.teamId), false);
            }
            ui.EndScroll();
            y += rosterHeight + 12;
            if (draft.Count == 0) return;
            float extra = (templateMenu ? deployment.Templates.Count * 38 : 0) + (armyMenu ? (ConfigValidator.MaxTeamId + 1) * 38 : 0);
            ui.Scroll("roster-properties", new Rect(x, y, width, Mathf.Max(60, area.yMax - y - 12)), 548 + 42 + extra + (deployment.rosterPolicy != null ? 72 : 0));
            float fy = 0;
            if (deployment.rosterPolicy != null) { ui.Label("roster-policy-note", new Rect(0, fy, width, 64), deployment.rosterPolicy.explanation, 12, WarSandboxUGUI.Muted); fy += 72; }
            ui.Code("field-section", new Rect(0, fy, width, 26), "UNIT // 编成属性", 13, WarSandboxUGUI.Accent, TextAnchor.MiddleLeft, true); fy += 32;
            ui.Button("field-template", new Rect(0, fy, width, 34), "兵种  /  " + draft[selected].Name + "  ▾", () => templateMenu = !templateMenu); fy += 42;
            if (templateMenu)
                for (int i = 0; i < deployment.Templates.Count; i++)
                {
                    var template = deployment.Templates[i];
                    ui.Button("template-" + i, new Rect(12, fy, width - 12, 32), template.unitTypeName, () =>
                    { if (CommitFields()) { deployment.SelectTemplate(selected, template); templateMenu = false; RefreshAfterUiChange(); } }); fy += 38;
                }
            bool custom = stats.HasCustom(draft[selected].template);
            ui.Button("field-unit-stats", new Rect(0, fy, width, 34), "兵种属性  /  " + (custom ? "自定义数值" : "官方数值") + "  ›", OpenStats, false, true, custom); fy += 42;
            ui.Button("field-army", new Rect(0, fy, width, 34), "军团  /  " + WarSandboxBattleController.DefaultArmyName(draft[selected].teamId) + "  ▾", () => armyMenu = !armyMenu); fy += 42;
            if (armyMenu)
                for (int i = 0; i <= ConfigValidator.MaxTeamId; i++)
                {
                    int team = i;
                    ui.Button("team-" + i, new Rect(12, fy, width - 12, 32), WarSandboxBattleController.DefaultArmyName(team), () =>
                    { if (CommitFields()) { var entry = draft[selected]; entry.teamId = team; draft.Set(selected, entry); armyMenu = false; RefreshAfterUiChange(); } }); fy += 38;
                }
            ui.Button("army-add", new Rect(0, fy, (width - 8) / 2, 32), "新增军团", () =>
            {
                if (!CommitFields()) return; int team = draft.NextArmyId();
                if (team >= 0 && draft.Add(draft[selected].template, team, deployment.UseTemplateSpawnDefaults)) { selected = draft.Count - 1; RefreshAfterUiChange(); }
                else inputError = "军团或编成数量已达上限。";
            });
            ui.Button("army-remove", new Rect((width + 8) / 2, fy, (width - 8) / 2, 32), "移除整团", () => { draft.RemoveArmy(draft[selected].teamId); ClampSelection(); RefreshAfterUiChange(); }); fy += 44;
            UGUIField(ui, "count", "人数", count, v => count = v, ref fy, width);
            UGUIField(ui, "x", "中心 X", this.x, v => this.x = v, ref fy, width);
            UGUIField(ui, "z", "中心 Z", z, v => z = v, ref fy, width);
            ui.Button("field-manual", new Rect(0, fy, width, 32), manual ? "阵型尺寸：手动" : "阵型尺寸：按密度", () => manual = !manual, false, true, manual); fy += 40;
            if (manual) { UGUIField(ui, "depth", "纵深 X", depth, v => depth = v, ref fy, width); UGUIField(ui, "width", "正面宽 Z", this.width, v => this.width = v, ref fy, width); }
            else { UGUIField(ui, "density", "密度", density, v => density = v, ref fy, width); UGUIField(ui, "aspect", "宽深比", aspect, v => aspect = v, ref fy, width); }
            ui.Button("field-update", new Rect(0, fy, width, 36), "更新编成", () => { if (CommitFields()) RefreshAfterUiChange(); }, true);
            ui.EndScroll();
        }
        private static void UGUIField(WarSandboxUGUI ui, string id, string name, string x, Action<string> change, ref float y, float width)
        {
            ui.Label("label-" + id, new Rect(0, y, 96, 32), name, 14, WarSandboxUGUI.Muted);
            ui.Field("input-" + id, new Rect(96, y, width - 96, 32), x, change); y += 40;
        }
        private void DrawUGUIMap(WarSandboxUGUI ui, Rect area)
        {
            ui.Panel("map-card", area); ui.Brackets("map-card-br", area, null, 10);
            ui.Code("map-code", new Rect(area.x + 22, area.y + 6, area.width - 160, 16), "RECON // DEPLOYMENT", 10, WarSandboxUGUI.Muted);
            ui.Label("map-title", new Rect(area.x + 12, area.y + 16, area.width - 144, 34), "部署预览", 21, null, true);
            ui.Button("map-place", new Rect(area.xMax - 124, area.y + 10, 110, 32), placing ? "取消放置" : "点击放置", () => placing = !placing, false, deployment.Draft.Count > 0, placing);
            string note = placing ? "在下方点击，放置选中的编成" : "色块对应军团 · 点击色块选择编成";
            if (deployment.controller.manager.terrainSurfaceAsset != null && deployment.Draft.Count > 0 &&
                deployment.controller.TryResolveGroundPoint(deployment.Draft[selected].center, out var ground, out _))
                note = "地表高程 " + ground.y.ToString("F1") + "m · 完整脚印须可通行";
            ui.Label("map-note", new Rect(area.x + 12, area.y + 48, area.width - 24, 30), note, 13, WarSandboxUGUI.Muted);
            Vector2 world = deployment.WorldSize; if (world.x <= 0 || world.y <= 0) return;
            float scale = Mathf.Min((area.width - 32) / world.x, (area.height - 106) / world.y);
            Rect map = new Rect(area.center.x - world.x * scale / 2, area.y + 86, world.x * scale, world.y * scale);
            ui.Panel("deployment-map-surface", map, WarSandboxUGUI.FieldFill, false, WarSandboxUGUI.Line);
            for (int i = 1; i < 8; i++)
            {
                ui.Panel("map-grid-x-" + i, new Rect(map.x + map.width * i / 8, map.y, 1, map.height), new Color32(20, 38, 50, 255), false);
                ui.Panel("map-grid-y-" + i, new Rect(map.x, map.y + map.height * i / 8, map.width, 1), new Color32(20, 38, 50, 255), false);
            }
            var rules = deployment.Draft.Rules;
            if (rules.staticObstaclesEnabled && rules.staticObstacles != null)
                for (int i = 0; i < rules.staticObstacles.Length; i++)
                    ui.Panel("map-wall-" + i, MapBounds(rules.staticObstacles[i].Bounds, world, map), WarSandboxUGUI.Dim, false);
            if (rules.gameMode == WarSandboxGameMode.ControlPoint)
            {
                var point = WarSandboxMinimapProjection.WorldToMap(rules.controlPointCenter, world, map);
                ui.Panel("map-objective", new Rect(point.x - 5, point.y - 5, 10, 10), WarSandboxUGUI.Amber, false);
            }
            var draft = deployment.Draft;
            for (int i = 0; i < draft.Count; i++)
            {
                var entry = draft[i]; var bounds = entry.Bounds;
                if (float.IsNaN(bounds.width) || float.IsInfinity(bounds.width) || float.IsNaN(bounds.height) || float.IsInfinity(bounds.height)) continue;
                var rect = MapBounds(bounds, world, map);
                rect = Rect.MinMaxRect(Mathf.Max(map.xMin, rect.xMin), Mathf.Max(map.yMin, rect.yMin), Mathf.Min(map.xMax, rect.xMax), Mathf.Min(map.yMax, rect.yMax));
                if (rect.width <= 0 || rect.height <= 0) continue;
                ui.Panel("map-unit-" + i, rect, WarSandboxTeamPalette.Resolve(entry.teamId), false);
                if (i == selected)
                {
                    ui.Panel("map-selected-top", new Rect(rect.x, rect.y, rect.width, 2), WarSandboxUGUI.Accent, false);
                    ui.Panel("map-selected-bottom", new Rect(rect.x, rect.yMax - 2, rect.width, 2), WarSandboxUGUI.Accent, false);
                    ui.Panel("map-selected-left", new Rect(rect.x, rect.y, 2, rect.height), WarSandboxUGUI.Accent, false);
                    ui.Panel("map-selected-right", new Rect(rect.xMax - 2, rect.y, 2, rect.height), WarSandboxUGUI.Accent, false);
                }
                if (rect.width > 40 && rect.height > 24) ui.Label("map-number-" + i, rect, (i + 1).ToString(), 14, WarSandboxUGUI.Ink, true);
            }
            ui.PointerArea("map-pointer", map, (normalized, button, shift) =>
            {
                if (button != 0 || !CommitFields()) return;
                Vector2 point = new Vector2(map.x + normalized.x * map.width, map.y + normalized.y * map.height);
                if (placing && draft.Count > 0)
                {
                    var entry = draft[selected]; var center = WarSandboxMinimapProjection.MapToWorld(point, world, map);
                    if (!deployment.controller.TryResolveGroundPoint(center, out center, out string error)) { inputError = error; return; }
                    // Authoring Y is not a terrain height array; the runtime presentation samples it on apply.
                    entry.center = new Vector3(center.x, entry.center.y, center.z); draft.Set(selected, entry); placing = false; RefreshAfterUiChange();
                }
                else for (int i = draft.Count - 1; i >= 0; i--)
                    if (MapBounds(draft[i].Bounds, world, map).Contains(point)) { selected = i; RefreshAfterUiChange(); break; }
            });
        }
        private void DrawUGUIPlans(WarSandboxUGUI ui)
        {
            float w = Mathf.Min(920, ui.Width - 40), x = (ui.Width - w) / 2, h = ui.Height;
            ui.Code("plans-brand", new Rect(x + 10, 16, w - 260, 24), "LOCAL PLANS // 本地方案", 12, WarSandboxUGUI.Accent, TextAnchor.MiddleLeft, true);
            ui.Label("plans-title", new Rect(x, 42, w - 260, 42), "本地方案库", 28, null, true);
            ui.Button("plans-refresh", new Rect(x + w - 244, 52, 100, 34), "刷新列表", RefreshPlans);
            ui.Button("plans-back", new Rect(x + w - 132, 52, 132, 34), "返回布阵", () => { plansOpen = false; pendingPlanAction = null; clearFocusRequested = true; });
            if (pendingPlanAction != null)
            {
                bool save = pendingPlanAction == "save";
                ui.Panel("plans-confirm", new Rect(x, 114, w, 234)); ui.Brackets("plans-confirm-br", new Rect(x, 114, w, 234));
                ui.Label("plans-confirm-title", new Rect(x + 18, 134, w - 36, 44), (save ? "覆盖方案 " : "载入方案 ") + pendingPlanSlot + "？", 24, null, true);
                ui.Label("plans-confirm-note", new Rect(x + 18, 192, w - 36, 60), save ? "旧文件将被替换。写入失败时会保留旧方案。" : "当前输入将被替换，已更新的布阵可撤销恢复。", 16, WarSandboxUGUI.Muted);
                ui.Button("plans-confirm-yes", new Rect(x + 28, 278, (w - 68) / 2, 40), save ? "确认覆盖" : "确认载入", ExecutePlanAction, true);
                ui.Button("plans-confirm-no", new Rect(x + 40 + (w - 68) / 2, 278, (w - 68) / 2, 40), "取消", () => pendingPlanAction = null);
            }
            else
            {
                ui.Panel("plans-save-card", new Rect(x, 104, w, 152)); ui.Brackets("plans-save-card-br", new Rect(x, 104, w, 152), null, 10);
                ui.Label("plans-save-heading", new Rect(x + 12, 112, w - 24, 28), "保存当前布阵", 18, null, true);
                ui.Label("plans-slot-label", new Rect(x + 12, 150, 70, 34), "编号", 14, WarSandboxUGUI.Muted);
                ui.Field("plans-slot", new Rect(x + 82, 150, 96, 34), planSlot, v => planSlot = v, WarSandboxLocalPlanStore.MaxSlotLength);
                ui.Label("plans-name-label", new Rect(x + 188, 150, 62, 34), "名称", 14, WarSandboxUGUI.Muted);
                ui.Field("plans-name", new Rect(x + 250, 150, w - 270, 34), planName, v => planName = v, 80);
                ui.Button("plans-save", new Rect(x + 22, 200, w - 44, 36), deployment.PlanStore.Exists(planSlot) ? "覆盖方案" : "另存新方案", () =>
                {
                    if (!CommitFields()) { planMessage = inputError; return; }
                    if (deployment.PlanStore.Exists(planSlot)) { pendingPlanAction = "save"; pendingPlanSlot = planSlot; pendingPlanName = planName; }
                    else SavePlan(planSlot, planName, false);
                    clearFocusRequested = true;
                }, true);
                ui.Label("plans-list-heading", new Rect(x, 274, w, 28), "已保存方案  /  " + planSlots.Length, 17, null, true);
                ui.Scroll("plans-list", new Rect(x, 312, w, Mathf.Max(80, h - 478)), Mathf.Max(72, planSlots.Length * 62));
                if (planSlots.Length == 0) ui.Label("plans-empty", new Rect(16, 10, w - 32, 64), "还没有方案。保存当前布阵后，下次启动仍能继续使用。", 16, WarSandboxUGUI.Muted);
                for (int i = 0; i < planSlots.Length; i++)
                {
                    int index = i;
                    string display = planLabels[i]; int suffix = display.LastIndexOf("  /  ", StringComparison.Ordinal);
                    if (suffix > 0 && !display.Contains("文件损坏")) display = display.Substring(0, suffix);
                    ui.Button("plan-item-" + i, new Rect(8, i * 62, w - 16, 54), display, () =>
                    {
                        selectedPlanSlot = planSlots[index]; planSlot = selectedPlanSlot;
                        if (deployment.PlanStore.TryLoad(selectedPlanSlot, out var plan, out _)) planName = plan.displayName;
                        planMessage = null; clearFocusRequested = true;
                    }, false, true, planSlots[i] == selectedPlanSlot);
                }
                ui.EndScroll();
                ui.Button("plans-load", new Rect(x, h - 150, w, 40), "载入选中方案", () => { pendingPlanAction = "load"; pendingPlanSlot = selectedPlanSlot; clearFocusRequested = true; }, true, selectedPlanSlot != null);
            }
            if (!string.IsNullOrEmpty(planMessage)) ui.Label("plans-message", new Rect(x, h - 106, w, 40), planMessage, 14, WarSandboxUGUI.Accent);
        }
    }
}
