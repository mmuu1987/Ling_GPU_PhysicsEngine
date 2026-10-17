using System;
using System.Collections.Generic;
using MassEngine.Game.Ui;
using UnityEngine;
using UnityEngine.UIElements;
using Tk = MassEngine.Game.Ui.WarSandboxUiToolkit;

namespace MassEngine.Game
{
    /// <summary>Phase 5 (continued): legion page, template picker, unit dossier (+ push), local plans. See .Toolkit.cs.</summary>
    public sealed partial class WarSandboxDeploymentHUD
    {
        // =====================================================================================================
        // Legion page — DrawLegion
        // =====================================================================================================
        private Label lgName, lgTotal, lgComposition, lgUnitName, lgRole, lgStatus;
        private Button lgRemove, lgRemoveArmy, lgFix;
        private VisualElement lgDossierBody;
        private ScrollView lgRoster;
        private readonly List<Label[]> lgStats = new List<Label[]>();
        private readonly List<TkRow> lgRows = new List<TkRow>();
        private string lgKey;

        private void BuildLegionPage(VisualElement r)
        {
            lgName = r.Q<Label>("lg-name"); lgTotal = r.Q<Label>("lg-total"); lgComposition = r.Q<Label>("lg-composition");
            lgUnitName = r.Q<Label>("lg-unit-name"); lgRole = r.Q<Label>("lg-role"); lgStatus = r.Q<Label>("lg-status");
            lgRoster = r.Q<ScrollView>("lg-roster"); lgDossierBody = r.Q("lg-dossier");
            dpLgPv = new TkUnitPreview(); r.Q("lg-preview").Add(dpLgPv);
            BuildStatCells(r.Q("lg-stats"), lgStats);
            DpOn("lg-back", ReturnFromLegion);
            DpOn("lg-add", () => AddFormation(false));
            lgRemove = DpOn("lg-remove", () => { deployment.Draft.Remove(selected); ClampSelection(); RefreshAfterUiChange(); });
            lgRemoveArmy = DpOn("lg-remove-army", () => removeArmyConfirm = true);
            DpBind("lg-count", () => count, v => { count = v; inputError = null; nextUiRefresh = 0; });
            DpOn("lg-update", () => { if (CommitFields()) RefreshAfterUiChange(); });
            DpOn("lg-template", () => { if (CommitFields()) { pickerRole = 0; pickerOpen = true; } });
            DpOn("lg-stats-open", OpenStats);
            DpOn("lg-army", () => { if (CommitFields()) armyMenu = true; });
            lgFix = DpOn("lg-fix", () => Locate32(tkFixRaw, tkFixIndex, tkFixProblem));
            DpOn("lg-done", ReturnFromLegion);
        }

