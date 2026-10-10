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
        public bool LaneApproach33 { get; }
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
        private TerrainNavigationBurstWorkspace burstWorkspace;
        private bool burstUnavailable;
        // P3-PERF-01 candidate (2026-10-10): Burst is the default in Editor and Player; --war-sandbox-nav-managed forces the managed solver.
        // --war-sandbox-nav-burst is still accepted (no-op). No preference or scene asset is written.
        private static readonly bool reserveOptIn = Array.Exists(Environment.GetCommandLineArgs(), a => a == "--war-sandbox-nav-burst");
        private static readonly bool reserveManagedOnly = Array.Exists(Environment.GetCommandLineArgs(), a => a == "--war-sandbox-nav-managed");
        public static bool BurstReserveRequested => !reserveManagedOnly;
        public static bool BurstArgumentPresent => reserveOptIn;
        public string PreparationBlockReason { get; private set; }
        private P3PreparationGate preparation;
        private System.Runtime.ExceptionServices.ExceptionDispatchInfo preparationFault;
        private static readonly bool preparationForceSyncArgument = Array.Exists(Environment.GetCommandLineArgs(), a => a == "--burst-force-sync-compilation");
        public string PreparationState => disposed ? "Disposed" : preparation != null ? preparation.State.ToString() : burstUnavailable ? "Unavailable" : "Unstarted";
        public int PreparationProbeCount { get; private set; }
        public int ManagedSolveCount { get; private set; }
        public int BurstSolveCount { get; private set; }
        public int ManagedNativeSolveCount { get; private set; }
        public double MaxPreparationProbeMilliseconds { get; private set; }
        // P3-CPU-01 (2026-10-10): dynamic-enemy fields can be solved by one scheduled Burst job
        // (goal extraction + Dijkstra + LaneApproach33) instead of on the main thread. Results are
        // bit-identical to the synchronous path for the same density snapshot; they reach the GPU
        // on a later Tick (normally the next one). Explicit commands stay synchronous/immediate.
        // Off by default (direct construction/tests keep the old timing); MassEngineManager opts in.
        // --war-sandbox-nav-sync forces the old synchronous path.
        private static readonly bool asyncSyncArgument = Array.Exists(Environment.GetCommandLineArgs(), a => a == "--war-sandbox-nav-sync");
        public const int MaxAsyncTicks = 3;
        public bool AsyncDynamicSolve { get; }
        public int AsyncSolveCount { get; private set; }
        public int AsyncDiscardedCount { get; private set; }
        public int AsyncForcedCompleteCount { get; private set; }
        public int LastAsyncLatencyTicks { get; private set; }
        public int MaxAsyncLatencyTicks { get; private set; }
        public bool AsyncSolveInFlight => inFlightTeam >= 0;
        private readonly Unity.Collections.NativeArray<uint>[] densityCopies;
        private readonly bool[] readyNative;
        private Unity.Jobs.JobHandle inFlightHandle;
        private int inFlightTeam = -1, inFlightTicks;
        private bool inFlightCancelled;
        private double inFlightMainMilliseconds;
        private static double Ms(long start) => (System.Diagnostics.Stopwatch.GetTimestamp() - start) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        private void ReleasePreparedWorkspace() { CompleteAndDiscardInFlight(); burstWorkspace?.Dispose(); burstWorkspace = null; }
        private bool BurstSolveReady()
        {
            preparationFault?.Throw();
            bool enabled = Unity.Burst.BurstCompiler.IsEnabled;
            bool forceSync = preparationForceSyncArgument || Unity.Burst.BurstCompiler.Options.EnableBurstCompileSynchronously;
            return BurstReserveRequested && preparation != null && preparation.CanUseBurst(this, enabled, forceSync, Time.realtimeSinceStartupAsDouble);
        }
        private bool ProbePreparedWorkspace()
        {
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            try { PreparationProbeCount++; return burstWorkspace.ProbeCompilation(); }
            finally { MaxPreparationProbeMilliseconds = Math.Max(MaxPreparationProbeMilliseconds, (System.Diagnostics.Stopwatch.GetTimestamp() - start) * 1000.0 / System.Diagnostics.Stopwatch.Frequency); }
        }
        private void AdvancePreparation()
        {
            preparationFault?.Throw();
            if (burstUnavailable) return;
            if (!BurstReserveRequested) { PreparationBlockReason = "ManagedOverride"; burstUnavailable = true; return; }
            bool enabled = Unity.Burst.BurstCompiler.IsEnabled;
            bool forceSync = preparationForceSyncArgument || Unity.Burst.BurstCompiler.Options.EnableBurstCompileSynchronously;
            double now = Time.realtimeSinceStartupAsDouble;
            try
            {
                if (preparation == null)
                {
                    // Refuse before allocation and before Run; do not override preferences.
                    if (!enabled || forceSync) { PreparationBlockReason = !enabled ? "CompilerDisabled" : "ForceSyncRefused"; burstUnavailable = true; return; }
                    burstWorkspace = Navigation.CreateBurstWorkspace();
                    preparation = new P3PreparationGate(this, ProbePreparedWorkspace, ReleasePreparedWorkspace, now);
                }
                preparation.Advance(this, enabled, forceSync, now);
                if (preparation.State == P3PreparationGate.Phase.Unavailable) PreparationBlockReason = preparation.Reason;
            }
            catch (Exception error)
            {
                preparation?.Dispose(); ReleasePreparedWorkspace();
                PreparationBlockReason = "Fault:" + error.GetType().Name;
                preparationFault = System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error);
                throw;
            }
        }
        private Vector2[] SolvePreparedBackend(IReadOnlyList<Vector2> goals, float radius)
        {
            if (inFlightTeam >= 0) throw new InvalidOperationException("Synchronous navigation solve while an async dynamic solve is in flight");
            if (BurstSolveReady())
            {
                Vector2[] result = burstWorkspace.Solve(goals, radius);
                if (burstWorkspace.LastRunUsedBurst) BurstSolveCount++;
                else { ManagedNativeSolveCount++; preparation.Dispose(); burstUnavailable = true; }
                return result;
            }
            ManagedSolveCount++;
            return Navigation.CreateFlowField(goals, radius);
        }


        public TerrainNavigationRuntime(TerrainNavigationGrid navigation, int teamCount, bool laneApproach33 = false, bool asyncDynamicSolve = false)
        {
            Navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
            LaneApproach33 = laneApproach33;
            AsyncDynamicSolve = asyncDynamicSolve && !asyncSyncArgument;
            densityCopies = new Unity.Collections.NativeArray<uint>[teamCount]; readyNative = new bool[teamCount];
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
                combatShader.SetInt("terrainLaneApproach33", LaneApproach33 ? 1 : 0);
                surfaceGpu.Bind(combatShader, shaders.SimulateCombatAndAccumulateDamage);
                combatShader.SetBuffer(shaders.SimulateCombatAndAccumulateDamage, "_NavigationWalkable", navigationMask);
            }
            if (projectileShader != null && shaders.SimulateProjectiles >= 0)
                surfaceGpu.Bind(projectileShader, shaders.SimulateProjectiles);
        }

        public void Tick(MassGpuBufferManager buffers, PipelineFrameContext context)
        {
            if (disposed) return;
            AdvancePreparation();
            if (inFlightTeam >= 0)
            {
                inFlightTicks++;
                if (inFlightHandle.IsCompleted || inFlightTicks >= MaxAsyncTicks) FinishInFlight(buffers, context);
            }
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
                readyNative[team] = AsyncDynamicSolve && density.Length == Navigation.CellCount && BurstSolveReady();
                if (readyNative[team])
                {
                    // Raw copy (no managed allocation); the job extracts the same goals later.
                    if (inFlightTeam == team) FinishInFlight(buffers, context);
                    if (!densityCopies[team].IsCreated)
                        densityCopies[team] = new Unity.Collections.NativeArray<uint>(Navigation.CellCount, Unity.Collections.Allocator.Persistent, Unity.Collections.NativeArrayOptions.UninitializedMemory);
                    densityCopies[team].CopyFrom(density);
                }
                else FillSnapshot(snapshot, density);
                ready[team] = true; readySince[team] = Time.realtimeSinceStartupAsDouble;
            }
            // While a job owns the shared workspace no other dynamic solve starts; ready teams wait.
            if (inFlightTeam >= 0) return;
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
                if (readyNative[team])
                {
                    readyNative[team] = false;
                    if (BurstSolveReady()) ScheduleDynamic(team);
                    else { FillSnapshot(readyTargets[team], densityCopies[team]); Upload(buffers, team, requestedStopRadius[team], readyTargets[team], true); }
                }
                else Upload(buffers, team, requestedStopRadius[team], readyTargets[team], true);
                nextDynamicTeam = (team + 1) % ready.Length;
                break;
            }
        }

        private void FillSnapshot(List<Vector2> snapshot, Unity.Collections.NativeArray<uint> density)
        {
            snapshot.Clear();
            for (int i = 0; i < density.Length; i++)
                if (density[i] > 0 && Navigation.IsWalkable(Navigation.CellCenter(i))) snapshot.Add(Navigation.CellCenter(i));
        }

        private void ScheduleDynamic(int team)
        {
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            float radius = requestedStopRadius[team];
            float near = Mathf.Max(Navigation.CellSize * 4, radius + Navigation.CellSize); // TerrainLaneApproach33.Apply
            inFlightHandle = burstWorkspace.ScheduleDynamic(densityCopies[team], radius, LaneApproach33, near);
            inFlightTeam = team; inFlightTicks = 0; inFlightCancelled = false;
            Unity.Jobs.JobHandle.ScheduleBatchedJobs();
            inFlightMainMilliseconds = Ms(start);
        }

        /// <summary>Completes the in-flight job (blocking only if it is still running) and uploads it if still wanted.</summary>
        private void FinishInFlight(MassGpuBufferManager buffers, PipelineFrameContext context)
        {
            if (inFlightTeam < 0) return;
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            if (!inFlightHandle.IsCompleted) AsyncForcedCompleteCount++;
            bool usedBurst = burstWorkspace.CompleteDynamic(inFlightHandle);
            int team = inFlightTeam; inFlightTeam = -1; inFlightHandle = default;
            LastAsyncLatencyTicks = inFlightTicks; MaxAsyncLatencyTicks = Math.Max(MaxAsyncLatencyTicks, inFlightTicks);
            var flow = context.teamFlows[team];
            if (!inFlightCancelled && context.battleStarted && flow.enabled && flow.dynamicFlowEnabled && flow.targetMode == 0)
            {
                buffers.flowFieldDirectionsBuffer.SetData(burstWorkspace.job.result, 0, team * Navigation.CellCount, Navigation.CellCount);
                CompletedFields++; AsyncSolveCount++;
            }
            else AsyncDiscardedCount++;
            if (usedBurst) BurstSolveCount++;
            else { ManagedNativeSolveCount++; preparation.Dispose(); burstUnavailable = true; }
            // Main-thread cost only (copy/schedule + completion + upload); the solve itself ran on a worker.
            LastSolveMilliseconds = (float)(inFlightMainMilliseconds + Ms(start));
            TotalSolveMilliseconds += LastSolveMilliseconds;
            MaxSolveMilliseconds = Mathf.Max(MaxSolveMilliseconds, LastSolveMilliseconds);
        }

        private void CompleteAndDiscardInFlight()
        {
            if (inFlightTeam < 0) return;
            inFlightHandle.Complete(); inFlightHandle = default; inFlightTeam = -1; AsyncDiscardedCount++;
        }

        /// <summary>Called after this team's density kernel, instead of the plane's straight-line generator.</summary>
        public void Rebuild(MassGpuBufferManager buffers, PipelineFrameContext context, int team)
        {
            if (disposed) throw new ObjectDisposedException(nameof(TerrainNavigationRuntime));
            var flow = context.teamFlows[team];
            if (!flow.enabled || (flow.targetMode == 0 && (!context.battleStarted || !flow.dynamicFlowEnabled)))
            {
                pending[team] = ready[team] = readyNative[team] = false;
                if (inFlightTeam == team) inFlightCancelled = true;
                buffers.flowFieldDirectionsBuffer.SetData(empty, 0, team * Navigation.CellCount, empty.Length); return;
            }
            if (flow.targetMode != 0)
            {
                // A new explicit command invalidates any in-flight dynamic result.
                pending[team] = ready[team] = readyNative[team] = false;
                if (inFlightTeam == team) inFlightCancelled = true;
                FinishInFlight(buffers, context); // frees the shared workspace; another team's result is still uploaded
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

        private void Upload(MassGpuBufferManager buffers, int team, float stopRadius, IReadOnlyList<Vector2> goals, bool dynamicEnemy = false)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            Vector2[] directions = SolvePreparedBackend(goals, stopRadius);
            if(LaneApproach33 && dynamicEnemy) TerrainLaneApproach33.Apply(Navigation, goals, directions, stopRadius);
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
            CompleteAndDiscardInFlight();
            preparation?.Dispose(); preparation = null;
            burstWorkspace?.Dispose(); burstWorkspace = null;
            // Requests use polling, not callbacks: no completion can write into a replacement world's buffers.
            Array.Clear(pending, 0, pending.Length);
            Array.Clear(ready, 0, ready.Length);
            Array.Clear(readyNative, 0, readyNative.Length);
            for (int team = 0; team < densityCopies.Length; team++)
                if (densityCopies[team].IsCreated) densityCopies[team].Dispose();
            navigationMask?.Release(); navigationMask = null;
            surfaceGpu?.Dispose(); surfaceGpu = null;
            SetVariant(combatShader, false); SetVariant(projectileShader, false);
        }
    }
}


