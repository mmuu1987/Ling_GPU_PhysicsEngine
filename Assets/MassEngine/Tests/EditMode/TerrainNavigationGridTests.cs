using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace MassEngine.Tests
{
    public sealed class TerrainNavigationGridTests
    {
        private static TerrainSurface Flat(int width, int depth, Vector2? origin = null) =>
            new TerrainSurface("navigation-test", 1, width + 1, depth + 1, origin ?? Vector2.zero,
                new Vector2(width, depth), 80, new float[(width + 1) * (depth + 1)], new bool[width * depth]);

        private static TerrainNavigationGrid Grid(int width, int depth, StaticObstacleRect[] obstacles = null,
            float clearance = 0, float obstaclePadding = 0) =>
            new TerrainNavigationGrid(Flat(width, depth), Vector2.zero, 1, width, depth, clearance, 0,
                obstacles, obstaclePadding);

        private static StaticObstacleRect BlockCell(int x, int z) =>
            new StaticObstacleRect(new Vector2(x + .5f, z + .5f), new Vector2(.2f, .2f));

        private static TerrainNavigationGrid WallWithGap() => Grid(9, 9,
            new[] { new StaticObstacleRect(new Vector2(4.5f, 3), new Vector2(.2f, 6)) }, .1f);

        private static int Cell(TerrainNavigationGrid grid, Vector2 position)
        {
            Assert.True(grid.TryGetCell(position, out int cell));
            return cell;
        }

        private static int Next(TerrainNavigationGrid grid, Vector2[] field, int current)
        {
            Vector2 direction = field[current];
            Assert.That(direction.magnitude, Is.EqualTo(1).Within(1e-5));
            int dx = Math.Sign(direction.x), dz = Math.Sign(direction.y);
            int x = current % grid.ResolutionX, z = current / grid.ResolutionX;
            int next = Cell(grid, grid.CellCenter(current) + new Vector2(dx, dz) * grid.CellSize);
            Assert.True(grid.IsWalkable(grid.CellCenter(next)));
            if (dx != 0 && dz != 0)
            {
                Assert.True(grid.IsWalkable(grid.CellCenter(z * grid.ResolutionX + x + dx)), "Diagonal X side blocked");
                Assert.True(grid.IsWalkable(grid.CellCenter((z + dz) * grid.ResolutionX + x)), "Diagonal Z side blocked");
            }
            Assert.That(Vector2.Distance(direction, (grid.CellCenter(next) - grid.CellCenter(current)).normalized),
                Is.LessThan(1e-5));
            return next;
        }

        [Test]
        public void RoutesAroundWallThroughGapAndTerminatesWithoutCycles()
        {
            var grid = WallWithGap();
            var start = new Vector2(1.5f, 2.5f);
            var target = new Vector2(7.5f, 2.5f);
            Assert.True(grid.AreConnected(start, target));
            var field = grid.CreateFlowField(new[] { target }, 0);
            int cell = Cell(grid, start), destination = Cell(grid, target), highestRow = 0;
            var visited = new HashSet<int>();
            while (cell != destination)
            {
                Assert.True(visited.Add(cell), "Flow contains a cycle");
                highestRow = Math.Max(highestRow, cell / grid.ResolutionX);
                cell = Next(grid, field, cell);
            }
            Assert.That(highestRow, Is.GreaterThanOrEqualTo(7), "Route did not use the wall opening");
            Assert.AreEqual(Vector2.zero, field[destination]);
        }

        [Test]
        public void SteeringOffCenterToDecodedNeighbourStaysInUsableCells()
        {
            var grid = WallWithGap();
            var field = grid.CreateFlowField(new[] { new Vector2(7.5f, 2.5f) }, 0);
            var offsets = new[] { new Vector2(-.49f, -.49f), new Vector2(.49f, -.49f),
                new Vector2(-.49f, .49f), new Vector2(.49f, .49f) };
            for (int cell = 0; cell < field.Length; cell++)
            {
                if (field[cell] == Vector2.zero) continue;
                Vector2 nextCenter = grid.CellCenter(Next(grid, field, cell));
                foreach (Vector2 offset in offsets)
                {
                    Vector2 actualPosition = grid.CellCenter(cell) + offset;
                    for (int step = 0; step <= 32; step++)
                        Assert.True(grid.IsWalkable(Vector2.Lerp(actualPosition, nextCenter, step / 32f)));
                }
            }
        }

        [Test]
        public void DisconnectedRegionHasNoStraightLineFallback()
        {
            var grid = Grid(9, 9, new[] { new StaticObstacleRect(new Vector2(4.5f, 4.5f), new Vector2(.2f, 12)) });
            var left = new Vector2(2.5f, 4.5f);
            var right = new Vector2(6.5f, 4.5f);
            Assert.False(grid.AreConnected(left, right));
            Assert.AreNotEqual(grid.ComponentAt(left), grid.ComponentAt(right));
            Assert.AreEqual(-1, grid.ComponentAt(new Vector2(4.5f, 4.5f)));
            Assert.AreEqual(-1, grid.ComponentAt(new Vector2(-1, 0)));
            Assert.False(grid.AreConnected(new Vector2(4.5f, 4.5f), new Vector2(4.5f, 4.5f)));
            var field = grid.CreateFlowField(new[] { right }, 0);
            for (int z = 0; z < 9; z++)
                for (int x = 0; x <= 4; x++) Assert.AreEqual(Vector2.zero, field[z * 9 + x]);
            Assert.AreNotEqual(Vector2.zero, field[Cell(grid, new Vector2(7.5f, 4.5f))]);
        }

        [Test]
        public void BlockedTargetsAreIgnoredEvenWithLargeStopRadiusAndNeverProjected()
        {
            var grid = Grid(5, 5, new[] { BlockCell(2, 2) });
            var invalid = new[] { new Vector2(2.5f, 2.5f), new Vector2(-1, 0),
                new Vector2(float.NaN, 1), new Vector2(5, 1), new Vector2(1, float.PositiveInfinity) };
            foreach (Vector2 direction in grid.CreateFlowField(invalid, 100)) Assert.AreEqual(Vector2.zero, direction);
            foreach (Vector2 direction in grid.CreateFlowField(null, 0)) Assert.AreEqual(Vector2.zero, direction);
            foreach (Vector2 direction in grid.CreateFlowField(Array.Empty<Vector2>(), 0)) Assert.AreEqual(Vector2.zero, direction);
            var valid = new Vector2(4.5f, 4.5f);
            var combined = new List<Vector2>(invalid) { valid, valid };
            CollectionAssert.AreEqual(grid.CreateFlowField(new[] { valid }, 0), grid.CreateFlowField(combined, 0));
        }

        [Test]
        public void TwoBlockedSideCellsPreventDiagonalConnectivity()
        {
            var grid = Grid(2, 2, new[] { BlockCell(1, 0), BlockCell(0, 1) });
            Assert.True(grid.IsWalkable(new Vector2(.5f, .5f)));
            Assert.True(grid.IsWalkable(new Vector2(1.5f, 1.5f)));
            Assert.False(grid.AreConnected(new Vector2(.5f, .5f), new Vector2(1.5f, 1.5f)));
            Assert.AreEqual(Vector2.zero, grid.CreateFlowField(new[] { new Vector2(1.5f, 1.5f) }, 0)[0]);
        }

        [Test]
        public void EvenOneBlockedSideCellRequiresTwoCardinalStepsInsteadOfDiagonal()
        {
            var grid = Grid(2, 2, new[] { BlockCell(1, 0) });
            var field = grid.CreateFlowField(new[] { new Vector2(1.5f, 1.5f) }, 0);
            Assert.AreEqual(Vector2.up, field[0]);
            Assert.AreEqual(Vector2.right, field[2]);
            Assert.True(grid.AreConnected(new Vector2(.5f, .5f), new Vector2(1.5f, 1.5f)));
        }

        [Test]
        public void ClearanceClosesPassageTooNarrowForAgentRadius()
        {
            var walls = new[] { new StaticObstacleRect(new Vector2(3.1f, 4.5f), new Vector2(.2f, 9)),
                new StaticObstacleRect(new Vector2(5.9f, 4.5f), new Vector2(.2f, 9)) };
            var small = Grid(9, 9, walls, .2f);
            var large = Grid(9, 9, walls, 1.4f); // Radius diameter 2.8 exceeds the 2.6m opening.
            var center = new Vector2(4.5f, 4.5f);
            Assert.True(small.IsWalkable(center));
            Assert.True(small.AreConnected(new Vector2(4.5f, 2.5f), new Vector2(4.5f, 6.5f)));
            Assert.False(large.IsWalkable(center));
            Assert.AreEqual(Vector2.zero, large.CreateFlowField(new[] { center }, 0)[Cell(large, center)]);
        }

        [Test]
        public void ObstaclePaddingAndClearanceBothCoverWholeCellNotOnlyCenter()
        {
            var obstacle = new[] { new StaticObstacleRect(new Vector2(2.3f, 2.5f), new Vector2(.2f, .2f)) };
            var point = new Vector2(1.5f, 2.5f);
            Assert.True(Grid(5, 5, obstacle).IsWalkable(point));
            Assert.False(Grid(5, 5, obstacle, .1f, .15f).IsWalkable(point));
            Assert.False(Grid(5, 5, obstacle, .25f).IsWalkable(point));
            Assert.False(Grid(5, 5, obstacle, 0, .25f).IsWalkable(point));
        }

        [Test]
        public void TerrainHoleSmallerThanNavCellRejectsWholeAabbDespiteClearCenterAndCorners()
        {
            var blocked = new bool[64];
            blocked[1] = true; // [0.5,1] x [0,0.5], away from coarse center and corners.
            var surface = new TerrainSurface("hole", 1, 9, 9, Vector2.zero, new Vector2(4, 4), 80,
                new float[81], blocked);
            foreach (var point in new[] { Vector2.zero, new Vector2(2, 0), new Vector2(0, 2),
                new Vector2(2, 2), Vector2.one })
            {
                Assert.True(surface.TrySample(point, out var sample));
                Assert.True(sample.Walkable);
            }
            var grid = new TerrainNavigationGrid(surface, Vector2.zero, 2, 2, 2, 0, 0);
            Assert.False(grid.IsWalkable(Vector2.one));
            Assert.True(grid.IsWalkable(new Vector2(3, 3)));
        }

        [Test]
        public void SteepTriangleAwayFromNavCenterRejectsEntireNavCell()
        {
            var heights = new float[81];
            heights[10] = 4;
            var surface = new TerrainSurface("steep", 1, 9, 9, Vector2.zero, new Vector2(4, 4), 40,
                heights, new bool[64]);
            Assert.True(surface.TrySample(new Vector2(2, 2), out var center));
            Assert.True(center.Walkable);
            Assert.False(new TerrainNavigationGrid(surface, Vector2.zero, 4, 1, 1, 0, 0).IsWalkable(new Vector2(2, 2)));
        }

        [Test]
        public void ClearanceAlsoDilatesRawTerrainHoles()
        {
            var blocked = new bool[25];
            blocked[2 * 5 + 2] = true;
            var surface = new TerrainSurface("clearance", 1, 6, 6, Vector2.zero, new Vector2(5, 5), 40,
                new float[36], blocked);
            var origin = new Vector2(.25f, .25f);
            var without = new TerrainNavigationGrid(surface, origin, .5f, 9, 9, 0, 0);
            var with = new TerrainNavigationGrid(surface, origin, .5f, 9, 9, .3f, 0);
            Assert.True(without.IsWalkable(new Vector2(1.5f, 2.5f)));
            Assert.False(with.IsWalkable(new Vector2(1.5f, 2.5f)));
        }

        [Test]
        public void FootprintChecksInteriorBlockedCellMissedByCenterAndFourCorners()
        {
            var grid = Grid(7, 3, new[] { BlockCell(2, 1) });
            var center = new Vector2(3.5f, 1.5f);
            Assert.True(grid.IsWalkable(center));
            foreach (var corner in new[] { new Vector2(.5f, .5f), new Vector2(6.5f, .5f),
                new Vector2(.5f, 2.5f), new Vector2(6.5f, 2.5f) }) Assert.True(grid.IsWalkable(corner));
            Assert.False(grid.IsFootprintWalkable(center, new Vector2(6, 2)));
            Assert.True(grid.IsFootprintWalkable(new Vector2(5.5f, 1.5f), Vector2.one));
        }

        [Test]
        public void FootprintIncludesEdgeTouchAndDoesNotAddClearanceTwice()
        {
            var grid = Grid(6, 5, new[] { BlockCell(3, 2) }, .1f);
            var center = new Vector2(2.5f, 2.5f);
            Assert.True(grid.IsWalkable(center));
            Assert.True(grid.IsFootprintWalkable(center, new Vector2(.98f, .98f)));
            Assert.False(grid.IsFootprintWalkable(center, Vector2.one)); // xMax=3 touches blocked nav cell.
            Assert.True(grid.IsFootprintWalkable(center, Vector2.zero));
            Assert.False(grid.IsFootprintWalkable(center, new Vector2(-1, 1)));
            Assert.False(grid.IsFootprintWalkable(center, new Vector2(float.NaN, 1)));
            Assert.False(grid.IsFootprintWalkable(new Vector2(float.PositiveInfinity, 1), Vector2.one));
        }

        [Test]
        public void OriginRangeAndSurfaceBoundaryPaddingUseWorldSpace()
        {
            var origin = new Vector2(10, -5);
            var surface = Flat(6, 4, origin);
            var grid = new TerrainNavigationGrid(surface, origin, 1, 6, 4, .25f, .25f);
            Assert.AreSame(surface, grid.Surface);
            Assert.AreEqual(origin, grid.Origin);
            Assert.AreEqual(1, grid.CellSize);
            Assert.AreEqual(6, grid.ResolutionX);
            Assert.AreEqual(4, grid.ResolutionZ);
            Assert.AreEqual(24, grid.CellCount);
            Assert.AreEqual(.25f, grid.Clearance);
            Assert.True(grid.TryGetCell(origin, out int first));
            Assert.AreEqual(0, first);
            Assert.AreEqual(new Vector2(10.5f, -4.5f), grid.CellCenter(first));
            Assert.False(grid.IsWalkable(origin)); // Range succeeds even though radius/border rejects it.
            Assert.True(grid.IsWalkable(new Vector2(11.5f, -3.5f)));
            Assert.False(grid.TryGetCell(new Vector2(16, -3), out _));
            Assert.False(grid.TryGetCell(new Vector2(12, -1), out _));
            Assert.False(grid.TryGetCell(new Vector2(9.999f, -3), out _));
            Assert.False(grid.TryGetCell(new Vector2(12, -5.001f), out _));
            Assert.False(grid.TryGetCell(new Vector2(float.NaN, 0), out _));
            Assert.False(grid.TryGetCell(new Vector2(0, float.PositiveInfinity), out _));
            Assert.False(grid.IsFootprintWalkable(new Vector2(11, -3), new Vector2(3, 1)));
            Assert.Throws<ArgumentOutOfRangeException>(() => grid.CellCenter(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => grid.CellCenter(24));
        }

        [Test]
        public void FlowGrid768OverSurface720RejectsEveryPartiallyOrFullyOutsideCell()
        {
            var origin = new Vector2(-360, -360);
            var surface = new TerrainSurface("world", 1, 2, 2, origin, new Vector2(720, 720), 40,
                new float[4], new bool[1]);
            var grid = new TerrainNavigationGrid(surface, origin, 3, 256, 256, 0, 0);
            Assert.True(grid.IsWalkable(origin));
            Assert.True(grid.IsWalkable(new Vector2(358.5f, 358.5f)));
            Assert.True(grid.TryGetCell(new Vector2(370, 0), out _));
            Assert.False(grid.IsWalkable(new Vector2(370, 0)));
            Assert.False(grid.IsWalkable(new Vector2(360, 0)));
            Assert.False(grid.IsWalkable(new Vector2(0, 360)));
            Assert.False(grid.TryGetCell(new Vector2(408, 0), out _));
            var offsetGrid = new TerrainNavigationGrid(surface, origin + Vector2.one, 3, 240, 240, 0, 0);
            Assert.False(offsetGrid.IsWalkable(new Vector2(359, 0)), "Partially outside cell must not pass a center-only check");
        }

        [Test]
        public void MultipleTargetsUseThreeDimensionalPathCostInsteadOfNearestXZ()
        {
            var heights = new float[81];
            for (int z = 0; z <= 8; z++)
                for (int x = 0; x <= 8; x++) heights[z * 9 + x] = x * 3;
            var surface = new TerrainSurface("slope", 1, 9, 9, Vector2.zero, new Vector2(8, 8), 80, heights, new bool[64]);
            var grid = new TerrainNavigationGrid(surface, Vector2.zero, 1, 8, 8, 0, 0);
            var nearXZ = new Vector2(.5f, 1.5f); // 3m XZ but sqrt(90)m along slope.
            var cheap = new Vector2(3.5f, 5.5f); // 4m level traverse.
            var field = grid.CreateFlowField(new[] { nearXZ, cheap }, 0);
            Assert.AreEqual(Vector2.up, field[Cell(grid, new Vector2(3.5f, 1.5f))]);
            Assert.AreEqual(Vector2.zero, field[Cell(grid, nearXZ)]);
            Assert.AreEqual(Vector2.zero, field[Cell(grid, cheap)]);
        }

        [Test]
        public void EdgeCostFollowsIntermediateRidgeNotEndpointHeightChord()
        {
            var heights = new float[17 * 17];
            for (int z = 0; z < 17; z++) heights[z * 17 + 4] = 3; // Narrow ridge at x=2, between nav centers x=1,3.
            var surface = new TerrainSurface("ridge", 1, 17, 17, Vector2.zero, new Vector2(8, 8), 85, heights, new bool[256]);
            var grid = new TerrainNavigationGrid(surface, Vector2.zero, 2, 4, 4, 0, 0);
            var start = new Vector2(3, 3);
            var nearXZ = new Vector2(1, 3);
            var cheap = new Vector2(3, 7);
            surface.TrySample(start, out var a);
            surface.TrySample(nearXZ, out var b);
            Assert.AreEqual(a.Position.y, b.Position.y);
            Assert.True(grid.AreConnected(start, nearXZ));
            Assert.AreEqual(Vector2.up, grid.CreateFlowField(new[] { nearXZ, cheap }, 0)[Cell(grid, start)]);
        }

        [Test]
        public void StopRadiusUsesReachablePathDistanceNotProximityThroughWall()
        {
            var grid = WallWithGap();
            var target = new Vector2(5.5f, 2.5f);
            var field = grid.CreateFlowField(new[] { target }, 3);
            Assert.AreEqual(Vector2.zero, field[Cell(grid, target)]);
            Assert.AreEqual(Vector2.zero, field[Cell(grid, new Vector2(6.5f, 2.5f))]);
            Assert.AreNotEqual(Vector2.zero, field[Cell(grid, new Vector2(3.5f, 2.5f))]);
            var unitGrid = Grid(4, 1);
            var radiusOne = unitGrid.CreateFlowField(new[] { new Vector2(.5f, .5f) }, 1);
            Assert.AreEqual(Vector2.zero, radiusOne[1]);
            Assert.AreEqual(Vector2.left, radiusOne[2]);
        }

        [Test]
        public void SourceInputsAndReturnedArraysRemainReadOnlyIsolatedAcrossRebuilds()
        {
            var heights = new float[36];
            var blocked = new bool[25];
            var surface = new TerrainSurface("snapshot", 1, 6, 6, Vector2.zero, new Vector2(5, 5), 40, heights, blocked);
            var obstacles = new[] { BlockCell(2, 2) };
            var grid = new TerrainNavigationGrid(surface, Vector2.zero, 1, 5, 5, 0, 0, obstacles);
            uint[] mask = grid.CopyWalkable();
            mask[0] = 0;
            mask[12] = 1;
            heights[0] = 1000;
            blocked[0] = true;
            obstacles[0] = BlockCell(0, 0);
            Assert.AreEqual(0, surface.HeightAtVertex(0, 0));
            Assert.True(surface.IsCellWalkable(0, 0));
            Assert.True(grid.IsWalkable(new Vector2(.5f, .5f)));
            Assert.False(grid.IsWalkable(new Vector2(2.5f, 2.5f)));
            var targets = new[] { new Vector2(4.5f, 4.5f) };
            var first = grid.CreateFlowField(targets, 0);
            var expected = (Vector2[])first.Clone();
            first[0] = new Vector2(9, 9);
            targets[0] = new Vector2(.5f, .5f);
            var second = grid.CreateFlowField(targets, 0);
            Assert.AreNotSame(first, second);
            var restored = grid.CreateFlowField(new[] { new Vector2(4.5f, 4.5f) }, 0);
            CollectionAssert.AreEqual(expected, restored);
            Assert.AreEqual(new Vector2(9, 9), first[0]);
            Assert.AreEqual(Vector2.zero, second[0]);
        }

        [TestCase(1)]
        [TestCase(7)]
        [TestCase(42)]
        public void FlatFieldMatchesIndependentQuadraticDijkstraOracle(int seed)
        {
            const int width = 12, depth = 10;
            var random = new System.Random(seed);
            var obstacles = new List<StaticObstacleRect>();
            for (int z = 0; z < depth; z++)
                for (int x = 0; x < width; x++)
                    if (random.Next(4) == 0) obstacles.Add(BlockCell(x, z));
            var grid = Grid(width, depth, obstacles.ToArray());
            var targets = new[] { new Vector2(.5f, .5f), new Vector2(10.5f, 7.5f), new Vector2(4.5f, 5.5f) };
            var field = grid.CreateFlowField(targets, 0);
            var mask = grid.CopyWalkable();
            var distance = new double[mask.Length];
            var settled = new bool[mask.Length];
            for (int cell = 0; cell < distance.Length; cell++) distance[cell] = double.PositiveInfinity;
            foreach (var target in targets)
            {
                int targetCell = Cell(grid, target);
                if (mask[targetCell] != 0) distance[targetCell] = 0;
            }
            for (int iteration = 0; iteration < distance.Length; iteration++)
            {
                int current = -1;
                for (int cell = 0; cell < distance.Length; cell++)
                    if (!settled[cell] && !double.IsInfinity(distance[cell]) &&
                        (current < 0 || distance[cell] < distance[current])) current = cell;
                if (current < 0) break;
                settled[current] = true;
                int x = current % width, z = current / width;
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, nz = z + dz;
                        if ((dx == 0 && dz == 0) || nx < 0 || nz < 0 || nx >= width || nz >= depth) continue;
                        int next = nz * width + nx;
                        if (mask[next] == 0 || (dx != 0 && dz != 0 &&
                            (mask[z * width + nx] == 0 || mask[nz * width + x] == 0))) continue;
                        distance[next] = Math.Min(distance[next], distance[current] + Math.Sqrt(dx * dx + dz * dz));
                    }
            }
            for (int cell = 0; cell < distance.Length; cell++)
            {
                if (double.IsInfinity(distance[cell]) || distance[cell] == 0)
                    Assert.AreEqual(Vector2.zero, field[cell]);
                else
                {
                    int next = Next(grid, field, cell);
                    double edge = Vector2.Distance(grid.CellCenter(cell), grid.CellCenter(next));
                    Assert.That(distance[next] + edge, Is.EqualTo(distance[cell]).Within(1e-5), "Non-optimal flow at " + cell);
                }
            }
        }

        [Test]
        public void InvalidConstructorAndRadiusInputsAreRejected()
        {
            var surface = Flat(3, 3);
            Assert.Throws<ArgumentNullException>(() => new TerrainNavigationGrid(null, Vector2.zero, 1, 3, 3, 0, 0));
            Assert.Throws<ArgumentException>(() => new TerrainNavigationGrid(surface, Vector2.zero, 0, 3, 3, 0, 0));
            Assert.Throws<ArgumentException>(() => new TerrainNavigationGrid(surface, Vector2.zero, 1, 0, 3, 0, 0));
            Assert.Throws<ArgumentException>(() => new TerrainNavigationGrid(surface, Vector2.zero, 1, int.MaxValue, 3, 0, 0));
            Assert.Throws<ArgumentException>(() => new TerrainNavigationGrid(surface, new Vector2(float.NaN, 0), 1, 3, 3, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new TerrainNavigationGrid(surface, Vector2.zero, 1, 3, 3, -1, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new TerrainNavigationGrid(surface, Vector2.zero, 1, 3, 3, 0, float.PositiveInfinity));
            Assert.Throws<ArgumentOutOfRangeException>(() => Grid(3, 3, obstaclePadding: float.NaN));
            Assert.Throws<ArgumentException>(() => Grid(3, 3, new[] { default(StaticObstacleRect) }));
            Assert.Throws<ArgumentOutOfRangeException>(() => Grid(3, 3).CreateFlowField(null, -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => Grid(3, 3).CreateFlowField(null, float.NaN));
        }
    }
}

namespace MassEngine
{
    /// <summary>
    /// Immutable, conservative XZ navigation over a TerrainSurface snapshot. A nav cell is
    /// usable only when its whole closed AABB, expanded by Clearance, is usable. Touching
    /// blocked terrain/obstacles counts as blocked; boundaryPadding additionally insets the
    /// surface's world bounds. Coarse cells can therefore reject otherwise usable narrow paths.
    /// </summary>
    /// <remarks>
    /// Flow vectors encode ONE next neighbour, not a continuously integrable velocity. Decode
    /// its signed X/Z offset, then steer from the agent's actual position to that neighbour's
    /// CellCenter. All diagonal edges require both side cells, so this segment stays in usable
    /// cells even when the agent is off-center. Do not bilinearly interpolate vectors, overshoot
    /// the neighbour center, or skip cells in a large movement step. Collision/flocking/knockback
    /// still need their own swept movement validation. Zero means stop, never direct-target fallback.
    /// CreateFlowField reuses work arrays and is deliberately not concurrent/reentrant.
    /// </remarks>
    public sealed class P3OriginalNavigationGrid
    {
        public TerrainSurface Surface { get; }
        public Vector2 Origin { get; }
        public float CellSize { get; }
        public int ResolutionX { get; }
        public int ResolutionZ { get; }
        public int CellCount { get; }
        public float Clearance { get; }

        // First four edges are stored once; the last four are their reverses.
        private static readonly int[] StepX = { 1, 0, 1, -1, -1, 0, -1, 1 };
        private static readonly int[] StepZ = { 0, 1, 1, 1, 0, -1, -1, -1 };
        private readonly uint[] walkable;
        private readonly int[] components;
        private readonly byte[] neighbours;
        private readonly double[] edgeCosts;
        private readonly double[] distances;
        private readonly int[] nextCells;
        private readonly int[] heap;
        private readonly int[] heapPositions;
        private readonly double maximumX;
        private readonly double maximumZ;
        private int heapCount;

        public P3OriginalNavigationGrid(TerrainSurface surface, Vector2 origin, float cellSize,
            int resolutionX, int resolutionZ, float clearance, float boundaryPadding,
            StaticObstacleRect[] obstacles = null, float obstaclePadding = 0)
        {
            if (surface == null) throw new ArgumentNullException(nameof(surface));
            if (!Finite(origin) || !P3ReferenceFinite(cellSize) || cellSize <= 0 ||
                resolutionX <= 0 || resolutionZ <= 0 || (long)resolutionX * resolutionZ > int.MaxValue / 4)
                throw new ArgumentException("Navigation bounds must be finite and dimensions positive.");
            ValidatePadding(clearance, nameof(clearance));
            ValidatePadding(boundaryPadding, nameof(boundaryPadding));
            ValidatePadding(obstaclePadding, nameof(obstaclePadding));
            maximumX = origin.x + (double)cellSize * resolutionX;
            maximumZ = origin.y + (double)cellSize * resolutionZ;
            if (maximumX > float.MaxValue || maximumZ > float.MaxValue ||
                (float)maximumX <= origin.x || (float)maximumZ <= origin.y ||
                (float)(origin.x + (double)cellSize * .5) <= origin.x ||
                (float)(origin.y + (double)cellSize * .5) <= origin.y ||
                (float)(maximumX - cellSize * .5) >= (float)maximumX ||
                (float)(maximumZ - cellSize * .5) >= (float)maximumZ)
                throw new ArgumentException("Navigation cells must have representable centers and bounds.");

            var obstacleSnapshot = obstacles == null ? Array.Empty<StaticObstacleRect>() :
                (StaticObstacleRect[])obstacles.Clone();
            foreach (var obstacle in obstacleSnapshot)
                if (!obstacle.IsValid) throw new ArgumentException("Navigation obstacles must be valid rectangles.", nameof(obstacles));

            Surface = surface;
            Origin = origin;
            CellSize = cellSize;
            ResolutionX = resolutionX;
            ResolutionZ = resolutionZ;
            CellCount = resolutionX * resolutionZ;
            Clearance = clearance;
            walkable = new uint[CellCount];
            components = new int[CellCount];
            neighbours = new byte[CellCount];
            edgeCosts = new double[CellCount * 4];
            distances = new double[CellCount];
            nextCells = new int[CellCount];
            heap = new int[CellCount];
            heapPositions = new int[CellCount];

            for (int z = 0; z < resolutionZ; z++)
                for (int x = 0; x < resolutionX; x++)
                    walkable[z * resolutionX + x] = CoversUsableSurface(x, z, boundaryPadding,
                        obstacleSnapshot, obstaclePadding) ? 1u : 0u;
            BuildEdges();
            BuildComponents();
        }

        /// <summary>Every step must be an existing legal cardinal graph edge, including terrain/obstacle clearance.</summary>
        public bool HasClearCardinalRoute33(int from,int to)
        {
            if(from<0||to<0||from>=CellCount||to>=CellCount||walkable[from]==0||walkable[to]==0)return false;
            if(from==to)return true;
            int step,bit;
            if(from/ResolutionX==to/ResolutionX){step=to>from?1:-1;bit=step>0?0:4;}
            else if(from%ResolutionX==to%ResolutionX){step=to>from?ResolutionX:-ResolutionX;bit=step>0?1:5;}
            else return false;
            for(int cell=from;cell!=to;cell+=step)if((neighbours[cell]&(1<<bit))==0)return false;
            return true;
        }

        public uint[] CopyWalkable() => (uint[])walkable.Clone();

        /// <summary>Range only, including blocked cells. Positive grid edges are exclusive.</summary>
        public bool TryGetCell(Vector2 position, out int cell)
        {
            cell = -1;
            if (!Finite(position) || position.x < Origin.x || position.y < Origin.y ||
                position.x >= maximumX || position.y >= maximumZ) return false;
            int x = (int)Math.Floor(((double)position.x - Origin.x) / CellSize);
            int z = (int)Math.Floor(((double)position.y - Origin.y) / CellSize);
            cell = z * ResolutionX + x;
            return true;
        }

        public bool IsWalkable(Vector2 position) => TryGetCell(position, out int cell) && walkable[cell] != 0;

        /// <summary>
        /// Checks every nav cell touched by the complete closed footprint (not just sample points).
        /// Size is the full XZ extent, may be zero, and must be finite/nonnegative. Clearance is
        /// already in the nav mask and is NOT applied a second time here.
        /// </summary>
        public bool IsFootprintWalkable(Vector2 center, Vector2 size)
        {
            if (!Finite(center) || !Finite(size) || size.x < 0 || size.y < 0) return false;
            double minX = center.x - (double)size.x * .5, maxX = center.x + (double)size.x * .5;
            double minZ = center.y - (double)size.y * .5, maxZ = center.y + (double)size.y * .5;
            if (minX < Origin.x || minZ < Origin.y || maxX > maximumX || maxZ > maximumZ) return false;
            ClosedCellRange(minX, maxX, Origin.x, CellSize, ResolutionX, out int x0, out int x1);
            ClosedCellRange(minZ, maxZ, Origin.y, CellSize, ResolutionZ, out int z0, out int z1);
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                    if (walkable[z * ResolutionX + x] == 0) return false;
            return true;
        }

        public bool AreConnected(Vector2 a, Vector2 b)
        {
            int component = ComponentAt(a);
            return component >= 0 && component == ComponentAt(b);
        }

        /// <summary>Returns a stable zero-based component id, or -1 for blocked/outside positions.</summary>
        public int ComponentAt(Vector2 position) => TryGetCell(position, out int cell) ? components[cell] : -1;

        public Vector2 CellCenter(int cell)
        {
            if (cell < 0 || cell >= CellCount) throw new ArgumentOutOfRangeException(nameof(cell));
            return new Vector2((float)(Origin.x + (cell % ResolutionX + .5) * CellSize),
                (float)(Origin.y + (cell / ResolutionX + .5) * CellSize));
        }

        /// <summary>
        /// Multi-source, indexed-heap Dijkstra using 3D length along the piecewise triangular
        /// surface. Invalid/outside/blocked targets are ignored, never projected. Target cells,
        /// unreachable cells, and cells at path distance &lt;= stopRadius return zero. The radius
        /// uses path distance to target CELL centers, not Euclidean proximity through a wall.
        /// Each returned array is independent; only that result allocation occurs per call.
        /// </summary>
        public Vector2[] CreateFlowField(IReadOnlyList<Vector2> targets, float stopRadius)
        {
            ValidatePadding(stopRadius, nameof(stopRadius));
            var result = new Vector2[CellCount];
            heapCount = 0;
            for (int cell = 0; cell < CellCount; cell++)
            {
                distances[cell] = double.PositiveInfinity;
                nextCells[cell] = -1;
                heapPositions[cell] = -1;
            }
            if (targets == null) return result;
            for (int i = 0; i < targets.Count; i++)
            {
                if (!TryGetCell(targets[i], out int target) || walkable[target] == 0 || distances[target] == 0) continue;
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
                Vector2 delta = CellCenter(next) - CellCenter(cell);
                double length = Math.Sqrt((double)delta.x * delta.x + (double)delta.y * delta.y);
                result[cell] = new Vector2((float)(delta.x / length), (float)(delta.y / length));
            }
            return result;
        }

        private bool CoversUsableSurface(int x, int z, float boundaryPadding,
            StaticObstacleRect[] obstacles, float obstaclePadding)
        {
            double minX = Origin.x + (double)x * CellSize - Clearance;
            double maxX = Origin.x + (double)(x + 1) * CellSize + Clearance;
            double minZ = Origin.y + (double)z * CellSize - Clearance;
            double maxZ = Origin.y + (double)(z + 1) * CellSize + Clearance;
            if (minX < (double)Surface.Origin.x + boundaryPadding || minZ < (double)Surface.Origin.y + boundaryPadding ||
                maxX > (double)Surface.Origin.x + Surface.Size.x - boundaryPadding ||
                maxZ > (double)Surface.Origin.y + Surface.Size.y - boundaryPadding) return false;

            // Enumerate ALL underlying source cells, including a blocked cell touched at an edge.
            ClosedCellRange(minX, maxX, Surface.Origin.x, Surface.Step.x, Surface.Width - 1, out int x0, out int x1);
            ClosedCellRange(minZ, maxZ, Surface.Origin.y, Surface.Step.y, Surface.Depth - 1, out int z0, out int z1);
            for (int sz = z0; sz <= z1; sz++)
                for (int sx = x0; sx <= x1; sx++)
                    if (!Surface.IsCellWalkable(sx, sz)) return false;
            foreach (var obstacle in obstacles)
            {
                double halfX = (double)obstacle.size.x * .5 + obstaclePadding;
                double halfZ = (double)obstacle.size.y * .5 + obstaclePadding;
                if (maxX >= obstacle.center.x - halfX && minX <= obstacle.center.x + halfX &&
                    maxZ >= obstacle.center.y - halfZ && minZ <= obstacle.center.y + halfZ) return false;
            }
            return true;
        }

        private void BuildEdges()
        {
            for (int cell = 0; cell < CellCount; cell++)
            {
                if (walkable[cell] == 0) continue;
                int x = cell % ResolutionX, z = cell / ResolutionX;
                for (int direction = 0; direction < 4; direction++)
                {
                    int nx = x + StepX[direction], nz = z + StepZ[direction];
                    if (nx < 0 || nz < 0 || nx >= ResolutionX || nz >= ResolutionZ) continue;
                    int adjacent = nz * ResolutionX + nx;
                    if (walkable[adjacent] == 0) continue;
                    if (StepX[direction] != 0 && StepZ[direction] != 0 &&
                        (walkable[z * ResolutionX + nx] == 0 || walkable[nz * ResolutionX + x] == 0)) continue;
                    double cost = SurfaceEdgeLength(CellCenter(cell), CellCenter(adjacent));
                    if (double.IsInfinity(cost) || double.IsNaN(cost) || cost <= 0) continue;
                    edgeCosts[cell * 4 + direction] = cost;
                    neighbours[cell] |= (byte)(1 << direction);
                    neighbours[adjacent] |= (byte)(1 << (direction + 4));
                }
            }
        }

        // Split at every terrain grid line, then at that cell's fixed lower-left/upper-right
        // diagonal. Midpoint gradients come from TrySample, not a second height/normal formula.
        // Merely measuring endpoint height difference would miss ridges between nav centers.
        private double SurfaceEdgeLength(Vector2 start, Vector2 end)
        {
            double sx = ((double)start.x - Surface.Origin.x) / Surface.Step.x;
            double sz = ((double)start.y - Surface.Origin.y) / Surface.Step.y;
            double dx = ((double)end.x - start.x) / Surface.Step.x;
            double dz = ((double)end.y - start.y) / Surface.Step.y;
            double deltaX = (double)end.x - start.x, deltaZ = (double)end.y - start.y;
            double horizontalSquared = deltaX * deltaX + deltaZ * deltaZ;
            double nextX = dx == 0 ? double.PositiveInfinity :
                ((dx > 0 ? Math.Floor(sx) + 1 : Math.Ceiling(sx) - 1) - sx) / dx;
            double nextZ = dz == 0 ? double.PositiveInfinity :
                ((dz > 0 ? Math.Floor(sz) + 1 : Math.Ceiling(sz) - 1) - sz) / dz;
            double stepX = dx == 0 ? double.PositiveInfinity : 1 / Math.Abs(dx);
            double stepZ = dz == 0 ? double.PositiveInfinity : 1 / Math.Abs(dz);
            double t = 0, length = 0;
            while (t < 1)
            {
                double finish = Math.Min(1, Math.Min(nextX, nextZ));
                double middle = (t + finish) * .5;
                double diagonal = dx == dz ? double.PositiveInfinity :
                    (Math.Floor(sx + dx * middle) - Math.Floor(sz + dz * middle) - sx + sz) / (dx - dz);
                if (diagonal > t && diagonal < finish)
                {
                    length += FaceLength(start, deltaX, deltaZ, horizontalSquared, t, diagonal);
                    length += FaceLength(start, deltaX, deltaZ, horizontalSquared, diagonal, finish);
                }
                else length += FaceLength(start, deltaX, deltaZ, horizontalSquared, t, finish);
                t = finish;
                if (nextX <= finish) nextX += stepX;
                if (nextZ <= finish) nextZ += stepZ;
            }
            return length;
        }

        private double FaceLength(Vector2 start, double dx, double dz, double horizontalSquared, double from, double to)
        {
            double middle = (from + to) * .5;
            var point = new Vector2((float)(start.x + dx * middle), (float)(start.y + dz * middle));
            if (!Surface.TrySample(point, out var sample)) return double.PositiveInfinity;
            double dy = sample.Gradient.x * dx + sample.Gradient.y * dz;
            return (to - from) * Math.Sqrt(horizontalSquared + dy * dy);
        }

        private void BuildComponents()
        {
            for (int cell = 0; cell < CellCount; cell++) components[cell] = -1;
            int component = 0;
            for (int seed = 0; seed < CellCount; seed++)
            {
                if (walkable[seed] == 0 || components[seed] >= 0) continue;
                int read = 0, count = 1;
                heap[0] = seed; // Constructor-only flood fill reuses the later Dijkstra heap storage.
                components[seed] = component;
                while (read < count)
                {
                    int cell = heap[read++];
                    for (int direction = 0; direction < 8; direction++)
                    {
                        if ((neighbours[cell] & (1 << direction)) == 0) continue;
                        int adjacent = cell + StepZ[direction] * ResolutionX + StepX[direction];
                        if (components[adjacent] >= 0) continue;
                        components[adjacent] = component;
                        heap[count++] = adjacent;
                    }
                }
                component++;
            }
        }

        private void PushOrDecrease(int cell)
        {
            int position = heapPositions[cell];
            if (position < 0) position = heapCount++;
            while (position > 0)
            {
                int parent = (position - 1) / 2;
                int parentCell = heap[parent];
                if (!Before(cell, parentCell)) break;
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
            int position = 0;
            while (position * 2 + 1 < heapCount)
            {
                int child = position * 2 + 1;
                if (child + 1 < heapCount && Before(heap[child + 1], heap[child])) child++;
                if (!Before(heap[child], last)) break;
                heap[position] = heap[child];
                heapPositions[heap[position]] = position;
                position = child;
            }
            heap[position] = last;
            heapPositions[last] = position;
            return minimum;
        }

        private bool Before(int a, int b) => distances[a] < distances[b] ||
            (distances[a] == distances[b] && a < b);

        private static void ClosedCellRange(double minimum, double maximum, double origin, double step,
            int count, out int first, out int last)
        {
            first = Math.Max(0, Math.Min(count - 1, (int)Math.Ceiling((minimum - origin) / step) - 1));
            last = Math.Max(0, Math.Min(count - 1, (int)Math.Floor((maximum - origin) / step)));
        }

        private static bool P3ReferenceFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private static bool Finite(Vector2 value) => P3ReferenceFinite(value.x) && P3ReferenceFinite(value.y);

        private static void ValidatePadding(float value, string name)
        {
            if (!P3ReferenceFinite(value) || value < 0) throw new ArgumentOutOfRangeException(name);
        }
    }
}


namespace MassEngine.Tests
{
    public sealed class P3UniformOutputTests
    {
        private static readonly string Root = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, ".."));
        private static readonly string Capture = System.IO.Path.Combine(Root, "Logs/InteractionRefinement-20261007/P3/fixed-input-20261008-01-capture");
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Explicit)]
        private struct Bits
        {
            [System.Runtime.InteropServices.FieldOffset(0)] public float value;
            [System.Runtime.InteropServices.FieldOffset(0)] public uint bits;
        }
        private static void Exact(Vector2[] expected, Vector2[] actual)
        {
            Assert.AreEqual(expected.Length, actual.Length);
            for (int i = 0; i < expected.Length; i++)
                if (new Bits { value = expected[i].x }.bits != new Bits { value = actual[i].x }.bits ||
                    new Bits { value = expected[i].y }.bits != new Bits { value = actual[i].y }.bits)
                    Assert.Fail("Different float bits at cell " + i);
        }
        private static void Set(object instance, string field, object value)
        {
            var f = instance.GetType().GetField(field, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.NotNull(f, field); f.SetValue(instance, value);
        }
        private static object Hydrate(System.Type type)
        {
            // Test-only hydration of the exact recorded graph; no reconstructed terrain or changed costs.
            object grid = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(type);
            using (var reader = new System.IO.BinaryReader(System.IO.File.OpenRead(System.IO.Path.Combine(Capture, "fixed-graph-0.bin"))))
            {
                Assert.AreEqual("P3_FIXED_GRAPH_V1", reader.ReadString());
                int rx = reader.ReadInt32(), rz = reader.ReadInt32(); float size = reader.ReadSingle();
                var origin = new Vector2(reader.ReadSingle(), reader.ReadSingle());
                double mx = reader.ReadDouble(), mz = reader.ReadDouble(); float clearance = reader.ReadSingle();
                int n = reader.ReadInt32(); Assert.AreEqual(rx * rz, n);
                Set(grid, "<ResolutionX>k__BackingField", rx); Set(grid, "<ResolutionZ>k__BackingField", rz);
                Set(grid, "<CellSize>k__BackingField", size); Set(grid, "<Origin>k__BackingField", origin);
                Set(grid, "<CellCount>k__BackingField", n); Set(grid, "<Clearance>k__BackingField", clearance);
                Set(grid, "maximumX", mx); Set(grid, "maximumZ", mz);
                var walkable = new uint[n]; for (int i = 0; i < n; i++) walkable[i] = reader.ReadUInt32();
                var neighbours = reader.ReadBytes(n); Assert.AreEqual(n, neighbours.Length);
                var costs = new double[n * 4]; for (int i = 0; i < costs.Length; i++) costs[i] = reader.ReadDouble();
                Assert.AreEqual(reader.BaseStream.Length, reader.BaseStream.Position);
                Set(grid, "walkable", walkable); Set(grid, "neighbours", neighbours); Set(grid, "edgeCosts", costs);
                Set(grid, "distances", new double[n]); Set(grid, "nextCells", new int[n]);
                Set(grid, "heap", new int[n]); Set(grid, "heapPositions", new int[n]); Set(grid, "components", new int[n]);
            }
            type.GetMethod("PrepareCellCenterAxes", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.Invoke(grid, null);
            type.GetMethod("PrepareUniformOutputDirections", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.Invoke(grid, null);
            return grid;
        }
        [Serializable] private sealed class Row
        {
            public bool candidateFirst; public double referenceMs, candidateMs;
            public long referenceAllocatedRaw, candidateAllocatedRaw;
        }
        [Serializable] private sealed class Report
        {
            public int sample, cells, targetCount; public float stopRadius;
            public bool exactBits, allocationCounterAvailable, uniformTableActive;
            public long canaryReportedBytes; public int canaryPayloadBytes = 1048576;
            public string scope = "Fixed recorded graph, original ordered goals and radius. Original frozen class versus current production class. Test-only graph hydration; no rendered frame-time claims.";
            public Row[] rows;
        }
        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)] [TestCase(5)]
        public void RecordedInputsAreBitExactAndBenchmark(int sample)
        {
            string output = null;
            foreach (string arg in Environment.GetCommandLineArgs())
                if (arg.StartsWith("--interaction-p8-output=", StringComparison.Ordinal)) output = arg.Substring("--interaction-p8-output=".Length);
            Assert.IsNotNull(output, "Run via isolated test runner");
            Vector2[] goals, expected; float radius;
            using (var reader = new System.IO.BinaryReader(System.IO.File.OpenRead(System.IO.Path.Combine(Capture, "fixed-sample-" + sample + "-input.bin"))))
            {
                Assert.AreEqual("P3_FIXED_INPUT_V1", reader.ReadString()); radius = reader.ReadSingle();
                goals = new Vector2[reader.ReadInt32()];
                for (int i = 0; i < goals.Length; i++) goals[i] = new Vector2(reader.ReadSingle(), reader.ReadSingle());
                Assert.AreEqual(reader.BaseStream.Length, reader.BaseStream.Position);
            }
            using (var reader = new System.IO.BinaryReader(System.IO.File.OpenRead(System.IO.Path.Combine(Capture, "fixed-sample-" + sample + "-expected.bin"))))
            {
                expected = new Vector2[reader.ReadInt32()];
                for (int i = 0; i < expected.Length; i++) expected[i] = new Vector2(reader.ReadSingle(), reader.ReadSingle());
                Assert.AreEqual(reader.BaseStream.Length, reader.BaseStream.Position);
            }
            var reference = (P3OriginalNavigationGrid)Hydrate(typeof(P3OriginalNavigationGrid));
            var candidate = (TerrainNavigationGrid)Hydrate(typeof(TerrainNavigationGrid));
            for (int i = 0; i < 3; i++) { Exact(expected, reference.CreateFlowField(goals, radius)); Exact(expected, candidate.CreateFlowField(goals, radius)); }
            long before = GC.GetAllocatedBytesForCurrentThread();
            var canary = new byte[1048576]; canary[0] = 1; canary[canary.Length-1] = 2;
            long canaryBytes = GC.GetAllocatedBytesForCurrentThread() - before; GC.KeepAlive(canary);
            var field = typeof(TerrainNavigationGrid).GetField("uniformOutputDirections", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var report = new Report { sample = sample, cells = expected.Length, targetCount = goals.Length, stopRadius = radius,
                exactBits = true, canaryReportedBytes = canaryBytes, allocationCounterAvailable = canaryBytes >= canary.Length,
                uniformTableActive = field != null && field.GetValue(candidate) != null, rows = new Row[12] };
            double toMs = 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            for (int pair = 0; pair < report.rows.Length; pair++)
            {
                var row = new Row { candidateFirst = pair % 2 != 0 };
                Vector2[] a = null, b = null;
                for (int pass = 0; pass < 2; pass++)
                {
                    bool current = row.candidateFirst ? pass == 0 : pass == 1;
                    long allocation = GC.GetAllocatedBytesForCurrentThread(), start = System.Diagnostics.Stopwatch.GetTimestamp();
                    var result = current ? candidate.CreateFlowField(goals, radius) : reference.CreateFlowField(goals, radius);
                    double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - start) * toMs;
                    long bytes = GC.GetAllocatedBytesForCurrentThread() - allocation;
                    if (current) { b = result; row.candidateMs = ms; row.candidateAllocatedRaw = bytes; }
                    else { a = result; row.referenceMs = ms; row.referenceAllocatedRaw = bytes; }
                }
                Exact(expected, a); Exact(expected, b); report.rows[pair] = row;
            }
            System.IO.File.WriteAllText(System.IO.Path.Combine(output, "output-fixed-" + sample + ".json"), JsonUtility.ToJson(report, true));
        }
        [TestCase(0f, 1f, 12, 10)] [TestCase(0f, 3f, 12, 10)]
        [TestCase(-360f, 3f, 12, 10)] [TestCase(100000f, .1f, 12, 10)]
        [TestCase(.03f, .1f, 12, 10)] [TestCase(-100000f, .2f, 12, 10)]
        [TestCase(0f, 1f, 1, 10)] [TestCase(0f, 1f, 12, 1)]
        public void ConstructedGraphsKeepExactDirectionsIncludingRoundedSpacing(float originValue, float step, int rx, int rz)
        {
            var origin = new Vector2(originValue, -originValue);
            var size = new Vector2(rx * step, rz * step);
            var surface = new TerrainSurface("output-exact", 1, 2, 2, origin, size, 80, new float[4], new bool[1]);
            var current = new TerrainNavigationGrid(surface, origin, step, rx, rz, 0, 0);
            var reference = new P3OriginalNavigationGrid(surface, origin, step, rx, rz, 0, 0);
            var goals = new[] { current.CellCenter(0), current.CellCenter(rx*rz-1), current.CellCenter((rx*rz)/2) };
            foreach (float radius in new[] { 0f, step, step*3 })
            {
                Exact(reference.CreateFlowField(goals, radius), current.CreateFlowField(goals, radius));
                Exact(reference.CreateFlowField(new[] { goals[2], goals[2] }, radius), current.CreateFlowField(new[] { goals[2], goals[2] }, radius));
            }
            Exact(reference.CreateFlowField(null, 0), current.CreateFlowField(null, 0));
            Exact(reference.CreateFlowField(new Vector2[0], 0), current.CreateFlowField(new Vector2[0], 0));
            Exact(reference.CreateFlowField(new[] { new Vector2(float.NaN, 0), new Vector2(float.PositiveInfinity, 0) }, 0),
                current.CreateFlowField(new[] { new Vector2(float.NaN, 0), new Vector2(float.PositiveInfinity, 0) }, 0));
            var f = typeof(TerrainNavigationGrid).GetField("uniformOutputDirections", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (f != null && originValue == 100000f && step == .1f) Assert.IsNull(f.GetValue(current), "Rounded spacing must fall back");
        }
    }
}

namespace MassEngine {
 /// <summary>Opt-in dynamic-enemy approach. Only substitutes a clear cardinal route to a REAL target in the same lane; never makes synthetic goals.</summary>
 public static class P3LaneReference {
  public static void Apply(P3OriginalNavigationGrid nav,IReadOnlyList<Vector2> goals,Vector2[] directions,float stopRadius){
   if(goals==null||goals.Count==0)return;
   var rows=new List<int>[nav.ResolutionZ];var columns=new List<int>[nav.ResolutionX];Vector2 mean=Vector2.zero;int total=0;
   foreach(var p in goals){if(!nav.TryGetCell(p,out int cell)||!nav.IsWalkable(p))continue;int x=cell%nav.ResolutionX,z=cell/nav.ResolutionX;if(rows[z]==null)rows[z]=new List<int>();if(columns[x]==null)columns[x]=new List<int>();rows[z].Add(cell);columns[x].Add(cell);mean+=nav.CellCenter(cell);total++;}
   if(total==0)return;mean/=total;foreach(var list in rows)list?.Sort();foreach(var list in columns)list?.Sort();
   float near=Mathf.Max(nav.CellSize*4,stopRadius+nav.CellSize);
   for(int cell=0;cell<directions.Length;cell++){
    Vector2 original=directions[cell];if(original.sqrMagnitude<.0001f)continue; // Never override stop/unreachable.
    Vector2 here=nav.CellCenter(cell),toward=mean-here;bool horizontal=Mathf.Abs(toward.x)>=Mathf.Abs(toward.y);int sign=(horizontal?toward.x:toward.y)>=0?1:-1;
    Vector2 proposed=horizontal?new Vector2(sign,0):new Vector2(0,sign);if(Vector2.Dot(proposed,original)<.5f)continue;
    var list=horizontal?rows[cell/nav.ResolutionX]:columns[cell%nav.ResolutionX];if(list==null)continue;int at=list.BinarySearch(cell);if(at<0)at=~at;else continue;int pick=sign>0?at:at-1;if(pick<0||pick>=list.Count)continue;int target=list[pick];
    if(Vector2.Distance(here,nav.CellCenter(target))<near||!nav.HasClearCardinalRoute33(cell,target))continue;
    directions[cell]=proposed;
   }
  }
 }
}


namespace MassEngine {
 /// <summary>Opt-in dynamic-enemy approach. Only substitutes a clear cardinal route to a REAL target in the same lane; never makes synthetic goals.</summary>
 public static class P3LaneSameGridReference {
  public static void Apply(TerrainNavigationGrid nav,IReadOnlyList<Vector2> goals,Vector2[] directions,float stopRadius){
   if(goals==null||goals.Count==0)return;
   var rows=new List<int>[nav.ResolutionZ];var columns=new List<int>[nav.ResolutionX];Vector2 mean=Vector2.zero;int total=0;
   foreach(var p in goals){if(!nav.TryGetCell(p,out int cell)||!nav.IsWalkable(p))continue;int x=cell%nav.ResolutionX,z=cell/nav.ResolutionX;if(rows[z]==null)rows[z]=new List<int>();if(columns[x]==null)columns[x]=new List<int>();rows[z].Add(cell);columns[x].Add(cell);mean+=nav.CellCenter(cell);total++;}
   if(total==0)return;mean/=total;foreach(var list in rows)list?.Sort();foreach(var list in columns)list?.Sort();
   float near=Mathf.Max(nav.CellSize*4,stopRadius+nav.CellSize);
   for(int cell=0;cell<directions.Length;cell++){
    Vector2 original=directions[cell];if(original.sqrMagnitude<.0001f)continue; // Never override stop/unreachable.
    Vector2 here=nav.CellCenter(cell),toward=mean-here;bool horizontal=Mathf.Abs(toward.x)>=Mathf.Abs(toward.y);int sign=(horizontal?toward.x:toward.y)>=0?1:-1;
    Vector2 proposed=horizontal?new Vector2(sign,0):new Vector2(0,sign);if(Vector2.Dot(proposed,original)<.5f)continue;
    var list=horizontal?rows[cell/nav.ResolutionX]:columns[cell%nav.ResolutionX];if(list==null)continue;int at=list.BinarySearch(cell);if(at<0)at=~at;else continue;int pick=sign>0?at:at-1;if(pick<0||pick>=list.Count)continue;int target=list[pick];
    if(Vector2.Distance(here,nav.CellCenter(target))<near||!nav.HasClearCardinalRoute33(cell,target))continue;
    directions[cell]=proposed;
   }
  }
 }
}


namespace MassEngine {
 /// <summary>Opt-in dynamic-enemy approach. Only substitutes a clear cardinal route to a REAL target in the same lane; never makes synthetic goals.</summary>
 public static class P3LaneCoarseProfile {
  public static long BuildTicks, SortTicks, ScanTicks;
  public static void Apply(P3OriginalNavigationGrid nav,IReadOnlyList<Vector2> goals,Vector2[] directions,float stopRadius){
   if(goals==null||goals.Count==0)return;
   long t0=System.Diagnostics.Stopwatch.GetTimestamp();
   var rows=new List<int>[nav.ResolutionZ];var columns=new List<int>[nav.ResolutionX];Vector2 mean=Vector2.zero;int total=0;
   foreach(var p in goals){if(!nav.TryGetCell(p,out int cell)||!nav.IsWalkable(p))continue;int x=cell%nav.ResolutionX,z=cell/nav.ResolutionX;if(rows[z]==null)rows[z]=new List<int>();if(columns[x]==null)columns[x]=new List<int>();rows[z].Add(cell);columns[x].Add(cell);mean+=nav.CellCenter(cell);total++;}
   if(total==0)return;mean/=total;long t1=System.Diagnostics.Stopwatch.GetTimestamp();foreach(var list in rows)list?.Sort();foreach(var list in columns)list?.Sort();long t2=System.Diagnostics.Stopwatch.GetTimestamp();
   float near=Mathf.Max(nav.CellSize*4,stopRadius+nav.CellSize);
   for(int cell=0;cell<directions.Length;cell++){
    Vector2 original=directions[cell];if(original.sqrMagnitude<.0001f)continue; // Never override stop/unreachable.
    Vector2 here=nav.CellCenter(cell),toward=mean-here;bool horizontal=Mathf.Abs(toward.x)>=Mathf.Abs(toward.y);int sign=(horizontal?toward.x:toward.y)>=0?1:-1;
    Vector2 proposed=horizontal?new Vector2(sign,0):new Vector2(0,sign);if(Vector2.Dot(proposed,original)<.5f)continue;
    var list=horizontal?rows[cell/nav.ResolutionX]:columns[cell%nav.ResolutionX];if(list==null)continue;int at=list.BinarySearch(cell);if(at<0)at=~at;else continue;int pick=sign>0?at:at-1;if(pick<0||pick>=list.Count)continue;int target=list[pick];
    if(Vector2.Distance(here,nav.CellCenter(target))<near||!nav.HasClearCardinalRoute33(cell,target))continue;
    directions[cell]=proposed;
   }
   long t3=System.Diagnostics.Stopwatch.GetTimestamp();BuildTicks=t1-t0;SortTicks=t2-t1;ScanTicks=t3-t2;
  }
 }
}



namespace MassEngine.Tests
{
    public sealed class P3LaneFixedTests
    {
        private static readonly System.Reflection.BindingFlags Flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
        private static object Hydrate(Type type) => typeof(P3UniformOutputTests).GetMethod("Hydrate", Flags).Invoke(null, new object[] { type });
        private static void Exact(Vector2[] a, Vector2[] b) => typeof(P3UniformOutputTests).GetMethod("Exact", Flags).Invoke(null, new object[] { a, b });
        [Serializable] private sealed class Row
        {
            public int repetition, order; public string mode; public double milliseconds, buildMs, sortMs, scanMs;
        }
        [Serializable] private sealed class Report
        {
            public int sample, goals, cells, changedCells;
            public float stopRadius;
            public bool exactBits, centerBitsExact, goalsUnchanged;
            public string scope = "Identical recorded pre-Lane directions and goals. Reference uses frozen original grid Center/route methods, whose exact results are checked. Current-grid/frozen-Lane separates axis-cache from scalar-loop effects. Array copy and comparisons outside clocks; Lane's normal list allocations included. Allocation byte API is known unavailable.";
            public Row[] rows;
        }
        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)] [TestCase(5)]
        public void RealLaneInputsAreBitExactAndMeasured(int sample)
        {
            string output = null;
            foreach (string arg in Environment.GetCommandLineArgs()) if (arg.StartsWith("--interaction-p8-output=", StringComparison.Ordinal)) output = arg.Substring("--interaction-p8-output=".Length);
            Assert.IsNotNull(output);
            string capture = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../Logs/InteractionRefinement-20261007/P3/fixed-input-20261008-01-capture"));
            Vector2[] goals, input; float radius;
            using (var r = new System.IO.BinaryReader(System.IO.File.OpenRead(System.IO.Path.Combine(capture, "fixed-sample-" + sample + "-input.bin"))))
            {
                Assert.AreEqual("P3_FIXED_INPUT_V1", r.ReadString()); radius = r.ReadSingle(); goals = new Vector2[r.ReadInt32()];
                for (int i = 0; i < goals.Length; i++) goals[i] = new Vector2(r.ReadSingle(), r.ReadSingle());
            }
            using (var r = new System.IO.BinaryReader(System.IO.File.OpenRead(System.IO.Path.Combine(capture, "fixed-sample-" + sample + "-expected.bin"))))
            {
                input = new Vector2[r.ReadInt32()];
                for (int i = 0; i < input.Length; i++) input[i] = new Vector2(r.ReadSingle(), r.ReadSingle());
            }
            var reference = (P3OriginalNavigationGrid)Hydrate(typeof(P3OriginalNavigationGrid));
            var current = (TerrainNavigationGrid)Hydrate(typeof(TerrainNavigationGrid));
            var oldCenters = new Vector2[input.Length]; var centers = new Vector2[input.Length];
            for (int i = 0; i < input.Length; i++) { oldCenters[i] = reference.CellCenter(i); centers[i] = current.CellCenter(i); }
            Exact(oldCenters, centers);
            var goalCopy = (Vector2[])goals.Clone();
            var expected = (Vector2[])input.Clone(); P3LaneReference.Apply(reference, goals, expected, radius);
            int changed = 0; for (int i = 0; i < input.Length; i++) if (input[i].x != expected[i].x || input[i].y != expected[i].y) changed++;
            Assert.Greater(changed, 0, "Real Lane replacement path must be exercised");
            var buffer = new Vector2[input.Length]; var rows = new List<Row>();
            string[] modes = { "reference", "current-grid-reference-lane", "current", "profiled-reference" };
            double toMs = 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            for (int repetition = -2; repetition < 12; repetition++)
                for (int order = 0; order < 4; order++)
                {
                    int mode = (repetition + 8 + order) % 4;
                    Array.Copy(input, buffer, input.Length);
                    long start = System.Diagnostics.Stopwatch.GetTimestamp();
                    if (mode == 0) P3LaneReference.Apply(reference, goals, buffer, radius);
                    else if (mode == 1) P3LaneSameGridReference.Apply(current, goals, buffer, radius);
                    else if (mode == 2) TerrainLaneApproach33.Apply(current, goals, buffer, radius);
                    else P3LaneCoarseProfile.Apply(reference, goals, buffer, radius);
                    double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - start) * toMs;
                    Exact(expected, buffer); Exact(goalCopy, goals);
                    if (repetition >= 0) rows.Add(new Row { repetition = repetition, order = order, mode = modes[mode], milliseconds = ms,
                        buildMs = mode == 3 ? P3LaneCoarseProfile.BuildTicks * toMs : 0,
                        sortMs = mode == 3 ? P3LaneCoarseProfile.SortTicks * toMs : 0,
                        scanMs = mode == 3 ? P3LaneCoarseProfile.ScanTicks * toMs : 0 });
                }
            using (var w = new System.IO.BinaryWriter(System.IO.File.Create(System.IO.Path.Combine(output, "lane-final-" + sample + ".bin"))))
            {
                w.Write(expected.Length); foreach (var p in expected) { w.Write(p.x); w.Write(p.y); }
            }
            var report = new Report { sample = sample, cells = input.Length, goals = goals.Length, stopRadius = radius, changedCells = changed,
                exactBits = true, centerBitsExact = true, goalsUnchanged = true, rows = rows.ToArray() };
            System.IO.File.WriteAllText(System.IO.Path.Combine(output, "lane-fixed-" + sample + ".json"), JsonUtility.ToJson(report, true));
        }
        [TestCase(0f, 1f, 12, 10)] [TestCase(.03f, .1f, 12, 10)]
        [TestCase(100000f, .1f, 12, 10)] [TestCase(-100000f, .2f, 12, 10)]
        [TestCase(-360f, 3f, 256, 256)] [TestCase(0f, 1f, 1, 12)] [TestCase(0f, 1f, 12, 1)]
        public void ConstructedAxisCentersMatchFrozenFormula(float originValue, float step, int rx, int rz)
        {
            var origin = new Vector2(originValue, -originValue);
            var surface = new TerrainSurface("lane-axis", 1, 2, 2, origin, new Vector2(rx * step, rz * step), 80, new float[4], new bool[1]);
            var current = new TerrainNavigationGrid(surface, origin, step, rx, rz, 0, 0);
            var reference = new P3OriginalNavigationGrid(surface, origin, step, rx, rz, 0, 0);
            var a = new Vector2[current.CellCount]; var b = new Vector2[current.CellCount];
            for (int i = 0; i < a.Length; i++) { a[i] = reference.CellCenter(i); b[i] = current.CellCenter(i); }
            Exact(a, b);
            Assert.Throws<ArgumentOutOfRangeException>(() => current.CellCenter(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => current.CellCenter(current.CellCount));
        }
        [Test]
        public void ScalarLanePreservesSpecialVectorBitsAndGoalWeights()
        {
            var surface = new TerrainSurface("lane-special", 1, 2, 2, Vector2.zero, new Vector2(16,16), 80, new float[4], new bool[1]);
            var current = new TerrainNavigationGrid(surface, Vector2.zero, 1, 16, 16, 0, 0);
            var reference = new P3OriginalNavigationGrid(surface, Vector2.zero, 1, 16, 16, 0, 0);
            var goals = new[] { new Vector2(14.5f,1.5f),new Vector2(1.5f,14.5f),new Vector2(1.5f,14.5f), new Vector2(float.NaN,0) };
            var values = new[] { Vector2.zero, new Vector2(-0f,0f), new Vector2(.49999997f,.8f), new Vector2(.5f,.8f),
                new Vector2(.50000006f,.8f), new Vector2(float.NaN,1), new Vector2(1,float.NaN),
                new Vector2(float.PositiveInfinity,1), new Vector2(1,float.NegativeInfinity), new Vector2(-.6f,.8f) };
            foreach (var value in values)
                foreach (float radius in new[] { 0f, 2f, 5f, float.NaN })
                {
                    var a = new Vector2[256]; a[17] = value; var b = (Vector2[])a.Clone();
                    P3LaneReference.Apply(reference, goals, a, radius); TerrainLaneApproach33.Apply(current, goals, b, radius); Exact(a, b);
                }
        }
    }
}


