using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MassEngine.Editor
{
    /// <summary>
    /// Creates a NEW far-LOD VAT profile whose far mesh is a multi-pose QEM simplification of the
    /// source profile's near cleanMesh (default, sourceSlot "full") or of its current far mesh ("far"). Unlike <see cref="VatLodReducer.CreateFarVariant"/> (grid
    /// clustering + per-cluster averaging), every far vertex is an original vertex and the far VAT
    /// textures are an exact column subset of the source far VAT: poses are copied, never blended.
    ///
    /// Slot pairing of the new profile (runtime: mid = midLod ?? lowLod, far = lowLod ?? midLod):
    ///   near: shared from source (cleanMesh + full VAT)
    ///   mid : the source's resolved MID data (unchanged on screen)
    ///   low : the new reduced far mesh + subset VAT (owned sub-assets of the new profile)
    /// The source profile and all its sub-assets are never modified; the output path must be new.
    /// </summary>
    public static class VatFarLodReducer
    {
        [Serializable]
        public sealed class Settings
        {
            public int targetTriangles = 200;
            /// <summary>Sampled frames per clip (idle/move/attack/death) for the error metric.</summary>
            public int samplesPerClip = 6;
            public bool preserveSeams = false;
            public float dropComponentAreaFraction = 0.002f;
            /// <summary>"full": simplify the near cleanMesh with the full VAT (artist topology, best quality; requires the far
            /// material to sample the same base texture as the cleanMesh UVs). "far": simplify the currently resolved far mesh.</summary>
            public string sourceSlot = "full";
            /// <summary>See VatQemSimplifier.Options.optimalPlacement. false keeps the far VAT an exact column subset.</summary>
            public bool optimalPlacement = false;
            /// <summary>See VatQemSimplifier.Options.splitChartCorners (fixes cross-UV-chart texture streaks).</summary>
            public bool splitChartCorners = true;
        }

        [Serializable]
        public sealed class Report
        {
            public string sourceProfile;
            public string outputProfile;
            public string sourceFarSlot;
            public string sourceFarMesh;
            public int sourceFarVertices;
            public int sourceFarTriangles;
            public int targetTriangles;
            public bool preserveSeams;
            public int resultVertices;
            public int resultTriangles;
            public bool reachedTarget;
            public int weldedNodes;
            public int droppedComponents;
            public int droppedComponentTriangles;
            public int strictCollapses;
            public int relaxedCollapses;
            public int sampledFrames;
            public int[] sampledFrameIndices;
            public float modelHeight;
            public float maxDeviation;
            public float meanDeviation;
            public float maxDeviationOfHeight;
            public float meanDeviationOfHeight;
            public int worstDeviationFrame;
            public string midMesh;
            public bool optimalPlacement;
            public int chartSplitVertices, crossChartTriangles;
            public double seconds;
        }

        private struct Slot
        {
            public string name;
            public Mesh mesh;
            public Texture2D positions, normals;
            public int width, height, rows;
        }

        public static VATProfile CreateFarVariant(VATProfile source, string assetPath, Settings settings, out Report report)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (settings.targetTriangles < 4) throw new ArgumentOutOfRangeException(nameof(settings), "targetTriangles must be >= 4.");
            if (settings.samplesPerClip < 2) throw new ArgumentOutOfRangeException(nameof(settings), "samplesPerClip must be >= 2.");
            VatBakeResult.ValidateNewPath(assetPath);
            if (!VatProfileValidation.TryValidate(source, out string error))
                throw new ArgumentException(error, nameof(source));

            if (settings.sourceSlot != "full" && settings.sourceSlot != "far")
                throw new ArgumentException("sourceSlot must be \"full\" or \"far\".", nameof(settings));
            Slot oldFar = ResolveFar(source);
            Slot far = settings.sourceSlot == "full" ? FullSlot(source) : oldFar;
            Slot mid = ResolveMid(source);
            if (far.mesh.subMeshCount != 1)
                throw new ArgumentException("Far mesh must have exactly one submesh (runtime draws submesh 0 only): " + far.mesh.name);
            if (!far.positions.isReadable || !far.normals.isReadable)
                throw new ArgumentException("Source far VAT textures must be readable: " + far.positions.name);
            int frameCount = source.totalFrameCount;
            if ((long)far.rows * frameCount > far.height)
                throw new ArgumentException("Source far VAT layout cannot hold totalFrameCount frames.");

            int vertexCount = far.mesh.vertexCount;
            int[] sourceTriangles = far.mesh.GetTriangles(0);
            if (sourceTriangles.Length / 3 <= settings.targetTriangles)
                throw new ArgumentException("Source far mesh already has " + sourceTriangles.Length / 3 + " triangles <= target " + settings.targetTriangles + ".");

            Color[] positionPixels = far.positions.GetPixels();
            Color[] normalPixels = far.normals.GetPixels();
            int[] frames = SampleFrames(source, settings.samplesPerClip);
            var poses = new float[frames.Length][];
            for (int i = 0; i < frames.Length; i++)
            {
                var pose = new float[vertexCount * 3];
                for (int v = 0; v < vertexCount; v++)
                {
                    Color p = positionPixels[Pixel(frames[i], v, far.width, far.rows)];
                    pose[3 * v] = p.r; pose[3 * v + 1] = p.g; pose[3 * v + 2] = p.b;
                }
                poses[i] = pose;
            }
            float[] uvs = null;
            var uvList = new List<Vector2>();
            far.mesh.GetUVs(0, uvList);
            if (uvList.Count == vertexCount)
            {
                uvs = new float[vertexCount * 2];
                for (int v = 0; v < vertexCount; v++) { uvs[2 * v] = uvList[v].x; uvs[2 * v + 1] = uvList[v].y; }
            }

            var options = new VatQemSimplifier.Options
            {
                targetTriangles = settings.targetTriangles,
                preserveSeams = settings.preserveSeams,
                dropComponentAreaFraction = settings.dropComponentAreaFraction,
                optimalPlacement = settings.optimalPlacement,
                splitChartCorners = settings.splitChartCorners
            };
            VatQemSimplifier.Result reduced = VatQemSimplifier.Simplify(vertexCount, poses, uvs, sourceTriangles, options);
            VatQemSimplifier.Deviation deviation = VatQemSimplifier.MeasureDeviation(poses, sourceTriangles, reduced);
            int[] kept = reduced.keptSourceVertices;
            if (kept.Length < 3 || reduced.triangles.Length < 3)
                throw new InvalidOperationException("Simplification produced an empty mesh.");

            float height = far.mesh.bounds.size.y;
            report = new Report
            {
                sourceProfile = AssetDatabase.GetAssetPath(source), outputProfile = assetPath,
                sourceFarSlot = far.name, sourceFarMesh = far.mesh.name,
                sourceFarVertices = vertexCount, sourceFarTriangles = sourceTriangles.Length / 3,
                targetTriangles = settings.targetTriangles, preserveSeams = settings.preserveSeams,
                resultVertices = kept.Length, resultTriangles = reduced.TriangleCount, reachedTarget = reduced.reachedTarget,
                weldedNodes = reduced.weldedNodes, droppedComponents = reduced.droppedComponents,
                droppedComponentTriangles = reduced.droppedComponentTriangles,
                strictCollapses = reduced.strictCollapses, relaxedCollapses = reduced.relaxedCollapses,
                sampledFrames = frames.Length, sampledFrameIndices = frames, modelHeight = height,
                maxDeviation = (float)deviation.max, meanDeviation = (float)deviation.mean,
                maxDeviationOfHeight = height > 0 ? (float)(deviation.max / height) : 0f,
                meanDeviationOfHeight = height > 0 ? (float)(deviation.mean / height) : 0f,
                worstDeviationFrame = frames[deviation.worstFrame], midMesh = mid.mesh.name,
                optimalPlacement = settings.optimalPlacement,
                chartSplitVertices = reduced.chartSplitVertices, crossChartTriangles = reduced.crossChartTriangles
            };

            using (var result = new VatBakeResult())
            {
                VATProfile profile = result.Profile;
                EditorUtility.CopySerialized(source, profile);
                profile.name = System.IO.Path.GetFileNameWithoutExtension(assetPath);
                // Mid keeps exactly what the source showed at mid distance.
                profile.midLodMesh = mid.mesh;
                profile.midLodPositionTexture = mid.positions;
                profile.midLodNormalTexture = mid.normals;
                profile.midLodTextureWidth = mid.width;
                profile.midLodTextureHeight = mid.height;
                profile.midLodRowsPerFrame = mid.rows;

                Bounds bounds = oldFar.mesh.bounds;
                bounds.Encapsulate(far.mesh.bounds);
                Mesh farMesh = result.Own(SubsetMesh(far.mesh, kept, reduced.keptUvSourceVertices ?? kept, reduced.triangles, bounds,
                    far.mesh.name + "_Qem" + settings.targetTriangles + (settings.preserveSeams ? "S" : "X") + (settings.optimalPlacement ? "O" : "") + (settings.splitChartCorners ? "" : "N")));
                Vector3Int layout = VatBakeUtility.CalculateLayout(kept.Length, frameCount);
                var farPositions = new Color[layout.x * layout.y];
                var farNormals = new Color[farPositions.Length];
                if (!settings.optimalPlacement)
                {
                    // Exact column subset: every far texel is a copied source texel.
                    for (int f = 0; f < frameCount; f++)
                        for (int k = 0; k < kept.Length; k++)
                        {
                            int from = Pixel(f, kept[k], far.width, far.rows);
                            int to = Pixel(f, k, layout.x, layout.z);
                            farPositions[to] = positionPixels[from];
                            farNormals[to] = normalPixels[from];
                        }
                }
                else
                {
                    var pose = new float[vertexCount * 3];
                    var normal = new float[vertexCount * 3];
                    for (int f = 0; f < frameCount; f++)
                    {
                        for (int v = 0; v < vertexCount; v++)
                        {
                            Color p = positionPixels[Pixel(f, v, far.width, far.rows)];
                            Color n = normalPixels[Pixel(f, v, far.width, far.rows)];
                            pose[3 * v] = p.r; pose[3 * v + 1] = p.g; pose[3 * v + 2] = p.b;
                            normal[3 * v] = n.r; normal[3 * v + 1] = n.g; normal[3 * v + 2] = n.b;
                        }
                        float[] placed = VatQemSimplifier.ComputeFramePositions(pose, sourceTriangles, reduced);
                        float[] normals = VatQemSimplifier.ComputeFrameNormals(normal, reduced);
                        for (int k = 0; k < kept.Length; k++)
                        {
                            int to = Pixel(f, k, layout.x, layout.z);
                            VatBakeUtility.CheckPosition(new Vector3(placed[3 * k], placed[3 * k + 1], placed[3 * k + 2]));
                            farPositions[to] = new Color(placed[3 * k], placed[3 * k + 1], placed[3 * k + 2], 1f);
                            farNormals[to] = new Color(normals[3 * k], normals[3 * k + 1], normals[3 * k + 2], 1f);
                        }
                    }
                }
                profile.lowLodMesh = farMesh;
                profile.lowLodPositionTexture = VatBakeUtility.CreateTexture(result, layout, farPositions, farMesh.name + "_Positions");
                profile.lowLodNormalTexture = VatBakeUtility.CreateTexture(result, layout, farNormals, farMesh.name + "_Normals");
                profile.lowLodTextureWidth = layout.x;
                profile.lowLodTextureHeight = layout.y;
                profile.lowLodRowsPerFrame = layout.z;

                if (!VatProfileValidation.TryValidate(profile, out error))
                    throw new InvalidOperationException(error);
                try
                {
                    AssetDatabase.CreateAsset(profile, assetPath);
                    AssetDatabase.AddObjectToAsset(profile.lowLodMesh, profile);
                    AssetDatabase.AddObjectToAsset(profile.lowLodPositionTexture, profile);
                    AssetDatabase.AddObjectToAsset(profile.lowLodNormalTexture, profile);
                    EditorUtility.SetDirty(profile);
                    AssetDatabase.SaveAssetIfDirty(profile);
                    timer.Stop();
                    report.seconds = timer.Elapsed.TotalSeconds;
                    return profile;
                }
                catch
                {
                    if (AssetDatabase.LoadMainAssetAtPath(assetPath) == profile)
                        AssetDatabase.DeleteAsset(assetPath);
                    throw;
                }
            }
        }

        /// <summary>Absolute frames: samplesPerClip evenly spaced samples (both ends included) per clip.</summary>
        public static int[] SampleFrames(VATProfile profile, int samplesPerClip)
        {
            var set = new SortedSet<int>();
            foreach (VATProfile.VATClipWindow window in new[] { profile.idle, profile.move, profile.attack, profile.death })
            {
                int last = Mathf.Max(0, window.frameCount - 1);
                int n = Mathf.Min(samplesPerClip, window.frameCount);
                for (int i = 0; i < n; i++)
                    set.Add(window.startFrame + (n == 1 ? 0 : Mathf.RoundToInt(i * last / (float)(n - 1))));
            }
            var frames = new int[set.Count];
            set.CopyTo(frames);
            return frames;
        }

        private static int Pixel(int frame, int vertex, int width, int rows)
        {
            return (frame * rows + vertex / width) * width + vertex % width;
        }

        private static Slot ResolveFar(VATProfile p)
        {
            if (p.HasLowLod) return LowSlot(p);
            if (p.HasMidLod) return MidSlot(p);
            return FullSlot(p);
        }

        private static Slot ResolveMid(VATProfile p)
        {
            if (p.HasMidLod) return MidSlot(p);
            if (p.HasLowLod) return LowSlot(p);
            return FullSlot(p);
        }

        private static Slot LowSlot(VATProfile p)
        {
            return new Slot { name = "low", mesh = p.lowLodMesh, positions = p.lowLodPositionTexture, normals = p.lowLodNormalTexture,
                width = p.lowLodTextureWidth, height = p.lowLodTextureHeight, rows = p.lowLodRowsPerFrame };
        }

        private static Slot MidSlot(VATProfile p)
        {
            return new Slot { name = "mid", mesh = p.midLodMesh, positions = p.midLodPositionTexture, normals = p.midLodNormalTexture,
                width = p.midLodTextureWidth, height = p.midLodTextureHeight, rows = p.midLodRowsPerFrame };
        }

        private static Slot FullSlot(VATProfile p)
        {
            return new Slot { name = "full", mesh = p.cleanMesh, positions = p.positionTexture, normals = p.normalTexture,
                width = p.textureWidth, height = p.textureHeight, rows = p.rowsPerFrame };
        }

        /// <summary>Copies every present vertex attribute for the kept vertices; keeps the source bounds
        /// (VAT meshes carry animation-expanded bounds used for culling).</summary>
        /// <summary>kept: position source per new vertex (positions, normals, VAT column, uv1+). uvKept: appearance source
        /// (uv0, tangent, color) - differs from kept only for chart split vertices.</summary>
        private static Mesh SubsetMesh(Mesh source, int[] kept, int[] uvKept, int[] triangles, Bounds bounds, string name)
        {
            var mesh = new Mesh { name = name, indexFormat = kept.Length > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(Pick(source.vertices, kept));
            if (source.HasVertexAttribute(VertexAttribute.Normal)) mesh.SetNormals(Pick(source.normals, kept));
            if (source.HasVertexAttribute(VertexAttribute.Tangent)) mesh.SetTangents(Pick(source.tangents, uvKept));
            if (source.HasVertexAttribute(VertexAttribute.Color)) mesh.SetColors(Pick(source.colors32, uvKept));
            for (int channel = 0; channel < 8; channel++)
            {
                VertexAttribute attribute = VertexAttribute.TexCoord0 + channel;
                if (!source.HasVertexAttribute(attribute)) continue;
                int dimension = source.GetVertexAttributeDimension(attribute);
                int[] pick = channel == 0 ? uvKept : kept;
                if (dimension <= 2)
                {
                    var uv = new List<Vector2>();
                    source.GetUVs(channel, uv);
                    mesh.SetUVs(channel, PickList(uv, pick));
                }
                else if (dimension == 3)
                {
                    var uv = new List<Vector3>();
                    source.GetUVs(channel, uv);
                    mesh.SetUVs(channel, PickList(uv, pick));
                }
                else
                {
                    var uv = new List<Vector4>();
                    source.GetUVs(channel, uv);
                    mesh.SetUVs(channel, PickList(uv, pick));
                }
            }
            mesh.SetTriangles(triangles, 0, false);
            mesh.bounds = bounds;
            return mesh;
        }

        private static List<T> PickList<T>(List<T> values, int[] kept)
        {
            var list = new List<T>(kept.Length);
            foreach (int k in kept) list.Add(values[k]);
            return list;
        }

        private static List<T> Pick<T>(T[] values, int[] kept)
        {
            var list = new List<T>(kept.Length);
            foreach (int k in kept) list.Add(values[k]);
            return list;
        }
    }
}
