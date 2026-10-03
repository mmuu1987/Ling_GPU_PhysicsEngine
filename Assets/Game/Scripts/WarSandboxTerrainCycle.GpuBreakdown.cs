#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using MassEngine.Projectiles;
using UnityEngine;

namespace MassEngine.Game
{
    public sealed partial class WarSandboxTerrainCycle
    {
        [Serializable] private sealed class GpuDrawBatch
        {
            public int unitType, lod, instances, verticesPerMesh;
            public long indicesPerMesh, vertexInvocations, triangles;
            public string mesh, shader;
        }
        [Serializable] private sealed class GpuWorkloadSnapshot
        {
            public string window, boundary, workload;
            public bool computeEnabled, worldVisible, frozenDraw;
            public float simulationSeconds;
            public int totalUnits, visibleUnits, activeProjectiles;
            public long visibleVertexInvocations, visibleTriangles;
            public List<GpuDrawBatch> batches = new List<GpuDrawBatch>();
        }

        private bool IsGpuBreakdown => report.phase == "gpu-breakdown";
        private string gpuWorkload = "full";
        private bool drawFrozenGpuSnapshot;
        private MassGpuRenderDispatcher frozenAgentRenderer;
        private ProjectileGpuRenderDispatcher frozenProjectileRenderer;
        private Bounds frozenRenderBounds;
        private Vector4 frozenCorpseSink;

        private IEnumerator RunGpuBreakdown()
        {
            // Complete normal path bookends the ablations. Each visit starts with the
            // same authored 100k scene; every battle warms up with the full path first.
            string[] modes = { "full", "compute-only", "frozen-draw", "empty", "full" };
            for (int visit = 0; visit < modes.Length; visit++)
            {
                string mode = modes[visit];
                Stage("gpu-breakdown-" + visit + "-" + mode);
                yield return Enter("launch-standard", false);
                Require(manager.UnitTypes.TotalAgentCount == 100000 && manager.enableGpuDispatch,
                    "GPU breakdown requires the unmodified 100k world.");
                Require(manager.systemConfig.simulationConfig != null && manager.systemConfig.lodConfig != null,
                    "GPU breakdown needs the authored simulation/LOD configuration.");
                foreach (var camera in FindObjectsByType<Camera>(FindObjectsSortMode.None))
                    Require(camera == renderCamera || !camera.isActiveAndEnabled || camera.cullingMask == 0,
                        "Another camera can draw the world and invalidate the masked-world diagnostic.");
                yield return new WaitForSecondsRealtime(3);
                yield return MeasureGpuWorkload(mode, "v" + visit + "-" + mode + "-setup", 8);
                yield return Click("start");
                float deadline = Time.realtimeSinceStartup + 180;
                while (controller.TelemetrySnapshot.battleSeconds < 20 && Time.realtimeSinceStartup < deadline)
                {
                    Require(manager.IsBattleRunning && !controller.BattleResult.valid, "GPU diagnostic warmup stopped early.");
                    yield return null;
                }
                Require(controller.TelemetrySnapshot.battleSeconds >= 20 && controller.SimulationSpeed == 1 && Time.timeScale == 1,
                    "GPU diagnostic did not warm up at normal battle speed.");
                yield return MeasureGpuWorkload(mode, "v" + visit + "-" + mode + "-battle", 12);
                Require(manager.enableGpuDispatch && !drawFrozenGpuSnapshot && renderCamera.cullingMask != 0,
                    "GPU diagnostic did not restore the full gameplay path.");
                yield return Leave(false);
                VerifySources(); report.release.rounds++;
            }
        }