namespace MassEngine.Tests {
 public sealed class P3LaneEarlyExitTests {
  static TerrainNavigationGrid Grid(string layout="flat") => (TerrainNavigationGrid)typeof(TerrainCardinalRouteCacheTests).GetMethod("Grid",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static).Invoke(null,new object[]{layout,16,16});
  static void Exact(Vector2[] a,Vector2[] b){Assert.AreEqual(a==null,b==null);if(a==null)return;Assert.AreEqual(a.Length,b.Length);for(int i=0;i<a.Length;i++){Assert.AreEqual(BitConverter.SingleToInt32Bits(a[i].x),BitConverter.SingleToInt32Bits(b[i].x),"x "+i);Assert.AreEqual(BitConverter.SingleToInt32Bits(a[i].y),BitConverter.SingleToInt32Bits(b[i].y),"y "+i);}}
  static Vector2[] Compare(TerrainNavigationGrid nav,Vector2[] goals,Vector2[] input,float radius){
   var a=input==null?null:(Vector2[])input.Clone();var b=input==null?null:(Vector2[])input.Clone();var saved=goals==null?null:(Vector2[])goals.Clone();
   Exception x=null,y=null;try{P3LaneSameGridReference.Apply(nav,goals,a,radius);}catch(Exception e){x=e;}
   try{TerrainLaneApproach33.Apply(nav,goals,b,radius);}catch(Exception e){y=e;}
   Assert.AreEqual(x?.GetType(),y?.GetType(),"Exception contract");Exact(a,b);Exact(saved,goals);return b;
  }
  [TestCase("flat")][TestCase("fractional")][TestCase("holes")][TestCase("wall-gap")][TestCase("closed-wall")][TestCase("hills")][TestCase("clearance")]
  public void PreserveSpecialBitsAndReadonlyRouteOutcomes(string layout){
   var nav=Grid(layout);float minusZero=BitConverter.Int32BitsToSingle(unchecked((int)0x80000000));
   var values=new[]{new Vector2(1,0),new Vector2(1,minusZero),new Vector2(-1,minusZero),new Vector2(0,1),new Vector2(minusZero,1),new Vector2(minusZero,-1),Vector2.zero,new Vector2(minusZero,minusZero),new Vector2(.49999997f,.8f),new Vector2(.5f,.8f),new Vector2(.50000006f,.8f),new Vector2(float.NaN,1),new Vector2(1,float.NaN),new Vector2(float.PositiveInfinity,1),new Vector2(1,float.NegativeInfinity),new Vector2(.009999999f,0)};
   var goals=new[]{nav.CellCenter(30),nav.CellCenter(225),nav.CellCenter(225),nav.CellCenter(17),new Vector2(float.NaN,0)};
   foreach(float radius in new[]{0f,2f,5f,float.NaN,float.PositiveInfinity,-1f})foreach(var value in values){var input=new Vector2[256];for(int i=0;i<input.Length;i++)input[i]=value;Compare(nav,goals,input,radius);}
   var random=new System.Random(17403);for(int rep=0;rep<8;rep++){var input=new Vector2[256];for(int i=0;i<input.Length;i++)input[i]=values[random.Next(values.Length)];Compare(nav,goals,input,rep);}
  }
  [TestCase(0)][TestCase(1)][TestCase(2)][TestCase(3)]
  public void SignedZeroMustNotSuppressRealBitReplacement(int axis){
   var nav=Grid();float nz=BitConverter.Int32BitsToSingle(unchecked((int)0x80000000));
   int from=axis==0?17:axis==1?30:axis==2?17:225;
   int target=axis==0?30:axis==1?17:axis==2?225:17;
   var input=new Vector2[256];input[from]=axis==0?new Vector2(1,nz):axis==1?new Vector2(-1,nz):axis==2?new Vector2(nz,1):new Vector2(nz,-1);
   var result=Compare(nav,new[]{nav.CellCenter(target)},input,0);
   Assert.AreEqual(0,BitConverter.SingleToInt32Bits(axis<2?result[from].y:result[from].x),"Fixture must actually rewrite minus zero to plus zero");
  }
  [TestCase(0)][TestCase(1)][TestCase(2)][TestCase(3)][TestCase(4)][TestCase(5)]
  public void LengthAndExceptionContract(int mode){
   var nav=Grid();Vector2[] input=mode==0?null:new Vector2[mode==1?0:mode==2?20:mode==3?256:257];
   if(input!=null){for(int i=0;i<Math.Min(input.Length,256);i++)input[i]=new Vector2(1,0);if(mode==5)input[256]=new Vector2(1,0);}
   Compare(nav,new[]{nav.CellCenter(30)},input,0);
  }
  [TestCase(0)][TestCase(1)][TestCase(2)]
  public void EmptyAndInvalidGoalsRetainEarlyReturn(int mode){
   Vector2[] goals=mode==0?null:mode==1?new Vector2[0]:new[]{new Vector2(float.NaN,0),new Vector2(float.PositiveInfinity,0)};
   Compare(mode<2?null:Grid(),goals,null,0);
  }
 }
}

