using System;
using System.Collections.Generic;
using UnityEngine;

namespace MassEngine
{
    /// <summary>
    /// GPU nav field (N series, Docs/GPU-NAVFIELD-N0-20261013.md): the fine (half cell size, double resolution)
    /// navigation graph in the layout GpuNavField.compute reads. A fine cell is walkable only when it is walkable on
    /// the fine grid AND its coarse parent is walkable (collision still uses the coarse mask, N0 section 3.6), so the
    /// fine field never routes through a gap the integrator refuses.
    /// Edges: 2 uints per cell, half-precision costs of the four stored directions 0..3 = (+x), (+z), (+x+z), (-x+z)
    /// (same order as TerrainNavigationGrid); 0x7C00 (half +inf) = no edge. Reverse directions read the neighbour.
    /// </summary>
    public sealed class GpuNavFieldGraph
    {
        public const float Unreachable = 1e30f;
        public const ushort NoEdge = 0x7C00;
        public static readonly int[] StepX = { 1, 0, 1, -1 };
        public static readonly int[] StepZ = { 0, 1, 1, 1 };

        public int Resolution { get; }
        public int CellCount { get; }
        public Vector2 Origin { get; }
        public float CellSize { get; }
        public uint[] Walkable { get; }
        public uint[] Edges { get; }

        public GpuNavFieldGraph(int resolution, Vector2 origin, float cellSize, uint[] walkable, uint[] edges)
        {
            if (resolution <= 0 || resolution > 4096) throw new ArgumentOutOfRangeException(nameof(resolution));
            if (!(cellSize > 0) || float.IsInfinity(cellSize)) throw new ArgumentOutOfRangeException(nameof(cellSize));
            int n = resolution * resolution;
            if (walkable == null || walkable.Length != n) throw new ArgumentException("walkable length");
            if (edges == null || edges.Length != 2 * n) throw new ArgumentException("edges length");
            Resolution = resolution; CellCount = n; Origin = origin; CellSize = cellSize; Walkable = walkable; Edges = edges;
        }

        public ushort EdgeBits(int cell, int direction)
        {
            uint word = Edges[2 * cell + (direction >> 1)];
            return (ushort)((direction & 1) == 0 ? word & 0xFFFF : word >> 16);
        }

        public float EdgeCost(int cell, int direction) => Mathf.HalfToFloat(EdgeBits(cell, direction));

        public static uint Pack(ushort low, ushort high) => low | ((uint)high << 16);

        /// <summary>Builds the fine graph. The fine grid must be the coarse grid at half cell size and double resolution.</summary>
        public static GpuNavFieldGraph FromGrids(TerrainNavigationGrid fine, TerrainNavigationGrid coarse)
        {
            if (fine == null) throw new ArgumentNullException(nameof(fine));
            if (coarse == null) throw new ArgumentNullException(nameof(coarse));
            if (fine.ResolutionX != fine.ResolutionZ || coarse.ResolutionX != coarse.ResolutionZ || fine.ResolutionX != 2 * coarse.ResolutionX)
                throw new ArgumentException("Fine grid must be square and exactly twice the coarse resolution.");
            if (!fine.Origin.Equals(coarse.Origin) || fine.CellSize * 2f != coarse.CellSize)
                throw new ArgumentException("Fine grid must share the coarse origin and use half its cell size.");
            int r = fine.ResolutionX, n = r * r, cr = coarse.ResolutionX;
            fine.CopyGraph(out byte[] neighbours, out double[] costs);
            uint[] fineWalk = fine.CopyWalkable(), coarseWalk = coarse.CopyWalkable();
            var walk = new uint[n];
            for (int z = 0; z < r; z++)
                for (int x = 0; x < r; x++)
                {
                    int c = z * r + x;
                    walk[c] = fineWalk[c] != 0 && coarseWalk[(z >> 1) * cr + (x >> 1)] != 0 ? 1u : 0u;
                }
            var edges = new uint[2 * n];
            var h = new ushort[4];
            for (int z = 0; z < r; z++)
                for (int x = 0; x < r; x++)
                {
                    int c = z * r + x;
                    for (int d = 0; d < 4; d++)
                    {
                        h[d] = NoEdge;
                        if (walk[c] == 0 || (neighbours[c] & (1 << d)) == 0) continue;
                        int nx = x + StepX[d], nz = z + StepZ[d];
                        if (nx < 0 || nz < 0 || nx >= r || nz >= r) continue;
                        if (walk[nz * r + nx] == 0) continue;
                        if (StepX[d] != 0 && StepZ[d] != 0 && (walk[z * r + nx] == 0 || walk[nz * r + x] == 0)) continue;
                        double cost = costs[c * 4 + d];
                        if (!(cost > 0) || double.IsInfinity(cost)) continue;
                        ushort bits = Mathf.FloatToHalf((float)cost);
                        if (bits == 0 || bits >= NoEdge) continue;
                        h[d] = bits;
                    }
                    edges[2 * c] = Pack(h[0], h[1]);
                    edges[2 * c + 1] = Pack(h[2], h[3]);
                }
            return new GpuNavFieldGraph(r, fine.Origin, fine.CellSize, walk, edges);
        }
    }

