using UnityEngine;

namespace MassEngine.Game
{
    /// <summary>
    /// One-page 编成 + 布阵 (2026-10-10 feedback): army → formation tree | map | inspector with two tabs
    /// (兵种与人数 / 位置与阵型). spatialSelection = a formation is selected (formation inspector); otherwise the
    /// inspector shows the selected army. The full legion page stays reachable from each army header (army-details-*).
    /// </summary>
    public sealed partial class WarSandboxDeploymentHUD
    {
        private int inspectorTab; // 0 = 兵种与人数, 1 = 位置与阵型

        private void DrawMergedOverview(WarSandboxUGUI ui, float top, float body)
        {
            float w = ui.Width;
            float left = Mathf.Clamp(w * .19f, 250f, 300f), right = Mathf.Clamp(w * .25f, 330f, 400f);
            DrawFormationTree(ui, new Rect(16, top, left, body));
            float mapX = 16 + left + 16;
            DrawUGUIMap(ui, new Rect(mapX, top, w - mapX - right - 32, body));
            DrawInspector(ui, new Rect(w - right - 16, top, right, body));
        }

        private void SelectArmy(int firstFormation)
        {
            if (!SelectFormation(firstFormation)) return;
            spatialSelection = false; placing = false; nextUiRefresh = 0;
        }

        private void DrawFormationTree(WarSandboxUGUI ui, Rect r)
        {
            var draft = deployment.Draft; var ids = ArmyIds();
            ui.Panel("army-overview", r);
            ui.Label("army-overview-title", new Rect(r.x + 12, r.y + 10, r.width - 24, 34), "军团与编成", 20, WarSandboxUGUI.Ink, true);
            float headH = Info31 ? 74 : 56, rowH = 50, addH = 30, inner = r.width - 24, bw = inner - 8;
            float content = 0;
            foreach (int team in ids)
            {
                content += headH + 6 + addH + 10;
                for (int i = 0; i < draft.Count; i++) if (draft[i].teamId == team) content += rowH + 4;
            }
            ui.Scroll("army-overview-list", new Rect(r.x + 12, r.y + 50, inner, r.height - 108), Mathf.Max(40, content));
            float y = 0; int current = draft.Count > 0 ? draft[selected].teamId : -1;
            foreach (int team in ids)
            {
                int first = -1, population = 0, formations = 0;
                for (int i = 0; i < draft.Count; i++) if (draft[i].teamId == team) { if (first < 0) first = i; population += draft[i].count; formations++; }
                int head = first;
                ui.Button("army-head-" + team, new Rect(0, y, bw, headH), "", () => SelectArmy(head), false, true, !spatialSelection && team == current);
                ui.Panel("army-swatch-" + team, new Rect(8, y + 10, 6, headH - 20), WarSandboxTeamPalette.Resolve(team), false);
                ui.Label("army-name-" + team, new Rect(22, y + 6, bw - 140, 26), WarSandboxBattleController.DefaultArmyName(team), 17, WarSandboxUGUI.Ink, true);
                ui.Label("army-summary-" + team, new Rect(22, y + 32, bw - 30, 20), population.ToString("N0") + " 人 · " + formations + " 个编成", 12, WarSandboxUGUI.Muted);
                if (Info31) ui.Label("army-composition31-" + team, new Rect(22, y + 50, bw - 30, 20), RosterInfo31.Composition(draft, team), 12, WarSandboxUGUI.Muted);
                ui.Button("army-position-" + team, new Rect(bw - 114, y + 6, 52, 26), "布阵", () => { if (SelectFormation(head)) { spatialSelection = true; inspectorTab = 1; } });
                ui.Button("army-details-" + team, new Rect(bw - 58, y + 6, 52, 26), "详情", () => OpenLegion(head));
                y += headH + 6;
                for (int i = 0; i < draft.Count; i++)
                {
                    var e = draft[i]; if (e.teamId != team) continue;
                    int index = i; bool active = spatialSelection && i == selected;
                    ui.Button("tree-formation-" + i, new Rect(14, y, bw - 14, rowH), "", () => { if (SelectFormation(index)) spatialSelection = true; }, false, true, active);
                    var portrait = PortraitEntry(e.template);
                    if (portrait != null && portrait.unitPreview != null) ui.PictureFit("tree-portrait-" + i, new Rect(20, y + 5, 36, 40), portrait.unitPreview);
                    ui.Label("tree-name-" + i, new Rect(62, y + 4, bw - 150, 24), e.Name, 14, WarSandboxUGUI.Ink, true);
                    ui.Label("tree-role-" + i, new Rect(62, y + 27, bw - 150, 18), "编成 " + (i + 1).ToString("00") + " · " + (WarSandboxUnitStats.IsRanged(e.template) ? "远程" : "近战"), 11, WarSandboxUGUI.Muted);
                    ui.LabelAligned("tree-count-" + i, new Rect(bw - 88, y + 8, 80, 34), e.count.ToString("N0"), 15, WarSandboxUGUI.Accent, true, TextAnchor.MiddleRight);
                    y += rowH + 4;
                }
                ui.Button("tree-add-" + team, new Rect(14, y, bw - 14, addH - 4), "＋ 添加编成", () => { if (SelectFormation(head) && AddFormation(false)) { spatialSelection = true; inspectorTab = 0; } });
                y += addH + 10;
            }
            ui.EndScroll();
            ui.Button("army-add", new Rect(r.x + 12, r.yMax - 50, inner, 38), "＋ 新建军团", () => { if (AddFormation(true)) { spatialSelection = true; inspectorTab = 0; } });
        }

