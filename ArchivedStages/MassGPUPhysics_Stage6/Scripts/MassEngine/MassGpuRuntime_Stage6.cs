using UnityEngine;
using UnityEngine.Rendering;

using DefenderMovementMode = GPUInstancingManager_Stage6.DefenderMovementMode;
using FlowFieldPreviewSnapshot = GPUInstancingManager_Stage6.FlowFieldPreviewSnapshot;
using TeamCombatSettings = GPUInstancingManager_Stage6.TeamCombatSettings;

public sealed partial class MassGpuRuntime_Stage6
{
    private readonly GPUInstancingManager_Stage6 owner;
    private readonly MassGpuRuntimeContext_Stage6 context = new MassGpuRuntimeContext_Stage6();
    private readonly MassGpuBufferSet_Stage6 buffers = new MassGpuBufferSet_Stage6();
    private readonly MassGpuDispatchScheduler_Stage6 dispatchScheduler = new MassGpuDispatchScheduler_Stage6();

    public MassGpuRuntime_Stage6(GPUInstancingManager_Stage6 owner)
    {
        this.owner = owner;
    }

    public FlowFieldPreviewSnapshot FlowFieldPreview => context.flowFieldPreview;

    public void Initialize()
    {
        MigrateLegacyTeamSettingsIfNeeded();

        bool scenarioApplied = false;
        if (scenarioConfig != null && scenarioConfig.autoApplyOnStart)
        {
            ApplyScenarioConfig();
            scenarioApplied = true;
        }

        if (!scenarioApplied && applyConfigAssetsOnStart)
            ApplyConfigAssetsToManager();

        attackerSettings.Normalize();
        defenderSettings.Normalize();
        InitializeBuffers();

        if (scenarioConfig != null && scenarioConfig.autoStartBattle)
            StartBattle();
    }

    public void Tick()
    {
        Update();
    }

    public void Release()
    {
        ReleaseBuffers();
    }

    public void StartBattle()
    {
        battleStarted = true;
        nextDynamicFlowUpdateTime = Time.time;
        nextDefenderDynamicFlowUpdateTime = Time.time;
    }

    public void StopBattle()
    {
        battleStarted = false;
        runtimeDynamicAttackerFlowActive = false;
        runtimeDynamicDefenderFlowActive = false;
        RestorePaintedAttackerFlowField("Battle stopped; attacker flow field restored to painted fallback.");
        RestorePaintedDefenderFlowField("Battle stopped; defender flow field restored to painted fallback.");
    }

    public void ResetBattleStarted()
    {
        battleStarted = false;
        runtimeDynamicAttackerFlowActive = false;
        runtimeDynamicDefenderFlowActive = false;
        RestorePaintedAttackerFlowField("Battle reset; attacker flow field restored to painted fallback.");
        RestorePaintedDefenderFlowField("Battle reset; defender flow field restored to painted fallback.");
    }

    public void ResetScenario()
    {
        battleStarted = false;
        runtimeDynamicAttackerFlowActive = false;
        runtimeDynamicDefenderFlowActive = false;
        ResetTelemetryState();
        ReleaseBuffers();
        Initialize();
        Debug.Log("[GPUInstancingManager_Stage6] Scenario reset: buffers released, re-initialized, all units respawned.");
    }

    public void MigrateLegacyTeamSettingsIfNeeded()
    {
        if (splitTeamSettingsInitialized)
            return;

        attackerSettings = TeamCombatSettings.Create(
            attackerSpawnCenter,
            attackerSpawnSize,
            targetAcquireRadius,
            attackRange,
            attackDamage,
            attackInterval,
            maxHp,
            maxSpeed,
            agentRadius,
            separationStrength,
            velocityDamping);

        defenderSettings = TeamCombatSettings.Create(
            defenderSpawnCenter,
            defenderSpawnSize,
            Mathf.Max(0.1f, defenderAggroRadius),
            attackRange,
            attackDamage,
            attackInterval,
            maxHp,
            maxSpeed,
            agentRadius,
            separationStrength,
            velocityDamping);

        splitTeamSettingsInitialized = true;
    }