namespace MassEngine
{
    /// <summary>
    /// Terrain-only CPU routing, shared mask/surface GPU storage and per-team async density snapshots.
    /// No agent-position readback, no per-agent pathfinder; the old plane keeps its GPU flow generator.
    /// </summary>
    public sealed class P3DensityRuntimeReference : IDisposable
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
        private readonly UnityEngine.Rendering.AsyncGPUReadbackRequest[] requests;
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

        public P3DensityRuntimeReference(TerrainNavigationGrid navigation, int teamCount, bool laneApproach33 = false)
        {
            Navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
            LaneApproach33 = laneApproach33;
            requests = new UnityEngine.Rendering.AsyncGPUReadbackRequest[teamCount]; pending = new bool[teamCount];
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
            if (disposed) throw new ObjectDisposedException(nameof(P3DensityRuntimeReference));
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
                requests[team] = UnityEngine.Rendering.AsyncGPUReadback.Request(buffers.runtimeFlowTargetDensityBuffer,
                    Navigation.CellCount * 4, team * Navigation.CellCount * 4);
                pending[team] = true;
            }
        }

        private void Upload(MassGpuBufferManager buffers, int team, float stopRadius, IReadOnlyList<Vector2> goals, bool dynamicEnemy = false)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            Vector2[] directions = Navigation.CreateFlowField(goals, stopRadius);
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
            // Requests use polling, not callbacks: no completion can write into a replacement world's buffers.
            Array.Clear(pending, 0, pending.Length);
            Array.Clear(ready, 0, ready.Length);
            navigationMask?.Release(); navigationMask = null;
            surfaceGpu?.Dispose(); surfaceGpu = null;
            SetVariant(combatShader, false); SetVariant(projectileShader, false);
        }
    }
}