        private void UpdateLegionPage()
        {
            var draft = deployment.Draft;
            int team = draft.Count > 0 ? draft[selected].teamId : 0, total = 0, melee = 0, ranged = 0, formations = 0;
            string key = team + ":";
            for (int i = 0; i < draft.Count; i++) if (draft[i].teamId == team)
            { total += draft[i].count; formations++; key += i + ","; if (WarSandboxUnitStats.IsRanged(draft[i].template)) ranged += draft[i].count; else melee += draft[i].count; }
            Tk.SetText(lgName, WarSandboxBattleController.DefaultArmyName(team));
            Tk.SetText(lgTotal, total.ToString("N0") + " 人");
            Tk.SetText(lgComposition, formations + " 个编成     /     " + melee.ToString("N0") + " 近战单位     /     " + ranged.ToString("N0") + " 远程单位");
            if (key != lgKey)
            {
                lgKey = key; lgRoster.Clear(); lgRows.Clear();
                for (int i = 0; i < draft.Count; i++)
                {
                    if (draft[i].teamId != team) continue;
                    int index = i;
                    var row = new TkRow { index = i, root = Tk.E("lg-row"), pic = Tk.E("lg-row__pic") };
                    var main = Tk.E("lg-row__main");
                    row.name = Tk.L("", "lg-row__name"); row.role = Tk.L("", "lg-row__role"); row.count = Tk.L("", "lg-row__count");
                    main.Add(row.name); main.Add(row.role);
                    row.root.Add(row.pic); row.root.Add(main); row.root.Add(row.count);
                    row.root.RegisterCallback<ClickEvent>(_ => { if (deployment == null) return; SelectFormation(index); nextUiRefresh = 0; });
                    lgRoster.Add(row.root); lgRows.Add(row);
                }
            }
            foreach (var row in lgRows)
            {
                var e = draft[row.index]; bool active = row.index == selected; var portrait = PortraitEntry(e.template);
                DpImage(row.pic, portrait != null ? portrait.unitPreview : null);
                Tk.SetText(row.name, e.Name);
                Tk.SetText(row.role, (Info31 && active ? "正在编辑 · " : "") + "编成 " + (row.index + 1).ToString("00") + " · " + (WarSandboxUnitStats.IsRanged(e.template) ? "远程" : "近战"));
                Tk.SetText(row.count, e.count.ToString("N0") + " 人");
                row.root.EnableInClassList("lg-row--on", active);
            }
            lgRemove.SetEnabled(draft.Count > 0); lgRemoveArmy.SetEnabled(draft.Count > 0);
            bool has = draft.Count > 0;
            foreach (var child in lgDossierBody.Children()) Tk.Show(child, has);
            if (has)
            {
                var e = draft[selected]; var portrait = PortraitEntry(e.template);
                Tk.SetText(lgUnitName, e.Name);
                dpLgPv.Bind(e.template, portrait != null ? portrait.unitPreview : null, null);
                bool isRanged = WarSandboxUnitStats.IsRanged(e.template);
                string role = isRanged ? "远程单位\n从距离外提供火力支援。" : "近战单位\n接近敌方后进行近身交战。";
                if (Info31) role = isRanged ? "远程单位 · 火力支援" : "近战单位 · 近身交战";
                Tk.SetText(lgRole, role + (Info31 ? "\n\n下方为草稿配置值\n应用后生效 · 默认=官方\n同兵种共用本局覆盖" : "\n\n实际兵种模型 · 3D预览"));
                UpdateStatCells(lgStats, e.template);
            }
            else dpLgPv.Deactivate();
            ShowStatus(lgStatus, lgFix);
        }

        // =====================================================================================================
        // Template picker — DrawToyPicker
        // =====================================================================================================
        private Label pkTitle, pkScope, pkNote, pkEmpty;
        private readonly Button[] pkFilters = new Button[3];
        private VisualElement pkCards;
        private sealed class PkCard { public UnitTypeConfig template; public VisualElement root, pic; public Label missing, role, stats, extra, sources; }
        private readonly List<PkCard> pkCardList = new List<PkCard>();
        private string pkKey;

        private void BuildPickerPage(VisualElement r)
        {
            pkTitle = r.Q<Label>("pk-title"); pkScope = r.Q<Label>("pk-scope"); pkNote = r.Q<Label>("pk-note"); pkEmpty = r.Q<Label>("pk-empty");
            pkCards = r.Q("pk-cards");
            DpOn("pk-back", () => pickerOpen = false);
            for (int f = 0; f < 3; f++) { int filter = f; pkFilters[f] = DpOn("pk-f" + f, () => pickerRole = filter); }
        }

