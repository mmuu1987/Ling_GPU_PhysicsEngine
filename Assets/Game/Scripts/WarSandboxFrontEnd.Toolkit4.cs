using System;
using System.Collections.Generic;
using MassEngine.Game.Ui;
using UnityEngine;
using UnityEngine.UIElements;
using Tk = MassEngine.Game.Ui.WarSandboxUiToolkit;

namespace MassEngine.Game
{
    /// <summary>
    /// Phase 4 of the UI Toolkit migration: settings (声音 / 镜头 via SettingsSafety29), help (Guide30), the unit library
    /// (兵种库) and the unit model zoom. View only — every control calls the same method as the uGUI pages in
    /// WarSandboxFrontEnd.Settings/.Settings29/.CameraSettings/.Help30/.UnitLibrary/.UnitPreview, with the same texts; nothing
    /// the game lacks (画面 / 操作 settings) is shown. A separate document (order 20) so these sit above the front end and battle HUD.
    /// </summary>
    public sealed partial class WarSandboxFrontEnd
    {
        private UIDocument pgDoc;
        private VisualElement pgRoot, pgLib, pgSet, pgHelp, pgZoom;
        private TkUnitPreview pgZoomPreview, pgLibPreview;

        private void HidePages()
        {
            if (pgRoot == null) return;
            Tk.Show(pgRoot, false);
            pgZoomPreview?.Deactivate();
            pgLibPreview?.Deactivate();
        }

        private void BuildPages()
        {
            pgDoc = Tk.CreateDocument(transform, "Front End Pages UI Toolkit", "Pages", 20);
            if (pgDoc == null) return;
            var r = pgDoc.rootVisualElement;
            pgRoot = r;
            pgLib = r.Q("lib"); pgSet = r.Q("set"); pgHelp = r.Q("help"); pgZoom = r.Q("zoom");
            BuildSettingsPage(r);
            BuildHelpPage(r);
            BuildLibraryPage(r);
            pgZoomPreview = new TkUnitPreview();
            r.Q("zoom-host").Add(pgZoomPreview);
            PgOn("zoom-close", () => unitPreviewOpen = false);
            Tk.HideScrollers(r);
            r.Query<Button>().ForEach(b => b.focusable = false);
        }

        private Button PgOn(string name, Action action)
        {
            var b = pgRoot.Q<Button>(name);
            if (b != null) b.clicked += () => { action(); nextRefresh = 0; };
            else Debug.LogWarning("[FrontEnd UI Toolkit] missing button " + name);
            return b;
        }

        /// <summary>Called at the end of ToolkitUpdate (every frame while the new UI is on).</summary>
        private void UpdateToolkitPages()
        {
            bool menu = session.State == WarSandboxEntryState.Menu;
            bool help = session.HelpOpen && Guide30.For(session) != null;
            bool settings = session.SettingsOpen && !session.HelpOpen;
            bool library = libraryOpen && menu;
            bool zoom = unitPreviewOpen && menu && !session.SettingsOpen && !session.ConfirmationOpen && !session.HelpOpen;
            if (!help && !settings && !library && !zoom) { HidePages(); return; }
            if (pgDoc == null) BuildPages();
            if (pgRoot == null) return;
            Tk.Show(pgRoot, true);
            Tk.Show(pgHelp, help);
            Tk.Show(pgSet, settings);
            Tk.Show(pgLib, library && !settings && !help);
            Tk.Show(pgZoom, zoom);
            if (help) UpdateHelpPage();
            if (settings) UpdateSettingsPage();
            if (library && !settings && !help) UpdateLibraryPage(); else pgLibPreview?.Deactivate();
            if (zoom) UpdateZoomPage(); else pgZoomPreview?.Deactivate();
        }

        // =====================================================================================================
        // Settings
        // =====================================================================================================
        private VisualElement setMain, setTabs, setAudio, setCamera, setGraphics, setConfirm, setFoot;
        private Label setCode, setTitle, setNote, setVolumeV, setConfirmText, setStatus, setError, setDefaultsText, camPanV, camZoomV, camLookV, camPitchV, camFramingV, gfxShadowV;
        private Button setTabAudio, setTabCamera, setTabGraphics, setDefaultsConfirm, setCancel;
        private TacSlider setVolume, camPan, camZoom, camLook, camPitch, camFraming, gfxShadow;
        private TacToggle setMute, camFollowZoom;

