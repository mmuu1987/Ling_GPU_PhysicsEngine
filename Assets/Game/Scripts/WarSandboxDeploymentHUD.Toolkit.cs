using System;
using System.Collections.Generic;
using MassEngine.Game.Ui;
using UnityEngine;
using UnityEngine.UIElements;
using Tk = MassEngine.Game.Ui.WarSandboxUiToolkit;

namespace MassEngine.Game
{
    /// <summary>
    /// Phase 5 of the UI Toolkit migration: the deployment HUD (overview tree | map | inspector, legion page, template picker,
    /// unit dossier, local plans and the dialogs). View only — every control calls the same method as the uGUI presenter in
    /// WarSandboxDeploymentHUD.Legion/.Merged/.UGUI/.UnitStats/.Tight/.Check32, with the same texts; state stays in the HUD.
    /// Refresh follows the uGUI cadence (nextUiRefresh, 0.1 s). The map gesture reuses Translation.cs (…At methods).
    /// Document order 5: above the battle HUD (0), below the front end (10: battle navigation strip) and pages (20: settings).
    /// </summary>
    public sealed partial class WarSandboxDeploymentHUD
    {
        private const int DeployDocumentOrder = 5;
        private UIDocument dpDoc;
        private VisualElement dpRoot, dpOv, dpLg, dpPk, dpSt, dpPl, dpCf, dpAm, dpRa, dpTd;
        private TkUnitPreview dpUnitPv, dpLgPv, dpStPv;

        private sealed class DpField { public TextField f; public Func<string> get; }
        private readonly List<DpField> dpFields = new List<DpField>();

        // ----- shared with Translation.cs / Resize.cs -----
        private bool TkMapActive => Tk.Enabled && dpMap != null;
        private bool MapLayoutAvailable => Tk.Enabled ? dpRoot != null && dpRoot.panel != null : runtimeUi != null;
        private Vector2 MapLayoutSize => Tk.Enabled ? (dpRoot != null ? dpRoot.layout.size : Vector2.zero) : runtimeUi != null ? new Vector2(runtimeUi.Width, runtimeUi.Height) : Vector2.zero;
        private float MapUiScale => Tk.Enabled ? Tk.Scale : runtimeUi != null ? runtimeUi.Scale : 1f;
        private bool TkGhostVisible => dpGhost != null && dpGhost.style.display == DisplayStyle.Flex;
        private void HideTkGhost() { if (dpGhost != null) Tk.Show(dpGhost, false); }
        private void BlurToolkitFocus() { (dpRoot?.panel?.focusController?.focusedElement as Focusable)?.Blur(); }

        private void HideDeployToolkit()
        {
            if (dpRoot == null) return;
            if (tkMapPointer != -1 && dpMap != null) { if (dpMap.HasPointerCapture(tkMapPointer)) dpMap.ReleasePointer(tkMapPointer); tkMapPointer = -1; }
            BlurToolkitFocus(); CancelBrushStroke();
            Tk.Show(dpRoot, false);
            dpUnitPv?.Deactivate(); dpLgPv?.Deactivate(); dpStPv?.Deactivate();
        }

        private void DestroyDeployToolkit()
        {
            if (dpDoc == null) return;
            dpUnitPv?.Deactivate(); dpLgPv?.Deactivate(); dpStPv?.Deactivate();
            Tk.DestroyDocument(dpDoc); dpDoc = null; dpRoot = null; dpMap = null; dpGhost = null;
            DisposeBrush(); dpBrushGhost = dpBrushRing = null; dpBrushPaint = dpBrushErase = dpBrushClear = null; dpBrushInfo = null;
        }

        private Button DpOn(string name, Action action)
        {
            var b = dpRoot.Q<Button>(name);
            if (b != null) b.clicked += () => { if (deployment == null) return; action(); nextUiRefresh = 0; };
            else Debug.LogWarning("[Deployment UI Toolkit] missing button " + name);
            return b;
        }

        private TextField DpBind(string name, Func<string> get, Action<string> set, int maxLength = 24)
        {
            var f = dpRoot.Q<TextField>(name);
            if (f == null) { Debug.LogWarning("[Deployment UI Toolkit] missing field " + name); return null; }
            f.isDelayed = false; f.maxLength = maxLength;
            // Same as the uGUI InputField onValueChanged: the HUD string follows every keystroke; nothing commits implicitly.
            f.RegisterValueChangedCallback(e => { if (deployment != null) set(e.newValue ?? ""); });
            dpFields.Add(new DpField { f = f, get = get });
            return f;
        }

        private void SyncDpFields()
        {
            foreach (var d in dpFields) { string v = d.get() ?? ""; if (d.f.value != v) d.f.SetValueWithoutNotify(v); }
        }

        private static void DpOnState(VisualElement e, bool on) => e?.EnableInClassList("btn--on", on);

        private static void DpImage(VisualElement e, Texture2D t)
        {
            if (e == null || ReferenceEquals(e.userData, t)) return;
            e.userData = t;
            e.style.backgroundImage = t != null ? new StyleBackground(t) : new StyleBackground(StyleKeyword.None);
        }

        private void BuildDeployToolkit()
        {
            dpDoc = Tk.CreateDocument(transform, "Deployment UI Toolkit", "Deploy", DeployDocumentOrder);
            if (dpDoc == null) return;
            var r = dpDoc.rootVisualElement;
            dpRoot = r; // visibility is toggled on the document root
            dpOv = r.Q("dp-ov"); dpLg = r.Q("dp-lg"); dpPk = r.Q("dp-pk"); dpSt = r.Q("dp-st"); dpPl = r.Q("dp-pl");
            dpCf = r.Q("dp-cf"); dpAm = r.Q("dp-am"); dpRa = r.Q("dp-ra"); dpTd = r.Q("dp-td");
            BuildOverview(r);
            BuildLegionPage(r);
            BuildPickerPage(r);
            BuildStatsPage(r);
            BuildPlansPage(r);
            BuildDialogs(r);
            Tk.HideScrollers(r);
            r.Query<Button>().ForEach(b => b.focusable = false);
        }

