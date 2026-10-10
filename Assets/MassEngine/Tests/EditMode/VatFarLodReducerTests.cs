using System;
using System.Collections.Generic;
using MassEngine.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MassEngine.Tests
{
    /// <summary>
    /// Contract tests for the multi-pose QEM far reducer: the far VAT is an exact column subset of the
    /// source far VAT, mid/near stay the source's, sources are never modified, outputs are new assets only.
    /// </summary>
    public sealed class VatFarLodReducerTests
    {
        private const string TempFolder = "Assets/VatFarLodReducerTestTemp";
        private const int Frames = 8;

        [TearDown]
        public void TearDown()
        {
            if (AssetDatabase.IsValidFolder(TempFolder))
                AssetDatabase.DeleteAsset(TempFolder);
        }

        // ------------------------------------------------------------ pure simplifier

        [Test]
        public void SimplifierKeepsOnlyOriginalVerticesAndMeetsBudget()
        {
            Sphere(24, 32, out Vector3[] positions, out Vector2[] uv, out int[] triangles);
            VatQemSimplifier.Result r = VatQemSimplifier.Simplify(positions.Length, new[] { Flatten(positions, 1f) },
                FlattenUv(uv), triangles, new VatQemSimplifier.Options { targetTriangles = 200 });
            Assert.LessOrEqual(r.TriangleCount, 200);
            Assert.GreaterOrEqual(r.TriangleCount, 150);
            var pairs = new HashSet<long>();
            for (int i = 0; i < r.keptSourceVertices.Length; i++)
            {
                Assert.That(r.keptSourceVertices[i], Is.InRange(0, positions.Length - 1));
                Assert.That(r.keptUvSourceVertices[i], Is.InRange(0, positions.Length - 1));
                Assert.IsTrue(pairs.Add((long)r.keptSourceVertices[i] * positions.Length + r.keptUvSourceVertices[i]), "duplicate kept vertex");
            }
            Assert.AreEqual(0, r.crossChartTriangles, "no triangle may mix UV charts");
            foreach (int index in r.triangles) Assert.That(index, Is.InRange(0, r.VertexCount - 1));
        }

        [Test]
        public void MultiPoseMetricKeepsCreaseThatOnlyExistsInLaterPose()
        {
            const int gx = 41, gy = 5;
            var flat = new float[gx * gy * 3];
            var folded = new float[gx * gy * 3];
            var triangles = new List<int>();
            for (int y = 0; y < gy; y++)
                for (int x = 0; x < gx; x++)
                {
                    int v = y * gx + x;
                    float px = (x - 20) / 10f, py = y / 10f;
                    flat[3 * v] = px; flat[3 * v + 1] = py; flat[3 * v + 2] = 0f;
                    folded[3 * v] = px; folded[3 * v + 1] = py; folded[3 * v + 2] = Mathf.Abs(px);
                }
            for (int y = 0; y < gy - 1; y++)
                for (int x = 0; x < gx - 1; x++)
                {
                    int a = y * gx + x;
                    triangles.AddRange(new[] { a, a + gx, a + 1, a + 1, a + gx, a + gx + 1 });
                }
            var options = new VatQemSimplifier.Options { targetTriangles = 24 };
            VatQemSimplifier.Result single = VatQemSimplifier.Simplify(gx * gy, new[] { flat }, null, triangles.ToArray(), options);
            VatQemSimplifier.Result multi = VatQemSimplifier.Simplify(gx * gy, new[] { flat, folded }, null, triangles.ToArray(), options);
            double singleError = VatQemSimplifier.MeasureDeviation(new[] { folded }, triangles.ToArray(), single).max;
            double multiError = VatQemSimplifier.MeasureDeviation(new[] { folded }, triangles.ToArray(), multi).max;
            Assert.Less(multiError, 1e-4, "The fold crease must survive when the folded pose is sampled.");
            Assert.Greater(singleError, 0.1, "Sanity: the bind pose alone cannot see the crease.");
        }

        [Test]
        public void SimplifierDropsTinyShellsAndNeverFoldsATetrahedron()
        {
            var tetra = new float[] { 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1 };
            int[] tt = { 0, 2, 1, 0, 1, 3, 0, 3, 2, 1, 2, 3 };
            VatQemSimplifier.Result r = VatQemSimplifier.Simplify(4, new[] { tetra }, null, tt, new VatQemSimplifier.Options { targetTriangles = 1 });
            Assert.AreEqual(4, r.TriangleCount);
            Assert.IsFalse(r.reachedTarget);

            Sphere(12, 16, out Vector3[] big, out Vector2[] uv, out int[] tri);
            var all = new Vector3[big.Length * 2];
            for (int i = 0; i < big.Length; i++) { all[i] = big[i]; all[i + big.Length] = big[i] * .02f + new Vector3(3, 0, 0); }
            var allTri = new int[tri.Length * 2];
            for (int i = 0; i < tri.Length; i++) { allTri[i] = tri[i]; allTri[i + tri.Length] = tri[i] + big.Length; }
            VatQemSimplifier.Result c = VatQemSimplifier.Simplify(all.Length, new[] { Flatten(all, 1f) }, null, allTri,
                new VatQemSimplifier.Options { targetTriangles = 100 });
            Assert.AreEqual(1, c.droppedComponents);
            foreach (int v in c.keptSourceVertices) Assert.Less(v, big.Length);
        }

        // ------------------------------------------------------------ Unity asset contract

        [Test]
        public void FarVariantIsExactColumnSubsetAndKeepsMidAndSource()
        {
            VATProfile source = CreateSourceProfile();
            int sourceLowVertices = source.lowLodMesh.vertexCount;
            Color[] sourcePixels = source.lowLodPositionTexture.GetPixels();
            Color[] sourceNormals = source.lowLodNormalTexture.GetPixels();

            VATProfile result = VatFarLodReducer.CreateFarVariant(source, TempFolder + "/Far.asset",
                new VatFarLodReducer.Settings { targetTriangles = 120, samplesPerClip = 2, preserveSeams = false, sourceSlot = "far" },
                out VatFarLodReducer.Report report);

            Assert.IsTrue(VatProfileValidation.TryValidate(result, out string error), error);
            Assert.LessOrEqual(result.lowLodMesh.GetIndexCount(0) / 3, 120u);
            Assert.AreEqual(report.resultTriangles, (int)(result.lowLodMesh.GetIndexCount(0) / 3));
            Assert.AreSame(source.cleanMesh, result.cleanMesh, "Near must be shared.");
            Assert.AreSame(source.lowLodMesh, result.midLodMesh, "Source far (low) becomes the new mid: mid unchanged on screen.");
            Assert.AreSame(source.lowLodPositionTexture, result.midLodPositionTexture);
            Assert.AreEqual(sourceLowVertices, source.lowLodMesh.vertexCount, "Source must not be modified.");
            Assert.AreEqual(source.lowLodMesh.bounds, result.lowLodMesh.bounds, "Animation-expanded bounds are kept.");

            // Every new vertex column must equal some source column bit-for-bit in every frame,
            // and the mesh vertex must be that source vertex.
            Color[] farPixels = result.lowLodPositionTexture.GetPixels();
            Color[] farNormals = result.lowLodNormalTexture.GetPixels();
            Vector3[] sourceVertices = source.lowLodMesh.vertices;
            Vector3[] farVertices = result.lowLodMesh.vertices;
            for (int k = 0; k < farVertices.Length; k++)
            {
                int match = Array.IndexOf(sourceVertices, farVertices[k]);
                Assert.GreaterOrEqual(match, 0, "Far vertex must be an original vertex.");
                bool columnMatches = false;
                for (int s = 0; s < sourceVertices.Length && !columnMatches; s++)
                {
                    if (sourceVertices[s] != farVertices[k]) continue;
                    columnMatches = true;
                    for (int f = 0; f < Frames; f++)
                    {
                        int a = Pixel(f, k, result.lowLodTextureWidth, result.lowLodRowsPerFrame);
                        int b = Pixel(f, s, source.lowLodTextureWidth, source.lowLodRowsPerFrame);
                        if (farPixels[a] != sourcePixels[b] || farNormals[a] != sourceNormals[b]) { columnMatches = false; break; }
                    }
                }
                Assert.IsTrue(columnMatches, "Far VAT column " + k + " must be copied, not blended.");
            }
            Assert.AreEqual(TempFolder + "/Far.asset", AssetDatabase.GetAssetPath(result.lowLodMesh));
        }

        [Test]
        public void FullSourceVariantIsColumnSubsetOfFullVat()
        {
            VATProfile source = CreateSourceProfile();
            VATProfile result = VatFarLodReducer.CreateFarVariant(source, TempFolder + "/FarFull.asset",
                new VatFarLodReducer.Settings { targetTriangles = 100, samplesPerClip = 2 }, out VatFarLodReducer.Report report);
            Assert.AreEqual("full", report.sourceFarSlot);
            Assert.AreEqual(0, report.crossChartTriangles);
            Assert.IsTrue(VatProfileValidation.TryValidate(result, out string error), error);
            Assert.AreSame(source.lowLodMesh, result.midLodMesh, "Mid must stay the source's resolved mid (here: low).");
            Assert.LessOrEqual(result.lowLodMesh.GetIndexCount(0) / 3, 100u);
            Color[] full = source.positionTexture.GetPixels();
            Color[] far = result.lowLodPositionTexture.GetPixels();
            Vector3[] cleanVertices = source.cleanMesh.vertices;
            Vector3[] farVertices = result.lowLodMesh.vertices;
            for (int k = 0; k < farVertices.Length; k++)
            {
                bool matched = false;
                for (int s = 0; s < cleanVertices.Length && !matched; s++)
                {
                    if (cleanVertices[s] != farVertices[k]) continue;
                    matched = true;
                    for (int f = 0; f < Frames && matched; f++)
                        matched = far[Pixel(f, k, result.lowLodTextureWidth, result.lowLodRowsPerFrame)] ==
                                  full[Pixel(f, s, source.textureWidth, source.rowsPerFrame)];
                }
                Assert.IsTrue(matched, "Far column " + k + " must be a copied full-VAT column.");
            }
        }

        [Test]
        public void OptimalPlacementWritesFiniteUnitNormalVat()
        {
            VATProfile source = CreateSourceProfile();
            VATProfile result = VatFarLodReducer.CreateFarVariant(source, TempFolder + "/FarOpt.asset",
                new VatFarLodReducer.Settings { targetTriangles = 100, samplesPerClip = 2, optimalPlacement = true }, out VatFarLodReducer.Report report);
            Assert.IsTrue(report.optimalPlacement);
            Assert.IsTrue(VatProfileValidation.TryValidate(result, out string error), error);
            Color[] positions = result.lowLodPositionTexture.GetPixels();
            Color[] normals = result.lowLodNormalTexture.GetPixels();
            int n = result.lowLodMesh.vertexCount;
            for (int f = 0; f < Frames; f++)
                for (int k = 0; k < n; k++)
                {
                    int i = Pixel(f, k, result.lowLodTextureWidth, result.lowLodRowsPerFrame);
                    var p = new Vector3(positions[i].r, positions[i].g, positions[i].b);
                    Assert.IsTrue(!float.IsNaN(p.x) && !float.IsInfinity(p.magnitude) && p.magnitude < 3f, "placed position must stay near the source surface");
                    Assert.AreEqual(1f, new Vector3(normals[i].r, normals[i].g, normals[i].b).magnitude, 5e-3f);
                }
        }

        [Test]
        public void FarVariantRefusesExistingPathAndUselessTarget()
        {
            VATProfile source = CreateSourceProfile();
            Assert.Throws<ArgumentException>(() => VatFarLodReducer.CreateFarVariant(source, TempFolder + "/Source.asset",
                new VatFarLodReducer.Settings { targetTriangles = 100 }, out _));
            Assert.Throws<ArgumentException>(() => VatFarLodReducer.CreateFarVariant(source, TempFolder + "/Big.asset",
                new VatFarLodReducer.Settings { targetTriangles = 100000 }, out _));
            Assert.IsNull(AssetDatabase.LoadMainAssetAtPath(TempFolder + "/Big.asset"));
        }

        // ------------------------------------------------------------ fixtures

        private static VATProfile CreateSourceProfile()
        {
            if (!AssetDatabase.IsValidFolder(TempFolder))
                AssetDatabase.CreateFolder("Assets", "VatFarLodReducerTestTemp");
            Sphere(16, 24, out Vector3[] positions, out Vector2[] uv, out int[] triangles);
            Mesh clean = NewMesh("Clean", positions, uv, triangles);
            Mesh low = NewMesh("Low", positions, uv, triangles);
            Vector3Int layout = VatBakeUtility.CalculateLayout(positions.Length, Frames);
            var pos = new Color[layout.x * layout.y];
            var nor = new Color[pos.Length];
            for (int f = 0; f < Frames; f++)
                for (int v = 0; v < positions.Length; v++)
                {
                    Vector3 p = positions[v];
                    p.x *= 1f + .1f * f;
                    p.y += .05f * f * p.x;
                    pos[Pixel(f, v, layout.x, layout.z)] = new Color(p.x, p.y, p.z, 1f);
                    Vector3 n = positions[v].normalized;
                    nor[Pixel(f, v, layout.x, layout.z)] = new Color(n.x, n.y, n.z, 1f);
                }
            var profile = ScriptableObject.CreateInstance<VATProfile>();
            profile.cleanMesh = clean;
            profile.positionTexture = NewTexture("FullPos", layout, pos);
            profile.normalTexture = NewTexture("FullNor", layout, nor);
            profile.lowLodMesh = low;
            profile.lowLodPositionTexture = NewTexture("LowPos", layout, pos);
            profile.lowLodNormalTexture = NewTexture("LowNor", layout, nor);
            profile.textureWidth = profile.lowLodTextureWidth = layout.x;
            profile.textureHeight = profile.lowLodTextureHeight = layout.y;
            profile.rowsPerFrame = profile.lowLodRowsPerFrame = layout.z;
            profile.totalFrameCount = Frames;
            profile.frameRate = 30;
            profile.idle = Window("Idle", 0, 2, true);
            profile.move = Window("Move", 2, 2, true);
            profile.attack = Window("Attack", 4, 2, false);
            profile.death = Window("Death", 6, 2, false);
            string path = TempFolder + "/Source.asset";
            AssetDatabase.CreateAsset(profile, path);
            foreach (UnityEngine.Object part in new UnityEngine.Object[] { clean, low, profile.positionTexture, profile.normalTexture,
                profile.lowLodPositionTexture, profile.lowLodNormalTexture })
                AssetDatabase.AddObjectToAsset(part, profile);
            AssetDatabase.SaveAssets();
            Assert.IsTrue(VatProfileValidation.TryValidate(profile, out string error), error);
            return profile;
        }

        private static VATProfile.VATClipWindow Window(string label, int start, int count, bool loop)
        {
            return new VATProfile.VATClipWindow { label = label, startFrame = start, frameCount = count, frameRate = 30, loop = loop };
        }

        private static Mesh NewMesh(string name, Vector3[] positions, Vector2[] uv, int[] triangles)
        {
            var mesh = new Mesh { name = name };
            mesh.vertices = positions;
            mesh.uv = uv;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 4f);
            return mesh;
        }

        private static Texture2D NewTexture(string name, Vector3Int layout, Color[] pixels)
        {
            var texture = new Texture2D(layout.x, layout.y, TextureFormat.RGBAHalf, false, true) { name = name, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            texture.SetPixels(pixels);
            texture.Apply(false, false);
            return texture;
        }

        private static int Pixel(int frame, int vertex, int width, int rows)
        {
            return (frame * rows + vertex / width) * width + vertex % width;
        }

        private static float[] Flatten(Vector3[] positions, float scale)
        {
            var o = new float[positions.Length * 3];
            for (int i = 0; i < positions.Length; i++) { o[3 * i] = positions[i].x * scale; o[3 * i + 1] = positions[i].y * scale; o[3 * i + 2] = positions[i].z * scale; }
            return o;
        }

        private static float[] FlattenUv(Vector2[] uv)
        {
            var o = new float[uv.Length * 2];
            for (int i = 0; i < uv.Length; i++) { o[2 * i] = uv[i].x; o[2 * i + 1] = uv[i].y; }
            return o;
        }

        /// <summary>UV sphere with a real seam column and per-segment pole vertices.</summary>
        private static void Sphere(int rings, int segments, out Vector3[] positions, out Vector2[] uv, out int[] triangles)
        {
            var p = new List<Vector3>();
            var t = new List<Vector2>();
            for (int r = 0; r <= rings; r++)
                for (int s = 0; s <= segments; s++)
                {
                    float th = Mathf.PI * r / rings, ph = 2 * Mathf.PI * s / segments;
                    p.Add(new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph)));
                    t.Add(new Vector2((float)s / segments, (float)r / rings));
                }
            var tri = new List<int>();
            int w = segments + 1;
            for (int r = 0; r < rings; r++)
                for (int s = 0; s < segments; s++)
                {
                    int a = r * w + s, b = a + 1, c = a + w, d = c + 1;
                    if (r != 0) tri.AddRange(new[] { a, b, c });
                    if (r != rings - 1) tri.AddRange(new[] { b, d, c });
                }
            positions = p.ToArray();
            uv = t.ToArray();
            triangles = tri.ToArray();
        }
    }
}