    private float AnimationDuration => runtimeVatFrameCount / Mathf.Max(runtimeVatFrameRate, 0.0001f);
    private int FlowFieldThreadGroupsX => Mathf.CeilToInt(Mathf.Max(1, flowFieldResolutionX * flowFieldResolutionZ) / 64f);
    private int DefenderFlowFieldThreadGroupsX => Mathf.CeilToInt(Mathf.Max(1, defenderFlowFieldResolutionX * defenderFlowFieldResolutionZ) / 64f);

    private Stage6ScenarioConfig_Stage6 scenarioConfig { get => owner.scenarioConfig; set => owner.scenarioConfig = value; }
    private bool applyConfigAssetsOnStart { get => owner.applyConfigAssetsOnStart; set => owner.applyConfigAssetsOnStart = value; }
    private bool applyConfigUnitCounts { get => owner.applyConfigUnitCounts; set => owner.applyConfigUnitCounts = value; }
    private Stage6TeamConfig_Stage6 attackerTeamConfig { get => owner.attackerTeamConfig; set => owner.attackerTeamConfig = value; }
    private Stage6TeamConfig_Stage6 defenderTeamConfig { get => owner.defenderTeamConfig; set => owner.defenderTeamConfig = value; }
    private int instanceCount { get => owner.instanceCount; set => owner.instanceCount = value; }
    private VATProfile_Stage5 vatProfile { get => owner.vatProfile; set => owner.vatProfile = value; }
    private Mesh instanceMesh { get => owner.instanceMesh; set => owner.instanceMesh = value; }
    private Material instanceMaterial { get => owner.instanceMaterial; set => owner.instanceMaterial = value; }
    private ComputeShader computeShader { get => owner.computeShader; set => owner.computeShader = value; }
    private ComputeShader spatialHashShader { get => owner.spatialHashShader; set => owner.spatialHashShader = value; }
    private ComputeShader runtimeFlowShader { get => owner.runtimeFlowShader; set => owner.runtimeFlowShader = value; }
    private ComputeShader combatSimulationShader { get => owner.combatSimulationShader; set => owner.combatSimulationShader = value; }
    private ComputeShader lodClassificationShader { get => owner.lodClassificationShader; set => owner.lodClassificationShader = value; }
    private Mesh midInstanceMesh { get => owner.midInstanceMesh; set => owner.midInstanceMesh = value; }
    private Mesh farInstanceMesh { get => owner.farInstanceMesh; set => owner.farInstanceMesh = value; }
    private Material midInstanceMaterial { get => owner.midInstanceMaterial; set => owner.midInstanceMaterial = value; }
    private Material farInstanceMaterial { get => owner.farInstanceMaterial; set => owner.farInstanceMaterial = value; }
    private VATProfile_Stage5 defenderVatProfile { get => owner.defenderVatProfile; set => owner.defenderVatProfile = value; }
    private Mesh defenderInstanceMesh { get => owner.defenderInstanceMesh; set => owner.defenderInstanceMesh = value; }
    private Mesh defenderMidInstanceMesh { get => owner.defenderMidInstanceMesh; set => owner.defenderMidInstanceMesh = value; }
    private Mesh defenderFarInstanceMesh { get => owner.defenderFarInstanceMesh; set => owner.defenderFarInstanceMesh = value; }
    private Material defenderInstanceMaterial { get => owner.defenderInstanceMaterial; set => owner.defenderInstanceMaterial = value; }
    private Material defenderMidInstanceMaterial { get => owner.defenderMidInstanceMaterial; set => owner.defenderMidInstanceMaterial = value; }
    private Material defenderFarInstanceMaterial { get => owner.defenderFarInstanceMaterial; set => owner.defenderFarInstanceMaterial = value; }
    private Vector3 spawnArea { get => owner.spawnArea; set => owner.spawnArea = value; }
    private bool spawnClusterForCollisionDemo { get => owner.spawnClusterForCollisionDemo; set => owner.spawnClusterForCollisionDemo = value; }
    private float clusteredSpawnRadius { get => owner.clusteredSpawnRadius; set => owner.clusteredSpawnRadius = value; }
    private bool enableTwoTeamCombat { get => owner.enableTwoTeamCombat; set => owner.enableTwoTeamCombat = value; }
    private bool battleStarted { get => owner.battleStarted; set => owner.battleStarted = value; }
    private DefenderMovementMode defenderMovementMode { get => owner.defenderMovementMode; set => owner.defenderMovementMode = value; }
    private int attackerCount { get => owner.attackerCount; set => owner.attackerCount = value; }
    private TeamCombatSettings attackerSettings { get => owner.attackerSettings; set => owner.attackerSettings = value; }
    private TeamCombatSettings defenderSettings { get => owner.defenderSettings; set => owner.defenderSettings = value; }
    private bool splitTeamSettingsInitialized { get => owner.splitTeamSettingsInitialized; set => owner.splitTeamSettingsInitialized = value; }
    private Vector3 attackerSpawnCenter { get => owner.attackerSpawnCenter; set => owner.attackerSpawnCenter = value; }
    private Vector3 attackerSpawnSize { get => owner.attackerSpawnSize; set => owner.attackerSpawnSize = value; }
    private Vector3 defenderSpawnCenter { get => owner.defenderSpawnCenter; set => owner.defenderSpawnCenter = value; }
    private Vector3 defenderSpawnSize { get => owner.defenderSpawnSize; set => owner.defenderSpawnSize = value; }
    private float targetAcquireRadius { get => owner.targetAcquireRadius; set => owner.targetAcquireRadius = value; }
    private float attackRange { get => owner.attackRange; set => owner.attackRange = value; }
    private int attackDamage { get => owner.attackDamage; set => owner.attackDamage = value; }
    private float attackInterval { get => owner.attackInterval; set => owner.attackInterval = value; }
    private int maxHp { get => owner.maxHp; set => owner.maxHp = value; }
    private float defenderGuardRadius { get => owner.defenderGuardRadius; set => owner.defenderGuardRadius = value; }
    private float defenderAggroRadius { get => owner.defenderAggroRadius; set => owner.defenderAggroRadius = value; }
    private float defenderMaxChaseDistance { get => owner.defenderMaxChaseDistance; set => owner.defenderMaxChaseDistance = value; }
    private float deathClipDuration { get => owner.deathClipDuration; set => owner.deathClipDuration = value; }
    private float cellSize { get => owner.cellSize; set => owner.cellSize = value; }
    private int maxAgentsPerCell { get => owner.maxAgentsPerCell; set => owner.maxAgentsPerCell = value; }
    private float agentRadius { get => owner.agentRadius; set => owner.agentRadius = value; }
    private float separationStrength { get => owner.separationStrength; set => owner.separationStrength = value; }
    private float velocityDamping { get => owner.velocityDamping; set => owner.velocityDamping = value; }
    private float maxSpeed { get => owner.maxSpeed; set => owner.maxSpeed = value; }
    private Vector2 simulationWorldSize { get => owner.simulationWorldSize; set => owner.simulationWorldSize = value; }
    private bool autoSizeSimulationWorldForTwoTeamCombat { get => owner.autoSizeSimulationWorldForTwoTeamCombat; set => owner.autoSizeSimulationWorldForTwoTeamCombat = value; }
    private float combatSimulationBoundsPadding { get => owner.combatSimulationBoundsPadding; set => owner.combatSimulationBoundsPadding = value; }
    private float boundaryPadding { get => owner.boundaryPadding; set => owner.boundaryPadding = value; }
    private float shadowCastingRadius { get => owner.shadowCastingRadius; set => owner.shadowCastingRadius = value; }
    private float midLodRadius { get => owner.midLodRadius; set => owner.midLodRadius = value; }
    private Transform lodCenter { get => owner.lodCenter; set => owner.lodCenter = value; }
    private bool enableFrustumCulling { get => owner.enableFrustumCulling; set => owner.enableFrustumCulling = value; }
    private Camera cullingCamera { get => owner.cullingCamera; set => owner.cullingCamera = value; }
    private float cullingRadius { get => owner.cullingRadius; set => owner.cullingRadius = value; }
    private int nearAnimationInterval { get => owner.nearAnimationInterval; set => owner.nearAnimationInterval = value; }
    private int midAnimationInterval { get => owner.midAnimationInterval; set => owner.midAnimationInterval = value; }
    private int farAnimationInterval { get => owner.farAnimationInterval; set => owner.farAnimationInterval = value; }
    private Vector2 idleClipFrameRange { get => owner.idleClipFrameRange; set => owner.idleClipFrameRange = value; }
    private Vector2 moveClipFrameRange { get => owner.moveClipFrameRange; set => owner.moveClipFrameRange = value; }
    private Vector2 attackClipFrameRange { get => owner.attackClipFrameRange; set => owner.attackClipFrameRange = value; }
    private Vector2 deathClipFrameRange { get => owner.deathClipFrameRange; set => owner.deathClipFrameRange = value; }
    private float idleClipFrameRate { get => owner.idleClipFrameRate; set => owner.idleClipFrameRate = value; }
    private float moveClipFrameRate { get => owner.moveClipFrameRate; set => owner.moveClipFrameRate = value; }
    private float attackClipFrameRate { get => owner.attackClipFrameRate; set => owner.attackClipFrameRate = value; }
    private float deathClipFrameRate { get => owner.deathClipFrameRate; set => owner.deathClipFrameRate = value; }
    private bool enableFlowFieldNavigation { get => owner.enableFlowFieldNavigation; set => owner.enableFlowFieldNavigation = value; }
    private float flowFieldCellSize { get => owner.flowFieldCellSize; set => owner.flowFieldCellSize = value; }
    private float flowFieldResponsiveness { get => owner.flowFieldResponsiveness; set => owner.flowFieldResponsiveness = value; }
    private float flowFieldWeight { get => owner.flowFieldWeight; set => owner.flowFieldWeight = value; }
    private PaintedFlowFieldAsset_Stage6 paintedFlowFieldAsset { get => owner.paintedFlowFieldAsset; set => owner.paintedFlowFieldAsset = value; }
    private PaintedFlowFieldAsset_Stage6 defenderPaintedFlowFieldAsset { get => owner.defenderPaintedFlowFieldAsset; set => owner.defenderPaintedFlowFieldAsset = value; }
    private int flowFieldPreviewStride { get => owner.flowFieldPreviewStride; set => owner.flowFieldPreviewStride = value; }
    private bool autoSizeRuntimeAttackerFlowField { get => owner.autoSizeRuntimeAttackerFlowField; set => owner.autoSizeRuntimeAttackerFlowField = value; }
    private float runtimeFlowFieldPadding { get => owner.runtimeFlowFieldPadding; set => owner.runtimeFlowFieldPadding = value; }
    private int runtimeFlowFieldMaxResolution { get => owner.runtimeFlowFieldMaxResolution; set => owner.runtimeFlowFieldMaxResolution = value; }
    private GPUInstancingManager_Stage6.RuntimeFlowPreviewMode runtimeFlowPreviewMode { get => owner.runtimeFlowPreviewMode; set => owner.runtimeFlowPreviewMode = value; }
    private bool enableRuntimeDynamicAttackerFlowField { get => owner.enableRuntimeDynamicAttackerFlowField; set => owner.enableRuntimeDynamicAttackerFlowField = value; }
    private float dynamicFlowUpdateInterval { get => owner.dynamicFlowUpdateInterval; set => owner.dynamicFlowUpdateInterval = value; }
    private int dynamicFlowSectorCount { get => owner.dynamicFlowSectorCount; set => owner.dynamicFlowSectorCount = value; }
    private float dynamicFlowTargetStopRadius { get => owner.dynamicFlowTargetStopRadius; set => owner.dynamicFlowTargetStopRadius = value; }
    private int dynamicFlowMinDefendersPerTarget { get => owner.dynamicFlowMinDefendersPerTarget; set => owner.dynamicFlowMinDefendersPerTarget = value; }
    private bool enableRuntimeDynamicDefenderFlowField { get => owner.enableRuntimeDynamicDefenderFlowField; set => owner.enableRuntimeDynamicDefenderFlowField = value; }
    private bool autoSizeRuntimeDefenderFlowField { get => owner.autoSizeRuntimeDefenderFlowField; set => owner.autoSizeRuntimeDefenderFlowField = value; }
    private float runtimeDefenderFlowFieldPadding { get => owner.runtimeDefenderFlowFieldPadding; set => owner.runtimeDefenderFlowFieldPadding = value; }
    private int runtimeDefenderFlowFieldMaxResolution { get => owner.runtimeDefenderFlowFieldMaxResolution; set => owner.runtimeDefenderFlowFieldMaxResolution = value; }
    private float dynamicDefenderFlowUpdateInterval { get => owner.dynamicDefenderFlowUpdateInterval; set => owner.dynamicDefenderFlowUpdateInterval = value; }
    private int dynamicDefenderFlowSectorCount { get => owner.dynamicDefenderFlowSectorCount; set => owner.dynamicDefenderFlowSectorCount = value; }
    private float dynamicDefenderFlowTargetStopRadius { get => owner.dynamicDefenderFlowTargetStopRadius; set => owner.dynamicDefenderFlowTargetStopRadius = value; }
    private int dynamicDefenderFlowMinAttackersPerTarget { get => owner.dynamicDefenderFlowMinAttackersPerTarget; set => owner.dynamicDefenderFlowMinAttackersPerTarget = value; }

