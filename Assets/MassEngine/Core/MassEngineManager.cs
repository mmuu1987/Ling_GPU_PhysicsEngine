using UnityEngine;

namespace MassEngine
{
    /// <summary>
    /// Scene entry point. Owns the unit type registry, buffer manager, compute pipeline
    /// orchestrator and render dispatcher. Configuration ScriptableObjects are read-only
    /// inputs: runtime state (click targets, resolved VAT data, per-frame settings) lives
    /// on this component and its runtime objects, never in the assets.
    /// </summary>
    public sealed class MassEngineManager : MonoBehaviour
    {
        private struct FlowTargetOverride
        {
            public bool active;
            public Vector3 point;
        }

        private struct TeamNavigationOverride
        {
            public bool active;
            public bool enabled;
            public bool dynamicTargeting;
        }

        private struct AllocationSignature
        {
            public int agentCount;
            public int gridCellCount;
            public int maxAgentsPerCell;
            public int flowFieldResolution;
            public int unitTypeCount;
            // Counts alone cannot see a scenario asset swap or a teamId edit: the GPU
            // teamIdReadBuffer is uploaded once per Initialize, so those edits must
            // change the signature or they silently never reach the GPU.
            public int scenarioConfigId;
            public int teamLayoutHash;

            public bool Equals(in AllocationSignature other)
            {
                return agentCount == other.agentCount &&
                       gridCellCount == other.gridCellCount &&
                       maxAgentsPerCell == other.maxAgentsPerCell &&
                       flowFieldResolution == other.flowFieldResolution &&
                       unitTypeCount == other.unitTypeCount &&
                       scenarioConfigId == other.scenarioConfigId &&
                       teamLayoutHash == other.teamLayoutHash;
            }
        }

        public const int AttackerTeamId = 0;
        public const int DefenderTeamId = 1;

        [Header("Scenario")]
        public ScenarioConfig scenarioConfig;
        public MassEngineSystemConfig systemConfig;
        public MassEngineShaderConfig shaderConfig;

        [Header("Runtime Anchors")]
        public Transform lodCenter;
        public Camera cullingCamera;

        [Header("Runtime")]
        public bool enableGpuDispatch;
        public bool rebuildRuntimeFlowEveryFrame;
        public bool battleStarted;

        private UnitTypeRegistry unitTypeRegistry;
        private ComputePipelineOrchestrator pipelineOrchestrator;
        private MassGpuBufferManager bufferManager;
        private MassGpuRenderDispatcher renderDispatcher;
        private BattleTelemetry telemetry;

        private UnitTypeGpuSettings[] gpuSettingsCache;
        private AllocationSignature allocationSignature;
        private readonly FlowTargetOverride[] flowTargetOverrides = new FlowTargetOverride[2];
        private readonly TeamNavigationOverride[] teamNavigationOverrides = new TeamNavigationOverride[2];
        private readonly bool[] flowFieldDirty = { true, true };
        private readonly int[] lastFlowTargetHash = new int[2];
        private readonly float[] nextDynamicFlowRebuildTime = new float[2];
        private readonly StaticObstacleRect[] activeStaticObstacles = new StaticObstacleRect[StaticObstacleMath.MaxObstacleCount];
        private readonly Vector4[] staticObstacleShaderRects = new Vector4[StaticObstacleMath.MaxObstacleCount];
        private int activeStaticObstacleCount;
        private float activeStaticObstaclePadding;
        private int lastStaticObstacleHash;
        // Non-serialized: a missing-shader block must never mutate the designer's
        // serialized enableGpuDispatch checkbox (fix-and-Reset could not recover).
        private bool gpuDispatchBlockedByShaders;
        private bool battleStateApplied;
        private bool initialized;
        private bool loggedSettingsCacheMismatch;
        private float nextShaderProbeTime;

        private Plane[] cachedFrustumPlanes;
        private Vector4[] cachedFrustumVectors;
        private static readonly Vector4[] EmptyFrustum = new Vector4[0];

        public UnitTypeRegistry UnitTypes { get { return unitTypeRegistry; } }
        public MassGpuBufferManager Buffers { get { return bufferManager; } }
        public BattleTelemetry Telemetry { get { return telemetry; } }
        public bool IsBattleRunning { get { return battleStarted; } }
        public int StaticObstacleCount { get { return activeStaticObstacleCount; } }
        public float StaticObstaclePadding { get { return activeStaticObstaclePadding; } }

        private void OnEnable()
        {
            if (!initialized)
                Initialize();
        }