        private IEnumerator MeasureGpuWorkload(string mode, string window, float seconds)
        {
            bool previousDispatch = manager.enableGpuDispatch;
            int previousMask = renderCamera.cullingMask;
            try
            {
                gpuWorkload = mode;
                bool frozen = mode == "frozen-draw" || mode == "empty";
                bool hiddenWorld = mode == "compute-only" || mode == "empty";
                if (frozen)
                {
                    // Cache the manager's existing renderers, including their configured
                    // projectile args. A new projectile renderer would reset its count.
                    frozenAgentRenderer = ReadManagerField<MassGpuRenderDispatcher>("renderDispatcher");
                    frozenProjectileRenderer = ReadManagerField<ProjectileGpuRenderDispatcher>("projectileRenderDispatcher");
                    Vector2 world = manager.systemConfig.simulationConfig.simulationWorldSize;
                    frozenRenderBounds = new Bounds(Vector3.zero, new Vector3(world.x + 40, 120, world.y + 40));
                    var lod = manager.systemConfig.lodConfig;
                    frozenCorpseSink = new Vector4(Mathf.Max(0, lod.corpseLingerSeconds),
                        Mathf.Max(0, lod.corpseSinkSeconds), Mathf.Max(0, lod.corpseSinkDepth), 0);
                    manager.enableGpuDispatch = false;
                    drawFrozenGpuSnapshot = mode == "frozen-draw";
                }
                if (hiddenWorld) renderCamera.cullingMask = 0;
                RecordGpuWorkload(window, "before"); // Synchronous count reads are outside timing.
                yield return new WaitForSecondsRealtime(2); // Drain the previous mode's GPU timing queue.
                yield return Measure("launch-standard", window, seconds);
                RecordGpuWorkload(window, "after");
                yield return CaptureReleaseFrame(window + ".png");
            }
            finally
            {
                drawFrozenGpuSnapshot = false;
                frozenAgentRenderer = null; frozenProjectileRenderer = null;
                if (manager != null) manager.enableGpuDispatch = previousDispatch;
                if (renderCamera != null) renderCamera.cullingMask = previousMask;
                gpuWorkload = "full";
            }
        }

        private T ReadManagerField<T>(string name) where T : class
        {
            // Diagnostic-only reflection once at a window boundary, never per frame.
            var field = typeof(MassEngineManager).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            var value = field?.GetValue(manager) as T;
            Require(value != null, "Missing manager diagnostic field: " + name);
            return value;
        }

        private void DrawFrozenGpuSnapshot()
        {
            Require(manager != null && !manager.enableGpuDispatch && manager.Buffers.IsAllocated,
                "Frozen GPU drawing lost its snapshot or resumed simulation.");
            frozenAgentRenderer.Draw(manager.UnitTypes, manager.Buffers, frozenRenderBounds, frozenCorpseSink);
            frozenProjectileRenderer.Draw(manager.systemConfig.projectileRenderConfig, manager.Buffers, frozenRenderBounds);
        }

        private void RecordGpuWorkload(string window, string boundary)
        {
            var snapshot = new GpuWorkloadSnapshot { window = window, boundary = boundary, workload = gpuWorkload,
                computeEnabled = manager.enableGpuDispatch, worldVisible = renderCamera.cullingMask != 0,
                frozenDraw = drawFrozenGpuSnapshot, simulationSeconds = controller.TelemetrySnapshot.battleSeconds,
                totalUnits = manager.UnitTypes.TotalAgentCount };
            var args = new uint[5];
            foreach (var unit in manager.UnitTypes.RegisteredTypes)
                for (int lod = 0; lod < MassGpuBufferManager.LodLevels; lod++)
                {
                    manager.Buffers.GetDrawArgsBuffer(unit.UnitTypeIndex, lod).GetData(args);
                    var runtime = unit.RenderRuntime;
                    var mesh = runtime.GetMesh(lod); var material = runtime.GetMaterial(lod);
                    var batch = new GpuDrawBatch { unitType = unit.UnitTypeIndex, lod = lod, instances = (int)args[1],
                        verticesPerMesh = mesh != null ? mesh.vertexCount : 0, indicesPerMesh = args[0],
                        mesh = mesh != null ? mesh.name : "missing", shader = material != null ? material.shader.name : "missing" };
                    batch.vertexInvocations = (long)batch.verticesPerMesh * batch.instances;
                    batch.triangles = batch.indicesPerMesh / 3 * batch.instances;
                    snapshot.visibleUnits += batch.instances; snapshot.visibleVertexInvocations += batch.vertexInvocations;
                    snapshot.visibleTriangles += batch.triangles; snapshot.batches.Add(batch);
                }
            manager.Buffers.projectileDrawArgsBuffer.GetData(args); snapshot.activeProjectiles = (int)args[1];
            report.gpuWorkloads.Add(snapshot); WriteReport();
        }
    }
}
#endif
