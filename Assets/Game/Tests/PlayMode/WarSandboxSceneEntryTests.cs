#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace MassEngine.Game.Tests
{
    public sealed class WarSandboxSceneEntryTests
    {
        private const string Menu = "Assets/Game/Experiments/LegacyScenes/WarSandboxMenu.unity";
        private const string Battlefield = "Assets/Game/Experiments/LegacyScenes/WarSandbox.unity";
        private float previousCaptureDelta;
        private WarSandboxBattlefieldCatalog copy;
        private WarSandboxSceneSession session;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            previousCaptureDelta = Time.captureDeltaTime; Time.captureDeltaTime = 0.02f;
            SceneManager.sceneLoaded += DisableAutoStart;
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(Menu, new LoadSceneParameters(LoadSceneMode.Single));
            SceneManager.sceneLoaded -= DisableAutoStart;
            session = WarSandboxSceneSession.Instance;
            Assert.That(session, Is.Not.Null);
            copy = Object.Instantiate(session.catalog);
            session.catalog = copy;
            yield return null;
            Assert.That(session.State, Is.EqualTo(WarSandboxEntryState.Menu));
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            SceneManager.sceneLoaded -= DisableAutoStart;
            if (WarSandboxSceneSession.Instance != null) Object.Destroy(WarSandboxSceneSession.Instance.gameObject);
            if (copy != null) Object.Destroy(copy);
            Time.captureDeltaTime = previousCaptureDelta; Time.timeScale = 1;
            Scene old = SceneManager.GetActiveScene();
            Scene empty = SceneManager.CreateScene("Scene Entry Test Cleanup"); SceneManager.SetActiveScene(empty);
            if (old.path == Menu || old.path == Battlefield) yield return SceneManager.UnloadSceneAsync(old);
            yield return null;
        }

        [UnityTest]
        public IEnumerator InitialLauncherEntersItsDefaultBattlefieldInSetup()
        {
            Object.Destroy(session.gameObject);
            yield return null;
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(Menu, new LoadSceneParameters(LoadSceneMode.Single));
            session = WarSandboxSceneSession.Instance;
            yield return null;
            yield return WaitForLoad();
            Assert.That(session.State, Is.EqualTo(WarSandboxEntryState.Battle), session.Error);
            Assert.That(session.CurrentEntryId, Is.EqualTo(session.catalog.defaultEntryId));
            Assert.That(session.Controller.Phase, Is.EqualTo(WarSandboxBattlePhase.Setup));
            Assert.That(session.Controller.manager.IsBattleRunning, Is.False);
        }

        [UnityTest]
        public IEnumerator SelectionReturnAndReentryReleaseGpuAndResetSessionState()
        {
            foreach (string id in new[] { "open-battle", "walled-point", "open-battle" })
            {
                Assert.That(session.TryEnterBattlefield(id, false, out string error), Is.True, error);
                Assert.That(session.IsLoading, Is.True);
                Assert.That(session.TryEnterBattlefield("walled-point", false, out _), Is.False);
                yield return WaitForLoad();
                Assert.That(session.State, Is.EqualTo(WarSandboxEntryState.Battle), session.Error);
                var controller = session.Controller; var manager = controller.manager;
                Assert.That(Object.FindObjectsByType<MassEngineManager>(FindObjectsSortMode.None).Length, Is.EqualTo(1));
                Assert.That(controller.Phase, Is.EqualTo(WarSandboxBattlePhase.Setup));
                Assert.That(manager.IsBattleRunning, Is.False);
                Assert.That(controller.gameMode, Is.EqualTo(id == "walled-point" ? WarSandboxGameMode.ControlPoint : WarSandboxGameMode.Annihilation));
                Assert.That(controller.SimulationSpeed, Is.EqualTo(1));
                Assert.That(controller.selectedTeam, Is.Zero);
                Assert.That(controller.StartDefaultBattle(), Is.True);
                controller.SetSimulationSpeed(4); controller.selectedTeam = 2;
                for (int i = 0; i < 4; i++) yield return null;
                Assert.That(session.TryReturnToMenu(false, out _), Is.False);
                Assert.That(session.TryEnterBattlefield(id, false, out _), Is.False);
                session.BeginConfirmation();
                Assert.That(manager.IsBattleRunning, Is.False);
                Assert.That(controller.StartDefaultBattle(), Is.False);
                controller.StartOrResumeBattle(); Assert.That(manager.IsBattleRunning, Is.False);
                session.CancelConfirmation(); Assert.That(manager.IsBattleRunning, Is.True);
                var oldBuffers = manager.Buffers;
                Assert.That(session.TryReturnToMenu(true, out error), Is.True, error);
                yield return WaitForLoad();
                Assert.That(session.State, Is.EqualTo(WarSandboxEntryState.Menu), session.Error);
                Assert.That(controller == null, Is.True);
                Assert.That(oldBuffers.IsAllocated, Is.False);
                Assert.That(session.Controller, Is.Null);
                Assert.That(session.CurrentEntryId, Is.Null);
                Assert.That(Object.FindObjectsByType<MassEngineManager>(FindObjectsSortMode.None), Is.Empty);
                Assert.That(Time.timeScale, Is.EqualTo(1));
                Assert.That(Resources.FindObjectsOfTypeAll<WarSandboxBattlefieldConfig>().Count(config => config.name.EndsWith("(Runtime)")), Is.Zero);
            }
        }

        [UnityTest]
        public IEnumerator InvalidRequestsStayInMenuAndPostLoadFailureCanReturnAndRetry()
        {
            copy.entries = new[] { new WarSandboxBattlefieldEntry
            { id = "bad", displayName = "Bad", scenePath = "Assets/Missing.unity", rules = copy.entries[0].rules } };
            copy.defaultEntryId = "bad";
            Assert.That(session.TryEnterBattlefield("bad", false, out var error), Is.False);
            Assert.That(error, Does.Contain("build"));
            Assert.That(session.State, Is.EqualTo(WarSandboxEntryState.Menu));
            Assert.That(SceneManager.GetActiveScene().path, Is.EqualTo(Menu));
            copy.entries[0].scenePath = Menu; // Valid build scene but no simulation manager.
            Assert.That(session.TryEnterBattlefield("bad", false, out error), Is.True, error);
            yield return WaitForLoad();
            Assert.That(session.State, Is.EqualTo(WarSandboxEntryState.Failed));
            Assert.That(session.InputBlocked, Is.True);
            Assert.That(session.Error, Does.Contain("no MassEngineManager"));
            Assert.That(session.TryReturnToMenu(true, out error), Is.True, error);
            yield return WaitForLoad();
            Assert.That(session.State, Is.EqualTo(WarSandboxEntryState.Menu));
            copy.entries[0].scenePath = Battlefield;
            Assert.That(session.TryEnterBattlefield("bad", false, out error), Is.True, error);
            yield return WaitForLoad();
            Assert.That(session.State, Is.EqualTo(WarSandboxEntryState.Battle), session.Error);
        }

        [UnityTest]
        public IEnumerator RuleSnapshotSurvivesSelectionEditsAndDirectLoadHasNoPreviousDefinition()
        {
            var source = copy.entries[1].rules;
            string original = EditorJsonUtility.ToJson(source);
            Assert.That(session.TryEnterBattlefield("walled-point", false, out var error), Is.True, error);
            copy.entries[1] = new WarSandboxBattlefieldEntry { id = "changed" };
            yield return WaitForLoad();
            Assert.That(session.State, Is.EqualTo(WarSandboxEntryState.Battle), session.Error);
            Assert.That(session.CurrentEntryId, Is.EqualTo("walled-point"));
            session.Controller.SetGameMode(WarSandboxGameMode.Annihilation);
            session.Controller.ResetBattle();
            Assert.That(session.Controller.gameMode, Is.EqualTo(WarSandboxGameMode.ControlPoint));
            Assert.That(EditorJsonUtility.ToJson(source), Is.EqualTo(original));
            yield return SceneManager.LoadSceneAsync(Battlefield, LoadSceneMode.Single);
            yield return null;
            Assert.That(WarSandboxSceneSession.Instance, Is.Null);
            var controller = Object.FindFirstObjectByType<WarSandboxBattleController>();
            Assert.That(controller, Is.Not.Null);
            Assert.That(controller.battlefieldConfig, Is.Null);
            Assert.That(controller.gameMode, Is.EqualTo(WarSandboxGameMode.Annihilation));
            Assert.That(controller.StartDefaultBattle(), Is.True);
        }

        [UnityTest]
        public IEnumerator RuntimeDeploymentRebuildsGpuAndRestartsWithoutTouchingTemplates()
        {
            Assert.That(session.TryEnterBattlefield("open-battle", false, out var error), Is.True, error);
            yield return WaitForLoad();
            var controller = session.Controller; var manager = controller.manager;
            var source = manager.scenarioConfig;
            string sourceJson = JsonUtility.ToJson(source);
            var unitJson = source.unitTypes.Select(unit => JsonUtility.ToJson(unit)).ToArray();
            var spawnJson = source.unitTypes.Select(unit => JsonUtility.ToJson(unit.spawnConfig)).ToArray();
            var editor = manager.GetComponent<WarSandboxRuntimeDeployment>();
            Assert.That(editor, Is.Not.Null);
            var originalBuffers = manager.Buffers;
            Assert.That(editor.TryBeginEdit(false, out error), Is.True, error);
            Assert.That(controller.StartDefaultBattle(), Is.False);
            Assert.That(session.TryReturnToMenu(false, out _), Is.False);
            session.BeginConfirmation(); session.CancelConfirmation();
            Assert.That(editor.IsEditing, Is.True); Assert.That(manager.enableGpuDispatch, Is.False);
            for (int i = 0; i < editor.Draft.Count; i++)
            {
                var entry = editor.Draft[i]; entry.count = 64; entry.manualSize = Vector3.zero;
                editor.Draft.Set(i, entry);
            }
            Assert.That(editor.TryApply(out error), Is.True, error);
            yield return null;
            Assert.That(originalBuffers.IsAllocated, Is.False);
            Assert.That(manager.UnitTypes.TotalAgentCount, Is.EqualTo(384));
            Assert.That(manager.scenarioConfig, Is.Not.SameAs(source));
            var first = manager.scenarioConfig;
            Assert.That(first.unitTypes[0].spawnConfig, Is.Not.SameAs(source.unitTypes[0].spawnConfig));
            Assert.That(controller.StartDefaultBattle(), Is.True);
            for (int i = 0; i < 8; i++) yield return null;
            Assert.That(editor.TryBeginEdit(false, out _), Is.False);
            controller.ResetBattle();
            Assert.That(manager.UnitTypes.TotalAgentCount, Is.EqualTo(384));
            Assert.That(controller.GetAliveUnitCount(2), Is.EqualTo(128));
            Assert.That(controller.GetMoveRoutePointCount(0), Is.Zero);
            Assert.That(editor.TryBeginEdit(false, out error), Is.True, error);
            var e = editor.Draft[0]; e.count = 32; editor.Draft.Set(0, e);
            Assert.That(editor.TryApply(out error), Is.True, error);
            yield return null;
            Assert.That(first == null, Is.True);
            Assert.That(manager.UnitTypes.TotalAgentCount, Is.EqualTo(352));
            Assert.That(controller.StartDefaultBattle(), Is.True);
            yield return null;
            Assert.That(JsonUtility.ToJson(source), Is.EqualTo(sourceJson));
            for (int i = 0; i < source.unitTypes.Length; i++)
            {
                Assert.That(JsonUtility.ToJson(source.unitTypes[i]), Is.EqualTo(unitJson[i]));
                Assert.That(JsonUtility.ToJson(source.unitTypes[i].spawnConfig), Is.EqualTo(spawnJson[i]));
            }
            var last = manager.scenarioConfig; var buffers = manager.Buffers;
            Assert.That(session.TryReturnToMenu(true, out error), Is.True, error);
            yield return WaitForLoad();
            Assert.That(buffers.IsAllocated, Is.False); Assert.That(last == null, Is.True);
            Assert.That(Resources.FindObjectsOfTypeAll<ScenarioConfig>().Count(config => config.name == "War Sandbox Deployment (Runtime)"), Is.Zero);
        }

        [UnityTest]
        public IEnumerator InvalidRuntimeDeploymentDoesNotReplaceBuffersAndCancelRestoresDispatch()
        {
            Assert.That(session.TryEnterBattlefield("walled-point", false, out var error), Is.True, error);
            yield return WaitForLoad();
            var controller = session.Controller; var manager = controller.manager;
            var editor = manager.GetComponent<WarSandboxRuntimeDeployment>();
            var scenario = manager.scenarioConfig; var buffers = manager.Buffers;
            Assert.That(editor.TryBeginEdit(false, out error), Is.True, error);
            var entry = editor.Draft[0]; entry.center = new Vector3(0, 0, -90); editor.Draft.Set(0, entry);
            Assert.That(editor.TryApply(out error), Is.False); Assert.That(error, Is.Not.Empty);
            Assert.That(manager.Buffers, Is.SameAs(buffers)); Assert.That(buffers.IsAllocated, Is.True);
            Assert.That(manager.scenarioConfig, Is.SameAs(scenario)); Assert.That(manager.IsBattleRunning, Is.False);
            Assert.That(editor.CancelEditing(), Is.True); yield return null;
            Assert.That(manager.enableGpuDispatch, Is.True);
            controller.SetStaticObstaclesEnabled(false);
            Assert.That(editor.TryBeginEdit(false, out error), Is.True, error);
            entry = editor.Draft[0]; entry.count = 100; editor.Draft.Set(0, entry);
            Assert.That(editor.TryApply(out error), Is.True, error);
            Assert.That(controller.staticObstaclesEnabled, Is.False);
            controller.ResetBattle(); Assert.That(controller.staticObstaclesEnabled, Is.False);
            Assert.That(controller.StartDefaultBattle(), Is.True);
        }

        [UnityTest]
        public IEnumerator LocalPlansReloadRulesAndDeploymentWithoutMutatingSourcesOrFailedDrafts()
        {
            string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "WarSandboxGpuPlans-" + System.Guid.NewGuid().ToString("N"));
            try
            {
                Assert.That(session.TryEnterBattlefield("open-battle", false, out var error), Is.True, error);
                yield return WaitForLoad();
                var controller = session.Controller; var manager = controller.manager;
                var source = manager.scenarioConfig;
                string sourceJson = JsonUtility.ToJson(source);
                var originals = source.unitTypes.Select(unit => JsonUtility.ToJson(unit) + JsonUtility.ToJson(unit.spawnConfig)).ToArray();
                var editor = manager.GetComponent<WarSandboxRuntimeDeployment>();
                editor.PlanStore = new WarSandboxLocalPlanStore(directory);
                Assert.That(editor.TryBeginEdit(false, out error), Is.True, error);
                for (int i = 0; i < editor.Draft.Count; i++)
                {
                    var unit = editor.Draft[i]; unit.count = 64; unit.manualSize = Vector3.zero; editor.Draft.Set(i, unit);
                }
                Assert.That(editor.TrySavePlan("A", "Alpha", false, out error), Is.True, error);
                var values = editor.Draft.Snapshot(); values[0].count = 32;
                var rules = editor.Draft.Rules; rules.gameMode = WarSandboxGameMode.ControlPoint; rules.controlPointCaptureSeconds = 7;
                editor.Draft.Replace(values, rules);
                Assert.That(editor.TrySavePlan("B", "Bravo", false, out error), Is.True, error);
                var buffers = manager.Buffers; var draft = editor.Draft;
                Assert.That(editor.PlanStore.TryLoad("B", out var broken, out error), Is.True, error);
                broken.planId = "Broken"; broken.terrainId = "missing-terrain";
                Assert.That(editor.PlanStore.TrySave(broken, false, out error), Is.True, error);
                Assert.That(editor.TryLoadPlan("Broken", out error), Is.False);
                Assert.That(editor.Draft, Is.SameAs(draft)); Assert.That(editor.Draft.Snapshot(), Is.EqualTo(values));
                Assert.That(manager.Buffers, Is.SameAs(buffers)); Assert.That(buffers.IsAllocated, Is.True);
                Assert.That(editor.TryLoadPlan("A", out error), Is.True, error);
                Assert.That(editor.Draft[0].count, Is.EqualTo(64));
                Assert.That(editor.Draft.Undo(), Is.True); Assert.That(editor.Draft[0].count, Is.EqualTo(32));
                Assert.That(editor.Draft.Rules.gameMode, Is.EqualTo(WarSandboxGameMode.ControlPoint));
                Assert.That(editor.CancelEditing(), Is.True);
                Assert.That(controller.gameMode, Is.EqualTo(WarSandboxGameMode.Annihilation));
                Assert.That(manager.scenarioConfig, Is.SameAs(source));
                Assert.That(session.TryReturnToMenu(false, out error), Is.True, error);
                yield return WaitForLoad(); Assert.That(buffers.IsAllocated, Is.False);
                Assert.That(session.TryEnterBattlefield("open-battle", false, out error), Is.True, error);
                yield return WaitForLoad();
                controller = session.Controller; manager = controller.manager;
                editor = manager.GetComponent<WarSandboxRuntimeDeployment>(); editor.PlanStore = new WarSandboxLocalPlanStore(directory);
                Assert.That(editor.TryBeginEdit(false, out error), Is.True, error);
                Assert.That(editor.TryLoadPlan("B", out error), Is.True, error);
                Assert.That(editor.TryApply(out error), Is.True, error); yield return null;
                Assert.That(manager.UnitTypes.TotalAgentCount, Is.EqualTo(352));
                Assert.That(controller.gameMode, Is.EqualTo(WarSandboxGameMode.ControlPoint));
                controller.ResetBattle(); Assert.That(controller.controlPointCaptureSeconds, Is.EqualTo(7));
                Assert.That(controller.StartDefaultBattle(), Is.True); yield return null;
                Assert.That(editor.TryBeginEdit(true, out error), Is.True, error);
                var previous = manager.scenarioConfig;
                Assert.That(editor.TryLoadPlan("A", out error), Is.True, error);
                Assert.That(editor.TryApply(out error), Is.True, error); yield return null;
                Assert.That(previous == null, Is.True); Assert.That(manager.UnitTypes.TotalAgentCount, Is.EqualTo(384));
                Assert.That(controller.gameMode, Is.EqualTo(WarSandboxGameMode.Annihilation));
                Assert.That(JsonUtility.ToJson(source), Is.EqualTo(sourceJson));
                for (int i = 0; i < source.unitTypes.Length; i++)
                    Assert.That(JsonUtility.ToJson(source.unitTypes[i]) + JsonUtility.ToJson(source.unitTypes[i].spawnConfig), Is.EqualTo(originals[i]));
            }
            finally { if (System.IO.Directory.Exists(directory)) System.IO.Directory.Delete(directory, true); }
        }

        private IEnumerator WaitForLoad()
        {
            float deadline = Time.realtimeSinceStartup + 30f;
            while (session.IsLoading && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(session.IsLoading, Is.False, "Scene transition timed out. state=" + session.State +
                " progress=" + session.LoadingProgress + " active=" + SceneManager.GetActiveScene().path +
                " alive=" + (session != null) + " enabled=" + session.isActiveAndEnabled + " error=" + session.Error);
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
