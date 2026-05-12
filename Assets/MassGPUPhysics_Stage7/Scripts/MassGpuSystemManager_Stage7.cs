using UnityEngine;
using UnityEngine.Rendering;

namespace MassGPUPhysics.Stage7
{
    public sealed class MassGpuSystemManager_Stage7 : MonoBehaviour
    {
        [Header("Scenario")]
        public ScenarioConfig_Stage7 scenarioConfig;
        public Stage7SystemConfig systemConfig;
        public Stage7ShaderConfig shaderConfig;

        [Header("Runtime Anchors")]
        public Transform lodCenter;
        public Camera cullingCamera;

        [Header("Runtime")]
        public bool enableGpuDispatch;
        public bool rebuildRuntimeFlowEveryFrame;
        public bool battleStarted;

        private UnitTypeRegistry unitTypeRegistry;
        private ComputePipelineOrchestrator pipelineOrchestrator;
        private MassGpuBufferManager_Stage7 bufferManager;
        private MassGpuRenderDispatcher_Stage7 renderDispatcher;
        private SimulationConfig fallbackSimulation;
        private LodConfig fallbackLod;
        private RuntimeFlowConfig fallbackFlow;
        private RuntimeCombatConfig fallbackCombat;

        public UnitTypeRegistry UnitTypes { get { return unitTypeRegistry; } }
        public MassGpuBufferManager_Stage7 Buffers { get { return bufferManager; } }

        private void Start()
        {
            Initialize();
        }

        private void Update()
        {
            if (!enableGpuDispatch || pipelineOrchestrator == null || bufferManager == null || !bufferManager.IsAllocated)
                return;
            if (unitTypeRegistry != null && unitTypeRegistry.TotalAgentCount != bufferManager.AgentCount)
            {
                Initialize();
                return;
            }

            pipelineOrchestrator.DispatchFrame(CreateFrameContext(ShouldRebuildAttackerFlow(), ShouldRebuildDefenderFlow()));
            renderDispatcher.Draw(unitTypeRegistry, bufferManager, ResolveRenderBounds());
        }

        private void OnDisable()
        {
            Release();
        }

        [ContextMenu("Stage7/Initialize")]
        public void Initialize()
        {
            Release();

            unitTypeRegistry = new UnitTypeRegistry();
            unitTypeRegistry.RegisterFromScenario(scenarioConfig);

            bufferManager = new MassGpuBufferManager_Stage7();
            MassGpuShaderSet_Stage7 shaders = MassGpuShaderSet_Stage7.Find(
                shaderConfig != null ? shaderConfig.spatialHashShader : null,
                shaderConfig != null ? shaderConfig.runtimeFlowShader : null,
                shaderConfig != null ? shaderConfig.combatSimulationShader : null,
                shaderConfig != null ? shaderConfig.lodClassificationShader : null);
            pipelineOrchestrator = new ComputePipelineOrchestrator(shaders, bufferManager);
            renderDispatcher = new MassGpuRenderDispatcher_Stage7();
            ApplyVatProfilesToConfigs();

            int totalAgents = unitTypeRegistry.TotalAgentCount;
            int gridCellCount = ComputeGridCellCount();

            bufferManager.Allocate(totalAgents, gridCellCount, Simulation.maxAgentsPerCell, Flow.flowFieldResolution, Flow.flowFieldResolution);
            unitTypeRegistry.InitializeAll(bufferManager, pipelineOrchestrator);
            UploadInitialAgents();
            UnitTypeConfig attackerConfig = unitTypeRegistry.FindFirstConfigForTeam(0);
            UnitTypeConfig defenderConfig = unitTypeRegistry.FindFirstConfigForTeam(1);
            bufferManager.ConfigureDrawArgs(
                attackerConfig != null ? attackerConfig.renderConfig : null,
                defenderConfig != null ? defenderConfig.renderConfig : null);
        }

        private void ApplyVatProfilesToConfigs()
        {
            if (unitTypeRegistry == null)
                return;

            for (int i = 0; i < unitTypeRegistry.RegisteredTypes.Count; i++)
                ApplyVatProfileToConfig(unitTypeRegistry.RegisteredTypes[i].Config);
        }

