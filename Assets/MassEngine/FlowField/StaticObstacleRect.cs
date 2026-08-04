using System;
using UnityEngine;

namespace MassEngine
{
    /// <summary>
    /// Axis-aligned XZ obstacle shared by the game layer, CPU validation and GPU
    /// navigation. Rotation is deliberately excluded so the shader cost stays fixed.
    /// </summary>
    [Serializable]
    public struct StaticObstacleRect
    {
        public Vector2 center;
        public Vector2 size;

        public StaticObstacleRect(Vector2 center, Vector2 size)
        {
            this.center = center;
            this.size = size;
        }

        public bool IsValid
        {
            get { return size.x > 0.01f && size.y > 0.01f && IsFinite(center) && IsFinite(size); }
        }

        public Rect Bounds
        {
            get
            {
                Vector2 safeSize = new Vector2(Mathf.Max(0f, size.x), Mathf.Max(0f, size.y));
                return new Rect(center - safeSize * 0.5f, safeSize);
            }
        }

        public Vector4 ToShaderRect()
        {
            Rect bounds = Bounds;
            return new Vector4(bounds.xMin, bounds.yMin, bounds.xMax, bounds.yMax);
        }

        private static bool IsFinite(Vector2 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                   !float.IsNaN(value.y) && !float.IsInfinity(value.y);
        }
    }

    public static class StaticObstacleMath
    {
        public const int MaxObstacleCount = 8;

        public static bool SegmentIntersects(StaticObstacleRect obstacle, Vector2 start, Vector2 end, float padding = 0f)
        {
            if (!obstacle.IsValid)
                return false;

            Rect bounds = obstacle.Bounds;
            float safePadding = Mathf.Max(0f, padding);
            bounds.xMin -= safePadding;
            bounds.xMax += safePadding;
            bounds.yMin -= safePadding;
            bounds.yMax += safePadding;

            Vector2 delta = end - start;
            float enter = 0f;
            float exit = 1f;
            return ClipAxis(start.x, delta.x, bounds.xMin, bounds.xMax, ref enter, ref exit) &&
                   ClipAxis(start.y, delta.y, bounds.yMin, bounds.yMax, ref enter, ref exit) &&
                   exit >= enter;
        }

        public static Vector3 ResolvePointOutside(StaticObstacleRect obstacle, Vector3 point, float padding)
        {
            if (!obstacle.IsValid)
                return point;

            Rect bounds = obstacle.Bounds;
            float safePadding = Mathf.Max(0f, padding);
            bounds.xMin -= safePadding;
            bounds.xMax += safePadding;
            bounds.yMin -= safePadding;
            bounds.yMax += safePadding;
            Vector2 xz = new Vector2(point.x, point.z);
            if (!bounds.Contains(xz))
                return point;

            float left = xz.x - bounds.xMin;
            float right = bounds.xMax - xz.x;
            float bottom = xz.y - bounds.yMin;
            float top = bounds.yMax - xz.y;
            float nearest = Mathf.Min(left, right, bottom, top);
            const float epsilon = 0.01f;
            if (Mathf.Approximately(nearest, left))
                point.x = bounds.xMin - epsilon;
            else if (Mathf.Approximately(nearest, right))
                point.x = bounds.xMax + epsilon;
            else if (Mathf.Approximately(nearest, bottom))
                point.z = bounds.yMin - epsilon;
            else
                point.z = bounds.yMax + epsilon;
            return point;
        }

        private static bool ClipAxis(float start, float delta, float minimum, float maximum, ref float enter, ref float exit)
        {
            if (Mathf.Abs(delta) <= 0.000001f)
                return start >= minimum && start <= maximum;

            float inverse = 1f / delta;
            float first = (minimum - start) * inverse;
            float second = (maximum - start) * inverse;
            if (first > second)
            {
                float swap = first;
                first = second;
                second = swap;
            }

            enter = Mathf.Max(enter, first);
            exit = Mathf.Min(exit, second);
            return exit >= enter;
        }
    }
}
