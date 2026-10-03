using System;
using UnityEngine;

namespace MassEngine.Game
{
    [RequireComponent(typeof(WarSandboxSceneSession))]
    public sealed partial class WarSandboxFrontEnd : MonoBehaviour
    {
        private WarSandboxSceneSession session;
        private WarSandboxUGUI ui;
        private bool confirmingQuit;
        private float nextRefresh;
        private Vector2 lastUiSize;
        // Catalog selection (B1 layout: list on the left, the selected battlefield on the right).
        private int catalogSelected = -1;
        private bool catalogReveal;
        private void Awake() => session = GetComponent<WarSandboxSceneSession>();
        private void Update()
        {
            if (session == null || WarSandboxSceneSession.Instance != session) return;
            if (libraryOpen && (session.State != WarSandboxEntryState.Menu || session.SettingsOpen || session.ConfirmationOpen)) CloseLibrary();
            bool closedPreview = Input.GetKeyDown(KeyCode.Escape) && unitPreviewOpen;
            if (closedPreview) { unitPreviewOpen = false; nextRefresh = 0; }
            if (Input.GetKeyDown(KeyCode.Escape) && libraryOpen && !closedPreview) LibraryEscape();
            if (Input.GetKeyDown(KeyCode.Escape) && session.SettingsOpen) CloseSettings();
            if (Input.GetKeyDown(KeyCode.Escape) && session.ConfirmationOpen) session.CancelConfirmation();
            if (CatalogInteractive()) CatalogKeys();
            if (ui == null) ui = new WarSandboxUGUI(transform, "Front End Canvas", 200);
            var size = new Vector2(ui.Width, ui.Height);
            if (size != lastUiSize) { lastUiSize = size; nextRefresh = 0; } // Resize must redraw now, not leave a clipped old layout.
            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + 0.1f;
            ui.Begin();
            if (session.SettingsOpen) DrawSettings();
            else if (session.ConfirmationOpen) DrawConfirmation();
            else switch (session.State)
            {
                case WarSandboxEntryState.Battle: DrawNavigation(); break;
                case WarSandboxEntryState.Loading: DrawLoading(); break;
                case WarSandboxEntryState.Failed: DrawFailure(); break;
                default: if (libraryOpen) DrawLibrary(); else DrawCatalog(); break;
            }
            if (unitPreviewOpen && session.State == WarSandboxEntryState.Menu && !session.SettingsOpen && !session.ConfirmationOpen)
                DrawExpandedUnitPreview();
            else if (session.State != WarSandboxEntryState.Menu) unitPreviewOpen = false;
            ui.End();
        }

        // ---- Battle system strip (top right): 设置 / 战场目录 / 退出 ----
        private const float NavWidth = 204, NavHeight = 30, NavTop = 10, NavMargin = 16;
        /// <summary>Screen-space (GUI, top-left origin) rect of the in-battle system strip; the battle HUD lays out around it.</summary>
        public static Rect NavigationRect(float width, float height)
        {
            float scale = Mathf.Clamp(Mathf.Min(width / 1280f, height / 720f), 0.85f, 1.5f);
            return new Rect(width - (NavMargin + NavWidth) * scale, NavTop * scale, NavWidth * scale, NavHeight * scale);
        }
        /// <summary>The same strip in UI units (what presenters pass to WarSandboxUGUI).</summary>
        public static Rect NavigationLayout(float uiWidth) => new Rect(uiWidth - NavMargin - NavWidth, NavTop, NavWidth, NavHeight);
        public static bool IsOverNavigation(Vector2 point)
        {
            var session = WarSandboxSceneSession.Instance;
            return session != null && (session.InputBlocked || NavigationRect(Screen.width, Screen.height).Contains(point));
        }
        private void DrawNavigation()
        {
            var r = NavigationLayout(ui.Width);
            ui.Button("nav-settings", new Rect(r.x, r.y, 56, r.height), "设置", session.OpenSettings);
            ui.Button("nav-menu", new Rect(r.x + 60, r.y, 88, r.height), "战场目录", RequestReturn);
            ui.Button("nav-quit", new Rect(r.x + 152, r.y, 52, r.height), "退出", RequestQuit);
            if (!string.IsNullOrEmpty(session.Error))
            {
                float w = Mathf.Min(420, ui.Width - 32);
                ui.Panel("nav-error-bg", new Rect(ui.Width - 16 - w, r.yMax + 8, w, 96), null, true, WarSandboxUGUI.Danger);
                ui.Label("nav-error", new Rect(ui.Width - 12 - w, r.yMax + 12, w - 8, 88), session.Error, 14, WarSandboxUGUI.Danger);
            }
        }

