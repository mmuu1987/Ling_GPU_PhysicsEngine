using System;
using System.Collections.Generic;
using MassEngine.Game.Ui;
using UnityEngine;
using UnityEngine.UIElements;
using Tk = MassEngine.Game.Ui.WarSandboxUiToolkit;

namespace MassEngine.Game
{
    /// <summary>
    /// Phase 2 of the UI Toolkit migration: Q 版 home, battlefield catalog, loading, failure, in-battle system strip and the
    /// end/leave/quit confirmation. View only — every action calls the same session/front-end methods as the uGUI pages, and
    /// every value shown comes from the catalog/session (no invented data). Settings, help, unit library and the unit zoom
    /// are not migrated yet and still draw through uGUI on top (canvas 200 &gt; panel 199).
    /// </summary>
    public sealed partial class WarSandboxFrontEnd
    {
        private UIDocument tkDoc;
        private VisualElement tkRoot, tkMenu, tkHome, tkCatalog, tkLoading, tkFailure, tkTopbar, tkNav, tkNavError, tkConfirm;
        private VisualElement tkDetail, tkEmpty, tkWarn, tkUnits, tkUnitsHead, tkPreview, tkUnitImg, tkUnitPager, tkLoadFill, tkHomePortrait;
        private Label tkCount, tkCode, tkTitle, tkArmies, tkTroops, tkMode, tkTerrain, tkDesc, tkBriefing, tkPreviewMissing, tkUnitMissing,
            tkUnitName, tkUnitTruth, tkUnitCounter, tkCatError, tkLoadValue, tkFailText, tkNavErrorText, tkConfirmCode, tkConfirmTitle, tkConfirmBody;
        private Button tkUnitZoom, tkConfirmOk, tkHomeHelp, tkTopHelp, tkNavHelp;
        private ScrollView tkList, tkBrief;
        private readonly List<VisualElement> tkRows = new List<VisualElement>();
        private readonly List<int> tkRowEntry = new List<int>();
        private WarSandboxBattlefieldEntry[] tkBuiltEntries;
        private int tkShownSelected = -2, tkShownPortrait = -1;
        private string tkShownPortraitEntry;
        private Rect tkNavRect;
        private List<WarSandboxUnitTemplateEntry> tkChoices = new List<WarSandboxUnitTemplateEntry>();

        /// <summary>True while the new UI draws the current screen (the switch is on and the page is a migrated one).</summary>
        private bool ToolkitOwnsScreen() => Tk.Enabled; // phase 4: settings, help, unit library and unit zoom are migrated too

        private void HideToolkit()
        {
            if (tkRoot != null) Tk.Show(tkRoot, false);
            HidePages();
        }

