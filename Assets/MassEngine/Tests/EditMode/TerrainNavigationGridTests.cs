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