        private void BuildSettingsPage(VisualElement r)
        {
            setMain = r.Q("set-main"); setTabs = r.Q("set-tabs"); setAudio = r.Q("set-audio"); setCamera = r.Q("set-camera"); setGraphics = r.Q("set-graphics");
            setConfirm = r.Q("set-confirm"); setFoot = r.Q("set-foot");
            setCode = r.Q<Label>("set-code"); setTitle = r.Q<Label>("set-title"); setNote = r.Q<Label>("set-note");
            setVolumeV = r.Q<Label>("set-volume-v"); setConfirmText = r.Q<Label>("set-confirm-text"); setStatus = r.Q<Label>("set-status");
            setError = r.Q<Label>("set-error"); setDefaultsText = r.Q<Label>("set-defaults-text");
            camPanV = r.Q<Label>("cam-pan-v"); camZoomV = r.Q<Label>("cam-zoom-v"); camLookV = r.Q<Label>("cam-look-v");
            camPitchV = r.Q<Label>("cam-pitch-v"); camFramingV = r.Q<Label>("cam-framing-v"); gfxShadowV = r.Q<Label>("gfx-shadow-v");
            setVolume = r.Q<TacSlider>("set-volume"); camPan = r.Q<TacSlider>("cam-pan"); camZoom = r.Q<TacSlider>("cam-zoom");
            camLook = r.Q<TacSlider>("cam-look"); camPitch = r.Q<TacSlider>("cam-pitch"); camFraming = r.Q<TacSlider>("cam-framing"); gfxShadow = r.Q<TacSlider>("gfx-shadow");
            setMute = r.Q<TacToggle>("set-mute"); camFollowZoom = r.Q<TacToggle>("cam-follow-zoom");

            setTabAudio = PgOn("set-tab-audio", () => { var s = SettingsSafety29.For(session); if (s != null) s.Page = 0; });
            setTabCamera = PgOn("set-tab-camera", () => { var s = SettingsSafety29.For(session); if (s != null) s.Page = 1; });
            setTabGraphics = PgOn("set-tab-graphics", () => { var s = SettingsSafety29.For(session); if (s != null) s.Page = 2; });
            PgOn("set-preview", () => WarSandboxAudio.Ensure().Play(WarSandboxSoundCue.Start));
            PgOn("set-defaults", () => { var s = SettingsSafety29.For(session); if (s != null) s.RequestDefaults(); else WarSandboxAudio.Ensure().RestoreDefaults(); });
            setDefaultsConfirm = PgOn("set-defaults-confirm", () => SettingsSafety29.For(session)?.ApplyDefaults());
            PgOn("set-defaults-cancel", () => SettingsSafety29.For(session)?.CancelDefaults());
            setCancel = PgOn("set-cancel", () => { var s = SettingsSafety29.For(session); if (s != null) s.Cancel(session); else session.CloseSettings(); });
            PgOn("set-save", () => { var s = SettingsSafety29.For(session); if (s != null) s.Save(session); else CloseSettings(); });

            setVolume.changed += v => WarSandboxAudio.Ensure().SetVolume(v);
            setMute.changed += v => WarSandboxAudio.Ensure().SetMuted(v);
            camPan.changed += v => EditCamera(c => { c.panSpeed = WarSandboxCameraSettings.SliderToMultiplier(v); return c; });
            camZoom.changed += v => EditCamera(c => { c.zoomSpeed = WarSandboxCameraSettings.SliderToMultiplier(v); return c; });
            camLook.changed += v => EditCamera(c => { c.lookSpeed = WarSandboxCameraSettings.SliderToMultiplier(v); return c; });
            camPitch.changed += v => EditCamera(c => { c.focusPitch = Mathf.Round(Mathf.Lerp(30f, 75f, Mathf.Clamp01(v))); return c; });
            camFraming.changed += v => EditCamera(c => { c.framing = Mathf.Round(Mathf.Clamp01(v) * 20f) / 20f; return c; });
            camFollowZoom.changed += v => EditCamera(c => { c.followAutoZoom = v; return c; });
            gfxShadow.changed += v => { var g = WarSandboxGraphicsPrefs.Current; g.shadowDistance = WarSandboxGraphicsSettings.SliderToDistance(v); WarSandboxGraphicsPrefs.Set(g); };
            WarSandboxGraphicsPrefs.Apply(); // also covers scenes entered without the menu session
        }

