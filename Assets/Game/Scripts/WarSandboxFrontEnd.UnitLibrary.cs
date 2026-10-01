using System;
using System.Collections.Generic;
using UnityEngine;

namespace MassEngine.Game
{
    /// <summary>
    /// 兵种库: edits the player-wide (global) unit stat layer from the battlefield catalog. Official values
    /// are read from the template assets and never written. Edits stay pending until "保存到全局".
    /// </summary>
    public sealed partial class WarSandboxFrontEnd
    {
        private enum LibraryModal { None, Unsaved, ResetAll }
        private bool libraryOpen;
        private WarSandboxUnitStatStore libraryStore;
        private int libraryIndex;
        private WarSandboxStatSet libraryPending;
        private string libraryPendingId;
        private string libraryNotice; private bool libraryNoticeError;
        private LibraryModal libraryModal;
        private Action libraryAfterDecision;

        /// <summary>Tests and tools may point the library at another file.</summary>
        public WarSandboxUnitStatStore LibraryStore
        {
            get => libraryStore ?? (libraryStore = new WarSandboxUnitStatStore());
            set => libraryStore = value ?? throw new ArgumentNullException(nameof(value));
        }
        public bool LibraryOpen => libraryOpen;

        public void OpenLibrary()
        {
            LibraryStore.Invalidate();
            libraryOpen = true; libraryModal = LibraryModal.None; libraryNotice = null;
            libraryPending = null; libraryPendingId = null; nextRefresh = 0;
        }

        private void CloseLibrary() { libraryOpen = false; libraryModal = LibraryModal.None; libraryPending = null; nextRefresh = 0; }

        private List<WarSandboxUnitTemplateEntry> LibraryTemplates()
        {
            var list = new List<WarSandboxUnitTemplateEntry>();
            if (session.catalog == null || session.catalog.templates == null) return list;
            foreach (var t in session.catalog.templates)
                if (t != null && t.config != null && !string.IsNullOrWhiteSpace(t.templateId)) list.Add(t);
            return list;
        }

        private bool LibraryDirty => libraryPending != null && libraryPendingId != null &&
            !libraryPending.SameAs(LibraryStore.Current.Get(libraryPendingId));

        private void LibraryGuard(Action action)
        {
            if (LibraryDirty) { libraryModal = LibraryModal.Unsaved; libraryAfterDecision = action; nextRefresh = 0; }
            else action();
        }

        private void LibraryNotice(string text, bool error) { libraryNotice = text; libraryNoticeError = error; nextRefresh = 0; }

        private bool SaveLibraryPending(WarSandboxUnitTemplateEntry entry)
        {
            var next = LibraryStore.Current.Clone();
            next.Replace(entry.templateId, entry.revision, libraryPending);
            string summary = "兵种库：" + entry.config.unitTypeName + "（" + entry.templateId + "）" +
                (libraryPending.Count == 0 ? "恢复官方" : libraryPending.Count + " 项");
            if (!LibraryStore.TrySave(next, summary, out string error)) { LibraryNotice(error, true); return false; }
            LibraryNotice("已保存到全局：" + entry.config.unitTypeName + "。下次进入战场时生效。", false);
            return true;
        }

        private void LibraryEscape()
        {
            if (libraryModal != LibraryModal.None) { libraryModal = LibraryModal.None; libraryAfterDecision = null; nextRefresh = 0; return; }
            LibraryGuard(CloseLibrary);
        }