namespace MassEngine {
[Unity.Burst.BurstCompile(CompileSynchronously=false,FloatMode=Unity.Burst.FloatMode.Strict,FloatPrecision=Unity.Burst.FloatPrecision.Standard)]
 internal struct TerrainNavigationSolveJob : Unity.Jobs.IJob {

        public int CellCount, ResolutionX, targetCount;
        public float CellSize, stopRadius;
        public Vector2 Origin;
        public bool hasTargets;
        [Unity.Collections.ReadOnly] public Unity.Collections.NativeArray<uint> walkable;
        [Unity.Collections.ReadOnly] public Unity.Collections.NativeArray<byte> neighbours;
        [Unity.Collections.ReadOnly] public Unity.Collections.NativeArray<double> edgeCosts;
        // Not [ReadOnly]: TerrainNavigationDynamicJob fills it on the worker before running this solve.
        public Unity.Collections.NativeArray<int> targetCells;
        [Unity.Collections.ReadOnly] public Unity.Collections.NativeArray<Vector2> uniformOutputDirections;
        public Unity.Collections.NativeArray<double> distances;
        public Unity.Collections.NativeArray<int> nextCells, heap, heapPositions, proof;
        public Unity.Collections.NativeArray<Vector2> result;
        private int heapCount;
        private static readonly int[] StepX = {1,0,1,-1,-1,0,-1,1};
        private static readonly int[] StepZ = {0,1,1,1,0,-1,-1,-1};
        [Unity.Burst.BurstDiscard] private static void ManagedOnly(ref bool compiled) {compiled=false;}
        internal Vector2 CellCenter(int cell) => new Vector2((float)(Origin.x+(cell%ResolutionX+.5)*CellSize),(float)(Origin.y+(cell/ResolutionX+.5)*CellSize));
        public void Execute()
        {
            bool compiled=true; ManagedOnly(ref compiled); proof[0]=compiled?1:0;
            heapCount = 0;
            for (int cell = 0; cell < CellCount; cell++)
            {
                result[cell] = Vector2.zero;
                distances[cell] = double.PositiveInfinity;
                nextCells[cell] = -1;
                heapPositions[cell] = -1;
            }
            if (!hasTargets) return;
            for (int i = 0; i < targetCount; i++)
            {
                int target=targetCells[i];
                if (target<0 || walkable[target] == 0 || distances[target] == 0) continue;
                distances[target] = 0;
                nextCells[target] = target;
                PushOrDecrease(target);
            }
            while (heapCount > 0)
            {
                int cell = PopMinimum();
                for (int direction = 0; direction < 8; direction++)
                {
                    if ((neighbours[cell] & (1 << direction)) == 0) continue;
                    int adjacent = cell + StepZ[direction] * ResolutionX + StepX[direction];
                    if (heapPositions[adjacent] == -2) continue;
                    double cost = direction < 4 ? edgeCosts[cell * 4 + direction] :
                        edgeCosts[adjacent * 4 + direction - 4];
                    double candidate = distances[cell] + cost;
                    if (candidate >= distances[adjacent]) continue;
                    distances[adjacent] = candidate;
                    nextCells[adjacent] = cell;
                    PushOrDecrease(adjacent);
                }
            }
            for (int cell = 0; cell < CellCount; cell++)
            {
                int next = nextCells[cell];
                if (next < 0 || next == cell || distances[cell] <= stopRadius) continue;
                if (uniformOutputDirections.Length == 9)
                {
                    int dx = next % ResolutionX - cell % ResolutionX;
                    int dz = next / ResolutionX - cell / ResolutionX;
                    result[cell] = uniformOutputDirections[(dz + 1) * 3 + dx + 1];
                }
                else
                {
                    Vector2 delta = CellCenter(next) - CellCenter(cell);
                    double length = Math.Sqrt((double)delta.x * delta.x + (double)delta.y * delta.y);
                    result[cell] = new Vector2((float)(delta.x / length), (float)(delta.y / length));
                }
            }

        }

