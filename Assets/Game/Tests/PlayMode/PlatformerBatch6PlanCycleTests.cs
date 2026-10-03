#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using Debug = UnityEngine.Debug;

namespace MassEngine.Game.Tests
{
    /// <summary>Owned-menu entry and real saved plans in two separate editor processes. No player data, fixed winner or physical-input claim.</summary>
    public sealed class PlatformerBatch6PlanBuildSetup : IPrebuildSetup
    {
        public void Setup()
        {
            const string menu = "Assets/Game/PlatformerBatch6/Prepared01/Integrated/Menu.unity";
            var catalog = AssetDatabase.LoadAssetAtPath<WarSandboxBattlefieldCatalog>("Assets/Game/PlatformerBatch6/Prepared01/Integrated/Catalog.asset");
            if (catalog == null) throw new InvalidOperationException("Owned collection is missing.");
            // SceneManager's player scene list is captured at Play startup, not by changing settings during Play.
            EditorBuildSettings.scenes = new[] { menu }.Concat(catalog.entries.Select(e => e.scenePath)).Distinct().Select(p => new EditorBuildSettingsScene(p, true)).ToArray();
        }
    }

    [PrebuildSetup(typeof(PlatformerBatch6PlanBuildSetup))]
    public sealed class PlatformerBatch6PlanCycleTests
    {
        private const string DefaultMenu = "Assets/Game/PlatformerBatch6/Prepared01/Integrated/Menu.unity";
        private const string Catalog = "Assets/Game/PlatformerBatch6/Prepared01/Integrated/Catalog.asset";
        private static readonly string[] Keys = { "crab", "enemy", "skull" };
        private static readonly int[] Counts = { 100, 75, 95 }, Hp = { 90, 110, 85 };
        private string phase, root, menu;
        private WarSandboxSceneSession session;
        private EditorBuildSettingsScene[] oldScenes;
        private float oldCapture;
        [Serializable] private sealed class Item { public string key, slot, sha256, templateId; public int count, hp; }
        [Serializable] private sealed class Receipt { public bool passed; public int processId; public string phase, utc; public Item[] items; }

        [UnitySetUp] public IEnumerator Setup()
        {
            phase = Environment.GetEnvironmentVariable("PLATFORMER6_PLAN_PHASE");
            Assert.That(phase, Is.EqualTo("seed").Or.EqualTo("reload"));
            root = Path.GetFullPath(Environment.GetEnvironmentVariable("PLATFORMER6_PLAN_ROOT") ?? "");
            Assert.That(root.Replace('\\','/'), Does.Contain("/Logs/AgentPlatformer6/plan-cycle-"));
            menu = Environment.GetEnvironmentVariable("PLATFORMER6_MENU_SCENE") ?? DefaultMenu;
            oldScenes = EditorBuildSettings.scenes;
            var catalog = AssetDatabase.LoadAssetAtPath<WarSandboxBattlefieldCatalog>(Catalog);
            Assert.That(catalog, Is.Not.Null);
            EditorBuildSettings.scenes = new[] { menu }.Concat(catalog.entries.Select(e => e.scenePath)).Distinct().Select(p => new EditorBuildSettingsScene(p, true)).ToArray();
            WarSandboxUnitStatStore.DefaultPathOverride = Path.Combine(root, "globals", "empty.json");
            oldCapture = Time.captureDeltaTime; Time.captureFramerate = 30;
            WarSandboxUGUI.ScreenSizeOverride = new Vector2(1280, 720);
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(menu, new LoadSceneParameters(LoadSceneMode.Single));
            session = WarSandboxSceneSession.Instance;
            Assert.That(session, Is.Not.Null); yield return null; yield return null;
            Assert.That(session.State, Is.EqualTo(WarSandboxEntryState.Menu), session.Error);
        }

        [UnityTearDown] public IEnumerator Cleanup()
        {
            if (session != null) Object.Destroy(session.gameObject);
            Time.captureDeltaTime = oldCapture; Time.timeScale = 1;
            WarSandboxUnitStatStore.DefaultPathOverride = null; WarSandboxUGUI.ScreenSizeOverride = null;
            var empty = SceneManager.CreateScene("PlatformerPlanCleanup" + Time.frameCount); SceneManager.SetActiveScene(empty);
            for (int i = SceneManager.sceneCount - 1; i >= 0; i--) { var s = SceneManager.GetSceneAt(i); if (s != empty && s.isLoaded) yield return SceneManager.UnloadSceneAsync(s); }
            if (oldScenes != null) EditorBuildSettings.scenes = oldScenes;
            yield return null;
        }

