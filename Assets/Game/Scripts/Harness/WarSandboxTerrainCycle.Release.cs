#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Profiling;

namespace MassEngine.Game
{
    public sealed partial class WarSandboxTerrainCycle
    {
        [Serializable] private sealed class ReleaseEvidence
        {
            public string method;
            public bool settingsFileUntouched, retainedResourcesStable;
            public int rounds, restarts;
            public bool frameTimingEnabled;
            public int stabilityRequestedRounds;
            public List<string> memoryBudgetFailures = new List<string>();
            public List<MemorySample> memory = new List<MemorySample>();
            public List<StabilityVisit> visits = new List<StabilityVisit>();
        }
        [Serializable] private sealed class MemorySample
        {
            public int round;
            public string stage;
            public bool processMemoryAvailable;
            public long nativeUsed, nativeReserved, managedUsed, managedReserved, graphicsEstimate, privateBytes, workingSet;
            public int objects, meshes, materials, renderTextures, scenarios, units, spawns, audioServices, audioSources, cueClips;
        }
        [Serializable] private sealed class StabilityVisit
        {
            public int round, population, restarts;
            public string battlefield;
            public bool terrain, runtimeCopiesReleased;
        }
        private sealed class MeasuredCanvas
        {
            public Canvas canvas;
            public Camera camera;
            public float planeDistance;
        }
        private readonly List<MeasuredCanvas> measuredCanvases = new List<MeasuredCanvas>();
        private int measuredCameraMask;
        private bool IsDesktopPerformance => report.phase == "desktop-performance" || IsGpuBreakdown;
        private bool IsReleaseRun => report.phase == "release-performance" || report.phase == "stability" || IsDesktopPerformance;
        private int stabilityRounds = 4;

        private void InitializeReleaseSettings()
        {
            settingsFile = AbsoluteArgument("--war-sandbox-settings-file=");
            Require(Disjoint(settingsFile, Application.persistentDataPath) && Disjoint(settingsFile, output) &&
                !File.Exists(settingsFile) && !Directory.Exists(settingsFile), "Release validation needs an unused, isolated settings path.");
            report.release = new ReleaseEvidence
            {
                method = "No player plans or settings writes. Stability: the recorded number of passes over all five presets, first pass warms caches; each visit applies an isolated deployment, performs two full GPU resets, starts again, manually ends, then unloads. Record menu memory before and after explicit unused-asset unload/GC. Only collected menu samples are compared for retained growth; Windows GetProcessMemoryInfo supplies private commit and working set (unavailable values fail validation); driver/OS pool reservations are estimates, not exact resource ownership. Performance uses full UI. release-performance uses a serialized offscreen completion fence, not desktop FPS; desktop-performance exclusively uses the normal visible/focused camera and Present path."
            };
            if (report.phase == "stability" && Argument("--terrain-cycle-rounds=") is string requestedRounds)
                Require(int.TryParse(requestedRounds, out stabilityRounds) && stabilityRounds >= 4 && stabilityRounds <= 30,
                    "Stability rounds must be between 4 and 30.");
            report.release.stabilityRequestedRounds = report.phase == "stability" ? stabilityRounds : 0;
            report.release.frameTimingEnabled = FrameTimingManager.IsFeatureEnabled();
        }

        private IEnumerator RunReleaseValidation()
        {
            Require(session.State == WarSandboxEntryState.Menu && !session.IsLoading, "Release validation must start at the catalog.");
            var audio = WarSandboxAudio.Ensure();
            Require(audio.SettingsPath == settingsFile && string.IsNullOrEmpty(audio.SettingsError), "Audio did not use isolated settings.");
            audio.SetMuted(true); // Keep unattended measurements silent; never save this preference.
            if (IsGpuBreakdown) yield return RunGpuBreakdown();
            else if (report.phase != "stability") yield return RunReleasePerformance();
            else yield return RunStability();
            VerifySources();
            Require(!File.Exists(settingsFile), "Validation wrote an audio preference file.");
            report.release.settingsFileUntouched = true;
            Stage("complete");
        }

        private IEnumerator RunReleasePerformance()
        {
            // ABBA gives both scenarios a repeat after caches and GPU have warmed.
            string[] entries = { "launch-standard", "launch-mountain", "launch-mountain", "launch-standard" };
            for (int round = 0; round < entries.Length; round++)
            {
                string id = entries[round]; bool terrain = id == "launch-mountain";
                Stage("release-performance-" + round + "-" + id);
                yield return Enter(id, terrain);
                Require(manager.UnitTypes.TotalAgentCount == (terrain ? 384 : 100000), "Release sample changed the authored population.");
                yield return new WaitForSecondsRealtime(3);
                if (!IsDesktopPerformance) AttachMeasuredUI();
                yield return Measure(id, "r" + round + "-setup", 8);
                yield return CaptureReleaseFrame(id + "-r" + round + "-setup-ui.png");
                yield return Click("start");
                float deadline = Time.realtimeSinceStartup + 180;
                while (controller.TelemetrySnapshot.battleSeconds < 20 && Time.realtimeSinceStartup < deadline)
                {
                    Require(manager.IsBattleRunning && !controller.BattleResult.valid, "Performance warmup stopped early.");
                    yield return null;
                }
                Require(controller.TelemetrySnapshot.battleSeconds >= 20 && controller.SimulationSpeed == 1 && Time.timeScale == 1,
                    "Performance sample did not reach normal-speed battle time.");
                yield return Measure(id, "r" + round + "-battle-after-20s", 12);
                Require(manager.IsBattleRunning && !controller.BattleResult.valid, "Sample included settlement.");
                yield return CaptureReleaseFrame(id + "-r" + round + "-battle-ui.png");
                yield return Leave(false);
                yield return CollectMenuMemory(round);
                report.release.rounds++;
            }
        }

