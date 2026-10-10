using System.Collections.Generic;
using UnityEngine;

namespace MassEngine.Game
{
    public sealed partial class WarSandboxFrontEnd
    {
        private string portraitBattlefieldId;
        private int portraitSelected;
        private bool unitPreviewOpen;
        private WarSandboxUnitTemplateEntry enlargedUnit;

        /// <summary>Two distinct images in the battlefield-detail region: overview left, actual unit right.</summary>
        public static void CatalogPreviewLayout(Rect area, out Rect overview, out Rect unit)
        {
            float gap = 12, right = Mathf.Clamp(area.width * .30f, 112, 214);
            right = Mathf.Min(right, Mathf.Max(0, area.width * .46f));
            unit = new Rect(area.xMax - right, area.y, right, area.height);
            float width = Mathf.Max(0, unit.x - gap - area.x);
            overview = new Rect(area.x, area.y, width, Mathf.Min(area.height, width * 9f / 16f));
        }

        public static List<WarSandboxUnitTemplateEntry> PreviewChoices(WarSandboxBattlefieldCatalog catalog, WarSandboxBattlefieldEntry entry)
        {
            var result = new List<WarSandboxUnitTemplateEntry>();
            if (catalog == null || entry == null || entry.featuredTemplateIds == null) return result;
            foreach (var id in entry.featuredTemplateIds)
            {
                var template = catalog.FindTemplate(id);
                if (template != null && template.config != null && !template.hiddenFromSelection && !result.Contains(template)) result.Add(template);
            }
            return result;
        }
        private void DrawCatalogPreviews(WarSandboxBattlefieldEntry entry, Rect area)
        {
            if (area.height < 60 || area.width < 120) return;
            if (portraitBattlefieldId != entry.id) { portraitBattlefieldId = entry.id; portraitSelected = 0; unitPreviewOpen = false; }
            var choices = PreviewChoices(session.catalog, entry);
            portraitSelected = Mathf.Clamp(portraitSelected, 0, Mathf.Max(0, choices.Count - 1));
            CatalogPreviewLayout(area, out Rect overview, out Rect unit);
            ui.Panel("detail-preview-frame", new Rect(overview.x - 1, overview.y - 1, overview.width + 2, overview.height + 2), WarSandboxUGUI.Line, false);
            ui.PictureFit("detail-preview", overview, entry.preview);
            ui.Chip("detail-preview-tag", new Rect(overview.x + 6, overview.y + 6, Mathf.Min(124, overview.width - 12), 20),
                "RECON // 战场预览", new Color32(4, 12, 18, 230), WarSandboxUGUI.Accent, null, overview.width < 190 ? 9 : 11);
            if (area.height > overview.height + 30)
                ui.LabelAligned("detail-preview-help", new Rect(overview.x, overview.yMax + 8, overview.width, 26), "右侧为单体实机模型，可点击放大。", 12, WarSandboxUGUI.Muted, false, TextAnchor.MiddleLeft);
            Rect portrait = unit; bool multiple = choices.Count > 1 && unit.height >= 110;
            if (multiple) portrait.height -= 30;
            DrawUnitPortrait("detail-unit", portrait, choices.Count > 0 ? choices[portraitSelected] : null);
            if (multiple)
            {
                float y = unit.yMax - 24;
                ui.TintButton("detail-unit-prev", new Rect(unit.x, y, 30, 24), "‹", () => SwitchPortrait(-1, choices.Count), WarSandboxUGUI.Raised, WarSandboxUGUI.Accent);
                ui.Code("detail-unit-counter", new Rect(unit.x + 32, y, unit.width - 64, 24), (portraitSelected + 1) + " / " + choices.Count,
                    11, WarSandboxUGUI.Muted, TextAnchor.MiddleCenter);
                ui.TintButton("detail-unit-next", new Rect(unit.xMax - 30, y, 30, 24), "›", () => SwitchPortrait(1, choices.Count), WarSandboxUGUI.Raised, WarSandboxUGUI.Accent);
            }
        }
        private void SwitchPortrait(int delta, int count)
        {
            if (count <= 0) return;
            portraitSelected = (portraitSelected + delta + count) % count; nextRefresh = 0;
        }
        private void DrawUnitPortrait(string id, Rect rect, WarSandboxUnitTemplateEntry entry)
        {
            ui.Panel(id + "-frame", rect, WarSandboxUGUI.FieldFill, false, WarSandboxUGUI.Line);
            ui.Code(id + "-tag", new Rect(rect.x + 8, rect.y + 3, rect.width - 58, 23), "UNIT // 单体", rect.width < 150 ? 10 : 11, WarSandboxUGUI.Accent);
            ui.TintButton(id + "-zoom", new Rect(rect.xMax - 46, rect.y + 4, 40, 22), "+", () => OpenUnitPreview(entry),
                WarSandboxUGUI.Raised, WarSandboxUGUI.Accent, entry != null && entry.unitPreview != null);
            float footer = rect.height < 180 ? 42 : 54;
            var image = new Rect(rect.x + 8, rect.y + 30, Mathf.Max(0, rect.width - 16), Mathf.Max(0, rect.height - 30 - footer));
            if (entry != null && entry.unitPreview != null)
            {
                ui.PictureFit(id + "-image", image, entry.unitPreview);
                ui.TintButton(id + "-open", image, "", () => OpenUnitPreview(entry), new Color(0, 0, 0, 0), WarSandboxUGUI.Ink);
            }
            else ui.LabelAligned(id + "-missing", image, "尚未配置真实模型图", 12, WarSandboxUGUI.Muted, false, TextAnchor.MiddleCenter);
            ui.LabelAligned(id + "-name", new Rect(rect.x + 8, rect.yMax - footer + 2, rect.width - 16, footer < 50 ? 16 : 24),
                entry != null && entry.config != null ? entry.config.unitTypeName : "单体预览待配置", rect.width < 150 || footer < 50 ? 11 : 14,
                WarSandboxUGUI.Ink, true, TextAnchor.MiddleCenter);
            ui.LabelAligned(id + "-truth", new Rect(rect.x + 4, rect.yMax - 24, rect.width - 8, 18), entry != null && entry.unitPreview != null ? "实际游戏模型 · 静态" : "真实模型图待生成", rect.width < 150 ? 9 : 11,
                WarSandboxUGUI.Muted, false, TextAnchor.MiddleCenter);
        }
        private void OpenUnitPreview(WarSandboxUnitTemplateEntry entry)
        {
            if (entry == null || entry.config == null) return;
            enlargedUnit = entry; unitPreviewOpen = true; nextRefresh = 0;
        }
        private void DrawExpandedUnitPreview()
        {
            if (enlargedUnit == null || enlargedUnit.config == null) { unitPreviewOpen = false; return; }
            ui.Panel("unit-zoom-shade", new Rect(0, 0, ui.Width, ui.Height), WarSandboxUGUI.Shade);
            float w = Mathf.Min(510, ui.Width - 40), h = Mathf.Min(600, ui.Height - 40);
            var panel = new Rect((ui.Width - w) * .5f, (ui.Height - h) * .5f, w, h);
            ui.Panel("unit-zoom-panel", panel); ui.Brackets("unit-zoom-brackets", panel);
            ui.Code("unit-zoom-code", new Rect(panel.x + 20, panel.y + 12, w - 110, 24), "UNIT // FULL BODY", 12, WarSandboxUGUI.Accent);
            ui.Button("unit-zoom-close", new Rect(panel.xMax - 86, panel.y + 12, 66, 28), "关闭", () => { unitPreviewOpen = false; nextRefresh = 0; });
            var physical = LibraryRadius(enlargedUnit);
            ui.ModelPreview("unit-zoom-image", new Rect(panel.x + 24, panel.y + 52, w - 48, h - 174), enlargedUnit.config, enlargedUnit.unitPreview, physical.Available ? (float?)physical.BaseRadius : null);
            ui.LabelAligned("unit-zoom-name", new Rect(panel.x + 20, panel.yMax - 114, w - 40, 30), enlargedUnit.config.unitTypeName,
                20, WarSandboxUGUI.Ink, true, TextAnchor.MiddleCenter);
            ui.LabelAligned("unit-zoom-radius", new Rect(panel.x + 20, panel.yMax - 82, w - 40, 24), physical.Summary + " · 预览", 13,
                WarSandboxUGUI.Ink, false, TextAnchor.MiddleCenter);
            ui.LabelAligned("unit-zoom-source", new Rect(panel.x + 20, panel.yMax - 57, w - 40, 24), "来源：" + physical.Source, 10,
                WarSandboxUGUI.Muted, false, TextAnchor.MiddleCenter);
            ui.LabelAligned("unit-zoom-truth", new Rect(panel.x + 20, panel.yMax - 32, w - 40, 22), UnitPreviewRadius.Legend, 12,
                WarSandboxUGUI.Muted, false, TextAnchor.MiddleCenter);
        }
    }
}