        [UnityTest, Timeout(1500000)] public IEnumerator MenuAndPlansSurviveAnotherEditorProcess()
        {
            string plans = Path.Combine(root, "plans"), receiptPath = Path.Combine(root, "seed-receipt.json");
            var previous = phase == "reload" ? JsonUtility.FromJson<Receipt>(File.ReadAllText(receiptPath)) : null;
            if (phase == "seed") Assert.That(Directory.Exists(plans), Is.False, "Seed data must be fresh.");
            else { Assert.That(previous.passed, Is.True); Assert.That(previous.processId, Is.Not.EqualTo(Process.GetCurrentProcess().Id)); }
            var items = new List<Item>();
            for (int index = 0; index < Keys.Length; index++)
            {
                string key = Keys[index], id = "troops6-" + key, slot = "platformer6-" + key;
                int card = Array.FindIndex(session.catalog.entries, e => e.id == id); Assert.That(card, Is.GreaterThanOrEqualTo(0));
                yield return Click("card-" + card); yield return Click("card-" + card + "-enter"); yield return WaitForLoad();
                Assert.That(session.State, Is.EqualTo(WarSandboxEntryState.Battle), session.Error);
                Assert.That(session.CurrentEntryId, Is.EqualTo(id));
                var controller = session.Controller; var manager = controller.manager;
                var deployment = controller.GetComponent<WarSandboxRuntimeDeployment>(); Assert.That(deployment, Is.Not.Null);
                deployment.PlanStore = new WarSandboxLocalPlanStore(plans);
                Assert.That(deployment.TryBeginEdit(false, out string error), Is.True, error);
                var original = deployment.Draft[0].template;
                Assert.That(session.catalog.TryGetTemplateId(original, out string templateId, out int revision), Is.True);
                Assert.That(templateId, Is.EqualTo("roster-platformer-" + key));
                int expectedCount = Counts[index] - 10, expectedHp = Mathf.RoundToInt(Hp[index] * 1.2f);
                string file = Path.Combine(plans, slot + ".json");
                if (phase == "seed")
                {
                    var edited = deployment.Draft[0]; edited.count = expectedCount;
                    Assert.That(deployment.Draft.Set(0, edited), Is.True);
                    Assert.That(deployment.SetStat(original, WarSandboxUnitStat.MaxHp, expectedHp), Is.True);
                    Assert.That(deployment.TrySavePlan(slot, "第六批 " + key + " 保存重载", false, out error), Is.True, error);
                }
                else
                {
                    var saved = previous.items.Single(i => i.key == key); Assert.That(Hash(file), Is.EqualTo(saved.sha256));
                    Assert.That(deployment.TryLoadPlan(slot, out error), Is.True, error);
                    Assert.That(deployment.Draft[0].template, Is.SameAs(original));
                    Assert.That(deployment.Draft[0].count, Is.EqualTo(expectedCount));
                    Assert.That(deployment.Draft.TryGetStat(original, WarSandboxUnitStat.MaxHp, out float restoredHp), Is.True);
                    Assert.That(restoredHp, Is.EqualTo(expectedHp));
                }
                Assert.That(deployment.PlanStore.TryLoad(slot, out var contract, out error), Is.True, error);
                Assert.That(contract.battlefieldId, Is.EqualTo(id));
                Assert.That(contract.entries[0].templateId, Is.EqualTo(templateId));
                Assert.That(contract.entries[0].count, Is.EqualTo(expectedCount));
                string frozenHash = Hash(file);
                Assert.That(deployment.TryApply(out error), Is.True, error); yield return null; yield return null;
                Assert.That(manager.UnitTypes.TotalAgentCount, Is.EqualTo(expectedCount + 80));
                Assert.That(manager.scenarioConfig.unitTypes[0].combatConfig.maxHp, Is.EqualTo(expectedHp));
                Assert.That(original.combatConfig.maxHp, Is.EqualTo(Hp[index]), "Authored template is read-only.");
                Assert.That(original.spawnConfig.unitCount, Is.EqualTo(Counts[index]));
                Assert.That(controller.StartDefaultBattle(), Is.True);
                float sim = 0; long initialHp = TotalHp(manager); bool captured = false;
                while (!controller.BattleResult.valid && sim < 180f)
                {
                    yield return null; sim += 1f / 30f;
                    if (phase == "seed" && !captured && sim > 6 && TotalHp(manager) < initialHp) { Shot(manager, Path.Combine(root, id + "-fight.png")); captured = true; }
                }
                Assert.That(controller.BattleResult.valid, Is.True, "Loaded deployment must reach a real result.");
                Assert.That(manager.IsBattleRunning, Is.False);
                Assert.That(Hash(file), Is.EqualTo(frozenHash), "Applying and fighting must not rewrite the saved plan.");
                items.Add(new Item { key = key, slot = slot, sha256 = frozenHash, templateId = templateId, count = expectedCount, hp = expectedHp });
                Assert.That(session.TryReturnToMenu(true, out error), Is.True, error); yield return WaitForLoad();
                Assert.That(Object.FindObjectsByType<MassEngineManager>(FindObjectsSortMode.None).Length, Is.EqualTo(0));
            }
            var receipt = new Receipt { passed = true, phase = phase, processId = Process.GetCurrentProcess().Id, utc = DateTime.UtcNow.ToString("O"), items = items.ToArray() };
            string target = phase == "seed" ? receiptPath : Path.Combine(root, "reload-receipt.json");
            using (var file = new FileStream(target, FileMode.CreateNew)) using (var writer = new StreamWriter(file)) writer.Write(JsonUtility.ToJson(receipt, true));
            Debug.Log("PLATFORMER6_PLAN_CYCLE_READY " + phase + " process=" + receipt.processId);
        }

