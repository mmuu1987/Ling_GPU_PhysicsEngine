using System;
using System.Collections.Generic;

namespace MassEngine.Editor
{
    /// <summary>
    /// Multi-pose quadric-error (QEM) half-edge-collapse simplifier for VAT meshes.
    ///
    /// Contract (why it exists instead of <c>VatLodReducer</c>'s grid clustering):
    /// * Every output vertex IS an original input vertex (half-edge collapse only, no averaging),
    ///   so the far VAT is an exact column subset of the source VAT: no new or blended poses.
    /// * The error metric sums per-frame quadrics over sampled frames from every clip
    ///   (idle/move/attack/death), so the mesh is simplified where it is flat in ALL poses,
    ///   not only in the bind pose.
    /// * UV seams and open boundaries are preserved first (strict pass); only if the target
    ///   cannot be reached that way does a relaxed pass collapse across them.
    /// * Link condition + per-frame flip checks keep the result manifold-ish and unflipped in
    ///   every sampled pose.
    ///
    /// Pure System code (no UnityEngine) so it is unit-testable outside the Editor.
    /// </summary>
    public static class VatQemSimplifier
    {
        public sealed class Options
        {
            /// <summary>Stop when the alive triangle count is at or below this.</summary>
            public int targetTriangles = 200;
            /// <summary>Connected components whose surface is below this fraction of the total are dropped
            /// before simplification (eyes, teeth, buttons). The largest component is never dropped.</summary>
            public double dropComponentAreaFraction = 0.002;
            /// <summary>Quadric weight of open-boundary constraint planes (relative, times edge length squared).</summary>
            public double boundaryWeight = 4.0;
            /// <summary>Quadric weight of UV-seam constraint planes (relative, times edge length squared).</summary>
            public double seamWeight = 1.0;
            /// <summary>Reject a collapse if any sampled frame turns a face normal by more than ~90 degrees.</summary>
            public double minFrameCosine = 0.0;
            /// <summary>Reject a collapse if the frame-averaged normal cosine falls below this.</summary>
            public double minMeanCosine = 0.3;
            /// <summary>Relative weld tolerance (fraction of the pose-0 bounding diagonal).</summary>
            public double weldTolerance = 1e-4;
            /// <summary>Allow the relaxed (seam/boundary crossing) pass when the strict pass gets stuck.</summary>
            public bool allowRelaxedPass = true;
            /// <summary>Strict pass keeps UV seams as locked lines (vertices slide only along them). When false,
            /// only open boundaries are locked and seams are crossed with nearest-UV attribute matching.</summary>
            public bool preserveSeams = true;
            /// <summary>When a collapse would give a surviving triangle a corner from a different UV chart (the cause of
            /// texture streaks), add a split vertex instead: position/VAT column copied from the kept vertex, UV taken from
            /// the removed corner (same chart as the rest of the triangle). Costs a few extra vertices, no shape change.</summary>
            public bool splitChartCorners = true;
            /// <summary>false: half-edge collapse, every output vertex is an original vertex and its VAT column is copied.
            /// true: the surviving vertex is re-placed per frame at the regularised minimum of the accumulated quadric
            /// (classic QEM placement; avoids the silhouette shrink of endpoint-only collapse at extreme ratios).
            /// Use ComputeFramePositions to evaluate the placement for any frame.</summary>
            public bool optimalPlacement = false;
            /// <summary>Tikhonov weight (relative to the mean quadric diagonal) pulling optimal placement toward the kept vertex.</summary>
            public double placementRegularization = 1e-3;
        }

        public sealed class Result
        {
            /// <summary>Kept source vertex indices, ascending. New vertex k == source vertex keptSourceVertices[k].</summary>
            public int[] keptSourceVertices;
            /// <summary>Triangles in NEW vertex indices.</summary>
            public int[] triangles;
            public int sourceVertices;
            public int sourceTriangles;
            public int weldedNodes;
            public int droppedComponents;
            public int droppedComponentTriangles;
            public int strictCollapses;
            public int relaxedCollapses;
            public int heapRebuilds;
            public double maxCollapseCost;
            public bool reachedTarget;
            /// <summary>Why candidate collapses were rejected (diagnostics): link, nonManifoldEdge, border, duplicateFace, flip, degenerate.</summary>
            public Dictionary<string, int> rejections = new Dictionary<string, int>();
            public bool optimalPlacement;
            /// <summary>Source vertex -> welded node (position-identical source vertices share a node).</summary>
            public int[] weldNodeOfSource;
            /// <summary>For each kept vertex: every welded node merged into its node (its quadric support).</summary>
            public int[][] keptClusterNodes;
            /// <summary>For each kept vertex: every source vertex (attribute) merged into it (normal support).</summary>
            public int[][] keptAttributeMembers;
            /// <summary>For each kept vertex: source vertex whose UV (and other non-position attributes) it uses.
            /// Equals keptSourceVertices[k] except for chart split vertices.</summary>
            public int[] keptUvSourceVertices;
            /// <summary>Number of kept vertices that are chart splits (position of one source vertex, UV of another).</summary>
            public int chartSplitVertices;
            /// <summary>Triangles whose corners' UVs come from more than one UV chart (should be 0 with splitChartCorners).</summary>
            public int crossChartTriangles;
            internal Options options;
            public int TriangleCount { get { return triangles == null ? 0 : triangles.Length / 3; } }
            public int VertexCount { get { return keptSourceVertices == null ? 0 : keptSourceVertices.Length; } }
        }

        internal static double Evaluate(double[] q, double x, double y, double z)
        {
            return q[0] * x * x + 2 * q[1] * x * y + 2 * q[2] * x * z + 2 * q[3] * x
                 + q[4] * y * y + 2 * q[5] * y * z + 2 * q[6] * y
                 + q[7] * z * z + 2 * q[8] * z + q[9];
        }

        /// <summary>argmin x^T Q x + lambda |x - anchor|^2 with lambda = reg * trace(A)/3; x,y,z hold the anchor on entry.</summary>
        internal static void SolvePlacement(double[] q, double regularization, ref double x, ref double y, ref double z)
        {
            double trace = (q[0] + q[4] + q[7]) / 3.0;
            double lambda = Math.Max(1e-12, regularization * trace);
            double a00 = q[0] + lambda, a01 = q[1], a02 = q[2], a11 = q[4] + lambda, a12 = q[5], a22 = q[7] + lambda;
            double b0 = -q[3] + lambda * x, b1 = -q[6] + lambda * y, b2 = -q[8] + lambda * z;
            double c00 = a11 * a22 - a12 * a12, c01 = a02 * a12 - a01 * a22, c02 = a01 * a12 - a02 * a11;
            double det = a00 * c00 + a01 * c01 + a02 * c02;
            if (Math.Abs(det) < 1e-30 || double.IsNaN(det)) return;
            double c11 = a00 * a22 - a02 * a02, c12 = a01 * a02 - a00 * a12, c22 = a00 * a11 - a01 * a01;
            double nx = (c00 * b0 + c01 * b1 + c02 * b2) / det;
            double ny = (c01 * b0 + c11 * b1 + c12 * b2) / det;
            double nz = (c02 * b0 + c12 * b1 + c22 * b2) / det;
            if (double.IsNaN(nx) || double.IsNaN(ny) || double.IsNaN(nz) || double.IsInfinity(nx) || double.IsInfinity(ny) || double.IsInfinity(nz)) return;
            x = nx; y = ny; z = nz;
        }

