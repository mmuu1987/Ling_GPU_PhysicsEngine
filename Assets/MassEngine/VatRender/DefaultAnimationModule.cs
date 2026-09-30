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

        private static float Finite(float value, float fallback) =>
            float.IsNaN(value) || float.IsInfinity(value) ? fallback : value;

        public void Contribute(ref UnitTypeGpuSettings settings)
        {
            if (Config == null)
                return;

            settings.moveReferenceSpeed = Mathf.Max(0f, Finite(Config.moveReferenceSpeed, 0f));
            settings.moveStopSpeed = Mathf.Max(0f, Finite(Config.moveStopSpeed, 0.05f));
            settings.moveStartSpeed = Mathf.Max(settings.moveStopSpeed + 0.01f, Finite(Config.moveStartSpeed, 0.12f));
            settings.moveAnimationSpeedMin = Mathf.Max(0.01f, Finite(Config.moveAnimationSpeedMin, 0.2f));
            settings.moveAnimationSpeedMax = Mathf.Max(settings.moveAnimationSpeedMin, Finite(Config.moveAnimationSpeedMax, 1.15f));
        }
    }
}