        private void BuildToolkit()
        {
            tkDoc = Tk.CreateDocument(transform, "Front End UI Toolkit", "FrontEnd", 10);
            if (tkDoc == null) return;
            var r = tkDoc.rootVisualElement;
            tkRoot = r;
            tkMenu = r.Q("fe-menu"); tkHome = r.Q("page-home"); tkCatalog = r.Q("page-catalog"); tkLoading = r.Q("page-loading");
            tkFailure = r.Q("page-failure"); tkTopbar = r.Q("fe-topbar"); tkNav = r.Q("fe-nav"); tkNavError = r.Q("fe-nav-error"); tkConfirm = r.Q("fe-confirm");
            tkDetail = r.Q("cat-detail"); tkEmpty = r.Q("cat-empty"); tkWarn = r.Q("cat-warn"); tkUnits = r.Q("cat-units"); tkUnitsHead = r.Q("cat-units-head");
            tkPreview = r.Q("cat-preview"); tkUnitImg = r.Q("cat-unit-img"); tkUnitPager = r.Q("cat-unit-pager"); tkLoadFill = r.Q("load-fill");
            tkHomePortrait = r.Q("home-portrait");
            tkCount = r.Q<Label>("cat-count"); tkCode = r.Q<Label>("cat-code"); tkTitle = r.Q<Label>("cat-title");
            tkArmies = r.Q<Label>("st-armies"); tkTroops = r.Q<Label>("st-troops"); tkMode = r.Q<Label>("st-mode"); tkTerrain = r.Q<Label>("st-terrain");
            tkDesc = r.Q<Label>("cat-desc"); tkBriefing = r.Q<Label>("cat-briefing"); tkPreviewMissing = r.Q<Label>("cat-preview-missing");
            tkUnitMissing = r.Q<Label>("cat-unit-missing"); tkUnitName = r.Q<Label>("cat-unit-name"); tkUnitTruth = r.Q<Label>("cat-unit-truth");
            tkUnitCounter = r.Q<Label>("cat-unit-counter"); tkCatError = r.Q<Label>("cat-error"); tkLoadValue = r.Q<Label>("load-value");
            tkFailText = r.Q<Label>("fail-text"); tkNavErrorText = r.Q<Label>("fe-nav-error-text");
            tkConfirmCode = r.Q<Label>("confirm-code"); tkConfirmTitle = r.Q<Label>("confirm-title"); tkConfirmBody = r.Q<Label>("confirm-body");
            tkList = r.Q<ScrollView>("cat-scroll"); tkBrief = r.Q<ScrollView>("cat-brief");
            tkUnitZoom = r.Q<Button>("cat-unit-zoom"); tkConfirmOk = r.Q<Button>("confirm-ok");
            tkHomeHelp = r.Q<Button>("home-help"); tkTopHelp = r.Q<Button>("top-help"); tkNavHelp = r.Q<Button>("nav-help");

            // Same actions as the uGUI pages (WarSandboxFrontEnd.Toy / .cs).
            On("home-play", () => { homeOpen = false; catalogReveal = true; });
            On("home-library", OpenLibrary);
            On("home-settings", session.OpenSettings);
            On("home-help", OpenHelp30);
            On("home-quit", RequestQuit);
            On("top-home", () => homeOpen = true);
            On("top-help", OpenHelp30);
            On("top-library", OpenLibrary);
            On("top-settings", session.OpenSettings);
            On("top-quit", RequestQuit);
            On("cat-enter", EnterSelectedBattlefield);
            On("cat-unit-prev", () => SwitchPortrait(-1, tkChoices.Count));
            On("cat-unit-next", () => SwitchPortrait(1, tkChoices.Count));
            On("cat-unit-zoom", () => { if (tkChoices.Count > 0) OpenUnitPreview(tkChoices[Mathf.Clamp(portraitSelected, 0, tkChoices.Count - 1)]); });
            tkUnitImg.RegisterCallback<ClickEvent>(_ => { if (tkChoices.Count > 0) OpenUnitPreview(tkChoices[Mathf.Clamp(portraitSelected, 0, tkChoices.Count - 1)]); });
            On("fail-return", () => session.TryReturnToMenu(true, out _));
            On("fail-quit", RequestQuit);
            On("nav-help", OpenHelp30);
            On("nav-settings", session.OpenSettings);
            On("nav-menu", () => { homeOpen = false; RequestReturn(); });
            On("nav-quit", RequestQuit);
            On("confirm-ok", () => { if (confirmingQuit) Application.Quit(); else session.TryReturnToMenu(true, out _); });
            On("confirm-cancel", session.CancelConfirmation);
            Tk.HideScrollers(r);
            // Enter/Space belong to the catalog/battle shortcuts (as uGUI: sendNavigationEvents = false), never to a previously clicked button.
            r.Query<Button>().ForEach(b => b.focusable = false);
            tkBuiltEntries = null; tkShownSelected = -2; tkShownPortraitEntry = null; tkNavRect = default;
        }

        private void On(string name, Action action)
        {
            var b = tkRoot.Q<Button>(name);
            if (b != null) b.clicked += () => { action(); nextRefresh = 0; };
            else Debug.LogWarning("[FrontEnd UI Toolkit] missing button " + name);
        }

