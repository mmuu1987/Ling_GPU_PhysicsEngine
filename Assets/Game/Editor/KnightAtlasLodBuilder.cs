using System;
using System.Collections.Generic;
using System.Linq;
using MassEngine.Editor;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MassEngine.Game.Editor
{
    /// <summary>Character recipe only: the author's 4x4 palette must never be averaged across tile boundaries.
    /// Reuses the production Full VAT samples; clusters geometry and every animation frame consistently.
    /// Does not change the general engine reducer, accepted profiles or source meshes.</summary>
    internal static class KnightAtlasLodBuilder
    {
        private static int Tile(Vector2 uv) => Mathf.Clamp(Mathf.FloorToInt(uv.x * 4), 0, 3) +
            4 * Mathf.Clamp(Mathf.FloorToInt(uv.y * 4), 0, 3);

        public static VATProfile Create(VATProfile source, string path, int budget, string reportPath = null)
        {
            if (System.IO.File.Exists(path) || System.IO.File.Exists(path + ".meta") || AssetDatabase.LoadMainAssetAtPath(path) != null)
                throw new InvalidOperationException("New atlas LOD path required: " + path);
            if (!VatProfileValidation.TryValidate(source, out string error)) throw new InvalidOperationException(error);
            Vector3[] vertices = source.cleanMesh.vertices, normals = source.cleanMesh.normals;
            Vector2[] uvs = source.cleanMesh.uv;
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
                        Mathf.FloorToInt(q.z / Mathf.Max(.001f, bounds.size.z) * resolution), Tile(uvs[i]));
                    if (!map.TryGetValue(key, out var list)) map[key] = list = new List<int>();
                    list.Add(i);
                }
                return map.Values.ToList();
            };
            List<List<int>> clusters = null;
            // Bounded small source (6666 vertices); deterministic scan avoids assuming voxel count monotonicity.
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
                int tile = Tile(uvs[clusters[c][0]]);
                foreach (int i in clusters[c])
                {
                    if (Tile(uvs[i]) != tile) throw new InvalidOperationException("Cross-palette cluster.");
                    remap[i] = c; vout[c] += vertices[i]; nout[c] += normals[i]; uvout[c] += uvs[i];
                }
                vout[c] /= clusters[c].Count; nout[c].Normalize(); uvout[c] /= clusters[c].Count;
                if (Tile(uvout[c]) != tile) throw new InvalidOperationException("Averaged UV escaped palette tile.");
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
            var mesh = new Mesh { name = "Knight_PaletteSafe_Low", vertices = vout, normals = nout, uv = uvout, triangles = indices.ToArray() };
            mesh.RecalculateTangents(); mesh.bounds = source.cleanMesh.bounds;
            int width = Mathf.NextPowerOfTwo(count), height = source.totalFrameCount;
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
                    if (float.IsNaN(p.x) || float.IsNaN(p.y) || float.IsNaN(p.z) || p.magnitude > 10)
                        throw new InvalidOperationException("Invalid reduced VAT coordinate.");
                    pos[f * width + c] = new Color(p.x, p.y, p.z, 1); nor[f * width + c] = new Color(n.x, n.y, n.z, 1);
                }
            Texture2D Texture(Color[] pixels, string name)
            {
                var t = new Texture2D(width, height, TextureFormat.RGBAHalf, false, true)
                { name = name, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                t.SetPixels(pixels); t.Apply(false, false); return t;
            }
            var profile = Object.Instantiate(source); profile.name = "KnightPaletteSafeVAT";
            profile.lowLodMesh = mesh; profile.lowLodPositionTexture = Texture(pos, "KnightPaletteSafe_Positions");
            profile.lowLodNormalTexture = Texture(nor, "KnightPaletteSafe_Normals");
            profile.lowLodTextureWidth = width; profile.lowLodTextureHeight = height; profile.lowLodRowsPerFrame = 1;
            if (!VatProfileValidation.TryValidate(profile, out error)) throw new InvalidOperationException(error);
            AssetDatabase.CreateAsset(profile, path);
            AssetDatabase.AddObjectToAsset(mesh, profile); AssetDatabase.AddObjectToAsset(profile.lowLodPositionTexture, profile);
            AssetDatabase.AddObjectToAsset(profile.lowLodNormalTexture, profile); EditorUtility.SetDirty(profile); AssetDatabase.SaveAssetIfDirty(profile);
            System.IO.File.WriteAllText(reportPath ?? (WarSandboxCharacterPilotBuilder.Evidence + "/atlas-lod.txt"),
                "Palette-safe recipe-only reduction; budget=" + budget + "; vertices=" + count + "; triangles=" + indices.Count / 3 +
                "; frames=" + height + "; cross-palette clusters=0; nonfinite VAT samples=0; full samples shared unchanged.\n");
            return profile;
        }
    }
}
