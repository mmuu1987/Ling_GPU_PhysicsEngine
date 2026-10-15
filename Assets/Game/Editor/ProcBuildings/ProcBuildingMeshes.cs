using System;
using System.Collections.Generic;
using UnityEngine;

namespace MassEngine.Game.Editor
{
    /// <summary>Building kinds of the procedural building library (2026-10-15, 档1 decoration only).</summary>
    public enum BK
    {
        Cottage, TownHouse, Longhouse, Barn, Windmill, Church, Inn,
        Well, Stall, Haystack, Cart, Crates, Woodpile, Fence, StoneFence,
        WallSeg, Tower, Gatehouse, Keep, Tent, Watchtower,
        RuinHouse, RuinTower, RuinWall, Rubble,
    }

    /// <summary>Colour set of one map theme (Green = default / autumn maps, Dry = canyon, Snow = snow plateau).</summary>
    public sealed class BPal
    {
        public string Name;
        public Color[] Walls, Roofs, Accents;
        public Color Timber, Stone, StoneDark, Door, Frame, Pane, Wood, WoodDark, Barn, Canvas, FlatTop, Snow, Hay, Rubble;
        public bool IsSnow, IsDry;
        public Color Top(Color c) => IsSnow ? Snow : c;
    }

    /// <summary>
    /// Deterministic low-poly building builders on top of <see cref="FlatMesh"/> (flat shading, one colour per face,
    /// same look as the procedural props). Local frame: floor centre at the origin, front (door / street side) faces −Z,
    /// a stone plinth reaches <see cref="Foundation"/> metres below the floor so a building can sit on gentle slopes.
    /// Sizes are real metres; the settlement layout uses <see cref="Footprint"/> for spacing.
    /// </summary>
    public static partial class ProcBuildingMeshes
    {
        public const float Foundation = 2.5f, Plinth = .3f;
        static Color H(string h) => ProcPropMeshes.Hex(h);

        public static readonly BPal Green = new BPal
        {
            Name = "Green",
            Walls = new[] { H("#F3E7CF"), H("#EFDDBF"), H("#F6EEDC"), H("#E9D6B4") },
            Roofs = new[] { H("#D65A3A"), H("#C9503A"), H("#E07B39"), H("#9A6440"), H("#5D7895") },
            Accents = new[] { H("#E8483F"), H("#3F7FC1"), H("#F2C94C"), H("#5BAA3F") },
            Timber = H("#7A5236"), Stone = H("#A9A498"), StoneDark = H("#8F8A80"), Door = H("#7A4A2C"), Frame = H("#F4EBDD"),
            Pane = H("#3E5168"), Wood = H("#B07A4A"), WoodDark = H("#6E4630"), Barn = H("#B8483A"), Canvas = H("#EFE3C6"),
            FlatTop = H("#CDB58C"), Snow = H("#F4F8FB"), Hay = H("#E9C25A"), Rubble = H("#9D9A90"),
        };
        public static readonly BPal Dry = new BPal
        {
            Name = "Dry", IsDry = true,
            Walls = new[] { H("#E8CFA0"), H("#DDBB86"), H("#EFDCB4"), H("#E3C392") },
            Roofs = new[] { H("#C8643E"), H("#B85A3A"), H("#D07A48") },
            Accents = new[] { H("#C8643E"), H("#3F8FA8"), H("#E8B04A"), H("#7FA65A") },
            Timber = H("#8A5A3B"), Stone = H("#C9AE82"), StoneDark = H("#B09670"), Door = H("#6F8FA0"), Frame = H("#F2E6CC"),
            Pane = H("#3B4A55"), Wood = H("#B8875A"), WoodDark = H("#7A5236"), Barn = H("#B9744A"), Canvas = H("#F0E2C0"),
            FlatTop = H("#D9BE8E"), Snow = H("#F4F8FB"), Hay = H("#E3C268"), Rubble = H("#C2A77E"),
        };
        public static readonly BPal Snowy = new BPal
        {
            Name = "Snow", IsSnow = true,
            Walls = new[] { H("#E9E2D3"), H("#DCD3C2"), H("#E4DCCB") },
            Roofs = new[] { H("#7B5A45"), H("#5E6E82"), H("#8A4E3A") },
            Accents = new[] { H("#C8463E"), H("#3F6FA8"), H("#E8B04A"), H("#4E8F5A") },
            Timber = H("#5E4130"), Stone = H("#9FA2A3"), StoneDark = H("#868A8C"), Door = H("#6A4029"), Frame = H("#EDE6DA"),
            Pane = H("#F2C46A"), Wood = H("#9A6A45"), WoodDark = H("#5E4130"), Barn = H("#9E3F35"), Canvas = H("#E8E2D4"),
            FlatTop = H("#F4F8FB"), Snow = H("#F4F8FB"), Hay = H("#D9B85A"), Rubble = H("#9A9C9C"),
        };
        public static readonly BPal[] Palettes = { Green, Dry, Snowy };
        public static BPal PaletteFor(string theme) => theme == "Dry" ? Dry : theme == "Snow" ? Snowy : Green;

        public static int Variants(BK k)
        {
            switch (k)
            {
                case BK.Cottage: return 3; case BK.TownHouse: return 4; case BK.Longhouse: return 2; case BK.Barn: return 2;
                case BK.Windmill: return 2; case BK.Church: return 2; case BK.Inn: return 2;
                case BK.Stall: return 3; case BK.Haystack: return 2; case BK.Crates: return 2;
                case BK.Tower: return 2; case BK.Keep: return 2; case BK.Tent: return 3; case BK.Watchtower: return 2;
                case BK.RuinHouse: return 3; case BK.RuinTower: return 2; case BK.RuinWall: return 2; case BK.Rubble: return 2;
                default: return 1;
            }
        }

        static readonly Vector2[] CottageDims = { new Vector2(6f, 5f), new Vector2(7f, 5.2f), new Vector2(5.5f, 5.5f) };
        static readonly Vector2[] TownDims = { new Vector2(6f, 6.5f), new Vector2(7f, 7f), new Vector2(5.6f, 7.5f), new Vector2(8f, 6.5f) };
        static readonly Vector2[] LongDims = { new Vector2(12f, 6f), new Vector2(14f, 6.5f) };
        static readonly Vector2[] BarnDims = { new Vector2(10f, 7f), new Vector2(12f, 8f) };
        static readonly Vector2[] KeepDims = { new Vector2(12f, 12f), new Vector2(14f, 11f) };
        static readonly Vector2[] RuinDims = { new Vector2(7f, 6f), new Vector2(9f, 6.5f), new Vector2(6f, 6f) };

        /// <summary>Ground footprint (x = facade width, y = depth) including roof overhang, for layout spacing.</summary>
        public static Vector2 Footprint(BK k, int v)
        {
            switch (k)
            {
                case BK.Cottage: return CottageDims[v] + new Vector2(1f, 1f);
                case BK.TownHouse: return TownDims[v] + new Vector2(1f, 1.4f);
                case BK.Longhouse: return LongDims[v] + new Vector2(1f, 1f);
                case BK.Barn: return BarnDims[v] + new Vector2(1f, 1f);
                case BK.Windmill: return new Vector2(7f, 8f);
                case BK.Church: return new Vector2(9.5f, 22f);
                case BK.Inn: return new Vector2(13f, 13f);
                case BK.Well: return new Vector2(3f, 3f);
                case BK.Stall: return new Vector2(3.6f, 2.8f);
                case BK.Haystack: return new Vector2(3.4f, 3.4f);
                case BK.Cart: return new Vector2(2.2f, 4.2f);
                case BK.Crates: return new Vector2(2.6f, 2.2f);
                case BK.Woodpile: return new Vector2(3f, 1.4f);
                case BK.Fence: case BK.StoneFence: return new Vector2(4f, .5f);
                case BK.WallSeg: return new Vector2(10f, 2.6f);
                case BK.Tower: return new Vector2(7.6f, 7.6f);
                case BK.Gatehouse: return new Vector2(14f, 6f);
                case BK.Keep: return KeepDims[v] + new Vector2(1.5f, 1.5f);
                case BK.Tent: return v == 0 ? new Vector2(3.4f, 4.4f) : new Vector2(5f, 5f);
                case BK.Watchtower: return new Vector2(4.5f, 4.5f);
                case BK.RuinHouse: return RuinDims[v];
                case BK.RuinTower: return new Vector2(7.5f, 7.5f);
                case BK.RuinWall: return new Vector2(9f, 1.6f);
                case BK.Rubble: return new Vector2(4f, 4f);
            }
            return new Vector2(4f, 4f);
        }

        /// <summary>True for kinds that get the stone plinth (and may stand on a slope).</summary>
        public static bool HasPlinth(BK k) => k <= BK.Inn || (k >= BK.WallSeg && k <= BK.Keep) || k == BK.RuinHouse || k == BK.RuinTower || k == BK.RuinWall;

