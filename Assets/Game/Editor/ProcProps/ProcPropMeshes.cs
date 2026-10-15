using System;
using System.Collections.Generic;
using UnityEngine;

namespace MassEngine.Game.Editor
{
    /// <summary>
    /// Flat-shaded low-poly mesh under construction: every triangle owns its 3 vertices (hard facets), one sRGB
    /// colour per face. AO is added per face later (colour alpha) by <see cref="ProcPropLibrary"/>.
    /// </summary>
    public sealed class FlatMesh
    {
        public readonly List<Vector3> V = new List<Vector3>();
        public readonly List<Color> C = new List<Color>();
        public readonly List<int> T = new List<int>();
        public int Triangles => T.Count / 3;

        /// <summary>Adds a triangle whose front face points away from <paramref name="inside"/>; degenerate faces are dropped.</summary>
        public void Tri(Vector3 a, Vector3 b, Vector3 c, Color col, Vector3 inside)
        {
            var n = Vector3.Cross(b - a, c - a);
            if (n.sqrMagnitude < 1e-12f) return;
            if (Vector3.Dot(n, (a + b + c) / 3f - inside) < 0) { var t = b; b = c; c = t; }
            int i = V.Count;
            V.Add(a); V.Add(b); V.Add(c);
            C.Add(col); C.Add(col); C.Add(col);
            T.Add(i); T.Add(i + 1); T.Add(i + 2);
        }

        public static Vector3 Normal(Vector3 a, Vector3 b, Vector3 c) => Vector3.Cross(b - a, c - a).normalized;
    }

    /// <summary>Deterministic shape builders (unit-ish sizes; the decorator rescales every prop to its target height).</summary>
    public static class ProcPropMeshes
    {
        // ---------------------------------------------------------------- palette (sRGB, Q版: bright, soft)
        public static Color Hex(string h) { ColorUtility.TryParseHtmlString(h, out var c); return c; }
        static readonly Color[] LeafGreens = { Hex("#6DBB4A"), Hex("#5BAA3F"), Hex("#80C757"), Hex("#4F9E3D") };
        static readonly Color[] BirchLeaves = { Hex("#A9D05B"), Hex("#95C34F"), Hex("#B8D766") };
        static readonly Color[] PineGreens = { Hex("#3F9150"), Hex("#368446"), Hex("#4C9E5B"), Hex("#2F7A45") };
        static readonly Color[] BushGreens = { Hex("#5FAF46"), Hex("#72BE52"), Hex("#58A54A"), Hex("#68B84C") };
        static readonly Color[] RockGreys = { Hex("#9D9C94"), Hex("#8E918B"), Hex("#A9A59A"), Hex("#93958F"), Hex("#A29E95"), Hex("#888C88") };
        static readonly Color Moss = Hex("#7FA65A"), Bark = Hex("#8A5A3B"), BarkDark = Hex("#6E4630"), BirchBark = Hex("#ECE6D6"), BirchBand = Hex("#4A4440");
        static readonly Color WoodLight = Hex("#E2C394"), WoodMid = Hex("#C79B66");
        static readonly Color[] GrassGreens = { Hex("#7DC35A"), Hex("#6AB24B"), Hex("#8CCB62") };
        static readonly Color[] Petals = { Hex("#F25C54"), Hex("#FFD34E"), Hex("#FFFFFF"), Hex("#B98BE8"), Hex("#FF9FB8") };
        static readonly Color SnowWhite = Hex("#F4F8FB");
        static readonly Color[] DryBush = { Hex("#A8A04A"), Hex("#8E9A45"), Hex("#B5A55A") };
        static readonly Color[] DryGrass = { Hex("#C9B66A"), Hex("#B8A65A"), Hex("#D4C27A") };
        static readonly Color[] SnowBush = { Hex("#4E8F5A"), Hex("#5A9A60") };
        static readonly Color[] AutumnLeaves = { Hex("#F2A33A"), Hex("#E8743B"), Hex("#F2C94C") };
        static readonly Color[] AutumnMix = { Hex("#F2A33A"), Hex("#E8743B"), Hex("#F2C94C"), Hex("#6DBB4A"), Hex("#5BAA3F"), Hex("#4F9E3D") };
        static readonly Color[] DryLeaves = { Hex("#9AA04A"), Hex("#8E9A45"), Hex("#A8A04A"), Hex("#7F8F44") };
        static readonly Color CypressGreen = Hex("#3E8A4C"), DeadWood = Hex("#8C7B6B"), Stem = Hex("#F4EBDD"), FernGreen = Hex("#5DAE4B");
        static readonly Color[] Caps = { Hex("#E8483F"), Hex("#E8483F"), Hex("#B9834F") };
        static readonly Color MountLow = Hex("#7FA05E"), MountMid = Hex("#8E9688"), MountHigh = Hex("#B4B7AE");

        static Color Shade(Color c, Vector3 n, System.Random r, float jitter = .04f, float warmTop = 0f)
        {
            float k = .95f + .08f * n.y + (float)(r.NextDouble() * 2 - 1) * jitter;
            var o = new Color(c.r * k, c.g * k, c.b * k, 1);
            if (warmTop > 0 && n.y > .55f) o = Color.Lerp(o, new Color(o.r * 1.08f + .03f, o.g * 1.06f + .02f, o.b * .9f, 1), warmTop);
            return o;
        }

        // ---------------------------------------------------------------- noise
        static float Hash(int x, int y, int z, int s)
        {
            unchecked
            {
                uint h = (uint)x * 0x8da6b343u ^ (uint)y * 0xd8163841u ^ (uint)z * 0xcb1ab31fu ^ (uint)s * 0x165667b1u;
                h ^= h >> 13; h *= 0x5bd1e995u; h ^= h >> 15;
                return (h & 0xffffff) / 16777215f;
            }
        }

        public static float Noise(Vector3 p, int s)
        {
            int x = Mathf.FloorToInt(p.x), y = Mathf.FloorToInt(p.y), z = Mathf.FloorToInt(p.z);
            float fx = p.x - x, fy = p.y - y, fz = p.z - z;
            fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy); fz = fz * fz * (3 - 2 * fz);
            float L(float a, float b, float t) => a + (b - a) * t;
            return L(L(L(Hash(x, y, z, s), Hash(x + 1, y, z, s), fx), L(Hash(x, y + 1, z, s), Hash(x + 1, y + 1, z, s), fx), fy),
                     L(L(Hash(x, y, z + 1, s), Hash(x + 1, y, z + 1, s), fx), L(Hash(x, y + 1, z + 1, s), Hash(x + 1, y + 1, z + 1, s), fx), fy), fz);
        }

