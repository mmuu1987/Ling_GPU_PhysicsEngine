using UnityEngine;

namespace MassEngine
{
    internal static class MassGpuShaderPropertyIds
    {
        // Frame constants
        public static readonly int DeltaTimeId = Shader.PropertyToID("deltaTime");
        public static readonly int FrameIndexId = Shader.PropertyToID("frameIndex");
        public static readonly int LodCenterId = Shader.PropertyToID("lodCenter");
        public static readonly int NearLodRadiusSqrId = Shader.PropertyToID("nearLodRadiusSqr");
        public static readonly int MidLodRadiusSqrId = Shader.PropertyToID("midLodRadiusSqr");
        public static readonly int EnableFrustumCullingId = Shader.PropertyToID("enableFrustumCulling");
        public static readonly int CullingRadiusId = Shader.PropertyToID("cullingRadius");
        public static readonly int MaxRenderDistanceSqrId = Shader.PropertyToID("maxRenderDistanceSqr");
        public static readonly int FarIncludeDeadId = Shader.PropertyToID("farIncludeDead");
        public static readonly int FrustumPlanesId = Shader.PropertyToID("frustumPlanes");
        public static readonly int NearAnimationIntervalId = Shader.PropertyToID("nearAnimationInterval");
        public static readonly int MidAnimationIntervalId = Shader.PropertyToID("midAnimationInterval");
        public static readonly int FarAnimationIntervalId = Shader.PropertyToID("farAnimationInterval");
        public static readonly int NearSimulationIntervalId = Shader.PropertyToID("nearSimulationInterval");
        public static readonly int MidSimulationIntervalId = Shader.PropertyToID("midSimulationInterval");
        public static readonly int FarSimulationIntervalId = Shader.PropertyToID("farSimulationInterval");

        // Agent / grid buffers
        public static readonly int AgentBufferId = Shader.PropertyToID("agentBuffer");
        public static readonly int AgentPositionReadBufferId = Shader.PropertyToID("agentPositionReadBuffer");
        public static readonly int AgentPositionBufferId = Shader.PropertyToID("agentPositionBuffer");
        public static readonly int GridCountsId = Shader.PropertyToID("gridCounts");
        public static readonly int GridAgentIndicesId = Shader.PropertyToID("gridAgentIndices");
        public static readonly int TeamGridCountsId = Shader.PropertyToID("teamGridCounts");
        public static readonly int TeamGridAgentIndicesId = Shader.PropertyToID("teamGridAgentIndices");
        public static readonly int SpatialHashStatsId = Shader.PropertyToID("spatialHashStats");
        public static readonly int TeamSpatialStatsId = Shader.PropertyToID("teamSpatialStats");
        public static readonly int GridCountsReadBufferId = Shader.PropertyToID("gridCountsReadBuffer");
        public static readonly int GridAgentIndicesReadBufferId = Shader.PropertyToID("gridAgentIndicesReadBuffer");
        public static readonly int TeamGridCountsReadBufferId = Shader.PropertyToID("teamGridCountsReadBuffer");
        public static readonly int TeamGridAgentIndicesReadBufferId = Shader.PropertyToID("teamGridAgentIndicesReadBuffer");
        public static readonly int GridCellCountId = Shader.PropertyToID("gridCellCount");
        public static readonly int GridResolutionId = Shader.PropertyToID("gridResolution");
        public static readonly int GridOriginId = Shader.PropertyToID("gridOrigin");
        public static readonly int GridWorldSizeId = Shader.PropertyToID("gridWorldSize");
        public static readonly int CellSizeId = Shader.PropertyToID("cellSize");
        public static readonly int MaxAgentsPerCellId = Shader.PropertyToID("maxAgentsPerCell");
        public static readonly int BoundaryPaddingId = Shader.PropertyToID("boundaryPadding");

        // Per-unit-type parameter channel
        public static readonly int UnitTypeSettingsId = Shader.PropertyToID("unitTypeSettings");
        public static readonly int UnitTypeIndexReadBufferId = Shader.PropertyToID("unitTypeIndexReadBuffer");