    private Plane[] frustumPlanes => context.frustumPlanes;
    private Vector4[] frustumPlaneVectors => context.frustumPlaneVectors;
    private Mesh runtimeAttackerNearMesh { get => context.runtimeAttackerNearMesh; set => context.runtimeAttackerNearMesh = value; }
    private Mesh runtimeAttackerMidMesh { get => context.runtimeAttackerMidMesh; set => context.runtimeAttackerMidMesh = value; }
    private Mesh runtimeAttackerFarMesh { get => context.runtimeAttackerFarMesh; set => context.runtimeAttackerFarMesh = value; }
    private Mesh runtimeDefenderNearMesh { get => context.runtimeDefenderNearMesh; set => context.runtimeDefenderNearMesh = value; }
    private Mesh runtimeDefenderMidMesh { get => context.runtimeDefenderMidMesh; set => context.runtimeDefenderMidMesh = value; }
    private Mesh runtimeDefenderFarMesh { get => context.runtimeDefenderFarMesh; set => context.runtimeDefenderFarMesh = value; }
    private Mesh runtimeGeneratedFarMesh { get => context.runtimeGeneratedFarMesh; set => context.runtimeGeneratedFarMesh = value; }
    private Material runtimeAttackerNearMaterial { get => context.runtimeAttackerNearMaterial; set => context.runtimeAttackerNearMaterial = value; }
    private Material runtimeAttackerMidMaterial { get => context.runtimeAttackerMidMaterial; set => context.runtimeAttackerMidMaterial = value; }
    private Material runtimeAttackerFarMaterial { get => context.runtimeAttackerFarMaterial; set => context.runtimeAttackerFarMaterial = value; }
    private Material runtimeDefenderNearMaterial { get => context.runtimeDefenderNearMaterial; set => context.runtimeDefenderNearMaterial = value; }
    private Material runtimeDefenderMidMaterial { get => context.runtimeDefenderMidMaterial; set => context.runtimeDefenderMidMaterial = value; }
    private Material runtimeDefenderFarMaterial { get => context.runtimeDefenderFarMaterial; set => context.runtimeDefenderFarMaterial = value; }
    private Bounds renderBounds { get => context.renderBounds; set => context.renderBounds = value; }
    private MassGpuShaderSet_Stage6 kernels { get => context.kernels; set => context.kernels = value; }
    private int agentThreadGroupsX { get => context.agentThreadGroupsX; set => context.agentThreadGroupsX = value; }
    private int gridThreadGroupsX { get => context.gridThreadGroupsX; set => context.gridThreadGroupsX = value; }
    private float runtimeVatFrameCount { get => context.runtimeVatFrameCount; set => context.runtimeVatFrameCount = value; }
    private float runtimeVatFrameRate { get => context.runtimeVatFrameRate; set => context.runtimeVatFrameRate = value; }
    private int gridResolutionX { get => context.gridResolutionX; set => context.gridResolutionX = value; }
    private int gridResolutionZ { get => context.gridResolutionZ; set => context.gridResolutionZ = value; }
    private int gridCellCount { get => context.gridCellCount; set => context.gridCellCount = value; }
    private Vector2 activeWorldSize { get => context.activeWorldSize; set => context.activeWorldSize = value; }
    private Vector2 gridOrigin { get => context.gridOrigin; set => context.gridOrigin = value; }
    private int flowFieldResolutionX { get => context.flowFieldResolutionX; set => context.flowFieldResolutionX = value; }
    private int flowFieldResolutionZ { get => context.flowFieldResolutionZ; set => context.flowFieldResolutionZ = value; }
    private Vector2 flowFieldOrigin { get => context.flowFieldOrigin; set => context.flowFieldOrigin = value; }
    private float activeFlowFieldCellSize { get => context.activeFlowFieldCellSize; set => context.activeFlowFieldCellSize = value; }
    private int defenderFlowFieldResolutionX { get => context.defenderFlowFieldResolutionX; set => context.defenderFlowFieldResolutionX = value; }
    private int defenderFlowFieldResolutionZ { get => context.defenderFlowFieldResolutionZ; set => context.defenderFlowFieldResolutionZ = value; }
    private Vector2 defenderFlowFieldOrigin { get => context.defenderFlowFieldOrigin; set => context.defenderFlowFieldOrigin = value; }
    private float activeDefenderFlowFieldCellSize { get => context.activeDefenderFlowFieldCellSize; set => context.activeDefenderFlowFieldCellSize = value; }
    private float nextDynamicFlowUpdateTime { get => context.nextDynamicFlowUpdateTime; set => context.nextDynamicFlowUpdateTime = value; }
    private float nextDefenderDynamicFlowUpdateTime { get => context.nextDefenderDynamicFlowUpdateTime; set => context.nextDefenderDynamicFlowUpdateTime = value; }
    private bool runtimeDynamicAttackerFlowActive { get => context.runtimeDynamicAttackerFlowActive; set => context.runtimeDynamicAttackerFlowActive = value; }
    private bool runtimeDynamicDefenderFlowActive { get => context.runtimeDynamicDefenderFlowActive; set => context.runtimeDynamicDefenderFlowActive = value; }
    private float lastRuntimeDynamicFlowUpdateTime { get => context.lastRuntimeDynamicFlowUpdateTime; set => context.lastRuntimeDynamicFlowUpdateTime = value; }
    private float lastRuntimeDynamicDefenderFlowUpdateTime { get => context.lastRuntimeDynamicDefenderFlowUpdateTime; set => context.lastRuntimeDynamicDefenderFlowUpdateTime = value; }