        private static void ApplyVatProfileToConfig(UnitTypeConfig config)
        {
            RenderConfig render = config != null ? config.renderConfig : null;
            ScriptableObject profile = render != null ? render.vatProfile : null;
            if (render == null || profile == null)
                return;

            if (!TryReadVatProfile(profile, out VatProfileData profileData, out string error))
            {
                Debug.LogError("Stage7 VAT Profile '" + profile.name + "' is invalid: " + error, profile);
                return;
            }

            render.nearMesh = profileData.cleanMesh;
            if (profileData.hasMidLod)
                render.midMesh = profileData.midLodMesh;
            else if (profileData.hasLowLod)
                render.midMesh = profileData.lowLodMesh;

            if (profileData.hasLowLod)
                render.farMesh = profileData.lowLodMesh;
            else if (profileData.hasMidLod)
                render.farMesh = profileData.midLodMesh;

            AnimationConfig animation = config.animationConfig;
            if (animation == null)
                return;

            animation.idleClipFrameRange = profileData.idle.ToRange();
            animation.moveClipFrameRange = profileData.move.ToRange();
            animation.attackClipFrameRange = profileData.attack.ToRange();
            animation.deathClipFrameRange = profileData.death.ToRange();
            animation.idleClipFrameRate = Mathf.Max(1, profileData.idle.frameRate);
            animation.moveClipFrameRate = Mathf.Max(1, profileData.move.frameRate);
            animation.attackClipFrameRate = Mathf.Max(1, profileData.attack.frameRate);
            animation.deathClipFrameRate = Mathf.Max(1, profileData.death.frameRate);
        }

        internal static bool TryReadVatProfile(ScriptableObject profile, out VatProfileData data, out string error)
        {
            data = default;
            error = string.Empty;
            if (profile == null)
            {
                error = "Profile is null.";
                return false;
            }

            System.Type type = profile.GetType();
            data.cleanMesh = ReadField<Mesh>(profile, type, "cleanMesh");
            data.positionTexture = ReadField<Texture>(profile, type, "positionTexture");
            data.normalTexture = ReadField<Texture>(profile, type, "normalTexture");
            data.midLodMesh = ReadField<Mesh>(profile, type, "midLodMesh");
            data.midLodPositionTexture = ReadField<Texture>(profile, type, "midLodPositionTexture");
            data.midLodNormalTexture = ReadField<Texture>(profile, type, "midLodNormalTexture");
            data.midLodTextureWidth = ReadField<int>(profile, type, "midLodTextureWidth");
            data.midLodTextureHeight = ReadField<int>(profile, type, "midLodTextureHeight");
            data.midLodRowsPerFrame = ReadField<int>(profile, type, "midLodRowsPerFrame");
            data.lowLodMesh = ReadField<Mesh>(profile, type, "lowLodMesh");
            data.lowLodPositionTexture = ReadField<Texture>(profile, type, "lowLodPositionTexture");
            data.lowLodNormalTexture = ReadField<Texture>(profile, type, "lowLodNormalTexture");
            data.lowLodTextureWidth = ReadField<int>(profile, type, "lowLodTextureWidth");
            data.lowLodTextureHeight = ReadField<int>(profile, type, "lowLodTextureHeight");
            data.lowLodRowsPerFrame = ReadField<int>(profile, type, "lowLodRowsPerFrame");
            data.textureWidth = ReadField<int>(profile, type, "textureWidth");
            data.textureHeight = ReadField<int>(profile, type, "textureHeight");
            data.rowsPerFrame = ReadField<int>(profile, type, "rowsPerFrame");
            data.totalFrameCount = ReadField<int>(profile, type, "totalFrameCount");
            data.frameRate = ReadField<int>(profile, type, "frameRate");
            data.idle = ReadClip(profile, type, "idle");
            data.move = ReadClip(profile, type, "move");
            data.attack = ReadClip(profile, type, "attack");
            data.death = ReadClip(profile, type, "death");
            data.hasLowLod = data.lowLodMesh != null &&
                             data.lowLodPositionTexture != null &&
                             data.lowLodNormalTexture != null &&
                             data.lowLodTextureWidth > 0 &&
                             data.lowLodTextureHeight > 0 &&
                             data.lowLodRowsPerFrame > 0;
            data.hasMidLod = data.midLodMesh != null &&
                             data.midLodPositionTexture != null &&
                             data.midLodNormalTexture != null &&
                             data.midLodTextureWidth > 0 &&
                             data.midLodTextureHeight > 0 &&
                             data.midLodRowsPerFrame > 0;

            if (data.cleanMesh == null)
            {
                error = "Profile is missing cleanMesh.";
                return false;
            }

            if (data.positionTexture == null || data.normalTexture == null)
            {
                error = "Profile is missing full LOD VAT position or normal texture.";
                return false;
            }

            if (data.textureWidth <= 0 || data.textureHeight <= 0 || data.rowsPerFrame <= 0 || data.totalFrameCount <= 0 || data.frameRate <= 0)
            {
                error = "Profile has invalid full LOD VAT layout values.";
                return false;
            }

            return true;
        }