        private void UpdatePickerPage()
        {
            var draft = deployment.Draft; var choices = deployment.ChoiceTemplates;
            Tk.SetText(pkTitle, Info31 ? WarSandboxBattleController.DefaultArmyName(draft[selected].teamId) + " · 编成 " + (selected + 1).ToString("00") + "：更换兵种" : "为当前编成选择兵种");
            Tk.SetText(pkScope, deployment.rosterPolicy != null ? deployment.rosterPolicy.explanation : "只列出当前战场允许使用的兵种；更换后请检查人数与阵型。");
            int meleeCount = 0, rangedCount = 0;
            foreach (var choice in choices) { if (WarSandboxUnitStats.IsRanged(choice)) rangedCount++; else meleeCount++; }
            string[] filters = { "全部  " + (meleeCount + rangedCount), "近战  " + meleeCount, "远程  " + rangedCount };
            for (int f = 0; f < 3; f++) { pkFilters[f].text = filters[f]; DpOnState(pkFilters[f], pickerRole == f); }
            Tk.SetText(pkNote, Info31 ? "点击即更换 · 数值按草稿计算，应用后生效；更换后核对人数" : "点击卡片更换 · 属性为本局有效值");
            int visibleCount = pickerRole == 1 ? meleeCount : pickerRole == 2 ? rangedCount : meleeCount + rangedCount;
            Tk.Show(pkEmpty, visibleCount == 0);

            string key = pickerRole + "|" + Info31 + "|";
            foreach (var t in choices) key += (t != null ? t.GetInstanceID() : 0) + ",";
            if (key != pkKey)
            {
                pkKey = key; pkCards.Clear(); pkCardList.Clear();
                foreach (var template in choices)
                {
                    bool ranged = WarSandboxUnitStats.IsRanged(template);
                    if ((pickerRole == 1 && ranged) || (pickerRole == 2 && !ranged)) continue;
                    var card = new PkCard { template = template, root = Tk.E("pk-card"), pic = Tk.E("pk-card__pic") };
                    card.missing = Tk.L("真实预览待配置", "pk-card__missing"); card.pic.Add(card.missing);
                    card.root.Add(card.pic);
                    card.root.Add(Tk.L(template.unitTypeName, "pk-card__name"));
                    card.role = Tk.L("", "pk-card__line"); card.stats = Tk.L("", "pk-card__line");
                    card.extra = Tk.L("", "pk-card__line", "pk-card__line--ink"); card.sources = Tk.L("", "pk-card__line");
                    card.root.Add(card.role); card.root.Add(card.stats); card.root.Add(card.extra); card.root.Add(card.sources);
                    var chosen = template;
                    card.root.RegisterCallback<ClickEvent>(_ =>
                    {
                        if (deployment == null || !CommitFields()) return;
                        deployment.SelectTemplate(selected, chosen); pickerOpen = false; RefreshAfterUiChange(); nextUiRefresh = 0;
                    });
                    pkCards.Add(card.root); pkCardList.Add(card);
                }
            }
            var current = draft[selected].template;
            foreach (var card in pkCardList)
            {
                var template = card.template; bool ranged = WarSandboxUnitStats.IsRanged(template); var entry = PortraitEntry(template);
                var texture = entry != null ? entry.unitPreview : null;
                DpImage(card.pic, texture); Tk.Show(card.missing, texture == null);
                card.root.EnableInClassList("pk-card--on", template == current);
                Tk.SetText(card.role, (template == current ? "当前兵种 · " : "") + (ranged ? "远程" : "近战"));
                var hp = deployment.DraftStats.Resolve(template, WarSandboxUnitStats.Get(WarSandboxUnitStat.MaxHp));
                var damage = deployment.DraftStats.Resolve(template, WarSandboxUnitStats.Get(WarSandboxUnitStat.AttackDamage));
                Tk.SetText(card.stats, "生命 " + hp.effective.ToString("0.#") + "  ·  攻击 " + damage.effective.ToString("0.#"));
                Tk.Show(card.extra, Info31); Tk.Show(card.sources, Info31);
                if (Info31)
                {
                    Tk.SetText(card.extra, "间隔 " + RosterInfo31.Effective(deployment, template, WarSandboxUnitStat.AttackInterval) + " · 攻击距离 " + RosterInfo31.Effective(deployment, template, WarSandboxUnitStat.AttackRange));
                    Tk.SetText(card.sources, RosterInfo31.Sources(deployment, template));
                }
            }
        }