namespace MassEngine.Tests {
 public sealed class P3DensityCopyTests {
  [Serializable] public sealed class Timing { public int repetition,order;public string mode;public double milliseconds; }
  [Serializable] public sealed class Report {public int sample,cells,targets;public bool exactBits,sourceUnchanged;public long scratchPayloadBytes;public string inputScope="Density support reconstructed from saved ordered target cells; positive magnitudes are synthetic, not captured GPU density values. NativeArray is owned test memory. Bulk copy included; allocation of persistent scratch excluded and disclosed. No live GetData/request timing.";public Timing[] rows;}
  static void Scan(TerrainNavigationGrid nav,Unity.Collections.NativeArray<uint> density,List<Vector2> snapshot){snapshot.Clear();for(int i=0;i<density.Length;i++)if(density[i]>0&&nav.IsWalkable(nav.CellCenter(i)))snapshot.Add(nav.CellCenter(i));}
  static void Bulk(TerrainNavigationGrid nav,Unity.Collections.NativeArray<uint> density,uint[] scratch,List<Vector2> snapshot){snapshot.Clear();density.CopyTo(scratch);for(int i=0;i<density.Length;i++)if(scratch[i]>0&&nav.IsWalkable(nav.CellCenter(i)))snapshot.Add(nav.CellCenter(i));}
  static void Exact(IReadOnlyList<Vector2> a,IReadOnlyList<Vector2> b){Assert.AreEqual(a.Count,b.Count);for(int i=0;i<a.Count;i++){Assert.AreEqual(BitConverter.SingleToInt32Bits(a[i].x),BitConverter.SingleToInt32Bits(b[i].x),"x "+i);Assert.AreEqual(BitConverter.SingleToInt32Bits(a[i].y),BitConverter.SingleToInt32Bits(b[i].y),"y "+i);}}
  static TerrainNavigationGrid Grid(string layout) => (TerrainNavigationGrid)typeof(TerrainCardinalRouteCacheTests).GetMethod("Grid",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static).Invoke(null,new object[]{layout,16,16});
  static string Output(){foreach(string arg in Environment.GetCommandLineArgs())if(arg.StartsWith("--interaction-p8-output=",StringComparison.Ordinal))return arg.Substring("--interaction-p8-output=".Length);throw new Exception("Missing output");}
  [TestCase(0)][TestCase(1)][TestCase(2)][TestCase(3)][TestCase(4)][TestCase(5)]
  public void ReconstructedCapturedSupportIsExactAndMeasured(int sample){
   string capture=System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath,"../Logs/InteractionRefinement-20261007/P3/fixed-input-20261008-01-capture"));Vector2[] goals;
   using(var r=new System.IO.BinaryReader(System.IO.File.OpenRead(System.IO.Path.Combine(capture,"fixed-sample-"+sample+"-input.bin")))){Assert.AreEqual("P3_FIXED_INPUT_V1",r.ReadString());r.ReadSingle();goals=new Vector2[r.ReadInt32()];for(int i=0;i<goals.Length;i++)goals[i]=new Vector2(r.ReadSingle(),r.ReadSingle());}
   var nav=(TerrainNavigationGrid)typeof(P3UniformOutputTests).GetMethod("Hydrate",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static).Invoke(null,new object[]{typeof(TerrainNavigationGrid)});
   var data=new uint[nav.CellCount];for(int i=0;i<goals.Length;i++){Assert.IsTrue(nav.TryGetCell(goals[i],out int cell));Assert.AreEqual(0u,data[cell],"Recorded goals must have unique support");data[cell]=i%2==0?uint.MaxValue:1u;}
   var scratch=new uint[data.Length];var buffer=new List<Vector2>(goals.Length);var rows=new List<Timing>();
   using(var density=new Unity.Collections.NativeArray<uint>(data,Unity.Collections.Allocator.Persistent)){
    Scan(nav,density,buffer);Exact(goals,buffer);Bulk(nav,density,scratch,buffer);Exact(goals,buffer);
    for(int rep=-3;rep<20;rep++)for(int order=0;order<2;order++){
     int mode=(rep+4+order)%2;long start=System.Diagnostics.Stopwatch.GetTimestamp();if(mode==0)Scan(nav,density,buffer);else Bulk(nav,density,scratch,buffer);double ms=(System.Diagnostics.Stopwatch.GetTimestamp()-start)*1000.0/System.Diagnostics.Stopwatch.Frequency;
     Exact(goals,buffer);if(rep>=0)rows.Add(new Timing{repetition=rep,order=order,mode=mode==0?"native-index":"bulk-copy-scan",milliseconds=ms});
    }
    CollectionAssert.AreEqual(data,density.ToArray());CollectionAssert.AreEqual(data,scratch);
   }
   var report=new Report{sample=sample,cells=data.Length,targets=goals.Length,exactBits=true,sourceUnchanged=true,scratchPayloadBytes=4L*data.Length,rows=rows.ToArray()};
   System.IO.File.WriteAllText(System.IO.Path.Combine(Output(),"density-copy-"+sample+".json"),JsonUtility.ToJson(report,true));
  }
  [TestCase(0)][TestCase(1)][TestCase(2)][TestCase(3)][TestCase(4)][TestCase(5)][TestCase(6)][TestCase(7)]
  public void BoundarySupportOrderAndScratchReuse(int mode){
   var nav=Grid(mode==5?"holes":mode==7?"fractional":"flat");var data=new uint[nav.CellCount];var walk=nav.CopyWalkable();
   for(int i=0;i<data.Length;i++)data[i]=mode==3?uint.MaxValue:mode==4?(i%7==0?(uint)(i+1):0):mode==5?(walk[i]==0?17u:0u):mode>=6?(i%3==0?1u:0u):0;
   if(mode==1){for(int i=0;i<data.Length;i++)if(walk[i]!=0){data[i]=1;break;}}
   if(mode==2){for(int i=data.Length-1;i>=0;i--)if(walk[i]!=0){data[i]=uint.MaxValue;break;}}
   var scratch=new uint[data.Length];for(int i=0;i<scratch.Length;i++)scratch[i]=uint.MaxValue;
   var a=new List<Vector2>();var b=new List<Vector2>();var saved=new List<Vector2>();
   using(var density=new Unity.Collections.NativeArray<uint>(data,Unity.Collections.Allocator.Persistent)){
    Scan(nav,density,a);Bulk(nav,density,scratch,b);Exact(a,b);saved.AddRange(b);CollectionAssert.AreEqual(data,scratch);CollectionAssert.AreEqual(data,density.ToArray());
    if(mode==0||mode==5)Assert.AreEqual(0,b.Count);if(mode==1||mode==2)Assert.AreEqual(1,b.Count);
   }
   // Reuse the same managed scratch for another empty team; previous output must survive.
   using(var empty=new Unity.Collections.NativeArray<uint>(data.Length,Unity.Collections.Allocator.Persistent)){Bulk(nav,empty,scratch,b);Assert.AreEqual(0,b.Count);}
   Exact(a,saved);foreach(uint x in scratch)Assert.AreEqual(0u,x);
  }
  [Test]
  public void CompletedGpuReadbackCopiesBeforeRequestLifetimeEnds(){
   Assert.IsTrue(SystemInfo.supportsAsyncGPUReadback,"This machine must exercise real async readback, not skip");
   var nav=Grid("holes");var data=new uint[nav.CellCount];for(int i=0;i<data.Length;i++)data[i]=i%5==0?uint.MaxValue:0u;
   var scratch=new uint[data.Length];var a=new List<Vector2>();var b=new List<Vector2>();
   using(var gpu=new ComputeBuffer(data.Length,4)){
    gpu.SetData(data);var request=UnityEngine.Rendering.AsyncGPUReadback.Request(gpu);request.WaitForCompletion();Assert.IsFalse(request.hasError);
    var density=request.GetData<uint>();Scan(nav,density,a);Bulk(nav,density,scratch,b);Exact(a,b);CollectionAssert.AreEqual(data,scratch);
   }
   // Only owned managed copies are used after buffer release; never keep/read the request view.
   Exact(a,b);CollectionAssert.AreEqual(data,scratch);
  }
 }
}


