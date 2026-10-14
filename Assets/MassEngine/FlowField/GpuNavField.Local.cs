using System;
using Unity.Jobs;
using UnityEngine;

namespace MassEngine
{
    /// <summary>
    /// N5a (Docs/GPU-NAVFIELD-N5-20261013.md): GPU layers for local Move/Retreat groups (LocalOrderChannel slots).
    /// Opt-in (RuntimeFlowConfig.gpuNavLocalOrders, needs gpuNavSteering). A slot layer is used only when its plan's
    /// navigation is the field's own clearance class; attack and hold groups always keep the CPU path. Layers are seeded
    /// one at a time by a single Fast Marching job and are sampled only after their seed is uploaded (CPU fallback until
    /// then, so a seed never has to be forced). Buffers are allocated on first use.
    /// </summary>
    public sealed partial class GpuNavField
    {
        public const int LocalSlots = LocalOrderChannel.PhysicalSlots;
        public bool LocalEnabled { get; set; }
        public int LocalSeedUploads { get; private set; }
        public int LocalCommits { get; private set; }
        public int LocalRejectedNavigation { get; private set; }
        public double LocalMaxSeedMilliseconds { get; private set; }

        private ComputeBuffer localA, localB;
        private readonly GpuNavGoal[] localGoals = new GpuNavGoal[LocalSlots];
        private readonly int[] localTeams = new int[LocalSlots];
        private readonly bool[] localValid = new bool[LocalSlots];
        private readonly int[] localCurrent = new int[LocalSlots];
        private readonly int[] localSerial = new int[LocalSlots];
        private uint localActiveMask;
        private GpuNavSeedWorkspace localSeed;
        private int localSeedSlot = -1, localSeedSerial;
        private readonly System.Collections.Generic.List<int> localQueue = new System.Collections.Generic.List<int>();

        /// <summary>Bit s: local slot s + 1 is seeded for its current goal, active, and local layers are enabled.</summary>
        public uint LocalValidMask
        {
            get
            {
                if (!LocalEnabled || localA == null) return 0;
                uint mask = 0;
                for (int s = 0; s < LocalSlots; s++) if (localValid[s] && (localActiveMask & (1u << s)) != 0) mask |= 1u << s;
                return mask;
            }
        }

        /// <summary>Slots referenced by any live order (LocalOrderChannel.UsedSlotMask, bit = slot - 1). Others are not relaxed.</summary>
        public void SetLocalActiveMask(uint mask) => localActiveMask = mask;

