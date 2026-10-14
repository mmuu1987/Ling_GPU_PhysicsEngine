using MassEngine.Game.Ui;
using UnityEngine;
using UnityEngine.UIElements;
using Tk = MassEngine.Game.Ui.WarSandboxUiToolkit;

namespace MassEngine.Game
{
    /// <summary>
    /// Battle HUD layout redesign, phase 1 (BattleCompact.uxml): one-row top pills, a ≡ menu that also carries the front end's
    /// system strip (设置 / 战场目录 / 退出), a compact command bar with the selected-army summary, and a bottom toast. Same element
    /// names as Battle.uxml, so WarSandboxCommandHUD.Toolkit binds and refreshes both layouts; this file only adds what the old
    /// layout does not have. Every action calls the existing method; no new gameplay data. The old layout stays behind
    /// <see cref="CompactBattleHud"/> (Editor menu "Tools/战争沙盒 UI/战斗界面：紧凑布局") until accepted.
    /// </summary>
    public sealed partial class WarSandboxCommandHUD
    {
        private const string CompactPrefKey = "WarSandbox.BattleHudCompact";
        private static int compactCache = -1;
        /// <summary>true = compact battle HUD (BattleCompact.uxml); false = the previous UI Toolkit layout (Battle.uxml).</summary>
        public static bool CompactBattleHud
        {
            get { if (compactCache < 0) compactCache = PlayerPrefs.GetInt(CompactPrefKey, 1) != 0 ? 1 : 0; return compactCache == 1; }
            set { compactCache = value ? 1 : 0; PlayerPrefs.SetInt(CompactPrefKey, compactCache); PlayerPrefs.Save(); }
        }

        private bool tkDocCompact, tkMenuOpen, tkForceHover;
        private VisualElement tkMenu, tkMenuViewHead, tkForcePop, tkBottom;
        private Button tkMenuBtn;
        private Label tkSideL, tkSideR;

        private string TkLayoutName() { tkDocCompact = CompactBattleHud; return tkDocCompact ? "BattleCompact" : "Battle"; }

        /// <summary>Start of BattleToolkitUpdate: the layout switch was flipped while the HUD exists, so rebuild it from scratch.</summary>
        private void TkCompactCheckLayout()
        {
            if (tkDoc == null || tkDocCompact == CompactBattleHud) return;
            DestroyBattleToolkit();
            tkTimerCache = null; tkArmyKey = ""; tkBarKey = ""; tkResultBuilt = false;
            tkArmyTeams.Clear(); tkArmyCards.Clear(); tkBarSegs.Clear(); tkBarLabelList.Clear(); tkBarTeams.Clear();
            nextUiRefresh = 0;
        }

        /// <summary>End of BuildBattleToolkit (before buttons are made non-focusable).</summary>
        private void TkCompactBind(VisualElement r)
        {
            tkMenuOpen = false; tkForceHover = false;
            tkMenu = tkMenuViewHead = tkForcePop = tkBottom = null; tkMenuBtn = null; tkSideL = tkSideR = null;
            if (!tkDocCompact) return;
            tkMenu = r.Q("bh-menu"); tkMenuViewHead = r.Q("bh-menu-view-head"); tkForcePop = r.Q("bh-force-pop"); tkBottom = r.Q("bh-bottom");
            tkSideL = r.Q<Label>("bh-side-l"); tkSideR = r.Q<Label>("bh-side-r");
            tkMenuBtn = r.Q<Button>("bh-menu-btn");
            if (tkMenuBtn != null) tkMenuBtn.clicked += () => { tkMenuOpen = !tkMenuOpen; TkCompactMenuVisual(); };
            // System strip entries (same calls as the front end's 设置 / 战场目录 / 退出).
            TkOn("bh-sys-settings", WarSandboxFrontEnd.BattleOpenSettings);
            TkOn("bh-sys-catalog", WarSandboxFrontEnd.BattleReturnToCatalog);
            TkOn("bh-sys-quit", WarSandboxFrontEnd.BattleQuit);
            // Any menu item closes the menu (the item's own action runs first).
            tkMenu?.Query<Button>().ForEach(b => b.clicked += () => { tkMenuOpen = false; TkCompactMenuVisual(); });
            // Pointer down anywhere on the HUD outside the menu and its button closes it; world clicks are handled per frame.
            r.RegisterCallback<PointerDownEvent>(e =>
            {
                if (!tkMenuOpen) return;
                var t = e.target as VisualElement;
                if (TkInside(t, tkMenu) || TkInside(t, tkMenuBtn)) return;
                tkMenuOpen = false; TkCompactMenuVisual();
            }, TrickleDown.TrickleDown);
            var timer = r.Q("bh-timer");
            if (timer != null)
            {
                timer.RegisterCallback<PointerEnterEvent>(_e => { tkForceHover = true; TkCompactForcePopVisual(); });
                timer.RegisterCallback<PointerLeaveEvent>(_e => { tkForceHover = false; TkCompactForcePopVisual(); });
            }
            Tk.Show(tkMenu, false); Tk.Show(tkForcePop, false);
        }

