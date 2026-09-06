using UnityEngine;

namespace MassEngine
{
    /// <summary>Spatial hash grid parameters for one frame.</summary>
    public struct GridFrameSettings
    {
        public int resolutionX;
        public int resolutionZ;
        public Vector2 origin;
        public Vector2 worldSize;
        public float cellSize;
        public int maxAgentsPerCell;
        public float boundaryPadding;
    }

    /// <summary>
    /// One team's flow field parameters for one frame. Every navigating team gets a record;
    /// unit types on the same team share the team's field. Grid parameters (resolution /
    /// origin / cellSize) are carried per team but must agree across teams: the flow layer
    /// partitions one shared grid, so a per-team grid would break the cell indexing.
    /// </summary>
    public struct TeamFlowFrameSettings
    {
        public bool enabled;
        public bool rebuildThisFrame;
        public bool dynamicFlowEnabled;
        public int threadGroupsX;
        public int resolutionX;
        public int resolutionZ;
        public Vector2 origin;
        public float cellSize;
        public int targetMode;           // FLOW_TARGET_NONE / POINT / AREA
        public Vector3 targetPoint;
        public Vector3 targetAreaCenter;
        public Vector3 targetAreaSize;
        public int sectorCount;
        public float targetStopRadius;
        public int minAgentsPerTarget;
    }

    /// <summary>
    /// GPU mirror of TeamFlowParams in AgentDataCommon.hlsl (48 bytes, sequential layout).
    /// One record per team, uploaded every frame into teamFlowParamsReadBuffer.
    ///
    /// This exists because per-team flow parameters cannot be uniforms: the combat kernel
    /// reads each agent's own team record, while a uniform would only hold whichever team
    /// was dispatched last.
    /// </summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct TeamFlowParams
    {
        public const int StrideBytes = 48;

        public int targetMode;
        public int flowEnabled;
        public int dynamicEnabled;
        public int minAgentsPerTarget;
        public float targetPointX;
        public float targetPointZ;
        public float targetStopRadius;
        public float sectorCount;
        public float areaCenterX;
        public float areaCenterZ;
        public float areaSizeX;
        public float areaSizeZ;

        /// <summary>
        /// Builds a team's record, applying the same clamps the per-team uniforms used to carry.
        /// They live here rather than at the upload site so every producer of a record gets them:
        /// a sector count past FlowTargetSlotsPerTeam would index outside the team's own slice.
        /// </summary>
        public static TeamFlowParams From(TeamFlowFrameSettings settings)
        {
            return new TeamFlowParams
            {
                targetMode = settings.targetMode,
                flowEnabled = settings.enabled ? 1 : 0,
                dynamicEnabled = settings.dynamicFlowEnabled ? 1 : 0,
                minAgentsPerTarget = Mathf.Max(1, settings.minAgentsPerTarget),
                targetPointX = settings.targetPoint.x,
                targetPointZ = settings.targetPoint.z,
                targetStopRadius = Mathf.Max(0f, settings.targetStopRadius),
                sectorCount = Mathf.Clamp(settings.sectorCount, 1, MassGpuBufferManager.FlowTargetSlotsPerTeam),
                areaCenterX = settings.targetAreaCenter.x,
                areaCenterZ = settings.targetAreaCenter.z,
                areaSizeX = Mathf.Max(0f, settings.targetAreaSize.x),
                areaSizeZ = Mathf.Max(0f, settings.targetAreaSize.z)
            };
        }
    }

    /// <summary>LOD, culling and animation cadence parameters for one frame.</summary>
    public struct LodFrameSettings
    {
        public Vector3 lodCenterPosition;
        public float nearLodRadius;
        public float midLodRadius;
        public float cullingRadius;
        public float maxRenderDistance;
        public bool farIncludeDead;
        // Corpse despawn, mirrored by CorpseLifetime. corpseLingerSeconds <= 0 disables it.
        public float corpseLingerSeconds;
        public float corpseSinkSeconds;
        public float corpseSinkDepth;
        public Vector4[] frustumPlanes;
        public int nearAnimationInterval;
        public int midAnimationInterval;
        public int farAnimationInterval;
        public int nearSimulationInterval;
        public int midSimulationInterval;
        public int farSimulationInterval;
    }

    /// <summary>
    /// Per-frame data handed to the compute pipeline. Per-unit-type parameters do NOT
    /// travel through here — they are uploaded once per frame as a
    /// StructuredBuffer&lt;UnitTypeGpuSettings&gt; by the buffer manager.
    /// </summary>
    public struct PipelineFrameContext
    {
        public float deltaTime;
        public int frameIndex;
        public int totalAgentCount;
        public int unitTypeCount;
        public int agentThreadGroupsX;
        public int gridThreadGroupsX;
        public int projectileThreadGroupsX;
        public float simulationTime;
        public bool battleStarted;
        public bool combatEnabled;
        public int attackerTeamId;
        public int defenderTeamId;
        public bool rebuildDensityMap;
        public int densityMapThreadGroupsX;
        public int densityMapThreadGroupsY;
        public float defenderGuardRadius;
        public int localTargetSearchCellRadius;
        public bool flowPreviewEnabled;
        public int runtimeFlowPreviewMode;
        public int staticObstacleCount;
        public float staticObstaclePadding;
        public Vector4[] staticObstacleRects;
        public GridFrameSettings grid;
        /// <summary>One entry per navigating team, indexed by raw teamId. Never null once built.</summary>
        public TeamFlowFrameSettings[] teamFlows;
        public LodFrameSettings lod;
    }

    /// <summary>Initialization context handed to each unit type.</summary>
    public struct UnitTypeInitContext
    {
        public int unitTypeIndex;
        public int bufferOffset;
        public int totalAgentCount;
        public MassGpuBufferManager bufferManager;
        public ComputePipelineOrchestrator pipeline;
    }
}
