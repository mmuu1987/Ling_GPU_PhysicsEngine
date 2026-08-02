using UnityEngine;

namespace MassEngine
{
    public sealed class DefaultFlockingModule : IFlockingModule
    {
        public FlockingConfig Config { get; private set; }

        public DefaultFlockingModule(FlockingConfig config)
        {
            Config = config;
        }

        public void Contribute(ref UnitTypeGpuSettings settings)
        {
            if (Config == null)
                return;

            settings.agentRadius = Mathf.Max(0.01f, Config.agentRadius);
            settings.separationStrength = Mathf.Max(0f, Config.separationStrength);
            settings.attractionStrength = Mathf.Max(0f, Config.attractionStrength);
            settings.densityAvoidanceStrength = Mathf.Max(0f, Config.densityAvoidanceStrength);
            settings.densityComfortPerSqm = Mathf.Max(0f, Config.densityComfortPerSqm);
            settings.densityPressureRangePerSqm = Mathf.Max(0.01f, Config.densityPressureRangePerSqm);
            settings.densitySpeedPenalty = Mathf.Clamp01(Config.densitySpeedPenalty);
            settings.speedVariation = Mathf.Clamp(Config.speedVariation, 0f, 0.5f);
            settings.laneBiasStrength = Mathf.Clamp01(Config.laneBiasStrength);
        }
    }
}