        /// <summary>Positions (3 floats per kept vertex) for ONE frame pose of the source mesh. Endpoint mode copies the kept
        /// vertex; optimal mode re-solves the quadric of the kept vertex's cluster in this frame (same planes and constraint
        /// weights as the simplifier), so every VAT frame - not only the sampled ones - gets a consistent placement.</summary>
        public static float[] ComputeFramePositions(float[] pose, int[] sourceTriangles, Result result)
        {
            if (pose == null || sourceTriangles == null || result == null) throw new ArgumentNullException();
            int kept = result.keptSourceVertices.Length;
            var output = new float[kept * 3];
            if (!result.optimalPlacement)
            {
                for (int k = 0; k < kept; k++)
                    for (int axis = 0; axis < 3; axis++) output[3 * k + axis] = pose[3 * result.keptSourceVertices[k] + axis];
                return output;
            }
            Options options = result.options ?? new Options();
            int[] node = result.weldNodeOfSource;
            int nodes = 0;
            foreach (int n in node) nodes = Math.Max(nodes, n + 1);
            var q = new double[nodes * 10];
            var edgeUse = new Dictionary<long, int>();
            var edgeAttr = new Dictionary<long, long>();
            var seam = new HashSet<long>();
            for (int t = 0; t + 2 < sourceTriangles.Length; t += 3)
            {
                int a = sourceTriangles[t], b = sourceTriangles[t + 1], c = sourceTriangles[t + 2];
                int na = node[a], nb = node[b], nc = node[c];
                if (na == nb || nb == nc || nc == na) continue;
                double nx, ny, nz, d, w;
                if (!Plane(pose, a, b, c, out nx, out ny, out nz, out d, out w)) continue;
                AddPlane(q, na, nx, ny, nz, d, w); AddPlane(q, nb, nx, ny, nz, d, w); AddPlane(q, nc, nx, ny, nz, d, w);
                int[] tri = { a, b, c };
                for (int k = 0; k < 3; k++)
                {
                    int p = tri[k], r = tri[(k + 1) % 3];
                    long key = EdgeKey(node[p], node[r]);
                    long attr = node[p] < node[r] ? ((long)p << 32) | (uint)r : ((long)r << 32) | (uint)p;
                    int count;
                    edgeUse.TryGetValue(key, out count);
                    edgeUse[key] = count + 1;
                    long first;
                    if (!edgeAttr.TryGetValue(key, out first)) edgeAttr[key] = attr;
                    else if (first != attr) seam.Add(key);
                }
            }
            for (int t = 0; t + 2 < sourceTriangles.Length; t += 3)
            {
                int[] tri = { sourceTriangles[t], sourceTriangles[t + 1], sourceTriangles[t + 2] };
                if (node[tri[0]] == node[tri[1]] || node[tri[1]] == node[tri[2]] || node[tri[0]] == node[tri[2]]) continue;
                for (int k = 0; k < 3; k++)
                {
                    int p = tri[k], r = tri[(k + 1) % 3], o = tri[(k + 2) % 3];
                    long key = EdgeKey(node[p], node[r]);
                    int use = edgeUse[key];
                    double weight = use == 1 ? options.boundaryWeight : (use == 2 && seam.Contains(key)) ? options.seamWeight : 0;
                    if (weight <= 0) continue;
                    double fx, fy, fz, unusedD, unusedW;
                    if (!Plane(pose, p, r, o, out fx, out fy, out fz, out unusedD, out unusedW)) continue;
                    double ex = pose[3 * r] - pose[3 * p], ey = pose[3 * r + 1] - pose[3 * p + 1], ez = pose[3 * r + 2] - pose[3 * p + 2];
                    double mx = ey * fz - ez * fy, my = ez * fx - ex * fz, mz = ex * fy - ey * fx;
                    double ml = Math.Sqrt(mx * mx + my * my + mz * mz);
                    if (ml < 1e-20) continue;
                    mx /= ml; my /= ml; mz /= ml;
                    double md = -(mx * pose[3 * p] + my * pose[3 * p + 1] + mz * pose[3 * p + 2]);
                    double mw = weight * (ex * ex + ey * ey + ez * ez);
                    AddPlane(q, node[p], mx, my, mz, md, mw); AddPlane(q, node[r], mx, my, mz, md, mw);
                }
            }
            var sum = new double[10];
            for (int k = 0; k < kept; k++)
            {
                Array.Clear(sum, 0, 10);
                foreach (int n in result.keptClusterNodes[k])
                    for (int i = 0; i < 10; i++) sum[i] += q[n * 10 + i];
                int v = result.keptSourceVertices[k];
                double x = pose[3 * v], y = pose[3 * v + 1], z = pose[3 * v + 2];
                SolvePlacement(sum, options.placementRegularization, ref x, ref y, ref z);
                output[3 * k] = (float)x; output[3 * k + 1] = (float)y; output[3 * k + 2] = (float)z;
            }
            return output;
        }

        /// <summary>Normals (3 floats per kept vertex) for one frame: endpoint mode copies; optimal mode averages the
        /// normals of every source vertex merged into the kept vertex (falls back to the kept vertex's own normal).</summary>
        public static float[] ComputeFrameNormals(float[] normals, Result result)
        {
            int kept = result.keptSourceVertices.Length;
            var output = new float[kept * 3];
            for (int k = 0; k < kept; k++)
            {
                int v = result.keptSourceVertices[k];
                double x = 0, y = 0, z = 0;
                if (result.optimalPlacement && result.keptAttributeMembers != null)
                    foreach (int m in result.keptAttributeMembers[k]) { x += normals[3 * m]; y += normals[3 * m + 1]; z += normals[3 * m + 2]; }
                double length = Math.Sqrt(x * x + y * y + z * z);
                if (length < 1e-8) { x = normals[3 * v]; y = normals[3 * v + 1]; z = normals[3 * v + 2]; length = Math.Sqrt(x * x + y * y + z * z); }
                if (length < 1e-8) { x = 0; y = 1; z = 0; length = 1; }
                output[3 * k] = (float)(x / length); output[3 * k + 1] = (float)(y / length); output[3 * k + 2] = (float)(z / length);
            }
            return output;
        }

        private static long EdgeKey(int a, int b)
        {
            return a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
        }

