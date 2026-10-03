using System;
using UnityEngine;

namespace MassEngine.Game
{
    /// <summary>CPU grid DDA against the exact a-b-d / a-d-c triangles used by TerrainSurface.
    /// No collider, height=0 plane, fixed sampling stride, or navigation projection is involved.</summary>
    public static class TerrainSurfaceQueries
    {
        public static bool Raycast(TerrainSurface surface, Ray ray, float maxDistance, out Vector3 point)
        {
            point = default;
            if (surface == null || !Finite(ray.origin) || !Finite(ray.direction) ||
                !Finite(maxDistance) || maxDistance < 0 || ray.direction.sqrMagnitude < 1e-12f) return false;
            ray = new Ray(ray.origin, ray.direction.normalized);
            double enter = 0, exit = maxDistance;
            if (!Clip(ray.origin.x, ray.direction.x, surface.Origin.x, surface.Origin.x + surface.Size.x, ref enter, ref exit) ||
                !Clip(ray.origin.z, ray.direction.z, surface.Origin.y, surface.Origin.y + surface.Size.y, ref enter, ref exit)) return false;
            Vector3 first = ray.GetPoint((float)enter);
            int x = Mathf.Clamp(Mathf.FloorToInt((first.x - surface.Origin.x) / surface.Step.x), 0, surface.Width - 2);
            int z = Mathf.Clamp(Mathf.FloorToInt((first.z - surface.Origin.y) / surface.Step.y), 0, surface.Depth - 2);
            int sx = Math.Sign(ray.direction.x), sz = Math.Sign(ray.direction.z);
            double nextX = NextCrossing(surface.Origin.x, surface.Step.x, x, sx, ray.origin.x, ray.direction.x);
            double nextZ = NextCrossing(surface.Origin.y, surface.Step.y, z, sz, ray.origin.z, ray.direction.z);
            double deltaX = sx == 0 ? double.PositiveInfinity : surface.Step.x / Math.Abs((double)ray.direction.x);
            double deltaZ = sz == 0 ? double.PositiveInfinity : surface.Step.y / Math.Abs((double)ray.direction.z);
            // At most one visit per crossed row/column, including vertical rays and edge ties.
            for (int visits = 0; visits < surface.Width + surface.Depth; visits++)
            {
                double end = Math.Min(exit, Math.Min(nextX, nextZ));
                var a = Vertex(surface, x, z); var b = Vertex(surface, x + 1, z);
                var c = Vertex(surface, x, z + 1); var d = Vertex(surface, x + 1, z + 1);
                float t1 = Triangle(ray, a, b, d), t2 = Triangle(ray, a, d, c);
                float t = Mathf.Min(InSegment(t1, enter, end), InSegment(t2, enter, end));
                if (!float.IsPositiveInfinity(t))
                {
                    var hit = ray.GetPoint(Mathf.Max(0, t));
                    if (surface.TrySample(new Vector2(hit.x, hit.z), out var sample))
                    { point = sample.Position; return true; }
                }
                if (end >= exit) return false;
                bool crossX = nextX <= nextZ, crossZ = nextZ <= nextX;
                enter = end;
                if (crossX) { x += sx; nextX += deltaX; }
                if (crossZ) { z += sz; nextZ += deltaZ; }
                if (x < 0 || z < 0 || x >= surface.Width - 1 || z >= surface.Depth - 1) return false;
            }
            return false;
        }

        private static float InSegment(float t, double start, double end) =>
            t >= Math.Max(0, start - 1e-4) && t <= end + 1e-4 ? t : float.PositiveInfinity;
        private static Vector3 Vertex(TerrainSurface s, int x, int z) =>
            new Vector3(s.Origin.x + x * s.Step.x, s.HeightAtVertex(x, z), s.Origin.y + z * s.Step.y);
        private static double NextCrossing(float origin, float step, int cell, int sign, float rayOrigin, float direction) =>
            sign == 0 ? double.PositiveInfinity : (origin + (double)(cell + (sign > 0 ? 1 : 0)) * step - rayOrigin) / direction;
        private static bool Clip(double origin, double direction, double min, double max, ref double enter, ref double exit)
        {
            if (direction == 0) return origin >= min && origin <= max;
            double a = (min - origin) / direction, b = (max - origin) / direction;
            enter = Math.Max(enter, Math.Min(a, b)); exit = Math.Min(exit, Math.Max(a, b));
            return enter <= exit;
        }
        private static float Triangle(Ray ray, Vector3 a, Vector3 b, Vector3 c)
        {
            var e1 = b - a; var e2 = c - a;
            var p = Vector3.Cross(ray.direction, e2);
            double det = Vector3.Dot(e1, p);
            if (Math.Abs(det) < 1e-10) return float.PositiveInfinity;
            var s = ray.origin - a;
            double u = Vector3.Dot(s, p) / det;
            var q = Vector3.Cross(s, e1);
            double v = Vector3.Dot(ray.direction, q) / det;
            if (u < -1e-6 || v < -1e-6 || u + v > 1 + 1e-6) return float.PositiveInfinity;
            return (float)(Vector3.Dot(e2, q) / det);
        }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
    }
}