        public static FlatMesh Build(BK k, int v, BPal p, bool far)
        {
            var b = new BB(7919 * ((int)k + 1) + 131 * v + p.Name.Length * 17, p, far);
            switch (k)
            {
                case BK.Cottage: Cottage(b, v); break;
                case BK.TownHouse: TownHouse(b, v); break;
                case BK.Longhouse: Longhouse(b, v); break;
                case BK.Barn: Barn(b, v); break;
                case BK.Windmill: Windmill(b, v); break;
                case BK.Church: Church(b, v); break;
                case BK.Inn: Inn(b, v); break;
                case BK.Well: Well(b); break;
                case BK.Stall: Stall(b, v); break;
                case BK.Haystack: Haystack(b, v); break;
                case BK.Cart: Cart(b); break;
                case BK.Crates: Crates(b, v); break;
                case BK.Woodpile: Woodpile(b); break;
                case BK.Fence: Fence(b); break;
                case BK.StoneFence: StoneFence(b); break;
                case BK.WallSeg: WallSeg(b); break;
                case BK.Tower: Tower(b, v); break;
                case BK.Gatehouse: Gatehouse(b); break;
                case BK.Keep: Keep(b, v); break;
                case BK.Tent: Tent(b, v); break;
                case BK.Watchtower: Watchtower(b, v); break;
                case BK.RuinHouse: RuinHouse(b, v); break;
                case BK.RuinTower: RuinTower(b, v); break;
                case BK.RuinWall: RuinWall(b, v); break;
                case BK.Rubble: Rubble(b, v); break;
            }
            return b.M;
        }

        // =============================================================== primitive builder
        sealed class BB
        {
            public readonly FlatMesh M = new FlatMesh();
            public readonly BPal P; public readonly bool Far;
            readonly System.Random r;
            public BB(int seed, BPal p, bool far) { r = new System.Random(seed); P = p; Far = far; }
            public float Rand(float a, float b) => a + (float)r.NextDouble() * (b - a);
            public int Next(int n) => r.Next(n);
            public bool Chance(float p) => r.NextDouble() < p;
            public Color Pick(Color[] set) => set[r.Next(set.Length)];
            Color J(Color c, float j = .025f) { float k = 1f + (float)(r.NextDouble() * 2 - 1) * j; return new Color(c.r * k, c.g * k, c.b * k, 1); }

            public void Tri(Vector3 a, Vector3 b, Vector3 c, Color col, Vector3 inside) => M.Tri(a, b, c, J(col), inside);
            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color col, Vector3 inside)
            {
                var cc = J(col);
                M.Tri(a, b, c, cc, inside); M.Tri(a, c, d, cc, inside);
            }

            /// <summary>Box without bottom face (unless asked); top face may have its own colour (snow).</summary>
            public void Box(Vector3 c, Vector3 s, Quaternion q, Color side, Color top, bool bottom = false)
            {
                Vector3 h = s * .5f;
                Vector3 Pt(float x, float y, float z) => c + q * new Vector3(x * h.x, y * h.y, z * h.z);
                Quad(Pt(-1, 1, -1), Pt(1, 1, -1), Pt(1, 1, 1), Pt(-1, 1, 1), top, c);
                if (bottom) Quad(Pt(-1, -1, -1), Pt(1, -1, -1), Pt(1, -1, 1), Pt(-1, -1, 1), side * .8f, c);
                Quad(Pt(-1, -1, -1), Pt(1, -1, -1), Pt(1, 1, -1), Pt(-1, 1, -1), side, c);
                Quad(Pt(-1, -1, 1), Pt(1, -1, 1), Pt(1, 1, 1), Pt(-1, 1, 1), side, c);
                Quad(Pt(-1, -1, -1), Pt(-1, -1, 1), Pt(-1, 1, 1), Pt(-1, 1, -1), side, c);
                Quad(Pt(1, -1, -1), Pt(1, -1, 1), Pt(1, 1, 1), Pt(1, 1, -1), side, c);
            }
            public void Box(Vector3 c, Vector3 s, Color side) => Box(c, s, Quaternion.identity, side, side);
            public void Box(Vector3 c, Vector3 s, Color side, Color top) => Box(c, s, Quaternion.identity, side, top);
            /// <summary>Box from a min corner on the ground plane (y = bottom).</summary>
            public void BoxB(float cx, float y0, float cz, float sx, float sy, float sz, Color side, Color top) =>
                Box(new Vector3(cx, y0 + sy * .5f, cz), new Vector3(sx, sy, sz), Quaternion.identity, side, top);