        // =====================================================================================================
        // Unit dossier — DrawUGUIStats / DrawStatRow / DrawUGUIPush
        // =====================================================================================================
        private Label stName, stId, stListTitle, stRoBody, stFootNote, pushTitle, pushNote;
        private VisualElement stCustom, stRowsHost, stPreviewHost, stPushModal;
        private ScrollView stList, stTable, pushRows;
        private Button stReset, stUndoGlobal, stPush, pushClear;
        private string stListKey, stRowsKey, pushKey;
        private readonly List<KeyValuePair<UnitTypeConfig, VisualElement>> stItems = new List<KeyValuePair<UnitTypeConfig, VisualElement>>();
        private sealed class StRow { public WarSandboxStatDefinition def; public VisualElement root, tick; public Label name, official, chip; public TacSlider slider; public TextField field; public Button clear; public float lo, hi; public bool log, focused; }
        private readonly List<StRow> stRows = new List<StRow>();

        private void BuildStatsPage(VisualElement r)
        {
            stName = r.Q<Label>("st-name"); stId = r.Q<Label>("st-id"); stListTitle = r.Q<Label>("st-list-title"); stRoBody = r.Q<Label>("st-ro-body");
            stFootNote = r.Q<Label>("st-foot-note"); pushTitle = r.Q<Label>("push-title"); pushNote = r.Q<Label>("push-note");
            stCustom = r.Q("st-custom"); stRowsHost = r.Q("st-rows"); stPreviewHost = r.Q("st-preview"); stPushModal = r.Q("st-push-modal");
            stList = r.Q<ScrollView>("st-list"); stTable = r.Q<ScrollView>("st-table"); pushRows = r.Q<ScrollView>("push-rows");
            dpStPv = new TkUnitPreview(); stPreviewHost.Add(dpStPv);
            stReset = DpOn("st-reset", () =>
            {
                var template = statsTemplate; if (template == null) return;
                if (deployment.Draft.ClearTemplateStats(template)) Notice("已清除本局对 " + template.unitTypeName + " 的修改。", false);
            });
            stUndoGlobal = DpOn("st-undo-global", () => { if (deployment.TryUndoGlobalChange(out string error)) Notice("已撤销上次全局修改。", false); else Notice(error, true); });
            stPush = DpOn("st-push", () => { pushConfirm = true; pushClearLocal = true; clearFocusRequested = true; });
            DpOn("st-done", CloseStats);
            pushClear = DpOn("push-clear-local", () => pushClearLocal = !pushClearLocal);
            DpOn("push-cancel", () => pushConfirm = false);
            DpOn("push-confirm", () =>
            {
                var template = statsTemplate; if (template == null) return;
                int n = deployment.Draft.Stats.Get(template).Count; string templateId = deployment.DraftStats.TemplateId(template);
                pushConfirm = false;
                if (deployment.TryPushStats(template, pushClearLocal, out string error))
                    Notice("已推送 " + n + " 项到全局（" + templateId + "）。下次应用布阵或进入战场时生效。", false);
                else Notice(error, true);
            });
        }

