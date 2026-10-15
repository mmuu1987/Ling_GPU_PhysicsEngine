using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace MassEngine.Game
{
    /// <summary>Legion dossier edits the existing draft; leaving it never applies GPU state or saves a plan.</summary>
    public sealed partial class WarSandboxDeploymentHUD
    {
        private bool legionOpen, pickerOpen, removeArmyConfirm, spatialSelection;
        private int pickerRole; // 0 all, 1 melee, 2 ranged; filtering never changes selection.
        public bool LegionOpen => legionOpen;
        public bool PickerOpen => pickerOpen;
        private void ReturnFromLegion()
        {
            if (!CommitFields()) return;
            legionOpen = pickerOpen = removeArmyConfirm = false;
            RefreshAfterUiChange(); nextUiRefresh = 0;
        }
        private bool SelectFormation(int index)
        {
            if (!CommitFields()) return false;
            selected = index; templateMenu = armyMenu = false; RefreshAfterUiChange(); nextUiRefresh = 0; return true;
        }
        private void OpenLegion(int index)
        {
            if (!CommitFields()) return;
            selected = index; legionOpen = true; placing = false; RefreshAfterUiChange(); nextUiRefresh = 0;
        }
        private bool AddFormation(bool newArmy)
        {
            if (!CommitFields()) return false;
            var draft = deployment.Draft;
            if (deployment.ChoiceTemplates.Count == 0) { inputError = "当前战场没有可用兵种。"; return false; }
            var template = draft.Count > 0 ? draft[selected].template : deployment.ChoiceTemplates[0];
            int team = newArmy ? draft.NextArmyId() : draft.Count > 0 ? draft[selected].teamId : 0;
            if (team >= 0 && draft.Add(template, team, deployment.UseTemplateSpawnDefaults))
            { selected = draft.Count - 1; RefreshAfterUiChange(); nextUiRefresh = 0; return true; }
            inputError = "军团或编成数量已达上限。"; return false;
        }
        private List<int> ArmyIds()
        {
            var ids = new List<int>(); var draft = deployment.Draft;
            for (int i = 0; i < draft.Count; i++) if (!ids.Contains(draft[i].teamId)) ids.Add(draft[i].teamId);
            return ids;
        }
        private WarSandboxUnitTemplateEntry PortraitEntry(UnitTypeConfig template)
        {
            var catalog = deployment.battlefieldCatalog;
            if (catalog == null || catalog.templates == null || template == null) return null;
            foreach (var entry in catalog.templates) if (entry != null && entry.config == template) return entry;
            // No similar-looking substitute: a missing real preview is shown honestly.
            return null;
        }
        private void RefreshUGUI()
        {
            if (translation.Active) return; // Map layout is frozen for the captured gesture; ghost alone follows the pointer.
            // UI Toolkit presenter (Tools/战争沙盒 UI): same state and actions, see WarSandboxDeploymentHUD.Toolkit*.cs.
            if (Ui.WarSandboxUiToolkit.Enabled) { runtimeUi?.SetVisible(false); RefreshDeployToolkit(); return; }
            HideDeployToolkit();
            if (runtimeUi == null) runtimeUi = new WarSandboxUGUI(transform, "Deployment Canvas", 100);
            if (clearFocusRequested) { EventSystem.current?.SetSelectedGameObject(null); clearFocusRequested = false; }
            runtimeUi.SetVisible(true);
            if (Time.unscaledTime < nextUiRefresh) return;
            nextUiRefresh = Time.unscaledTime + .1f;
            var ui = runtimeUi; ui.Begin();
            ui.Panel("deployment-bg", new Rect(0, 0, ui.Width, ui.Height), WarSandboxUGUI.Background);
            if (Confirming) DrawUGUIConfirmation(ui);
            else if (plansOpen) DrawUGUIPlans(ui);
            else if (statsOpen && deployment.Draft != null) DrawUGUIStats(ui);
            else if (deployment.Draft != null)
            {
                ClampSelection();
                if (pickerOpen && deployment.Draft.Count > 0) DrawToyPicker(ui);
                else if (legionOpen) DrawLegion(ui);
                else DrawDeploymentOverview(ui);
            }
            DrawTightDialog(ui);
            ui.End();
        }
        private bool Info31 => RosterInfo31.Active(deployment);
        private void DraftStatus(WarSandboxUGUI ui, Rect rect)
        {
            if (Check32) { DrawCheck32(ui, rect); return; }
            bool valid = deployment.TryValidate(out string error);
            string message = inputError ?? (HasPendingInputs() ? "输入尚未更新 · 点击更新或完成返回；不会立即影响战斗" : error ?? TightDraftNotice() ?? "已写入本局草稿 · 应用布阵后生效，保存方案需另存");
            if (FlowPolish23.Active(deployment))
                message = inputError ?? (error != null ? "暂不能开战 · " + error : HasPendingInputs() ? "输入未更新 · 点击更新或完成返回，再检查布阵" : TightDraftNotice() ?? (legionOpen ? "编成已更新 · 完成返回后检查位置；尚未开战" : FlowPolish23.DraftSummary(deployment)));
            var state28 = PlanState28.For(deployment.controller);
            if (state28 != null && inputError == null && valid && TightDraftNotice() == null) message = state28.DraftStatus(HasPendingInputs());
            ui.Label("deployment-validation", rect, message, 14, inputError != null || !valid ? WarSandboxUGUI.Danger : WarSandboxUGUI.Muted);
        }
        private void DrawDeploymentOverview(WarSandboxUGUI ui)
        {
            float w = ui.Width, h = ui.Height;
            ui.Panel("deployment-header", new Rect(16, 50, w - 32, 62), WarSandboxUGUI.Bar);
            ui.Label("deployment-title", new Rect(32, 58, w - 350, 44), FlowPolish23.Active(deployment) ? "编成 → 布阵 → 开战" : "战前布阵", FlowPolish23.Active(deployment) ? 25 : 29, WarSandboxUGUI.Ink, true);
            ui.Button("deployment-plans", new Rect(w - 200, 62, 164, 38), "方案库", OpenPlanLibrary);
            DrawTightHeader(ui);
            float top = 130, body = h - (Check32 ? 252 : 236);
            // 2026-10-10 feedback: 编成 and 布阵 on one page (tree | map | inspector), see WarSandboxDeploymentHUD.Merged.cs.
            DrawMergedOverview(ui, top, body);
            DrawDeploymentFooter(ui);
            if (deployment.Draft.Count > 0) DrawLegionOverlays(ui, deployment.Draft[selected].teamId);
        }
        private void DrawDeploymentFooter(WarSandboxUGUI ui)
        {
            float w = ui.Width, h = ui.Height;
            ui.Panel("deployment-footer", new Rect(12, h - (Check32 ? 110 : 94), w - 24, Check32 ? 98 : 82), WarSandboxUGUI.Bar);
            DraftStatus(ui, new Rect(24, h - (Check32 ? 108 : 90), w - 48, Check32 ? 46 : 30));
            ui.Button("roster-undo", new Rect(28, h - 54, 78, 32), "撤销", () => { deployment.Draft.Undo(); ClampSelection(); RefreshAfterUiChange(); }, false, deployment.Draft.CanUndo);
            ui.Button("roster-redo", new Rect(116, h - 54, 78, 32), "重做", () => { deployment.Draft.Redo(); ClampSelection(); RefreshAfterUiChange(); }, false, deployment.Draft.CanRedo);
            ui.Button("deployment-cancel", new Rect(w - 486, h - 56, 134, 36), "取消修改", () => { deployment.CancelEditing(); ReleaseCamera(); });
            // Always route pending text through CommitFields/TryApply. The draft may be invalid while being corrected.
            ui.Button("deployment-apply", new Rect(w - 340, h - 56, 140, 36), "应用布阵", () => Apply(false));
            ui.Button("deployment-start", new Rect(w - 186, h - 60, 154, 42), Check32 ? StartLabel32() : FlowPolish23.Active(deployment) ? (HasPendingInputs() ? "更新并开战  →" : "开战  →") : "应用并开战  →", () => { if (!RequestTightStart()) Apply(true); }, true);
        }
        private void DrawLegion(WarSandboxUGUI ui)
        {
            float w = ui.Width, h = ui.Height; var draft = deployment.Draft;
            int team = draft.Count > 0 ? draft[selected].teamId : 0, total = 0, melee = 0, ranged = 0, formations = 0;
            for (int i = 0; i < draft.Count; i++) if (draft[i].teamId == team)
            { total += draft[i].count; formations++; if (WarSandboxUnitStats.IsRanged(draft[i].template)) ranged += draft[i].count; else melee += draft[i].count; }
            ui.Panel("legion-header", new Rect(12, 50, w - 24, 64), WarSandboxUGUI.Bar);
            ui.Button("legion-back", new Rect(28, 62, 150, 40), "‹ 返回布阵", ReturnFromLegion);
            ui.LabelAligned("legion-name", new Rect(204, 56, w - 470, 52), WarSandboxBattleController.DefaultArmyName(team), 30, WarSandboxUGUI.Ink, true, TextAnchor.MiddleCenter);
            ui.Chip("legion-total", new Rect(w - 236, 64, 204, 36), total.ToString("N0") + " 人", WarSandboxUGUI.Surface, WarSandboxUGUI.Ink, null, 22, false);
            float left = (w - 64) * .57f, rx = left + 44, rw = w - rx - 20, top = 184, body = h - top - 100;
            ui.Panel("legion-summary", new Rect(20, 128, w - 40, 44), WarSandboxUGUI.Raised);
            ui.Label("legion-composition", new Rect(30, 130, w - 60, 40), formations + " 个编成     /     " + melee.ToString("N0") + " 近战单位     /     " + ranged.ToString("N0") + " 远程单位", 18, WarSandboxUGUI.Ink, true);
            ui.Scroll("legion-roster", new Rect(20, top, left, body - 62), Mathf.Max(80, formations * 116));
            int row = 0;
            for (int i = 0; i < draft.Count; i++)
            {
                var e = draft[i]; if (e.teamId != team) continue; int index = i; float y = row++ * 116; bool active = i == selected;
                ui.Button("roster-item-" + i, new Rect(0, y, left - 8, 102), "", () => SelectFormation(index), false, true, active);
                var portrait = PortraitEntry(e.template);
                if (portrait != null && portrait.unitPreview != null) ui.PictureFit("formation-portrait-" + i, new Rect(14, y + 8, 76, 86), portrait.unitPreview);
                ui.Label("formation-name-" + i, new Rect(104, y + 12, left - 220, 38), e.Name, 21, WarSandboxUGUI.Ink, true);
                ui.Label("formation-role-" + i, new Rect(104, y + 52, left - 220, 28), (Info31 && active ? "正在编辑 · " : "") + "编成 " + (i + 1).ToString("00") + " · " + (WarSandboxUnitStats.IsRanged(e.template) ? "远程" : "近战"), 14, WarSandboxUGUI.Muted);
                ui.LabelAligned("formation-count-" + i, new Rect(left - 112, y + 24, 98, 50), e.count.ToString("N0") + " 人", 20, WarSandboxUGUI.Accent, true, TextAnchor.MiddleCenter);
            }
            ui.EndScroll();
            ui.Button("roster-add", new Rect(20, top + body - 50, left - 210, 42), "＋ 添加编成", () => AddFormation(false));
            ui.Button("roster-remove", new Rect(left - 178, top + body - 50, 96, 42), "移除编成", () => { draft.Remove(selected); ClampSelection(); RefreshAfterUiChange(); }, false, draft.Count > 0);
            ui.Button("army-remove", new Rect(left - 72, top + body - 50, 94, 42), "移除军团", () => removeArmyConfirm = true, false, draft.Count > 0);
            ui.Panel("legion-dossier", new Rect(rx, top, rw, body));
            if (draft.Count > 0)
            {
                var e = draft[selected]; var portrait = PortraitEntry(e.template);
                ui.LabelAligned("legion-unit-name", new Rect(rx + 12, top + 8, rw - 24, 38), e.Name, 25, WarSandboxUGUI.Ink, true, TextAnchor.MiddleCenter);
                float pictureH = Mathf.Max(110, body - 252);
                var pic = new Rect(rx + 18, top + 50, rw * .54f, pictureH);
                ui.ModelPreview("legion-unit-preview", pic, e.template, portrait != null ? portrait.unitPreview : null);
                string role = WarSandboxUnitStats.IsRanged(e.template) ? "远程单位\n从距离外提供火力支援。" : "近战单位\n接近敌方后进行近身交战。";
                if (Info31) role = WarSandboxUnitStats.IsRanged(e.template) ? "远程单位 · 火力支援" : "近战单位 · 近身交战";
                ui.Label("legion-unit-role", new Rect(rx + rw * .59f, top + 64, rw * .37f, pictureH - 16), role + (Info31 ? "\n\n下方为草稿配置值\n应用后生效 · 默认=官方\n同兵种共用本局覆盖" : "\n\n实际兵种模型 · 3D预览"), 16, WarSandboxUGUI.Muted);
                float sy = top + 56 + pictureH, sw = (rw - 40) / 3;
                var keys = new[] { WarSandboxUnitStat.MaxHp, WarSandboxUnitStat.AttackDamage, WarSandboxUnitStat.AttackInterval };
                for (int i = 0; i < keys.Length; i++)
                {
                    var def = WarSandboxUnitStats.Get(keys[i]); var value = deployment.DraftStats.Resolve(e.template, def);
                    float sx = rx + 12 + i * (sw + 8);
                    ui.Panel("legion-stat-" + i, new Rect(sx, sy, sw, Info31 ? 86 : 68), WarSandboxUGUI.Raised);
                    ui.LabelAligned("legion-stat-name-" + i, new Rect(sx + 2, sy + 3, sw - 4, 24), def.Label, 13, WarSandboxUGUI.Muted, false, TextAnchor.MiddleCenter);
                    if (Info31) ui.LabelAligned("legion-stat-source31-" + i, new Rect(sx + 2, sy + 58, sw - 4, 24), RosterInfo31.Origin(value, def), 12, value.source == WarSandboxStatSource.Official ? WarSandboxUGUI.Muted : WarSandboxUGUI.Accent, false, TextAnchor.MiddleCenter);
                    ui.LabelAligned("legion-stat-value-" + i, new Rect(sx + 2, sy + (Info31 ? 24 : 30), sw - 4, 32), def.Format(value.effective) + def.Unit, 21, WarSandboxUGUI.Ink, true, TextAnchor.MiddleCenter);
                }
                float fy = sy + (Info31 ? 98 : 80);
                ui.Label("label-count", new Rect(rx + 12, fy, 74, 38), "人数", 17);
                ui.Field("input-count", new Rect(rx + 86, fy, rw * .19f, 38), count, v => { count = v; inputError = null; nextUiRefresh = 0; });
                ui.Button("legion-update-count", new Rect(rx + 94 + rw * .19f, fy, 68, 38), "更新", () => { if (CommitFields()) RefreshAfterUiChange(); nextUiRefresh = 0; });
                ui.Button("field-template", new Rect(rx + rw * .56f, fy, rw * .4f, 38), "更换兵种", () => { if (CommitFields()) { pickerRole = 0; pickerOpen = true; nextUiRefresh = 0; } });
                ui.Button("field-unit-stats", new Rect(rx + 12, fy + 50, (rw - 36) / 2, 36), "详细属性  ›", OpenStats);
                ui.Button("field-army", new Rect(rx + 24 + (rw - 36) / 2, fy + 50, (rw - 36) / 2, 36), "调整归属军团", () => { if (CommitFields()) { armyMenu = true; nextUiRefresh = 0; } });
            }
            ui.Panel("legion-footer", new Rect(12, h - 86, w - 24, 74), WarSandboxUGUI.Bar);
            DraftStatus(ui, new Rect(24, h - 78, w - 372, 56));
            ui.Button("legion-done", new Rect(w - 320, h - 72, 286, 46), "完成并返回布阵  →", ReturnFromLegion, true);
            DrawLegionOverlays(ui, team);
        }
        private void AssignFormationArmy(int team)
        {
            if (team < 0 || !CommitFields() || deployment.Draft.Count == 0) return;
            var entry = deployment.Draft[selected]; entry.teamId = team; deployment.Draft.Set(selected, entry);
            armyMenu = false; RefreshAfterUiChange(); nextUiRefresh = 0;
        }
        private void DrawToyPicker(WarSandboxUGUI ui)
        {
            float w = ui.Width, h = ui.Height;
            ui.Panel("picker-header", new Rect(16, 50, w - 32, 66), WarSandboxUGUI.Bar);
            ui.Label("picker-title", new Rect(32, 60, w - 270, 46), Info31 ? WarSandboxBattleController.DefaultArmyName(deployment.Draft[selected].teamId) + " · 编成 " + (selected + 1).ToString("00") + "：更换兵种" : "为当前编成选择兵种", 28, WarSandboxUGUI.Ink, true);
            ui.Button("picker-back", new Rect(w - 196, 64, 160, 38), "取消更换", () => { pickerOpen = false; nextUiRefresh = 0; });
            ui.Label("picker-scope", new Rect(24, 128, w - 48, 44), deployment.rosterPolicy != null ? deployment.rosterPolicy.explanation : "只列出当前战场允许使用的兵种；更换后请检查人数与阵型。", 15, WarSandboxUGUI.Muted);
            int meleeCount = 0, rangedCount = 0;
            foreach (var choice in deployment.ChoiceTemplates) { if (WarSandboxUnitStats.IsRanged(choice)) rangedCount++; else meleeCount++; }
            string[] filters = { "全部  " + (meleeCount + rangedCount), "近战  " + meleeCount, "远程  " + rangedCount };
            for (int f = 0; f < 3; f++)
            {
                int filter = f;
                ui.Button("picker-filter-" + f, new Rect(24 + f * 146, 178, 134, 36), filters[f], () => { pickerRole = filter; nextUiRefresh = 0; }, false, true, pickerRole == f);
            }
            ui.Label("picker-action-note", new Rect(476, 178, w - 500, 36), Info31 ? "点击即更换 · 数值按草稿计算，应用后生效；更换后核对人数" : "点击卡片更换 · 属性为本局有效值", 14, WarSandboxUGUI.Muted);
            int visibleCount = pickerRole == 1 ? meleeCount : pickerRole == 2 ? rangedCount : meleeCount + rangedCount;
            float cardW = (w - 88) / 4, rowH = Info31 ? 346 : 274;
            ui.Scroll("picker-grid", new Rect(24, 230, w - 48, h - 254), Mathf.Max(80, Mathf.Ceil(visibleCount / 4f) * rowH));
            if (visibleCount == 0) ui.Label("picker-empty", new Rect(16, 12, w - 100, 52), "当前战场没有此类兵种，试试其他分类。", 18, WarSandboxUGUI.Muted);
            int slot = 0;
            for (int i = 0; i < deployment.ChoiceTemplates.Count; i++)
            {
                var template = deployment.ChoiceTemplates[i]; bool ranged = WarSandboxUnitStats.IsRanged(template);
                if ((pickerRole == 1 && ranged) || (pickerRole == 2 && !ranged)) continue;
                float x = slot % 4 * (cardW + 12), y = slot / 4 * rowH; slot++; var entry = PortraitEntry(template);
                ui.Button("template-" + i, new Rect(x, y, cardW, rowH - 14), "", () =>
                {
                    if (!CommitFields()) return;
                    deployment.SelectTemplate(selected, template); pickerOpen = false; RefreshAfterUiChange(); nextUiRefresh = 0;
                }, false, true, template == deployment.Draft[selected].template);
                if (entry != null && entry.unitPreview != null) ui.PictureFit("picker-image-" + i, new Rect(x + 16, y + 10, cardW - 32, 148), entry.unitPreview);
                else ui.Label("picker-missing-" + i, new Rect(x + 16, y + 30, cardW - 32, 80), "真实预览待配置", 14, WarSandboxUGUI.Muted);
                ui.LabelAligned("picker-name-" + i, new Rect(x + 8, y + 164, cardW - 16, 36), template.unitTypeName, 18, WarSandboxUGUI.Ink, true, TextAnchor.MiddleCenter);
                ui.LabelAligned("picker-role-" + i, new Rect(x + 8, y + 199, cardW - 16, 26), (template == deployment.Draft[selected].template ? "当前兵种 · " : "") + (ranged ? "远程" : "近战"), 13, WarSandboxUGUI.Muted, false, TextAnchor.MiddleCenter);
                var hp = deployment.DraftStats.Resolve(template, WarSandboxUnitStats.Get(WarSandboxUnitStat.MaxHp));
                var damage = deployment.DraftStats.Resolve(template, WarSandboxUnitStats.Get(WarSandboxUnitStat.AttackDamage));
                ui.LabelAligned("picker-stats-" + i, new Rect(x + 8, y + 229, cardW - 16, 24), "生命 " + hp.effective.ToString("0.#") + "  ·  攻击 " + damage.effective.ToString("0.#"), 13, WarSandboxUGUI.Muted, false, TextAnchor.MiddleCenter);
                if (Info31)
                {
                    ui.LabelAligned("picker-extra31-" + i, new Rect(x + 8, y + 255, cardW - 16, 24), "间隔 " + RosterInfo31.Effective(deployment, template, WarSandboxUnitStat.AttackInterval) + " · 攻击距离 " + RosterInfo31.Effective(deployment, template, WarSandboxUnitStat.AttackRange), 12, WarSandboxUGUI.Ink, false, TextAnchor.MiddleCenter);
                    ui.LabelAligned("picker-source31-" + i, new Rect(x + 8, y + 281, cardW - 16, 44), RosterInfo31.Sources(deployment, template), 12, WarSandboxUGUI.Muted, false, TextAnchor.MiddleCenter);
                }
            }
            ui.EndScroll();
        }
    }
}



