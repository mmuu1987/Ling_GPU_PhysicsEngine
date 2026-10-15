using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace MassEngine.Editor
{
    /// <summary>
    /// Vertex AO baker for VAT profiles (menu MassEngine/顶点AO). For every LOD mesh the runtime draws (same pairing as
    /// ResolvedUnitTypeRuntime), it reconstructs real animated poses from the VAT position/normal textures (idle, move,
    /// attack; never the bind pose, so arms-down armpits and legs are right), ray-casts a cosine hemisphere per vertex
    /// against the posed mesh plus a ground plane, averages over poses, and writes the result to uv3.x of a NEW mesh copy.
    /// Mid/far meshes take the near mesh's AO by nearest-vertex transfer in the same poses (decimated meshes are too coarse
    /// to ray-cast). Only new assets are written (in the output folder) plus the library entry; profiles, textures and the
    /// source meshes are untouched. Re-baking overwrites this tool's own copies in place (GUIDs kept).
    /// </summary>
    public static class VertexAoBaker
    {
        public sealed class Settings
        {
            // 2026-10-13 v2 ("有点脏"): 192 rays, 10% radius, floor 0.6, 4 smoothing passes - fewer speckles, only real creases darken.
            public int rays = 192;                // per vertex per pose
            public float radiusFraction = 0.10f;  // ray length as a fraction of the model height
            public float biasFraction = 0.0025f;  // ray start offset along the normal
            public bool groundPlane = true;       // feet / lower legs darken against the ground
            public float groundWeight = 0.7f;
            public int posesPerClip = 3;          // idle, move, attack
            public float contrast = 1.0f;         // ao = pow(ao, contrast)
            public float minAo = 0.6f;
            public int smoothIterations = 4;
            public int transferNeighbours = 4;
        }

        public sealed class Report
        {
            public readonly StringBuilder text = new StringBuilder();
            public bool ok;
        }

        private struct LodSet
        {
            public string lod;
            public Mesh mesh;
            public Texture pos, norm;
            public int width, height, rowsPerFrame;
        }

        // ---------------------------------------------------------------- menu

        private const string EnablePath = "MassEngine/顶点AO/运行时启用顶点AO（下次进入战斗生效）";

        [MenuItem("MassEngine/顶点AO/烘焙选中的 VAT Profile", false, 41)]
        private static void BakeSelected()
        {
            foreach (var o in Selection.objects)
            {
                if (!(o is ScriptableObject so)) continue;
                string path = AssetDatabase.GetAssetPath(so);
                string folder = "Assets/Game/Content/VertexAO/" + Path.GetFileNameWithoutExtension(path);
                var r = Bake(so, folder, "Assets/Game/Resources/" + VertexAoLibrary.ResourcePath + ".asset", new Settings(), null);
                Debug.Log(r.text.ToString());
            }
        }

        [MenuItem(EnablePath, false, 60)]
        private static void ToggleEnabled()
        {
            VertexAoLibrary.Enabled = !VertexAoLibrary.Enabled;
            Menu.SetChecked(EnablePath, VertexAoLibrary.Enabled);
            Debug.Log("[VertexAO] 运行时顶点AO：" + (VertexAoLibrary.Enabled ? "开" : "关") + "（下次进入战斗生效）");
        }

        [MenuItem(EnablePath, true)]
        private static bool ToggleEnabledValidate() { Menu.SetChecked(EnablePath, VertexAoLibrary.Enabled); return true; }

        // ---------------------------------------------------------------- bake

        /// <summary>Bakes every distinct LOD mesh of <paramref name="profile"/>. <paramref name="dumpPath"/> (optional, outside
        /// Assets) receives the near mesh in its first idle pose with AO, for offline inspection.</summary>
        public static Report Bake(ScriptableObject profile, string outputFolder, string libraryPath, Settings s, string dumpPath)
        {
            var report = new Report();
            var log = report.text;
            log.AppendLine("[VertexAO] bake " + (profile != null ? profile.name : "null"));
            if (!VatProfileReader.TryRead(profile, out VatProfileData p, out string error)) { log.AppendLine("  FAIL profile: " + error); return report; }

            var near = new LodSet { lod = "near", mesh = p.cleanMesh, pos = p.positionTexture, norm = p.normalTexture, width = p.textureWidth, height = p.textureHeight, rowsPerFrame = p.rowsPerFrame };
            LodSet mid = p.hasMidLod ? Mid(p) : p.hasLowLod ? Low(p) : near; mid.lod = "mid";
            LodSet far = p.hasLowLod ? Low(p) : p.hasMidLod ? Mid(p) : near; far.lod = "far";

            var frames = new List<int>();
            foreach (var clip in new[] { p.idle, p.move, p.attack })
                for (int i = 0; i < s.posesPerClip; i++)
                {
                    int f = clip.startFrame + Mathf.FloorToInt(i * Mathf.Max(1, clip.frameCount) / (float)s.posesPerClip);
                    f = Mathf.Clamp(f, 0, p.totalFrameCount - 1);
                    if (!frames.Contains(f)) frames.Add(f);
                }
            log.AppendLine("  poses (frames): " + string.Join(",", frames));

            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                // ---- near: ray-cast AO per pose ----
                int n = near.mesh.vertexCount;
                int[] tris = AllTriangles(near.mesh);
                var nearPoses = new List<Vector3[]>();
                var aoSum = new float[n];
                for (int fi = 0; fi < frames.Count; fi++)
                {
                    if (EditorUtility.DisplayCancelableProgressBar("顶点AO", "near pose " + (fi + 1) + "/" + frames.Count, fi / (float)frames.Count))
                    { log.AppendLine("  cancelled"); return report; }
                    if (!ReadPose(near, frames[fi], n, out var pos, out var nor, out string perr)) { log.AppendLine("  FAIL near pose: " + perr); return report; }
                    if (fi == 0 && !CheckPose(near.mesh, tris, pos, log)) return report;
                    nearPoses.Add(pos);
                    var ao = RayAo(pos, nor, tris, s);
                    for (int i = 0; i < n; i++) aoSum[i] += ao[i];
                }
                var nearAo = new float[n];
                for (int i = 0; i < n; i++) nearAo[i] = aoSum[i] / frames.Count;
                Weld(near.mesh, nearAo);
                Smooth(near.mesh, tris, nearAo, s.smoothIterations);
                for (int i = 0; i < n; i++) nearAo[i] = Mathf.Max(s.minAo, Mathf.Pow(Mathf.Clamp01(nearAo[i]), s.contrast));

                var results = new List<(LodSet set, float[] ao)> { (near, nearAo) };
                foreach (var set in new[] { mid, far })
                {
                    if (set.mesh == null || results.Exists(r => r.set.mesh == set.mesh)) continue;
                    EditorUtility.DisplayProgressBar("顶点AO", set.lod + " transfer", 0.9f);
                    int m = set.mesh.vertexCount;
                    var sum = new float[m];
                    for (int fi = 0; fi < frames.Count; fi++)
                    {
                        if (!ReadPose(set, frames[fi], m, out var pos, out _, out string perr)) { log.AppendLine("  FAIL " + set.lod + " pose: " + perr); return report; }
                        var t = Transfer(pos, nearPoses[fi], nearAo, s.transferNeighbours);
                        for (int i = 0; i < m; i++) sum[i] += t[i];
                    }
                    for (int i = 0; i < m; i++) sum[i] /= frames.Count;
                    results.Add((set, sum));
                }

                // ---- write copies + library ----
                EnsureFolder(outputFolder);
                var lib = LoadOrCreateLibrary(libraryPath);
                foreach (var (set, ao) in results)
                {
                    var copy = WriteCopy(set.mesh, ao, outputFolder);
                    Upsert(lib, set.mesh, copy, profile.name + " / " + set.lod);
                    float mn = 1, mx = 0, mean = 0;
                    foreach (var a in ao) { mn = Mathf.Min(mn, a); mx = Mathf.Max(mx, a); mean += a; }
                    mean /= Mathf.Max(1, ao.Length);
                    log.AppendLine("  " + set.lod + ": " + set.mesh.name + " v=" + set.mesh.vertexCount + " -> " + AssetDatabase.GetAssetPath(copy) +
                                   "  ao min/mean/max " + mn.ToString("0.00") + "/" + mean.ToString("0.00") + "/" + mx.ToString("0.00"));
                }
                EditorUtility.SetDirty(lib);
                AssetDatabase.SaveAssetIfDirty(lib);
                VertexAoLibrary.ResetCache();
                if (!string.IsNullOrEmpty(dumpPath)) Dump(dumpPath, nearPoses[0], tris, nearAo);
                report.ok = true;
                log.AppendLine("  done in " + sw.Elapsed.TotalSeconds.ToString("0.0") + " s");
            }
            catch (Exception e) { log.AppendLine("  EXCEPTION " + e); }
            finally { EditorUtility.ClearProgressBar(); }
            return report;
        }

        private static LodSet Mid(VatProfileData p) => new LodSet { mesh = p.midLodMesh, pos = p.midLodPositionTexture, norm = p.midLodNormalTexture, width = p.midLodTextureWidth, height = p.midLodTextureHeight, rowsPerFrame = p.midLodRowsPerFrame };
        private static LodSet Low(VatProfileData p) => new LodSet { mesh = p.lowLodMesh, pos = p.lowLodPositionTexture, norm = p.lowLodNormalTexture, width = p.lowLodTextureWidth, height = p.lowLodTextureHeight, rowsPerFrame = p.lowLodRowsPerFrame };

        private static int[] AllTriangles(Mesh mesh)
        {
            var list = new List<int>();
            for (int sm = 0; sm < mesh.subMeshCount; sm++)
                if (mesh.GetTopology(sm) == MeshTopology.Triangles) list.AddRange(mesh.GetTriangles(sm));
            return list.ToArray();
        }

        // ---------------------------------------------------------------- VAT readback

        /// <summary>Positions/normals of the first <paramref name="count"/> vertices at <paramref name="frame"/>, decoded with the
        /// shader's layout: x = id % width, y = frame * rowsPerFrame + id / width.</summary>
        private static bool ReadPose(LodSet set, int frame, int count, out Vector3[] pos, out Vector3[] nor, out string error)
        {
            pos = nor = null; error = null;
            if (set.pos == null || set.norm == null || set.width <= 0 || set.rowsPerFrame <= 0) { error = "missing textures/layout"; return false; }
            if ((long)set.width * set.rowsPerFrame < count) { error = "layout smaller than vertex count"; return false; }
            int y0 = frame * set.rowsPerFrame;
            if (y0 + set.rowsPerFrame > set.pos.height) { error = "frame outside texture"; return false; }
            var pc = ReadRows(set.pos, y0, set.rowsPerFrame);
            var nc = ReadRows(set.norm, y0, set.rowsPerFrame);
            if (pc == null || nc == null) { error = "texture readback failed"; return false; }
            pos = new Vector3[count]; nor = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                int idx = (i / set.width) * set.width + (i % set.width); // row-major, rows from y0 upward
                var c = pc[idx]; pos[i] = new Vector3(c.r, c.g, c.b);
                var d = nc[idx]; var v = new Vector3(d.r, d.g, d.b); nor[i] = v.sqrMagnitude > 1e-12f ? v.normalized : Vector3.up;
            }
            return true;
        }

        private static Color[] ReadRows(Texture tex, int y0, int rows)
        {
            int w = tex.width;
            if (tex is Texture2D t2 && t2.isReadable) return t2.GetPixels(0, y0, w, rows);
            var rt = RenderTexture.GetTemporary(w, tex.height, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            var prev = RenderTexture.active;
            try
            {
                rt.filterMode = FilterMode.Point;
                Graphics.Blit(tex, rt);
                RenderTexture.active = rt;
                var tmp = new Texture2D(w, rows, TextureFormat.RGBAHalf, false, true);
                tmp.ReadPixels(new Rect(0, y0, w, rows), 0, 0, false);
                tmp.Apply(false);
                var px = tmp.GetPixels();
                UnityEngine.Object.DestroyImmediate(tmp);
                return px;
            }
            finally { RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt); }
        }

        /// <summary>Guard against a wrong row order: in a correctly decoded pose, triangle edges are about as long as in the
        /// bind pose; a scrambled readback gives edges the size of the whole model.</summary>
        private static bool CheckPose(Mesh mesh, int[] tris, Vector3[] pose, StringBuilder log)
        {
            var bind = mesh.vertices;
            var a = new List<float>(); var b = new List<float>();
            for (int t = 0; t + 2 < tris.Length; t += 3 * Mathf.Max(1, tris.Length / 3000 / 3))
            {
                a.Add((bind[tris[t]] - bind[tris[t + 1]]).magnitude);
                b.Add((pose[tris[t]] - pose[tris[t + 1]]).magnitude);
            }
            a.Sort(); b.Sort();
            float ma = a.Count > 0 ? a[a.Count / 2] : 0, mb = b.Count > 0 ? b[b.Count / 2] : 0;
            float ratio = ma > 1e-6f ? mb / ma : 0;
            log.AppendLine("  pose check: median edge bind " + ma.ToString("0.0000") + " / pose " + mb.ToString("0.0000") + " (ratio " + ratio.ToString("0.00") + ")");
            if (ratio < 0.5f || ratio > 2f) { log.AppendLine("  FAIL pose check: VAT readback does not match the mesh"); return false; }
            return true;
        }

        // ---------------------------------------------------------------- AO

        private static float[] RayAo(Vector3[] pos, Vector3[] nor, int[] tris, Settings s)
        {
            int n = pos.Length;
            var bvh = new Bvh(pos, tris);
            Vector3 mn = pos[0], mx = pos[0];
            foreach (var v in pos) { mn = Vector3.Min(mn, v); mx = Vector3.Max(mx, v); }
            float height = Mathf.Max(0.01f, mx.y - mn.y);
            float R = s.radiusFraction * height, bias = s.biasFraction * height;
            float groundY = Mathf.Abs(mn.y) < 0.1f * height ? Mathf.Min(0f, mn.y) : mn.y;
            // Hammersley cosine-weighted hemisphere (z up), rotated per vertex.
            int K = Mathf.Max(8, s.rays);
            var dirs = new Vector3[K];
            for (int k = 0; k < K; k++)
            {
                float u = (k + 0.5f) / K, v = RadicalInverse((uint)k);
                float r = Mathf.Sqrt(u), phi = 2 * Mathf.PI * v;
                dirs[k] = new Vector3(r * Mathf.Cos(phi), r * Mathf.Sin(phi), Mathf.Sqrt(Mathf.Max(0, 1 - u)));
            }
            var ao = new float[n];
            Parallel.For(0, n, i =>
            {
                Vector3 nn = nor[i];
                Vector3 t = Mathf.Abs(nn.y) < 0.99f ? Vector3.Cross(nn, Vector3.up).normalized : Vector3.Cross(nn, Vector3.right).normalized;
                Vector3 bt = Vector3.Cross(nn, t);
                float rot = Hash(i) * 2 * Mathf.PI, cr = Mathf.Cos(rot), sr = Mathf.Sin(rot);
                Vector3 o = pos[i] + nn * bias;
                float occ = 0;
                for (int k = 0; k < K; k++)
                {
                    var d0 = dirs[k];
                    float x = d0.x * cr - d0.y * sr, y = d0.x * sr + d0.y * cr;
                    Vector3 d = t * x + bt * y + nn * d0.z;
                    float hit = bvh.Raycast(o, d, R, i);
                    float w = hit < R ? 1 - (hit / R) * (hit / R) : 0;
                    if (s.groundPlane && d.y < -1e-4f)
                    {
                        float tg = (groundY - o.y) / d.y;
                        if (tg > 0 && tg < R && tg < hit) w = s.groundWeight * (1 - (tg / R) * (tg / R));
                    }
                    occ += w;
                }
                ao[i] = 1 - occ / K;
            });
            return ao;
        }

        /// <summary>
        /// Per-face AO for static meshes (procedural props): rays from each triangle centroid along its face normal
        /// hemisphere, same BVH / ground plane / falloff as the VAT bake; result already has contrast + minAo applied.
        /// </summary>
        public static float[] ComputeFaceAo(Vector3[] verts, int[] tris, Settings s)
        {
            int fc = tris.Length / 3;
            var ao = new float[fc];
            if (fc == 0) return ao;
            var bvh = new Bvh(verts, tris);
            Vector3 mn = verts[0], mx = verts[0];
            foreach (var v in verts) { mn = Vector3.Min(mn, v); mx = Vector3.Max(mx, v); }
            float height = Mathf.Max(0.01f, mx.y - mn.y);
            float R = s.radiusFraction * height, bias = s.biasFraction * height, groundY = mn.y;
            int K = Mathf.Max(8, s.rays);
            var dirs = new Vector3[K];
            for (int k = 0; k < K; k++)
            {
                float u = (k + 0.5f) / K, v = RadicalInverse((uint)k);
                float r = Mathf.Sqrt(u), phi = 2 * Mathf.PI * v;
                dirs[k] = new Vector3(r * Mathf.Cos(phi), r * Mathf.Sin(phi), Mathf.Sqrt(Mathf.Max(0, 1 - u)));
            }
            Parallel.For(0, fc, f =>
            {
                Vector3 a = verts[tris[f * 3]], b = verts[tris[f * 3 + 1]], c = verts[tris[f * 3 + 2]];
                Vector3 nn = Vector3.Cross(b - a, c - a);
                if (nn.sqrMagnitude < 1e-12f) { ao[f] = 1; return; }
                nn.Normalize();
                Vector3 t = Mathf.Abs(nn.y) < 0.99f ? Vector3.Cross(nn, Vector3.up).normalized : Vector3.Cross(nn, Vector3.right).normalized;
                Vector3 bt = Vector3.Cross(nn, t);
                float rot = Hash(f) * 2 * Mathf.PI, cr = Mathf.Cos(rot), sr = Mathf.Sin(rot);
                Vector3 o = (a + b + c) / 3f + nn * bias;
                float occ = 0;
                for (int k = 0; k < K; k++)
                {
                    var d0 = dirs[k];
                    float x = d0.x * cr - d0.y * sr, y = d0.x * sr + d0.y * cr;
                    Vector3 d = t * x + bt * y + nn * d0.z;
                    float hit = bvh.Raycast(o, d, R, -1);
                    float w = hit < R ? 1 - (hit / R) * (hit / R) : 0;
                    if (s.groundPlane && d.y < -1e-4f)
                    {
                        float tg = (groundY - o.y) / d.y;
                        if (tg > 0 && tg < R && tg < hit) w = s.groundWeight * (1 - (tg / R) * (tg / R));
                    }
                    occ += w;
                }
                ao[f] = Mathf.Max(s.minAo, Mathf.Pow(Mathf.Clamp01(1 - occ / K), s.contrast));
            });
            return ao;
        }

        private static float RadicalInverse(uint b)
        {
            b = (b << 16) | (b >> 16);
            b = ((b & 0x55555555u) << 1) | ((b & 0xAAAAAAAAu) >> 1);
            b = ((b & 0x33333333u) << 2) | ((b & 0xCCCCCCCCu) >> 2);
            b = ((b & 0x0F0F0F0Fu) << 4) | ((b & 0xF0F0F0F0u) >> 4);
            b = ((b & 0x00FF00FFu) << 8) | ((b & 0xFF00FF00u) >> 8);
            return b * 2.3283064365386963e-10f;
        }

        private static float Hash(int i)
        {
            uint x = (uint)i * 747796405u + 2891336453u;
            x = ((x >> (int)((x >> 28) + 4u)) ^ x) * 277803737u;
            x = (x >> 22) ^ x;
            return x / 4294967296f;
        }

        /// <summary>Split vertices at UV/normal seams share a position in the bind pose; give them one AO so seams do not show.</summary>
        private static void Weld(Mesh mesh, float[] ao)
        {
            var v = mesh.vertices;
            float q = Mathf.Max(1e-5f, mesh.bounds.size.magnitude * 1e-5f);
            var groups = new Dictionary<Vector3Int, List<int>>();
            for (int i = 0; i < v.Length; i++)
            {
                var key = new Vector3Int(Mathf.RoundToInt(v[i].x / q), Mathf.RoundToInt(v[i].y / q), Mathf.RoundToInt(v[i].z / q));
                if (!groups.TryGetValue(key, out var g)) groups[key] = g = new List<int>();
                g.Add(i);
            }
            foreach (var g in groups.Values)
            {
                if (g.Count < 2) continue;
                float sum = 0; foreach (int i in g) sum += ao[i];
                sum /= g.Count; foreach (int i in g) ao[i] = sum;
            }
        }

        private static void Smooth(Mesh mesh, int[] tris, float[] ao, int iterations)
        {
            int n = ao.Length;
            var nb = new List<int>[n];
            for (int i = 0; i < n; i++) nb[i] = new List<int>(6);
            for (int t = 0; t + 2 < tris.Length; t += 3)
                for (int e = 0; e < 3; e++)
                {
                    int a = tris[t + e], b = tris[t + (e + 1) % 3];
                    if (!nb[a].Contains(b)) nb[a].Add(b);
                    if (!nb[b].Contains(a)) nb[b].Add(a);
                }
            var tmp = new float[n];
            for (int it = 0; it < iterations; it++)
            {
                for (int i = 0; i < n; i++)
                {
                    if (nb[i].Count == 0) { tmp[i] = ao[i]; continue; }
                    float s = 0; foreach (int j in nb[i]) s += ao[j];
                    tmp[i] = 0.5f * ao[i] + 0.5f * s / nb[i].Count;
                }
                Array.Copy(tmp, ao, n);
            }
            Weld(mesh, ao);
        }

        private static float[] Transfer(Vector3[] target, Vector3[] source, float[] sourceAo, int k)
        {
            var result = new float[target.Length];
            k = Mathf.Max(1, k);
            Parallel.For(0, target.Length, i =>
            {
                var bestD = new float[k]; var bestI = new int[k];
                for (int j = 0; j < k; j++) { bestD[j] = float.MaxValue; bestI[j] = -1; }
                Vector3 p = target[i];
                for (int s = 0; s < source.Length; s++)
                {
                    float d = (source[s] - p).sqrMagnitude;
                    if (d >= bestD[k - 1]) continue;
                    int j = k - 1;
                    while (j > 0 && bestD[j - 1] > d) { bestD[j] = bestD[j - 1]; bestI[j] = bestI[j - 1]; j--; }
                    bestD[j] = d; bestI[j] = s;
                }
                float ws = 0, a = 0;
                for (int j = 0; j < k; j++)
                {
                    if (bestI[j] < 0) continue;
                    float w = 1f / (Mathf.Sqrt(bestD[j]) + 1e-4f);
                    ws += w; a += w * sourceAo[bestI[j]];
                }
                result[i] = ws > 0 ? a / ws : 1f;
            });
            return result;
        }

        // ---------------------------------------------------------------- assets

        private static Mesh WriteCopy(Mesh source, float[] ao, string folder)
        {
            var copy = UnityEngine.Object.Instantiate(source);
            copy.name = source.name + "_AO";
            var uv3 = new List<Vector2>(ao.Length);
            for (int i = 0; i < ao.Length; i++) uv3.Add(new Vector2(ao[i], 0f));
            copy.SetUVs(3, uv3);
            string path = folder + "/" + Sanitize(source.name) + "_AO.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null)
            {
                EditorUtility.CopySerialized(copy, existing);
                existing.name = copy.name;
                UnityEngine.Object.DestroyImmediate(copy);
                EditorUtility.SetDirty(existing);
                AssetDatabase.SaveAssetIfDirty(existing);
                return existing;
            }
            AssetDatabase.CreateAsset(copy, path);
            AssetDatabase.SaveAssetIfDirty(copy);
            return copy;
        }

        private static string Sanitize(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name;
        }

        private static VertexAoLibrary LoadOrCreateLibrary(string path)
        {
            var lib = AssetDatabase.LoadAssetAtPath<VertexAoLibrary>(path);
            if (lib != null) return lib;
            EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
            lib = ScriptableObject.CreateInstance<VertexAoLibrary>();
            AssetDatabase.CreateAsset(lib, path);
            return lib;
        }

        private static void Upsert(VertexAoLibrary lib, Mesh source, Mesh baked, string note)
        {
            for (int i = 0; i < lib.entries.Count; i++)
                if (lib.entries[i].source == source) { lib.entries[i] = new VertexAoLibrary.Entry { source = source, baked = baked, note = note }; return; }
            lib.entries.Add(new VertexAoLibrary.Entry { source = source, baked = baked, note = note });
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }

        /// <summary>Text dump: "v x y z ao" per vertex, then "t a b c" per triangle.</summary>
        private static void Dump(string path, Vector3[] pos, int[] tris, float[] ao)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var sb = new StringBuilder();
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            for (int i = 0; i < pos.Length; i++)
                sb.Append("v ").Append(pos[i].x.ToString("0.####", ci)).Append(' ').Append(pos[i].y.ToString("0.####", ci)).Append(' ')
                  .Append(pos[i].z.ToString("0.####", ci)).Append(' ').Append(ao[i].ToString("0.###", ci)).Append('\n');
            for (int t = 0; t + 2 < tris.Length; t += 3)
                sb.Append("t ").Append(tris[t]).Append(' ').Append(tris[t + 1]).Append(' ').Append(tris[t + 2]).Append('\n');
            File.WriteAllText(path, sb.ToString());
        }

        // ---------------------------------------------------------------- BVH (read-only after build; safe for Parallel.For)

        private sealed class Bvh
        {
            private readonly Vector3[] v;
            private readonly int[] tri;      // triangle vertex indices
            private int[] order;             // triangle order
            private Node[] nodes;
            private int nodeCount;

            private struct Node { public Vector3 min, max; public int left, start, count; }

            public Bvh(Vector3[] vertices, int[] triangles)
            {
                v = vertices; tri = triangles;
                int tc = triangles.Length / 3;
                order = new int[tc];
                for (int i = 0; i < tc; i++) order[i] = i;
                var cent = new Vector3[tc];
                for (int i = 0; i < tc; i++) cent[i] = (v[tri[i * 3]] + v[tri[i * 3 + 1]] + v[tri[i * 3 + 2]]) / 3f;
                nodes = new Node[Mathf.Max(1, tc * 2)];
                nodeCount = 0;
                Build(cent, 0, tc);
            }

            private int Build(Vector3[] cent, int start, int count)
            {
                int index = nodeCount++;
                Vector3 mn = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue), mx = -mn, cmn = mn, cmx = mx;
                for (int i = start; i < start + count; i++)
                {
                    int t = order[i];
                    for (int e = 0; e < 3; e++) { var p = v[tri[t * 3 + e]]; mn = Vector3.Min(mn, p); mx = Vector3.Max(mx, p); }
                    cmn = Vector3.Min(cmn, cent[t]); cmx = Vector3.Max(cmx, cent[t]);
                }
                nodes[index].min = mn; nodes[index].max = mx;
                if (count <= 4) { nodes[index].start = start; nodes[index].count = count; nodes[index].left = -1; return index; }
                Vector3 ext = cmx - cmn;
                int axis = ext.x > ext.y ? (ext.x > ext.z ? 0 : 2) : (ext.y > ext.z ? 1 : 2);
                float split = (cmn[axis] + cmx[axis]) * 0.5f;
                int mid = start;
                for (int i = start; i < start + count; i++)
                    if (cent[order[i]][axis] < split) { int tmp = order[i]; order[i] = order[mid]; order[mid] = tmp; mid++; }
                if (mid == start || mid == start + count) mid = start + count / 2;
                nodes[index].count = 0;
                int l = Build(cent, start, mid - start);
                int r = Build(cent, mid, start + count - mid);
                nodes[index].left = l;
                nodes[index].start = r; // right child index stored in start for inner nodes
                return index;
            }

            /// <summary>Distance to the nearest hit within maxDist (maxDist when none); triangles using <paramref name="skipVertex"/> are ignored.</summary>
            public float Raycast(Vector3 o, Vector3 d, float maxDist, int skipVertex)
            {
                float best = maxDist;
                Vector3 inv = new Vector3(1f / (Mathf.Abs(d.x) > 1e-12f ? d.x : 1e-12f), 1f / (Mathf.Abs(d.y) > 1e-12f ? d.y : 1e-12f), 1f / (Mathf.Abs(d.z) > 1e-12f ? d.z : 1e-12f));
                Span<int> stack = stackalloc int[64];
                int sp = 0; stack[sp++] = 0;
                while (sp > 0)
                {
                    var node = nodes[stack[--sp]];
                    if (!HitBox(node.min, node.max, o, inv, best)) continue;
                    if (node.left < 0)
                    {
                        for (int i = node.start; i < node.start + node.count; i++)
                        {
                            int t = order[i];
                            int a = tri[t * 3], b = tri[t * 3 + 1], c = tri[t * 3 + 2];
                            if (a == skipVertex || b == skipVertex || c == skipVertex) continue;
                            float h = HitTri(o, d, v[a], v[b], v[c]);
                            if (h > 0 && h < best) best = h;
                        }
                    }
                    else if (sp < 62) { stack[sp++] = node.left; stack[sp++] = node.start; }
                }
                return best;
            }

            private static bool HitBox(Vector3 mn, Vector3 mx, Vector3 o, Vector3 inv, float maxT)
            {
                float t1 = (mn.x - o.x) * inv.x, t2 = (mx.x - o.x) * inv.x;
                float tmin = Mathf.Min(t1, t2), tmax = Mathf.Max(t1, t2);
                t1 = (mn.y - o.y) * inv.y; t2 = (mx.y - o.y) * inv.y;
                tmin = Mathf.Max(tmin, Mathf.Min(t1, t2)); tmax = Mathf.Min(tmax, Mathf.Max(t1, t2));
                t1 = (mn.z - o.z) * inv.z; t2 = (mx.z - o.z) * inv.z;
                tmin = Mathf.Max(tmin, Mathf.Min(t1, t2)); tmax = Mathf.Min(tmax, Mathf.Max(t1, t2));
                return tmax >= Mathf.Max(0, tmin) && tmin < maxT;
            }

            // Möller–Trumbore, double-sided.
            private static float HitTri(Vector3 o, Vector3 d, Vector3 a, Vector3 b, Vector3 c)
            {
                Vector3 e1 = b - a, e2 = c - a, pv = Vector3.Cross(d, e2);
                float det = Vector3.Dot(e1, pv);
                if (Mathf.Abs(det) < 1e-12f) return -1;
                float invDet = 1f / det;
                Vector3 tv = o - a;
                float u = Vector3.Dot(tv, pv) * invDet;
                if (u < 0 || u > 1) return -1;
                Vector3 qv = Vector3.Cross(tv, e1);
                float w = Vector3.Dot(d, qv) * invDet;
                if (w < 0 || u + w > 1) return -1;
                return Vector3.Dot(e2, qv) * invDet;
            }
        }
    }
}