        // ---- Catalog (B1) ----
        /// <summary>List group of a catalog entry, from the official id prefixes; unknown ids fall into "战场".</summary>
        public static string CatalogGroup(string id)
        {
            id = id ?? "";
            if (id.StartsWith("launch-", StringComparison.Ordinal)) return "首发战役";
            if (id.StartsWith("unified-", StringComparison.Ordinal)) return "自由编成";
            if (id.StartsWith("cavalry-", StringComparison.Ordinal) || id.StartsWith("dragons-", StringComparison.Ordinal) ||
                id.StartsWith("giants-", StringComparison.Ordinal) || id.StartsWith("nonhuman", StringComparison.Ordinal) ||
                id.StartsWith("troops", StringComparison.Ordinal)) return "新兵种";
            return "战场";
        }
        /// <summary>
        /// Splits an official description ("2 支军团 · 512 人 · 歼灭战\n简介") into its facts. Returns false (and the whole text
        /// as <paramref name="flavour"/>) when the first line does not follow that form; nothing is invented.
        /// </summary>
        public static bool TryParseDescription(string description, out string armies, out string troops, out string mode, out string flavour)
        {
            armies = troops = mode = null; flavour = description ?? "";
            if (string.IsNullOrEmpty(description)) return false;
            int newline = description.IndexOf('\n');
            string first = newline >= 0 ? description.Substring(0, newline) : description;
            var parts = first.Split(new[] { " · " }, StringSplitOptions.None);
            if (parts.Length != 3 || !parts[0].EndsWith(" 支军团", StringComparison.Ordinal) || !parts[1].EndsWith(" 人", StringComparison.Ordinal)) return false;
            armies = parts[0].Substring(0, parts[0].Length - 4).Trim(); troops = parts[1].Substring(0, parts[1].Length - 2).Trim(); mode = parts[2].Trim();
            flavour = newline >= 0 ? description.Substring(newline + 1).Trim() : "";
            return armies.Length > 0 && troops.Length > 0;
        }
        private bool CatalogInteractive() => session.State == WarSandboxEntryState.Menu && !unitPreviewOpen && !libraryOpen && !session.SettingsOpen && !session.ConfirmationOpen &&
            !session.IsLoading && session.catalog != null && session.catalog.entries != null && session.catalog.entries.Length > 0 && !WarSandboxUGUI.IsTyping;
        private void CatalogKeys()
        {
            var entries = session.catalog.entries; EnsureCatalogSelection();
            int move = Input.GetKeyDown(KeyCode.DownArrow) ? 1 : Input.GetKeyDown(KeyCode.UpArrow) ? -1 : 0;
            if (move != 0) { SelectCatalog(NextCatalogIndex(entries, catalogSelected, move)); }
            if ((Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) && session.Error == null)
                session.TryEnterBattlefield(entries[catalogSelected].id, false, out _);
        }
        private void EnsureCatalogSelection()
        {
            var entries = session.catalog.entries;
            if (catalogSelected >= 0 && catalogSelected < entries.Length && !entries[catalogSelected].hiddenFromSelection) return;
            catalogSelected = Array.FindIndex(entries, e => e != null && !e.hiddenFromSelection && e.id == session.catalog.defaultEntryId);
            if (catalogSelected < 0) catalogSelected = Array.FindIndex(entries, e => e != null && !e.hiddenFromSelection);
        }
        public static int NextCatalogIndex(WarSandboxBattlefieldEntry[] entries, int current, int direction)
        {
            if (entries == null || direction == 0) return current;
            int step = direction > 0 ? 1 : -1;
            for (int i = current + step; i >= 0 && i < entries.Length; i += step)
                if (entries[i] != null && !entries[i].hiddenFromSelection) return i;
            return current; // No wrapping; retain the existing arrow-key behaviour at the ends.
        }
        private void SelectCatalog(int index)
        {
            var entries = session.catalog.entries;
            if (index < 0 || index >= entries.Length || entries[index] == null || entries[index].hiddenFromSelection) return;
            catalogSelected = index; catalogReveal = true; nextRefresh = 0;
        }
        private void DrawCatalog()
        {
            float W = ui.Width, H = ui.Height;
            ui.Panel("catalog-bg", new Rect(0, 0, W, H), WarSandboxUGUI.Background);
            ui.Panel("catalog-top", new Rect(0, 0, W, 52), WarSandboxUGUI.Bar);
            ui.Panel("catalog-top-line", new Rect(0, 51, W, 1), WarSandboxUGUI.Line, false);
            ui.Code("brand", new Rect(14, 0, 210, 52), "MASS WAR SANDBOX", 15, WarSandboxUGUI.Ink, TextAnchor.MiddleLeft, true);
            ui.Code("brand-sep", new Rect(196, 0, 40, 52), "//", 15, WarSandboxUGUI.Accent, TextAnchor.MiddleLeft, true);
            ui.LabelAligned("brand-page", new Rect(220, 0, 160, 52), "作战选择", 16, WarSandboxUGUI.Ink, true, TextAnchor.MiddleLeft);
            ui.Button("quit", new Rect(W - 76, 11, 60, 30), "退出", RequestQuit);
            ui.Button("menu-settings", new Rect(W - 142, 11, 60, 30), "设置", session.OpenSettings);
            ui.Button("menu-unit-library", new Rect(W - 228, 11, 80, 30), "兵种库", OpenLibrary);
            string validation = null;
            bool valid = session.catalog != null && session.catalog.TryValidate(WarSandboxSceneSession.CanLoadScene, out validation);
            string message = session.Error ?? validation;
            if (!valid || message != null)
            {
                float ew = Mathf.Min(720, W - 48), ex = (W - ew) / 2;
                ui.Panel("catalog-error-card", new Rect(ex, 120, ew, 220), null, true, WarSandboxUGUI.Danger);
                ui.Code("catalog-error-code", new Rect(ex + 20, 132, ew - 40, 22), "CATALOG // ERROR", 12, WarSandboxUGUI.Danger);
                ui.Label("catalog-error", new Rect(ex + 20, 160, ew - 40, 110), message ?? "没有可用的战场目录。", 16, WarSandboxUGUI.Danger);
                ui.Button("catalog-recheck", new Rect(ex + 20, 282, 140, 40), "重新检查", session.ClearError); return;
            }
            var entries = session.catalog.entries; EnsureCatalogSelection();
            ui.Code("catalog-version", new Rect(W - 400, 0, 164, 52), session.catalog.SelectableEntryCount + " 战场", 12, WarSandboxUGUI.Dim, TextAnchor.MiddleRight);

            // Left: grouped list.
            float lx = 24, ly = 72, lw = Mathf.Clamp(W * 0.34f, 300, 440), lh = H - ly - 24;
            var list = new Rect(lx, ly, lw, lh);
            ui.Panel("catalog-list-bg", list); ui.Brackets("catalog-list-br", list);
            float rowH = 36, headH = 30, content = 0; string group = null;
            for (int i = 0; i < entries.Length; i++) { if (entries[i].hiddenFromSelection) continue; string g = CatalogGroup(entries[i].id); if (g != group) { content += headH; group = g; } content += rowH; }
            float sw = lw - 2;
            ui.Scroll("catalog-scroll", new Rect(lx + 1, ly + 10, sw, lh - 52), content + 8);
            float y = 0, selectedTop = 0; group = null; int number = 0;
            for (int i = 0; i < entries.Length; i++)
            {
                var entry = entries[i]; if (entry.hiddenFromSelection) continue; string g = CatalogGroup(entry.id);
                if (g != group)
                {
                    group = g; int count = 0; foreach (var e in entries) if (!e.hiddenFromSelection && CatalogGroup(e.id) == g) count++;
                    ui.LabelAligned("group-" + i, new Rect(8, y, sw - 16, headH), g, 13, WarSandboxUGUI.Accent, true, TextAnchor.MiddleLeft);
                    ui.Code("group-" + i + "-count", new Rect(8, y, sw - 20, headH), count.ToString("00"), 12, WarSandboxUGUI.Accent, TextAnchor.MiddleRight);
                    y += headH;
                }
                number++; int index = i; bool selected = i == catalogSelected; string k = "card-" + i;
                if (selected) selectedTop = y;
                ui.TintButton(k, new Rect(0, y, sw, rowH - 2), "", () => SelectCatalog(index), selected ? WarSandboxUGUI.Soft : new Color32(12, 23, 32, 255), WarSandboxUGUI.Ink);
                if (selected) ui.Panel(k + "-bar", new Rect(0, y, 3, rowH - 2), WarSandboxUGUI.Accent, false);
                ui.Code(k + "-index", new Rect(8, y, 40, rowH - 2), number.ToString("00"), 11, selected ? WarSandboxUGUI.Accent : WarSandboxUGUI.Dim);
                TryParseDescription(entry.description, out _, out string troops, out _, out _);
                bool isNew = g == "新兵种";
                int nameSize = sw < 340 ? 13 : 15;
                float nameWidth = sw - 38 - 76 - (isNew ? 36 : 0), nameInk = 0;
                foreach (char ch in entry.displayName ?? "") nameInk += (ch < 0x2E80 ? 0.57f : 1.03f) * nameSize;
                ui.LabelAligned(k + "-name", new Rect(38, y, nameWidth, rowH - 2), entry.displayName, nameSize, selected ? WarSandboxUGUI.Ink : new Color32(196, 214, 226, 255), selected, TextAnchor.MiddleLeft);
                if (isNew) ui.Chip(k + "-new", new Rect(Mathf.Min(38 + 10 + nameInk, 38 + nameWidth) + 6, y + 11, 30, 14), "NEW", WarSandboxUGUI.Amber, WarSandboxUGUI.Deep, null, 9);
                ui.Code(k + "-count", new Rect(sw - 82, y, 76, rowH - 2), troops ?? "", 12, selected ? WarSandboxUGUI.Accent : WarSandboxUGUI.Muted, TextAnchor.MiddleRight);
                y += rowH;
            }
            ui.EndScroll();
            if (catalogReveal) { ui.ScrollIntoView("catalog-scroll", selectedTop - headH, selectedTop + rowH); catalogReveal = false; }
            ui.Code("catalog-hint", new Rect(lx + 18, ly + lh - 38, lw - 36, 30), "↑↓ 选择    ENTER 部署", 12, WarSandboxUGUI.Muted);

            // Right: selected battlefield.
            var sel = entries[catalogSelected];
            float rx = lx + lw + 24, rw = W - rx - 24, ry = ly, rh = lh, px = rx + 28, pw = rw - 56;
            var detail = new Rect(rx, ry, rw, rh);
            ui.Panel("catalog-detail-bg", detail); ui.Brackets("catalog-detail-br", detail);
            var rules = sel.rules != null ? sel.rules.rules : WarSandboxBattlefieldRules.Default;
            bool point = rules.gameMode == WarSandboxGameMode.ControlPoint;
            string terrain = rules.staticObstaclesEnabled && rules.staticObstacles != null ? rules.staticObstacles.Length + " 处障碍" : sel.terrainSurface != null ? "山地" : "开阔平原";
            string terrainCode = rules.staticObstaclesEnabled ? "OBSTACLES" : sel.terrainSurface != null ? "MOUNTAIN" : "FLAT-GROUND";
            bool parsed = TryParseDescription(sel.description, out string armies, out string total, out string parsedMode, out string flavour);
            ui.Code("detail-code", new Rect(px, ry + 22, pw, 20), "OP-" + (catalogSelected + 1).ToString("00") + " // " + (point ? "CONTROL-POINT" : "ANNIHILATION") + " // " + terrainCode, 12, WarSandboxUGUI.Muted);
            ui.LabelAligned("detail-title", new Rect(px - 10, ry + 44, pw + 10, 52), sel.displayName, 32, WarSandboxUGUI.Ink, true, TextAnchor.MiddleLeft);
            string[] statNames = { "军团", "兵力", "模式", "地形" };
            string[] statValues = { parsed ? armies : "—", parsed ? total : "—", point ? "据点战" : "歼灭战", terrain };
            float gap = 10, bw = (pw - gap * 3) / 4, by = ry + 108;
            for (int i = 0; i < 4; i++)
            {
                var box = new Rect(px + i * (bw + gap), by, bw, 74);
                ui.Panel("detail-stat-" + i, box, new Color32(14, 27, 37, 255), false, WarSandboxUGUI.Line);
                ui.LabelAligned("detail-stat-" + i + "-name", new Rect(box.x + 4, box.y + 6, box.width - 8, 22), statNames[i], 12, WarSandboxUGUI.Muted, false, TextAnchor.MiddleLeft);
                ui.Code("detail-stat-" + i + "-value", new Rect(box.x + 4, box.y + 32, box.width - 8, 34), statValues[i], bw >= 130 ? 21 : 15, WarSandboxUGUI.Ink);
            }
            float ty = by + 88;
            ui.Label("detail-text", new Rect(px - 10, ty, pw + 10, 46), parsed ? flavour : sel.description ?? "", 15, new Color32(196, 214, 226, 255));
            ty += 50;
            long troopsValue = 0;
            if (parsed && long.TryParse(total.Replace(",", ""), out troopsValue) && troopsValue >= 20000)
            {
                ui.Chip("detail-warning", new Rect(px, ty, 214, 24), "▲ 运行负载较高 · 建议独立显卡", new Color32(40, 30, 8, 255), WarSandboxUGUI.Amber, WarSandboxUGUI.Amber, 12, false);
                ty += 34;
            }
            float buttonsY = ry + rh - 70, availableH = buttonsY - 16 - ty;
            DrawCatalogPreviews(sel, new Rect(px, ty, pw, Mathf.Max(0, availableH)));
            ui.Button("card-" + catalogSelected + "-enter", new Rect(rx + rw - 28 - 220, buttonsY, 220, 48), "部署    ›", () => session.TryEnterBattlefield(sel.id, false, out _), true);
            ui.Chip("deploy-key", new Rect(rx + rw - 28 - 64, buttonsY + 16, 52, 16), "ENTER", WarSandboxUGUI.Deep, WarSandboxUGUI.Accent, null, 10);
        }