    /// <summary>Goal rule of one field layer; mirrors the NAV_GOAL_* modes of GpuNavField.compute.</summary>
    public struct GpuNavGoal
    {
        public const int None = 0, Density = 1, Point = 2, Area = 3, Line = 4;
        public int mode;
        public int cell;          // Point: fine cell index (-1 = none). Line: the snapped target cell
        public Vector4 rect;      // Area: centre xz, half size xz (world). Line: line centre xz, along-line unit xz
        public Vector4 line;      // Line: tMin, tMax (m along the line), forward unit xz (target - centroid)
        public uint[] density;    // Density (reference only): coarse density slice, goal where > 0
        public uint[] bits;       // Line: goal cell bit mask (CellCount / 32 words, uploaded to the kernel)
        public int[] cells;       // Line: the goal cells (ascending)
        public float depth;       // Line: estimated depth of the settled block behind the line (m); not part of Equals
        public int coarseResolution;

        public bool Equals(GpuNavGoal other) => mode == other.mode && cell == other.cell && rect == other.rect && line == other.line;
    }

    /// <summary>
    /// Managed reference of GpuNavField.compute (tests and the in-Editor N1 check). Relax performs exactly the
    /// kernel's operations in the kernel's order, so a GPU pass must match it bit for bit.
    /// </summary>
    /// <summary>Graph = 8-neighbour shortest path (N1/N2). Eikonal = 8-neighbour triangle update (N3, round wavefronts).</summary>
    public enum GpuNavMetric { Graph = 0, Eikonal = 1 }

    public static class GpuNavFieldReference
    {
        static readonly int[] TriA = { 0, 0, 1, 1, 4, 4, 5, 5 };
        static readonly int[] TriB = { 2, 7, 2, 3, 3, 6, 6, 7 };
        const float FiniteCost = 1e20f;

        /// <summary>
        /// One cell update from 8 neighbour values v and edge costs w (direction order +x,+z,+x+z,-x+z,-x,-z,-x-z,+x-z;
        /// missing neighbour: v = Unreachable, w = +inf). Same operations, same order as NavFieldRelax and GpuNavSeedJob.
        /// </summary>
        public static float CellUpdate(float[] v, float[] w, GpuNavMetric metric)
        {
            float d = GpuNavFieldGraph.Unreachable;
            for (int e = 0; e < 8; e++) d = Min(d, v[e] + w[e]);
            if (metric == GpuNavMetric.Eikonal)
                for (int t = 0; t < 8; t++)
                {
                    float a = v[TriA[t]], b = v[TriB[t]], wa = w[TriA[t]], wb = w[TriB[t]];
                    if (!(wa < FiniteCost && wb < FiniteCost && a < GpuNavFieldGraph.Unreachable && b < GpuNavFieldGraph.Unreachable)) continue;
                    float dd = a - b, dd2 = dd * dd, wa2 = wa * wa;
                    if (dd >= 0f && dd2 * 2f <= wa2) d = Min(d, a + Mathf.Sqrt(wa2 - dd2));
                }
            return Min(d, GpuNavFieldGraph.Unreachable);
        }

        /// <summary>Gathers the 8 neighbour values/costs of cell (x, z) as NavFieldRelax does.</summary>
        public static void Gather(GpuNavFieldGraph g, float[] input, int x, int z, float[] v, float[] w)
        {
            int r = g.Resolution, c = z * r + x;
            for (int k = 0; k < 8; k++) { v[k] = GpuNavFieldGraph.Unreachable; w[k] = float.PositiveInfinity; }
            if (x + 1 < r) { v[0] = input[c + 1]; w[0] = g.EdgeCost(c, 0); }
            if (z + 1 < r) { v[1] = input[c + r]; w[1] = g.EdgeCost(c, 1); }
            if (x + 1 < r && z + 1 < r) { v[2] = input[c + r + 1]; w[2] = g.EdgeCost(c, 2); }
            if (x > 0 && z + 1 < r) { v[3] = input[c + r - 1]; w[3] = g.EdgeCost(c, 3); }
            if (x > 0) { v[4] = input[c - 1]; w[4] = g.EdgeCost(c - 1, 0); }
            if (z > 0) { v[5] = input[c - r]; w[5] = g.EdgeCost(c - r, 1); }
            if (x > 0 && z > 0) { v[6] = input[c - r - 1]; w[6] = g.EdgeCost(c - r - 1, 2); }
            if (x + 1 < r && z > 0) { v[7] = input[c - r + 1]; w[7] = g.EdgeCost(c - r + 1, 3); }
        }

