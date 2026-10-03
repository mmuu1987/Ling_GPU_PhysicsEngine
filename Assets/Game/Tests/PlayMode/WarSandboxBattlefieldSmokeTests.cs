#if UNITY_EDITOR
using System;
using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace MassEngine.Game.Tests
{
    public sealed class WarSandboxBattlefieldSmokeTests
    {
        private const string RulesA = "Assets/Game/Settings/BattlefieldRules_A_Annihilation.asset";
        private const string RulesB = "Assets/Game/Settings/BattlefieldRules_B_ControlPoint.asset";
        private float previousCaptureDelta;

        [SetUp]
        public void SetUp() { previousCaptureDelta = Time.captureDeltaTime; Time.captureDeltaTime = 0.02f; }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.captureDeltaTime = previousCaptureDelta; Time.timeScale = 1;
            Scene empty = SceneManager.CreateScene("Battlefield Smoke Cleanup");
            SceneManager.SetActiveScene(empty);
            for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene != empty && scene.path == "Assets/Game/Scenes/WarSandbox.unity")
                    yield return SceneManager.UnloadSceneAsync(scene);
            }
        }

        [UnityTest]
        public IEnumerator ShippingSceneSwitchesRulesAndRestartsWithoutAssetWrites()
        {
            if (!SystemInfo.supportsComputeShaders) Assert.Ignore("Compute shaders unavailable.");
            var a = AssetDatabase.LoadAssetAtPath<WarSandboxBattlefieldConfig>(RulesA);
            var b = AssetDatabase.LoadAssetAtPath<WarSandboxBattlefieldConfig>(RulesB);
            Assert.That(a, Is.Not.Null); Assert.That(b, Is.Not.Null);
            string originalA = EditorJsonUtility.ToJson(a), originalB = EditorJsonUtility.ToJson(b);
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Game/Scenes/WarSandbox.unity", new LoadSceneParameters(LoadSceneMode.Single));
            var manager = Object.FindFirstObjectByType<MassEngineManager>(); Assert.That(manager, Is.Not.Null);
            var controller = manager.GetComponent<WarSandboxBattleController>();
            if (controller == null) controller = manager.gameObject.AddComponent<WarSandboxBattleController>();
            controller.manager = manager;
            yield return null;
            for (int i = 0; i < 900 && (manager.Buffers == null || !manager.Buffers.IsAllocated); i++) yield return null;
            Assert.That(manager.Buffers != null && manager.Buffers.IsAllocated, Is.True);

            foreach (var config in new[] { a, b, a, b })
            {
                controller.ResetBattle();
                Assert.That(controller.TryApplyBattlefieldConfig(config, out string error), Is.True, error);
                yield return null;
                Assert.That(controller.Phase, Is.EqualTo(WarSandboxBattlePhase.Setup));
                Assert.That(manager.IsBattleRunning, Is.False);
                Assert.That(controller.gameMode, Is.EqualTo(config.rules.gameMode));
                Assert.That(controller.controlPointRadius, Is.EqualTo(config.rules.controlPointRadius));
                Assert.That(controller.controlPointCaptureSeconds, Is.EqualTo(config.rules.controlPointCaptureSeconds));
                Assert.That(manager.StaticObstacleCount, Is.EqualTo(config.rules.staticObstaclesEnabled ? config.rules.staticObstacles.Length : 0));
                for (int i = 0; i < manager.StaticObstacleCount; i++)
                {
                    Assert.That(manager.TryGetStaticObstacle(i, out var rectangle), Is.True);
                    var visual = manager.transform.Find("Static Obstacle " + (i + 1));
                    Assert.That(visual, Is.Not.Null);
                    Assert.That(visual.position, Is.EqualTo(new Vector3(rectangle.center.x, 2, rectangle.center.y)));
                    Assert.That(visual.localScale, Is.EqualTo(new Vector3(rectangle.size.x, 4, rectangle.size.y)));
                }
                if (manager.StaticObstacleCount == 0) Assert.That(manager.transform.Find("Static Obstacle 1"), Is.Null);
                Assert.That(controller.StartDefaultBattle(), Is.True);
                for (int i = 0; i < 30; i++) yield return null;
                Assert.That(controller.Phase, Is.EqualTo(WarSandboxBattlePhase.Running));
                Assert.That(controller.GetArmy(0).currentOrder.type, Is.EqualTo(config.rules.gameMode == WarSandboxGameMode.ControlPoint ? ArmyOrderType.Move : ArmyOrderType.Attack));
                controller.PauseBattle(); Assert.That(manager.IsBattleRunning, Is.False);
                controller.ResetBattle();
                controller.SetGameMode(config.rules.gameMode == WarSandboxGameMode.ControlPoint ? WarSandboxGameMode.Annihilation : WarSandboxGameMode.ControlPoint);
                controller.SetStaticObstaclesEnabled(!config.rules.staticObstaclesEnabled);
                controller.SetSimulationSpeed(2);
                yield return null;
                Assert.That(controller.gameMode, Is.Not.EqualTo(config.rules.gameMode), "Update must not replace Setup choices.");
                controller.ResetBattle();
                Assert.That(controller.gameMode, Is.EqualTo(config.rules.gameMode));
                Assert.That(controller.staticObstaclesEnabled, Is.EqualTo(config.rules.staticObstaclesEnabled));
                Assert.That(controller.SimulationSpeed, Is.EqualTo(1));
                Assert.That(controller.GetMoveRoutePointCount(0), Is.Zero);
                Assert.That(controller.ControlPointCaptureProgress, Is.Zero);
                Assert.That(EditorJsonUtility.ToJson(a), Is.EqualTo(originalA));
                Assert.That(EditorJsonUtility.ToJson(b), Is.EqualTo(originalB));
            }

            // The editor assembly remains out of Game's runtime dependency graph.
            Type editor = Type.GetType("MassEngine.Game.Editor.WarSandboxBattlefieldEditor, Game.Editor", true);
            string before = EditorJsonUtility.ToJson(controller);
            foreach (string operation in new[] { "TryApply", "TryCapture" })
            {
                object[] args = { controller, a, null };
                Assert.That((bool)editor.GetMethod(operation).Invoke(null, args), Is.False);
                Assert.That(args[2] as string, Does.Contain("Play Mode"));
            }
            object[] ensureArgs = { manager, null, null };
            Assert.That((bool)editor.GetMethod("TryEnsureController").Invoke(null, ensureArgs), Is.False);
            Assert.That(EditorJsonUtility.ToJson(controller), Is.EqualTo(before));
            Assert.That(EditorJsonUtility.ToJson(a), Is.EqualTo(originalA));
            Assert.That(EditorJsonUtility.ToJson(b), Is.EqualTo(originalB));
        }

        [UnityTest]
        public IEnumerator AssignedRulesApplyBeforeStartAndInvalidRulesStayPaused()
        {
            var root = new GameObject("Assigned Battlefield Startup"); root.SetActive(false);
            var manager = root.AddComponent<MassEngineManager>(); manager.enabled = false;
            var controller = root.AddComponent<WarSandboxBattleController>(); controller.manager = manager;
            var rules = ScriptableObject.CreateInstance<WarSandboxBattlefieldConfig>();
            rules.rules.gameMode = WarSandboxGameMode.ControlPoint; rules.rules.controlPointRadius = 42;
            controller.battlefieldConfig = rules;
            try
            {
                root.SetActive(true); yield return null;
                Assert.That(controller.gameMode, Is.EqualTo(WarSandboxGameMode.ControlPoint));
                Assert.That(controller.controlPointRadius, Is.EqualTo(42));
                Assert.That(controller.Phase, Is.EqualTo(WarSandboxBattlePhase.Setup));
                Assert.That(manager.IsBattleRunning, Is.False);
                controller.SetGameMode(WarSandboxGameMode.Annihilation);
                yield return null;
                Assert.That(controller.gameMode, Is.EqualTo(WarSandboxGameMode.Annihilation));
                var invalid = ScriptableObject.CreateInstance<WarSandboxBattlefieldConfig>();
                try
                {
                    invalid.rules.controlPointRadius = float.NaN; controller.battlefieldConfig = invalid;
                    Assert.That(controller.StartDefaultBattle(), Is.False);
                    Assert.That(manager.IsBattleRunning, Is.False);
                    Assert.That(controller.BattlefieldRuleError, Does.Contain("controlPointRadius"));
                }
                finally { Object.Destroy(invalid); }
            }
            finally { Object.Destroy(root); Object.Destroy(rules); }
            yield return null;
        }
    }
}
#endif