        private void DrawInspector(WarSandboxUGUI ui, Rect r)
        {
            var draft = deployment.Draft;
            ui.Panel("inspector-card", r);
            if (draft.Count == 0)
            {
                ui.Label("inspector-empty", new Rect(r.x + 14, r.y + 14, r.width - 28, 60), "还没有编成。\n点左侧「＋ 新建军团」开始。", 15, WarSandboxUGUI.Muted);
                return;
            }
            var e = draft[selected]; int team = e.teamId;
            if (!spatialSelection) { DrawArmyInspector(ui, r, team); return; }
            string role = WarSandboxUnitStats.IsRanged(e.template) ? "远程" : "近战";
            string scope = (Check32 ? DeploymentCheck32.Scope(draft, selected) : WarSandboxBattleController.DefaultArmyName(team) + " · 编成 " + (selected + 1).ToString("00")) + " · " + role;
            ui.Label("inspector-scope", new Rect(r.x + 14, r.y + 8, r.width - 28, 20), scope, 12, WarSandboxUGUI.Muted);
            ui.Label("inspector-name", new Rect(r.x + 14, r.y + 28, r.width - 134, 32), e.Name, 20, WarSandboxUGUI.Ink, true);
            ui.LabelAligned("inspector-count", new Rect(r.xMax - 126, r.y + 28, 112, 32), e.count.ToString("N0") + " 人", 16, WarSandboxUGUI.Accent, true, TextAnchor.MiddleRight);
            float tw = (r.width - 34) / 2;
            ui.Button("inspector-tab-unit", new Rect(r.x + 12, r.y + 66, tw, 32), "兵种与人数", () => { inspectorTab = 0; nextUiRefresh = 0; }, false, true, inspectorTab == 0);
            ui.Button("inspector-tab-place", new Rect(r.x + 22 + tw, r.y + 66, tw, 32), "位置与阵型", () => { inspectorTab = 1; nextUiRefresh = 0; }, false, true, inspectorTab == 1);
            var content = new Rect(r.x + 12, r.y + 108, r.width - 24, r.height - 120);
            if (inspectorTab == 1) DrawPlacementFields(ui, content);
            else DrawUnitFields(ui, content, e);
        }

