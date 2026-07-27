using UnityEngine;

public static class SceneViewCameraBoundsUtility_Stage6
{
    public static Bounds CalculateBounds(Transform target)
    {
        Renderer renderer = target.GetComponent<Renderer>();
        Bounds bounds = renderer != null
            ? renderer.bounds
            : new Bounds(target.position, Vector3.one);

        CalculateChildBounds(target, ref bounds);

        if (bounds.extents == Vector3.zero)
            bounds.extents = new Vector3(0.5f, 0.5f, 0.5f);

        return bounds;
    }

    public static Bounds TransformBounds(Matrix4x4 matrix, Bounds bounds)
    {
        Vector3 center = matrix.MultiplyPoint(bounds.center);
        Vector3 extents = bounds.extents;
        Vector3 axisX = matrix.MultiplyVector(new Vector3(extents.x, 0f, 0f));
        Vector3 axisY = matrix.MultiplyVector(new Vector3(0f, extents.y, 0f));
        Vector3 axisZ = matrix.MultiplyVector(new Vector3(0f, 0f, extents.z));

        extents.x = Mathf.Abs(axisX.x) + Mathf.Abs(axisY.x) + Mathf.Abs(axisZ.x);
        extents.y = Mathf.Abs(axisX.y) + Mathf.Abs(axisY.y) + Mathf.Abs(axisZ.y);
        extents.z = Mathf.Abs(axisX.z) + Mathf.Abs(axisY.z) + Mathf.Abs(axisZ.z);

        return new Bounds(center, extents * 2f);
    }

    private static void CalculateChildBounds(Transform target, ref Bounds totalBounds)
    {
        foreach (Transform child in target)
        {
            Renderer renderer = child.GetComponent<Renderer>();
            if (renderer != null)
            {
                totalBounds.Encapsulate(renderer.bounds.min);
                totalBounds.Encapsulate(renderer.bounds.max);
            }

            CalculateChildBounds(child, ref totalBounds);
        }
    }
}
