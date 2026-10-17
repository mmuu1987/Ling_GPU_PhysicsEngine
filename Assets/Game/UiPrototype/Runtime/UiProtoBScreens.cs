using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace MassEngine.UiPrototype
{
    static class UI
    {
        public static Label L(string text, params string[] cls)
        {
            var l = new Label(text);
            foreach (var c in cls) l.AddToClassList(c);
            return l;
        }

        public static VisualElement E(params string[] cls)
        {
            var e = new VisualElement();
            foreach (var c in cls) e.AddToClassList(c);
            return e;
        }

        public static bool Toy => TacTheme.Toy;

        public static bool Ctrl => Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
    }

    // =====================================================================================
    sealed class OpsScreen
    {
        readonly UiProtoBApp app;
        readonly ScrollView list;
        readonly List<VisualElement> rows = new List<VisualElement>();
        readonly Label code, title, desc, legions, troops, mode, modeEn, terrain, terrainEn, warnText, est;
        readonly VisualElement warn, objectives, unitsBox;
        readonly TacMapView map;
        int sel = -1;

        public ProtoData.Op Op => ProtoData.Ops[Mathf.Max(0, sel)];

        public OpsScreen(UiProtoBApp app, VisualElement el)
        {
            this.app = app;
            list = el.Q<ScrollView>("ops-scroll");
            code = el.Q<Label>("op-code"); title = el.Q<Label>("op-title"); desc = el.Q<Label>("op-desc");
            legions = el.Q<Label>("st-legions"); troops = el.Q<Label>("st-troops");
            mode = el.Q<Label>("st-mode"); modeEn = el.Q<Label>("st-mode-en");
            terrain = el.Q<Label>("st-terrain"); terrainEn = el.Q<Label>("st-terrain-en");
            warn = el.Q("op-warn"); warnText = el.Q<Label>("op-warn-text"); est = el.Q<Label>("op-est");
            objectives = el.Q("op-objectives"); unitsBox = el.Q("op-units");
            map = el.Q<TacMapView>("op-map");
            el.Q<Label>("ops-count").text = UI.Toy ? $"共 {ProtoData.Ops.Length} 个战场" : "CATALOG // " + ProtoData.Ops.Length;

            string group = null;
            for (int i = 0; i < ProtoData.Ops.Length; i++)
            {
                var op = ProtoData.Ops[i];
                if (op.group != group)
                {
                    group = op.group;
                    int n = 0; foreach (var o in ProtoData.Ops) if (o.group == group) n++;
                    var head = UI.E("list-section");
                    head.Add(UI.L(group, "list-section__t"));
                    head.Add(UI.E("list-section__line"));
                    head.Add(UI.L(n + " 项", "list-section__n"));
                    list.Add(head);
                }
                var row = app.opsRow.Instantiate().Q(className: "op-row");
                row.Q<Label>("idx").text = (i + 1).ToString("00");
                row.Q<Label>("name").text = op.name;
                row.Q<Label>("meta").text = $"{op.legions} 军团 · {op.mode} · {op.terrain}";
                row.Q<Label>("count").text = ProtoData.N(op.troops);
                row.Q<Label>("new").style.display = op.isNew ? DisplayStyle.Flex : DisplayStyle.None;
                int k = i;
                row.RegisterCallback<ClickEvent>(e => { Select(k); if (e.clickCount >= 2) Deploy(); });
                list.Add(row);
                rows.Add(row);
            }
            app.ApplyFonts(list);

            el.Q<Button>("op-deploy").clicked += Deploy;
            el.Q<Button>("op-plans").clicked += () => app.Toast("方案库：原型中未实现");
            el.Q<Button>("op-units-lib").clicked += () => app.Toast("兵种库：原型中未实现");
        }

        public void Select(int i)
        {
            i = Mathf.Clamp(i, 0, rows.Count - 1);
            sel = i;
            for (int r = 0; r < rows.Count; r++) rows[r].EnableInClassList("op-row--sel", r == i);
            var op = ProtoData.Ops[i];
            code.text = UI.Toy ? $"第 {i + 1} 关 · {op.mode}" : $"OP-{i + 1:00} // {op.code} // {op.slug}";
            title.text = op.name;
            desc.text = op.desc;
            legions.text = op.legions.ToString();
            troops.text = ProtoData.N(op.troops);
            mode.text = op.mode; modeEn.text = op.modeEn;
            terrain.text = op.terrain; terrainEn.text = op.terrainEn;
            bool heavy = op.troops > 20000;
            warn.style.display = heavy ? DisplayStyle.Flex : DisplayStyle.None;
            warnText.text = op.troops >= 150000
                ? "兵力 " + ProtoData.N(op.troops) + "：超出已验收规模，帧率可能明显下降。"
                : "兵力超过 20,000：首次进入会编译着色器变体，约需 30 秒。";
            est.text = "预计载入 ~" + (2 + op.troops / 25000) + " s";

            objectives.Clear();
            for (int o = 0; o < op.objectives.Length; o++)
            {
                var line = UI.E("objective");
                line.Add(UI.E("objective__mark", o == 0 ? "objective__mark--done" : "objective__mark"));
                line.Add(UI.L(op.objectives[o], "objective__t"));
                objectives.Add(line);
            }
            unitsBox.Clear();
            foreach (var u in op.units) unitsBox.Add(UI.L(u, "chip", "chip--unit"));
            app.ApplyFonts(objectives); app.ApplyFonts(unitsBox);

            map.seed = op.seed;
            map.showZones = true;
            map.armies.Clear();
            int perSide = Mathf.Max(1, (op.legions + 1) / 2);
            var rng = new System.Random(op.seed);
            for (int s = 0; s < 2; s++)
                for (int k = 0; k < perSide; k++)
                {
                    float y = 0.18f + 0.64f * (k + 0.5f) / perSide;
                    float wN = 0.06f + 0.04f * (float)rng.NextDouble(), hN = 0.5f / perSide;
                    float x = s == 0 ? 0.24f - wN * 0.5f : 0.76f - wN * 0.5f;
                    map.armies.Add(new MapArmy { rect = new Rect(x, y - hN * 0.5f, wN, hN), color = s == 0 ? ProtoData.Atk : ProtoData.Def, alive = 0.85f, side = s });
                }
            map.Refresh();
            var target = rows[i];
            list.schedule.Execute(() => list.ScrollTo(target));
        }

        public void OnKeys()
        {
            if (Input.GetKeyDown(KeyCode.DownArrow)) Select(sel + 1);
            if (Input.GetKeyDown(KeyCode.UpArrow)) Select(sel - 1);
            if (UiProtoBApp.Enter()) Deploy();
        }

        void Deploy()
        {
            app.deploy.LoadOp(Op);
            app.Show("deploy");
        }
    }

    // =====================================================================================
    sealed class DeployScreen
    {
        readonly UiProtoBApp app;
        readonly TacDeployMap map;
        readonly ScrollView compList;
        readonly List<VisualElement> cards = new List<VisualElement>();
        readonly Label status, statusSub, total, totalAtkT, totalDefT, cursor, bannerText, code, forces,
            formTitle, countHint, densityV, aspectV, sizeL, unitName, unitEn, unitTier, unitCode, statTitle;
        readonly VisualElement totalAtk, totalDef, banner, foot, unitChips, unitStats, statRows;
        readonly TacIcon statusIcon, unitIcon;
        readonly Button save, apply, start, undoB, redoB;
        readonly Button[] tabs;
        readonly VisualElement[] panes;
        readonly TextField fCount;
        readonly TacSlider fDensity, fAspect;
        readonly TacSeg fFacing;
        readonly TacCycler fShape;
        readonly TacToggle fLock;
        readonly List<List<DeployComp>> undo = new List<List<DeployComp>>(), redo = new List<List<DeployComp>>();
        List<DeployComp> prev, lastApplied;
        bool dirty;

        public IReadOnlyList<DeployComp> Comps => map.comps;
        public ProtoData.Op Op { get; private set; } = ProtoData.Ops[0];
        DeployComp Sel => map.selected >= 0 && map.selected < map.comps.Count ? map.comps[map.selected] : null;
        bool AnyInvalid { get { foreach (var c in map.comps) if (c.Invalid) return true; return false; } }
        int Total { get { int t = 0; foreach (var c in map.comps) t += c.count; return t; } }

        public DeployScreen(UiProtoBApp app, VisualElement el)
        {
            this.app = app;
            map = el.Q<TacDeployMap>("deploy-map");
            compList = el.Q<ScrollView>("comp-scroll");
            status = el.Q<Label>("dp-status"); statusSub = el.Q<Label>("dp-status-sub"); statusIcon = el.Q<TacIcon>("dp-status-icon");
            foot = el.Q(className: "deploy-foot");
            total = el.Q<Label>("dp-total"); totalAtkT = el.Q<Label>("dp-total-atk-t"); totalDefT = el.Q<Label>("dp-total-def-t");
            totalAtk = el.Q("dp-total-atk"); totalDef = el.Q("dp-total-def");
            cursor = el.Q<Label>("map-cursor");
            banner = el.Q("map-banner"); bannerText = el.Q<Label>("map-banner-text");
            code = el.Q<Label>("dp-code"); forces = el.Q<Label>("dp-forces");
            formTitle = el.Q<Label>("form-title"); countHint = el.Q<Label>("f-count-hint");
            densityV = el.Q<Label>("f-density-v"); aspectV = el.Q<Label>("f-aspect-v"); sizeL = el.Q<Label>("f-size");
            unitName = el.Q<Label>("unit-name"); unitEn = el.Q<Label>("unit-en"); unitTier = el.Q<Label>("unit-tier"); unitCode = el.Q<Label>("unit-code");
            unitIcon = el.Q<TacIcon>("unit-icon"); unitChips = el.Q("unit-chips"); unitStats = el.Q("unit-stats");
            statTitle = el.Q<Label>("stat-title"); statRows = el.Q("stat-rows");
            save = el.Q<Button>("dp-save"); apply = el.Q<Button>("dp-apply"); start = el.Q<Button>("dp-start");
            undoB = el.Q<Button>("map-undo"); redoB = el.Q<Button>("map-redo");
            tabs = new[] { el.Q<Button>("tab-unit"), el.Q<Button>("tab-form"), el.Q<Button>("tab-stat") };
            panes = new[] { el.Q("pane-unit"), el.Q("pane-form"), el.Q("pane-stat") };
            fCount = el.Q<TextField>("f-count");
            fDensity = el.Q<TacSlider>("f-density"); fAspect = el.Q<TacSlider>("f-aspect");
            fFacing = el.Q<TacSeg>("f-facing"); fShape = el.Q<TacCycler>("f-shape"); fLock = el.Q<TacToggle>("f-lock");

            for (int i = 0; i < tabs.Length; i++) { int k = i; tabs[i].clicked += () => SelectTab(k); }

            map.changed += () => { RefreshCards(); RefreshStatus(); RefreshForm(); RefreshTotals(); };
            map.committed += Commit;
            map.selectionChanged += _ => { RefreshCards(); RefreshForm(); RefreshUnit(); RefreshStats(); };
            map.hover += w => cursor.text = $"X {Mathf.Clamp(w.x, 0, 1024):0000}  Z {Mathf.Clamp(w.y, 0, 1024):0000}";
            el.Q<TacSeg>("map-tool").changed += i => map.tool = i;
            el.Q<TacToggle>("map-snap").changed += v => map.snap = v;
            undoB.clicked += Undo; redoB.clicked += Redo;
            el.Q<Button>("map-center").clicked += () => app.Toast("点击放置：先选编成，再点击地图（原型中未实现）");
            el.Q<Button>("map-banner-undo").clicked += RevertToValid;

            fCount.isDelayed = true;
            fCount.RegisterValueChangedCallback(e => ApplyCount(e.newValue));
            fDensity.changed += v => { var c = Sel; if (c == null) return; c.density = v; c.Resize(); map.Validate(); map.Refresh(); RefreshCards(); RefreshStatus(); RefreshForm(); };
            fDensity.released += Commit;
            fAspect.changed += v => { var c = Sel; if (c == null) return; c.aspect = v; c.Resize(); map.Validate(); map.Refresh(); RefreshCards(); RefreshStatus(); RefreshForm(); };
            fAspect.released += Commit;
            fFacing.changed += i => { var c = Sel; if (c == null) return; c.facing = i; c.Resize(); Commit(); };
            fShape.changed += i =>
            {
                var c = Sel; if (c == null) return;
                c.aspect = i == 0 ? 1f : i == 1 ? 3f : i == 2 ? 0.4f : 2f;
                if (i == 3) c.density = Mathf.Max(0.05f, c.density * 0.5f);
                c.Resize(); Commit();
            };

            el.Q<Button>("comp-add").clicked += AddComp;
            el.Q<Button>("comp-copy").clicked += CopyComp;
            el.Q<Button>("comp-del").clicked += DeleteComp;
            el.Q<Button>("dp-revert").clicked += () => { Restore(lastApplied); prev = Snapshot(); dirty = false; RefreshAll(); app.Toast("已恢复到上次应用的布阵"); };
            save.clicked += () => app.Toast("方案已保存（模拟）");
            apply.clicked += () => { lastApplied = Snapshot(); dirty = false; RefreshAll(); app.Toast("已应用到本局"); };
            start.clicked += StartBattle;
            el.Q<Button>("dp-back").clicked += () => app.Show("ops");
            el.Q<Button>("dp-plans").clicked += () => app.Toast("方案库：原型中未实现");
            el.Q<Button>("dp-units").clicked += () => app.Toast("兵种库：原型中未实现");
            el.Q<Button>("unit-swap").clicked += () => app.Toast("更换兵种：原型中未实现");
            el.Q<Button>("stat-reset").clicked += () => app.Toast("已恢复默认数值（模拟）");

            LoadOp(ProtoData.Ops[0]);
            SelectTab(0);
        }

        public void LoadOp(ProtoData.Op op)
        {
            Op = op;
            code.text = UI.Toy ? op.name : "BATTLE PLANNER // " + op.slug;
            map.comps.Clear();
            foreach (var s in ProtoData.Comps)
            {
                var u = ProtoData.Units[s.unit];
                var c = new DeployComp { unit = s.unit, name = u.name, side = s.side, color = s.color, count = s.count, density = s.density, aspect = s.aspect, facing = s.side == 0 ? 0 : 2, center = s.center };
                c.Resize();
                map.comps.Add(c);
            }
            map.Validate();
            undo.Clear(); redo.Clear();
            prev = Snapshot(); lastApplied = Snapshot(); dirty = false;
            map.selected = -1;
            RebuildCards();
            map.Select(0);
            RefreshAll();
        }

        List<DeployComp> Snapshot()
        {
            var l = new List<DeployComp>(map.comps.Count);
            foreach (var c in map.comps) l.Add(c.Clone());
            return l;
        }

        void Restore(List<DeployComp> s)
        {
            int keep = map.selected;
            map.comps.Clear();
            foreach (var c in s) map.comps.Add(c.Clone());
            map.selected = Mathf.Min(keep, map.comps.Count - 1);
            map.Validate();
            if (cards.Count != map.comps.Count) RebuildCards();
        }

        void Commit()
        {
            undo.Add(prev);
            if (undo.Count > 32) undo.RemoveAt(0);
            prev = Snapshot();
            redo.Clear();
            dirty = true;
            RefreshAll();
        }

        void Undo()
        {
            if (undo.Count == 0) return;
            redo.Add(Snapshot());
            var s = undo[undo.Count - 1]; undo.RemoveAt(undo.Count - 1);
            Restore(s); prev = Snapshot(); dirty = true;
            RefreshAll();
        }

        void Redo()
        {
            if (redo.Count == 0) return;
            undo.Add(Snapshot());
            var s = redo[redo.Count - 1]; redo.RemoveAt(redo.Count - 1);
            Restore(s); prev = Snapshot(); dirty = true;
            RefreshAll();
        }

        void RevertToValid()
        {
            int guard = 64;
            while (AnyInvalid && undo.Count > 0 && guard-- > 0) Undo();
            if (AnyInvalid) { Restore(lastApplied); prev = Snapshot(); RefreshAll(); }
            app.Toast("已撤销到上一次有效布阵");
        }

        public void SelectTab(int i)
        {
            for (int k = 0; k < tabs.Length; k++)
            {
                tabs[k].EnableInClassList("tab--on", k == i);
                panes[k].style.display = k == i ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        public void DemoInvalid()
        {
            map.Select(0);
            var c = map.comps[0];
            c.center = new Vector2(288, 704);
            map.Validate();
            Commit();
        }

        void RebuildCards()
        {
            compList.Clear();
            cards.Clear();
            for (int i = 0; i < map.comps.Count; i++) cards.Add(null);
            for (int side = 0; side < 2; side++)
            {
                var head = UI.E("side-head");
                if (side == 0) head.AddToClassList("side-head--first");
                var sw = UI.E("side-head__sw"); sw.style.backgroundColor = side == 0 ? ProtoData.Atk : ProtoData.Def;
                head.Add(sw);
                head.Add(UI.L(side == 0 ? (UI.Toy ? "攻方" : "攻方 · ATTACKER") : (UI.Toy ? "守方" : "守方 · DEFENDER"), "side-head__t"));
                head.Add(UI.E("spacer"));
                int n = 0; foreach (var c in map.comps) if (c.side == side) n++;
                head.Add(UI.L(n + " 个编成", "side-head__n"));
                compList.Add(head);
                for (int i = 0; i < map.comps.Count; i++)
                {
                    if (map.comps[i].side != side) continue;
                    var card = app.compCard.Instantiate().Q(className: "comp-card");
                    int k = i;
                    card.RegisterCallback<ClickEvent>(_ => map.Select(k));
                    card.Q("stripe").style.backgroundColor = map.comps[i].color;
                    card.Q("share").style.backgroundColor = map.comps[i].color;
                    compList.Add(card);
                    cards[i] = card;
                }
            }
            app.ApplyFonts(compList);
            forces.text = UI.Toy ? $"{map.comps.Count} 个编成" : "FORCES // " + map.comps.Count;
        }

        void RefreshAll()
        {
            map.Validate();
            RefreshCards(); RefreshTotals(); RefreshStatus(); RefreshForm(); RefreshUnit(); RefreshStats();
            map.Refresh();
        }

        void RefreshCards()
        {
            int[] sideTotal = new int[2];
            foreach (var c in map.comps) sideTotal[c.side] += c.count;
            for (int i = 0; i < map.comps.Count && i < cards.Count; i++)
            {
                var c = map.comps[i]; var card = cards[i]; if (card == null) continue;
                var u = ProtoData.Units[c.unit];
                card.Q<Label>("idx").text = "编成 " + (i + 1);
                card.Q<Label>("name").text = u.name;
                card.Q<Label>("role").text = u.role + " · " + u.size;
                card.Q<Label>("count").text = ProtoData.N(c.count);
                card.Q<Label>("size").text = $"{c.size.x:0} × {c.size.y:0} m";
                card.Q("share").style.width = Length.Percent(100f * c.count / Mathf.Max(1, sideTotal[c.side]));
                card.EnableInClassList("comp-card--sel", i == map.selected);
                card.EnableInClassList("comp-card--bad", c.Invalid);
            }
        }

        void RefreshTotals()
        {
            int a = 0, d = 0;
            foreach (var c in map.comps) if (c.side == 0) a += c.count; else d += c.count;
            // 2026-10-17: no unit cap; the bar shows the attack / defence split of the current total.
            total.text = ProtoData.N(a + d);
            float sum = Mathf.Max(1f, (float)a + d);
            totalAtk.style.width = Length.Percent(100f * a / sum);
            totalDef.style.width = Length.Percent(100f * d / sum);
            totalAtkT.text = "攻方 " + ProtoData.N(a);
            totalDefT.text = "守方 " + ProtoData.N(d);
        }

        void RefreshStatus()
        {
            int bad = -1;
            for (int i = 0; i < map.comps.Count; i++) if (map.comps[i].Invalid) { bad = i; break; }
            foot.RemoveFromClassList("deploy-foot--bad"); foot.RemoveFromClassList("deploy-foot--dirty");
            if (bad >= 0)
            {
                banner.AddToClassList("map-banner--on");
                bannerText.text = $"编成 {bad + 1}：{map.comps[bad].reason}。可以继续调整，或按 Esc 撤销到上一次有效布阵。";
                foot.AddToClassList("deploy-foot--bad");
                status.text = "布阵无效 · 不能开战";
                statusSub.text = "保存方案、应用布阵、开战已禁用";
                statusIcon.icon = "warn";
            }
            else
            {
                banner.RemoveFromClassList("map-banner--on");
                if (dirty)
                {
                    foot.AddToClassList("deploy-foot--dirty");
                    status.text = "已修改 · 尚未应用";
                    statusSub.text = "应用后才会更新本局；可撤销 " + undo.Count + " 步";
                    statusIcon.icon = "info";
                }
                else
                {
                    status.text = "布阵有效 · 可开战";
                    statusSub.text = "已应用到本局 · 方案未保存";
                    statusIcon.icon = "check";
                }
            }
            bool ok = bad < 0;
            save.SetEnabled(ok); apply.SetEnabled(ok && dirty); start.SetEnabled(ok);
            start.EnableInClassList("pulse", ok);
            undoB.SetEnabled(undo.Count > 0); redoB.SetEnabled(redo.Count > 0);
        }

        void RefreshForm()
        {
            var c = Sel;
            if (c == null) { formTitle.text = "未选择编成"; return; }
            var u = ProtoData.Units[c.unit];
            formTitle.text = $"编成 {map.selected + 1} · {u.name}";
            if (!fCount.ClassListContains("tfield--error")) fCount.SetValueWithoutNotify(c.count.ToString());
            fDensity.SetValueWithoutNotify(c.density);
            densityV.text = $"{c.density:0.00} 人/m²";
            fAspect.SetValueWithoutNotify(c.aspect);
            aspectV.text = $"{c.aspect:0.0}";
            fFacing.index = c.facing;
            sizeL.text = $"脚印 {c.size.x:0} × {c.size.y:0} m · 面积 {c.size.x * c.size.y:N0} m²";
        }

        void RefreshUnit()
        {
            var c = Sel; if (c == null) return;
            var u = ProtoData.Units[c.unit];
            unitName.text = u.name; unitEn.text = u.en; unitTier.text = u.tier; unitCode.text = UI.Toy ? "兵种介绍" : "UNIT // " + u.en;
            unitIcon.icon = u.icon;
            unitChips.Clear();
            foreach (var t in new[] { u.role, u.size, $"体型 r {u.radius:0.0#} m", c.side == 0 ? "攻方" : "守方" })
                unitChips.Add(UI.L(t, "chip", "chip--unit"));
            unitStats.Clear();
            AddBar("生命", u.hp, 6000f, ProtoData.N(u.hp), true);
            AddBar("攻击", u.atk, 180f, u.atk.ToString(), false);
            AddBar("防御", u.def, 80f, u.def.ToString(), false);
            AddBar("移速", u.speed, 8f, $"{u.speed:0.0} m/s", false);
            AddBar("射程", u.range, 50f, $"{u.range:0.#} m", false);
            AddBar("攻速", 1f / u.interval, 1f, $"{u.interval:0.0} s", false);
            app.ApplyFonts(unitChips); app.ApplyFonts(unitStats);
        }

        void AddBar(string k, float v, float max, string text, bool log)
        {
            var row = UI.E("sbar-row");
            row.Add(UI.L(k, "sbar-row__k"));
            var track = UI.E("sbar-row__track");
            var fill = UI.E("sbar-row__fill");
            float t = log ? Mathf.Log10(1 + v) / Mathf.Log10(1 + max) : v / max;
            fill.style.width = Length.Percent(Mathf.Clamp01(t) * 100f);
            track.Add(fill);
            row.Add(track);
            row.Add(UI.L(text, "sbar-row__v"));
            unitStats.Add(row);
        }

        void RefreshStats()
        {
            var c = Sel; if (c == null) return;
            var u = ProtoData.Units[c.unit];
            statTitle.text = "数值覆盖 · " + u.name;
            statRows.Clear();
            Row("生命", u.hp.ToString(), c.unit == "shield" ? "180" : "");
            Row("攻击", u.atk.ToString(), c.unit == "swordsman" ? "28" : "");
            Row("防御", u.def.ToString(), "");
            Row("移速 m/s", u.speed.ToString("0.0"), c.unit == "knight" ? "8.5" : "");
            Row("射程 m", u.range.ToString("0.#"), "");
            Row("攻击间隔 s", u.interval.ToString("0.0"), "");
            app.ApplyFonts(statRows);
        }

        void Row(string k, string b, string o)
        {
            var r = UI.E("trow");
            r.Add(UI.L(k, "tcell", "tcell--k"));
            r.Add(UI.L(b, "tcell"));
            r.Add(UI.L(o == "" ? "—" : o, "tcell", o == "" ? "tcell" : "tcell--ovr"));
            r.Add(UI.L(o == "" ? b : o, "tcell", "tcell--eff"));
            statRows.Add(r);
        }

        void ApplyCount(string s)
        {
            var c = Sel; if (c == null) return;
            s = (s ?? "").Replace(",", "").Replace("，", "").Trim();
            if (!int.TryParse(s, out int n)) { CountError("请输入整数"); return; }
            if (n < 1) { CountError("至少 1 人"); return; }
            fCount.RemoveFromClassList("tfield--error");
            countHint.RemoveFromClassList("field__hint--error");
            countHint.text = "回车确认";
            c.count = n;
            if (fLock.value) c.Resize(); else c.density = n / (c.size.x * c.size.y);
            Commit();
        }

        void CountError(string msg)
        {
            fCount.AddToClassList("tfield--error");
            countHint.AddToClassList("field__hint--error");
            countHint.text = msg + " · 未应用";
        }

        void AddComp()
        {
            var src = Sel ?? map.comps[0];
            var z = map.zones[src.side];
            var c = new DeployComp { unit = src.unit, name = src.name, side = src.side, color = src.color, count = 1000, density = 0.5f, aspect = 2f, facing = src.side == 0 ? 0 : 2, center = z.center };
            c.Resize();
            map.comps.Add(c);
            map.Validate();
            RebuildCards();
            map.Select(map.comps.Count - 1);
            Commit();
        }

        void CopyComp()
        {
            var src = Sel; if (src == null) return;
            var c = src.Clone();
            c.center += new Vector2(src.side == 0 ? -src.size.x - 16 : src.size.x + 16, 0);
            map.comps.Add(c);
            map.Validate();
            RebuildCards();
            map.Select(map.comps.Count - 1);
            Commit();
        }

        void DeleteComp()
        {
            if (Sel == null || map.comps.Count <= 1) return;
            map.comps.RemoveAt(map.selected);
            map.selected = Mathf.Min(map.selected, map.comps.Count - 1);
            map.Validate();
            RebuildCards();
            Commit();
        }

        void StartBattle()
        {
            if (AnyInvalid) return;
            lastApplied = Snapshot(); dirty = false; RefreshAll();
            app.battle.StartFrom(map.comps, Op);
            app.Show("battle");
        }

        public void OnKeys()
        {
            if (Input.GetKeyDown(KeyCode.Escape)) { if (AnyInvalid) RevertToValid(); else app.Show("ops"); }
            if (UI.Ctrl && Input.GetKeyDown(KeyCode.Z)) Undo();
            if (UI.Ctrl && Input.GetKeyDown(KeyCode.Y)) Redo();
            if (UiProtoBApp.Enter()) StartBattle();
            if (Input.GetKeyDown(KeyCode.Delete)) DeleteComp();
            if (!UI.Ctrl && Input.GetKeyDown(KeyCode.R) && Sel != null) { map.Rotate(map.selected); Commit(); }
            if (Input.GetKeyDown(KeyCode.Tab) && map.comps.Count > 0) map.Select((map.selected + 1) % map.comps.Count);
        }
    }

    // =====================================================================================
    sealed class BattleScreen
    {
        internal sealed class Army
        {
            public string name, unit, order = "进攻";
            public int side, start;
            public Color color;
            public float alive, kills;
            public Rect rect;
            public bool ranged;
        }

        static readonly float[] Speeds = { 0.5f, 1f, 2f, 4f };
        readonly UiProtoBApp app;
        internal readonly List<Army> armies = new List<Army>();
        readonly List<VisualElement> cards = new List<VisualElement>();
        readonly ScrollView armyList;
        readonly VisualElement logList, barAtk, barDef, selHp, selStripe, phaseDot, techPanel;
        readonly Label clock, atkT, defT, speedTag, name, code, phase, mode, selCode, selOrder, selName, selCount, selStart, selPct, selLoss, selKills, selMorale, pauseText, techText;
        readonly TacIcon pauseIcon;
        readonly Button pauseB, follow, overview;
        readonly Dictionary<string, Button> cmds = new Dictionary<string, Button>();
        readonly TacMapView world, minimap;
        readonly TacSeg speedSeg;
        int sel;
        bool paused, active, finished, contact;
        float speed = 1f, uiTimer;
        public float Time { get; private set; }
        public bool Finished => finished;
        public ProtoData.Op Op { get; private set; } = ProtoData.Ops[0];

        public BattleScreen(UiProtoBApp app, VisualElement el)
        {
            this.app = app;
            armyList = el.Q<ScrollView>("b-armies"); logList = el.Q("b-log-list");
            barAtk = el.Q("b-bar-atk"); barDef = el.Q("b-bar-def");
            selHp = el.Q("b-sel-hp"); selStripe = el.Q("b-sel-stripe"); phaseDot = el.Q("b-phase-dot");
            techPanel = el.Q("b-tech-panel"); techText = el.Q<Label>("b-tech-text");
            clock = el.Q<Label>("b-clock"); atkT = el.Q<Label>("b-atk"); defT = el.Q<Label>("b-def"); speedTag = el.Q<Label>("b-speed-tag");
            name = el.Q<Label>("b-name"); code = el.Q<Label>("b-code"); phase = el.Q<Label>("b-phase"); mode = el.Q<Label>("b-mode");
            selCode = el.Q<Label>("b-sel-code"); selOrder = el.Q<Label>("b-sel-order"); selName = el.Q<Label>("b-sel-name");
            selCount = el.Q<Label>("b-sel-count"); selStart = el.Q<Label>("b-sel-start"); selPct = el.Q<Label>("b-sel-pct");
            selLoss = el.Q<Label>("b-sel-loss"); selKills = el.Q<Label>("b-sel-kills"); selMorale = el.Q<Label>("b-sel-morale");
            pauseB = el.Q<Button>("b-pause"); pauseText = el.Q<Label>("b-pause-text"); pauseIcon = el.Q<TacIcon>("b-pause-icon");
            follow = el.Q<Button>("cmd-follow"); overview = el.Q<Button>("cmd-overview");
            world = el.Q<TacMapView>("b-world"); minimap = el.Q<TacMapView>("b-minimap");
            speedSeg = el.Q<TacSeg>("b-speed");

            cmds["进攻"] = el.Q<Button>("cmd-attack"); cmds["移动"] = el.Q<Button>("cmd-move");
            cmds["防守"] = el.Q<Button>("cmd-hold"); cmds["撤退"] = el.Q<Button>("cmd-retreat");
            foreach (var kv in cmds) { var o = kv.Key; kv.Value.clicked += () => Order(o); }
            pauseB.clicked += TogglePause;
            follow.clicked += () => { follow.ToggleInClassList("cmd--on"); Log(follow.ClassListContains("cmd--on") ? "镜头跟随军团 " + (sel + 1) : "取消跟随", "cmd"); };
            overview.clicked += () => { overview.ToggleInClassList("cmd--on"); Log(overview.ClassListContains("cmd--on") ? "全景视角" : "退出全景", "cmd"); };
            speedSeg.changed += i => { speed = Speeds[i]; speedTag.text = "×" + Speeds[i].ToString("0.#"); Log("速度 ×" + Speeds[i].ToString("0.#"), "cmd"); };
            el.Q<Button>("b-help").clicked += () => app.Toast("左键框选 · 右键下达移动 · A/M/H/R 命令 · 1–9 选军团");
            el.Q<Button>("b-tech").clicked += () => techPanel.ToggleInClassList("b-techp--on");
            el.Q<Button>("b-settings").clicked += () => app.OpenModal("settings");
            el.Q<Button>("b-catalog").clicked += () => app.Show("ops");
            el.Q<Button>("b-exit").clicked += () => app.Toast("退出：原型中不会关闭程序");
            el.Q<Button>("b-redeploy").clicked += () => app.Show("deploy");
            el.Q<Button>("b-restart").clicked += () => StartFrom(app.deploy.Comps, Op);
            el.Q<Button>("b-end").clicked += FinishNow;
            minimap.clicked += p =>
            {
                minimap.cameraView = new Rect(Mathf.Clamp(p.x - 0.18f, 0, 0.64f), Mathf.Clamp(p.y - 0.16f, 0, 0.68f), 0.36f, 0.32f);
                minimap.Refresh();
            };
            el.schedule.Execute(Tick).Every(100);
        }

        public void SetActive(bool a)
        {
            active = a;
            if (a && armies.Count == 0) StartFrom(app.deploy.Comps, app.deploy.Op);
        }

        public void StartFrom(IReadOnlyList<DeployComp> comps, ProtoData.Op op)
        {
            Op = op;
            armies.Clear();
            foreach (var c in comps)
            {
                var u = ProtoData.Units[c.unit];
                var r = c.Rect;
                armies.Add(new Army
                {
                    name = (c.side == 0 ? "攻方 · " : "守方 · ") + u.name, unit = c.unit, side = c.side, color = c.color,
                    start = c.count, alive = c.count, ranged = u.role == "远程",
                    order = c.side == 0 ? "进攻" : "防守",
                    rect = new Rect(0.06f + r.x / 1024f * 0.88f, 0.16f + r.y / 1024f * 0.7f, Mathf.Max(0.006f, r.width / 1024f * 0.88f), Mathf.Max(0.01f, r.height / 1024f * 0.7f)),
                });
            }
            Time = 0; paused = false; finished = false; contact = false; sel = 0;
            name.text = op.name; code.text = UI.Toy ? "当前战场" : "OP // " + op.slug; mode.text = "· " + op.mode;
            logList.Clear();
            Log("战斗开始 · " + ProtoData.N(Total(0)) + " 对 " + ProtoData.N(Total(1)), "cmd");
            RebuildCards();
            SetPaused(false);
            Refresh();
        }

        /// <summary>Fast-forward for screenshots.</summary>
        public void Demo()
        {
            if (armies.Count == 0) StartFrom(app.deploy.Comps, app.deploy.Op);
            for (int i = 0; i < 700 && !finished; i++) Step(0.1f);
            sel = 0;
            Refresh();
        }

        int Total(int side) { float t = 0; foreach (var a in armies) if (a.side == side) t += a.alive; return Mathf.RoundToInt(t); }
        int Start(int side) { int t = 0; foreach (var a in armies) if (a.side == side) t += a.start; return t; }

        void Tick()
        {
            if (!active || paused || finished || armies.Count == 0) return;
            Step(0.1f * speed);
            world.time = minimap.time = Time;
            world.Refresh(); minimap.Refresh();
            uiTimer += 0.1f;
            if (uiTimer >= 0.25f) { uiTimer = 0; Refresh(); }
            if (finished) el_Schedule();
        }

        void el_Schedule() => app.OpenModal("result");

        void Step(float dt)
        {
            Time += dt;
            float[] power = new float[2];
            foreach (var a in armies)
            {
                if (a.alive <= 0.5f) continue;
                var u = ProtoData.Units[a.unit];
                float dir = a.side == 0 ? 1f : -1f;
                float front = a.side == 0 ? a.rect.xMax : a.rect.xMin;
                float target = a.ranged ? (a.side == 0 ? 0.36f : 0.64f) : (a.side == 0 ? 0.495f : 0.505f);
                float v = 0f;
                if (a.order == "进攻" || a.order == "移动") v = (a.order == "移动" ? 0.006f : 0.004f) * Mathf.Min(2f, u.speed / 3f);
                if (a.order == "撤退") v = -0.006f;
                if (a.order == "防守") v = 0f;
                if (v > 0 && dir * (target - front) <= 0) v = 0;
                a.rect.x = Mathf.Clamp(a.rect.x + dir * v * dt * 10f, 0.01f, 0.99f - a.rect.width);
                bool engaged = a.ranged ? Time > 8f : Mathf.Abs(front - 0.5f) < 0.02f;
                if (engaged) power[a.side] += a.alive * (u.atk / 20f) * (a.order == "撤退" ? 0.2f : 1f);
            }
            if (power[0] > 0 && power[1] > 0)
            {
                if (!contact) { contact = true; Log("前线接敌", "warn"); }
                for (int side = 0; side < 2; side++)
                {
                    float dmg = power[1 - side] * 0.0012f * dt;
                    float pool = 0; foreach (var a in armies) if (a.side == side && a.alive > 0) pool += a.alive * Exposure(a);
                    if (pool <= 0) continue;
                    foreach (var a in armies)
                    {
                        if (a.side != side || a.alive <= 0) continue;
                        float before = a.alive;
                        float loss = dmg * a.alive * Exposure(a) / pool;
                        a.alive = Mathf.Max(0, a.alive - loss);
                        if (before >= a.start * 0.5f && a.alive < a.start * 0.5f) Log(a.name + " 兵力跌破 50%", "warn");
                        if (before > 0.5f && a.alive <= 0.5f) Log(a.name + " 全军覆没", "warn");
                        float share = 0; foreach (var e in armies) if (e.side != side && e.alive > 0) share += e.alive;
                        foreach (var e in armies) if (e.side != side && e.alive > 0) e.kills += loss * e.alive / Mathf.Max(1f, share);
                    }
                }
            }
            if (Total(0) < Start(0) * 0.02f || Total(1) < Start(1) * 0.02f) finished = true;
        }

        static float Exposure(Army a) => (a.ranged ? 0.35f : 1f) * (a.order == "防守" ? 0.7f : 1f);

        void RebuildCards()
        {
            armyList.Clear(); cards.Clear();
            for (int i = 0; i < armies.Count; i++)
            {
                var card = app.armyCard.Instantiate().Q(className: "army-card");
                int k = i;
                card.RegisterCallback<ClickEvent>(_ => Select(k));
                card.Q("stripe").style.backgroundColor = armies[i].color;
                card.Q<Label>("key").text = (i + 1).ToString();
                card.Q<Label>("name").text = armies[i].name;
                armyList.Add(card);
                cards.Add(card);
            }
            app.ApplyFonts(armyList);
        }

        void Select(int i)
        {
            if (i < 0 || i >= armies.Count) return;
            sel = i;
            Refresh();
        }

        void Order(string o)
        {
            if (sel >= armies.Count) return;
            armies[sel].order = o;
            Log($"军团 {sel + 1} · {armies[sel].name} → {o}", "cmd");
            Refresh();
        }

        void TogglePause() { SetPaused(!paused); Log(paused ? "已暂停" : "继续", "cmd"); }

        void SetPaused(bool p)
        {
            paused = p;
            pauseText.text = p ? "继续" : "暂停";
            pauseIcon.icon = p ? "play" : "pause";
            pauseB.EnableInClassList("cmd--paused", p);
            phase.text = finished ? "已结束" : p ? "已暂停" : "交战中";
            phaseDot.EnableInClassList("status-dot--paused", p);
        }

        public void FinishNow()
        {
            if (armies.Count == 0) StartFrom(app.deploy.Comps, app.deploy.Op);
            if (!finished) { finished = true; Log("本局结束", "warn"); }
            Refresh();
            app.OpenModal("result");
        }

        static string OrderClass(string o) => o == "防守" ? "chip--order-hold" : o == "撤退" ? "chip--order-retreat" : o == "移动" ? "chip--order-move" : "";

        void Refresh()
        {
            if (armies.Count == 0) return;
            int a = Total(0), d = Total(1);
            atkT.text = ProtoData.N(a); defT.text = ProtoData.N(d);
            barAtk.style.flexGrow = Mathf.Max(0.001f, a); barDef.style.flexGrow = Mathf.Max(0.001f, d);
            int s = Mathf.FloorToInt(Time);
            clock.text = $"{s / 60:00}:{s % 60:00}";
            phase.text = finished ? "已结束" : paused ? "已暂停" : contact ? "交战中" : "推进中";

            var x = armies[Mathf.Clamp(sel, 0, armies.Count - 1)];
            selCode.text = UI.Toy ? $"当前选中 · 军团 {sel + 1}" : $"SELECTED // 军团 {sel + 1}";
            selName.text = x.name;
            selStripe.style.backgroundColor = x.color;
            selCount.text = ProtoData.N(x.alive);
            selStart.text = "/ " + ProtoData.N(x.start);
            float pct = x.alive / Mathf.Max(1f, x.start);
            selPct.text = Mathf.RoundToInt(pct * 100) + "%";
            selHp.style.width = Length.Percent(pct * 100f);
            selHp.EnableInClassList("hp__fill--mid", pct < 0.6f && pct >= 0.3f);
            selHp.EnableInClassList("hp__fill--low", pct < 0.3f);
            selLoss.text = ProtoData.N(x.start - x.alive);
            selKills.text = ProtoData.N(x.kills);
            selMorale.text = pct > 0.7f ? "高" : pct > 0.4f ? "动摇" : "崩溃边缘";
            selOrder.text = x.order;
            foreach (var c in new[] { "chip--order-hold", "chip--order-retreat", "chip--order-move" }) selOrder.RemoveFromClassList(c);
            if (OrderClass(x.order) != "") selOrder.AddToClassList(OrderClass(x.order));
            foreach (var kv in cmds) kv.Value.EnableInClassList("cmd--on", kv.Key == x.order);

            for (int i = 0; i < armies.Count && i < cards.Count; i++)
            {
                var ar = armies[i]; var card = cards[i];
                float p = ar.alive / Mathf.Max(1f, ar.start);
                card.Q<Label>("count").text = ProtoData.N(ar.alive);
                var ord = card.Q<Label>("order");
                ord.text = ar.order;
                foreach (var c in new[] { "chip--order-hold", "chip--order-retreat", "chip--order-move" }) ord.RemoveFromClassList(c);
                if (OrderClass(ar.order) != "") ord.AddToClassList(OrderClass(ar.order));
                var hp = card.Q("hp");
                hp.style.width = Length.Percent(p * 100f);
                hp.EnableInClassList("hp__fill--mid", p < 0.6f && p >= 0.3f);
                hp.EnableInClassList("hp__fill--low", p < 0.3f);
                card.EnableInClassList("army-card--sel", i == sel);
                card.EnableInClassList("army-card--dead", ar.alive < 1f);
            }

            world.armies.Clear(); minimap.armies.Clear();
            for (int i = 0; i < armies.Count; i++)
            {
                var ar = armies[i];
                var m = new MapArmy { rect = ar.rect, color = ar.color, alive = Mathf.Clamp01(ar.alive / Mathf.Max(1f, ar.start)) * 0.9f + 0.05f, selected = i == sel, side = ar.side };
                if (ar.alive < 1f) m.alive = 0;
                world.armies.Add(m); minimap.armies.Add(m);
            }
            world.Refresh(); minimap.Refresh();
            techText.text = $"units {ProtoData.N(a + d)}  ·  sim 33.3 ms\nframe {14 + (a + d) / 9000f:0.0} ms  ·  gpu {3 + (a + d) / 12000f:0.0} ms\nflow 256×256  ·  cells 3 m";
        }

        void Log(string msg, string kind = "")
        {
            int s = Mathf.FloorToInt(Time);
            var line = UI.E("log-line");
            if (kind != "") line.AddToClassList("log-line--" + kind);
            line.Add(UI.L($"{s / 60:00}:{s % 60:00}", "log-line__t", "mono"));
            line.Add(UI.L(msg, "log-line__m"));
            logList.Insert(0, line);
            while (logList.childCount > 6) logList.RemoveAt(logList.childCount - 1);
            app.ApplyFonts(line);
        }

        public void OnKeys()
        {
            if (Input.GetKeyDown(KeyCode.Space)) TogglePause();
            if (Input.GetKeyDown(KeyCode.A)) Order("进攻");
            if (Input.GetKeyDown(KeyCode.M)) Order("移动");
            if (Input.GetKeyDown(KeyCode.H)) Order("防守");
            if (Input.GetKeyDown(KeyCode.R)) Order("撤退");
            if (Input.GetKeyDown(KeyCode.F)) follow.ToggleInClassList("cmd--on");
            if (Input.GetKeyDown(KeyCode.F3)) overview.ToggleInClassList("cmd--on");
            if (Input.GetKeyDown(KeyCode.Tab)) Select((sel + 1) % Mathf.Max(1, armies.Count));
            for (int i = 0; i < 9; i++) if (Input.GetKeyDown(KeyCode.Alpha1 + i)) Select(i);
        }
    }

    // =====================================================================================
    sealed class ResultModal
    {
        readonly UiProtoBApp app;
        readonly Label title, sub, op, kTotal, kDead, kRatio, kTime;
        readonly VisualElement rowsAtk, rowsDef, sideAtk, sideDef, winAtk, winDef;

        public ResultModal(UiProtoBApp app, VisualElement el)
        {
            this.app = app;
            title = el.Q<Label>("res-title"); sub = el.Q<Label>("res-sub"); op = el.Q<Label>("res-op");
            kTotal = el.Q<Label>("res-k-total"); kDead = el.Q<Label>("res-k-dead"); kRatio = el.Q<Label>("res-k-ratio"); kTime = el.Q<Label>("res-k-time");
            rowsAtk = el.Q("res-rows-atk"); rowsDef = el.Q("res-rows-def");
            sideAtk = el.Q("res-side-atk"); sideDef = el.Q("res-side-def");
            winAtk = el.Q("res-win-atk"); winDef = el.Q("res-win-def");
            el.Q<Button>("res-again").clicked += Again;
            el.Q<Button>("res-redeploy").clicked += () => { app.CloseModal(); app.Show("deploy"); };
            el.Q<Button>("res-catalog").clicked += () => { app.CloseModal(); app.Show("ops"); };
        }

        public void Again()
        {
            app.CloseModal();
            app.battle.StartFrom(app.deploy.Comps, app.deploy.Op);
            app.Show("battle");
        }

        public void Populate(BattleScreen b)
        {
            float[] start = new float[2], alive = new float[2];
            foreach (var a in b.armies) { start[a.side] += a.start; alive[a.side] += a.alive; }
            float fa = alive[0] / Mathf.Max(1f, start[0]), fd = alive[1] / Mathf.Max(1f, start[1]);
            bool decisive = fa < 0.02f || fd < 0.02f;
            int winner = fa >= fd ? 0 : 1;
            title.text = decisive ? (winner == 0 ? "攻方胜利" : "守方胜利") : "手动结束";
            title.EnableInClassList("result__title--draw", !decisive);
            int s = Mathf.FloorToInt(b.Time);
            string time = $"{s / 60:00}:{s % 60:00}";
            sub.text = decisive
                ? $"{b.Op.mode} · {(winner == 0 ? "守方" : "攻方")}全军覆没 · 用时 {time}"
                : $"{b.Op.mode} · 未分胜负 · 当前占优：{(winner == 0 ? "攻方" : "守方")}（{Mathf.RoundToInt(Mathf.Max(fa, fd) * 100)}% 兵力）· 用时 {time}";
            op.text = UI.Toy ? b.Op.name : "OP // " + b.Op.slug;
            float dead = start[0] + start[1] - alive[0] - alive[1];
            kTotal.text = ProtoData.N(start[0] + start[1]);
            kDead.text = ProtoData.N(dead);
            float la = start[0] - alive[0], ld = start[1] - alive[1];
            kRatio.text = la <= 0 ? "—" : $"1 : {ld / Mathf.Max(1f, la):0.0}";
            kTime.text = time;
            winAtk.style.display = decisive && winner == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            winDef.style.display = decisive && winner == 1 ? DisplayStyle.Flex : DisplayStyle.None;
            sideAtk.EnableInClassList("side--win", decisive && winner == 0);
            sideDef.EnableInClassList("side--win", decisive && winner == 1);
            rowsAtk.Clear(); rowsDef.Clear();
            foreach (var a in b.armies)
            {
                var r = UI.E("srow-res");
                r.Add(UI.L(a.name.Substring(5), "scell", "scell--k"));
                r.Add(UI.L(ProtoData.N(a.start), "scell"));
                r.Add(UI.L(ProtoData.N(a.alive), "scell", "scell--alive"));
                r.Add(UI.L(ProtoData.N(a.start - a.alive), "scell", "scell--loss"));
                r.Add(UI.L(ProtoData.N(a.kills), "scell"));
                (a.side == 0 ? rowsAtk : rowsDef).Add(r);
            }
            app.ApplyFonts(rowsAtk); app.ApplyFonts(rowsDef);
        }
    }

    // =====================================================================================
    sealed class SettingsModal
    {
        readonly UiProtoBApp app;
        readonly Button[] tabs;
        readonly VisualElement[] panes;

        public SettingsModal(UiProtoBApp app, VisualElement el)
        {
            this.app = app;
            tabs = new[] { el.Q<Button>("st-video"), el.Q<Button>("st-audio"), el.Q<Button>("st-control"), el.Q<Button>("st-camera") };
            panes = new[] { el.Q("sp-video"), el.Q("sp-audio"), el.Q("sp-control"), el.Q("sp-camera") };
            for (int i = 0; i < tabs.Length; i++) { int k = i; tabs[i].clicked += () => Tab(k); }
            Bind(el, "s-dist", "s-dist-v", v => $"{Mathf.Round(v / 50f) * 50f:0} m");
            Bind(el, "s-master", "s-master-v", v => $"{v:0}%");
            Bind(el, "s-sfx", "s-sfx-v", v => $"{v:0}%");
            Bind(el, "s-amb", "s-amb-v", v => $"{v:0}%");
            Bind(el, "s-pan", "s-pan-v", v => $"{v:0.0}×");
            var keys = el.Q("s-keys");
            for (int i = 0; i < ProtoData.Keymap.GetLength(0); i++)
            {
                var r = UI.E("krow");
                r.Add(UI.L(ProtoData.Keymap[i, 0], "krow__k"));
                r.Add(UI.L(ProtoData.Keymap[i, 1], "krow__v"));
                keys.Add(r);
            }
            el.Q<Button>("set-close").clicked += app.CloseModal;
            el.Q<Button>("set-cancel").clicked += app.CloseModal;
            el.Q("set-dim").RegisterCallback<ClickEvent>(_ => app.CloseModal());
            el.Q<Button>("set-save").clicked += () => { app.CloseModal(); app.Toast("设置已保存（模拟）"); };
            el.Q<Button>("set-defaults").clicked += () => app.Toast("已恢复默认（模拟）");
            Tab(0);
        }

        static void Bind(VisualElement el, string slider, string label, Func<float, string> fmt)
        {
            var s = el.Q<TacSlider>(slider); var l = el.Q<Label>(label);
            if (s == null || l == null) return;
            l.text = fmt(s.value);
            s.changed += v => l.text = fmt(v);
        }

        public void Open() { app.ApplyFonts(panes[0].parent); }

        void Tab(int i)
        {
            for (int k = 0; k < tabs.Length; k++)
            {
                tabs[k].EnableInClassList("vtab--on", k == i);
                panes[k].style.display = k == i ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }
    }
}

