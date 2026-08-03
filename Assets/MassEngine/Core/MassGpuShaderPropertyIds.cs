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
        public static readonly int LocalTargetSearchCellRadiusId = Shader.PropertyToID("localTargetSearchCellRadius");
        public static readonly int DefenderGuardRadiusId = Shader.PropertyToID("defenderGuardRadius");
        public static readonly int DefenderMovementModeId = Shader.PropertyToID("defenderMovementMode");

        // Combat buffers
        public static readonly int TeamIdReadBufferId = Shader.PropertyToID("teamIdReadBuffer");
        public static readonly int HpBufferId = Shader.PropertyToID("hpBuffer");
        public static readonly int HpReadBufferId = Shader.PropertyToID("hpReadBuffer");
        public static readonly int TargetAgentIndexBufferId = Shader.PropertyToID("targetAgentIndexBuffer");
        public static readonly int AttackCooldownBufferId = Shader.PropertyToID("attackCooldownBuffer");
        public static readonly int HomePositionReadBufferId = Shader.PropertyToID("homePositionReadBuffer");
        public static readonly int PendingDamageBufferId = Shader.PropertyToID("pendingDamageBuffer");
        public static readonly int PendingDamageReadBufferId = Shader.PropertyToID("pendingDamageReadBuffer");

        // Flow fields
        public static readonly int FlowFieldDirectionsId = Shader.PropertyToID("flowFieldDirections");
        public static readonly int FlowFieldEnabledId = Shader.PropertyToID("flowFieldEnabled");
        public static readonly int FlowFieldResolutionId = Shader.PropertyToID("flowFieldResolution");
        public static readonly int FlowFieldOriginId = Shader.PropertyToID("flowFieldOrigin");
        public static readonly int FlowFieldCellSizeId = Shader.PropertyToID("flowFieldCellSize");
        public static readonly int AttackerFlowTargetModeId = Shader.PropertyToID("attackerFlowTargetMode");
        public static readonly int AttackerFlowTargetPointId = Shader.PropertyToID("attackerFlowTargetPoint");
        public static readonly int AttackerFlowTargetAreaId = Shader.PropertyToID("attackerFlowTargetArea");
        public static readonly int DefenderFlowTargetModeId = Shader.PropertyToID("defenderFlowTargetMode");
        public static readonly int DefenderFlowTargetPointId = Shader.PropertyToID("defenderFlowTargetPoint");
        public static readonly int DefenderFlowTargetAreaId = Shader.PropertyToID("defenderFlowTargetArea");
        public static readonly int DefenderFlowFieldDirectionsId = Shader.PropertyToID("defenderFlowFieldDirections");
        public static readonly int DefenderFlowFieldEnabledId = Shader.PropertyToID("defenderFlowFieldEnabled");
        public static readonly int DefenderFlowFieldResolutionId = Shader.PropertyToID("defenderFlowFieldResolution");
        public static readonly int DefenderFlowFieldOriginId = Shader.PropertyToID("defenderFlowFieldOrigin");
        public static readonly int DefenderFlowFieldCellSizeId = Shader.PropertyToID("defenderFlowFieldCellSize");

        // Runtime dynamic flow
        public static readonly int RuntimeAttackerTargetDensityId = Shader.PropertyToID("runtimeAttackerTargetDensity");
        public static readonly int RuntimeAttackerFlowStatsId = Shader.PropertyToID("runtimeAttackerFlowStats");
        public static readonly int RuntimeAttackerFlowTargetsId = Shader.PropertyToID("runtimeAttackerFlowTargets");
        public static readonly int RuntimeAttackerFlowPreviewTextureId = Shader.PropertyToID("runtimeAttackerFlowPreviewTexture");
        public static readonly int RuntimeDefenderTargetDensityId = Shader.PropertyToID("runtimeDefenderTargetDensity");
        public static readonly int RuntimeDefenderFlowStatsId = Shader.PropertyToID("runtimeDefenderFlowStats");
        public static readonly int RuntimeDefenderFlowTargetsId = Shader.PropertyToID("runtimeDefenderFlowTargets");
        public static readonly int RuntimeDefenderFlowPreviewTextureId = Shader.PropertyToID("runtimeDefenderFlowPreviewTexture");
        public static readonly int RuntimeFlowPreviewModeId = Shader.PropertyToID("runtimeFlowPreviewMode");
        public static readonly int FlowPreviewEnabledId = Shader.PropertyToID("flowPreviewEnabled");
        public static readonly int RuntimeDynamicAttackerFlowEnabledId = Shader.PropertyToID("runtimeDynamicAttackerFlowEnabled");
        public static readonly int RuntimeDynamicDefenderFlowEnabledId = Shader.PropertyToID("runtimeDynamicDefenderFlowEnabled");
        public static readonly int DynamicFlowSectorCountId = Shader.PropertyToID("dynamicFlowSectorCount");
        public static readonly int DynamicFlowTargetStopRadiusId = Shader.PropertyToID("dynamicFlowTargetStopRadius");
        public static readonly int DynamicFlowMinDefendersPerTargetId = Shader.PropertyToID("dynamicFlowMinDefendersPerTarget");
        public static readonly int DynamicDefenderFlowSectorCountId = Shader.PropertyToID("dynamicDefenderFlowSectorCount");
        public static readonly int DynamicDefenderFlowTargetStopRadiusId = Shader.PropertyToID("dynamicDefenderFlowTargetStopRadius");
        public static readonly int DynamicDefenderFlowMinAttackersPerTargetId = Shader.PropertyToID("dynamicDefenderFlowMinAttackersPerTarget");

        // Density map
        public static readonly int DensityMapId = Shader.PropertyToID("densityMap");
        public static readonly int DensityMapWriteId = Shader.PropertyToID("densityMapWrite");

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
