using System;
using System.Collections.Generic;
using System.Linq;
using MassEngine.Editor;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MassEngine.Game.Editor
{
    /// <summary>Opt-in grid-palette reducer. Never used implicitly for arbitrary textured models.</summary>
    internal static class CharacterPaletteLod
    {
        private static int Tile(Vector2 uv, int columns, int rows) => Mathf.Clamp(Mathf.FloorToInt(uv.x * columns), 0, columns-1) +
            columns * Mathf.Clamp(Mathf.FloorToInt(uv.y * rows), 0, rows-1);

        public static VATProfile Create(VATProfile source, string path, int budget, int columns, int rows, string reportPath)
        {
            if (System.IO.File.Exists(path) || System.IO.File.Exists(path + ".meta") || AssetDatabase.LoadMainAssetAtPath(path) != null)
                throw new InvalidOperationException("New atlas LOD path required: " + path);
            if (!VatProfileValidation.TryValidate(source, out string error)) throw new InvalidOperationException(error);
            CharacterGeometry.Require(columns>=1 && columns<=32 && rows>=1 && rows<=32 && budget>=8 && budget<source.cleanMesh.vertexCount, "Invalid palette dimensions or vertex budget.");
            Vector3[] vertices = source.cleanMesh.vertices, normals = source.cleanMesh.normals;
            Vector2[] uvs = source.cleanMesh.uv;
            foreach(var uv in uvs) CharacterGeometry.Require(CharacterGeometry.Finite(uv.x) && CharacterGeometry.Finite(uv.y) && uv.x>=0 && uv.x<=1 && uv.y>=0 && uv.y<=1,"Palette-grid UV must be finite and within [0,1].");
            Bounds bounds = new Bounds(vertices[0], Vector3.zero);
            foreach (var v in vertices) bounds.Encapsulate(v);
            Func<int, List<List<int>>> group = resolution =>
            {
                var map = new Dictionary<(int, int, int, int), List<int>>();
                for (int i = 0; i < vertices.Length; i++)
                {
                    Vector3 q = vertices[i] - bounds.min;
                    var key = (Mathf.FloorToInt(q.x / Mathf.Max(.001f, bounds.size.x) * resolution),
                        Mathf.FloorToInt(q.y / Mathf.Max(.001f, bounds.size.y) * resolution),
                        Mathf.FloorToInt(q.z / Mathf.Max(.001f, bounds.size.z) * resolution), Tile(uvs[i], columns, rows));
                    if (!map.TryGetValue(key, out var list)) map[key] = list = new List<int>();
                    list.Add(i);
                }
                return map.Values.ToList();
            };
            List<List<int>> clusters = null;
            // Bounded by the recipe preflight vertex budget; deterministic scan avoids assuming voxel count monotonicity.
            for (int r = 1; r <= 80; r++)
            {
                var candidate = group(r);
                if (candidate.Count <= budget && (clusters == null || candidate.Count > clusters.Count)) clusters = candidate;
            }
            if (clusters == null || clusters.Count < 8) throw new InvalidOperationException("No usable palette-preserving LOD.");
            int count = clusters.Count;
            var remap = new int[vertices.Length]; var vout = new Vector3[count]; var nout = new Vector3[count]; var uvout = new Vector2[count];
            for (int c = 0; c < count; c++)
            {
                int tile = Tile(uvs[clusters[c][0]], columns, rows);
                foreach (int i in clusters[c])
                {
                    if (Tile(uvs[i], columns, rows) != tile) throw new InvalidOperationException("Cross-palette cluster.");
                    remap[i] = c; vout[c] += vertices[i]; nout[c] += normals[i]; uvout[c] += uvs[i];
                }
                vout[c] /= clusters[c].Count; nout[c].Normalize(); uvout[c] /= clusters[c].Count;
                if (Tile(uvout[c], columns, rows) != tile) throw new InvalidOperationException("Averaged UV escaped palette tile.");
            }
            var indices = new List<int>(); var seen = new HashSet<(int, int, int)>();
            int[] originalIndices = source.cleanMesh.triangles;
            for (int i = 0; i < originalIndices.Length; i += 3)
            {
                int a = remap[originalIndices[i]], b = remap[originalIndices[i + 1]], c = remap[originalIndices[i + 2]];
                if (a == b || b == c || c == a) continue;
                int lo = Math.Min(a, Math.Min(b, c)), hi = Math.Max(a, Math.Max(b, c));
                if (!seen.Add((lo, a + b + c - lo - hi, hi))) continue;
                indices.Add(a); indices.Add(b); indices.Add(c);
            }
            if (indices.Count == 0) throw new InvalidOperationException("Empty palette LOD topology.");
            var mesh = new Mesh { name = source.name + "_PaletteLow", vertices = vout, normals = nout, uv = uvout, triangles = indices.ToArray() };
            mesh.RecalculateTangents(); mesh.bounds = source.cleanMesh.bounds;
            int width = Mathf.NextPowerOfTwo(count), height = source.totalFrameCount;
            CharacterGeometry.Require(width<=16384 && height<=16384, "Palette texture dimensions exceed GPU limit.");
            Color[] fullP = source.positionTexture.GetPixels(), fullN = source.normalTexture.GetPixels();
            var pos = new Color[width * height]; var nor = new Color[pos.Length];
            for (int f = 0; f < height; f++)
                for (int c = 0; c < count; c++)
                {
                    Vector3 p = Vector3.zero, n = Vector3.zero;
                    foreach (int old in clusters[c])
                    {
                        int index = f * source.rowsPerFrame * source.textureWidth + old;
                        Color a = fullP[index], b = fullN[index];
                        p += new Vector3(a.r, a.g, a.b); n += new Vector3(b.r, b.g, b.b);
                    }
                    p /= clusters[c].Count; n = n.sqrMagnitude > 1e-6f ? n.normalized : Vector3.up;
                    if (!CharacterGeometry.Finite(p) || !CharacterGeometry.Finite(n) || Mathf.Max(Mathf.Abs(p.x), Mathf.Abs(p.y), Mathf.Abs(p.z)) > 65504)
                        throw new InvalidOperationException("Invalid reduced VAT coordinate.");
                    pos[f * width + c] = new Color(p.x, p.y, p.z, 1); nor[f * width + c] = new Color(n.x, n.y, n.z, 1);
                }
            Texture2D Texture(Color[] pixels, string name)
            {
                var t = new Texture2D(width, height, TextureFormat.RGBAHalf, false, true)
                { name = name, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                t.SetPixels(pixels); t.Apply(false, false); return t;
            }
            var profile = Object.Instantiate(source); profile.name = System.IO.Path.GetFileNameWithoutExtension(path);
            profile.lowLodMesh = mesh; profile.lowLodPositionTexture = Texture(pos, "PaletteLowPositions");
            profile.lowLodNormalTexture = Texture(nor, "PaletteLowNormals");
            profile.lowLodTextureWidth = width; profile.lowLodTextureHeight = height; profile.lowLodRowsPerFrame = 1;
            if (!VatProfileValidation.TryValidate(profile, out error)) throw new InvalidOperationException(error);
            AssetDatabase.CreateAsset(profile, path);
            AssetDatabase.AddObjectToAsset(mesh, profile); AssetDatabase.AddObjectToAsset(profile.lowLodPositionTexture, profile);
            AssetDatabase.AddObjectToAsset(profile.lowLodNormalTexture, profile); EditorUtility.SetDirty(profile); AssetDatabase.SaveAssetIfDirty(profile);
            System.IO.File.WriteAllText(reportPath,
                "Palette-safe recipe-only reduction; budget=" + budget + "; vertices=" + count + "; triangles=" + indices.Count / 3 +
                "; frames=" + height + "; cross-palette clusters=0; nonfinite VAT samples=0; full samples shared unchanged.\n");
            return profile;
        }
    }
}
