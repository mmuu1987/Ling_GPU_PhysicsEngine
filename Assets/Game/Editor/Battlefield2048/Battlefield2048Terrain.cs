using System;
using System.Collections.Generic;
using UnityEngine;

namespace MassEngine.Game.Editor
{
    /// <summary>Value noise helpers with an explicit seed, so a template can retry with a sub-seed (2048 maps, 2026-10-14).</summary>
    public sealed class Noise2048
    {
        readonly float ox, oz;
        public Noise2048(int seed)
        {
            var r = new System.Random(seed);
            ox = 1000f + (float)r.NextDouble() * 8000f; oz = 1000f + (float)r.NextDouble() * 8000f;
        }
        /// <summary>Perlin 0..1.</summary>
        public float P(float x, float z, float f, float o = 0f) => Mathf.PerlinNoise(x * f + ox + o, z * f + oz + o * 1.7f);
        public float Fbm(float x, float z, float f, int octaves, float o = 0f)
        {
            float sum = 0, amp = 1, norm = 0;
            for (int i = 0; i < octaves; i++) { sum += P(x, z, f, o + i * 31.7f) * amp; norm += amp; amp *= .5f; f *= 2.03f; }
            return sum / norm;
        }
        /// <summary>Ridged multifractal 0..1 (sharp crests).</summary>
        public float Ridged(float x, float z, float f, int octaves, float o = 0f)
        {
            float sum = 0, amp = 1, norm = 0;
            for (int i = 0; i < octaves; i++)
            {
                float n = 1f - Mathf.Abs(2f * P(x, z, f, o + 57.1f + i * 19.3f) - 1f);
                sum += n * n * amp; norm += amp; amp *= .5f; f *= 2.03f;
            }
            return sum / norm;
        }
        /// <summary>fBm sampled at a domain-warped position.</summary>
        public float FbmWarp(float x, float z, float f, int octaves, float warpFrequency, float warp, float o = 0f)
        {
            float wx = x + warp * (Fbm(x, z, warpFrequency, 3, o + 101f) - .5f) * 2f;
            float wz = z + warp * (Fbm(x, z, warpFrequency, 3, o + 203f) - .5f) * 2f;
            return Fbm(wx, wz, f, octaves, o);
        }
    }

    /// <summary>
    /// Height + obstacle masks on the 2048 gameplay vertex grid (513², 4 m, origin −1024) with the shaping operators of the
    /// approved plan: blur, thermal and hydraulic (droplet) erosion, terraces, corridor grading/carving, slope clamping and
    /// 180° point symmetry. Editor-only; the generator samples it at exactly these vertices.
    /// </summary>
    public sealed class Field2048
    {
        public const int N = 513, Cells = N - 1;
        public const float Step = 4f, Half = 1024f;
        public readonly float[] H = new float[N * N], Forest = new float[N * N], Rock = new float[N * N];
        public readonly float[] Open = new float[N * N], Trail = new float[N * N], Lock = new float[N * N];

        public static float X(int i) => -Half + i * Step;

        public static float Sample(float[] g, float x, float z)
        {
            float fx = Mathf.Clamp((x + Half) / Step, 0f, N - 1.0001f), fz = Mathf.Clamp((z + Half) / Step, 0f, N - 1.0001f);
            int ix = (int)fx, iz = (int)fz; float tx = fx - ix, tz = fz - iz; int i = iz * N + ix;
            return Mathf.Lerp(Mathf.Lerp(g[i], g[i + 1], tx), Mathf.Lerp(g[i + N], g[i + N + 1], tx), tz);
        }

        public void Apply(float[] g, Func<float, float, float, float> f)
        {
            for (int z = 0; z < N; z++)
            {
                float wz = X(z);
                for (int x = 0; x < N; x++) { int i = z * N + x; g[i] = f(X(x), wz, g[i]); }
            }
        }

        /// <summary>Separable box blur, repeated (3 passes ≈ Gaussian). Radius in vertices.</summary>
        public static float[] Blur(float[] src, int radius, int passes)
        {
            var a = (float[])src.Clone(); var b = new float[a.Length];
            for (int p = 0; p < passes; p++)
            {
                BoxLine(a, b, radius, true); BoxLine(b, a, radius, false);
            }
            return a;
        }