        // Team identity + combat globals
        public static readonly int EnableTwoTeamCombatId = Shader.PropertyToID("enableTwoTeamCombat");
        public static readonly int BattleStartedId = Shader.PropertyToID("battleStarted");
        public static readonly int AttackerTeamIdId = Shader.PropertyToID("attackerTeamId");
        public static readonly int DefenderTeamIdId = Shader.PropertyToID("defenderTeamId");
        public static readonly int TeamCountId = Shader.PropertyToID("teamCount");
        public static readonly int LocalTargetSearchCellRadiusId = Shader.PropertyToID("localTargetSearchCellRadius");
        public static readonly int DefenderGuardRadiusId = Shader.PropertyToID("defenderGuardRadius");

        // Combat buffers
        public static readonly int TeamIdReadBufferId = Shader.PropertyToID("teamIdReadBuffer");
        // One stance per team, indexed by raw teamId. Replaced the defenderMovementMode
        // uniform: stance belongs to a team, not to whichever team is "the defender".
        public static readonly int TeamStanceReadBufferId = Shader.PropertyToID("teamStanceReadBuffer");
        public static readonly int HpBufferId = Shader.PropertyToID("hpBuffer");
        public static readonly int HpReadBufferId = Shader.PropertyToID("hpReadBuffer");
        public static readonly int TargetAgentIndexBufferId = Shader.PropertyToID("targetAgentIndexBuffer");
        public static readonly int AttackCooldownBufferId = Shader.PropertyToID("attackCooldownBuffer");
        public static readonly int HomePositionReadBufferId = Shader.PropertyToID("homePositionReadBuffer");
        public static readonly int PendingDamageBufferId = Shader.PropertyToID("pendingDamageBuffer");
        public static readonly int PendingDamageReadBufferId = Shader.PropertyToID("pendingDamageReadBuffer");

        // Projectile system
        public static readonly int ProjectileBufferId = Shader.PropertyToID("projectileBuffer");
        public static readonly int LaunchRequestBufferId = Shader.PropertyToID("launchRequestBuffer");
        public static readonly int MaxProjectilesId = Shader.PropertyToID("maxProjectiles");
        public static readonly int ActiveProjectileIndicesId = Shader.PropertyToID("activeProjectileIndices");
        public static readonly int ProjectileAttackerTeamIdId = Shader.PropertyToID("_ProjectileAttackerTeamId");
        public static readonly int ProjectileAttackerColorId = Shader.PropertyToID("_ProjectileAttackerColor");
        public static readonly int ProjectileDefenderColorId = Shader.PropertyToID("_ProjectileDefenderColor");
        public static readonly int ProjectileTrailWidthId = Shader.PropertyToID("_ProjectileTrailWidth");
        public static readonly int ProjectileTrailLengthScaleId = Shader.PropertyToID("_ProjectileTrailLengthScale");
        public static readonly int ProjectileTrailMinLengthId = Shader.PropertyToID("_ProjectileTrailMinLength");
        public static readonly int CurrentTimeId = Shader.PropertyToID("currentTime");

        // Flow fields
        public static readonly int FlowFieldDirectionsId = Shader.PropertyToID("flowFieldDirections");
        public static readonly int FlowFieldDirectionsReadBufferId = Shader.PropertyToID("flowFieldDirectionsReadBuffer");
        public static readonly int FlowFieldResolutionId = Shader.PropertyToID("flowFieldResolution");
        public static readonly int FlowFieldOriginId = Shader.PropertyToID("flowFieldOrigin");
        public static readonly int FlowFieldCellSizeId = Shader.PropertyToID("flowFieldCellSize");
        /// <summary>Per-team flow configuration. Replaced the attacker*/defender* uniform pairs.</summary>
        public static readonly int TeamFlowParamsReadBufferId = Shader.PropertyToID("teamFlowParamsReadBuffer");
        /// <summary>Which team the flow kernels rebuild this dispatch.</summary>
        public static readonly int FlowTeamIdId = Shader.PropertyToID("flowTeamId");
        public static readonly int StaticObstacleCountId = Shader.PropertyToID("staticObstacleCount");
        public static readonly int StaticObstaclePaddingId = Shader.PropertyToID("staticObstaclePadding");
        public static readonly int StaticObstacleRectsId = Shader.PropertyToID("staticObstacleRects");