        private static void PgSlider(TacSlider s, float v)
        {
            if (s.HasPointerCapture(PointerId.mousePointerId)) return; // the slider is being dragged; it already shows the value
            if (!Mathf.Approximately(s.value, v)) s.SetValueWithoutNotify(v);
        }

        private static void PgToggle(TacToggle t, bool v) { if (t.value != v) t.value = v; }

        private void UpdateSettingsPage()
        {
            var safety = SettingsSafety29.For(session);
            safety?.Begin();
            var audio = WarSandboxAudio.Ensure(); var st = audio.Settings;
            bool cam = safety != null && safety.Page == 1, gfx = safety != null && safety.Page == 2, confirm = safety != null && safety.ConfirmDefaults;
            Tk.SetText(setCode, gfx ? "系统设置 · 画面" : cam ? "系统设置 · 镜头" : "系统设置 · 声音");
            Tk.SetText(setTitle, confirm ? (gfx ? "恢复默认画面设置？" : cam ? "恢复默认镜头设置？" : "恢复默认声音设置？") : (safety != null ? "设置" : "声音设置"));
            Tk.Show(setMain, !confirm); Tk.Show(setFoot, !confirm); Tk.Show(setConfirm, confirm);
            if (confirm)
            {
                Tk.SetText(setConfirmText, gfx
                    ? "阴影距离恢复为 " + WarSandboxGraphicsSettings.DefaultShadowDistance.ToString("0") + " 米。\n只影响画面，不改声音、镜头、编成、布阵或本地方案。\n确认后仍需保存；取消修改可恢复打开时的值。"
                    : cam
                    ? "平移、缩放、转向恢复 ×1.00，聚焦俯角 50°，取景适中，跟随自动缩放开启。\n只影响镜头，不改声音、编成、布阵或本地方案。\n确认后仍需保存；取消修改可恢复打开时的值。"
                    : "音效音量恢复为 65%，关闭静音。\n只影响声音，不改编成、布阵或本地方案。\n确认后仍需保存；取消修改可恢复打开时的值。");
                string t = gfx ? "恢复画面默认值" : cam ? "恢复镜头默认值" : "恢复声音默认值";
                if (setDefaultsConfirm.text != t) setDefaultsConfirm.text = t;
                return;
            }
            Tk.Show(setTabs, safety != null);
            setTabAudio.EnableInClassList("vtab--on", !cam && !gfx); setTabCamera.EnableInClassList("vtab--on", cam); setTabGraphics.EnableInClassList("vtab--on", gfx);
            Tk.Show(setAudio, !cam && !gfx); Tk.Show(setCamera, cam); Tk.Show(setGraphics, gfx);
            if (gfx)
            {
                var g = WarSandboxGraphicsPrefs.Current;
                PgSlider(gfxShadow, WarSandboxGraphicsSettings.DistanceToSlider(g.shadowDistance));
                Tk.SetText(gfxShadowV, g.shadowDistance.ToString("0") + " 米");
            }
            else if (!cam)
            {
                Tk.SetText(setNote, safety != null
                    ? "调整立即试听；保存并返回后，下次启动继续使用。\n取消或 Esc 撤销本次调整。打开设置会暂停正在进行的战斗。"
                    : "调整立即生效，保存后下次启动继续使用。\n打开设置时，正在进行的战斗会暂停。");
                PgSlider(setVolume, st.volume);
                Tk.SetText(setVolumeV, Mathf.RoundToInt(st.volume * 100) + "%");
                PgToggle(setMute, st.muted);
            }
            else
            {
                var c = WarSandboxCameraPrefs.Current;
                PgSlider(camPan, WarSandboxCameraSettings.MultiplierToSlider(c.panSpeed)); Tk.SetText(camPanV, "×" + c.panSpeed.ToString("0.00"));
                PgSlider(camZoom, WarSandboxCameraSettings.MultiplierToSlider(c.zoomSpeed)); Tk.SetText(camZoomV, "×" + c.zoomSpeed.ToString("0.00"));
                PgSlider(camLook, WarSandboxCameraSettings.MultiplierToSlider(c.lookSpeed)); Tk.SetText(camLookV, "×" + c.lookSpeed.ToString("0.00"));
                PgSlider(camPitch, Mathf.InverseLerp(30f, 75f, c.focusPitch)); Tk.SetText(camPitchV, Mathf.RoundToInt(c.focusPitch) + "°");
                PgSlider(camFraming, c.framing); Tk.SetText(camFramingV, c.FramingLabel);
                PgToggle(camFollowZoom, c.followAutoZoom);
            }
            Tk.SetText(setDefaultsText, safety != null ? (gfx ? "恢复画面默认值" : cam ? "恢复镜头默认值" : "恢复声音默认值") : "恢复默认");
            Tk.Show(setStatus, safety != null);
            if (safety != null)
            {
                Tk.SetText(setStatus, safety.Pending ? (gfx ? "尚未保存 · 已立即生效，取消修改会恢复" : cam ? "尚未保存 · 关闭设置后即可试用，取消修改会恢复" : "尚未保存 · 本次调整只用于当前会话试听") : "当前设置未修改");
                setStatus.EnableInClassList("pg-status--pending", safety.Pending);
            }
            string error = safety != null ? (string.IsNullOrEmpty(safety.Error) ? null : cam || gfx ? safety.Error : safety.Error + "\n不会自动覆盖原文件；可重试保存或取消修改。") : audio.SettingsError;
            Tk.Show(setError, !string.IsNullOrEmpty(error));
            Tk.SetText(setError, error);
            bool cancel = safety != null || !string.IsNullOrEmpty(audio.SettingsError);
            Tk.Show(setCancel, cancel);
            string cancelText = safety != null ? "取消修改并返回" : "返回（不保存）";
            if (setCancel.text != cancelText) setCancel.text = cancelText;
        }

