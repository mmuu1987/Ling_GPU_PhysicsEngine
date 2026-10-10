using System;
using System.Collections.Generic;
using UnityEngine;

namespace MassEngine.Game
{
    /// <summary>Stage-specific navigation; no scenario/catalog/plan data is authored here.</summary>
    public sealed partial class WarSandboxFrontEnd
    {
        private bool homeOpen = true, openDeploymentOnEnter;
        private string catalogFilter = "全部";
        public bool HomeOpen => homeOpen;

        private void EnterSelectedBattlefield()
        {
            EnsureCatalogSelection();
            if (catalogSelected < 0) return;
            if (session.TryEnterBattlefield(session.catalog.entries[catalogSelected].id, false, out _))
            { openDeploymentOnEnter = true; homeOpen = false; }
        }
        private void DrawNavigation()
        {
            // A small, consistent system strip; it never covers the main legion editor.
            var r = NavigationLayout(ui.Width);
            if (Guide30.For(session) != null) { ui.Button("nav-help", new Rect(r.x, r.y, 56, r.height), "帮助", OpenHelp30); r.x += 60; }
            ui.Button("nav-settings", new Rect(r.x, r.y, 56, r.height), "设置", session.OpenSettings);
            ui.Button("nav-menu", new Rect(r.x + 60, r.y, 88, r.height), "战场目录", () => { homeOpen = false; RequestReturn(); });
            ui.Button("nav-quit", new Rect(r.x + 152, r.y, 52, r.height), "退出", RequestQuit);
            if (!string.IsNullOrEmpty(session.Error))
            {
                ui.Panel("nav-error-bg", new Rect(ui.Width - 436, 52, 420, 76));
                ui.Label("nav-error", new Rect(ui.Width - 426, 56, 400, 68), session.Error, 14, WarSandboxUGUI.Danger);
            }
        }
        private void DrawHome()
        {
            float w = ui.Width, h = ui.Height;
            ui.Panel("home-bg", new Rect(0, 0, w, h), WarSandboxUGUI.Background);
            ui.Panel("home-sky", new Rect(20, 20, w - 40, h - 40), WarSandboxUGUI.Soft);
            ui.Label("home-eyebrow", new Rect(62, h * .18f, w * .5f, 30), "你的军团，你的战场", 18, WarSandboxUGUI.Accent);
            ui.Label("home-title", new Rect(54, h * .18f + 38, w * .55f, 86), "战争沙盒", 58, WarSandboxUGUI.Ink, true);
            ui.Label("home-description", new Rect(62, h * .18f + 130, w * .48f, 64), "选择战场，组建自己的军团。\n布好阵，再一起出发。", 20, WarSandboxUGUI.Muted);
            float y = h * .18f + 230;
            ui.Button("home-play", new Rect(72, y, 280, 58), "开始游戏  →", () => { homeOpen = false; nextRefresh = 0; }, true);
            ui.Button("menu-library", new Rect(72, y + 74, 132, 42), "兵种图鉴", OpenLibrary);
            ui.Button("menu-settings", new Rect(220, y + 74, 132, 42), "设置", session.OpenSettings);
            ui.Button("menu-quit", new Rect(72, y + 130, 280, 38), "退出游戏", RequestQuit);
            if (Guide30.For(session) != null) ui.Button("menu-help", new Rect(370, y + 74, 132, 42), "操作帮助", OpenHelp30);
            var t = session.catalog != null ? session.catalog.FindTemplate("roster-robot-expressive") : null;
            var rect = new Rect(w * .59f, h * .14f, w * .31f, h * .64f);
            ui.Panel("home-portrait-card", rect, WarSandboxUGUI.Surface);
            if (t != null && t.unitPreview != null) ui.PictureFit("home-portrait", new Rect(rect.x + 24, rect.y + 20, rect.width - 48, rect.height - 78), t.unitPreview);
            ui.LabelAligned("home-portrait-note", new Rect(rect.x + 10, rect.yMax - 48, rect.width - 20, 34), "认识你的新队友", 18, WarSandboxUGUI.Ink, true, TextAnchor.MiddleCenter);
            ui.Label("home-footer", new Rect(64, h - 66, w - 128, 30), "本地单机  ·  方案与兵种数值保存在本机", 13, WarSandboxUGUI.Muted);
        }
        private void DrawCatalog()
        {
            if (homeOpen) { DrawHome(); return; }
            float w = ui.Width, h = ui.Height;
            ui.Panel("catalog-bg", new Rect(0, 0, w, h), WarSandboxUGUI.Background);
            ui.Panel("catalog-header", new Rect(12, 12, w - 24, 66), WarSandboxUGUI.Bar);
            ui.Button("catalog-home", new Rect(28, 26, 120, 38), "‹ 主菜单", () => { homeOpen = true; nextRefresh = 0; });
            ui.LabelAligned("catalog-title", new Rect(170, 20, w - 560, 48), "选择战场", 30, WarSandboxUGUI.Ink, true, TextAnchor.MiddleLeft);
            ui.Button("menu-library", new Rect(w - 286, 26, 136, 38), "兵种图鉴", OpenLibrary);
            ui.Button("menu-settings", new Rect(w - 138, 26, 110, 38), "设置", session.OpenSettings);
            if (Guide30.For(session) != null) ui.Button("catalog-help", new Rect(w - 390, 26, 96, 38), "帮助", OpenHelp30);
            var entries = session.catalog != null ? session.catalog.entries : null;
            if (entries == null || entries.Length == 0)
            { ui.Label("catalog-empty", new Rect(40, 110, w - 80, 80), "没有可用战场，请检查战场目录。", 20, WarSandboxUGUI.Danger); return; }
            EnsureCatalogSelection();
            var groups = new List<string> { "全部" };
            foreach (var e in entries) if (e != null && !e.hiddenFromSelection && !groups.Contains(CatalogGroup(e.id))) groups.Add(CatalogGroup(e.id));
            float sidebar = 152, right = Mathf.Clamp(w * .28f, 310, 430), top = 100, body = h - 200;
            ui.Panel("catalog-groups", new Rect(20, top, sidebar, body));
            ui.Label("catalog-groups-title", new Rect(28, top + 12, sidebar - 16, 34), "战场分类", 17, WarSandboxUGUI.Ink, true);
            for (int i = 0; i < groups.Count; i++)
            {
                string group = groups[i];
                ui.Button("catalog-group-" + i, new Rect(32, top + 62 + i * 50, sidebar - 24, 40), group, () =>
                {
                    catalogFilter = group;
                    int first = Array.FindIndex(entries, e => e != null && !e.hiddenFromSelection && (group == "全部" || CatalogGroup(e.id) == group));
                    if (first >= 0) SelectCatalog(first);
                }, false, true, catalogFilter == group);
            }
            float cx = sidebar + 40, cw = w - cx - right - 44, cardW = (cw - 16) / 2, rowH = 210;
            var visible = new List<int>();
            for (int i = 0; i < entries.Length; i++) if (entries[i] != null && !entries[i].hiddenFromSelection && (catalogFilter == "全部" || CatalogGroup(entries[i].id) == catalogFilter)) visible.Add(i);
            if (visible.Count > 0 && !visible.Contains(catalogSelected)) catalogSelected = visible[0];
            ui.Scroll("catalog-scroll", new Rect(cx, top, cw, body), Mathf.Ceil(visible.Count / 2f) * rowH);
            for (int n = 0; n < visible.Count; n++)
            {
                int index = visible[n]; var e = entries[index]; string key = "card-" + index; float x = n % 2 * (cardW + 16), y = n / 2 * rowH;
                ui.Button(key, new Rect(x, y, cardW, rowH - 16), "", () => SelectCatalog(index), false, true, index == catalogSelected);
                ui.PictureFit(key + "-preview", new Rect(x + 10, y + 10, cardW - 20, 108), e.preview);
                ui.Label(key + "-name", new Rect(x + 6, y + 120, cardW - 12, 38), e.displayName, 17, WarSandboxUGUI.Ink, true);
                TryParseDescription(e.description, out string armies, out string count, out _, out _);
                ui.Label(key + "-facts", new Rect(x + 6, y + 158, cardW - 12, 30), (count ?? "") + " 人 · " + (armies ?? "") + " 军团", 13, WarSandboxUGUI.Muted);
            }
            ui.EndScroll();
            if (catalogReveal)
            { int n = visible.IndexOf(catalogSelected); if (n >= 0) ui.ScrollIntoView("catalog-scroll", n / 2 * rowH, n / 2 * rowH + rowH); catalogReveal = false; }
            if (catalogSelected < 0) return;
            var sel = entries[catalogSelected]; float rx = w - right - 20;
            ui.Panel("catalog-detail-bg", new Rect(rx, top, right, body));
            ui.Label("detail-title", new Rect(rx + 14, top + 18, right - 28, 64), sel.displayName, 27, WarSandboxUGUI.Ink, true);
            ui.Scroll("catalog-detail-scroll", new Rect(rx + 14, top + 96, right - 28, body - 112), 340);
            ui.Label("detail-section", new Rect(0, 0, right - 56, 32), "这场战斗", 17, WarSandboxUGUI.Accent, true);
            ui.Label("detail-text", new Rect(0, 44, right - 56, 106), sel.description ?? "", 17);
            ui.Panel("detail-objective", new Rect(0, 162, right - 52, 156), WarSandboxUGUI.Raised);
            ui.Label("detail-briefing", new Rect(8, 168, right - 68, 142), sel.briefing ?? "在下一步布置军团。", 16, WarSandboxUGUI.Muted);
            ui.EndScroll();
            ui.Panel("catalog-footer", new Rect(12, h - 82, w - 24, 70), WarSandboxUGUI.Bar);
            ui.Label("catalog-step", new Rect(30, h - 70, w - 370, 42), "选择战场  →  战前布阵  →  开战", 17, WarSandboxUGUI.Ink);
            ui.Button("card-" + catalogSelected + "-enter", new Rect(w - 286, h - 68, 252, 44), "进入布阵  →", EnterSelectedBattlefield, true);
            if (!string.IsNullOrEmpty(session.Error)) ui.Label("catalog-error", new Rect(cx, h - 111, cw, 26), session.Error, 13, WarSandboxUGUI.Danger);
        }
    }
}