        private void Update()
        {
            if (pipelineOrchestrator == null || bufferManager == null)
                return;

            // Inspector flips of battleStarted route through the same accounting
            // (telemetry pause/resume, flow dirty) as the StartBattle/StopBattle API.
            if (battleStarted != battleStateApplied)
            {
                if (battleStarted)
                    StartBattle();
                else
                    StopBattle();
            }

            if (!enableGpuDispatch)
                return;

            if (gpuDispatchBlockedByShaders)
            {
                // Cheap throttled probe (four null checks + HasKernel): the moment the
                // shader config is completed at runtime the engine recovers on its own
                // instead of demanding a manual ResetScenario.
                if (Time.unscaledTime >= nextShaderProbeTime)
                {
                    nextShaderProbeTime = Time.unscaledTime + 1f;
                    MassGpuShaderSet probe = MassGpuShaderSet.Find(
                        shaderConfig != null ? shaderConfig.spatialHashShader : null,
                        shaderConfig != null ? shaderConfig.runtimeFlowShader : null,
                        shaderConfig != null ? shaderConfig.combatSimulationShader : null,
                        shaderConfig != null ? shaderConfig.lodClassificationShader : null);
                    if (probe.IsValid)
                    {
                        Debug.Log("MassEngine: shader config completed - reinitializing scenario.", this);
                        Initialize();
                    }
                }
                return;
            }

            // Signature check BEFORE the allocation early-out: an initially empty or
            // invalid scenario must recover the moment its configs become valid.
            if (!CurrentAllocationSignature().Equals(allocationSignature))
            {
                Initialize();
                return;
            }

            if (!bufferManager.IsAllocated)
                return;

            RefreshAndUploadUnitTypeSettings();

            PipelineFrameContext context = CreateFrameContext();
            if (context.attackerFlow.rebuildThisFrame && telemetry != null)
                telemetry.NotifyFlowRebuild(AttackerTeamId);
            if (context.defenderFlow.rebuildThisFrame && telemetry != null)
                telemetry.NotifyFlowRebuild(DefenderTeamId);

            pipelineOrchestrator.DispatchFrame(context);
            renderDispatcher.Draw(unitTypeRegistry, bufferManager, ResolveRenderBounds());

            if (telemetry != null)
            {
                telemetry.Tick(bufferManager, Time.time);
                if (telemetry.DeviceResetSuspected)
                {
                    // The sentinel written at allocation vanished from GPU memory: a
                    // device reset/TDR wiped the buffers. All simulation state lives
                    // only on the GPU, so the sole recovery is a full reinitialize.
                    Debug.LogWarning("MassEngine: GPU buffer sentinel lost (device reset/driver restart suspected) - reinitializing scenario.", this);
                    Initialize();
                    return;
                }
            }
        }

        private void OnDisable()
        {
            Release();
        }

        [ContextMenu("MassEngine/Initialize")]
        public void Initialize()
        {
            if (!Application.isPlaying)
            {
                // Edit-mode allocation would live (leaking GPU memory) until the next
                // domain reload; the context menu is still useful as a config check.
                ValidateConfigsOnly();
                return;
            }

            loggedSearchRadiusClamp = false;
            loggedSettingsCacheMismatch = false;
            gpuDispatchBlockedByShaders = false;

            Release();

            unitTypeRegistry = new UnitTypeRegistry();
            unitTypeRegistry.RegisterFromScenario(scenarioConfig);

            MassGpuShaderSet shaders = MassGpuShaderSet.Find(
                shaderConfig != null ? shaderConfig.spatialHashShader : null,
                shaderConfig != null ? shaderConfig.runtimeFlowShader : null,
                shaderConfig != null ? shaderConfig.combatSimulationShader : null,
                shaderConfig != null ? shaderConfig.lodClassificationShader : null);
            gpuDispatchBlockedByShaders = !shaders.IsValid;
            if (gpuDispatchBlockedByShaders)
                Debug.LogError("MassEngine shader config is incomplete (missing: " + shaders.DescribeMissing() + "); GPU dispatch is blocked until the shaders are assigned and the scenario reinitializes.", this);

            bufferManager = new MassGpuBufferManager();
            pipelineOrchestrator = new ComputePipelineOrchestrator(shaders, bufferManager);
            renderDispatcher = new MassGpuRenderDispatcher();
            telemetry = new BattleTelemetry(shaders.SpatialHashShader);

            if (gpuDispatchBlockedByShaders)
            {
                // Do not allocate the full GPU working set (tens to hundreds of MB at
                // 400k agents) for a pipeline that cannot dispatch; the Update probe
                // reinitializes the moment the shader config becomes valid.
                allocationSignature = CurrentAllocationSignature();
                initialized = true;
                battleStateApplied = battleStarted;
                return;
            }

            // Physics ledger: an out-of-envelope scenario must announce itself with
            // concrete numbers instead of failing as an unexplained frame-rate collapse.
            ScenarioPhysicsReport physicsReport = ScenarioPhysics.Evaluate(
                scenarioConfig != null ? scenarioConfig.unitTypes : null, Simulation, Flow, Lod);
            if (physicsReport.HasIssues)
                Debug.LogWarning(physicsReport.Describe(), this);

            ResolveRenderRuntimes();

            int totalAgents = unitTypeRegistry.TotalAgentCount;
            int gridCellCount = ComputeGridCellCount();
            int unitTypeCount = unitTypeRegistry.UnitTypeCount;

            // Signature is recorded BEFORE allocation: if anything below throws (a user
            // IUnitType override, an out-of-range buffer size), the next frame must not
            // retry the whole Release+Allocate cycle until the configs actually change.
            allocationSignature = CurrentAllocationSignature();

            bufferManager.Allocate(totalAgents, gridCellCount, Simulation.maxAgentsPerCell, Flow.flowFieldResolution, Flow.flowFieldResolution, unitTypeCount);
            unitTypeRegistry.InitializeAll(bufferManager, pipelineOrchestrator);
            UploadInitialAgents();
            bufferManager.ConfigureDrawArgs(unitTypeRegistry.RegisteredTypes);

            gpuSettingsCache = new UnitTypeGpuSettings[unitTypeCount];
            RefreshAndUploadUnitTypeSettings();

            flowFieldDirty[0] = true;
            flowFieldDirty[1] = true;
            nextDynamicFlowRebuildTime[0] = 0f;
            nextDynamicFlowRebuildTime[1] = 0f;
            initialized = true;

            battleStateApplied = battleStarted;
            if (battleStarted && telemetry != null)
                telemetry.NotifyBattleStarted(Time.time);

            if (!enableGpuDispatch)
                Debug.Log("MassEngine: buffers allocated for " + totalAgents + " agents but enableGpuDispatch is OFF - the pipeline will not run until it is enabled.", this);
        }

