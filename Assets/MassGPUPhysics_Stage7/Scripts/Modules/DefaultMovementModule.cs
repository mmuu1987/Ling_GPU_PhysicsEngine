using UnityEngine;

namespace MassGPUPhysics.Stage7
{
    public sealed class DefaultMovementModule : IMovementModule
    {
        public MovementConfig Config { get; private set; }
        public FlowFieldTarget Target { get; private set; }

        public DefaultMovementModule(MovementConfig config)
        {
            Config = config;
            if (config != null && config.flowTargetMode == FlowFieldTargetMode.Area)
                SetTargetArea(config.targetAreaCenter, config.targetAreaSize);
            else
                SetTargetPoint(config != null ? config.targetPoint : Vector3.zero);
        }

        public void SetTargetPoint(Vector3 point)
        {
            Target = new FlowFieldTarget
            {
                mode = FlowFieldTargetMode.Point,
                center = point,
                size = Vector3.zero
            };
        }

        public void SetTargetArea(Vector3 center, Vector3 size)
        {
            Target = new FlowFieldTarget
            {
                mode = FlowFieldTargetMode.Area,
                center = center,
                size = new Vector3(Mathf.Max(0f, size.x), Mathf.Max(0f, size.y), Mathf.Max(0f, size.z))
            };
        }

        public Vector3 ComputeDesiredVelocity(Vector3 agentPosition, Vector2 flowDirection, float flowWeight)
        {
            float maxSpeed = Config != null ? Mathf.Max(0f, Config.maxSpeed) : 6f;
            float configuredWeight = Config != null ? Config.flowFieldWeight : 1f;
            float weight = Mathf.Clamp01(flowWeight) * Mathf.Clamp01(configuredWeight);
            Vector2 direction = flowDirection.sqrMagnitude > 0.000001f
                ? flowDirection.normalized
                : ComputeFallbackTargetDirection(agentPosition);

            if (direction.sqrMagnitude <= 0.000001f)
                return Vector3.zero;

            return new Vector3(direction.x, 0f, direction.y) * maxSpeed * weight;
        }

        private Vector2 ComputeFallbackTargetDirection(Vector3 agentPosition)
        {
            Vector3 target = Target.center;
            if (Target.mode == FlowFieldTargetMode.Area)
            {
                Vector3 halfSize = Target.size * 0.5f;
                target.x = Mathf.Clamp(agentPosition.x, Target.center.x - halfSize.x, Target.center.x + halfSize.x);
                target.z = Mathf.Clamp(agentPosition.z, Target.center.z - halfSize.z, Target.center.z + halfSize.z);
            }

            Vector2 delta = new Vector2(target.x - agentPosition.x, target.z - agentPosition.z);
            return delta.sqrMagnitude > 0.000001f ? delta.normalized : Vector2.zero;
        }
    }
}