        /// <summary>N4: the crowd multiplier of one cell, same operations as NavFieldRelax (gain 0 = no multiplication).</summary>
        public static float CrowdMultiplier(float ema, float gain, float invFull, float cap)
        {
            float crowd = Min(ema * invFull, cap);
            return 1f + gain * crowd;
        }

        /// <summary>N4 reference: as Relax(metric) with every edge cost of cell c scaled by CrowdMultiplier(ema[c]).</summary>
        public static void Relax(GpuNavFieldGraph g, in GpuNavGoal goal, float[] input, float[] output, GpuNavMetric metric,
            float[] ema, float gain, float invFull, float cap)
        {
            if (!(gain > 0f)) { Relax(g, goal, input, output, metric); return; }
            int r = g.Resolution; var v = new float[8]; var w = new float[8];
            for (int z = 0; z < r; z++)
                for (int x = 0; x < r; x++)
                {
                    int c = z * r + x;
                    if (g.Walkable[c] == 0) { output[c] = GpuNavFieldGraph.Unreachable; continue; }
                    if (IsGoal(g, goal, x, z, c)) { output[c] = 0f; continue; }
                    Gather(g, input, x, z, v, w);
                    float m = CrowdMultiplier(ema[c], gain, invFull, cap);
                    for (int k = 0; k < 8; k++) w[k] = w[k] * m;
                    output[c] = CellUpdate(v, w, metric);
                }
        }

        public static void Relax(GpuNavFieldGraph g, in GpuNavGoal goal, float[] input, float[] output, GpuNavMetric metric)
        {
            if (metric == GpuNavMetric.Graph) { Relax(g, goal, input, output); return; }
            int r = g.Resolution; var v = new float[8]; var w = new float[8];
            for (int z = 0; z < r; z++)
                for (int x = 0; x < r; x++)
                {
                    int c = z * r + x;
                    if (g.Walkable[c] == 0) { output[c] = GpuNavFieldGraph.Unreachable; continue; }
                    if (IsGoal(g, goal, x, z, c)) { output[c] = 0f; continue; }
                    Gather(g, input, x, z, v, w);
                    output[c] = CellUpdate(v, w, metric);
                }
        }

        public static int Converge(GpuNavFieldGraph g, in GpuNavGoal goal, ref float[] field, int maxPasses, GpuNavMetric metric)
        {
            var other = new float[field.Length];
            for (int pass = 1; pass <= maxPasses; pass++)
            {
                Relax(g, goal, field, other, metric);
                bool same = true;
                for (int i = 0; i < field.Length && same; i++) same = field[i] == other[i];
                var t = field; field = other; other = t;
                if (same) return pass;
            }
            return maxPasses;
        }
        public static bool IsGoal(GpuNavFieldGraph g, in GpuNavGoal goal, int x, int z, int c)
        {
            switch (goal.mode)
            {
                case GpuNavGoal.Density:
                    return goal.density != null && goal.density[(z >> 1) * goal.coarseResolution + (x >> 1)] > 0;
                case GpuNavGoal.Point:
                    return c == goal.cell;
                case GpuNavGoal.Line:
                    return goal.bits != null && ((goal.bits[c >> 5] >> (c & 31)) & 1u) != 0;
                case GpuNavGoal.Area:
                {
                    float cx = g.Origin.x + (x + .5f) * g.CellSize, cz = g.Origin.y + (z + .5f) * g.CellSize;
                    return Mathf.Abs(cx - goal.rect.x) <= goal.rect.z && Mathf.Abs(cz - goal.rect.y) <= goal.rect.w;
                }
                default: return false;
            }
        }

        static float Min(float a, float b) => b < a ? b : a;