        static void BoxLine(float[] src, float[] dst, int r, bool horizontal)
        {
            float inv = 1f / (2 * r + 1);
            for (int line = 0; line < N; line++)
            {
                float sum = 0;
                for (int k = -r; k <= r; k++) sum += src[Index(line, Mathf.Clamp(k, 0, N - 1), horizontal)];
                for (int k = 0; k < N; k++)
                {
                    dst[Index(line, k, horizontal)] = sum * inv;
                    sum += src[Index(line, Mathf.Min(k + r + 1, N - 1), horizontal)] - src[Index(line, Mathf.Max(k - r, 0), horizontal)];
                }
            }
        }

        static int Index(int line, int k, bool horizontal) => horizontal ? line * N + k : k * N + line;

        /// <summary>Thermal erosion: material slides off slopes steeper than the talus angle (Lock &gt; .5 untouched).</summary>
        public void Thermal(int iterations, float talusDegrees, float rate)
        {
            float talus = Mathf.Tan(talusDegrees * Mathf.Deg2Rad) * Step;
            int[] off = { 1, -1, N, -N };
            for (int it = 0; it < iterations; it++)
                for (int z = 1; z < N - 1; z++)
                    for (int x = 1; x < N - 1; x++)
                    {
                        int i = z * N + x; if (Lock[i] > .5f) continue;
                        float best = 0; int bn = -1;
                        foreach (int o in off) { float d = H[i] - H[i + o]; if (d > best) { best = d; bn = i + o; } }
                        if (bn < 0 || best <= talus || Lock[bn] > .5f) continue;
                        float m = rate * (best - talus) * .5f; H[i] -= m; H[bn] += m;
                    }
        }

        /// <summary>Droplet hydraulic erosion (simplified Beyer / Lague model) — carves gullies into slopes, deposits in hollows.</summary>
        public void Hydraulic(int seed, int droplets, float strength = 1f)
        {
            var rng = new System.Random(seed);
            const float inertia = .08f, capacity = 3f, minCapacity = .02f, gravity = 4f, evaporate = .025f;
            float erode = .05f * strength, deposit = .1f;
            for (int k = 0; k < droplets; k++)
            {
                float px = 1f + (float)rng.NextDouble() * (N - 3), pz = 1f + (float)rng.NextDouble() * (N - 3);
                float dx = 0, dz = 0, speed = 1f, water = 1f, sediment = 0f;
                for (int life = 0; life < 40; life++)
                {
                    int ix = (int)px, iz = (int)pz; float fx = px - ix, fz = pz - iz; int i = iz * N + ix;
                    float h00 = H[i], h10 = H[i + 1], h01 = H[i + N], h11 = H[i + N + 1];
                    float gx = (h10 - h00) * (1 - fz) + (h11 - h01) * fz, gz = (h01 - h00) * (1 - fx) + (h11 - h10) * fx;
                    float h = h00 * (1 - fx) * (1 - fz) + h10 * fx * (1 - fz) + h01 * (1 - fx) * fz + h11 * fx * fz;
                    dx = dx * inertia - gx * (1 - inertia); dz = dz * inertia - gz * (1 - inertia);
                    float len = Mathf.Sqrt(dx * dx + dz * dz);
                    if (len < 1e-6f) { float a = (float)rng.NextDouble() * Mathf.PI * 2f; dx = Mathf.Cos(a); dz = Mathf.Sin(a); } else { dx /= len; dz /= len; }
                    px += dx; pz += dz;
                    if (px < 1 || pz < 1 || px >= N - 2 || pz >= N - 2) break;
                    float nh = Bilinear(px, pz), dh = nh - h;
                    float cap = Mathf.Max(-dh * speed * water * capacity, minCapacity);
                    if (sediment > cap || dh > 0)
                    {
                        float amount = dh > 0 ? Mathf.Min(dh, sediment) : (sediment - cap) * deposit;
                        sediment -= amount; Spread(i, fx, fz, amount);
                    }
                    else
                    {
                        float amount = Mathf.Min(Mathf.Min((cap - sediment) * erode, -dh), .6f);
                        if (Lock[i] > .5f) amount = 0f;
                        Spread(i, fx, fz, -amount); sediment += amount;
                    }
                    speed = Mathf.Sqrt(Mathf.Max(0f, speed * speed - dh * gravity));
                    water *= 1f - evaporate;
                }
            }
        }

