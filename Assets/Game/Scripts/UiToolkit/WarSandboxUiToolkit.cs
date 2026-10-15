using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace MassEngine.Game.Ui
{
    /// <summary>
    /// Shared plumbing for the Q 版 UI Toolkit screens that replace the immediate-mode uGUI presenters, one phase at a time.
    /// - <see cref="Enabled"/>: new/old UI switch (PlayerPrefs; Editor menu "Tools/战争沙盒 UI/…"). Old uGUI stays until every phase is accepted.
    /// - One runtime PanelSettings for all documents, sorted just under the front-end uGUI canvas (200), so legacy uGUI overlays
    ///   that are not migrated yet (settings, unit zoom, help) still draw and receive clicks on top.
    /// - System fonts only (Microsoft YaHei UI / YaHei / SimHei, Segoe UI Black for numbers); nothing is stored in the repo.
    /// - <see cref="PointerOverUI"/> / <see cref="IsTyping"/> feed WarSandboxUGUI.PointerOverUI / IsTyping, so camera and selection
    ///   code keeps a single query for both UI systems.
    /// </summary>
    public static class WarSandboxUiToolkit
    {
        public const string PrefKey = "WarSandbox.UiToolkit";
        public const string ResourceFolder = "WarSandboxUi/";
        /// <summary>Just below the front-end uGUI canvas (200) and above the battle/deployment canvases (50/100).</summary>
        public const int SortingOrder = 199;
        /// <summary>Layout reference: the screens are authored for at least 1440 × 810 panel units.</summary>
        public const float ReferenceWidth = 1440f, ReferenceHeight = 810f;

        static int enabledCache = -1;
        /// <summary>true = new Q 版 UI Toolkit screens where a phase is migrated; false = the old uGUI everywhere.</summary>
        public static bool Enabled
        {
            get { if (enabledCache < 0) enabledCache = PlayerPrefs.GetInt(PrefKey, 1) != 0 ? 1 : 0; return enabledCache == 1; }
            set { enabledCache = value ? 1 : 0; PlayerPrefs.SetInt(PrefKey, enabledCache); PlayerPrefs.Save(); }
        }

        static PanelSettings panelSettings;
        static readonly List<UIDocument> documents = new List<UIDocument>();
        static FontAsset fontMain, fontNumber;
        static bool fontsTried;
        static float lastScale = -1;

        static float ScreenWidth => WarSandboxUGUI.ScreenSizeOverride.HasValue ? WarSandboxUGUI.ScreenSizeOverride.Value.x : Screen.width;
        static float ScreenHeight => WarSandboxUGUI.ScreenSizeOverride.HasValue ? WarSandboxUGUI.ScreenSizeOverride.Value.y : Screen.height;
        /// <summary>Panel units → pixels. Same clamped "shrink" rule as WarSandboxUGUI, against a larger reference.</summary>
        public static float Scale => Mathf.Clamp(Mathf.Min(ScreenWidth / ReferenceWidth, ScreenHeight / ReferenceHeight), 0.6f, 1.6f);
        /// <summary>Converts a screen-space GUI rect (top-left origin, pixels) into panel units.</summary>
        public static Rect ScreenToPanel(Rect gui) { float s = Scale; return new Rect(gui.x / s, gui.y / s, gui.width / s, gui.height / s); }

        public static PanelSettings Panel
        {
            get
            {
                if (panelSettings == null)
                {
                    panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
                    panelSettings.name = "WarSandbox UI Toolkit Panel";
                    panelSettings.hideFlags = HideFlags.DontSave;
                    panelSettings.themeStyleSheet = Resources.Load<ThemeStyleSheet>(ResourceFolder + "WarSandboxUi.Theme");
                    panelSettings.scaleMode = PanelScaleMode.ConstantPixelSize;
                    panelSettings.sortingOrder = SortingOrder;
                    panelSettings.clearColor = false;
                    lastScale = -1;
                }
                UpdateScale();
                return panelSettings;
            }
        }

        /// <summary>Call once per frame from any presenter; cheap when the screen size did not change.</summary>
        public static void UpdateScale()
        {
            if (panelSettings == null) return;
            float s = Scale;
            if (!Mathf.Approximately(s, lastScale)) { lastScale = s; panelSettings.scale = s; }
        }

        /// <summary>Creates an inactive child with a UIDocument, assigns panel + tree, then activates it (avoids a null-panel first enable).</summary>
        public static UIDocument CreateDocument(Transform owner, string name, string uxml, int order = 0)
        {
            var tree = Resources.Load<VisualTreeAsset>(ResourceFolder + uxml);
            if (tree == null) { Debug.LogError("[WarSandboxUiToolkit] missing Resources/" + ResourceFolder + uxml + ".uxml"); return null; }
            var go = new GameObject(name);
            go.SetActive(false);
            go.transform.SetParent(owner, false);
            var doc = go.AddComponent<UIDocument>();
            doc.panelSettings = Panel;
            doc.visualTreeAsset = tree;
            doc.sortingOrder = order;
            go.SetActive(true);
            var root = doc.rootVisualElement;
            if (root != null)
            {
                root.pickingMode = PickingMode.Ignore;
                root.style.position = Position.Absolute;
                root.style.left = 0; root.style.top = 0; root.style.right = 0; root.style.bottom = 0;
                ApplyFonts(root, true);
            }
            documents.Add(doc);
            return doc;
        }

        public static void DestroyDocument(UIDocument doc)
        {
            if (doc == null) return;
            documents.Remove(doc);
            if (Application.isPlaying) UnityEngine.Object.Destroy(doc.gameObject); else UnityEngine.Object.DestroyImmediate(doc.gameObject);
        }

        // ---------------- input bridge ----------------
        /// <summary>True when the mouse is over a pickable element of a visible UI Toolkit document (panel pick, not rect guessing).</summary>
        public static bool PointerOverUI() => PointerOverUI(Input.mousePosition);
        public static bool PointerOverUI(Vector2 mouse)
        {
            for (int i = documents.Count - 1; i >= 0; i--)
            {
                var doc = documents[i];
                if (doc == null) { documents.RemoveAt(i); continue; }
                if (!doc.isActiveAndEnabled) continue;
                var root = doc.rootVisualElement;
                var panel = root?.panel;
                if (panel == null || root.resolvedStyle.display == DisplayStyle.None) continue;
                var p = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(mouse.x, Screen.height - mouse.y));
                var picked = panel.Pick(p);
                if (picked != null && picked != root && IsInside(picked, root)) return true;
            }
            return false;
        }

        public static bool IsTyping
        {
            get
            {
                foreach (var doc in documents)
                {
                    if (doc == null || !doc.isActiveAndEnabled) continue;
                    var focused = doc.rootVisualElement?.panel?.focusController?.focusedElement as VisualElement;
                    if (focused != null && (focused is TextField || focused.GetFirstAncestorOfType<TextField>() != null)) return true;
                }
                return false;
            }
        }

        static bool IsInside(VisualElement e, VisualElement root)
        {
            for (; e != null; e = e.parent) if (e == root) return true;
            return false;
        }

        // ---------------- fonts ----------------
        static void LoadFonts()
        {
            if (fontsTried) return;
            fontsTried = true;
            var cjk = Sys("Microsoft YaHei UI", "Regular") ?? Sys("Microsoft YaHei", "Regular") ?? Sys("SimHei", "Regular");
            var number = Sys("Segoe UI Black", "Regular") ?? Sys("Segoe UI", "Black") ?? Sys("Segoe UI", "Bold");
            fontMain = cjk;
            if (number != null && cjk != null) number.fallbackFontAssetTable = new List<FontAsset> { cjk };
            fontNumber = number ?? cjk;
        }

        static FontAsset Sys(string family, string style)
        {
            try
            {
                var fa = FontAsset.CreateFontAsset(family, style, 72);
                if (fa != null) { fa.name = family; fa.hideFlags = HideFlags.DontSave; fa.isMultiAtlasTexturesEnabled = true; }
                return fa;
            }
            catch (Exception e) { Debug.LogWarning("[WarSandboxUiToolkit] font " + family + ": " + e.Message); return null; }
        }

        /// <summary>Main font is inherited from the root; elements with class "mono" (numbers, keys) get the number font.</summary>
        public static void ApplyFonts(VisualElement ve, bool isRoot = false)
        {
            if (ve == null) return;
            LoadFonts();
            if (fontMain != null && isRoot) ve.style.unityFontDefinition = FontDefinition.FromSDFFont(fontMain);
            if (fontNumber != null)
            {
                var def = FontDefinition.FromSDFFont(fontNumber);
                ve.Query(className: "mono").ForEach(e => e.style.unityFontDefinition = def);
            }
        }

        // ---------------- small builders ----------------
        public static Label L(string text, params string[] classes)
        {
            var l = new Label(text);
            foreach (var c in classes) l.AddToClassList(c);
            return l;
        }

        public static VisualElement E(params string[] classes)
        {
            var e = new VisualElement();
            foreach (var c in classes) e.AddToClassList(c);
            return e;
        }

        public static void Show(VisualElement e, bool visible)
        {
            if (e == null) return;
            var d = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (e.style.display != d) e.style.display = d;
        }

        public static void SetText(Label l, string text)
        {
            if (l != null && l.text != text) l.text = text ?? "";
        }

        /// <summary>Hides the default scroll bars (USS cannot; see the prototype notes).</summary>
        public static void HideScrollers(VisualElement root)
        {
            root?.Query<ScrollView>().ForEach(sv =>
            {
                sv.verticalScrollerVisibility = ScrollerVisibility.Hidden;
                sv.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            });
        }

        /// <summary>Tests/captures: forget the cached switch so the next read comes from PlayerPrefs again.</summary>
        public static void ResetCache() => enabledCache = -1;
    }
}
