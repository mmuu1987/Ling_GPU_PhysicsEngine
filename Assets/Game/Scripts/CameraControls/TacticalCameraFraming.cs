using UnityEngine;

/// <summary>
/// Fits a world-space box into a perspective view for a fixed camera orientation (2026-10-10 camera feedback).
/// All 8 corners are projected with both the vertical and the horizontal field of view, so wide screens are used
/// fully; the aim point is re-centred because perspective makes the near half of a tilted box look larger.
/// </summary>
public static class TacticalCameraFraming
{
    public static float YawToward(Vector3 from, Vector3 to, float fallbackYaw)
    {
        Vector2 d = new Vector2(to.x - from.x, to.z - from.z);
        return d.sqrMagnitude > 1f && CameraMotionSafety.IsFinite(d.x) && CameraMotionSafety.IsFinite(d.y)
            ? Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg
            : fallbackYaw;
    }

    public static bool TryFit(Bounds bounds, Quaternion rotation, float verticalFovDegrees, float aspect, float margin,
        float minDistance, out Vector3 lookAt, out float distance)
    {
        lookAt = bounds.center; distance = Mathf.Max(0.1f, minDistance);
        if (!CameraMotionSafety.IsFinite(bounds.center) || !CameraMotionSafety.IsFinite(bounds.extents) ||
            !CameraMotionSafety.IsFinite(aspect) || aspect <= 0f || !CameraMotionSafety.IsFinite(verticalFovDegrees))
            return false;
        float tanV = Mathf.Tan(Mathf.Clamp(verticalFovDegrees, 5f, 170f) * 0.5f * Mathf.Deg2Rad) * Mathf.Clamp(margin, 0.2f, 1f);
        float tanH = tanV * aspect;
        Vector3 r = rotation * Vector3.right, u = rotation * Vector3.up, f = rotation * Vector3.forward;
        Vector3 e = bounds.extents;
        var corners = new Vector3[8];
        for (int i = 0; i < 8; i++)
            corners[i] = bounds.center + new Vector3((i & 1) != 0 ? e.x : -e.x, (i & 2) != 0 ? e.y : -e.y, (i & 4) != 0 ? e.z : -e.z);

        Vector3 c = bounds.center;
        float d = Required(corners, c, r, u, f, tanH, tanV, distance);
        for (int iteration = 0; iteration < 3; iteration++)
        {
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            foreach (var p in corners)
            {
                Vector3 q = p - c;
                float depth = Mathf.Max(0.01f, d + Vector3.Dot(q, f));
                float sx = Vector3.Dot(q, r) / (depth * tanH), sy = Vector3.Dot(q, u) / (depth * tanV);
                minX = Mathf.Min(minX, sx); maxX = Mathf.Max(maxX, sx); minY = Mathf.Min(minY, sy); maxY = Mathf.Max(maxY, sy);
            }
            float midX = (minX + maxX) * 0.5f, midY = (minY + maxY) * 0.5f;
            if (Mathf.Abs(midX) < 0.01f && Mathf.Abs(midY) < 0.01f) break;
            c += r * (midX * d * tanH) + u * (midY * d * tanV);
            d = Required(corners, c, r, u, f, tanH, tanV, distance);
        }
        if (!CameraMotionSafety.IsFinite(c) || !CameraMotionSafety.IsFinite(d)) return false;
        lookAt = c; distance = d;
        return true;
    }

    private static float Required(Vector3[] corners, Vector3 c, Vector3 r, Vector3 u, Vector3 f, float tanH, float tanV, float minDistance)
    {
        float d = minDistance;
        foreach (var p in corners)
        {
            Vector3 q = p - c;
            float x = Vector3.Dot(q, r), y = Vector3.Dot(q, u), z = Vector3.Dot(q, f);
            d = Mathf.Max(d, Mathf.Abs(x) / tanH - z, Mathf.Abs(y) / tanV - z);
        }
        return d;
    }
}