        /// <summary>Same routing as RefreshUGUI; called instead of it while the new UI is on.</summary>
        private void RefreshDeployToolkit()
        {
            if (dpDoc == null) BuildDeployToolkit();
            if (dpRoot == null) return;
            Tk.Show(dpRoot, true);
            if (clearFocusRequested) { BlurToolkitFocus(); clearFocusRequested = false; }
            if (Time.unscaledTime < nextUiRefresh) return;
            nextUiRefresh = Time.unscaledTime + .1f;

            var draft = deployment.Draft;
            int page = 0; // 1 confirm, 2 plans, 3 stats, 4 picker, 5 legion, 6 overview
            if (Confirming) page = 1;
            else if (plansOpen) page = 2;
            else if (statsOpen && draft != null) page = 3;
            else if (draft != null) { ClampSelection(); page = pickerOpen && draft.Count > 0 ? 4 : legionOpen ? 5 : 6; }
            if (page == 3 && !UpdateStatsPage()) page = 0; // CloseStats() when the draft has no templates, like DrawUGUIStats

            Tk.Show(dpCf, page == 1);
            Tk.Show(dpPl, page == 2);
            Tk.Show(dpSt, page == 3);
            Tk.Show(dpPk, page == 4);
            Tk.Show(dpLg, page == 5);
            Tk.Show(dpOv, page == 6);
            if (page == 2) UpdatePlansPage();
            if (page == 4) UpdatePickerPage();
            if (page == 5) UpdateLegionPage();
            if (page == 6) UpdateOverview();
            if (page != 3) dpStPv?.Deactivate();
            if (page != 5) dpLgPv?.Deactivate();
            if (page != 6) dpUnitPv?.Deactivate();

            bool overlays = page == 5 || (page == 6 && draft.Count > 0);
            Tk.Show(dpAm, overlays && armyMenu);
            Tk.Show(dpRa, overlays && removeArmyConfirm);
            if (overlays && armyMenu) UpdateArmyMenu(draft.Count > 0 ? draft[selected].teamId : 0);
            bool tight = TightEnabled && tightDialog != 0;
            Tk.Show(dpTd, tight);
            if (tight) UpdateTightDialog();
            SyncDpFields();
        }

        // =====================================================================================================
        // Draft status line (DraftStatus / DrawCheck32)
        // =====================================================================================================
        private bool tkFixRaw; private int tkFixIndex; private string tkFixProblem;

        private string TkDraftStatus(out bool bad, out bool locate)
        {
            locate = false;
            if (Check32)
            {
                bool valid = deployment.TryValidate(out string validation); bool pending = HasPendingInputs();
                string problem = inputError ?? validation; int index = DeploymentCheck32.Index(problem, deployment.Draft);
                bool raw = inputError != null && index < 0 && (inputError.Contains("当前输入未写入草稿") || inputError == "请输入有效的有限数值。");
                string message;
                if (problem != null) { message = "暂不能开战 · " + (raw ? DeploymentCheck32.Scope(deployment.Draft, selected) + "：" + problem : DeploymentCheck32.Problem(problem, deployment.Draft)); message += "\n" + (raw ? "输入尚未写入草稿；请修正后再更新。" : DeploymentCheck32.Hint(problem)); }
                else if (pending) message = "输入未更新 · 点击开战会先更新输入、校验并应用\n只想检查位置可先更新；应用到本局不等于保存方案。";
                else { var p = PlanState28.For(deployment.controller); message = (p != null ? p.DraftStatus(false) : "布阵校验通过") + "\n" + (TightDraftNotice() ?? (legionOpen ? "完成返回仅更新草稿；返回布阵后可应用并开战。" : "校验通过 · 开战会自动应用当前草稿，无需先点应用。")); }
                locate = (raw || index >= 0) && deployment.Draft.Count > 0;
                tkFixRaw = raw; tkFixIndex = index; tkFixProblem = problem;
                bad = problem != null || !valid;
                return message;
            }
            bool ok = deployment.TryValidate(out string error);
            string text = inputError ?? (HasPendingInputs() ? "输入尚未更新 · 点击更新或完成返回；不会立即影响战斗" : error ?? TightDraftNotice() ?? "已写入本局草稿 · 应用布阵后生效，保存方案需另存");
            if (FlowPolish23.Active(deployment))
                text = inputError ?? (error != null ? "暂不能开战 · " + error : HasPendingInputs() ? "输入未更新 · 点击更新或完成返回，再检查布阵" : TightDraftNotice() ?? (legionOpen ? "编成已更新 · 完成返回后检查位置；尚未开战" : FlowPolish23.DraftSummary(deployment)));
            var state28 = PlanState28.For(deployment.controller);
            if (state28 != null && inputError == null && ok && TightDraftNotice() == null) text = state28.DraftStatus(HasPendingInputs());
            bad = inputError != null || !ok;
            return text;
        }

        private void ShowStatus(Label label, Button fix)
        {
            string text = TkDraftStatus(out bool bad, out bool locate);
            Tk.SetText(label, text);
            label.EnableInClassList("dp-status--bad", bad);
            Tk.Show(fix, locate);
            if (locate && fix != null) fix.text = tkFixRaw ? "修正输入" : "定位问题";
        }

        // =====================================================================================================
        // Overview: header, tree, map, inspector, footer
        // =====================================================================================================
        private Label dpTitle, dpStatus, dpMapNote, dpInspScope, dpInspName, dpInspCount, dpArmyText, dpFacingLabel, dpResizeValues;
        private Button dpChallenge, dpArmyAdd, dpTight, dpPlace, dpUndo, dpRedo, dpFix, dpStart, dpTabUnit, dpTabPlace, dpFacingAuto, dpManual;
        private VisualElement dpInspEmpty, dpInspHead, dpInspArmy, dpInspForm, dpUnit, dpPlaceScroll, dpTightPresets, dpManualOn, dpManualOff, dpUnitPreviewHost;
        private ScrollView dpTreeList;
        private TextField dpFx, dpFz, dpFdepth, dpFwidth, dpFdensity, dpFaspect;
        private readonly List<Label[]> dpUnitStats = new List<Label[]>();