        static float Fbm(Vector3 p, int s, int oct)
        {
            float sum = 0, amp = 1, norm = 0;
            for (int i = 0; i < oct; i++) { sum += Noise(p, s + i * 17) * amp; norm += amp; amp *= .5f; p *= 2.07f; }
            return sum / norm;
        }

        static float R(System.Random r, float a, float b) => a + (float)r.NextDouble() * (b - a);

        // ---------------------------------------------------------------- icosphere
        static readonly Dictionary<int, (Vector3[] v, int[] f)> icoCache = new Dictionary<int, (Vector3[], int[])>();

        public static (Vector3[] v, int[] f) Ico(int subdiv)
        {
            if (icoCache.TryGetValue(subdiv, out var hit)) return hit;
            float t = (1f + Mathf.Sqrt(5f)) / 2f;
            var v = new List<Vector3>
            {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1),
            };
            for (int i = 0; i < v.Count; i++) v[i] = v[i].normalized;
            var f = new List<int> { 0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11, 1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8,
                                    3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9, 4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1 };
            for (int s = 0; s < subdiv; s++)
            {
                var mid = new Dictionary<long, int>();
                int Mid(int a, int b)
                {
                    long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                    if (mid.TryGetValue(key, out int m)) return m;
                    v.Add(((v[a] + v[b]) * .5f).normalized); mid[key] = v.Count - 1; return v.Count - 1;
                }
                var nf = new List<int>(f.Count * 4);
                for (int i = 0; i < f.Count; i += 3)
                {
                    int a = f[i], b = f[i + 1], c = f[i + 2], ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                    nf.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
                }
                f = nf;
            }
            var res = (v.ToArray(), f.ToArray());
            icoCache[subdiv] = res;
            return res;
        }

        // ---------------------------------------------------------------- primitives
        /// <summary>Noisy ellipsoid; colour from (face normal, centroid).</summary>
        static void Blob(FlatMesh m, Vector3 c, Vector3 radii, int subdiv, float rough, int seed, Func<Vector3, Vector3, Color> col, float dropBelowY = float.NegativeInfinity)
        {
            var (uv, f) = Ico(subdiv);
            var p = new Vector3[uv.Length];
            for (int i = 0; i < uv.Length; i++)
            {
                float d = 1f + rough * (Noise(uv[i] * 2.3f + Vector3.one * 7f, seed) - .5f) * 2f;
                p[i] = c + Vector3.Scale(uv[i] * d, radii);
            }
            for (int i = 0; i < f.Length; i += 3)
            {
                Vector3 a = p[f[i]], b = p[f[i + 1]], cc = p[f[i + 2]];
                if (Mathf.Max(a.y, Mathf.Max(b.y, cc.y)) < dropBelowY) continue;
                var n = FlatMesh.Normal(a, b, cc); if (Vector3.Dot(n, (a + b + cc) / 3f - c) < 0) n = -n;
                m.Tri(a, b, cc, col(n, (a + b + cc) / 3f), c);
            }
        }

        /// <summary>Chunky rock: rough icosphere with planar cuts (big facets) and a flat base.</summary>
        static void Rock(FlatMesh m, Vector3 c, Vector3 radii, int subdiv, int cuts, int seed, Color baseCol, float moss, System.Random r) =>
            Rock(m, c, radii, subdiv, cuts, seed, baseCol, moss, r, Moss);

        static void Rock(FlatMesh m, Vector3 c, Vector3 radii, int subdiv, int cuts, int seed, Color baseCol, float moss, System.Random r, Color mossCol)
        {
            var (uv, f) = Ico(subdiv);
            var rr = new System.Random(seed);
            var planes = new List<Vector4> { new Vector4(0, -1, 0, .42f) };
            for (int k = 0; k < cuts; k++)
            {
                var n = new Vector3(R(rr, -1, 1), R(rr, -.25f, 1f), R(rr, -1, 1)).normalized;
                planes.Add(new Vector4(n.x, n.y, n.z, R(rr, .6f, .85f)));
            }
            var p = new Vector3[uv.Length];
            for (int i = 0; i < uv.Length; i++)
            {
                var q = uv[i] * (1f + .16f * (Noise(uv[i] * 2.1f + Vector3.one * 3f, seed) - .5f) * 2f);
                foreach (var pl in planes) { var n = new Vector3(pl.x, pl.y, pl.z); float over = Vector3.Dot(q, n) - pl.w; if (over > 0) q -= over * n; }
                p[i] = c + Vector3.Scale(q, radii);
            }
            for (int i = 0; i < f.Length; i += 3)
            {
                Vector3 a = p[f[i]], b = p[f[i + 1]], cc = p[f[i + 2]];
                var n = FlatMesh.Normal(a, b, cc); if (Vector3.Dot(n, (a + b + cc) / 3f - c) < 0) n = -n;
                var col = moss > 0 && n.y > .62f ? Color.Lerp(baseCol, mossCol, moss) : baseCol;
                m.Tri(a, b, cc, Shade(col, n, r, .05f), c);
            }
        }

        /// <summary>Tapered prism along a slightly bent axis; top optionally capped. Colour per face index.</summary>
        static void Trunk(FlatMesh m, Vector3 bottom, Vector3 top, float r0, float r1, int sides, int segs, Vector3 bend, float rot, Func<int, int, Color> col, bool cap)
        {
            Vector3 Axis(float t) => Vector3.Lerp(bottom, top, t) + bend * Mathf.Sin(Mathf.PI * t * .5f);
            var rings = new Vector3[segs + 1][];
            for (int s = 0; s <= segs; s++)
            {
                float t = s / (float)segs, rad = Mathf.Lerp(r0, r1, t);
                var a = Axis(t); rings[s] = new Vector3[sides];
                for (int j = 0; j < sides; j++)
                {
                    float ang = rot + (j + s * .5f) * Mathf.PI * 2f / sides;
                    rings[s][j] = a + new Vector3(Mathf.Cos(ang) * rad, 0, Mathf.Sin(ang) * rad);
                }
            }
            for (int s = 0; s < segs; s++)
            {
                var inside = (Axis(s / (float)segs) + Axis((s + 1) / (float)segs)) * .5f;
                for (int j = 0; j < sides; j++)
                {
                    int k = (j + 1) % sides;
                    m.Tri(rings[s][j], rings[s][k], rings[s + 1][j], col(s, j * 2), inside);
                    m.Tri(rings[s][k], rings[s + 1][k], rings[s + 1][j], col(s, j * 2 + 1), inside);
                }
            }
            if (cap)
            {
                var tc = Axis(1f); var inside = Axis(.9f);
                for (int j = 0; j < sides; j++) m.Tri(rings[segs][j], rings[segs][(j + 1) % sides], tc, col(segs, -1), inside);
            }
        }

        /// <summary>One jagged pine tier: rim, mid ring, apex, concave underside.</summary>
        static void Tier(FlatMesh m, Vector3 bc, float h, float rad, int sides, float jag, float droop, float rot, Color c, System.Random r, float snow = 0f)
        {
            Color S(Color col, Vector3 n) => snow > 0 && n.y > .42f ? Color.Lerp(col, SnowWhite, snow) : col;
            var rim = new Vector3[sides]; var mid = new Vector3[sides];
            for (int j = 0; j < sides; j++)
            {
                float a = rot + j * Mathf.PI * 2f / sides, rr = rad * (j % 2 == 0 ? 1f : jag);
                rim[j] = bc + new Vector3(Mathf.Cos(a) * rr, -droop * (j % 2 == 0 ? 1f : .35f), Mathf.Sin(a) * rr);
                float a2 = a + Mathf.PI / sides;
                mid[j] = bc + new Vector3(Mathf.Cos(a2) * rad * .55f, h * .45f, Mathf.Sin(a2) * rad * .55f);
            }
            var apex = bc + Vector3.up * h; var inside = bc + Vector3.up * h * .3f; var under = bc + Vector3.up * h * .14f;
            for (int j = 0; j < sides; j++)
            {
                int k = (j + 1) % sides;
                Vector3 n1 = FlatMesh.Normal(rim[j], rim[k], mid[j]); if (n1.y < 0) n1 = -n1;
                m.Tri(rim[j], rim[k], mid[j], S(Shade(c, n1, r, .05f, .25f), n1), inside);
                Vector3 n2 = FlatMesh.Normal(mid[j], mid[(j + sides - 1) % sides], rim[j]); if (n2.y < 0) n2 = -n2;
                m.Tri(mid[j], mid[(j + sides - 1) % sides], rim[j], S(Shade(c, n2, r, .05f, .25f), n2), inside);
                m.Tri(mid[j], mid[k], apex, S(Shade(c, Vector3.up, r, .05f, .25f), Vector3.up), inside);
                m.Tri(rim[j], rim[k], under, Shade(c * .82f, Vector3.down, r, .03f), bc + Vector3.up * h);
            }
        }

        static void Blade(FlatMesh m, Vector3 p0, float w, float h, Vector3 lean, float rot, Color c, System.Random r)
        {
            var b = new Vector3[3];
            for (int j = 0; j < 3; j++) { float a = rot + j * Mathf.PI * 2f / 3f; b[j] = p0 + new Vector3(Mathf.Cos(a) * w, 0, Mathf.Sin(a) * w); }
            var tip = p0 + Vector3.up * h + lean; var inside = (b[0] + b[1] + b[2] + tip) / 4f;
            for (int j = 0; j < 3; j++) m.Tri(b[j], b[(j + 1) % 3], tip, Shade(c, Vector3.up, r, .06f), inside);
        }

        // ---------------------------------------------------------------- props (near/far)
        public static FlatMesh Broadleaf(int seed, bool far) => BroadleafCore(seed, far, LeafGreens);
        public static FlatMesh BroadleafAutumn(int seed, bool far) => BroadleafCore(seed, far, AutumnLeaves);

        static FlatMesh BroadleafCore(int seed, bool far, Color[] pal)
        {
            var r = new System.Random(seed); var m = new FlatMesh();
            var leaf = pal[seed % pal.Length];
            float H = 10f, trunkTop = H * R(r, .42f, .5f);
            var bend = new Vector3(R(r, -.35f, .35f), 0, R(r, -.35f, .35f));
            Trunk(m, Vector3.down * .3f, Vector3.up * trunkTop, H * .065f, H * .04f, far ? 4 : 6, far ? 1 : 2, bend, R(r, 0, 6.3f),
                (s, j) => Shade(j % 3 == 0 ? BarkDark : Bark, Vector3.zero, r, .05f), false);
            var top = Vector3.up * trunkTop + bend;
            Func<Vector3, Vector3, Color> lc = (n, p) => Shade(leaf, n, r, .045f, .3f);
            Blob(m, top + Vector3.up * H * .2f, new Vector3(H * .31f, H * .25f, H * .31f) * R(r, .95f, 1.08f), far ? 1 : 1, .14f, seed, lc);
            int extra = far ? 1 : 3 + seed % 2;
            for (int i = 0; i < extra; i++)
            {
                float a = i * Mathf.PI * 2f / extra + R(r, -.4f, .4f), d = H * R(r, .17f, .22f);
                var c = top + new Vector3(Mathf.Cos(a) * d, H * R(r, .06f, .24f), Mathf.Sin(a) * d);
                float s = H * R(r, .17f, .23f);
                Blob(m, c, new Vector3(s, s * .85f, s), far ? 0 : 1, .16f, seed + 11 * (i + 1), lc);
            }
            return m;
        }

        public static FlatMesh Birch(int seed, bool far)
        {
            var r = new System.Random(seed); var m = new FlatMesh();
            var leaf = BirchLeaves[seed % BirchLeaves.Length];
            float H = 11f, trunkTop = H * .62f;
            var bend = new Vector3(R(r, -.3f, .3f), 0, R(r, -.3f, .3f));
            Trunk(m, Vector3.down * .3f, Vector3.up * trunkTop, H * .035f, H * .024f, far ? 4 : 6, far ? 2 : 4, bend, R(r, 0, 6.3f),
                (s, j) => (s * 7 + j) % 5 == 0 ? Shade(BirchBand, Vector3.zero, r, .04f) : Shade(BirchBark, Vector3.zero, r, .03f), false);
            var top = Vector3.up * trunkTop + bend;
            Func<Vector3, Vector3, Color> lc = (n, p) => Shade(leaf, n, r, .045f, .3f);
            Blob(m, top + Vector3.up * H * .12f, new Vector3(H * .17f, H * .26f, H * .17f), 1, .16f, seed, lc);
            int extra = far ? 0 : 3;
            for (int i = 0; i < extra; i++)
            {
                float a = i * Mathf.PI * 2f / extra + R(r, -.5f, .5f), d = H * R(r, .09f, .13f);
                var c = top + new Vector3(Mathf.Cos(a) * d, H * R(r, -.1f, .12f), Mathf.Sin(a) * d);
                float s = H * R(r, .11f, .14f);
                Blob(m, c, new Vector3(s, s * 1.3f, s), 1, .15f, seed + 13 * (i + 1), lc);
            }
            return m;
        }

        public static FlatMesh Pine(int seed, bool far) => PineCore(seed, far, 0f);
        public static FlatMesh PineSnow(int seed, bool far) => PineCore(seed, far, .9f);

        static FlatMesh PineCore(int seed, bool far, float snow)
        {
            var r = new System.Random(seed); var m = new FlatMesh();
            var green = PineGreens[seed % PineGreens.Length];
            float H = 12f;
            Trunk(m, Vector3.down * .3f, Vector3.up * H * .3f, H * .045f, H * .03f, far ? 4 : 6, 1, Vector3.zero, R(r, 0, 6.3f),
                (s, j) => Shade(BarkDark, Vector3.zero, r, .05f), false);
            int tiers = far ? 3 : 4 + seed % 2;
            float y = H * .16f, step = (H * .84f - H * .3f) / tiers;
            for (int i = 0; i < tiers; i++)
            {
                float t = i / (float)Mathf.Max(1, tiers - 1);
                float rad = H * Mathf.Lerp(.29f, .12f, t) * R(r, .94f, 1.06f), h = step * (i == tiers - 1 ? 2.2f : 1.9f);
                Tier(m, Vector3.up * y, h, rad, far ? 6 : 10, .78f, H * .02f, R(r, 0, 6.3f), Shade(green, Vector3.up, r, .03f), r, snow);
                y += step;
            }
            return m;
        }

        // ---------------------------------------------------------------- 远景林冠团 (backdrop canopy clumps)
        // One mesh = 9-13 crowns packed in a disc (radius ~9 m before scaling), so a few thousand of them read as a
        // continuous forest. Only rim crowns get trunks; trunks reach y = -4 so the clump can be sunk on slopes.
        public static FlatMesh ClumpGreen(int seed, bool far) => ClumpCore(seed, far, LeafGreens, .25f, 0f);
        public static FlatMesh ClumpAutumn(int seed, bool far) => ClumpCore(seed, far, AutumnMix, .15f, 0f);
        public static FlatMesh ClumpPine(int seed, bool far) => ClumpCore(seed, far, LeafGreens, 1f, 0f);
        public static FlatMesh ClumpPineSnow(int seed, bool far) => ClumpCore(seed, far, LeafGreens, 1f, .9f);
        public static FlatMesh ClumpDry(int seed, bool far) => ClumpCore(seed, far, DryLeaves, .3f, 0f);

        static FlatMesh ClumpCore(int seed, bool far, Color[] pal, float pineShare, float snow)
        {
            var r = new System.Random(seed); var m = new FlatMesh();
            int n = (far ? 9 : 11) + seed % 3;
            const float R0 = 9f, Root = -4f;
            for (int i = 0; i < n; i++)
            {
                float t = (i + .5f) / n, rad = R0 * Mathf.Sqrt(t) * R(r, .85f, 1.1f), a = i * 2.39996f + R(r, -.3f, .3f);
                var p = new Vector3(Mathf.Cos(a) * rad, 0, Mathf.Sin(a) * rad);
                bool rim = t > .45f;
                float H = R(r, 9f, 13f) * (rim ? .9f : 1.05f);
                if (r.NextDouble() < pineShare)
                {
                    var green = PineGreens[r.Next(PineGreens.Length)];
                    if (rim) Trunk(m, p + Vector3.up * Root, p + Vector3.up * H * .3f, H * .045f, H * .03f, 4, 1, Vector3.zero, R(r, 0, 6.3f),
                        (s, j) => Shade(BarkDark, Vector3.zero, r, .05f), false);
                    int tiers = far ? 2 : 3;
                    float y = H * .18f, step = H * .8f / (tiers + .6f);
                    for (int k = 0; k < tiers; k++)
                    {
                        float tt = k / (float)Mathf.Max(1, tiers - 1);
                        Tier(m, p + Vector3.up * y, step * (k == tiers - 1 ? 2.2f : 1.8f), H * Mathf.Lerp(.3f, .16f, tt) * R(r, .94f, 1.06f), 6, .78f, H * .02f,
                            R(r, 0, 6.3f), Shade(green, Vector3.up, r, .03f), r, snow);
                        y += step;
                    }
                }
                else
                {
                    var leaf = pal[r.Next(pal.Length)];
                    float top = H * .45f;
                    if (rim) Trunk(m, p + Vector3.up * Root, p + Vector3.up * top, H * .06f, H * .04f, 4, 1, Vector3.zero, R(r, 0, 6.3f),
                        (s, j) => Shade(Bark, Vector3.zero, r, .05f), false);
                    var c = p + Vector3.up * (top + H * .22f);
                    var r3 = new Vector3(H * .34f, H * .27f, H * .34f) * R(r, .9f, 1.1f);
                    Func<Vector3, Vector3, Color> lc = (nn, pp) => Shade(leaf, nn, r, .045f, .3f);
                    Blob(m, c, r3, far ? 0 : 1, .14f, seed + 31 * i, lc, c.y - r3.y * .55f);
                }
            }
            return m;
        }

        public static FlatMesh Bush(int seed, bool far) => BushCore(seed, far, BushGreens, 0f);
        public static FlatMesh BushDryKind(int seed, bool far) => BushCore(seed, far, DryBush, 0f);
        public static FlatMesh BushSnowKind(int seed, bool far) => BushCore(seed, far, SnowBush, .9f);

        static FlatMesh BushCore(int seed, bool far, Color[] pal, float snow)
        {
            var r = new System.Random(seed); var m = new FlatMesh();
            var g = pal[seed % pal.Length];
            Func<Vector3, Vector3, Color> lc = (n, p) => snow > 0 && n.y > .5f ? Color.Lerp(Shade(g, n, r, .05f), SnowWhite, snow) : Shade(g, n, r, .05f, .25f);
            int n0 = far ? 2 : 3 + seed % 2;
            for (int i = 0; i < n0; i++)
            {
                float a = i * Mathf.PI * 2f / n0 + R(r, -.3f, .3f), d = i == 0 ? 0 : R(r, .35f, .5f);
                float s = i == 0 ? .62f : R(r, .38f, .5f);
                var c = new Vector3(Mathf.Cos(a) * d, s * .7f, Mathf.Sin(a) * d);
                Blob(m, c, new Vector3(s, s * .82f, s), far ? 0 : 1, .18f, seed + 19 * i, lc, 0f);
            }
            if (!far && seed % 4 == 1 && snow <= 0 && pal == BushGreens)   // one flowering bush
                for (int i = 0; i < 7; i++)
                {
                    float a = R(r, 0, 6.3f), d = R(r, .2f, .55f);
                    var c = new Vector3(Mathf.Cos(a) * d, R(r, .75f, 1.05f), Mathf.Sin(a) * d);
                    Blob(m, c, Vector3.one * .07f, 0, 0, seed + i, (n, p) => Shade(Petals[0], n, r, .04f));
                }
            return m;
        }

        public static FlatMesh RockLarge(int seed, bool far) => RockLargeCore(seed, far, Moss, .55f, .4f);
        public static FlatMesh RockSnow(int seed, bool far) => RockLargeCore(seed, far, SnowWhite, .92f, .9f);

        static FlatMesh RockLargeCore(int seed, bool far, Color top, float amount, float amount2)
        {
            var r = new System.Random(seed); var m = new FlatMesh();
            var c = RockGreys[seed % RockGreys.Length];
            Rock(m, new Vector3(0, .42f, 0), new Vector3(R(r, 1.1f, 1.4f), R(r, .8f, 1f), R(r, .95f, 1.2f)), far ? 1 : 2, far ? 5 : 7, seed, c, amount, r, top);
            if (!far && seed % 2 == 0)
                Rock(m, new Vector3(R(r, .8f, 1.1f), .2f, R(r, -.6f, .6f)), Vector3.one * R(r, .35f, .5f), 1, 4, seed + 5, c, amount2, r, top);
            return m;
        }

        public static FlatMesh RockTall(int seed, bool far)
        {
            var r = new System.Random(seed); var m = new FlatMesh();
            var c = RockGreys[(seed + 2) % RockGreys.Length];
            Rock(m, new Vector3(0, 1.15f, 0), new Vector3(R(r, .5f, .65f), 2.4f, R(r, .45f, .6f)), far ? 1 : 2, far ? 4 : 6, seed, c, .35f, r);
            return m;
        }

        public static FlatMesh RockPile(int seed, bool far)
        {
            var r = new System.Random(seed); var m = new FlatMesh();
            int n = far ? 2 : 3 + seed % 3;
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2f / n + R(r, -.4f, .4f), d = i == 0 ? 0 : R(r, .55f, .85f), s = i == 0 ? .7f : R(r, .35f, .55f);
                var c = RockGreys[(seed + i) % RockGreys.Length];
                Rock(m, new Vector3(Mathf.Cos(a) * d, s * .42f, Mathf.Sin(a) * d), new Vector3(s * 1.15f, s * .85f, s), 1, far ? 3 : 5, seed + 23 * i, c, .5f, r);
            }
            return m;
        }

        public static FlatMesh Grass(int seed, bool far) => GrassCore(seed, far, GrassGreens);
        public static FlatMesh GrassDryKind(int seed, bool far) => GrassCore(seed, far, DryGrass);

        static FlatMesh GrassCore(int seed, bool far, Color[] pal)
        {
            var r = new System.Random(seed); var m = new FlatMesh();
            var g = pal[seed % pal.Length];
            int n = far ? 4 : 7 + seed % 3;
            for (int i = 0; i < n; i++)
            {
                float a = R(r, 0, 6.3f), d = R(r, 0, .28f);
                var p = new Vector3(Mathf.Cos(a) * d, -.05f, Mathf.Sin(a) * d);
                var lean = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * R(r, .1f, .3f);
                Blade(m, p, R(r, .05f, .08f), R(r, .6f, 1f), lean, R(r, 0, 6.3f), g, r);
            }
            return m;
        }

        public static FlatMesh Flowers(int seed, bool far)
        {
            var r = new System.Random(seed); var m = new FlatMesh();
            var g = GrassGreens[(seed + 1) % GrassGreens.Length];
            var petal = Petals[seed % Petals.Length];
            int blades = far ? 2 : 4, stems = far ? 2 : 3 + seed % 2;
            for (int i = 0; i < blades; i++)
            {
                float a = R(r, 0, 6.3f), d = R(r, 0, .25f);
                Blade(m, new Vector3(Mathf.Cos(a) * d, -.05f, Mathf.Sin(a) * d), .06f, R(r, .45f, .7f), new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * .2f, R(r, 0, 6.3f), g, r);
            }
            for (int i = 0; i < stems; i++)
            {
                float a = R(r, 0, 6.3f), d = R(r, .05f, .3f), h = R(r, .75f, 1f);
                var p = new Vector3(Mathf.Cos(a) * d, -.05f, Mathf.Sin(a) * d);
                var lean = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * .12f;
                Blade(m, p, .025f, h, lean, R(r, 0, 6.3f), g * .9f, r);
                Blob(m, p + Vector3.up * h + lean, new Vector3(.14f, .09f, .14f), 0, .1f, seed + i, (n, q) => Shade(petal, n, r, .03f));
            }
            return m;
        }

        public static FlatMesh Stump(int seed, bool far)
        {
            var r = new System.Random(seed); var m = new FlatMesh();
            int sides = far ? 6 : 9; float rad = .5f, h = .6f;
            Trunk(m, Vector3.down * .15f, Vector3.up * h, rad * 1.12f, rad, sides, 1, Vector3.zero, R(r, 0, 6.3f),
                (s, j) => Shade(j % 3 == 0 ? BarkDark : Bark, Vector3.zero, r, .05f), false);
            // top: light core + mid ring (rings read as a cut stump)
            var top = Vector3.up * h; var inside = Vector3.up * h * .5f;
            float rot = R(r, 0, 6.3f);
            for (int j = 0; j < sides; j++)
            {
                float a0 = rot + (j + .5f) * Mathf.PI * 2f / sides, a1 = rot + (j + 1.5f) * Mathf.PI * 2f / sides;
                Vector3 o0 = top + new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)) * rad * .96f, o1 = top + new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1)) * rad * .96f;
                Vector3 i0 = top + new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)) * rad * .55f, i1 = top + new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1)) * rad * .55f;
                m.Tri(i0, i1, top + Vector3.up * .02f, Shade(WoodLight, Vector3.up, r, .03f), inside);
                m.Tri(o0, o1, i0, Shade(WoodMid, Vector3.up, r, .03f), inside);
                m.Tri(o1, i1, i0, Shade(WoodMid, Vector3.up, r, .03f), inside);
            }
            if (!far)
                for (int i = 0; i < 4; i++)   // root flares
                {
                    float a = rot + i * Mathf.PI / 2f + R(r, -.3f, .3f);
                    var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                    var side = Vector3.Cross(Vector3.up, dir) * .13f;
                    Vector3 b0 = dir * rad * .9f + side + Vector3.down * .1f, b1 = dir * rad * .9f - side + Vector3.down * .1f, up = dir * rad * .95f + Vector3.up * .3f, tip = dir * rad * 1.75f + Vector3.down * .08f;
                    var ins = (b0 + b1 + up + tip) / 4f - dir * .05f;
                    var bc = Shade(Bark, Vector3.zero, r, .05f);
                    m.Tri(b0, up, tip, bc, ins); m.Tri(b1, tip, up, bc, ins); m.Tri(b0, b1, up, bc, ins);
                }
            return m;
        }

        public static FlatMesh Logs(int seed, bool far)
        {
            var r = new System.Random(seed); var m = new FlatMesh();
            float rad = .3f; int sides = far ? 6 : 8;
            var logs = new[] { (new Vector3(0, rad, -rad * 1.05f), 2.2f), (new Vector3(.1f, rad, rad * 1.05f), 2.0f), (new Vector3(-.05f, rad * 2.75f, 0), 1.85f) };
            int count = seed % 2 == 0 ? 3 : 2;
            for (int i = 0; i < count; i++)
            {
                var (c, len) = logs[i];
                float rr = rad * R(r, .9f, 1.05f);
                Vector3 a = c - Vector3.right * len * .5f, b = c + Vector3.right * len * .5f;
                // build along X by rotating a vertical trunk: generate points directly
                var ringA = new Vector3[sides]; var ringB = new Vector3[sides];
                float rot = R(r, 0, 6.3f);
                for (int j = 0; j < sides; j++)
                {
                    float ang = rot + j * Mathf.PI * 2f / sides;
                    var off = new Vector3(0, Mathf.Cos(ang) * rr, Mathf.Sin(ang) * rr);
                    ringA[j] = a + off; ringB[j] = b + off;
                }
                for (int j = 0; j < sides; j++)
                {
                    int k = (j + 1) % sides; var bark = Shade(j % 3 == 0 ? BarkDark : Bark, Vector3.zero, r, .05f);
                    m.Tri(ringA[j], ringA[k], ringB[j], bark, c); m.Tri(ringA[k], ringB[k], ringB[j], bark, c);
                    m.Tri(ringA[j], ringA[k], a - Vector3.right * .01f, Shade(WoodLight, Vector3.up, r, .03f), c);
                    m.Tri(ringB[j], ringB[k], b + Vector3.right * .01f, Shade(WoodLight, Vector3.up, r, .03f), c);
                }
            }
            return m;
        }

        // ---------------------------------------------------------------- variety pack (round 2)
        public static FlatMesh Cypress(int seed, bool far)
        {
            var r = new System.Random(seed); var m = new FlatMesh();
            float H = 10f;
            Trunk(m, Vector3.down * .3f, Vector3.up * H * .18f, H * .04f, H * .03f, far ? 4 : 5, 1, Vector3.zero, R(r, 0, 6.3f), (s, j) => Shade(BarkDark, Vector3.zero, r, .05f), false);
            Func<Vector3, Vector3, Color> lc = (n, p) => Shade(CypressGreen * R(r, .96f, 1.04f), n, r, .05f, .2f);
            Blob(m, Vector3.up * H * .5f, new Vector3(H * .17f, H * .36f, H * .17f), 1, .12f, seed, lc);
            if (!far) Blob(m, Vector3.up * H * .82f, new Vector3(H * .1f, H * .16f, H * .1f), 1, .12f, seed + 3, lc);
            return m;
        }

        public static FlatMesh DeadTree(int seed, bool far)
        {
            var r = new System.Random(seed); var m = new FlatMesh();
            float H = 10f; int sides = far ? 4 : 5;
            Func<int, int, Color> wood = (s, j) => Shade(j % 3 == 0 ? DeadWood * .85f : DeadWood, Vector3.zero, r, .05f);
            var bend = new Vector3(R(r, -.4f, .4f), 0, R(r, -.4f, .4f));
            Trunk(m, Vector3.down * .3f, Vector3.up * H * .75f, H * .05f, H * .018f, sides, far ? 2 : 3, bend, R(r, 0, 6.3f), wood, true);
            int branches = far ? 2 : 4;
            for (int i = 0; i < branches; i++)
            {
                float y = H * R(r, .35f, .65f), a = i * Mathf.PI * 2f / branches + R(r, -.5f, .5f);
                var b0 = Vector3.up * y + bend * (y / (H * .75f)) * .7f;
                var dir = new Vector3(Mathf.Cos(a), R(r, .6f, 1.1f), Mathf.Sin(a)).normalized;
                var b1 = b0 + dir * H * R(r, .2f, .32f);
                Trunk(m, b0, b1, H * .02f, H * .007f, 4, 1, Vector3.zero, a, wood, true);
                if (!far)
                {
                    var tw = b0 + (b1 - b0) * .6f; var d2 = (dir + new Vector3(-Mathf.Sin(a), .3f, Mathf.Cos(a)) * .8f).normalized;
                    Trunk(m, tw, tw + d2 * H * .1f, H * .009f, H * .004f, 3, 1, Vector3.zero, a, wood, true);
                }
            }
            return m;
        }

        public static FlatMesh FallenLog(int seed, bool far)
        {
            var r = new System.Random(seed); var m = new FlatMesh();
            int sides = far ? 6 : 9; float rad = .3f, len = 3f;
            var ra = new Vector3[sides]; var rb = new Vector3[sides];
            float rot = R(r, 0, 6.3f);
            for (int j = 0; j < sides; j++)
            {
                float ang = rot + j * Mathf.PI * 2f / sides;
                var off = new Vector3(0, Mathf.Cos(ang) * rad + rad * .85f, Mathf.Sin(ang) * rad);
                ra[j] = new Vector3(-len * .5f, 0, 0) + off;
                rb[j] = new Vector3(len * .5f, 0, 0) + off * .85f + new Vector3(R(r, -.12f, .12f), 0, 0);   // broken, thinner end
            }
            var c = new Vector3(0, rad * .85f, 0);
            for (int j = 0; j < sides; j++)
            {
                int k = (j + 1) % sides;
                var n = FlatMesh.Normal(ra[j], ra[k], rb[j]); if (Vector3.Dot(n, (ra[j] + rb[k]) * .5f - c) < 0) n = -n;
                var col = n.y > .45f ? Moss : (j % 3 == 0 ? BarkDark : Bark);
                m.Tri(ra[j], ra[k], rb[j], Shade(col, n, r, .05f), new Vector3((ra[j].x + rb[j].x) * .5f, c.y, 0));
                m.Tri(ra[k], rb[k], rb[j], Shade(col, n, r, .05f), new Vector3((ra[j].x + rb[j].x) * .5f, c.y, 0));
                m.Tri(ra[j], ra[k], new Vector3(-len * .5f - .01f, c.y, 0), Shade(WoodLight, Vector3.up, r, .03f), c);
                m.Tri(rb[j], rb[k], new Vector3(len * .5f + .25f, c.y + .05f, 0), Shade(WoodMid, Vector3.up, r, .05f), c);   // jagged break
            }
            return m;
        }

        public static FlatMesh Mushrooms(int seed, bool far)
        {
            var r = new System.Random(seed); var m = new FlatMesh();
            var cap = Caps[seed % Caps.Length]; bool dots = cap == Caps[0];
            int n = far ? 2 : 3 + seed % 3;
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2f / n + R(r, -.4f, .4f), d = i == 0 ? 0 : R(r, .25f, .4f), s = i == 0 ? 1f : R(r, .5f, .75f);
                var p = new Vector3(Mathf.Cos(a) * d, 0, Mathf.Sin(a) * d);
                float h = .55f * s, cr = .3f * s;
                Trunk(m, p + Vector3.down * .05f, p + Vector3.up * h, .07f * s, .06f * s, far ? 4 : 6, 1, Vector3.zero, a, (q, j) => Shade(Stem, Vector3.zero, r, .03f), false);
                var cc = p + Vector3.up * (h - .02f);
                Blob(m, cc, new Vector3(cr, cr * .75f, cr), far ? 0 : 1, .05f, seed + i, (nn, q) => nn.y < -.2f ? Shade(Stem * .9f, nn, r, .03f) : Shade(cap, nn, r, .04f), cc.y + cr * .05f);
                // underside disc
                for (int j = 0; j < 8; j++)
                {
                    float a0 = j * Mathf.PI / 4f, a1 = (j + 1) * Mathf.PI / 4f;
                    m.Tri(cc + new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)) * cr, cc + new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1)) * cr, cc, Shade(Stem * .85f, Vector3.down, r, .03f), cc + Vector3.up);
                }
                if (dots && !far)
                    for (int k = 0; k < 4; k++)
                    {
                        float da = R(r, 0, 6.3f), dr = R(r, .2f, .6f) * cr;
                        var dp = cc + new Vector3(Mathf.Cos(da) * dr, cr * .75f * Mathf.Sqrt(1 - (dr / cr) * (dr / cr)), Mathf.Sin(da) * dr);
                        Blob(m, dp, Vector3.one * cr * .13f, 0, 0, seed + k, (nn, q) => Shade(Color.white, nn, r, .02f));
                    }
            }
            return m;
        }

        public static FlatMesh Fern(int seed, bool far)
        {
            var r = new System.Random(seed); var m = new FlatMesh();
            int fronds = far ? 4 : 7 + seed % 2;
            for (int i = 0; i < fronds; i++)
            {
                float a = i * Mathf.PI * 2f / fronds + R(r, -.25f, .25f), len = R(r, .8f, 1.1f), lift = R(r, .5f, .8f);
                var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)); var side = Vector3.Cross(Vector3.up, dir);
                Vector3 b = Vector3.zero, mid = dir * len * .5f + Vector3.up * lift * len, tip = dir * len + Vector3.up * lift * len * .55f;
                float w = .13f * len;
                Vector3 ml = mid + side * w, mr = mid - side * w, under = Vector3.down * .02f;
                var c = Shade(FernGreen * R(r, .92f, 1.06f), Vector3.up, r, .04f);
                var cd = Shade(FernGreen * .8f, Vector3.down, r, .03f);
                m.Tri(b, ml, mid + Vector3.up * .03f, c, under - Vector3.up); m.Tri(b, mid + Vector3.up * .03f, mr, c, under - Vector3.up);
                m.Tri(ml, tip, mid + Vector3.up * .03f, c, mid - Vector3.up); m.Tri(mid + Vector3.up * .03f, tip, mr, c, mid - Vector3.up);
                // underside (so fronds read from below too)
                m.Tri(b, ml, mr, cd, mid + Vector3.up); m.Tri(ml, tip, mr, cd, mid + Vector3.up);
            }
            return m;
        }

        public static FlatMesh FlowerPatch(int seed, bool far)
        {
            var r = new System.Random(seed); var m = new FlatMesh();
            var g = GrassGreens[seed % GrassGreens.Length];
            int stems = far ? 5 : 12 + seed % 5, blades = far ? 3 : 8;
            for (int i = 0; i < blades; i++)
            {
                float a = R(r, 0, 6.3f), d = R(r, 0, .9f);
                Blade(m, new Vector3(Mathf.Cos(a) * d, -.05f, Mathf.Sin(a) * d), .05f, R(r, .35f, .6f), new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * .15f, R(r, 0, 6.3f), g, r);
            }
            for (int i = 0; i < stems; i++)
            {
                float a = R(r, 0, 6.3f), d = Mathf.Sqrt((float)r.NextDouble()) * .95f, h = R(r, .5f, .8f);
                var p = new Vector3(Mathf.Cos(a) * d, -.05f, Mathf.Sin(a) * d);
                var petal = Petals[(seed + i * 3) % Petals.Length];
                Blade(m, p, .02f, h, Vector3.zero, R(r, 0, 6.3f), g * .9f, r);
                Blob(m, p + Vector3.up * h, new Vector3(.11f, .07f, .11f), 0, .1f, seed + i, (n, q) => Shade(petal, n, r, .03f));
            }
            return m;
        }

        public static FlatMesh Pebbles(int seed, bool far)
        {
            var r = new System.Random(seed); var m = new FlatMesh();
            int n = far ? 3 : 4 + seed % 4;
            for (int i = 0; i < n; i++)
            {
                float a = R(r, 0, 6.3f), d = i == 0 ? 0 : R(r, .3f, 1f), s = i == 0 ? .5f : R(r, .2f, .38f);
                Rock(m, new Vector3(Mathf.Cos(a) * d, s * .35f, Mathf.Sin(a) * d), new Vector3(s * 1.2f, s * .7f, s), 0, 3, seed + 31 * i, RockGreys[(seed + i) % RockGreys.Length], .3f, r);
            }
            return m;
        }

        public static FlatMesh Mountain(int seed, bool far) => MountainCore(seed, far, false);
        public static FlatMesh MountainSnow(int seed, bool far) => MountainCore(seed, far, true);

        static FlatMesh MountainCore(int seed, bool far, bool snow)
        {
            var r = new System.Random(seed); var m = new FlatMesh();
            int rings = far ? 9 : 18, sectors = far ? 20 : 40;
            // main peak + 2 shoulders (offsets in unit disc), ridged noise for gullies
            var peaks = new List<Vector3> { new Vector3(R(r, -.12f, .12f), 1f, R(r, -.12f, .12f)) };
            for (int i = 0; i < 2; i++) { float a = R(r, 0, 6.3f), d = R(r, .35f, .55f); peaks.Add(new Vector3(Mathf.Cos(a) * d, R(r, .5f, .72f), Mathf.Sin(a) * d)); }
            float Hgt(float x, float z)
            {
                float h = 0;
                foreach (var pk in peaks)
                {
                    float d = Mathf.Sqrt((x - pk.x) * (x - pk.x) + (z - pk.z) * (z - pk.z));
                    h = Mathf.Max(h, pk.y * Mathf.Pow(Mathf.Clamp01(1f - d / 1.05f), 1.35f));
                }
                var q = new Vector3(x * 3.2f, 0, z * 3.2f);
                float ridge = 1f - Mathf.Abs(2f * Fbm(q, seed, 3) - 1f);
                return h * (.78f + .32f * ridge) - .04f;
            }
            Vector3 P(int ring, int sec)
            {
                float rr = ring / (float)rings, a = (sec + (ring % 2) * .5f) * Mathf.PI * 2f / sectors;
                float x = Mathf.Cos(a) * rr, z = Mathf.Sin(a) * rr;
                return new Vector3(x * .95f, Hgt(x, z) * .95f, z * .95f);
            }
            var grid = new Vector3[rings + 1][];
            for (int i = 0; i <= rings; i++) { grid[i] = new Vector3[i == 0 ? 1 : sectors]; for (int j = 0; j < grid[i].Length; j++) grid[i][j] = i == 0 ? new Vector3(0, Hgt(0, 0) * .95f, 0) : P(i, j); }
            float top = 0; foreach (var g in grid) foreach (var q in g) top = Mathf.Max(top, q.y);
            void Face(Vector3 a, Vector3 b, Vector3 c)
            {
                var n = FlatMesh.Normal(a, b, c); if (n.y < 0) n = -n;
                float t = ((a + b + c) / 3f).y / top;
                Color col = t < .3f ? MountLow : t < (snow ? .55f : .74f) ? MountMid : (snow ? SnowWhite : MountHigh);
                if (n.y < .55f && t > .18f) col = Color.Lerp(col, MountMid * .9f, snow && t > .55f ? .3f : .55f);
                if (snow && t < .3f) col = Color.Lerp(MountLow, SnowWhite, .65f);
                m.Tri(a, b, c, Shade(col, n, r, .04f), (a + b + c) / 3f - n);
            }
            for (int j = 0; j < sectors; j++) Face(grid[0][0], grid[1][j], grid[1][(j + 1) % sectors]);
            for (int i = 1; i < rings; i++)
                for (int j = 0; j < sectors; j++)
                {
                    int k = (j + 1) % sectors;
                    // inner ring i, outer ring i+1 (outer is offset by half a sector on odd rings)
                    Vector3 a = grid[i][j], b = grid[i][k], c = grid[i + 1][j], d = grid[i + 1][k];
                    if (i % 2 == 1) { Face(a, c, d); Face(a, d, b); } else { Face(a, c, b); Face(b, c, d); }
                }
            // skirt: outer ring drops below the base so uneven backdrop ground never shows a floating rim
            for (int j = 0; j < sectors; j++)
            {
                int k = (j + 1) % sectors;
                Vector3 a = grid[rings][j], b = grid[rings][k];
                Vector3 c = new Vector3(a.x * 1.06f, -.25f, a.z * 1.06f), d = new Vector3(b.x * 1.06f, -.25f, b.z * 1.06f);
                var col = Shade(MountLow * .9f, Vector3.up, r, .04f);
                m.Tri(a, c, b, col, Vector3.up * -.1f); m.Tri(b, c, d, col, Vector3.up * -.1f);
            }
            return m;
        }
    }
}
