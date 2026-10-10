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
using Object = UnityEngine.Object;

namespace MassEngine.Game.Tests
{
    /// <summary>
    /// Visual record of every runtime screen on the official launcher (catalog, unit library, settings, confirmation,
    /// battle setup/running/move target/paused help/result, deployment, plan library, control point, three armies and
    /// a small window). Measures nothing beyond "the screen opens"; PNGs go to Logs/UiStyle/shots-*. Used to compare the
    /// UI before and after the B "tactical command" style unification. Never writes project assets or player settings.
    /// </summary>
    public sealed class WarSandboxUiStyleCaptureTests
    {
        private const string Menu = "Assets/Game/Content/Characters/OfficialRoster/Version03/LaunchMenu.unity";
        private float previousCaptureDelta;
        private string directory, shots;
        private WarSandboxSceneSession session;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), "WarSandboxUiStyle-" + Guid.NewGuid().ToString("N"));
            WarSandboxUnitStatStore.DefaultPathOverride = Path.Combine(directory, WarSandboxUnitStatStore.FileName);
            shots = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "UiStyle", "shots-" + DateTime.Now.ToString("MMdd-HHmmss")));
            Directory.CreateDirectory(shots);
            previousCaptureDelta = Time.captureDeltaTime; Time.captureDeltaTime = 0.02f;
            WarSandboxUGUI.ScreenSizeOverride = new Vector2(1280, 720);
            SceneManager.sceneLoaded += DisableAutoStart;
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(Menu, new LoadSceneParameters(LoadSceneMode.Single));
            SceneManager.sceneLoaded -= DisableAutoStart;
            session = WarSandboxSceneSession.Instance;
            Assert.That(session, Is.Not.Null);
            yield return null;
            Assert.That(session.State, Is.EqualTo(WarSandboxEntryState.Menu));
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            SceneManager.sceneLoaded -= DisableAutoStart;
            if (WarSandboxSceneSession.Instance != null) Object.Destroy(WarSandboxSceneSession.Instance.gameObject);
            Time.captureDeltaTime = previousCaptureDelta; Time.timeScale = 1;
            WarSandboxUnitStatStore.DefaultPathOverride = null;
            WarSandboxUGUI.ScreenSizeOverride = null;
            Scene old = SceneManager.GetActiveScene();
            Scene empty = SceneManager.CreateScene("Ui Style Capture Cleanup"); SceneManager.SetActiveScene(empty);
            if (old.IsValid() && old != empty) yield return SceneManager.UnloadSceneAsync(old);
            yield return null;
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }

        [UnityTest, Timeout(900000)]
        public IEnumerator CaptureEveryRuntimeScreen()
        {
            var front = session.GetComponent<WarSandboxFrontEnd>();
            yield return Capture("01-catalog");
            front.OpenLibrary(); yield return Capture("02-library"); Call(front, "CloseLibrary");
            session.OpenSettings(); yield return Capture("03-settings"); session.CloseSettings();
            Set(front, "confirmingQuit", true); session.BeginConfirmation(); yield return Capture("04-confirm-quit");
            session.CancelConfirmation(); Set(front, "confirmingQuit", false);

            yield return Enter("launch-open");
            var controller = session.Controller; var hud = controller.GetComponent<WarSandboxCommandHUD>();
            yield return Capture("05-battle-setup");
            Assert.That(controller.StartDefaultBattle(), Is.True);
            yield return Frames(90); yield return Capture("06-battle-running");
            Call(hud, "BeginMoveOrder"); yield return Capture("07-move-target"); Call(hud, "CancelMoveTarget");
            controller.TogglePause(); Set(hud, "helpOpen", true); yield return Capture("08-paused-help"); Set(hud, "helpOpen", false);
            Assert.That(controller.EndBattle(), Is.True); yield return Capture("09-result");
            var deployment = controller.GetComponent<WarSandboxDeploymentHUD>();
            deployment.RequestEdit(); yield return Capture("10-deployment");
            Call(deployment, "OpenPlanLibrary"); yield return Capture("11-plan-library");

            yield return Enter("launch-point");
            Assert.That(session.Controller.StartDefaultBattle(), Is.True);
            yield return Frames(150); yield return Capture("12-control-point");

            yield return Enter("launch-three");
            Assert.That(session.Controller.StartDefaultBattle(), Is.True);
            yield return Frames(90); yield return Capture("13-three-armies");
            WarSandboxUGUI.ScreenSizeOverride = new Vector2(640, 480);
            yield return Capture("14-small-window-battle");
            WarSandboxUGUI.ScreenSizeOverride = new Vector2(1280, 720);

            Assert.That(session.TryReturnToMenu(true, out string error), Is.True, error);
            yield return WaitForLoad();
            WarSandboxUGUI.ScreenSizeOverride = new Vector2(640, 480);
            yield return Capture("15-small-window-catalog");
            WarSandboxUGUI.ScreenSizeOverride = new Vector2(1280, 720);
            Debug.Log("[UiStyle] PASS shots=" + shots);
        }

        private IEnumerator Enter(string id)
        {
            if (session.State != WarSandboxEntryState.Menu)
            {
                Assert.That(session.TryReturnToMenu(true, out string back), Is.True, back);
                yield return WaitForLoad();
            }
            Assert.That(session.TryEnterBattlefield(id, false, out string error), Is.True, error);
            yield return WaitForLoad();
            Assert.That(session.State, Is.EqualTo(WarSandboxEntryState.Battle), session.Error);
            yield return Frames(30);
        }

        private static IEnumerator Frames(int count) { for (int i = 0; i < count; i++) yield return null; }

        private IEnumerator Capture(string name)
        {
            for (int i = 0; i < 15; i++) yield return null;
            float until = Time.realtimeSinceStartup + 0.4f;
            while (Time.realtimeSinceStartup < until) yield return null;
            CaptureViaCameras(Path.Combine(shots, name + ".png"));
            Debug.Log("[UiStyle] shot " + name);
        }

        // Same capture path as WarSandboxUnitStatsPlayModeTests: batch editors may never reach end-of-frame.
        private static void CaptureViaCameras(string path)
        {
            var size = WarSandboxUGUI.ScreenSizeOverride ?? new Vector2(Screen.width, Screen.height);
            int w = Mathf.Max(64, (int)size.x), h = Mathf.Max(64, (int)size.y);
            var target = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
            foreach (var camera in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).Where(c => c.isActiveAndEnabled).OrderBy(c => c.depth))
            { var old = camera.targetTexture; camera.targetTexture = target; camera.Render(); camera.targetTexture = old; }
            var ui = new GameObject("Capture UI Camera").AddComponent<Camera>();
            ui.enabled = false; ui.clearFlags = CameraClearFlags.Nothing; ui.cullingMask = 1 << 31; ui.targetTexture = target;
            var canvases = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None)
                .Where(c => c.isRootCanvas && c.isActiveAndEnabled && c.renderMode == RenderMode.ScreenSpaceOverlay).ToArray();
            var layers = new Dictionary<GameObject, int>();
            foreach (var canvas in canvases)
            {
                foreach (var t in canvas.GetComponentsInChildren<Transform>(true)) { layers[t.gameObject] = t.gameObject.layer; t.gameObject.layer = 31; }
                canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = ui; canvas.planeDistance = 1;
            }
            Canvas.ForceUpdateCanvases(); ui.Render();
            foreach (var canvas in canvases) { canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.worldCamera = null; }
            foreach (var pair in layers) if (pair.Key != null) pair.Key.layer = pair.Value;
            var previous = RenderTexture.active; RenderTexture.active = target;
            var texture = new Texture2D(w, h, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, w, h), 0, 0); texture.Apply(); RenderTexture.active = previous;
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.Destroy(texture); Object.Destroy(ui.gameObject); target.Release(); Object.Destroy(target);
        }

        private static void Set(object target, string field, object value) =>
            target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
        private static void Call(object target, string method) =>
            target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).Invoke(target, null);

        private IEnumerator WaitForLoad()
        {
            float deadline = Time.realtimeSinceStartup + 120f;
            while (session.IsLoading && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(session.IsLoading, Is.False, "Scene transition timed out. state=" + session.State + " error=" + session.Error);
            yield return null; yield return null;
        }

        private static void DisableAutoStart(Scene scene, LoadSceneMode mode)
        {
            if (scene.path == Menu && WarSandboxSceneSession.Instance != null)
                WarSandboxSceneSession.Instance.enterDefaultOnStart = false;
        }
    }
}
#endif