namespace MassEngine.Tests {
 public sealed class P3DensityRuntimeTests {
  static readonly System.Reflection.BindingFlags F=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic;
  sealed class World:IDisposable {
   public TerrainNavigationGrid nav;public object runtime;public MassGpuBufferManager buffers;public PipelineFrameContext context;
   public World(bool reference){
    nav=(TerrainNavigationGrid)typeof(TerrainCardinalRouteCacheTests).GetMethod("Grid",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static).Invoke(null,new object[]{"holes",16,16});
    runtime=reference?(object)new P3DensityRuntimeReference(nav,2,true):new TerrainNavigationRuntime(nav,2,true);
    buffers=new MassGpuBufferManager{runtimeFlowTargetDensityBuffer=new ComputeBuffer(512,4),flowFieldDirectionsBuffer=new ComputeBuffer(512,8)};
    buffers.flowFieldDirectionsBuffer.SetData(new Vector2[512]);
    var data=new uint[512];for(int i=0;i<256;i++){data[i]=i%17==0?1u:0;data[256+i]=i%23==0?uint.MaxValue:0;}buffers.runtimeFlowTargetDensityBuffer.SetData(data);
    context=new PipelineFrameContext{battleStarted=true,teamFlows=new[]{new TeamFlowFrameSettings{enabled=true,dynamicFlowEnabled=true},new TeamFlowFrameSettings{enabled=true,dynamicFlowEnabled=true}}};
   }
   public object Field(string name)=>runtime.GetType().GetField(name,F).GetValue(runtime);
   public void Call(string method,params object[] args)=>runtime.GetType().GetMethod(method,F).Invoke(runtime,args);
   public int Completed=>(int)runtime.GetType().GetProperty("CompletedFields").GetValue(runtime);
   public void RequestBoth(){Call("Rebuild",buffers,context,0);Call("Rebuild",buffers,context,1);foreach(var r in (UnityEngine.Rendering.AsyncGPUReadbackRequest[])Field("requests")){r.WaitForCompletion();Assert.IsFalse(r.hasError);}}
   public void Tick()=>Call("Tick",buffers,context);
   public Vector2[] Directions(){var x=new Vector2[512];buffers.flowFieldDirectionsBuffer.GetData(x);return x;}
   public void Dispose(){((IDisposable)runtime).Dispose();buffers.runtimeFlowTargetDensityBuffer?.Release();buffers.flowFieldDirectionsBuffer?.Release();}
  }
  static void Exact(Vector2[] a,Vector2[] b){Assert.AreEqual(a.Length,b.Length);for(int i=0;i<a.Length;i++){Assert.AreEqual(BitConverter.SingleToInt32Bits(a[i].x),BitConverter.SingleToInt32Bits(b[i].x));Assert.AreEqual(BitConverter.SingleToInt32Bits(a[i].y),BitConverter.SingleToInt32Bits(b[i].y));}}
  static void Compare(World a,World b){Assert.AreEqual(a.Completed,b.Completed);foreach(string n in new[]{"pending","ready"})CollectionAssert.AreEqual((bool[])a.Field(n),(bool[])b.Field(n));Assert.AreEqual(a.Field("nextDynamicTeam"),b.Field("nextDynamicTeam"));var x=(List<Vector2>[])a.Field("readyTargets");var y=(List<Vector2>[])b.Field("readyTargets");for(int i=0;i<2;i++)Exact(x[i].ToArray(),y[i].ToArray());Exact(a.Directions(),b.Directions());}
  [TestCase(0)][TestCase(1)][TestCase(2)][TestCase(3)][TestCase(4)][TestCase(5)][TestCase(6)][TestCase(7)][TestCase(8)]
  public void RealRuntimeLifecycleMatchesFrozenReference(int mode){
   using(var a=new World(true))using(var b=new World(false)){
    a.RequestBoth();b.RequestBoth();
    foreach(var w in new[]{a,b}){
     if(mode==1)w.context.teamFlows[0].enabled=false;
     if(mode==2)w.context.battleStarted=false;
     if(mode==3)w.context.teamFlows[0].dynamicFlowEnabled=false;
     if(mode==4){w.context.teamFlows[0].targetMode=1;w.context.teamFlows[0].targetPoint=new Vector3(14.5f,0,1.5f);w.Call("Rebuild",w.buffers,w.context,0);}
     if(mode==5)((IDisposable)w.runtime).Dispose();
     if(mode==6){w.context.teamFlows[0].enabled=false;w.Call("Rebuild",w.buffers,w.context,0);}
    }
    a.Tick();b.Tick();Compare(a,b);
    if(mode==0){Assert.AreEqual(1,b.Completed);Assert.IsTrue(((bool[])b.Field("ready"))[1]);}
    if(mode==2||mode==5)Assert.AreEqual(0,b.Completed);
    if(mode==7)foreach(var w in new[]{a,b}){w.context.teamFlows[1].enabled=false;w.Call("Rebuild",w.buffers,w.context,1);}
    a.Tick();b.Tick();Compare(a,b);
    if(mode==0)Assert.AreEqual(2,b.Completed);
    if(mode==7)Assert.AreEqual(1,b.Completed);
    if(mode==8){a.RequestBoth();b.RequestBoth();a.Tick();b.Tick();Compare(a,b);a.Tick();b.Tick();Compare(a,b);Assert.AreEqual(4,b.Completed);}
    var scratch=typeof(TerrainNavigationRuntime).GetField("densityScratch",F);
    if(scratch!=null){if(mode==2||mode==5)Assert.IsNull(scratch.GetValue(b.runtime));else Assert.AreEqual(256,((uint[])scratch.GetValue(b.runtime)).Length);((IDisposable)b.runtime).Dispose();Assert.IsNull(scratch.GetValue(b.runtime));b.Tick();}
   }
  }
  [UnityEngine.TestTools.UnityTest]
  public System.Collections.IEnumerator DeferredOwnedTargetsSurviveAnotherEditorUpdate(){
   using(var a=new World(true))using(var b=new World(false)){
    a.RequestBoth();b.RequestBoth();a.Tick();b.Tick();Compare(a,b);var saved=((List<Vector2>[])b.Field("readyTargets"))[1].ToArray();
    yield return null;
    a.Tick();b.Tick();Compare(a,b);Assert.AreEqual(2,b.Completed);Exact(saved,((List<Vector2>[])b.Field("readyTargets"))[1].ToArray());
   }
  }
 }
}
