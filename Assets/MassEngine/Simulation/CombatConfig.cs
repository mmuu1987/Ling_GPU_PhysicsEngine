using UnityEngine;

namespace MassEngine
{
    [CreateAssetMenu(menuName = "MassEngine/Combat Config")]
    public sealed class CombatConfig : ScriptableObject
    {
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
    }
}
