using System;
using System.Collections.Generic;
using UnityEngine;

namespace MassEngine.Game.Editor
{
    /// <summary>2026-10-15 detailed builders, part 2 (user approved the sample precision): remaining village, fort and ruin kinds.</summary>
    public static partial class ProcBuildingMeshes
    {
        static readonly Color Moss = H("#6E9F45"), Charred = H("#4A3B32"), Rope = H("#C8B48A");

        // =============================================================== shared bits
        static void PorchRoof(BB b, Vector3 fb, Vector3 o, float w, float y, float depth, Color roof, Color post, bool posts)
        {
            var p = b.P;
            var q = FQ(o) * Quaternion.Euler(16f, 0, 0);
            b.Box(fb + Vector3.up * y + q * new Vector3(0, 0, depth * .5f), new Vector3(w, .14f, depth + .1f), q, roof, p.Top(roof), true);
            for (int i = 1; i <= 2; i++) b.Box(fb + Vector3.up * y + q * new Vector3(0, .1f, depth * i / 2.4f), new Vector3(w + .04f, .08f, .14f), q, roof * .84f, p.Top(roof * .84f));
            for (int s = -1; s <= 1; s += 2)
            {
                if (posts) FBox(b, fb, o, s * (w * .5f - .15f), (y - .2f) * .5f, .16f, y - .2f, .16f, post, post, depth - .3f);
                else
                {
                    Vector3 a = fb + FR(o) * s * (w * .5f - .2f) + Vector3.up * (y - .9f), e = a + o * .8f + Vector3.up * .75f;
                    b.Box((a + e) * .5f, new Vector3(.12f, (e - a).magnitude, .12f), Quaternion.LookRotation(o, (e - a).normalized), post, post);
                }
            }
        }

        static void Bench(BB b, Vector3 at, Vector3 o)
        {
            var p = b.P;
            FBox(b, at, o, 0, .45f, 1.6f, .08f, .4f, p.Wood, p.Top(p.Wood), .05f);
            FBox(b, at, o, -.65f, .22f, .1f, .44f, .34f, p.WoodDark, p.WoodDark, .08f);
            FBox(b, at, o, .65f, .22f, .1f, .44f, .34f, p.WoodDark, p.WoodDark, .08f);
        }

        static void Tuft(BB b, Vector3 at, float s = 1f)
        {
            Color g = b.P.IsDry ? H("#A8A04A") : b.P.IsSnow ? H("#5A8A50") : Moss;
            for (int i = 0; i < 3; i++)
                b.Box(at + new Vector3((i - 1) * .12f * s, .18f * s, 0), new Vector3(.08f * s, .4f * s, .05f * s), Quaternion.Euler(b.Rand(-25f, 25f), i * 60f, (i - 1) * 25f), g, g);
        }

        static void Sack(BB b, Vector3 at, float yaw)
        {
            Color c = b.P.Canvas * .92f;
            b.Box(at + Vector3.up * .3f, new Vector3(.55f, .6f, .4f), Quaternion.Euler(0, yaw, 0), c, b.P.Top(c));
            b.Box(at + Vector3.up * .66f, new Vector3(.3f, .14f, .25f), Quaternion.Euler(0, yaw, 0), c * .9f, b.P.Top(c * .9f));
        }

        static void LogPile(BB b, Vector3 c, Vector3 o, float len, int rows)
        {
            var p = b.P; Vector3 r = FR(o);
            for (int row = 0; row < rows; row++)
                for (int i = 0; i < 6 - row; i++)
                    b.CylAxis(c + r * (-.8f + row * .16f + i * .32f) + Vector3.up * (.16f + row * .28f), o, .15f, len, 6, p.Wood, p.Hay * .9f);
        }

        /// <summary>Wall of staggered stone blocks with a jagged top (heights per column) and real holes.</summary>
        static void BlockWall(BB b, Vector3 a, Vector3 e, float t, float[] colH, Func<float, float, bool> hole, Color stone, Color stone2)
        {
            Vector3 d = e - a; float len = d.magnitude; var q = Quaternion.LookRotation(Vector3.Cross(d / len, Vector3.up), Vector3.up);
            int n = colH.Length; float cw = len / n, course = .5f;
            float hMax = 0f; foreach (var h in colH) hMax = Mathf.Max(hMax, h);
            b.Box(a + d * .5f + Vector3.up * (-Foundation * .5f + .02f), new Vector3(len + .1f, Foundation + .04f, t + .1f), q, b.P.StoneDark, b.P.Top(b.P.StoneDark));
            for (int k = 0; k * course < hMax; k++)
            {
                float y0 = k * course + .04f, off = (k % 2) * cw * .5f;
                for (float u0 = -off; u0 < len - .01f; u0 += cw)
                {
                    float s0 = Mathf.Max(0f, u0), s1 = Mathf.Min(len, u0 + cw); if (s1 - s0 < .12f) continue;
                    float um = (s0 + s1) * .5f; int col = Mathf.Clamp((int)(um / cw), 0, n - 1);
                    if (y0 + course > colH[col] + .01f) continue;
                    if (hole != null && hole(um, y0 + course * .5f)) continue;
                    Color c = b.Chance(.3f) ? stone2 : (b.Chance(.5f) ? stone : stone * .96f);
                    bool top = y0 + course * 2f > colH[col] + .01f;
                    b.Box(a + d * (um / len) + Vector3.up * (y0 + course * .5f - .02f) + q * new Vector3(0, 0, b.Rand(-.04f, .04f)), new Vector3(s1 - s0 - .015f, course - .015f, t), q, c, top ? b.P.Top(c) : c);
                }
            }
        }

        static float[] Profile(BB b, int n, float hMax, float hMin, float rough, float start)
        {
            var h = new float[n]; float prev = start;
            for (int i = 0; i < n; i++) { prev = Mathf.Clamp(prev + b.Rand(-rough, rough * .75f), hMin, hMax); h[i] = Mathf.Round(prev / .5f) * .5f; }
            return h;
        }

