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
    /// <summary>Isolated M6.1 viewer and opt-in budget probe. Not a battle simulation.</summary>
    public sealed class WarSandboxTerrainPrototype : MonoBehaviour
    {
        public TerrainSurfaceAsset terrain;
        public ComputeShader samplingShader;
        public Mesh surfaceMesh;
        private TerrainSurface surface;
        private TerrainSurfaceGpu gpu;
        private ComputeShader shader;
        private ComputeBuffer queries, samples, velocities;
        private RenderTexture renderTarget;
        private CommandBuffer renderCommands;
        private int queryCount;
        private string output, stage = "viewer";
        private bool benchmarking;
        private readonly List<Measurement> measurements = new List<Measurement>();
        private readonly List<string> errors = new List<string>();

        [Serializable]
        private sealed class Measurement
        {
            public string variant;
            public int count, round, frames;
            public float meanFrameMs, p95FrameMs, syncBatchMsPerDispatch;
            public long queryBufferBytes;
        }

        [Serializable]
        private sealed class Report
        {
            public bool passed, resourcesReleased, developmentBuild;
            public string error, method, cpu, gpu, api, unity, os, buildGuid;
            public int gpuMemoryMB, width, height, terrainVersion, walkableCells, blockedCells;
            public int vSync, targetFrameRate, quality;
            public int renderedTerrainPixels;
            public string terrainId;
            public long terrainBufferBytes, meshBufferBytes;
            public Measurement[] measurements;
        }

        private void Start()
        {
            benchmarking = Array.IndexOf(Environment.GetCommandLineArgs(), "--terrain-benchmark") >= 0;
            try
            {
                if (terrain == null || !terrain.TryCreateSurface(out surface, out _)) throw new InvalidOperationException("Invalid terrain asset.");
                if (!SystemInfo.supportsComputeShaders || samplingShader == null) throw new InvalidOperationException("A compute-capable graphics device is required.");
                shader = Instantiate(samplingShader);
                gpu = new TerrainSurfaceGpu(surface);
                Application.runInBackground = true;
                if (!benchmarking) return;
                output = Path.GetFullPath(Argument("--terrain-output=") ?? Path.Combine(Application.persistentDataPath, "TerrainPrototype"));
                Directory.CreateDirectory(output);
                QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1;
                Application.logMessageReceived += TrackError;
                StartCoroutine(Guarded(Benchmark()));
            }
            catch (Exception ex)
            {
                if (benchmarking) Finish(false, ex.ToString());
                else { Release(); Debug.LogException(ex); enabled = false; }
            }
        }

        private IEnumerator Benchmark()
        {
            foreach (var camera in FindObjectsByType<MyCameraManager>(FindObjectsSortMode.None)) camera.LockInput();
            yield return null;
            PrepareOffscreenRender();
            Graphics.ExecuteCommandBuffer(renderCommands);
            CaptureTerrain();
            foreach (int count in new[] { 100000, 110000 })
            {
                ReleaseQueries();
                queryCount = count;
                queries = new ComputeBuffer(count, 16); samples = new ComputeBuffer(count, 16); velocities = new ComputeBuffer(count, 16);
                var data = new Vector4[count];
                var random = new System.Random(6101);
                for (int i = 0; i < count; i++)
                    data[i] = new Vector4(-360 + (float)random.NextDouble() * 720, -360 + (float)random.NextDouble() * 720,
                        (float)random.NextDouble() * 6 - 3, (float)random.NextDouble() * 6 - 3);
                queries.SetData(data);
                int flat = shader.FindKernel("FlatBaseline"), terrainKernel = shader.FindKernel("SampleSurface");
                shader.SetInt("_QueryCount", count);
                Bind(flat); Bind(terrainKernel); gpu.Bind(shader, terrainKernel);
                Dispatch(terrainKernel);
                var readback = new Vector4[count]; samples.GetData(readback);
                for (int i = 0; i < count; i += 23)
                {
                    Require(surface.TrySample(new Vector2(data[i].x, data[i].y), out var expected), "Unexpected query outside surface.");
                    Require(Mathf.Abs(expected.Position.y - readback[i].x) < .002f &&
                        Mathf.Abs(expected.Gradient.x - readback[i].y) < .002f &&
                        Mathf.Abs(expected.Gradient.y - readback[i].z) < .002f &&
                        readback[i].w == (expected.Walkable ? 1 : 0), "CPU/GPU terrain disagreement at query " + i);
                }
                for (int round = 0; round < 4; round++)
                {
                    bool projected = round == 1 || round == 2;
                    int kernel = projected ? terrainKernel : flat;
                    string variant = projected ? "surface" : "flat";
                    stage = count + " / " + variant + " / " + round;
                    for (int frame = 0; frame < 120; frame++) { Graphics.ExecuteCommandBuffer(renderCommands); Dispatch(kernel); yield return null; }
                    var frames = new List<float>(360);
                    double previous = Time.realtimeSinceStartupAsDouble;
                    for (int frame = 0; frame < 360; frame++)
                    {
                        Graphics.ExecuteCommandBuffer(renderCommands); Dispatch(kernel); yield return null;
                        double now = Time.realtimeSinceStartupAsDouble;
                        frames.Add((float)((now - previous) * 1000)); previous = now;
                        Require(errors.Count == 0, string.Join("\n", errors));
                    }
                    // Separate throughput measurement with a synchronization point. This is NOT a GPU timestamp.
                    var syncTimes = new List<float>(9);
                    var one = new Vector4[1];
                    for (int batch = 0; batch < 9; batch++)
                    {
                        samples.GetData(one, 0, 0, 1);
                        var timer = System.Diagnostics.Stopwatch.StartNew();
                        for (int dispatch = 0; dispatch < 64; dispatch++) Dispatch(kernel);
                        samples.GetData(one, 0, 0, 1);
                        timer.Stop(); syncTimes.Add((float)timer.Elapsed.TotalMilliseconds / 64);
                    }
                    var csv = new StringBuilder("frame,wall_ms\n");
                    float total = 0;
                    for (int i = 0; i < frames.Count; i++)
                    { total += frames[i]; csv.Append(i).Append(',').Append(frames[i].ToString("F6", CultureInfo.InvariantCulture)).Append('\n'); }
                    File.WriteAllText(Path.Combine(output, count + "-" + round + "-" + variant + ".csv"), csv.ToString());
                    File.WriteAllText(Path.Combine(output, count + "-" + round + "-" + variant + "-sync.csv"),
                        "amortized_ms\n" + string.Join("\n", syncTimes.ConvertAll(ms => ms.ToString("F6", CultureInfo.InvariantCulture))));
                    frames.Sort(); syncTimes.Sort();
                    measurements.Add(new Measurement
                    {
                        count = count, round = round, variant = variant, frames = frames.Count,
                        meanFrameMs = total / frames.Count, p95FrameMs = frames[Mathf.CeilToInt(frames.Count * .95f) - 1],
                        syncBatchMsPerDispatch = syncTimes[4], queryBufferBytes = count * 48L
                    });
                    Debug.Log("TERRAIN_BUDGET " + JsonUtility.ToJson(measurements[measurements.Count - 1]));
                    yield return null;
                }
            }
            Require(File.Exists(Path.Combine(output, "terrain.png")), "Terrain screenshot was not written.");
        }

        private void Bind(int kernel)
        { shader.SetBuffer(kernel, "_Queries", queries); shader.SetBuffer(kernel, "_Samples", samples); shader.SetBuffer(kernel, "_Velocities", velocities); }
        private void Dispatch(int kernel) => shader.Dispatch(kernel, (queryCount + 63) / 64, 1, 1);
        private void ReleaseQueries()
        { queries?.Release(); samples?.Release(); velocities?.Release(); queries = samples = velocities = null; }
        private void Release()
        {
            ReleaseQueries(); gpu?.Dispose(); if (shader != null) Destroy(shader); shader = null;
            renderCommands?.Release(); renderCommands = null;
            if (renderTarget != null) { renderTarget.Release(); Destroy(renderTarget); renderTarget = null; }
        }
        private void OnDestroy() { Application.logMessageReceived -= TrackError; Release(); }
        private void TrackError(string message, string stack, LogType type)
        { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message); }
        private static void Require(bool condition, string error) { if (!condition) throw new InvalidOperationException(error); }
        private static string Argument(string prefix)
        { foreach (string arg in Environment.GetCommandLineArgs()) if (arg.StartsWith(prefix, StringComparison.Ordinal)) return arg.Substring(prefix.Length); return null; }

        private IEnumerator Guarded(IEnumerator routine)
        {
            string failure = null;
            while (true)
            {
                bool moved = false; object next = null;
                try { moved = routine.MoveNext(); if (moved) next = routine.Current; }
                catch (Exception ex) { failure = ex.ToString(); }
                if (!moved || failure != null) break;
                yield return next;
            }
            (routine as IDisposable)?.Dispose();
            Finish(failure == null && errors.Count == 0, failure ?? string.Join("\n", errors));
        }

        private void Finish(bool passed, string error)
        {
            int exitCode = passed ? 0 : 1;
            try
            {
                int passable = 0;
                if (surface != null)
                    for (int z = 0; z < surface.Depth - 1; z++)
                        for (int x = 0; x < surface.Width - 1; x++) if (surface.IsCellWalkable(x, z)) passable++;
                long meshBytes = 0;
                if (surfaceMesh != null)
                {
                    for (int i = 0; i < surfaceMesh.vertexBufferCount; i++) meshBytes += (long)surfaceMesh.GetVertexBufferStride(i) * surfaceMesh.vertexCount;
                    meshBytes += surfaceMesh.GetIndexCount(0) * 4L;
                }
                Release();
                var report = new Report
                {
                    passed = passed, error = error, resourcesReleased = (gpu == null || !gpu.IsAllocated) && queries == null && renderTarget == null && renderCommands == null,
                    cpu = SystemInfo.processorType, gpu = SystemInfo.graphicsDeviceName, gpuMemoryMB = SystemInfo.graphicsMemorySize,
                    api = SystemInfo.graphicsDeviceVersion, unity = Application.unityVersion, os = SystemInfo.operatingSystem,
                    buildGuid = Application.buildGUID, width = Screen.width, height = Screen.height,
                    developmentBuild = Debug.isDebugBuild, vSync = QualitySettings.vSyncCount,
                    targetFrameRate = Application.targetFrameRate, quality = QualitySettings.GetQualityLevel(),
                    terrainId = surface?.Id, terrainVersion = surface?.Version ?? 0,
                    terrainBufferBytes = surface?.GpuBytes ?? 0, meshBufferBytes = meshBytes,
                    renderedTerrainPixels = renderedTerrainPixels,
                    walkableCells = passable, blockedCells = (surface?.CellCount ?? 0) - passable,
                    method = "Isolated Development Player, hidden window, explicit 1280x720 offscreen mesh draw every measured frame, fixed camera/mesh, vSync off, uncapped. Color+depth render target is shared by both variants and excluded from terrain data bytes. 100k/110k sampling queries (NOT battle agents). ABBA flat/surface/surface/flat, 120 warmup + 360 measured frames each. Separate median of 9 batches of 64 dispatches + synchronized 1-record readback; includes CPU submission/driver/sync, not GPU timestamp. Raw wall frames untrimmed. Excludes battle, pathfinding, rendering units, presentation and full integration costs. Nonempty/nonmagenta GPU image, CPU/GPU sample parity and buffer release required; performance verdict is recorded separately.",
                    measurements = measurements.ToArray()
                };
                if (output != null) File.WriteAllText(Path.Combine(output, "budget.json"), JsonUtility.ToJson(report, true));
                Debug.Log("TERRAIN_PROTOTYPE " + (passed ? "PASS" : "FAIL") + " " + error);
            }
            catch (Exception ex) { exitCode = 1; Debug.LogException(ex); }
            finally { Application.logMessageReceived -= TrackError; Release(); Application.Quit(exitCode); }
        }

        // Same text as before, drawn in the shared B "tactical command" palette (IMGUI: this scene has no uGUI canvas).
        private GUIStyle codeStyle, bodyStyle;
        private void OnGUI()
        {
            if (surface == null) return;
            if (codeStyle == null)
            {
                Font mono = Font.CreateDynamicFontFromOSFont(new[] { "Consolas", "Cascadia Mono", "Courier New" }, 13);
                codeStyle = new GUIStyle(GUI.skin.label) { font = mono, fontSize = 13, fontStyle = FontStyle.Bold };
                codeStyle.normal.textColor = WarSandboxUGUI.Accent;
                bodyStyle = new GUIStyle(GUI.skin.label) { fontSize = 13 };
                bodyStyle.normal.textColor = WarSandboxUGUI.Ink;
            }
            var panel = new Rect(16, 16, 490, 110);
            Fill(panel, WarSandboxUGUI.Surface);
            Fill(new Rect(panel.x, panel.y, panel.width, 1), WarSandboxUGUI.Line); Fill(new Rect(panel.x, panel.yMax - 1, panel.width, 1), WarSandboxUGUI.Line);
            Fill(new Rect(panel.x, panel.y, 1, panel.height), WarSandboxUGUI.Line); Fill(new Rect(panel.xMax - 1, panel.y, 1, panel.height), WarSandboxUGUI.Line);
            const float l = 12, t = 2;
            foreach (var corner in new[] { new Vector2(panel.x - 1, panel.y - 1), new Vector2(panel.xMax + 1 - l, panel.y - 1), new Vector2(panel.x - 1, panel.yMax + 1 - t), new Vector2(panel.xMax + 1 - l, panel.yMax + 1 - t) })
                Fill(new Rect(corner.x, corner.y, l, t), WarSandboxUGUI.Accent);
            foreach (var corner in new[] { new Vector2(panel.x - 1, panel.y - 1), new Vector2(panel.xMax + 1 - t, panel.y - 1), new Vector2(panel.x - 1, panel.yMax + 1 - l), new Vector2(panel.xMax + 1 - t, panel.yMax + 1 - l) })
                Fill(new Rect(corner.x, corner.y, t, l), WarSandboxUGUI.Accent);
            GUI.Label(new Rect(30, 20, 460, 22), "M6.1  //  Continuous terrain prototype", codeStyle);
            GUI.Label(new Rect(30, 44, 460, 22), "48m plateau / west ramp / narrow east ramp / canyon", bodyStyle);
            bodyStyle.normal.textColor = WarSandboxUGUI.Muted;
            GUI.Label(new Rect(30, 66, 460, 22), "Red: steep or excluded. Green/yellow: passable surface.", bodyStyle);
            bodyStyle.normal.textColor = benchmarking ? WarSandboxUGUI.Amber : WarSandboxUGUI.Muted;
            GUI.Label(new Rect(30, 88, 460, 22), benchmarking ? "Budget probe: " + stage : "RMB + WASD: fly / wheel: zoom. Battle integration: M6.2.", bodyStyle);
            bodyStyle.normal.textColor = WarSandboxUGUI.Ink;
        }
        private static void Fill(Rect rect, Color color)
        {
            Color before = GUI.color; GUI.color = color; GUI.DrawTexture(rect, Texture2D.whiteTexture); GUI.color = before;
        }

        private int renderedTerrainPixels;

        private void PrepareOffscreenRender()
        {
            // Hidden/minimized Windows players can skip screen rendering. Submit an actual draw explicitly.
            var camera = Camera.main;
            var filter = FindFirstObjectByType<MeshFilter>();
            var renderer = filter != null ? filter.GetComponent<MeshRenderer>() : null;
            Require(camera != null && filter != null && filter.sharedMesh == surfaceMesh && renderer != null && renderer.sharedMaterial != null,
                "Missing prototype camera, mesh or material.");
            renderTarget = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Require(renderTarget.Create(), "Cannot allocate prototype render target.");
            renderCommands = new CommandBuffer { name = "M6.1 explicit terrain draw" };
            renderCommands.SetRenderTarget(renderTarget);
            renderCommands.ClearRenderTarget(true, true, Color.clear);
            renderCommands.SetViewProjectionMatrices(camera.worldToCameraMatrix, GL.GetGPUProjectionMatrix(camera.projectionMatrix, true));
            renderCommands.SetViewport(new Rect(0, 0, renderTarget.width, renderTarget.height));
            // A raw draw has no camera stage to compensate the RT projection's Y flip.
            renderCommands.SetInvertCulling(SystemInfo.graphicsUVStartsAtTop);
            renderCommands.DrawMesh(surfaceMesh, filter.transform.localToWorldMatrix, renderer.sharedMaterial, 0, 0);
            renderCommands.SetInvertCulling(false);
        }

        private void CaptureTerrain()
        {
            var previous = RenderTexture.active;
            var image = new Texture2D(renderTarget.width, renderTarget.height, TextureFormat.RGBA32, false);
            try
            {
                RenderTexture.active = renderTarget;
                image.ReadPixels(new Rect(0, 0, renderTarget.width, renderTarget.height), 0, 0);
                image.Apply();
                var pixels = image.GetPixels32();
                if (SystemInfo.graphicsUVStartsAtTop)
                    for (int y = 0; y < renderTarget.height / 2; y++)
                        for (int x = 0; x < renderTarget.width; x++)
                        {
                            int a = y * renderTarget.width + x, b = (renderTarget.height - 1 - y) * renderTarget.width + x;
                            var pixel = pixels[a]; pixels[a] = pixels[b]; pixels[b] = pixel;
                        }
                image.SetPixels32(pixels); image.Apply();
                int magenta = 0;
                foreach (var pixel in pixels)
                {
                    if (pixel.a < 128) continue;
                    renderedTerrainPixels++;
                    if (pixel.r > 200 && pixel.b > 200 && pixel.g < 75) magenta++;
                }
                File.WriteAllBytes(Path.Combine(output, "terrain.png"), image.EncodeToPNG());
                Require(renderedTerrainPixels > pixels.Length / 50 && magenta < renderedTerrainPixels / 10,
                    "Terrain GPU image is empty or contains the error shader.");
            }
            finally { RenderTexture.active = previous; Destroy(image); }
        }
    }
}