        private static bool Plane(float[] pose, int a, int b, int c, out double nx, out double ny, out double nz, out double d, out double weight)
        {
            double ax = pose[3 * a], ay = pose[3 * a + 1], az = pose[3 * a + 2];
            double ux = pose[3 * b] - ax, uy = pose[3 * b + 1] - ay, uz = pose[3 * b + 2] - az;
            double vx = pose[3 * c] - ax, vy = pose[3 * c + 1] - ay, vz = pose[3 * c + 2] - az;
            nx = uy * vz - uz * vy; ny = uz * vx - ux * vz; nz = ux * vy - uy * vx;
            double length = Math.Sqrt(nx * nx + ny * ny + nz * nz);
            d = 0; weight = 0;
            if (length < 1e-20) return false;
            weight = 0.5 * length;
            nx /= length; ny /= length; nz /= length;
            d = -(nx * ax + ny * ay + nz * az);
            return true;
        }

        private static void AddPlane(double[] q, int node, double a, double b, double c, double d, double w)
        {
            int o = node * 10;
            q[o] += w * a * a; q[o + 1] += w * a * b; q[o + 2] += w * a * c; q[o + 3] += w * a * d;
            q[o + 4] += w * b * b; q[o + 5] += w * b * c; q[o + 6] += w * b * d;
            q[o + 7] += w * c * c; q[o + 8] += w * c * d;
            q[o + 9] += w * d * d;
        }

        public sealed class Deviation
        {
            public double max;
            public double mean;
            public int worstFrame;
        }

        /// <summary>One-sided distance from every source vertex used by a source triangle to the simplified
        /// surface, per pose; reports the worst and mean over all poses (same units as the poses).</summary>
        public static Deviation MeasureDeviation(float[][] poses, int[] sourceTriangles, Result result)
        {
            if (poses == null || result == null || result.triangles == null) throw new ArgumentNullException();
            var used = new HashSet<int>(sourceTriangles);
            var output = new Deviation();
            double sum = 0;
            long count = 0;
            for (int f = 0; f < poses.Length; f++)
            {
                float[] p = poses[f];
                // Append the placed kept positions after the source vertices so one index space serves both.
                float[] placed = ComputeFramePositions(p, sourceTriangles, result);
                var combined = new float[p.Length + placed.Length];
                Array.Copy(p, combined, p.Length);
                Array.Copy(placed, 0, combined, p.Length, placed.Length);
                int offset = p.Length / 3;
                foreach (int v in used)
                {
                    double best = double.MaxValue;
                    for (int t = 0; t < result.triangles.Length && best > 0; t += 3)
                    {
                        int a = offset + result.triangles[t];
                        int b = offset + result.triangles[t + 1];
                        int c = offset + result.triangles[t + 2];
                        best = Math.Min(best, PointTriangleDistance(combined, v, a, b, c));
                    }
                    if (best == double.MaxValue) best = 0;
                    sum += best;
                    count++;
                    if (best > output.max) { output.max = best; output.worstFrame = f; }
                }
            }
            output.mean = count == 0 ? 0 : sum / count;
            return output;
        }

        private static double PointTriangleDistance(float[] pose, int pv, int ia, int ib, int ic)
        {
            double px = pose[3 * pv], py = pose[3 * pv + 1], pz = pose[3 * pv + 2];
            double ax = pose[3 * ia], ay = pose[3 * ia + 1], az = pose[3 * ia + 2];
            double bx = pose[3 * ib], by = pose[3 * ib + 1], bz = pose[3 * ib + 2];
            double cx = pose[3 * ic], cy = pose[3 * ic + 1], cz = pose[3 * ic + 2];
            double abx = bx - ax, aby = by - ay, abz = bz - az, acx = cx - ax, acy = cy - ay, acz = cz - az;
            double apx = px - ax, apy = py - ay, apz = pz - az;
            double d1 = abx * apx + aby * apy + abz * apz, d2 = acx * apx + acy * apy + acz * apz;
            double qx, qy, qz;
            if (d1 <= 0 && d2 <= 0) { qx = ax; qy = ay; qz = az; return Dist(px, py, pz, qx, qy, qz); }
            double bpx = px - bx, bpy = py - by, bpz = pz - bz;
            double d3 = abx * bpx + aby * bpy + abz * bpz, d4 = acx * bpx + acy * bpy + acz * bpz;
            if (d3 >= 0 && d4 <= d3) return Dist(px, py, pz, bx, by, bz);
            double vc = d1 * d4 - d3 * d2;
            if (vc <= 0 && d1 >= 0 && d3 <= 0) { double v = d1 / (d1 - d3); return Dist(px, py, pz, ax + v * abx, ay + v * aby, az + v * abz); }
            double cpx = px - cx, cpy = py - cy, cpz = pz - cz;
            double d5 = abx * cpx + aby * cpy + abz * cpz, d6 = acx * cpx + acy * cpy + acz * cpz;
            if (d6 >= 0 && d5 <= d6) return Dist(px, py, pz, cx, cy, cz);
            double vb = d5 * d2 - d1 * d6;
            if (vb <= 0 && d2 >= 0 && d6 <= 0) { double w = d2 / (d2 - d6); return Dist(px, py, pz, ax + w * acx, ay + w * acy, az + w * acz); }
            double va = d3 * d6 - d5 * d4;
            if (va <= 0 && (d4 - d3) >= 0 && (d5 - d6) >= 0)
            {
                double w = (d4 - d3) / ((d4 - d3) + (d5 - d6));
                return Dist(px, py, pz, bx + w * (cx - bx), by + w * (cy - by), bz + w * (cz - bz));
            }
            double denominator = va + vb + vc;
            if (Math.Abs(denominator) < 1e-30) return Dist(px, py, pz, ax, ay, az);
            double vv = vb / denominator, ww = vc / denominator;
            return Dist(px, py, pz, ax + abx * vv + acx * ww, ay + aby * vv + acy * ww, az + abz * vv + acz * ww);
        }

        private static double Dist(double ax, double ay, double az, double bx, double by, double bz)
        {
            double x = ax - bx, y = ay - by, z = az - bz;
            return Math.Sqrt(x * x + y * y + z * z);
        }

        private struct Candidate
        {
            public double cost;
            public int from, to, versionFrom, versionTo;
        }

        private sealed class MinHeap
        {
            private Candidate[] items = new Candidate[1024];
            public int Count { get; private set; }

            public void Clear() { Count = 0; }

            public void Push(Candidate c)
            {
                if (Count == items.Length) Array.Resize(ref items, items.Length * 2);
                int i = Count++;
                while (i > 0)
                {
                    int parent = (i - 1) >> 1;
                    if (items[parent].cost <= c.cost) break;
                    items[i] = items[parent];
                    i = parent;
                }
                items[i] = c;
            }

            public Candidate Pop()
            {
                Candidate top = items[0];
                Candidate last = items[--Count];
                int i = 0;
                while (true)
                {
                    int l = 2 * i + 1;
                    if (l >= Count) break;
                    int r = l + 1;
                    int m = r < Count && items[r].cost < items[l].cost ? r : l;
                    if (items[m].cost >= last.cost) break;
                    items[i] = items[m];
                    i = m;
                }
                if (Count > 0) items[i] = last;
                return top;
            }
        }