        private sealed class TkArmy { public int team, first; public VisualElement head, swatch; public Label name, line, comp; public readonly List<TkRow> rows = new List<TkRow>(); }
        private sealed class TkRow { public int index; public VisualElement root, pic; public Label name, role, count; }
        private readonly List<TkArmy> dpArmies = new List<TkArmy>();
        private string dpTreeKey;

        private void BuildOverview(VisualElement r)
        {
            dpTitle = r.Q<Label>("dp-title"); dpStatus = r.Q<Label>("dp-status"); dpMapNote = r.Q<Label>("dp-map-note");
            dpInspScope = r.Q<Label>("dp-insp-scope"); dpInspName = r.Q<Label>("dp-insp-name"); dpInspCount = r.Q<Label>("dp-insp-count");
            dpArmyText = r.Q<Label>("dp-army-text"); dpFacingLabel = r.Q<Label>("dp-facing-label"); dpResizeValues = r.Q<Label>("dp-resize-values");
            dpInspEmpty = r.Q("dp-insp-empty"); dpInspHead = r.Q("dp-insp-head"); dpInspArmy = r.Q("dp-insp-army"); dpInspForm = r.Q("dp-insp-form");
            dpUnit = r.Q("dp-unit"); dpPlaceScroll = r.Q("dp-place-scroll"); dpTightPresets = r.Q("dp-tight-presets");
            dpManualOn = r.Q("dp-manual-on"); dpManualOff = r.Q("dp-manual-off"); dpUnitPreviewHost = r.Q("dp-unit-preview");
            dpTreeList = r.Q<ScrollView>("dp-tree-list");
            dpUnitPv = new TkUnitPreview(); dpUnitPreviewHost.Add(dpUnitPv);
            BuildStatCells(r.Q("dp-unit-stats"), dpUnitStats);
            Tk.SetText(dpMapNote, "拖色块平移 · 选中后拖边/角改尺寸 · R旋转朝向 · Shift锁比例 · 松手校验 · Esc/移出取消 · 滚轮缩放 · 右键拖动看图 · 画笔：左键涂 / Shift擦 / Alt拖动平移 / [ ]笔刷");

            // Header / footer — DrawDeploymentOverview, DrawTightHeader, DrawDeploymentFooter.
            dpTight = DpOn("dp-tight", () => { if (CommitFields()) { tightDialog = 1; clearFocusRequested = true; } });
            dpChallenge = DpOn("dp-challenge", CycleChallenge);
            BuildBudgetControls(r);
            DpOn("dp-plans", OpenPlanLibrary);
            dpUndo = DpOn("dp-undo", () => { deployment.Draft.Undo(); ClampSelection(); RefreshAfterUiChange(); });
            dpRedo = DpOn("dp-redo", () => { deployment.Draft.Redo(); ClampSelection(); RefreshAfterUiChange(); });
            dpFix = DpOn("dp-fix", () => Locate32(tkFixRaw, tkFixIndex, tkFixProblem));
            DpOn("dp-cancel", () => { deployment.CancelEditing(); ReleaseCamera(); });
            DpOn("dp-apply", () => Apply(false));
            dpStart = DpOn("dp-start", () => { if (!RequestTightStart()) Apply(true); });

            // Tree — DrawFormationTree.
            dpArmyAdd = DpOn("dp-army-add", () => { if (AddFormation(true)) { spatialSelection = true; inspectorTab = 0; } });

            // Map header — DrawUGUIMap.
            dpPlace = DpOn("dp-place", () => { placing = !placing; spatialSelection = true; });
            BuildMap(r);
            BuildBrushControls(r);

            // Inspector — DrawInspector / DrawArmyInspector / DrawUnitFields / DrawPlacementFields / DrawFacingControls / DrawTightPresets.
            DpOn("dp-army-add-formation", () => { if (AddFormation(false)) { spatialSelection = true; inspectorTab = 0; } });
            DpOn("dp-army-details", () => OpenLegion(selected));
            DpOn("dp-army-remove", () => removeArmyConfirm = true);
            dpTabUnit = DpOn("dp-tab-unit", () => inspectorTab = 0);
            dpTabPlace = DpOn("dp-tab-place", () => inspectorTab = 1);
            DpBind("dp-unit-count", () => count, v => { count = v; inputError = null; nextUiRefresh = 0; });
            DpOn("dp-unit-update", () => { if (CommitFields()) RefreshAfterUiChange(); });
            DpOn("dp-unit-template", () => { if (CommitFields()) { pickerRole = 0; pickerOpen = true; } });
            DpOn("dp-unit-stats-open", OpenStats);
            DpOn("dp-unit-army", () => { if (CommitFields()) armyMenu = true; });
            DpOn("dp-unit-remove", () => { deployment.Draft.Remove(selected); ClampSelection(); RefreshAfterUiChange(); });
            DpOn("dp-preset-standard", () => SetTightPreset(false));
            DpOn("dp-preset-tight", () => SetTightPreset(true));
            DpOn("dp-facing-ccw", () => RotateSelected(-1));
            DpOn("dp-facing-cw", () => RotateSelected(1));
            dpFacingAuto = DpOn("dp-facing-auto", SetFacingAuto);
            dpFx = DpBind("dp-f-x", () => x, v => x = v);
            dpFz = DpBind("dp-f-z", () => z, v => z = v);
            dpManual = DpOn("dp-manual", () => manual = !manual);
            dpFdepth = DpBind("dp-f-depth", () => depth, v => depth = v);
            dpFwidth = DpBind("dp-f-width", () => width, v => width = v);
            dpFdensity = DpBind("dp-f-density", () => density, v => density = v);
            dpFaspect = DpBind("dp-f-aspect", () => aspect, v => aspect = v);
            DpOn("dp-place-update", () => { if (CommitFields()) RefreshAfterUiChange(); });
        }

