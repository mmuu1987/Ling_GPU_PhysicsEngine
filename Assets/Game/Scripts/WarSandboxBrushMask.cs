using System;
using System.Collections.Generic;
using UnityEngine;

namespace MassEngine.Game
{
    /// <summary>Brush deployment (2026-10-15): the painted footprint of one composition. Cells are relative to the
    /// entry centre, which is the middle of the cell rectangle, so moving the centre moves the whole shape.
    /// Immutable: every edit builds a new mask, so draft snapshots / Undo keep working as plain values.</summary>
    public sealed class WarSandboxBrushMask
    {
        public const float DefaultCell = 2f;
        /// <summary>Largest side in cells (1024 m at the default cell).</summary>
        public const int MaxSide = 512;
        public readonly float cell;
        public readonly int width, height; // cells along world X / world Z
        public readonly int painted;
        private readonly bool[] bits;
        private readonly Dictionary<float, Vector2[]> lattice = new Dictionary<float, Vector2[]>();
        private readonly Dictionary<long, Vector2[]> ordered = new Dictionary<long, Vector2[]>();

        public WarSandboxBrushMask(float cell, int width, int height, bool[] bits)
        {
            if (!(cell > 0f) || float.IsInfinity(cell) || width < 1 || height < 1 || width > MaxSide || height > MaxSide || bits == null || bits.Length != width * height)
                throw new ArgumentException("invalid brush mask");
            this.cell = cell; this.width = width; this.height = height; this.bits = (bool[])bits.Clone();
            foreach (bool b in this.bits) if (b) painted++;
        }

        public bool this[int i, int j] => i >= 0 && j >= 0 && i < width && j < height && bits[j * width + i];
        public Vector2 Size => new Vector2(width * cell, height * cell);
        public float Area => painted * cell * cell;
        public Vector2 CellCenter(int i, int j) => new Vector2((i + .5f - width * .5f) * cell, (j + .5f - height * .5f) * cell);
        public bool ContainsLocal(float x, float z) => this[Mathf.FloorToInt(x / cell + width * .5f), Mathf.FloorToInt(z / cell + height * .5f)];

        /// <summary>Lattice spacing: the composition's density (0.05-1.5 /m2) but never closer than one body (2r).</summary>
        public static float Spacing(float density, float radius)
        {
            float d = float.IsNaN(density) ? .5f : Mathf.Clamp(density, .05f, SpawnConfig.PackingLimitPerSquareMeter);
            return Mathf.Max(1f / Mathf.Sqrt(d), 2f * Mathf.Max(.05f, radius));
        }

        /// <summary>Square lattice of the given spacing, centred in the cell rectangle, keeping points inside painted cells.</summary>
        public Vector2[] Lattice(float spacing)
        {
            spacing = Mathf.Max(.5f, spacing);
            if (lattice.TryGetValue(spacing, out var cached)) return cached;
            if (lattice.Count > 6) lattice.Clear();
            float w = width * cell, h = height * cell;
            int nx = Mathf.Max(1, Mathf.FloorToInt(w / spacing)), nz = Mathf.Max(1, Mathf.FloorToInt(h / spacing));
            float ox = -(nx - 1) * spacing * .5f, oz = -(nz - 1) * spacing * .5f;
            var list = new List<Vector2>();
            for (int b = 0; b < nz; b++)
                for (int a = 0; a < nx; a++)
                {
                    float x = ox + a * spacing, z = oz + b * spacing;
                    if (ContainsLocal(x, z)) list.Add(new Vector2(x, z));
                }
            var result = list.ToArray(); lattice[spacing] = result; return result;
        }

        public int Capacity(float spacing) => Lattice(spacing).Length;

        /// <summary>Lattice points front first (yaw: 0 = +Z, 90 = +X), ties broken left to right.</summary>
        public Vector2[] FrontFirst(float spacing, float yawDegrees)
        {
            long key = ((long)BitConverter.SingleToInt32Bits(Mathf.Max(.5f, spacing)) << 32) ^ (uint)BitConverter.SingleToInt32Bits(yawDegrees);
            if (ordered.TryGetValue(key, out var cached)) return cached;
            if (ordered.Count > 6) ordered.Clear();
            var points = Lattice(spacing);
            float rad = yawDegrees * Mathf.Deg2Rad; var f = new Vector2(Mathf.Sin(rad), Mathf.Cos(rad)); var side = new Vector2(f.y, -f.x);
            var keys = new double[points.Length]; var result = (Vector2[])points.Clone();
            for (int k = 0; k < points.Length; k++)
                keys[k] = -Math.Round(Vector2.Dot(points[k], f) / Mathf.Max(.5f, spacing) * 4.0) * 1e7 + Vector2.Dot(points[k], side);
            Array.Sort(keys, result);
            ordered[key] = result; return result;
        }

        /// <summary>Spawn offsets (relative to the entry centre) for count members, front first, with a small deterministic
        /// jitter that never brings two members closer than 2r. Null when the shape cannot hold count members.</summary>
        public Vector2[] Offsets(int count, float yawDegrees, float spacing, float radius, int seed)
        {
            var points = FrontFirst(spacing, yawDegrees);
            if (count < 1 || points.Length < count) return null;
            float jitter = Mathf.Max(0f, Mathf.Min(.2f * spacing, (spacing - 2f * Mathf.Max(.05f, radius)) * .5f));
            var result = new Vector2[count];
            for (int k = 0; k < count; k++)
            {
                uint h = (uint)k * 0x9e3779b9u ^ (uint)seed * 0x85ebca6bu;
                result[k] = points[k] + new Vector2(Signed(h ^ 0x68bc21ebu), Signed(h ^ 0x02e5be93u)) * jitter * .5f;
            }
            return result;
        }