        /// <summary>Per frame while <see cref="ToolkitOwnsScreen"/>; structural work only happens when the state or selection changes.</summary>
        private void ToolkitUpdate()
        {
            if (tkDoc == null) BuildToolkit();
            if (tkDoc == null) return;
            Tk.UpdateScale();
            Tk.Show(tkRoot, true);
            var state = session.State;
            bool battle = state == WarSandboxEntryState.Battle, confirm = session.ConfirmationOpen;
            bool guide = Guide30.For(session) != null;
            Tk.Show(tkMenu, !battle);
            bool library = state == WarSandboxEntryState.Menu && libraryOpen; // the library page (Pages.uxml) replaces home/catalog
            Tk.Show(tkHome, state == WarSandboxEntryState.Menu && homeOpen && !library);
            Tk.Show(tkCatalog, state == WarSandboxEntryState.Menu && !homeOpen && !library);
            Tk.Show(tkTopbar, state == WarSandboxEntryState.Menu && !homeOpen && !library);
            Tk.Show(tkLoading, state == WarSandboxEntryState.Loading);
            Tk.Show(tkFailure, state == WarSandboxEntryState.Failed);
            Tk.Show(tkNav, battle && !confirm && !session.SettingsOpen && !session.HelpOpen && !BattleHudOwnsSystemMenu); // compact battle HUD carries the strip in its menu
            Tk.Show(tkConfirm, confirm);
            Tk.Show(tkHomeHelp, guide); Tk.Show(tkTopHelp, guide); Tk.Show(tkNavHelp, guide);

            if (state == WarSandboxEntryState.Menu && homeOpen && !library) UpdateToolkitHome();
            if (state == WarSandboxEntryState.Menu && !homeOpen && !library) UpdateToolkitCatalog();
            if (state == WarSandboxEntryState.Loading)
            {
                float p = Mathf.Clamp01(session.LoadingProgress);
                tkLoadFill.style.width = Length.Percent(p * 100f);
                Tk.SetText(tkLoadValue, (p * 100).ToString("0") + "%  ·  准备规则与军团");
            }
            if (state == WarSandboxEntryState.Failed) Tk.SetText(tkFailText, session.Error ?? "");
            if (battle && !confirm) UpdateToolkitNavigation();
            else Tk.Show(tkNavError, false);
            if (confirm) UpdateToolkitConfirmation();
            UpdateToolkitPages();
        }

        private void UpdateToolkitHome()
        {
            var t = session.catalog != null ? session.catalog.FindTemplate("roster-robot-expressive") : null;
            var tex = t != null ? t.unitPreview : null;
            var current = tkHomePortrait.style.backgroundImage.value.texture;
            if (current != tex) tkHomePortrait.style.backgroundImage = tex != null ? new StyleBackground(tex) : new StyleBackground(StyleKeyword.None);
        }

        // ---------------- catalog ----------------
        private void UpdateToolkitCatalog()
        {
            var entries = session.catalog != null ? session.catalog.entries : null;
            bool empty = entries == null || entries.Length == 0;
            Tk.Show(tkEmpty, empty);
            Tk.Show(tkDetail, !empty);
            Tk.Show(tkList.parent, !empty);
            if (empty) return;
            catalogFilter = "全部"; // The grouped list shows every section; the uGUI filter tabs do not exist here.
            if (!ReferenceEquals(entries, tkBuiltEntries)) RebuildToolkitList(entries);
            EnsureCatalogSelection();
            if (catalogSelected != tkShownSelected) ShowToolkitDetail(entries);
            if (tkShownPortraitEntry != (catalogSelected >= 0 ? entries[catalogSelected].id : null) || tkShownPortrait != portraitSelected) ShowToolkitPortrait(entries);
            if (catalogReveal)
            {
                int row = tkRowEntry.IndexOf(catalogSelected);
                if (row >= 0) { var target = tkRows[row]; tkList.schedule.Execute(() => tkList.ScrollTo(target)); }
                catalogReveal = false;
            }
            string error = session.Error;
            Tk.SetText(tkCatError, error ?? "");
            Tk.Show(tkCatError, !string.IsNullOrEmpty(error));
        }

