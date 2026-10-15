using System;
using System.Collections.Generic;
using UnityEngine;

namespace MassEngine.Game.Editor
{
    /// <summary>
    /// 2026-10-15 detailed ("HD") building builders, user request "建筑质量不行…搞复杂点的建筑" → option A: same Q版
    /// flat-shaded vertex-colour style, but real geometric detail (roof tile courses, barge boards, jetties with beam ends,
    /// braces, window sills / lintels / mullions / shutters / flower boxes, dormers, quoins, string courses, buttresses,
    /// lancet and rose windows, voussoir arches, machicolations, corbelled turrets, portcullis …), about 2000–5000 triangles.
    /// The old builders stay as the distance LOD. Same local frame and footprints as <see cref="Build"/>.
    /// </summary>
    public static partial class ProcBuildingMeshes
    {
        public static bool HasHD(BK k) => k <= BK.Inn || (k >= BK.WallSeg && k <= BK.Watchtower) || k == BK.RuinHouse || k == BK.RuinTower || k == BK.RuinWall;

        public static FlatMesh BuildHD(BK k, int v, BPal p)
        {
            var b = new BB(7919 * ((int)k + 1) + 131 * v + p.Name.Length * 17, p, false);
            switch (k)
            {
                case BK.TownHouse: TownHouseHD(b, v); break;
                case BK.Church: ChurchHD(b, v); break;
                case BK.Keep: KeepHD(b, v); break;
                case BK.Gatehouse: GatehouseHD(b); break;
                case BK.Cottage: CottageHD(b, v); break;
                case BK.Longhouse: LonghouseHD(b, v); break;
                case BK.Barn: BarnHD(b, v); break;
                case BK.Windmill: WindmillHD(b, v); break;
                case BK.Inn: InnHD(b, v); break;
                case BK.WallSeg: WallSegHD(b); break;
                case BK.Tower: TowerHD(b, v); break;
                case BK.Watchtower: WatchtowerHD(b, v); break;
                case BK.Tent: TentHD(b, v); break;
                case BK.RuinHouse: RuinHouseHD(b, v); break;
                case BK.RuinTower: RuinTowerHD(b, v); break;
                case BK.RuinWall: RuinWallHD(b, v); break;
                default: return Build(k, v, p, false);
            }
            return b.M;
        }

        static readonly Color DarkHole = H("#2E3440"), Iron = H("#4A4440"), Leaf = H("#5BAA3F"), Gold = H("#E8B04A"), Clay = H("#B5653F"), Flame = H("#F2994A");

        // =============================================================== face-local helpers
        static Quaternion FQ(Vector3 o) => Quaternion.LookRotation(o, Vector3.up);
        static Vector3 FR(Vector3 o) => FQ(o) * Vector3.right;

        /// <summary>Box on a wall face: x right / y up in the face plane, depth outward from the surface (+ proud).</summary>
        static void FBox(BB b, Vector3 face, Vector3 o, float x, float y, float w, float h, float depth, Color side, Color top, float proud = 0f)
        {
            var q = FQ(o);
            b.Box(face + q * new Vector3(x, y, depth * .5f + proud), new Vector3(w, h, depth), q, side, top);
        }

        static void Pane(BB b, Vector3 c, Vector3 o, float w, float h, Color col)
        {
            Vector3 r = FR(o) * w * .5f, u = Vector3.up * h * .5f;
            b.Quad(c - r - u, c + r - u, c + r + u, c - r + u, col, c - o);
        }

        static Vector3 ArcDir(Vector3 r, float a) => r * Mathf.Cos(a) + Vector3.up * Mathf.Sin(a);

        /// <summary>Ring of voussoir blocks around centre <paramref name="c"/> (on the wall surface) from angle a0 to a1.</summary>
        static void Ring(BB b, Vector3 c, Vector3 o, float radius, float band, float depth, int n, float a0, float a1, Color col, Color key)
        {
            Vector3 r = FR(o);
            float step = (a1 - a0) / n, chord = 2f * (radius + band) * Mathf.Sin(Mathf.Abs(step) * .5f) * 1.04f;
            for (int i = 0; i < n; i++)
            {
                float am = a0 + step * (i + .5f);
                Vector3 radial = ArcDir(r, am);
                Color cc = (n % 2 == 1 && i == n / 2 && a1 - a0 < 4f) ? key : (i % 2 == 0 ? col : col * .93f);
                b.Box(c + radial * (radius + band * .5f) + o * depth * .5f, new Vector3(chord, band, depth), Quaternion.LookRotation(o, radial), cc, cc);
            }
        }

        static void Fan(BB b, Vector3 c, Vector3 o, float radius, int n, float a0, float a1, Color col)
        {
            Vector3 r = FR(o), cc = c + o * .03f;
            for (int i = 0; i < n; i++)
            {
                float t0 = a0 + (a1 - a0) * i / n, t1 = a0 + (a1 - a0) * (i + 1) / n;
                b.Tri(cc, cc + ArcDir(r, t0) * radius, cc + ArcDir(r, t1) * radius, col, cc - o);
            }
        }

        /// <summary>Round-headed opening: pane rectangle of height <paramref name="below"/> under a semicircle, voussoir ring.</summary>
        static void ArchOpening(BB b, Vector3 spring, Vector3 o, float radius, float below, Color fill, Color stone, float band = .3f, int n = 7)
        {
            Fan(b, spring, o, radius, 8, 0f, Mathf.PI, fill);
            if (below > 0f) Pane(b, spring + o * .03f - Vector3.up * below * .5f, o, radius * 2f, below, fill);
            Ring(b, spring, o, radius, band, .16f, n, 0f, Mathf.PI, stone, stone * 1.08f);
            if (below > 0f)
            {
                FBox(b, spring, o, -radius - band * .5f, -below * .5f, band, below, .14f, stone, stone);
                FBox(b, spring, o, radius + band * .5f, -below * .5f, band, below, .14f, stone, stone);
            }
        }

        /// <summary>Window with frame, sill, lintel, mullion cross, optional shutters and flower box. face = window centre.</summary>
        static void WinHD(BB b, Vector3 face, Vector3 o, float w, float h, Color frame, bool shutters, Color shutter, bool flowers, bool cross = true)
        {
            var p = b.P;
            Pane(b, face + o * .03f, o, w, h, p.Pane);
            FBox(b, face, o, 0, -h * .5f - .07f, w + .42f, .14f, .26f, frame, p.Top(frame));
            FBox(b, face, o, 0, h * .5f + .09f, w + .32f, .18f, .14f, frame, p.Top(frame));
            FBox(b, face, o, -w * .5f - .06f, 0, .12f, h, .1f, frame, frame);
            FBox(b, face, o, w * .5f + .06f, 0, .12f, h, .1f, frame, frame);
            if (cross)
            {
                FBox(b, face, o, 0, 0, .07f, h, .06f, frame, frame);
                FBox(b, face, o, 0, h * .12f, w, .07f, .06f, frame, frame);
            }
            if (shutters)
            {
                float sx = w * .5f + .12f + w * .25f;
                FBox(b, face, o, -sx, 0, w * .5f, h + .06f, .07f, shutter, shutter);
                FBox(b, face, o, sx, 0, w * .5f, h + .06f, .07f, shutter, shutter);
                FBox(b, face, o, -sx, h * .2f, w * .5f - .1f, .06f, .03f, shutter * .8f, shutter * .8f, .07f);
                FBox(b, face, o, sx, h * .2f, w * .5f - .1f, .06f, .03f, shutter * .8f, shutter * .8f, .07f);
            }
            if (flowers)
            {
                FBox(b, face, o, 0, -h * .5f - .3f, w + .1f, .3f, .34f, p.WoodDark, p.Top(p.WoodDark));
                int n = Mathf.Max(2, Mathf.RoundToInt(w / .3f));
                for (int i = 0; i < n; i++)
                {
                    Color c = i % 2 == 0 ? Leaf : p.Accents[(i / 2) % p.Accents.Length];
                    FBox(b, face, o, ((i + .5f) / n - .5f) * w, -h * .5f - .06f + (i % 2) * .05f, .26f, .24f, .22f, c, p.Top(c), .08f);
                }
            }
        }

