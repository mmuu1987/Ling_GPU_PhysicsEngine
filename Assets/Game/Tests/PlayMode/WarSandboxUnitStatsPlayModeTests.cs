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
    /// End-to-end on the official launcher: global stats (兵种库) reach the GPU when a battlefield loads,
    /// the deployment dossier edits plan-local values and pushes them to the global layer. Uses a
    /// temporary global file; the player's real persistentDataPath is never touched. Screenshots go to Logs/AgentUnitStats.
    /// </summary>
    public sealed class WarSandboxUnitStatsPlayModeTests
    {
        private const string Menu = "Assets/Game/Content/Characters/OfficialRoster/Version03/LaunchMenu.unity";
        private const string Field = "dragons-dragon-evolved-phalanx";
        private float previousCaptureDelta;
        private string directory, shots;
        private WarSandboxSceneSession session;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), "WarSandboxUnitStatsPlay-" + Guid.NewGuid().ToString("N"));
            WarSandboxUnitStatStore.DefaultPathOverride = Path.Combine(directory, WarSandboxUnitStatStore.FileName);
            shots = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "AgentUnitStats", "shots-" + DateTime.Now.ToString("MMdd-HHmmss")));
            Directory.CreateDirectory(shots);
            previousCaptureDelta = Time.captureDeltaTime; Time.captureDeltaTime = 0.02f;
            // Batch editors report a 640x480 screen; lay the UI out (and capture) at the 720p reference size.
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
            Scene empty = SceneManager.CreateScene("Unit Stats Test Cleanup"); SceneManager.SetActiveScene(empty);
            if (old.IsValid() && old != empty) yield return SceneManager.UnloadSceneAsync(old);
            yield return null;
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }

        [UnityTest]
        public IEnumerator GlobalStatsReachTheGpuAndTheDossierPushesPlanValues()
        {
            Debug.Log("[UnitStatsPlay] screen " + Screen.width + "x" + Screen.height + " shots " + shots);
            var catalog = session.catalog;

            // 1. Official values: no global file, the authored scenario runs untouched.
            Assert.That(session.TryEnterBattlefield(Field, false, out string error), Is.True, error);
            yield return WaitForLoad();
            Assert.That(session.State, Is.EqualTo(WarSandboxEntryState.Battle), session.Error);
            var controller = session.Controller; var manager = controller.manager;
            var deployment = controller.GetComponent<WarSandboxRuntimeDeployment>();
            Assert.That(deployment.HasCustomStats, Is.False); Assert.That(session.StatsWarning, Is.Null);
            var authored = manager.scenarioConfig;
            Assert.That(authored.name, Does.Not.Contain("(Runtime)"));
            var dragon = authored.unitTypes.First(WarSandboxUnitStats.IsRanged);
            var knight = authored.unitTypes.First(u => !WarSandboxUnitStats.IsRanged(u));
            Assert.That(catalog.TryGetTemplateId(dragon, out string dragonId, out int dragonRevision), Is.True);
            int officialDamage = dragon.combatConfig.attackDamage, knightDamage = knight.combatConfig.attackDamage, officialHp = dragon.combatConfig.maxHp;
            float officialSplash = dragon.combatConfig.projectileSplashRadius, officialSpeed = dragon.movementConfig.maxSpeed;
            Debug.Log("[UnitStatsPlay] dragon " + dragonId + " damage " + officialDamage + " splash " + officialSplash + " hp " + officialHp + " speed " + officialSpeed);
            yield return Capture("01-battle-official");

            // 2. A global override for the dragon template (as the 兵种库 would write it).
            var global = new WarSandboxGlobalStats(); var set = new WarSandboxStatSet();
            set.Set(WarSandboxUnitStat.AttackDamage, officialDamage * 1.5f); set.Set(WarSandboxUnitStat.ProjectileSplashRadius, officialSplash + 1.5f);
            global.Replace(dragonId, dragonRevision, set);
            Assert.That(new WarSandboxUnitStatStore().TrySave(global, "PlayMode 测试：巨龙", out error), Is.True, error);
            int tunedDamage = Mathf.RoundToInt(officialDamage * 1.5f);

            // 3. Catalog and unit library.
            Assert.That(session.TryReturnToMenu(true, out error), Is.True, error);
            yield return WaitForLoad();
            Assert.That(session.State, Is.EqualTo(WarSandboxEntryState.Menu), session.Error);
            yield return Capture("02-catalog");
            var front = session.GetComponent<WarSandboxFrontEnd>();
            front.OpenLibrary();
            var listed = catalog.templates.Where(t => t != null && t.config != null && !string.IsNullOrWhiteSpace(t.templateId)).ToList();
            Set(front, "libraryIndex", listed.FindIndex(t => t.templateId == dragonId));
            yield return Capture("03-library");
            Assert.That(front.LibraryOpen, Is.True);
            Call(front, "CloseLibrary");

            // 4. Re-entering applies the global layer at load time; the GPU sees the tuned values.
            Assert.That(session.TryEnterBattlefield(Field, false, out error), Is.True, error);
            yield return WaitForLoad();
            Assert.That(session.State, Is.EqualTo(WarSandboxEntryState.Battle), session.Error);
            controller = session.Controller; manager = controller.manager; deployment = controller.GetComponent<WarSandboxRuntimeDeployment>();
            Assert.That(session.StatsWarning, Is.Null);
            Assert.That(deployment.HasCustomStats, Is.True);
            Assert.That(manager.scenarioConfig.name, Does.Contain("(Runtime)"));
            Assert.That(manager.UnitTypes.UnitTypeCount, Is.EqualTo(manager.scenarioConfig.unitTypes.Length));
            var gpu = new UnitTypeGpuSettings[manager.UnitTypes.UnitTypeCount];
            Assert.That(manager.UnitTypes.FillGpuSettings(gpu), Is.True);
            for (int i = 0; i < gpu.Length; i++)
            {
                bool isDragon = WarSandboxUnitStats.IsRanged(manager.scenarioConfig.unitTypes[i]);
                Assert.That(gpu[i].attackDamage, Is.EqualTo(isDragon ? tunedDamage : knightDamage), "unit type " + i);
            }
            Assert.That(dragon.combatConfig.attackDamage, Is.EqualTo(officialDamage), "The template asset is never written.");
            yield return Capture("04-battle-custom");
            Assert.That(controller.StartDefaultBattle(), Is.True);
            for (int i = 0; i < 120; i++) yield return null;
            Assert.That(manager.IsBattleRunning, Is.True);
            yield return Capture("05-battle-running-custom");
            controller.ResetBattle(); yield return null; yield return null;

            // 5. Deployment dossier: plan-local edits, push to global, apply.
            var hud = controller.GetComponent<WarSandboxDeploymentHUD>();
            hud.RequestEdit(); yield return null;
            Assert.That(deployment.IsEditing, Is.True, deployment.Error);
            int entry = Enumerable.Range(0, deployment.Draft.Count).First(i => deployment.Draft[i].template == dragon);
            Set(hud, "selected", entry);
            yield return Capture("06-deployment-before");
            Call(hud, "OpenStats");
            Assert.That(hud.StatsOpen, Is.True);
            Assert.That(deployment.SetStat(dragon, WarSandboxUnitStat.MaxHp, officialHp * 2), Is.True);
            Assert.That(deployment.SetStat(dragon, WarSandboxUnitStat.MaxSpeed, officialSpeed + 1), Is.True);
            yield return Capture("07-dossier");
            Set(hud, "pushConfirm", true);
            yield return Capture("08-push-confirm");
            Set(hud, "pushConfirm", false);
            Assert.That(deployment.TryPushStats(dragon, true, out error), Is.True, error);
            Assert.That(new WarSandboxUnitStatStore().Current.Get(dragonId).Count, Is.EqualTo(4));
            Assert.That(deployment.Draft.Stats.IsEmpty, Is.True);
            yield return Capture("09-dossier-after-push");
            Call(hud, "CloseStats");
            yield return Capture("10-deployment-after");
            Assert.That(deployment.TryApply(out error), Is.True, error);
            Assert.That(manager.UnitTypes.FillGpuSettings(gpu = new UnitTypeGpuSettings[manager.UnitTypes.UnitTypeCount]), Is.True);
            for (int i = 0; i < gpu.Length; i++)
                if (WarSandboxUnitStats.IsRanged(manager.scenarioConfig.unitTypes[i]))
                {
                    Assert.That(gpu[i].maxSpeed, Is.EqualTo(officialSpeed + 1).Within(0.001f));
                    Assert.That(manager.scenarioConfig.unitTypes[i].combatConfig.maxHp, Is.EqualTo(officialHp * 2));
                }
            Assert.That((dragon.combatConfig.maxHp, dragon.movementConfig.maxSpeed), Is.EqualTo((officialHp, officialSpeed)));

            Assert.That(session.TryReturnToMenu(true, out error), Is.True, error);
            yield return WaitForLoad();
            Debug.Log("[UnitStatsPlay] PASS shots=" + shots);
        }

        private IEnumerator Capture(string name)
        {
            for (int i = 0; i < 15; i++) yield return null;
            float until = Time.realtimeSinceStartup + 0.4f;
            while (Time.realtimeSinceStartup < until) yield return null;
            string path = Path.Combine(shots, name + ".png");
            CaptureViaCameras(path);
            Debug.Log("[UnitStatsPlay] shot " + name);
        }

        // Batch mode may never reach end-of-frame; render the cameras and overlay canvases into a texture instead.
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
            target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);

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
