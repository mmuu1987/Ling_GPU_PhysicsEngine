#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Profiling;
using Unity.Profiling;
using UnityEngine.Rendering;
using System.Reflection;

namespace MassEngine.Game.Editor
{
    // P0 measurement/isolation only. Ordinary Editor/player sessions do not install this observer.
    // No scene/asset saves, no build method, and no product behaviour changes.
    [InitializeOnLoad]
    public static class InteractionRefinementP0
    {
        private static string mode, output;
        private static int stage, lastFrame = -1;
        private static double deadline, nextStep;
        private static float runningSince;
        private static bool finishing;
        private static ProfilerRecorder mainThread, renderThread, gc;
        private static readonly FrameTiming[] timing = new FrameTiming[1];
        private static readonly List<Row> rows = new List<Row>();
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private static long memoryBefore;
        private static bool OwnedGui => Environment.GetCommandLineArgs().Contains("--interaction-p0-owned-editor");
        private static bool measurementComplete;
        private static int lastRendered = -1, renderedSamples;
        private static Vector3 cameraStart, cameraEnd;
        private static Quaternion cameraRotationStart, cameraRotationEnd;
        private static Type profilerDriver;
        private static MethodInfo getRawFrame;
        private static PropertyInfo lastProfilerFrame;
        private static string gpuTimingSource = "FrameTimingManager", profilerSetup = "not requested";


        [Serializable] private sealed class FunctionalReceipt { public bool passed; public string error; }
        [Serializable] private sealed class Row
        {
            public int frame;
            public float seconds;
            public double frameMs, mainThreadMs, renderThreadMs, cpuTimingMs, gpuTimingMs;
            public long gcBytes;
        }
        [Serializable] private sealed class PerfReceipt
        {
            public bool passed, editorOnly = true, osInputTest = false, synchronousAgentReadback = false;
            public bool renderedEditor, cameraFixed, gpuProfilingRequested;
            public int renderedGameSamples, vSyncCount, targetFrameRate;
            public Vector3 cameraStart, cameraEnd;
            public string gpuTimingSource, profilerSetup;
            public string error, unity, gpu, graphicsApi, scene, timingNote;
            public int units, width, height, frames, gpuSamples, graphicsMemoryMB;
            public bool mainThreadCounterAvailable, renderThreadCounterAvailable, gcCounterAvailable, gpuTimingAvailable;
            public double frameMedianMs, frameP95Ms, mainThreadMedianMs, mainThreadP95Ms, gpuMedianMs, gpuP95Ms, gcBytesPerSample;
            public long unityAllocatedBefore, unityAllocatedAfter, unityReservedAfter, managedUsedAfter, graphicsDriverAllocatedAfter;
        }