        // =====================================================================================================
        // Help (Guide30)
        // =====================================================================================================
        private Label helpCode, helpTitle, helpBody, helpNote, helpCloseText;
        private TacSeg helpTabs;
        private ScrollView helpScroll;
        private Button helpSkip;
        private int helpShownPage = -1;

        private void BuildHelpPage(VisualElement r)
        {
            helpCode = r.Q<Label>("help-code"); helpTitle = r.Q<Label>("help-title"); helpBody = r.Q<Label>("help-body");
            helpNote = r.Q<Label>("help-note"); helpCloseText = r.Q<Label>("help-close-text");
            helpTabs = r.Q<TacSeg>("help-tabs"); helpScroll = r.Q<ScrollView>("help-scroll");
            helpTabs.changed += k => Guide30.For(session)?.Select(k);
            PgOn("help-close", () => Guide30.For(session)?.Close());
            helpSkip = PgOn("help-skip", () => Guide30.For(session)?.Close());
        }

        private void UpdateHelpPage()
        {
            var guide = Guide30.For(session);
            Tk.SetText(helpCode, guide.Intro ? "首次布阵" : "操作帮助");
            Tk.SetText(helpTitle, guide.Intro ? "先编成，再布阵，最后开战" : "随时查看，按需使用");
            if (helpTabs.index != guide.Page) helpTabs.index = guide.Page;
            if (helpShownPage != guide.Page) { helpShownPage = guide.Page; Tk.SetText(helpBody, HelpBody30(guide.Page)); helpScroll.scrollOffset = Vector2.zero; }
            Tk.SetText(helpNote, guide.Error ?? (guide.Intro ? "可直接跳过；完成或跳过后不再自动弹出，操作帮助仍可随时打开。" : "查看帮助会暂停战斗并取消未完成选点；关闭后恢复原运行/暂停状态。已下达命令不变。"));
            helpNote.EnableInClassList("pg-help__note--bad", guide.Error != null);
            Tk.SetText(helpCloseText, guide.Intro ? "知道了，开始布阵" : "关闭帮助");
            Tk.Show(helpSkip, guide.Intro);
        }

