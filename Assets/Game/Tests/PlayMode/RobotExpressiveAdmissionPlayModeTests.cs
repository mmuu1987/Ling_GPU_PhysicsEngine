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
    public sealed class RobotExpressiveAdmissionPlayModeTests
    {
        private const string Menu = "Assets/Game/OfficialRoster/Version08/LaunchMenu.unity";
        private string temporary, evidence;
        private float oldDelta;
        private WarSandboxSceneSession session;
        [UnitySetUp]
        public IEnumerator SetUp()
        {
            temporary = Path.Combine(Path.GetTempPath(), "WarSandboxRobotAdmission-" + Guid.NewGuid().ToString("N"));
            WarSandboxUnitStatStore.DefaultPathOverride = Path.Combine(temporary, "globals", WarSandboxUnitStatStore.FileName);
            evidence = Environment.GetEnvironmentVariable("ROBOT_FORMAL_OUTPUT"); Assert.That(evidence, Is.Not.Null.And.Not.Empty);
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
        public IEnumerator RealFormalRobotPreviewLibraryAndDeploymentUseTheNewOfficialIdentity()
        {
            const string id="roster-robot-expressive",battle="robot-expressive";
            var front=session.GetComponent<WarSandboxFrontEnd>();var entry=session.catalog.FindTemplate(id);Assert.That(entry,Is.Not.Null);Assert.That(entry.hiddenFromSelection,Is.False);
            yield return Select(battle);Assert.That(Image("detail-unit-image").texture,Is.SameAs(entry.unitPreview));AssertImageAspect("detail-unit-image");Capture("robot-catalog-1280.png");
            Button("detail-unit-zoom").onClick.Invoke();yield return Frames(10);Assert.That(Image("unit-zoom-image").texture,Is.SameAs(entry.unitPreview));Capture("robot-full-body.png");
            Button("unit-zoom-close").onClick.Invoke();yield return Frames(10);
            WarSandboxUGUI.ScreenSizeOverride=new Vector2(640,480);yield return Frames(15);AssertImageAspect("detail-unit-image");Capture("robot-catalog-640.png");
            WarSandboxUGUI.ScreenSizeOverride=new Vector2(1280,720);yield return Frames(15);
            front.OpenLibrary();yield return Frames(15);
            var list=(List<WarSandboxUnitTemplateEntry>)front.GetType().GetMethod("LibraryTemplates",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(front,null);
            Assert.That(list.Count,Is.EqualTo(66));int index=list.FindIndex(x=>x.templateId==id);Assert.That(index,Is.GreaterThanOrEqualTo(0));
            Button("lib-item-"+index).onClick.Invoke();yield return Frames(15);Assert.That(Image("lib-unit-image").texture,Is.SameAs(entry.unitPreview));Capture("robot-library-1280.png");
            Button("lib-open-preview").onClick.Invoke();yield return Frames(10);Assert.That(Image("unit-zoom-image").texture,Is.SameAs(entry.unitPreview));
            Button("unit-zoom-close").onClick.Invoke();yield return Frames(10);Button("lib-back").onClick.Invoke();yield return Frames(15);
            Assert.That(session.TryEnterBattlefield(battle,false,out string error),Is.True,error);yield return WaitForLoad();yield return Frames(20);
            var deployment=session.Controller.GetComponent<WarSandboxRuntimeDeployment>();Assert.That(deployment.SourceScenario==null||deployment.SourceScenario.unitTypes.Length==2,Is.True);
            Capture("robot-setup.png");deployment.PlanStore=new WarSandboxLocalPlanStore(Path.Combine(temporary,"plans"));Assert.That(deployment.TryBeginEdit(false,out error),Is.True,error);yield return Frames(15);
            Assert.That(deployment.Templates.Contains(entry.config),Is.True);Assert.That(deployment.Draft[0].template,Is.SameAs(entry.config));
            Capture("robot-deployment.png");Assert.That(deployment.TryValidate(out error),Is.True,error);
            Debug.Log("ROBOT_FORMAL_UI_PASS actual catalogue detail, zoom, 640/1280 aspect, 66-unit library and formal deployment.");
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
        private static IEnumerator Frames(int count) { for (int i = 0; i < count; i++) yield return null; }
        private static IEnumerable<T> Active<T>() where T : Component => Object.FindObjectsByType<T>(FindObjectsSortMode.None).Where(c => c.gameObject.activeInHierarchy);
        private static Button Button(string name) => Active<Button>().Single(b => b.name == name);
        private static RawImage Image(string name) => Active<RawImage>().Single(i => i.name == name);
        private static void AssertImageAspect(string name)
        {
            var image = Image(name); Rect rect = image.rectTransform.rect;
            Assert.That(rect.width, Is.GreaterThan(0)); Assert.That(rect.height, Is.GreaterThan(0));
            Assert.That(rect.width / rect.height, Is.EqualTo(image.texture.width / (float)image.texture.height).Within(.0001f), "Image must not stretch: " + name);
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