        private void PushOrDecrease(int cell)
        {
            int position = heapPositions[cell];
            if (position < 0) position = heapCount++;
            // Priorities do not change during one sift. Keep the same distance/id order,
            // but load the moving node's priority once instead of at each comparison.
            double cellDistance = distances[cell];
            while (position > 0)
            {
                int parent = (position - 1) / 2;
                int parentCell = heap[parent];
                double parentDistance = distances[parentCell];
                if (!(cellDistance < parentDistance || (cellDistance == parentDistance && cell < parentCell))) break;
                heap[position] = parentCell;
                heapPositions[parentCell] = position;
                position = parent;
            }
            heap[position] = cell;
            heapPositions[cell] = position;
        }

        private int PopMinimum()
        {
            int minimum = heap[0];
            int last = heap[--heapCount];
            heapPositions[minimum] = -2;
            if (heapCount == 0) return minimum;
            double lastDistance = distances[last];
            int position = 0;
            while (position * 2 + 1 < heapCount)
            {
                int child = position * 2 + 1;
                int childCell = heap[child];
                double childDistance = distances[childCell];
                if (child + 1 < heapCount)
                {
                    int rightCell = heap[child + 1];
                    double rightDistance = distances[rightCell];
                    if (rightDistance < childDistance || (rightDistance == childDistance && rightCell < childCell))
                    {
                        child++;
                        childCell = rightCell;
                        childDistance = rightDistance;
                    }
                }
                if (!(childDistance < lastDistance || (childDistance == lastDistance && childCell < last))) break;
                heap[position] = childCell;
                heapPositions[childCell] = position;
                position = child;
            }
            heap[position] = last;
            heapPositions[last] = position;
            return minimum;
        }


}

