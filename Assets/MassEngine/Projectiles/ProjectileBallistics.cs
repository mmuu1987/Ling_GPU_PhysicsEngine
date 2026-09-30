using UnityEngine;

namespace MassEngine.Projectiles
{
    /// <summary>Paired with RangedClearance.hlsl. Speed remains the maximum horizontal speed for lofts.</summary>
    public static class ProjectileBallistics
    {
        public static float Clearance(float sourceHalfHeight, float sourceScaleY, float targetHalfHeight, float targetScaleY, float horizontalDistance = 0f)
        {
            float body = 2 * Mathf.Max(Mathf.Max(.05f, sourceHalfHeight) * Mathf.Max(.01f, Mathf.Abs(sourceScaleY)),
                Mathf.Max(.05f, targetHalfHeight) * Mathf.Max(.01f, Mathf.Abs(targetScaleY)));
            return Mathf.Clamp(Mathf.Max(body * 1.25f, horizontalDistance * body * .2f), .5f, 12f);
        }
        public static float FlightTime(Vector3 source, Vector3 target, float speed, float gravity, float lifetime, float clearance)
        {
            if (speed <= 0 || lifetime <= 0) return 0;
            Vector3 delta = target - source;
            float time = gravity < -.01f ? new Vector2(delta.x, delta.z).magnitude / speed : delta.magnitude / speed;
            if (time > lifetime || time < .00001f) return 0;
            if (gravity < -.01f && clearance > 0)
            {
                float apex = Mathf.Max(source.y, target.y) + clearance;
                float loft = Mathf.Sqrt(2 * (apex - source.y) / -gravity) + Mathf.Sqrt(2 * (apex - target.y) / -gravity);
                time = Mathf.Max(time, Mathf.Min(loft, lifetime * .95f));
            }
            return time;
        }
    }
}
