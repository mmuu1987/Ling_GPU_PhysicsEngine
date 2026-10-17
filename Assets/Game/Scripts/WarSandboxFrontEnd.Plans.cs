using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Tk = MassEngine.Game.Ui.WarSandboxUiToolkit;

namespace MassEngine.Game
{
    /// <summary>
    /// 方案库 from the battlefield catalog (2026-10-17): pick a saved plan, the game enters the plan's battlefield and loads it
    /// there (WarSandboxDeploymentHUD.TryEnterPlanBattlefield). Fixed-size modal; list rebuilt each time it opens.
    /// </summary>
    public sealed partial class WarSandboxFrontEnd
    {
        private bool menuPlansOpen;
        private string menuPlanSelected;
        private VisualElement tkPlans;
        private ScrollView tkPlansList;
        private Label tkPlansMsg;
        private readonly List<Button> tkPlanItems = new List<Button>();

        private void BuildMenuPlans(VisualElement r)
        {
            tkPlans = r.Q("fe-plans"); tkPlansList = r.Q<ScrollView>("plans-list"); tkPlansMsg = r.Q<Label>("plans-msg");
            On("top-plans", OpenMenuPlans);
            On("plans-close", () => menuPlansOpen = false);
            On("plans-load", LoadMenuPlan);
        }

        private void OpenMenuPlans()
        {
            menuPlansOpen = true; menuPlanSelected = null;
            Tk.SetText(tkPlansMsg, "");
            RefreshMenuPlans();
        }

        private void RefreshMenuPlans()
        {
            if (tkPlansList == null) return;
            tkPlansList.Clear(); tkPlanItems.Clear();
            var store = new WarSandboxLocalPlanStore();
            if (!store.TryListSlots(out var slots, out var error)) { Tk.SetText(tkPlansMsg, error); return; }
            if (slots.Length == 0)
            {
                var empty = new Label("还没有方案。在战前布阵里点「方案库」保存当前布阵。");
                empty.AddToClassList("para"); empty.AddToClassList("fe-plans__empty");
                tkPlansList.Add(empty);
                return;
            }
            foreach (var slot in slots)
            {
                string text = store.TryLoad(slot, out var plan, out _) && plan != null
                    ? plan.displayName + "\n" + WarSandboxDeploymentHUD.PlanMapName(plan.battlefieldId) + "  ·  " + slot
                    : slot + "\n文件损坏或版本不支持";
                var item = new Button { text = text, userData = slot, focusable = false };
                item.AddToClassList("btn"); item.AddToClassList("fe-plans__item");
                string s = slot;
                item.clicked += () => { menuPlanSelected = s; foreach (var b in tkPlanItems) b.EnableInClassList("btn--on", (string)b.userData == s); Tk.SetText(tkPlansMsg, ""); };
                item.RegisterCallback<ClickEvent>(e => { if (e.clickCount == 2) { menuPlanSelected = s; LoadMenuPlan(); } });
                tkPlansList.Add(item); tkPlanItems.Add(item);
            }
        }

        private void LoadMenuPlan()
        {
            if (string.IsNullOrEmpty(menuPlanSelected)) { Tk.SetText(tkPlansMsg, "请先选择一个方案。"); return; }
            if (WarSandboxDeploymentHUD.TryEnterPlanBattlefield(menuPlanSelected, new WarSandboxLocalPlanStore(), false, out var error)) menuPlansOpen = false;
            else Tk.SetText(tkPlansMsg, error);
        }

        private void UpdateMenuPlans(bool catalogShown)
        {
            if (session.State != WarSandboxEntryState.Menu || !catalogShown) menuPlansOpen = false;
            Tk.Show(tkPlans, menuPlansOpen && !session.SettingsOpen && !session.ConfirmationOpen && !session.HelpOpen);
        }

        private void MenuPlansEscape()
        {
            if (menuPlansOpen && Input.GetKeyDown(KeyCode.Escape)) { menuPlansOpen = false; nextRefresh = 0; }
        }
    }
}