        /// <summary>Returns false (after CloseStats) when the draft has no templates, like DrawUGUIStats.</summary>
        private bool UpdateStatsPage()
        {
            var templatesInDraft = DraftTemplates();
            if (templatesInDraft.Count == 0) { CloseStats(); return false; }
            if (!templatesInDraft.Contains(statsTemplate)) statsTemplate = templatesInDraft[0];
            var template = statsTemplate;
            var resolver = deployment.DraftStats;
            string templateId = resolver.TemplateId(template);
            bool ranged = WarSandboxUnitStats.IsRanged(template);

            Tk.SetText(stName, template.unitTypeName);
            Tk.SetText(stId, (templateId ?? "未注册模板编号") + "   ·   " + (ranged ? "远程单位" : "近战单位") + "   ·   本方案内同一兵种共用一份数值");
            Tk.Show(stCustom, resolver.HasCustom(template));

            // ----- left: templates used by this plan + read-only parameters + radius preview -----
            Tk.SetText(stListTitle, "本方案兵种  " + templatesInDraft.Count);
            string key = "";
            foreach (var t in templatesInDraft) key += t.GetInstanceID() + ",";
            if (key != stListKey)
            {
                stListKey = key; stList.Clear(); stItems.Clear();
                foreach (var t in templatesInDraft)
                {
                    var chosen = t;
                    var item = Tk.E("st-item"); item.Add(Tk.L("", "st-item__name"));
                    item.RegisterCallback<ClickEvent>(_ => { if (deployment == null) return; statsTemplate = chosen; pushConfirm = false; statsNotice = null; clearFocusRequested = true; nextUiRefresh = 0; });
                    stList.Add(item); stItems.Add(new KeyValuePair<UnitTypeConfig, VisualElement>(t, item));
                }
            }
            foreach (var pair in stItems)
            {
                pair.Value.EnableInClassList("st-item--on", pair.Key == template);
                Tk.SetText(pair.Value.Q<Label>(), pair.Key.unitTypeName + (resolver.HasCustom(pair.Key) ? "   ·  自定义" : ""));
            }
            Tk.SetText(stRoBody, WarSandboxStatTheme.ReadOnlySummary(template));
            var radiusValue = resolver.Resolve(template, WarSandboxUnitStats.Get(WarSandboxUnitStat.AgentRadius));
            Tk.Show(stPreviewHost, radiusValue.applies);
            if (radiusValue.applies) dpStPv.Bind(template, null, radiusValue.effective); else dpStPv.Deactivate();

            // ----- right: field table (flocking first, grouped) -----
            var rows = new List<WarSandboxStatDefinition>();
            foreach (var d in WarSandboxUnitStats.Definitions) if (WarSandboxUnitStats.Applies(template, d)) { if (d.UsesFlocking) rows.Insert(0, d); else rows.Add(d); }
            string rowsKey = template.GetInstanceID() + ":" + rows.Count;
            if (rowsKey != stRowsKey)
            {
                stRowsKey = rowsKey; stRowsHost.Clear(); stRows.Clear();
                string group = null; int n = 0;
                foreach (var d in rows)
                {
                    if (d.Group != group)
                    {
                        group = d.Group;
                        var head = Tk.E("st-group-row");
                        head.Add(Tk.L(group, "lib-group"));
                        head.Add(Tk.L(d.Role == WarSandboxStatRole.MeleeOnly ? "仅近战单位" : d.Role == WarSandboxStatRole.RangedOnly ? "仅远程单位" : "", "st-group-note"));
                        stRowsHost.Add(head);
                    }
                    var row = BuildStatsRow(d);
                    row.root.EnableInClassList("lib-row--alt", n++ % 2 == 0);
                    stRowsHost.Add(row.root); stRows.Add(row);
                }
                stTable.scrollOffset = Vector2.zero;
            }
            foreach (var row in stRows) UpdateStatsRow(row, template, resolver);

            // ----- footer -----
            var local = deployment.Draft.Stats.Get(template);
            string warning = WarSandboxStatTheme.RangeWarning(template, s => resolver.Resolve(template, WarSandboxUnitStats.Get(s)));
            string storeWarning = deployment.StatStore.Warning;
            Tk.SetText(stFootNote, statsNotice ?? storeWarning ?? warning ?? "修改保存在本局方案：应用布阵后生效，保存方案时一并写入。推送到全局后，所有战场的此兵种都会使用新数值。");
            stFootNote.EnableInClassList("lib-foot__note--ok", statsNotice != null && !statsNoticeError);
            stFootNote.EnableInClassList("lib-foot__note--bad", (statsNotice != null && statsNoticeError) || (statsNotice == null && storeWarning != null));
            stFootNote.EnableInClassList("st-foot__note--warn", statsNotice == null && storeWarning == null && warning != null);
            stReset.SetEnabled(local.Count > 0);
            stUndoGlobal.SetEnabled(deployment.StatStore.CanUndo);
            stPush.text = "推送到全局…  " + local.Count + " 项";
            stPush.SetEnabled(local.Count > 0 && templateId != null);

            Tk.Show(stPushModal, pushConfirm);
            if (pushConfirm) UpdatePushModal(template, templateId, resolver, local);
            return true;
        }

