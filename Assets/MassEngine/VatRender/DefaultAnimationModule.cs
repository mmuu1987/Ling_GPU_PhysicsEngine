using UnityEngine;

namespace MassEngine
{
    public sealed class DefaultAnimationModule : IAnimationModule
    {
        public AnimationConfig Config { get; private set; }

        public DefaultAnimationModule(AnimationConfig config)
        {
            Config = config;
        }

        public void Contribute(ref UnitTypeGpuSettings settings)
        {
            if (Config == null)
                return;

            settings.moveAnimationSpeedMin = Mathf.Max(0.01f, Config.moveAnimationSpeedMin);
            settings.moveAnimationSpeedMax = Mathf.Max(settings.moveAnimationSpeedMin, Config.moveAnimationSpeedMax);
        }
    }
}