        private void RebuildToolkitList(WarSandboxBattlefieldEntry[] entries)
        {
            tkBuiltEntries = entries; tkShownSelected = -2;
            tkList.Clear(); tkRows.Clear(); tkRowEntry.Clear();
            string group = null; int number = 0, selectable = 0;
            foreach (var e in entries) if (e != null && !e.hiddenFromSelection) selectable++;
            Tk.SetText(tkCount, "共 " + selectable + " 个战场");
            for (int i = 0; i < entries.Length; i++)
            {
                var entry = entries[i];
                if (entry == null || entry.hiddenFromSelection) continue;
                string g = CatalogGroup(entry.id);
                if (g != group)
                {
                    group = g; int count = 0;
                    foreach (var e in entries) if (e != null && !e.hiddenFromSelection && CatalogGroup(e.id) == g) count++;
                    var head = Tk.E("list-section");
                    head.Add(Tk.L(g, "list-section__t"));
                    head.Add(Tk.E("list-section__line"));
                    head.Add(Tk.L(count + " 项", "list-section__n"));
                    tkList.Add(head);
                }
                number++;
                bool parsed = TryParseDescription(entry.description, out string armies, out string troops, out string mode, out _);
                var row = Tk.E("op-row");
                row.Add(Tk.E("op-row__bar"));
                row.Add(Tk.L(number.ToString("00"), "op-row__idx", "mono"));
                var main = Tk.E("op-row__main"); main.pickingMode = PickingMode.Ignore;
                var line = Tk.E("row"); line.pickingMode = PickingMode.Ignore;
                line.Add(Tk.L(entry.displayName ?? entry.id, "op-row__name"));
                if (g == "新兵种") line.Add(Tk.L("NEW", "chip", "chip--new"));
                main.Add(line);
                var meta = Tk.L(parsed ? armies + " 军团 · " + mode : "", "op-row__meta");
                Tk.Show(meta, parsed);
                main.Add(meta);
                row.Add(main);
                row.Add(Tk.L(troops ?? "", "op-row__count", "mono"));
                int index = i;
                row.RegisterCallback<ClickEvent>(ev =>
                {
                    SelectCatalog(index);
                    if (ev.clickCount >= 2 && session.Error == null) EnterSelectedBattlefield();
                });
                tkList.Add(row); tkRows.Add(row); tkRowEntry.Add(i);
            }
            Tk.ApplyFonts(tkList);
        }

        private void ShowToolkitDetail(WarSandboxBattlefieldEntry[] entries)
        {
            tkShownSelected = catalogSelected;
            for (int r = 0; r < tkRows.Count; r++) tkRows[r].EnableInClassList("op-row--sel", tkRowEntry[r] == catalogSelected);
            if (catalogSelected < 0) { Tk.Show(tkDetail, false); return; }
            var sel = entries[catalogSelected];
            var rules = sel.rules != null ? sel.rules.rules : WarSandboxBattlefieldRules.Default;
            bool point = rules.gameMode == WarSandboxGameMode.ControlPoint;
            string terrain = rules.staticObstaclesEnabled && rules.staticObstacles != null ? rules.staticObstacles.Length + " 处障碍" : sel.terrainSurface != null ? "山地" : "开阔平原";
            bool parsed = TryParseDescription(sel.description, out string armies, out string total, out _, out string flavour);
            int ordinal = tkRowEntry.IndexOf(catalogSelected) + 1;
            string mode = point ? "据点战" : "歼灭战";
            Tk.SetText(tkCode, "第 " + ordinal + " 关 · " + mode);
            Tk.SetText(tkTitle, sel.displayName ?? sel.id);
            Tk.SetText(tkArmies, parsed ? armies : "—");
            Tk.SetText(tkTroops, parsed ? total : "—");
            Tk.SetText(tkMode, mode);
            Tk.SetText(tkTerrain, terrain);
            string text = parsed ? flavour : sel.description ?? "";
            Tk.SetText(tkDesc, text);
            Tk.Show(tkDesc, !string.IsNullOrEmpty(text));
            Tk.Show(tkWarn, parsed && long.TryParse(total.Replace(",", ""), out long troops) && troops >= 20000);
            Tk.SetText(tkBriefing, string.IsNullOrEmpty(sel.briefing) ? "在下一步布置军团。" : sel.briefing);
            var preview = sel.preview;
            tkPreview.style.backgroundImage = preview != null ? new StyleBackground(preview) : new StyleBackground(StyleKeyword.None);
            Tk.Show(tkPreviewMissing, preview == null);
            tkBrief.scrollOffset = Vector2.zero;
        }