        private void DrawLibrary()
        {
            float w = ui.Width, h = ui.Height;
            ui.Panel("lib-bg", new Rect(0, 0, w, h), WarSandboxStatTheme.Card);
            var templates = LibraryTemplates();
            float W = Mathf.Min(1180, w - 32), x0 = (w - W) / 2;
            ui.LabelAligned("lib-brand", new Rect(x0 + 4, 14, 400, 20), "UNIT LIBRARY  /  兵种库", 11, WarSandboxStatTheme.Cyan, true, TextAnchor.MiddleLeft);
            ui.LabelAligned("lib-title", new Rect(x0 + 4, 34, W - 200, 40), "全局兵种数值", 28, WarSandboxStatTheme.Bright, true, TextAnchor.MiddleLeft);
            ui.LabelAligned("lib-note", new Rect(x0 + 4, 74, W - 160, 26), W < 900 ? "按模板编号生效 · 官方数值不会被修改" : "按模板编号生效 · 影响所有战场与方案中未单独覆盖的字段 · 官方数值来自兵种资源，不会被修改",
                13, WarSandboxStatTheme.Muted, false, TextAnchor.MiddleLeft);
            ui.TintButton("lib-back", new Rect(x0 + W - 150, 34, 150, 40), "返回目录", () => LibraryGuard(CloseLibrary),
                WarSandboxStatTheme.Cyan, WarSandboxStatTheme.Deep, true, true);
            ui.Panel("lib-head-line", new Rect(x0, 104, W, 1), WarSandboxStatTheme.Line, false);
            if (templates.Count == 0)
            {
                ui.LabelAligned("lib-empty", new Rect(x0, 130, W, 40), "当前目录没有登记兵种模板。", 18, WarSandboxStatTheme.Red, false, TextAnchor.MiddleLeft);
                return;
            }
            libraryIndex = Mathf.Clamp(libraryIndex, 0, templates.Count - 1);
            var entry = templates[libraryIndex];
            var saved = LibraryStore.Current;
            if (libraryPending == null || libraryPendingId != entry.templateId)
            { libraryPending = saved.Get(entry.templateId); libraryPendingId = entry.templateId; }

            // Left: all catalog templates
            float leftW = Mathf.Min(300, W * 0.28f), top = 116, bottom = h - 24;
            int adjusted = 0; foreach (var t in templates) if (saved.Get(t.templateId).Count > 0) adjusted++;
            ui.LabelAligned("lib-list-title", new Rect(x0, top, leftW, 20), "共 " + templates.Count + " 个兵种  ·  已调整 " + adjusted, 11, WarSandboxStatTheme.Cyan, true, TextAnchor.MiddleLeft);
            float listTop = top + 26, listH = bottom - listTop;
            ui.TintScroll("lib-list", new Rect(x0, listTop, leftW, listH), templates.Count * 52, WarSandboxStatTheme.Card);
            for (int i = 0; i < templates.Count; i++)
            {
                int index = i; var t = templates[i]; bool on = i == libraryIndex; bool hasGlobal = saved.Get(t.templateId).Count > 0;
                ui.TintButton("lib-item-" + i, new Rect(0, i * 52, leftW - 6, 48), t.config.unitTypeName + (hasGlobal ? "   · 全局" : "") + "\n" + t.templateId,
                    () => { if (index != libraryIndex) LibraryGuard(() => { libraryIndex = index; libraryPending = null; libraryNotice = null; nextRefresh = 0; }); },
                    on ? WarSandboxStatTheme.Alpha(WarSandboxStatTheme.Cyan, 0.2f) : WarSandboxStatTheme.Button,
                    hasGlobal ? WarSandboxStatTheme.Purple : on ? WarSandboxStatTheme.Bright : WarSandboxStatTheme.ButtonInk, true, on, 13);
                ui.Panel("lib-item-edge-" + i, new Rect(0, i * 52, 3, 48), on ? WarSandboxStatTheme.Cyan : WarSandboxStatTheme.Alpha(WarSandboxStatTheme.Cyan, 0f), false);
            }
            ui.EndScroll();

            // Right: dossier of the selected template (global layer)
            float rx = x0 + leftW + 20, rw = x0 + W - rx;
            var card = new Rect(rx, top, rw, bottom - top);
            ui.Panel("lib-card", card, WarSandboxStatTheme.Alpha(WarSandboxStatTheme.Row, 0.55f));
            WarSandboxStatTheme.Corners(ui, "lib-card", card);
            var template = entry.config; bool ranged = WarSandboxUnitStats.IsRanged(template);
            ui.LabelAligned("lib-name", new Rect(rx + 20, top + 12, rw * 0.55f, 36), template.unitTypeName, 24, WarSandboxStatTheme.Bright, true, TextAnchor.MiddleLeft);
            ui.LabelAligned("lib-id", new Rect(rx + 20, top + 48, rw - 40, 20), entry.templateId + "  ·  r" + entry.revision + "  ·  " + (ranged ? "远程单位" : "近战单位"),
                12, WarSandboxStatTheme.Muted, false, TextAnchor.MiddleLeft);
            float lx = rx + rw - 20 - 2 * 58;
            WarSandboxStatTheme.Chip(ui, "lib-legend-glb", new Rect(lx, top + 18, 52, 22), "全局", WarSandboxStatTheme.Purple);
            WarSandboxStatTheme.Chip(ui, "lib-legend-off", new Rect(lx + 58, top + 18, 52, 22), "官方", WarSandboxStatTheme.Official);
            if (LibraryDirty) WarSandboxStatTheme.Chip(ui, "lib-dirty", new Rect(lx - 92, top + 18, 84, 22), "未保存", WarSandboxStatTheme.Amber);
            ui.Panel("lib-card-line", new Rect(rx + 16, top + 76, rw - 32, 1), WarSandboxStatTheme.Line, false);

            float labelW = 118, fieldW = 86, chipW = 50, resetW = 34, gap = 10, innerW = rw - 40;
            float sliderW = Mathf.Max(60, innerW - labelW - fieldW - chipW - resetW - 4 * gap);
            bool twoRows = rw - 40 < 790;
            float footerTop = bottom - (twoRows ? 158 : 110), tableTop = top + 86;
            var rows = new List<WarSandboxStatDefinition>();
            foreach (var d in WarSandboxUnitStats.Definitions) if (WarSandboxUnitStats.Applies(template, d)) rows.Add(d);
            string group = null; float content = 0;
            foreach (var d in rows) { if (d.Group != group) { content += 30; group = d.Group; } content += 46; }
            ui.TintScroll("lib-table", new Rect(rx + 20, tableTop, innerW, footerTop - tableTop - 6), content, WarSandboxStatTheme.Alpha(WarSandboxStatTheme.Row, 0f));
            float y = 0; group = null; int row = 0;
            foreach (var d in rows)
            {
                if (d.Group != group)
                {
                    group = d.Group;
                    ui.LabelAligned("lib-g-" + d.Key, new Rect(0, y + 6, innerW * 0.6f, 20), group, 12, WarSandboxStatTheme.Cyan, true, TextAnchor.MiddleLeft);
                    ui.Panel("lib-gl-" + d.Key, new Rect(0, y + 28, innerW, 1), WarSandboxStatTheme.Alpha(WarSandboxStatTheme.Cyan, 0.16f), false);
                    y += 30;
                }
                DrawLibraryRow(d, template, saved, entry.templateId, row++, y, innerW, labelW, sliderW, fieldW, chipW, resetW, gap);
                y += 46;
            }
            ui.EndScroll();

            ui.Panel("lib-foot-line", new Rect(rx + 16, footerTop, rw - 32, 1), WarSandboxStatTheme.Line, false);
            string storeWarning = LibraryStore.Warning;
            string note = libraryNotice ?? storeWarning ?? (string.IsNullOrEmpty(saved.LastChange) ? "尚未修改全局数值：所有兵种按官方数值运行。" : "上次修改：" + saved.LastChange);
            Color noteColor = libraryNotice != null ? (libraryNoticeError ? WarSandboxStatTheme.Red : WarSandboxStatTheme.Cyan)
                : storeWarning != null ? WarSandboxStatTheme.Red : WarSandboxStatTheme.Muted;
            ui.LabelAligned("lib-foot-note", new Rect(rx + 20, footerTop + 8, rw - 40, 40), note, 13, noteColor, false, TextAnchor.MiddleLeft);
            float bh = 40, by = bottom - 54, leftRow = twoRows ? by - 48 : by;
            ui.TintButton("lib-reset-all", new Rect(rx + 20, leftRow, 150, bh), "全部恢复官方…", () => { libraryModal = LibraryModal.ResetAll; nextRefresh = 0; },
                WarSandboxStatTheme.Alpha(WarSandboxStatTheme.Red, 0.16f), WarSandboxStatTheme.Red, !saved.IsEmpty);
            ui.TintButton("lib-undo", new Rect(rx + 178, leftRow, 170, bh), "撤销上次全局修改", () =>
            {
                if (LibraryStore.TryUndo(out string error)) { libraryPending = null; LibraryNotice("已撤销上次全局修改。", false); }
                else LibraryNotice(error, true);
            }, WarSandboxStatTheme.Button, WarSandboxStatTheme.ButtonInk, LibraryStore.CanUndo);
            float right = rx + rw - 20;
            ui.TintButton("lib-save", new Rect(right - 160, by, 160, bh), "保存到全局", () => SaveLibraryPending(entry),
                WarSandboxStatTheme.Purple, WarSandboxStatTheme.Deep, LibraryDirty, true);
            ui.TintButton("lib-discard", new Rect(right - 160 - 8 - 110, by, 110, bh), "放弃修改", () => { libraryPending = null; LibraryNotice(null, false); },
                WarSandboxStatTheme.Button, WarSandboxStatTheme.ButtonInk, LibraryDirty);
            ui.TintButton("lib-restore", new Rect(right - 160 - 8 - 110 - 8 - 130, by, 130, bh), "恢复此兵种", () =>
            { libraryPending = new WarSandboxStatSet(); LibraryNotice("已清空此兵种的全局数值（未保存）。保存后按官方数值运行。", false); },
                WarSandboxStatTheme.Button, WarSandboxStatTheme.ButtonInk, libraryPending.Count > 0);

            if (libraryModal == LibraryModal.Unsaved) DrawLibraryUnsaved(entry);
            else if (libraryModal == LibraryModal.ResetAll) DrawLibraryResetAll(saved);
        }