        public void StartBattle()
        {
            battleStarted = true;
            battleStateApplied = true;
            // Dynamic flow only generates while the battle runs; rebuild immediately
            // instead of waiting out the throttle interval.
            flowFieldDirty[AttackerTeamId] = true;
            flowFieldDirty[DefenderTeamId] = true;
            if (telemetry != null)
                telemetry.NotifyBattleStarted(Time.time);
        }

        public void StopBattle()
        {
            PauseBattle();
            ClearFlowTargetOverride(AttackerTeamId);
            ClearFlowTargetOverride(DefenderTeamId);
            ClearTeamNavigationOverride(AttackerTeamId);
            ClearTeamNavigationOverride(DefenderTeamId);
        }

        /// <summary>
        /// Pauses simulation and telemetry without discarding runtime army orders.
        /// Use StopBattle when orders should be cleared as well.
        /// </summary>
        public void PauseBattle()
        {
            battleStarted = false;
            battleStateApplied = false;
            if (telemetry != null)
                telemetry.NotifyBattleStopped(Time.time);
        }

        public void ResetScenario()
        {
            ClearFlowTargetOverride(AttackerTeamId);
            ClearFlowTargetOverride(DefenderTeamId);
            ClearTeamNavigationOverride(AttackerTeamId);
            ClearTeamNavigationOverride(DefenderTeamId);
            if (telemetry != null)
                telemetry.NotifyReset();
            Initialize();
        }

        /// <summary>
        /// Runtime flow target override for a team (e.g. from a mouse click). Stored on
        /// the manager — configuration assets are never written.
        /// </summary>
        public void SetFlowTargetOverride(int teamId, Vector3 point)
        {
            if (teamId < 0 || teamId >= flowTargetOverrides.Length)
            {
                Debug.LogWarning("MassEngine: SetFlowTargetOverride teamId " + teamId + " is invalid - the engine maintains flow fields for teams 0 and 1 only.", this);
                return;
            }

            flowTargetOverrides[teamId] = new FlowTargetOverride { active = true, point = ResolvePointOutsideStaticObstacles(point) };
            flowFieldDirty[teamId] = true;
        }

        public void ClearFlowTargetOverride(int teamId)
        {
            if (teamId < 0 || teamId >= flowTargetOverrides.Length)
                return;

            flowTargetOverrides[teamId] = default;
            flowFieldDirty[teamId] = true;
        }

        /// <summary>
        /// Overrides one team's navigation doctrine at runtime without mutating
        /// RuntimeFlowConfig. enabled=false holds position; enabled=true routes through
        /// that team's flow field. dynamicTargeting chooses enemy-driven targets when no
        /// explicit point override is active.
        /// </summary>
        public void SetTeamNavigationOverride(int teamId, bool enabled, bool dynamicTargeting)
        {
            if (teamId < 0 || teamId >= teamNavigationOverrides.Length)
            {
                Debug.LogWarning("MassEngine: SetTeamNavigationOverride teamId " + teamId + " is invalid - the engine maintains teams 0 and 1 only.", this);
                return;
            }

            teamNavigationOverrides[teamId] = new TeamNavigationOverride
            {
                active = true,
                enabled = enabled,
                dynamicTargeting = dynamicTargeting
            };
            flowFieldDirty[teamId] = true;
        }

