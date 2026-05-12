using UnityEngine;

namespace MassGPUPhysics.Stage7
{
    [CreateAssetMenu(menuName = "MassGPUPhysics/Stage7/Combat Config")]
    public sealed class CombatConfig : ScriptableObject
    {
        [Min(0.1f)] public float targetAcquireRadius = 18f;
        [Min(0.05f)] public float attackRange = 1.35f;
        [Min(1)] public int attackDamage = 10;
        [Min(0.01f)] public float attackInterval = 0.8f;
        [Min(1)] public int maxHp = 100;
    }
}
