using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MassEngine
{
    /// <summary>
    /// Terrain-only CPU routing, shared mask/surface GPU storage and per-team async density snapshots.
    /// No agent-position readback, no per-agent pathfinder; the old plane keeps its GPU flow generator.
    /// </summary>
    public sealed class TerrainNavigationRuntime : IDisposable
    {
        public const string Keyword = "MASS_TERRAIN_ENABLED";
        public TerrainNavigationGrid Navigation { get; }
        public bool IsAllocated => surfaceGpu != null && surfaceGpu.IsAllocated && navigationMask != null;
        public long GpuBytes => IsAllocated ? surfaceGpu.Bytes + (long)Navigation.CellCount * 4 : 0;
        public int CompletedFields { get; private set; }
        public float LastSolveMilliseconds { get; private set; }
        public double TotalSolveMilliseconds { get; private set; }
        public float MaxSolveMilliseconds { get; private set; }
        public float MaxDynamicQueueWaitMilliseconds { get; private set; }

        private TerrainSurfaceGpu surfaceGpu;
        private ComputeBuffer navigationMask;
        private readonly AsyncGPUReadbackRequest[] requests;
        private readonly bool[] pending;
        private readonly float[] requestedStopRadius;
        private readonly List<Vector2>[] readyTargets;
        private readonly bool[] ready;
        private readonly double[] readySince;
        private int nextDynamicTeam;
        private readonly List<Vector2> targets = new List<Vector2>();
        private readonly Vector2[] empty;
        private ComputeShader combatShader, projectileShader;
        private bool disposed;

        public TerrainNavigationRuntime(TerrainNavigationGrid navigation, int teamCount)
        {
            Navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
            requests = new AsyncGPUReadbackRequest[teamCount]; pending = new bool[teamCount];
            requestedStopRadius = new float[teamCount]; empty = new Vector2[navigation.CellCount];
            readyTargets = new List<Vector2>[teamCount]; ready = new bool[teamCount]; readySince = new double[teamCount];
            for (int team = 0; team < teamCount; team++) readyTargets[team] = new List<Vector2>();
            try
            {
                surfaceGpu = new TerrainSurfaceGpu(navigation.Surface);
                navigationMask = new ComputeBuffer(navigation.CellCount, 4);
                navigationMask.SetData(navigation.CopyWalkable());
            }
            catch { Dispose(); throw; }
        }

        public static void SetVariant(MassGpuShaderSet shaders, bool enabled)
        {
            SetVariant(shaders.CombatSimulationShader, enabled);
            SetVariant(shaders.ProjectileShader, enabled);
        }
        private static void SetVariant(ComputeShader shader, bool enabled)
        { if (shader != null) { if (enabled) shader.EnableKeyword(Keyword); else shader.DisableKeyword(Keyword); } }

        public void Bind(MassGpuShaderSet shaders)
        {
            if (disposed) throw new ObjectDisposedException(nameof(TerrainNavigationRuntime));
            combatShader = shaders.CombatSimulationShader; projectileShader = shaders.ProjectileShader;
            if (combatShader != null && shaders.SimulateCombatAndAccumulateDamage >= 0)
            {
                surfaceGpu.Bind(combatShader, shaders.SimulateCombatAndAccumulateDamage);
                combatShader.SetBuffer(shaders.SimulateCombatAndAccumulateDamage, "_NavigationWalkable", navigationMask);
            }
            if (projectileShader != null && shaders.SimulateProjectiles >= 0)
                surfaceGpu.Bind(projectileShader, shaders.SimulateProjectiles);
        }

        public void Tick(MassGpuBufferManager buffers, PipelineFrameContext context)
        {
            if (disposed) return;
            for (int team = 0; team < pending.Length; team++)
            {
                if (!pending[team] || !requests[team].done) continue;
                pending[team] = false;
                var flow = context.teamFlows[team];
                if (requests[team].hasError || !context.battleStarted || !flow.enabled || !flow.dynamicFlowEnabled || flow.targetMode != 0)
                    continue;
                // Completed readback data survives only one frame. Copy sparse targets now;
                // deferring GetData itself would read a disposed native request next frame.
                var snapshot = readyTargets[team]; snapshot.Clear();
                var density = requests[team].GetData<uint>();
                for (int i = 0; i < density.Length; i++)
                    if (density[i] > 0 && Navigation.IsWalkable(Navigation.CellCenter(i))) snapshot.Add(Navigation.CellCenter(i));
                ready[team] = true; readySince[team] = Time.realtimeSinceStartupAsDouble;
            }
            // One dynamic solve per manager tick. Round robin prevents an active low-ID
            // team from starving later teams; explicit player commands remain immediate.
            for (int offset = 0; offset < ready.Length; offset++)
            {
                int team = (nextDynamicTeam + offset) % ready.Length;
                if (!ready[team]) continue;
                ready[team] = false;
                var flow = context.teamFlows[team];
                if (!context.battleStarted || !flow.enabled || !flow.dynamicFlowEnabled || flow.targetMode != 0) continue;
                MaxDynamicQueueWaitMilliseconds = Mathf.Max(MaxDynamicQueueWaitMilliseconds,
                    (float)((Time.realtimeSinceStartupAsDouble - readySince[team]) * 1000));
                Upload(buffers, team, requestedStopRadius[team], readyTargets[team]);
                nextDynamicTeam = (team + 1) % ready.Length;
                break;
            }
        }

        /// <summary>Called after this team's density kernel, instead of the plane's straight-line generator.</summary>
        public void Rebuild(MassGpuBufferManager buffers, PipelineFrameContext context, int team)
        {
            var flow = context.teamFlows[team];
            if (!flow.enabled || (flow.targetMode == 0 && (!context.battleStarted || !flow.dynamicFlowEnabled)))
            { pending[team] = ready[team] = false; buffers.flowFieldDirectionsBuffer.SetData(empty, 0, team * Navigation.CellCount, empty.Length); return; }
            if (flow.targetMode != 0)
            {
                // A new explicit command invalidates any in-flight dynamic result.
                pending[team] = ready[team] = false;
                targets.Clear();
                if (flow.targetMode == 1) targets.Add(new Vector2(flow.targetPoint.x, flow.targetPoint.z));
                else
                {
                    Vector2 center = new Vector2(flow.targetAreaCenter.x, flow.targetAreaCenter.z);
                    Vector2 half = new Vector2(flow.targetAreaSize.x, flow.targetAreaSize.z) * .5f;
                    for (int i = 0; i < Navigation.CellCount; i++)
                    {
                        Vector2 p = Navigation.CellCenter(i);
                        if (Mathf.Abs(p.x - center.x) <= half.x && Mathf.Abs(p.y - center.y) <= half.y && Navigation.IsWalkable(p)) targets.Add(p);
                    }
                }
                Upload(buffers, team, flow.targetStopRadius, targets);
            }
            else if (!pending[team] && !ready[team])
            {
                requestedStopRadius[team] = flow.targetStopRadius;
                requests[team] = AsyncGPUReadback.Request(buffers.runtimeFlowTargetDensityBuffer,
                    Navigation.CellCount * 4, team * Navigation.CellCount * 4);
                pending[team] = true;
            }
        }

        private void Upload(MassGpuBufferManager buffers, int team, float stopRadius, IReadOnlyList<Vector2> goals)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            Vector2[] directions = Navigation.CreateFlowField(goals, stopRadius);
            LastSolveMilliseconds = (float)watch.Elapsed.TotalMilliseconds;
            TotalSolveMilliseconds += LastSolveMilliseconds;
            MaxSolveMilliseconds = Mathf.Max(MaxSolveMilliseconds, LastSolveMilliseconds);
            buffers.flowFieldDirectionsBuffer.SetData(directions, 0, team * Navigation.CellCount, directions.Length);
            CompletedFields++;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            // Requests use polling, not callbacks: no completion can write into a replacement world's buffers.
            Array.Clear(pending, 0, pending.Length);
            Array.Clear(ready, 0, ready.Length);
            navigationMask?.Release(); navigationMask = null;
            surfaceGpu?.Dispose(); surfaceGpu = null;
            SetVariant(combatShader, false); SetVariant(projectileShader, false);
        }
    }
}