        /// <param name="vertexCount">Source vertex count.</param>
        /// <param name="poses">poses[f][3*v + axis]: object-space positions per sampled frame (at least one frame).</param>
        /// <param name="uvs">Optional uv0 as 2*v floats (only used to choose the matching attribute vertex across seams).</param>
        /// <param name="sourceTriangles">Triangle list of source vertex indices.</param>
        public static Result Simplify(int vertexCount, float[][] poses, float[] uvs, int[] sourceTriangles, Options options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (vertexCount <= 0) throw new ArgumentException("vertexCount must be positive.");
            if (poses == null || poses.Length == 0) throw new ArgumentException("At least one pose frame is required.");
            foreach (float[] pose in poses)
                if (pose == null || pose.Length != vertexCount * 3) throw new ArgumentException("Every pose must hold 3 floats per vertex.");
            if (uvs != null && uvs.Length != vertexCount * 2) throw new ArgumentException("uvs must hold 2 floats per vertex.");
            if (sourceTriangles == null || sourceTriangles.Length == 0 || sourceTriangles.Length % 3 != 0)
                throw new ArgumentException("Triangle list is empty or not a multiple of 3.");
            if (options.targetTriangles < 1) throw new ArgumentException("targetTriangles must be >= 1.");
            foreach (int index in sourceTriangles)
                if (index < 0 || index >= vertexCount) throw new ArgumentException("Triangle index out of range.");

            var state = new State(vertexCount, poses, uvs, sourceTriangles, options);
            return state.Run();
        }

        private sealed class State
        {
            private readonly int vertexCount;
            private readonly float[][] poses;
            private readonly float[] uvs;
            private readonly Options options;
            private readonly int frames;

            // Attribute vertex (= source vertex) -> welded position node.
            private readonly int[] nodeOf;
            private int nodeCount;
            private int[] nodeRepresentative;

            private int[] corners;          // 3 attribute vertices per triangle (mutable on collapse)
            private bool[] triangleAlive;
            private int aliveTriangles;
            private List<int>[] nodeTriangles; // may contain stale entries; always filter
            private bool[] nodeAlive;
            private int[] nodeVersion;
            private double[] quadrics;       // node * frames * 10
            private double[] nodePos;        // node * frames * 3 (moves under optimal placement)
            private List<int>[] nodeMembers;
            private List<int>[] attributeMembers;
            // Chart split vertices: ids >= vertexCount. splitPos/splitUv hold ORIGINAL source vertex ids.
            private readonly List<int> splitPos = new List<int>();
            private readonly List<int> splitUv = new List<int>();
            private readonly Dictionary<long, int> splitLookup = new Dictionary<long, int>();
            private int[] chartOf;          // original source vertex -> UV chart id
            private readonly double[] placement;  // frames * 3 scratch
            private readonly double[] costScratch = new double[10];
            private readonly Result result = new Result();
            private bool relaxed;

            public State(int vertexCount, float[][] poses, float[] uvs, int[] sourceTriangles, Options options)
            {
                this.vertexCount = vertexCount;
                this.poses = poses;
                this.uvs = uvs;
                this.options = options;
                frames = poses.Length;
                placement = new double[frames * 3];
                nodeOf = new int[vertexCount];
                result.sourceVertices = vertexCount;
                result.sourceTriangles = sourceTriangles.Length / 3;

                var list = new List<int>(sourceTriangles.Length);
                for (int t = 0; t < sourceTriangles.Length; t += 3)
                {
                    int a = sourceTriangles[t], b = sourceTriangles[t + 1], c = sourceTriangles[t + 2];
                    if (a == b || b == c || c == a) continue;
                    list.Add(a); list.Add(b); list.Add(c);
                }
                if (list.Count == 0) throw new ArgumentException("Every source triangle is degenerate.");
                corners = list.ToArray();
            }

            public Result Run()
            {
                Weld();
                result.optimalPlacement = options.optimalPlacement;
                result.options = options;
                result.weldNodeOfSource = (int[])nodeOf.Clone();
                nodePos = new double[nodeCount * frames * 3];
                nodeMembers = new List<int>[nodeCount];
                for (int n = 0; n < nodeCount; n++)
                {
                    nodeMembers[n] = new List<int>(1) { n };
                    for (int f = 0; f < frames; f++)
                        for (int axis = 0; axis < 3; axis++)
                            nodePos[(n * frames + f) * 3 + axis] = Px(f, nodeRepresentative[n], axis);
                }
                attributeMembers = new List<int>[Math.Max(4, vertexCount * 2)];
                for (int v = 0; v < vertexCount; v++) attributeMembers[v] = new List<int>(1) { v };
                BuildCharts();
                int triangleCount = corners.Length / 3;
                triangleAlive = new bool[triangleCount];
                // Weld may collapse triangles whose corners share a position.
                for (int t = 0; t < triangleCount; t++)
                {
                    int a = Node(corners[3 * t]), b = Node(corners[3 * t + 1]), c = Node(corners[3 * t + 2]);
                    triangleAlive[t] = a != b && b != c && c != a;
                    if (triangleAlive[t]) aliveTriangles++;
                }
                nodeAlive = new bool[nodeCount];
                nodeVersion = new int[nodeCount];
                nodeTriangles = new List<int>[nodeCount];
                for (int n = 0; n < nodeCount; n++) nodeTriangles[n] = new List<int>(8);
                for (int t = 0; t < triangleCount; t++)
                {
                    if (!triangleAlive[t]) continue;
                    for (int k = 0; k < 3; k++)
                    {
                        int n = Node(corners[3 * t + k]);
                        nodeTriangles[n].Add(t);
                        nodeAlive[n] = true;
                    }
                }

                DropSmallComponents();
                BuildQuadrics();
                Collapse();
                return Emit();
            }

            private int Node(int attribute) { return nodeOf[PosSource(attribute)]; }

            private int PosSource(int attribute) { return attribute < vertexCount ? attribute : splitPos[attribute - vertexCount]; }

            private int UvSource(int attribute) { return attribute < vertexCount ? attribute : splitUv[attribute - vertexCount]; }

            private int Chart(int attribute) { return chartOf[UvSource(attribute)]; }

            private double U(int attribute, int axis) { return uvs[2 * UvSource(attribute) + axis]; }

            private List<int> Members(int attribute)
            {
                if (attribute < vertexCount) return attributeMembers[attribute];
                while (attributeMembers.Length <= attribute) Array.Resize(ref attributeMembers, attributeMembers.Length * 2);
                return attributeMembers[attribute] ?? (attributeMembers[attribute] = new List<int>(1) { PosSource(attribute) });
            }

            private int SplitVertex(int positionAttribute, int uvAttribute)
            {
                int pos = PosSource(positionAttribute), uv = UvSource(uvAttribute);
                long key = (long)pos * vertexCount + uv;
                int id;
                if (splitLookup.TryGetValue(key, out id)) return id;
                id = vertexCount + splitPos.Count;
                splitPos.Add(pos); splitUv.Add(uv);
                splitLookup.Add(key, id);
                return id;
            }