        // =============================================================== cottage
        static void CottageHD(BB b, int v)
        {
            var p = b.P; Vector2 dim = CottageDims[v]; float w = dim.x, d = dim.y, wh = 3.1f;
            Color wall = b.Pick(p.Walls), roof = b.Pick(p.Roofs), shc = Shutter(b), frame = p.IsDry ? p.Frame : p.Timber * 1.25f;
            bool stone = !p.IsDry && v == 1; Color wc = stone ? p.Stone : wall;
            b.Base(w, d, p.StoneDark);
            Band(b, 0, 0, w, d, Plinth - .02f, .22f, .18f, p.StoneDark);
            b.BoxB(0, Plinth, 0, w, wh, d, wc, wc);
            for (int sx = -1; sx <= 1; sx += 2) for (int sz = -1; sz <= 1; sz += 2) Quoins(b, sx * w * .5f, sz * d * .5f, sx, sz, Plinth + .1f, Plinth + wh, stone ? p.StoneDark : p.Stone);
            if (!p.IsDry && v == 2) { HalfTimber(b, w, d, Plinth + .2f, Plinth + wh); Braces(b, w, d, Plinth + .2f, Plinth + wh); }
            float y = Plinth + wh;
            float doorX = v == 1 ? -w * .22f : 0f;
            Vector3 fz = new Vector3(0, Plinth, -d * .5f);
            DoorHD(b, fz + new Vector3(doorX, 0, 0), Front, 1.05f, 2.05f, frame, p.Door, v == 1, 1);
            for (int i = -1; i <= 1; i += 2) if (Mathf.Abs(i * w * .3f - doorX) > 1.3f) WinHD(b, fz + new Vector3(i * w * .3f, 1.65f, 0), Front, .9f, 1f, frame, true, shc, true);
            WinHD(b, new Vector3(-w * .2f, Plinth + 1.65f, d * .5f), Back, .85f, .95f, frame, false, shc, false);
            WinHD(b, new Vector3(w * .22f, Plinth + 1.65f, d * .5f), Back, .85f, .95f, frame, false, shc, false);
            WinHD(b, new Vector3(-w * .5f, Plinth + 1.65f, 0), Left, .75f, .9f, frame, false, shc, false);
            Bench(b, fz + new Vector3(doorX + (v == 1 ? 1.9f : -1.9f) * (w > 6.5f ? 1f : .8f), -Plinth, 0), Front);
            if (p.IsDry)
            {
                if (v == 1)
                {
                    b.Hip(new Vector3(0, y, 0), w, d, 1.5f, .45f, roof);
                    ChimneyHD(b, new Vector3(w * .25f, y + .6f, 0), 1.4f, p.Stone);
                }
                else
                {
                    b.FlatRoof(new Vector3(0, y, 0), w, d, wall);
                    for (int i = 0; i < 3; i++) b.Box(new Vector3(-w * .5f - .1f, y - .35f, -d * .3f + i * d * .3f), new Vector3(.4f, .18f, .18f), p.Wood, p.Wood);   // vigas
                    b.Cyl(new Vector3(-w * .3f, y + .24f, d * .25f), .3f, .2f, .7f, 7, Clay, DarkHole);
                    for (int sx = -1; sx <= 1; sx += 2) b.Box(new Vector3(w * .15f + sx * 1.1f, y + 1.15f, -d * .1f), new Vector3(.1f, 1.8f, .1f), p.Wood, p.Wood);
                    for (int i = 0; i < 4; i++) b.Box(new Vector3(w * .15f, y + 2.1f, -d * .1f - .9f + i * .6f), new Vector3(2.5f, .08f, .1f), p.Wood, p.Wood);
                }
                PorchRoof(b, fz + new Vector3(doorX, 0, 0), Front, 2f, 2.65f, 1.1f, b.Pick(p.Accents), p.Wood, false);
                return;
            }
            float rise = p.IsSnow ? 3.1f : 2.6f;
            if (v == 1)
            {
                GableHD(b, new Vector3(0, y, 0), d, w, rise + .3f, .45f, roof, wall, true, 4, p.Timber);
                WinHD(b, new Vector3(0, y + rise * .35f, -d * .5f), Front, .55f, .65f, frame, false, shc, false, false);
                ChimneyHD(b, new Vector3(w * .26f, y + (rise + .3f) * .48f - .1f, d * .2f), (rise + .3f) * .52f + 1.2f, p.StoneDark);
            }
            else
            {
                GableHD(b, new Vector3(0, y, 0), w, d, rise, .45f, roof, wall, false, 4, p.Timber);
                if (v == 0) Dormer(b, -w * .12f, y, d * .5f, rise, .25f, wall, roof, frame);
                ChimneyHD(b, new Vector3(w * .3f, y + rise * .6f - .1f, d * .2f), rise * .4f + 1.3f, p.StoneDark);
            }
            PorchRoof(b, fz + new Vector3(doorX, 0, 0), Front, 1.9f, 2.6f, .9f, roof, p.Timber, false);
            // woodpile lean-to on the right side
            Vector3 sf = new Vector3(w * .5f, 0, d * .1f);
            LogPile(b, sf + Right * .45f, Right, .8f, 3);
            b.Box(sf + Right * .55f + Vector3.up * 1.35f, new Vector3(1.2f, .1f, 2.2f), Quaternion.Euler(0, 0, -14f), roof * .9f, p.Top(roof * .9f));
        }

        // =============================================================== longhouse
        static void LonghouseHD(BB b, int v)
        {
            var p = b.P; Vector2 dim = LongDims[v]; float w = dim.x, d = dim.y, wh = 3.2f;
            Color wall = b.Pick(p.Walls), roof = b.Pick(p.Roofs), shc = Shutter(b), frame = p.IsDry ? p.Frame : p.Timber * 1.25f;
            b.Base(w, d, p.StoneDark);
            Band(b, 0, 0, w, d, Plinth - .02f, .22f, .18f, p.StoneDark);
            b.BoxB(0, Plinth, 0, w, wh, d, wall, wall);
            for (int sx = -1; sx <= 1; sx += 2) for (int sz = -1; sz <= 1; sz += 2) Quoins(b, sx * w * .5f, sz * d * .5f, sx, sz, Plinth + .1f, Plinth + wh, p.Stone);
            if (!p.IsDry) { HalfTimber(b, w, d, Plinth + .2f, Plinth + wh); Braces(b, w, d, Plinth + .2f, Plinth + wh); }
            float y = Plinth + wh;
            Vector3 fz = new Vector3(0, Plinth, -d * .5f);
            float doorX = -w * .12f;
            DoorHD(b, fz + new Vector3(doorX, 0, 0), Front, 1.2f, 2.1f, frame, p.Door, false, 1);
            foreach (float x in new[] { -w * .38f, -w * .25f, w * .1f, w * .24f, w * .38f })
                if (Mathf.Abs(x - doorX) > 1.2f) WinHD(b, fz + new Vector3(x, 1.7f, 0), Front, .85f, 1f, frame, true, shc, x > 0);
                        for (int i = 0; i < 4; i++) WinHD(b, new Vector3(((i + .5f) / 4f - .5f) * w, Plinth + 1.7f, d * .5f), Back, .8f, .95f, frame, false, shc, false);
            WinHD(b, new Vector3(-w * .5f, Plinth + 1.7f, 0), Left, .75f, .9f, frame, false, shc, false);
            WinHD(b, new Vector3(w * .5f, Plinth + 1.7f, 0), Right, .75f, .9f, frame, false, shc, false);
            Barrel(b, new Vector3(-w * .5f + .6f, 0, -d * .5f - .6f));
            Barrel(b, new Vector3(-w * .5f + 1.4f, 0, -d * .5f - .5f));
            Bench(b, fz + new Vector3(w * .17f, -Plinth, 0), Front);
            if (p.IsDry)
            {
                b.FlatRoof(new Vector3(0, y, 0), w, d, wall);
                int n = Mathf.RoundToInt(w / 1.1f);
                for (int i = 0; i < n; i++) b.Box(new Vector3(((i + .5f) / n - .5f) * w, y + .95f, -d * .5f), new Vector3(w / n * .5f, .3f, .3f), wall, p.Top(wall));
                // front arcade
                for (int i = 0; i < 4; i++)
                {
                    float x = doorX - 3f + i * 2f;
                    b.Box(new Vector3(x, Plinth + 1.3f, -d * .5f - 1.4f), new Vector3(.3f, 2.6f, .3f), wall, wall);
                }
                b.Box(new Vector3(doorX, Plinth + 2.85f, -d * .5f - .75f), new Vector3(6.6f, .5f, 1.6f), wall, p.FlatTop);
                for (int i = 0; i < 3; i++) ArchOpening(b, new Vector3(doorX - 2f + i * 2f, Plinth + 2.1f, -d * .5f - 1.56f), Front, .8f, 0f, wall * .8f, wall, .14f, 5);
                b.Cyl(new Vector3(w * .3f, y + .24f, d * .2f), .3f, .2f, .7f, 7, Clay, DarkHole);
                return;
            }
            float rise = p.IsSnow ? 3.4f : 2.9f;
            GableHD(b, new Vector3(0, y, 0), w, d, rise, .5f, roof, wall, false, 5, p.Timber);
            if (v == 0) { Dormer(b, -w * .28f, y, d * .5f, rise, .25f, wall, roof, frame); Dormer(b, w * .22f, y, d * .5f, rise, .25f, wall, roof, frame); }
            else
            {   // cross gable over the door
                float cw = 3.4f, cx = doorX;
                GableHD(b, new Vector3(cx, y, -d * .25f + .2f), d * .5f + .4f + .2f, cw, rise * .85f, .4f, roof, wall, true, 4, p.Timber);
                WinHD(b, new Vector3(cx, y + rise * .3f, -d * .5f - .1f), Front, .6f, .7f, frame, false, shc, false, false);
            }
            ChimneyHD(b, new Vector3(-w * .32f, y + rise * .8f - .1f, d * .1f), rise * .25f + 1.4f, p.StoneDark);
            ChimneyHD(b, new Vector3(w * .34f, y + rise * .8f - .1f, d * .1f), rise * .25f + 1.2f, p.StoneDark);
            PorchRoof(b, fz + new Vector3(doorX, 0, 0), Front, 2.6f, 2.7f, 1.4f, roof, p.Timber, true);
        }