        /// <summary>Gothic lancet: rectangle + pointed head, glass, stone frame, optional central mullion. face = centre of the rectangle.</summary>
        static void Lancet(BB b, Vector3 face, Vector3 o, float w, float h, Color frame, Color glass, Color glass2, bool mullion)
        {
            var p = b.P; Vector3 r = FR(o), cc = face + o * .03f; float tip = w * .85f;
            Pane(b, cc - Vector3.up * h * .25f, o, w, h * .5f, glass);
            Pane(b, cc + Vector3.up * h * .25f, o, w, h * .5f, glass2);
            b.Tri(cc + Vector3.up * h * .5f - r * w * .5f, cc + Vector3.up * h * .5f + r * w * .5f, cc + Vector3.up * (h * .5f + tip), glass, cc - o);
            FBox(b, face, o, -w * .5f - .09f, 0, .18f, h, .14f, frame, frame);
            FBox(b, face, o, w * .5f + .09f, 0, .18f, h, .14f, frame, frame);
            FBox(b, face, o, 0, -h * .5f - .1f, w + .55f, .2f, .32f, frame, p.Top(frame));
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 a = face + Vector3.up * h * .5f + r * s * (w * .5f + .09f), e = face + Vector3.up * (h * .5f + tip + .14f);
                Vector3 d = e - a;
                b.Box((a + e) * .5f + o * .07f, new Vector3(.18f, d.magnitude + .12f, .14f), Quaternion.LookRotation(o, d.normalized), frame, frame);
            }
            if (mullion) FBox(b, face, o, 0, tip * .4f, .07f, h + tip * .8f, .08f, frame, frame);
            FBox(b, face, o, 0, 0, w, .06f, .06f, frame, frame);
        }

        static void CrossSlit(BB b, Vector3 face, Vector3 o, float h)
        {
            Pane(b, face + o * .03f, o, .24f, h, DarkHole);
            Pane(b, face + o * .03f + Vector3.up * h * .12f, o, h * .45f, .2f, DarkHole);
        }

        static void Quoins(BB b, float x, float z, float sx, float sz, float y0, float y1, Color col)
        {
            int n = Mathf.Max(1, Mathf.FloorToInt((y1 - y0) / .55f));
            float ch = (y1 - y0) / n;
            for (int i = 0; i < n; i++)
            {
                bool lx = i % 2 == 0; float ax = lx ? .9f : .5f, az = lx ? .5f : .9f;
                b.Box(new Vector3(x - sx * (ax * .5f - .06f), y0 + (i + .5f) * ch, z - sz * (az * .5f - .06f)), new Vector3(ax, ch * .84f, az), col, b.P.Top(col));
            }
        }

        static void Band(BB b, float cx, float cz, float w, float d, float y, float h, float proud, Color col) =>
            b.Box(new Vector3(cx, y, cz), new Vector3(w + 2f * proud, h, d + 2f * proud), col, b.P.Top(col));

        /// <summary>Frustum with a rectangular base (battered plinths).</summary>
        static void Frustum(BB b, float cx, float y0, float cz, float w0, float d0, float w1, float d1, float h, Color side, Color top)
        {
            Vector3 c = new Vector3(cx, y0 + h * .5f, cz);
            Vector3 A(float sx, float sz) => new Vector3(cx + sx * w0 * .5f, y0, cz + sz * d0 * .5f);
            Vector3 B(float sx, float sz) => new Vector3(cx + sx * w1 * .5f, y0 + h, cz + sz * d1 * .5f);
            b.Quad(A(-1, -1), A(1, -1), B(1, -1), B(-1, -1), side, c);
            b.Quad(A(1, -1), A(1, 1), B(1, 1), B(1, -1), side, c);
            b.Quad(A(1, 1), A(-1, 1), B(-1, 1), B(1, 1), side, c);
            b.Quad(A(-1, 1), A(-1, -1), B(-1, -1), B(-1, 1), side, c);
            b.Quad(B(-1, -1), B(1, -1), B(1, 1), B(-1, 1), top, c - Vector3.up * h);
        }