            /// <summary>UV charts: union of source vertices sharing a triangle, plus position-welded vertices with equal UV.</summary>
            private void BuildCharts()
            {
                var parent = new int[vertexCount];
                for (int v = 0; v < vertexCount; v++) parent[v] = v;
                Func<int, int> find = null;
                find = x => { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; };
                Action<int, int> union = (a, b) => { a = find(a); b = find(b); if (a != b) parent[a] = b; };
                for (int t = 0; t < corners.Length; t += 3) { union(corners[t], corners[t + 1]); union(corners[t + 1], corners[t + 2]); }
                if (uvs != null)
                {
                    var byNodeUv = new Dictionary<string, int>();
                    for (int v = 0; v < vertexCount; v++)
                    {
                        string key = nodeOf[v] + ":" + Math.Round(uvs[2 * v] * 1e5) + ":" + Math.Round(uvs[2 * v + 1] * 1e5);
                        int other;
                        if (byNodeUv.TryGetValue(key, out other)) union(v, other); else byNodeUv.Add(key, v);
                    }
                }
                chartOf = new int[vertexCount];
                for (int v = 0; v < vertexCount; v++) chartOf[v] = find(v);
            }

            private double Px(int f, int attribute, int axis) { return poses[f][3 * PosSource(attribute) + axis]; }

            private double NP(int f, int node, int axis) { return nodePos[(node * frames + f) * 3 + axis]; }

            // ---------------------------------------------------------------- weld
            private void Weld()
            {
                double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
                double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
                for (int v = 0; v < vertexCount; v++)
                {
                    double x = Px(0, v, 0), y = Px(0, v, 1), z = Px(0, v, 2);
                    if (double.IsNaN(x) || double.IsNaN(y) || double.IsNaN(z) || double.IsInfinity(x) || double.IsInfinity(y) || double.IsInfinity(z))
                        throw new ArgumentException("Pose contains non-finite positions.");
                    minX = Math.Min(minX, x); minY = Math.Min(minY, y); minZ = Math.Min(minZ, z);
                    maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y); maxZ = Math.Max(maxZ, z);
                }
                double diagonal = Math.Sqrt((maxX - minX) * (maxX - minX) + (maxY - minY) * (maxY - minY) + (maxZ - minZ) * (maxZ - minZ));
                double eps = Math.Max(1e-9, diagonal * options.weldTolerance);
                var buckets = new Dictionary<long, List<int>>();
                var representatives = new List<int>();
                for (int v = 0; v < vertexCount; v++)
                {
                    long key = Key(Px(0, v, 0) / eps, Px(0, v, 1) / eps, Px(0, v, 2) / eps);
                    List<int> nodes;
                    if (!buckets.TryGetValue(key, out nodes)) { nodes = new List<int>(2); buckets.Add(key, nodes); }
                    int found = -1;
                    foreach (int node in nodes)
                        if (SameInAllFrames(representatives[node], v, eps)) { found = node; break; }
                    if (found < 0)
                    {
                        found = representatives.Count;
                        representatives.Add(v);
                        nodes.Add(found);
                    }
                    nodeOf[v] = found;
                }
                nodeCount = representatives.Count;
                nodeRepresentative = representatives.ToArray();
                result.weldedNodes = nodeCount;
            }

            private static long Key(double x, double y, double z)
            {
                long ix = (long)Math.Round(x), iy = (long)Math.Round(y), iz = (long)Math.Round(z);
                unchecked { return (ix * 73856093L) ^ (iy * 19349663L) ^ (iz * 83492791L); }
            }

            private bool SameInAllFrames(int a, int b, double eps)
            {
                for (int f = 0; f < frames; f++)
                    for (int axis = 0; axis < 3; axis++)
                        if (Math.Abs(Px(f, a, axis) - Px(f, b, axis)) > eps) return false;
                return true;
            }

            // ---------------------------------------------------------------- components
            private void DropSmallComponents()
            {
                if (options.dropComponentAreaFraction <= 0) return;
                var parent = new int[nodeCount];
                for (int i = 0; i < nodeCount; i++) parent[i] = i;
                int triangleCount = triangleAlive.Length;
                for (int t = 0; t < triangleCount; t++)
                {
                    if (!triangleAlive[t]) continue;
                    Union(parent, Node(corners[3 * t]), Node(corners[3 * t + 1]));
                    Union(parent, Node(corners[3 * t]), Node(corners[3 * t + 2]));
                }
                var area = new Dictionary<int, double>();
                var count = new Dictionary<int, int>();
                double total = 0;
                for (int t = 0; t < triangleCount; t++)
                {
                    if (!triangleAlive[t]) continue;
                    double a = MeanArea(corners[3 * t], corners[3 * t + 1], corners[3 * t + 2]);
                    int root = Find(parent, Node(corners[3 * t]));
                    double existing;
                    area.TryGetValue(root, out existing);
                    area[root] = existing + a;
                    int c;
                    count.TryGetValue(root, out c);
                    count[root] = c + 1;
                    total += a;
                }
                int largest = -1;
                double largestArea = -1;
                foreach (var pair in area)
                    if (pair.Value > largestArea) { largestArea = pair.Value; largest = pair.Key; }
                var drop = new HashSet<int>();
                foreach (var pair in area)
                    if (pair.Key != largest && pair.Value < total * options.dropComponentAreaFraction) drop.Add(pair.Key);
                if (drop.Count == 0) return;
                for (int t = 0; t < triangleCount; t++)
                {
                    if (!triangleAlive[t]) continue;
                    if (!drop.Contains(Find(parent, Node(corners[3 * t])))) continue;
                    triangleAlive[t] = false;
                    aliveTriangles--;
                    result.droppedComponentTriangles++;
                }
                result.droppedComponents = drop.Count;
                for (int n = 0; n < nodeCount; n++)
                    if (nodeAlive[n] && drop.Contains(Find(parent, n))) nodeAlive[n] = false;
            }

            private static int Find(int[] parent, int x)
            {
                while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; }
                return x;
            }

            private static void Union(int[] parent, int a, int b)
            {
                a = Find(parent, a); b = Find(parent, b);
                if (a != b) parent[a] = b;
            }

            private double MeanArea(int a, int b, int c)
            {
                double sum = 0;
                for (int f = 0; f < frames; f++)
                {
                    double nx, ny, nz;
                    Cross(f, a, b, c, out nx, out ny, out nz);
                    sum += 0.5 * Math.Sqrt(nx * nx + ny * ny + nz * nz);
                }
                return sum / frames;
            }

            private void Cross(int f, int a, int b, int c, out double nx, out double ny, out double nz)
            {
                double ax = Px(f, a, 0), ay = Px(f, a, 1), az = Px(f, a, 2);
                double ux = Px(f, b, 0) - ax, uy = Px(f, b, 1) - ay, uz = Px(f, b, 2) - az;
                double vx = Px(f, c, 0) - ax, vy = Px(f, c, 1) - ay, vz = Px(f, c, 2) - az;
                nx = uy * vz - uz * vy;
                ny = uz * vx - ux * vz;
                nz = ux * vy - uy * vx;
            }

