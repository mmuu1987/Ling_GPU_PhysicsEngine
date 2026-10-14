using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;
using TextElement = UnityEngine.UIElements.TextElement;

namespace MassEngine.UiPrototype
{
    /// <summary>
    /// Entry point of the B-style UI Toolkit prototype. Pure UI: screen navigation, mock data and input only.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class UiProtoBApp : MonoBehaviour
    {
        public VisualTreeAsset opsRow, compCard, armyCard;
        [Tooltip("Q 版皮肤：同一套 UXML/交互，换 UiProtoToy.uss 和地图调色板")] public bool toy;

        UIDocument doc;
        VisualElement root, app, topbar, toast;
        Label crumb, clock, toastText;
        readonly Dictionary<string, VisualElement> screens = new Dictionary<string, VisualElement>();
        readonly Dictionary<string, VisualElement> modals = new Dictionary<string, VisualElement>();
        IVisualElementScheduledItem toastHide;
        FontAsset fontMain, fontMono;

        internal OpsScreen ops;
        internal DeployScreen deploy;
        internal BattleScreen battle;
        internal ResultModal result;
        internal SettingsModal settings;

        public string Current { get; private set; } = "";
        public string Modal { get; private set; } = "";
        public bool IsReady { get; private set; }
        public bool CaptureDone { get; private set; }
        public string CaptureLog { get; private set; } = "";

        void OnEnable() { doc = GetComponent<UIDocument>(); TacTheme.Toy = toy; }

        IEnumerator Start()
        {
            yield return null;
            if (!IsReady) Build();
        }

        void Build()
        {
            root = doc.rootVisualElement;
            app = root.Q("app");
            if (app == null) { Debug.LogError("[UiProtoB] Root.uxml not assigned or missing #app"); return; }
            TacTheme.Toy = toy;
            LoadFonts();
            ApplyFonts(root);
            if (toy) ToyText(root);

            topbar = root.Q("topbar");
            crumb = root.Q<Label>("crumb");
            clock = root.Q<Label>("clock");
            toast = root.Q("toast");
            toastText = root.Q<Label>("toast-text");
            screens.Clear(); modals.Clear();
            foreach (var n in new[] { "ops", "deploy", "battle" }) screens[n] = root.Q("screen-" + n);
            foreach (var n in new[] { "result", "settings" }) modals[n] = root.Q("modal-" + n);
            foreach (var s in screens.Values) s.style.display = DisplayStyle.None;
            foreach (var m in modals.Values) m.style.display = DisplayStyle.None;

            ops = new OpsScreen(this, screens["ops"]);
            deploy = new DeployScreen(this, screens["deploy"]);
            battle = new BattleScreen(this, screens["battle"]);
            result = new ResultModal(this, modals["result"]);
            settings = new SettingsModal(this, modals["settings"]);

            root.Q<Button>("top-library").clicked += () => Toast("兵种库：原型中未实现");
            root.Q<Button>("top-settings").clicked += () => OpenModal("settings");
            root.Q<Button>("top-exit").clicked += () => Toast("退出：原型中不会关闭程序");
            root.Q<Button>("nav-ops").clicked += () => Nav("ops");
            root.Q<Button>("nav-deploy").clicked += () => Nav("deploy");
            root.Q<Button>("nav-battle").clicked += () => Nav("battle");
            root.Q<Button>("nav-result").clicked += () => Nav("result");
            root.Q<Button>("nav-settings").clicked += () => Nav("settings");

            ops.Select(0);
            Show("ops");
            root.Query<ScrollView>().ForEach(sv =>
            {
                sv.verticalScrollerVisibility = ScrollerVisibility.Hidden;
                sv.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            });
            IsReady = true;
        }

        // ---------------- fonts ----------------
        void LoadFonts()
        {
            var cjk = Sys("Microsoft YaHei UI", "Regular") ?? Sys("Microsoft YaHei", "Regular") ?? Sys("SimHei", "Regular");
            var latin = toy ? null : Sys("Bahnschrift", "Regular") ?? Sys("Segoe UI", "Regular");
            var mono = toy ? Sys("Segoe UI Black", "Regular") ?? Sys("Segoe UI", "Black") ?? Sys("Segoe UI", "Bold")
                           : Sys("Consolas", "Regular") ?? Sys("Cascadia Mono", "Regular");
            fontMain = latin ?? cjk;
            if (latin != null && cjk != null) latin.fallbackFontAssetTable = new List<FontAsset> { cjk };
            fontMono = mono ?? fontMain;
            if (mono != null && cjk != null) mono.fallbackFontAssetTable = new List<FontAsset> { cjk };
            Debug.Log($"[UiProtoB] fonts main={(latin != null ? "Bahnschrift" : "fallback")} cjk={(cjk != null ? cjk.name : "none")} mono={(mono != null ? mono.name : "none")}");
        }

        /// <summary>Q 版：把装饰性的英文代码标签换成中文，或直接隐藏（映射为空串）。</summary>
        static readonly Dictionary<string, string> ToyMap = new Dictionary<string, string>
        {
            { "MASS WAR SANDBOX", "万人战场" }, { "//", "·" }, { "SYS 00:00:00", "" },
            { "AFTER-ACTION // 战后报告", "战后报告" }, { "SYSTEM // CONFIG", "" },
            { "ATTACKER", "攻方" }, { "攻方 · ATTACKER", "攻方" }, { "守方 · DEFENDER", "守方" }, { "DEFENDER", "守方" }, { "SPEED", "速度" }, { "TELEMETRY", "调试信息" },
            { "FORCES", "军团" }, { "// 1–9 选择 · TAB 切换", "按 1–9 选择" }, { "TACTICAL", "小地图" }, { "// 1024 m", "" }, { "1:4096", "" },
            { "RECON // 1024 × 1024 m", "地图预览" }, { "N ▲", "北 ▲" }, { "GRID 64 m", "" },
            { "LEGIONS", "" }, { "TROOPS", "" }, { "ANNIHILATION", "" }, { "FOREST HILLS", "" },
            { "X 0512  Z 0512", "" }, { "100 m", "100 米" }, { "就绪 · READY", "准备就绪" }, { "WIN", "胜利" },
            { "UNIT // SWORDSMAN", "兵种介绍" }, { "OP-01 // FOREST-HILLS-1024", "森林丘陵" },
            { "UI 原型 · 无游戏逻辑 · 数据为模拟", "Q 版 UI 原型 · 数据为模拟" },
        };

        static void ToyText(VisualElement ve)
        {
            ve.Query<TextElement>().ForEach(t =>
            {
                if (string.IsNullOrEmpty(t.text)) return;
                if (!ToyMap.TryGetValue(t.text, out var v))
                {
                    // 「画面 · VIDEO」这类中英双写：去掉英文尾巴
                    var m = System.Text.RegularExpressions.Regex.Match(t.text, @"^(.+?) · [A-Z][A-Z &/\-]*$");
                    if (!m.Success) return;
                    v = m.Groups[1].Value;
                }
                if (v.Length == 0) t.style.display = DisplayStyle.None; else t.text = v;
            });
        }

        static FontAsset Sys(string family, string style)
        {
            try
            {
                var fa = FontAsset.CreateFontAsset(family, style, 72);
                if (fa != null) { fa.name = family; fa.isMultiAtlasTexturesEnabled = true; }
                return fa;
            }
            catch (Exception e) { Debug.LogWarning($"[UiProtoB] font {family}: {e.Message}"); return null; }
        }

        internal void ApplyFonts(VisualElement ve)
        {
            if (fontMain != null)
            {
                var def = FontDefinition.FromSDFFont(fontMain);
                if (ve == root) root.style.unityFontDefinition = def;
                ve.Query<TextElement>().ForEach(t => { if (!t.ClassListContains("mono")) t.style.unityFontDefinition = def; });
            }
            if (fontMono != null)
            {
                var def = FontDefinition.FromSDFFont(fontMono);
                ve.Query(className: "mono").ForEach(e => e.style.unityFontDefinition = def);
            }
        }

        // ---------------- navigation ----------------
        void Nav(string n)
        {
            if (n == "result") { if (Current != "battle") Show("battle"); battle.FinishNow(); return; }
            if (n == "settings") { OpenModal("settings"); return; }
            CloseModal();
            Show(n);
        }

        public void Show(string n)
        {
            if (!screens.ContainsKey(n)) return;
            foreach (var kv in screens) if (kv.Key != n) kv.Value.style.display = DisplayStyle.None;
            var el = screens[n];
            if (Current != n) Enter(el, "screen--pre");
            Current = n;
            topbar.style.display = n == "battle" ? DisplayStyle.None : DisplayStyle.Flex;
            crumb.text = n == "ops" ? "作战选择" : n == "deploy" ? "配兵布阵" : "战斗";
            battle.SetActive(n == "battle");
            root.Q("protonav").Query<Button>().ForEach(b => b.RemoveFromClassList("protonav__btn--on"));
            root.Q<Button>("nav-" + n)?.AddToClassList("protonav__btn--on");
        }

        public void OpenModal(string n)
        {
            if (!modals.ContainsKey(n)) return;
            CloseModal();
            Modal = n;
            if (n == "result") result.Populate(battle);
            if (n == "settings") settings.Open();
            Enter(modals[n], "modal--pre");
        }

        public void CloseModal()
        {
            if (Modal == "") return;
            modals[Modal].style.display = DisplayStyle.None;
            Modal = "";
        }

        static void Enter(VisualElement el, string pre)
        {
            el.AddToClassList(pre);
            el.style.display = DisplayStyle.Flex;
            el.schedule.Execute(() => el.RemoveFromClassList(pre)).StartingIn(20);
        }

        public void Toast(string msg)
        {
            toastText.text = msg;
            toast.AddToClassList("toast--on");
            toastHide?.Pause();
            toastHide = toast.schedule.Execute(() => toast.RemoveFromClassList("toast--on")).StartingIn(2200);
        }

        // ---------------- input ----------------
        void Update()
        {
            if (!IsReady) return;
            clock.text = toy ? DateTime.Now.ToString("HH:mm") : "SYS " + DateTime.Now.ToString("HH:mm:ss");
            if (Typing()) return;

            if (Input.GetKeyDown(KeyCode.F5)) Nav("ops");
            if (Input.GetKeyDown(KeyCode.F6)) Nav("deploy");
            if (Input.GetKeyDown(KeyCode.F7)) Nav("battle");
            if (Input.GetKeyDown(KeyCode.F8)) Nav("result");
            if (Input.GetKeyDown(KeyCode.F9)) Nav("settings");

            if (Modal != "")
            {
                if (Input.GetKeyDown(KeyCode.Escape)) CloseModal();
                else if (Modal == "result" && Enter()) result.Again();
                return;
            }
            if (Current == "ops") ops.OnKeys();
            else if (Current == "deploy") deploy.OnKeys();
            else if (Current == "battle") battle.OnKeys();
        }

        internal static bool Enter() => Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);

