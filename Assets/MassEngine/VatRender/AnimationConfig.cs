using UnityEngine;

namespace MassEngine
{
    /// <summary>
    /// Per-unit-type animation tuning. Clip frame data (start/count/rate) is NOT stored
    /// here — it lives in the VAT profile referenced by RenderConfig and is resolved at
    /// initialization into ResolvedUnitTypeRuntime. LOD animation cadence is global and
    /// lives in LodConfig.
    /// </summary>
    [CreateAssetMenu(menuName = "MassEngine/Animation Config")]
    public sealed class AnimationConfig : ScriptableObject
    {
        [Tooltip("Locomotion clip playback speed at zero velocity (scales up to Max at full speed).")]
        [Range(0.1f, 1.5f)] public float moveAnimationSpeedMin = 0.85f;
        [Tooltip("Locomotion clip playback speed at max velocity.")]
        [Range(0.5f, 2f)] public float moveAnimationSpeedMax = 1.15f;
    }
}