            // ---------------------------------------------------------------- quadrics
            private void BuildQuadrics()
            {
                quadrics = new double[nodeCount * frames * 10];
                int triangleCount = triangleAlive.Length;
                for (int t = 0; t < triangleCount; t++)
                {
                    if (!triangleAlive[t]) continue;
                    int a = corners[3 * t], b = corners[3 * t + 1], c = corners[3 * t + 2];
                    for (int f = 0; f < frames; f++)
                    {
                        double nx, ny, nz;
                        Cross(f, a, b, c, out nx, out ny, out nz);
                        double length = Math.Sqrt(nx * nx + ny * ny + nz * nz);
                        if (length < 1e-20) continue;
                        double weight = 0.5 * length;
                        nx /= length; ny /= length; nz /= length;
                        double d = -(nx * Px(f, a, 0) + ny * Px(f, a, 1) + nz * Px(f, a, 2));
                        AddPlane(Node(a), f, nx, ny, nz, d, weight);
                        AddPlane(Node(b), f, nx, ny, nz, d, weight);
                        AddPlane(Node(c), f, nx, ny, nz, d, weight);
                    }
                }

                // Constraint planes perpendicular to faces along open boundaries and UV seams.
                for (int t = 0; t < triangleCount; t++)
                {
                    if (!triangleAlive[t]) continue;
                    for (int k = 0; k < 3; k++)
                    {
                        int p = corners[3 * t + k], q = corners[3 * t + (k + 1) % 3];
                        EdgeKind kind = ClassifyEdge(Node(p), Node(q));
                        double w = kind == EdgeKind.Boundary ? options.boundaryWeight : kind == EdgeKind.Seam ? options.seamWeight : 0;
                        if (w <= 0) continue;
                        int r = corners[3 * t + (k + 2) % 3];
                        for (int f = 0; f < frames; f++)
                        {
                            double fx, fy, fz;
                            Cross(f, p, q, r, out fx, out fy, out fz);
                            double ex = Px(f, q, 0) - Px(f, p, 0), ey = Px(f, q, 1) - Px(f, p, 1), ez = Px(f, q, 2) - Px(f, p, 2);
                            double mx = ey * fz - ez * fy, my = ez * fx - ex * fz, mz = ex * fy - ey * fx;
                            double ml = Math.Sqrt(mx * mx + my * my + mz * mz);
                            if (ml < 1e-20) continue;
                            mx /= ml; my /= ml; mz /= ml;
                            double d = -(mx * Px(f, p, 0) + my * Px(f, p, 1) + mz * Px(f, p, 2));
                            double weight = w * (ex * ex + ey * ey + ez * ez);
                            AddPlane(Node(p), f, mx, my, mz, d, weight);
                            AddPlane(Node(q), f, mx, my, mz, d, weight);
                        }
                    }
                }
            }

            private void AddPlane(int node, int f, double a, double b, double c, double d, double w)
            {
                int o = (node * frames + f) * 10;
                quadrics[o] += w * a * a; quadrics[o + 1] += w * a * b; quadrics[o + 2] += w * a * c; quadrics[o + 3] += w * a * d;
                quadrics[o + 4] += w * b * b; quadrics[o + 5] += w * b * c; quadrics[o + 6] += w * b * d;
                quadrics[o + 7] += w * c * c; quadrics[o + 8] += w * c * d;
                quadrics[o + 9] += w * d * d;
            }

            /// <summary>Collapse cost from->to; fills `placement` (frames*3) with the surviving node's positions.</summary>
            private double Cost(int from, int to)
            {
                double sum = 0;
                for (int f = 0; f < frames; f++)
                {
                    int a = (from * frames + f) * 10, b = (to * frames + f) * 10;
                    double[] q = costScratch;
                    for (int i = 0; i < 10; i++) q[i] = quadrics[a + i] + quadrics[b + i];
                    double x = NP(f, to, 0), y = NP(f, to, 1), z = NP(f, to, 2);
                    if (options.optimalPlacement)
                        SolvePlacement(q, options.placementRegularization, ref x, ref y, ref z);
                    placement[3 * f] = x; placement[3 * f + 1] = y; placement[3 * f + 2] = z;
                    sum += Evaluate(q, x, y, z);
                }
                return Math.Max(0, sum / frames);
            }

            // ---------------------------------------------------------------- topology queries
            private enum EdgeKind { None, Interior, Seam, Boundary, NonManifold }

            /// <summary>Alive triangles incident to node (stale entries removed in place).</summary>
            private List<int> Triangles(int node)
            {
                List<int> list = nodeTriangles[node];
                int write = 0;
                for (int i = 0; i < list.Count; i++)
                {
                    int t = list[i];
                    if (!triangleAlive[t] || !Contains(t, node)) continue;
                    bool duplicate = false;
                    for (int j = 0; j < write; j++) if (list[j] == t) { duplicate = true; break; }
                    if (!duplicate) list[write++] = t;
                }
                if (write < list.Count) list.RemoveRange(write, list.Count - write);
                return list;
            }

            private bool Contains(int t, int node)
            {
                return Node(corners[3 * t]) == node || Node(corners[3 * t + 1]) == node || Node(corners[3 * t + 2]) == node;
            }

            private int CornerOf(int t, int node)
            {
                for (int k = 0; k < 3; k++) if (Node(corners[3 * t + k]) == node) return corners[3 * t + k];
                return -1;
            }

            private EdgeKind ClassifyEdge(int a, int b)
            {
                int count = 0, firstA = -1, firstB = -1;
                bool seam = false;
                foreach (int t in Triangles(a))
                {
                    if (!Contains(t, b)) continue;
                    int ca = CornerOf(t, a), cb = CornerOf(t, b);
                    if (count == 0) { firstA = ca; firstB = cb; }
                    else if (ca != firstA || cb != firstB) seam = true;
                    count++;
                }
                if (count == 0) return EdgeKind.None;
                if (count == 1) return EdgeKind.Boundary;
                if (count > 2) return EdgeKind.NonManifold;
                return seam ? EdgeKind.Seam : EdgeKind.Interior;
            }

            private void Neighbors(int node, HashSet<int> output)
            {
                output.Clear();
                foreach (int t in Triangles(node))
                    for (int k = 0; k < 3; k++)
                    {
                        int n = Node(corners[3 * t + k]);
                        if (n != node) output.Add(n);
                    }
            }

            /// <summary>Count of border (boundary/seam/non-manifold) edges at node; -1 if none.</summary>
            private int BorderEdgeCount(int node, HashSet<int> scratch)
            {
                Neighbors(node, scratch);
                int border = 0;
                foreach (int n in scratch)
                    if (IsBorder(ClassifyEdge(node, n))) border++;
                return border;
            }