  internal sealed class TerrainNavigationBurstWorkspace:IDisposable {
   public TerrainNavigationSolveJob job;public readonly TerrainNavigationGrid nav;public long PayloadBytes;bool disposed;
   private bool counted;
   public static int CreatedWorkspaces{get;private set;}
   public static int DisposedWorkspaces{get;private set;}
   public static int ActiveWorkspaces{get;private set;}
   public static int BurstCalls{get;private set;}
   public static int ManagedNativeCalls{get;private set;}
   public bool IsDisposed=>disposed;
   public bool LastRunUsedBurst=>!disposed && job.proof[0]==1;
   Unity.Collections.NativeArray<T> Alloc<T>(int n) where T:struct => new Unity.Collections.NativeArray<T>(n,Unity.Collections.Allocator.Persistent,Unity.Collections.NativeArrayOptions.UninitializedMemory);
   Unity.Collections.NativeArray<T> Copy<T>(T[] a) where T:struct => new Unity.Collections.NativeArray<T>(a,Unity.Collections.Allocator.Persistent);
   public TerrainNavigationBurstWorkspace(TerrainNavigationGrid nav,uint[] walkableSource,byte[] neighboursSource,double[] costsSource,Vector2[] uniformSource){this.nav=nav;int n=nav.CellCount;try{
    job.CellCount=n;job.ResolutionX=nav.ResolutionX;job.CellSize=nav.CellSize;job.Origin=nav.Origin;
    job.walkable=Copy(walkableSource);job.neighbours=Copy(neighboursSource);job.edgeCosts=Copy(costsSource);
    job.uniformOutputDirections=Copy(uniformSource??new Vector2[0]);
    job.distances=Alloc<double>(n);job.nextCells=Alloc<int>(n);job.heap=Alloc<int>(n);job.heapPositions=Alloc<int>(n);job.result=Alloc<Vector2>(n);job.targetCells=Alloc<int>(n);job.proof=Alloc<int>(1);
    PayloadBytes=69L*n+8L*job.uniformOutputDirections.Length+4;
    counted=true;CreatedWorkspaces++;ActiveWorkspaces++;
   }catch{Dispose();throw;}}
   void Prepare(IReadOnlyList<Vector2> goals,float radius){if(disposed)throw new ObjectDisposedException("Workspace");if(float.IsNaN(radius)||float.IsInfinity(radius)||radius<0)throw new ArgumentOutOfRangeException("stopRadius");job.stopRadius=radius;job.hasTargets=goals!=null;job.targetCount=goals==null?0:goals.Count;
    if(job.targetCount>job.targetCells.Length){job.targetCells.Dispose();job.targetCells=Alloc<int>(job.targetCount);}
    for(int i=0;i<job.targetCount;i++)job.targetCells[i]=nav.TryGetCell(goals[i],out int cell)?cell:-1;
   }

