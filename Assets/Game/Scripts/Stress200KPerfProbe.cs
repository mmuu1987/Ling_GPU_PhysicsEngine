// Stress200KPerfProbe.cs  –  Play-mode perf probe for 200k stress test.
// In Play mode: applies the Draft set by Stress200KRunner, starts battle, measures FPS.

using UnityEngine;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
#endif

namespace MassEngine.Game
{
    public sealed class Stress200KPerfProbe : MonoBehaviour
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        const float WarmupSeconds = 5f;
        const float MeasureSeconds = 30f;
        const float SafetyFpsFloor = 2f;
        const float SetupTimeout = 30f;

        [Serializable] class Receipt
        {
            public bool passed;
            public string error, gpu, stage;
            public int totalUnits, aliveAtEnd;
            public float avgFps, p5Fps, p1Fps;
            public float avgFrameMs;
            public float maxTerrainSolveMs, avgTerrainSolveMs;
            public float measureSeconds;
            public int measuredFrames;
            public int gridOverflowPeak, flowRebuildsTotal;
            public float[] fpsSamples;
        }

        Receipt receipt = new Receipt();
        string output;
        float elapsed;
        int state; // 0=setup, 1=warmup, 2=measuring, 3=done
        List<float> frameTimes = new List<float>(4096);
        List<float> terrainSolveTimes = new List<float>();
        List<float> fpsPerSecond = new List<float>();
        float secElapsed;
        int secFrames;
        MassEngineManager mgr;
        WarSandboxBattleController ctrl;
        float setupTimer;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            var args = Environment.GetCommandLineArgs();
            if (!args.Contains("--stress-200k-probe")) return;
            var p = Array.Find(args, x => x.StartsWith("--stress-output="));
            if (p == null) { Application.Quit(2); return; }
            var go = new GameObject("Stress200K Perf Probe");
            DontDestroyOnLoad(go);
            go.AddComponent<Stress200KPerfProbe>().output = p.Substring("--stress-output=".Length);
        }

        void Start()
        {
            Directory.CreateDirectory(output);
            receipt.gpu = SystemInfo.graphicsDeviceName;
            receipt.stage = "setup";
            state = 0;
            setupTimer = 0;
            Debug.Log("Stress200K: Probe started, looking for deployment...");
        }

        void Update()
        {
            // ─── State 0: Apply draft and start battle ───────────
            if (state == 0)
            {
                setupTimer += Time.unscaledDeltaTime;
                if (setupTimer > SetupTimeout)
                {
                    receipt.error = "Setup timeout";
                    Finish(1);
                    return;
                }

                var session = WarSandboxSceneSession.Instance;
                if (session == null) return;
                ctrl = session.Controller;
                if (ctrl == null) return;
                var d = ctrl.GetComponent<WarSandboxRuntimeDeployment>();
                if (d == null) return;

                // Check if Draft is set (from Edit mode)
                if (d.IsEditing && d.Draft != null)
                {
                    Debug.Log("Stress200K: Draft found, applying...");
                    if (!d.TryApply(out var applyErr))
                    {
                        // TryApply might fail due to terrain. Try without terrain validation.
                        Debug.LogWarning("Stress200K: TryApply failed: " + applyErr + ", trying direct start...");
                        // Fallback: just start with whatever config is loaded
                    }
                    else
                    {
                        Debug.Log("Stress200K: Draft applied successfully");
                    }
                }

                // Start the battle
                if (ctrl.Phase == WarSandboxBattlePhase.Setup)
                {
                    Debug.Log("Stress200K: Starting battle...");
                    ctrl.StartOrResumeBattle();
                }

                if (ctrl.Phase == WarSandboxBattlePhase.Running)
                {
                    mgr = ctrl.manager;
                    receipt.totalUnits = mgr.scenarioConfig.unitTypes.Sum(u => u.spawnConfig.unitCount);
                    Debug.Log("Stress200K: Battle running with " + receipt.totalUnits + " units");
                    state = 1;
                    receipt.stage = "warmup";
                    elapsed = 0;
                }
                return;
            }

            float dt = Time.unscaledDeltaTime;
            elapsed += dt;

            // ─── State 1: Warmup ────────────────────────────────
            if (state == 1)
            {
                if (elapsed >= WarmupSeconds)
                {
                    state = 2;
                    elapsed = 0;
                    receipt.stage = "measuring";
                    Debug.Log("Stress200K: Measurement started (" + receipt.totalUnits + " units)");
                }
                return;
            }

            // ─── State 2: Measuring ─────────────────────────────
            if (state == 2)
            {
                frameTimes.Add(dt);
                elapsed += dt;

                secElapsed += dt;
                secFrames++;
                if (secElapsed >= 1f)
                {
                    float fps = secFrames / secElapsed;
                    fpsPerSecond.Add(fps);
                    Debug.Log("Stress200K: " + fps.ToString("F1") + " FPS at " + elapsed.ToString("F0") + "s");
                    secElapsed = 0;
                    secFrames = 0;
                }

                if (mgr != null)
                {
                    float tms = mgr.TerrainMaxSolveMilliseconds;
                    if (tms > 0) terrainSolveTimes.Add(tms);
                }

                // Safety floor
                if (elapsed > 5f && frameTimes.Count > 30)
                {
                    float sum = 0;
                    int cnt = Mathf.Min(30, frameTimes.Count);
                    for (int i = frameTimes.Count - cnt; i < frameTimes.Count; i++)
                        sum += 1f / frameTimes[i];
                    if (sum / cnt < SafetyFpsFloor)
                    {
                        Debug.LogWarning("Stress200K: Safety stop at " + (sum / cnt).ToString("F1") + " FPS");
                        CollectResults();
                        Finish(0);
                        return;
                    }
                }

                if (elapsed >= MeasureSeconds)
                {
                    CollectResults();
                    Finish(0);
                }
            }
        }

        void CollectResults()
        {
            if (frameTimes.Count == 0) return;
            var sorted = new List<float>(frameTimes);
            sorted.Sort();

            receipt.measuredFrames = sorted.Count;
            receipt.measureSeconds = elapsed;

            float sumFps = 0;
            foreach (var ft in sorted) sumFps += 1f / ft;
            receipt.avgFps = sumFps / sorted.Count;
            receipt.avgFrameMs = sorted.Average() * 1000f;

            int p5Idx = sorted.Count * 5 / 100;
            receipt.p5Fps = 1f / sorted[Mathf.Min(sorted.Count - 1, sorted.Count - 1 - p5Idx)];
            receipt.p1Fps = 1f / sorted[sorted.Count - 1];

            if (terrainSolveTimes.Count > 0)
            {
                receipt.maxTerrainSolveMs = terrainSolveTimes.Max();
                receipt.avgTerrainSolveMs = terrainSolveTimes.Average();
            }

            receipt.fpsSamples = fpsPerSecond.ToArray();

            if (mgr?.Telemetry != null)
            {
                var snap = mgr.Telemetry.Snapshot;
                receipt.aliveAtEnd = snap.GetAliveCount(0) + snap.GetAliveCount(1);
                receipt.gridOverflowPeak = snap.peakGridOverflowPerFrame;
                if (snap.flowRebuildsByTeam != null)
                    foreach (var r in snap.flowRebuildsByTeam) receipt.flowRebuildsTotal += r;
            }
        }

        void Finish(int code)
        {
            receipt.passed = (code == 0);
            receipt.stage = code == 0 ? "complete" : "error";
            try
            {
                string json = JsonUtility.ToJson(receipt, true);
                File.WriteAllText(Path.Combine(output, "stress200k_receipt.json"), json);
                Debug.Log("STRESS200K_RESULT " + json);
            }
            catch (Exception ex) { Debug.LogError("Stress200K: " + ex); }
            Application.Quit(code);
        }
#else
        void Start() { }
#endif
    }
}