            private bool IsBorder(EdgeKind kind)
            {
                return kind == EdgeKind.Boundary || kind == EdgeKind.NonManifold || (kind == EdgeKind.Seam && options.preserveSeams);
            }

            // ---------------------------------------------------------------- collapse
            private readonly HashSet<int> scratchA = new HashSet<int>();
            private readonly HashSet<int> scratchB = new HashSet<int>();
            private readonly HashSet<int> scratchC = new HashSet<int>();

            private bool IsAllowed(int from, int to)
            {
                if (!nodeAlive[from] || !nodeAlive[to] || from == to) return false;
                List<int> fromTriangles = Triangles(from);
                int shared = 0;
                scratchC.Clear();
                foreach (int t in fromTriangles)
                {
                    if (!Contains(t, to)) continue;
                    shared++;
                    for (int k = 0; k < 3; k++)
                    {
                        int n = Node(corners[3 * t + k]);
                        if (n != from && n != to) scratchC.Add(n);
                    }
                }
                if (shared == 0) return Reject("notAdjacent");
                if (shared > 2) return Reject("nonManifoldEdge");

                // Link condition: common neighbours must be exactly the edge's opposite vertices.
                Neighbors(from, scratchA);
                Neighbors(to, scratchB);
                foreach (int n in scratchA)
                    if (n != to && scratchB.Contains(n) && !scratchC.Contains(n)) return Reject("link");
                // Never collapse a lone triangle / tetrahedron-like closed tiny shell into nothing.
                if (scratchA.Count <= 2 && scratchB.Count <= 2) return Reject("tiny");

                if (!relaxed)
                {
                    EdgeKind kind = ClassifyEdge(from, to);
                    int fromBorder = BorderEdgeCount(from, scratchA);
                    if (fromBorder > 0)
                    {
                        // A border vertex may only slide along its own border line, and junctions stay locked.
                        if (kind != EdgeKind.Boundary && !(kind == EdgeKind.Seam && options.preserveSeams)) return Reject("borderOffLine");
                        if (fromBorder != 2) return Reject("borderJunction");
                    }
                    else if (kind == EdgeKind.NonManifold) return Reject("nonManifoldEdge");
                    // Seam vertices carry >1 attribute; a non-border vertex carries exactly one.
                }

                // A surviving face must not coincide with an existing face of `to` (closed tiny shells
                // such as a tetrahedron would otherwise fold into a double-sided sliver).
                List<int> toTriangles = Triangles(to);
                foreach (int t in fromTriangles)
                {
                    if (Contains(t, to)) continue;
                    int o1 = -1, o2 = -1;
                    for (int k = 0; k < 3; k++)
                    {
                        int n = Node(corners[3 * t + k]);
                        if (n == from) continue;
                        if (o1 < 0) o1 = n; else o2 = n;
                    }
                    foreach (int u in toTriangles)
                        if (!Contains(u, from) && Contains(u, o1) && Contains(u, o2)) return Reject("duplicateFace");
                }

                // Flip / degeneracy check in every sampled pose for faces that survive. Under optimal
                // placement the surviving node moves too, so faces around `to` are checked as well.
                Cost(from, to); // fills `placement`
                if (!FacesStayValid(fromTriangles, from, to, to)) return false;
                if (options.optimalPlacement && !FacesStayValid(toTriangles, from, to, from)) return false;
                return true;
            }

            /// <summary>Faces in `triangles` not containing `skip`: replace nodes from/to by `placement` and test every frame.</summary>
            private bool FacesStayValid(List<int> triangles, int from, int to, int skip)
            {
                foreach (int t in triangles)
                {
                    if (Contains(t, skip)) continue;
                    int n0 = Node(corners[3 * t]), n1 = Node(corners[3 * t + 1]), n2 = Node(corners[3 * t + 2]);
                    double meanCos = 0;
                    for (int f = 0; f < frames; f++)
                    {
                        double ox, oy, oz, nx, ny, nz;
                        NodeCross(f, n0, n1, n2, -1, -1, out ox, out oy, out oz);
                        NodeCross(f, n0, n1, n2, from, to, out nx, out ny, out nz);
                        double ol = Math.Sqrt(ox * ox + oy * oy + oz * oz), nl = Math.Sqrt(nx * nx + ny * ny + nz * nz);
                        if (nl < 1e-14) return Reject("degenerate");
                        if (ol < 1e-14) { meanCos += 1; continue; }
                        double cos = (ox * nx + oy * ny + oz * nz) / (ol * nl);
                        if (cos < options.minFrameCosine) return Reject("flipFrame");
                        meanCos += cos;
                    }
                    if (meanCos / frames < options.minMeanCosine) return Reject("flipMean");
                }
                return true;
            }

            /// <summary>Face normal (unnormalised) of nodes n0,n1,n2 in frame f; nodes equal to a or b read `placement`.</summary>
            private void NodeCross(int f, int n0, int n1, int n2, int a, int b, out double nx, out double ny, out double nz)
            {
                double p0x, p0y, p0z, p1x, p1y, p1z, p2x, p2y, p2z;
                At(f, n0, a, b, out p0x, out p0y, out p0z);
                At(f, n1, a, b, out p1x, out p1y, out p1z);
                At(f, n2, a, b, out p2x, out p2y, out p2z);
                double ux = p1x - p0x, uy = p1y - p0y, uz = p1z - p0z, vx = p2x - p0x, vy = p2y - p0y, vz = p2z - p0z;
                nx = uy * vz - uz * vy; ny = uz * vx - ux * vz; nz = ux * vy - uy * vx;
            }

            private void At(int f, int node, int a, int b, out double x, out double y, out double z)
            {
                if (node == a || node == b) { x = placement[3 * f]; y = placement[3 * f + 1]; z = placement[3 * f + 2]; }
                else { x = NP(f, node, 0); y = NP(f, node, 1); z = NP(f, node, 2); }
            }

            private bool Reject(string reason)
            {
                int count;
                result.rejections.TryGetValue(reason, out count);
                result.rejections[reason] = count + 1;
                return false;
            }

