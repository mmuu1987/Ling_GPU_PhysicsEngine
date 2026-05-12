using UnityEngine;

namespace MassGPUPhysics.Stage7
{
    public sealed class DefaultFlockingModule : IFlockingModule
    {
        public FlockingConfig Config { get; private set; }

        public DefaultFlockingModule(FlockingConfig config)
        {
            Config = config;
        }

        public Vector3 ComputeSeparationForce(Vector3 agentPosition, Vector3 neighborPosition, float agentRadius, float separationStrength)
        {
            Vector3 diff = agentPosition - neighborPosition;
            diff.y = 0f;

            float distance = diff.magnitude;
            if (distance <= 0.0001f)
                diff = Vector3.right;
            else
                diff /= distance;

            float overlap = agentRadius * 2f - distance;
            if (overlap <= 0f)
                return Vector3.zero;

            return diff * overlap * Mathf.Max(0f, separationStrength);
        }

        public Vector3 ComputeAttractionForce(Vector3 agentPosition, Vector3 targetPosition, float attractionStrength)
        {
            Vector3 toTarget = targetPosition - agentPosition;
            toTarget.y = 0f;

            if (toTarget.sqrMagnitude <= 0.000001f)
                return Vector3.zero;

            return toTarget.normalized * Mathf.Max(0f, attractionStrength);
        }
    }
}