        float Bilinear(float px, float pz)
        {
            int ix = (int)px, iz = (int)pz; float fx = px - ix, fz = pz - iz; int i = iz * N + ix;
            return H[i] * (1 - fx) * (1 - fz) + H[i + 1] * fx * (1 - fz) + H[i + N] * (1 - fx) * fz + H[i + N + 1] * fx * fz;
        }

        void Spread(int i, float fx, float fz, float amount)
        {
            H[i] += amount * (1 - fx) * (1 - fz); H[i + 1] += amount * fx * (1 - fz);
            H[i + N] += amount * (1 - fx) * fz; H[i + N + 1] += amount * fx * fz;
        }

        /// <summary>Limits the slope between neighbouring vertices to maxDegrees where weight &gt; 0 (two-sided, weighted).</summary>
        public void SlopeClamp(float[] weight, float maxDegrees, int iterations)
        {
            float max = Mathf.Tan(maxDegrees * Mathf.Deg2Rad) * Step;
            int[] off = { 1, -1, N, -N };
            for (int it = 0; it < iterations; it++)
                for (int z = 1; z < N - 1; z++)
                    for (int x = 1; x < N - 1; x++)
                    {
                        int i = z * N + x; float w = weight[i]; if (w <= 0f) continue;
                        float lo = float.MinValue, hi = float.MaxValue;
                        foreach (int o in off) { lo = Mathf.Max(lo, H[i + o] - max); hi = Mathf.Min(hi, H[i + o] + max); }
                        float target = lo <= hi ? Mathf.Clamp(H[i], lo, hi) : (lo + hi) * .5f;
                        H[i] = Mathf.Lerp(H[i], target, w * .5f);
                    }
        }

        /// <summary>180° point symmetry: east half = west half rotated about the centre (fair for both teams); blended over |x| &lt; seam.</summary>
        public static void Symmetrize(float[] g, float seam)
        {
            var src = (float[])g.Clone();
            for (int z = 0; z < N; z++)
                for (int x = 0; x < N; x++)
                {
                    float wx = X(x); if (wx < -seam) continue;
                    int m = (N - 1 - z) * N + (N - 1 - x);
                    float w = wx >= seam ? 1f : Smooth(-seam, seam, wx);
                    g[z * N + x] = Mathf.Lerp(src[z * N + x], src[m], w);
                }
        }

        public static float Smooth(float a, float b, float v) { float t = Mathf.Clamp01((v - a) / (b - a)); return t * t * (3 - 2 * t); }

        /// <summary>Terrace with sharp risers: the rise of each step happens in the last <paramref name="riser"/> fraction.</summary>
        public static float Terrace(float h, float step, float riser)
        {
            float t = h / step, f = Mathf.Floor(t), r = t - f;
            float up = Smooth(1f - riser, 1f, r);
            return (f + up) * step + (r - up) * step * .12f;   // small residual tilt keeps plateaus from looking flat-shaded
        }

        // ------------------------------------------------------------------ walkability (same test as TerrainSurface)
        /// <summary>Cell walkable by slope (both triangles within the limit), not counting obstacles.</summary>
        public bool SlopeWalkable(int cx, int cz, float limitDegrees)
        {
            float lim = Mathf.Tan(limitDegrees * Mathf.Deg2Rad); lim *= lim;
            int i = cz * N + cx;
            float h00 = H[i], h10 = H[i + 1], h01 = H[i + N], h11 = H[i + N + 1];
            float ax = (h10 - h00) / Step, az = (h01 - h00) / Step, bx = (h11 - h01) / Step, bz = (h11 - h10) / Step;
            return ax * ax + az * az <= lim && bx * bx + bz * bz <= lim;
        }

        public float SlopeDegrees(int cx, int cz)
        {
            int i = cz * N + cx;
            float h00 = H[i], h10 = H[i + 1], h01 = H[i + N], h11 = H[i + N + 1];
            float gx = ((h10 - h00) + (h11 - h01)) * .5f / Step, gz = ((h01 - h00) + (h11 - h10)) * .5f / Step;
            return Mathf.Atan(Mathf.Sqrt(gx * gx + gz * gz)) * Mathf.Rad2Deg;
        }
    }
}
