using UnityEngine;

namespace MassEngine
{
    public sealed class DefaultMovementModule : IMovementModule
    {
        public MovementConfig Config { get; private set; }

        private FlowFieldTarget runtimeOverride;
        private bool hasRuntimeOverride;

        /// <summary>
        /// Configured targets are re-read from the config LIVE (inspector edits apply
        /// next frame, like every other config field); a runtime SetTargetPoint/Area
        /// call overrides the config until the unit type is rebuilt.
        /// </summary>
        public FlowFieldTarget Target
        {
            get { return hasRuntimeOverride ? runtimeOverride : DeriveConfiguredTarget(); }
        }

        public DefaultMovementModule(MovementConfig config)
        {
            Config = config;
        }

        public void SetTargetPoint(Vector3 point)
        {
            runtimeOverride = new FlowFieldTarget
            {
                active = true,
                mode = FlowFieldTargetMode.Point,
                center = point,
                size = Vector3.zero
            };
            hasRuntimeOverride = true;
        }

        public void SetTargetArea(Vector3 center, Vector3 size)
        {
            runtimeOverride = new FlowFieldTarget
            {
                active = true,
                mode = FlowFieldTargetMode.Area,
                center = center,
                size = new Vector3(Mathf.Max(0f, size.x), Mathf.Max(0f, size.y), Mathf.Max(0f, size.z))
            };
            hasRuntimeOverride = true;
        }

        public void ClearTarget()
        {
            runtimeOverride = default;
            hasRuntimeOverride = false;
        }

        private FlowFieldTarget DeriveConfiguredTarget()
        {
            if (Config == null || !Config.useConfiguredFlowTarget)
                return default;

            if (Config.flowTargetMode == FlowFieldTargetMode.Area)
            {
                return new FlowFieldTarget
                {
                    active = true,
                    mode = FlowFieldTargetMode.Area,
                    center = Config.targetAreaCenter,
                    size = new Vector3(Mathf.Max(0f, Config.targetAreaSize.x), Mathf.Max(0f, Config.targetAreaSize.y), Mathf.Max(0f, Config.targetAreaSize.z))
                };
            }

            return new FlowFieldTarget
            {
                active = true,
                mode = FlowFieldTargetMode.Point,
                center = Config.targetPoint,
                size = Vector3.zero
            };
        }

        public void Contribute(ref UnitTypeGpuSettings settings)
        {
            if (Config == null)
                return;

            settings.maxSpeed = Mathf.Max(0.01f, Config.maxSpeed);
            settings.velocityDamping = Mathf.Clamp(Config.velocityDamping, 0f, 20f);
            settings.flowFieldWeight = Mathf.Clamp01(Config.flowFieldWeight);
            settings.flowFieldResponsiveness = Mathf.Max(0f, Config.flowFieldResponsiveness);
        }
    }
}
