using UnityEngine;

namespace MassEngine
{
    public sealed class DefaultCombatModule : ICombatModule
    {
        public CombatConfig Config { get; private set; }

        public int MaxHp
        {
            get { return Config != null ? Mathf.Max(1, Config.maxHp) : 100; }
        }

        public DefaultCombatModule(CombatConfig config)
        {
            Config = config;
        }

        public void Contribute(ref UnitTypeGpuSettings settings)
        {
            if (Config == null)
                return;

            settings.targetAcquireRadius = Mathf.Max(0.1f, Config.targetAcquireRadius);
            settings.attackRange = Mathf.Max(0.05f, Config.attackRange);
            settings.attackDamage = Mathf.Max(1, Config.attackDamage);
            settings.attackInterval = Mathf.Max(0.01f, Config.attackInterval);
        }
    }
}