    private ComputeBuffer agentBuffer { get => buffers.agentBuffer; set => buffers.agentBuffer = value; }
    private ComputeBuffer agentPositionReadBuffer { get => buffers.agentPositionReadBuffer; set => buffers.agentPositionReadBuffer = value; }
    private ComputeBuffer agentPositionWriteBuffer { get => buffers.agentPositionWriteBuffer; set => buffers.agentPositionWriteBuffer = value; }
    private ComputeBuffer gridCountsBuffer { get => buffers.gridCountsBuffer; set => buffers.gridCountsBuffer = value; }
    private ComputeBuffer gridAgentIndicesBuffer { get => buffers.gridAgentIndicesBuffer; set => buffers.gridAgentIndicesBuffer = value; }
    private ComputeBuffer flowFieldDirectionsBuffer { get => buffers.flowFieldDirectionsBuffer; set => buffers.flowFieldDirectionsBuffer = value; }
    private ComputeBuffer defenderFlowFieldDirectionsBuffer { get => buffers.defenderFlowFieldDirectionsBuffer; set => buffers.defenderFlowFieldDirectionsBuffer = value; }
    private ComputeBuffer runtimeAttackerTargetDensityBuffer { get => buffers.runtimeAttackerTargetDensityBuffer; set => buffers.runtimeAttackerTargetDensityBuffer = value; }
    private ComputeBuffer runtimeAttackerFlowStatsBuffer { get => buffers.runtimeAttackerFlowStatsBuffer; set => buffers.runtimeAttackerFlowStatsBuffer = value; }
    private ComputeBuffer runtimeAttackerFlowTargetsBuffer { get => buffers.runtimeAttackerFlowTargetsBuffer; set => buffers.runtimeAttackerFlowTargetsBuffer = value; }
    private ComputeBuffer runtimeDefenderTargetDensityBuffer { get => buffers.runtimeDefenderTargetDensityBuffer; set => buffers.runtimeDefenderTargetDensityBuffer = value; }
    private ComputeBuffer runtimeDefenderFlowStatsBuffer { get => buffers.runtimeDefenderFlowStatsBuffer; set => buffers.runtimeDefenderFlowStatsBuffer = value; }
    private ComputeBuffer runtimeDefenderFlowTargetsBuffer { get => buffers.runtimeDefenderFlowTargetsBuffer; set => buffers.runtimeDefenderFlowTargetsBuffer = value; }
    private ComputeBuffer teamIdBuffer { get => buffers.teamIdBuffer; set => buffers.teamIdBuffer = value; }
    private ComputeBuffer hpBuffer { get => buffers.hpBuffer; set => buffers.hpBuffer = value; }
    private ComputeBuffer targetAgentIndexBuffer { get => buffers.targetAgentIndexBuffer; set => buffers.targetAgentIndexBuffer = value; }
    private ComputeBuffer attackCooldownBuffer { get => buffers.attackCooldownBuffer; set => buffers.attackCooldownBuffer = value; }
    private ComputeBuffer homePositionBuffer { get => buffers.homePositionBuffer; set => buffers.homePositionBuffer = value; }
    private ComputeBuffer pendingDamageReadBuffer { get => buffers.pendingDamageReadBuffer; set => buffers.pendingDamageReadBuffer = value; }
    private ComputeBuffer pendingDamageWriteBuffer { get => buffers.pendingDamageWriteBuffer; set => buffers.pendingDamageWriteBuffer = value; }
    private ComputeBuffer nearAttackerAgentIndexBuffer { get => buffers.nearAttackerAgentIndexBuffer; set => buffers.nearAttackerAgentIndexBuffer = value; }
    private ComputeBuffer midAttackerAgentIndexBuffer { get => buffers.midAttackerAgentIndexBuffer; set => buffers.midAttackerAgentIndexBuffer = value; }
    private ComputeBuffer farAttackerAgentIndexBuffer { get => buffers.farAttackerAgentIndexBuffer; set => buffers.farAttackerAgentIndexBuffer = value; }
    private ComputeBuffer nearDefenderAgentIndexBuffer { get => buffers.nearDefenderAgentIndexBuffer; set => buffers.nearDefenderAgentIndexBuffer = value; }
    private ComputeBuffer midDefenderAgentIndexBuffer { get => buffers.midDefenderAgentIndexBuffer; set => buffers.midDefenderAgentIndexBuffer = value; }
    private ComputeBuffer farDefenderAgentIndexBuffer { get => buffers.farDefenderAgentIndexBuffer; set => buffers.farDefenderAgentIndexBuffer = value; }
    private ComputeBuffer nearAttackerArgsBuffer { get => buffers.nearAttackerArgsBuffer; set => buffers.nearAttackerArgsBuffer = value; }
    private ComputeBuffer midAttackerArgsBuffer { get => buffers.midAttackerArgsBuffer; set => buffers.midAttackerArgsBuffer = value; }
    private ComputeBuffer farAttackerArgsBuffer { get => buffers.farAttackerArgsBuffer; set => buffers.farAttackerArgsBuffer = value; }
    private ComputeBuffer nearDefenderArgsBuffer { get => buffers.nearDefenderArgsBuffer; set => buffers.nearDefenderArgsBuffer = value; }
    private ComputeBuffer midDefenderArgsBuffer { get => buffers.midDefenderArgsBuffer; set => buffers.midDefenderArgsBuffer = value; }
    private ComputeBuffer farDefenderArgsBuffer { get => buffers.farDefenderArgsBuffer; set => buffers.farDefenderArgsBuffer = value; }
    private MaterialPropertyBlock nearAttackerPropertyBlock { get => buffers.nearAttackerPropertyBlock; set => buffers.nearAttackerPropertyBlock = value; }
    private MaterialPropertyBlock midAttackerPropertyBlock { get => buffers.midAttackerPropertyBlock; set => buffers.midAttackerPropertyBlock = value; }
    private MaterialPropertyBlock farAttackerPropertyBlock { get => buffers.farAttackerPropertyBlock; set => buffers.farAttackerPropertyBlock = value; }
    private MaterialPropertyBlock nearDefenderPropertyBlock { get => buffers.nearDefenderPropertyBlock; set => buffers.nearDefenderPropertyBlock = value; }
    private MaterialPropertyBlock midDefenderPropertyBlock { get => buffers.midDefenderPropertyBlock; set => buffers.midDefenderPropertyBlock = value; }
    private MaterialPropertyBlock farDefenderPropertyBlock { get => buffers.farDefenderPropertyBlock; set => buffers.farDefenderPropertyBlock = value; }
    private RenderTexture runtimeAttackerFlowPreviewTexture { get => buffers.runtimeAttackerFlowPreviewTexture; set => buffers.runtimeAttackerFlowPreviewTexture = value; }
    private RenderTexture runtimeDefenderFlowPreviewTexture { get => buffers.runtimeDefenderFlowPreviewTexture; set => buffers.runtimeDefenderFlowPreviewTexture = value; }
}
