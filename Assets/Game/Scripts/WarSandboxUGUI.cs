using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MassEngine.Game
{
    // Retained uGUI nodes: drawing only updates changed values; no per-frame hierarchy rebuild.
    // Owned by a presenter and destroyed with it. Does not write scenes or shared assets.
    public sealed class WarSandboxUGUI : IDisposable
    {
        // Toy-table presentation: cream paper, sky blue navigation and warm primary actions.
        public static readonly Color Background = new Color32(245, 245, 232, 255);
        public static readonly Color Surface = new Color32(255, 253, 244, 250);
        public static readonly Color Ink = new Color32(38, 66, 81, 255);
        public static readonly Color Muted = new Color32(94, 113, 118, 255);
        public static readonly Color Accent = new Color32(37, 125, 176, 255);
        public static readonly Color Soft = new Color32(216, 239, 247, 255);
        public static readonly Color Line = new Color32(208, 217, 211, 255);
        public static readonly Color Danger = new Color32(182, 65, 61, 255);
        public static readonly Color Amber = new Color32(255, 185, 73, 255);
        public static readonly Color Purple = new Color32(123, 99, 165, 255);
        public static readonly Color Raised = new Color32(235, 243, 240, 255);
        public static readonly Color FieldFill = new Color32(255, 255, 250, 255);
        public static readonly Color Dim = new Color32(121, 138, 141, 255);
        public static readonly Color Deep = new Color32(45, 60, 68, 255);
        public static readonly Color Bar = new Color32(172, 217, 236, 250);
        public static readonly Color Shade = new Color(0.12f, 0.20f, 0.24f, 0.72f);
        private Texture2D roundedTexture;
        private Sprite roundedSprite;
        private sealed class Node
        {
            public RectTransform rect;
            public Text text;
            public Image image;
            public Button button;
            public InputField input;
            public Slider slider;
            public RawImage raw;
            public Action click;
            public Action<string> change;
            public Action<string> endEdit;
            public Action<float> changeNumber;
            public Action<Vector2, int, bool> pointer;
            public RectTransform content;
            public Image[] edges;
            public Outline outline;
            public bool used;
        }
        private readonly Dictionary<string, Node> nodes = new Dictionary<string, Node>();
        private readonly Dictionary<RectTransform, int> siblingIndices = new Dictionary<RectTransform, int>();
        private readonly GameObject root;
        private readonly Font font, mono;
        private readonly Canvas canvas;
        private readonly GraphicRaycaster raycaster;
        private static readonly List<WarSandboxUGUI> instances = new List<WarSandboxUGUI>();
        private static readonly List<RaycastResult> hits = new List<RaycastResult>();
        private RectTransform parent;
        private GameObject ownedEvents;
        /// <summary>Capture/test hook: lay out as if the screen had this size (null = the real screen). Never set by the game.</summary>
        public static Vector2? ScreenSizeOverride { get; set; }
        private static float ScreenWidth => ScreenSizeOverride.HasValue ? ScreenSizeOverride.Value.x : Screen.width;
        private static float ScreenHeight => ScreenSizeOverride.HasValue ? ScreenSizeOverride.Value.y : Screen.height;
        public float Width => ScreenWidth / Scale;
        public float Height => ScreenHeight / Scale;
        public float Scale => Mathf.Clamp(Mathf.Min(ScreenWidth / 1280f, ScreenHeight / 720f), 0.5f, 1.5f);
        public bool Visible => root != null && root.activeInHierarchy;

        public WarSandboxUGUI(Transform owner, string name, int order)
        {
            root = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            root.transform.SetParent(owner, false);
            canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = order;
            root.GetComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            raycaster = root.GetComponent<GraphicRaycaster>();
            font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "SimHei", "Arial" }, 18);
            mono = Font.CreateDynamicFontFromOSFont(new[] { "Consolas", "Cascadia Mono", "Courier New", "Microsoft YaHei" }, 16);
            roundedTexture = new Texture2D(64, 64, TextureFormat.RGBA32, false) { name = "Toy UI rounded card", hideFlags = HideFlags.DontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color[64 * 64];
            for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
            {
                float dx = Mathf.Max(0, Mathf.Abs(x - 31.5f) - 15.5f), dy = Mathf.Max(0, Mathf.Abs(y - 31.5f) - 15.5f);
                pixels[y * 64 + x] = new Color(1, 1, 1, Mathf.Clamp01(16f - Mathf.Sqrt(dx * dx + dy * dy)));
            }
            roundedTexture.SetPixels(pixels); roundedTexture.Apply(false, true);
            roundedSprite = Sprite.Create(roundedTexture, new Rect(0, 0, 64, 64), new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect, new Vector4(18, 18, 18, 18));
            roundedSprite.hideFlags = HideFlags.DontSave;
            EnsureEvents(); instances.Add(this);
        }
        private void EnsureEvents()
        {
            if (EventSystem.current != null) return;
            ownedEvents = new GameObject("Sandbox UI Events", typeof(EventSystem), typeof(StandaloneInputModule));
            ownedEvents.transform.SetParent(root.transform.parent, false);
            // Space/Enter belong to battle shortcuts, not an old highlighted button.
            ownedEvents.GetComponent<EventSystem>().sendNavigationEvents = false;
        }
        public static bool IsTyping
        {
            get
            {
                var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
                var field = selected != null ? selected.GetComponent<InputField>() : null;
                return field != null && field.isFocused;
            }
        }
        public static bool PointerOverUI()
        {
            if (EventSystem.current == null) return false;
            var data = new PointerEventData(EventSystem.current) { position = Input.mousePosition };
            foreach (var ui in instances)
            {
                if (!ui.Visible) continue;
                hits.Clear(); ui.raycaster.Raycast(data, hits);
                if (hits.Count > 0) return true;
            }
            return false;
        }
        public void SetVisible(bool value)
        {
            if (root.activeSelf != value) root.SetActive(value);
            if (value) EnsureEvents();
        }
        public void Begin()
        {
            SetVisible(true); canvas.scaleFactor = Scale;
            root.GetComponent<CanvasScaler>().scaleFactor = Scale;
            parent = (RectTransform)root.transform;
            siblingIndices.Clear();
            foreach (var node in nodes.Values) node.used = false;
        }
        public void End()
        {
            foreach (var node in nodes.Values)
                if (!node.used && node.rect.gameObject.activeSelf) node.rect.gameObject.SetActive(false);
        }
        private Node Get(string id, Rect rect)
        {
            if (!nodes.TryGetValue(id, out var node))
            {
                node = new Node { rect = new GameObject(id, typeof(RectTransform)).GetComponent<RectTransform>() };
                node.rect.SetParent(parent, false); nodes.Add(id, node);
            }
            node.used = true;
            if (!node.rect.gameObject.activeSelf) node.rect.gameObject.SetActive(true);
            if (node.rect.parent != parent) node.rect.SetParent(parent, false);
            // Preserve draw order after switching pages or adding rows: reused modal and
            // input nodes must not slip underneath nodes created on a later visit.
            siblingIndices.TryGetValue(parent, out int sibling);
            if (node.rect.GetSiblingIndex() != sibling) node.rect.SetSiblingIndex(sibling);
            siblingIndices[parent] = sibling + 1;
            Place(node.rect, rect); return node;
        }
        public static void Place(RectTransform transform, Rect rect)
        {
            transform.anchorMin = transform.anchorMax = new Vector2(0, 1); transform.pivot = new Vector2(0, 1);
            var position = new Vector2(rect.x, -rect.y); var size = new Vector2(Mathf.Max(0, rect.width), Mathf.Max(0, rect.height));
            if (transform.anchoredPosition != position) transform.anchoredPosition = position;
            if (transform.sizeDelta != size) transform.sizeDelta = size;
        }
        private Image Image(Node node)
        {
            if (node.image == null) node.image = node.rect.gameObject.AddComponent<Image>();
            bool rounded = Mathf.Min(node.rect.sizeDelta.x, node.rect.sizeDelta.y) >= 28;
            node.image.sprite = rounded ? roundedSprite : null;
            node.image.type = rounded ? UnityEngine.UI.Image.Type.Sliced : UnityEngine.UI.Image.Type.Simple;
            return node.image;
        }
        private Text Text(Node node)
        {
            if (node.text != null) return node.text;
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text)); go.transform.SetParent(node.rect, false);
            var rect = (RectTransform)go.transform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(10, 2); rect.offsetMax = new Vector2(-10, -2);
            node.text = go.GetComponent<Text>(); node.text.font = font; node.text.raycastTarget = false;
            node.text.supportRichText = false; node.text.horizontalOverflow = HorizontalWrapMode.Wrap;
            node.text.verticalOverflow = VerticalWrapMode.Truncate; return node.text;
        }
        /// <summary>Filled rectangle. Default (themed) panels get a 1px line border; explicit colours stay borderless unless <paramref name="edge"/> is given.</summary>
        public void Panel(string id, Rect rect, Color? color = null, bool block = true, Color? edge = null)
        {
            var node = Get(id, rect); var image = Image(node); image.color = color ?? Surface; image.raycastTarget = block;
            Edge(node, edge ?? (color == null ? Line : Color.clear));
        }
        public void Label(string id, Rect rect, string value, int size = 16, Color? color = null, bool bold = false)
        {
            var text = Text(Get(id, rect)); if (text.text != value) text.text = value;
            text.fontSize = size; text.color = color ?? Ink; text.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            text.alignment = TextAnchor.MiddleLeft;
        }
        public void Button(string id, Rect rect, string value, Action click, bool primary = false, bool enabled = true, bool selected = false)
        {
            var node = Get(id, rect); var image = Image(node);
            if (node.button == null)
            {
                node.button = node.rect.gameObject.AddComponent<Button>(); node.button.targetGraphic = image;
                node.button.navigation = new Navigation { mode = Navigation.Mode.None };
                node.button.onClick.AddListener(() => node.click?.Invoke());
            }
            node.click = click; node.button.interactable = enabled; image.raycastTarget = true;
            image.color = primary ? Amber : selected ? Soft : Raised;
            var colors = node.button.colors; colors.normalColor = Color.white; colors.highlightedColor = Color.white;
            colors.pressedColor = new Color(0.7f, 0.7f, 0.7f); colors.selectedColor = colors.normalColor; colors.disabledColor = new Color(0.62f, 0.62f, 0.62f, 0.55f);
            node.button.colors = colors;
            Edge(node, primary ? Color.clear : selected ? Accent : Line);
            var text = Text(node); if (text.text != value) text.text = value;
            text.fontSize = 15; text.color = enabled ? primary ? Deep : selected ? Accent : Ink : Dim;
            text.fontStyle = primary || selected ? FontStyle.Bold : FontStyle.Normal; text.alignment = TextAnchor.MiddleCenter;
        }
        public void Field(string id, Rect rect, string value, Action<string> change, int limit = 24)
        {
            var node = Get(id, rect); Image(node).color = FieldFill; Edge(node, Line);
            if (node.input == null)
            {
                var text = Text(node); text.alignment = TextAnchor.MiddleLeft; text.fontSize = 16; text.color = Ink;
                node.input = node.rect.gameObject.AddComponent<InputField>(); node.input.textComponent = text;
                node.input.targetGraphic = node.image; node.input.lineType = InputField.LineType.SingleLine;
                node.input.onValueChanged.AddListener(v => node.change?.Invoke(v));
            }
            node.change = change; node.input.characterLimit = limit;
            node.input.customCaretColor = true; node.input.caretColor = Accent; node.input.selectionColor = new Color(Accent.r, Accent.g, Accent.b, 0.3f);
            if (node.input.text != (value ?? "")) node.input.SetTextWithoutNotify(value ?? "");
        }
        public void PointerArea(string id, Rect rect, Action<Vector2, int, bool> action)
        {
            var node = Get(id, rect); node.pointer = action;
            Image(node).color = Color.clear;
            if (node.rect.GetComponent<EventTrigger>() == null)
            {
                var trigger = node.rect.gameObject.AddComponent<EventTrigger>();
                var entry = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
                entry.callback.AddListener(data =>
                {
                    var pointer = (PointerEventData)data;
                    RectTransformUtility.ScreenPointToLocalPointInRectangle(node.rect, pointer.position, pointer.pressEventCamera, out var point);
                    var size = node.rect.rect.size;
                    node.pointer?.Invoke(new Vector2(point.x / size.x, -point.y / size.y), (int)pointer.button,
                        Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift));
                });
                trigger.triggers.Add(entry);
            }
        }
        public void Slider(string id, Rect rect, float value, Action<float> change)
        {
            var node = Get(id, rect);
            if (node.slider == null)
            {
                Image(node).color = Line;
                var handle = new GameObject("Handle", typeof(RectTransform), typeof(Image));
                var transform = (RectTransform)handle.transform; transform.SetParent(node.rect, false);
                transform.anchorMin = Vector2.zero; transform.anchorMax = Vector2.up;
                transform.sizeDelta = new Vector2(20, 0); handle.GetComponent<Image>().color = Accent;
                node.slider = node.rect.gameObject.AddComponent<Slider>();
                node.slider.minValue = 0; node.slider.maxValue = 1;
                node.slider.handleRect = transform; node.slider.targetGraphic = handle.GetComponent<Image>();
                node.slider.navigation = new Navigation { mode = Navigation.Mode.None };
                node.slider.onValueChanged.AddListener(v => node.changeNumber?.Invoke(v));
            }
            node.changeNumber = change; node.slider.SetValueWithoutNotify(value);
        }
        // ---- Themed variants (additive): explicit colours for dark panels; the light defaults above are unchanged. ----
        public void LabelAligned(string id, Rect rect, string value, int size, Color color, bool bold, TextAnchor anchor)
        {
            var text = Text(Get(id, rect)); if (text.text != value) text.text = value;
            text.fontSize = size; text.color = color; text.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal; text.alignment = anchor;
        }
        public void TintButton(string id, Rect rect, string value, Action click, Color fill, Color ink, bool enabled = true, bool bold = false, int size = 15)
        {
            var node = Get(id, rect); var image = Image(node);
            if (node.button == null)
            {
                node.button = node.rect.gameObject.AddComponent<Button>(); node.button.targetGraphic = image;
                node.button.navigation = new Navigation { mode = Navigation.Mode.None };
                node.button.onClick.AddListener(() => node.click?.Invoke());
            }
            node.click = click; node.button.interactable = enabled; image.color = fill; image.raycastTarget = true;
            var colors = node.button.colors; colors.normalColor = Color.white; colors.highlightedColor = new Color(0.8f, 0.9f, 0.95f);
            colors.pressedColor = new Color(0.62f, 0.74f, 0.8f); colors.selectedColor = Color.white; colors.disabledColor = new Color(1, 1, 1, 0.4f);
            node.button.colors = colors;
            var text = Text(node); if (text.text != value) text.text = value;
            text.fontSize = size; text.color = enabled ? ink : new Color(ink.r, ink.g, ink.b, 0.45f);
            text.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal; text.alignment = TextAnchor.MiddleCenter;
        }
        /// <summary>Single-line input committed on end-edit (Enter / focus loss). Never overwritten while focused.</summary>
        public void TintField(string id, Rect rect, string value, Action<string> endEdit, Color fill, Color ink, bool enabled = true, int limit = 12)
        {
            var node = Get(id, rect); Image(node).color = fill;
            if (node.input == null)
            {
                var text = Text(node); text.alignment = TextAnchor.MiddleRight; text.fontSize = 15;
                node.input = node.rect.gameObject.AddComponent<InputField>(); node.input.textComponent = text;
                node.input.targetGraphic = node.image; node.input.lineType = InputField.LineType.SingleLine;
                node.input.onValueChanged.AddListener(v => node.change?.Invoke(v));
                node.input.onEndEdit.AddListener(v => node.endEdit?.Invoke(v));
            }
            node.text.color = ink; node.input.customCaretColor = true; node.input.caretColor = ink;
            node.input.selectionColor = new Color(ink.r, ink.g, ink.b, 0.3f);
            node.endEdit = endEdit; node.input.characterLimit = limit; node.input.interactable = enabled;
            if (!node.input.isFocused && node.input.text != (value ?? "")) node.input.SetTextWithoutNotify(value ?? "");
        }
        public void TintSlider(string id, Rect rect, float value, Action<float> change, Color track, Color handle, bool enabled = true)
        {
            Slider(id, rect, value, change);
            var node = nodes[id]; node.image.color = track; node.slider.interactable = enabled;
            var knob = node.slider.handleRect.GetComponent<Image>(); knob.color = enabled ? handle : new Color(handle.r, handle.g, handle.b, 0.35f);
            var colors = node.slider.colors; colors.disabledColor = Color.white; node.slider.colors = colors;
        }
        public void TintScroll(string id, Rect rect, float contentHeight, Color fill)
        {
            Scroll(id, rect, contentHeight);
            nodes[id].image.color = fill;
        }
        public void Picture(string id, Rect rect, Texture texture)
        {
            var node = Get(id, rect);
            if (node.raw == null) { node.raw = node.rect.gameObject.AddComponent<RawImage>(); node.raw.raycastTarget = false; }
            node.raw.texture = texture; node.raw.color = texture != null ? Color.white : Background;
        }
        /// <summary>Letterbox, never crop or stretch a battlefield photograph/full-body unit image.</summary>
        public static Rect AspectFit(Rect area, float sourceWidth, float sourceHeight)
        {
            if (area.width <= 0 || area.height <= 0 || sourceWidth <= 0 || sourceHeight <= 0)
                return new Rect(area.center.x, area.center.y, 0, 0);
            float factor = Mathf.Min(area.width / sourceWidth, area.height / sourceHeight);
            var size = new Vector2(sourceWidth * factor, sourceHeight * factor);
            return new Rect(area.center - size * .5f, size);
        }
        public void PictureFit(string id, Rect rect, Texture texture)
        {
            Picture(id, texture != null ? AspectFit(rect, texture.width, texture.height) : rect, texture);
        }
        public void Scroll(string id, Rect rect, float contentHeight)
        {
            var node = Get(id, rect);
            if (node.content == null)
            {
                Image(node).color = new Color(0, 0, 0, 0);
                node.rect.gameObject.AddComponent<RectMask2D>();
                var scroll = node.rect.gameObject.AddComponent<ScrollRect>();
                scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 28;
                node.content = new GameObject("Content", typeof(RectTransform)).GetComponent<RectTransform>();
                node.content.SetParent(node.rect, false); node.content.anchorMin = node.content.anchorMax = new Vector2(0, 1);
                node.content.pivot = new Vector2(0, 1); scroll.content = node.content; scroll.viewport = node.rect;
            }
            node.content.sizeDelta = new Vector2(rect.width, Mathf.Max(rect.height, contentHeight)); parent = node.content;
        }
        public void EndScroll() { parent = (RectTransform)root.transform; }
        /// <summary>Scrolls a vertical list just enough that content rows <paramref name="top"/>..<paramref name="bottom"/> are visible.</summary>
        public void ScrollIntoView(string id, float top, float bottom)
        {
            if (!nodes.TryGetValue(id, out var node) || node.content == null) return;
            float view = node.rect.rect.height; var position = node.content.anchoredPosition; float y = position.y;
            if (top < y) y = top; else if (bottom > y + view) y = bottom - view;
            y = Mathf.Clamp(y, 0, Mathf.Max(0, node.content.sizeDelta.y - view));
            if (!Mathf.Approximately(y, position.y)) node.content.anchoredPosition = new Vector2(position.x, y);
        }
        // ---- B style primitives ----
        private static readonly Vector2[] EdgeMin = { new Vector2(0, 1), new Vector2(0, 0), new Vector2(0, 0), new Vector2(1, 0) };
        private static readonly Vector2[] EdgeMax = { new Vector2(1, 1), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
        private static readonly Vector2[] EdgePivot = { new Vector2(0.5f, 1), new Vector2(0.5f, 0), new Vector2(0, 0.5f), new Vector2(1, 0.5f) };
        /// <summary>1px border as four anchored child images (no raycast); a transparent colour hides it.</summary>
        private void Edge(Node node, Color color)
        {
            if (node.outline == null && color.a > 0 && node.image != null)
            {
                node.outline = node.rect.gameObject.AddComponent<Outline>();
                node.outline.effectDistance = new Vector2(1, -1); node.outline.useGraphicAlpha = true;
            }
            if (node.outline != null) { node.outline.enabled = color.a > 0; node.outline.effectColor = color; }
        }
        /// <summary>The cyan corner brackets that frame primary B panels. Purely decorative (no raycast).</summary>
        public void Brackets(string id, Rect r, Color? color = null, float length = 14, float thickness = 2)
        {
            // Rounded cards replace tactical corner decoration. Kept as a no-op for older presenters.
        }
        /// <summary>Monospace label for codes, keys and numbers ("OP-05 // ANNIHILATION", "02:14").</summary>
        public void Code(string id, Rect rect, string value, int size, Color color, TextAnchor anchor = TextAnchor.MiddleLeft, bool bold = false)
        {
            LabelAligned(id, rect, value, size, color, bold, anchor); nodes[id].text.font = font;
        }
        /// <summary>Small filled or outlined chip (NEW tags, key badges, warnings). Never blocks the pointer.</summary>
        public void Chip(string id, Rect rect, string value, Color fill, Color ink, Color? edge = null, int size = 10, bool monospace = true)
        {
            var node = Get(id, rect); var image = Image(node); image.color = fill; image.raycastTarget = false; Edge(node, edge ?? Color.clear);
            var text = Text(node); text.rectTransform.offsetMin = new Vector2(2, 0); text.rectTransform.offsetMax = new Vector2(-2, 0);
            if (text.text != value) text.text = value;
            text.font = monospace ? mono : font; text.fontSize = size; text.color = ink; text.fontStyle = FontStyle.Bold; text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
        }
        public void Dispose()
        {
            instances.Remove(this);
            if (ownedEvents != null) UnityEngine.Object.Destroy(ownedEvents);
            if (root != null) UnityEngine.Object.Destroy(root);
            if (font != null) UnityEngine.Object.Destroy(font);
            if (mono != null) UnityEngine.Object.Destroy(mono);
            if (roundedSprite != null) UnityEngine.Object.Destroy(roundedSprite);
            if (roundedTexture != null) UnityEngine.Object.Destroy(roundedTexture);
        }
    }
}