        private IEnumerator Click(string name)
        {
            for (int i = 0; i < 15; i++) yield return null;
            var candidates = Object.FindObjectsByType<Button>(FindObjectsSortMode.None).Where(b => b.name == name && b.isActiveAndEnabled).ToArray();
            Assert.That(candidates.Length, Is.EqualTo(1), name + " available=" + string.Join(",", Object.FindObjectsByType<Button>(FindObjectsSortMode.None).Where(b => b.isActiveAndEnabled).Select(b => b.name)));
            var button = candidates[0];
            Assert.That(button.IsInteractable(), Is.True, name); button.onClick.Invoke(); yield return null;
        }
        private IEnumerator WaitForLoad()
        {
            float deadline = Time.realtimeSinceStartup + 120;
            while (session.IsLoading && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(session.IsLoading, Is.False, session.Error); yield return null; yield return null;
        }
        private static string Hash(string file) { using (var h = SHA256.Create()) return string.Concat(h.ComputeHash(File.ReadAllBytes(file)).Select(b => b.ToString("x2"))); }
        private static long TotalHp(MassEngineManager manager) { var hp = new int[manager.UnitTypes.TotalAgentCount]; manager.Buffers.combatBuffers.hpReadBuffer.GetData(hp); return hp.Sum(v => (long)v); }
        private static void Shot(MassEngineManager manager, string file)
        {
            int count = manager.UnitTypes.TotalAgentCount; var teams = new int[count]; var pos = new Vector2[count]; var hp = new int[count];
            manager.Buffers.combatBuffers.teamIdBuffer.GetData(teams); manager.Buffers.agentPositionReadBuffer.GetData(pos); manager.Buffers.combatBuffers.hpReadBuffer.GetData(hp);
            Vector2 center = Vector2.zero; int living = 0; for (int i = 0; i < count; i++) if (hp[i] > 0) { center += pos[i]; living++; } center /= Mathf.Max(1, living);
            var camera = Camera.allCameras[0]; var p = camera.transform.position; var r = camera.transform.rotation; var old = camera.targetTexture;
            var target = new RenderTexture(1280, 592, 24); var image = new Texture2D(1280, 592, TextureFormat.RGB24, false);
            try { camera.transform.position = new Vector3(center.x - 8, 14, center.y - 22); camera.transform.LookAt(new Vector3(center.x, .7f, center.y)); camera.targetTexture = target; camera.Render(); RenderTexture.active = target; image.ReadPixels(new Rect(0, 0, 1280, 592), 0, 0); image.Apply(); File.WriteAllBytes(file, image.EncodeToPNG()); }
            finally { camera.targetTexture = old; RenderTexture.active = null; camera.transform.SetPositionAndRotation(p, r); Object.Destroy(image); Object.Destroy(target); }
        }
    }
}
#endif
