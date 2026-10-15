using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MassEngine.Tests
{
    /// <summary>GPU nav field N1 (Docs/GPU-NAVFIELD-N0-20261013.md): reference semantics and GPU == reference bit for bit.</summary>
    public sealed class GpuNavFieldTests
    {
        const string ShaderPath = "Assets/MassEngine/FlowField/Shaders/GpuNavField.compute";

        // r x r graph, cell 2 m, straight cost 2, diagonal 2.828; a wall along x = r/2 with a gap; random blocked cells.
        static GpuNavFieldGraph MakeGraph(int r, int seed, bool wall = true, double blockedFraction = .08)
        {
            var rng = new System.Random(seed);
            int n = r * r;
            var walk = new uint[n];
            for (int z = 0; z < r; z++)
                for (int x = 0; x < r; x++)
                {
                    bool blocked = rng.NextDouble() < blockedFraction;
                    if (wall && x == r / 2 && (z < r / 2 - 2 || z > r / 2 + 1)) blocked = true;
                    walk[z * r + x] = blocked ? 0u : 1u;
                }
            var edges = new uint[2 * n];
            ushort straight = Mathf.FloatToHalf(2f), diagonal = Mathf.FloatToHalf(2.828427f);
            for (int z = 0; z < r; z++)
                for (int x = 0; x < r; x++)
                {
                    int c = z * r + x; var h = new ushort[4];
                    for (int d = 0; d < 4; d++)
                    {
                        h[d] = GpuNavFieldGraph.NoEdge;
                        int nx = x + GpuNavFieldGraph.StepX[d], nz = z + GpuNavFieldGraph.StepZ[d];
                        if (walk[c] == 0 || nx < 0 || nz < 0 || nx >= r || nz >= r || walk[nz * r + nx] == 0) continue;
                        bool diag = d >= 2;
                        if (diag && (walk[z * r + nx] == 0 || walk[nz * r + x] == 0)) continue;
                        // Slightly varied costs so ties/rounding are exercised.
                        h[d] = diag ? diagonal : (ushort)(straight + (ushort)rng.Next(0, 3));
                    }
                    edges[2 * c] = GpuNavFieldGraph.Pack(h[0], h[1]);
                    edges[2 * c + 1] = GpuNavFieldGraph.Pack(h[2], h[3]);
                }
            return new GpuNavFieldGraph(r, new Vector2(-r, -r), 2f, walk, edges);
        }

        static int WalkableGoal(GpuNavFieldGraph g, int x, int z)
        {
            int r = g.Resolution;
            for (int k = 0; k < r * r; k++) { int c = (z * r + x + k) % (r * r); if (g.Walkable[c] != 0) return c; }
            return -1;
        }

        [Test]
        public void Pack_RoundTripsHalfCosts()
        {
            var g = MakeGraph(8, 1, false);
            for (int c = 0; c < g.CellCount; c++)
                for (int d = 0; d < 4; d++)
                {
                    ushort bits = g.EdgeBits(c, d);
                    Assert.AreEqual(Mathf.HalfToFloat(bits), g.EdgeCost(c, d));
                    if (bits == GpuNavFieldGraph.NoEdge) Assert.IsTrue(float.IsPositiveInfinity(g.EdgeCost(c, d)));
                }
        }

        [Test]
        public void Reference_ConvergesToDijkstra()
        {
            var g = MakeGraph(40, 7);
            var goal = new GpuNavGoal { mode = GpuNavGoal.Point, cell = WalkableGoal(g, 3, 20) };
            var field = GpuNavFieldReference.Filled(g.CellCount, GpuNavFieldGraph.Unreachable);
            int passes = GpuNavFieldReference.Converge(g, goal, ref field, 2000);
            Assert.Less(passes, 2000, "did not converge");
            var exact = GpuNavFieldReference.Dijkstra(g, goal);
            int reachable = 0;
            for (int c = 0; c < g.CellCount; c++)
            {
                bool a = field[c] >= GpuNavFieldGraph.Unreachable, b = exact[c] >= GpuNavFieldGraph.Unreachable;
                Assert.AreEqual(b, a, "reachability differs at cell " + c);
                if (a) continue;
                reachable++;
                Assert.AreEqual(exact[c], field[c], Mathf.Max(1e-3f, exact[c] * 1e-5f), "distance at cell " + c);
            }
            Assert.Greater(reachable, g.CellCount / 2);
        }

        [Test]
        public void Reference_StaleLowValuesRise()
        {
            // Value iteration must also correct values that are too LOW (moved goal / added cost): no ageing term needed.
            // Connected graph only: a pocket cut off from every goal counts up without bound instead (component labels, N2).
            var g = MakeGraph(32, 11, true, 0);
            var goal = new GpuNavGoal { mode = GpuNavGoal.Point, cell = WalkableGoal(g, 28, 4) };
            var fromHigh = GpuNavFieldReference.Filled(g.CellCount, GpuNavFieldGraph.Unreachable);
            var fromZero = GpuNavFieldReference.Filled(g.CellCount, 0f);
            GpuNavFieldReference.Converge(g, goal, ref fromHigh, 4000);
            int passes = GpuNavFieldReference.Converge(g, goal, ref fromZero, 100000);
            Assert.Less(passes, 100000, "stale low values did not rise to the fixed point");
            for (int c = 0; c < g.CellCount; c++) Assert.AreEqual(fromHigh[c], fromZero[c], "cell " + c);
        }

        [Test]
        public void Reference_AreaGoal_MarksCellsByCentre()
        {
            var g = MakeGraph(16, 3, false);
            var goal = new GpuNavGoal { mode = GpuNavGoal.Area, rect = new Vector4(-8f, -8f, 3f, 1f) };
            int goals = 0;
            for (int z = 0; z < 16; z++) for (int x = 0; x < 16; x++)
                if (GpuNavFieldReference.IsGoal(g, goal, x, z, z * 16 + x)) goals++;
            // Origin -16, cell 2: centres -15 + 2i. x within [-11,-5] -> 4 cells; z within [-9,-7] -> 2 cells (inclusive).
            Assert.AreEqual(8, goals);
        }

        static ComputeShader LoadShader()
        {
            if (!SystemInfo.supportsComputeShaders) Assert.Ignore("compute shaders unsupported");
            var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(ShaderPath);
            Assert.IsNotNull(shader, ShaderPath);
            return shader;
        }

        [TestCase(GpuNavGoal.Point)]
        [TestCase(GpuNavGoal.Area)]
        public void Gpu_PassesMatchReferenceBitForBit(int mode)
        {
            var shader = LoadShader();
            var g = MakeGraph(48, 5);
            var goal = mode == GpuNavGoal.Point
                ? new GpuNavGoal { mode = GpuNavGoal.Point, cell = WalkableGoal(g, 40, 30) }
                : new GpuNavGoal { mode = GpuNavGoal.Area, rect = new Vector4(-40f, 10f, 5f, 9f) };
            var rng = new System.Random(99);
            var start = new float[g.CellCount];
            for (int i = 0; i < start.Length; i++) start[i] = rng.NextDouble() < .3 ? GpuNavFieldGraph.Unreachable : (float)(rng.NextDouble() * 120.0);
            using (var field = new DisposableField(new GpuNavField(shader, g, 2) { Metric = GpuNavMetric.Graph }))
            {
                const int team = 1; // non-zero layer offset
                field.F.SetGoalForTest(team, goal);
                field.F.WriteDistances(team, start);
                var expected = (float[])start.Clone(); var scratch = new float[g.CellCount];
                var actual = new float[g.CellCount];
                foreach (int passes in new[] { 1, 7, 60 })
                {
                    field.F.RunPasses(null, team, passes);
                    for (int p = 0; p < passes; p++) { GpuNavFieldReference.Relax(g, goal, expected, scratch); var t = expected; expected = scratch; scratch = t; }
                    field.F.ReadDistances(team, actual);
                    for (int c = 0; c < g.CellCount; c++)
                        if (System.BitConverter.SingleToInt32Bits(expected[c]) != System.BitConverter.SingleToInt32Bits(actual[c]))
                            Assert.Fail("pass " + passes + " cell " + c + ": reference " + expected[c].ToString("R") + " gpu " + actual[c].ToString("R"));
                }
                // Layer 0 untouched (never written): still Unreachable.
                field.F.ReadDistances(0, actual);
                for (int c = 0; c < g.CellCount; c++) Assert.AreEqual(GpuNavFieldGraph.Unreachable, actual[c]);
            }
        }

        static void AssertBitwise(float[] expected, float[] actual, string what)
        {
            for (int c = 0; c < expected.Length; c++)
                if (System.BitConverter.SingleToInt32Bits(expected[c]) != System.BitConverter.SingleToInt32Bits(actual[c]))
                    Assert.Fail(what + " cell " + c + ": expected " + expected[c].ToString("R") + " actual " + actual[c].ToString("R"));
        }

        // N2: the seed IS the GPU fixed point (same half costs, same float additions; unique fixed point).
        [TestCase(GpuNavGoal.Point)]
        [TestCase(GpuNavGoal.Area)]
        public void Seed_EqualsGpuFixedPoint_AndAPassChangesNothing(int mode)
        {
            var shader = LoadShader();
            var g = MakeGraph(48, 21); // random blocks: isolated pockets exist
            var goal = mode == GpuNavGoal.Point
                ? new GpuNavGoal { mode = GpuNavGoal.Point, cell = WalkableGoal(g, 6, 40) }
                : new GpuNavGoal { mode = GpuNavGoal.Area, rect = new Vector4(20f, -30f, 7f, 4f) };
            var reference = GpuNavFieldReference.Filled(g.CellCount, GpuNavFieldGraph.Unreachable);
            Assert.Less(GpuNavFieldReference.Converge(g, goal, ref reference, 5000), 5000);
            using (var field = new DisposableField(new GpuNavField(shader, g, 2) { Metric = GpuNavMetric.Graph }))
            {
                // Stale finite values everywhere (old goal): the seed must replace all of them, pockets included.
                field.F.WriteDistances(0, GpuNavFieldReference.Filled(g.CellCount, 3f));
                field.F.SeedNow(0, goal);
                var seeded = new float[g.CellCount];
                field.F.ReadDistances(0, seeded);
                AssertBitwise(reference, seeded, "seed vs converged reference");
                field.F.RunPasses(null, 0, 3);
                var after = new float[g.CellCount];
                field.F.ReadDistances(0, after);
                AssertBitwise(seeded, after, "GPU passes after seed");
                Assert.AreEqual(1, field.F.SeedUploads);
            }
        }

        [Test]
        public void Seed_DensityGoal_MatchesReference()
        {
            var shader = LoadShader();
            var g = MakeGraph(32, 8);
            var density = new uint[16 * 16];
            density[3 * 16 + 12] = 5; density[4 * 16 + 12] = 1; density[13 * 16 + 2] = 9;
            var goal = new GpuNavGoal { mode = GpuNavGoal.Density, density = density, coarseResolution = 16 };
            var reference = GpuNavFieldReference.Filled(g.CellCount, GpuNavFieldGraph.Unreachable);
            Assert.Less(GpuNavFieldReference.Converge(g, goal, ref reference, 5000), 5000);
            using (var field = new DisposableField(new GpuNavField(shader, g, 1) { Metric = GpuNavMetric.Graph }))
            {
                field.F.SeedNow(0, goal);
                var seeded = new float[g.CellCount];
                field.F.ReadDistances(0, seeded);
                AssertBitwise(reference, seeded, "density seed");
            }
        }

        // ---- N3: eikonal metric (8-neighbour triangle update)

        static float MaxRelDiff(float[] a, float[] b, out int bitDiff)
        {
            float worst = 0; bitDiff = 0;
            for (int i = 0; i < a.Length; i++)
            {
                if (System.BitConverter.SingleToInt32Bits(a[i]) != System.BitConverter.SingleToInt32Bits(b[i])) bitDiff++;
                bool ua = a[i] >= GpuNavFieldGraph.Unreachable, ub = b[i] >= GpuNavFieldGraph.Unreachable;
                if (ua != ub) return float.PositiveInfinity;
                if (ua) continue;
                worst = Mathf.Max(worst, Mathf.Abs(a[i] - b[i]) / Mathf.Max(1f, Mathf.Abs(b[i])));
            }
            return worst;
        }

        [Test]
        public void Eikonal_OpenGround_IsRoundNotOctagonal()
        {
            const int r = 81;
            var g = MakeGraph(r, 1, false, 0);
            int centre = 40 * r + 40;
            var goal = new GpuNavGoal { mode = GpuNavGoal.Point, cell = centre };
            var field = GpuNavFieldReference.Filled(g.CellCount, GpuNavFieldGraph.Unreachable);
            GpuNavFieldReference.Converge(g, goal, ref field, 4000, GpuNavMetric.Eikonal);
            var graph = GpuNavFieldReference.Filled(g.CellCount, GpuNavFieldGraph.Unreachable);
            GpuNavFieldReference.Converge(g, goal, ref graph, 4000, GpuNavMetric.Graph);
            // MakeGraph varies straight costs by up to 2 half steps; compare to the same graph's Euclidean scale loosely.
            var angles = new System.Collections.Generic.List<float>(); var graphAngles = new System.Collections.Generic.List<float>();
            float worstDist = 0;
            for (int z = 2; z < r - 2; z++)
                for (int x = 2; x < r - 2; x++)
                {
                    float rx = x - 40, rz = z - 40, rad = Mathf.Sqrt(rx * rx + rz * rz);
                    if (rad < 8 || rad > 36) continue;
                    int c = z * r + x;
                    worstDist = Mathf.Max(worstDist, Mathf.Abs(field[c] - 2f * rad) / (2f * rad));
                    float truth = Mathf.Atan2(-rz, -rx) * Mathf.Rad2Deg;
                    angles.Add(AngleError(field, r, c, truth)); graphAngles.Add(AngleError(graph, r, c, truth));
                }
            angles.Sort(); graphAngles.Sort();
            float p95 = angles[(int)(angles.Count * .95f)], graphP95 = graphAngles[(int)(graphAngles.Count * .95f)];
            Debug.Log("[N3TEST] " + "eikonal: dist err max " + worstDist + ", angle p95 " + p95 + "; graph angle p95 " + graphP95);
            Assert.Less(worstDist, .03f, "distance vs Euclidean");
            Assert.Less(p95, 4f, "eikonal -grad direction error p95 (deg); graph metric ~20");
            Assert.Greater(graphP95, 10f, "the graph metric should show its octagon error (sanity of the measure)");
        }

        static float AngleError(float[] f, int r, int c, float truthDeg)
        {
            float gx = f[c + 1] - f[c - 1], gz = f[c + r] - f[c - r];
            float a = Mathf.Atan2(-gz, -gx) * Mathf.Rad2Deg;
            return Mathf.Abs(Mathf.DeltaAngle(a, truthDeg));
        }

        [TestCase(GpuNavGoal.Point)]
        [TestCase(GpuNavGoal.Area)]
        public void Gpu_EikonalPasses_MatchReference(int mode)
        {
            var shader = LoadShader();
            var g = MakeGraph(48, 5);
            var goal = mode == GpuNavGoal.Point
                ? new GpuNavGoal { mode = GpuNavGoal.Point, cell = WalkableGoal(g, 40, 30) }
                : new GpuNavGoal { mode = GpuNavGoal.Area, rect = new Vector4(-40f, 10f, 5f, 9f) };
            var expected = GpuNavFieldReference.Filled(g.CellCount, GpuNavFieldGraph.Unreachable);
            var scratch = new float[g.CellCount]; var actual = new float[g.CellCount];
            using (var field = new DisposableField(new GpuNavField(shader, g, 1) { Metric = GpuNavMetric.Eikonal }))
            {
                field.F.SetGoalForTest(0, goal);
                field.F.RunPasses(null, 0, 200);
                for (int p = 0; p < 200; p++) { GpuNavFieldReference.Relax(g, goal, expected, scratch, GpuNavMetric.Eikonal); var t = expected; expected = scratch; scratch = t; }
                field.F.ReadDistances(0, actual);
                float rel = MaxRelDiff(actual, expected, out int bits);
                Debug.Log("[N3TEST] " + "eikonal GPU vs CPU after 200 passes: max rel " + rel + ", bit-different cells " + bits + "/" + g.CellCount);
                Assert.Less(rel, 1e-5f);
            }
        }

        [Test]
        public void Seed_Eikonal_IsTheFixedPoint()
        {
            var shader = LoadShader();
            var g = MakeGraph(48, 21);
            var goal = new GpuNavGoal { mode = GpuNavGoal.Point, cell = WalkableGoal(g, 6, 40) };
            var reference = GpuNavFieldReference.Filled(g.CellCount, GpuNavFieldGraph.Unreachable);
            Assert.Less(GpuNavFieldReference.Converge(g, goal, ref reference, 5000, GpuNavMetric.Eikonal), 5000);
            using (var field = new DisposableField(new GpuNavField(shader, g, 1) { Metric = GpuNavMetric.Eikonal }))
            {
                field.F.WriteDistances(0, GpuNavFieldReference.Filled(g.CellCount, 3f));
                field.F.SeedNow(0, goal);
                var seeded = new float[g.CellCount]; var after = new float[g.CellCount];
                field.F.ReadDistances(0, seeded);
                float rel = MaxRelDiff(seeded, reference, out int bits);
                field.F.RunPasses(null, 0, 3);
                field.F.ReadDistances(0, after);
                float drift = MaxRelDiff(after, seeded, out int changed);
                Debug.Log("[N3TEST] " + "eikonal seed vs converged CPU: max rel " + rel + " (bit-different " + bits + "); 3 GPU passes after the seed: max rel " + drift + " (changed " + changed + ")");
                Assert.Less(rel, 1e-5f);
                Assert.Less(drift, 1e-5f);
            }
        }

        // ---- N4: crowd cost

        [TestCase(GpuNavMetric.Graph)]
        [TestCase(GpuNavMetric.Eikonal)]
        public void Gpu_CrowdCost_MatchesReference(GpuNavMetric metric)
        {
            var shader = LoadShader();
            var g = MakeGraph(48, 9);
            var goal = new GpuNavGoal { mode = GpuNavGoal.Point, cell = WalkableGoal(g, 40, 30) };
            int n = g.CellCount;
            var rng = new System.Random(4);
            var ema = new float[n * 2];
            for (int i = 0; i < ema.Length; i++) ema[i] = rng.NextDouble() < .3 ? (float)(rng.NextDouble() * 9.0) : 0f;
            var layer0 = new float[n]; System.Array.Copy(ema, layer0, n);
            const float gain = 2f, full = 6f, cap = 1f;
            var expected = GpuNavFieldReference.Filled(n, GpuNavFieldGraph.Unreachable);
            var scratch = new float[n]; var actual = new float[n];
            using (var field = new DisposableField(new GpuNavField(shader, g, 2) { Metric = metric, CrowdEnabled = true, CrowdGain = gain, CrowdFullPerCell = full, CrowdCap = cap }))
            {
                field.F.WriteCrowd(ema);
                field.F.SetGoalForTest(0, goal);
                field.F.RunPasses(null, 0, 150);
                for (int p = 0; p < 150; p++) { GpuNavFieldReference.Relax(g, goal, expected, scratch, metric, layer0, gain, 1f / full, cap); var t = expected; expected = scratch; scratch = t; }
                field.F.ReadDistances(0, actual);
                float rel = MaxRelDiff(actual, expected, out int bits);
                Debug.Log("[N4TEST] crowd " + metric + " GPU vs CPU after 150 passes: max rel " + rel + ", bit-different " + bits + "/" + n);
                if (metric == GpuNavMetric.Graph) Assert.AreEqual(0, bits, "graph metric with crowd must stay bit-exact");
                else Assert.Less(rel, 1e-5f);
                // The crowd must actually change the field.
                var plain = GpuNavFieldReference.Filled(n, GpuNavFieldGraph.Unreachable);
                GpuNavFieldReference.Converge(g, goal, ref plain, 4000, metric);
                Assert.Greater(MaxRelDiff(actual, plain, out _), .01f);
            }
        }

        [Test]
        public void Crowd_PricesExplicitMovesOnly()
        {
            Assert.IsTrue(GpuNavField.CrowdAppliesTo(GpuNavGoal.Point));
            Assert.IsTrue(GpuNavField.CrowdAppliesTo(GpuNavGoal.Area));
            Assert.IsFalse(GpuNavField.CrowdAppliesTo(GpuNavGoal.Density), "attack goals keep the N3 field (front line must not repel)");
            Assert.IsFalse(GpuNavField.CrowdAppliesTo(GpuNavGoal.None));
        }

        [Test]
        public void Crowd_CountsAliveOwnTeamAgents_AndSmooths()
        {
            var shader = LoadShader();
            var g = MakeGraph(16, 2, false, 0);
            int n = g.CellCount;
            // Origin (-16,-16), 2 m cells: world (x, z) -> cell ((x + 16) / 2, (z + 16) / 2).
            Vector3 P(int cx, int cz) => new Vector3(-16f + cx * 2f + .5f, 0f, -16f + cz * 2f + 1.5f);
            var agents = new AgentData[11]; var teams = new int[11];
            agents[9].position = P(12, 12); teams[9] = 0; agents[9].currentState = 3;  // attacking: not congestion
            agents[10].position = P(12, 12); teams[10] = 0; agents[10].currentState = 2; // engaging: not congestion
            agents[7].position = P(8, 8); teams[7] = 0; agents[7].locomotionSpeed = 3f;   // moving freely: no congestion
            agents[8].position = P(8, 8); teams[8] = 0; agents[8].locomotionSpeed = .75f; // half the free speed: 0.5
            agents[0].position = P(3, 4); teams[0] = 0;
            agents[1].position = P(3, 4); teams[1] = 0;
            agents[2].position = P(3, 4); teams[2] = 1;
            agents[3].position = P(3, 4); teams[3] = 0; agents[3].currentState = 4; // dead: not counted
            agents[4].position = P(10, 2); teams[4] = 1;
            agents[5].position = new Vector3(500f, 0f, 0f); teams[5] = 0;     // outside the grid
            agents[6].position = P(5, 5); teams[6] = 7;                        // team outside the field
            var agentBuffer = new ComputeBuffer(agents.Length, AgentData.StrideBytes);
            var teamBuffer = new ComputeBuffer(teams.Length, 4);
            try
            {
                agentBuffer.SetData(agents); teamBuffer.SetData(teams);
                using (var field = new DisposableField(new GpuNavField(shader, g, 2) { CrowdEnabled = true, CrowdTimeConstant = 1f, CrowdFreeSpeed = 1.5f }))
                {
                    var ema = new float[2 * n];
                    field.F.UpdateCrowd(agentBuffer, teamBuffer, agents.Length, .1f); // first update: EMA = counts
                    field.F.ReadCrowd(ema);
                    int a = 4 * 16 + 3, b = 2 * 16 + 10;
                    Assert.AreEqual(2f, ema[a], "team 0 at (3,4)");
                    Assert.AreEqual(1f, ema[n + a], "team 1 at (3,4)");
                    Assert.AreEqual(1f, ema[n + b], "team 1 at (10,2)");
                    float total = 0; foreach (var e in ema) total += e;
                    Assert.AreEqual(.5f, ema[8 * 16 + 8], 1e-5f, "congestion weight by real speed");
                    Assert.AreEqual(4.5f, total, 1e-5f, "dead, fighting, outside and foreign-team agents are not counted");
                    agents[1].position = P(6, 6); agentBuffer.SetData(agents);
                    field.F.UpdateCrowd(agentBuffer, teamBuffer, agents.Length, Mathf.Log(2f)); // alpha = 1/2
                    field.F.ReadCrowd(ema);
                    Assert.AreEqual(1.5f, ema[a], 1e-5f);
                    Assert.AreEqual(.5f, ema[6 * 16 + 6], 1e-5f);
                    Assert.AreEqual(.5f, ema[8 * 16 + 8], 1e-5f);
                }
            }
            finally { agentBuffer.Release(); teamBuffer.Release(); }
        }

        [Test]
        public void Crowd_BlockingColumn_DeflectsTheGradientSideways()
        {
            // Open 64 x 64, goal at the north edge centre; a dense friendly column fills x = 30..33 for z = 30..50.
            // Behind the column (cell x 30, z 26) the eikonal gradient should point past the column, not straight through it.
            const int r = 64;
            var g = MakeGraph(r, 3, false, 0);
            var goal = new GpuNavGoal { mode = GpuNavGoal.Area, rect = new Vector4(0f, r - 2f, 6f, 1f) };
            var ema = new float[g.CellCount];
            for (int z = 30; z <= 50; z++) for (int x = 30; x <= 33; x++) ema[z * r + x] = 12f;
            var f = GpuNavFieldReference.Filled(g.CellCount, GpuNavFieldGraph.Unreachable);
            var other = new float[g.CellCount];
            for (int p = 0; p < 600; p++) { GpuNavFieldReference.Relax(g, goal, f, other, GpuNavMetric.Eikonal, ema, 2f, 1f / 6f, 1f); var t = f; f = other; other = t; }
            var plain = GpuNavFieldReference.Filled(g.CellCount, GpuNavFieldGraph.Unreachable);
            GpuNavFieldReference.Converge(g, goal, ref plain, 4000, GpuNavMetric.Eikonal);
            int c = 26 * r + 30;
            float Gx(float[] d) => d[c + 1] - d[c - 1];
            float Gz(float[] d) => d[c + r] - d[c - r];
            float sidewaysPlain = Mathf.Abs(Gx(plain)) / Mathf.Sqrt(Gx(plain) * Gx(plain) + Gz(plain) * Gz(plain));
            float sidewaysCrowd = Mathf.Abs(Gx(f)) / Mathf.Sqrt(Gx(f) * Gx(f) + Gz(f) * Gz(f));
            Debug.Log("[N4TEST] behind the column: sideways share plain " + sidewaysPlain + " crowd " + sidewaysCrowd + "; distance plain " + plain[c] + " crowd " + f[c]);
            Assert.Less(sidewaysPlain, .1f, "without crowd the way is straight north");
            Assert.Greater(sidewaysCrowd, .2f, "with the column priced in, the way bends around it");
            Assert.Less(f[c] - plain[c], 30f, "the detour costs only a few cells");
        }

        // ---- N5a: local order slot layers

        static float[] Layer(GpuNavField f, int slot) { var d = new float[f.Graph.CellCount]; f.ReadLocalDistances(slot, d); return d; }

        [Test]
        public void Local_SlotLayer_SampledOnlyAfterSeed_IsTheFixedPoint()
        {
            var shader = LoadShader();
            var g = MakeGraph(48, 31);
            var goal = new GpuNavGoal { mode = GpuNavGoal.Point, cell = WalkableGoal(g, 40, 8) };
            var reference = GpuNavFieldReference.Filled(g.CellCount, GpuNavFieldGraph.Unreachable);
            Assert.Less(GpuNavFieldReference.Converge(g, goal, ref reference, 5000, GpuNavMetric.Eikonal), 5000);
            using (var field = new DisposableField(new GpuNavField(shader, g, 2) { Metric = GpuNavMetric.Eikonal, LocalEnabled = true }))
            {
                var f = field.F;
                f.CommitLocalPointForTest(3, 1, goal.cell);
                f.SetLocalActiveMask(1u << 2);
                Assert.AreEqual(0u, f.LocalValidMask, "no CPU seed uploaded yet: the CPU local flow stays in use");
                f.CompleteLocalSeeds();
                Assert.AreEqual(1u << 2, f.LocalValidMask);
                Assert.AreEqual(1, f.LocalSeedUploads);
                var seeded = Layer(f, 3);
                float rel = MaxRelDiff(seeded, reference, out int bits);
                for (int i = 0; i < 4; i++) f.TickLocalForTest();
                float drift = MaxRelDiff(Layer(f, 3), seeded, out int changed);
                Debug.Log("[N5TEST] local slot seed vs converged CPU: max rel " + rel + " (bit-different " + bits + "); after local relax: max rel " + drift + " (changed " + changed + ")");
                Assert.Less(rel, 1e-5f);
                Assert.Less(drift, 1e-5f);
                foreach (float v in Layer(f, 1)) Assert.AreEqual(GpuNavFieldGraph.Unreachable, v, "other slots untouched");
                var team = new float[g.CellCount]; f.ReadDistances(1, team);
                foreach (float v in team) Assert.AreEqual(GpuNavFieldGraph.Unreachable, v, "team layers untouched");
                f.SetLocalActiveMask(0);
                Assert.AreEqual(0u, f.LocalValidMask, "slots no order references are not sampled");
                f.SetLocalActiveMask(1u << 2); f.LocalEnabled = false;
                Assert.AreEqual(0u, f.LocalValidMask, "toggle off = CPU path");
            }
        }

        [Test]
        public void Local_Recommit_Invalidates_AndAttackHoldStayOnCpu()
        {
            var shader = LoadShader();
            var g = MakeGraph(32, 5);
            using (var field = new DisposableField(new GpuNavField(shader, g, 2) { LocalEnabled = true }))
            {
                var f = field.F;
                f.SetLocalActiveMask(0x1FFu);
                f.CommitLocalPointForTest(2, 0, WalkableGoal(g, 4, 4));
                f.CompleteLocalSeeds();
                Assert.AreEqual(1u << 1, f.LocalValidMask);
                f.CommitLocalPointForTest(2, 0, WalkableGoal(g, 28, 28));
                Assert.AreEqual(0u, f.LocalValidMask, "a new group in the slot never samples the old layer");
                f.CompleteLocalSeeds();
                Assert.AreEqual(1u << 1, f.LocalValidMask);
                int commits = f.LocalCommits;
                f.CommitLocalSlot(2, 0, LocalOrderKind.Attack, null);
                Assert.AreEqual(0u, f.LocalValidMask, "attack groups keep the CPU path");
                f.CommitLocalSlot(2, 0, LocalOrderKind.Hold, null);
                Assert.AreEqual(0u, f.LocalValidMask);
                Assert.AreEqual(commits, f.LocalCommits);
            }
        }

        [Test]
        public void Local_UsesItsOwnTeamsCrowd()
        {
            var shader = LoadShader();
            var g = MakeGraph(48, 12);
            int n = g.CellCount;
            var goal = new GpuNavGoal { mode = GpuNavGoal.Point, cell = WalkableGoal(g, 40, 30) };
            var rng = new System.Random(8);
            var ema = new float[n * 2];
            for (int i = n; i < 2 * n; i++) ema[i] = rng.NextDouble() < .3 ? (float)(rng.NextDouble() * 9.0) : 0f; // team 1 only
            var layer1 = new float[n]; System.Array.Copy(ema, n, layer1, 0, n);
            const float gain = 2f, full = 6f, cap = 1f;
            using (var field = new DisposableField(new GpuNavField(shader, g, 2) { Metric = GpuNavMetric.Graph, LocalEnabled = true, CrowdEnabled = true, CrowdGain = gain, CrowdFullPerCell = full, CrowdCap = cap, PassesPerFrame = 50 }))
            {
                var f = field.F;
                f.WriteCrowd(ema);
                f.SetLocalActiveMask(0x1FFu);
                f.CommitLocalPointForTest(5, 1, goal.cell);
                f.CommitLocalPointForTest(6, 0, goal.cell);
                f.CompleteLocalSeeds();
                var expected = Layer(f, 5); var scratch = new float[n];
                var team0 = Layer(f, 6);
                for (int i = 0; i < 3; i++) f.TickLocalForTest();
                for (int p = 0; p < 150; p++) { GpuNavFieldReference.Relax(g, goal, expected, scratch, GpuNavMetric.Graph, layer1, gain, 1f / full, cap); var t = expected; expected = scratch; scratch = t; }
                var actual = Layer(f, 5);
                float rel = MaxRelDiff(actual, expected, out int bits);
                float team0Drift = MaxRelDiff(Layer(f, 6), team0, out _);
                Debug.Log("[N5TEST] local team-1 layer with team-1 crowd vs CPU (150 passes): max rel " + rel + ", bit-different " + bits + "; team-0 layer drift " + team0Drift);
                Assert.AreEqual(0, bits, "graph metric local layer with crowd must be bit-exact");
                Assert.Greater(MaxRelDiff(actual, team0, out _), .01f, "team 1 crowd changes the team 1 layer");
                Assert.Less(team0Drift, 1e-5f, "team 0 has no crowd: its layer stays the plain fixed point");
            }
        }

        static TerrainNavigationGrid SmallNav(float clearance, bool wall = false)
        {
            var blocked = new bool[256]; if (wall) for (int z = 0; z < 16; z++) blocked[z * 16 + 7] = true;
            var surface = new TerrainSurface("N5", 1, 17, 17, new Vector2(-8, -8), new Vector2(16, 16), 45, new float[289], blocked);
            return new TerrainNavigationGrid(surface, new Vector2(-8, -8), 1, 16, 16, clearance, .1f);
        }

        [Test]
        public void Local_SharesNavigation_OnlyForTheSameClearanceClassAndMask()
        {
            var shader = LoadShader();
            var coarse = SmallNav(.5f);
            using (var field = new DisposableField(new GpuNavField(shader, MakeGraph(32, 2), 2, coarse)))
            {
                Assert.IsTrue(field.F.SharesNavigation(coarse));
                Assert.IsTrue(field.F.SharesNavigation(coarse.CreateIndependentSolver()), "the local factory's solver copy");
                Assert.IsTrue(field.F.SharesNavigation(SmallNav(.5f)));
                Assert.IsFalse(field.F.SharesNavigation(SmallNav(1.5f)), "larger clearance class");
                Assert.IsFalse(field.F.SharesNavigation(SmallNav(.5f, wall: true)), "different walkable mask");
                Assert.IsFalse(field.F.SharesNavigation(null));
            }
        }

        // ---- N5b: formation line goals

        static Vector2 CellCentre(GpuNavFieldGraph g, int c) => new Vector2(g.Origin.x + (c % g.Resolution + .5f) * g.CellSize, g.Origin.y + (c / g.Resolution + .5f) * g.CellSize);

        [TestCase(GpuNavMetric.Graph)]
        [TestCase(GpuNavMetric.Eikonal)]
        public void Line_GpuMatchesReference_AndSeedIsTheFixedPoint(GpuNavMetric metric)
        {
            var shader = LoadShader();
            var g = MakeGraph(48, 17, wall: true, blockedFraction: .005);
            using (var field = new DisposableField(new GpuNavField(shader, g, 2) { Metric = metric }))
            {
                var f = field.F;
                int targetCell = WalkableGoal(g, 12, 36);
                var target = CellCentre(g, targetCell);
                Assert.IsTrue(f.BuildLineGoal(targetCell, target, target - new Vector2(10f, 40f), new Vector2(16f, 20f), 200, out var goal));
                Assert.Greater(goal.cells.Length, 4);
                int n = g.CellCount;
                var expected = GpuNavFieldReference.Filled(n, GpuNavFieldGraph.Unreachable); var scratch = new float[n]; var actual = new float[n];
                f.SetGoalForTest(1, goal);
                f.RunPasses(null, 1, 150);
                for (int p = 0; p < 150; p++) { GpuNavFieldReference.Relax(g, goal, expected, scratch, metric); var t = expected; expected = scratch; scratch = t; }
                f.ReadDistances(1, actual);
                float rel = MaxRelDiff(actual, expected, out int bits);
                foreach (int c in goal.cells) Assert.AreEqual(0f, actual[c], "every line cell is a goal");
                var reference = GpuNavFieldReference.Filled(n, GpuNavFieldGraph.Unreachable);
                Assert.Less(GpuNavFieldReference.Converge(g, goal, ref reference, 5000, metric), 5000);
                f.SeedNow(0, goal);
                var seeded = new float[n]; f.ReadDistances(0, seeded);
                float seedRel = MaxRelDiff(seeded, reference, out _);
                Debug.Log("[N5BTEST] line " + metric + ": cells " + goal.cells.Length + "; GPU vs CPU 150 passes max rel " + rel + " bit-different " + bits + "; seed vs converged " + seedRel);
                if (metric == GpuNavMetric.Graph) Assert.AreEqual(0, bits); else Assert.Less(rel, 1e-5f);
                Assert.Less(seedRel, 1e-5f);
            }
        }

        [Test]
        public void Line_Geometry_PerpendicularAheadClippedAndFallback()
        {
            var shader = LoadShader();
            var open = MakeGraph(64, 3, wall: false, blockedFraction: 0);
            using (var field = new DisposableField(new GpuNavField(shader, open, 1)))
            {
                var f = field.F;
                int tc = f.FineCell(new Vector2(1f, -19f));
                var target = CellCentre(open, tc);
                // Army south of the target, 40 m wide (x); 4000 agents at 0.6 m^2 re-form 60 m deep: an east-west line 30 m beyond the target.
                Assert.IsTrue(f.BuildLineGoal(tc, target, target - new Vector2(0f, 40f), new Vector2(20f, 30f), 4000, out var line));
                int row = line.cells[0] / open.Resolution;
                foreach (int c in line.cells) Assert.AreEqual(row, c / open.Resolution, "perpendicular to the approach = one row");
                Assert.AreEqual(target.y + 30f, CellCentre(open, line.cells[0]).y, 1.01f, "half the depth beyond the target");
                Assert.AreEqual(21, line.cells.Length, "40 m frontage at 2 m cells");
                Assert.AreEqual(-20f, line.line.x, .01f); Assert.AreEqual(20f, line.line.y, .01f);
                // Diagonal approach: every cell lies on the line (within one cell) and the chain is 8-connected.
                Assert.IsTrue(f.BuildLineGoal(tc, target, target - new Vector2(30f, 30f), new Vector2(15f, 15f), 1000, out var diag));
                var u = new Vector2(diag.line.z, diag.line.w); var centre = new Vector2(diag.rect.x, diag.rect.y);
                foreach (int c in diag.cells) Assert.LessOrEqual(Mathf.Abs(Vector2.Dot(CellCentre(open, c) - centre, u)), open.CellSize * 1.01f);
                // The offset is capped.
                f.LineMaxOffset = 6f;
                Assert.IsTrue(f.BuildLineGoal(tc, target, target - new Vector2(0f, 40f), new Vector2(20f, 30f), 1000000, out var capped));
                Assert.AreEqual(target.y + 6f, CellCentre(open, capped.cells[0]).y, 1.01f);
                // No direction (army already on the target) = no line.
                Assert.IsFalse(f.BuildLineGoal(tc, target, target + new Vector2(1f, 0f), new Vector2(20f, 30f), 4000, out _));
            }
            var walled = MakeGraph(64, 3, wall: true, blockedFraction: 0); // wall at x = 32 (gap rows 30..33)
            using (var field = new DisposableField(new GpuNavField(shader, walled, 1)))
            {
                var f = field.F;
                int tc = 50 * 64 + 26; // row 50, six cells west of the wall
                var target = CellCentre(walled, tc);
                Assert.IsTrue(f.BuildLineGoal(tc, target, target - new Vector2(0f, 40f), new Vector2(40f, 0f), 0, out var line));
                foreach (int c in line.cells) Assert.Less(c % 64, 32, "the line stops at the wall, never jumps it");
                Assert.AreEqual(-10f, line.line.x, 1.01f, "east end (v points west) right before the wall");
                Assert.AreEqual(40f, line.line.y, .01f, "west end: the full half frontage");
            }
        }

        [Test]
        public void Line_BuiltOncePerOrder_FromTelemetry_PointWithoutIt()
        {
            var shader = LoadShader();
            var g = MakeGraph(64, 3, wall: false, blockedFraction: 0);
            using (var field = new DisposableField(new GpuNavField(shader, g, 2) { FormationLineEnabled = true, LineTelemetryWaitFrames = 0 }))
            {
                var f = field.F;
                var centroid = new Vector3(0f, 0f, -50f); bool valid = true;
                f.TeamSpatial = t => new TeamSpatialTelemetry { valid = valid, aliveCount = 100, centroid = centroid, bounds = new Bounds(centroid, new Vector3(30f, 1f, 20f)) };
                var flows = new[] { new TeamFlowFrameSettings { enabled = true, targetMode = 1, targetPoint = new Vector3(0f, 0f, 20f) }, default };
                var context = new PipelineFrameContext { teamFlows = flows };
                var a = f.ResolveGoal(context, 0);
                Assert.AreEqual(GpuNavGoal.Line, a.mode);
                centroid = new Vector3(40f, 0f, 0f); // the army moves: the order's line must not change
                var b = f.ResolveGoal(context, 0);
                Assert.IsTrue(a.Equals(b)); Assert.AreSame(a.bits, b.bits);
                Assert.AreEqual(1, f.LineGoals);
                flows[0].targetPoint = new Vector3(-20f, 0f, 30f); // a new order builds a new line
                var c = f.ResolveGoal(context, 0);
                Assert.AreEqual(GpuNavGoal.Line, c.mode); Assert.IsFalse(a.Equals(c)); Assert.AreEqual(2, f.LineGoals);
                valid = false; flows[0].targetPoint = new Vector3(10f, 0f, 30f);
                Assert.AreEqual(GpuNavGoal.Point, f.ResolveGoal(context, 0).mode, "no telemetry: the original point goal");
                Assert.AreEqual(1, f.LineFallbacks);
                valid = true; f.FormationLineEnabled = false; flows[0].targetPoint = new Vector3(5f, 0f, 30f);
                Assert.AreEqual(GpuNavGoal.Point, f.ResolveGoal(context, 0).mode, "toggle off = point");
                Assert.AreEqual(0u, f.LineMask);
            }
        }

        // ---- N5c: arrival zone

        [Test]
        public void ArrivalZone_FromLineDepthOrPointDisc_OffByDefault()
        {
            var shader = LoadShader();
            var g = MakeGraph(64, 3, wall: false, blockedFraction: 0);
            using (var field = new DisposableField(new GpuNavField(shader, g, 2)))
            {
                var f = field.F;
                int tc = f.FineCell(new Vector2(1f, -19f));
                var target = CellCentre(g, tc);
                Assert.IsTrue(f.BuildLineGoal(tc, target, target - new Vector2(0f, 40f), new Vector2(20f, 30f), 2000, out var line));
                Assert.AreEqual(2000 * .6f / 40f, line.depth, 1e-3f);
                f.SetGoalForTest(0, line);
                Assert.AreEqual(0f, f.ArrivalZone(0), "toggle off");
                f.ArrivalFollowEnabled = true;
                Assert.AreEqual(line.depth * 1.5f, f.ArrivalZone(0), 1e-3f);
                f.SetGoalForTest(1, new GpuNavGoal { mode = GpuNavGoal.Point, cell = tc });
                Assert.AreEqual(0f, f.ArrivalZone(1), "point without telemetry");
                f.TeamSpatial = t => new TeamSpatialTelemetry { valid = true, aliveCount = 1000 };
                Assert.AreEqual(Mathf.Sqrt(1000 * .6f / Mathf.PI) * 1.5f, f.ArrivalZone(1), 1e-3f);
                f.SetGoalForTest(1, new GpuNavGoal { mode = GpuNavGoal.Density });
                Assert.AreEqual(0f, f.ArrivalZone(1), "dynamic pursuit has no arrival zone");
                Assert.AreEqual(0f, f.ArrivalZone(7));
            }
        }

        sealed class DisposableField : System.IDisposable
        {
            public readonly GpuNavField F;
            public DisposableField(GpuNavField f) { F = f; }
            public void Dispose() => F.Dispose();
        }
    }
}