            private void Apply(int from, int to)
            {
                List<int> fromTriangles = new List<int>(Triangles(from));
                // Which attribute of `to` matches each attribute of `from`: the one it shares an edge triangle with.
                var mapping = new Dictionary<int, int>();
                var toAttributes = new List<int>();
                foreach (int t in Triangles(to))
                {
                    int ct = CornerOf(t, to);
                    if (!toAttributes.Contains(ct)) toAttributes.Add(ct);
                }
                foreach (int t in fromTriangles)
                {
                    if (!Contains(t, to)) continue;
                    int cf = CornerOf(t, from), ct = CornerOf(t, to);
                    if (!mapping.ContainsKey(cf)) mapping.Add(cf, ct);
                }
                Cost(from, to); // fills `placement`
                for (int f = 0; f < frames; f++)
                    for (int axis = 0; axis < 3; axis++)
                        nodePos[(to * frames + f) * 3 + axis] = placement[3 * f + axis];
                nodeMembers[to].AddRange(nodeMembers[from]);
                nodeMembers[from].Clear();

                foreach (int t in fromTriangles)
                {
                    if (Contains(t, to))
                    {
                        triangleAlive[t] = false;
                        aliveTriangles--;
                        continue;
                    }
                    for (int k = 0; k < 3; k++)
                    {
                        int c = corners[3 * t + k];
                        if (Node(c) != from) continue;
                        int mapped;
                        if (!mapping.TryGetValue(c, out mapped))
                        {
                            mapped = MapAcrossCollapse(c, toAttributes);
                            mapping.Add(c, mapped);
                        }
                        corners[3 * t + k] = mapped;
                    }
                    nodeTriangles[to].Add(t);
                }
                foreach (var pair in mapping)
                    if (pair.Key != pair.Value && Members(pair.Key).Count > 0)
                    {
                        Members(pair.Value).AddRange(Members(pair.Key));
                        Members(pair.Key).Clear();
                    }
                nodeAlive[from] = false;
                nodeTriangles[from].Clear();
                int fo = from * frames * 10, to10 = to * frames * 10;
                for (int i = 0; i < frames * 10; i++) quadrics[to10 + i] += quadrics[fo + i];
                nodeVersion[to]++;
                nodeVersion[from]++;
            }

            /// <summary>Attribute of the kept node that replaces corner `c` of the removed node in triangles not shared by both.</summary>
            private int MapAcrossCollapse(int c, List<int> toAttributes)
            {
                if (!options.splitChartCorners || uvs == null || toAttributes.Count == 0) return NearestUv(c, toAttributes);
                int chart = Chart(c);
                var sameChart = toAttributes.FindAll(a => Chart(a) == chart);
                if (sameChart.Count > 0) return NearestUv(c, sameChart);
                return SplitVertex(toAttributes[0], c);
            }

            private int NearestUv(int attribute, List<int> candidates)
            {
                if (candidates.Count == 0) return nodeRepresentative[0];
                if (uvs == null || candidates.Count == 1) return candidates[0];
                int best = candidates[0];
                double bestDistance = double.MaxValue;
                foreach (int c in candidates)
                {
                    double du = U(c, 0) - U(attribute, 0), dv = U(c, 1) - U(attribute, 1);
                    double d = du * du + dv * dv;
                    if (d < bestDistance) { bestDistance = d; best = c; }
                }
                return best;
            }

            private void PushEdges(MinHeap heap, int node)
            {
                Neighbors(node, scratchA);
                var neighbors = new List<int>(scratchA);
                foreach (int n in neighbors)
                {
                    heap.Push(new Candidate { cost = Cost(node, n), from = node, to = n, versionFrom = nodeVersion[node], versionTo = nodeVersion[n] });
                    heap.Push(new Candidate { cost = Cost(n, node), from = n, to = node, versionFrom = nodeVersion[n], versionTo = nodeVersion[node] });
                }
            }

            private void Rebuild(MinHeap heap)
            {
                heap.Clear();
                result.heapRebuilds++;
                for (int n = 0; n < nodeCount; n++)
                {
                    if (!nodeAlive[n]) continue;
                    Neighbors(n, scratchA);
                    foreach (int m in scratchA)
                        heap.Push(new Candidate { cost = Cost(n, m), from = n, to = m, versionFrom = nodeVersion[n], versionTo = nodeVersion[m] });
                }
            }

            private void Collapse()
            {
                var heap = new MinHeap();
                relaxed = false;
                Rebuild(heap);
                int collapsesSinceRebuild = 0;
                while (aliveTriangles > options.targetTriangles)
                {
                    if (heap.Count == 0)
                    {
                        if (collapsesSinceRebuild == 0)
                        {
                            if (relaxed || !options.allowRelaxedPass) break;
                            relaxed = true;
                        }
                        collapsesSinceRebuild = 0;
                        Rebuild(heap);
                        if (heap.Count == 0) break;
                        continue;
                    }
                    Candidate c = heap.Pop();
                    if (!nodeAlive[c.from] || !nodeAlive[c.to]) continue;
                    if (nodeVersion[c.from] != c.versionFrom || nodeVersion[c.to] != c.versionTo) continue;
                    if (!IsAllowed(c.from, c.to)) continue;
                    Apply(c.from, c.to);
                    collapsesSinceRebuild++;
                    if (relaxed) result.relaxedCollapses++; else result.strictCollapses++;
                    result.maxCollapseCost = Math.Max(result.maxCollapseCost, c.cost);
                    // Neighbours of `to` changed their cost to it; their edges to others only need re-validation at pop time.
                    PushEdges(heap, c.to);
                }
                result.reachedTarget = aliveTriangles <= options.targetTriangles;
            }

            // ---------------------------------------------------------------- output
            private Result Emit()
            {
                var used = new SortedSet<int>();
                var seen = new HashSet<string>();
                var kept = new List<int>();
                for (int t = 0; t < triangleAlive.Length; t++)
                {
                    if (!triangleAlive[t]) continue;
                    int a = corners[3 * t], b = corners[3 * t + 1], c = corners[3 * t + 2];
                    // Canonical rotation keeps orientation; drops exact oriented duplicates only.
                    int r0 = a, r1 = b, r2 = c;
                    if (b < r0 || c < r0)
                    {
                        if (b < c) { r0 = b; r1 = c; r2 = a; } else { r0 = c; r1 = a; r2 = b; }
                    }
                    if (!seen.Add(r0 + "," + r1 + "," + r2)) continue;
                    kept.Add(a); kept.Add(b); kept.Add(c);
                    used.Add(a); used.Add(b); used.Add(c);
                }
                var map = new Dictionary<int, int>();
                result.keptSourceVertices = new int[used.Count];
                int index = 0;
                foreach (int v in used) { map.Add(v, index); result.keptSourceVertices[index] = v; index++; }
                result.triangles = new int[kept.Count];
                for (int i = 0; i < kept.Count; i++) result.triangles[i] = map[kept[i]];
                result.keptClusterNodes = new int[result.keptSourceVertices.Length][];
                result.keptAttributeMembers = new int[result.keptSourceVertices.Length][];
                result.keptUvSourceVertices = new int[result.keptSourceVertices.Length];
                for (int k = 0; k < result.keptSourceVertices.Length; k++)
                {
                    int v = result.keptSourceVertices[k];
                    result.keptClusterNodes[k] = nodeMembers[Node(v)].ToArray();
                    List<int> members = Members(v);
                    result.keptAttributeMembers[k] = members.Count > 0 ? members.ToArray() : new[] { PosSource(v) };
                    result.keptUvSourceVertices[k] = UvSource(v);
                    if (v >= vertexCount) result.chartSplitVertices++;
                    result.keptSourceVertices[k] = PosSource(v);
                }
                for (int t = 0; t < kept.Count; t += 3)
                    if (Chart(kept[t]) != Chart(kept[t + 1]) || Chart(kept[t + 1]) != Chart(kept[t + 2])) result.crossChartTriangles++;
                return result;
            }
        }
    }
}
