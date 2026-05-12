using UnityEngine;

namespace MassGPUPhysics.Stage7
{
    public struct PipelineFrameContext
    {
        public float deltaTime;
        public int totalAgentCount;
        public int agentThreadGroupsX;
        public int gridThreadGroupsX;
        public int flowFieldThreadGroupsX;
        public int defenderFlowFieldThreadGroupsX;
        public bool rebuildDensityMap;
        public int densityMapThreadGroupsX;
        public int densityMapThreadGroupsY;
        public bool rebuildAttackerFlow;
        public bool rebuildDefenderFlow;
        public bool battleStarted;
        public Vector3 lodCenterPosition;
        public float nearLodRadius;
        public float midLodRadius;
        public float cullingRadius;
        public Vector4[] frustumPlanes;

        public int gridResolutionX;
        public int gridResolutionZ;
        public Vector2 gridOrigin;
        public Vector2 gridWorldSize;
        public float cellSize;
        public int maxAgentsPerCell;

        public int attackerCount;
        public UnitTypeGpuSettings attackerSettings;
        public UnitTypeGpuSettings defenderSettings;
        public float boundaryPadding;

        public int flowFieldResolutionX;
        public int flowFieldResolutionZ;
        public Vector2 flowFieldOrigin;
        public float flowFieldCellSize;
        public bool flowFieldEnabled;
        public float flowFieldWeight;
        public float flowFieldResponsiveness;

        public int defenderFlowFieldResolutionX;
        public int defenderFlowFieldResolutionZ;
        public Vector2 defenderFlowFieldOrigin;
        public float defenderFlowFieldCellSize;
        public bool defenderFlowFieldEnabled;

        public int runtimeFlowPreviewMode;
        public bool runtimeDynamicAttackerFlowEnabled;
        public bool runtimeDynamicDefenderFlowEnabled;
        public int dynamicFlowSectorCount;
        public float dynamicFlowTargetStopRadius;
        public int dynamicFlowMinDefendersPerTarget;
        public int dynamicDefenderFlowSectorCount;
        public float dynamicDefenderFlowTargetStopRadius;
        public int dynamicDefenderFlowMinAttackersPerTarget;

        public int defenderMovementMode;
        public float defenderGuardRadius;
        public float defenderMaxChaseDistance;
        public float deathClipDuration;
        public float animationDuration;
        public int nearAnimationInterval;
        public int midAnimationInterval;
        public int farAnimationInterval;
        public int frameIndex;
    }

    public struct UnitTypeInitContext
    {
        public int bufferOffset;
        public int totalAgentCount;
        public MassGpuBufferManager_Stage7 bufferManager;
        public ComputePipelineOrchestrator pipeline;
    }

    public struct UnitTypeGpuSettings
    {
        public float agentRadius;
        public float separationStrength;
        public int separationSkipInterval;
        public float densityAvoidanceStrength;
        public int densityComfortCount;
        public float densityPressureRange;
        public float densitySpeedPenalty;
        public float speedVariation;
        public float laneBiasStrength;
        public float velocityDamping;
        public float maxSpeed;
        public int flowTargetMode;
        public Vector3 flowTargetPoint;
        public Vector3 flowTargetAreaCenter;
        public Vector3 flowTargetAreaSize;
        public float targetAcquireRadius;
        public float attackRange;
        public int attackDamage;
        public float attackInterval;

        public static UnitTypeGpuSettings FromConfig(UnitTypeConfig config)
        {
            FlockingConfig flocking = config != null ? config.flockingConfig : null;
            MovementConfig movement = config != null ? config.movementConfig : null;
            CombatConfig combat = config != null ? config.combatConfig : null;

            return new UnitTypeGpuSettings
            {
                agentRadius = flocking != null ? Mathf.Max(0.01f, flocking.agentRadius) : 0.45f,
                separationStrength = flocking != null ? Mathf.Max(0f, flocking.separationStrength) : 18f,
                separationSkipInterval = flocking != null ? Mathf.Max(1, flocking.separationSkipInterval) : 1,
                densityAvoidanceStrength = flocking != null ? Mathf.Max(0f, flocking.densityAvoidanceStrength) : 2f,
                densityComfortCount = flocking != null ? Mathf.Max(0, flocking.densityComfortCount) : 2,
                densityPressureRange = flocking != null ? Mathf.Max(0.01f, flocking.densityPressureRange) : 6f,
                densitySpeedPenalty = flocking != null ? Mathf.Clamp01(flocking.densitySpeedPenalty) : 0.45f,
                speedVariation = flocking != null ? Mathf.Clamp(flocking.speedVariation, 0f, 0.5f) : 0.08f,
                laneBiasStrength = flocking != null ? Mathf.Clamp01(flocking.laneBiasStrength) : 0.25f,
                velocityDamping = movement != null ? Mathf.Clamp(movement.velocityDamping, 0f, 20f) : 5f,
                maxSpeed = movement != null ? Mathf.Max(0.01f, movement.maxSpeed) : 6f,
                flowTargetMode = movement != null && movement.useConfiguredFlowTarget ? (int)movement.flowTargetMode + 1 : 0,
                flowTargetPoint = movement != null ? movement.targetPoint : Vector3.zero,
                flowTargetAreaCenter = movement != null ? movement.targetAreaCenter : Vector3.zero,
                flowTargetAreaSize = movement != null ? movement.targetAreaSize : Vector3.zero,
                targetAcquireRadius = combat != null ? Mathf.Max(0.1f, combat.targetAcquireRadius) : 18f,
                attackRange = combat != null ? Mathf.Max(0.05f, combat.attackRange) : 1.35f,
                attackDamage = combat != null ? Mathf.Max(1, combat.attackDamage) : 10,
                attackInterval = combat != null ? Mathf.Max(0.01f, combat.attackInterval) : 0.8f
            };
        }
    }
}
