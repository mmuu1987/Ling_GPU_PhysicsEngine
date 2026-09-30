using System;
using NUnit.Framework;
using UnityEngine;

namespace MassEngine.Tests
{
    public sealed class TerrainSurfaceTests
    {
        private static TerrainSurface Surface(float[] heights, bool blocked = false, float slope = 60) =>
            new TerrainSurface("test", 1, 2, 2, new Vector2(10, -5), new Vector2(2, 4), slope, heights, new[] { blocked });

        [Test]
        public void SamplesBothTrianglesOfNonPlanarCellUsingMeshDiagonal()
        {
            var surface = Surface(new[] { 0f, 2f, 4f, 10f }, slope: 85);
            Assert.True(surface.TrySample(new Vector2(11.5f, -4), out var lower));
            Assert.That(lower.Position.y, Is.EqualTo(3.5f).Within(1e-6));
            Assert.That(lower.Gradient, Is.EqualTo(new Vector2(1, 2)));
            Assert.True(surface.TrySample(new Vector2(10.5f, -2), out var upper));
            Assert.That(upper.Position.y, Is.EqualTo(4.5f).Within(1e-6));
            Assert.That(upper.Gradient, Is.EqualTo(new Vector2(3, 1)));
        }

        [Test]
        public void BoundaryVerticesAndDiagonalAreContinuous()
        {
            var surface = Surface(new[] { 0f, 2f, 4f, 10f }, slope: 85);
            Assert.True(surface.TrySample(new Vector2(12, -1), out var max));
            Assert.That(max.Position.y, Is.EqualTo(10));
            surface.TrySample(new Vector2(11, -3), out var center);
            Assert.That(center.Position.y, Is.EqualTo(5).Within(1e-6));
            surface.TrySample(new Vector2(11 - 1e-5f, -3), out var beside);
            Assert.That(beside.Position.y, Is.EqualTo(center.Position.y).Within(1e-4));
        }

        [TestCase(9.99f, -3)]
        [TestCase(12.01f, -3)]
        [TestCase(11, -5.01f)]
        [TestCase(11, -0.99f)]
        [TestCase(float.NaN, -3)]
        [TestCase(float.PositiveInfinity, -3)]
        public void OutsideAndNonFinitePositionsAreRejected(float x, float z)
        { Assert.False(Surface(new float[4]).TrySample(new Vector2(x, z), out _)); }

        [Test]
        public void WholeCellRejectsSteepSecondTriangleAndDiagonalSlope()
        {
            var steepHalf = Surface(new[] { 0f, 0f, 10f, 0f }, slope: 40);
            Assert.False(steepHalf.IsCellWalkable(0, 0));
            // Each axis alone is 30 degrees; combined gradient is steeper than 35.
            float g = Mathf.Tan(30 * Mathf.Deg2Rad);
            Assert.False(Surface(new[] { 0, 2 * g, 4 * g, 6 * g }, slope: 35).IsCellWalkable(0, 0));
            Assert.True(Surface(new float[4], slope: 0).IsCellWalkable(0, 0));
            Assert.False(Surface(new float[4], blocked: true).IsCellWalkable(0, 0));
        }

        [Test]
        public void LiftedVelocityKeepsHeadingSpeedAndTangency()
        {
            Surface(new[] { 0f, 1f, 1f, 2f }).TrySample(new Vector2(11, -3), out var sample);
            Vector3 velocity = sample.SurfaceVelocity(new Vector2(3, -4));
            Assert.That(velocity.magnitude, Is.EqualTo(5).Within(1e-5));
            Assert.That(Vector3.Dot(velocity, sample.Normal), Is.EqualTo(0).Within(1e-5));
            Assert.That(velocity.x / velocity.z, Is.EqualTo(-.75f).Within(1e-5));
            Assert.That(sample.SurfaceVelocity(Vector2.zero), Is.EqualTo(Vector3.zero));
        }

        [Test]
        public void SnapshotsAndFailedAssetEditsPreserveAuthoredData()
        {
            var asset = ScriptableObject.CreateInstance<TerrainSurfaceAsset>();
            try
            {
                var heights = new[] { 2f, 2f, 2f, 2f };
                var blocked = new[] { false };
                asset.Initialize("immutable", 2, 2, 2, Vector2.zero, Vector2.one, 40, heights, blocked);
                Assert.True(asset.TryCreateSurface(out var before, out _));
                heights[0] = 900; blocked[0] = true;
                Assert.Throws<ArgumentException>(() => asset.Initialize("bad", 0, 2, 2, Vector2.zero, Vector2.one, 40, heights, blocked));
                Assert.True(asset.TryCreateSurface(out var after, out _));
                Assert.AreEqual("immutable", after.Id); Assert.AreEqual(2, after.Version);
                Assert.AreEqual(2, before.HeightAtVertex(0, 0)); Assert.AreEqual(2, after.HeightAtVertex(0, 0));
                Assert.True(after.IsCellWalkable(0, 0));
            }
            finally { UnityEngine.Object.DestroyImmediate(asset); }
        }

        [Test]
        public void RejectsCorruptOrIncompleteData()
        {
            Assert.Throws<ArgumentException>(() => Surface(new[] { 0f }));
            Assert.Throws<ArgumentException>(() => Surface(new[] { 0f, float.NaN, 0f, 0f }));
            Assert.Throws<ArgumentException>(() => Surface(new float[4], slope: 89));
            var asset = ScriptableObject.CreateInstance<TerrainSurfaceAsset>();
            try { Assert.False(asset.TryCreateSurface(out _, out var error)); Assert.IsNotEmpty(error); }
            finally { UnityEngine.Object.DestroyImmediate(asset); }
        }

        [Test]
        public void PrototypeHasConnectedRampPassageHighGroundAndExclusion()
        {
            var asset = TerrainPrototype.CreateAsset();
            try
            {
                Assert.True(asset.TryCreateSurface(out var surface, out _));
                Assert.AreEqual(526340, surface.GpuBytes);
                surface.TrySample(new Vector2(-100, 0), out var plateau);
                Assert.AreEqual(48, plateau.Position.y); Assert.True(plateau.Walkable);
                surface.TrySample(new Vector2(-80, -40), out var exclusion); Assert.False(exclusion.Walkable);
                surface.TrySample(new Vector2(240, 28), out var cliff); Assert.False(cliff.Walkable);
                // Center line crosses ascent, high ground, descent and the canyon floor.
                for (float x = -300; x <= 330; x += .7f)
                {
                    Assert.True(surface.TrySample(new Vector2(x, 0), out var sample));
                    Assert.True(sample.Walkable, "Blocked center passage at " + x);
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(asset); }
        }
    }
}
