using UnityEngine;

namespace MassGPUPhysics.Stage7
{
    [CreateAssetMenu(menuName = "MassGPUPhysics/Stage7/Runtime Flow Config")]
    public sealed class RuntimeFlowConfig : ScriptableObject
    {
        [Min(16)] public int flowFieldResolution = 128;
        [Min(0.1f)] public float flowFieldCellSize = 2f;
        public Vector2 flowFieldOrigin = new Vector2(-80f, -80f);
        public bool flowFieldEnabled = true;
        public bool defenderFlowFieldEnabled;
        [Range(0f, 1f)] public float flowFieldWeight = 1f;
        [Min(0f)] public float flowFieldResponsiveness = 6f;
        public FlowFieldPreviewMode runtimeFlowPreviewMode = FlowFieldPreviewMode.FlowDirection;
        public bool runtimeDynamicAttackerFlowEnabled = true;
        public bool runtimeDynamicDefenderFlowEnabled;
        [Range(1, 8)] public int dynamicFlowSectorCount = 5;
        [Min(0f)] public float dynamicFlowTargetStopRadius = 2f;
        [Min(1)] public int dynamicFlowMinDefendersPerTarget = 8;
        [Range(1, 8)] public int dynamicDefenderFlowSectorCount = 5;
        [Min(0f)] public float dynamicDefenderFlowTargetStopRadius = 2f;
        [Min(1)] public int dynamicDefenderFlowMinAttackersPerTarget = 8;
    }
}
