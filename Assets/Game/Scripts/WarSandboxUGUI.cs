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
        public static readonly Color Background = new Color32(239, 243, 246, 255);
        public static readonly Color Surface = new Color32(255, 255, 255, 250);
        public static readonly Color Ink = new Color32(27, 43, 55, 255);
        public static readonly Color Muted = new Color32(93, 111, 123, 255);
        public static readonly Color Accent = new Color32(16, 111, 103, 255);
        public static readonly Color Soft = new Color32(225, 237, 236, 255);
        public static readonly Color Line = new Color32(211, 222, 228, 255);
        public static readonly Color Danger = new Color32(164, 50, 47, 255);
        private sealed class Node
        {
            public RectTransform rect;
            public Text text;
            public Image image;
            public Button button;
            public InputField input;
            public RawImage raw;
            public Action click;
            public Action<string> change;
            public Action<Vector2, int, bool> pointer;
            public RectTransform content;
            public bool used;
        }
        private readonly Dictionary<string, Node> nodes = new Dictionary<string, Node>();
        private readonly Dictionary<RectTransform, int> siblingIndices = new Dictionary<RectTransform, int>();
        private readonly GameObject root;
        private readonly Font font;
        private readonly Canvas canvas;
        private readonly GraphicRaycaster raycaster;
        private static readonly List<WarSandboxUGUI> instances = new List<WarSandboxUGUI>();
        private static readonly List<RaycastResult> hits = new List<RaycastResult>();
        private RectTransform parent;
        private GameObject ownedEvents;
        public float Width => Screen.width / Scale;
        public float Height => Screen.height / Scale;
        public float Scale => Mathf.Clamp(Mathf.Min(Screen.width / 1280f, Screen.height / 720f), 0.85f, 1.5f);
        public bool Visible => root != null && root.activeInHierarchy;

        public WarSandboxUGUI(Transform owner, string name, int order)
        {
            root = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            root.transform.SetParent(owner, false);
            canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = order;
            root.GetComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            raycaster = root.GetComponent<GraphicRaycaster>();
            font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "SimHei", "Arial" }, 18);
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
        public void Panel(string id, Rect rect, Color? color = null, bool block = true)
        { var image = Image(Get(id, rect)); image.color = color ?? Surface; image.raycastTarget = block; }
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
            node.click = click; node.button.interactable = enabled;
            image.color = primary ? Accent : selected ? Soft : Background;
            var colors = node.button.colors; colors.normalColor = Color.white; colors.highlightedColor = new Color(0.88f, 0.94f, 0.94f);
            colors.pressedColor = new Color(0.72f, 0.86f, 0.85f); colors.disabledColor = new Color(1, 1, 1, 0.45f); node.button.colors = colors;
            var text = Text(node); if (text.text != value) text.text = value;
            text.fontSize = 15; text.color = enabled ? primary ? Color.white : Ink : Muted;
            text.fontStyle = primary || selected ? FontStyle.Bold : FontStyle.Normal; text.alignment = TextAnchor.MiddleCenter;
        }
        public void Field(string id, Rect rect, string value, Action<string> change, int limit = 24)
        {
            var node = Get(id, rect); Image(node).color = Background;
            if (node.input == null)
            {
                var text = Text(node); text.alignment = TextAnchor.MiddleLeft; text.fontSize = 16; text.color = Ink;
                node.input = node.rect.gameObject.AddComponent<InputField>(); node.input.textComponent = text;
                node.input.targetGraphic = node.image; node.input.lineType = InputField.LineType.SingleLine;
                node.input.onValueChanged.AddListener(v => node.change?.Invoke(v));
            }
            node.change = change; node.input.characterLimit = limit;
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
        public void Picture(string id, Rect rect, Texture texture)
        {
            var node = Get(id, rect);
            if (node.raw == null) { node.raw = node.rect.gameObject.AddComponent<RawImage>(); node.raw.raycastTarget = false; }
            node.raw.texture = texture; node.raw.color = texture != null ? Color.white : Background;
        }
        public void Scroll(string id, Rect rect, float contentHeight)
        {
            var node = Get(id, rect);
            if (node.content == null)
            {
                Image(node).color = Surface;
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
        public void Dispose()
        {
            instances.Remove(this);
            if (ownedEvents != null) UnityEngine.Object.Destroy(ownedEvents);
            if (root != null) UnityEngine.Object.Destroy(root);
            if (font != null) UnityEngine.Object.Destroy(font);
        }
    }
}
