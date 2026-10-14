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

        [Header("GPU Nav Field")]
        [Tooltip("Opt-in, default off (Docs/GPU-NAVFIELD-N0-20261013.md). Shadow mode for now: builds the 2 m GPU distance field with the terrain navigation (load / navigation rebuild) and keeps it current every frame; agents still steer by the CPU flow field. Needs MassEngineShaderConfig.gpuNavFieldShader.")]
        public bool gpuNavField;
        [Tooltip("GPU relaxation passes per team per frame (about 0.03 ms each at 512x512).")]
        [Range(1, 64)] public int gpuNavPassesPerFrame = 2;
        [Tooltip("N3, opt-in, default off: agents steer by the GPU field's -gradient (eikonal, round wavefronts) wherever their team's field is seeded; the CPU flow field stays the fallback. Requires gpuNavField.")]
        public bool gpuNavSteering;
        [Tooltip("N4, opt-in, default off: each team's own agents make crowded 2 m cells slower in its GPU field, so following troops spread and route around a blocked front. Requires gpuNavField (and gpuNavSteering to affect movement).")]
        public bool gpuNavCrowd;
        [Tooltip("Extra cost at full crowd: cell cost x (1 + gain * min(density / fullDensity, cap)).")]
        [Min(0f)] public float gpuNavCrowdGain = 2f;
        [Tooltip("Agents per square metre counted as fully packed.")]
        [Min(0.01f)] public float gpuNavCrowdFullDensity = 1.5f;
        [Tooltip("Upper bound of density / fullDensity.")]
        [Min(0f)] public float gpuNavCrowdCap = 1f;
        [Tooltip("Smoothing time constant of the crowd counts, seconds (damps oscillation between routes).")]
        [Min(0.01f)] public float gpuNavCrowdTimeConstant = 0.5f;
        [Tooltip("Agents count by congestion, not density: weight = 1 - realSpeed / freeSpeed (clamped). A dense column at this speed or faster adds no cost. 0 = every agent counts fully.")]
        [Min(0f)] public float gpuNavCrowdFreeSpeed = 1.5f;
        [Tooltip("N5a: local Move/Retreat groups (LocalOrderChannel) steer on GPU fine-field layers of their own (needs gpuNavField + gpuNavSteering). Default off.")]
        public bool gpuNavLocalOrders;
        [Tooltip("N5b: a team point order steers to a formation line (perpendicular to centroid -> target, the army's frontage wide, half its depth beyond the target) instead of one cell. Needs gpuNavField + gpuNavSteering. Default off.")]
        public bool gpuNavFormationLine;
        [Min(2f)] public float gpuNavLineMinWidth = 8f;
        [Min(2f)] public float gpuNavLineMaxWidth = 400f;
        [Tooltip("Largest distance the line is placed beyond the clicked point (half the army depth, capped here).")]
        [Min(0f)] public float gpuNavLineMaxOffset = 60f;
        [Tooltip("Ground per agent of a settled block (m^2). Re-formed depth = alive * this / frontage; the line sits half that beyond the target. Measured ~0.6 (N5b).")]
        [Min(0f)] public float gpuNavLinePackingArea = 0.6f;
        [Tooltip("N5c: inside the arrival zone of a GPU-nav goal, plain marchers follow the friend ahead instead of shoving into the arrived ranks (the engage follow-gap rule). Needs gpuNavField + gpuNavSteering. Default off.")]
        public bool gpuNavArrivalFollow;
        [Tooltip("Arrival zone = this x the estimated depth of the block that settles at the goal.")]
        [Min(0f)] public float gpuNavArrivalZoneScale = 1.5f;

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
