#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace MassEngine.Game
{
    /// <summary>Opt-in M5.3 player A/B probe, injected only into a temporary validation build.</summary>
    public sealed class WarSandboxVatPerformance : MonoBehaviour
    {
        public VATProfile referenceMale;
        public VATProfile rebakedMale;
        private static WarSandboxVatPerformance instance;
        private readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
        private readonly List<string> errors = new List<string>();
        private readonly List<Sample> samples = new List<Sample>();
        private WarSandboxSceneSession session;
        private string output;
        private string stage = "startup";

        [Serializable]
        private sealed class Sample
        {
            public string variant, phase;
            public int round, units, frames, gpuFrames;
            public float meanMs, p50Ms, p95Ms, p99Ms, gpuMeanMs, seconds;
            public int[] alive;
        }

        [Serializable]
        private sealed class Report
        {
            public bool passed;
            public string stage, error, unity, cpu, gpu, api, os;
            public int gpuMemoryMB, width, height, vSync, quality;
            public bool developmentBuild;
            public string referenceProfile, rebakedProfile;
            public Vector3 cameraPosition, cameraTarget;
            public string method;
            public Sample[] samples;
        }

        private static readonly Vector3 CameraPosition = new Vector3(0, 260, -220);
        private static readonly Vector3 CameraTarget = new Vector3(0, 0, 40);

        private void Awake()
        {
            if (instance != null || Array.IndexOf(Environment.GetCommandLineArgs(), "--vat-performance") < 0)
            { Destroy(gameObject); return; }
            instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            if (instance != this) return;
            output = Argument("--vat-performance-output=") ?? Path.Combine(Application.persistentDataPath, "VatPerformance");
            Directory.CreateDirectory(output);
            Application.runInBackground = true;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            Application.logMessageReceived += TrackError;
            StartCoroutine(Guarded(Run()));
        }

        private IEnumerator Run()
        {
            Require(referenceMale != null && rebakedMale != null && referenceMale != rebakedMale, "Missing distinct A/B profiles.");
            float deadline = Time.realtimeSinceStartup + 30f;
            while ((!SplashScreen.isFinished || WarSandboxSceneSession.Instance == null) && Time.realtimeSinceStartup < deadline)
                yield return null;
            Require(SplashScreen.isFinished, "Splash screen timed out.");
            session = WarSandboxSceneSession.Instance;
            Require(session != null && session.State == WarSandboxEntryState.Menu, "Probe must start on its temporary menu.");
            Screen.SetResolution(1024, 768, FullScreenMode.Windowed);
            yield return new WaitForSecondsRealtime(.5f);
            Screen.SetResolution(1280, 720, FullScreenMode.Windowed);
            yield return new WaitForSecondsRealtime(1f);
            Require(Screen.width == 1280 && Screen.height == 720, "Player resolution did not settle at 1280x720.");

            // ABBA counterbalances warm-cache / thermal drift. Each round loads a fresh full-strength scenario.
            bool[] variants = { false, true, true, false };
            for (int round = 0; round < variants.Length; round++)
            {
                bool rebaked = variants[round];
                string variant = rebaked ? "rebaked" : "reference";
                stage = round + "-" + variant + "-load";
                Require(session.TryEnterBattlefield("open-battle", false, out string error), error);
                yield return WaitForLoad();
                Require(session.State == WarSandboxEntryState.Battle, session.Error);
                var controller = session.Controller;
                var manager = controller.manager;
                ScenarioConfig source = manager.scenarioConfig;
                var snapshots = CaptureSources(source);
                var clone = Own(Instantiate(source));
                clone.unitTypes = new UnitTypeConfig[source.unitTypes.Length];
                int replaced = 0;
                for (int i = 0; i < clone.unitTypes.Length; i++)
                {
                    UnitTypeConfig unit = Own(Instantiate(source.unitTypes[i]));
                    clone.unitTypes[i] = unit;
                    if (unit.renderConfig != null && unit.renderConfig.vatProfile == referenceMale)
                    {
                        var render = Own(Instantiate(unit.renderConfig));
                        unit.renderConfig = render;
                        render.vatProfile = rebaked ? rebakedMale : referenceMale;
                        // The runtime, not the probe, owns mesh/VAT pairing and LOD fallbacks.
                        render.nearMesh = render.midMesh = render.farMesh = null;
                        replaced++;
                    }
                }
                Require(replaced == 2, "Expected exactly two Male attacker formations.");
                manager.PauseBattle();
                manager.scenarioConfig = clone;
                controller.ResetBattle();
                Require(manager.UnitTypes.TotalAgentCount == 110000 && manager.Buffers.IsAllocated,
                    "The shipped 110k scenario was not restored.");
                Require(controller.Phase == WarSandboxBattlePhase.Setup && !manager.IsBattleRunning, "Setup unexpectedly started.");
                var camera = manager.cullingCamera;
                Require(camera != null, "Missing culling/render camera.");
                foreach (var control in FindObjectsByType<MyCameraManager>(FindObjectsSortMode.None)) control.LockInput();
                camera.transform.position = CameraPosition;
                camera.transform.LookAt(CameraTarget);
                if (manager.lodCenter != null && manager.lodCenter != camera.transform)
                    manager.lodCenter.position = CameraPosition;
                yield return Warmup(3f);
                yield return Measure(controller, round, variant, "setup", 8f);
                yield return Screenshot(round + "-" + variant + "-setup.png");

                Require(controller.StartDefaultBattle(), "Default battle orders rejected.");
                // Same simulation-time window per round; no HP/count/commands are changed.
                yield return Warmup(20f);
                yield return Measure(controller, round, variant, "battle-20s", 12f);
                Require(manager.IsBattleRunning && !controller.BattleResult.valid, "Battle ended before the sample window completed.");
                yield return Screenshot(round + "-" + variant + "-battle.png");
                foreach (var pair in snapshots)
                    Require(JsonUtility.ToJson(pair.Key) == pair.Value, "Probe mutated source asset: " + pair.Key.name);
                var buffers = manager.Buffers;
                Require(session.TryReturnToMenu(true, out error), error);
                yield return WaitForLoad();
                Require(session.State == WarSandboxEntryState.Menu && !buffers.IsAllocated && session.Controller == null,
                    "Round retained the old scene or GPU buffers.");
                ReleaseOwned();
                yield return null;
                yield return Resources.UnloadUnusedAssets();
            }
            stage = "complete";
        }

        private IEnumerator Measure(WarSandboxBattleController controller, int round, string variant, string phase, float seconds)
        {
            stage = round + "-" + variant + "-" + phase;
            var frames = new List<float>(4096);
            var timings = new FrameTiming[1];
            double gpuTotal = 0;
            int gpuFrames = 0;
            float start = Time.time;
            double realStart = Time.realtimeSinceStartupAsDouble;
            double previous = realStart;
            while (Time.time - start < seconds)
            {
                FrameTimingManager.CaptureFrameTimings();
                yield return null;
                double now = Time.realtimeSinceStartupAsDouble;
                frames.Add((float)((now - previous) * 1000));
                previous = now;
                if (FrameTimingManager.GetLatestTimings(1, timings) > 0 && timings[0].gpuFrameTime > 0)
                { gpuTotal += timings[0].gpuFrameTime; gpuFrames++; }
                Require(errors.Count == 0, string.Join("\n", errors));
                Require(now - realStart < 120, "Frame sample exceeded real-time watchdog.");
            }
            Require(frames.Count >= 60, "Too few frames for a useful sample.");
            float total = 0;
            foreach (float ms in frames) total += ms;
            // Preserve untrimmed samples in acquisition order, outside the timing window.
            var csv = new System.Text.StringBuilder("frame,wall_ms\n");
            for (int i = 0; i < frames.Count; i++)
                csv.Append(i).Append(',').Append(frames[i].ToString("F6", CultureInfo.InvariantCulture)).Append('\n');
            File.WriteAllText(Path.Combine(output, round + "-" + variant + "-" + phase + ".csv"), csv.ToString());
            frames.Sort();
            var alive = new int[controller.ArmyCount];
            for (int i = 0; i < alive.Length; i++) alive[i] = controller.GetAliveUnitCount(i);
            var sample = new Sample
            {
                round = round, variant = variant, phase = phase, units = controller.manager.UnitTypes.TotalAgentCount,
                frames = frames.Count, meanMs = total / frames.Count, p50Ms = Percentile(frames, .5f),
                p95Ms = Percentile(frames, .95f), p99Ms = Percentile(frames, .99f),
                gpuFrames = gpuFrames, gpuMeanMs = gpuFrames > 0 ? (float)(gpuTotal / gpuFrames) : 0,
                seconds = (float)(Time.realtimeSinceStartupAsDouble - realStart), alive = alive
            };
            samples.Add(sample);
            File.WriteAllText(Path.Combine(output, "progress.json"), JsonUtility.ToJson(sample, true));
            Debug.Log(string.Format(CultureInfo.InvariantCulture, "VAT_PERF {0} {1}: {2:F3} ms mean / {3:F3} ms p95 ({4} frames)",
                variant, phase, sample.meanMs, sample.p95Ms, sample.frames));
        }

        private IEnumerator Warmup(float seconds)
        {
            float start = Time.time;
            double deadline = Time.realtimeSinceStartupAsDouble + 120;
            while (Time.time - start < seconds)
            {
                Require(errors.Count == 0, string.Join("\n", errors));
                Require(Time.realtimeSinceStartupAsDouble < deadline, "Warmup timed out.");
                yield return null;
            }
        }

        private IEnumerator WaitForLoad()
        {
            float deadline = Time.realtimeSinceStartup + 60;
            while (session.IsLoading && Time.realtimeSinceStartup < deadline) yield return null;
            Require(!session.IsLoading, "Scene transition timed out.");
            yield return null;
            Require(errors.Count == 0, string.Join("\n", errors));
        }

        private IEnumerator Screenshot(string filename)
        {
            // Captures happen outside timed windows, after the real player has presented frames.
            string path = Path.Combine(output, filename);
            ScreenCapture.CaptureScreenshot(path);
            float deadline = Time.realtimeSinceStartup + 10;
            while (!File.Exists(path) && Time.realtimeSinceStartup < deadline) yield return null;
            Require(File.Exists(path), "Missing screenshot: " + path);
            yield return new WaitForSecondsRealtime(.2f);
        }

        private static Dictionary<UnityEngine.Object, string> CaptureSources(ScenarioConfig scenario)
        {
            var result = new Dictionary<UnityEngine.Object, string>();
            void Add(UnityEngine.Object value) { if (value != null && !result.ContainsKey(value)) result.Add(value, JsonUtility.ToJson(value)); }
            Add(scenario);
            foreach (var unit in scenario.unitTypes)
            { Add(unit); Add(unit.spawnConfig); Add(unit.renderConfig); Add(unit.combatConfig); Add(unit.animationConfig); Add(unit.movementConfig); Add(unit.flockingConfig); }
            return result;
        }

        private T Own<T>(T value) where T : UnityEngine.Object { owned.Add(value); return value; }
        private void ReleaseOwned() { for (int i = owned.Count - 1; i >= 0; i--) if (owned[i] != null) Destroy(owned[i]); owned.Clear(); }
        private static float Percentile(List<float> sorted, float p) => sorted[Mathf.Clamp(Mathf.CeilToInt(p * sorted.Count) - 1, 0, sorted.Count - 1)];
        private static void Require(bool condition, string error) { if (!condition) throw new InvalidOperationException(error); }
        private void TrackError(string message, string stack, LogType type)
        { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message); }
        private static string Argument(string prefix)
        { foreach (string arg in Environment.GetCommandLineArgs()) if (arg.StartsWith(prefix, StringComparison.Ordinal)) return arg.Substring(prefix.Length); return null; }

        private IEnumerator Guarded(IEnumerator routine)
        {
            var stack = new Stack<IEnumerator>();
            stack.Push(routine);
            while (stack.Count > 0)
            {
                object next = null;
                bool moved = false;
                string failure = null;
                try { moved = stack.Peek().MoveNext(); if (moved) next = stack.Peek().Current; }
                catch (Exception ex) { failure = ex.ToString(); }
                if (failure != null) { Finish(false, failure); yield break; }
                if (!moved) { stack.Pop(); continue; }
                if (next is IEnumerator nested) stack.Push(nested); else yield return next;
            }
            Finish(errors.Count == 0, string.Join("\n", errors));
        }

        private void Finish(bool passed, string error)
        {
            var report = new Report
            {
                passed = passed, stage = stage, error = error, unity = Application.unityVersion,
                cpu = SystemInfo.processorType, gpu = SystemInfo.graphicsDeviceName, gpuMemoryMB = SystemInfo.graphicsMemorySize,
                api = SystemInfo.graphicsDeviceVersion, os = SystemInfo.operatingSystem, width = Screen.width, height = Screen.height,
                vSync = QualitySettings.vSyncCount, quality = QualitySettings.GetQualityLevel(), developmentBuild = Debug.isDebugBuild,
                referenceProfile = referenceMale != null ? referenceMale.name : "missing", rebakedProfile = rebakedMale != null ? rebakedMale.name : "missing",
                cameraPosition = CameraPosition, cameraTarget = CameraTarget,
                method = "ABBA, shipped 110000 units, fixed camera, 1280x720, vSync off, uncapped; setup after 3s warmup for 8s; battle after 20 simulation seconds for 12s. Wall-clock frame intervals, no trimming; GPU timing is supplementary and zero means unavailable. Female unchanged. passed means capture/lifecycle checks passed, not a performance threshold verdict. Not a full-battle or human acceptance test.",
                samples = samples.ToArray()
            };
            File.WriteAllText(Path.Combine(output, "performance.json"), JsonUtility.ToJson(report, true));
            Application.logMessageReceived -= TrackError;
            Debug.Log("VAT_PERFORMANCE " + (passed ? "PASS" : "FAIL") + " " + error);
            Application.Quit(passed ? 0 : 1);
        }
    }
}
#endif