        /// <summary>Whether a local plan's navigation is this field's clearance class (same grid shape, clearance and mask).</summary>
        public bool SharesNavigation(TerrainNavigationGrid navigation)
        {
            if (navigation == null || Coarse == null) return false;
            if (ReferenceEquals(navigation, Coarse)) return true;
            if (navigation.Clearance != Coarse.Clearance || navigation.CellSize != Coarse.CellSize || !navigation.Origin.Equals(Coarse.Origin)
                || navigation.ResolutionX != Coarse.ResolutionX || navigation.ResolutionZ != Coarse.ResolutionZ) return false;
            var a = navigation.CopyWalkable(); var b = Coarse.CopyWalkable();
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        /// <summary>
        /// A local group was committed into slot (1-based). Move/Retreat with a shared navigation get a point layer;
        /// anything else clears the slot's layer (CPU path).
        /// </summary>
        public void CommitLocalSlot(int slot, int team, LocalOrderKind kind, LocalOrderPlan plan)
        {
            if (disposed || slot < 1 || slot > LocalSlots) return;
            int s = slot - 1;
            localValid[s] = false; localSerial[s]++;
            localQueue.Remove(s);
            localGoals[s] = new GpuNavGoal { mode = GpuNavGoal.None, cell = -1, coarseResolution = CoarseResolution };
            if (!LocalEnabled || plan == null || (kind != LocalOrderKind.Move && kind != LocalOrderKind.Retreat)) return;
            if (team < 0 || team >= TeamCount) return;
            if (!SharesNavigation(plan.Navigation)) { LocalRejectedNavigation++; return; }
            var target = plan.ResolvedTarget;
            CommitLocalPoint(s, team, SnapWalkable(FineCell(target), target));
        }

        /// <summary>Tests: commit a point goal (fine cell) into slot (1-based) without a LocalOrderPlan.</summary>
        public void CommitLocalPointForTest(int slot, int team, int fineCell)
        {
            if (disposed || slot < 1 || slot > LocalSlots) return;
            int s = slot - 1;
            localValid[s] = false; localSerial[s]++; localQueue.Remove(s);
            localGoals[s] = new GpuNavGoal { mode = GpuNavGoal.None, cell = -1, coarseResolution = CoarseResolution };
            CommitLocalPoint(s, team, fineCell);
        }

        private void CommitLocalPoint(int s, int team, int cell)
        {
            if (cell < 0 || team < 0 || team >= TeamCount) return;
            EnsureLocalBuffers();
            localGoals[s] = new GpuNavGoal { mode = GpuNavGoal.Point, cell = cell, coarseResolution = CoarseResolution };
            localTeams[s] = team;
            localQueue.Add(s);
            LocalCommits++;
        }

        private void EnsureLocalBuffers()
        {
            if (localA != null) return;
            int n = Graph.CellCount * LocalSlots;
            var fill = GpuNavFieldReference.Filled(n, GpuNavFieldGraph.Unreachable);
            localA = new ComputeBuffer(n, 4); localA.SetData(fill);
            localB = new ComputeBuffer(n, 4); localB.SetData(fill);
            localSeed = new GpuNavSeedWorkspace(Graph, 1);
            GpuBytes += (long)n * 8;
        }

        private void TickLocal(MassGpuBufferManager buffers)
        {
            if (localA == null) return;
            PollLocalSeed();
            if (!LocalEnabled) return;
            int n = Graph.CellCount;
            for (int s = 0; s < LocalSlots; s++)
            {
                if (!localValid[s] || (localActiveMask & (1u << s)) == 0 || localGoals[s].mode == GpuNavGoal.None) continue;
                int passes = PassesPerFrame + ((PassesPerFrame + localCurrent[s]) & 1); // end with the latest values in A
                for (int p = 0; p < passes; p++)
                {
                    var input = localCurrent[s] == 0 ? localA : localB;
                    var output = localCurrent[s] == 0 ? localB : localA;
                    RelaxLayer(buffers, localGoals[s], input, output, s * n, localTeams[s] * n, localTeams[s]);
                    localCurrent[s] ^= 1;
                    PassCount++;
                }
            }
        }

        private void PollLocalSeed()
        {
            var t = localSeed.teams[0];
            if (localSeedSlot >= 0 && t.handle.IsCompleted)
            {
                t.handle.Complete(); t.scheduled = false;
                int s = localSeedSlot; localSeedSlot = -1;
                if (localSerial[s] == localSeedSerial && localGoals[s].Equals(t.goal))
                {
                    int n = Graph.CellCount;
                    (localCurrent[s] == 0 ? localA : localB).SetData(t.distances, 0, s * n, n);
                    localValid[s] = true;
                    LocalSeedUploads++;
                    double ms = (Time.realtimeSinceStartupAsDouble - t.scheduledTime) * 1000.0;
                    if (ms > LocalMaxSeedMilliseconds) LocalMaxSeedMilliseconds = ms;
                }
            }
            if (localSeedSlot >= 0 || localQueue.Count == 0) return;
            int next = localQueue[0]; localQueue.RemoveAt(0);
            if (localGoals[next].mode == GpuNavGoal.None) return;
            int count = localSeed.WriteGoals(Graph, localGoals[next], t);
            t.goal = localGoals[next];
            t.handle = localSeed.Job(t, count, Metric).Schedule();
            t.scheduled = true; t.scheduledFrame = Time.frameCount; t.scheduledTime = Time.realtimeSinceStartupAsDouble;
            localSeedSlot = next; localSeedSerial = localSerial[next];
            Unity.Jobs.JobHandle.ScheduleBatchedJobs();
        }

        /// <summary>Tests / diagnostics: completes the running local seed and uploads it now.</summary>
        public void CompleteLocalSeeds()
        {
            if (localA == null) return;
            for (int guard = 0; guard < LocalSlots + 1 && (localSeedSlot >= 0 || localQueue.Count > 0); guard++)
            {
                if (localSeedSlot >= 0) localSeed.teams[0].handle.Complete();
                PollLocalSeed();
            }
        }

        /// <summary>Tests / diagnostics: the latest values of a local slot layer (slot 1-based).</summary>
        public void ReadLocalDistances(int slot, float[] destination)
        {
            if (disposed) throw new ObjectDisposedException(nameof(GpuNavField));
            int n = Graph.CellCount, s = slot - 1;
            if (localA == null) { for (int i = 0; i < n; i++) destination[i] = GpuNavFieldGraph.Unreachable; return; }
            (localCurrent[s] == 0 ? localA : localB).GetData(destination, 0, s * n, n);
        }

        /// <summary>Tests: relax active, valid local layers as Tick does (no buffers: point goals only).</summary>
        public void TickLocalForTest() => TickLocal(null);

        private void DisposeLocal()
        {
            if (localSeed != null)
            {
                if (localSeedSlot >= 0) localSeed.teams[0].handle.Complete();
                localSeed.Dispose(); localSeed = null;
            }
            localSeedSlot = -1; localQueue.Clear();
            localA?.Release(); localA = null;
            localB?.Release(); localB = null;
        }
    }
}
