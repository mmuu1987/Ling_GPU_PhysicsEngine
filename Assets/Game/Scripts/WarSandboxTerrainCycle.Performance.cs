#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace MassEngine.Game
{
    public sealed partial class WarSandboxTerrainCycle
    {
        [Serializable] private sealed class PerformanceSample
        {
            public string battlefield, window, method;
            public string workload;
            public bool diagnostic;
            public int units, frames, renderedFrames, flowRebuilds, gpuTimingSamples, width, height;
            public float simulationStart, simulationEnd;
            public double meanMs, p50Ms, p95Ms, p99Ms, maxMs, gpuMeanMs, flowCpuMs, meanFlowSolveMs;
            public double renderSubmitMeanMs, gpuCompletionWaitMeanMs, flowFrameMeanMs, otherFrameMeanMs;
            public int flowFrames;
            public bool frameTimingEnabled, desktopPresentation;
            public int focusedFrames;
            public int osForegroundFrames;
            public bool osForegroundCheckEnabled;
            public float lifetimeMaxFlowSolveMs;
            public float lifetimeMaxDynamicQueueWaitMs;
            public long sharedTerrainGpuBytes, graphicsDriverAllocatedBytes;
            public Vector3 cameraPosition, cameraTarget;
        }
        private Vector3 cameraTarget;
        private RenderPipeline.StandardRequest renderRequest;
        private bool cameraWasEnabled;
        private double lastRenderSubmitMs, lastGpuCompletionWaitMs;

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
#endif
        private bool IsOsForeground()
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            GetWindowThreadProcessId(GetForegroundWindow(), out uint processId);
            return processId == report.processId;
#else
            return Application.isFocused;
#endif
        }

        private void LateUpdate()
        {
            if (drawFrozenGpuSnapshot && !finished)
            {
                try { DrawFrozenGpuSnapshot(); }
                catch (Exception error) { report.errors.Add(error.ToString()); }
            }
            if (renderCamera == null || renderTarget == null || finished) return;
            // DrawMeshInstancedIndirect has been queued by the manager's Update at this point.
            // Hidden Windows players skip the ordinary render loop, including targetTexture cameras.
            try
            {
                double started = Time.realtimeSinceStartupAsDouble;
                RenderPipeline.SubmitRenderRequest(renderCamera, renderRequest);
                lastRenderSubmitMs = (Time.realtimeSinceStartupAsDouble - started) * 1000;
                lastGpuCompletionWaitMs = 0;
                if (report.phase == "performance" || IsReleaseRun)
                {
                    // Hidden players have no Present pacing: otherwise CPU frame intervals can
                    // measure queued submissions followed by large driver stalls, not completed frames.
                    // A four-byte readback fences this frame. Report this serialized cost explicitly;
                    // it is deliberately not advertised as normal overlapping desktop FPS.
                    started = Time.realtimeSinceStartupAsDouble;
                    var completion = AsyncGPUReadback.Request(renderTarget, 0, 0, 1, 0, 1, 0, 1);
                    completion.WaitForCompletion();
                    lastGpuCompletionWaitMs = (Time.realtimeSinceStartupAsDouble - started) * 1000;
                    Require(!completion.hasError, "Offscreen GPU completion readback failed.");
                }
            }
            catch (Exception error) { report.errors.Add(error.ToString()); }
        }

        private IEnumerator RunPerformance()
        {
            foreach (string id in new[] { "mountain-battle", "open-battle" })
            {
                bool terrain = id == "mountain-battle";
                Stage("performance-" + id);
                yield return Enter(id, terrain);
                Require(manager.UnitTypes.TotalAgentCount == (terrain ? 384 : 110000), "Unexpected performance population.");
                yield return new WaitForSecondsRealtime(3);
                yield return Measure(id, "setup", 8);
                yield return CaptureWorld(id + "-setup.png");
                Require(controller.StartDefaultBattle(), controller.CommandError);
                float deadline = Time.realtimeSinceStartup + 100;
                while (controller.TelemetrySnapshot.battleSeconds < 20 && Time.realtimeSinceStartup < deadline)
                {
                    Require(manager.IsBattleRunning && !controller.BattleResult.valid, "Performance battle ended before warmup.");
                    yield return null;
                }
                Require(controller.TelemetrySnapshot.battleSeconds >= 20, "Performance warmup timed out.");
                yield return Measure(id, "battle-after-20s", 12);
                yield return CaptureWorld(id + "-battle.png");
                Require(manager.IsBattleRunning && !controller.BattleResult.valid, "Performance window included settlement.");
                VerifySources();
                yield return Leave(false);
                yield return Resources.UnloadUnusedAssets();
            }
            VerifySources(); Stage("complete");
        }

        private IEnumerator Measure(string id, string window, float seconds)
        {
            var sample = new PerformanceSample { battlefield = id, window = window, units = manager.UnitTypes.TotalAgentCount,
                width = IsDesktopPerformance ? Screen.width : renderTarget.width, height = IsDesktopPerformance ? Screen.height : renderTarget.height,
                cameraPosition = renderCamera.transform.position, cameraTarget = cameraTarget,
                frameTimingEnabled = FrameTimingManager.IsFeatureEnabled(), desktopPresentation = IsDesktopPerformance,
                workload = IsGpuBreakdown ? gpuWorkload : "full", diagnostic = IsGpuBreakdown,
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
                osForegroundCheckEnabled = IsDesktopPerformance,
#endif
                simulationStart = controller.TelemetrySnapshot.battleSeconds, sharedTerrainGpuBytes = manager.TerrainGpuBytes,
                method = "1280x720 full URP StandardRequest in LateUpdate into offscreen RenderTexture, normal indirect unit rendering; hidden development player, vSync off, uncapped. Each frame waits for a one-pixel GPU readback, so this is serialized CPU+GPU end-to-end cost including synchronization, NOT desktop FPS. Wall intervals without trimming. Overlay UI is not in the render texture. GPU FrameTiming is supplementary; zero samples means unavailable. Graphics-driver allocation is Unity's estimate, not board capacity. Different populations are separate baselines, not a terrain/plane speed ratio." };
            if (IsReleaseRun) sample.method = "Authored camera and full uGUI at the recorded resolution; full URP StandardRequest, hidden Development player, vSync off, uncapped. Every frame waits for a one-pixel GPU readback. Serialized end-to-end wall time includes CPU, GPU and synchronization; NOT desktop FPS. Submit and completion wait are wall spans, NOT isolated GPU kernel timings. CPU flow delta excludes upload. FrameTiming zero samples means unavailable. No trimming; separate populations are not a terrain cost ratio.";
            if (IsDesktopPerformance) sample.method = "Visible, focused player using the normal Unity camera/Present path and overlay uGUI; no StandardRequest, target texture or forced GPU readback. vSync off, uncapped, authored camera. Wall intervals include normal CPU/GPU overlap and presentation. GPU FrameTiming uses unique timestamps only; zero samples means unavailable. Submit/wait subspans are not collected in this mode. Screenshots are outside measured windows.";
            if (IsGpuBreakdown) sample.method += " DIAGNOSTIC workload=" + gpuWorkload + ": full retains normal gameplay; compute-only masks all world drawing but keeps manager updates; frozen-draw disables manager GPU updates and replays its existing draw dispatchers/buffers; empty masks world drawing and disables manager GPU updates. Overlay UI remains. Frozen modes do not advance simulation or animation, and omitted work changes overlap/content: differences are ablation evidence, NOT additive GPU pass timings or a product FPS result. LOD readbacks occur outside sample windows.";
            var frames = new List<double>(); var gpu = new List<double>();
            var submits = new List<double>(); var waits = new List<double>();
            var flowFrames = new List<double>(); var otherFrames = new List<double>();
            var timings = new FrameTiming[1]; var csv = new StringBuilder("frame,wall_ms,gpu_ms,flow_fields,total_flow_cpu_ms,render_submit_ms,gpu_completion_wait_ms,flow_cpu_delta_ms\n");
            int startFields = manager.TerrainCompletedFields, firstRendered = renderedFrames;
            double startCpu = manager.TerrainTotalSolveMilliseconds;
            ulong lastGpuTimestamp = 0;
            double previousFlowCpu = startCpu;
            double start = Time.realtimeSinceStartupAsDouble, previous = start;
            while (Time.realtimeSinceStartupAsDouble - start < seconds)
            {
                FrameTimingManager.CaptureFrameTimings();
                yield return null;
                double now = Time.realtimeSinceStartupAsDouble, ms = (now - previous) * 1000; previous = now;
                frames.Add(ms);
                double flowDelta = manager.TerrainTotalSolveMilliseconds - previousFlowCpu;
                previousFlowCpu = manager.TerrainTotalSolveMilliseconds;
                (flowDelta > 0 ? flowFrames : otherFrames).Add(ms);
                submits.Add(lastRenderSubmitMs); waits.Add(lastGpuCompletionWaitMs);
                if (Application.isFocused) sample.focusedFrames++;
                if (IsDesktopPerformance)
                {
                    bool osForeground = IsOsForeground();
                    if (osForeground) sample.osForegroundFrames++;
                    Require(Application.isFocused && osForeground,
                        "Desktop performance requires both Unity focus and the OS foreground window to belong to this player.");
                }
                double gpuMs = 0;
                if (FrameTimingManager.GetLatestTimings(1, timings) > 0 && timings[0].gpuFrameTime > 0 && timings[0].frameStartTimestamp != lastGpuTimestamp)
                { gpuMs = timings[0].gpuFrameTime; lastGpuTimestamp = timings[0].frameStartTimestamp; gpu.Add(gpuMs); }
                csv.Append(frames.Count).Append(',').Append(ms.ToString("F6", CultureInfo.InvariantCulture)).Append(',')
                    .Append(gpuMs.ToString("F6", CultureInfo.InvariantCulture)).Append(',').Append(manager.TerrainCompletedFields).Append(',')
                    .Append(manager.TerrainTotalSolveMilliseconds.ToString("F6", CultureInfo.InvariantCulture)).Append(',')
                    .Append(lastRenderSubmitMs.ToString("F6", CultureInfo.InvariantCulture)).Append(',')
                    .Append(lastGpuCompletionWaitMs.ToString("F6", CultureInfo.InvariantCulture)).Append(',')
                    .Append(flowDelta.ToString("F6", CultureInfo.InvariantCulture)).Append('\n');
            }
            sample.frames = frames.Count; sample.renderedFrames = renderedFrames - firstRendered;
            Require(sample.frames >= 60 && sample.renderedFrames >= sample.frames - 1, "Performance frames did not render the world camera.");
            sample.flowRebuilds = manager.TerrainCompletedFields - startFields;
            sample.flowCpuMs = manager.TerrainTotalSolveMilliseconds - startCpu;
            sample.meanFlowSolveMs = sample.flowRebuilds > 0 ? sample.flowCpuMs / sample.flowRebuilds : 0;
            sample.lifetimeMaxFlowSolveMs = manager.TerrainMaxSolveMilliseconds;
            sample.lifetimeMaxDynamicQueueWaitMs = manager.TerrainMaxDynamicQueueWaitMilliseconds;
            sample.simulationEnd = controller.TelemetrySnapshot.battleSeconds;
            sample.graphicsDriverAllocatedBytes = UnityEngine.Profiling.Profiler.GetAllocatedMemoryForGraphicsDriver();
            sample.gpuTimingSamples = gpu.Count; sample.gpuMeanMs = Mean(gpu);
            sample.renderSubmitMeanMs = Mean(submits); sample.gpuCompletionWaitMeanMs = Mean(waits);
            sample.flowFrames = flowFrames.Count; sample.flowFrameMeanMs = Mean(flowFrames); sample.otherFrameMeanMs = Mean(otherFrames);
            sample.meanMs = Mean(frames); frames.Sort(); sample.p50Ms = Percentile(frames, .5); sample.p95Ms = Percentile(frames, .95);
            sample.p99Ms = Percentile(frames, .99); sample.maxMs = frames[frames.Count - 1];
            report.performance.Add(sample);
            File.WriteAllText(Path.Combine(output, id + "-" + window + ".csv"), csv.ToString()); WriteReport();
        }

        private void ConfigureCamera(bool terrain)
        {
            ReleaseCamera();
            renderCamera = manager.cullingCamera; Require(renderCamera != null, "Missing battle camera.");
            foreach (var control in FindObjectsByType<MyCameraManager>(FindObjectsSortMode.None)) control.LockInput();
            if (!IsPresetRun && !IsPlayabilityRun && !IsReleaseRun)
            {
                renderCamera.transform.position = terrain ? new Vector3(-95, 235, -280) : new Vector3(0, 260, -220);
                cameraTarget = terrain ? new Vector3(-75, 24, 0) : new Vector3(0, 0, 40);
                renderCamera.transform.LookAt(cameraTarget);
            }
            else cameraTarget = renderCamera.transform.position + renderCamera.transform.forward * 100;
            if (manager.lodCenter != null && manager.lodCenter != renderCamera.transform) manager.lodCenter.position = renderCamera.transform.position;
            if (IsDesktopPerformance)
            {
                cameraWasEnabled = renderCamera.enabled; renderCamera.enabled = true;
                Require(renderCamera.targetTexture == null, "Desktop camera unexpectedly renders to a texture.");
            }
            else CreateCaptureTarget();
        }
        private void CreateCaptureTarget()
        {
            cameraWasEnabled = renderCamera.enabled; renderCamera.enabled = false;
            renderTarget = new RenderTexture(IsPresetRun || IsPlayabilityRun || IsReleaseRun || IsLightLoop ? Screen.width : 1280, IsPresetRun || IsPlayabilityRun || IsReleaseRun || IsLightLoop ? Screen.height : 720,
                24, RenderTextureFormat.ARGB32) { name = "Battle validation camera" };
            Require(renderTarget.Create(), "Could not create the validation render target.");
            renderCamera.targetTexture = renderTarget;
            renderRequest = new RenderPipeline.StandardRequest { destination = renderTarget };
        }
        private void CameraRendered(ScriptableRenderContext context, Camera camera)
        { if (camera == renderCamera) renderedFrames++; }
        private IEnumerator CaptureWorld(string filename)
        {
            if (!CaptureLightLoopImage(filename)) yield break;
            int before = renderedFrames;
            yield return null; yield return null;
            Require(renderTarget != null && renderedFrames > before, "World camera did not render.");
            var previous = RenderTexture.active;
            var image = new Texture2D(renderTarget.width, renderTarget.height, TextureFormat.RGB24, false);
            try
            {
                RenderTexture.active = renderTarget;
                image.ReadPixels(new Rect(0, 0, renderTarget.width, renderTarget.height), 0, 0); image.Apply();
                var pixels = image.GetPixels32(); var colors = new HashSet<int>(); int magenta = 0, count = 0;
                for (int i = 0; i < pixels.Length; i += 31)
                {
                    var p = pixels[i]; colors.Add((p.r >> 3) << 10 | (p.g >> 3) << 5 | (p.b >> 3)); count++;
                    if (p.r > 220 && p.b > 220 && p.g < 30) magenta++;
                }
                // Preserve failure evidence before asserting. Sparse sampling can miss
                // a small surviving army at a low-resolution, wide-angle camera.
                File.WriteAllBytes(Path.Combine(output, filename), image.EncodeToPNG());
                int sparseColors = colors.Count;
                if (colors.Count <= 24)
                    foreach (var p in pixels) colors.Add((p.r >> 3) << 10 | (p.g >> 3) << 5 | (p.b >> 3));
                Debug.Log("WORLD_CAPTURE " + filename + " sparseColors=" + sparseColors + " colors=" + colors.Count +
                    " magenta=" + magenta + "/" + count + " size=" + image.width + "x" + image.height);
                Require(colors.Count > 24 && magenta < count / 20,
                    "Blank or missing-shader world capture: " + filename + " colors=" + colors.Count + " magenta=" + magenta + "/" + count);
            }
            finally { RenderTexture.active = previous; Destroy(image); }
        }
        private void ReleaseCamera()
        {
            RestoreMeasuredUI();
            if (renderCamera != null && renderCamera.targetTexture == renderTarget) renderCamera.targetTexture = null;
            if (renderCamera != null) renderCamera.enabled = cameraWasEnabled;
            if (renderTarget != null) { renderTarget.Release(); Destroy(renderTarget); }
            renderTarget = null; renderCamera = null; renderRequest = null;
        }
        private static double Mean(List<double> values) { double sum = 0; foreach (double value in values) sum += value; return values.Count == 0 ? 0 : sum / values.Count; }
        private static double Percentile(List<double> sorted, double q) => sorted[Math.Min(sorted.Count - 1, (int)Math.Ceiling(sorted.Count * q) - 1)];
    }
}
#endif