        private static void BuildStatCells(VisualElement host, List<Label[]> cells)
        {
            for (int i = 0; i < 3; i++)
            {
                var cell = Tk.E("dp-stat"); if (i == 2) cell.AddToClassList("dp-stat--last");
                var k = Tk.L("", "dp-stat__k"); var v = Tk.L("", "dp-stat__v"); var s = Tk.L("", "dp-stat__s");
                cell.Add(k); cell.Add(v); cell.Add(s); host.Add(cell);
                cells.Add(new[] { k, v, s });
            }
        }

        private static readonly WarSandboxUnitStat[] DossierKeys = { WarSandboxUnitStat.MaxHp, WarSandboxUnitStat.AttackDamage, WarSandboxUnitStat.AttackInterval };

        private void UpdateStatCells(List<Label[]> cells, UnitTypeConfig template)
        {
            for (int i = 0; i < cells.Count; i++)
            {
                var def = WarSandboxUnitStats.Get(DossierKeys[i]); var value = deployment.DraftStats.Resolve(template, def);
                Tk.SetText(cells[i][0], def.Label);
                Tk.SetText(cells[i][1], def.Format(value.effective) + def.Unit);
                Tk.Show(cells[i][2], Info31);
                if (Info31) { Tk.SetText(cells[i][2], RosterInfo31.Origin(value, def)); cells[i][2].EnableInClassList("dp-stat__s--custom", value.source != WarSandboxStatSource.Official); }
            }
        }

        private void UpdateOverview()
        {
            var draft = deployment.Draft;
            bool flow = FlowPolish23.Active(deployment);
            Tk.SetText(dpTitle, flow ? "编成 → 布阵 → 开战" : "战前布阵");
            dpTitle.EnableInClassList("dp-head__title--flow", flow);
            Tk.Show(dpTight, TightEnabled);
            UpdateChallengeButton();
            UpdateBudgetButton();
            UpdateTree();
            dpPlace.text = placing ? "取消放置" : "点击放置";
            dpPlace.SetEnabled(draft.Count > 0); DpOnState(dpPlace, placing);
            UpdateMap();
            UpdateInspector();
            ShowStatus(dpStatus, dpFix);
            dpUndo.SetEnabled(draft.CanUndo); dpRedo.SetEnabled(draft.CanRedo);
            dpStart.text = Check32 ? StartLabel32() : flow ? (HasPendingInputs() ? "更新并开战  →" : "开战  →") : "应用并开战  →";
        }

        private void UpdateTree()
        {
            var draft = deployment.Draft; var ids = ArmyIds();
            string key = Info31 ? "i|" : "|";
            foreach (int team in ids) { key += team + ":"; for (int i = 0; i < draft.Count; i++) if (draft[i].teamId == team) key += i + ","; key += ";"; }
            if (key != dpTreeKey)
            {
                dpTreeKey = key; dpTreeList.Clear(); dpArmies.Clear();
                foreach (int team in ids)
                {
                    var a = new TkArmy { team = team, first = -1 };
                    for (int i = 0; i < draft.Count; i++) if (draft[i].teamId == team) { a.first = i; break; }
                    int head = a.first;
                    var box = Tk.E("tr-army");
                    a.head = Tk.E("tr-head");
                    a.swatch = Tk.E("tr-head__sw");
                    var main = Tk.E("tr-head__main");
                    a.name = Tk.L("", "tr-head__name"); a.line = Tk.L("", "tr-head__line"); a.comp = Tk.L("", "tr-head__line");
                    main.Add(a.name); main.Add(a.line); main.Add(a.comp);
                    main.RegisterCallback<ClickEvent>(_ => { if (deployment == null) return; SelectArmy(head); nextUiRefresh = 0; });
                    a.swatch.RegisterCallback<ClickEvent>(_ => { if (deployment == null) return; SelectArmy(head); nextUiRefresh = 0; });
                    var btns = Tk.E("tr-head__btns");
                    btns.Add(MiniButton("布阵", "tr-mini", () => { if (SelectFormation(head)) { spatialSelection = true; inspectorTab = 1; } }));
                    btns.Add(MiniButton("详情", "tr-mini", () => OpenLegion(head)));
                    a.head.Add(a.swatch); a.head.Add(main); a.head.Add(btns);
                    box.Add(a.head);
                    for (int i = 0; i < draft.Count; i++)
                    {
                        if (draft[i].teamId != team) continue;
                        int index = i;
                        var row = new TkRow { index = i, root = Tk.E("tr-row"), pic = Tk.E("tr-pic") };
                        var rm = Tk.E("tr-row__main");
                        row.name = Tk.L("", "tr-row__name"); row.role = Tk.L("", "tr-row__role"); row.count = Tk.L("", "tr-row__count");
                        rm.Add(row.name); rm.Add(row.role);
                        row.root.Add(row.pic); row.root.Add(rm); row.root.Add(row.count);
                        row.root.RegisterCallback<ClickEvent>(_ => { if (deployment == null) return; if (SelectFormation(index)) spatialSelection = true; nextUiRefresh = 0; });
                        box.Add(row.root); a.rows.Add(row);
                    }
                    box.Add(MiniButton("＋ 添加编成", "tr-add", () => { if (SelectFormation(head) && AddFormation(false)) { spatialSelection = true; inspectorTab = 0; } }));
                    dpTreeList.Add(box); dpArmies.Add(a);
                }
            }
            int current = draft.Count > 0 ? draft[selected].teamId : -1;
            foreach (var a in dpArmies)
            {
                int population = 0, formations = 0;
                for (int i = 0; i < draft.Count; i++) if (draft[i].teamId == a.team) { population += draft[i].count; formations++; }
                a.head.EnableInClassList("tr-head--on", !spatialSelection && a.team == current);
                a.swatch.style.backgroundColor = WarSandboxTeamPalette.Resolve(a.team);
                Tk.SetText(a.name, WarSandboxBattleController.DefaultArmyName(a.team) + ChallengeSuffix(a.team));
                Tk.SetText(a.line, population.ToString("N0") + " 人 · " + formations + " 个编成" + BudgetLine(a.team, out bool overBudget));
                a.line.EnableInClassList("tr-head__line--bad", overBudget);
                Tk.Show(a.comp, Info31);
                if (Info31) Tk.SetText(a.comp, RosterInfo31.Composition(draft, a.team));
                foreach (var row in a.rows)
                {
                    var e = draft[row.index]; var portrait = PortraitEntry(e.template);
                    DpImage(row.pic, portrait != null ? portrait.unitPreview : null);
                    Tk.SetText(row.name, e.Name);
                    Tk.SetText(row.role, "编成 " + (row.index + 1).ToString("00") + " · " + (WarSandboxUnitStats.IsRanged(e.template) ? "远程" : "近战"));
                    Tk.SetText(row.count, e.count.ToString("N0"));
                    row.root.EnableInClassList("tr-row--on", spatialSelection && row.index == selected);
                }
            }
        }