        // =====================================================================================================
        // Unit zoom
        // =====================================================================================================
        private void UpdateZoomPage()
        {
            var e = enlargedUnit;
            if (e == null || e.config == null) { unitPreviewOpen = false; pgZoomPreview.Deactivate(); return; }
            var physical = LibraryRadius(e);
            pgZoomPreview.Bind(e.config, e.unitPreview, physical.Available ? (float?)physical.BaseRadius : null);
            Tk.SetText(pgRoot.Q<Label>("zoom-name"), e.config.unitTypeName);
            Tk.SetText(pgRoot.Q<Label>("zoom-radius"), physical.Summary + " · 预览");
            Tk.SetText(pgRoot.Q<Label>("zoom-source"), "来源：" + physical.Source);
            Tk.SetText(pgRoot.Q<Label>("zoom-legend"), UnitPreviewRadius.Legend);
        }

        // =====================================================================================================
        // Unit library (兵种库)
        // =====================================================================================================
        private sealed class LibRow
        {
            public WarSandboxStatDefinition def;
            public VisualElement root, tick;
            public Label name, official, chip;
            public TacSlider slider;
            public TextField field;
            public Button clear;
            public float lo, hi; public bool log, focused;
        }

        private VisualElement libBody, libRowsHost, libPortrait, libUnsaved, libResetAll;
        private Label libNote, libEmpty, libCount, libName, libId, libDirty, libRadius, libRadiusSource, libRadiusLegend, libFootNote, libUnsavedTitle, libResetAllBody;
        private ScrollView libList, libTable;
        private Button libResetAllBtn, libUndo, libRestore, libDiscard, libSave;
        private readonly List<VisualElement> libItems = new List<VisualElement>();
        private readonly List<LibRow> libRows = new List<LibRow>();
        private string libListKey, libRowsKey;

        private void BuildLibraryPage(VisualElement r)
        {
            libBody = r.Q("lib-body"); libRowsHost = r.Q("lib-rows"); libPortrait = r.Q("lib-portrait");
            libUnsaved = r.Q("lib-unsaved"); libResetAll = r.Q("lib-resetall");
            libNote = r.Q<Label>("lib-note"); libEmpty = r.Q<Label>("lib-empty"); libCount = r.Q<Label>("lib-count");
            libName = r.Q<Label>("lib-name"); libId = r.Q<Label>("lib-id"); libDirty = r.Q<Label>("lib-dirty");
            libRadius = r.Q<Label>("lib-radius"); libRadiusSource = r.Q<Label>("lib-radius-source"); libRadiusLegend = r.Q<Label>("lib-radius-legend");
            libFootNote = r.Q<Label>("lib-foot-note"); libUnsavedTitle = r.Q<Label>("lib-unsaved-title"); libResetAllBody = r.Q<Label>("lib-resetall-body");
            libList = r.Q<ScrollView>("lib-list"); libTable = r.Q<ScrollView>("lib-table");
            Tk.SetText(libNote, "同模型展示代表角色 · 修改数值仅作用于该模板编号，不自动同步到旧战场同模型变体");
            pgLibPreview = new TkUnitPreview();
            libPortrait.Add(pgLibPreview);

            // Same actions as DrawLibrary / DrawLibraryUnsaved / DrawLibraryResetAll.
            PgOn("lib-back", () => LibraryGuard(CloseLibrary));
            PgOn("lib-open-preview", () => { var e = LibraryEntry(); if (e != null) OpenUnitPreview(e); });
            libResetAllBtn = PgOn("lib-reset-all", () => libraryModal = LibraryModal.ResetAll);
            libUndo = PgOn("lib-undo", () =>
            {
                if (LibraryStore.TryUndo(out string error)) { libraryPending = null; LibraryNotice("已撤销上次全局修改。", false); }
                else LibraryNotice(error, true);
            });
            libSave = PgOn("lib-save", () => { var e = LibraryEntry(); if (e != null) SaveLibraryPending(e); });
            libDiscard = PgOn("lib-discard", () => { libraryPending = null; LibraryNotice(null, false); });
            libRestore = PgOn("lib-restore", () => { libraryPending = new WarSandboxStatSet(); LibraryNotice("已清空此兵种的全局数值（未保存）。保存后按官方数值运行。", false); });
            PgOn("lib-unsaved-cancel", () => { libraryModal = LibraryModal.None; libraryAfterDecision = null; });
            PgOn("lib-unsaved-discard", () =>
            { libraryModal = LibraryModal.None; libraryPending = null; var next = libraryAfterDecision; libraryAfterDecision = null; next?.Invoke(); });
            PgOn("lib-unsaved-save", () =>
            {
                var e = LibraryEntry();
                libraryModal = LibraryModal.None;
                if (e == null || !SaveLibraryPending(e)) { libraryAfterDecision = null; return; }
                libraryPending = null; var next = libraryAfterDecision; libraryAfterDecision = null; next?.Invoke();
            });
            PgOn("lib-resetall-cancel", () => libraryModal = LibraryModal.None);
            PgOn("lib-resetall-confirm", () =>
            {
                libraryModal = LibraryModal.None;
                if (LibraryStore.TrySave(new WarSandboxGlobalStats(), "兵种库：全部恢复官方", out string error)) { libraryPending = null; LibraryNotice("已全部恢复官方数值。", false); }
                else LibraryNotice(error, true);
            });
        }