   // Same job entry and proof, but no grid initialization/copy. No solve scheduling changes.
   public bool ProbeCompilation(){if(disposed)throw new ObjectDisposedException("Workspace");var probe=job;probe.CellCount=0;probe.targetCount=0;probe.hasTargets=false;Unity.Jobs.IJobExtensions.Run(probe);if(job.proof[0]==1)BurstCalls++;else ManagedNativeCalls++;return LastRunUsedBurst;}
   // ---- P3-CPU-01 async dynamic solve (shares job arrays; caller guarantees one user at a time)
   Unity.Collections.NativeArray<int> rowStart,columnStart,rowCursor,columnCursor,rowCells,columnCells,goalCount;
   public int LastDynamicGoalCount=>goalCount.IsCreated?goalCount[0]:0;
   public Unity.Jobs.JobHandle ScheduleDynamic(Unity.Collections.NativeArray<uint> density,float radius,bool lane,float near){
    if(disposed)throw new ObjectDisposedException("Workspace");
    if(float.IsNaN(radius)||float.IsInfinity(radius)||radius<0)throw new ArgumentOutOfRangeException("stopRadius");
    if(!density.IsCreated||density.Length!=nav.CellCount)throw new ArgumentException("density");
    int n=nav.CellCount,rx=nav.ResolutionX,rz=nav.ResolutionZ;
    if(!rowStart.IsCreated){rowStart=Alloc<int>(rz+1);columnStart=Alloc<int>(rx+1);rowCursor=Alloc<int>(rz);columnCursor=Alloc<int>(rx);rowCells=Alloc<int>(n);columnCells=Alloc<int>(n);goalCount=Alloc<int>(1);}
    if(job.targetCells.Length<n){job.targetCells.Dispose();job.targetCells=Alloc<int>(n);}
    job.stopRadius=radius;job.hasTargets=true;job.targetCount=0;
    var dynamicJob=new TerrainNavigationDynamicJob{solve=job,density=density,ResolutionZ=rz,
     MaximumX=nav.Origin.x+(double)nav.CellSize*rx,MaximumZ=nav.Origin.y+(double)nav.CellSize*rz, // same expressions as TerrainNavigationGrid
     lane=lane,near=near,rowStart=rowStart,columnStart=columnStart,rowCursor=rowCursor,columnCursor=columnCursor,rowCells=rowCells,columnCells=columnCells,goalCount=goalCount};
    return Unity.Jobs.IJobExtensions.Schedule(dynamicJob);
   }
   public bool CompleteDynamic(Unity.Jobs.JobHandle handle){handle.Complete();if(job.proof[0]==1)BurstCalls++;else ManagedNativeCalls++;return LastRunUsedBurst;}
   public Vector2[] Solve(IReadOnlyList<Vector2> goals,float radius){Prepare(goals,radius);var output=new Vector2[nav.CellCount];Unity.Jobs.IJobExtensions.Run(job);
    if(job.proof[0]==1)BurstCalls++;else ManagedNativeCalls++;
    job.result.CopyTo(output);return output;}
   public void Dispose(){if(disposed)return;disposed=true;
    if(counted){counted=false;DisposedWorkspaces++;ActiveWorkspaces--;}
if(job.walkable.IsCreated)job.walkable.Dispose();if(job.neighbours.IsCreated)job.neighbours.Dispose();if(job.edgeCosts.IsCreated)job.edgeCosts.Dispose();if(job.uniformOutputDirections.IsCreated)job.uniformOutputDirections.Dispose();if(job.distances.IsCreated)job.distances.Dispose();if(job.nextCells.IsCreated)job.nextCells.Dispose();if(job.heap.IsCreated)job.heap.Dispose();if(job.heapPositions.IsCreated)job.heapPositions.Dispose();if(job.result.IsCreated)job.result.Dispose();if(job.targetCells.IsCreated)job.targetCells.Dispose();if(job.proof.IsCreated)job.proof.Dispose();
    if(rowStart.IsCreated)rowStart.Dispose();if(columnStart.IsCreated)columnStart.Dispose();if(rowCursor.IsCreated)rowCursor.Dispose();if(columnCursor.IsCreated)columnCursor.Dispose();if(rowCells.IsCreated)rowCells.Dispose();if(columnCells.IsCreated)columnCells.Dispose();if(goalCount.IsCreated)goalCount.Dispose();}
  }

}

