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

        /// <summary>
        /// Whether this doctrine gives <paramref name="teamId"/> a flow field at all. Only team 1
        /// carries its own toggle - that pair of fields is what a two-army config models. Any
        /// further team inherits the attacker doctrine (advance with dynamic targeting) and
        /// diverges at runtime through MassEngineManager.SetTeamNavigationOverride, rather than
        /// through new config fields that would drop the settings in every serialized asset.
        ///
        /// Public so the scene gizmos answer this the same way the manager does. A gizmo that
        /// recomputed the rule reported a third army's configured target as ignored while the GPU
        /// went on executing it.
        /// </summary>
        public bool ResolveTeamFlowEnabled(int teamId)
        {
            return teamId == MassEngineManager.DefenderTeamId ? defenderFlowFieldEnabled : flowFieldEnabled;
        }

        /// <summary>
        /// Whether <paramref name="teamId"/> steers its field at enemy density centroids.
        /// Subordinate to <see cref="ResolveTeamFlowEnabled"/>: with no field there is nothing to steer.
        /// </summary>
        public bool ResolveTeamDynamicTargeting(int teamId)
        {
            return teamId == MassEngineManager.DefenderTeamId
                ? runtimeDynamicDefenderFlowEnabled
                : runtimeDynamicAttackerFlowEnabled;
        }
    }
}