        public void ClearTeamNavigationOverride(int teamId)
        {
            if (teamId < 0 || teamId >= teamNavigationOverrides.Length)
                return;

            teamNavigationOverrides[teamId] = default;
            flowFieldDirty[teamId] = true;
        }

        /// <summary>
        /// Replaces the runtime obstacle set without mutating any ScriptableObject.
        /// Invalid/zero-sized entries are ignored and the GPU contract is capped at 8.
        /// </summary>
        public void SetStaticObstacles(StaticObstacleRect[] obstacles, float padding)
        {
            float safePadding = Mathf.Max(0f, padding);
            int hash = safePadding.GetHashCode();
            int validCount = 0;
            if (obstacles != null)
            {
                for (int i = 0; i < obstacles.Length && validCount < StaticObstacleMath.MaxObstacleCount; i++)
                {
                    StaticObstacleRect obstacle = obstacles[i];
                    if (!obstacle.IsValid)
                        continue;
                    unchecked
                    {
                        hash = hash * 31 + obstacle.center.GetHashCode();
                        hash = hash * 31 + obstacle.size.GetHashCode();
                    }
                    validCount++;
                }
            }

            if (hash == lastStaticObstacleHash && validCount == activeStaticObstacleCount &&
                Mathf.Approximately(safePadding, activeStaticObstaclePadding))
                return;

            lastStaticObstacleHash = hash;
            activeStaticObstaclePadding = safePadding;
            activeStaticObstacleCount = 0;
            for (int i = 0; i < activeStaticObstacles.Length; i++)
            {
                activeStaticObstacles[i] = default;
                staticObstacleShaderRects[i] = Vector4.zero;
            }

            if (obstacles != null)
            {
                for (int i = 0; i < obstacles.Length && activeStaticObstacleCount < StaticObstacleMath.MaxObstacleCount; i++)
                {
                    StaticObstacleRect obstacle = obstacles[i];
                    if (!obstacle.IsValid)
                        continue;
                    activeStaticObstacles[activeStaticObstacleCount] = obstacle;
                    staticObstacleShaderRects[activeStaticObstacleCount] = obstacle.ToShaderRect();
                    activeStaticObstacleCount++;
                }
            }

            flowFieldDirty[AttackerTeamId] = true;
            flowFieldDirty[DefenderTeamId] = true;
        }

        public bool TryGetStaticObstacle(int obstacleIndex, out StaticObstacleRect obstacle)
        {
            obstacle = default;
            if (obstacleIndex < 0 || obstacleIndex >= activeStaticObstacleCount)
                return false;
            obstacle = activeStaticObstacles[obstacleIndex];
            return true;
        }