        // =============================================================== barn
        static void BarnHD(BB b, int v)
        {
            var p = b.P; Vector2 dim = BarnDims[v]; float w = dim.x, d = dim.y, wh = 4.2f;
            Color wall = p.IsDry ? b.Pick(p.Walls) : p.Barn, roof = p.IsDry ? b.Pick(p.Roofs) : b.Pick(new[] { p.Roofs[p.Roofs.Length - 1], p.Roofs[0], p.WoodDark });
            Color trim = p.IsDry ? p.Wood : p.Frame, batten = wall * .82f;
            b.Base(w, d, p.StoneDark);
            b.BoxB(0, Plinth, 0, w, .6f, d, p.Stone, p.Top(p.Stone));
            b.BoxB(0, Plinth + .6f, 0, w - .04f, wh - .6f, d - .04f, wall, wall);
            float y = Plinth + wh;
            if (!p.IsDry)
            {
                foreach (var o in new[] { Front, Back })
                {
                    int n = Mathf.RoundToInt(w / .7f);
                    for (int i = 1; i < n; i++) b.Box(new Vector3(-w * .5f + w * i / n, Plinth + .6f + (wh - .6f) * .5f, o.z * (d * .5f + .02f)), new Vector3(.08f, wh - .7f, .06f), batten, batten);
                }
                foreach (var o in new[] { Left, Right })
                {
                    int n = Mathf.RoundToInt(d / .7f);
                    for (int i = 1; i < n; i++) b.Box(new Vector3(o.x * (w * .5f + .02f), Plinth + .6f + (wh - .6f) * .5f, -d * .5f + d * i / n), new Vector3(.06f, wh - .7f, .08f), batten, batten);
                }
            }
            for (int sx = -1; sx <= 1; sx += 2) for (int sz = -1; sz <= 1; sz += 2) b.Box(new Vector3(sx * (w * .5f + .02f), Plinth + wh * .5f, sz * (d * .5f + .02f)), new Vector3(.32f, wh, .32f), trim, p.Top(trim));
            Band(b, 0, 0, w, d, Plinth + wh - .2f, .26f, .06f, trim);
            // big double door with X braces + frame
            Vector3 fz = new Vector3(0, Plinth, -d * .5f);
            float dw = 3.8f, dh = 3.4f;
            FBox(b, fz, Front, 0, dh * .5f, dw, dh, .1f, p.WoodDark, p.WoodDark, .02f);
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 c0 = fz + Front * .14f + FR(Front) * s * dw * .25f;
                FBox(b, c0, Front, -dw * .25f + .07f, dh * .5f, .14f, dh, .06f, trim, trim);
                FBox(b, c0, Front, dw * .25f - .07f, dh * .5f, .14f, dh, .06f, trim, trim);
                FBox(b, c0, Front, 0, .1f, dw * .5f, .16f, .06f, trim, trim);
                FBox(b, c0, Front, 0, dh - .1f, dw * .5f, .16f, .06f, trim, trim);
                float ang = Mathf.Atan2(dh, dw * .5f) * Mathf.Rad2Deg;
                b.Box(c0 + Vector3.up * dh * .5f, new Vector3((new Vector2(dw * .5f, dh)).magnitude - .2f, .14f, .06f), FQ(Front) * Quaternion.Euler(0, 0, s * ang), trim, trim);
            }
            FBox(b, fz, Front, 0, dh + .25f, dw + .6f, .2f, .3f, trim, p.Top(trim));
            FBox(b, fz, Front, 0, dh + .1f, dw * 2f, .12f, .1f, Iron, Iron, .2f);   // rail
            DoorHD(b, fz + new Vector3(w * .32f, 0, 0), Front, .9f, 2f, trim, p.WoodDark, false, 0);
            for (int i = 0; i < 3; i++) WinHD(b, new Vector3(((i + .5f) / 3f - .5f) * w, Plinth + 2.6f, d * .5f), Back, .7f, .6f, trim, false, default, false, false);
            WinHD(b, new Vector3(-w * .35f, Plinth + 2.6f, -d * .5f), Front, .7f, .6f, trim, false, default, false, false);
            // roof
            float rise = p.IsSnow ? 4.2f : 3.6f;
            GableHD(b, new Vector3(0, y, 0), w, d, rise, .55f, roof, wall, false, 6, trim);
            // hay loft doors in both gable ends with hoist
            foreach (var o in new[] { Left, Right })
            {
                Vector3 gf = new Vector3(o.x * w * .5f, y + 1.1f, 0);
                FBox(b, gf, o, 0, 0, 1.3f, 1.3f, .08f, p.WoodDark, p.WoodDark);
                FBox(b, gf, o, 0, 0, 1.5f, .12f, .1f, trim, trim, .02f);
                FBox(b, gf, o, 0, .72f, 1.6f, .14f, .14f, trim, trim);
                FBox(b, gf, o, 0, -.5f, .9f, .4f, .5f, p.Hay, p.Top(p.Hay), .05f);
                FBox(b, gf, o, 0, 1.75f, .2f, .2f, 1.1f, p.Timber, p.Top(p.Timber));
                FBox(b, gf, o, 0, 1.15f, .04f, 1.1f, .04f, Rope, Rope, .95f);
            }
            // cupola with weathervane
            Vector3 cu = new Vector3(w * .1f, y + rise - .3f, 0);
            b.Box(cu + Vector3.up * .6f, new Vector3(1.1f, 1.2f, 1.1f), trim, trim);
            foreach (var o in new[] { Front, Back, Left, Right }) for (int l = 0; l < 3; l++) FBox(b, cu + o * .55f + Vector3.up * (.35f + l * .25f), o, 0, 0, .8f, .07f, .06f, p.WoodDark, p.WoodDark);
            b.Cyl(cu + Vector3.up * 1.2f, 1f, 0f, 1f, 4, p.Top(roof), p.Top(roof), Mathf.PI * .25f);
            b.Box(cu + Vector3.up * 2.6f, new Vector3(.06f, 1f, .06f), Iron, Iron);
            b.Box(cu + Vector3.up * 2.9f, new Vector3(.7f, .2f, .04f), Iron, Iron);
            // hay bales by the door
            for (int i = 0; i < 3; i++) b.BoxB(-w * .5f + .9f + i * 1.25f * (i == 2 ? .5f : 1f), i == 2 ? .7f : 0f, -d * .5f - .9f, 1.15f, .7f, .8f, p.Hay, p.Top(p.Hay));
        }

