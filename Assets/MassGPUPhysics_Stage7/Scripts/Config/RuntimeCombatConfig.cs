using UnityEngine;

namespace MassGPUPhysics.Stage7
{
    [CreateAssetMenu(menuName = "MassGPUPhysics/Stage7/Runtime Combat Config")]
    public sealed class RuntimeCombatConfig : ScriptableObject
    {
        [Min(0f)] public float defenderGuardRadius = 1.5f;
        [Min(0.1f)] public float defenderMaxChaseDistance = 24f;
        [Min(0.01f)] public float deathClipDuration = 1.5f;
    }
}