        private IEnumerator RunStability()
        {
            string[] entries = { "launch-open", "launch-mountain", "launch-point", "launch-three", "launch-standard" };
            int[] populations = { 512, 384, 512, 768, 100000 };
            MemorySample baseline = null;
            for (int round = 0; round < stabilityRounds; round++)
            {
                for (int preset = 0; preset < entries.Length; preset++)
                {
                    string id = entries[preset]; bool terrain = preset == 1;
                    Stage("stability-r" + round + "-" + id);
                    yield return Enter(id, terrain); VerifyFullStrength();
                    Require(manager.UnitTypes.TotalAgentCount == populations[preset], "Fresh scene inherited another deployment.");
                    var visit = new StabilityVisit { round = round, battlefield = id, population = populations[preset], terrain = terrain };
                    report.release.visits.Add(visit);
                    yield return Click("edit");
                    Require(deployment.IsEditing, "Deployment UI did not open.");
                    var originalBuffers = manager.Buffers;
                    yield return Click("deployment-apply"); VerifyFullStrength();
                    Require(!originalBuffers.IsAllocated && manager.scenarioConfig != sourceScenario, "Applying deployment retained the previous GPU world or edited the source.");
                    report.appliedPlans++;
                    for (int restart = 0; restart < 2; restart++)
                    {
                        yield return Click("start"); yield return new WaitForSecondsRealtime(1.5f);
                        Require(manager.IsBattleRunning && manager.TerrainGpuAllocated == terrain, "Battle did not start with the correct terrain provider.");
                        var oldBuffers = manager.Buffers;
                        yield return Click("reset"); VerifyFullStrength();
                        Require(!oldBuffers.IsAllocated && !ReferenceEquals(oldBuffers, manager.Buffers) && manager.Buffers.IsAllocated &&
                            manager.UnitTypes.TotalAgentCount == populations[preset], "Reset retained buffers or lost the population.");
                        visit.restarts++; report.release.restarts++;
                    }
                    yield return Click("start"); yield return new WaitForSecondsRealtime(1);
                    yield return Click("end-battle");
                    Require(controller.BattleResult.valid && controller.BattleResult.winnerTeamId == -1, "Manual end did not settle without a winner.");
                    RecordMemory(round, id + "-active");
                    if (round == stabilityRounds - 1) yield return CapturePresetUI(id + "-last-settlement-ui.png");
                    yield return Leave(true, "result-menu"); visit.runtimeCopiesReleased = true;
                    VerifySources();
                    Require(FindObjectsByType<WarSandboxAudio>(FindObjectsSortMode.None).Length == 1 &&
                        FindObjectsByType<WarSandboxRuntimeDeployment>(FindObjectsSortMode.None).Length == 0,
                        "Scene changes leaked gameplay/audio owners.");
                }
                yield return CollectMenuMemory(round);
                var collected = report.release.memory.Last();
                if (baseline == null) baseline = collected;
                else
                {
                    // Keep the observation window intact if a retention budget is exceeded;
                    // final validation still fails, with every checkpoint available for diagnosis.
                    try { VerifyRetainedMemory(baseline, collected); }
                    catch (InvalidOperationException error) { report.release.memoryBudgetFailures.Add("round " + round + ": " + error.Message); }
                }
                report.release.rounds++;
                WriteReport();
            }
            report.release.retainedResourcesStable = report.release.memoryBudgetFailures.Count == 0;
            Require(report.release.retainedResourcesStable, string.Join("\n", report.release.memoryBudgetFailures));
        }

        private IEnumerator CaptureReleaseFrame(string filename)
        {
            if (!IsDesktopPerformance) { yield return CaptureWorld(filename); yield break; }
            yield return new WaitForEndOfFrame();
            var screenshot = ScreenCapture.CaptureScreenshotAsTexture();
            try
            {
                Require(screenshot != null && screenshot.width == Screen.width && screenshot.height == Screen.height, "Desktop screenshot is unavailable.");
                File.WriteAllBytes(Path.Combine(output, filename), screenshot.EncodeToPNG());
            }
            finally { if (screenshot != null) Destroy(screenshot); }
        }

