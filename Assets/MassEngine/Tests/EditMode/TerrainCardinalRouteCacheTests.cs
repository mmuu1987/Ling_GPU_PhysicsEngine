using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace MassEngine.Tests
{
    /// <summary>
    /// Equivalence guards for the P3 cardinal-route cache. These also pass with the legacy
    /// uncached implementation; they are not a failing-before/fixed-after bug reproduction.
    /// Small CPU-only fixtures, no stopwatch assertions, scene edits or GPU allocation.
    /// </summary>
    public sealed class TerrainCardinalRouteCacheTests
    {
        private static readonly FieldInfo NeighboursField = typeof(TerrainNavigationGrid)
            .GetField("neighbours", BindingFlags.Instance | BindingFlags.NonPublic);

        private sealed class SlowRouteOracle
        {
            private readonly int width, count;
            private readonly uint[] walkable;
            private readonly byte[] neighbours;

            public SlowRouteOracle(TerrainNavigationGrid nav)
            {
                Assert.NotNull(NeighboursField, "Update the test snapshot adapter if the graph storage is renamed.");
                width = nav.ResolutionX;
                count = nav.CellCount;
                walkable = nav.CopyWalkable();
                // Reflection occurs once per grid, outside all route queries. No mutation of the graph.
                neighbours = (byte[])((byte[])NeighboursField.GetValue(nav)).Clone();
            }

            public bool HasRoute(int from, int to)
            {
                if (from < 0 || to < 0 || from >= count || to >= count ||
                    walkable[from] == 0 || walkable[to] == 0) return false;
                if (from == to) return true;
                int step, bit;
                if (from / width == to / width) { step = to > from ? 1 : -1; bit = step > 0 ? 0 : 4; }
                else if (from % width == to % width) { step = to > from ? width : -width; bit = step > 0 ? 1 : 5; }
                else return false;
                for (int cell = from; cell != to; cell += step)
                    if ((neighbours[cell] & (1 << bit)) == 0) return false;
                return true;
            }
        }

        private static TerrainNavigationGrid Grid(string layout, int width, int depth)
        {
            float cellSize = layout == "fractional" ? .3f : 1f;
            Vector2 origin = layout == "fractional" ? new Vector2(13.25f, -9.75f) : Vector2.zero;
            var heights = new float[(width + 1) * (depth + 1)];
            var blocked = new bool[width * depth];
            var obstacles = new List<StaticObstacleRect>();
            float clearance = 0, boundary = 0;
            switch (layout)
            {
                case "flat":
                case "fractional":
                    break;
                case "holes":
                    blocked[(depth / 2) * width + width / 2] = true;
                    if (width > 5 && depth > 4) blocked[width + 2] = true;
                    break;
                case "wall-gap":
                    obstacles.Add(new StaticObstacleRect(new Vector2(width / 2 + .5f, .5f), new Vector2(.2f, 2f)));
                    obstacles.Add(new StaticObstacleRect(new Vector2(width / 2 + .5f, depth - .5f), new Vector2(.2f, 2f)));
                    break;
                case "closed-wall":
                    obstacles.Add(new StaticObstacleRect(new Vector2(width / 2 + .5f, depth * .5f), new Vector2(.2f, depth + 2f)));
                    break;
                case "hills":
                    for (int z = 0; z <= depth; z++)
                        for (int x = 0; x <= width; x++)
                            heights[z * (width + 1) + x] = .13f * x + .21f * z + .17f * ((x + z) % 3);
                    break;
                case "clearance":
                    clearance = .15f;
                    boundary = .2f;
                    obstacles.Add(new StaticObstacleRect(new Vector2(width * .5f, depth * .5f), new Vector2(.6f, 1.2f)));
                    break;
                default:
                    throw new ArgumentException("Unknown test layout: " + layout);
            }
            var surface = new TerrainSurface("cardinal-cache-" + layout, 1, width + 1, depth + 1,
                origin, new Vector2(width * cellSize, depth * cellSize), 60, heights, blocked);
            return new TerrainNavigationGrid(surface, origin, cellSize, width, depth,
                clearance, boundary, obstacles.ToArray());
        }

        [TestCase("flat", 6, 4)]
        [TestCase("holes", 8, 6)]
        [TestCase("wall-gap", 9, 7)]
        [TestCase("closed-wall", 9, 7)]
        [TestCase("hills", 7, 5)]
        [TestCase("fractional", 6, 5)]
        [TestCase("clearance", 9, 7)]
        [TestCase("flat", 8, 1)]
        [TestCase("holes", 8, 1)]
        [TestCase("flat", 1, 8)]
        [TestCase("holes", 1, 8)]
        [TestCase("flat", 1, 1)]
        [TestCase("holes", 1, 1)]
        public void EveryOrderedPairAndInvalidIndexMatchesLegacyEdges(string layout, int width, int depth)
        {
            var nav = Grid(layout, width, depth);
            var slow = new SlowRouteOracle(nav);
            int checks = 0, clearRoutes = 0;
            // Ordered pairs explicitly cover both orientations, self, boundaries and non-cardinal pairs.
            for (int from = 0; from < nav.CellCount; from++)
                for (int to = 0; to < nav.CellCount; to++)
                {
                    bool expected = slow.HasRoute(from, to);
                    Assert.AreEqual(expected, nav.HasClearCardinalRoute33(from, to),
                        layout + " route " + from + " -> " + to);
                    checks++;
                    if (expected) clearRoutes++;
                }
            int[] invalid = { -1, -3, int.MinValue, nav.CellCount, nav.CellCount + 1, int.MaxValue };
            foreach (int bad in invalid)
            {
                for (int cell = 0; cell < nav.CellCount; cell++)
                {
                    Assert.False(nav.HasClearCardinalRoute33(bad, cell));
                    Assert.False(nav.HasClearCardinalRoute33(cell, bad));
                    checks += 2;
                }
                foreach (int other in invalid)
                {
                    Assert.False(nav.HasClearCardinalRoute33(bad, other));
                    checks++;
                }
            }
            TestContext.WriteLine(layout + " " + width + "x" + depth + ": " + checks +
                " route checks, " + clearRoutes + " clear ordered routes; no timing assertions.");
        }

        [Test]
        public void DifferentGridInstancesAndDetoursDoNotShareCardinalConnectivity()
        {
            var open = Grid("flat", 9, 7);
            var detour = Grid("wall-gap", 9, 7);
            var closed = Grid("closed-wall", 9, 7);
            var narrow = Grid("flat", 1, 8);
            Vector2 left = new Vector2(1.5f, .5f), right = new Vector2(7.5f, .5f);
            int from = 1, to = 7;
            Assert.True(detour.AreConnected(left, right), "Fixture must allow a detour around the wall end.");
            Assert.False(detour.HasClearCardinalRoute33(from, to), "General component membership is not a clear straight route.");
            Assert.False(closed.HasClearCardinalRoute33(from, to));
            Assert.True(narrow.HasClearCardinalRoute33(0, 7));
            // Revisit the first grid after constructing graphs with different masks and dimensions.
            Assert.True(open.HasClearCardinalRoute33(from, to));
            Assert.True(open.HasClearCardinalRoute33(to, from));
            Assert.False(detour.HasClearCardinalRoute33(to, from));
        }

        private static List<Vector2> Goals(TerrainNavigationGrid nav)
        {
            var goals = new List<Vector2>();
            if (nav.ResolutionX == 1 || nav.ResolutionZ == 1)
            {
                Vector2 end = nav.CellCenter(nav.CellCount - 1);
                if (nav.IsWalkable(end)) goals.Add(end);
            }
            else
            {
                for (int z = 1; z < nav.ResolutionZ - 1; z++)
                {
                    int x = Math.Max(0, nav.ResolutionX - 2 - (z % 3 == 0 ? 3 : 0));
                    Vector2 goal = nav.CellCenter(z * nav.ResolutionX + x);
                    if (nav.IsWalkable(goal)) goals.Add(goal);
                }
            }
            Assert.That(goals.Count, Is.GreaterThan(0), "Lane fixture needs a real walkable target.");
            // Do not deduplicate: the existing lane mean weights repeated valid goals.
            for (int repeat = 0; repeat < 3; repeat++) goals.Add(goals[0]);
            AddInvalidGoals(nav, goals);
            return goals;
        }

        private static void AddInvalidGoals(TerrainNavigationGrid nav, List<Vector2> goals)
        {
            goals.Add(new Vector2(float.NaN, nav.Origin.y));
            goals.Add(new Vector2(nav.Origin.x, float.PositiveInfinity));
            goals.Add(new Vector2(float.NegativeInfinity, nav.Origin.y));
            goals.Add(nav.Origin - Vector2.one);
            goals.Add(nav.Origin + new Vector2((nav.ResolutionX + 1) * nav.CellSize, 0));
            uint[] mask = nav.CopyWalkable();
            for (int cell = 0; cell < mask.Length; cell++)
                if (mask[cell] == 0) { goals.Add(nav.CellCenter(cell)); break; }
        }

        [TestCase("flat", 16, 12, 0f)]
        [TestCase("flat", 16, 12, .6f)]
        [TestCase("flat", 16, 12, 2f)]
        [TestCase("flat", 16, 12, 4f)]
        [TestCase("flat", 16, 12, 20f)]
        [TestCase("holes", 16, 12, .6f)]
        [TestCase("wall-gap", 9, 7, 0f)]
        [TestCase("closed-wall", 9, 7, 2f)]
        [TestCase("hills", 16, 12, .6f)]
        [TestCase("fractional", 16, 12, .6f)]
        [TestCase("flat", 16, 1, 0f)]
        [TestCase("flat", 1, 16, 0f)]
        public void LaneFinalComponentsMatchLegacyIncludingDuplicatesAndFilteredGoals(
            string layout, int width, int depth, float stopRadius)
        {
            var nav = Grid(layout, width, depth);
            var goals = Goals(nav);
            Vector2[] goalSnapshot = goals.ToArray();
            Vector2[] original = nav.CreateFlowField(goals, stopRadius);
            Vector2[] expected = (Vector2[])original.Clone(), actual = (Vector2[])original.Clone();
            LegacyApply(nav, goals, expected, stopRadius, new SlowRouteOracle(nav));
            TerrainLaneApproach33.Apply(nav, goals, actual, stopRadius);
            AssertComponents(expected, actual);
            AssertComponents(goalSnapshot, goals.ToArray());
            int changed = 0, stopped = 0;
            for (int cell = 0; cell < original.Length; cell++)
            {
                if (original[cell].x == 0 && original[cell].y == 0)
                {
                    AssertComponents(Vector2.zero, actual[cell], "Stopped/unreachable " + cell);
                    stopped++;
                }
                if (original[cell].x != actual[cell].x || original[cell].y != actual[cell].y) changed++;
            }
            if (layout == "flat" && width > 1 && depth > 1 && stopRadius <= 4)
                Assert.That(changed, Is.GreaterThan(0), "The lane replacement branch must actually be exercised.");
            TestContext.WriteLine(layout + " lane " + width + "x" + depth + " radius=" + stopRadius +
                ": " + actual.Length + " exact vector comparisons, changed=" + changed + ", zero-preserved=" + stopped);
        }

        [Test]
        public void NullEmptyAndEntirelyInvalidGoalsLeaveDirectionsUnchanged()
        {
            var nav = Grid("holes", 12, 12);
            var invalid = new List<Vector2>();
            AddInvalidGoals(nav, invalid);
            foreach (IReadOnlyList<Vector2> goals in new IReadOnlyList<Vector2>[] { null, Array.Empty<Vector2>(), invalid })
            {
                var original = new Vector2[nav.CellCount];
                for (int cell = 0; cell < original.Length; cell++) original[cell] = new Vector2(.6f, .8f);
                var expected = (Vector2[])original.Clone();
                var actual = (Vector2[])original.Clone();
                LegacyApply(nav, goals, expected, .6f, new SlowRouteOracle(nav));
                TerrainLaneApproach33.Apply(nav, goals, actual, .6f);
                AssertComponents(original, actual);
                AssertComponents(expected, actual);
            }
        }

        [Test]
        public void DuplicateTargetsRetainTheirWeightInTheMeanAxisDecision()
        {
            var nav = Grid("flat", 12, 12);
            int cell = 13;
            var horizontal = new Vector2(9.5f, 1.5f);
            var vertical = new Vector2(1.5f, 9.5f);
            var unique = new[] { horizontal, vertical };
            var duplicated = new[] { horizontal, vertical, vertical };
            AssertLaneCell(nav, cell, unique, new Vector2(.6f, .8f), 0, Vector2.right);
            AssertLaneCell(nav, cell, duplicated, new Vector2(.6f, .8f), 0, Vector2.up);
        }

        [TestCase(.49999997f, false)]
        [TestCase(.5f, true)]
        [TestCase(.50000006f, true)]
        public void DotThresholdRetainsStrictLessThanHalf(float dot, bool replace)
        {
            var nav = Grid("flat", 12, 3);
            var original = new Vector2(dot, .8f);
            AssertLaneCell(nav, 13, new[] { new Vector2(9.5f, 1.5f) }, original, 0,
                replace ? Vector2.right : original);
        }

        [TestCase(0f, 3, false)]
        [TestCase(0f, 4, true)]
        [TestCase(0f, 5, true)]
        [TestCase(5f, 5, false)]
        [TestCase(5f, 6, true)]
        [TestCase(5f, 7, true)]
        public void NearThresholdKeepsBothCellAndStopRadiusTermsAndStrictComparison(float stopRadius, int distance, bool replace)
        {
            var nav = Grid("flat", 12, 3);
            var original = new Vector2(.6f, .8f);
            AssertLaneCell(nav, 13, new[] { new Vector2(1.5f + distance, 1.5f) }, original, stopRadius,
                replace ? Vector2.right : original);
        }

        [TestCase(1, 0)]
        [TestCase(-1, 0)]
        [TestCase(0, 1)]
        [TestCase(0, -1)]
        public void AllCardinalSignsUseTheSameLegacyClearRoute(int dx, int dz)
        {
            var nav = Grid("flat", 13, 13);
            int cell = 6 * 13 + 6;
            Vector2 here = nav.CellCenter(cell), proposed = new Vector2(dx, dz);
            var original = dx != 0 ? new Vector2(dx * .6f, .8f) : new Vector2(.8f, dz * .6f);
            AssertLaneCell(nav, cell, new[] { here + proposed * 5 }, original, 0, proposed);
        }

        [Test]
        public void CurrentTargetCellIsNotReplacedEvenWithNonzeroInput()
        {
            var nav = Grid("flat", 12, 12);
            var original = new Vector2(.6f, .8f);
            AssertLaneCell(nav, 13, new[] { nav.CellCenter(13), new Vector2(9.5f, 1.5f) }, original, 0, original);
        }

        private static void AssertLaneCell(TerrainNavigationGrid nav, int cell, IReadOnlyList<Vector2> goals,
            Vector2 original, float stopRadius, Vector2 expectedCell)
        {
            // Crafted input vectors pin Apply's public branch boundaries; they do not assert that
            // Dijkstra emits these vectors at the same radius. Integrated fields are tested above.
            var expected = new Vector2[nav.CellCount];
            expected[cell] = original;
            var actual = (Vector2[])expected.Clone();
            LegacyApply(nav, goals, expected, stopRadius, new SlowRouteOracle(nav));
            TerrainLaneApproach33.Apply(nav, goals, actual, stopRadius);
            AssertComponents(expected, actual);
            AssertComponents(expectedCell, actual[cell], "Explicit branch result");
        }

        private static void AssertComponents(Vector2[] expected, Vector2[] actual)
        {
            Assert.AreEqual(expected.Length, actual.Length);
            for (int cell = 0; cell < expected.Length; cell++)
                AssertComponents(expected[cell], actual[cell], "Cell " + cell);
        }

        private static void AssertComponents(Vector2 expected, Vector2 actual, string message)
        {
            // Scalar exact equality, not Unity Vector2's approximate == operator and not a tolerance.
            Assert.AreEqual(expected.x, actual.x, message + " X");
            Assert.AreEqual(expected.y, actual.y, message + " Z");
        }

        private static void LegacyApply(TerrainNavigationGrid nav, IReadOnlyList<Vector2> goals,
            Vector2[] directions, float stopRadius, SlowRouteOracle slow)
        {
            // Frozen legacy TerrainLaneApproach33.Apply. Only the route call is redirected to the
            // independent slow edge walker, so a production cache error cannot contaminate the oracle.
            if (goals == null || goals.Count == 0) return;
            var rows = new List<int>[nav.ResolutionZ];
            var columns = new List<int>[nav.ResolutionX];
            Vector2 mean = Vector2.zero;
            int total = 0;
            foreach (var p in goals)
            {
                if (!nav.TryGetCell(p, out int cell) || !nav.IsWalkable(p)) continue;
                int x = cell % nav.ResolutionX, z = cell / nav.ResolutionX;
                if (rows[z] == null) rows[z] = new List<int>();
                if (columns[x] == null) columns[x] = new List<int>();
                rows[z].Add(cell);
                columns[x].Add(cell);
                mean += nav.CellCenter(cell);
                total++;
            }
            if (total == 0) return;
            mean /= total;
            foreach (var list in rows) list?.Sort();
            foreach (var list in columns) list?.Sort();
            float near = Mathf.Max(nav.CellSize * 4, stopRadius + nav.CellSize);
            for (int cell = 0; cell < directions.Length; cell++)
            {
                Vector2 original = directions[cell];
                if (original.sqrMagnitude < .0001f) continue;
                Vector2 here = nav.CellCenter(cell), toward = mean - here;
                bool horizontal = Mathf.Abs(toward.x) >= Mathf.Abs(toward.y);
                int sign = (horizontal ? toward.x : toward.y) >= 0 ? 1 : -1;
                Vector2 proposed = horizontal ? new Vector2(sign, 0) : new Vector2(0, sign);
                if (Vector2.Dot(proposed, original) < .5f) continue;
                var list = horizontal ? rows[cell / nav.ResolutionX] : columns[cell % nav.ResolutionX];
                if (list == null) continue;
                int at = list.BinarySearch(cell);
                if (at < 0) at = ~at;
                else continue;
                int pick = sign > 0 ? at : at - 1;
                if (pick < 0 || pick >= list.Count) continue;
                int target = list[pick];
                if (Vector2.Distance(here, nav.CellCenter(target)) < near || !slow.HasRoute(cell, target)) continue;
                directions[cell] = proposed;
            }
        }
    }
}