        /// <summary>One Jacobi pass: output[c] = 0 at goals, min over 8 neighbours of input[n] + w(c,n) elsewhere.</summary>
        public static void Relax(GpuNavFieldGraph g, in GpuNavGoal goal, float[] input, float[] output)
        {
            int r = g.Resolution;
            for (int z = 0; z < r; z++)
                for (int x = 0; x < r; x++)
                {
                    int c = z * r + x;
                    float d;
                    if (g.Walkable[c] == 0) d = GpuNavFieldGraph.Unreachable;
                    else if (IsGoal(g, goal, x, z, c)) d = 0f;
                    else
                    {
                        d = GpuNavFieldGraph.Unreachable;
                        if (x + 1 < r) d = Min(d, input[c + 1] + g.EdgeCost(c, 0));
                        if (z + 1 < r) d = Min(d, input[c + r] + g.EdgeCost(c, 1));
                        if (x + 1 < r && z + 1 < r) d = Min(d, input[c + r + 1] + g.EdgeCost(c, 2));
                        if (x > 0 && z + 1 < r) d = Min(d, input[c + r - 1] + g.EdgeCost(c, 3));
                        if (x > 0) d = Min(d, input[c - 1] + g.EdgeCost(c - 1, 0));
                        if (z > 0) d = Min(d, input[c - r] + g.EdgeCost(c - r, 1));
                        if (x > 0 && z > 0) d = Min(d, input[c - r - 1] + g.EdgeCost(c - r - 1, 2));
                        if (x + 1 < r && z > 0) d = Min(d, input[c - r + 1] + g.EdgeCost(c - r + 1, 3));
                        d = Min(d, GpuNavFieldGraph.Unreachable);
                    }
                    output[c] = d;
                }
        }

        public static float[] Filled(int n, float value) { var a = new float[n]; for (int i = 0; i < n; i++) a[i] = value; return a; }

        /// <summary>Runs passes until nothing changes (or maxPasses). Returns the passes used.</summary>
        public static int Converge(GpuNavFieldGraph g, in GpuNavGoal goal, ref float[] field, int maxPasses)
        {
            var other = new float[field.Length];
            for (int pass = 1; pass <= maxPasses; pass++)
            {
                Relax(g, goal, field, other);
                bool same = true;
                for (int i = 0; i < field.Length && same; i++) same = field[i] == other[i];
                var t = field; field = other; other = t;
                if (same) return pass;
            }
            return maxPasses;
        }

        /// <summary>Exact shortest distances on the same graph and costs (double accumulation), Unreachable elsewhere.</summary>
        public static float[] Dijkstra(GpuNavFieldGraph g, in GpuNavGoal goal)
        {
            int r = g.Resolution, n = g.CellCount;
            var dist = new double[n];
            for (int i = 0; i < n; i++) dist[i] = double.PositiveInfinity;
            var heap = new List<(double d, int c)>();
            void Push(double d, int c)
            {
                heap.Add((d, c)); int i = heap.Count - 1;
                while (i > 0) { int p = (i - 1) / 2; if (heap[p].d <= heap[i].d) break; (heap[p], heap[i]) = (heap[i], heap[p]); i = p; }
            }
            (double d, int c) Pop()
            {
                var top = heap[0]; var last = heap[heap.Count - 1]; heap.RemoveAt(heap.Count - 1);
                if (heap.Count > 0)
                {
                    heap[0] = last; int i = 0;
                    while (true)
                    {
                        int l = 2 * i + 1, m = i;
                        if (l < heap.Count && heap[l].d < heap[m].d) m = l;
                        if (l + 1 < heap.Count && heap[l + 1].d < heap[m].d) m = l + 1;
                        if (m == i) break;
                        (heap[m], heap[i]) = (heap[i], heap[m]); i = m;
                    }
                }
                return top;
            }
            for (int z = 0; z < r; z++)
                for (int x = 0; x < r; x++)
                {
                    int c = z * r + x;
                    if (g.Walkable[c] != 0 && IsGoal(g, goal, x, z, c)) { dist[c] = 0; Push(0, c); }
                }
            int[] ox = { 1, 0, 1, -1, -1, 0, -1, 1 }, oz = { 0, 1, 1, 1, 0, -1, -1, -1 };
            while (heap.Count > 0)
            {
                var (d, c) = Pop();
                if (d > dist[c]) continue;
                int x = c % r, z = c / r;
                for (int k = 0; k < 8; k++)
                {
                    int nx = x + ox[k], nz = z + oz[k];
                    if (nx < 0 || nz < 0 || nx >= r || nz >= r) continue;
                    int a = nz * r + nx;
                    if (g.Walkable[a] == 0) continue;
                    float w = k < 4 ? g.EdgeCost(c, k) : g.EdgeCost(a, k - 4);
                    if (float.IsInfinity(w)) continue;
                    double nd = d + w;
                    if (nd < dist[a]) { dist[a] = nd; Push(nd, a); }
                }
            }
            var result = new float[n];
            for (int i = 0; i < n; i++) result[i] = double.IsInfinity(dist[i]) ? GpuNavFieldGraph.Unreachable : (float)dist[i];
            return result;
        }
    }
}
