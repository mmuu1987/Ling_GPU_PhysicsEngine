using UnityEngine;

namespace MassGPUPhysics.Stage7
{
    [CreateAssetMenu(menuName = "MassGPUPhysics/Stage7/Flocking Config")]
    public sealed class FlockingConfig : ScriptableObject
    {
        [Min(0.01f)] public float agentRadius = 0.45f;
        [Min(0f)] public float separationStrength = 18f;
        [Min(0f)] public float attractionStrength = 1f;
        [Min(1)] public int separationSkipInterval = 1;
        [Min(0f)] public float densityAvoidanceStrength = 2f;
        [Min(0)] public int densityComfortCount = 2;
        [Min(0.01f)] public float densityPressureRange = 6f;
        [Range(0f, 1f)] public float densitySpeedPenalty = 0.45f;
        [Range(0f, 0.5f)] public float speedVariation = 0.08f;
        [Range(0f, 1f)] public float laneBiasStrength = 0.25f;
    }
}
