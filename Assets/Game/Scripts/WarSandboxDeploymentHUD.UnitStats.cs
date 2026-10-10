using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace MassEngine.Game
{
    /// <summary>Shared dark "tactical" palette for the unit stat screens (deployment dossier and front-end library).</summary>
    public static class WarSandboxStatTheme
    {
        public static readonly Color Shade = WarSandboxUGUI.Background;
        public static readonly Color Card = WarSandboxUGUI.Surface;
        public static readonly Color Row = WarSandboxUGUI.Raised;
        public static readonly Color Line = WarSandboxUGUI.Line;
        // Text and accent colours come from the shared B theme so every runtime screen uses one palette.
        public static readonly Color Text = WarSandboxUGUI.Ink;
        public static readonly Color Bright = WarSandboxUGUI.Ink;
        public static readonly Color Muted = WarSandboxUGUI.Muted;
        public static readonly Color Cyan = WarSandboxUGUI.Accent;
        public static readonly Color Amber = WarSandboxUGUI.Amber;
        public static readonly Color Red = WarSandboxUGUI.Danger;
        public static readonly Color Purple = WarSandboxUGUI.Purple;
        public static readonly Color Official = WarSandboxUGUI.Muted;
        public static readonly Color Button = WarSandboxUGUI.Soft;
        public static readonly Color ButtonInk = WarSandboxUGUI.Ink;
        public static readonly Color Field = WarSandboxUGUI.FieldFill;
        public static readonly Color Deep = WarSandboxUGUI.Deep;
        public static Color Alpha(Color c, float a) => new Color(c.r, c.g, c.b, a);
        public static Color SourceColor(WarSandboxStatSource source) =>
            source == WarSandboxStatSource.Local ? Cyan : source == WarSandboxStatSource.Global ? Purple : Official;
        public static string SourceName(WarSandboxStatSource source) =>
            source == WarSandboxStatSource.Local ? "本局" : source == WarSandboxStatSource.Global ? "全局" : "官方";

        public static void Corners(WarSandboxUGUI ui, string id, Rect r)
        {
            // Rounded shared panel primitives supply the frame.
        }

        public static void Chip(WarSandboxUGUI ui, string id, Rect r, string text, Color color)
        {
            ui.Panel(id, r, Alpha(color, 0.14f), false);
            ui.Panel(id + "-edge", new Rect(r.x, r.y, 2, r.height), color, false);
            ui.LabelAligned(id + "-text", r, text, 12, color, true, TextAnchor.MiddleCenter);
        }

        /// <summary>Slider window: a quarter to four times the official value (log scale), widened to include the current value.</summary>
        public static void SliderWindow(WarSandboxStatDefinition d, float official, float current, out float lo, out float hi, out bool log)
        {
            if (official > 0) { lo = Mathf.Max(d.Min, official * 0.25f); hi = Mathf.Min(d.Max, official * 4f); }
            else { lo = d.Min; hi = d.Max; }
            lo = Mathf.Min(lo, current); hi = Mathf.Max(hi, current);
            if (hi - lo < 0.0001f) hi = lo + 1;
            log = lo > 0;
        }
        public static float ToSlider(float v, float lo, float hi, bool log) =>
            Mathf.Clamp01(log ? Mathf.Log(Mathf.Max(v, lo) / lo) / Mathf.Log(hi / lo) : (v - lo) / (hi - lo));
        public static float FromSlider(float t, float lo, float hi, bool log) =>
            log ? lo * Mathf.Pow(hi / lo, Mathf.Clamp01(t)) : Mathf.Lerp(lo, hi, Mathf.Clamp01(t));

        public static bool TryParse(string text, out float value)
        {
            text = (text ?? "").Trim().Replace('，', '.').Replace("。", ".");
            return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
                !float.IsNaN(value) && !float.IsInfinity(value);
        }

        /// <summary>Authored, non-tunable parameters shown for reference.</summary>
        public static string ReadOnlySummary(UnitTypeConfig template)
        {
            var c = template != null ? template.combatConfig : null;
            if (c == null) return "无战斗配置";
            if (!WarSandboxUnitStats.IsRanged(template)) return "近战单位 · 碰撞与模型尺寸由兵种资源决定";
            return "出手时机 " + c.attackReleasePhase.ToString("0.##", CultureInfo.InvariantCulture) +
                "\n弹道高度 " + c.projectileOriginHeight.ToString("0.##", CultureInfo.InvariantCulture) + " 米" +
                "\n命中半径 " + c.projectileHitRadius.ToString("0.##", CultureInfo.InvariantCulture) + " 米" +
                "\n拖尾长度 " + c.projectileTrailLength.ToString("0.##", CultureInfo.InvariantCulture);
        }

        /// <summary>Warning when targets are acquired closer than the unit can hit.</summary>
        public static string RangeWarning(UnitTypeConfig template, System.Func<WarSandboxUnitStat, WarSandboxStatResolver.Value> value)
        {
            var acquire = value(WarSandboxUnitStat.TargetAcquireRadius); if (!acquire.applies) return null;
            float reach = value(WarSandboxUnitStat.AttackRange).effective;
            var projectile = value(WarSandboxUnitStat.ProjectileRange);
            if (projectile.applies) reach = Mathf.Max(reach, projectile.effective);
            return acquire.effective + 0.001f < reach ? "提示：索敌半径小于射程，单位可能不会在最远射程开火。" : null;
        }
    }

    public sealed partial class WarSandboxDeploymentHUD
    {
        private bool statsOpen, pushConfirm, pushClearLocal = true;
        private UnitTypeConfig statsTemplate;
        private string statsNotice; private bool statsNoticeError;

        public bool StatsOpen => statsOpen;

        private void OpenStats()
        {
            if (deployment.Draft == null || deployment.Draft.Count == 0 || !CommitFields()) return;
            statsTemplate = deployment.Draft[selected].template;
            statsOpen = true; pushConfirm = false; pushClearLocal = true; statsNotice = null;
            templateMenu = armyMenu = placing = false; clearFocusRequested = true; nextUiRefresh = 0;
        }

        private void CloseStats() { statsOpen = pushConfirm = false; statsNotice = null; clearFocusRequested = true; nextUiRefresh = 0; }

        private List<UnitTypeConfig> DraftTemplates()
        {
            var list = new List<UnitTypeConfig>(); var draft = deployment.Draft;
            if (draft == null) return list;
            for (int i = 0; i < draft.Count; i++) if (draft[i].template != null && !list.Contains(draft[i].template)) list.Add(draft[i].template);
            return list;
        }

        private void Notice(string text, bool error) { statsNotice = text; statsNoticeError = error; nextUiRefresh = 0; }

        private void DrawUGUIStats(WarSandboxUGUI ui)
        {
            // Full height (the battle navigation is a top-right strip now); the card starts below that strip.
            float w = ui.Width, h = ui.Height;
            var templatesInDraft = DraftTemplates();
            if (templatesInDraft.Count == 0) { CloseStats(); return; }
            if (!templatesInDraft.Contains(statsTemplate)) statsTemplate = templatesInDraft[0];
            var template = statsTemplate;
            var resolver = deployment.DraftStats;
            string templateId = resolver.TemplateId(template);
            bool ranged = WarSandboxUnitStats.IsRanged(template);

            ui.Panel("st-shade", new Rect(0, 0, w, h), WarSandboxStatTheme.Shade);
            float W = Mathf.Min(1080, w - 32), y0 = WarSandboxFrontEnd.NavigationLayout(w).yMax + 8, H = h - y0 - 10, x0 = (w - W) / 2;
            var card = new Rect(x0, y0, W, H);
            ui.Panel("st-card", card, WarSandboxStatTheme.Card);
            ui.Panel("st-card-top", new Rect(x0, y0, W, 1), WarSandboxStatTheme.Line, false);
            WarSandboxStatTheme.Corners(ui, "st-card", card);

            // Header
            ui.LabelAligned("st-brand", new Rect(x0 + 20, y0 + 10, 360, 20), "UNIT DOSSIER  /  兵种档案", 11, WarSandboxStatTheme.Cyan, true, TextAnchor.MiddleLeft);
            ui.LabelAligned("st-name", new Rect(x0 + 20, y0 + 30, W * 0.5f, 38), template.unitTypeName, 26, WarSandboxStatTheme.Bright, true, TextAnchor.MiddleLeft);
            ui.LabelAligned("st-id", new Rect(x0 + 20, y0 + 68, W * 0.6f, 20),
                (templateId ?? "未注册模板编号") + "   ·   " + (ranged ? "远程单位" : "近战单位") + "   ·   本方案内同一兵种共用一份数值",
                12, WarSandboxStatTheme.Muted, false, TextAnchor.MiddleLeft);
            float lx = x0 + W - 20 - 3 * 58;
            WarSandboxStatTheme.Chip(ui, "st-legend-loc", new Rect(lx, y0 + 18, 52, 22), "本局", WarSandboxStatTheme.Cyan);
            WarSandboxStatTheme.Chip(ui, "st-legend-glb", new Rect(lx + 58, y0 + 18, 52, 22), "全局", WarSandboxStatTheme.Purple);
            WarSandboxStatTheme.Chip(ui, "st-legend-off", new Rect(lx + 116, y0 + 18, 52, 22), "官方", WarSandboxStatTheme.Official);
            ui.LabelAligned("st-legend-note", new Rect(lx - 120, y0 + 46, 120 + 3 * 58, 20), "优先级  本局 › 全局 › 官方", 11, WarSandboxStatTheme.Muted, false, TextAnchor.MiddleRight);
            if (resolver.HasCustom(template))
                WarSandboxStatTheme.Chip(ui, "st-custom", new Rect(lx - 92, y0 + 18, 80, 22), "自定义", WarSandboxStatTheme.Amber);
            ui.Panel("st-head-line", new Rect(x0 + 16, y0 + 96, W - 32, 1), WarSandboxStatTheme.Line, false);

            // Left: templates used by this plan + read-only parameters
            float leftW = Mathf.Min(230, W * 0.26f), ly = y0 + 108, lx0 = x0 + 16;
            ui.LabelAligned("st-list-title", new Rect(lx0, ly, leftW, 20), "本方案兵种  " + templatesInDraft.Count, 11, WarSandboxStatTheme.Cyan, true, TextAnchor.MiddleLeft);
            ly += 24;
            float listH = Mathf.Min(templatesInDraft.Count * 44, H * 0.42f);
            ui.TintScroll("st-list", new Rect(lx0, ly, leftW, listH), templatesInDraft.Count * 44, WarSandboxStatTheme.Card);
            for (int i = 0; i < templatesInDraft.Count; i++)
            {
                var t = templatesInDraft[i]; bool on = t == template;
                ui.TintButton("st-item-" + i, new Rect(0, i * 44, leftW, 40), t.unitTypeName + (resolver.HasCustom(t) ? "   ·  自定义" : ""),
                    () => { statsTemplate = t; pushConfirm = false; statsNotice = null; clearFocusRequested = true; nextUiRefresh = 0; },
                    on ? WarSandboxStatTheme.Alpha(WarSandboxStatTheme.Cyan, 0.2f) : WarSandboxStatTheme.Button,
                    on ? WarSandboxStatTheme.Bright : WarSandboxStatTheme.ButtonInk, true, on, 14);
                ui.Panel("st-item-edge-" + i, new Rect(0, i * 44, 3, 40), on ? WarSandboxStatTheme.Cyan : WarSandboxStatTheme.Alpha(WarSandboxStatTheme.Cyan, 0f), false);
            }
            ui.EndScroll();
            ly += listH + 14;
            ui.Panel("st-ro", new Rect(lx0, ly, leftW, 120), WarSandboxStatTheme.Alpha(Color.black, 0.22f), false);
            ui.LabelAligned("st-ro-title", new Rect(lx0 + 10, ly + 6, leftW - 20, 18), "只读参数 · 由兵种资源决定", 11, WarSandboxStatTheme.Amber, true, TextAnchor.MiddleLeft);
            ui.LabelAligned("st-ro-body", new Rect(lx0 + 10, ly + 26, leftW - 20, 90), WarSandboxStatTheme.ReadOnlySummary(template), 12, WarSandboxStatTheme.Muted, false, TextAnchor.UpperLeft);

            var radiusValue = resolver.Resolve(template, WarSandboxUnitStats.Get(WarSandboxUnitStat.AgentRadius));
            float previewY = ly + 126, previewH = y0 + H - 18 - previewY;
            if (radiusValue.applies && previewH >= 140)
                ui.ModelPreview("st-radius-model", new Rect(lx0, previewY, leftW, previewH), template, null, radiusValue.effective);

            // Right: field table
            float rx = lx0 + leftW + 22, rw = x0 + W - 16 - rx, ty = y0 + 108;
            float labelW = 118, fieldW = 86, chipW = 50, resetW = 34, gap = 10;
            float sliderW = Mathf.Max(60, rw - labelW - fieldW - chipW - resetW - 4 * gap);
            ui.LabelAligned("st-col-0", new Rect(rx, ty, labelW, 20), "字段", 11, WarSandboxStatTheme.Muted, false, TextAnchor.MiddleLeft);
            ui.LabelAligned("st-col-1", new Rect(rx + labelW + gap, ty, sliderW, 20), "调整  ·  竖线为官方值", 11, WarSandboxStatTheme.Muted, false, TextAnchor.MiddleLeft);
            ui.LabelAligned("st-col-2", new Rect(rx + labelW + sliderW + 2 * gap, ty, fieldW, 20), "数值", 11, WarSandboxStatTheme.Muted, false, TextAnchor.MiddleRight);
            ui.LabelAligned("st-col-3", new Rect(rx + labelW + sliderW + fieldW + 3 * gap, ty, chipW, 20), "来源", 11, WarSandboxStatTheme.Muted, false, TextAnchor.MiddleCenter);
            bool twoRows = W - 40 < 740;
            float footerTop = y0 + H - (twoRows ? 160 : 112);
            float tableTop = ty + 24, tableH = footerTop - tableTop - 8;
            var rows = new List<WarSandboxStatDefinition>();
            foreach (var d in WarSandboxUnitStats.Definitions) if (WarSandboxUnitStats.Applies(template, d)) { if (d.UsesFlocking) rows.Insert(0, d); else rows.Add(d); }
            string group = null; float content = 0;
            foreach (var d in rows) { if (d.Group != group) { content += 30; group = d.Group; } content += 46; }
            ui.TintScroll("st-table", new Rect(rx, tableTop, rw, tableH), content, WarSandboxStatTheme.Card);
            float y = 0; group = null; int row = 0;
            foreach (var d in rows)
            {
                if (d.Group != group)
                {
                    group = d.Group;
                    ui.LabelAligned("st-g-" + d.Key, new Rect(0, y + 6, rw * 0.6f, 20), group, 12, WarSandboxStatTheme.Cyan, true, TextAnchor.MiddleLeft);
                    ui.LabelAligned("st-gn-" + d.Key, new Rect(rw * 0.4f, y + 6, rw * 0.6f, 20),
                        d.Role == WarSandboxStatRole.MeleeOnly ? "仅近战单位" : d.Role == WarSandboxStatRole.RangedOnly ? "仅远程单位" : "", 11, WarSandboxStatTheme.Muted, false, TextAnchor.MiddleRight);
                    ui.Panel("st-gl-" + d.Key, new Rect(0, y + 28, rw, 1), WarSandboxStatTheme.Alpha(WarSandboxStatTheme.Cyan, 0.16f), false);
                    y += 30;
                }
                DrawStatRow(ui, template, resolver, d, row++, y, rw, labelW, sliderW, fieldW, chipW, resetW, gap);
                y += 46;
            }
            ui.EndScroll();

            // Footer
            ui.Panel("st-foot-line", new Rect(x0 + 16, footerTop, W - 32, 1), WarSandboxStatTheme.Line, false);
            var local = deployment.Draft.Stats.Get(template);
            string warning = WarSandboxStatTheme.RangeWarning(template, s => resolver.Resolve(template, WarSandboxUnitStats.Get(s)));
            string storeWarning = deployment.StatStore.Warning;
            string line = statsNotice ?? storeWarning ?? warning ??
                "修改保存在本局方案：应用布阵后生效，保存方案时一并写入。推送到全局后，所有战场的此兵种都会使用新数值。";
            Color lineColor = statsNotice != null ? (statsNoticeError ? WarSandboxStatTheme.Red : WarSandboxStatTheme.Cyan)
                : storeWarning != null ? WarSandboxStatTheme.Red : warning != null ? WarSandboxStatTheme.Amber : WarSandboxStatTheme.Muted;
            ui.LabelAligned("st-foot-note", new Rect(x0 + 20, footerTop + 8, W - 40, 40), line, 13, lineColor, false, TextAnchor.MiddleLeft);
            float bh = 40, by = y0 + H - 56, leftRow = twoRows ? by - 48 : by;
            ui.TintButton("st-reset", new Rect(x0 + 20, leftRow, 160, bh), "恢复本兵种", () =>
            {
                if (deployment.Draft.ClearTemplateStats(template)) Notice("已清除本局对 " + template.unitTypeName + " 的修改。", false);
            }, WarSandboxStatTheme.Button, WarSandboxStatTheme.ButtonInk, local.Count > 0);
            ui.TintButton("st-undo-global", new Rect(x0 + 188, leftRow, 170, bh), "撤销上次全局修改", () =>
            {
                if (deployment.TryUndoGlobalChange(out string error)) Notice("已撤销上次全局修改。", false); else Notice(error, true);
            }, WarSandboxStatTheme.Button, WarSandboxStatTheme.ButtonInk, deployment.StatStore.CanUndo);
            ui.TintButton("st-push", new Rect(x0 + W - 20 - 150 - 8 - 190, by, 190, bh), "推送到全局…  " + local.Count + " 项", () =>
            { pushConfirm = true; pushClearLocal = true; clearFocusRequested = true; nextUiRefresh = 0; },
                WarSandboxStatTheme.Alpha(WarSandboxStatTheme.Purple, 0.22f), WarSandboxStatTheme.Bright, local.Count > 0 && templateId != null, true);
            ui.TintButton("st-done", new Rect(x0 + W - 20 - 150, by, 150, bh), "完成", CloseStats, WarSandboxStatTheme.Cyan, WarSandboxStatTheme.Deep, true, true);

            if (pushConfirm) DrawUGUIPush(ui, template, templateId, resolver, local);
        }

        private void DrawStatRow(WarSandboxUGUI ui, UnitTypeConfig template, WarSandboxStatResolver resolver, WarSandboxStatDefinition d,
            int row, float y, float rw, float labelW, float sliderW, float fieldW, float chipW, float resetW, float gap)
        {
            var value = resolver.Resolve(template, d);
            string k = "st-r-" + d.Key;
            if (row % 2 == 0) ui.Panel(k + "-bg", new Rect(0, y, rw, 42), WarSandboxStatTheme.Alpha(WarSandboxStatTheme.Row, 0.7f), false);
            ui.LabelAligned(k + "-name", new Rect(6, y, labelW - 6, 42), d.Label + (string.IsNullOrEmpty(d.Unit) ? "" : "  " + d.Unit), 14,
                value.Customized ? WarSandboxStatTheme.Bright : WarSandboxStatTheme.Text, value.Customized, TextAnchor.MiddleLeft);
            WarSandboxStatTheme.SliderWindow(d, value.official, value.effective, out float lo, out float hi, out bool log);
            float sx = labelW + gap;
            var sliderRect = new Rect(sx, y + 12, sliderW, 12);
            ui.TintSlider(k + "-slider", sliderRect, WarSandboxStatTheme.ToSlider(value.effective, lo, hi, log), t =>
            {
                float next = d.Step(WarSandboxStatTheme.FromSlider(t, lo, hi, log));
                if (deployment.SetStat(template, d.Stat, next)) { statsNotice = null; nextUiRefresh = 0; }
            }, WarSandboxStatTheme.Alpha(WarSandboxStatTheme.Cyan, 0.18f), WarSandboxStatTheme.SourceColor(value.source));
            float tick = sx + 10 + (sliderW - 20) * WarSandboxStatTheme.ToSlider(value.official, lo, hi, log);
            ui.Panel(k + "-tick", new Rect(tick - 1, y + 7, 2, 22), WarSandboxStatTheme.Alpha(WarSandboxStatTheme.Bright, 0.75f), false);
            ui.LabelAligned(k + "-official", new Rect(sx, y + 27, sliderW, 15), value.Customized ? "官方 " + d.Format(value.official) : "", 11,
                WarSandboxStatTheme.Official, false, value.official <= lo + (hi - lo) * 0.5f ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight);
            float fx = labelW + sliderW + 2 * gap;
            ui.TintField(k + "-field", new Rect(fx, y + 5, fieldW, 32), d.Format(value.effective), text =>
            {
                if (!WarSandboxStatTheme.TryParse(text, out float typed)) { Notice(d.Label + "：请输入有限数字。", true); return; }
                if (d.UsesFlocking && !d.Accepts(typed)) { Notice("半径必须为 0.05–4.8 米；应用时还需通过网格、间距和地形校验。", true); return; }
                float clamped = d.Clamp(typed);
                // Focus leaving an unchanged field must not create a local override.
                if (WarSandboxUnitStats.Same(clamped, value.effective) && clamped == typed) return;
                deployment.SetStat(template, d.Stat, clamped);
                Notice(clamped != typed ? d.Label + " 已限制在 " + d.Format(d.Min) + " – " + d.Format(d.Max) + "。" : null, clamped != typed);
            }, WarSandboxStatTheme.Field, value.Customized ? WarSandboxStatTheme.SourceColor(value.source) : WarSandboxStatTheme.Text);
            WarSandboxStatTheme.Chip(ui, k + "-src", new Rect(fx + fieldW + gap, y + 10, chipW, 22), WarSandboxStatTheme.SourceName(value.source),
                WarSandboxStatTheme.SourceColor(value.source));
            ui.TintButton(k + "-clear", new Rect(fx + fieldW + chipW + 2 * gap, y + 6, resetW, 30), "×", () =>
            { if (deployment.Draft.ClearStat(template, d.Stat)) Notice(d.Label + " 已恢复为" + (value.hasGlobal ? "全局" : "官方") + "数值。", false); },
                WarSandboxStatTheme.Button, WarSandboxStatTheme.ButtonInk, value.hasLocal, false, 16);
        }

        private void DrawUGUIPush(WarSandboxUGUI ui, UnitTypeConfig template, string templateId, WarSandboxStatResolver resolver, WarSandboxStatSet local)
        {
            float w = ui.Width, h = ui.Height - 62;
            ui.Panel("push-shade", new Rect(0, 0, w, h), WarSandboxStatTheme.Alpha(Color.black, 0.6f));
            int n = local.Count;
            float W = Mathf.Min(600, w - 40), H = Mathf.Min(h - 30, 330 + n * 32), x = (w - W) / 2, y = Mathf.Max(14, (h - H) / 2);
            var card = new Rect(x, y, W, H);
            ui.Panel("push-card", card, WarSandboxStatTheme.Card);
            WarSandboxStatTheme.Corners(ui, "push-card", card);
            ui.LabelAligned("push-brand", new Rect(x + 22, y + 14, W - 44, 18), "PUSH TO GLOBAL  /  推送到全局", 11, WarSandboxStatTheme.Purple, true, TextAnchor.MiddleLeft);
            ui.LabelAligned("push-title", new Rect(x + 22, y + 34, W - 44, 34), template.unitTypeName + "  ·  " + n + " 项修改", 22, WarSandboxStatTheme.Bright, true, TextAnchor.MiddleLeft);
            ui.LabelAligned("push-note", new Rect(x + 22, y + 70, W - 44, 40), "按模板编号 " + templateId + " 生效：所有战场与方案中使用此兵种、且未单独覆盖的字段都会改变。",
                13, WarSandboxStatTheme.Muted, false, TextAnchor.UpperLeft);
            float ry = y + 116;
            ui.LabelAligned("push-h0", new Rect(x + 22, ry, 150, 20), "字段", 11, WarSandboxStatTheme.Muted, false, TextAnchor.MiddleLeft);
            ui.LabelAligned("push-h1", new Rect(x + 180, ry, 170, 20), "当前全局 / 官方", 11, WarSandboxStatTheme.Muted, false, TextAnchor.MiddleRight);
            ui.LabelAligned("push-h2", new Rect(x + W - 22 - 150, ry, 150, 20), "推送后", 11, WarSandboxStatTheme.Muted, false, TextAnchor.MiddleRight);
            ry += 24;
            int i = 0;
            foreach (var pair in local.Values)
            {
                var d = WarSandboxUnitStats.Get(pair.Key); var value = resolver.Resolve(template, d);
                string before = value.hasGlobal ? d.Format(value.global) + "  全局" : d.Format(value.official) + "  官方";
                if (i % 2 == 0) ui.Panel("push-row-bg-" + i, new Rect(x + 16, ry, W - 32, 30), WarSandboxStatTheme.Alpha(WarSandboxStatTheme.Row, 0.8f), false);
                ui.LabelAligned("push-row-n-" + i, new Rect(x + 22, ry, 150, 30), d.Label, 14, WarSandboxStatTheme.Text, false, TextAnchor.MiddleLeft);
                ui.LabelAligned("push-row-b-" + i, new Rect(x + 180, ry, 170, 30), before, 14,
                    value.hasGlobal ? WarSandboxStatTheme.Purple : WarSandboxStatTheme.Official, false, TextAnchor.MiddleRight);
                ui.LabelAligned("push-row-arrow-" + i, new Rect(x + 360, ry, 40, 30), "→", 14, WarSandboxStatTheme.Muted, false, TextAnchor.MiddleCenter);
                ui.LabelAligned("push-row-a-" + i, new Rect(x + W - 22 - 150, ry, 150, 30), d.Format(pair.Value), 15, WarSandboxStatTheme.Cyan, true, TextAnchor.MiddleRight);
                ry += 32; i++;
            }
            ry += 8;
            ui.TintButton("push-clear-local", new Rect(x + 22, ry, W - 44, 34), (pushClearLocal ? "■  " : "□  ") + "推送后清除本局中这些字段（推荐：之后跟随全局数值）",
                () => { pushClearLocal = !pushClearLocal; nextUiRefresh = 0; },
                pushClearLocal ? WarSandboxStatTheme.Alpha(WarSandboxStatTheme.Cyan, 0.14f) : WarSandboxStatTheme.Button, WarSandboxStatTheme.Text, true, false, 14);
            ry += 42;
            ui.LabelAligned("push-undo-note", new Rect(x + 22, ry, W - 44, 36), "下次应用布阵或进入战场时生效。可在兵种档案或兵种库里“撤销上次全局修改”。",
                12, WarSandboxStatTheme.Muted, false, TextAnchor.UpperLeft);
            float by = y + H - 56, bw = (W - 44 - 10) / 2;
            ui.TintButton("push-cancel", new Rect(x + 22, by, bw, 40), "取消", () => { pushConfirm = false; nextUiRefresh = 0; },
                WarSandboxStatTheme.Button, WarSandboxStatTheme.ButtonInk);
            ui.TintButton("push-confirm", new Rect(x + 32 + bw, by, bw, 40), "确认推送", () =>
            {
                pushConfirm = false;
                if (deployment.TryPushStats(template, pushClearLocal, out string error))
                    Notice("已推送 " + n + " 项到全局（" + templateId + "）。下次应用布阵或进入战场时生效。", false);
                else Notice(error, true);
            }, WarSandboxStatTheme.Purple, WarSandboxStatTheme.Deep, true, true);
        }
    }
}

