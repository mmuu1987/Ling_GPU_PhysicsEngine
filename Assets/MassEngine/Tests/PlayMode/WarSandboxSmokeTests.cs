#if UNITY_EDITOR
using System.Collections;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace MassEngine.Tests
{
    /// <summary>
    /// End-to-end smoke test for the shipping scene: load, fight, pause, resume, reset.
    /// It asserts the projectile render contract at full scale (the kernel tests run on
    /// eight agents) and, because the test framework fails on any logged error, it also
    /// certifies the whole loop stays exception-free.
    /// </summary>
    public sealed class WarSandboxSmokeTests
    {
        private const string ScenePath = "Assets/Game/Scenes/WarSandbox.unity";
        private const int InitializeFrameBudget = 900;
        private const int BattleFrameBudget = 900;
        private const float FrameDt = 0.02f;

        private float previousCaptureDeltaTime;
        // The draw args can report instances even when the dispatcher refuses to draw
        // (missing material or mesh), which is exactly the silent failure the warning
        // exists for. Counting it is what proves the shipping scene is wired up.
        private int skipWarnings;
        private Application.LogCallback countSkips;

        [SetUp]
        public void SetUp()
        {
            // The engine integrates with Time.deltaTime, and a batchmode frame costs about
            // a millisecond of wall clock. Without a fixed capture step the whole frame
            // budget would advance barely a second of battle, the two armies would never
            // close the 50m between their front lines, and nothing would ever be fired.
            previousCaptureDeltaTime = Time.captureDeltaTime;
            Time.captureDeltaTime = FrameDt;

            skipWarnings = 0;
            countSkips = (condition, stackTrace, type) =>
            {
                if (type == LogType.Warning && condition.Contains("projectile trails skipped"))
                    skipWarnings++;
            };
            Application.logMessageReceived += countSkips;
        }

        [TearDown]
        public void TearDown()
        {
            Time.captureDeltaTime = previousCaptureDeltaTime;
            Application.logMessageReceived -= countSkips;
        }

        [UnityTest]
        public IEnumerator WarSandboxRunsFullBattleLifecycleWithoutErrors()
        {
            if (!SystemInfo.supportsComputeShaders)
                Assert.Ignore("compute shaders unavailable on this device");

            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(
                ScenePath, new LoadSceneParameters(LoadSceneMode.Single));

            MassEngineManager manager = null;
            for (int frame = 0; frame < InitializeFrameBudget; frame++)
            {
                manager = Object.FindFirstObjectByType<MassEngineManager>();
                if (manager != null && manager.Buffers != null && manager.Buffers.IsAllocated)
                    break;
                yield return null;
            }

            Assert.IsNotNull(manager, "WarSandbox contains no MassEngineManager");
            Assert.IsNotNull(manager.Buffers, "MassEngineManager never allocated its GPU buffers");
            Assert.IsTrue(manager.Buffers.IsAllocated, "MassEngineManager buffers are not allocated");
            Assert.Greater(manager.Buffers.MaxProjectiles, 0,
                "the scene reserves no projectile pool, so the render path could never be exercised");

            // --- fight: projectiles must actually reach the indirect draw args ---
            manager.StartBattle();

            uint peak = 0;
            for (int frame = 0; frame < BattleFrameBudget && peak == 0; frame++)
            {
                yield return null;
                uint count = ReadInstanceCount(manager);
                if (count > peak)
                    peak = count;
            }

            Assert.Greater(peak, 0u,
                "no projectile ever became renderable in WarSandbox; ranged units either never fired or the active list never filled");

            // --- pause: the frozen shots stay on screen instead of blinking out ---
            manager.StopBattle();

            // StopBattle freezes the simulation kernels, so no projectile can move, expire or
            // hit from here on. Launch requests already in the async readback pipeline do
            // still land as new projectiles for a frame or two; nothing produces more of
            // them, so let that settle before sampling the set that must stay frozen.
            for (int frame = 0; frame < 8; frame++)
                yield return null;

            uint paused = ReadInstanceCount(manager);
            Assert.Greater(paused, 0u,
                "the projectiles in flight disappeared when the battle was paused instead of freezing on screen");

            for (int frame = 0; frame < 5; frame++)
            {
                yield return null;
                Assert.AreEqual(paused, ReadInstanceCount(manager),
                    "paused frame " + frame + ": the renderable projectile count drifted while the battle was paused");
            }

            // --- resume: the pipeline picks up again from the same pool ---
            manager.StartBattle();
            for (int frame = 0; frame < 30; frame++)
                yield return null;

            // --- reset: no trail may survive a scenario reset ---
            manager.ResetScenario();
            for (int frame = 0; frame < InitializeFrameBudget; frame++)
            {
                if (manager.Buffers != null && manager.Buffers.IsAllocated)
                    break;
                yield return null;
            }
            yield return null;

            Assert.AreEqual(0u, ReadInstanceCount(manager),
                "projectile trails survived ResetScenario");

            // --- and the fight can start over ---
            manager.StartBattle();
            uint afterReset = 0;
            for (int frame = 0; frame < BattleFrameBudget && afterReset == 0; frame++)
            {
                yield return null;
                afterReset = ReadInstanceCount(manager);
            }

            Assert.Greater(afterReset, 0u, "WarSandbox could not produce projectiles again after a reset");

            Assert.AreEqual(0, skipWarnings,
                "the dispatcher skipped drawing in the shipping scene, so the tracers reported here would never reach the screen");
        }

        private static uint ReadInstanceCount(MassEngineManager manager)
        {
            ComputeBuffer args = manager.Buffers.projectileDrawArgsBuffer;
            if (args == null)
                return 0;

            uint[] values = new uint[5];
            args.GetData(values);
            return values[1];
        }
    }
}
#endif
