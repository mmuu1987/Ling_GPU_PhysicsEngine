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

        [Tooltip("Melee area damage (stomp) around the struck target. 0 = single target (default; behaviour unchanged). " +
                 "Every other living enemy whose body circle overlaps this radius also takes the same melee hit once. No friendly fire. Ranged units ignore it.")]
        [Min(0f)] public float meleeSplashRadius = 0f;

        [Tooltip("Trample (2026-10-16, cavalry): 0 = off (default; behaviour unchanged). While a melee unit moves at trample speed " +
                 "(trampleMinSpeedFraction x maxSpeed) and is not swinging, it rides through small enemies instead of stopping: each enemy " +
                 "body it runs into takes attackDamage x this once and is shoved aside; large enemy bodies stop the charge.")]
        [Min(0f)] public float trampleDamageMultiplier = 0f;

        [Tooltip("Trample: fraction of the rider's speed lost per trampled enemy. The charge ends once speed drops below the trample speed.")]
        [Range(0f, 1f)] public float trampleSpeedLoss = 0.0025f;

        [Tooltip("Trample: minimum speed as a fraction of maxSpeed. Open-ground cruise is about 0.55 x maxSpeed (velocity damping), " +
                 "so this sits below it; separate from chargeMinSpeedFraction on purpose.")]
        [Range(0.05f, 2f)] public float trampleMinSpeedFraction = 0.45f;

        [Tooltip("Trample: share of incoming damage taken while still at trample speed (charge momentum). 1 = unchanged.")]
        [Range(0f, 1f)] public float trampleDamageTaken = 1f;

        [Tooltip("Flight (2026-10-15): altitude in metres for a flying unit. 0 = ground unit (default; behaviour unchanged). " +
                 "A flying unit still moves on the ground grid but is drawn this high; ground melee units can neither target nor touch it, " +
                 "only ranged units and other flyers hit it. Splash stays in its own layer (ground or air).")]
        [Min(0f)] public float flightAltitude = 0f;

        [Tooltip("Fireball look (2026-10-16), render only: alpha 0 = legacy look (default). Otherwise splash shots draw as a glowing " +
                 "fireball with a flickering flame tail and burst on impact (fire ball, shock ring, smoke), tinted with this colour.")]
        public Color projectileFxColor = new Color(0f, 0f, 0f, 0f);
    }
}
