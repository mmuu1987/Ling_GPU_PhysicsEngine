using System;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace MassEngine
{
    /// <summary>
    /// N2 seed: float Dijkstra over the fine graph with the SAME half edge costs and the same float addition as the
    /// relax kernel. With every edge >= 1 m and distances far below 2^23 m, fl(d + w) > d strictly, so the per-cell
    /// equations d = min(fl(d_n + w)) have one fixed point: this result is exactly what the GPU converges to, and a
    /// GPU pass over it changes nothing. Unreached cells (no goal in their component) become Unreachable, which also
    /// removes stale finite values that plain relaxation would only "count up".
    /// metric 1 (eikonal, N3): Fast Marching - the same heap order, but a popped cell re-evaluates each neighbour with the
    /// full 8-neighbour triangle update (GpuNavFieldReference.CellUpdate). Every candidate exceeds its inputs, so the
    /// order is causal and the result is the relax kernel's fixed point (up to the GPU's sqrt rounding).
    /// </summary>
    [Unity.Burst.BurstCompile(CompileSynchronously = false, FloatMode = Unity.Burst.FloatMode.Strict, FloatPrecision = Unity.Burst.FloatPrecision.Standard)]
    internal struct GpuNavSeedJob : IJob
    {
        public int resolution, goalCount, metric;
        [ReadOnly] public NativeArray<byte> neighbours;   // bit d = edge in direction d (0..7)
        [ReadOnly] public NativeArray<float> costs;       // 4 per cell, directions 0..3; d >= 4 uses the adjacent cell's d - 4
        [ReadOnly] public NativeArray<int> goalCells;
        public NativeArray<float> distances;
        public NativeArray<int> heap, heapPositions, proof;
        private int heapCount;
        private static readonly int[] StepX = { 1, 0, 1, -1, -1, 0, -1, 1 };
        private static readonly int[] StepZ = { 0, 1, 1, 1, 0, -1, -1, -1 };
        private static readonly int[] TriA = { 0, 0, 1, 1, 4, 4, 5, 5 };
        private static readonly int[] TriB = { 2, 7, 2, 3, 3, 6, 6, 7 };
        private const float FiniteCost = 1e20f;

        private static float Min(float a, float b) => b < a ? b : a;

        /// <summary>GpuNavFieldReference.CellUpdate on the job's arrays (unknown neighbours are +inf, i.e. not below Unreachable).</summary>
        private float Update(int cell)
        {
            int bits = neighbours[cell];
            float d = GpuNavFieldGraph.Unreachable;
            float v0 = 0, v1 = 0, v2 = 0, v3 = 0, v4 = 0, v5 = 0, v6 = 0, v7 = 0, w0 = 0, w1 = 0, w2 = 0, w3 = 0, w4 = 0, w5 = 0, w6 = 0, w7 = 0;
            for (int k = 0; k < 8; k++)
            {
                float v = GpuNavFieldGraph.Unreachable, w = float.PositiveInfinity;
                if ((bits & (1 << k)) != 0)
                {
                    int adjacent = cell + StepZ[k] * resolution + StepX[k];
                    v = distances[adjacent];
                    w = k < 4 ? costs[cell * 4 + k] : costs[adjacent * 4 + k - 4];
                }
                d = Min(d, v + w);
                switch (k)
                {
                    case 0: v0 = v; w0 = w; break; case 1: v1 = v; w1 = w; break; case 2: v2 = v; w2 = w; break; case 3: v3 = v; w3 = w; break;
                    case 4: v4 = v; w4 = w; break; case 5: v5 = v; w5 = w; break; case 6: v6 = v; w6 = w; break; default: v7 = v; w7 = w; break;
                }
            }
            for (int t = 0; t < 8; t++)
            {
                int ia = TriA[t], ib = TriB[t];
                float a = Pick(ia, v0, v1, v2, v3, v4, v5, v6, v7), b = Pick(ib, v0, v1, v2, v3, v4, v5, v6, v7);
                float wa = Pick(ia, w0, w1, w2, w3, w4, w5, w6, w7), wb = Pick(ib, w0, w1, w2, w3, w4, w5, w6, w7);
                if (!(wa < FiniteCost && wb < FiniteCost && a < GpuNavFieldGraph.Unreachable && b < GpuNavFieldGraph.Unreachable)) continue;
                float dd = a - b, dd2 = dd * dd, wa2 = wa * wa;
                if (dd >= 0f && dd2 * 2f <= wa2) d = Min(d, a + (float)Math.Sqrt(wa2 - dd2));
            }
            return Min(d, GpuNavFieldGraph.Unreachable);
        }

        private static float Pick(int i, float a0, float a1, float a2, float a3, float a4, float a5, float a6, float a7)
        {
            switch (i) { case 0: return a0; case 1: return a1; case 2: return a2; case 3: return a3; case 4: return a4; case 5: return a5; case 6: return a6; default: return a7; }
        }

        [Unity.Burst.BurstDiscard] private static void ManagedOnly(ref bool compiled) { compiled = false; }

        public void Execute()
        {
            bool compiled = true; ManagedOnly(ref compiled); proof[0] = compiled ? 1 : 0;
            int n = resolution * resolution;
            heapCount = 0;
            for (int c = 0; c < n; c++) { distances[c] = float.PositiveInfinity; heapPositions[c] = -1; }
            for (int i = 0; i < goalCount; i++)
            {
                int g = goalCells[i];
                if (g < 0 || g >= n || distances[g] == 0f) continue;
                distances[g] = 0f;
                PushOrDecrease(g);
            }
            while (heapCount > 0)
            {
                int cell = PopMinimum();
                float dc = distances[cell];
                int bits = neighbours[cell];
                for (int d = 0; d < 8; d++)
                {
                    if ((bits & (1 << d)) == 0) continue;
                    int adjacent = cell + StepZ[d] * resolution + StepX[d];
                    if (heapPositions[adjacent] == -2) continue;
                    float candidate;
                    if (metric == 1)
                    {
                        if (distances[adjacent] == 0f) continue; // goal
                        candidate = Update(adjacent);
                        if (!(candidate < GpuNavFieldGraph.Unreachable)) continue;
                    }
                    else
                    {
                        float w = d < 4 ? costs[cell * 4 + d] : costs[adjacent * 4 + d - 4];
                        candidate = dc + w;
                    }
                    if (!(candidate < distances[adjacent])) continue;
                    distances[adjacent] = candidate;
                    PushOrDecrease(adjacent);
                }
            }
            for (int c = 0; c < n; c++) if (float.IsPositiveInfinity(distances[c])) distances[c] = GpuNavFieldGraph.Unreachable;
        }

        private void PushOrDecrease(int cell)
        {
            int position = heapPositions[cell];
            if (position < 0) position = heapCount++;
            float distance = distances[cell];
            while (position > 0)
            {
                int parent = (position - 1) / 2;
                int parentCell = heap[parent];
                if (!(distance < distances[parentCell])) break;
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
            heapPositions[minimum] = -2;
            heapCount--;
            if (heapCount > 0)
            {
                int last = heap[heapCount];
                float distance = distances[last];
                int position = 0;
                while (true)
                {
                    int child = position * 2 + 1;
                    if (child >= heapCount) break;
                    int right = child + 1;
                    if (right < heapCount && distances[heap[right]] < distances[heap[child]]) child = right;
                    if (!(distances[heap[child]] < distance)) break;
                    heap[position] = heap[child];
                    heapPositions[heap[child]] = position;
                    position = child;
                }
                heap[position] = last;
                heapPositions[last] = position;
            }
            return minimum;
        }
    }

    /// <summary>Native copy of the fine graph plus one seed workspace per team (owned by GpuNavField).</summary>
    internal sealed class GpuNavSeedWorkspace : IDisposable
    {
        public readonly int resolution;
        public NativeArray<byte> neighbours;
        public NativeArray<float> costs;
        public readonly Team[] teams;

        internal sealed class Team
        {
            public NativeArray<int> goals, heap, heapPositions, proof;
            public NativeArray<float> distances;
            public JobHandle handle;
            public bool scheduled;
            public GpuNavGoal goal;
            public int scheduledFrame, goalChangedFrame;
            public double scheduledTime;
        }

        public GpuNavSeedWorkspace(GpuNavFieldGraph graph, int teamCount)
        {
            resolution = graph.Resolution;
            int n = graph.CellCount, r = resolution;
            try
            {
                neighbours = new NativeArray<byte>(n, Allocator.Persistent);
                costs = new NativeArray<float>(n * 4, Allocator.Persistent);
                var nb = new byte[n]; var cs = new float[n * 4];
                for (int c = 0; c < n; c++)
                {
                    int x = c % r, z = c / r;
                    for (int d = 0; d < 4; d++)
                    {
                        cs[c * 4 + d] = graph.EdgeCost(c, d);
                        if (graph.EdgeBits(c, d) == GpuNavFieldGraph.NoEdge) continue;
                        int nx = x + GpuNavFieldGraph.StepX[d], nz = z + GpuNavFieldGraph.StepZ[d];
                        if (nx < 0 || nz < 0 || nx >= r || nz >= r) continue; // FromGrids never stores such an edge
                        nb[c] |= (byte)(1 << d);
                        nb[nz * r + nx] |= (byte)(1 << (d + 4));
                    }
                }
                neighbours.CopyFrom(nb); costs.CopyFrom(cs);
                teams = new Team[teamCount];
                for (int t = 0; t < teamCount; t++)
                    teams[t] = new Team
                    {
                        goals = new NativeArray<int>(n, Allocator.Persistent, NativeArrayOptions.UninitializedMemory),
                        heap = new NativeArray<int>(n, Allocator.Persistent, NativeArrayOptions.UninitializedMemory),
                        heapPositions = new NativeArray<int>(n, Allocator.Persistent, NativeArrayOptions.UninitializedMemory),
                        proof = new NativeArray<int>(1, Allocator.Persistent),
                        distances = new NativeArray<float>(n, Allocator.Persistent, NativeArrayOptions.UninitializedMemory),
                    };
            }
            catch { Dispose(); throw; }
        }

        public long CpuBytes => (long)resolution * resolution * (1 + 16 + (teams != null ? teams.Length : 0) * 16);

        /// <summary>Fills the team's goal list; returns the count (0 = no goal: everything Unreachable).</summary>
        public int WriteGoals(GpuNavFieldGraph graph, in GpuNavGoal goal, Team team)
        {
            int r = graph.Resolution, count = 0;
            switch (goal.mode)
            {
                case GpuNavGoal.Point:
                    if (goal.cell >= 0 && goal.cell < graph.CellCount && graph.Walkable[goal.cell] != 0) team.goals[count++] = goal.cell;
                    break;
                case GpuNavGoal.Line:
                    if (goal.cells != null)
                        foreach (int c in goal.cells)
                            if (c >= 0 && c < graph.CellCount && graph.Walkable[c] != 0) team.goals[count++] = c;
                    break;
                case GpuNavGoal.Area:
                {
                    int x0 = Mathf.Max(0, Mathf.FloorToInt((goal.rect.x - goal.rect.z - graph.Origin.x) / graph.CellSize) - 1);
                    int x1 = Mathf.Min(r - 1, Mathf.CeilToInt((goal.rect.x + goal.rect.z - graph.Origin.x) / graph.CellSize) + 1);
                    int z0 = Mathf.Max(0, Mathf.FloorToInt((goal.rect.y - goal.rect.w - graph.Origin.y) / graph.CellSize) - 1);
                    int z1 = Mathf.Min(r - 1, Mathf.CeilToInt((goal.rect.y + goal.rect.w - graph.Origin.y) / graph.CellSize) + 1);
                    for (int z = z0; z <= z1; z++)
                        for (int x = x0; x <= x1; x++)
                        {
                            int c = z * r + x;
                            if (graph.Walkable[c] != 0 && GpuNavFieldReference.IsGoal(graph, goal, x, z, c)) team.goals[count++] = c;
                        }
                    break;
                }
                case GpuNavGoal.Density:
                {
                    if (goal.density == null) break;
                    int cr = goal.coarseResolution;
                    for (int cc = 0; cc < goal.density.Length && cc < cr * cr; cc++)
                    {
                        if (goal.density[cc] == 0) continue;
                        int cx = cc % cr, cz = cc / cr;
                        for (int k = 0; k < 4; k++)
                        {
                            int c = (cz * 2 + (k >> 1)) * r + cx * 2 + (k & 1);
                            if (graph.Walkable[c] != 0) team.goals[count++] = c;
                        }
                    }
                    break;
                }
            }
            return count;
        }

        public GpuNavSeedJob Job(Team team, int goalCount, GpuNavMetric metric) => new GpuNavSeedJob
        {
            resolution = resolution, goalCount = goalCount, metric = (int)metric, neighbours = neighbours, costs = costs, goalCells = team.goals,
            distances = team.distances, heap = team.heap, heapPositions = team.heapPositions, proof = team.proof,
        };

        public void Dispose()
        {
            if (teams != null)
                foreach (var t in teams)
                {
                    if (t == null) continue;
                    if (t.scheduled) { t.handle.Complete(); t.scheduled = false; }
                    if (t.goals.IsCreated) t.goals.Dispose();
                    if (t.heap.IsCreated) t.heap.Dispose();
                    if (t.heapPositions.IsCreated) t.heapPositions.Dispose();
                    if (t.proof.IsCreated) t.proof.Dispose();
                    if (t.distances.IsCreated) t.distances.Dispose();
                }
            if (neighbours.IsCreated) neighbours.Dispose();
            if (costs.IsCreated) costs.Dispose();
        }
    }
}