namespace MassEngine {
 /// <summary>
 /// P3-CPU-01: one dynamic-enemy field off the main thread. Same arithmetic, order and tie rules as
 /// TerrainNavigationRuntime.Tick's snapshot + TerrainNavigationBurstWorkspace.Solve + TerrainLaneApproach33.Apply.
 /// </summary>
 [Unity.Burst.BurstCompile(CompileSynchronously=false,FloatMode=Unity.Burst.FloatMode.Strict,FloatPrecision=Unity.Burst.FloatPrecision.Standard)]
 internal struct TerrainNavigationDynamicJob : Unity.Jobs.IJob {
  public TerrainNavigationSolveJob solve;
  public int ResolutionZ; public double MaximumX, MaximumZ;
  public bool lane; public float near;
  [Unity.Collections.ReadOnly] public Unity.Collections.NativeArray<uint> density;
  public Unity.Collections.NativeArray<int> rowStart, columnStart, rowCursor, columnCursor, rowCells, columnCells, goalCount;
  [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Explicit)]
  struct FloatBits { [System.Runtime.InteropServices.FieldOffset(0)] public float F; [System.Runtime.InteropServices.FieldOffset(0)] public int I; }
  static int Bits(float value) { var b = new FloatBits(); b.F = value; return b.I; }

