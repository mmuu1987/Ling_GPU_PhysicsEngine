using UnityEngine;

namespace MassEngine
{
    [CreateAssetMenu(menuName = "MassEngine/Runtime Combat Config")]
    public sealed class RuntimeCombatConfig : ScriptableObject
    {
        [Min(0f)] public float defenderGuardRadius = 1.5f;
        [Tooltip("Fallback death clip duration used only for unit types without a VAT profile.")]
        [Min(0.01f)] public float deathClipDuration = 1.5f;
    }
}