        private void DrawUnitFields(WarSandboxUGUI ui, Rect c, WarSandboxDeploymentEntry e)
        {
            var portrait = PortraitEntry(e.template);
            float statH = Info31 ? 74 : 60, fixedH = statH + 10 + 48 + 42 + 34;
            float ph = Mathf.Min(170, c.height - fixedH - 8), y = c.y;
            // Short screens drop the 3D preview first; numbers and actions always stay visible.
            if (ph >= 70) { ui.ModelPreview("inspector-unit-preview", new Rect(c.x, y, c.width, ph), e.template, portrait != null ? portrait.unitPreview : null); y += ph + 8; }
            float sw = (c.width - 16) / 3;
            var keys = new[] { WarSandboxUnitStat.MaxHp, WarSandboxUnitStat.AttackDamage, WarSandboxUnitStat.AttackInterval };
            for (int i = 0; i < keys.Length; i++)
            {
                var def = WarSandboxUnitStats.Get(keys[i]); var value = deployment.DraftStats.Resolve(e.template, def);
                float sx = c.x + i * (sw + 8);
                ui.Panel("inspector-stat-" + i, new Rect(sx, y, sw, statH), WarSandboxUGUI.Raised);
                ui.LabelAligned("inspector-stat-name-" + i, new Rect(sx + 2, y + 2, sw - 4, 20), def.Label, 12, WarSandboxUGUI.Muted, false, TextAnchor.MiddleCenter);
                ui.LabelAligned("inspector-stat-value-" + i, new Rect(sx + 2, y + 21, sw - 4, 28), def.Format(value.effective) + def.Unit, 18, WarSandboxUGUI.Ink, true, TextAnchor.MiddleCenter);
                if (Info31) ui.LabelAligned("inspector-stat-source31-" + i, new Rect(sx + 2, y + 50, sw - 4, 20), RosterInfo31.Origin(value, def), 11, value.source == WarSandboxStatSource.Official ? WarSandboxUGUI.Muted : WarSandboxUGUI.Accent, false, TextAnchor.MiddleCenter);
            }
            y += statH + 10;
            ui.Label("label-count", new Rect(c.x, y, 50, 36), "人数", 15);
            ui.Field("input-count", new Rect(c.x + 50, y, c.width - 128, 36), count, v => { count = v; inputError = null; nextUiRefresh = 0; });
            ui.Button("inspector-update-count", new Rect(c.xMax - 70, y, 70, 36), "更新", () => { if (CommitFields()) RefreshAfterUiChange(); nextUiRefresh = 0; });
            y += 48;
            float hw = (c.width - 8) / 2;
            ui.Button("field-template", new Rect(c.x, y, hw, 34), "更换兵种", () => { if (CommitFields()) { pickerRole = 0; pickerOpen = true; nextUiRefresh = 0; } });
            ui.Button("field-unit-stats", new Rect(c.x + hw + 8, y, hw, 34), "详细属性  ›", OpenStats);
            y += 42;
            ui.Button("field-army", new Rect(c.x, y, hw, 34), "调整归属军团", () => { if (CommitFields()) { armyMenu = true; nextUiRefresh = 0; } });
            ui.Button("inspector-remove-formation", new Rect(c.x + hw + 8, y, hw, 34), "移除编成", () => { deployment.Draft.Remove(selected); ClampSelection(); RefreshAfterUiChange(); });
        }

        private void DrawPlacementFields(WarSandboxUGUI ui, Rect c)
        {
            ui.Scroll("placement-fields", c, TightEnabled ? 610 : 510);
            float fy = 0, fw = c.width - 16;
            DrawTightPresets(ui, ref fy, fw);
            DrawFacingControls(ui, ref fy, fw);
            UGUIField(ui, "x", "中心 X", x, v => x = v, ref fy, fw);
            UGUIField(ui, "z", "中心 Z", z, v => z = v, ref fy, fw);
            ui.Button("field-manual", new Rect(0, fy, fw, 36), manual ? "阵型：手动尺寸" : "阵型：密度 / 宽深比", () => manual = !manual); fy += 46;
            if (manual) { UGUIField(ui, "depth", "纵深 X", depth, v => depth = v, ref fy, fw); UGUIField(ui, "width", "宽度 Z", width, v => width = v, ref fy, fw); }
            else { UGUIField(ui, "density", "密度", density, v => density = v, ref fy, fw); UGUIField(ui, "aspect", "宽深比", aspect, v => aspect = v, ref fy, fw); }
            ui.Button("field-update", new Rect(0, fy + 4, fw, 38), "更新位置与阵型", () => { if (CommitFields()) RefreshAfterUiChange(); });
            ui.Label("resize-values", new Rect(0, fy + 50, fw, 105), "地图上拖边/角调整尺寸 · Shift锁比例\n松手校验，人数和体型不变\n编辑尺寸后切为自动占地", 12, WarSandboxUGUI.Muted);
            ui.EndScroll();
        }