        private void DrawLibraryRow(WarSandboxStatDefinition d, UnitTypeConfig template, WarSandboxGlobalStats saved, string id, int row, float y,
            float rw, float labelW, float sliderW, float fieldW, float chipW, float resetW, float gap)
        {
            WarSandboxUnitStats.TryGetOfficial(template, d, out float official);
            bool hasPending = libraryPending.TryGet(d.Stat, out float pending);
            bool hasSaved = saved.TryGet(id, d.Stat, out float savedValue);
            float effective = hasPending ? pending : official;
            bool changed = hasPending != hasSaved || (hasPending && pending != savedValue);
            bool custom = !WarSandboxUnitStats.Same(effective, official);
            var source = hasPending ? WarSandboxStatSource.Global : WarSandboxStatSource.Official;
            string k = "lib-r-" + d.Key;
            if (row % 2 == 0) ui.Panel(k + "-bg", new Rect(0, y, rw, 42), WarSandboxStatTheme.Alpha(WarSandboxStatTheme.Card, 0.6f), false);
            ui.LabelAligned(k + "-name", new Rect(6, y, labelW - 6, 42), d.Label + (string.IsNullOrEmpty(d.Unit) ? "" : "  " + d.Unit), 14,
                custom ? WarSandboxStatTheme.Bright : WarSandboxStatTheme.Text, custom, TextAnchor.MiddleLeft);
            WarSandboxStatTheme.SliderWindow(d, official, effective, out float lo, out float hi, out bool log);
            float sx = labelW + gap;
            ui.TintSlider(k + "-slider", new Rect(sx, y + 12, sliderW, 12), WarSandboxStatTheme.ToSlider(effective, lo, hi, log), t =>
            {
                if (libraryPending.Set(d.Stat, d.Step(WarSandboxStatTheme.FromSlider(t, lo, hi, log)))) { libraryNotice = null; nextRefresh = 0; }
            }, WarSandboxStatTheme.Alpha(WarSandboxStatTheme.Purple, 0.2f), WarSandboxStatTheme.SourceColor(source));
            float tick = sx + 10 + (sliderW - 20) * WarSandboxStatTheme.ToSlider(official, lo, hi, log);
            ui.Panel(k + "-tick", new Rect(tick - 1, y + 7, 2, 22), WarSandboxStatTheme.Alpha(WarSandboxStatTheme.Bright, 0.75f), false);
            ui.LabelAligned(k + "-official", new Rect(sx, y + 27, sliderW, 15), custom ? "官方 " + d.Format(official) : "", 11,
                WarSandboxStatTheme.Official, false, official <= lo + (hi - lo) * 0.5f ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight);
            float fx = labelW + sliderW + 2 * gap;
            ui.TintField(k + "-field", new Rect(fx, y + 5, fieldW, 32), d.Format(effective), text =>
            {
                if (!WarSandboxStatTheme.TryParse(text, out float typed)) { LibraryNotice(d.Label + "：请输入数字。", true); return; }
                float clamped = d.Clamp(typed);
                if (WarSandboxUnitStats.Same(clamped, effective) && clamped == typed) return;
                libraryPending.Set(d.Stat, clamped);
                LibraryNotice(clamped != typed ? d.Label + " 已限制在 " + d.Format(d.Min) + " – " + d.Format(d.Max) + "。" : null, clamped != typed);
            }, WarSandboxStatTheme.Field, changed ? WarSandboxStatTheme.Amber : custom ? WarSandboxStatTheme.Purple : WarSandboxStatTheme.Text);
            WarSandboxStatTheme.Chip(ui, k + "-src", new Rect(fx + fieldW + gap, y + 10, chipW, 22),
                WarSandboxStatTheme.SourceName(source) + (changed ? "*" : ""), changed ? WarSandboxStatTheme.Amber : WarSandboxStatTheme.SourceColor(source));
            ui.TintButton(k + "-clear", new Rect(fx + fieldW + chipW + 2 * gap, y + 6, resetW, 30), "×", () =>
            { if (libraryPending.Remove(d.Stat)) { libraryNotice = null; nextRefresh = 0; } },
                WarSandboxStatTheme.Button, WarSandboxStatTheme.ButtonInk, hasPending, false, 16);
        }

