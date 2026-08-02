using UnityEngine;

namespace MassEngine
{
    [CreateAssetMenu(menuName = "MassEngine/Runtime Flow Config")]
    public sealed class RuntimeFlowConfig : ScriptableObject
    {
        [Min(16)] public int flowFieldResolution = 128;
        [Min(0.1f)] public float flowFieldCellSize = 2f;
        public Vector2 flowFieldOrigin = new Vector2(-80f, -80f);
        [Tooltip("Master switch for the attacker flow field. Dynamic targeting below is SUBORDINATE to this: off means the attacker army has no navigation at all.")]
        public bool flowFieldEnabled = true;
        [Tooltip("Master switch for the defender flow field. ALSO selects the defender doctrine: OFF = HOLD_POSITION (pinned near spawn within guardRadius), ON = FLOW_FIELD (mobile, chases within acquire radius).")]
        public bool defenderFlowFieldEnabled;

        [Header("Rebuild Cadence")]
        [Tooltip("Seconds between dynamic attacker flow field rebuilds. 0 = every frame.")]
        [Min(0f)] public float dynamicFlowUpdateInterval = 0.35f;
        [Tooltip("Seconds between dynamic defender flow field rebuilds. 0 = every frame.")]
        [Min(0f)] public float dynamicDefenderFlowUpdateInterval = 0.35f;

        [Header("Preview")]
        [Tooltip("When off, flow kernels skip all preview texture writes (zero preview GPU cost).")]
        public bool runtimeFlowPreviewEnabled;
        public FlowFieldPreviewMode runtimeFlowPreviewMode = FlowFieldPreviewMode.FlowDirection;

        [Header("Dynamic Targets")]
        [Tooltip("Steer the attacker flow field at enemy density centroids. Requires flowFieldEnabled — with the master switch off this does nothing (the ledger warns).")]
        public bool runtimeDynamicAttackerFlowEnabled = true;
        [Tooltip("Steer the defender flow field at enemy density centroids. Requires defenderFlowFieldEnabled.")]
        public bool runtimeDynamicDefenderFlowEnabled;
        [Range(1, 8)] public int dynamicFlowSectorCount = 5;
        [Min(0f)] public float dynamicFlowTargetStopRadius = 2f;
        [Min(1)] public int dynamicFlowMinDefendersPerTarget = 8;
        [Range(1, 8)] public int dynamicDefenderFlowSectorCount = 5;
        [Min(0f)] public float dynamicDefenderFlowTargetStopRadius = 2f;
        [Min(1)] public int dynamicDefenderFlowMinAttackersPerTarget = 8;
    }
}