            /// <summary>n-gon frustum standing on <paramref name="b"/>; r1 = 0 gives a cone.</summary>
            public void Cyl(Vector3 b, float r0, float r1, float h, int n, Color side, Color top, float rot = 0f, bool cap = true)
            {
                var inside = b + Vector3.up * h * .5f;
                for (int i = 0; i < n; i++)
                {
                    float a0 = rot + i * Mathf.PI * 2f / n, a1 = rot + (i + 1) * Mathf.PI * 2f / n;
                    Vector3 d0 = new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)), d1 = new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1));
                    Vector3 p0 = b + d0 * r0, p1 = b + d1 * r0, q0 = b + d0 * r1 + Vector3.up * h, q1 = b + d1 * r1 + Vector3.up * h;
                    if (r1 <= .001f) Tri(p0, p1, b + Vector3.up * h, side, inside);
                    else
                    {
                        Quad(p0, p1, q1, q0, side, inside);
                        if (cap) Tri(q0, q1, b + Vector3.up * h, top, inside);
                    }
                }
            }

            /// <summary>Horizontal cylinder along <paramref name="axis"/> (wheels, logs, barrels on side).</summary>
            public void CylAxis(Vector3 c, Vector3 axis, float radius, float len, int n, Color side, Color ends)
            {
                axis.Normalize();
                Vector3 u = Vector3.Cross(axis, Mathf.Abs(axis.y) > .9f ? Vector3.right : Vector3.up).normalized, w = Vector3.Cross(axis, u);
                Vector3 a = c - axis * len * .5f, e = c + axis * len * .5f;
                for (int i = 0; i < n; i++)
                {
                    float t0 = i * Mathf.PI * 2f / n, t1 = (i + 1) * Mathf.PI * 2f / n;
                    Vector3 o0 = (u * Mathf.Cos(t0) + w * Mathf.Sin(t0)) * radius, o1 = (u * Mathf.Cos(t1) + w * Mathf.Sin(t1)) * radius;
                    Quad(a + o0, a + o1, e + o1, e + o0, side, c);
                    Tri(a + o0, a + o1, a, ends, c); Tri(e + o0, e + o1, e, ends, c);
                }
            }

            /// <summary>Gable roof, ridge along local X, eaves at <paramref name="c"/>.y, span along Z.</summary>
            public void Gable(Vector3 c, float len, float width, float rise, float over, Color roof, Color gableWall, bool endWalls = true, float thick = .28f)
            {
                float half = width * .5f, slant = Mathf.Sqrt(rise * rise + half * half) + over;
                Vector3 ridge = c + new Vector3(0, rise, 0);
                for (int s = -1; s <= 1; s += 2)
                {
                    Vector3 down = new Vector3(0, -rise, s * half).normalized, n = new Vector3(0, half, s * rise).normalized;
                    Vector3 mid = ridge + down * (slant * .5f) + n * thick * .5f;
                    Box(mid, new Vector3(len + 2f * over, thick, slant), Quaternion.LookRotation(down, n), roof, P.Top(roof), true);
                }
                Box(ridge + new Vector3(0, thick * .7f, 0), new Vector3(len + 2f * over, thick * 1.2f, thick * 1.5f), Quaternion.identity, roof * .85f, P.Top(roof * .85f));
                if (!endWalls) return;
                for (int s = -1; s <= 1; s += 2)
                {
                    float x = c.x + s * len * .5f;
                    Tri(new Vector3(x, c.y, c.z - half), new Vector3(x, c.y, c.z + half), new Vector3(x, c.y + rise, c.z), gableWall, new Vector3(c.x, c.y + rise * .3f, c.z));
                }
            }

            /// <summary>Same gable roof but with the ridge along Z (gable end faces the street).</summary>
            public void GableZ(Vector3 c, float len, float width, float rise, float over, Color roof, Color gableWall, float thick = .28f)
            {
                float half = width * .5f, slant = Mathf.Sqrt(rise * rise + half * half) + over;
                Vector3 ridge = c + new Vector3(0, rise, 0);
                for (int s = -1; s <= 1; s += 2)
                {
                    Vector3 down = new Vector3(s * half, -rise, 0).normalized, n = new Vector3(s * rise, half, 0).normalized;
                    Vector3 mid = ridge + down * (slant * .5f) + n * thick * .5f;
                    Box(mid, new Vector3(slant, thick, len + 2f * over), Quaternion.LookRotation(Vector3.forward, n), roof, P.Top(roof), true);
                }
                Box(ridge + new Vector3(0, thick * .7f, 0), new Vector3(thick * 1.5f, thick * 1.2f, len + 2f * over), Quaternion.identity, roof * .85f, P.Top(roof * .85f));
                for (int s = -1; s <= 1; s += 2)
                {
                    float z = c.z + s * len * .5f;
                    Tri(new Vector3(c.x - half, c.y, z), new Vector3(c.x + half, c.y, z), new Vector3(c.x, c.y + rise, z), gableWall, new Vector3(c.x, c.y + rise * .3f, c.z));
                }
            }

            /// <summary>Closed hip roof (pyramid when len == width), base at <paramref name="c"/>.y including overhang.</summary>
            public void Hip(Vector3 c, float len, float width, float rise, float over, Color roof)
            {
                float hx = len * .5f + over, hz = width * .5f + over, rx = Mathf.Max(0f, hx - hz);
                Vector3 a = c + new Vector3(-hx, 0, -hz), b = c + new Vector3(hx, 0, -hz), d = c + new Vector3(hx, 0, hz), e = c + new Vector3(-hx, 0, hz);
                Vector3 r0 = c + new Vector3(-rx, rise, 0), r1 = c + new Vector3(rx, rise, 0), inside = c + Vector3.up * rise * .3f;
                Color top = P.Top(roof);
                Quad(a, b, r1, r0, top, inside); Quad(d, e, r0, r1, top, inside);
                Tri(b, d, r1, top, inside); Tri(e, a, r0, top, inside);
                Quad(a, b, d, e, roof * .55f, inside + Vector3.up * 5f);
                if (P.IsSnow)   // roof colour fascia so the snow cap still reads as a roof
                {
                    Box(c + new Vector3(0, -.12f, -hz), new Vector3(2 * hx, .24f, .2f), roof);
                    Box(c + new Vector3(0, -.12f, hz), new Vector3(2 * hx, .24f, .2f), roof);
                    Box(c + new Vector3(-hx, -.12f, 0), new Vector3(.2f, .24f, 2 * hz), roof);
                    Box(c + new Vector3(hx, -.12f, 0), new Vector3(.2f, .24f, 2 * hz), roof);
                }
            }

            /// <summary>Flat roof with a low parapet (dry palette).</summary>
            public void FlatRoof(Vector3 c, float len, float width, Color wall)
            {
                Box(c + new Vector3(0, .12f, 0), new Vector3(len + .2f, .24f, width + .2f), wall, P.FlatTop);
                float t = .25f, ph = .55f;
                Box(c + new Vector3(0, .24f + ph * .5f, -width * .5f), new Vector3(len + .2f, ph, t), wall, P.Top(wall));
                Box(c + new Vector3(0, .24f + ph * .5f, width * .5f), new Vector3(len + .2f, ph, t), wall, P.Top(wall));
                Box(c + new Vector3(-len * .5f, .24f + ph * .5f, 0), new Vector3(t, ph, width), wall, P.Top(wall));
                Box(c + new Vector3(len * .5f, .24f + ph * .5f, 0), new Vector3(t, ph, width), wall, P.Top(wall));
            }

            /// <summary>Window / door on a wall face: frame box + dark pane quad (+ optional shutters).</summary>
            public void Opening(Vector3 face, Vector3 outward, float w, float h, Color frame, Color pane, bool shutters = false, Color shutter = default)
            {
                if (Far) return;
                var q = Quaternion.LookRotation(outward, Vector3.up);
                Vector3 right = q * Vector3.right;
                Box(face + outward * .04f, new Vector3(w + .22f, h + .22f, .08f), q, frame, frame);
                Vector3 c = face + outward * .085f;
                Vector3 hr = right * w * .5f, hu = Vector3.up * h * .5f;
                Quad(c - hr - hu, c + hr - hu, c + hr + hu, c - hr + hu, pane, c - outward);
                if (shutters)
                {
                    Box(face + outward * .06f - right * (w * .5f + .35f), new Vector3(.5f, h + .1f, .07f), q, shutter, shutter);
                    Box(face + outward * .06f + right * (w * .5f + .35f), new Vector3(.5f, h + .1f, .07f), q, shutter, shutter);
                }
            }

            public void Door(Vector3 faceBottom, Vector3 outward, float w, float h, Color frame, Color door)
            {
                if (Far) return;
                var q = Quaternion.LookRotation(outward, Vector3.up);
                Box(faceBottom + Vector3.up * (h * .5f + .05f) + outward * .04f, new Vector3(w + .3f, h + .2f, .08f), q, frame, frame);
                Box(faceBottom + Vector3.up * (h * .5f) + outward * .07f, new Vector3(w, h, .06f), q, door, door);
                Box(faceBottom + Vector3.up * .08f + outward * .4f, new Vector3(w + .6f, .16f, .7f), q, P.Stone, P.Top(P.Stone));
            }

            /// <summary>Thin beam between two points lying on a wall face (half-timbering).</summary>
            public void Beam(Vector3 a, Vector3 b, Vector3 outward, float width, Color col)
            {
                if (Far) return;
                Vector3 d = b - a; float len = d.magnitude; if (len < .05f) return;
                var q = Quaternion.LookRotation(outward, d / len);
                Box((a + b) * .5f + outward * .04f, new Vector3(width, len, .08f), q, col, col);
            }

            public void Base(float w, float d, Color stone)
            {
                Box(new Vector3(0, (Plinth - Foundation) * .5f, 0), new Vector3(w + .3f, Foundation + Plinth, d + .3f), Quaternion.identity, stone, P.Top(stone));
            }

            public void Chimney(Vector3 baseOnRoof, float h, Color stone)
            {
                Box(baseOnRoof + Vector3.up * h * .5f, new Vector3(.7f, h, .7f), stone, P.Top(stone * .6f));
                if (!Far) Box(baseOnRoof + Vector3.up * (h + .08f), new Vector3(.9f, .16f, .9f), stone * .9f, P.Top(stone * .5f));
            }

            /// <summary>Crenellated parapet along a segment (merlons on top of a wall at height y).</summary>
            public void Merlons(Vector3 a, Vector3 b, float y, float thick, Color stone, float step = 1.4f)
            {
                if (Far) { Box(new Vector3((a.x + b.x) * .5f, y + .35f, (a.z + b.z) * .5f), new Vector3(Mathf.Max(thick, Mathf.Abs(b.x - a.x)), .7f, Mathf.Max(thick, Mathf.Abs(b.z - a.z))), stone, P.Top(stone)); return; }
                Vector3 d = b - a; float len = d.magnitude; int n = Mathf.Max(1, Mathf.RoundToInt(len / step));
                var q = Quaternion.LookRotation(Vector3.Cross(d / len, Vector3.up), Vector3.up);
                for (int i = 0; i < n; i++)
                {
                    Vector3 c = a + d * ((i + .5f) / n);
                    Box(new Vector3(c.x, y + .45f, c.z), new Vector3(len / n * .55f, .9f, thick), q, stone, P.Top(stone));
                }
            }

            /// <summary>Ring of merlons on a round tower.</summary>
            public void MerlonRing(Vector3 c, float radius, float y, int n, Color stone)
            {
                for (int i = 0; i < n; i++)
                {
                    float a = (i + .5f) * Mathf.PI * 2f / n;
                    var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                    Box(c + dir * radius + Vector3.up * (y + .45f), new Vector3(radius * Mathf.PI * 2f / n * .55f, .9f, .6f), Quaternion.LookRotation(dir), stone, P.Top(stone));
                    if (Far) { i++; }
                }
            }

            /// <summary>Dark slit / opening quad on a face (arrow slits, belfry openings).</summary>
            public void Slit(Vector3 face, Vector3 outward, float w, float h, Color col)
            {
                if (Far) return;
                Vector3 right = Vector3.Cross(Vector3.up, outward).normalized;
                Vector3 c = face + outward * .03f; Vector3 hr = right * w * .5f, hu = Vector3.up * h * .5f;
                Quad(c - hr - hu, c + hr - hu, c + hr + hu, c - hr + hu, col, c - outward);
            }
        }

        // =============================================================== helpers for wall faces
        static readonly Vector3 Front = Vector3.back, Back = Vector3.forward, Left = Vector3.left, Right = Vector3.right;

        /// <summary>Evenly spaced openings along a wall of length <paramref name="len"/> (axis along X for front/back).</summary>
        static void Windows(BB b, float len, float depthHalf, float y, int count, Vector3 outward, float w, float h, bool shutters, Color shutter, float skipCenter = 0f)
        {
            for (int i = 0; i < count; i++)
            {
                float t = (i + .5f) / count * len - len * .5f;
                if (skipCenter > 0f && Mathf.Abs(t) < skipCenter) continue;
                Vector3 face = outward.z != 0 ? new Vector3(t, y, outward.z * depthHalf) : new Vector3(outward.x * depthHalf, y, t);
                b.Opening(face, outward, w, h, b.P.Frame, b.P.Pane, shutters, shutter);
            }
        }

        static void HalfTimber(BB b, float w, float d, float y0, float y1)
        {
            if (b.Far) return;
            Color t = b.P.Timber;
            float hw = w * .5f + .01f, hd = d * .5f + .01f;
            foreach (var o in new[] { Front, Back })
            {
                float z = o.z * hd;
                b.Beam(new Vector3(-w * .5f, y0 + .1f, z), new Vector3(w * .5f, y0 + .1f, z), o, .22f, t);
                b.Beam(new Vector3(-w * .5f, y1 - .1f, z), new Vector3(w * .5f, y1 - .1f, z), o, .22f, t);
                int posts = Mathf.Max(2, Mathf.RoundToInt(w / 1.8f));
                for (int i = 0; i <= posts; i++)
                {
                    float x = -w * .5f + w * i / posts;
                    b.Beam(new Vector3(x, y0, z), new Vector3(x, y1, z), o, .2f, t);
                }
                b.Beam(new Vector3(-w * .5f, y0 + .1f, z), new Vector3(-w * .5f + w / posts, y1 - .1f, z), o, .16f, t);
                b.Beam(new Vector3(w * .5f, y0 + .1f, z), new Vector3(w * .5f - w / posts, y1 - .1f, z), o, .16f, t);
            }
            foreach (var o in new[] { Left, Right })
            {
                float x = o.x * hw;
                b.Beam(new Vector3(x, y0 + .1f, -d * .5f), new Vector3(x, y0 + .1f, d * .5f), o, .22f, t);
                b.Beam(new Vector3(x, y1 - .1f, -d * .5f), new Vector3(x, y1 - .1f, d * .5f), o, .22f, t);
                b.Beam(new Vector3(x, y0, 0), new Vector3(x, y1, 0), o, .2f, t);
            }
        }

        static Color Shutter(BB b) => b.Pick(b.P.Accents) * .9f;

        // =============================================================== houses
        static void Cottage(BB b, int v)
        {
            var p = b.P; Vector2 dim = CottageDims[v]; float w = dim.x, d = dim.y, wh = 3.1f;
            Color wall = b.Pick(p.Walls), roof = b.Pick(p.Roofs), stone = p.Stone;
            b.Base(w, d, stone);
            b.BoxB(0, Plinth, 0, w, wh, d, wall, wall);
            float y = Plinth + wh;
            if (p.IsDry && v != 1) b.FlatRoof(new Vector3(0, y, 0), w, d, wall);
            else if (p.IsDry) b.Hip(new Vector3(0, y, 0), w, d, 1.4f, .4f, roof);
            else b.Gable(new Vector3(0, y, 0), w, d, p.IsSnow ? 3f : 2.4f, .45f, roof, wall);
            bool sh = !p.IsDry && b.Chance(.6f); Color shc = Shutter(b);
            float doorX = v == 1 ? -w * .22f : 0f;
            b.Door(new Vector3(doorX, Plinth, -d * .5f), Front, 1.1f, 2f, p.Frame, p.Door);
            for (int i = -1; i <= 1; i += 2) if (Mathf.Abs(i * w * .28f - doorX) > 1.1f) b.Opening(new Vector3(i * w * .28f, Plinth + 1.7f, -d * .5f), Front, .9f, 1f, p.Frame, p.Pane, sh, shc);
            Windows(b, w, d * .5f, Plinth + 1.7f, 2, Back, .9f, 1f, sh, shc);
            Windows(b, d, w * .5f, Plinth + 1.7f, 1, Left, .8f, .9f, false, shc);
            Windows(b, d, w * .5f, Plinth + 1.7f, 1, Right, .8f, .9f, false, shc);
            if (!(p.IsDry && v != 1)) b.Chimney(new Vector3(w * .3f, y + .8f, d * .18f), 2.2f, p.StoneDark);
            else if (!b.Far) { b.BoxB(-w * .25f, y + .24f, d * .2f, 1.2f, .5f, 1.2f, p.Wood, p.Wood); }
        }

        static void TownHouse(BB b, int v)
        {
            var p = b.P; Vector2 dim = TownDims[v]; float w = dim.x, d = dim.y, g = 3f, u = 2.8f, jet = p.IsDry ? 0f : .4f;
            Color wall = b.Pick(p.Walls), roof = b.Pick(p.Roofs), stone = p.Stone, lower = p.IsDry ? wall : (b.Chance(.5f) ? stone : wall);
            b.Base(w, d, stone);
            b.BoxB(0, Plinth, 0, w, g, d, lower, lower);
            float y1 = Plinth + g, y2 = y1 + u;
            b.BoxB(0, y1, 0, w, u, d + jet * 2f, wall, wall);
            if (!p.IsDry) HalfTimber(b, w, d + jet * 2f, y1, y2);
            bool gableFront = v % 2 == 1;
            if (p.IsDry) b.FlatRoof(new Vector3(0, y2, 0), w, d, wall);
            else if (gableFront) b.GableZ(new Vector3(0, y2, 0), d + jet * 2f, w, p.IsSnow ? 4f : 3.2f, .4f, roof, wall);
            else b.Gable(new Vector3(0, y2, 0), w, d + jet * 2f, p.IsSnow ? 3.6f : 2.9f, .45f, roof, wall);
            bool sh = !p.IsDry && b.Chance(.5f); Color shc = Shutter(b);
            float fz = -d * .5f, uz = -d * .5f - jet;
            b.Door(new Vector3(-w * .25f, Plinth, fz), Front, 1.1f, 2.1f, p.Frame, p.Door);
            b.Opening(new Vector3(w * .2f, Plinth + 1.6f, fz), Front, 1.4f, 1.1f, p.Frame, p.Pane, false, shc);
            int up = w > 6.5f ? 3 : 2;
            for (int i = 0; i < up; i++) b.Opening(new Vector3(((i + .5f) / up - .5f) * w, y1 + 1.4f, uz), Front, .85f, 1.05f, p.Frame, p.Pane, sh, shc);
            Windows(b, w, d * .5f + jet, y1 + 1.4f, 2, Back, .85f, 1f, sh, shc);
            Windows(b, w, d * .5f, Plinth + 1.6f, 1, Back, .9f, 1f, false, shc);
            Windows(b, d, w * .5f, y1 + 1.4f, 1, Left, .8f, .9f, false, shc);
            if (gableFront && !p.IsDry) b.Opening(new Vector3(0, y2 + 1.1f, uz), Front, .6f, .7f, p.Frame, p.Pane);
            if (!p.IsDry) b.Chimney(new Vector3(gableFront ? w * .28f : w * .3f, y2 + (gableFront ? .9f : 1.2f), d * .2f), 2.4f, p.StoneDark);
            else if (!b.Far)
            {
                b.BoxB(w * .25f, y2 + .24f, -d * .2f, 1.4f, 1.6f, 1.4f, wall, p.FlatTop);   // stair house on the roof terrace
                b.Box(new Vector3(-w * .2f, y2 + 1.3f, 0), new Vector3(2.2f, .08f, 1.6f), b.Pick(p.Accents), b.Pick(p.Accents));   // awning
                b.Box(new Vector3(-w * .2f - 1f, y2 + .8f, -.7f), new Vector3(.1f, 1f, .1f), p.Wood);
                b.Box(new Vector3(-w * .2f + 1f, y2 + .8f, -.7f), new Vector3(.1f, 1f, .1f), p.Wood);
            }
        }

        static void Longhouse(BB b, int v)
        {
            var p = b.P; Vector2 dim = LongDims[v]; float w = dim.x, d = dim.y, wh = 3.2f;
            Color wall = b.Pick(p.Walls), roof = b.Pick(p.Roofs);
            b.Base(w, d, p.Stone);
            b.BoxB(0, Plinth, 0, w, wh, d, wall, wall);
            float y = Plinth + wh;
            if (p.IsDry) b.FlatRoof(new Vector3(0, y, 0), w, d, wall);
            else if (v == 0) b.Hip(new Vector3(0, y, 0), w, d, p.IsSnow ? 3.4f : 2.8f, .5f, roof);
            else b.Gable(new Vector3(0, y, 0), w, d, p.IsSnow ? 3.4f : 2.8f, .5f, roof, wall);
            bool sh = !p.IsDry && b.Chance(.6f); Color shc = Shutter(b);
            b.Door(new Vector3(-w * .12f, Plinth, -d * .5f), Front, 1.2f, 2.1f, p.Frame, p.Door);
            foreach (float x in new[] { -w * .38f, w * .1f, w * .33f }) b.Opening(new Vector3(x, Plinth + 1.7f, -d * .5f), Front, .9f, 1f, p.Frame, p.Pane, sh, shc);
            Windows(b, w, d * .5f, Plinth + 1.7f, 3, Back, .9f, 1f, sh, shc);
            Windows(b, d, w * .5f, Plinth + 1.7f, 1, Left, .8f, .9f, false, shc);
            if (!p.IsDry) { b.Chimney(new Vector3(-w * .3f, y + 1f, 0f), 2.3f, p.StoneDark); b.Chimney(new Vector3(w * .32f, y + 1f, 0f), 2f, p.StoneDark); }
            if (!b.Far && !p.IsDry)   // lean-to porch over the door
            {
                b.Box(new Vector3(-w * .12f, Plinth + 2.55f, -d * .5f - .7f), new Vector3(2.4f, .12f, 1.5f), Quaternion.Euler(-12f, 0, 0), roof, p.Top(roof));
                b.Box(new Vector3(-w * .12f - 1.05f, Plinth + 1.2f, -d * .5f - 1.25f), new Vector3(.14f, 2.4f, .14f), p.Timber);
                b.Box(new Vector3(-w * .12f + 1.05f, Plinth + 1.2f, -d * .5f - 1.25f), new Vector3(.14f, 2.4f, .14f), p.Timber);
            }
        }

        static void Barn(BB b, int v)
        {
            var p = b.P; Vector2 dim = BarnDims[v]; float w = dim.x, d = dim.y, wh = 4.2f;
            Color wall = p.IsDry ? b.Pick(p.Walls) : p.Barn, roof = p.IsDry ? b.Pick(p.Roofs) : b.Pick(new[] { p.Roofs[p.Roofs.Length - 1], p.Roofs[0], p.WoodDark });
            b.Base(w, d, p.Stone);
            b.BoxB(0, Plinth, 0, w, wh, d, wall, wall);
            float y = Plinth + wh;
            b.Gable(new Vector3(0, y, 0), w, d, p.IsSnow ? 4f : 3.4f, .5f, roof, wall);
            if (!b.Far)
            {
                Color trim = p.Frame;
                // big double door with X-braces on the front
                b.Box(new Vector3(0, Plinth + 1.75f, -d * .5f - .05f), new Vector3(3.6f, 3.5f, .1f), p.WoodDark, p.WoodDark);
                b.Beam(new Vector3(-1.8f, Plinth, -d * .5f - .06f), new Vector3(1.8f, Plinth + 3.5f, -d * .5f - .06f), Front, .2f, trim);
                b.Beam(new Vector3(1.8f, Plinth, -d * .5f - .06f), new Vector3(-1.8f, Plinth + 3.5f, -d * .5f - .06f), Front, .2f, trim);
                b.Beam(new Vector3(-1.9f, Plinth + 3.6f, -d * .5f - .06f), new Vector3(1.9f, Plinth + 3.6f, -d * .5f - .06f), Front, .22f, trim);
                foreach (var o in new[] { Front, Back })
                    for (int s = -1; s <= 1; s += 2) b.Beam(new Vector3(s * w * .5f, Plinth, o.z * d * .5f), new Vector3(s * w * .5f, y, o.z * d * .5f), o, .25f, trim);
                b.Opening(new Vector3(w * .5f, y + 1.2f, 0), Right, .9f, .9f, trim, p.WoodDark);
                b.Opening(new Vector3(-w * .5f, y + 1.2f, 0), Left, .9f, .9f, trim, p.WoodDark);
                Windows(b, w, d * .5f, Plinth + 2.4f, 2, Back, .8f, .8f, false, trim);
            }
        }

        static void Windmill(BB b, int v)
        {
            var p = b.P; float r0 = 3.1f, r1 = 2.3f, h = 9f;
            Color wall = v == 0 ? p.Stone : b.Pick(p.Walls), roof = b.Pick(p.Roofs);
            b.Cyl(new Vector3(0, -Foundation, 0), r0 + .3f, r0 + .3f, Foundation + Plinth, 8, p.Stone, p.Top(p.Stone), Mathf.PI / 8f);
            b.Cyl(new Vector3(0, Plinth, 0), r0, r1, h, 8, wall, wall, Mathf.PI / 8f);
            b.Cyl(new Vector3(0, Plinth + h, 0), r1 + .45f, 0f, 3.2f, 8, p.Top(roof), p.Top(roof), Mathf.PI / 8f);
            if (p.IsSnow) b.Cyl(new Vector3(0, Plinth + h - .15f, 0), r1 + .5f, r1 + .5f, .25f, 8, roof, roof, Mathf.PI / 8f, false);
            b.Door(new Vector3(0, Plinth, -r0 + .2f), Front, 1.1f, 2f, p.Frame, p.Door);
            b.Opening(new Vector3(0, Plinth + 5f, -(r0 + r1) * .5f + .1f), Front, .7f, .9f, p.Frame, p.Pane);
            // hub + 4 sails on the front
            Vector3 hub = new Vector3(0, Plinth + h - .4f, -r1 - .9f);
            b.CylAxis(hub + new Vector3(0, 0, .4f), Vector3.forward, .35f, 1.4f, 6, p.WoodDark, p.WoodDark);
            float a0 = v * 20f + 15f;
            for (int i = 0; i < 4; i++)
            {
                var q = Quaternion.Euler(0, 0, a0 + i * 90f);
                Vector3 dir = q * Vector3.up, side = q * Vector3.right;
                b.Box(hub + dir * 3.6f, new Vector3(.25f, 7.2f, .2f), q, p.WoodDark, p.WoodDark);
                if (b.Far) continue;
                b.Box(hub + dir * 4.3f + side * .75f + Vector3.back * .05f, new Vector3(1.3f, 5.2f, .06f), q, p.Canvas, p.Canvas);
                for (int k = 0; k < 3; k++) b.Box(hub + dir * (2.2f + k * 2f) + side * .75f + Vector3.back * .1f, new Vector3(1.4f, .1f, .06f), q, p.Wood, p.Wood);
            }
        }

        static void Church(BB b, int v)
        {
            var p = b.P; float w = 8f, len = 15f, wh = 6f;
            Color wall = v == 0 ? p.Stone : b.Pick(p.Walls), roof = b.Pick(p.Roofs), trim = p.StoneDark;
            float nz = 2.5f;   // nave centre z (tower in front)
            b.Box(new Vector3(0, (Plinth - Foundation) * .5f, 1.2f), new Vector3(w + .4f, Foundation + Plinth, 20.6f), Quaternion.identity, p.Stone, p.Top(p.Stone));
            b.BoxB(0, Plinth, nz, w, wh, len, wall, wall);
            b.GableZ(new Vector3(0, Plinth + wh, nz), len, w, p.IsSnow ? 5f : 4.4f, .45f, roof, wall);
            for (int i = 0; i < 4; i++)
            {
                float z = nz - len * .5f + (i + .7f) * len / 4.4f;
                b.Opening(new Vector3(-w * .5f, Plinth + 3.2f, z), Left, .8f, 2.4f, trim, p.IsSnow ? p.Pane : H("#5B7FA8"));
                b.Opening(new Vector3(w * .5f, Plinth + 3.2f, z), Right, .8f, 2.4f, trim, p.IsSnow ? p.Pane : H("#5B7FA8"));
                if (!b.Far) { b.BoxB(-w * .5f - .25f, Plinth, z + len / 8.8f, .5f, wh - .6f, .6f, trim, p.Top(trim)); b.BoxB(w * .5f + .25f, Plinth, z + len / 8.8f, .5f, wh - .6f, .6f, trim, p.Top(trim)); }
            }
            // apse at the back
            b.Cyl(new Vector3(0, Plinth, nz + len * .5f), 3.2f, 3.2f, wh - 1.2f, 8, wall, wall, Mathf.PI / 8f);
            b.Cyl(new Vector3(0, Plinth + wh - 1.2f, nz + len * .5f), 3.5f, 0f, 2.6f, 8, p.Top(roof), p.Top(roof), Mathf.PI / 8f);
            // front tower
            float tw = 4.6f, th = 13.5f, tz = nz - len * .5f - 1.6f;
            b.BoxB(0, Plinth, tz, tw, th, tw, wall, wall);
            b.Door(new Vector3(0, Plinth, tz - tw * .5f), Front, 1.6f, 2.8f, trim, p.Door);
            b.Opening(new Vector3(0, Plinth + 6.2f, tz - tw * .5f), Front, .9f, 1.6f, trim, p.Pane);
            foreach (var o in new[] { Front, Left, Right, Back })
            {
                Vector3 face = new Vector3(o.x * tw * .5f, Plinth + th - 2f, tz + o.z * tw * .5f);
                b.Opening(face, o, 1.3f, 1.9f, trim, H("#2E3440"));
            }
            b.BoxB(0, Plinth + th, tz, tw + .5f, .4f, tw + .5f, trim, p.Top(trim));
            if (p.IsDry) { b.Cyl(new Vector3(0, Plinth + th + .4f, tz), 2.2f, 1.2f, 1.4f, 8, wall, p.Top(wall)); b.Cyl(new Vector3(0, Plinth + th + 1.8f, tz), 1.2f, 0f, 1.3f, 8, roof, roof); }
            else b.Cyl(new Vector3(0, Plinth + th + .4f, tz), tw * .72f, 0f, v == 0 ? 7.5f : 5.5f, 4, p.Top(roof), p.Top(roof), Mathf.PI / 4f);
        }

        static void Inn(BB b, int v)
        {
            var p = b.P; float w = 12f, d = 7f, g = 3.2f, u = 2.9f;
            Color wall = b.Pick(p.Walls), roof = b.Pick(p.Roofs), stone = p.Stone;
            b.Box(new Vector3(0, (Plinth - Foundation) * .5f, 2.75f), new Vector3(w + .4f, Foundation + Plinth, 12.9f), Quaternion.identity, stone, p.Top(stone));
            // main block (front) and back wing at the right end
            b.BoxB(0, Plinth, 0, w, g, d, p.IsDry ? wall : stone, p.IsDry ? wall : stone);
            b.BoxB(0, Plinth + g, 0, w, u, d, wall, wall);
            float y2 = Plinth + g + u, wx = w * .5f - 3f;
            b.BoxB(wx, Plinth, 6.25f, 6f, g + u, 5.5f, wall, wall);
            if (p.IsDry)
            {
                b.FlatRoof(new Vector3(0, y2, 0), w, d, wall);
                b.FlatRoof(new Vector3(wx, y2, 6.25f), 6f, 5.5f, wall);
            }
            else
            {
                HalfTimber(b, w, d, Plinth + g, y2);
                b.Gable(new Vector3(0, y2, 0), w, d, p.IsSnow ? 3.8f : 3.2f, .5f, roof, wall);
                b.GableZ(new Vector3(wx, y2, 6.6f), 6.2f, 6f, p.IsSnow ? 3.3f : 2.8f, .4f, roof, wall);
                b.Chimney(new Vector3(-w * .32f, y2 + 1.4f, .6f), 2.6f, p.StoneDark);
                b.Chimney(new Vector3(wx, y2 + 1.6f, 7.8f), 2f, p.StoneDark);
            }
            bool sh = b.Chance(.5f) && !p.IsDry; Color shc = Shutter(b);
            b.Door(new Vector3(-1f, Plinth, -d * .5f), Front, 1.6f, 2.3f, p.Frame, p.Door);
            foreach (float x in new[] { -4.4f, 2.4f, 4.6f }) b.Opening(new Vector3(x, Plinth + 1.7f, -d * .5f), Front, 1.3f, 1.1f, p.Frame, p.Pane, false, shc);
            Windows(b, w, d * .5f, Plinth + g + 1.4f, 4, Front, .9f, 1.05f, sh, shc);
            Windows(b, d, w * .5f, Plinth + g + 1.4f, 2, Left, .9f, 1f, sh, shc);
            if (!b.Far)   // hanging sign
            {
                b.Box(new Vector3(-3f, Plinth + g + .2f, -d * .5f - .6f), new Vector3(.12f, .12f, 1.2f), p.WoodDark);
                b.Box(new Vector3(-3f, Plinth + g - .45f, -d * .5f - 1f), new Vector3(.08f, .9f, .8f), b.Pick(p.Accents), p.Wood);
                b.BoxB(-6.5f + .2f, Plinth, -d * .5f - 1.2f, .5f, .9f, .5f, p.WoodDark, p.Wood);    // barrels by the door
                b.BoxB(-5.8f + .2f, Plinth, -d * .5f - 1.1f, .5f, .9f, .5f, p.WoodDark, p.Wood);
            }
        }

        // =============================================================== small village props
        static void Well(BB b)
        {
            var p = b.P;
            b.Cyl(new Vector3(0, -.3f, 0), 1.15f, 1.15f, 1.2f, 8, p.Stone, p.Pane * .6f, Mathf.PI / 8f);
            b.Box(new Vector3(-1f, 1.3f, 0), new Vector3(.18f, 2.4f, .18f), p.WoodDark);
            b.Box(new Vector3(1f, 1.3f, 0), new Vector3(.18f, 2.4f, .18f), p.WoodDark);
            b.Gable(new Vector3(0, 2.4f, 0), 2.6f, 2f, .8f, .2f, b.Pick(p.Roofs), p.WoodDark, false, .14f);
            b.CylAxis(new Vector3(0, 1.8f, 0), Vector3.right, .12f, 1.9f, 6, p.Wood, p.Wood);
            if (!b.Far) b.Cyl(new Vector3(0, 1f, 0), .25f, .3f, .45f, 6, p.Wood, p.WoodDark);
        }

        static void Stall(BB b, int v)
        {
            var p = b.P; Color a = p.Accents[v % p.Accents.Length], c = p.Canvas;
            for (int sx = -1; sx <= 1; sx += 2) for (int sz = -1; sz <= 1; sz += 2)
                b.Box(new Vector3(sx * 1.6f, 1.15f, sz * 1.15f), new Vector3(.12f, 2.3f + (sz > 0 ? .4f : 0f), .12f), p.WoodDark);
            b.BoxB(0, 0, -.6f, 3.2f, 1f, 1f, p.Wood, p.Wood);
            for (int i = 0; i < 4; i++)
                b.Box(new Vector3(-1.35f + i * .9f, 2.6f, 0), new Vector3(.9f, .07f, 2.9f), Quaternion.Euler(-11f, 0, 0), i % 2 == 0 ? a : c, p.Top(i % 2 == 0 ? a : c));
            if (b.Far) return;
            for (int i = 0; i < 4; i++) b.Box(new Vector3(-1.1f + i * .72f, 1.15f, -.6f), new Vector3(.45f, .3f, .45f), p.Accents[(i + v + 1) % p.Accents.Length], p.Accents[(i + v + 2) % p.Accents.Length]);
        }

        static void Haystack(BB b, int v)
        {
            var p = b.P;
            if (v == 0)
            {
                b.Cyl(new Vector3(0, -.2f, 0), 1.6f, 1.45f, 1.6f, 9, p.Hay, p.Hay);
                b.Cyl(new Vector3(0, 1.4f, 0), 1.45f, 0f, 1.5f, 9, p.Top(p.Hay), p.Top(p.Hay));
            }
            else
            {
                b.BoxB(-.6f, -.1f, 0, 1.2f, .9f, 2.2f, p.Hay, p.Top(p.Hay));
                b.BoxB(.65f, -.1f, .1f, 1.2f, .9f, 2.2f, p.Hay * .95f, p.Top(p.Hay));
                b.BoxB(0, .8f, .05f, 1.2f, .9f, 2.2f, p.Hay * 1.03f, p.Top(p.Hay));
            }
        }

        static void Cart(BB b)
        {
            var p = b.P;
            b.BoxB(0, .75f, 0, 1.7f, .5f, 2.8f, p.Wood, p.WoodDark);
            b.CylAxis(new Vector3(-1f, .55f, .3f), Vector3.right, .55f, .14f, 8, p.WoodDark, p.Wood);
            b.CylAxis(new Vector3(1f, .55f, .3f), Vector3.right, .55f, .14f, 8, p.WoodDark, p.Wood);
            b.Box(new Vector3(-.4f, .7f, -1.9f), new Vector3(.1f, .1f, 1.6f), Quaternion.Euler(-12f, 0, 0), p.WoodDark, p.WoodDark);
            b.Box(new Vector3(.4f, .7f, -1.9f), new Vector3(.1f, .1f, 1.6f), Quaternion.Euler(-12f, 0, 0), p.WoodDark, p.WoodDark);
            if (!b.Far) b.BoxB(0, 1.25f, .2f, 1.3f, .5f, 1.8f, p.Hay, p.Top(p.Hay));
        }

        static void Crates(BB b, int v)
        {
            var p = b.P;
            b.BoxB(-.5f, 0, 0, 1f, 1f, 1f, p.Wood, p.Top(p.Wood));
            b.BoxB(.6f, 0, .1f, .9f, .9f, .9f, p.Wood * .92f, p.Top(p.Wood));
            if (v == 0) b.BoxB(-.4f, 1f, .05f, .8f, .8f, .8f, p.Wood * 1.05f, p.Top(p.Wood));
            b.Cyl(new Vector3(.2f, 0, -.85f), .38f, .38f, .95f, 7, p.WoodDark, p.Top(p.Wood));
            if (v == 1) b.Cyl(new Vector3(-.6f, 0, -.8f), .38f, .38f, .95f, 7, p.WoodDark, p.Top(p.Wood));
        }

        static void Woodpile(BB b)
        {
            var p = b.P;
            for (int row = 0; row < 3; row++)
                for (int i = 0; i < 5 - row; i++)
                    b.CylAxis(new Vector3(-1.1f + row * .27f + i * .55f, .25f + row * .46f, 0), Vector3.forward, .25f, 1.2f, 6, p.Wood, p.Hay * .9f);
            b.Box(new Vector3(0, 1.45f, 0), new Vector3(3f, .1f, 1.4f), Quaternion.Euler(0, 0, 4f), p.Top(p.WoodDark), p.Top(p.WoodDark));
        }

        static void Fence(BB b)
        {
            var p = b.P;
            for (int i = 0; i < 3; i++) b.BoxB(-1.9f + i * 1.9f, -.2f, 0, .16f, 1.3f, .16f, p.WoodDark, p.Top(p.WoodDark));
            b.Box(new Vector3(0, .5f, 0), new Vector3(4f, .1f, .08f), p.Wood);
            b.Box(new Vector3(0, .95f, 0), new Vector3(4f, .1f, .08f), p.Wood, p.Top(p.Wood));
        }

        static void StoneFence(BB b)
        {
            var p = b.P;
            for (int i = 0; i < 4; i++) b.BoxB(-1.5f + i, -.2f, b.Rand(-.05f, .05f), 1.05f, b.Rand(.85f, 1.05f), .55f, b.Rand(0f, 1f) < .5f ? p.Stone : p.StoneDark, p.Top(p.Stone));
        }

        // =============================================================== fort
        static void WallSeg(BB b)
        {
            var p = b.P; float L = 10f, t = 2.4f, h = 6.4f;
            b.Box(new Vector3(0, (h - Foundation) * .5f, 0), new Vector3(L, h + Foundation, t), Quaternion.identity, p.Stone, p.Top(p.StoneDark));
            b.Merlons(new Vector3(-L * .5f, 0, -t * .5f + .3f), new Vector3(L * .5f, 0, -t * .5f + .3f), h, .6f, p.Stone);
            if (b.Far) return;
            b.Box(new Vector3(0, h + .3f, t * .5f - .15f), new Vector3(L, .6f, .3f), p.Stone, p.Top(p.Stone));
            for (int i = 0; i < 5; i++) b.Box(new Vector3(-L * .5f + 1f + i * 2f + b.Rand(-.3f, .3f), b.Rand(1f, 5f), -t * .5f - .03f), new Vector3(b.Rand(.8f, 1.4f), b.Rand(.4f, .6f), .08f), p.StoneDark, p.StoneDark);
            b.Box(new Vector3(0, .3f, -t * .5f - .15f), new Vector3(L, .6f, .3f), p.StoneDark, p.Top(p.StoneDark));
        }

        static void Tower(BB b, int v)
        {
            var p = b.P; float r = 3.6f, h = 10.5f;
            b.Cyl(new Vector3(0, -Foundation, 0), r + .25f, r, Foundation + 1f, 10, p.StoneDark, p.Top(p.StoneDark), 0f);
            b.Cyl(new Vector3(0, 1f, 0), r, r, h - 1f, 10, p.Stone, p.Top(p.Stone));
            b.Cyl(new Vector3(0, h, 0), r + .3f, r + .3f, .4f, 10, p.StoneDark, p.Top(p.StoneDark));
            if (v == 0) b.MerlonRing(new Vector3(0, 0, 0), r + .05f, h + .4f, 10, p.Stone);
            else
            {
                b.Cyl(new Vector3(0, h + .4f, 0), r + .1f, r + .1f, 1.2f, 10, p.Stone, p.Top(p.Stone), 0f, false);
                Color roof = b.Pick(p.Roofs);
                b.Cyl(new Vector3(0, h + 1.6f, 0), r + .7f, 0f, 5.2f, 10, p.Top(roof), p.Top(roof));
            }
            for (int i = 0; i < 4; i++)
            {
                float a = i * Mathf.PI * .5f + .3f; var o = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                b.Slit(o * r + Vector3.up * (4f + (i % 2) * 2.5f), o, .3f, 1.2f, H("#2E3440"));
            }
        }

        static void Gatehouse(BB b)
        {
            var p = b.P; float tw = 4.8f, th = 10f, gap = 4.4f, d = 5.6f;
            for (int s = -1; s <= 1; s += 2)
            {
                float x = s * (gap * .5f + tw * .5f);
                b.Box(new Vector3(x, (th - Foundation) * .5f, 0), new Vector3(tw, th + Foundation, d), Quaternion.identity, p.Stone, p.Top(p.Stone));
                b.Merlons(new Vector3(x - tw * .5f, 0, -d * .5f + .3f), new Vector3(x + tw * .5f, 0, -d * .5f + .3f), th, .6f, p.Stone);
                b.Merlons(new Vector3(x - tw * .5f, 0, d * .5f - .3f), new Vector3(x + tw * .5f, 0, d * .5f - .3f), th, .6f, p.Stone);
                b.Slit(new Vector3(x, 6f, -d * .5f), Front, .3f, 1.3f, H("#2E3440"));
            }
            float archY = 6.2f;
            b.Box(new Vector3(0, (archY + th - .8f) * .5f, 0), new Vector3(gap + .2f, th - .8f - archY, d - .4f), p.Stone, p.Top(p.Stone));
            b.Merlons(new Vector3(-gap * .5f, 0, -d * .5f + .5f), new Vector3(gap * .5f, 0, -d * .5f + .5f), th - .8f, .6f, p.Stone);
            if (b.Far) return;
            b.Box(new Vector3(0, archY * .5f, d * .5f - .6f), new Vector3(gap, archY, .25f), p.WoodDark, p.WoodDark);
            for (int i = 0; i < 4; i++) b.Box(new Vector3(-gap * .5f + (i + .5f) * gap / 4f, archY - .9f, -d * .5f + .5f), new Vector3(.12f, 1.8f, .12f), H("#4A4440"));
            b.Box(new Vector3(0, archY - .1f, -d * .5f + .5f), new Vector3(gap, .14f, .14f), H("#4A4440"));
            Color banner = p.Accents[0];
            b.Box(new Vector3(0, th + .9f, 0), new Vector3(.1f, 3f, .1f), p.WoodDark);
            b.Box(new Vector3(.6f, th + 1.9f, 0), new Vector3(1.2f, .8f, .05f), banner, banner);
        }

        static void Keep(BB b, int v)
        {
            var p = b.P; Vector2 dim = KeepDims[v]; float w = dim.x, d = dim.y, h = 14f;
            b.Box(new Vector3(0, (1.2f - Foundation) * .5f, 0), new Vector3(w + .8f, Foundation + 1.2f, d + .8f), Quaternion.identity, p.StoneDark, p.Top(p.StoneDark));
            b.BoxB(0, 1.2f, 0, w, h - 1.2f, d, p.Stone, p.Top(p.Stone));
            b.BoxB(0, h, 0, w + .6f, .45f, d + .6f, p.StoneDark, p.Top(p.StoneDark));
            float hw = w * .5f + .3f, hd = d * .5f + .3f;
            b.Merlons(new Vector3(-hw, 0, -hd), new Vector3(hw, 0, -hd), h + .45f, .6f, p.Stone);
            b.Merlons(new Vector3(-hw, 0, hd), new Vector3(hw, 0, hd), h + .45f, .6f, p.Stone);
            b.Merlons(new Vector3(-hw, 0, -hd), new Vector3(-hw, 0, hd), h + .45f, .6f, p.Stone);
            b.Merlons(new Vector3(hw, 0, -hd), new Vector3(hw, 0, hd), h + .45f, .6f, p.Stone);
            Color roof = b.Pick(p.Roofs);
            for (int sx = -1; sx <= 1; sx += 2) for (int sz = -1; sz <= 1; sz += 2)
            {
                var c = new Vector3(sx * w * .5f, h - 2f, sz * d * .5f);
                b.Cyl(c, 1.4f, 1.4f, 3.6f, 8, p.Stone, p.Top(p.Stone));
                b.Cyl(c + Vector3.up * 3.6f, 1.8f, 0f, 3f, 8, p.Top(roof), p.Top(roof));
            }
            if (v == 1) b.Hip(new Vector3(0, h + .45f, 0), w - 2.4f, d - 2.4f, 4f, 0f, roof);
            b.Door(new Vector3(0, 1.2f, -d * .5f), Front, 1.8f, 2.8f, p.StoneDark, p.Door);
            if (!b.Far) b.Box(new Vector3(0, .6f, -d * .5f - 1.2f), new Vector3(2.8f, 1.2f, 2.4f), Quaternion.identity, p.StoneDark, p.Top(p.StoneDark));
            foreach (var o in new[] { Front, Back, Left, Right })
                for (int i = -1; i <= 1; i++)
                {
                    float half = o.z != 0 ? d * .5f : w * .5f, span = o.z != 0 ? w : d;
                    Vector3 face = o.z != 0 ? new Vector3(i * span * .3f, 0, o.z * half) : new Vector3(o.x * half, 0, i * span * .3f);
                    if (o == Front && i == 0) { b.Opening(face + Vector3.up * 7f, o, 1f, 1.5f, p.StoneDark, p.Pane); continue; }
                    b.Slit(face + Vector3.up * (6f + (i + 1) % 2 * 3f), o, .35f, 1.3f, H("#2E3440"));
                }
            b.Box(new Vector3(0, h + 3.4f + (v == 1 ? 3.6f : 0f), 0), new Vector3(.12f, 4f, .12f), p.WoodDark);
            b.Box(new Vector3(.8f, h + 4.6f + (v == 1 ? 3.6f : 0f), 0), new Vector3(1.6f, 1f, .05f), p.Accents[0], p.Accents[0]);
        }

        static void Tent(BB b, int v)
        {
            var p = b.P;
            if (v == 0)
            {
                Color c = p.Canvas;
                b.GableZ(Vector3.zero, 4f, 3f, 2.4f, 0f, c, c * .92f, .08f);
                if (!b.Far) b.Tri(new Vector3(-.5f, 0, -2.03f), new Vector3(.5f, 0, -2.03f), new Vector3(0, 1.6f, -2.03f), H("#3A3330"), Vector3.zero);
            }
            else
            {
                Color a = p.Accents[v == 1 ? 0 : 1], c = p.Canvas;
                b.Cyl(new Vector3(0, -.1f, 0), 2.3f, 2.3f, 1.9f, 10, c, c, 0f, false);
                for (int i = 0; i < 10; i++)
                {
                    float a0 = i * Mathf.PI * .2f, a1 = (i + 1) * Mathf.PI * .2f;
                    Vector3 p0 = new Vector3(Mathf.Cos(a0) * 2.55f, 1.8f, Mathf.Sin(a0) * 2.55f), p1 = new Vector3(Mathf.Cos(a1) * 2.55f, 1.8f, Mathf.Sin(a1) * 2.55f);
                    Color col = i % 2 == 0 ? a : c;
                    b.Tri(p0, p1, new Vector3(0, 3.9f, 0), p.Top(col), new Vector3(0, 2f, 0));
                    b.Tri(p0, p1, new Vector3(0, 1.8f, 0), col * .6f, new Vector3(0, 3f, 0));
                }
                b.Box(new Vector3(0, 4.4f, 0), new Vector3(.08f, 1.2f, .08f), p.WoodDark);
                b.Box(new Vector3(.35f, 4.75f, 0), new Vector3(.7f, .45f, .04f), a, a);
                if (!b.Far) b.Tri(new Vector3(-.6f, -.1f, -2.32f), new Vector3(.6f, -.1f, -2.32f), new Vector3(0, 1.6f, -2.32f), H("#3A3330"), Vector3.zero);
            }
        }

        static void Watchtower(BB b, int v)
        {
            var p = b.P; float h = v == 0 ? 7.5f : 9f, s = 1.6f;
            for (int sx = -1; sx <= 1; sx += 2) for (int sz = -1; sz <= 1; sz += 2)
                b.Box(new Vector3(sx * s, (h - 1f) * .5f, sz * s), new Vector3(.3f, h + 1f, .3f), p.WoodDark, p.Top(p.WoodDark));
            if (!b.Far)
                foreach (var o in new[] { Front, Back, Left, Right })
                {
                    Vector3 c = o * s, side = Vector3.Cross(Vector3.up, o);
                    b.Beam(c - side * s + Vector3.up * .3f, c + side * s + Vector3.up * (h * .5f), o, .14f, p.Wood);
                    b.Beam(c + side * s + Vector3.up * .3f, c - side * s + Vector3.up * (h * .5f), o, .14f, p.Wood);
                }
            b.BoxB(0, h, 0, 2 * s + 1f, .25f, 2 * s + 1f, p.Wood, p.Top(p.Wood));
            foreach (var o in new[] { Front, Back, Left, Right })
            {
                Vector3 c = o * (s + .45f) + Vector3.up * (h + .75f);
                b.Box(c, o.z != 0 ? new Vector3(2 * s + 1f, 1f, .1f) : new Vector3(.1f, 1f, 2 * s + 1f), p.Wood, p.Top(p.Wood));
            }
            for (int sx = -1; sx <= 1; sx += 2) for (int sz = -1; sz <= 1; sz += 2)
                b.Box(new Vector3(sx * (s + .4f), h + 1.5f, sz * (s + .4f)), new Vector3(.18f, 3f, .18f), p.WoodDark);
            Color roof = p.IsDry ? p.Canvas : b.Pick(p.Roofs);
            b.Hip(new Vector3(0, h + 3f, 0), 2 * s + 1.2f, 2 * s + 1.2f, 1.6f, .3f, roof);
            if (!b.Far) for (int i = 0; i < 8; i++) b.Box(new Vector3(-s + .4f, i * h / 8f + .5f, -s - .2f), new Vector3(.6f, .08f, .08f), p.Wood);
        }

        // =============================================================== ruins
        /// <summary>Wall built from vertical strips with a crumbled top (heights noise along the wall), with gaps.</summary>
        static void RuinedWall(BB b, Vector3 a, Vector3 e, float t, float hMax, float hMin, Color stone, Color top, float gapChance)
        {
            Vector3 d = e - a; float len = d.magnitude; int n = Mathf.Max(2, Mathf.RoundToInt(len / 1.1f));
            var q = Quaternion.LookRotation(Vector3.Cross(d / len, Vector3.up), Vector3.up);
            float hPrev = hMax;
            for (int i = 0; i < n; i++)
            {
                float h = Mathf.Clamp(hPrev + b.Rand(-1.4f, 1.1f), hMin, hMax);
                hPrev = h;
                if (b.Chance(gapChance) && i > 0 && i < n - 1) { hPrev = hMin; continue; }
                Vector3 c = a + d * ((i + .5f) / n);
                b.Box(new Vector3(c.x, (h - Foundation) * .5f, c.z), new Vector3(len / n + .02f, h + Foundation, t), q, b.Chance(.25f) ? stone * .9f : stone, top);
            }
        }

        static void RuinHouse(BB b, int v)
        {
            var p = b.P; Vector2 dim = RuinDims[v]; float w = dim.x, d = dim.y;
            Color stone = p.IsDry ? b.Pick(p.Walls) * .92f : p.Stone, top = p.Top(stone * .9f);
            b.Box(new Vector3(0, (Plinth - Foundation) * .5f, 0), new Vector3(w + .3f, Foundation + Plinth, d + .3f), Quaternion.identity, p.StoneDark, p.Top(p.StoneDark));
            float hMax = v == 1 ? 5.5f : 4.2f;
            RuinedWall(b, new Vector3(-w * .5f, 0, -d * .5f), new Vector3(w * .5f, 0, -d * .5f), .5f, hMax, .6f, stone, top, .2f);
            RuinedWall(b, new Vector3(w * .5f, 0, -d * .5f), new Vector3(w * .5f, 0, d * .5f), .5f, hMax + 1.2f, .4f, stone, top, .1f);
            RuinedWall(b, new Vector3(w * .5f, 0, d * .5f), new Vector3(-w * .5f, 0, d * .5f), .5f, hMax, .3f, stone, top, .35f);
            RuinedWall(b, new Vector3(-w * .5f, 0, d * .5f), new Vector3(-w * .5f, 0, -d * .5f), .5f, hMax * .7f, .3f, stone, top, .3f);
            if (b.Far) return;
            for (int i = 0; i < 7; i++)   // rubble inside
                b.Box(new Vector3(b.Rand(-w * .35f, w * .35f), b.Rand(.1f, .4f), b.Rand(-d * .35f, d * .35f)), new Vector3(b.Rand(.5f, 1.1f), b.Rand(.4f, .7f), b.Rand(.5f, 1f)),
                    Quaternion.Euler(b.Rand(-20, 20), b.Rand(0, 360), b.Rand(-20, 20)), b.Chance(.5f) ? p.Rubble : p.StoneDark, p.Top(p.Rubble));
            Color charred = H("#4A3B32");
            for (int i = 0; i < 3; i++)   // fallen roof beams
                b.Box(new Vector3(b.Rand(-w * .25f, w * .25f), .9f + i * .3f, b.Rand(-d * .25f, d * .25f)), new Vector3(.26f, .26f, d * .9f),
                    Quaternion.Euler(b.Rand(10, 30), b.Rand(-40, 40), b.Rand(-10, 10)), charred, p.Top(charred));
        }

        static void RuinTower(BB b, int v)
        {
            var p = b.P; float r = v == 0 ? 3.4f : 2.8f; int n = 12;
            Color stone = p.IsDry ? p.Stone : p.Stone * .97f;
            b.Cyl(new Vector3(0, -Foundation, 0), r + .3f, r + .2f, Foundation + .6f, n, p.StoneDark, p.Top(p.StoneDark));
            float h = v == 0 ? 9f : 7f, hPrev = h;
            for (int i = 0; i < n; i++)
            {
                float hh = Mathf.Clamp(hPrev + b.Rand(-2.4f, 1.6f), 1.2f, h); hPrev = hh;
                if (i == 7) { hPrev = 1.5f; continue; }   // breach
                float a = (i + .5f) * Mathf.PI * 2f / n; var o = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                b.Box(o * r + Vector3.up * (hh * .5f + .3f), new Vector3(r * Mathf.PI * 2f / n + .1f, hh, .8f), Quaternion.LookRotation(o), stone, p.Top(stone * .9f));
                if (!b.Far && hh > 4f && i % 3 == 0) b.Slit(o * (r + .4f) + Vector3.up * 3f, o, .3f, 1f, H("#2E3440"));
            }
            if (b.Far) return;
            for (int i = 0; i < 6; i++)
            {
                float a = 7.5f * Mathf.PI * 2f / n + b.Rand(-.3f, .3f), rr = r + b.Rand(.6f, 2.6f);
                b.Box(new Vector3(Mathf.Cos(a) * rr, .3f, Mathf.Sin(a) * rr), new Vector3(b.Rand(.6f, 1.2f), b.Rand(.5f, .8f), b.Rand(.6f, 1f)), Quaternion.Euler(b.Rand(-25, 25), b.Rand(0, 360), b.Rand(-25, 25)), p.Rubble, p.Top(p.Rubble));
            }
        }

        static void RuinWall(BB b, int v)
        {
            var p = b.P; Color stone = p.Stone, top = p.Top(stone * .9f);
            RuinedWall(b, new Vector3(-4.3f, 0, 0), new Vector3(4.3f, 0, 0), 1.4f, v == 0 ? 5.5f : 4f, .5f, stone, top, .15f);
            if (b.Far) return;
            for (int i = 0; i < 6; i++)
                b.Box(new Vector3(b.Rand(-4f, 4f), .3f, b.Rand(-2.2f, -1f) * (b.Chance(.5f) ? 1 : -1)), new Vector3(b.Rand(.6f, 1.2f), b.Rand(.5f, .8f), b.Rand(.6f, 1f)),
                    Quaternion.Euler(b.Rand(-25, 25), b.Rand(0, 360), b.Rand(-25, 25)), p.Rubble, p.Top(p.Rubble));
        }

        static void Rubble(BB b, int v)
        {
            var p = b.P; int n = v == 0 ? 10 : 14;
            for (int i = 0; i < n; i++)
            {
                float r = b.Rand(0f, 1.8f), a = b.Rand(0f, Mathf.PI * 2f), s = Mathf.Lerp(1.2f, .5f, r / 1.8f);
                b.Box(new Vector3(Mathf.Cos(a) * r, s * .35f + (1.8f - r) * .25f, Mathf.Sin(a) * r), new Vector3(s * b.Rand(.8f, 1.3f), s * .7f, s),
                    Quaternion.Euler(b.Rand(-30, 30), b.Rand(0, 360), b.Rand(-30, 30)), b.Chance(.6f) ? p.Rubble : p.StoneDark, p.Top(p.Rubble));
            }
            if (v == 1 && !b.Far) b.Box(new Vector3(.3f, .6f, 0), new Vector3(.25f, .25f, 3.2f), Quaternion.Euler(14f, 30f, 0), H("#4A3B32"), p.Top(H("#4A3B32")));
        }
    }
}