        private void DrawArmyInspector(WarSandboxUGUI ui, Rect r, int team)
        {
            var draft = deployment.Draft; int population = 0, formations = 0;
            for (int i = 0; i < draft.Count; i++) if (draft[i].teamId == team) { population += draft[i].count; formations++; }
            ui.Label("inspector-scope", new Rect(r.x + 14, r.y + 8, r.width - 28, 20), "军团", 12, WarSandboxUGUI.Muted);
            ui.Label("inspector-name", new Rect(r.x + 14, r.y + 28, r.width - 134, 32), WarSandboxBattleController.DefaultArmyName(team), 20, WarSandboxUGUI.Ink, true);
            ui.LabelAligned("inspector-count", new Rect(r.xMax - 126, r.y + 28, 112, 32), population.ToString("N0") + " 人", 16, WarSandboxUGUI.Accent, true, TextAnchor.MiddleRight);
            string text = formations + " 个编成" + (Info31 ? "\n" + RosterInfo31.Composition(draft, team) : "") +
                          "\n\n点左侧编成，或在地图上点色块，\n即可编辑兵种、人数、位置与朝向。";
            ui.Panel("inspector-army-summary", new Rect(r.x + 12, r.y + 72, r.width - 24, 132), WarSandboxUGUI.Raised);
            ui.Label("inspector-army-text", new Rect(r.x + 24, r.y + 80, r.width - 48, 116), text, 14, WarSandboxUGUI.Ink);
            float hw = (r.width - 32) / 2, y = r.y + 218;
            ui.Button("inspector-army-add", new Rect(r.x + 12, y, hw, 36), "＋ 添加编成", () => { if (AddFormation(false)) { spatialSelection = true; inspectorTab = 0; } });
            ui.Button("inspector-army-details", new Rect(r.x + 20 + hw, y, hw, 36), "军团详情  ›", () => OpenLegion(selected));
            ui.Button("inspector-army-remove", new Rect(r.x + 12, y + 46, r.width - 24, 34), "移除军团", () => removeArmyConfirm = true);
        }

        /// <summary>Army reassignment and remove-army dialogs, shared by the one-page overview and the full legion page.</summary>
        private void DrawLegionOverlays(WarSandboxUGUI ui, int team)
        {
            float w = ui.Width, h = ui.Height; var draft = deployment.Draft;
            if (armyMenu)
            {
                ui.Panel("army-picker-shade", new Rect(0, 0, w, h), WarSandboxUGUI.Shade);
                float ax = (w - 520) / 2, ay = (h - 400) / 2;
                ui.Panel("army-picker-card", new Rect(ax, ay, 520, 400));
                ui.Label("army-picker-heading", new Rect(ax + 20, ay + 16, 480, 42), "将当前编成编入…", 25, WarSandboxUGUI.Ink, true);
                ui.Label("army-picker-note", new Rect(ax + 20, ay + 62, 480, 42), "保留兵种、人数与位置，仅调整本局军团归属。", 14, WarSandboxUGUI.Muted);
                var ids = ArmyIds();
                for (int n = 0; n < ids.Count; n++)
                {
                    int target = ids[n];
                    ui.Button("army-assign-" + target, new Rect(ax + 24 + n % 2 * 242, ay + 116 + n / 2 * 48, 232, 38), WarSandboxBattleController.DefaultArmyName(target), () => AssignFormationArmy(target), false, true, target == team);
                }
                int next = draft.NextArmyId();
                ui.Button("army-assign-new", new Rect(ax + 24, ay + 338, 232, 40), "＋ 独立为新军团", () => AssignFormationArmy(next), false, next >= 0);
                ui.Button("army-assign-cancel", new Rect(ax + 266, ay + 338, 232, 40), "取消", () => { armyMenu = false; nextUiRefresh = 0; });
            }
            if (removeArmyConfirm)
            {
                ui.Panel("legion-remove-shade", new Rect(0, 0, w, h), WarSandboxUGUI.Shade);
                float cx = (w - 460) / 2, cy = (h - 220) / 2;
                ui.Panel("legion-remove-card", new Rect(cx, cy, 460, 220));
                ui.Label("legion-remove-note", new Rect(cx + 20, cy + 20, 420, 80), "移除这个军团的全部编成？\n仅修改本局草稿，返回布阵后可撤销。", 20);
                ui.Button("legion-remove-confirm", new Rect(cx + 20, cy + 132, 200, 44), "确认移除", () => { draft.RemoveArmy(team); ClampSelection(); RefreshAfterUiChange(); removeArmyConfirm = false; legionOpen = false; });
                ui.Button("legion-remove-cancel", new Rect(cx + 238, cy + 132, 200, 44), "保留军团", () => removeArmyConfirm = false, true);
            }
        }
    }
}