        /// <summary>The selected template, with the pending set synchronised exactly like DrawLibrary.</summary>
        private WarSandboxUnitTemplateEntry LibraryEntry()
        {
            var templates = LibraryTemplates();
            if (templates.Count == 0) return null;
            libraryIndex = Mathf.Clamp(libraryIndex, 0, templates.Count - 1);
            var entry = templates[libraryIndex];
            if (libraryPending == null || libraryPendingId != entry.templateId)
            { libraryPending = LibraryStore.Current.Get(entry.templateId); libraryPendingId = entry.templateId; }
            return entry;
        }

        private void UpdateLibraryPage()
        {
            var templates = LibraryTemplates();
            bool empty = templates.Count == 0;
            Tk.Show(libEmpty, empty); Tk.Show(libBody, !empty);
            Tk.Show(libUnsaved, libraryModal == LibraryModal.Unsaved && !empty);
            Tk.Show(libResetAll, libraryModal == LibraryModal.ResetAll && !empty);
            if (empty) { pgLibPreview.Deactivate(); return; }
            var entry = LibraryEntry();
            var saved = LibraryStore.Current;

            // ----- left: all catalog templates -----
            string key = "";
            foreach (var t in templates) key += t.templateId + "|";
            if (key != libListKey)
            {
                libListKey = key; libList.Clear(); libItems.Clear();
                for (int i = 0; i < templates.Count; i++)
                {
                    int index = i; var t = templates[i];
                    var item = Tk.E("lib-item");
                    var row = Tk.E("row");
                    row.Add(Tk.L(t.config.unitTypeName, "lib-item__name"));
                    var chip = Tk.L("全局", "chip", "lib-chip", "lib-chip--global"); chip.name = "global";
                    row.Add(chip);
                    item.Add(row);
                    item.Add(Tk.L(t.templateId, "lib-item__id"));
                    item.RegisterCallback<ClickEvent>(_ =>
                    {
                        if (index != libraryIndex) LibraryGuard(() => { libraryIndex = index; libraryPending = null; libraryNotice = null; nextRefresh = 0; });
                    });
                    libList.Add(item); libItems.Add(item);
                }
            }
            int adjusted = 0;
            for (int i = 0; i < templates.Count && i < libItems.Count; i++)
            {
                bool global = saved.Get(templates[i].templateId).Count > 0; if (global) adjusted++;
                libItems[i].EnableInClassList("lib-item--on", i == libraryIndex);
                libItems[i].EnableInClassList("lib-item--global", global);
                Tk.Show(libItems[i].Q("global"), global);
            }
            Tk.SetText(libCount, "共 " + templates.Count + " 个兵种  ·  已调整 " + adjusted);

            // ----- right: dossier of the selected template -----
            var template = entry.config; bool ranged = WarSandboxUnitStats.IsRanged(template);
            Tk.SetText(libName, template.unitTypeName);
            Tk.SetText(libId, entry.templateId + "  ·  r" + entry.revision + "  ·  " + (ranged ? "远程单位" : "近战单位"));
            Tk.Show(libDirty, LibraryDirty);
            var physical = LibraryRadius(entry);
            Tk.SetText(libRadius, physical.Summary + "  ·  预览");
            Tk.SetText(libRadiusSource, "来源：" + physical.Source);
            Tk.SetText(libRadiusLegend, UnitPreviewRadius.Legend);
            Tk.Show(libPortrait, !unitPreviewOpen);
            if (unitPreviewOpen) pgLibPreview.Deactivate();
            else pgLibPreview.Bind(entry.config, entry.unitPreview, physical.Available ? (float?)physical.BaseRadius : null);

            // ----- stat rows (flocking first, grouped) -----
            var rows = new List<WarSandboxStatDefinition>();
            foreach (var d in WarSandboxUnitStats.Definitions) if (WarSandboxUnitStats.Applies(template, d)) { if (d.UsesFlocking) rows.Insert(0, d); else rows.Add(d); }
            string rowsKey = entry.templateId + ":" + rows.Count;
            if (rowsKey != libRowsKey)
            {
                libRowsKey = rowsKey; libRowsHost.Clear(); libRows.Clear();
                string group = null; int n = 0;
                foreach (var d in rows)
                {
                    if (d.Group != group) { group = d.Group; libRowsHost.Add(Tk.L(group, "lib-group")); }
                    var row = BuildLibraryRow(d);
                    row.root.EnableInClassList("lib-row--alt", n++ % 2 == 0);
                    libRowsHost.Add(row.root); libRows.Add(row);
                }
                libTable.scrollOffset = Vector2.zero;
            }
            foreach (var row in libRows) UpdateLibraryRow(row, template, saved, entry.templateId);

            // ----- footer -----
            string storeWarning = LibraryStore.Warning;
            Tk.SetText(libFootNote, libraryNotice ?? storeWarning ?? (string.IsNullOrEmpty(saved.LastChange) ? "尚未修改全局数值：所有兵种按官方数值运行。" : "上次修改：" + saved.LastChange));
            libFootNote.EnableInClassList("lib-foot__note--ok", libraryNotice != null && !libraryNoticeError);
            libFootNote.EnableInClassList("lib-foot__note--bad", (libraryNotice != null && libraryNoticeError) || (libraryNotice == null && storeWarning != null));
            libResetAllBtn.SetEnabled(!saved.IsEmpty);
            libUndo.SetEnabled(LibraryStore.CanUndo);
            libSave.SetEnabled(LibraryDirty);
            libDiscard.SetEnabled(LibraryDirty);
            libRestore.SetEnabled(libraryPending.Count > 0);

            if (libraryModal == LibraryModal.Unsaved) Tk.SetText(libUnsavedTitle, "保存对 " + template.unitTypeName + " 的修改？");
            if (libraryModal == LibraryModal.ResetAll)
                Tk.SetText(libResetAllBody, "将清除 " + saved.Count + " 个兵种的全局数值。各方案里单独保存的本局数值不受影响。\n此操作可用“撤销上次全局修改”恢复。");
        }

