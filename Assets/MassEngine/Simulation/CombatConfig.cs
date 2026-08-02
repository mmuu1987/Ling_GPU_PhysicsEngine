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
    }
}
