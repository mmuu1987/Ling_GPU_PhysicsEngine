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
#if UNITY_EDITOR
        private TerrainNavigationBurstWorkspace burstWorkspace;
        private bool burstUnavailable;
#endif
#if UNITY_EDITOR
        // Process-local Editor opt-in only; no preference or scene asset is written.
        private static readonly bool reserveOptIn = Array.Exists(Environment.GetCommandLineArgs(), a => a == "--war-sandbox-nav-burst");
        private static readonly bool reserveManagedOnly = Array.Exists(Environment.GetCommandLineArgs(), a => a == "--war-sandbox-nav-managed");
        public static bool BurstReserveRequested => reserveOptIn && !reserveManagedOnly;
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
        private void ReleasePreparedWorkspace() { burstWorkspace?.Dispose(); burstWorkspace = null; }
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
            if (!BurstReserveRequested) { PreparationBlockReason = reserveManagedOnly ? "ManagedOverride" : "DefaultOff"; burstUnavailable = true; return; }
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
            preparationFault?.Throw();
            bool enabled = Unity.Burst.BurstCompiler.IsEnabled;
            bool forceSync = preparationForceSyncArgument || Unity.Burst.BurstCompiler.Options.EnableBurstCompileSynchronously;
            if (BurstReserveRequested && preparation != null && preparation.CanUseBurst(this, enabled, forceSync, Time.realtimeSinceStartupAsDouble))
            {
                Vector2[] result = burstWorkspace.Solve(goals, radius);
                if (burstWorkspace.LastRunUsedBurst) BurstSolveCount++;
                else { ManagedNativeSolveCount++; preparation.Dispose(); burstUnavailable = true; }
                return result;
            }
            ManagedSolveCount++;
            return Navigation.CreateFlowField(goals, radius);
        }
#endif


        public TerrainNavigationRuntime(TerrainNavigationGrid navigation, int teamCount, bool laneApproach33 = false)
        {
            Navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
            LaneApproach33 = laneApproach33;
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
#if UNITY_EDITOR
            AdvancePreparation();
#endif
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
                Upload(buffers, team, requestedStopRadius[team], readyTargets[team], true);
                nextDynamicTeam = (team + 1) % ready.Length;
                break;
            }
        }

        /// <summary>Called after this team's density kernel, instead of the plane's straight-line generator.</summary>
        public void Rebuild(MassGpuBufferManager buffers, PipelineFrameContext context, int team)
        {
            if (disposed) throw new ObjectDisposedException(nameof(TerrainNavigationRuntime));
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

        private void Upload(MassGpuBufferManager buffers, int team, float stopRadius, IReadOnlyList<Vector2> goals, bool dynamicEnemy = false)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
#if UNITY_EDITOR
            Vector2[] directions = SolvePreparedBackend(goals, stopRadius);
#else
            // Player/AOT is not qualified: retain the adopted managed backend unconditionally.
            Vector2[] directions = Navigation.CreateFlowField(goals, stopRadius);
#endif
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
#if UNITY_EDITOR
            preparation?.Dispose(); preparation = null;
#endif
#if UNITY_EDITOR
            burstWorkspace?.Dispose(); burstWorkspace = null;
#endif
            // Requests use polling, not callbacks: no completion can write into a replacement world's buffers.
            Array.Clear(pending, 0, pending.Length);
            Array.Clear(ready, 0, ready.Length);
            navigationMask?.Release(); navigationMask = null;
            surfaceGpu?.Dispose(); surfaceGpu = null;
            SetVariant(combatShader, false); SetVariant(projectileShader, false);
        }
    }
}


#if UNITY_EDITOR
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
        [Unity.Collections.ReadOnly] public Unity.Collections.NativeArray<int> targetCells;
        [Unity.Collections.ReadOnly] public Unity.Collections.NativeArray<Vector2> uniformOutputDirections;
        public Unity.Collections.NativeArray<double> distances;
        public Unity.Collections.NativeArray<int> nextCells, heap, heapPositions, proof;
        public Unity.Collections.NativeArray<Vector2> result;
        private int heapCount;
        private static readonly int[] StepX = {1,0,1,-1,-1,0,-1,1};
        private static readonly int[] StepZ = {0,1,1,1,0,-1,-1,-1};
        [Unity.Burst.BurstDiscard] private static void ManagedOnly(ref bool compiled) {compiled=false;}
        private Vector2 CellCenter(int cell) => new Vector2((float)(Origin.x+(cell%ResolutionX+.5)*CellSize),(float)(Origin.y+(cell/ResolutionX+.5)*CellSize));
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
#if UNITY_EDITOR
   private bool counted;
   public static int CreatedWorkspaces{get;private set;}
   public static int DisposedWorkspaces{get;private set;}
   public static int ActiveWorkspaces{get;private set;}
   public static int BurstCalls{get;private set;}
   public static int ManagedNativeCalls{get;private set;}
#endif
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
#if UNITY_EDITOR
    counted=true;CreatedWorkspaces++;ActiveWorkspaces++;
#endif
   }catch{Dispose();throw;}}
   void Prepare(IReadOnlyList<Vector2> goals,float radius){if(disposed)throw new ObjectDisposedException("Workspace");if(float.IsNaN(radius)||float.IsInfinity(radius)||radius<0)throw new ArgumentOutOfRangeException("stopRadius");job.stopRadius=radius;job.hasTargets=goals!=null;job.targetCount=goals==null?0:goals.Count;
    if(job.targetCount>job.targetCells.Length){job.targetCells.Dispose();job.targetCells=Alloc<int>(job.targetCount);}
    for(int i=0;i<job.targetCount;i++)job.targetCells[i]=nav.TryGetCell(goals[i],out int cell)?cell:-1;
   }

#if UNITY_EDITOR
   // Same job entry and proof, but no grid initialization/copy. No solve scheduling changes.
   public bool ProbeCompilation(){if(disposed)throw new ObjectDisposedException("Workspace");var probe=job;probe.CellCount=0;probe.targetCount=0;probe.hasTargets=false;Unity.Jobs.IJobExtensions.Run(probe);if(job.proof[0]==1)BurstCalls++;else ManagedNativeCalls++;return LastRunUsedBurst;}
#endif
   public Vector2[] Solve(IReadOnlyList<Vector2> goals,float radius){Prepare(goals,radius);var output=new Vector2[nav.CellCount];Unity.Jobs.IJobExtensions.Run(job);
#if UNITY_EDITOR
    if(job.proof[0]==1)BurstCalls++;else ManagedNativeCalls++;
#endif
    job.result.CopyTo(output);return output;}
   public void Dispose(){if(disposed)return;disposed=true;
#if UNITY_EDITOR
    if(counted){counted=false;DisposedWorkspaces++;ActiveWorkspaces--;}
#endif
if(job.walkable.IsCreated)job.walkable.Dispose();if(job.neighbours.IsCreated)job.neighbours.Dispose();if(job.edgeCosts.IsCreated)job.edgeCosts.Dispose();if(job.uniformOutputDirections.IsCreated)job.uniformOutputDirections.Dispose();if(job.distances.IsCreated)job.distances.Dispose();if(job.nextCells.IsCreated)job.nextCells.Dispose();if(job.heap.IsCreated)job.heap.Dispose();if(job.heapPositions.IsCreated)job.heapPositions.Dispose();if(job.result.IsCreated)job.result.Dispose();if(job.targetCells.IsCreated)job.targetCells.Dispose();if(job.proof.IsCreated)job.proof.Dispose();}
  }

}

#endif
#if UNITY_EDITOR
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
#endif