        private Button MiniButton(string text, string cls, Action action)
        {
            var b = new Button { text = text, focusable = false };
            b.AddToClassList("btn"); b.AddToClassList(cls);
            b.clicked += () => { if (deployment == null) return; action(); nextUiRefresh = 0; };
            return b;
        }

        private void UpdateInspector()
        {
            var draft = deployment.Draft;
            bool empty = draft.Count == 0;
            Tk.Show(dpInspEmpty, empty); Tk.Show(dpInspHead, !empty);
            Tk.Show(dpInspArmy, !empty && !spatialSelection); Tk.Show(dpInspForm, !empty && spatialSelection);
            if (empty) { dpUnitPv.Deactivate(); return; }
            var e = draft[selected]; int team = e.teamId;
            if (!spatialSelection)
            {
                dpUnitPv.Deactivate();
                int population = 0, formations = 0;
                for (int i = 0; i < draft.Count; i++) if (draft[i].teamId == team) { population += draft[i].count; formations++; }
                Tk.SetText(dpInspScope, "军团");
                Tk.SetText(dpInspName, WarSandboxBattleController.DefaultArmyName(team) + ChallengeSuffix(team));
                Tk.SetText(dpInspCount, population.ToString("N0") + " 人");
                UpdateBudgetRow(team);
                Tk.SetText(dpArmyText, formations + " 个编成" + BudgetArmyText(team) + (Info31 ? "\n" + RosterInfo31.Composition(draft, team) : "") +
                    (draft.IsTeamLocked(team) ? "\n\n挑战模式：这是电脑指挥的军团，\n编成已锁定，只能编辑你指挥的军团。" : "\n\n点左侧编成，或在地图上点色块，\n即可编辑兵种、人数、位置与朝向。"));
                return;
            }
            string role = WarSandboxUnitStats.IsRanged(e.template) ? "远程" : "近战";
            Tk.SetText(dpInspScope, (Check32 ? DeploymentCheck32.Scope(draft, selected) : WarSandboxBattleController.DefaultArmyName(team) + " · 编成 " + (selected + 1).ToString("00")) + " · " + role + (draft.IsLocked(selected) ? " · 已锁定" : ""));
            Tk.SetText(dpInspName, e.Name);
            Tk.SetText(dpInspCount, e.count.ToString("N0") + " 人" + BudgetUnitText(e));
            dpTabUnit.EnableInClassList("tab--on", inspectorTab == 0); dpTabPlace.EnableInClassList("tab--on", inspectorTab == 1);
            Tk.Show(dpUnit, inspectorTab != 1); Tk.Show(dpPlaceScroll, inspectorTab == 1);
            if (inspectorTab == 1)
            {
                dpUnitPv.Deactivate();
                Tk.Show(dpTightPresets, TightEnabled);
                Tk.SetText(dpFacingLabel, FacingLabel());
                dpFacingAuto.SetEnabled(e.facing != WarSandboxDeploymentFacing.Auto);
                dpManual.text = manual ? "阵型：手动尺寸" : "阵型：密度 / 宽深比";
                Tk.Show(dpManualOn, manual); Tk.Show(dpManualOff, !manual);
                Tk.Show(dpManual, e.brush == null);
                UpdateBrushControls(e);
                Tk.SetText(dpResizeValues, e.brush != null ? "画笔形状：用画笔/橡皮修改形状\n密度决定士兵间距，人数和体型不变\n「恢复矩形」回到自动占地" :
                    "地图上拖边/角调整尺寸 · Shift锁比例\n松手校验，人数和体型不变\n编辑尺寸后切为自动占地");
                return;
            }
            // Short screens drop the 3D preview first; numbers and actions always stay visible.
            bool preview = dpUnitPreviewHost.layout.height >= 70;
            dpUnitPv.style.visibility = preview ? Visibility.Visible : Visibility.Hidden;
            if (preview) { var portrait = PortraitEntry(e.template); dpUnitPv.Bind(e.template, portrait != null ? portrait.unitPreview : null, null); }
            else dpUnitPv.Deactivate();
            UpdateStatCells(dpUnitStats, e.template);
        }

        // =====================================================================================================
        // Map (DrawUGUIMap + DrawTerrainMap + DrawFacingMark + DrawResizeHandles; gesture = Translation.cs)
        // =====================================================================================================
        private VisualElement dpMapArea, dpMap, dpTerrain, dpWalls, dpBlocks, dpOutlines, dpHandles, dpGhost, dpObjective, dpSelOutline;
        private readonly List<VisualElement> dpWallPool = new List<VisualElement>(), dpProblemPool = new List<VisualElement>();
        private sealed class TkBlock { public VisualElement root, mark; public Label label; }
        private readonly List<TkBlock> dpBlockPool = new List<TkBlock>();
        private readonly Dictionary<DeploymentResizeHandle, VisualElement> dpHandleViews = new Dictionary<DeploymentResizeHandle, VisualElement>();
        private Texture2D dpTerrainShown;
        private int tkMapPointer = -1;
        // Zoom / pan (2026-10-15): 1x-6x about the cursor, right/middle drag pans, 适应 resets.
        private float dpZoom = 1f; private Vector2 dpPan; private int dpPanPointer = -1; private Vector2 dpPanLast; private Button dpFit;

        private static VisualElement Layer(VisualElement parent, string cls = null)
        {
            var e = cls != null ? Tk.E(cls) : new VisualElement();
            e.pickingMode = PickingMode.Ignore;
            if (cls == null) { e.style.position = Position.Absolute; e.style.left = 0; e.style.top = 0; e.style.right = 0; e.style.bottom = 0; }
            parent.Add(e);
            return e;
        }