        private StRow BuildStatsRow(WarSandboxStatDefinition d)
        {
            var row = new StRow { def = d, root = Tk.E("lib-row") };
            row.name = Tk.L("", "lib-row__name");
            var wrap = Tk.E("lib-row__slider");
            row.slider = new TacSlider { low = 0, high = 1 };
            row.tick = Tk.E("lib-row__tick"); row.tick.pickingMode = PickingMode.Ignore;
            row.official = Tk.L("", "lib-row__official"); row.official.pickingMode = PickingMode.Ignore;
            wrap.Add(row.slider); wrap.Add(row.tick); wrap.Add(row.official);
            row.field = new TextField { isDelayed = true };
            row.field.AddToClassList("lib-field");
            row.chip = Tk.L("", "chip", "lib-chip");
            row.clear = new Button { text = "×", focusable = false };
            row.clear.AddToClassList("btn"); row.clear.AddToClassList("btn--sm"); row.clear.AddToClassList("lib-row__clear");
            row.root.Add(row.name); row.root.Add(wrap); row.root.Add(row.field); row.root.Add(row.chip); row.root.Add(row.clear);

            // Same edits as DrawStatRow.
            row.slider.changed += t =>
            {
                var template = statsTemplate; if (deployment == null || template == null) return;
                float next = d.Step(WarSandboxStatTheme.FromSlider(t, row.lo, row.hi, row.log));
                if (deployment.SetStat(template, d.Stat, next)) { statsNotice = null; nextUiRefresh = 0; }
            };
            row.field.RegisterCallback<FocusInEvent>(_ => row.focused = true);
            row.field.RegisterCallback<FocusOutEvent>(_ => row.focused = false);
            row.field.RegisterValueChangedCallback(e => CommitStatsField(row, e.newValue));
            row.clear.clicked += () =>
            {
                var template = statsTemplate; if (deployment == null || template == null) return;
                var value = deployment.DraftStats.Resolve(template, d);
                if (deployment.Draft.ClearStat(template, d.Stat)) Notice(d.Label + " 已恢复为" + (value.hasGlobal ? "全局" : "官方") + "数值。", false);
            };
            return row;
        }

        private void CommitStatsField(StRow row, string text)
        {
            var template = statsTemplate; if (deployment == null || template == null) return;
            var d = row.def; var value = deployment.DraftStats.Resolve(template, d);
            if (!WarSandboxStatTheme.TryParse(text, out float typed)) { Notice(d.Label + "：请输入有限数字。", true); return; }
            if (d.UsesFlocking && !d.Accepts(typed)) { Notice("半径必须为 0.05–4.8 米；应用时还需通过网格、间距和地形校验。", true); return; }
            float clamped = d.Clamp(typed);
            // Focus leaving an unchanged field must not create a local override.
            if (WarSandboxUnitStats.Same(clamped, value.effective) && clamped == typed) return;
            deployment.SetStat(template, d.Stat, clamped);
            Notice(clamped != typed ? d.Label + " 已限制在 " + d.Format(d.Min) + " – " + d.Format(d.Max) + "。" : null, clamped != typed);
        }

        private static string SourceClass(WarSandboxStatSource s) => s == WarSandboxStatSource.Local ? "local" : s == WarSandboxStatSource.Global ? "global" : "official";

        private void UpdateStatsRow(StRow row, UnitTypeConfig template, WarSandboxStatResolver resolver)
        {
            var d = row.def; var value = resolver.Resolve(template, d);
            row.root.EnableInClassList("lib-row--custom", value.Customized);
            Tk.SetText(row.name, d.Label + (string.IsNullOrEmpty(d.Unit) ? "" : "  " + d.Unit));
            if (!row.slider.HasPointerCapture(PointerId.mousePointerId))
            {
                WarSandboxStatTheme.SliderWindow(d, value.official, value.effective, out row.lo, out row.hi, out row.log);
                float v = WarSandboxStatTheme.ToSlider(value.effective, row.lo, row.hi, row.log);
                if (!Mathf.Approximately(row.slider.value, v)) row.slider.SetValueWithoutNotify(v);
            }
            string source = SourceClass(value.source);
            row.slider.EnableInClassList("dp-slider--local", source == "local");
            row.slider.EnableInClassList("lib-slider--global", source == "global");
            row.slider.EnableInClassList("lib-slider--official", source == "official");
            row.tick.style.left = Length.Percent(WarSandboxStatTheme.ToSlider(value.official, row.lo, row.hi, row.log) * 100f);
            Tk.SetText(row.official, value.Customized ? "官方 " + d.Format(value.official) : "");
            row.official.EnableInClassList("lib-row__official--right", value.official > row.lo + (row.hi - row.lo) * 0.5f);
            if (!row.focused) { string text = d.Format(value.effective); if (row.field.value != text) row.field.SetValueWithoutNotify(text); }
            row.field.EnableInClassList("lib-field--custom", value.Customized);
            Tk.SetText(row.chip, WarSandboxStatTheme.SourceName(value.source));
            row.chip.EnableInClassList("dp-chip--local", source == "local");
            row.chip.EnableInClassList("lib-chip--global", source == "global");
            row.chip.EnableInClassList("lib-chip--official", source == "official");
            row.clear.SetEnabled(value.hasLocal);
        }

