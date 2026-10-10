#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace MassEngine.Game.Tests
{
    /// <summary>Actual retained UI callbacks/PNG renders; not OS input, human art acceptance or a new balance test.</summary>
    public sealed class ToyUiFlowPlayModeTests
    {
        private const string Menu = "Assets/Game/Content/Characters/OfficialRoster/Version08/LaunchMenu.unity";
        private string temporary, evidence;
        private float oldDelta;
        private WarSandboxSceneSession session;
        [UnitySetUp]
        public IEnumerator SetUp()
        {
            temporary = Path.Combine(Path.GetTempPath(), "WarSandboxToyUI-" + Guid.NewGuid().ToString("N"));
            WarSandboxUnitStatStore.DefaultPathOverride = Path.Combine(temporary, "globals", WarSandboxUnitStatStore.FileName);
            evidence = Environment.GetEnvironmentVariable("TOY_UI_OUTPUT"); Assert.That(evidence, Is.Not.Null.And.Not.Empty); Directory.CreateDirectory(evidence);
            oldDelta = Time.captureDeltaTime; Time.captureDeltaTime = .02f; WarSandboxUGUI.ScreenSizeOverride = new Vector2(1280, 720);
            SceneManager.sceneLoaded += DisableAutoStart;
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(Menu, new LoadSceneParameters(LoadSceneMode.Single));
            SceneManager.sceneLoaded -= DisableAutoStart;
            session = WarSandboxSceneSession.Instance; Assert.That(session, Is.Not.Null); yield return Frames(20);
            Assert.That(session.State, Is.EqualTo(WarSandboxEntryState.Menu));
        }
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            SceneManager.sceneLoaded -= DisableAutoStart;
            if (WarSandboxSceneSession.Instance != null) Object.Destroy(WarSandboxSceneSession.Instance.gameObject);
            Time.captureDeltaTime = oldDelta; Time.timeScale = 1; WarSandboxUGUI.ScreenSizeOverride = null; WarSandboxUnitStatStore.DefaultPathOverride = null;
            var old = SceneManager.GetActiveScene(); var empty = SceneManager.CreateScene("Quality Preview Cleanup"); SceneManager.SetActiveScene(empty);
            if (old.IsValid() && old != empty) yield return SceneManager.UnloadSceneAsync(old);
            yield return null; if (Directory.Exists(temporary)) Directory.Delete(temporary, true);
        }
        [UnityTest, Timeout(900000)]
        public IEnumerator StageFlowKeepsDraftAndRealPortraitWithoutApplyingOnReturn()
        {
            var front = session.GetComponent<WarSandboxFrontEnd>();
            Assert.That(front.HomeOpen, Is.True); Capture("01-home-720.png");
            Button("menu-library").onClick.Invoke(); yield return Frames(15);
            Assert.That(Active<Button>().Count(x => x.name.StartsWith("lib-item-")), Is.EqualTo(33));
            Capture("00-deduplicated-library.png");
            Button("lib-back").onClick.Invoke(); yield return Frames(15);
            Button("home-play").onClick.Invoke(); yield return Frames(15);
            Assert.That(front.HomeOpen, Is.False);
            yield return Select("robot-expressive"); Capture("02-catalog-720.png");
            int index = Array.FindIndex(session.catalog.entries, e => e.id == "robot-expressive");
            Button("card-" + index + "-enter").onClick.Invoke(); yield return WaitForLoad(); yield return Frames(30);
            var controller = session.Controller;
            var deployment = controller.GetComponent<WarSandboxRuntimeDeployment>();
            var editor = controller.GetComponent<WarSandboxDeploymentHUD>();
            deployment.PlanStore = new WarSandboxLocalPlanStore(Path.Combine(temporary, "plans"));
            Assert.That(deployment.Draft, Is.Not.Null, "Enter battlefield must open deployment rather than start combat");
            Assert.That(controller.Phase, Is.EqualTo(WarSandboxBattlePhase.Setup));
            Assert.That(Active<Button>().Any(x => x.name == "field-template"), Is.False, "Composition tools stay off deployment overview");
            Assert.That(Active<Button>().Any(x => x.name == "placement-clear"), Is.False, "Spatial editor starts collapsed");
            Capture("03-deployment-720.png");
            Button("map-place").onClick.Invoke(); yield return Frames(15);
            Assert.That(Active<Button>().Any(x => x.name == "placement-clear"), Is.True); Capture("03b-spatial-720.png");
            Button("placement-clear").onClick.Invoke(); yield return Frames(15);
            var draft = deployment.Draft; int team = draft[0].teamId, initial = draft[0].count;
            int liveInitial = controller.GetArmy(team).initialUnitCount;
            var camera = Camera.main; Vector3 position = camera != null ? camera.transform.position : Vector3.zero;
            Button("army-details-" + team).onClick.Invoke(); yield return Frames(15);
            Assert.That(editor.LegionOpen, Is.True);
            var entry = session.catalog.FindTemplate("roster-robot-expressive");
            Assert.That(Image("legion-unit-preview").texture, Is.TypeOf<RenderTexture>());
            Assert.That(Image("legion-unit-preview").GetComponent<UnitModelPreviewWidget>().Config, Is.SameAs(draft[0].template)); AssertImageAspect("legion-unit-preview");
            Assert.That(Active<Button>().Any(x => x.name == "deployment-start"), Is.False);
            Capture("04-legion-720.png");
            WarSandboxUGUI.ScreenSizeOverride = new Vector2(1920, 1080); yield return Frames(20);
            AssertImageAspect("legion-unit-preview"); Capture("05-legion-1080.png");
            WarSandboxUGUI.ScreenSizeOverride = new Vector2(1280, 720); yield return Frames(20);
            int beforeCount = draft.Count;
            Active<InputField>().Single(x => x.name == "input-count").text = "0";
            Button("legion-update-count").onClick.Invoke(); yield return Frames(15);
            Assert.That(draft[0].count, Is.EqualTo(initial), "Invalid count must not enter draft");
            Assert.That(Active<Text>().Any(t => t.text.Contains("大于0")), Is.True);
            Button("roster-add").onClick.Invoke(); yield return Frames(15);
            Assert.That(draft.Count, Is.EqualTo(beforeCount), "Failed commit must not add a formation");
            Active<InputField>().Single(x => x.name == "input-count").text = "invalid";
            Button("legion-done").onClick.Invoke(); yield return Frames(15);
            Assert.That(editor.LegionOpen, Is.True, "Bad numeric text must not silently navigate or apply");
            Active<InputField>().Single(x => x.name == "input-count").text = (initial + 8).ToString();
            yield return Frames(15);
            Assert.That(Active<Text>().Any(t => t.text.Contains("输入尚未更新")), Is.True);
            Button("legion-update-count").onClick.Invoke(); yield return Frames(15);
            Assert.That(draft[0].count, Is.EqualTo(initial + 8));
            Assert.That(controller.GetArmy(team).initialUnitCount, Is.EqualTo(liveInitial));
            Capture("08-count-updated-720.png");
            Button("field-template").onClick.Invoke(); yield return Frames(15); Assert.That(editor.PickerOpen, Is.True);
            Assert.That(Active<Button>().Count(b => b.name.StartsWith("template-")), Is.EqualTo(deployment.ChoiceTemplates.Count));
            Capture("09-picker-all-720.png");
            Button("picker-filter-2").onClick.Invoke(); yield return Frames(15);
            Assert.That(Active<Button>().Count(b => b.name.StartsWith("template-")), Is.EqualTo(deployment.ChoiceTemplates.Count(t => WarSandboxUnitStats.IsRanged(t))));
            Assert.That(draft[0].count, Is.EqualTo(initial + 8), "Filtering must not mutate the draft");
            Capture("10-picker-ranged-720.png");
            var originalTemplate = draft[0].template;
            int rangedIndex = Enumerable.Range(0, deployment.ChoiceTemplates.Count).First(i => WarSandboxUnitStats.IsRanged(deployment.ChoiceTemplates[i]));
            Button("template-" + rangedIndex).onClick.Invoke(); yield return Frames(15);
            Assert.That(editor.PickerOpen, Is.False);
            Assert.That(draft[0].template, Is.SameAs(deployment.ChoiceTemplates[rangedIndex]), "Filtered card must select its actual template, not its visible slot index");
            Assert.That(Image("legion-unit-preview").GetComponent<UnitModelPreviewWidget>().Config, Is.SameAs(draft[0].template));
            Assert.That(controller.GetArmy(team).initialUnitCount, Is.EqualTo(liveInitial));
            Button("field-template").onClick.Invoke(); yield return Frames(15);
            int originalIndex = Enumerable.Range(0, deployment.ChoiceTemplates.Count).First(i => deployment.ChoiceTemplates[i] == originalTemplate);
            Button("template-" + originalIndex).onClick.Invoke(); yield return Frames(15);
            Assert.That(draft[0].template, Is.SameAs(originalTemplate));
            Active<InputField>().Single(x => x.name == "input-count").text = (initial + 8).ToString();
            Button("legion-update-count").onClick.Invoke(); yield return Frames(15);
            Button("field-template").onClick.Invoke(); yield return Frames(15);
            Button("picker-filter-1").onClick.Invoke(); yield return Frames(15);
            Assert.That(Active<Button>().Count(b => b.name.StartsWith("template-")), Is.EqualTo(deployment.ChoiceTemplates.Count(t => !WarSandboxUnitStats.IsRanged(t))));
            Button("picker-filter-0").onClick.Invoke(); yield return Frames(15);
            Button("picker-back").onClick.Invoke(); yield return Frames(15);
            Button("field-army").onClick.Invoke(); yield return Frames(15);
            Button("army-assign-cancel").onClick.Invoke(); yield return Frames(15);
            Button("field-unit-stats").onClick.Invoke(); yield return Frames(15);
            Button("st-done").onClick.Invoke(); yield return Frames(15);
            Assert.That(editor.LegionOpen, Is.True);
            Button("legion-done").onClick.Invoke(); yield return Frames(15);
            Assert.That(editor.LegionOpen, Is.False); Assert.That(deployment.Draft, Is.SameAs(draft));
            Assert.That(draft[0].count, Is.EqualTo(initial + 8));
            Assert.That(controller.GetArmy(team).initialUnitCount, Is.EqualTo(liveInitial), "Returning does not apply runtime roster");
            Assert.That(controller.Phase, Is.EqualTo(WarSandboxBattlePhase.Setup));
            if (camera != null) Assert.That(camera.transform.position, Is.EqualTo(position), "Dossier must not disturb deployment camera");
            Button("deployment-plans").onClick.Invoke(); yield return Frames(15);
            Button("plans-back").onClick.Invoke(); yield return Frames(15);
            Assert.That(deployment.TryValidate(out string error), Is.True, error);
            Button("deployment-start").onClick.Invoke(); yield return Frames(30);
            Assert.That(controller.Phase, Is.EqualTo(WarSandboxBattlePhase.Running));
            Assert.That(controller.GetArmy(team).initialUnitCount, Is.EqualTo(initial + 8));
            Button("start").onClick.Invoke(); yield return Frames(15);
            Assert.That(controller.Phase, Is.EqualTo(WarSandboxBattlePhase.Paused)); Capture("06-battle-720.png");
            Button("battle-tools").onClick.Invoke(); yield return Frames(15);
            Button("end-battle").onClick.Invoke(); yield return Frames(20);
            Assert.That(controller.BattleResult.valid, Is.True);
            Assert.That(Active<Button>().Any(x => x.name == "attack"), Is.False, "Result replaces command layer");
            Assert.That(Active<Button>().Any(x => x.name == "result-edit"), Is.True); Capture("07-result-720.png");
            Debug.Log("TOY_UI_FLOW_PASS callbacks, draft-only return, invalid input, real portrait, 720/1080, apply, pause and terminal command suppression.");
        }
        private IEnumerator Select(string id)
        {
            int index = Array.FindIndex(session.catalog.entries, e => e.id == id); Assert.That(index, Is.GreaterThanOrEqualTo(0));
            // A retained list may scroll; the registered callback is still the actual card callback.
            Button("card-" + index).onClick.Invoke(); yield return Frames(12);
        }
        private IEnumerator WaitForLoad()
        {
            float until = Time.realtimeSinceStartup + 120; while (session.IsLoading && Time.realtimeSinceStartup < until) yield return null;
            Assert.That(session.IsLoading, Is.False, session.Error); Assert.That(session.State, Is.EqualTo(WarSandboxEntryState.Battle), session.Error); yield return Frames(15);
        }
        private static IEnumerator Frames(int count) { for (int i = 0; i < count; i++) yield return null; yield return new WaitForSecondsRealtime(.22f); }
        private static IEnumerable<T> Active<T>() where T : Component => Object.FindObjectsByType<T>(FindObjectsSortMode.None).Where(c => c.gameObject.activeInHierarchy);
        private static Button Button(string name) { var matches = Active<Button>().Where(b => b.name == name).ToArray(); Assert.That(matches.Length, Is.EqualTo(1), "Expected exactly one active button: " + name); return matches[0]; }
        private static RawImage Image(string name) => Active<RawImage>().Single(i => i.name == name);
        private static void AssertImageAspect(string name)
        {
            var image = Image(name); Rect rect = image.rectTransform.rect;
            Assert.That(rect.width, Is.GreaterThan(0)); Assert.That(rect.height, Is.GreaterThan(0));
            Assert.That(rect.width / rect.height, Is.EqualTo(image.texture.width / (float)image.texture.height).Within(image.texture is RenderTexture ? 2f / image.texture.height : .0001f), "Image must not stretch: " + name);
        }
        private static void DisableAutoStart(Scene scene, LoadSceneMode mode)
        { if (scene.path == Menu && WarSandboxSceneSession.Instance != null) WarSandboxSceneSession.Instance.enterDefaultOnStart = false; }
        private void Capture(string name)
        {
            string path = Path.Combine(evidence, name); Assert.That(File.Exists(path), Is.False, "Do not overwrite evidence.");
            var size = WarSandboxUGUI.ScreenSizeOverride ?? new Vector2(Screen.width, Screen.height); int w = (int)size.x, h = (int)size.y;
            var target = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32); var texture = new Texture2D(w, h, TextureFormat.RGB24, false);
            var oldTarget = RenderTexture.active;
            var camera = new GameObject("Quality PNG UI Camera").AddComponent<Camera>(); camera.enabled = false; camera.clearFlags = CameraClearFlags.Nothing; camera.cullingMask = 1 << 31; camera.targetTexture = target;
            var canvases = Active<Canvas>().Where(c => c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay).ToArray(); var layers = new Dictionary<GameObject, int>();
            try
            {
                foreach (var real in Active<Camera>().Where(c => c != camera).OrderBy(c => c.depth))
                { var old = real.targetTexture; try { real.targetTexture = target; real.Render(); } finally { real.targetTexture = old; } }
                foreach (var canvas in canvases)
                {
                    foreach (var t in canvas.GetComponentsInChildren<Transform>(true)) { layers[t.gameObject] = t.gameObject.layer; t.gameObject.layer = 31; }
                    canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
                }
                Canvas.ForceUpdateCanvases(); camera.Render(); RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, w, h), 0, 0); texture.Apply();
                File.WriteAllBytes(path, texture.EncodeToPNG());
            }
            finally
            {
                foreach (var canvas in canvases) { canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.worldCamera = null; }
                foreach (var pair in layers) if (pair.Key != null) pair.Key.layer = pair.Value;
                RenderTexture.active = oldTarget; Object.Destroy(texture); Object.Destroy(camera.gameObject); target.Release(); Object.Destroy(target);
            }
        }
    }
}
#endif