        // =============================================================== windmill
        static void WindmillHD(BB b, int v)
        {
            var p = b.P; float r0 = 3.1f, r1 = 2.3f, h = 9f; int n = 8; float rot = Mathf.PI / 8f;
            Color wall = v == 0 ? p.Stone : b.Pick(p.Walls), roof = b.Pick(p.Roofs), frame = p.Frame;
            b.Cyl(new Vector3(0, -Foundation, 0), r0 + .4f, r0 + .35f, Foundation + Plinth, n, p.StoneDark, p.Top(p.StoneDark), rot);
            b.Cyl(new Vector3(0, Plinth, 0), r0, r1, h, n, wall, wall, rot);
            float R(float y) => r0 + (r1 - r0) * (y - Plinth) / h;
            foreach (float yy in new[] { Plinth + .5f, Plinth + 4.4f }) b.Cyl(new Vector3(0, yy - .12f, 0), R(yy) + .12f, R(yy) + .1f, .25f, n, p.StoneDark, p.Top(p.StoneDark), rot, false);
            // facets: normals at k*45°
            Vector3 Fn(int k) { float a = k * Mathf.PI / 4f; return new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)); }
            float Ap(float y) => R(y) * Mathf.Cos(Mathf.PI / 8f);
            DoorHD(b, new Vector3(0, Plinth, -Ap(Plinth + 1f) + .05f), Front, 1.1f, 2.2f, p.StoneDark, p.Door, true, 2);
            WinHD(b, Fn(1) * Ap(Plinth + 2.2f) + Vector3.up * (Plinth + 2.2f), Fn(1), .6f, .8f, frame, true, Shutter(b), true, false);
            WinHD(b, Fn(4) * Ap(Plinth + 3.4f) + Vector3.up * (Plinth + 3.4f), Fn(4), .6f, .8f, frame, false, default, false, false);
            WinHD(b, Fn(6) * Ap(Plinth + 6.2f) + Vector3.up * (Plinth + 6.2f), Fn(6), .6f, .8f, frame, false, default, false, false);
            WinHD(b, Fn(3) * Ap(Plinth + 7.4f) + Vector3.up * (Plinth + 7.4f), Fn(3), .55f, .7f, frame, false, default, false, false);
            // reefing gallery at 4.6 m
            float gy = Plinth + 4.6f, gr = R(gy) + 1.1f;
            b.Cyl(new Vector3(0, gy - .15f, 0), gr, gr, .18f, 12, p.Wood, p.Top(p.Wood));
            for (int i = 0; i < 12; i++)
            {
                float a = i * Mathf.PI / 6f; Vector3 o = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                b.Box(o * (gr - .1f) + Vector3.up * (gy + .45f), new Vector3(.08f, .9f, .08f), p.WoodDark, p.WoodDark);
                float a1 = (i + 1) * Mathf.PI / 6f; Vector3 o1 = new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1));
                Vector3 m = (o + o1) * .5f * (gr - .1f);
                b.Box(m + Vector3.up * (gy + .85f), new Vector3(.06f, .08f, (o1 - o).magnitude * (gr - .1f) + .05f), Quaternion.LookRotation(o1 - o), p.Wood, p.Wood);
                if (i % 3 == 0) { Vector3 s0 = o * (R(gy) + .05f) + Vector3.up * (gy - 1f), s1 = o * (gr - .2f) + Vector3.up * (gy - .1f); b.Box((s0 + s1) * .5f, new Vector3(.08f, .08f, (s1 - s0).magnitude), Quaternion.LookRotation(s1 - s0), p.WoodDark, p.WoodDark); }
            }
            // boat cap
            float cy = Plinth + h;
            b.Cyl(new Vector3(0, cy - .1f, 0), r1 + .35f, r1 + .35f, .3f, 12, p.WoodDark, p.Top(p.WoodDark));
            b.Cyl(new Vector3(0, cy + .2f, 0), r1 + .45f, r1 * .6f, 1.6f, 12, p.Top(roof), p.Top(roof));
            b.Cyl(new Vector3(0, cy + 1.8f, 0), r1 * .6f, 0f, 1.5f, 12, p.Top(roof), p.Top(roof));
            for (int i = 1; i <= 2; i++) b.Cyl(new Vector3(0, cy + .2f + i * .55f, 0), r1 + .45f - (r1 * .4f + .45f) * i * .55f / 1.6f + .06f, r1 + .45f - (r1 * .4f + .45f) * (i * .55f + .1f) / 1.6f + .06f, .1f, 12, roof * .84f, p.Top(roof * .84f), 0f, false);
            b.Cyl(new Vector3(0, cy + 3.3f, 0), .1f, .1f, .5f, 6, Gold, Gold);
            // tail pole at the back
            Vector3 t0 = new Vector3(0, cy + .3f, r1 + .3f), t1 = new Vector3(0, 1.4f, r0 + 2.2f);
            b.Box((t0 + t1) * .5f, new Vector3(.22f, .22f, (t1 - t0).magnitude), Quaternion.LookRotation(t1 - t0), p.WoodDark, p.WoodDark);
            b.CylAxis(t1 + Vector3.down * .2f, Vector3.right, .55f, .12f, 8, p.Wood, p.WoodDark);
            // hub + 4 lattice sails
            Vector3 hub = new Vector3(0, cy + .3f, -r1 - 1.1f);
            b.CylAxis(hub + new Vector3(0, 0, .55f), Vector3.forward, .38f, 1.3f, 8, p.WoodDark, p.WoodDark);
            b.Cyl(hub + Vector3.back * .1f, .32f, .32f, .01f, 8, Iron, Iron);
            float a0 = v * 20f + 15f;
            for (int i = 0; i < 4; i++)
            {
                var q = Quaternion.Euler(0, 0, a0 + i * 90f);
                Vector3 dir = q * Vector3.up, side = q * Vector3.right;
                b.Box(hub + dir * 3.8f, new Vector3(.26f, 7.6f, .22f), q, p.WoodDark, p.WoodDark);
                b.Box(hub + dir * 4.5f + side * 1.45f + Vector3.back * .12f, new Vector3(.1f, 5.6f, .1f), q, p.Wood, p.Wood);
                for (int k = 0; k < 8; k++) b.Box(hub + dir * (1.9f + k * .74f) + side * .75f + Vector3.back * .12f, new Vector3(1.5f, .08f, .08f), q, p.Wood, p.Wood);
                for (int k = 0; k < 2; k++) b.Box(hub + dir * 4.5f + side * (.45f + k * .55f) + Vector3.back * .1f, new Vector3(.05f, 5.4f, .05f), q, p.Wood, p.Wood);
                bool full = v == 0 || i % 2 == 0;
                b.Box(hub + dir * (full ? 4.5f : 5.8f) + side * .75f + Vector3.back * .05f, new Vector3(1.35f, full ? 5.4f : 2.8f, .04f), q, p.Canvas, p.Canvas);
            }
            // sacks by the door
            Sack(b, new Vector3(1.3f, 0, -r0 - .5f), 15f); Sack(b, new Vector3(1.9f, 0, -r0 - .2f), -20f); Sack(b, new Vector3(1.55f, .6f, -r0 - .35f), 40f);
        }

        // =============================================================== inn
        static void InnHD(BB b, int v)
        {
            var p = b.P; float w = 12f, d = 7f, g = 3.2f, u = 2.9f, jet = p.IsDry ? 0f : .4f;
            Color wall = b.Pick(p.Walls), roof = b.Pick(p.Roofs), stone = p.Stone, shc = Shutter(b), frame = p.IsDry ? p.Frame : p.Timber * 1.25f;
            b.Box(new Vector3(0, (Plinth - Foundation) * .5f, 2.75f), new Vector3(w + .4f, Foundation + Plinth, 12.9f), Quaternion.identity, p.StoneDark, p.Top(p.StoneDark));
            Color gcol = p.IsDry ? wall : stone;
            b.BoxB(0, Plinth, 0, w, g, d, gcol, gcol);
            for (int sx = -1; sx <= 1; sx += 2) for (int sz = -1; sz <= 1; sz += 2) Quoins(b, sx * w * .5f, sz * d * .5f, sx, sz, Plinth + .1f, Plinth + g, p.StoneDark);
            float y1 = Plinth + g, dU = d + 2f * jet;
            b.BoxB(0, y1, 0, w, u, dU, wall, wall);
            float y2 = y1 + u, wx = w * .5f - 3f;
            b.BoxB(wx, Plinth, 6.25f, 6f, g + u, 5.5f, wall, wall);
            if (!p.IsDry)
            {
                HalfTimber(b, w, dU, y1, y2); Braces(b, w, dU, y1, y2);
                int posts = Mathf.Max(2, Mathf.RoundToInt(w / 1.8f));
                for (int i = 0; i <= posts; i++) for (int s = -1; s <= 1; s += 2)
                    b.Box(new Vector3(-w * .5f + w * i / posts, y1 - .12f, s * (d * .5f + jet * .5f)), new Vector3(.2f, .24f, jet + .12f), p.Timber, p.Top(p.Timber));
                HalfTimber(b, 6f, 5.5f, y1, y2);
            }
            Vector3 fz = new Vector3(0, Plinth, -d * .5f);
            DoorHD(b, fz + new Vector3(-1f, 0, 0), Front, 1.7f, 2.6f, p.StoneDark * 1.1f, p.Door, true, 2);
            Lantern(b, fz + new Vector3(-2.3f, 2.4f, 0), Front); Lantern(b, fz + new Vector3(.3f, 2.4f, 0), Front);
            foreach (float x in new[] { -4.4f, 2.4f, 4.6f }) WinHD(b, fz + new Vector3(x, 1.7f, 0), Front, 1.4f, 1.15f, frame, false, shc, true);
            for (int i = 0; i < 4; i++) WinHD(b, new Vector3(((i + .5f) / 4f - .5f) * w * .92f, y1 + 1.45f, -dU * .5f), Front, .9f, 1.05f, frame, true, shc, true);
            for (int i = 0; i < 2; i++) WinHD(b, new Vector3(-w * .5f, y1 + 1.45f, ((i + .5f) / 2f - .5f) * d), Left, .85f, 1f, frame, false, shc, false);
            WinHD(b, new Vector3(-w * .5f, Plinth + 1.7f, 0), Left, .85f, 1f, frame, false, shc, false);
            WinHD(b, new Vector3(wx + 3f, Plinth + 1.7f, 6.25f), Right, .85f, 1f, frame, false, shc, false);
            WinHD(b, new Vector3(wx + 3f, y1 + 1.45f, 6.25f), Right, .85f, 1f, frame, false, shc, false);
            WinHD(b, new Vector3(wx, y1 + 1.45f, 9f), Back, .85f, 1f, frame, false, shc, false);
            // hanging sign on a wrought bracket
            Vector3 sb = fz + new Vector3(-3.2f, g + .1f, 0);
            FBox(b, sb, Front, 0, 0, .1f, .1f, 1.3f, Iron, Iron);
            FBox(b, sb, Front, 0, -.35f, .06f, .7f, .06f, Iron, Iron, .4f); 
            Color sign = b.Pick(p.Accents);
            b.Box(sb + Front * 1f + Vector3.down * .6f, new Vector3(.08f, .8f, .9f), sign, p.Wood);
            b.Box(sb + Front * 1f + Vector3.down * .6f, new Vector3(.12f, .3f, .3f), Gold, Gold);
            // terrace: tables, benches, barrels
            for (int t = 0; t < 2; t++)
            {
                Vector3 tc = new Vector3(3.6f + t * 2.4f - 4.8f * (v), 0, -d * .5f - 1.7f);
                if (v == 1) tc.x = -5f + t * 2.3f;
                b.Box(tc + Vector3.up * .75f, new Vector3(1.4f, .08f, .8f), p.Wood, p.Top(p.Wood));
                b.Box(tc + Vector3.up * .37f, new Vector3(.12f, .74f, .12f), p.WoodDark, p.WoodDark);
                b.Box(tc + new Vector3(0, .42f, -.65f), new Vector3(1.4f, .07f, .3f), p.Wood, p.Top(p.Wood));
                b.Box(tc + new Vector3(0, .42f, .65f), new Vector3(1.4f, .07f, .3f), p.Wood, p.Top(p.Wood));
                b.Cyl(tc + new Vector3(.3f, .79f, 0), .07f, .07f, .16f, 5, p.Frame, p.Frame);
            }
            Barrel(b, new Vector3(-w * .5f + .5f, 0, -d * .5f - .6f)); Barrel(b, new Vector3(-w * .5f + 1.3f, 0, -d * .5f - .5f)); Barrel(b, new Vector3(-w * .5f + .9f, .9f, -d * .5f - .55f));
            if (p.IsDry)
            {
                b.FlatRoof(new Vector3(0, y2, 0), w, dU, wall);
                b.FlatRoof(new Vector3(wx, y2, 6.25f), 6f, 5.5f, wall);
                for (int sx = -1; sx <= 1; sx += 2) for (int sz = -1; sz <= 1; sz += 2) b.Box(new Vector3(-2.5f + sx * 2f, y2 + 1.2f, sz * 1.6f), new Vector3(.14f, 1.9f, .14f), p.Wood, p.Wood);
                for (int i = 0; i < 8; i++) b.Box(new Vector3(-5f + i * .7f, y2 + 2.2f, 0), new Vector3(.1f, .1f, 3.6f), p.Wood, p.Wood);
                b.Box(new Vector3(-2.5f, y2 + .3f, 0), new Vector3(3f, .04f, 2f), b.Pick(p.Accents), b.Pick(p.Accents));
                b.Cyl(new Vector3(3f, y2 + .24f, 1.5f), .3f, .2f, .7f, 7, Clay, DarkHole);
                PorchRoof(b, fz + new Vector3(-1f, 0, 0), Front, 3f, 2.9f, 1.3f, b.Pick(p.Accents), p.Wood, true);
                return;
            }
            float rise = p.IsSnow ? 3.9f : 3.3f;
            GableHD(b, new Vector3(0, y2, 0), w, dU, rise, .5f, roof, wall, false, 6, p.Timber);
            GableHD(b, new Vector3(wx, y2, 6.6f), 6.2f, 6f, rise * .85f, .4f, roof, wall, true, 5, p.Timber);
            Dormer(b, -w * .3f, y2, dU * .5f, rise, .28f, wall, roof, frame);
            Dormer(b, w * .05f, y2, dU * .5f, rise, .28f, wall, roof, frame);
            ChimneyHD(b, new Vector3(-w * .36f, y2 + rise * .76f - .1f, dU * .12f), rise * .3f + 1.5f, p.StoneDark);
            ChimneyHD(b, new Vector3(wx, y2 + rise * .85f * .5f, 8.2f), rise * .5f + 1.2f, p.StoneDark);
            PorchRoof(b, fz + new Vector3(-1f, 0, 0), Front, 3f, 2.95f, 1.2f, roof, p.Timber, false);
        }

        // =============================================================== fort walls and towers
        static void WallSegHD(BB b)
        {
            var p = b.P; float L = 10f, t = 2.4f, h = 6.4f;
            Color st = p.Stone, sd = p.StoneDark;
            Frustum(b, 0, -Foundation, 0, L, t + 1.4f, L, t + .2f, Foundation + 1.4f, sd, p.Top(sd));
            b.Box(new Vector3(0, (h + 1.4f) * .5f, 0), new Vector3(L, h - 1.4f, t), Quaternion.identity, st, p.Top(sd));
            Band(b, 0, 0, L, t, 1.45f, .22f, .08f, sd);
            // stone patches (both faces) for texture
            for (int s = -1; s <= 1; s += 2)
                for (int i = 0; i < 9; i++)
                {
                    float x = -L * .5f + .6f + i * 1.1f + b.Rand(-.2f, .2f), y = b.Rand(1.9f, h - .6f);
                    b.Box(new Vector3(x, y, s * (t * .5f + .02f)), new Vector3(b.Rand(.6f, 1.1f), b.Rand(.35f, .5f), .08f), b.Chance(.5f) ? sd : st * 1.05f, st);
                }
            // outer parapet (−Z) with crenels and slits in every merlon
            float py = h;
            b.Box(new Vector3(0, py + .35f, -t * .5f + .3f), new Vector3(L, .7f, .6f), st, p.Top(st));
            int n = 7;
            for (int i = 0; i < n; i++)
            {
                float x = -L * .5f + (i + .5f) * L / n;
                b.Box(new Vector3(x, py + 1.2f, -t * .5f + .3f), new Vector3(L / n * .58f, 1f, .6f), st, p.Top(st));
                Pane(b, new Vector3(x, py + 1.1f, -t * .5f - .03f), Front, .14f, .6f, DarkHole);
            }
            // walkway + inner low wall
            b.Box(new Vector3(0, py + .08f, .1f), new Vector3(L, .16f, t - .6f), sd, p.Top(sd * 1.05f));
            b.Box(new Vector3(0, py + .3f, t * .5f - .15f), new Vector3(L, .6f, .3f), st, p.Top(st));
            // inner buttresses + outer drains
            for (int s = -1; s <= 1; s += 2)
            {
                FBox(b, new Vector3(s * L * .3f, 0, t * .5f), Back, 0, (h - 1.2f) * .5f, .9f, h - 1.2f, .7f, sd, p.Top(sd));
                var q = FQ(Back) * Quaternion.Euler(40f, 0, 0);
                b.Box(new Vector3(s * L * .3f, h - 1.1f, t * .5f + .45f), new Vector3(.95f, .14f, .8f), q, sd, p.Top(sd));
                FBox(b, new Vector3(s * L * .18f, py - .25f, -t * .5f), Front, 0, 0, .2f, .2f, .6f, sd, sd);
            }
            // torch on the inner side
            Vector3 tf = new Vector3(0, 3f, t * .5f + .05f);
            b.Box(tf, new Vector3(.08f, .5f, .08f), Iron, Iron);
            b.Cyl(tf + Vector3.up * .25f, .12f, .02f, .35f, 5, Flame, Flame);
            Tuft(b, new Vector3(-L * .4f, py + .16f, .2f), .8f);
        }

        static void TowerHD(BB b, int v)
        {
            var p = b.P; float r = 3.6f, h = 10.5f; int n = 12;
            Color st = p.Stone, sd = p.StoneDark, roof = b.Pick(p.Roofs);
            b.Cyl(new Vector3(0, -Foundation, 0), r + .7f, r, Foundation + 1.6f, n, sd, p.Top(sd));
            b.Cyl(new Vector3(0, 1.6f, 0), r, r, h - 1.6f, n, st, p.Top(st));
            foreach (float yy in new[] { 4.2f, 7.6f }) b.Cyl(new Vector3(0, yy, 0), r + .1f, r + .1f, .25f, n, sd, p.Top(sd), 0f, false);
            for (int i = 0; i < 16; i++)
            {
                float a = i * Mathf.PI / 8f; var o = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                FBox(b, o * r + Vector3.up * (h - .2f), o, 0, -.25f, .42f, .5f, .55f, sd, sd);
                FBox(b, o * r + Vector3.up * (h - .2f), o, 0, -.7f, .36f, .4f, .28f, sd, sd);
            }
            b.Cyl(new Vector3(0, h - .2f, 0), r + .5f, r + .5f, 1.1f, n, st, p.Top(st));
            b.MerlonRing(Vector3.zero, r + .32f, h + .9f, 14, st);
            for (int i = 0; i < 6; i++)
            {
                float a = i * Mathf.PI / 3f + .3f; var o = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                CrossSlit(b, o * r + Vector3.up * (i % 2 == 0 ? 3.2f : 6.1f), o, 1.2f);
            }
            DoorHD(b, new Vector3(0, 1.6f, r - .1f), Back, 1.2f, 2.3f, sd, p.Door, true, 0);
            for (int i = 0; i < 4; i++) b.Box(new Vector3(0, .2f + i * .37f, r + .5f + (3 - i) * .4f), new Vector3(1.4f, .4f + i * .37f * 0f, .45f), sd, p.Top(sd));
            if (v == 1)
            {
                b.Cyl(new Vector3(0, h + .9f, 0), r + .1f, 0f, 5.6f, n, p.Top(roof), p.Top(roof));
                b.Cyl(new Vector3(0, h + .85f, 0), r + .2f, r + .2f, .14f, n, roof * .8f, p.Top(roof * .8f), 0f, false);
                for (int k = 0; k < 3; k++)
                {
                    float a = k * Mathf.PI * 2f / 3f + .5f; var o = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                    Vector3 lc = o * (r * .55f) + Vector3.up * (h + 2.4f);
                    b.Box(lc, new Vector3(.7f, .9f, .7f), FQ(o), st, st);
                    Pane(b, lc + o * .36f, o, .3f, .5f, DarkHole);
                    b.Cyl(lc + Vector3.up * .45f, .55f, 0f, .6f, 4, p.Top(roof), p.Top(roof), Mathf.PI * .25f + a);
                }
                b.Box(new Vector3(0, h + 7.4f, 0), new Vector3(.1f, 2.4f, .1f), p.WoodDark, p.WoodDark);
                b.Box(new Vector3(.6f, h + 8.1f, 0), new Vector3(1.2f, .75f, .05f), p.Accents[0], p.Accents[0]);
            }
            else
            {
                b.Cyl(new Vector3(0, h + .9f, 0), r - .2f, r - .2f, .12f, n, sd, p.Top(sd * 1.05f));
                Vector3 hc = new Vector3(r * .3f, h + 1f, r * .3f);   // stair hatch hut
                b.BoxB(hc.x, hc.y, hc.z, 1.4f, 1.6f, 1.4f, st, p.Top(st));
                b.Cyl(hc + Vector3.up * 1.6f, 1.15f, 0f, .8f, 4, p.Top(roof), p.Top(roof), Mathf.PI * .25f);
                Pane(b, hc + new Vector3(0, .7f, -.72f), Front, .6f, 1.2f, DarkHole);
                Vector3 bc = new Vector3(-r * .35f, h + 1f, -r * .2f);   // brazier
                b.Cyl(bc, .25f, .45f, .6f, 6, Iron, DarkHole);
                b.Cyl(bc + Vector3.up * .6f, .3f, 0f, .5f, 5, Flame, Flame);
                b.Box(new Vector3(-r * .4f, h + 2.6f, r * .3f), new Vector3(.1f, 3.4f, .1f), p.WoodDark, p.WoodDark);
                b.Box(new Vector3(-r * .4f + .6f, h + 3.8f, r * .3f), new Vector3(1.2f, .75f, .05f), p.Accents[0], p.Accents[0]);
            }
        }

        static void WatchtowerHD(BB b, int v)
        {
            var p = b.P; float h = v == 0 ? 7.5f : 9f, s = 1.6f, sb = 2.1f;
            Color wd = p.WoodDark, wo = p.Wood;
            for (int sx = -1; sx <= 1; sx += 2) for (int sz = -1; sz <= 1; sz += 2)
            {
                Vector3 bot = new Vector3(sx * sb, -.3f, sz * sb), top = new Vector3(sx * s, h + .1f, sz * s);
                b.Box((bot + top) * .5f, new Vector3(.32f, (top - bot).magnitude, .32f), Quaternion.LookRotation(Vector3.forward, (top - bot).normalized), wd, p.Top(wd));
                b.BoxB(sx * sb, -.4f, sz * sb, .8f, .6f, .8f, p.Stone, p.Top(p.Stone));
            }
            float Hs(float y) => sb + (s - sb) * (y + .3f) / (h + .4f);
            foreach (var o in new[] { Front, Back, Left, Right })
            {
                Vector3 side = Vector3.Cross(Vector3.up, o);
                for (int lv = 0; lv < 2; lv++)
                {
                    float ya = .3f + lv * h * .48f, yb = ya + h * .45f;
                    Vector3 a0 = o * Hs(ya) - side * Hs(ya) + Vector3.up * ya, a1 = o * Hs(yb) + side * Hs(yb) + Vector3.up * yb;
                    Vector3 c0 = o * Hs(ya) + side * Hs(ya) + Vector3.up * ya, c1 = o * Hs(yb) - side * Hs(yb) + Vector3.up * yb;
                    foreach (var (pa, pb) in new[] { (a0, a1), (c0, c1) })
                        b.Box((pa + pb) * .5f + o * .12f, new Vector3(.14f, (pb - pa).magnitude, .12f), Quaternion.LookRotation(o, (pb - pa).normalized), wo, wo);
                    Vector3 hb = o * Hs(yb) + Vector3.up * yb;
                    b.Box(hb + o * .1f, new Vector3(o.z != 0 ? 2 * Hs(yb) + .3f : .16f, .16f, o.x != 0 ? 2 * Hs(yb) + .3f : .16f), wd, wd);
                }
            }
            // platform planks
            float pw = 2 * s + 1.2f;
            for (int i = 0; i < 9; i++) b.Box(new Vector3(-pw * .5f + (i + .5f) * pw / 9f, h + .12f, 0), new Vector3(pw / 9f - .03f, .14f, pw), i % 2 == 0 ? wo : wo * .92f, p.Top(i % 2 == 0 ? wo : wo * .92f));
            b.BoxB(0, h - .2f, 0, pw - .2f, .2f, pw - .2f, wd, wd);
            // railing
            foreach (var o in new[] { Front, Back, Left, Right })
            {
                Vector3 side = Vector3.Cross(Vector3.up, o);
                for (int i = 0; i < 5; i++) b.Box(o * (pw * .5f - .08f) + side * ((i / 4f - .5f) * (pw - .2f)) + Vector3.up * (h + .7f), new Vector3(.12f, 1.1f, .12f), wd, wd);
                b.Box(o * (pw * .5f - .08f) + Vector3.up * (h + 1.2f), o.z != 0 ? new Vector3(pw, .12f, .14f) : new Vector3(.14f, .12f, pw), wo, p.Top(wo));
                for (int k = 0; k < 2; k++) b.Box(o * (pw * .5f - .05f) + Vector3.up * (h + .45f + k * .35f), o.z != 0 ? new Vector3(pw - .1f, .2f, .06f) : new Vector3(.06f, .2f, pw - .1f), wo * .95f, wo * .95f);
            }
            for (int sx = -1; sx <= 1; sx += 2) for (int sz = -1; sz <= 1; sz += 2)
                b.Box(new Vector3(sx * (s + .4f), h + 1.6f, sz * (s + .4f)), new Vector3(.18f, 3f, .18f), wd, wd);
            Color roof = p.IsDry ? p.Canvas : b.Pick(p.Roofs);
            b.Hip(new Vector3(0, h + 3.05f, 0), 2 * s + 1.2f, 2 * s + 1.2f, 1.8f, .35f, roof);
            // bell + flag
            b.Cyl(new Vector3(0, h + 2.3f, 0), .32f, .18f, .5f, 8, Gold * .85f, Gold * .85f);
            b.Box(new Vector3(0, h + 2.85f, 0), new Vector3(.06f, .3f, .06f), Iron, Iron);
            b.Box(new Vector3(0, h + 5.5f, 0), new Vector3(.08f, 1.6f, .08f), wd, wd);
            b.Box(new Vector3(.45f, h + 5.95f, 0), new Vector3(.9f, .5f, .04f), p.Accents[0], p.Accents[0]);
            // ladder
            for (int sx = -1; sx <= 1; sx += 2) b.Box(new Vector3(-s + .4f + sx * .32f, h * .5f, -sb - .35f + .25f), new Vector3(.08f, h + .4f, .08f), Quaternion.Euler(-4f, 0, 0), wo, wo);
            for (int i = 0; i < 12; i++) b.Box(new Vector3(-s + .4f, .4f + i * h / 12f, -sb - .35f + .25f + (i * h / 12f - h * .5f) * .07f), new Vector3(.6f, .07f, .07f), wo, wo);
        }

        // =============================================================== tents
        static void TentHD(BB b, int v)
        {
            var p = b.P; Color c = p.Canvas;
            if (v == 0)
            {
                float len = 4f, wdt = 3f, rise = 2.4f;
                b.GableZ(Vector3.zero, len, wdt, rise, 0f, c, c * .92f, .08f);
                foreach (int s in new[] { -1, 1 }) b.Box(new Vector3(0, rise * .5f + .1f, s * (len * .5f + .05f)), new Vector3(.08f, rise + .4f, .08f), p.WoodDark, p.WoodDark);
                b.Box(new Vector3(0, rise + .02f, 0), new Vector3(.07f, .07f, len + .5f), p.WoodDark, p.WoodDark);
                Vector3 f = new Vector3(0, 0, -len * .5f - .03f);
                b.Tri(f + new Vector3(-.55f, 0, 0), f + new Vector3(.55f, 0, 0), f + new Vector3(0, 1.7f, 0), H("#3A3330"), Vector3.zero);
                b.Box(f + new Vector3(-.7f, .7f, -.05f), new Vector3(.25f, 1.5f, .25f), Quaternion.Euler(0, 0, -12f), c * .88f, c * .88f);   // rolled flap
                for (int s = -1; s <= 1; s += 2) for (int e = -1; e <= 1; e += 2)
                {
                    Vector3 a = new Vector3(s * wdt * .25f, rise * .5f, e * len * .35f), peg = new Vector3(s * (wdt * .5f + 1f), 0f, e * len * .35f);
                    b.Box((a + peg) * .5f, new Vector3(.03f, .03f, (peg - a).magnitude), Quaternion.LookRotation(peg - a), Rope, Rope);
                    b.Box(peg + Vector3.up * .1f, new Vector3(.08f, .25f, .08f), p.WoodDark, p.WoodDark);
                }
                b.Box(new Vector3(0, rise + .45f, len * .5f), new Vector3(.04f, .5f, .04f), p.WoodDark, p.WoodDark);
                b.Box(new Vector3(.22f, rise + .6f, len * .5f), new Vector3(.44f, .24f, .03f), p.Accents[1 % p.Accents.Length], p.Accents[1 % p.Accents.Length]);
                return;
            }
            Color a1 = p.Accents[v == 1 ? 0 : 1];
            int n = 14; float r = 2.3f, wh = 1.9f, er = 2.55f, ry = 1.8f, peak = 4.1f;
            for (int i = 0; i < n; i++)
            {
                float t0 = i * Mathf.PI * 2f / n, t1 = (i + 1) * Mathf.PI * 2f / n;
                Vector3 d0 = new Vector3(Mathf.Cos(t0), 0, Mathf.Sin(t0)), d1 = new Vector3(Mathf.Cos(t1), 0, Mathf.Sin(t1));
                Color col = i % 2 == 0 ? a1 : c;
                bool door = i == n * 3 / 4;   // −Z side
                if (!door) b.Quad(d0 * r + Vector3.up * -.1f, d1 * r + Vector3.up * -.1f, d1 * r + Vector3.up * wh, d0 * r + Vector3.up * wh, c, Vector3.up);
                else b.Quad(d0 * r + Vector3.up * -.1f, d1 * r + Vector3.up * -.1f, d1 * r + Vector3.up * wh, d0 * r + Vector3.up * wh, H("#3A3330"), Vector3.up);
                Vector3 p0 = d0 * er + Vector3.up * ry, p1 = d1 * er + Vector3.up * ry;
                b.Tri(p0, p1, new Vector3(0, peak, 0), p.Top(col), new Vector3(0, ry, 0));
                b.Tri(p0, p1, new Vector3(0, ry, 0), col * .6f, new Vector3(0, peak, 0));
                // scalloped valance
                Vector3 m = (d0 + d1).normalized * er * Mathf.Cos(Mathf.PI / n) + Vector3.up * (ry - .35f);
                b.Tri(p0 + Vector3.down * .02f, p1 + Vector3.down * .02f, m, col, Vector3.up * ry);
                // guy ropes
                if (i % 2 == 0)
                {
                    Vector3 ga = d0 * er + Vector3.up * ry, gp = d0 * (er + 1.4f);
                    b.Box((ga + gp) * .5f, new Vector3(.03f, .03f, (gp - ga).magnitude), Quaternion.LookRotation(gp - ga), Rope, Rope);
                    b.Box(gp + Vector3.up * .1f, new Vector3(.08f, .25f, .08f), p.WoodDark, p.WoodDark);
                }
            }
            b.Cyl(new Vector3(0, peak - .05f, 0), .12f, .12f, .25f, 6, Gold, Gold);
            b.Box(new Vector3(0, peak + .7f, 0), new Vector3(.07f, 1.2f, .07f), p.WoodDark, p.WoodDark);
            b.Box(new Vector3(.4f, peak + 1.05f, 0), new Vector3(.8f, .45f, .04f), a1, a1);
            for (int s = -1; s <= 1; s += 2) b.Box(new Vector3(s * .7f, .9f, -r - .05f), new Vector3(.22f, 1.9f, .22f), Quaternion.Euler(0, 0, s * 8f), a1, a1);   // tied flaps
        }

        // =============================================================== ruins
        static void RuinHouseHD(BB b, int v)
        {
            var p = b.P; Vector2 dim = RuinDims[v]; float w = dim.x, d = dim.y, t = .55f;
            Color stone = p.IsDry ? b.Pick(p.Walls) * .92f : p.Stone, stone2 = p.IsDry ? stone * .9f : p.StoneDark;
            b.Box(new Vector3(0, (Plinth - Foundation) * .5f, 0), new Vector3(w + .3f, Foundation + Plinth, d + .3f), Quaternion.identity, p.StoneDark, p.Top(p.StoneDark));
            float hMax = v == 1 ? 5.5f : 4.5f;
            int nf = Mathf.RoundToInt(w / .9f), ns = Mathf.RoundToInt(d / .9f);
            // front: door hole + window hole
            float doorU = w * .35f, winU = w * .72f;
            BlockWall(b, new Vector3(-w * .5f, Plinth, -d * .5f), new Vector3(w * .5f, Plinth, -d * .5f), t, Profile(b, nf, hMax, .5f, 1.3f, hMax),
                (u, y) => (Mathf.Abs(u - doorU) < .6f && y < 2.2f) || (Mathf.Abs(u - winU) < .45f && y > 1.4f && y < 2.6f), stone, stone2);
            // right gable wall stays tall with a window
            var gh = new float[ns]; for (int i = 0; i < ns; i++) { float f = 1f - Mathf.Abs((i + .5f) / ns - .5f) * 2f; gh[i] = Mathf.Round((hMax + 1.6f * f - (i == ns - 1 ? 2f : 0f)) / .5f) * .5f; }
            BlockWall(b, new Vector3(w * .5f - t * .5f, Plinth, -d * .5f + t * .5f), new Vector3(w * .5f - t * .5f, Plinth, d * .5f - t * .5f), t, gh,
                (u, y) => Mathf.Abs(u - d * .5f) < .45f && y > 3f && y < 4.1f, stone, stone2);
            BlockWall(b, new Vector3(w * .5f, Plinth, d * .5f), new Vector3(-w * .5f, Plinth, d * .5f), t, Profile(b, nf, hMax * .8f, .5f, 1.6f, hMax * .6f), null, stone, stone2);
            BlockWall(b, new Vector3(-w * .5f + t * .5f, Plinth, d * .5f - t * .5f), new Vector3(-w * .5f + t * .5f, Plinth, -d * .5f + t * .5f), t, Profile(b, ns, hMax * .6f, .5f, 1.5f, 1.5f), null, stone, stone2);
            // wooden lintel remains over the door, charred beams, broken roof fragment, chimney stub
            b.Box(new Vector3(-w * .5f + doorU, Plinth + 2.35f, -d * .5f), new Vector3(1.6f, .25f, t + .1f), Charred, p.Top(Charred));
            for (int i = 0; i < 3; i++)
                b.Box(new Vector3(b.Rand(-w * .25f, w * .25f), .9f + i * .3f, b.Rand(-d * .25f, d * .25f)), new Vector3(.26f, .26f, d * .9f),
                    Quaternion.Euler(b.Rand(10, 30), b.Rand(-40, 40), b.Rand(-10, 10)), Charred, p.Top(Charred));
            Vector3 rc = new Vector3(w * .5f - 1.2f, Plinth + hMax - .2f, d * .1f);
            var rq = Quaternion.Euler(0, 0, 28f);
            b.Box(rc, new Vector3(2.6f, .22f, 2.4f), rq, p.Roofs[0] * .85f, p.Top(p.Roofs[0] * .85f), true);
            for (int i = 0; i < 3; i++) b.Box(rc + rq * new Vector3(-1f + i * .8f, .16f, 0), new Vector3(.12f, .1f, 2.4f), rq, p.Roofs[0] * .7f, p.Top(p.Roofs[0] * .7f));
            b.BoxB(-w * .5f + .9f, Plinth, d * .5f - .9f, 1f, hMax * .7f, 1f, stone2, p.Top(stone2));
            for (int i = 0; i < 9; i++)
                b.Box(new Vector3(b.Rand(-w * .35f, w * .35f), b.Rand(.1f, .4f), b.Rand(-d * .35f, d * .35f)), new Vector3(b.Rand(.4f, .9f), b.Rand(.35f, .55f), b.Rand(.4f, .8f)),
                    Quaternion.Euler(b.Rand(-20, 20), b.Rand(0, 360), b.Rand(-20, 20)), b.Chance(.5f) ? p.Rubble : p.StoneDark, p.Top(p.Rubble));
            for (int i = 0; i < 6; i++) Tuft(b, new Vector3(b.Rand(-w * .45f, w * .45f), Plinth, b.Rand(-d * .45f, d * .45f)), b.Rand(.8f, 1.3f));
            for (int i = 0; i < 4; i++) b.Box(new Vector3(w * .5f + .04f, Plinth + b.Rand(.5f, 3f), b.Rand(-d * .4f, d * .4f)), new Vector3(.12f, b.Rand(.8f, 1.6f), b.Rand(.6f, 1.2f)), Moss, Moss);   // ivy
        }

        static void RuinTowerHD(BB b, int v)
        {
            var p = b.P; float r = v == 0 ? 3.4f : 2.8f, h = v == 0 ? 9f : 7f; int n = 14; float course = .5f, t = .8f;
            Color st = p.Stone, st2 = p.StoneDark;
            b.Cyl(new Vector3(0, -Foundation, 0), r + .5f, r + .3f, Foundation + .6f, n, st2, p.Top(st2));
            var colH = Profile(b, n, h, 1f, 2f, h);
            colH[7] = 1f; colH[8] = Mathf.Min(colH[8], 2f); colH[6] = Mathf.Min(colH[6], 3f);   // breach
            float seg = Mathf.PI * 2f / n;
            for (int k = 0; k * course < h; k++)
            {
                float y0 = .6f + k * course, off = (k % 2) * .5f;
                for (int i = 0; i < n; i++)
                {
                    float am = (i + .5f + off) * seg; int col = (int)(((i + off) % n + n) % n);
                    if (y0 + course - .6f > colH[col] + .01f) continue;
                    if (i % 4 == 1 && k >= 5 && k <= 7) continue;   // slit holes
                    var o = new Vector3(Mathf.Cos(am), 0, Mathf.Sin(am));
                    Color c = b.Chance(.3f) ? st2 : st * b.Rand(.95f, 1.03f);
                    bool top = y0 + 2f * course - .6f > colH[col] + .01f;
                    b.Box(o * (r + b.Rand(-.03f, .03f)) + Vector3.up * (y0 + course * .5f), new Vector3(2f * (r + t * .5f) * Mathf.Sin(seg * .5f) + .02f, course - .015f, t), Quaternion.LookRotation(o), c, top ? p.Top(c) : c);
                }
            }
            for (int i = 0; i < 10; i++)
            {
                float a = 7.5f * seg + b.Rand(-.4f, .4f), rr = r + b.Rand(.4f, 2.8f);
                b.Box(new Vector3(Mathf.Cos(a) * rr, .25f, Mathf.Sin(a) * rr), new Vector3(b.Rand(.5f, 1f), b.Rand(.4f, .6f), b.Rand(.5f, .8f)), Quaternion.Euler(b.Rand(-25, 25), b.Rand(0, 360), b.Rand(-25, 25)), b.Chance(.5f) ? p.Rubble : st2, p.Top(p.Rubble));
            }
            for (int i = 0; i < 5; i++)
            {
                float a = b.Rand(0f, Mathf.PI * 2f); var o = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                b.Box(o * (r + t * .5f + .04f) + Vector3.up * b.Rand(1f, 4f), new Vector3(b.Rand(.6f, 1.2f), b.Rand(.8f, 1.8f), .1f), Quaternion.LookRotation(o), Moss, Moss);
            }
            for (int i = 0; i < 5; i++) { float a = b.Rand(0f, Mathf.PI * 2f), rr = b.Rand(0f, r * .7f); Tuft(b, new Vector3(Mathf.Cos(a) * rr, .6f, Mathf.Sin(a) * rr), 1.2f); }
            b.Box(new Vector3(0, 1.1f, 0), new Vector3(.3f, .3f, r * 1.8f), Quaternion.Euler(25f, 30f, 0), Charred, p.Top(Charred));
        }

        static void RuinWallHD(BB b, int v)
        {
            var p = b.P; float L = 8.6f, hMax = v == 0 ? 5.5f : 4f;
            int nc = 10;
            var colH = Profile(b, nc, hMax, .5f, 1.2f, hMax);
            float au = L * .5f, ar = 1.2f, spring = 2.2f;
            if (v == 0) for (int i = 3; i <= 6; i++) colH[i] = Mathf.Max(colH[i], spring + ar + 1f);
            BlockWall(b, new Vector3(-L * .5f, 0, 0), new Vector3(L * .5f, 0, 0), 1.4f, colH,
                v == 0 ? (Func<float, float, bool>)((u, y) => Mathf.Abs(u - au) < ar && (y < spring || (new Vector2(u - au, y - spring)).magnitude < ar)) : null, p.Stone, p.StoneDark);
            if (v == 0) Ring(b, new Vector3(0, spring, -.7f), Front, ar, .3f, .2f, 7, 0f, Mathf.PI, p.StoneDark, p.StoneDark * 1.1f);
            for (int i = 0; i < 8; i++)
                b.Box(new Vector3(b.Rand(-4f, 4f), .25f, b.Rand(-2.2f, -1f) * (b.Chance(.5f) ? 1 : -1)), new Vector3(b.Rand(.5f, 1f), b.Rand(.4f, .6f), b.Rand(.5f, .8f)),
                    Quaternion.Euler(b.Rand(-25, 25), b.Rand(0, 360), b.Rand(-25, 25)), p.Rubble, p.Top(p.Rubble));
            for (int i = 0; i < 5; i++) Tuft(b, new Vector3(b.Rand(-4f, 4f), 0f, b.Rand(-1.6f, 1.6f)), 1.2f);
            for (int i = 0; i < 3; i++) b.Box(new Vector3(b.Rand(-3.5f, 3.5f), b.Rand(.8f, 2.5f), -.74f), new Vector3(b.Rand(.6f, 1.2f), b.Rand(.8f, 1.6f), .1f), Moss, Moss);
        }
    }
}
