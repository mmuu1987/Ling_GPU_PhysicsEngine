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
    /// One team's flow field parameters for one frame. The pipeline currently maintains
    /// exactly two flow fields (attacker team 0, defender team 1); unit types on the same
    /// team share the team's field.
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

    /// <summary>LOD, culling and animation cadence parameters for one frame.</summary>
    public struct LodFrameSettings
    {
        public Vector3 lodCenterPosition;
        public float nearLodRadius;
        public float midLodRadius;
        public float cullingRadius;
        public float maxRenderDistance;
        public bool farIncludeDead;
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
        public bool battleStarted;
        public bool combatEnabled;
        public int attackerTeamId;
        public int defenderTeamId;
        public bool rebuildDensityMap;
        public int densityMapThreadGroupsX;
        public int densityMapThreadGroupsY;
        public int defenderMovementMode;
        public float defenderGuardRadius;
        public int localTargetSearchCellRadius;
        public bool flowPreviewEnabled;
        public int runtimeFlowPreviewMode;
        public int staticObstacleCount;
        public float staticObstaclePadding;
        public Vector4[] staticObstacleRects;
        public GridFrameSettings grid;
        public TeamFlowFrameSettings attackerFlow;
        public TeamFlowFrameSettings defenderFlow;
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