        public Vector3 ResolvePointOutsideStaticObstacles(Vector3 point)
        {
            // Two passes handle touching/overlapping rectangles without an unbounded loop.
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < activeStaticObstacleCount; i++)
                    point = StaticObstacleMath.ResolvePointOutside(activeStaticObstacles[i], point, activeStaticObstaclePadding);
            }
            return point;
        }

        public void Release()
        {
            if (unitTypeRegistry != null)
                unitTypeRegistry.ReleaseAll();
            unitTypeRegistry = null;

            if (bufferManager != null)
                bufferManager.ReleaseAll();
            bufferManager = null;
            renderDispatcher = null;
            pipelineOrchestrator = null;
            gpuSettingsCache = null;
            initialized = false;
        }

        private void ResolveRenderRuntimes()
        {
            float fallbackDeathClip = RuntimeCombat.deathClipDuration;
            for (int i = 0; i < unitTypeRegistry.RegisteredTypes.Count; i++)
            {
                IUnitType unitType = unitTypeRegistry.RegisteredTypes[i];
                unitType.AttachRenderRuntime(ResolvedUnitTypeRuntime.Resolve(unitType.Config, fallbackDeathClip));
            }
        }

        private void RefreshAndUploadUnitTypeSettings()
        {
            if (unitTypeRegistry == null || bufferManager == null || gpuSettingsCache == null)
                return;

            if (unitTypeRegistry.FillGpuSettings(gpuSettingsCache))
            {
                bufferManager.UploadUnitTypeSettings(gpuSettingsCache);
            }
            else if (!loggedSettingsCacheMismatch)
            {
                loggedSettingsCacheMismatch = true;
                Debug.LogError("MassEngine: settings cache (" + gpuSettingsCache.Length + ") does not match the registry (" + unitTypeRegistry.UnitTypeCount + " unit types); per-frame parameter upload is stalled. Rebuild via ResetScenario()/Initialize().", this);
            }
        }

        private void UploadInitialAgents()
        {
            if (unitTypeRegistry == null || bufferManager == null || !bufferManager.IsAllocated)
                return;

            int total = unitTypeRegistry.TotalAgentCount;
            AgentData[] agents = new AgentData[total];
            int[] teamIds = new int[total];
            int[] hpValues = new int[total];
            int[] unitTypeIndices = new int[total];

            unitTypeRegistry.GenerateAgents(agents);
            unitTypeRegistry.FillCombatArrays(teamIds, hpValues, unitTypeIndices);
            bufferManager.UploadInitialData(agents, teamIds, hpValues, unitTypeIndices);
        }

        private PipelineFrameContext CreateFrameContext()
        {
            int total = unitTypeRegistry != null ? unitTypeRegistry.TotalAgentCount : 0;
            int agentThreadGroups = Mathf.Max(1, Mathf.CeilToInt(total / 64f));
            int gridThreadGroups = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(1, bufferManager != null ? bufferManager.GridCellCount : 1) / 64f));
            int flowResolution = Flow.flowFieldResolution;
            int flowThreadGroups = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(1, flowResolution * flowResolution) / 64f));
            int densityMapThreadGroups = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(1, flowResolution) / 8f));

            return new PipelineFrameContext
            {
                deltaTime = Time.deltaTime,
                frameIndex = Time.frameCount,
                totalAgentCount = total,
                unitTypeCount = unitTypeRegistry != null ? unitTypeRegistry.UnitTypeCount : 0,
                teamCount = bufferManager != null ? bufferManager.teamCount : 2,
                agentThreadGroupsX = agentThreadGroups,
                gridThreadGroupsX = gridThreadGroups,
                battleStarted = battleStarted,
                combatEnabled = true,
                attackerTeamId = AttackerTeamId,
                defenderTeamId = DefenderTeamId,
                // Density pressure feeds crowd motion every frame; the map must stay in
                // sync with agent positions rather than piggybacking on flow rebuilds.
                rebuildDensityMap = total > 0,
                densityMapThreadGroupsX = densityMapThreadGroups,
                densityMapThreadGroupsY = densityMapThreadGroups,
                defenderMovementMode = ResolveTeamFlowEnabled(DefenderTeamId) ? 1 : 0,
                defenderGuardRadius = RuntimeCombat.defenderGuardRadius,
                localTargetSearchCellRadius = ComputeLocalTargetSearchCellRadius(),
                flowPreviewEnabled = Flow.runtimeFlowPreviewEnabled,
                runtimeFlowPreviewMode = (int)Flow.runtimeFlowPreviewMode,
                staticObstacleCount = activeStaticObstacleCount,
                staticObstaclePadding = activeStaticObstaclePadding,
                staticObstacleRects = staticObstacleShaderRects,
                grid = new GridFrameSettings
                {
                    resolutionX = ComputeGridResolutionX(),
                    resolutionZ = ComputeGridResolutionZ(),
                    origin = ComputeGridOrigin(),
                    worldSize = Simulation.simulationWorldSize,
                    cellSize = Simulation.cellSize,
                    maxAgentsPerCell = Simulation.maxAgentsPerCell,
                    boundaryPadding = Simulation.boundaryPadding
                },
                attackerFlow = BuildTeamFlowSettings(AttackerTeamId, flowThreadGroups, flowResolution),
                defenderFlow = BuildTeamFlowSettings(DefenderTeamId, flowThreadGroups, flowResolution),
                teamFlows = BuildAllTeamFlowSettings(flowThreadGroups, flowResolution),
                lod = new LodFrameSettings
                {
                    lodCenterPosition = ResolveLodCenter(),
                    nearLodRadius = Lod.nearLodRadius,
                    midLodRadius = Lod.midLodRadius,
                    cullingRadius = Lod.cullingRadius,
                    maxRenderDistance = Lod.maxRenderDistance,
                    farIncludeDead = Lod.farIncludeDead,
                    frustumPlanes = BuildFrustumPlanes(),
                    nearAnimationInterval = Lod.nearAnimationInterval,
                    midAnimationInterval = Lod.midAnimationInterval,
                    farAnimationInterval = Lod.farAnimationInterval,
                    nearSimulationInterval = Lod.nearSimulationInterval,
                    midSimulationInterval = Lod.midSimulationInterval,
                    farSimulationInterval = Lod.farSimulationInterval
                }
            };
        }

        /// <summary>
        /// Flow rebuild gating, decomposed into its orthogonal parts:
        /// 1. enabled  — is this team's flow field switched on at all;
        /// 2. reason   — a static/override target exists, or dynamic targeting is on;
        /// 3. cadence  — dirty flag (target changed / just initialized) rebuilds now;
        ///               dynamic targeting rebuilds on its configured interval;
        ///               rebuildRuntimeFlowEveryFrame forces every frame.
        /// A purely static target rebuilds only when dirty — its field does not change.
        /// </summary>
        private TeamFlowFrameSettings BuildTeamFlowSettings(int teamId, int flowThreadGroups, int flowResolution)
        {
            bool isAttacker = teamId == AttackerTeamId;
            bool enabled = ResolveTeamFlowEnabled(teamId);
            bool dynamicEnabled = ResolveTeamDynamicTargeting(teamId);
            float dynamicInterval = isAttacker ? Flow.dynamicFlowUpdateInterval : Flow.dynamicDefenderFlowUpdateInterval;

            TeamFlowFrameSettings settings = new TeamFlowFrameSettings
            {
                enabled = enabled,
                dynamicFlowEnabled = dynamicEnabled,
                threadGroupsX = flowThreadGroups,
                resolutionX = flowResolution,
                resolutionZ = flowResolution,
                origin = Flow.flowFieldOrigin,
                cellSize = Flow.flowFieldCellSize,
                sectorCount = isAttacker ? Flow.dynamicFlowSectorCount : Flow.dynamicDefenderFlowSectorCount,
                targetStopRadius = isAttacker ? Flow.dynamicFlowTargetStopRadius : Flow.dynamicDefenderFlowTargetStopRadius,
                minAgentsPerTarget = isAttacker ? Flow.dynamicFlowMinDefendersPerTarget : Flow.dynamicDefenderFlowMinAttackersPerTarget,
                targetMode = 0
            };

            // Target resolution: click override wins over configured target.
            FlowTargetOverride overrideTarget = flowTargetOverrides[teamId];
            bool hasTarget = false;
            if (overrideTarget.active)
            {
                settings.targetMode = 1; // FLOW_TARGET_POINT
                settings.targetPoint = overrideTarget.point;
                hasTarget = true;
            }
            else if (unitTypeRegistry != null && unitTypeRegistry.TryGetConfiguredFlowTarget(teamId, out FlowFieldTarget configured))
            {
                if (configured.mode == FlowFieldTargetMode.Area)
                {
                    settings.targetMode = 2; // FLOW_TARGET_AREA
                    settings.targetAreaCenter = configured.center;
                    settings.targetAreaSize = configured.size;
                }
                else
                {
                    settings.targetMode = 1;
                    settings.targetPoint = configured.center;
                }
                hasTarget = true;
            }

            // A changed resolved target (override set/cleared, module target edited at
            // runtime) marks the field dirty even without an explicit notification.
            int resolvedTargetHash = ComputeTargetHash(settings.targetMode, settings.targetPoint, settings.targetAreaCenter, settings.targetAreaSize);
            if (resolvedTargetHash != lastFlowTargetHash[teamId])
            {
                lastFlowTargetHash[teamId] = resolvedTargetHash;
                flowFieldDirty[teamId] = true;
            }

            if (!enabled)
            {
                settings.rebuildThisFrame = false;
                return settings;
            }

            if (!hasTarget && !dynamicEnabled)
            {
                // No reason to steer — but a dirty field (target just removed) must still
                // dispatch one Generate pass, which now zero-fills the direction field so
                // agents stop marching toward a ghost target.
                settings.rebuildThisFrame = flowFieldDirty[teamId];
                flowFieldDirty[teamId] = false;
                return settings;
            }

            bool rebuild = rebuildRuntimeFlowEveryFrame || flowFieldDirty[teamId];

            // Dynamic targets drift with the battle; refresh on the configured interval.
            // (Dynamic generation only runs on the GPU while the battle is started.)
            if (!rebuild && dynamicEnabled && battleStarted && Time.time >= nextDynamicFlowRebuildTime[teamId])
                rebuild = true;

            if (rebuild)
            {
                flowFieldDirty[teamId] = false;
                nextDynamicFlowRebuildTime[teamId] = Time.time + Mathf.Max(0f, dynamicInterval);
            }

            settings.rebuildThisFrame = rebuild;
            return settings;
        }

        private TeamFlowFrameSettings[] BuildAllTeamFlowSettings(int flowThreadGroups, int flowResolution)
        {
            int count = bufferManager != null ? bufferManager.teamCount : 2;
            TeamFlowFrameSettings[] settings = new TeamFlowFrameSettings[count];
            for (int i = 0; i < count; i++)
            {
                settings[i] = BuildTeamFlowSettings(i, flowThreadGroups, flowResolution);
            }
            return settings;
        }

        private bool ResolveTeamFlowEnabled(int teamId)
        {
            TeamNavigationOverride runtime = teamNavigationOverrides[teamId];
            if (runtime.active)
                return runtime.enabled;

            return teamId == AttackerTeamId ? Flow.flowFieldEnabled : Flow.defenderFlowFieldEnabled;
        }

        private bool ResolveTeamDynamicTargeting(int teamId)
        {
            TeamNavigationOverride runtime = teamNavigationOverrides[teamId];
            if (runtime.active)
                return runtime.dynamicTargeting;

            return teamId == AttackerTeamId
                ? Flow.runtimeDynamicAttackerFlowEnabled
                : Flow.runtimeDynamicDefenderFlowEnabled;
        }

        /// <summary>
        /// Derives the local target search cell radius from the largest configured
        /// acquire radius. Values beyond the shader's hard cap are clamped WITH a warning
        /// instead of being silently truncated.
        /// </summary>
        private static int ComputeTargetHash(int mode, Vector3 point, Vector3 areaCenter, Vector3 areaSize)
        {
            unchecked
            {
                int hash = mode;
                hash = hash * 31 + point.GetHashCode();
                hash = hash * 31 + areaCenter.GetHashCode();
                hash = hash * 31 + areaSize.GetHashCode();
                return hash;
            }
        }

        /// <summary>Edit-mode config check for the context menu (no GPU allocation).</summary>
        private void ValidateConfigsOnly()
        {
            if (scenarioConfig == null || scenarioConfig.unitTypes == null || scenarioConfig.unitTypes.Length == 0)
            {
                Debug.LogWarning("MassEngine: scenarioConfig has no unit types.", this);
                return;
            }

            int validCount = 0;
            for (int i = 0; i < scenarioConfig.unitTypes.Length; i++)
            {
                ValidationResult result = ConfigValidator.Validate(scenarioConfig.unitTypes[i]);
                foreach (string error in result.Errors)
                    Debug.LogError("MassEngine unit type " + i + ": " + error, scenarioConfig.unitTypes[i]);
                foreach (string warning in result.Warnings)
                    Debug.LogWarning("MassEngine unit type " + i + ": " + warning, scenarioConfig.unitTypes[i]);
                if (result.IsValid)
                    validCount++;
            }

            Debug.Log("MassEngine config check: " + validCount + "/" + scenarioConfig.unitTypes.Length + " unit types valid. (GPU initialization runs in Play mode.)", this);

            ScenarioPhysicsReport physicsReport = ScenarioPhysics.Evaluate(scenarioConfig.unitTypes, Simulation, Flow, Lod);
            if (physicsReport.HasIssues)
                Debug.LogWarning(physicsReport.Describe(), this);
            else
                Debug.Log("MassEngine scenario physics check: OK (" + physicsReport.TotalAgents + " agents fit the configured world/grid/flow).", this);
        }

        private int ComputeLocalTargetSearchCellRadius()
        {
            const int shaderCap = 4; // LOCAL_TARGET_SEARCH_MAX_CELL_RADIUS in AgentDataCommon.hlsl
            float maxAcquireRadius = 0f;
            if (gpuSettingsCache != null)
            {
                for (int i = 0; i < gpuSettingsCache.Length; i++)
                    maxAcquireRadius = Mathf.Max(maxAcquireRadius, gpuSettingsCache[i].targetAcquireRadius);
            }

            int neededRadius = Mathf.Max(1, Mathf.CeilToInt(maxAcquireRadius / Mathf.Max(0.1f, Simulation.cellSize)));
            if (neededRadius > shaderCap && !loggedSearchRadiusClamp)
            {
                loggedSearchRadiusClamp = true;
                Debug.LogWarning(
                    "MassEngine targetAcquireRadius " + maxAcquireRadius + " needs a search radius of " + neededRadius +
                    " cells but the shader caps it at " + shaderCap + " (cellSize " + Simulation.cellSize +
                    "); effective acquire distance is limited to ~" + (shaderCap * Simulation.cellSize) + "m axially.", this);
            }

            return Mathf.Min(neededRadius, shaderCap);
        }

        private bool loggedSearchRadiusClamp;

        private Vector3 ResolveLodCenter()
        {
            if (lodCenter != null)
                return lodCenter.position;

            Camera camera = cullingCamera != null ? cullingCamera : Camera.main;
            return camera != null ? camera.transform.position : Vector3.zero;
        }

        private Vector4[] BuildFrustumPlanes()
        {
            if (!Lod.enableFrustumCulling)
                return EmptyFrustum;

            Camera camera = cullingCamera != null ? cullingCamera : Camera.main;
            if (camera == null)
                return EmptyFrustum;

            if (cachedFrustumPlanes == null)
            {
                cachedFrustumPlanes = new Plane[6];
                cachedFrustumVectors = new Vector4[6];
            }

            GeometryUtility.CalculateFrustumPlanes(camera, cachedFrustumPlanes);
            for (int i = 0; i < 6; i++)
            {
                Vector3 normal = cachedFrustumPlanes[i].normal;
                cachedFrustumVectors[i] = new Vector4(normal.x, normal.y, normal.z, cachedFrustumPlanes[i].distance);
            }

            return cachedFrustumVectors;
        }

        private AllocationSignature CurrentAllocationSignature()
        {
            int agentCount = 0;
            int unitTypeCount = 0;
            int teamLayoutHash = 17;
            if (scenarioConfig != null && scenarioConfig.unitTypes != null)
            {
                for (int i = 0; i < scenarioConfig.unitTypes.Length; i++)
                {
                    UnitTypeConfig config = scenarioConfig.unitTypes[i];
                    if (config == null || config.spawnConfig == null || config.spawnConfig.unitCount <= 0)
                        continue;
                    if (config.teamId != 0 && config.teamId != 1)
                        continue;
                    agentCount += config.spawnConfig.unitCount;
                    unitTypeCount++;
                    teamLayoutHash = teamLayoutHash * 31 + config.teamId;
                }
            }

            return new AllocationSignature
            {
                agentCount = agentCount,
                gridCellCount = ComputeGridCellCount(),
                maxAgentsPerCell = Simulation.maxAgentsPerCell,
                flowFieldResolution = Flow.flowFieldResolution,
                unitTypeCount = unitTypeCount,
                scenarioConfigId = scenarioConfig != null ? scenarioConfig.GetInstanceID() : 0,
                teamLayoutHash = teamLayoutHash
            };
        }

        private int ComputeGridCellCount()
        {
            return ComputeGridResolutionX() * ComputeGridResolutionZ();
        }

        private int ComputeGridResolutionX()
        {
            return Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(1f, Simulation.simulationWorldSize.x) / Mathf.Max(0.1f, Simulation.cellSize)));
        }

        private int ComputeGridResolutionZ()
        {
            return Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(1f, Simulation.simulationWorldSize.y) / Mathf.Max(0.1f, Simulation.cellSize)));
        }

        private Vector2 ComputeGridOrigin()
        {
            return new Vector2(-Simulation.simulationWorldSize.x * 0.5f, -Simulation.simulationWorldSize.y * 0.5f);
        }

        private Bounds ResolveRenderBounds()
        {
            Vector2 worldSize = Simulation.simulationWorldSize;
            return new Bounds(Vector3.zero, new Vector3(worldSize.x + 40f, 120f, worldSize.y + 40f));
        }

        // Config access with non-persistent fallbacks so a partially wired scene still
        // runs with defaults. Fallbacks are plain defaults created on demand and
        // destroyed on release; shared assets are never created or modified here.
        private SimulationConfig fallbackSimulation;
        private LodConfig fallbackLod;
        private RuntimeFlowConfig fallbackFlow;
        private RuntimeCombatConfig fallbackCombat;

        private SimulationConfig Simulation
        {
            get
            {
                if (systemConfig != null && systemConfig.simulationConfig != null)
                    return systemConfig.simulationConfig;
                return fallbackSimulation != null ? fallbackSimulation : (fallbackSimulation = CreateFallbackConfig<SimulationConfig>());
            }
        }

        private LodConfig Lod
        {
            get
            {
                if (systemConfig != null && systemConfig.lodConfig != null)
                    return systemConfig.lodConfig;
                return fallbackLod != null ? fallbackLod : (fallbackLod = CreateFallbackConfig<LodConfig>());
            }
        }

        private RuntimeFlowConfig Flow
        {
            get
            {
                if (systemConfig != null && systemConfig.runtimeFlowConfig != null)
                    return systemConfig.runtimeFlowConfig;
                return fallbackFlow != null ? fallbackFlow : (fallbackFlow = CreateFallbackConfig<RuntimeFlowConfig>());
            }
        }

        private RuntimeCombatConfig RuntimeCombat
        {
            get
            {
                if (systemConfig != null && systemConfig.runtimeCombatConfig != null)
                    return systemConfig.runtimeCombatConfig;
                return fallbackCombat != null ? fallbackCombat : (fallbackCombat = CreateFallbackConfig<RuntimeCombatConfig>());
            }
        }

        private static T CreateFallbackConfig<T>() where T : ScriptableObject
        {
            T config = ScriptableObject.CreateInstance<T>();
            config.hideFlags = HideFlags.DontSave;
            return config;
        }

        private void OnDestroy()
        {
            DestroyFallback(ref fallbackSimulation);
            DestroyFallback(ref fallbackLod);
            DestroyFallback(ref fallbackFlow);
            DestroyFallback(ref fallbackCombat);
        }

        private static void DestroyFallback<T>(ref T config) where T : ScriptableObject
        {
            if (config == null)
                return;

            if (Application.isPlaying)
                Destroy(config);
            else
                DestroyImmediate(config);
            config = null;
        }
    }
}