        // ---- Loading / failure / confirmation ----
        private void DrawLoading()
        {
            ui.Panel("loading-bg", new Rect(0, 0, ui.Width, ui.Height), WarSandboxUGUI.Background);
            float w = Mathf.Min(520, ui.Width - 48), x = (ui.Width - w) / 2, y = ui.Height * 0.38f;
            ui.Code("loading-code", new Rect(x, y - 26, w, 22), "DEPLOYING // 部署中", 12, WarSandboxUGUI.Accent);
            ui.LabelAligned("loading-title", new Rect(x - 10, y, w + 10, 46), "正在部署战场", 28, WarSandboxUGUI.Ink, true, TextAnchor.MiddleLeft);
            ui.Panel("loading-track", new Rect(x, y + 66, w, 4), WarSandboxUGUI.Line);
            ui.Panel("loading-progress", new Rect(x, y + 66, w * session.LoadingProgress, 4), WarSandboxUGUI.Accent);
            ui.Code("loading-value", new Rect(x, y + 82, w, 28), (session.LoadingProgress * 100).ToString("0") + "%  ·  准备规则与军团", 13, WarSandboxUGUI.Muted);
        }
        private void DrawFailure()
        {
            ui.Panel("failure-bg", new Rect(0, 0, ui.Width, ui.Height), WarSandboxUGUI.Background);
            float w = Mathf.Min(680, ui.Width - 48), x = (ui.Width - w) / 2;
            var card = new Rect(x - 24, 40, w + 48, ui.Height - 80);
            ui.Panel("failure-card", card, null, true, WarSandboxUGUI.Danger);
            ui.Code("failure-code", new Rect(x, 56, w, 22), "DEPLOYMENT // FAILED", 12, WarSandboxUGUI.Danger);
            ui.LabelAligned("failure-title", new Rect(x - 10, 80, w + 10, 44), "战场未能就绪", 28, WarSandboxUGUI.Ink, true, TextAnchor.MiddleLeft);
            ui.Scroll("failure-scroll", new Rect(x, 136, w, ui.Height - 290), 480);
            ui.Label("failure-text", new Rect(6, 8, w - 12, 460), session.Error, 15, WarSandboxUGUI.Danger); ui.EndScroll();
            ui.Button("failure-return", new Rect(x, ui.Height - 140, w, 44), "返回战场目录", () => session.TryReturnToMenu(true, out _), true);
            ui.Button("failure-quit", new Rect(x, ui.Height - 88, w, 36), "退出游戏", RequestQuit);
        }
        private void RequestReturn()
        {
            confirmingQuit = false;
            if (session.RequiresEndConfirmation) session.BeginConfirmation(); else session.TryReturnToMenu(false, out _);
        }
        private void RequestQuit() { confirmingQuit = true; session.BeginConfirmation(); }
        private void DrawConfirmation()
        {
            ui.Panel("modal-shade", new Rect(0, 0, ui.Width, ui.Height), WarSandboxUGUI.Shade);
            float w = Mathf.Min(500, ui.Width - 40), x = (ui.Width - w) / 2, y = Mathf.Max(20, (ui.Height - 280) / 2);
            var card = new Rect(x, y, w, 280);
            ui.Panel("modal-card", card); ui.Brackets("modal-card-br", card);
            ui.Code("modal-code", new Rect(x + 24, y + 18, w - 48, 20), confirmingQuit ? "SYSTEM // EXIT" : "MISSION // ABORT", 12, WarSandboxUGUI.Accent);
            ui.LabelAligned("modal-title", new Rect(x + 14, y + 40, w - 28, 44), confirmingQuit ? "退出游戏？" : "结束本局？", 26, WarSandboxUGUI.Ink, true, TextAnchor.MiddleLeft);
            ui.Label("modal-body", new Rect(x + 14, y + 90, w - 28, 80), (WarSandboxRuntimeDeployment.BlocksCommands(session.Controller)
                ? "未应用的布阵修改不会保留。" : "当前战斗进度不会保留。") + (session.Error == null ? "" : "\n" + session.Error), 15, WarSandboxUGUI.Muted);
            ui.Button("modal-confirm", new Rect(x + 24, y + 180, w - 48, 40), confirmingQuit ? "退出游戏" : "结束并返回目录", () =>
            { if (confirmingQuit) Application.Quit(); else session.TryReturnToMenu(true, out _); }, true);
            ui.Button("modal-cancel", new Rect(x + 24, y + 228, w - 48, 34), "取消", session.CancelConfirmation);
        }
        private void OnEnable() { nextRefresh = 0; }
        private void OnDisable() { ui?.SetVisible(false); }
        private void OnDestroy() { ui?.Dispose(); }
    }
}