  // TerrainNavigationGrid.TryGetCell (the grid's validated origin/size are finite).
  bool TryGetCell(Vector2 position, out int cell) {
   cell = -1;
   if (float.IsNaN(position.x) || float.IsInfinity(position.x) || float.IsNaN(position.y) || float.IsInfinity(position.y) ||
       position.x < solve.Origin.x || position.y < solve.Origin.y || position.x >= MaximumX || position.y >= MaximumZ) return false;
   int x = (int)Math.Floor(((double)position.x - solve.Origin.x) / solve.CellSize);
   int z = (int)Math.Floor(((double)position.y - solve.Origin.y) / solve.CellSize);
   cell = z * solve.ResolutionX + x;
   return true;
  }

  // TerrainNavigationGrid.HasClearCardinalRoute33.
  bool HasClearCardinalRoute33(int from, int to) {
   int n = solve.CellCount, rx = solve.ResolutionX;
   if (from < 0 || to < 0 || from >= n || to >= n || solve.walkable[from] == 0 || solve.walkable[to] == 0) return false;
   if (from == to) return true;
   int step, bit;
   if (from / rx == to / rx) { step = to > from ? 1 : -1; bit = step > 0 ? 0 : 4; }
   else if (from % rx == to % rx) { step = to > from ? rx : -rx; bit = step > 0 ? 1 : 5; }
   else return false;
   for (int cell = from; cell != to; cell += step) if ((solve.neighbours[cell] & (1 << bit)) == 0) return false;
   return true;
  }

  public void Execute() {
   int n = solve.CellCount, rx = solve.ResolutionX;
   // 1) Tick snapshot: density>0 && IsWalkable(CellCenter(i)) -> goal CellCenter(i); Prepare maps it back with TryGetCell.
   int count = 0;
   for (int i = 0; i < n; i++) {
    if (density[i] == 0) continue;
    if (!TryGetCell(solve.CellCenter(i), out int cell) || solve.walkable[cell] == 0) continue;
    solve.targetCells[count++] = cell;
   }
   goalCount[0] = count;
   // 2) Dijkstra (identical job code).
   solve.targetCount = count; solve.hasTargets = true;
   solve.Execute();
   if (!lane || count == 0) return;
   // 3) TerrainLaneApproach33.Apply. Every goal passes its TryGetCell/IsWalkable filter (checked in 1).
   Vector2 mean = Vector2.zero;
   for (int k = 0; k < count; k++) mean += solve.CellCenter(solve.targetCells[k]);
   mean /= count;
   for (int z = 0; z <= ResolutionZ; z++) rowStart[z] = 0;
   for (int x = 0; x <= rx; x++) columnStart[x] = 0;
   for (int k = 0; k < count; k++) { int c = solve.targetCells[k]; rowStart[c / rx + 1]++; columnStart[c % rx + 1]++; }
   for (int z = 0; z < ResolutionZ; z++) { rowStart[z + 1] += rowStart[z]; rowCursor[z] = rowStart[z]; }
   for (int x = 0; x < rx; x++) { columnStart[x + 1] += columnStart[x]; columnCursor[x] = columnStart[x]; }
   for (int k = 0; k < count; k++) { int c = solve.targetCells[k]; rowCells[rowCursor[c / rx]++] = c; columnCells[columnCursor[c % rx]++] = c; }
   SortBuckets(rowStart, rowCells, ResolutionZ); SortBuckets(columnStart, columnCells, rx);
   for (int cell = 0; cell < n; cell++) {
    Vector2 original = solve.result[cell]; if (original.sqrMagnitude < .0001f) continue;
    int row = cell / rx, column = cell % rx;
    if (rowStart[row] == rowStart[row + 1] && columnStart[column] == columnStart[column + 1]) continue;
    Vector2 here = solve.CellCenter(cell), toward = mean - here; bool horizontal = Mathf.Abs(toward.x) >= Mathf.Abs(toward.y); int sign = (horizontal ? toward.x : toward.y) >= 0 ? 1 : -1;
    Vector2 proposed = horizontal ? new Vector2(sign, 0) : new Vector2(0, sign);
    if (Bits(original.x) == Bits(proposed.x) && Bits(original.y) == Bits(proposed.y)) continue;
    if (Vector2.Dot(proposed, original) < .5f) continue;
    int begin = horizontal ? rowStart[row] : columnStart[column], end = horizontal ? rowStart[row + 1] : columnStart[column + 1];
    if (begin == end) continue;
    var list = horizontal ? rowCells : columnCells;
    // List<int>.BinarySearch: found -> skip; otherwise ~result is the first element greater than cell.
    int lo = begin, hi = end;
    while (lo < hi) { int mid = lo + ((hi - lo) >> 1); if (list[mid] < cell) lo = mid + 1; else hi = mid; }
    if (lo < end && list[lo] == cell) continue;
    int pick = sign > 0 ? lo : lo - 1; if (pick < begin || pick >= end) continue; int target = list[pick];
    if (Vector2.Distance(here, solve.CellCenter(target)) < near || !HasClearCardinalRoute33(cell, target)) continue;
    solve.result[cell] = proposed;
   }
  }