        private static float Signed(uint v)
        {
            v ^= v >> 16; v *= 0x7feb352du; v ^= v >> 15; v *= 0x846ca68bu; v ^= v >> 16;
            return (v & 0x00ffffffu) / 16777216f * 2f - 1f;
        }

        public bool[] CopyBits() => (bool[])bits.Clone();

        /// <summary>Quarter turns clockwise seen from above (+Z up, +X right): (x, z) -> (z, -x).</summary>
        public WarSandboxBrushMask Rotate(int quarters)
        {
            int q = ((quarters % 4) + 4) % 4; var m = this;
            for (int t = 0; t < q; t++)
            {
                var next = new bool[m.width * m.height]; int nw = m.height, nh = m.width;
                for (int j = 0; j < m.height; j++)
                    for (int i = 0; i < m.width; i++)
                        if (m.bits[j * m.width + i]) next[(m.width - 1 - i) * nw + j] = true;
                m = new WarSandboxBrushMask(m.cell, nw, nh, next);
            }
            return m;
        }

        // ---- plan files (phase 2): row-major bits, LSB first, base64 ----
        public string ToBase64()
        {
            var bytes = new byte[(bits.Length + 7) / 8];
            for (int k = 0; k < bits.Length; k++) if (bits[k]) bytes[k >> 3] |= (byte)(1 << (k & 7));
            return Convert.ToBase64String(bytes);
        }

        public static bool TryFromBase64(float cell, int width, int height, string data, out WarSandboxBrushMask mask, out string error)
        {
            mask = null; error = null;
            if (!(cell >= .5f && cell <= 16f) || width < 1 || height < 1 || width > MaxSide || height > MaxSide || string.IsNullOrEmpty(data))
            { error = "方案中的画笔形状尺寸无效。"; return false; }
            byte[] bytes;
            try { bytes = Convert.FromBase64String(data); } catch (FormatException) { error = "方案中的画笔形状数据损坏。"; return false; }
            if (bytes.Length != (width * height + 7) / 8) { error = "方案中的画笔形状数据长度不符。"; return false; }
            var bits = new bool[width * height]; bool any = false;
            for (int k = 0; k < bits.Length; k++) any |= bits[k] = (bytes[k >> 3] & (1 << (k & 7))) != 0;
            if (!any) { error = "方案中的画笔形状为空。"; return false; }
            mask = new WarSandboxBrushMask(cell, width, height, bits); return true;
        }
    }

    /// <summary>Footprint tests that understand brush masks: painted shapes (own or hostile) may interlock cell by cell,
    /// e.g. a ring around an enemy block. Two rectangles keep the old rectangle test.</summary>
    public static class WarSandboxBrushFootprint
    {
        public static bool Overlaps(Rect a, Rect b) => Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin) > 0.01f &&
            Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin) > 0.01f;

        public static Rect CellRect(WarSandboxDeploymentEntry e, int i, int j)
        {
            var c = e.brush.CellCenter(i, j); float h = e.brush.cell * .5f;
            return new Rect(e.center.x + c.x - h, e.center.z + c.y - h, e.brush.cell, e.brush.cell);
        }

        /// <summary>True when any painted cell (or the rectangle) of e overlaps r.</summary>
        public static bool Covers(WarSandboxDeploymentEntry e, Rect r)
        {
            if (!Overlaps(e.Bounds, r)) return false;
            if (e.brush == null) return true;
            var m = e.brush;
            for (int j = 0; j < m.height; j++)
                for (int i = 0; i < m.width; i++)
                    if (m[i, j] && Overlaps(CellRect(e, i, j), r)) return true;
            return false;
        }

        public static bool Overlaps(WarSandboxDeploymentEntry a, WarSandboxDeploymentEntry b)
        {
            if (!Overlaps(a.Bounds, b.Bounds)) return false;
            if (a.brush == null && b.brush == null) return true;
            if (a.brush == null || (b.brush != null && b.brush.painted < a.brush.painted)) { var t = a; a = b; b = t; }
            var m = a.brush;
            for (int j = 0; j < m.height; j++)
                for (int i = 0; i < m.width; i++)
                {
                    if (!m[i, j]) continue;
                    if (b.brush == null) { if (Overlaps(CellRect(a, i, j), b.Bounds)) return true; continue; }
                    var c = m.CellCenter(i, j);
                    if (b.brush.ContainsLocal(a.center.x + c.x - b.center.x, a.center.z + c.y - b.center.z)) return true;
                }
            return false;
        }

        /// <summary>Count-lock for brush shapes: the lattice at the body-limited spacing must hold every member.</summary>
        public static bool Fits(WarSandboxDeploymentEntry e, float radius, out string error)
        {
            error = null;
            if (e.brush == null) return true;
            int capacity = e.brush.Capacity(WarSandboxBrushMask.Spacing(e.density, radius));
            if (capacity >= e.count) return true;
            error = "画笔区域只能放下 " + capacity + " 人，还差 " + (e.count - capacity) + " 人：请多涂一些、提高密度或减少人数。";
            return false;
        }
    }
}