        private void DrawLibraryUnsaved(WarSandboxUnitTemplateEntry entry)
        {
            float w = ui.Width, h = ui.Height;
            ui.Panel("lib-modal-shade", new Rect(0, 0, w, h), WarSandboxStatTheme.Alpha(Color.black, 0.62f));
            float W = Mathf.Min(480, w - 40), H = 230, x = (w - W) / 2, y = (h - H) / 2;
            var card = new Rect(x, y, W, H);
            ui.Panel("lib-modal-card", card, WarSandboxStatTheme.Card); WarSandboxStatTheme.Corners(ui, "lib-modal-card", card);
            ui.LabelAligned("lib-modal-title", new Rect(x + 22, y + 18, W - 44, 34), "保存对 " + entry.config.unitTypeName + " 的修改？", 22, WarSandboxStatTheme.Bright, true, TextAnchor.MiddleLeft);
            ui.LabelAligned("lib-modal-body", new Rect(x + 22, y + 58, W - 44, 54), "这些全局数值还没有保存。放弃后恢复为上次保存的数值。", 14, WarSandboxStatTheme.Muted, false, TextAnchor.UpperLeft);
            float bw = (W - 44 - 16) / 3, by = y + H - 60;
            ui.TintButton("lib-modal-cancel", new Rect(x + 22, by, bw, 40), "取消", () => { libraryModal = LibraryModal.None; libraryAfterDecision = null; nextRefresh = 0; },
                WarSandboxStatTheme.Button, WarSandboxStatTheme.ButtonInk);
            ui.TintButton("lib-modal-discard", new Rect(x + 30 + bw, by, bw, 40), "放弃", () =>
            { libraryModal = LibraryModal.None; libraryPending = null; var next = libraryAfterDecision; libraryAfterDecision = null; next?.Invoke(); nextRefresh = 0; },
                WarSandboxStatTheme.Alpha(WarSandboxStatTheme.Red, 0.16f), WarSandboxStatTheme.Red);
            ui.TintButton("lib-modal-save", new Rect(x + 38 + 2 * bw, by, bw, 40), "保存", () =>
            {
                libraryModal = LibraryModal.None;
                if (!SaveLibraryPending(entry)) { libraryAfterDecision = null; return; }
                libraryPending = null; var next = libraryAfterDecision; libraryAfterDecision = null; next?.Invoke(); nextRefresh = 0;
            }, WarSandboxStatTheme.Purple, WarSandboxStatTheme.Deep, true, true);
        }