        private static bool TkInside(VisualElement e, VisualElement parent)
        {
            if (parent == null) return false;
            for (; e != null; e = e.parent) if (e == parent) return true;
            return false;
        }

        /// <summary>Every frame while the HUD is shown (right after the root is made visible).</summary>
        private void TkCompactFrame()
        {
            if (!tkDocCompact) { WarSandboxFrontEnd.BattleHudOwnsSystemMenu = false; return; }
            bool result = controller != null && controller.BattleResult.valid;
            // The front end strip hides while our right pill carries its entries (not on the result screen, which has its own exits).
            WarSandboxFrontEnd.BattleHudOwnsSystemMenu = !result;
            if (result && tkMenuOpen) tkMenuOpen = false;
            // A click on the battlefield (not on any UI document) closes the menu.
            if (tkMenuOpen && (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1)) && !Tk.PointerOverUI()) tkMenuOpen = false;
            TkCompactMenuVisual();
            // Bottom stack keeps clear of the minimap.
            bool mini = tkMini != null && tkMini.style.display != DisplayStyle.None;
            tkBottom?.EnableInClassList("bc-bottom--mini", mini);
        }

        private void TkCompactMenuVisual()
        {
            Tk.Show(tkMenu, tkMenuOpen);
            tkMenuBtn?.EnableInClassList("bc-menu-btn--on", tkMenuOpen);
            // One popup at a time: the help/diagnostics card waits while the menu is open.
            if (tkMenuOpen) Tk.Show(tkDetails, false);
        }

        private void TkCompactForcePopVisual() => Tk.Show(tkForcePop, tkDocCompact && tkForceHover && tkBarTeams.Count > 2);

        /// <summary>End of each throttled refresh (also on the result screen), after the shared code has updated everything.</summary>
        private void TkCompactRefresh(bool result)
        {
            if (!tkDocCompact) return;
            if (result) { tkMenuOpen = false; TkCompactMenuVisual(); return; }
            bool setup = controller.Phase == WarSandboxBattlePhase.Setup;
            Tk.Show(tkMenuViewHead, setup);
            // Shorter member-selection line: status only; the full instructions are in 操作帮助.
            if (selectionPreviewEnabled)
                Tk.SetText(tkP7State, SelectionStatusP7() + (playerScopeEnabled ? " · Esc 取消选点 / 清选区" : " · 玩家下令入口禁用"));
            // Top pill sides: two armies by name; more than two as a count plus total, per-army counts on hover.
            int n = tkBarTeams.Count;
            if (n <= 2)
            {
                Color? c0 = null, c1 = null;
                if (n > 0) { Color c = WarSandboxTeamPalette.Resolve(tkBarTeams[0]); c0 = c; }
                if (n > 1) { Color c = WarSandboxTeamPalette.Resolve(tkBarTeams[1]); c1 = c; }
                TkSide(tkSideL, n > 0 ? tkBarLabelList[0].text : "", c0);
                TkSide(tkSideR, n > 1 ? tkBarLabelList[1].text : "", c1);
            }
            else
            {
                int total = 0;
                for (int i = 0; i < n; i++) total += Mathf.Max(0, controller.GetAliveUnitCount(tkBarTeams[i]));
                TkSide(tkSideL, n + " 个军团 · 悬停查看", null);
                TkSide(tkSideR, "总兵力 " + total.ToString("N0"), null);
            }
            TkCompactForcePopVisual();
            if (tkMenuOpen) Tk.Show(tkDetails, false);
        }

        private static void TkSide(Label l, string text, Color? color)
        {
            if (l == null) return;
            Tk.SetText(l, text);
            if (color.HasValue) l.style.color = color.Value; else l.style.color = StyleKeyword.Null;
        }

        /// <summary>HideBattleToolkit / DestroyBattleToolkit: give the system strip back to the front end.</summary>
        private void TkCompactHidden()
        {
            tkMenuOpen = false;
            WarSandboxFrontEnd.BattleHudOwnsSystemMenu = false;
        }
    }
}
