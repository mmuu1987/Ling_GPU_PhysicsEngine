using UnityEngine;

namespace MassEngine
{
    [CreateAssetMenu(menuName = "MassEngine/Combat Config")]
    public sealed class CombatConfig : ScriptableObject
    {
        [Tooltip("Release point within a ranged attack cycle; preserves sustained attackInterval, adds first-shot windup.")]
        [Range(0.05f, 0.95f)] public float attackReleasePhase = 0.55f;
        [Tooltip("Launch height above feet at scale 1; approximate, not a bone socket.")]
        [Min(0.05f)] public float projectileOriginHeight = 1.3f;
        [Tooltip("Aim height / half body height at scale 1, also used for intervening-unit capsules.")]
        [Min(0.05f)] public float projectileTargetHeight = 1f;
        [Tooltip("Maximum distance for keeping a target valid. New targets are acquired from a bounded local spatial-hash search; long-range navigation remains flow-field driven.")]
        [Min(0.1f)] public float targetAcquireRadius = 8f;
        [Min(0.05f)] public float attackRange = 1.35f;
        [Min(1)] public int attackDamage = 10;
        [Min(0.01f)] public float attackInterval = 0.8f;
        [Min(1)] public int maxHp = 100;

        [Header("Ranged Weapon (leave projectileRange = 0 for melee)")]
        [Tooltip("Projectile attack range. 0 = melee mode (instant damage), >0 = ranged mode (spawn projectile).")]
        [Min(0f)] public float projectileRange = 0f;

        [Tooltip("Initial projectile velocity (m/s).")]
        [Min(0.1f)] public float projectileSpeed = 15f;

        [Tooltip("Gravity acceleration applied to projectile (0 = straight line, -9.8 = ballistic arc).")]
        public float projectileGravity = 0f;

        [Tooltip("Hit detection radius for projectile collision.")]
        [Min(0.1f)] public float projectileHitRadius = 0.5f;

        [Tooltip("Maximum projectile lifetime before auto-destruction (seconds).")]
        [Min(0.5f)] public float projectileMaxLifetime = 5f;

        [Tooltip("Base tracer length before the shared render scale is applied. 0 keeps the global minimum length.")]
        [Min(0f)] public float projectileTrailLength = 1f;

        [Tooltip("Area damage around the projectile impact point (unit hit or ground). 0 = single target (default; behaviour unchanged). " +
                 "Every other living enemy whose body circle overlaps this radius also takes the full projectile damage once. No friendly fire.")]
        [Min(0f)] public float projectileSplashRadius = 0f;

        [Tooltip("Melee charge: the first melee hit after closing in at speed deals attackDamage x this. 1 = off (default; behaviour unchanged). " +
                 "Applies only when the attacker was not already attacking on its previous decision and its speed is at least chargeMinSpeedFraction x maxSpeed. Ranged units ignore it.")]
        [Min(1f)] public float chargeDamageMultiplier = 1f;

        [Tooltip("Minimum approach speed for a charge hit, as a fraction of MovementConfig.maxSpeed.")]
        [Range(0f, 2f)] public float chargeMinSpeedFraction = 0.6f;
    }
}
