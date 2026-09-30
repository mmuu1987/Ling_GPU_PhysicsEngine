using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace MassEngine.Game.Tests
{
    public sealed class WarSandboxUGUITests
    {
        private GameObject owner;
        private WarSandboxUGUI ui;
        private readonly Rect rect = new Rect(10, 10, 220, 42);
        [SetUp]
        public void SetUp()
        {
            owner = new GameObject("UI test owner");
            ui = new WarSandboxUGUI(owner.transform, "Test Canvas", 100);
        }
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            ui.Dispose(); Object.Destroy(owner); yield return null;
        }
        [Test]
        public void VolumeSliderUsesLatestCallbackWithoutSavingOnRefresh()
        {
            int changes = 0; float volume = 0;
            ui.Begin(); ui.Slider("volume", rect, .65f, value => changes += 100); ui.End();
            var slider = owner.GetComponentInChildren<Slider>();
            ui.Begin(); ui.Slider("volume", rect, .27f, value => { changes++; volume = value; }); ui.End();
            Assert.That(changes, Is.Zero); Assert.That(slider.value, Is.EqualTo(.27f));
            slider.value = .4f; Assert.That(changes, Is.EqualTo(1)); Assert.That(volume, Is.EqualTo(.4f));
            Assert.That(owner.GetComponentsInChildren<Slider>().Length, Is.EqualTo(1));
        }

        [Test]
        public void RepeatedRefreshRetainsNodesAndUsesLatestButtonAction()
        {
            int result = 0;
            ui.Begin(); ui.Button("action", rect, "old", () => result = 1); ui.End();
            var button = owner.GetComponentInChildren<Button>(); int id = button.GetInstanceID();
            for (int i = 0; i < 12; i++) { ui.Begin(); ui.Button("action", rect, "new", () => result += 2); ui.End(); }
            Assert.That(owner.GetComponentsInChildren<Button>().Length, Is.EqualTo(1));
            Assert.That(owner.GetComponentInChildren<Button>().GetInstanceID(), Is.EqualTo(id));
            button.onClick.Invoke(); Assert.That(result, Is.EqualTo(2), "Listeners must not accumulate.");
            Assert.That(button.GetComponentInChildren<Text>().text, Is.EqualTo("new"));
            Assert.That(button.navigation.mode, Is.EqualTo(Navigation.Mode.None));
        }
        [Test]
        public void HiddenViewsDeactivateTheirControlsWithoutDestroyingNodes()
        {
            ui.Begin(); ui.Button("action", rect, "button", () => { }); ui.End();
            var button = owner.GetComponentInChildren<Button>();
            ui.Begin(); ui.Label("status", rect, "status"); ui.End();
            Assert.That(button.gameObject.activeInHierarchy, Is.False);
            ui.Begin(); ui.Button("action", rect, "back", () => { }); ui.End();
            Assert.That(owner.GetComponentInChildren<Button>(), Is.SameAs(button));
            ui.SetVisible(false); Assert.That(button.gameObject.activeInHierarchy, Is.False);
        }
        [Test]
        public void ReusedModalStaysAboveNewlyCreatedContent()
        {
            ui.Begin(); ui.Panel("background", rect); ui.Panel("modal", rect); ui.End();
            ui.Begin(); ui.Panel("background", rect); ui.Button("later-row", rect, "row", () => { }); ui.Panel("modal", rect); ui.End();
            var modal = owner.transform.Find("Test Canvas/modal");
            var row = owner.transform.Find("Test Canvas/later-row");
            Assert.That(modal.GetSiblingIndex(), Is.GreaterThan(row.GetSiblingIndex()));
        }
        [Test]
        public void InputChangesReachDraftButProgrammaticRefreshDoesNotFireChange()
        {
            int changes = 0; string value = null;
            ui.Begin(); ui.Field("input", rect, "100", s => { changes++; value = s; }); ui.End();
            var input = owner.GetComponentInChildren<InputField>();
            Assert.That(changes, Is.Zero);
            input.text = "200"; Assert.That(value, Is.EqualTo("200")); Assert.That(changes, Is.EqualTo(1));
            ui.Begin(); ui.Field("input", rect, "300", s => { changes++; value = s; }); ui.End();
            Assert.That(input.text, Is.EqualTo("300")); Assert.That(changes, Is.EqualTo(1));
        }
        [Test]
        public void ScrollViewClipsContentAndKeepsItsScrollPosition()
        {
            ui.Begin(); ui.Scroll("scroll", new Rect(0, 0, 300, 200), 900); ui.Button("inside", rect, "button", () => { }); ui.EndScroll(); ui.End();
            var scroll = owner.GetComponentInChildren<ScrollRect>();
            Assert.That(scroll.GetComponent<RectMask2D>(), Is.Not.Null);
            Assert.That(scroll.horizontal, Is.False);
            scroll.content.anchoredPosition = new Vector2(0, 120);
            ui.Begin(); ui.Scroll("scroll", new Rect(0, 0, 300, 200), 900); ui.Button("inside", rect, "button", () => { }); ui.EndScroll(); ui.End();
            Assert.That(scroll.content.anchoredPosition.y, Is.EqualTo(120));
        }
        [UnityTest]
        public IEnumerator CanvasUsesRaycastablePanelsButNotDecorativeText()
        {
            ui.Begin(); ui.Panel("modal", new Rect(0, 0, ui.Width, ui.Height)); ui.Label("label", rect, "title"); ui.End();
            yield return null; Canvas.ForceUpdateCanvases();
            Assert.That(owner.GetComponentInChildren<Text>().raycastTarget, Is.False);
            var canvas = owner.GetComponentInChildren<Canvas>();
            Assert.That(canvas.renderMode, Is.EqualTo(RenderMode.ScreenSpaceOverlay));
            var data = new PointerEventData(EventSystem.current) { position = new Vector2(Screen.width / 2f, Screen.height / 2f) };
            var hits = new System.Collections.Generic.List<RaycastResult>(); canvas.GetComponent<GraphicRaycaster>().Raycast(data, hits);
            Assert.That(hits.Count, Is.GreaterThan(0));
            Assert.That(hits[0].gameObject.name, Is.EqualTo("modal"));
        }
        [UnityTest]
        public IEnumerator PointerAreaDispatchesCoordinatesAndRightClick()
        {
            Vector2 received = Vector2.zero; int button = -1;
            ui.Begin(); ui.PointerArea("map", new Rect(10, 10, 200, 200), (p, b, shift) => { received = p; button = b; }); ui.End();
            yield return null; Canvas.ForceUpdateCanvases();
            var target = owner.GetComponentInChildren<EventTrigger>(); var transform = (RectTransform)target.transform;
            var point = RectTransformUtility.WorldToScreenPoint(null, transform.TransformPoint(new Vector3(100, -100, 0)));
            var data = new PointerEventData(EventSystem.current) { position = point, button = PointerEventData.InputButton.Right };
            ExecuteEvents.Execute(target.gameObject, data, ExecuteEvents.pointerClickHandler);
            Assert.That(button, Is.EqualTo(1)); Assert.That(received.x, Is.EqualTo(0.5f).Within(0.01f)); Assert.That(received.y, Is.EqualTo(0.5f).Within(0.01f));
        }
        [UnityTest]
        public IEnumerator HidingCanvasDoesNotDisableItsEventSystem()
        {
            ui.Begin(); ui.Button("button", rect, "button", () => { }); ui.End(); yield return null;
            var system = EventSystem.current; Assert.That(system, Is.Not.Null);
            ui.SetVisible(false); yield return null;
            Assert.That(system.isActiveAndEnabled, Is.True);
        }
        [UnityTest]
        public IEnumerator DisposeReleasesOwnedCanvasAndFont()
        {
            ui.Begin(); ui.Label("label", rect, "test"); ui.End();
            var canvas = owner.GetComponentInChildren<Canvas>(); var font = owner.GetComponentInChildren<Text>().font;
            ui.Dispose(); yield return null;
            Assert.That(canvas == null, Is.True); Assert.That(font == null, Is.True);
        }
    }
}