        private LibRow BuildLibraryRow(WarSandboxStatDefinition d)
        {
            var row = new LibRow { def = d, root = Tk.E("lib-row") };
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

            // Same edits as DrawLibraryRow.
            row.slider.changed += t =>
            {
                if (libraryPending != null && libraryPending.Set(d.Stat, d.Step(WarSandboxStatTheme.FromSlider(t, row.lo, row.hi, row.log)))) libraryNotice = null;
            };
            row.field.RegisterCallback<FocusInEvent>(_ => row.focused = true);
            row.field.RegisterCallback<FocusOutEvent>(_ => row.focused = false);
            row.field.RegisterValueChangedCallback(e => CommitLibraryField(row, e.newValue));
            row.clear.clicked += () => { if (libraryPending != null && libraryPending.Remove(d.Stat)) libraryNotice = null; };
            return row;
        }

        private void CommitLibraryField(LibRow row, string text)
        {
            var d = row.def; var entry = LibraryEntry(); if (entry == null || libraryPending == null) return;
            WarSandboxUnitStats.TryGetOfficial(entry.config, d, out float official);
            float effective = libraryPending.TryGet(d.Stat, out float pending) ? pending : official;
            if (!WarSandboxStatTheme.TryParse(text, out float typed)) { LibraryNotice(d.Label + "：请输入有限数字。", true); return; }
            if (d.UsesFlocking && !d.Accepts(typed)) { LibraryNotice("半径必须为 0.05–4.8 米；实际入场还需通过网格、间距和地形校验。", true); return; }
            float clamped = d.Clamp(typed);
            if (WarSandboxUnitStats.Same(clamped, effective) && clamped == typed) return;
            libraryPending.Set(d.Stat, clamped);
            LibraryNotice(clamped != typed ? d.Label + " 已限制在 " + d.Format(d.Min) + " – " + d.Format(d.Max) + "。" : null, clamped != typed);
        }