        private static T ReadField<T>(object target, System.Type targetType, string fieldName)
        {
            System.Reflection.FieldInfo field = targetType.GetField(fieldName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (field == null)
                return default;

            object value = field.GetValue(target);
            return value is T typed ? typed : default;
        }

        private static VatClipData ReadClip(object target, System.Type targetType, string fieldName)
        {
            System.Reflection.FieldInfo field = targetType.GetField(fieldName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (field == null)
                return default;

            object clip = field.GetValue(target);
            if (clip == null)
                return default;

            System.Type clipType = clip.GetType();
            return new VatClipData
            {
                startFrame = ReadField<int>(clip, clipType, "startFrame"),
                frameCount = Mathf.Max(1, ReadField<int>(clip, clipType, "frameCount")),
                frameRate = Mathf.Max(1, ReadField<int>(clip, clipType, "frameRate"))
            };
        }

        internal struct VatProfileData
        {
            public Mesh cleanMesh;
            public Texture positionTexture;
            public Texture normalTexture;
            public Mesh midLodMesh;
            public Texture midLodPositionTexture;
            public Texture midLodNormalTexture;
            public int midLodTextureWidth;
            public int midLodTextureHeight;
            public int midLodRowsPerFrame;
            public Mesh lowLodMesh;
            public Texture lowLodPositionTexture;
            public Texture lowLodNormalTexture;
            public int lowLodTextureWidth;
            public int lowLodTextureHeight;
            public int lowLodRowsPerFrame;
            public int textureWidth;
            public int textureHeight;
            public int rowsPerFrame;
            public int totalFrameCount;
            public int frameRate;
            public VatClipData idle;
            public VatClipData move;
            public VatClipData attack;
            public VatClipData death;
            public bool hasMidLod;
            public bool hasLowLod;
        }

        internal struct VatClipData
        {
            public int startFrame;
            public int frameCount;
            public int frameRate;

            public Vector2 ToRange()
            {
                return new Vector2(startFrame, Mathf.Max(1, frameCount));
            }
        }

        public void StartBattle()
        {
            battleStarted = true;
        }

        public void StopBattle()
        {
            battleStarted = false;
        }

        public void ResetScenario()
        {
            Initialize();
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

            DestroyFallback(ref fallbackSimulation);
            DestroyFallback(ref fallbackLod);
            DestroyFallback(ref fallbackFlow);
            DestroyFallback(ref fallbackCombat);
        }

        private void UploadInitialAgents()
        {
            if (unitTypeRegistry == null || bufferManager == null || !bufferManager.IsAllocated)
                return;

            int total = unitTypeRegistry.TotalAgentCount;
            AgentData[] agents = new AgentData[total];
            int[] teamIds = new int[total];
            int[] hpValues = new int[total];

            unitTypeRegistry.GenerateAgents(agents);
            unitTypeRegistry.FillCombatArrays(teamIds, hpValues);
            bufferManager.UploadInitialData(agents, teamIds, hpValues);
        }

        private PipelineFrameContext CreateFrameContext(bool rebuildAttackerFlow, bool rebuildDefenderFlow)
        {
            int total = unitTypeRegistry != null ? unitTypeRegistry.TotalAgentCount : 0;
            int agentThreadGroups = Mathf.CeilToInt(total / 64f);
            int gridThreadGroups = Mathf.CeilToInt(Mathf.Max(1, bufferManager != null ? bufferManager.GridCellCount : 1) / 64f);
            int flowResolution = Flow.flowFieldResolution;
            int flowThreadGroups = Mathf.CeilToInt(Mathf.Max(1, flowResolution * flowResolution) / 64f);
            int densityMapThreadGroups = Mathf.CeilToInt(Mathf.Max(1, flowResolution) / 8f);
            UnitTypeConfig attackerConfig = unitTypeRegistry != null ? unitTypeRegistry.FindFirstConfigForTeam(0) : null;
            UnitTypeConfig defenderConfig = unitTypeRegistry != null ? unitTypeRegistry.FindFirstConfigForTeam(1) : null;
            bool rebuildDensityMap = rebuildRuntimeFlowEveryFrame || rebuildAttackerFlow || rebuildDefenderFlow;

            return new PipelineFrameContext
            {
                deltaTime = Time.deltaTime,
                totalAgentCount = total,
                agentThreadGroupsX = Mathf.Max(1, agentThreadGroups),
                gridThreadGroupsX = Mathf.Max(1, gridThreadGroups),
                flowFieldThreadGroupsX = Mathf.Max(1, flowThreadGroups),
                defenderFlowFieldThreadGroupsX = Mathf.Max(1, flowThreadGroups),
                rebuildDensityMap = rebuildDensityMap,
                densityMapThreadGroupsX = Mathf.Max(1, densityMapThreadGroups),
                densityMapThreadGroupsY = Mathf.Max(1, densityMapThreadGroups),
                rebuildAttackerFlow = rebuildAttackerFlow,
                rebuildDefenderFlow = rebuildDefenderFlow,
                battleStarted = battleStarted,
                lodCenterPosition = ResolveLodCenter(),
                nearLodRadius = Lod.nearLodRadius,
                midLodRadius = Lod.midLodRadius,
                cullingRadius = Lod.cullingRadius,
                frustumPlanes = BuildFrustumPlanes(),
                gridResolutionX = ComputeGridResolutionX(),
                gridResolutionZ = ComputeGridResolutionZ(),
                gridOrigin = ComputeGridOrigin(),
                gridWorldSize = Simulation.simulationWorldSize,
                cellSize = Simulation.cellSize,
                maxAgentsPerCell = Simulation.maxAgentsPerCell,
                attackerCount = unitTypeRegistry != null ? unitTypeRegistry.CountAgentsForTeam(0) : 0,
                attackerSettings = UnitTypeGpuSettings.FromConfig(attackerConfig),
                defenderSettings = UnitTypeGpuSettings.FromConfig(defenderConfig),
                boundaryPadding = Simulation.boundaryPadding,
                flowFieldResolutionX = flowResolution,
                flowFieldResolutionZ = flowResolution,
                flowFieldOrigin = Flow.flowFieldOrigin,
                flowFieldCellSize = Flow.flowFieldCellSize,
                flowFieldEnabled = Flow.flowFieldEnabled,
                flowFieldWeight = Flow.flowFieldWeight,
                flowFieldResponsiveness = Flow.flowFieldResponsiveness,
                defenderFlowFieldResolutionX = flowResolution,
                defenderFlowFieldResolutionZ = flowResolution,
                defenderFlowFieldOrigin = Flow.flowFieldOrigin,
                defenderFlowFieldCellSize = Flow.flowFieldCellSize,
                defenderFlowFieldEnabled = Flow.defenderFlowFieldEnabled,
                runtimeFlowPreviewMode = (int)Flow.runtimeFlowPreviewMode,
                runtimeDynamicAttackerFlowEnabled = Flow.runtimeDynamicAttackerFlowEnabled,
                runtimeDynamicDefenderFlowEnabled = Flow.runtimeDynamicDefenderFlowEnabled,
                dynamicFlowSectorCount = Flow.dynamicFlowSectorCount,
                dynamicFlowTargetStopRadius = Flow.dynamicFlowTargetStopRadius,
                dynamicFlowMinDefendersPerTarget = Flow.dynamicFlowMinDefendersPerTarget,
                dynamicDefenderFlowSectorCount = Flow.dynamicDefenderFlowSectorCount,
                dynamicDefenderFlowTargetStopRadius = Flow.dynamicDefenderFlowTargetStopRadius,
                dynamicDefenderFlowMinAttackersPerTarget = Flow.dynamicDefenderFlowMinAttackersPerTarget,
                defenderMovementMode = Flow.defenderFlowFieldEnabled ? 1 : 0,
                defenderGuardRadius = RuntimeCombat.defenderGuardRadius,
                defenderMaxChaseDistance = RuntimeCombat.defenderMaxChaseDistance,
                deathClipDuration = RuntimeCombat.deathClipDuration,
                animationDuration = RuntimeCombat.deathClipDuration,
                nearAnimationInterval = Lod.nearAnimationInterval,
                midAnimationInterval = Lod.midAnimationInterval,
                farAnimationInterval = Lod.farAnimationInterval,
                frameIndex = Time.frameCount
            };
        }

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
                return new Vector4[0];

            Camera camera = cullingCamera != null ? cullingCamera : Camera.main;
            if (camera == null)
                return new Vector4[0];

            Plane[] planes = GeometryUtility.CalculateFrustumPlanes(camera);
            Vector4[] result = new Vector4[planes.Length];
            for (int i = 0; i < planes.Length; i++)
            {
                Vector3 normal = planes[i].normal;
                result[i] = new Vector4(normal.x, normal.y, normal.z, planes[i].distance);
            }

            return result;
        }

        private int ComputeGridCellCount()
        {
            float safeCellSize = Mathf.Max(0.1f, Simulation.cellSize);
            int cellsX = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(1f, Simulation.simulationWorldSize.x) / safeCellSize));
            int cellsZ = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(1f, Simulation.simulationWorldSize.y) / safeCellSize));
            return cellsX * cellsZ;
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

        private bool ShouldRebuildAttackerFlow()
        {
            return Flow.flowFieldEnabled && (rebuildRuntimeFlowEveryFrame || HasConfiguredFlowTarget(0)) &&
                   (Flow.runtimeDynamicAttackerFlowEnabled || HasConfiguredFlowTarget(0));
        }

        private bool ShouldRebuildDefenderFlow()
        {
            return Flow.defenderFlowFieldEnabled && (rebuildRuntimeFlowEveryFrame || HasConfiguredFlowTarget(1)) &&
                   (Flow.runtimeDynamicDefenderFlowEnabled || HasConfiguredFlowTarget(1));
        }

        private bool HasConfiguredFlowTarget(int teamId)
        {
            UnitTypeConfig config = unitTypeRegistry != null ? unitTypeRegistry.FindFirstConfigForTeam(teamId) : null;
            MovementConfig movement = config != null ? config.movementConfig : null;
            return movement != null && movement.useConfiguredFlowTarget;
        }

        private SimulationConfig Simulation
        {
            get
            {
                if (systemConfig != null && systemConfig.simulationConfig != null)
                    return systemConfig.simulationConfig;
                return fallbackSimulation ?? (fallbackSimulation = CreateFallbackConfig<SimulationConfig>());
            }
        }

        private LodConfig Lod
        {
            get
            {
                if (systemConfig != null && systemConfig.lodConfig != null)
                    return systemConfig.lodConfig;
                return fallbackLod ?? (fallbackLod = CreateFallbackConfig<LodConfig>());
            }
        }

        private RuntimeFlowConfig Flow
        {
            get
            {
                if (systemConfig != null && systemConfig.runtimeFlowConfig != null)
                    return systemConfig.runtimeFlowConfig;
                return fallbackFlow ?? (fallbackFlow = CreateFallbackConfig<RuntimeFlowConfig>());
            }
        }

        private RuntimeCombatConfig RuntimeCombat
        {
            get
            {
                if (systemConfig != null && systemConfig.runtimeCombatConfig != null)
                    return systemConfig.runtimeCombatConfig;
                return fallbackCombat ?? (fallbackCombat = CreateFallbackConfig<RuntimeCombatConfig>());
            }
        }

        private static T CreateFallbackConfig<T>() where T : ScriptableObject
        {
            T config = ScriptableObject.CreateInstance<T>();
            config.hideFlags = HideFlags.DontSave;
            return config;
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