        private void UpdatePushModal(UnitTypeConfig template, string templateId, WarSandboxStatResolver resolver, WarSandboxStatSet local)
        {
            int n = local.Count;
            Tk.SetText(pushTitle, template.unitTypeName + "  ·  " + n + " 项修改");
            Tk.SetText(pushNote, "按模板编号 " + templateId + " 生效：所有战场与方案中使用此兵种、且未单独覆盖的字段都会改变。");
            string key = template.GetInstanceID() + "|";
            foreach (var pair in local.Values) key += pair.Key + "=" + pair.Value + ";";
            if (key != pushKey)
            {
                pushKey = key; pushRows.Clear();
                int i = 0;
                foreach (var pair in local.Values)
                {
                    var d = WarSandboxUnitStats.Get(pair.Key); var value = resolver.Resolve(template, d);
                    string before = value.hasGlobal ? d.Format(value.global) + "  全局" : d.Format(value.official) + "  官方";
                    var row = Tk.E("st-prow"); row.EnableInClassList("st-prow--alt", i++ % 2 == 0);
                    row.Add(Tk.L(d.Label, "st-prow__k"));
                    var b = Tk.L(before, "st-prow__b"); b.EnableInClassList("st-prow__b--global", value.hasGlobal); row.Add(b);
                    row.Add(Tk.L("→", "st-prow__arrow"));
                    row.Add(Tk.L(d.Format(pair.Value), "st-prow__a"));
                    pushRows.Add(row);
                }
            }
            pushClear.text = (pushClearLocal ? "■  " : "□  ") + "推送后清除本局中这些字段（推荐：之后跟随全局数值）";
            DpOnState(pushClear, pushClearLocal);
        }

        // =====================================================================================================
        // Local plans — DrawUGUIPlans
        // =====================================================================================================
        private VisualElement plConfirm, plMain;
        private Label plConfirmTitle, plConfirmNote, plSaveHeading, plSaveBlocked, plListHeading, plEmpty, plMessage;
        private Button plConfirmYes, plSave, plLoad;
        private ScrollView plList;
        private string plKey;
        private readonly List<VisualElement> plItems = new List<VisualElement>();

        private void BuildPlansPage(VisualElement r)
        {
            plConfirm = r.Q("pl-confirm"); plMain = r.Q("pl-main");
            plConfirmTitle = r.Q<Label>("pl-confirm-title"); plConfirmNote = r.Q<Label>("pl-confirm-note"); plSaveHeading = r.Q<Label>("pl-save-heading");
            plSaveBlocked = r.Q<Label>("pl-save-blocked"); plListHeading = r.Q<Label>("pl-list-heading"); plEmpty = r.Q<Label>("pl-empty"); plMessage = r.Q<Label>("pl-message");
            plList = r.Q<ScrollView>("pl-list");
            DpOn("pl-refresh", RefreshPlans);
            DpOn("pl-back", () => { plansOpen = false; pendingPlanAction = null; clearFocusRequested = true; });
            plConfirmYes = DpOn("pl-confirm-yes", ExecutePlanAction);
            DpOn("pl-confirm-no", () => pendingPlanAction = null);
            DpBind("pl-slot", () => planSlot, v => planSlot = v, WarSandboxLocalPlanStore.MaxSlotLength);
            DpBind("pl-name", () => planName, v => planName = v, 80);
            plSave = DpOn("pl-save", () =>
            {
                if (!CommitFields()) { planMessage = inputError; return; }
                if (deployment.PlanStore.Exists(planSlot)) { pendingPlanAction = "save"; pendingPlanSlot = planSlot; pendingPlanName = planName; }
                else SavePlan(planSlot, planName, false);
                clearFocusRequested = true;
            });
            plLoad = DpOn("pl-load", () => { pendingPlanAction = "load"; pendingPlanSlot = selectedPlanSlot; clearFocusRequested = true; });
        }