        private void UpdateLibraryRow(LibRow row, UnitTypeConfig template, WarSandboxGlobalStats saved, string id)
        {
            var d = row.def;
            WarSandboxUnitStats.TryGetOfficial(template, d, out float official);
            bool hasPending = libraryPending.TryGet(d.Stat, out float pending);
            bool hasSaved = saved.TryGet(id, d.Stat, out float savedValue);
            float effective = hasPending ? pending : official;
            bool changed = hasPending != hasSaved || (hasPending && pending != savedValue);
            bool custom = !WarSandboxUnitStats.Same(effective, official);
            var source = hasPending ? WarSandboxStatSource.Global : WarSandboxStatSource.Official;
            row.root.EnableInClassList("lib-row--custom", custom);
            Tk.SetText(row.name, d.Label + (string.IsNullOrEmpty(d.Unit) ? "" : "  " + d.Unit));
            if (!row.slider.HasPointerCapture(PointerId.mousePointerId))
            {
                WarSandboxStatTheme.SliderWindow(d, official, effective, out row.lo, out row.hi, out row.log);
                PgSlider(row.slider, WarSandboxStatTheme.ToSlider(effective, row.lo, row.hi, row.log));
            }
            row.slider.EnableInClassList("lib-slider--global", source == WarSandboxStatSource.Global);
            row.slider.EnableInClassList("lib-slider--official", source == WarSandboxStatSource.Official);
            float tick = WarSandboxStatTheme.ToSlider(official, row.lo, row.hi, row.log);
            row.tick.style.left = Length.Percent(tick * 100f);
            Tk.SetText(row.official, custom ? "官方 " + d.Format(official) : "");
            row.official.EnableInClassList("lib-row__official--right", official > row.lo + (row.hi - row.lo) * 0.5f);
            if (!row.focused) { string v = d.Format(effective); if (row.field.value != v) row.field.SetValueWithoutNotify(v); }
            row.field.EnableInClassList("lib-field--changed", changed);
            row.field.EnableInClassList("lib-field--custom", custom && !changed);
            Tk.SetText(row.chip, WarSandboxStatTheme.SourceName(source) + (changed ? "*" : ""));
            row.chip.EnableInClassList("lib-chip--dirty", changed);
            row.chip.EnableInClassList("lib-chip--global", !changed && source == WarSandboxStatSource.Global);
            row.chip.EnableInClassList("lib-chip--official", !changed && source == WarSandboxStatSource.Official);
            row.clear.SetEnabled(hasPending);
        }
    }
}
