using NUnit.Framework;
using UnityEngine;

namespace MassEngine.Game.Tests
{
    public sealed class TerrainSurfaceQueriesTests
    {
        [TestCase(.75f, .25f, 1f)]
        [TestCase(.25f, .75f, 1f)]
        [TestCase(.5f, .5f, 2f)]
        [TestCase(1f, 1f, 4f)]
        public void VerticalRayHitsSameTriangleNotBilinearSurface(float x, float z, float height)
        {
            var surface = new TerrainSurface("triangles", 1, 2, 2, Vector2.zero, Vector2.one, 80,
                new[] { 0f, 0f, 0f, 4f }, new bool[1]);
            Assert.That(TerrainSurfaceQueries.Raycast(surface, new Ray(new Vector3(x, 10, z), Vector3.down), 20, out var point), Is.True);
            Assert.That(point.y, Is.EqualTo(height).Within(1e-5));
            Assert.That(surface.TrySample(new Vector2(point.x, point.z), out var sample), Is.True);
            Assert.That(point, Is.EqualTo(sample.Position));
        }

        [Test]
        public void ShallowRayTraversesCellsAndReturnsNearestRidgeRatherThanGroundPlane()
        {
            // Two ridges at x=10 and x=20. A horizontal ray must hit the first one.
            const int width = 31;
            var heights = new float[width * 2];
            for (int z = 0; z < 2; z++)
                for (int x = 0; x < width; x++) heights[z * width + x] = x == 10 || x == 20 ? 8 : 0;
            var surface = new TerrainSurface("ridges", 1, width, 2, Vector2.zero, new Vector2(30, 1), 80, heights, new bool[30]);
            Assert.That(TerrainSurfaceQueries.Raycast(surface, new Ray(new Vector3(-2, 4, .5f), Vector3.right), 40, out var point), Is.True);
            Assert.That(point.x, Is.EqualTo(9.5f).Within(1e-4));
            Assert.That(point.y, Is.EqualTo(4).Within(1e-4));
            Assert.That(TerrainSurfaceQueries.Raycast(surface, new Ray(new Vector3(32, 4, .5f), Vector3.left), 40, out point), Is.True);
            Assert.That(point.x, Is.EqualTo(20.5f).Within(1e-4));
        }

        [Test]
        public void OutsideUpwardShortAndInvalidRaysDoNotInventAHit()
        {
            var surface = new TerrainSurface("plane", 1, 2, 2, Vector2.zero, Vector2.one, 40,
                new[] { 5f, 5f, 5f, 5f }, new bool[1]);
            Assert.That(TerrainSurfaceQueries.Raycast(surface, new Ray(new Vector3(-1, 10, .5f), Vector3.down), 20, out _), Is.False);
            Assert.That(TerrainSurfaceQueries.Raycast(surface, new Ray(new Vector3(.5f, 10, .5f), Vector3.up), 20, out _), Is.False);
            Assert.That(TerrainSurfaceQueries.Raycast(surface, new Ray(new Vector3(.5f, 10, .5f), Vector3.down), 4, out _), Is.False);
            Assert.That(TerrainSurfaceQueries.Raycast(surface, new Ray(new Vector3(float.NaN, 10, .5f), Vector3.down), 20, out _), Is.False);
            Assert.That(TerrainSurfaceQueries.Raycast(null, new Ray(Vector3.up, Vector3.down), 20, out _), Is.False);
        }

        [Test]
        public void GridEdgeAndCornerDiagonalRaysAreNotSkipped()
        {
            var surface = new TerrainSurface("grid", 1, 5, 5, Vector2.zero, Vector2.one * 4, 40,
                new float[25], new bool[16]);
            foreach (var origin in new[] { new Vector3(4, 4, 4), new Vector3(2, 4, 4), new Vector3(0, 4, 0) })
            {
                Vector3 target = new Vector3(2, 0, 2);
                Assert.That(TerrainSurfaceQueries.Raycast(surface, new Ray(origin, target - origin), 20, out var point), Is.True);
                Assert.That(Vector3.Distance(point, target), Is.LessThan(1e-4));
            }
        }
    }
}