        private IEnumerator CollectMenuMemory(int round)
        {
            Require(session.State == WarSandboxEntryState.Menu && manager == null && renderTarget == null, "Memory checkpoint is not an unloaded catalog.");
            yield return new WaitForSecondsRealtime(.5f);
            RecordMemory(round, "menu-before-collection");
            yield return Resources.UnloadUnusedAssets();
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            yield return null; yield return null;
            RecordMemory(round, "menu-collected");
        }

        private void RecordMemory(int round, string stage)
        {
            var sample = new MemorySample { round = round, stage = stage,
                nativeUsed = Profiler.GetTotalAllocatedMemoryLong(), nativeReserved = Profiler.GetTotalReservedMemoryLong(),
                managedUsed = Profiler.GetMonoUsedSizeLong(), managedReserved = Profiler.GetMonoHeapSizeLong(),
                graphicsEstimate = Profiler.GetAllocatedMemoryForGraphicsDriver(),
                objects = Resources.FindObjectsOfTypeAll<GameObject>().Length, meshes = Resources.FindObjectsOfTypeAll<Mesh>().Length,
                materials = Resources.FindObjectsOfTypeAll<Material>().Length, renderTextures = Resources.FindObjectsOfTypeAll<RenderTexture>().Length,
                scenarios = Resources.FindObjectsOfTypeAll<ScenarioConfig>().Length, units = Resources.FindObjectsOfTypeAll<UnitTypeConfig>().Length,
                spawns = Resources.FindObjectsOfTypeAll<SpawnConfig>().Length,
                audioServices = FindObjectsByType<WarSandboxAudio>(FindObjectsSortMode.None).Length,
                audioSources = FindObjectsByType<AudioSource>(FindObjectsSortMode.None).Length,
                cueClips = Resources.FindObjectsOfTypeAll<AudioClip>().Count(c => c.name.StartsWith("Sandbox ", StringComparison.Ordinal)) };
            sample.processMemoryAvailable = WarSandboxProcessMemory.TryRead(out sample.privateBytes, out sample.workingSet);
            report.release.memory.Add(sample);
            Require(sample.processMemoryAvailable, "Windows process memory counters are unavailable; zero is not evidence of stable memory.");
        }

        private static void VerifyRetainedMemory(MemorySample baseline, MemorySample sample)
        {
            const long MiB = 1024 * 1024;
            Require(baseline.processMemoryAvailable && sample.processMemoryAvailable &&
                baseline.privateBytes > 0 && sample.privateBytes > 0 && baseline.workingSet > 0 && sample.workingSet > 0,
                "Cannot compare unavailable process memory counters.");
            Require(sample.audioServices == 1 && sample.audioSources == 1 && sample.cueClips == 5,
                "Audio objects accumulated after scene changes.");
            Require(sample.scenarios <= baseline.scenarios && sample.units <= baseline.units && sample.spawns <= baseline.spawns,
                "Runtime deployment assets accumulated after collection.");
            Require(sample.objects <= baseline.objects + 8 && sample.meshes <= baseline.meshes + 2 &&
                sample.materials <= baseline.materials + 4 && sample.renderTextures <= baseline.renderTextures + 2,
                "Native scene objects grew beyond the warmed-cache allowance.");
            Require(sample.nativeUsed <= baseline.nativeUsed + 8 * MiB && sample.managedUsed <= baseline.managedUsed + 4 * MiB &&
                sample.graphicsEstimate <= baseline.graphicsEstimate + 16 * MiB && sample.privateBytes <= baseline.privateBytes + 64 * MiB,
                "Retained memory grew beyond the warmed-cache allowance (native 8 / managed 4 / graphics estimate 16 / process private 64 MiB).");
        }

        private void AttachMeasuredUI()
        {
            Require(measuredCanvases.Count == 0, "Measurement UI already attached.");
            measuredCameraMask = renderCamera.cullingMask; renderCamera.cullingMask = -1;
            foreach (var canvas in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            {
                if (!canvas.isRootCanvas || canvas.renderMode != RenderMode.ScreenSpaceOverlay || !canvas.isActiveAndEnabled) continue;
                measuredCanvases.Add(new MeasuredCanvas { canvas = canvas, camera = canvas.worldCamera, planeDistance = canvas.planeDistance });
                canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = renderCamera; canvas.planeDistance = 1;
            }
            Require(measuredCanvases.Count >= 2, "Battle and front-end UI were not included in the measurement.");
            Canvas.ForceUpdateCanvases();
        }

        private void RestoreMeasuredUI()
        {
            if (measuredCanvases.Count == 0) return;
            foreach (var previous in measuredCanvases) if (previous.canvas != null)
            {
                previous.canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                previous.canvas.worldCamera = previous.camera; previous.canvas.planeDistance = previous.planeDistance;
            }
            if (renderCamera != null) renderCamera.cullingMask = measuredCameraMask;
            measuredCanvases.Clear();
        }
    }
}
#endif
