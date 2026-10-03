#if UNITY_EDITOR
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MassEngine.Tests
{
    public sealed class TerrainSurfaceGpuTests
    {
        [Test]
        public void RealGpuAgreesWithIndependentTriangleValuesAndCpuAtGridEdges()
        {
            Assert.True(SystemInfo.supportsComputeShaders, "M6.1 needs a graphics device.");
            var shader = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/MassEngine/Terrain/Shaders/TerrainSurfaceProbe.compute"));
            var surface = new TerrainSurface("gpu", 1, 3, 2, new Vector2(10, -5), new Vector2(4, 4), 80,
                new[] { 0f, 2f, 4f, 4f, 10f, 12f }, new[] { false, true });
            var input = new[]
            {
                new Vector4(11.5f, -4, 3, 4), new Vector4(10.5f, -2, -3, 4),
                new Vector4(14, -1, 0, 0), new Vector4(12, -3, 1, 0),
                new Vector4(9, -3, 1, 0), new Vector4(float.NaN, -3, 1, 0), new Vector4(10, -5, 0, 1)
            };
            try
            {
                using (var gpu = new TerrainSurfaceGpu(surface))
                using (var queries = new ComputeBuffer(input.Length, 16))
                using (var output = new ComputeBuffer(input.Length, 16))
                using (var velocities = new ComputeBuffer(input.Length, 16))
                {
                    int kernel = shader.FindKernel("SampleSurface");
                    gpu.Bind(shader, kernel);
                    queries.SetData(input);
                    shader.SetInt("_QueryCount", input.Length);
                    shader.SetBuffer(kernel, "_Queries", queries); shader.SetBuffer(kernel, "_Samples", output);
                    shader.SetBuffer(kernel, "_Velocities", velocities); shader.Dispatch(kernel, 1, 1, 1);
                    var samples = new Vector4[input.Length]; var actualVelocity = new Vector4[input.Length];
                    output.GetData(samples); velocities.GetData(actualVelocity);
                    Assert.That(samples[0].x, Is.EqualTo(3.5f).Within(1e-5));
                    Assert.That(samples[1].x, Is.EqualTo(4.5f).Within(1e-5));
                    Assert.That(samples[2].x, Is.EqualTo(12).Within(1e-5)); Assert.AreEqual(0, samples[2].w);
                    for (int i = 0; i < input.Length; i++)
                    {
                        if (!surface.TrySample(new Vector2(input[i].x, input[i].y), out var expected))
                        { Assert.AreEqual(-1, samples[i].w); Assert.AreEqual(0, actualVelocity[i].w); continue; }
                        Assert.That(samples[i].x, Is.EqualTo(expected.Position.y).Within(1e-5));
                        Assert.That(samples[i].y, Is.EqualTo(expected.Gradient.x).Within(1e-5));
                        Assert.That(samples[i].z, Is.EqualTo(expected.Gradient.y).Within(1e-5));
                        Assert.AreEqual(expected.Walkable ? 1 : 0, samples[i].w);
                        Vector3 expectedVelocity = expected.SurfaceVelocity(new Vector2(input[i].z, input[i].w));
                        Assert.That(Vector3.Distance(expectedVelocity, (Vector3)actualVelocity[i]), Is.LessThan(1e-5));
                    }
                    gpu.Dispose(); Assert.False(gpu.IsAllocated); // Also verifies double disposal on scope exit.
                }
            }
            finally { Object.DestroyImmediate(shader); }
        }

        [Test]
        public void VisibleMeshAndSamplerUseTheSameHeightSurface()
        {
            var asset = TerrainPrototype.CreateAsset();
            var go = new GameObject("M6.1 collider test");
            Mesh mesh = null;
            try
            {
                Assert.True(asset.TryCreateSurface(out var surface, out _));
                mesh = TerrainPrototype.CreateMesh(surface);
                var collider = go.AddComponent<MeshCollider>(); collider.sharedMesh = mesh;
                Physics.SyncTransforms();
                var random = new System.Random(61);
                for (int i = 0; i < 120; i++)
                {
                    var point = new Vector2(-300 + (float)random.NextDouble() * 600, -110 + (float)random.NextDouble() * 220);
                    Assert.True(surface.TrySample(point, out var sample));
                    Assert.True(collider.Raycast(new Ray(new Vector3(point.x, 150, point.y), Vector3.down), out var hit, 200));
                    Assert.That(hit.point.y, Is.EqualTo(sample.Position.y).Within(.001f));
                }
            }
            finally { Object.DestroyImmediate(go); Object.DestroyImmediate(mesh); Object.DestroyImmediate(asset); }
        }
    }
}
#endif