        bool Typing()
        {
            var fe = root.panel?.focusController?.focusedElement as VisualElement;
            return fe != null && (fe is TextField || fe.GetFirstAncestorOfType<TextField>() != null);
        }

        // ---------------- capture (used by the Editor menu) ----------------
        public void StartCapture(string dir) => StartCoroutine(CaptureRoutine(dir));

        IEnumerator CaptureRoutine(string dir)
        {
            Directory.CreateDirectory(dir);
            var rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            rt.Create();
            var ps = Instantiate(doc.panelSettings);
            ps.targetTexture = rt;
            ps.clearColor = true;
            ps.colorClearValue = Color.black;
            ps.scaleMode = PanelScaleMode.ConstantPixelSize;
            ps.scale = 1f;
            doc.panelSettings = ps;
            yield return null;
            yield return null;
            if (!IsReady || doc.rootVisualElement.Q("app") != app) { IsReady = false; Build(); }
            yield return new WaitForSecondsRealtime(1.0f);

            var shots = new List<(string, Action)>
            {
                ("01-ops", () => { CloseModal(); Show("ops"); ops.Select(0); }),
                ("02-deploy", () => { Show("deploy"); deploy.SelectTab(0); }),
                ("03-deploy-invalid", () => { deploy.DemoInvalid(); deploy.SelectTab(1); }),
                ("04-battle", () => { Show("battle"); battle.Demo(); }),
                ("05-result", () => { battle.FinishNow(); }),
                ("06-settings", () => { CloseModal(); Show("ops"); OpenModal("settings"); }),
            };
            var log = new System.Text.StringBuilder();
            foreach (var (name, act) in shots)
            {
                try { act(); }
                catch (Exception e) { log.AppendLine(name + " ERROR " + e); }
                yield return new WaitForSecondsRealtime(1.2f);
                yield return new WaitForEndOfFrame();
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
                Destroy(tex);
                log.AppendLine(name + " ok");
            }
            CaptureLog = log.ToString();
            CaptureDone = true;
        }
    }
}