  // Ascending insertion sort per bucket (buckets arrive already ascending from the in-order snapshot).
  static void SortBuckets(Unity.Collections.NativeArray<int> start, Unity.Collections.NativeArray<int> cells, int buckets) {
   for (int b = 0; b < buckets; b++)
    for (int i = start[b] + 1; i < start[b + 1]; i++) {
     int v = cells[i], j = i - 1;
     while (j >= start[b] && cells[j] > v) { cells[j + 1] = cells[j]; j--; }
     cells[j + 1] = v;
    }
  }
 }
}

namespace MassEngine {
 // Editor-only preparation policy owned by TerrainNavigationRuntime; normal solves do not probe compilation.
 public sealed class P3PreparationUnavailableException : System.Exception { public P3PreparationUnavailableException(string message):base(message){} }
 public sealed class P3PreparationGate : System.IDisposable {
  public enum Phase { Waiting, Ready, Unavailable, Faulted, Disposed }
  public Phase State {get;private set;} = Phase.Waiting;
  public string Reason {get;private set;}
  readonly object owner;readonly System.Func<bool> probe;readonly System.Action release;
  readonly double deadline;double nextProbe,lastTime;bool released;
  public P3PreparationGate(object owner,System.Func<bool> probe,System.Action release,double now,double timeout=60) {
   if(owner==null||probe==null||release==null)throw new System.ArgumentNullException();
   if(double.IsNaN(now)||double.IsInfinity(now)||now<0||double.IsNaN(timeout)||double.IsInfinity(timeout)||timeout<=0)throw new System.ArgumentOutOfRangeException();
   this.owner=owner;this.probe=probe;this.release=release;lastTime=nextProbe=now;deadline=now+timeout;
  }
  void ReleaseOnce(){if(released)return;released=true;release();}
  bool Reject(string reason){Reason=reason;State=Phase.Unavailable;ReleaseOnce();return false;}
  bool Validate(object currentOwner,bool enabled,bool forceSync,double now){
   if(State==Phase.Disposed||State==Phase.Unavailable||State==Phase.Faulted)return false;
   if(double.IsNaN(now)||double.IsInfinity(now)||now<lastTime)throw new System.ArgumentOutOfRangeException(nameof(now));
   lastTime=now;
   if(!object.ReferenceEquals(owner,currentOwner))return Reject("owner-replaced");
   if(!enabled)return Reject("disabled");
   if(forceSync)return Reject("forced-sync");
   if(State==Phase.Waiting&&now>=deadline)return Reject("timeout");
   return true;
  }
  // Normal navigation asks this only: it never starts/probes compilation as a side effect.
  public bool CanUseBurst(object currentOwner,bool enabled,bool forceSync,double now)=>Validate(currentOwner,enabled,forceSync,now)&&State==Phase.Ready;
  public bool Advance(object currentOwner,bool enabled,bool forceSync,double now){
   if(!Validate(currentOwner,enabled,forceSync,now))return false;
   if(State==Phase.Ready)return true;
   if(now<nextProbe)return false;
   nextProbe=now+0.1;
   try {if(probe())State=Phase.Ready;return State==Phase.Ready;}
   catch(P3PreparationUnavailableException){return Reject("backend-unavailable");}
   catch {State=Phase.Faulted;Reason="unexpected-error";ReleaseOnce();throw;}
  }
  public void Dispose(){if(State==Phase.Disposed)return;State=Phase.Disposed;ReleaseOnce();}
 }
}

