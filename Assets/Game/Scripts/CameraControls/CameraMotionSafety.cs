using UnityEngine;

/// <summary>
/// Pure safety math shared by runtime camera controllers. It prevents wheel spikes,
/// editor stalls and invalid serialized values from producing exponential camera
/// travel or NaN/Infinity transforms.
/// </summary>
public static class CameraMotionSafety
{
    public static float ResolveZoomDistance(
        float currentDistance,
        float wheelDelta,
        float sensitivity,
        float minDistance,
        float maxDistance)
    {
        minDistance = Mathf.Max(0.1f, SanitizePositive(minDistance, 1f));
        maxDistance = Mathf.Max(minDistance, SanitizePositive(maxDistance, 2500f));
        currentDistance = Mathf.Clamp(SanitizePositive(currentDistance, minDistance), minDistance, maxDistance);
        wheelDelta = Mathf.Clamp(IsFinite(wheelDelta) ? wheelDelta : 0f, -0.25f, 0.25f);
        sensitivity = Mathf.Clamp(IsFinite(sensitivity) ? sensitivity : 1f, 0f, 20f);

        // 0.1 wheel units at the legacy sensitivity of 10 changes distance by
        // roughly 22%, but can never cross the focus point or grow without bounds.
        float exponent = Mathf.Clamp(-wheelDelta * sensitivity * 0.25f, -2f, 2f);
        return Mathf.Clamp(currentDistance * Mathf.Exp(exponent), minDistance, maxDistance);
    }

    public static Vector3 ClampStep(Vector3 step, float maxMagnitude)
    {
        if (!IsFinite(step))
            return Vector3.zero;

        maxMagnitude = Mathf.Max(0.01f, SanitizePositive(maxMagnitude, 100f));
        return Vector3.ClampMagnitude(step, maxMagnitude);
    }

    public static Vector3 ClampWorldPosition(Vector3 position, float maxCoordinate)
    {
        if (!IsFinite(position))
            return Vector3.zero;

        maxCoordinate = Mathf.Max(10f, SanitizePositive(maxCoordinate, 5000f));
        position.x = Mathf.Clamp(position.x, -maxCoordinate, maxCoordinate);
        position.y = Mathf.Clamp(position.y, -maxCoordinate, maxCoordinate);
        position.z = Mathf.Clamp(position.z, -maxCoordinate, maxCoordinate);
        return position;
    }

    public static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    public static bool IsFinite(Vector3 value)
    {
        return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
    }

    private static float SanitizePositive(float value, float fallback)
    {
        return IsFinite(value) && value > 0f ? value : fallback;
    }
}
