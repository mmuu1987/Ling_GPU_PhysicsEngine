using UnityEngine;

namespace MassEngine
{
    /// <summary>Presentation tuning only. Clips remain in VATProfile; no rebake required.</summary>
    [CreateAssetMenu(menuName = "MassEngine/Animation Config")]
    public sealed class AnimationConfig : ScriptableObject
    {
        [Tooltip("Reference movement speed at scale 1 for 1x Move playback; 0 uses maxSpeed. Tune per body/stride, not per army.")]
        [Min(0f)] public float moveReferenceSpeed;
        [Tooltip("Actual speed below which locomotion returns to Idle (at scale 1).")]
        [Min(0f)] public float moveStopSpeed = 0.05f;
        [Tooltip("Speed required to leave Idle; must exceed Stop Speed to avoid jitter.")]
        [Min(0f)] public float moveStartSpeed = 0.12f;
        [Range(0.05f, 1.5f)] public float moveAnimationSpeedMin = 0.2f;
        [Range(0.5f, 2f)] public float moveAnimationSpeedMax = 1.15f;
    }
}
