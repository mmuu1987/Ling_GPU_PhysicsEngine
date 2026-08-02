using UnityEngine;

namespace MassEngine
{
    [CreateAssetMenu(menuName = "MassEngine/Flocking Config")]
    public sealed class FlockingConfig : ScriptableObject
    {
        [Min(0.01f)] public float agentRadius = 0.45f;
        [Min(0f)] public float separationStrength = 18f;
        [Min(0f)] public float attractionStrength = 1f;
        [Min(0f)] public float densityAvoidanceStrength = 2f;
        [Tooltip("Crowd pressure starts above this density (agents/m2). Marching density is ~0.5; physical packing ~1.5.")]
        [Min(0f)] public float densityComfortPerSqm = 0.6f;
        [Tooltip("Pressure ramps to full over this many agents/m2 above comfort.")]
        [Min(0.01f)] public float densityPressureRangePerSqm = 1.2f;
        [Range(0f, 1f)] public float densitySpeedPenalty = 0.45f;
        [Range(0f, 0.5f)] public float speedVariation = 0.08f;
        [Range(0f, 1f)] public float laneBiasStrength = 0.25f;
    }
}