        /// <summary>Gable roof + tile courses (horizontal lips) + barge boards. alongZ: ridge along Z (gable to the street).</summary>
        static void GableHD(BB b, Vector3 c, float len, float width, float rise, float over, Color roof, Color wall, bool alongZ, int courses, Color barge)
        {
            const float thick = .28f;
            if (alongZ) b.GableZ(c, len, width, rise, over, roof, wall, thick); else b.Gable(c, len, width, rise, over, roof, wall, true, thick);
            float half = width * .5f, slant = Mathf.Sqrt(rise * rise + half * half) + over;
            Vector3 ridge = c + Vector3.up * rise, axis = alongZ ? Vector3.forward : Vector3.right;
            Color lip = roof * .84f;
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 down = alongZ ? new Vector3(s * half, -rise, 0).normalized : new Vector3(0, -rise, s * half).normalized;
                Vector3 n = alongZ ? new Vector3(s * rise, half, 0).normalized : new Vector3(0, half, s * rise).normalized;
                var q = Quaternion.LookRotation(down, n);
                for (int i = 1; i <= courses; i++)
                {
                    Vector3 pos = ridge + down * (slant * i / courses - .1f) + n * (thick + .05f);
                    b.Box(pos, new Vector3(len + 2f * over, .12f, .2f), alongZ ? Quaternion.LookRotation(Vector3.forward, n) * Quaternion.Euler(0, 90f, 0) : q, lip, b.P.Top(lip));
                }
                for (int e = -1; e <= 1; e += 2)
                {
                    Vector3 pos = ridge + down * (slant * .5f) + n * (thick * .5f) + axis * e * (len * .5f + over + .06f);
                    b.Box(pos, new Vector3(.14f, .46f, slant), q, barge, b.P.Top(barge));
                }
            }
        }

        static void Dormer(BB b, float x, float roofY, float half, float rise, float t, Color wall, Color roof, Color frame)
        {
            float zf = -half * (1f - t) - .05f, yb = roofY + rise * t - .25f, dw = 1.8f, dh = 1.7f, depth = 2.4f;
            b.Box(new Vector3(x, yb + dh * .5f, zf + depth * .5f), new Vector3(dw, dh, depth), wall, wall);
            GableHD(b, new Vector3(x, yb + dh, zf + depth * .5f), depth, dw, .9f, .18f, roof, wall, true, 2, b.P.Timber);
            WinHD(b, new Vector3(x, yb + dh * .5f + .05f, zf), Front, .8f, .85f, frame, false, default, false);
        }

        static void ChimneyHD(BB b, Vector3 baseOnRoof, float h, Color stone)
        {
            var p = b.P;
            b.Box(baseOnRoof + Vector3.up * h * .5f, new Vector3(.85f, h, .85f), stone, p.Top(stone));
            b.Box(baseOnRoof + Vector3.up * h * .62f, new Vector3(.97f, .14f, .97f), stone * .9f, p.Top(stone * .9f));
            b.Box(baseOnRoof + Vector3.up * (h + .1f), new Vector3(1.05f, .2f, 1.05f), stone * .88f, p.Top(stone * .85f));
            b.Cyl(baseOnRoof + new Vector3(-.2f, h + .2f, 0), .15f, .12f, .42f, 6, Clay, DarkHole);
            b.Cyl(baseOnRoof + new Vector3(.22f, h + .2f, .05f), .15f, .12f, .32f, 6, Clay, DarkHole);
        }

        static void DoorHD(BB b, Vector3 fb, Vector3 o, float w, float h, Color frame, Color door, bool arched, int steps)
        {
            var p = b.P;
            float hh = arched ? h - w * .5f : h;
            FBox(b, fb, o, 0, hh * .5f, w, hh, .1f, door, door);
            int planks = Mathf.Max(2, Mathf.RoundToInt(w / .32f));
            for (int i = 1; i < planks; i++) FBox(b, fb, o, (i / (float)planks - .5f) * w, hh * .5f, .04f, hh - .1f, .03f, door * .78f, door * .78f, .1f);
            FBox(b, fb, o, 0, hh * .28f, w, .1f, .03f, Iron, Iron, .1f);
            FBox(b, fb, o, 0, hh * .74f, w, .1f, .03f, Iron, Iron, .1f);
            FBox(b, fb, o, w * .32f, hh * .5f, .09f, .09f, .1f, Gold, Gold, .1f);
            FBox(b, fb, o, -w * .5f - .12f, hh * .5f, .24f, hh, .16f, frame, frame);
            FBox(b, fb, o, w * .5f + .12f, hh * .5f, .24f, hh, .16f, frame, frame);
            if (arched)
            {
                Fan(b, fb + Vector3.up * hh + o * .08f, o, w * .5f, 6, 0f, Mathf.PI, door);
                Ring(b, fb + Vector3.up * hh, o, w * .5f, .24f, .18f, 7, 0f, Mathf.PI, frame, frame * 1.08f);
            }
            else FBox(b, fb, o, 0, h + .11f, w + .5f, .22f, .18f, frame, p.Top(frame));
            for (int i = 0; i < steps; i++)
                FBox(b, fb, o, 0, -.075f - i * .15f, w + .8f + i * .3f, .15f, .4f * (i + 1), p.Stone, p.Top(p.Stone));
        }

        static void Lantern(BB b, Vector3 face, Vector3 o)
        {
            FBox(b, face, o, 0, 0, .06f, .06f, .5f, Iron, Iron);
            FBox(b, face, o, 0, -.27f, .22f, .32f, .22f, H("#FFE08A"), Iron, .34f);
            FBox(b, face, o, 0, -.08f, .3f, .07f, .3f, Iron, Iron, .3f);
        }

        static void Awning(BB b, Vector3 face, Vector3 o, float w, float y, float depth, Color a, Color c)
        {
            int n = Mathf.Max(3, Mathf.RoundToInt(w / .45f));
            var q = FQ(o) * Quaternion.Euler(22f, 0, 0);
            Vector3 r = FR(o), ctr = face + Vector3.up * y + q * new Vector3(0, 0, depth * .5f);
            for (int i = 0; i < n; i++)
            {
                Color col = i % 2 == 0 ? a : c;
                b.Box(ctr + r * ((i + .5f) / n - .5f) * w, new Vector3(w / n, .06f, depth), q, col, b.P.Top(col));
                Vector3 edge = face + Vector3.up * y + q * new Vector3(((i + .5f) / n - .5f) * w, -.14f, depth);
                b.Box(edge, new Vector3(w / n, .26f, .04f), FQ(o), col, col);
            }
            FBox(b, face, o, -w * .5f + .1f, y - .5f, .06f, .06f, depth * .9f, Iron, Iron);
            FBox(b, face, o, w * .5f - .1f, y - .5f, .06f, .06f, depth * .9f, Iron, Iron);
        }

        /// <summary>Diagonal braces on the timber-framed storey (complements <see cref="HalfTimber"/>).</summary>
        static void Braces(BB b, float w, float d, float y0, float y1)
        {
            Color t = b.P.Timber; int posts = Mathf.Max(2, Mathf.RoundToInt(w / 1.8f));
            foreach (var o in new[] { Front, Back })
            {
                float z = o.z * (d * .5f + .01f);
                for (int i = 1; i < posts - 1; i++)
                {
                    float x0 = -w * .5f + w * i / posts, x1 = -w * .5f + w * (i + 1) / posts;
                    b.Beam(new Vector3(x0, y0 + .15f, z), new Vector3((x0 + x1) * .5f, y0 + (y1 - y0) * .45f, z), o, .14f, t);
                    b.Beam(new Vector3(x1, y0 + .15f, z), new Vector3((x0 + x1) * .5f, y0 + (y1 - y0) * .45f, z), o, .14f, t);
                }
                b.Beam(new Vector3(-w * .5f, y0 + (y1 - y0) * .42f, z), new Vector3(w * .5f, y0 + (y1 - y0) * .42f, z), o, .12f, t);
            }
        }

        static void Barrel(BB b, Vector3 at)
        {
            var p = b.P;
            b.Cyl(at, .32f, .38f, .45f, 8, p.Wood, p.Wood);
            b.Cyl(at + Vector3.up * .45f, .38f, .32f, .45f, 8, p.Wood, p.Top(p.WoodDark));
            b.Cyl(at + Vector3.up * .2f, .37f, .37f, .06f, 8, Iron, Iron, 0f, false);
            b.Cyl(at + Vector3.up * .66f, .37f, .37f, .06f, 8, Iron, Iron, 0f, false);
        }

        static void Banner(BB b, Vector3 face, Vector3 o, float w, float h, Color a, Color c)
        {
            FBox(b, face, o, 0, 0, w * .5f, h, .06f, a, a, .05f);
            FBox(b, face, o, w * .25f, 0, w * .5f, h, .06f, c, c, .05f);
            Vector3 r = FR(o), f = face + o * .12f - Vector3.up * h * .5f;
            b.Tri(f - r * w * .5f, f + r * w * .5f, f - Vector3.up * w * .45f, c, f - o);
            FBox(b, face, o, 0, h * .5f + .08f, w + .3f, .1f, .1f, Iron, Iron, .05f);
            FBox(b, face, o, 0, h * .18f, w * .4f, w * .4f, .03f, Gold, Gold, .11f);
        }

        // =============================================================== town houses
        static void TownHouseHD(BB b, int v)
        {
            var p = b.P; Vector2 dim = TownDims[v]; float w = dim.x, d = dim.y;
            bool dry = p.IsDry;
            Color wall = b.Pick(p.Walls), roof = b.Pick(p.Roofs), stone = p.Stone, shc = Shutter(b), frame = dry ? p.Frame : p.Timber * 1.25f;
            float g = 3.1f, u = 2.8f, jet = dry ? 0f : .42f;
            int floors = (v == 0 || v == 2) ? 2 : 1;
            bool stoneGround = !dry && (v % 2 == 1 || b.Chance(.4f));
            Color gcol = stoneGround ? stone : wall, qcol = stoneGround ? p.StoneDark : stone;

            b.Base(w, d, p.StoneDark);
            Band(b, 0, 0, w, d, Plinth - .02f, .22f, .2f, p.StoneDark);
            b.BoxB(0, Plinth, 0, w, g, d, gcol, gcol);
            for (int sx = -1; sx <= 1; sx += 2) for (int sz = -1; sz <= 1; sz += 2) Quoins(b, sx * w * .5f, sz * d * .5f, sx, sz, Plinth + .1f, Plinth + g, qcol);

            // upper storeys (jettied front and back)
            float y = Plinth + g; float dPrev = d;
            for (int k = 0; k < floors; k++)
            {
                float dk = d + 2f * jet * (k + 1);
                b.BoxB(0, y, 0, w, u, dk, wall, wall);
                if (!dry)
                {
                    HalfTimber(b, w, dk, y, y + u);
                    Braces(b, w, dk, y, y + u);
                    int posts = Mathf.Max(2, Mathf.RoundToInt(w / 1.8f));
                    for (int i = 0; i <= posts; i++)
                        for (int s = -1; s <= 1; s += 2)
                            b.Box(new Vector3(-w * .5f + w * i / posts, y - .12f, s * (dPrev * .5f + jet * .5f)), new Vector3(.2f, .24f, jet + .12f), p.Timber, p.Top(p.Timber));
                }
                else Band(b, 0, 0, w, dk, y + .05f, .16f, .06f, stone);
                // windows of this storey
                int up = w > 6.5f ? 3 : 2;
                bool sh = !dry ? b.Chance(.6f) : true;
                for (int i = 0; i < up; i++)
                {
                    float x = ((i + .5f) / up - .5f) * w * .92f;
                    if (v == 2 && k == 0 && i == up / 2) continue;   // oriel there
                    WinHD(b, new Vector3(x, y + 1.45f, -dk * .5f), Front, .82f, 1.05f, frame, sh, shc, k == 0 && !dry && i != 1 || (dry && k == 0));
                    if (i != 1 || up == 2) WinHD(b, new Vector3(x, y + 1.45f, dk * .5f), Back, .78f, .95f, frame, false, shc, false);
                }
                WinHD(b, new Vector3(-w * .5f, y + 1.45f, 0), Left, .7f, .9f, frame, false, shc, false);
                WinHD(b, new Vector3(w * .5f, y + 1.45f, 0), Right, .7f, .9f, frame, false, shc, false);
                y += u; dPrev = dk;
            }
            float dTop = d + 2f * jet * floors, top = y;

            // ground floor front
            Vector3 fz = new Vector3(0, Plinth, -d * .5f);
            switch (v)
            {
                case 0:   // shop with awning, door left
                    DoorHD(b, fz + new Vector3(-w * .3f, 0, 0), Front, 1.1f, 2.15f, frame, p.Door, false, 1);
                    WinHD(b, fz + new Vector3(w * .14f, 1.55f, 0), Front, 2.1f, 1.3f, frame, false, shc, false);
                    Awning(b, fz, Front, 3.2f, 2.65f, 1.3f, b.Pick(p.Accents), p.Canvas);
                    Lantern(b, fz + new Vector3(-w * .3f - 1f, 2.3f, 0), Front);
                    Barrel(b, new Vector3(w * .38f, Plinth, -d * .5f - .55f));
                    break;
                case 1:   // arched door between two flower windows
                    DoorHD(b, fz, Front, 1.3f, 2.55f, stone * 1.05f, p.Door, true, 2);
                    WinHD(b, fz + new Vector3(-w * .3f, 1.6f, 0), Front, .9f, 1.05f, frame, true, shc, true);
                    WinHD(b, fz + new Vector3(w * .3f, 1.6f, 0), Front, .9f, 1.05f, frame, true, shc, true);
                    Lantern(b, fz + new Vector3(1.05f, 2.3f, 0), Front);
                    break;
                case 2:   // door right, shop window left, oriel above
                    DoorHD(b, fz + new Vector3(w * .28f, 0, 0), Front, 1.05f, 2.15f, frame, p.Door, false, 1);
                    WinHD(b, fz + new Vector3(-w * .17f, 1.55f, 0), Front, 1.8f, 1.25f, frame, false, shc, false);
                    Awning(b, fz + new Vector3(-w * .17f, 0, 0), Front, 2.6f, 2.65f, 1.1f, b.Pick(p.Accents), p.Canvas);
                    {   // oriel (bay window) on the first upper storey
                        float oy = Plinth + g, oz = -(d * .5f + jet), od = .75f, ow = 2.2f;
                        b.Box(new Vector3(0, oy + 1.4f, oz - od * .5f), new Vector3(ow, 2.1f, od), wall, wall);
                        b.Box(new Vector3(0, oy + .25f, oz - od * .5f), new Vector3(ow + .1f, .3f, od + .1f), p.Timber, p.Top(p.Timber));
                        b.Cyl(new Vector3(0, oy - .25f, oz - .3f), .15f, .55f, .4f, 4, p.Timber, p.Timber, Mathf.PI * .25f);
                        b.Hip(new Vector3(0, oy + 2.45f, oz - od * .5f), ow, od, .5f, .12f, roof);
                        WinHD(b, new Vector3(0, oy + 1.45f, oz - od), Front, 1.3f, 1f, frame, false, shc, false);
                        WinHD(b, new Vector3(-ow * .5f, oy + 1.45f, oz - od * .5f), Left, .4f, .9f, frame, false, shc, false, false);
                        WinHD(b, new Vector3(ow * .5f, oy + 1.45f, oz - od * .5f), Right, .4f, .9f, frame, false, shc, false, false);
                    }
                    break;
                default:  // wide house: centre door, double windows, outside stair to the upper door on the right side
                    DoorHD(b, fz, Front, 1.2f, 2.2f, frame, p.Door, false, 1);
                    WinHD(b, fz + new Vector3(-w * .3f, 1.6f, 0), Front, 1.5f, 1.05f, frame, true, shc, true);
                    WinHD(b, fz + new Vector3(w * .3f, 1.6f, 0), Front, 1.5f, 1.05f, frame, true, shc, true);
                    {
                        float sx = w * .5f, rise = g, run = d - 1.6f; int steps = 9;
                        for (int i = 0; i < steps; i++)
                        {
                            float h = rise * (i + 1) / steps;
                            b.Box(new Vector3(sx + .6f, Plinth + h - .1f, d * .5f - .3f - run * (i + .5f) / steps), new Vector3(1.1f, .2f, run / steps + .05f), p.Wood, p.Top(p.Wood));
                        }
                        b.Box(new Vector3(sx + .6f, Plinth + rise, -d * .5f + .9f), new Vector3(1.2f, .2f, 1.6f), p.Wood, p.Top(p.Wood));
                        for (int i = 0; i < 4; i++) b.Box(new Vector3(sx + 1.12f, Plinth + rise * (i + .5f) / 4f * .5f + rise * .25f, d * .5f - .3f - run * i / 3f), new Vector3(.1f, rise * .5f + 1f, .1f), p.WoodDark, p.WoodDark);
                        Vector3 ra = new Vector3(sx + 1.12f, Plinth + .9f, d * .5f - .3f), rb = new Vector3(sx + 1.12f, Plinth + rise + .9f, -d * .5f + 1.4f);
                        b.Box((ra + rb) * .5f, new Vector3(.1f, .1f, (rb - ra).magnitude), Quaternion.LookRotation(rb - ra), p.WoodDark, p.WoodDark);
                        DoorHD(b, new Vector3(sx, Plinth + rise + .1f, -d * .5f + .9f), Right, .9f, 2f, frame, p.Door, false, 0);
                        b.Box(new Vector3(sx + .7f, Plinth + rise + 2.5f, -d * .5f + .9f), new Vector3(1.6f, .1f, 1.8f), Quaternion.Euler(0, 0, -18f), roof, p.Top(roof));
                    }
                    break;
            }
            // back door + side ground window
            DoorHD(b, new Vector3(w * .25f, Plinth, d * .5f), Back, 1f, 2f, frame, p.Door, false, 1);
            WinHD(b, new Vector3(-w * .5f, Plinth + 1.6f, -d * .2f), Left, .7f, .9f, frame, false, shc, false);

            // roof
            if (dry)
            {
                b.FlatRoof(new Vector3(0, top, 0), w, dTop, wall);
                int n = Mathf.RoundToInt(w / 1.1f);
                for (int i = 0; i < n; i++) b.Box(new Vector3(((i + .5f) / n - .5f) * w, top + .95f, -dTop * .5f), new Vector3(w / n * .5f, .3f, .3f), wall, p.Top(wall));
                b.BoxB(w * .28f, top + .24f, dTop * .22f, 1.5f, 1.9f, 1.5f, wall, p.FlatTop);
                DoorHD(b, new Vector3(w * .28f, top + .24f, dTop * .22f - .75f), Front, .7f, 1.5f, p.Frame, p.Door, false, 0);
                for (int sx = -1; sx <= 1; sx += 2) for (int sz = -1; sz <= 1; sz += 2)
                    b.Box(new Vector3(-w * .18f + sx * 1.3f, top + 1.2f, -dTop * .1f + sz * 1f), new Vector3(.12f, 1.9f, .12f), p.Wood, p.Wood);
                for (int i = 0; i < 6; i++) b.Box(new Vector3(-w * .18f - 1.4f + i * .56f, top + 2.18f, -dTop * .1f), new Vector3(.1f, .1f, 2.4f), p.Wood, p.Wood);
                b.Box(new Vector3(-w * .18f, top + .3f, -dTop * .1f), new Vector3(2f, .04f, 1.4f), b.Pick(p.Accents), b.Pick(p.Accents));
                b.Cyl(new Vector3(-w * .38f, top + .24f, dTop * .3f), .3f, .2f, .7f, 7, Clay, DarkHole);
                b.Cyl(new Vector3(-w * .3f, top + .24f, dTop * .36f), .25f, .18f, .55f, 7, Clay, DarkHole);
                b.Box(new Vector3(-w * .5f + .1f, top - .4f, 0), new Vector3(.3f, .14f, .7f), Clay, Clay);   // spout
            }
            else if (v == 0 || v == 2)   // gable to the street
            {
                float rise = p.IsSnow ? 4.6f : 4.1f, z = -dTop * .5f;
                GableHD(b, new Vector3(0, top, 0), dTop, w, rise, .5f, roof, wall, true, 6, p.Timber);
                b.Beam(new Vector3(0, top, z), new Vector3(0, top + rise - .3f, z), Front, .2f, p.Timber);
                b.Beam(new Vector3(-w * .3f, top + rise * .4f, z), new Vector3(w * .3f, top + rise * .4f, z), Front, .18f, p.Timber);
                b.Beam(new Vector3(-w * .36f, top + .1f, z), new Vector3(-.1f, top + rise * .4f, z), Front, .14f, p.Timber);
                b.Beam(new Vector3(w * .36f, top + .1f, z), new Vector3(.1f, top + rise * .4f, z), Front, .14f, p.Timber);
                WinHD(b, new Vector3(-w * .17f, top + rise * .2f + .1f, z), Front, .55f, .7f, frame, false, shc, false, false);
                WinHD(b, new Vector3(w * .17f, top + rise * .2f + .1f, z), Front, .55f, .7f, frame, false, shc, false, false);
                WinHD(b, new Vector3(0, top + rise * .62f, z), Front, .5f, .6f, frame, false, shc, false, false);
                b.Box(new Vector3(0, top + rise * .82f, z - .55f), new Vector3(.22f, .22f, 1.2f), p.Timber, p.Top(p.Timber));   // hoist beam
                b.Box(new Vector3(0, top + rise * .82f - .6f, z - 1.05f), new Vector3(.03f, 1.1f, .03f), p.Hay * .8f, p.Hay * .8f);
                ChimneyHD(b, new Vector3(w * .24f, top + rise * .52f - .2f, dTop * .22f), rise * .5f + 1.3f, p.StoneDark);
            }
            else   // eaves to the street, dormers
            {
                float rise = p.IsSnow ? 3.9f : 3.4f, half = dTop * .5f;
                GableHD(b, new Vector3(0, top, 0), w, dTop, rise, .5f, roof, wall, false, 5, p.Timber);
                if (w > 7.5f) { Dormer(b, -w * .24f, top, half, rise, .3f, wall, roof, frame); Dormer(b, w * .24f, top, half, rise, .3f, wall, roof, frame); }
                else Dormer(b, 0, top, half, rise, .3f, wall, roof, frame);
                WinHD(b, new Vector3(-w * .5f, top + rise * .35f, 0), Left, .55f, .7f, frame, false, shc, false, false);
                WinHD(b, new Vector3(w * .5f, top + rise * .35f, 0), Right, .55f, .7f, frame, false, shc, false, false);
                ChimneyHD(b, new Vector3(w * .32f, top + rise * (1f - .22f * 2f) - .1f, dTop * .22f), rise * .44f + 1.4f, p.StoneDark);
            }
        }

        // =============================================================== church
        static void Buttress(BB b, Vector3 foot, Vector3 o, float h, Color stone)
        {
            var p = b.P; float h1 = h * .55f;
            FBox(b, foot, o, 0, h1 * .5f, .75f, h1, .85f, stone, p.Top(stone));
            FBox(b, foot, o, 0, h1 + (h - h1) * .5f, .6f, h - h1, .5f, stone, p.Top(stone));
            var q = FQ(o) * Quaternion.Euler(38f, 0, 0);
            b.Box(foot + Vector3.up * (h1 + .08f) + o * .62f, new Vector3(.8f, .14f, .62f), q, stone * .92f, p.Top(stone * .92f));
            b.Box(foot + Vector3.up * (h + .08f) + o * .3f, new Vector3(.66f, .14f, .5f), q, stone * .92f, p.Top(stone * .92f));
        }

        static void ChurchHD(BB b, int v)
        {
            var p = b.P; float w = 8f, len = 15f, wh = 6.2f, nz = 2.5f;
            Color wall = v == 0 ? p.Stone : b.Pick(p.Walls), roof = b.Pick(p.Roofs), trim = p.StoneDark;
            Color glass = p.IsSnow ? p.Pane : H("#5B7FA8"), glass2 = H("#7E6BB0");
            float rise = p.IsSnow ? 5.2f : 4.6f;
            b.Box(new Vector3(0, (Plinth - Foundation) * .5f, 1.2f), new Vector3(w + .6f, Foundation + Plinth, 20.6f), Quaternion.identity, trim, p.Top(trim));
            b.BoxB(0, Plinth, nz, w, wh, len, wall, wall);
            Band(b, 0, nz, w, len, Plinth + .3f, .55f, .12f, trim);
            Band(b, 0, nz, w, len, Plinth + wh - .15f, .3f, .18f, trim);
            GableHD(b, new Vector3(0, Plinth + wh, nz), len, w, rise, .5f, roof, wall, true, 7, trim);
            // cross on the east gable
            Vector3 ce = new Vector3(0, Plinth + wh + rise + .3f, nz + len * .5f + .2f);
            b.Box(ce + Vector3.up * .7f, new Vector3(.16f, 1.6f, .16f), Gold, Gold);
            b.Box(ce + Vector3.up * 1.05f, new Vector3(.8f, .16f, .16f), Gold, Gold);
            int bays = 4;
            for (int i = 0; i <= bays; i++)
            {
                float z = nz - len * .5f + .4f + i * (len - .8f) / bays;
                if (i == 0) continue;
                Buttress(b, new Vector3(-w * .5f, Plinth, z), Left, wh - .3f, trim);
                Buttress(b, new Vector3(w * .5f, Plinth, z), Right, wh - .3f, trim);
            }
            for (int i = 0; i < bays; i++)
            {
                float z = nz - len * .5f + .4f + (i + .5f) * (len - .8f) / bays;
                Lancet(b, new Vector3(-w * .5f, Plinth + 2.9f, z), Left, .95f, 2.3f, trim, glass, glass2, true);
                Lancet(b, new Vector3(w * .5f, Plinth + 2.9f, z), Right, .95f, 2.3f, trim, glass, glass2, true);
            }
            // east gable: round window
            Vector3 eg = new Vector3(0, Plinth + wh + rise * .62f, nz + len * .5f);
            Fan(b, eg, Back, .6f, 10, 0f, Mathf.PI * 2f, glass);
            Ring(b, eg, Back, .6f, .2f, .16f, 10, 0f, Mathf.PI * 2f, trim, trim);
            FBox(b, eg, Back, 0, 0, 1.2f, .07f, .08f, trim, trim); FBox(b, eg, Back, 0, 0, .07f, 1.2f, .08f, trim, trim);

            // apse
            Vector3 ac = new Vector3(0, Plinth, nz + len * .5f);
            float ar = 3.2f, ap = ar * Mathf.Cos(Mathf.PI / 8f), ah = wh - 1.2f;
            b.Cyl(ac, ar, ar, ah, 8, wall, wall, Mathf.PI / 8f);
            b.Cyl(ac + Vector3.up * (ah - .25f), ar + .2f, ar + .2f, .25f, 8, trim, p.Top(trim), Mathf.PI / 8f);
            b.Cyl(ac + Vector3.up * ah, ar + .45f, 0f, 2.9f, 8, p.Top(roof), p.Top(roof), Mathf.PI / 8f);
            for (int k = 1; k <= 3; k++)
            {
                float a = k * Mathf.PI / 4f; Vector3 o = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                Lancet(b, ac + o * ap + Vector3.up * 2.2f, o, .5f, 1.5f, trim, glass, glass2, false);
            }

            // front tower in stages
            float tw = 4.6f, th = 13.5f, tz = nz - len * .5f - 1.6f, fzz = tz - tw * .5f;
            b.BoxB(0, Plinth, tz, tw, th, tw, wall, wall);
            foreach (float yy in new[] { Plinth + 4.6f, Plinth + 9.2f }) Band(b, 0, tz, tw, tw, yy, .3f, .14f, trim);
            for (int sx = -1; sx <= 1; sx += 2) for (int sz = -1; sz <= 1; sz += 2) Quoins(b, sx * tw * .5f, tz + sz * tw * .5f, sx, sz, Plinth + .1f, Plinth + th, trim);
            // portal: two orders, steps, gable hood
            Vector3 pf = new Vector3(0, Plinth, fzz);
            FBox(b, pf, Front, 0, 1.75f, 2.9f, 3.5f, .45f, wall, p.Top(wall));
            Vector3 pf2 = pf + Front * .45f;
            DoorHD(b, pf2, Front, 1.7f, 3.1f, trim, p.Door, true, 3);
            Ring(b, pf2 + Vector3.up * (3.1f - .85f), Front, 1.12f, .26f, .1f, 9, 0f, Mathf.PI, trim * 1.06f, trim);
            {
                Vector3 hb = pf2 + Vector3.up * 3.75f; Vector3 r = FR(Front);
                for (int s = -1; s <= 1; s += 2)
                {
                    Vector3 a = hb + r * s * 1.55f, e = hb + Vector3.up * 1.05f;
                    b.Box((a + e) * .5f + Front * .12f, new Vector3(.24f, (e - a).magnitude + .2f, .3f), Quaternion.LookRotation(Front, (e - a).normalized), trim, p.Top(trim));
                }
            }
            // rose window
            Vector3 rw = new Vector3(0, Plinth + 6.4f, fzz);
            Fan(b, rw, Front, .85f, 12, 0f, Mathf.PI * 2f, glass2);
            Fan(b, rw + Front * .02f, Front, .35f, 8, 0f, Mathf.PI * 2f, Gold);
            Ring(b, rw, Front, .85f, .26f, .16f, 12, 0f, Mathf.PI * 2f, trim, trim);
            for (int k = 0; k < 4; k++) b.Box(rw + Front * .08f, new Vector3(1.7f, .07f, .06f), FQ(Front) * Quaternion.Euler(0, 0, k * 45f), trim, trim);
            // clock
            Vector3 ck = new Vector3(0, Plinth + 8.45f, fzz);
            Fan(b, ck, Front, .62f, 12, 0f, Mathf.PI * 2f, p.Frame);
            Ring(b, ck, Front, .62f, .12f, .1f, 12, 0f, Mathf.PI * 2f, Gold, Gold);
            FBox(b, ck, Front, 0, .2f, .07f, .42f, .04f, DarkHole, DarkHole, .05f);
            FBox(b, ck, Front, .16f, 0, .32f, .07f, .04f, DarkHole, DarkHole, .05f);
            // belfry: paired arched openings with louvres on every side
            foreach (var o in new[] { Front, Back, Left, Right })
            {
                Vector3 face = new Vector3(o.x * tw * .5f, Plinth + th - 2.5f, tz + o.z * tw * .5f);
                for (int s = -1; s <= 1; s += 2)
                {
                    Vector3 sp = face + FR(o) * s * .62f;
                    ArchOpening(b, sp, o, .38f, 1.1f, DarkHole, trim, .16f, 5);
                    for (int l = 0; l < 3; l++) FBox(b, sp, o, 0, -.85f + l * .3f, .74f, .06f, .1f, p.WoodDark, p.WoodDark, .04f);
                }
                FBox(b, face, o, 0, -.55f, .16f, 1.1f, .14f, trim, trim);
            }
            // crown: cornice, corner pinnacles, octagonal spire with lucarnes, finial
            float cy = Plinth + th;
            b.BoxB(0, cy - .1f, tz, tw + .6f, .45f, tw + .6f, trim, p.Top(trim));
            for (int sx = -1; sx <= 1; sx += 2) for (int sz = -1; sz <= 1; sz += 2)
            {
                Vector3 pc = new Vector3(sx * (tw * .5f + .05f), cy + .35f, tz + sz * (tw * .5f + .05f));
                b.Cyl(pc, .32f, .32f, .9f, 4, trim, p.Top(trim), Mathf.PI * .25f);
                b.Cyl(pc + Vector3.up * .9f, .36f, 0f, 1.4f, 4, p.Top(trim), p.Top(trim), Mathf.PI * .25f);
            }
            if (p.IsDry)
            {
                Vector3 dc = new Vector3(0, cy + .35f, tz);
                b.Cyl(dc, 1.9f, 1.9f, 1.4f, 12, wall, wall);
                for (int k = 0; k < 4; k++) { float a = k * Mathf.PI * .5f; var o = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)); ArchOpening(b, dc + o * 1.86f + Vector3.up * .9f, o, .25f, .4f, DarkHole, trim, .1f, 5); }
                float[] rr = { 2.05f, 1.9f, 1.5f, .9f }, hh = { .55f, .5f, .45f, .4f };
                float yy = cy + 1.75f;
                for (int k = 0; k < 3; k++) { b.Cyl(new Vector3(0, yy, tz), rr[k], rr[k + 1], hh[k], 12, roof, roof); yy += hh[k]; }
                b.Cyl(new Vector3(0, yy, tz), .9f, 0f, .6f, 12, roof, roof);
                b.Box(new Vector3(0, yy + 1.1f, tz), new Vector3(.12f, 1.2f, .12f), Gold, Gold);
                b.Box(new Vector3(0, yy + 1.3f, tz), new Vector3(.6f, .12f, .12f), Gold, Gold);
            }
            else
            {
                float sh = v == 0 ? 8.5f : 6.5f, sr = tw * .5f * 1.02f;
                b.Cyl(new Vector3(0, cy + .35f, tz), sr, 0f, sh, 8, p.Top(roof), p.Top(roof), Mathf.PI / 8f);
                b.Cyl(new Vector3(0, cy + .25f, tz), sr + .12f, sr + .12f, .2f, 8, roof * .8f, p.Top(roof * .8f), Mathf.PI / 8f);
                for (int k = 0; k < 4; k++)
                {
                    float a = k * Mathf.PI * .5f; var o = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                    Vector3 lc = new Vector3(0, cy + 1.1f, tz) + o * (sr * Mathf.Cos(Mathf.PI / 8f) * .8f);
                    b.Box(lc + Vector3.up * .55f, new Vector3(.8f, 1.1f, .8f), FQ(o), wall, wall);
                    Pane(b, lc + o * .42f + Vector3.up * .5f, o, .35f, .6f, DarkHole);
                    b.Cyl(lc + Vector3.up * 1.1f, .62f, 0f, .7f, 4, p.Top(roof), p.Top(roof), Mathf.PI * .25f + a);
                }
                Vector3 fin = new Vector3(0, cy + .35f + sh, tz);
                b.Cyl(fin - Vector3.up * .1f, .14f, .14f, .3f, 6, Gold, Gold);
                b.Box(fin + Vector3.up * .8f, new Vector3(.12f, 1.3f, .12f), Gold, Gold);
                b.Box(fin + Vector3.up * 1.05f, new Vector3(.65f, .12f, .12f), Gold, Gold);
            }
        }

        // =============================================================== keep
        static void Corbels(BB b, Vector3 a, Vector3 e, float y, Vector3 o, Color stone, float step = 1.1f)
        {
            float len = (e - a).magnitude; int n = Mathf.Max(1, Mathf.RoundToInt(len / step));
            for (int i = 0; i < n; i++)
            {
                Vector3 c = a + (e - a) * ((i + .5f) / n);
                FBox(b, new Vector3(c.x, y, c.z), o, 0, -.25f, .38f, .5f, .62f, stone, stone);
                FBox(b, new Vector3(c.x, y, c.z), o, 0, -.7f, .34f, .4f, .32f, stone, stone);
            }
        }

        static void KeepHD(BB b, int v)
        {
            var p = b.P; Vector2 dim = KeepDims[v]; float w = dim.x, d = dim.y, h = 14f;
            Color st = p.Stone, sd = p.StoneDark, roof = b.Pick(p.Roofs);
            Frustum(b, 0, -Foundation, 0, w + 1.8f, d + 1.8f, w + .3f, d + .3f, Foundation + 2.2f, sd, p.Top(sd));
            b.BoxB(0, 2.2f, 0, w, h - 2.2f, d, st, p.Top(st));
            Band(b, 0, 0, w, d, 2.3f, .3f, .14f, sd);
            Band(b, 0, 0, w, d, 7.6f, .3f, .1f, sd);
            // flat corner buttresses
            for (int sx = -1; sx <= 1; sx += 2) for (int sz = -1; sz <= 1; sz += 2)
            {
                b.BoxB(sx * (w * .5f - .5f), 2.2f, sz * (d * .5f + .15f), 1.6f, h - 3.4f, .3f, st * 1.03f, p.Top(st));
                b.BoxB(sx * (w * .5f + .15f), 2.2f, sz * (d * .5f - .5f), .3f, h - 3.4f, 1.6f, st * 1.03f, p.Top(st));
            }
            // machicolated parapet
            float hw = w * .5f + .55f, hd = d * .5f + .55f, py = h;
            Corbels(b, new Vector3(-w * .5f + 1.4f, 0, -d * .5f), new Vector3(w * .5f - 1.4f, 0, -d * .5f), py, Front, sd);
            Corbels(b, new Vector3(-w * .5f + 1.4f, 0, d * .5f), new Vector3(w * .5f - 1.4f, 0, d * .5f), py, Back, sd);
            Corbels(b, new Vector3(-w * .5f, 0, -d * .5f + 1.4f), new Vector3(-w * .5f, 0, d * .5f - 1.4f), py, Left, sd);
            Corbels(b, new Vector3(w * .5f, 0, -d * .5f + 1.4f), new Vector3(w * .5f, 0, d * .5f - 1.4f), py, Right, sd);
            b.BoxB(0, py, 0, 2f * hw, .35f, 2f * hd, sd, p.Top(sd));
            b.Box(new Vector3(0, py + .85f, -hd + .25f), new Vector3(2f * hw, 1f, .5f), st, p.Top(st));
            b.Box(new Vector3(0, py + .85f, hd - .25f), new Vector3(2f * hw, 1f, .5f), st, p.Top(st));
            b.Box(new Vector3(-hw + .25f, py + .85f, 0), new Vector3(.5f, 1f, 2f * hd), st, p.Top(st));
            b.Box(new Vector3(hw - .25f, py + .85f, 0), new Vector3(.5f, 1f, 2f * hd), st, p.Top(st));
            float my = py + 1.35f;
            b.Merlons(new Vector3(-hw + 1.6f, 0, -hd + .25f), new Vector3(hw - 1.6f, 0, -hd + .25f), my, .5f, st, 1.2f);
            b.Merlons(new Vector3(-hw + 1.6f, 0, hd - .25f), new Vector3(hw - 1.6f, 0, hd - .25f), my, .5f, st, 1.2f);
            b.Merlons(new Vector3(-hw + .25f, 0, -hd + 1.6f), new Vector3(-hw + .25f, 0, hd - 1.6f), my, .5f, st, 1.2f);
            b.Merlons(new Vector3(hw - .25f, 0, -hd + 1.6f), new Vector3(hw - .25f, 0, hd - 1.6f), my, .5f, st, 1.2f);
            // corbelled corner turrets with conical roofs
            for (int sx = -1; sx <= 1; sx += 2) for (int sz = -1; sz <= 1; sz += 2)
            {
                Vector3 c = new Vector3(sx * (w * .5f + .25f), 0, sz * (d * .5f + .25f));
                b.Cyl(c + Vector3.up * (h - 3.2f), .35f, 1.7f, 1.8f, 10, sd, sd);
                b.Cyl(c + Vector3.up * (h - 1.4f), 1.7f, 1.7f, 4.2f, 10, st, p.Top(st));
                b.Cyl(c + Vector3.up * (h + 2.5f), 1.85f, 1.85f, .3f, 10, sd, p.Top(sd));
                b.Cyl(c + Vector3.up * (h + 2.8f), 2.15f, 0f, 3.6f, 10, p.Top(roof), p.Top(roof));
                b.Cyl(c + Vector3.up * (h + 2.7f), 2.2f, 2.2f, .14f, 10, roof * .8f, p.Top(roof * .8f), 0f, false);
                b.Cyl(c + Vector3.up * (h + 6.3f), .1f, .1f, .5f, 6, Gold, Gold);
                var o1 = new Vector3(sx, 0, 0); var o2 = new Vector3(0, 0, sz);
                CrossSlit(b, c + o1 * 1.68f + Vector3.up * (h + .7f), o1, .9f);
                CrossSlit(b, c + o2 * 1.68f + Vector3.up * (h + .7f), o2, .9f);
            }
            // central roof / stair turret
            if (v == 1)
            {
                b.Hip(new Vector3(0, py + .35f, 0), w - 2.2f, d - 2.2f, 4.2f, .1f, roof);
                ChimneyHD(b, new Vector3(-w * .2f, py + 2.2f, d * .15f), 2.8f, sd);
            }
            else
            {
                Vector3 sc = new Vector3(w * .22f, py + .35f, d * .2f);
                b.Cyl(sc, 1.4f, 1.4f, 3.4f, 10, st, p.Top(st));
                b.Cyl(sc + Vector3.up * 3.3f, 1.65f, 1.65f, .25f, 10, sd, p.Top(sd));
                b.Cyl(sc + Vector3.up * 3.55f, 1.9f, 0f, 3f, 10, p.Top(roof), p.Top(roof));
                Pane(b, sc + new Vector3(0, 2f, -1.42f), Front, .3f, .8f, DarkHole);
                b.Box(sc + Vector3.up * 7.6f, new Vector3(.12f, 3.4f, .12f), p.WoodDark, p.WoodDark);
                b.Box(sc + new Vector3(.75f, 8.6f, 0), new Vector3(1.5f, .9f, .05f), p.Accents[0], p.Accents[0]);
                b.Box(sc + new Vector3(.75f, 8.6f, 0), new Vector3(.5f, .9f, .07f), p.Accents[2 % p.Accents.Length], p.Accents[2 % p.Accents.Length]);
                ChimneyHD(b, new Vector3(-w * .25f, py + .35f, -d * .15f), 1.6f, sd);
            }
            // windows: paired arched on each face (upper hall), cross slits lower
            foreach (var o in new[] { Front, Back, Left, Right })
            {
                float half = o.z != 0 ? d * .5f : w * .5f, span = o.z != 0 ? w : d;
                Vector3 c = o.z != 0 ? new Vector3(0, 0, o.z * half) : new Vector3(o.x * half, 0, 0);
                Vector3 r = FR(o);
                for (int s = -1; s <= 1; s += 2) ArchOpening(b, c + Vector3.up * 10.4f + r * s * .45f, o, .36f, 1.1f, p.Pane, sd, .2f, 5);
                FBox(b, c + Vector3.up * 9.85f, o, 0, 0, .2f, 1.1f, .16f, sd, sd);
                FBox(b, c + Vector3.up * 9.25f, o, 0, 0, 2.2f, .18f, .3f, sd, p.Top(sd));
                for (int i = -1; i <= 1; i += 2)
                {
                    CrossSlit(b, c + Vector3.up * 5.4f + r * i * span * .28f, o, 1.3f);
                    CrossSlit(b, c + Vector3.up * 10.2f + r * i * span * .3f, o, 1.1f);
                }
                Vector3 sp = c + Vector3.up * (py + .1f) + r * span * .2f;
                FBox(b, sp, o, 0, 0, .2f, .2f, 1.1f, sd, sd, .5f);   // water spout
            }
            // external stair along the front to the raised door
            {
                float fz0 = -d * .5f; int steps = 11; float x0 = -w * .5f + 1.4f, x1 = w * .02f, riseTo = 4.4f, s0 = 0f;
                for (int i = 0; i < steps; i++)
                {
                    float x = x0 + (x1 - x0) * (i + .5f) / steps, top = riseTo * (i + 1) / steps;
                    b.Box(new Vector3(x, (top - 1f) * .5f, fz0 - .85f), new Vector3((x1 - x0) / steps + .02f, top + 1f, 1.5f), sd, p.Top(sd));
                }
                b.Box(new Vector3(x1 + 1.2f, (riseTo - 1f) * .5f, fz0 - .95f), new Vector3(2.6f, riseTo + 1f, 1.7f), sd, p.Top(sd));
                b.Box(new Vector3((x0 + x1) * .5f, s0 + riseTo * .5f + .55f, fz0 - 1.55f), new Vector3((new Vector2(x1 - x0, riseTo)).magnitude, .4f, .25f),
                    Quaternion.Euler(0, 0, Mathf.Atan2(riseTo, x1 - x0) * Mathf.Rad2Deg), st, p.Top(st));
                b.Box(new Vector3(x1 + 1.2f, riseTo + .5f, fz0 - 1.75f), new Vector3(2.6f, .9f, .25f), st, p.Top(st));
                DoorHD(b, new Vector3(x1 + 1.2f, riseTo, fz0), Front, 1.5f, 2.6f, sd, p.Door, true, 0);
                Banner(b, new Vector3(-w * .16f, h - 2.4f, fz0), Front, 1.6f, 3.6f, p.Accents[0], p.Accents[1 % p.Accents.Length]);
                Banner(b, new Vector3(w * .16f + .3f, h - 2.4f, fz0), Front, 1.6f, 3.6f, p.Accents[0], p.Accents[1 % p.Accents.Length]);
            }
        }

        // =============================================================== gatehouse
        static void GatehouseHD(BB b)
        {
            var p = b.P; float gap = 4.6f, th = 10.5f, r = 2.55f, tx = gap * .5f + 2.35f, tzc = -.35f, d = 5.6f;
            Color st = p.Stone, sd = p.StoneDark, roof = b.Pick(p.Roofs);
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 c = new Vector3(s * tx, 0, tzc);
                b.Box(new Vector3(s * tx, (th - Foundation) * .5f, 1.2f), new Vector3(4.7f, th + Foundation, 3.2f), st, p.Top(st));   // rear block
                b.Cyl(c + Vector3.up * -Foundation, r + .6f, r, Foundation + 1.8f, 14, sd, p.Top(sd));
                b.Cyl(c + Vector3.up * 1.8f, r, r, th - 1.8f, 14, st, p.Top(st));
                b.Cyl(c + Vector3.up * 4.6f, r + .1f, r + .1f, .25f, 14, sd, p.Top(sd), 0f, false);
                b.Cyl(c + Vector3.up * 8f, r + .1f, r + .1f, .25f, 14, sd, p.Top(sd), 0f, false);
                for (int i = 0; i < 9; i++)
                {
                    float a = Mathf.PI + i * Mathf.PI / 8f; var o = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                    if (o.z > .35f) continue;
                    FBox(b, c + o * r + Vector3.up * (th - .2f), o, 0, -.25f, .4f, .5f, .5f, sd, sd);
                    FBox(b, c + o * r + Vector3.up * (th - .2f), o, 0, -.7f, .34f, .4f, .26f, sd, sd);
                }
                b.Cyl(c + Vector3.up * (th - .2f), r + .45f, r + .45f, 1.1f, 14, st, p.Top(st));
                b.MerlonRing(c, r + .3f, th + .9f, 12, st);
                b.Cyl(c + Vector3.up * (th + .9f), r + .05f, 0f, 4.6f, 14, p.Top(roof), p.Top(roof));
                b.Cyl(c + Vector3.up * (th + 5.4f), .1f, .1f, 1.8f, 6, p.WoodDark, p.WoodDark);
                b.Box(c + new Vector3(s * .5f, th + 6.75f, 0), new Vector3(1f, .6f, .05f), p.Accents[0], p.Accents[0]);
                foreach (float a in new[] { -Mathf.PI * .5f, -Mathf.PI * .5f + s * .9f })
                {
                    var o = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                    CrossSlit(b, c + o * r + Vector3.up * 3.2f, o, 1.2f);
                    CrossSlit(b, c + o * r + Vector3.up * 6.6f, o, 1.2f);
                }
                Banner(b, c + new Vector3(-s * .9f, 6.2f, 0) + Vector3.back * Mathf.Sqrt(r * r - .81f), Front, 1.1f, 3f, p.Accents[0], p.Accents[1 % p.Accents.Length]);
                // torch beside the gate
                Vector3 tf = new Vector3(s * (tx - 1.9f), 3.1f, tzc - Mathf.Sqrt(r * r - 3.61f) - .12f);
                b.Box(tf, new Vector3(.08f, .5f, .08f), Iron, Iron);
                b.Cyl(tf + Vector3.up * .25f, .12f, .02f, .35f, 5, Flame, Flame);
            }
            // centre block with the gate arch
            float fzg = -2.3f, spring = 3.2f, ar = gap * .5f - .3f;
            b.Box(new Vector3(0, (th - .8f - Foundation) * .5f, .3f), new Vector3(gap + .6f, th - .8f + Foundation, 5.2f), st, p.Top(st));
            Vector3 gf = new Vector3(0, 0, fzg);
            Vector3 sp = gf + Vector3.up * spring;
            Fan(b, sp, Front, ar, 10, 0f, Mathf.PI, p.Door);
            Pane(b, gf + Vector3.up * spring * .5f + Front * .03f, Front, ar * 2f, spring, p.Door);
            for (int i = 1; i < 7; i++) { float x = (i / 7f - .5f) * ar * 2f; float top = spring + Mathf.Sqrt(Mathf.Max(0f, ar * ar - x * x)) - .05f; FBox(b, gf, Front, x, top * .5f, .05f, top, .04f, p.Door * .78f, p.Door * .78f, .02f); }
            foreach (float yy in new[] { 1f, 2.6f }) FBox(b, gf, Front, 0, yy, ar * 2f, .14f, .05f, Iron, Iron, .02f);
            for (int i = 0; i < 7; i++)   // portcullis (raised, upper part visible)
            {
                float x = (i / 6f - .5f) * (ar * 2f - .3f); float top = spring + Mathf.Sqrt(Mathf.Max(0f, ar * ar - x * x)) - .1f, bot = spring - .4f;
                if (top <= bot) continue;
                FBox(b, gf, Front, x, (top + bot) * .5f, .1f, top - bot, .1f, Iron, Iron, .08f);
                b.Cyl(gf + FR(Front) * x + Front * .13f + Vector3.up * (bot - .3f), .01f, .07f, .3f, 4, Iron, Iron);
            }
            foreach (float yy in new[] { spring - .15f, spring + .55f, spring + 1.25f })
            {
                float hwid = yy <= spring ? ar : Mathf.Sqrt(Mathf.Max(0f, ar * ar - (yy - spring) * (yy - spring)));
                FBox(b, gf, Front, 0, yy, hwid * 2f - .2f, .1f, .1f, Iron, Iron, .08f);
            }
            Ring(b, sp, Front, ar, .5f, .3f, 9, 0f, Mathf.PI, st * 1.04f, sd);
            FBox(b, gf, Front, -ar - .25f, spring * .5f, .5f, spring, .3f, st * 1.04f, st);
            FBox(b, gf, Front, ar + .25f, spring * .5f, .5f, spring, .3f, st * 1.04f, st);
            // coat of arms
            Vector3 ca = gf + Vector3.up * (spring + ar + 1.3f);
            FBox(b, ca, Front, 0, .05f, .9f, .7f, .1f, p.Accents[1 % p.Accents.Length], p.Accents[1 % p.Accents.Length], .02f);
            { Vector3 rr = FR(Front), f0 = ca + Front * .13f - Vector3.up * .3f; b.Tri(f0 - rr * .45f, f0 + rr * .45f, f0 - Vector3.up * .35f, p.Accents[1 % p.Accents.Length], f0 - Front); }
            FBox(b, ca, Front, 0, 0, .14f, 1.1f, .04f, Gold, Gold, .12f);
            FBox(b, ca, Front, 0, .15f, .9f, .14f, .04f, Gold, Gold, .12f);
            // bretèche over the gate
            Vector3 br = gf + Vector3.up * (th - 2.6f);
            FBox(b, br, Front, 0, .2f, 2.4f, 1.5f, .9f, st, p.Top(st));
            for (int i = -1; i <= 1; i++) FBox(b, br, Front, i * .9f, -.75f, .32f, .5f, .7f, sd, sd);
            Pane(b, br + Front * .93f + Vector3.up * .25f, Front, .25f, .7f, DarkHole);
            b.Merlons(new Vector3(-1.1f, 0, fzg - .75f), new Vector3(1.1f, 0, fzg - .75f), th - 1.65f, .3f, st, .8f);
            // walkway parapets of the centre block
            b.Merlons(new Vector3(-gap * .5f - .3f, 0, fzg + .3f), new Vector3(gap * .5f + .3f, 0, fzg + .3f), th - .8f, .5f, st, 1.15f);
            b.Merlons(new Vector3(-gap * .5f - .3f, 0, 2.6f), new Vector3(gap * .5f + .3f, 0, 2.6f), th - .8f, .5f, st, 1.15f);
            for (int s = -1; s <= 1; s += 2) b.Merlons(new Vector3(s * tx - 2.2f, 0, 2.55f), new Vector3(s * tx + 2.2f, 0, 2.55f), th, .5f, st, 1.2f);
            // rear arch (simple)
            ArchOpening(b, new Vector3(0, spring, 2.9f), Back, ar, spring, DarkHole, sd, .4f, 7);
            // banner pole on the centre
            b.Box(new Vector3(0, th + 1.2f, .3f), new Vector3(.12f, 4f, .12f), p.WoodDark, p.WoodDark);
            b.Box(new Vector3(.8f, th + 2.6f, .3f), new Vector3(1.6f, 1f, .05f), p.Accents[0], p.Accents[0]);
        }
    }
}