        private void UpdatePlansPage()
        {
            bool confirm = pendingPlanAction != null;
            Tk.Show(plConfirm, confirm); Tk.Show(plMain, !confirm);
            if (confirm)
            {
                bool save = pendingPlanAction == "save";
                Tk.SetText(plConfirmTitle, (save ? "覆盖方案 " : "载入方案 ") + pendingPlanSlot + "？");
                Tk.SetText(plConfirmNote, save ? "旧文件将被替换。写入失败时会保留旧方案。" : PlanLoadNote());
                plConfirmYes.text = save ? "确认覆盖" : "确认载入";
            }
            else
            {
                Tk.SetText(plSaveHeading, PlanState28.For(deployment.controller) != null ? "保存当前草稿（不会自动应用到本局）" : "保存当前布阵");
                // Deployment B: an invalid draft cannot be saved (the player fixes the red formations first).
                bool saveValid = deployment.TryValidate(out string saveProblem) || HasPendingInputs();
                Tk.Show(plSaveBlocked, !saveValid);
                if (!saveValid) Tk.SetText(plSaveBlocked, "布阵有问题，修正后才能保存：" + DeploymentCheck32.Problem(saveProblem, deployment.Draft));
                plSave.text = !saveValid ? "布阵有问题，不能保存" : deployment.PlanStore.Exists(planSlot) ? "覆盖方案" : "另存新方案";
                plSave.SetEnabled(saveValid);
                Tk.SetText(plListHeading, "已保存方案  /  " + planSlots.Length);
                Tk.Show(plEmpty, planSlots.Length == 0);
                string key = string.Join("\n", planLabels);
                if (key != plKey || plItems.Count != planSlots.Length)
                {
                    plKey = key;
                    foreach (var item in plItems) item.RemoveFromHierarchy();
                    plItems.Clear();
                    for (int i = 0; i < planSlots.Length; i++)
                    {
                        int index = i;
                        string display = planLabels[i]; int suffix = display.LastIndexOf("  /  ", StringComparison.Ordinal);
                        if (suffix > 0 && !display.Contains("文件损坏")) display = display.Substring(0, suffix);
                        var item = Tk.E("pl-item"); item.Add(Tk.L(display, "pl-item__t"));
                        item.RegisterCallback<ClickEvent>(evt =>
                        {
                            if (deployment == null || index >= planSlots.Length) return;
                            selectedPlanSlot = planSlots[index]; planSlot = selectedPlanSlot;
                            if (deployment.PlanStore.TryLoad(selectedPlanSlot, out var plan, out _)) planName = plan.displayName;
                            planMessage = null; clearFocusRequested = true; nextUiRefresh = 0;
                        });
                        plList.Add(item); plItems.Add(item);
                    }
                }
                for (int i = 0; i < plItems.Count && i < planSlots.Length; i++) plItems[i].EnableInClassList("pl-item--on", planSlots[i] == selectedPlanSlot);
                plLoad.SetEnabled(selectedPlanSlot != null);
            }
            Tk.Show(plMessage, !string.IsNullOrEmpty(planMessage));
            Tk.SetText(plMessage, planMessage);
            plMessage.EnableInClassList("pl-message--bad", PlanState28.For(deployment.controller) != null && planActionFailed28);
        }
    }
}