        private void ShowToolkitPortrait(WarSandboxBattlefieldEntry[] entries)
        {
            var sel = catalogSelected >= 0 ? entries[catalogSelected] : null;
            if (sel == null) return;
            if (portraitBattlefieldId != sel.id) { portraitBattlefieldId = sel.id; portraitSelected = 0; unitPreviewOpen = false; }
            tkChoices = PreviewChoices(session.catalog, sel);
            portraitSelected = Mathf.Clamp(portraitSelected, 0, Mathf.Max(0, tkChoices.Count - 1));
            tkShownPortraitEntry = sel.id; tkShownPortrait = portraitSelected;
            tkUnits.Clear();
            for (int i = 0; i < tkChoices.Count; i++)
            {
                int k = i;
                var chip = Tk.L(tkChoices[i].config.unitTypeName, "chip", "chip--unit");
                chip.EnableInClassList("fe-chip--on", i == portraitSelected);
                chip.RegisterCallback<ClickEvent>(_ => { portraitSelected = k; nextRefresh = 0; });
                tkUnits.Add(chip);
            }
            Tk.Show(tkUnits, tkChoices.Count > 0); Tk.Show(tkUnitsHead, tkChoices.Count > 0);
            var unit = tkChoices.Count > 0 ? tkChoices[portraitSelected] : null;
            var tex = unit != null ? unit.unitPreview : null;
            tkUnitImg.style.backgroundImage = tex != null ? new StyleBackground(tex) : new StyleBackground(StyleKeyword.None);
            Tk.Show(tkUnitMissing, tex == null);
            Tk.SetText(tkUnitName, unit != null && unit.config != null ? unit.config.unitTypeName : "单体预览待配置");
            Tk.SetText(tkUnitTruth, tex != null ? "实际游戏模型 · 静态" : "真实模型图待生成");
            Tk.Show(tkUnitPager, tkChoices.Count > 1);
            Tk.SetText(tkUnitCounter, (portraitSelected + 1) + " / " + Mathf.Max(1, tkChoices.Count));
            tkUnitZoom.SetEnabled(unit != null && tex != null);
        }

        // ---------------- battle strip / confirmation ----------------
        private void UpdateToolkitNavigation()
        {
            var screen = NavigationRect(Screen.width, Screen.height);
            var r = Tk.ScreenToPanel(screen);
            if (r != tkNavRect)
            {
                tkNavRect = r;
                tkNav.style.left = r.x; tkNav.style.top = r.y; tkNav.style.width = r.width; tkNav.style.height = r.height;
                float k = r.height / NavHeight; // uGUI layout units → panel units
                tkNav.Query<Button>().ForEach(b => b.style.fontSize = Mathf.Max(10f, 14f * k));
                tkNavError.style.right = NavMargin * k; tkNavError.style.top = r.yMax + 8 * k; tkNavError.style.width = 420 * k;
            }
            // Below the compact HUD's top pills (top 12, height 40) while it carries the strip; otherwise under the strip.
            tkNavError.style.top = BattleHudOwnsSystemMenu ? 60f : tkNavRect.yMax + 8 * (tkNavRect.height / NavHeight);
            string error = session.Error;
            Tk.Show(tkNavError, !string.IsNullOrEmpty(error));
            Tk.SetText(tkNavErrorText, error ?? "");
        }

        private void UpdateToolkitConfirmation()
        {
            var plans = PlanState28.For(session.Controller);
            Tk.SetText(tkConfirmCode, confirmingQuit ? "退出" : "结束本局");
            Tk.SetText(tkConfirmTitle, confirmingQuit ? "退出游戏？" : plans != null && plans.NeedsLeaveWarning ? "离开当前战场？" : "结束本局？");
            Tk.SetText(tkConfirmBody, PlanState28.LeaveText(session.Controller, WarSandboxRuntimeDeployment.BlocksCommands(session.Controller)
                ? "未应用的布阵修改不会保留。" : "当前战斗进度不会保留。") + (session.Error == null ? "" : "\n" + session.Error));
            tkConfirmOk.text = confirmingQuit ? "退出游戏" : "结束并返回目录";
            tkConfirmOk.EnableInClassList("btn--primary", !confirmingQuit);
            tkConfirmOk.EnableInClassList("btn--danger-solid", confirmingQuit);
        }

        private void DestroyToolkit()
        {
            Tk.DestroyDocument(tkDoc);
            tkDoc = null; tkRoot = null;
        }
    }
}