        static InteractionRefinementP0()
        {
            if (!Application.isBatchMode && !OwnedGui) return;
            mode = Arg("--interaction-p0-mode=");
            if (string.IsNullOrEmpty(mode)) return;
            try
            {
                output = Arg("--interaction-p0-output=");
                string allowed = Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/InteractionRefinement-20261007/P0")) + Path.DirectorySeparatorChar;
                if (string.IsNullOrEmpty(output) || !Path.IsPathFullyQualified(output) ||
                    !Path.GetFullPath(output).StartsWith(allowed, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("P0 output must be an isolated owned log directory.");
                output = Path.GetFullPath(output);
                if (!Directory.Exists(output)) throw new InvalidOperationException("P0 runner must create its evidence directory first.");
                if (mode != "editmode" && mode != "functional" && mode != "performance") throw new InvalidOperationException("Unknown P0 mode.");
                WarSandboxUnitStatStore.DefaultPathOverride = Path.Combine(output, "globals.json");
                Debug.Log("INTERACTION_P0_ISOLATED " + mode);
                if (mode == "editmode") return;
                deadline = EditorApplication.timeSinceStartup + 540;
                EditorApplication.update += Tick;
                if (mode == "performance" && OwnedGui)
                {
                    RenderPipelineManager.endContextRendering += Rendered;
                    ConfigureGpuProfiler();
                }
            }
            catch (Exception e)
            {
                Debug.LogError("P0 setup failed: " + e.Message);
                EditorApplication.delayCall += () => EditorApplication.Exit(1);
            }
        }

        private static string Arg(string prefix) => Environment.GetCommandLineArgs().FirstOrDefault(x => x.StartsWith(prefix, StringComparison.Ordinal))?.Substring(prefix.Length);
        public static void Begin()
        {
            if ((!Application.isBatchMode && !OwnedGui) || (mode != "functional" && mode != "performance")) throw new InvalidOperationException("Explicit P0 batch opt-in required.");
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Refuse an already playing editor.");
            string[] expected = { "MainMenu", "Green", "Autumn", "Winter" };
            var actual = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (!actual.SequenceEqual(expected.Select(n => "Assets/Game/Scenes/" + n + ".unity")))
                throw new InvalidOperationException("Formal scene list changed; P0 will not rewrite it.");
            EditorSceneManager.OpenScene("Assets/Game/Scenes/MainMenu.unity", OpenSceneMode.Single);
            if (OwnedGui)
            {
                PrepareGameView();
                Application.runInBackground = true;
            }
            EditorApplication.EnterPlaymode();
        }
        private static Button Button(string name) => UnityEngine.Object.FindObjectsByType<Button>(FindObjectsSortMode.None)
            .Single(b => b.name == name && b.gameObject.activeInHierarchy);
        private static ProfilerRecorder Counter(ProfilerCategory category, string name)
        {
            try { return ProfilerRecorder.StartNew(category, name, 1); }
            catch { return default; }
        }
        private static double Ms(ProfilerRecorder recorder) => recorder.Valid ? recorder.LastValue / 1000000.0 : -1;

        private static void Tick()
        {
            if (finishing) return;
            try
            {
                if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("P0 observer deadline exceeded.");
                if (mode == "functional")
                {
                    string path = Path.Combine(output, "observations/receipt.json");
                    if (File.Exists(path))
                    {
                        var receipt = JsonUtility.FromJson<FunctionalReceipt>(File.ReadAllText(path));
                        finishing = true; EditorApplication.update -= Tick; EditorApplication.Exit(receipt.passed ? 0 : 1);
                    }
                    return;
                }
                if (!EditorApplication.isPlaying || Time.frameCount == lastFrame) return;
                lastFrame = Time.frameCount;
                var session = WarSandboxSceneSession.Instance;
                if (session == null || EditorApplication.timeSinceStartup < nextStep) return;
                if (stage == 0)
                {
                    // Editor realtime already includes import/startup; wait from Play/session readiness instead.
                    if (nextStep == 0) { nextStep = EditorApplication.timeSinceStartup + 3; return; }
                    Button("home-play").onClick.Invoke(); stage = 1; nextStep = EditorApplication.timeSinceStartup + .4; return;
                }
                if (stage == 1) { Button("card-0").onClick.Invoke(); stage = 2; nextStep = EditorApplication.timeSinceStartup + .4; return; }
                if (stage == 2) { Button("card-0-enter").onClick.Invoke(); stage = 3; nextStep = EditorApplication.timeSinceStartup + .4; return; }
                if (stage == 3)
                {
                    if (session.IsLoading || session.Controller == null) return;
                    var d = session.Controller.GetComponent<WarSandboxRuntimeDeployment>();
                    if (d == null || !d.IsEditing) return;
                    if (d.Draft.Snapshot().Sum(e => e.count) != 2048) throw new InvalidOperationException("Default 2048-person deployment changed.");
                    Button("deployment-start").onClick.Invoke(); stage = 4; return;
                }
                var controller = session.Controller;
                if (controller == null) throw new InvalidOperationException("Controller disappeared.");
                if (stage == 4)
                {
                    if (controller.Phase != WarSandboxBattlePhase.Running) return;
                    if (controller.manager.Buffers.AgentCount != 2048) throw new InvalidOperationException("Unexpected GPU population.");
                    mainThread = Counter(ProfilerCategory.Internal, "Main Thread");
                    renderThread = Counter(ProfilerCategory.Internal, "Render Thread");
                    gc = Counter(ProfilerCategory.Memory, "GC Allocated In Frame");
                    memoryBefore = Profiler.GetTotalAllocatedMemoryLong(); runningSince = Time.realtimeSinceStartup; stage = 5; return;
                }
                float elapsed = Time.realtimeSinceStartup - runningSince;
                if (controller.Phase != WarSandboxBattlePhase.Running) throw new InvalidOperationException("Battle ended or paused before sampling completed.");
                if (OwnedGui)
                {
                    if (measurementComplete) FinishPerformance(null);
                    else if (elapsed > 45) throw new InvalidOperationException("No completed rendered Game-camera sampling window.");
                }
                else
                {
                    // Retain diagnostic batch collection, but never qualify it as rendered performance.
                    if (elapsed >= 10) RecordFrame(elapsed, null);
                    if (elapsed >= 30) FinishPerformance(null);
                }
            }
            catch (Exception e)
            {
                if (mode == "performance") FinishPerformance(e.ToString());
                else { finishing = true; File.WriteAllText(Path.Combine(output, "observer-error.txt"), e.ToString()); EditorApplication.Exit(1); }
            }
        }
        private static Type EditorType(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name)).FirstOrDefault(t => t != null);
        private static void PrepareGameView()
        {
            Type viewType = EditorType("UnityEditor.GameView"), sizesType = EditorType("UnityEditor.GameViewSizes");
            if (viewType == null || sizesType == null) throw new InvalidOperationException("Game View API unavailable.");
            object sizes = sizesType.GetProperty("instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.FlattenHierarchy).GetValue(null);
            var groupMethod = sizesType.GetMethod("GetGroup");
            object group = groupMethod.Invoke(sizes, new[] { Enum.Parse(groupMethod.GetParameters()[0].ParameterType, "Standalone") });
            Type groupType = group.GetType(); int count = (int)groupType.GetMethod("GetTotalCount").Invoke(group, null), match = -1;
            for (int i = 0; i < count; i++)
            {
                object size = groupType.GetMethod("GetGameViewSize").Invoke(group, new object[] { i }); Type t = size.GetType();
                if ((int)t.GetProperty("width").GetValue(size) == 1920 && (int)t.GetProperty("height").GetValue(size) == 1080) { match = i; break; }
            }
            if (match < 0) throw new InvalidOperationException("No existing 1920x1080 preset; user resolution presets were not modified.");
            var view = EditorWindow.GetWindow(viewType);
            var selection = viewType.GetProperty("selectedSizeIndex", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            SessionState.SetInt("InteractionP0.oldGameSize", (int)selection.GetValue(view));
            SessionState.SetBool("InteractionP0.oldMaximized", view.maximized);
            SessionState.SetBool("InteractionP0.gameViewSaved", true);
            selection.SetValue(view, match); view.Show(); view.maximized = true; view.Focus(); view.Repaint();
        }
        private static void ConfigureGpuProfiler()
        {
            if (!Environment.GetCommandLineArgs().Contains("--interaction-p0-gpu-profile")) return;
            try
            {
                profilerDriver = EditorType("UnityEditorInternal.ProfilerDriver");
                var setArea = profilerDriver.GetMethod("SetAreaEnabled", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                var getArea = profilerDriver.GetMethod("GetAreaEnabled", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                object gpu = Enum.Parse(setArea.GetParameters()[0].ParameterType, "GPU");
                if (!SessionState.GetBool("InteractionP0.profilerSaved", false))
                {
                    SessionState.SetBool("InteractionP0.profilerEnabled", Profiler.enabled);
                    SessionState.SetBool("InteractionP0.gpuEnabled", (bool)getArea.Invoke(null, new[] { gpu }));
                    SessionState.SetBool("InteractionP0.profilerSaved", true);
                }
                setArea.Invoke(null, new[] { gpu, (object)true }); Profiler.enabled = true;
                getRawFrame = profilerDriver.GetMethod("GetRawFrameDataView", BindingFlags.Public | BindingFlags.Static);
                lastProfilerFrame = profilerDriver.GetProperty("lastFrameIndex", BindingFlags.Public | BindingFlags.Static);
                profilerSetup = getRawFrame != null && lastProfilerFrame != null ? "GPU profiler enabled, no deep profiling requested" : "GPU frame API unavailable";
            }
            catch (Exception e) { profilerSetup = e.GetType().Name + ": " + e.Message; }
        }
        private static double ProfiledGpuMs()
        {
            if (getRawFrame == null || lastProfilerFrame == null) return -1;
            object view = null;
            try
            {
                int frame = (int)lastProfilerFrame.GetValue(null) - 4; // Allow asynchronous GPU timing to arrive.
                if (frame < 0) return -1;
                view = getRawFrame.Invoke(null, new object[] { frame, 0 }); Type type = view.GetType();
                if (!(bool)type.GetProperty("valid").GetValue(view)) return -1;
                var property = type.GetProperty("frameGpuTimeMs");
                return property != null ? Convert.ToDouble(property.GetValue(view)) : -1;
            }
            catch { return -1; }
            finally { (view as IDisposable)?.Dispose(); }
        }
        private static void Rendered(ScriptableRenderContext context, List<Camera> cameras)
        {
            if (finishing || stage != 5 || !EditorApplication.isPlaying || Time.frameCount == lastRendered) return;
            var camera = cameras.FirstOrDefault(c => c != null && c.cameraType == CameraType.Game);
            if (camera == null) return;
            lastRendered = Time.frameCount;
            float elapsed = Time.realtimeSinceStartup - runningSince;
            if (elapsed < 10 || measurementComplete) return;
            RecordFrame(elapsed, camera);
            if (elapsed >= 30) measurementComplete = true;
        }
        private static void RecordFrame(float elapsed, Camera camera)
        {
            FrameTimingManager.CaptureFrameTimings();
            uint count = FrameTimingManager.GetLatestTimings(1, timing);
            double gpu = count > 0 ? timing[0].gpuFrameTime : -1;
            if (gpu <= 0)
            {
                gpu = ProfiledGpuMs();
                if (gpu > 0) gpuTimingSource = "Profiler frameGpuTimeMs, latest completed frame minus 4; profiling overhead included";
            }
            if (camera != null)
            {
                if (renderedSamples == 0) { cameraStart = camera.transform.position; cameraRotationStart = camera.transform.rotation; }
                cameraEnd = camera.transform.position; cameraRotationEnd = camera.transform.rotation; renderedSamples++;
            }
            rows.Add(new Row { frame = Time.frameCount, seconds = elapsed, frameMs = Time.unscaledDeltaTime * 1000.0,
                mainThreadMs = Ms(mainThread), renderThreadMs = Ms(renderThread), cpuTimingMs = count > 0 ? timing[0].cpuFrameTime : -1,
                gpuTimingMs = gpu, gcBytes = gc.Valid ? gc.LastValue : -1 });
        }
        private static void RestoreOwnedEditorState()
        {
            RenderPipelineManager.endContextRendering -= Rendered;
            if (SessionState.GetBool("InteractionP0.profilerSaved", false))
            {
                var driver = EditorType("UnityEditorInternal.ProfilerDriver");
                var setter = driver.GetMethod("SetAreaEnabled", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                setter.Invoke(null, new[] { Enum.Parse(setter.GetParameters()[0].ParameterType, "GPU"), (object)SessionState.GetBool("InteractionP0.gpuEnabled", false) });
                Profiler.enabled = SessionState.GetBool("InteractionP0.profilerEnabled", false);
                SessionState.SetBool("InteractionP0.profilerSaved", false);
            }
            if (SessionState.GetBool("InteractionP0.gameViewSaved", false))
            {
                Type type = EditorType("UnityEditor.GameView"); var view = EditorWindow.GetWindow(type);
                view.maximized = SessionState.GetBool("InteractionP0.oldMaximized", false);
                type.GetProperty("selectedSizeIndex", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(view, SessionState.GetInt("InteractionP0.oldGameSize", 0));
                SessionState.SetBool("InteractionP0.gameViewSaved", false);
            }
        }
        private static double Quantile(IEnumerable<double> source, double q)
        {
            var data = source.Where(v => v >= 0 && !double.IsNaN(v) && !double.IsInfinity(v)).OrderBy(v => v).ToArray();
            return data.Length == 0 ? -1 : data[Math.Min(data.Length - 1, (int)Math.Ceiling(q * data.Length) - 1)];
        }
        private static void FinishPerformance(string error)
        {
            if (finishing) return;
            finishing = true; EditorApplication.update -= Tick;
            try
            {
                var controller = WarSandboxSceneSession.Instance != null ? WarSandboxSceneSession.Instance.Controller : null;
                var report = new PerfReceipt {
                    passed = error == null && rows.Count >= 100 && OwnedGui && renderedSamples >= 100 && Screen.width == 1920 && Screen.height == 1080 &&
                        Vector3.Distance(cameraStart, cameraEnd) < .001f && Quaternion.Angle(cameraRotationStart, cameraRotationEnd) < .01f,
                    renderedEditor = OwnedGui, renderedGameSamples = renderedSamples, cameraStart = cameraStart, cameraEnd = cameraEnd,
                    cameraFixed = renderedSamples > 0 && Vector3.Distance(cameraStart, cameraEnd) < .001f && Quaternion.Angle(cameraRotationStart, cameraRotationEnd) < .01f,
                    gpuProfilingRequested = Environment.GetCommandLineArgs().Contains("--interaction-p0-gpu-profile"),
                    gpuTimingSource = gpuTimingSource, profilerSetup = profilerSetup, vSyncCount = QualitySettings.vSyncCount, targetFrameRate = Application.targetFrameRate,
                    error = error, unity = Application.unityVersion,
                    gpu = SystemInfo.graphicsDeviceName, graphicsApi = SystemInfo.graphicsDeviceType.ToString(), graphicsMemoryMB = SystemInfo.graphicsMemorySize,
                    scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().path,
                    units = controller != null ? controller.manager.Buffers.AgentCount : 0, width = Screen.width, height = Screen.height, frames = rows.Count,
                    mainThreadCounterAvailable = mainThread.Valid, renderThreadCounterAvailable = renderThread.Valid, gcCounterAvailable = gc.Valid,
                    gpuSamples = rows.Count(r => r.gpuTimingMs > 0), gpuTimingAvailable = rows.Any(r => r.gpuTimingMs > 0),
                    frameMedianMs = Quantile(rows.Select(r => r.frameMs), .5), frameP95Ms = Quantile(rows.Select(r => r.frameMs), .95),
                    mainThreadMedianMs = Quantile(rows.Select(r => r.mainThreadMs), .5), mainThreadP95Ms = Quantile(rows.Select(r => r.mainThreadMs), .95),
                    gpuMedianMs = Quantile(rows.Where(r => r.gpuTimingMs > 0).Select(r => r.gpuTimingMs), .5),
                    gpuP95Ms = Quantile(rows.Where(r => r.gpuTimingMs > 0).Select(r => r.gpuTimingMs), .95),
                    gcBytesPerSample = gc.Valid && rows.Count > 0 ? rows.Average(r => (double)r.gcBytes) : -1,
                    unityAllocatedBefore = memoryBefore, unityAllocatedAfter = Profiler.GetTotalAllocatedMemoryLong(),
                    unityReservedAfter = Profiler.GetTotalReservedMemoryLong(), managedUsedAfter = Profiler.GetMonoUsedSizeLong(),
                    graphicsDriverAllocatedAfter = Profiler.GetAllocatedMemoryForGraphicsDriver(),
                    timingNote = "Rendered Game-camera frames only in owned GUI mode. 1920x1080; 10s warmup + 20s samples. Latest CPU/GPU samples can lag; GPU profiling overhead is included when requested. Nonpositive GPU timings are unavailable, not zero-cost. Batch collections are NOT qualified performance."
                };
                using (var writer = new StreamWriter(Path.Combine(output, "frames.csv")))
                {
                    writer.WriteLine("frame,seconds,frameMs,mainThreadMs,renderThreadMs,cpuTimingMs,gpuTimingMs,gcBytes");
                    foreach (var r in rows) writer.WriteLine(string.Join(",", r.frame.ToString(Inv), r.seconds.ToString(Inv), r.frameMs.ToString(Inv), r.mainThreadMs.ToString(Inv), r.renderThreadMs.ToString(Inv), r.cpuTimingMs.ToString(Inv), r.gpuTimingMs.ToString(Inv), r.gcBytes.ToString(Inv)));
                }
                File.WriteAllText(Path.Combine(output, "performance.json"), JsonUtility.ToJson(report, true));
                if (mainThread.Valid) mainThread.Dispose(); if (renderThread.Valid) renderThread.Dispose(); if (gc.Valid) gc.Dispose();
                RestoreOwnedEditorState();
                EditorApplication.Exit(report.passed ? 0 : 1);
            }
            catch (Exception e) { File.WriteAllText(Path.Combine(output, "observer-error.txt"), e.ToString()); EditorApplication.Exit(1); }
        }
    }
}
#endif