        // Runtime dynamic flow
        public static readonly int RuntimeFlowTargetDensityId = Shader.PropertyToID("runtimeFlowTargetDensity");
        public static readonly int RuntimeFlowStatsId = Shader.PropertyToID("runtimeFlowStats");
        public static readonly int RuntimeFlowTargetsId = Shader.PropertyToID("runtimeFlowTargets");
        public static readonly int RuntimeFlowPreviewTextureId = Shader.PropertyToID("runtimeFlowPreviewTexture");
        public static readonly int RuntimeFlowPreviewModeId = Shader.PropertyToID("runtimeFlowPreviewMode");
        public static readonly int FlowPreviewEnabledId = Shader.PropertyToID("flowPreviewEnabled");

        // Density map
        public static readonly int DensityMapId = Shader.PropertyToID("densityMap");
        public static readonly int DensityMapWriteId = Shader.PropertyToID("densityMapWrite");
        public static readonly int AttackerDensityMapId = Shader.PropertyToID("attackerDensityMap");
        public static readonly int DefenderDensityMapId = Shader.PropertyToID("defenderDensityMap");
        public static readonly int AttackerDensityMapWriteId = Shader.PropertyToID("attackerDensityMapWrite");
        public static readonly int DefenderDensityMapWriteId = Shader.PropertyToID("defenderDensityMapWrite");
        public static readonly int EngagementSlotAssignmentBufferId = Shader.PropertyToID("engagementSlotAssignmentBuffer");
        public static readonly int EngagementSlotOccupancyBufferId = Shader.PropertyToID("engagementSlotOccupancyBuffer");
        public static readonly int EngagementSlotOccupancyReadBufferId = Shader.PropertyToID("engagementSlotOccupancyReadBuffer");

        // LOD classification (per unit type)
        public static readonly int ClassifyUnitTypeIndexId = Shader.PropertyToID("classifyUnitTypeIndex");
        public static readonly int NearVisibleAgentIndicesId = Shader.PropertyToID("nearVisibleAgentIndices");
        public static readonly int MidVisibleAgentIndicesId = Shader.PropertyToID("midVisibleAgentIndices");
        public static readonly int FarVisibleAgentIndicesId = Shader.PropertyToID("farVisibleAgentIndices");

        // Render path
        public static readonly int VisibleAgentIndicesId = Shader.PropertyToID("visibleAgentIndices");
        public static readonly int VATPosTexId = Shader.PropertyToID("_VATPosTex");
        public static readonly int VATNormTexId = Shader.PropertyToID("_VATNormTex");
        public static readonly int VATTexWidthId = Shader.PropertyToID("_VATTexWidth");
        public static readonly int VATTexHeightId = Shader.PropertyToID("_VATTexHeight");
        public static readonly int VATFrameCountId = Shader.PropertyToID("_VATFrameCount");
        public static readonly int VATRowsPerFrameId = Shader.PropertyToID("_VATRowsPerFrame");
        public static readonly int VATFrameRateId = Shader.PropertyToID("_VATFrameRate");
        public static readonly int IdleClipStartFrameId = Shader.PropertyToID("_IdleClipStartFrame");
        public static readonly int IdleClipFrameCountId = Shader.PropertyToID("_IdleClipFrameCount");
        public static readonly int IdleClipFrameRateId = Shader.PropertyToID("_IdleClipFrameRate");
        public static readonly int MoveClipStartFrameId = Shader.PropertyToID("_MoveClipStartFrame");
        public static readonly int MoveClipFrameCountId = Shader.PropertyToID("_MoveClipFrameCount");
        public static readonly int MoveClipFrameRateId = Shader.PropertyToID("_MoveClipFrameRate");
        public static readonly int AttackClipStartFrameId = Shader.PropertyToID("_AttackClipStartFrame");
        public static readonly int AttackClipFrameCountId = Shader.PropertyToID("_AttackClipFrameCount");
        public static readonly int AttackClipFrameRateId = Shader.PropertyToID("_AttackClipFrameRate");
        public static readonly int DeathClipStartFrameId = Shader.PropertyToID("_DeathClipStartFrame");
        public static readonly int DeathClipFrameCountId = Shader.PropertyToID("_DeathClipFrameCount");
        public static readonly int DeathClipFrameRateId = Shader.PropertyToID("_DeathClipFrameRate");
    }
}