        private void BuildMap(VisualElement r)
        {
            dpMapArea = r.Q("dp-map-area"); dpMap = r.Q("dp-map");
            dpTerrain = Layer(dpMap, "dp-map__terrain");
            for (int i = 1; i < 8; i++)
            {
                var gx = Layer(dpMap, "dp-map__gx"); gx.style.left = Length.Percent(100f * i / 8);
                var gy = Layer(dpMap, "dp-map__gy"); gy.style.top = Length.Percent(100f * i / 8);
            }
            dpWalls = Layer(dpMap);
            dpObjective = Layer(dpMap, "dp-map__objective");
            dpBlocks = Layer(dpMap);
            dpOutlines = Layer(dpMap);
            dpSelOutline = Layer(dpOutlines, "dp-blk"); dpSelOutline.AddToClassList("dp-blk--sel");
            dpHandles = Layer(dpMap);
            foreach (var h in ResizeHandles) dpHandleViews[h] = Layer(dpHandles, "dp-handle");
            dpGhost = Layer(dpMap, "dp-ghost"); Tk.Show(dpGhost, false);

            // One left pointer, no click handler: release cannot also place a formation (WarSandboxDeploymentMapPointer).
            dpMap.pickingMode = PickingMode.Position;
            dpMap.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0 || tkMapPointer != -1 || deployment == null) return;
                tkMapPointer = e.pointerId; dpMap.CapturePointer(e.pointerId); e.StopPropagation();
                if (BrushBegin(e.localPosition, e.shiftKey, e.altKey)) return;
                TranslationDownAt(e.localPosition, ScreenPixels(e.position));
            });
            dpMap.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (e.pointerId != tkMapPointer || deployment == null) return;
                if (brushStroke) { BrushMove(e.localPosition); return; }
                TranslationMoveAt(e.localPosition, ScreenPixels(e.position));
            });
            dpMap.RegisterCallback<PointerUpEvent>(e =>
            {
                if (e.pointerId != tkMapPointer || e.button != 0) return;
                tkMapPointer = -1; if (dpMap.HasPointerCapture(e.pointerId)) dpMap.ReleasePointer(e.pointerId);
                if (brushStroke) { if (deployment != null) { BrushMove(e.localPosition); BrushEnd(); } else CancelBrushStroke(); return; }
                if (deployment != null) TranslationUpAt(e.localPosition, ScreenPixels(e.position));
            });
            dpMap.RegisterCallback<PointerCaptureOutEvent>(_ => { if (tkMapPointer != -1) { tkMapPointer = -1; CancelTranslation(); CancelBrushStroke("画笔已取消。"); } });

            // Zoom / pan: only the map element's size and offset change. Every layer is laid out in map-local
            // pixels, so drag / resize / rotate keep working unchanged.
            dpMapArea.style.overflow = Overflow.Hidden;
            dpMapArea.RegisterCallback<WheelEvent>(e =>
            {
                if (deployment == null || tkMapPointer != -1) return;
                ZoomMapAt(dpMapArea.WorldToLocal(e.mousePosition), e.delta.y < 0 ? 1.25f : 0.8f);
                e.StopPropagation();
            });
            dpMapArea.RegisterCallback<PointerDownEvent>(e =>
            {
                if ((e.button != 1 && e.button != 2) || dpPanPointer != -1 || tkMapPointer != -1) return;
                dpPanPointer = e.pointerId; dpPanLast = e.position; dpMapArea.CapturePointer(e.pointerId); e.StopPropagation();
            });
            dpMapArea.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (e.pointerId != dpPanPointer) return;
                Vector2 p = e.position; dpPan += p - dpPanLast; dpPanLast = p;
            });
            dpMapArea.RegisterCallback<PointerUpEvent>(e =>
            {
                if (e.pointerId != dpPanPointer) return;
                dpPanPointer = -1; if (dpMapArea.HasPointerCapture(e.pointerId)) dpMapArea.ReleasePointer(e.pointerId);
            });
            dpMapArea.RegisterCallback<PointerCaptureOutEvent>(_ => dpPanPointer = -1);
            dpFit = new Button(() => { dpZoom = 1f; dpPan = Vector2.zero; }) { text = "适应" };
            dpFit.AddToClassList("btn"); dpFit.AddToClassList("btn--sm");
            dpFit.style.position = Position.Absolute; dpFit.style.right = 8; dpFit.style.bottom = 8;
            dpMapArea.Add(dpFit); Tk.Show(dpFit, false);
        }

        // Map frame inside the area: fit-to-area times zoom, centred, then panned and clamped so a zoomed map
        // always covers the area. zoom 1 / pan 0 is exactly the previous fitted layout.
        private Rect MapFrame(Rect area, Vector2 world, float zoom, ref Vector2 pan)
        {
            float scale = Mathf.Min((area.width - 4) / world.x, (area.height - 4) / world.y) * zoom;
            float w = Mathf.Floor(world.x * scale), h = Mathf.Floor(world.y * scale);
            float left = Mathf.Floor((area.width - w) / 2), top = Mathf.Floor((area.height - h) / 2);
            pan.x = w <= area.width ? 0f : Mathf.Clamp(pan.x, area.width - w - left, -left);
            pan.y = h <= area.height ? 0f : Mathf.Clamp(pan.y, area.height - h - top, -top);
            return new Rect(Mathf.Floor(left + pan.x), Mathf.Floor(top + pan.y), w, h);
        }

        private void ZoomMapAt(Vector2 p, float factor)
        {
            var area = dpMapArea.contentRect; Vector2 world = deployment.WorldSize;
            if (area.width < 8 || area.height < 8 || world.x <= 0 || world.y <= 0) return;
            float next = Mathf.Clamp(dpZoom * factor, 1f, 6f);
            if (Mathf.Approximately(next, dpZoom)) return;
            Rect before = MapFrame(area, world, dpZoom, ref dpPan);
            Vector2 u = new Vector2((p.x - before.x) / Mathf.Max(1f, before.width), (p.y - before.y) / Mathf.Max(1f, before.height));
            dpZoom = next;
            Vector2 none = Vector2.zero;
            Rect centred = MapFrame(area, world, dpZoom, ref none);
            dpPan = p - new Vector2(u.x * centred.width, u.y * centred.height) - centred.position;
            MapFrame(area, world, dpZoom, ref dpPan);
        }

        private static Vector2 ScreenPixels(Vector3 panelPosition) => (Vector2)panelPosition * Tk.Scale;

        private void UpdateMap()
        {
            var area = dpMapArea.contentRect;
            Vector2 world = deployment.WorldSize;
            if (area.width < 8 || area.height < 8 || world.x <= 0 || world.y <= 0) { Tk.Show(dpMap, false); return; }
            Tk.Show(dpMap, true);
            Rect frame = MapFrame(area, world, dpZoom, ref dpPan);
            float w = frame.width, h = frame.height;
            dpMap.style.left = frame.x; dpMap.style.top = frame.y;
            dpMap.style.width = w; dpMap.style.height = h;
            if (dpFit != null) { Tk.Show(dpFit, dpZoom > 1.001f); dpFit.text = "适应（" + dpZoom.ToString("0.#") + "×）"; }
            var map = new Rect(0, 0, w, h);
            translationMap = map; translationWorld = world;

            var terrain = TerrainMapTexture();
            if (terrain != dpTerrainShown) { dpTerrainShown = terrain; dpTerrain.style.backgroundImage = terrain != null ? new StyleBackground(terrain) : new StyleBackground(StyleKeyword.None); }

            var draft = deployment.Draft; var rules = draft.Rules;
            int walls = rules.staticObstaclesEnabled && rules.staticObstacles != null ? rules.staticObstacles.Length : 0;
            while (dpWallPool.Count < walls) dpWallPool.Add(Layer(dpWalls, "dp-map__wall"));
            for (int i = 0; i < dpWallPool.Count; i++)
            {
                Tk.Show(dpWallPool[i], i < walls);
                if (i < walls) Place(dpWallPool[i], MapBounds(rules.staticObstacles[i].Bounds, world, map));
            }
            bool objective = rules.gameMode == WarSandboxGameMode.ControlPoint;
            Tk.Show(dpObjective, objective);
            if (objective) { var p = WarSandboxMinimapProjection.WorldToMap(rules.controlPointCenter, world, map); dpObjective.style.left = p.x - 5; dpObjective.style.top = p.y - 5; }

            // Deployment B: the draft may be temporarily invalid; outline the formation(s) named by the first problem.
            int problemA = -1, problemB = -1;
            if (!deployment.TryValidate(out string problem) && problem != null)
            {
                problemA = DeploymentCheck32.Index(problem, draft);
                var other = System.Text.RegularExpressions.Regex.Match(problem, "与编成 (\\d+)");
                if (other.Success && int.TryParse(other.Groups[1].Value, out int j) && j >= 1 && j <= draft.Count) problemB = j - 1;
            }
            while (dpBlockPool.Count < draft.Count)
            {
                var b = new TkBlock { root = Layer(dpBlocks, "dp-blk") };
                b.mark = Layer(b.root, "dp-blk__mark");
                b.label = new Label(); b.label.AddToClassList("dp-blk__label"); b.label.pickingMode = PickingMode.Ignore; b.root.Add(b.label);
                dpBlockPool.Add(b);
            }
            while (dpProblemPool.Count < 2) dpProblemPool.Add(Layer(dpOutlines, "dp-problem"));
            Tk.Show(dpProblemPool[0], false); Tk.Show(dpProblemPool[1], false); Tk.Show(dpSelOutline, false);
            int problems = 0;
            for (int i = 0; i < dpBlockPool.Count; i++)
            {
                var b = dpBlockPool[i];
                if (i >= draft.Count) { Tk.Show(b.root, false); continue; }
                if (brushStroke && i == brushIndex) { Tk.Show(b.root, false); continue; } // the stroke preview stands in for it
                var entry = draft[i]; var bounds = entry.Bounds;
                if (float.IsNaN(bounds.width) || float.IsInfinity(bounds.width) || float.IsNaN(bounds.height) || float.IsInfinity(bounds.height)) { Tk.Show(b.root, false); continue; }
                var rect = MapBounds(bounds, world, map);
                rect = Rect.MinMaxRect(Mathf.Max(map.xMin, rect.xMin), Mathf.Max(map.yMin, rect.yMin), Mathf.Min(map.xMax, rect.xMax), Mathf.Min(map.yMax, rect.yMax));
                if (rect.width <= 0 || rect.height <= 0) { Tk.Show(b.root, false); continue; }
                Tk.Show(b.root, true); Place(b.root, rect);
                b.root.style.backgroundColor = WarSandboxTeamPalette.Resolve(entry.teamId);
                ApplyBrushLook(b.root, entry, i);
                if ((i == problemA || i == problemB) && problems < 2) { const float t = 3; var o = dpProblemPool[problems++]; Tk.Show(o, true); Place(o, new Rect(rect.x - t, rect.y - t, rect.width + 2 * t, rect.height + 2 * t)); }
                if (spatialSelection && i == selected) { Tk.Show(dpSelOutline, true); Place(dpSelOutline, rect); }
                // White bar on the front edge (DrawFacingMark), in block-local coordinates.
                const float inset = 3, bar = 3;
                bool mark = rect.width > 2 * inset + 2 && rect.height > 2 * inset + 2;
                Tk.Show(b.mark, mark);
                if (mark)
                {
                    Rect m;
                    switch (FacingQuarter(i))
                    {
                        case 0: m = new Rect(inset, inset, rect.width - 2 * inset, bar); break;
                        case 1: m = new Rect(rect.width - inset - bar, inset, bar, rect.height - 2 * inset); break;
                        case 2: m = new Rect(inset, rect.height - inset - bar, rect.width - 2 * inset, bar); break;
                        default: m = new Rect(inset, inset, bar, rect.height - 2 * inset); break;
                    }
                    Place(b.mark, m);
                }
                bool label = rect.width > 40 && rect.height > 24;
                Tk.Show(b.label, label);
                if (label) Tk.SetText(b.label, (i + 1) + " " + FacingArrow(i));
            }
            bool handles = spatialSelection && !placing && draft.Count > 0 && !brushMode && draft[selected].brush == null;
            Tk.Show(dpHandles, handles);
            if (handles) TkPlaceHandles(MapBounds(draft[selected].Bounds, world, map));
            PruneBrushTextures();
        }

        private static void Place(VisualElement e, Rect r)
        {
            e.style.left = r.x; e.style.top = r.y; e.style.width = r.width; e.style.height = r.height;
        }

        private void TkPlaceHandles(Rect r)
        {
            float s = 8 / Tk.Scale; var map = translationMap;
            foreach (var h in ResizeHandles)
            {
                var p = HandlePoint(r, h);
                p.x = Mathf.Clamp(p.x, map.xMin + s / 2, map.xMax - s / 2); p.y = Mathf.Clamp(p.y, map.yMin + s / 2, map.yMax - s / 2);
                Place(dpHandleViews[h], new Rect(p.x - s / 2, p.y - s / 2, s, s));
            }
        }

        private void ShowTkGhost(Rect r, Color color)
        {
            if (dpGhost == null) return;
            Tk.Show(dpGhost, true); Place(dpGhost, r); dpGhost.style.backgroundColor = color;
        }

        /// <summary>UI Toolkit twin of ResizeFieldsPreview: handles and fields show the uncommitted size while dragging.</summary>
        private void TkResizePreview(WarSandboxDeploymentEntry e, bool preview)
        {
            if (dpRoot == null) return;
            var current = deployment.Draft != null && selected < deployment.Draft.Count ? deployment.Draft[selected].Bounds : new Rect();
            TkPlaceHandles(MapBounds(preview ? e.Bounds : current, translationWorld, translationMap));
            dpFx?.SetValueWithoutNotify(preview ? Format(e.center.x) : x);
            dpFz?.SetValueWithoutNotify(preview ? Format(e.center.z) : z);
            dpFdensity?.SetValueWithoutNotify(preview ? Format(e.density) : density);
            dpFaspect?.SetValueWithoutNotify(preview ? Format(e.aspect) : aspect);
            dpFdepth?.SetValueWithoutNotify(preview ? Format(e.Size.x) : depth);
            dpFwidth?.SetValueWithoutNotify(preview ? Format(e.Size.z) : width);
            Tk.SetText(dpResizeValues, preview ?
                "预览（未提交）\nD(X) " + e.Size.x.ToString("0.##") + " · W(Z) " + e.Size.z.ToString("0.##") + "\n密度 " + e.density.ToString("0.###") + " · 比例 " + e.aspect.ToString("0.###") +
                (resizeError == null ? "\n松手校验；Shift锁比例" : "\n" + resizeError) : "边/角调整尺寸 · Shift锁比例\n松手校验，人数和体型不变\n编辑尺寸后切为自动占地");
        }

        // =====================================================================================================
        // Dialogs: redeploy confirmation, army reassignment, remove army, high-load trial
        // =====================================================================================================
        private VisualElement dpAmList; private string dpAmKey; private readonly List<KeyValuePair<int, Button>> dpAmButtons = new List<KeyValuePair<int, Button>>();
        private Button dpAmNew, dpTdConfirm; private Label dpTdTitle, dpTdWarning;

        private void BuildDialogs(VisualElement r)
        {
            DpOn("cf-yes", () => { Confirming = false; resumeAfterConfirmation = false; Open(true); });
            DpOn("cf-no", CancelConfirmation);
            dpAmList = r.Q("am-list");
            dpAmNew = DpOn("am-new", () => AssignFormationArmy(deployment.Draft.NextArmyId()));
            DpOn("am-cancel", () => armyMenu = false);
            DpOn("ra-confirm", () =>
            {
                var draft = deployment.Draft; if (draft.Count == 0) { removeArmyConfirm = false; return; }
                draft.RemoveArmy(draft[selected].teamId); ClampSelection(); RefreshAfterUiChange(); removeArmyConfirm = false; legionOpen = false;
            });
            DpOn("ra-cancel", () => removeArmyConfirm = false);
            dpTdTitle = r.Q<Label>("td-title"); dpTdWarning = r.Q<Label>("td-warning");
            DpOn("td-cancel", () => { tightDialog = 0; clearFocusRequested = true; });
            dpTdConfirm = DpOn("td-confirm", () => { if (tightDialog == 1) LoadTightTrial(); else { tightDialog = 0; Apply(true); } });
        }

        private void UpdateArmyMenu(int team)
        {
            var ids = ArmyIds();
            string key = string.Join(",", ids);
            if (key != dpAmKey)
            {
                dpAmKey = key; dpAmList.Clear(); dpAmButtons.Clear();
                foreach (int id in ids)
                {
                    int target = id;
                    var b = MiniButton(WarSandboxBattleController.DefaultArmyName(target), "am-item", () => AssignFormationArmy(target));
                    dpAmList.Add(b); dpAmButtons.Add(new KeyValuePair<int, Button>(target, b));
                }
            }
            foreach (var pair in dpAmButtons) DpOnState(pair.Value, pair.Key == team);
            dpAmNew.SetEnabled(deployment.Draft.NextArmyId() >= 0);
        }

        private void UpdateTightDialog()
        {
            Tk.SetText(dpTdTitle, tightDialog == 1 ? "载入双方各五万人的试验布阵？" : "确认开始高负载试验？");
            Tk.SetText(dpTdWarning, tightDialog == 1 ? "密度0.7，角色体型不变。\n将替换当前草稿，可撤销；建议先保存原方案。\n载入不会自动开战。十万人可能严重卡顿。" : "这不是流畅性能保证，可能明显卡顿。\n保持现有碰撞、禁行与占地检查，不缩小角色。\n确认后才会应用当前布阵并开始战斗。");
            dpTdConfirm.text = tightDialog == 1 ? "确认载入草稿" : "确认开战";
        }
    }
}