        private void DrawLibraryResetAll(WarSandboxGlobalStats saved)
        {
            float w = ui.Width, h = ui.Height;
            ui.Panel("lib-reset-shade", new Rect(0, 0, w, h), WarSandboxStatTheme.Alpha(Color.black, 0.62f));
            float W = Mathf.Min(480, w - 40), H = 240, x = (w - W) / 2, y = (h - H) / 2;
            var card = new Rect(x, y, W, H);
            ui.Panel("lib-reset-card", card, WarSandboxStatTheme.Card); WarSandboxStatTheme.Corners(ui, "lib-reset-card", card);
            ui.LabelAligned("lib-reset-title", new Rect(x + 22, y + 18, W - 44, 34), "全部恢复官方数值？", 22, WarSandboxStatTheme.Bright, true, TextAnchor.MiddleLeft);
            ui.LabelAligned("lib-reset-body", new Rect(x + 22, y + 58, W - 44, 70),
                "将清除 " + saved.Count + " 个兵种的全局数值。各方案里单独保存的本局数值不受影响。\n此操作可用“撤销上次全局修改”恢复。", 14, WarSandboxStatTheme.Muted, false, TextAnchor.UpperLeft);
            float bw = (W - 44 - 10) / 2, by = y + H - 60;
            ui.TintButton("lib-reset-cancel", new Rect(x + 22, by, bw, 40), "取消", () => { libraryModal = LibraryModal.None; nextRefresh = 0; },
                WarSandboxStatTheme.Button, WarSandboxStatTheme.ButtonInk);
            ui.TintButton("lib-reset-confirm", new Rect(x + 32 + bw, by, bw, 40), "全部恢复", () =>
            {
                libraryModal = LibraryModal.None;
                if (LibraryStore.TrySave(new WarSandboxGlobalStats(), "兵种库：全部恢复官方", out string error)) { libraryPending = null; LibraryNotice("已全部恢复官方数值。", false); }
                else LibraryNotice(error, true);
            }, WarSandboxStatTheme.Red, WarSandboxStatTheme.Deep, true, true);
        }
    }
}
