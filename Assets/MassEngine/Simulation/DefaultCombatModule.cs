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

            // 远程武器参数
            settings.projectileRange = Mathf.Max(0f, Config.projectileRange);
            settings.projectileSpeed = Mathf.Max(0.1f, Config.projectileSpeed);
            settings.projectileGravity = Config.projectileGravity;
            settings.projectileHitRadius = Mathf.Max(0.1f, Config.projectileHitRadius);
            settings.projectileMaxLifetime = Mathf.Max(0.5f, Config.projectileMaxLifetime);
            settings.projectileTrailLength = Mathf.Max(0f, Config.projectileTrailLength);
        }
    }
}
