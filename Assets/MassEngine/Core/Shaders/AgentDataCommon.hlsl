// Shared declarations and helpers for the MassEngine compute pipeline.
// Keep this file in sync with AgentData.cs / UnitTypeGpuSettings.cs.
//
// MassEngine contracts enforced here:
// - Team identity has exactly ONE source of truth: teamIdReadBuffer. No kernel may
//   infer team membership from buffer index ranges.
// - Per-unit-type tuning parameters travel through unitTypeSettings (StructuredBuffer),
//   indexed via unitTypeIndexReadBuffer. No per-unit-type scalar uniforms.
// - hp is double-buffered: hpReadBuffer is last frame's snapshot (read by every kernel),
//   hpBuffer is this frame's write target (written only by the combat kernel).

#define STATE_IDLE 0
#define STATE_MOVE 1
#define STATE_ENGAGE 2
#define STATE_ATTACK 3
#define STATE_DEAD 4
#define DEFENDER_MODE_HOLD_POSITION 0
#define DEFENDER_MODE_FLOW_FIELD 1
#define FLOW_TARGET_NONE 0
#define FLOW_TARGET_POINT 1
#define FLOW_TARGET_AREA 2
// Hard upper bound for the local target search radius (cells). The effective radius is
// min(localTargetSearchCellRadius, ceil(targetAcquireRadius / cellSize)) and is driven
// from C# so a configured acquire radius is never silently truncated without a warning.
#define LOCAL_TARGET_SEARCH_MAX_CELL_RADIUS 4
#define LOCAL_TARGET_SEARCH_INTERVAL 4u
#define LOCAL_TARGET_SEARCH_GROUP_SIZE 64u
#define ENGAGEMENT_SLOT_COUNT 8u
#define ENGAGEMENT_SLOT_SHIFT 3u
#define ENGAGEMENT_SLOT_MASK 7u

struct AgentData
{
    float3 position;
    float3 rotation;
    float3 scale;
    float3 velocity;
    int currentState;
    float currentAnimationTime;
};

// Mirror of MassEngine.UnitTypeGpuSettings (144 bytes, sequential layout).
struct UnitTypeSettings
{
    float agentRadius;
    float separationStrength;
    float velocityDamping;
    float maxSpeed;
    float targetAcquireRadius;
    float attackRange;
    float attackInterval;
    int attackDamage;
    float densityAvoidanceStrength;
    float densityPressureRangePerSqm;
    float densitySpeedPenalty;
    float speedVariation;
    float laneBiasStrength;
    float flowFieldWeight;
    float flowFieldResponsiveness;
    float attractionStrength;
    float idleClipDuration;
    float moveClipDuration;
    float attackClipDuration;
    float deathClipDuration;
    float moveAnimationSpeedMin;
    float moveAnimationSpeedMax;
    float densityComfortPerSqm;
    float projectileRange;
    float projectileSpeed;
    float projectileGravity;
    float projectileHitRadius;
    float projectileMaxLifetime;
    int padding3;
    int teamId;
    int padding0;
    int padding1;
    int padding2;
    // 补齐到 36×4 = 144 字节（16字节对齐）
    int padding4;
    int padding5;
    int padding6;
};

RWStructuredBuffer<AgentData> agentBuffer;
StructuredBuffer<float2> agentPositionReadBuffer;
RWStructuredBuffer<float2> agentPositionBuffer;
RWStructuredBuffer<uint> gridCounts;
RWStructuredBuffer<uint> gridAgentIndices;
RWStructuredBuffer<uint> teamGridCounts;
RWStructuredBuffer<uint> teamGridAgentIndices;
// [0] = agents dropped this frame because their cell was full (visibility for the
// silent failure mode where overflow victims vanish from neighborhood queries).
RWStructuredBuffer<int> spatialHashStats;
// Two fixed-size records used by low-frequency telemetry. Per-team layout (8 ints):
// alive count, sum X, sum Z, min X, min Z, max X, max Z, reserved.
RWStructuredBuffer<int> teamSpatialStats;
int telemetryObservationZoneEnabled;
float4 telemetryObservationZone;
StructuredBuffer<uint> gridCountsReadBuffer;
StructuredBuffer<uint> gridAgentIndicesReadBuffer;
StructuredBuffer<uint> teamGridCountsReadBuffer;
StructuredBuffer<uint> teamGridAgentIndicesReadBuffer;
RWStructuredBuffer<float2> flowFieldDirections;
RWStructuredBuffer<float2> defenderFlowFieldDirections;
StructuredBuffer<float2> flowFieldDirectionsReadBuffer;
StructuredBuffer<float2> defenderFlowFieldDirectionsReadBuffer;
Texture2D<uint> densityMap;
RWTexture2D<uint> densityMapWrite;
Texture2D<uint> attackerDensityMap;
Texture2D<uint> defenderDensityMap;
RWTexture2D<uint> attackerDensityMapWrite;
RWTexture2D<uint> defenderDensityMapWrite;
RWStructuredBuffer<uint> runtimeAttackerTargetDensity;
RWStructuredBuffer<int> runtimeAttackerFlowStats;
RWStructuredBuffer<float4> runtimeAttackerFlowTargets;
RWTexture2D<float4> runtimeAttackerFlowPreviewTexture;
RWStructuredBuffer<uint> runtimeDefenderTargetDensity;
RWStructuredBuffer<int> runtimeDefenderFlowStats;
RWStructuredBuffer<float4> runtimeDefenderFlowTargets;
RWTexture2D<float4> runtimeDefenderFlowPreviewTexture;
int runtimeFlowPreviewMode;
int flowPreviewEnabled;

StructuredBuffer<UnitTypeSettings> unitTypeSettings;
StructuredBuffer<int> unitTypeIndexReadBuffer;

RWStructuredBuffer<int> hpBuffer;            // this frame's write target (combat kernel only)
StructuredBuffer<int> hpReadBuffer;          // last frame's snapshot (read everywhere)
StructuredBuffer<int> teamIdReadBuffer;
RWStructuredBuffer<int> targetAgentIndexBuffer;
RWStructuredBuffer<int> engagementSlotAssignmentBuffer;
RWStructuredBuffer<uint> engagementSlotOccupancyBuffer;
StructuredBuffer<uint> engagementSlotOccupancyReadBuffer;
RWStructuredBuffer<float> attackCooldownBuffer;
StructuredBuffer<float3> homePositionReadBuffer;
RWStructuredBuffer<int> pendingDamageBuffer;
StructuredBuffer<int> pendingDamageReadBuffer;

// Projectile system
RWStructuredBuffer<int> launchRequestBuffer;

// LOD classification runs once per unit type; only the buffers of the unit type
// currently being classified are bound.
int classifyUnitTypeIndex;
AppendStructuredBuffer<uint> nearVisibleAgentIndices;
AppendStructuredBuffer<uint> midVisibleAgentIndices;
AppendStructuredBuffer<uint> farVisibleAgentIndices;

float deltaTime;
float currentTime;
uint frameIndex;

float3 lodCenter;
float nearLodRadiusSqr;
float midLodRadiusSqr;

int enableFrustumCulling;
float cullingRadius;
float maxRenderDistanceSqr; // 0 = unlimited
int farIncludeDead; // 1 = corpses render in the far tier too (no 120m corpse pop line)
float4 frustumPlanes[6];

int nearAnimationInterval;
int midAnimationInterval;
int farAnimationInterval;

// LOD-scaled simulation frequency: agents in the near/mid/far tier run their DECISION
// pass every 1/2/4-th frame (configurable). Rate-dependent quantities are integrated
// with the compensated timestep, so behaviour (DPS, speeds) matches full rate.
int nearSimulationInterval;
int midSimulationInterval;
int farSimulationInterval;

uint gridCellCount;
int2 gridResolution;
float2 gridOrigin;
float2 gridWorldSize;
float cellSize;
uint maxAgentsPerCell;
float boundaryPadding;

int flowFieldEnabled;
int2 flowFieldResolution;
float2 flowFieldOrigin;
float flowFieldCellSize;
int attackerFlowTargetMode;
float4 attackerFlowTargetPoint;
float4 attackerFlowTargetArea;
int defenderFlowTargetMode;
float4 defenderFlowTargetPoint;
float4 defenderFlowTargetArea;
int runtimeDynamicAttackerFlowEnabled;
int runtimeDynamicDefenderFlowEnabled;
int dynamicFlowSectorCount;
float dynamicFlowTargetStopRadius;
int dynamicFlowMinDefendersPerTarget;
int dynamicDefenderFlowSectorCount;
float dynamicDefenderFlowTargetStopRadius;
int dynamicDefenderFlowMinAttackersPerTarget;
int defenderMovementMode;
int defenderFlowFieldEnabled;
int2 defenderFlowFieldResolution;
float2 defenderFlowFieldOrigin;
float defenderFlowFieldCellSize;

#define MAX_STATIC_OBSTACLES 8
int staticObstacleCount;
float staticObstaclePadding;
float4 staticObstacleRects[MAX_STATIC_OBSTACLES];

int enableTwoTeamCombat;
int battleStarted;
int attackerTeamId;
int defenderTeamId;
int localTargetSearchCellRadius;
float defenderGuardRadius;

UnitTypeSettings GetUnitSettings(uint agentIndex)
{
    return unitTypeSettings[unitTypeIndexReadBuffer[agentIndex]];
}

bool IsInsideFrustum(float3 position)
{
    if (enableFrustumCulling == 0)
        return true;

    [unroll]
    for (int i = 0; i < 6; i++)
    {
        if (dot(frustumPlanes[i].xyz, position) + frustumPlanes[i].w < -cullingRadius)
            return false;
    }

    return true;
}

float SafeDt()
{
    return min(deltaTime, 0.05);
}

float4 ExpandedStaticObstacleRect(int obstacleIndex, float extraPadding)
{
    float padding = max(0.0, staticObstaclePadding + extraPadding);
    float4 rect = staticObstacleRects[obstacleIndex];
    return rect + float4(-padding, -padding, padding, padding);
}

bool PointInsideStaticObstacleRect(float2 positionXZ, float4 rect)
{
    return positionXZ.x >= rect.x && positionXZ.x <= rect.z && positionXZ.y >= rect.y && positionXZ.y <= rect.w;
}

bool SegmentIntersectsStaticObstacleRect(float2 start, float2 end, float4 rect, out float enterDistance01)
{
    float2 delta = end - start;
    float enter = 0.0;
    float exit = 1.0;

    if (abs(delta.x) <= 0.000001)
    {
        if (start.x < rect.x || start.x > rect.z)
        {
            enterDistance01 = 1.0;
            return false;
        }
    }
    else
    {
        float2 tx = (float2(rect.x, rect.z) - start.x) / delta.x;
        enter = max(enter, min(tx.x, tx.y));
        exit = min(exit, max(tx.x, tx.y));
    }

    if (abs(delta.y) <= 0.000001)
    {
        if (start.y < rect.y || start.y > rect.w)
        {
            enterDistance01 = 1.0;
            return false;
        }
    }
    else
    {
        float2 ty = (float2(rect.y, rect.w) - start.y) / delta.y;
        enter = max(enter, min(ty.x, ty.y));
        exit = min(exit, max(ty.x, ty.y));
    }

    enterDistance01 = enter;
    return exit >= enter && exit >= 0.0 && enter <= 1.0;
}

float2 DirectionOutOfStaticObstacle(float2 positionXZ, float4 rect)
{
    float4 distances = float4(
        positionXZ.x - rect.x,
        rect.z - positionXZ.x,
        positionXZ.y - rect.y,
        rect.w - positionXZ.y);
    float nearest = min(min(distances.x, distances.y), min(distances.z, distances.w));
    if (nearest == distances.x)
        return float2(-1.0, 0.0);
    if (nearest == distances.y)
        return float2(1.0, 0.0);
    if (nearest == distances.z)
        return float2(0.0, -1.0);
    return float2(0.0, 1.0);
}

// The existing runtime flow is a direct per-cell steering field, not an integration
// cost field. Preserve that architecture and detour only when the cell-to-target ray
// crosses an obstacle. The cheapest visible expanded corner becomes the local waypoint.
float2 StaticObstacleAwareDirection(float2 start, float2 target, float extraPadding)
{
    float2 direct = target - start;
    if (dot(direct, direct) <= 0.0001)
        return 0.0;

    int count = clamp(staticObstacleCount, 0, MAX_STATIC_OBSTACLES);
    int blockingObstacle = -1;
    float nearestEnter = 2.0;
    for (int obstacleIndex = 0; obstacleIndex < count; obstacleIndex++)
    {
        float4 rect = ExpandedStaticObstacleRect(obstacleIndex, extraPadding);
        if (PointInsideStaticObstacleRect(start, rect))
            return DirectionOutOfStaticObstacle(start, rect);

        float enter;
        if (SegmentIntersectsStaticObstacleRect(start, target, rect, enter) && enter < nearestEnter)
        {
            blockingObstacle = obstacleIndex;
            nearestEnter = enter;
        }
    }

    if (blockingObstacle < 0)
        return normalize(direct);

    float4 blockingRect = ExpandedStaticObstacleRect(blockingObstacle, extraPadding);
    float cornerMargin = max(0.2, extraPadding * 0.5 + 0.1);
    float bestScore = 1e20;
    float2 bestCorner = float2(blockingRect.x - cornerMargin, blockingRect.y - cornerMargin);
    [unroll]
    for (int cornerIndex = 0; cornerIndex < 4; cornerIndex++)
    {
        float2 corner = float2(
            cornerIndex < 2 ? blockingRect.x - cornerMargin : blockingRect.z + cornerMargin,
            (cornerIndex & 1) == 0 ? blockingRect.y - cornerMargin : blockingRect.w + cornerMargin);
        float ignoredEnter;
        bool crossesBlockingRect = SegmentIntersectsStaticObstacleRect(start, corner, blockingRect, ignoredEnter);
        float score = length(corner - start) + length(target - corner);
        if (crossesBlockingRect)
            score += 100000.0;

        // Reject a corner whose first leg immediately cuts through another obstacle.
        for (int otherIndex = 0; otherIndex < count; otherIndex++)
        {
            if (otherIndex == blockingObstacle)
                continue;
            float4 otherRect = ExpandedStaticObstacleRect(otherIndex, extraPadding);
            if (SegmentIntersectsStaticObstacleRect(start, corner, otherRect, ignoredEnter))
                score += 100000.0;
        }

        if (score < bestScore)
        {
            bestScore = score;
            bestCorner = corner;
        }
    }

    float2 detour = bestCorner - start;
    return dot(detour, detour) > 0.0001 ? normalize(detour) : normalize(direct);
}

void ApplyStaticObstacleBounds(uint agentIndex, inout AgentData agent)
{
    int count = clamp(staticObstacleCount, 0, MAX_STATIC_OBSTACLES);
    float radius = max(0.05, GetUnitSettings(agentIndex).agentRadius);
    for (int obstacleIndex = 0; obstacleIndex < count; obstacleIndex++)
    {
        float4 rect = ExpandedStaticObstacleRect(obstacleIndex, radius);
        if (!PointInsideStaticObstacleRect(agent.position.xz, rect))
            continue;

        float2 normal = DirectionOutOfStaticObstacle(agent.position.xz, rect);
        if (normal.x < 0.0)
            agent.position.x = rect.x - 0.001;
        else if (normal.x > 0.0)
            agent.position.x = rect.z + 0.001;
        else if (normal.y < 0.0)
            agent.position.z = rect.y - 0.001;
        else
            agent.position.z = rect.w + 0.001;

        float inwardSpeed = dot(agent.velocity.xz, -normal);
        if (inwardSpeed > 0.0)
            agent.velocity.xz += normal * inwardSpeed;
    }
}

// -----------------------------------------------------------------------------
// LOD-scaled simulation cadence.
// The stagger phase is per 64-agent group (= one threadgroup), NOT per agent, so a
// whole threadgroup takes the same active/skip branch and skipping saves real time
// (per-lane stagger would diverge every warp and pay the heavy path anyway).
// -----------------------------------------------------------------------------
int GetSimulationInterval(float3 position)
{
    float3 offset = position - lodCenter;
    offset.y = 0.0;
    float distSqr = dot(offset, offset);
    if (distSqr <= nearLodRadiusSqr)
        return max(1, nearSimulationInterval);
    if (distSqr <= midLodRadiusSqr)
        return max(1, midSimulationInterval);
    return max(1, farSimulationInterval);
}

bool IsSimulationActiveFrame(uint index, int simInterval)
{
    if (simInterval <= 1)
        return true;

    uint simGroup = index / LOCAL_TARGET_SEARCH_GROUP_SIZE;
    return ((frameIndex + simGroup) % (uint)simInterval) == 0u;
}

// -----------------------------------------------------------------------------
// State model (authoritative; mirrored by AgentStateMachine.cs and requirements.md R10):
// - Alive states (Idle/Move/Engage/Attack) are RE-DERIVED every frame from the combat
//   situation by priority Attack > Engage > Move > Idle. There are no edge constraints
//   between alive states.
// - Dead has absolute priority, is entered exactly when hp <= 0, and is terminal.
// -----------------------------------------------------------------------------
int ResolveAliveState(bool inAttackHold, bool hasEngageTarget, bool hasMoveDirection)
{
    if (inAttackHold)
        return STATE_ATTACK;
    if (hasEngageTarget)
        return STATE_ENGAGE;
    if (hasMoveDirection)
        return STATE_MOVE;
    return STATE_IDLE;
}

float GetClipDurationForState(UnitTypeSettings settings, int state)
{
    if (state == STATE_DEAD)
        return max(settings.deathClipDuration, 0.0001);
    if (state == STATE_ATTACK)
        return max(settings.attackClipDuration, 0.0001);
    if (state == STATE_MOVE || state == STATE_ENGAGE)
        return max(settings.moveClipDuration, 0.0001);
    return max(settings.idleClipDuration, 0.0001);
}

// Advances the VAT time accumulator. Looping states wrap at their OWN clip duration so
// the wrap point is phase-aligned with the clip (no visual pop); Dead clamps at the end
// of the death clip and stays there.
void UpdateAnimationTime(uint index, inout AgentData agent, int interval)
{
    interval = max(interval, 1);
    if ((frameIndex % (uint)interval) != 0)
        return;

    UnitTypeSettings settings = GetUnitSettings(index);
    float duration = GetClipDurationForState(settings, agent.currentState);
    bool loop = agent.currentState != STATE_DEAD;

    float animationSpeed = 1.0;
    if (agent.currentState == STATE_MOVE || agent.currentState == STATE_ENGAGE)
    {
        float maxMoveSpeed = max(0.01, settings.maxSpeed);
        float speed01 = saturate(length(agent.velocity.xz) / maxMoveSpeed);
        animationSpeed = lerp(settings.moveAnimationSpeedMin, settings.moveAnimationSpeedMax, speed01);
    }

    float nextTime = agent.currentAnimationTime + deltaTime * interval * animationSpeed;
    agent.currentAnimationTime = loop ? fmod(nextTime, duration) : min(nextTime, duration);
}

void SetAgentState(inout AgentData agent, int state)
{
    if (agent.currentState != state)
        agent.currentAnimationTime = 0.0;

    agent.currentState = state;
}

bool IsLocomotionState(int state)
{
    return state == STATE_IDLE || state == STATE_MOVE || state == STATE_ENGAGE;
}

void SetCrowdState(inout AgentData agent, int state)
{
    if (agent.currentState == state)
        return;

    if (!IsLocomotionState(agent.currentState) || !IsLocomotionState(state))
        agent.currentAnimationTime = 0.0;

    agent.currentState = state;
}

int2 PositionXzToCell(float2 positionXZ)
{
    float2 local = (positionXZ - gridOrigin) / max(cellSize, 0.0001);
    int2 cell = (int2)floor(local);
    cell.x = clamp(cell.x, 0, gridResolution.x - 1);
    cell.y = clamp(cell.y, 0, gridResolution.y - 1);
    return cell;
}

int2 PositionToCell(float3 position)
{
    return PositionXzToCell(position.xz);
}

uint CellToIndex(int2 cell)
{
    return (uint)(cell.y * gridResolution.x + cell.x);
}

int2 PositionXzToFlowFieldCell(float2 positionXZ)
{
    float2 local = (positionXZ - flowFieldOrigin) / max(flowFieldCellSize, 0.0001);
    int2 cell = (int2)floor(local);
    cell.x = clamp(cell.x, 0, flowFieldResolution.x - 1);
    cell.y = clamp(cell.y, 0, flowFieldResolution.y - 1);
    return cell;
}

int2 PositionToFlowFieldCell(float3 position)
{
    return PositionXzToFlowFieldCell(position.xz);
}

uint FlowFieldCellToIndex(int2 cell)
{
    return (uint)(cell.y * flowFieldResolution.x + cell.x);
}

uint FlowFieldCellCount()
{
    return (uint)max(1, flowFieldResolution.x * flowFieldResolution.y);
}

float2 FlowFieldCellCenter(int2 cell)
{
    return flowFieldOrigin + ((float2)cell + 0.5) * flowFieldCellSize;
}

int2 PositionXzToDefenderFlowFieldCell(float2 positionXZ)
{
    float2 local = (positionXZ - defenderFlowFieldOrigin) / max(defenderFlowFieldCellSize, 0.0001);
    int2 cell = (int2)floor(local);
    cell.x = clamp(cell.x, 0, defenderFlowFieldResolution.x - 1);
    cell.y = clamp(cell.y, 0, defenderFlowFieldResolution.y - 1);
    return cell;
}

int2 PositionToDefenderFlowFieldCell(float3 position)
{
    return PositionXzToDefenderFlowFieldCell(position.xz);
}

uint DefenderFlowFieldCellToIndex(int2 cell)
{
    return (uint)(cell.y * defenderFlowFieldResolution.x + cell.x);
}

uint DefenderFlowFieldCellCount()
{
    return (uint)max(1, defenderFlowFieldResolution.x * defenderFlowFieldResolution.y);
}

float2 DefenderFlowFieldCellCenter(int2 cell)
{
    return defenderFlowFieldOrigin + ((float2)cell + 0.5) * defenderFlowFieldCellSize;
}

float2 SampleFlowDirection(uint index, float3 position)
{
    if (flowFieldEnabled == 0 || GetUnitSettings(index).flowFieldWeight <= 0.0)
        return 0.0;

    float2 direction = flowFieldDirectionsReadBuffer[FlowFieldCellToIndex(PositionToFlowFieldCell(position))];
    float lengthSqr = dot(direction, direction);
    if (lengthSqr <= 0.0001)
        return 0.0;

    if (lengthSqr > 1.0)
        direction *= rsqrt(lengthSqr);

    return direction;
}

float2 SampleDefenderFlowDirection(uint index, float3 position)
{
    if (defenderFlowFieldEnabled == 0 || GetUnitSettings(index).flowFieldWeight <= 0.0)
        return 0.0;

    float2 direction = defenderFlowFieldDirectionsReadBuffer[DefenderFlowFieldCellToIndex(PositionToDefenderFlowFieldCell(position))];
    float lengthSqr = dot(direction, direction);
    if (lengthSqr <= 0.0001)
        return 0.0;

    if (lengthSqr > 1.0)
        direction *= rsqrt(lengthSqr);

    return direction;
}

float2 FallbackDirection(uint id)
{
    uint h = id * 1664525u + 1013904223u;
    float angle = (float)(h & 65535u) * (6.2831853 / 65535.0);
    return float2(cos(angle), sin(angle));
}

float Hash01(uint seed)
{
    seed ^= seed >> 16;
    seed *= 2246822519u;
    seed ^= seed >> 13;
    seed *= 3266489917u;
    seed ^= seed >> 16;
    return (float)(seed & 0x00FFFFFFu) / 16777215.0;
}

float SignedHash01(uint seed)
{
    return Hash01(seed) * 2.0 - 1.0;
}

uint SampleDensityCell(int2 cell)
{
    cell.x = clamp(cell.x, 0, flowFieldResolution.x - 1);
    cell.y = clamp(cell.y, 0, flowFieldResolution.y - 1);
    return densityMap[cell];
}

uint SampleFriendlyDensityCell(uint selfIndex, int2 cell)
{
    cell.x = clamp(cell.x, 0, flowFieldResolution.x - 1);
    cell.y = clamp(cell.y, 0, flowFieldResolution.y - 1);

    int team = teamIdReadBuffer[selfIndex];
    uint result = densityMap[cell];
    if (team == attackerTeamId)
        result = attackerDensityMap[cell];
    else if (team == defenderTeamId)
        result = defenderDensityMap[cell];

    // The current battle path is two-team, but keep non-standard/editor teams safe.
    return result;
}

float2 SampleFriendlyDensityGradientCell(uint selfIndex, int2 cell)
{
    float densityL = (float)SampleFriendlyDensityCell(selfIndex, cell + int2(-1, 0));
    float densityR = (float)SampleFriendlyDensityCell(selfIndex, cell + int2(1, 0));
    float densityD = (float)SampleFriendlyDensityCell(selfIndex, cell + int2(0, -1));
    float densityU = (float)SampleFriendlyDensityCell(selfIndex, cell + int2(0, 1));
    return float2(densityR - densityL, densityU - densityD) * 0.5;
}

int2 DirectionToCellStep(float2 direction)
{
    float lenSqr = dot(direction, direction);
    if (lenSqr <= 0.0001)
        return int2(0, 0);

    float2 unitDirection = direction * rsqrt(lenSqr);
    int2 step = (int2)round(unitDirection);
    if (step.x == 0 && step.y == 0)
    {
        if (abs(unitDirection.x) > abs(unitDirection.y))
            step.x = unitDirection.x >= 0.0 ? 1 : -1;
        else
            step.y = unitDirection.y >= 0.0 ? 1 : -1;
    }

    return step;
}

float SampleAheadFriendlyDensity(uint selfIndex, int2 cell, float2 desiredDirection)
{
    // Single result variable: keeps fxc's flow analysis happy after inlining
    // (the early-return form triggered a spurious uninitialized-variable warning).
    float result = (float)SampleFriendlyDensityCell(selfIndex, cell);
    int2 ahead = DirectionToCellStep(desiredDirection);
    if (ahead.x != 0 || ahead.y != 0)
    {
        int2 side = DirectionToCellStep(float2(-desiredDirection.y, desiredDirection.x));
        float aheadCenter = (float)SampleFriendlyDensityCell(selfIndex, cell + ahead);
        float aheadLeft = (float)SampleFriendlyDensityCell(selfIndex, cell + ahead + side);
        float aheadRight = (float)SampleFriendlyDensityCell(selfIndex, cell + ahead - side);
        result = (aheadCenter * 2.0 + aheadLeft + aheadRight) * 0.25;
    }

    return result;
}

float FriendlyNavigationCost(uint selfIndex, float3 position, float2 direction)
{
    float lenSqr = dot(direction, direction);
    if (lenSqr <= 0.0001)
        return 1e20;

    float2 unitDirection = direction * rsqrt(lenSqr);
    float lookAhead = max(0.5, flowFieldCellSize);
    int2 nearCell = PositionToFlowFieldCell(position + float3(unitDirection.x * lookAhead, 0.0, unitDirection.y * lookAhead));
    int2 farCell = PositionToFlowFieldCell(position + float3(unitDirection.x * lookAhead * 2.0, 0.0, unitDirection.y * lookAhead * 2.0));
    return (float)SampleFriendlyDensityCell(selfIndex, nearCell) +
           (float)SampleFriendlyDensityCell(selfIndex, farCell) * 0.45;
}

float2 ApplyFriendlyDensityNavigation(uint selfIndex, float3 position, float2 navigationDirection)
{
    float lenSqr = dot(navigationDirection, navigationDirection);
    if (lenSqr <= 0.0001)
        return navigationDirection;

    float2 forward = navigationDirection * rsqrt(lenSqr);
    float2 side = float2(-forward.y, forward.x);
    // Roughly +/- 38 degrees: enough to move into a neighboring lane without
    // overriding the global flow field or turning a marching column backwards.
    float2 left = normalize(forward * 0.79 + side * 0.61);
    float2 right = normalize(forward * 0.79 - side * 0.61);
    float forwardCost = FriendlyNavigationCost(selfIndex, position, forward);
    float leftCost = FriendlyNavigationCost(selfIndex, position, left) + 0.65;
    float rightCost = FriendlyNavigationCost(selfIndex, position, right) + 0.65;

    // A stable per-agent epsilon breaks perfectly symmetric queues without flicker.
    float lanePreference = SignedHash01(selfIndex ^ 0xA24BAED5u) * 0.04;
    leftCost += lanePreference;
    rightCost -= lanePreference;

    float2 bestDirection = forward;
    float bestCost = forwardCost;
    if (leftCost < bestCost)
    {
        bestCost = leftCost;
        bestDirection = left;
    }
    if (rightCost < bestCost)
    {
        bestCost = rightCost;
        bestDirection = right;
    }

    float congestionGain = saturate((forwardCost - bestCost) / max(1.0, forwardCost));
    float2 blended = lerp(forward, bestDirection, congestionGain * 0.78);
    float blendedLenSqr = dot(blended, blended);
    return blendedLenSqr > 0.0001 ? blended * rsqrt(blendedLenSqr) : forward;
}

struct DensityPressureSample
{
    float2 avoidance;
    float speedScale;
    float pressure;
    float centerPressure;
    float aheadPressure;
};

DensityPressureSample ComputeDensityPressure(uint selfIndex, float3 position, float2 desiredDirection)
{
    DensityPressureSample result;
    result.avoidance = 0.0;
    result.speedScale = 1.0;
    result.pressure = 0.0;
    result.centerPressure = 0.0;
    result.aheadPressure = 0.0;

    UnitTypeSettings settings = GetUnitSettings(selfIndex);
    int2 cell = PositionToFlowFieldCell(position);
    // Density thresholds are authored in agents/m2 so they stay valid no matter how
    // the flow-field cell size is retuned (a per-cell threshold silently saturated
    // to 1.0 everywhere when cells grew from 2m to 5m).
    float cellArea = max(0.25, flowFieldCellSize * flowFieldCellSize);
    float comfort = max(0.0, settings.densityComfortPerSqm);
    float range = max(0.01, settings.densityPressureRangePerSqm);
    // Friendly pressure must remain strong while approaching an enemy formation. The
    // previous all-agent map treated the enemy front as a wall, forcing the combat path
    // to globally weaken avoidance and consequently allowing friendly ranks to overlap.
    float centerDensity = (float)SampleFriendlyDensityCell(selfIndex, cell) / cellArea;
    float centerPressure = saturate((centerDensity - comfort) / range);
    float aheadPressure = saturate((SampleAheadFriendlyDensity(selfIndex, cell, desiredDirection) / cellArea - comfort) / range);
    float pressure = max(centerPressure, aheadPressure);
    result.pressure = pressure;
    result.centerPressure = centerPressure;
    result.aheadPressure = aheadPressure;

    float penalty = saturate(settings.densitySpeedPenalty);
    result.speedScale = saturate(1.0 - pressure * penalty);

    float strength = max(0.0, settings.densityAvoidanceStrength);
    float2 gradient = SampleFriendlyDensityGradientCell(selfIndex, cell);
    float gradLenSqr = dot(gradient, gradient);
    if (pressure > 0.0 && strength > 0.0 && gradLenSqr > 0.0001)
        result.avoidance = -normalize(gradient) * strength * pressure;

    float desiredLenSqr = dot(desiredDirection, desiredDirection);
    if (aheadPressure > 0.0 && strength > 0.0 && desiredLenSqr > 0.0001)
    {
        float2 forward = desiredDirection * rsqrt(desiredLenSqr);
        float2 side = float2(-forward.y, forward.x);
        float lane = SignedHash01(selfIndex ^ 0xD1B54A35u);
        result.avoidance += side * lane * strength * aheadPressure * 0.65;
    }

    return result;
}

float AgentSpeedMultiplier(uint agentId, uint selfIndex)
{
    float variation = saturate(GetUnitSettings(selfIndex).speedVariation);
    return 1.0 + SignedHash01(agentId ^ 0x9E3779B9u) * variation;
}

float2 ApplyStableLaneBias(uint agentId, uint selfIndex, float2 direction, float pressure)
{
    float lenSqr = dot(direction, direction);
    if (lenSqr <= 0.0001)
        return direction;

    float strength = saturate(GetUnitSettings(selfIndex).laneBiasStrength);
    if (strength <= 0.0)
        return direction * rsqrt(lenSqr);

    float2 unitDirection = direction * rsqrt(lenSqr);
    float2 side = float2(-unitDirection.y, unitDirection.x);
    float lane = SignedHash01(agentId ^ 0x85EBCA6Bu);
    float laneScale = strength * lerp(0.35, 1.0, saturate(pressure));
    return normalize(unitDirection + side * lane * laneScale);
}

float3 HsvToRgb(float3 hsv)
{
    float4 k = float4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
    float3 p = abs(frac(hsv.xxx + k.xyz) * 6.0 - k.www);
    return hsv.z * lerp(k.xxx, saturate(p - k.xxx), hsv.y);
}

float3 FlowDirectionToPreviewColor(float2 direction)
{
    float magnitude = saturate(length(direction));
    if (magnitude <= 0.0001)
        return float3(0.03, 0.03, 0.03);

    float hue = atan2(direction.y, direction.x) / 6.2831853;
    if (hue < 0.0)
        hue += 1.0;

    float saturation = lerp(0.35, 0.9, magnitude);
    float value = lerp(0.28, 0.88, magnitude);
    return HsvToRgb(float3(hue, saturation, value));
}

float GetAgentRadius(uint index)
{
    return GetUnitSettings(index).agentRadius;
}

float GetSeparationStrength(uint index)
{
    return GetUnitSettings(index).separationStrength;
}

float GetVelocityDamping(uint index)
{
    return GetUnitSettings(index).velocityDamping;
}

float GetMaxSpeed(uint index)
{
    return GetUnitSettings(index).maxSpeed;
}

float GetTargetAcquireRadius(uint index)
{
    return GetUnitSettings(index).targetAcquireRadius;
}

int GetLocalTargetSearchCellRadius(float maxTargetRadius)
{
    int radiusFromDistance = (int)ceil(max(0.0, maxTargetRadius) / max(cellSize, 0.0001));
    int configuredLimit = clamp(localTargetSearchCellRadius, 1, LOCAL_TARGET_SEARCH_MAX_CELL_RADIUS);
    return clamp(radiusFromDistance, 1, configuredLimit);
}

// Search cadence counts DECISION passes, not global frames, so it stays aligned with
// the LOD simulation interval for any value (a frame-based gate silently stretched the
// re-target period to lcm(simInterval, 4) frames for non-power-of-two intervals).
bool ShouldSearchForLocalTarget(uint selfIndex, int simInterval)
{
    uint groupIndex = selfIndex / LOCAL_TARGET_SEARCH_GROUP_SIZE;
    uint interval = (uint)max(1, simInterval);
    uint decisionIndex = (frameIndex + groupIndex) / interval;
    uint cadence = max(1u, LOCAL_TARGET_SEARCH_INTERVAL / interval);
    return (decisionIndex % cadence) == 0u;
}

float GetAttackRange(uint index)
{
    UnitTypeSettings settings = GetUnitSettings(index);
    // 统一使用有效射程：远程单位用 projectileRange，近战单位用 attackRange
    return settings.projectileRange > 0.01 ? settings.projectileRange : settings.attackRange;
}

int GetAttackDamage(uint index)
{
    return GetUnitSettings(index).attackDamage;
}

float GetAttackInterval(uint index)
{
    return GetUnitSettings(index).attackInterval;
}

float AttackExitRange(uint selfIndex)
{
    float effectiveRange = GetAttackRange(selfIndex);
    return effectiveRange + max(0.25, GetAgentRadius(selfIndex) * 0.75);
}

float3 ClampVelocity(uint selfIndex, float3 velocity)
{
    velocity.y = 0.0;
    float speedSqr = dot(velocity.xz, velocity.xz);
    float maxSpeed = GetMaxSpeed(selfIndex);
    float maxSpeedSqr = maxSpeed * maxSpeed;
    if (speedSqr > maxSpeedSqr && speedSqr > 0.0001)
        velocity.xz *= maxSpeed * rsqrt(speedSqr);
    return velocity;
}

void ApplyBounds(inout AgentData agent)
{
    float2 minXZ = gridOrigin + boundaryPadding;
    float2 maxXZ = gridOrigin + gridWorldSize - boundaryPadding;

    if (agent.position.x < minXZ.x)
    {
        agent.position.x = minXZ.x;
        agent.velocity.x = abs(agent.velocity.x) * 0.25;
    }
    else if (agent.position.x > maxXZ.x)
    {
        agent.position.x = maxXZ.x;
        agent.velocity.x = -abs(agent.velocity.x) * 0.25;
    }

    if (agent.position.z < minXZ.y)
    {
        agent.position.z = minXZ.y;
        agent.velocity.z = abs(agent.velocity.z) * 0.25;
    }
    else if (agent.position.z > maxXZ.y)
    {
        agent.position.z = maxXZ.y;
        agent.velocity.z = -abs(agent.velocity.z) * 0.25;
    }
}

void FaceDirection(inout AgentData agent, float2 direction)
{
    if (dot(direction, direction) <= 0.0001)
        return;

    agent.rotation.y = degrees(atan2(direction.x, direction.y));
}

float NormalizeAngleDeltaDegrees(float angle)
{
    return angle - 360.0 * floor((angle + 180.0) / 360.0);
}

void SmoothFaceDirectionDt(inout AgentData agent, float2 direction, float deadZoneDistance, float maxDegreesPerSecond, float dt)
{
    float lenSqr = dot(direction, direction);
    float deadZoneSqr = deadZoneDistance * deadZoneDistance;
    if (lenSqr <= max(0.0001, deadZoneSqr))
        return;

    float targetYaw = degrees(atan2(direction.x, direction.y));
    float deltaYaw = NormalizeAngleDeltaDegrees(targetYaw - agent.rotation.y);
    float maxStep = max(1.0, maxDegreesPerSecond) * dt;
    agent.rotation.y += clamp(deltaYaw, -maxStep, maxStep);
}

void SmoothFaceDirection(inout AgentData agent, float2 direction, float deadZoneDistance, float maxDegreesPerSecond)
{
    SmoothFaceDirectionDt(agent, direction, deadZoneDistance, maxDegreesPerSecond, SafeDt());
}

// Alive test against last frame's hp snapshot. All kernels (including the combat kernel
// when it inspects OTHER agents) use the snapshot; only the combat kernel resolves its
// OWN hp for this frame. An agent killed this frame is therefore observed alive by its
// neighbours until next frame — a deterministic one-frame latency.
bool IsAliveIndex(uint index)
{
    return hpReadBuffer[index] > 0;
}

bool IsEnemy(uint selfIndex, uint otherIndex)
{
    if (enableTwoTeamCombat == 0)
        return false;

    return teamIdReadBuffer[selfIndex] != teamIdReadBuffer[otherIndex];
}

bool IsDefenderTeam(uint index)
{
    return enableTwoTeamCombat != 0 && teamIdReadBuffer[index] == defenderTeamId;
}

// retainExisting: true when validating an agent's CURRENT target (hysteresis applies),
// false for fresh acquisition (strict gates). Hold defenders keep an engaged target out
// to AttackExitRange, mirroring the attacker-side hysteresis — without it, crowd jitter
// across the exact attackRange line produced systematic one-sided trades.
bool TargetIsUsable(uint selfIndex, uint otherIndex, float distSqr, float3 selfPosition, bool retainExisting)
{
    if (selfIndex == otherIndex || !IsAliveIndex(otherIndex) || !IsEnemy(selfIndex, otherIndex))
        return false;

    if (IsDefenderTeam(selfIndex))
    {
        if (defenderMovementMode == DEFENDER_MODE_HOLD_POSITION)
        {
            float holdRange = retainExisting ? AttackExitRange(selfIndex) : GetAttackRange(selfIndex);
            return distSqr <= holdRange * holdRange;
        }

        // FLOW_FIELD defenders: aggro-radius only. A spawn-anchored chase leash broke
        // this doctrine outright (defenders relocated by their flow field could never
        // acquire targets again); leashing belongs to a future explicit doctrine, not here.
        float defenderAcquireRadius = GetTargetAcquireRadius(selfIndex);
        float aggroSqr = defenderAcquireRadius * defenderAcquireRadius;
        return distSqr <= aggroSqr;
    }

    float acquireRadius = GetTargetAcquireRadius(selfIndex);
    float acquireSqr = acquireRadius * acquireRadius;
    return distSqr <= acquireSqr;
}

bool CurrentTargetIsValid(uint selfIndex, AgentData self, int targetIndex)
{
    if (targetIndex < 0)
        return false;

    uint otherIndex = (uint)targetIndex;
    float2 delta = agentPositionReadBuffer[otherIndex] - self.position.xz;
    bool retainExisting = self.currentState == STATE_ATTACK;
    return TargetIsUsable(selfIndex, otherIndex, dot(delta, delta), self.position, retainExisting);
}

float2 ConfiguredFlowTargetOffset(float2 position, int targetMode, float4 targetPoint, float4 targetArea)
{
    if (targetMode == FLOW_TARGET_POINT)
        return targetPoint.xy - position;

    if (targetMode == FLOW_TARGET_AREA)
    {
        float2 halfSize = max(targetArea.zw, 0.0) * 0.5;
        float2 closest = clamp(position, targetArea.xy - halfSize, targetArea.xy + halfSize);
        return closest - position;
    }

    return 0.0;
}

struct NeighborhoodQueryResult
{
    int bestEnemyIndex;
    float bestEnemyScore;
    float2 separation;
};

uint CurrentEngagementOccupancy(uint targetIndex, uint slot)
{
    uint packed = engagementSlotOccupancyReadBuffer[targetIndex * ENGAGEMENT_SLOT_COUNT + slot];
    uint stamp = frameIndex & 0x00FFFFFFu;
    return (packed >> 8) == stamp ? packed & 0xFFu : 0u;
}

uint CurrentTargetLoad(uint targetIndex)
{
    uint load = 0u;
    [unroll]
    for (uint slot = 0u; slot < ENGAGEMENT_SLOT_COUNT; slot++)
        load += CurrentEngagementOccupancy(targetIndex, slot);
    return load;
}

float TargetLoadCapacity(uint selfIndex, uint targetIndex)
{
    float selfRadius = max(0.05, GetAgentRadius(selfIndex));
    float targetRadius = max(0.05, GetAgentRadius(targetIndex));
    float attackRange = max(0.1, GetAttackRange(selfIndex));
    float engagementRadius = min(max((selfRadius + targetRadius) * 0.8, attackRange * 0.72), attackRange * 0.86);
    float circumference = 6.2831853 * max(engagementRadius, selfRadius + targetRadius);
    float geometricCapacity = floor(circumference / max(0.1, selfRadius * 2.0));
    return clamp(geometricCapacity, 1.0, (float)ENGAGEMENT_SLOT_COUNT);
}

float TargetLoadRatio(uint selfIndex, uint targetIndex)
{
    return (float)CurrentTargetLoad(targetIndex) / TargetLoadCapacity(selfIndex, targetIndex);
}

float TargetSelectionScore(uint selfIndex, uint targetIndex, float distSqr)
{
    float distanceScore = sqrt(max(0.0, distSqr)) / max(0.1, GetTargetAcquireRadius(selfIndex));
    float loadRatio = TargetLoadRatio(selfIndex, targetIndex);
    float loadPenalty = saturate(loadRatio) * 0.30 + max(0.0, loadRatio - 1.0) * 0.15;

    // A stable per-agent affinity breaks up synchronized switching when many agents
    // observe the same one-frame-old load snapshot. Distance still dominates when
    // candidates are meaningfully separated.
    float affinity = Hash01(selfIndex ^ (targetIndex * 0x9E3779B9u)) * 0.45;
    return distanceScore + loadPenalty + affinity;
}

NeighborhoodQueryResult QueryCombatNeighborhood(uint selfIndex, AgentData agent, float maxTargetRadius, bool searchForEnemy)
{
    float2 selfPosition = agent.position.xz;
    int2 homeCell = PositionXzToCell(selfPosition);
    float selfRadius = GetAgentRadius(selfIndex);
    int queryCellRadius = searchForEnemy ? GetLocalTargetSearchCellRadius(maxTargetRadius) : 1;

    NeighborhoodQueryResult result;
    result.bestEnemyIndex = -1;
    result.bestEnemyScore = 1e20;
    result.separation = 0.0;
    uint enemyTeamSlot = IsDefenderTeam(selfIndex) ? 0u : 1u;

    [loop]
    for (int dz = -queryCellRadius; dz <= queryCellRadius; dz++)
    {
        [loop]
        for (int dx = -queryCellRadius; dx <= queryCellRadius; dx++)
        {
            int2 cell = homeCell + int2(dx, dz);
            if (cell.x < 0 || cell.y < 0 || cell.x >= gridResolution.x || cell.y >= gridResolution.y)
                continue;

            uint cellIndex = CellToIndex(cell);
            if (searchForEnemy)
            {
                uint enemyCellIndex = enemyTeamSlot * gridCellCount + cellIndex;
                uint enemyCount = min(teamGridCountsReadBuffer[enemyCellIndex], maxAgentsPerCell);
                for (uint i = 0; i < enemyCount; i++)
                {
                    uint otherIndex = teamGridAgentIndicesReadBuffer[enemyCellIndex * maxAgentsPerCell + i];
                    if (!IsAliveIndex(otherIndex))
                        continue;

                    float2 toOther = agentPositionReadBuffer[otherIndex] - selfPosition;
                    float distSqr = dot(toOther, toOther);
                    if (TargetIsUsable(selfIndex, otherIndex, distSqr, agent.position, false))
                    {
                        float score = TargetSelectionScore(selfIndex, otherIndex, distSqr);
                        if (score < result.bestEnemyScore)
                        {
                            result.bestEnemyScore = score;
                            result.bestEnemyIndex = (int)otherIndex;
                        }
                    }
                }
            }

            if (abs(dx) > 1 || abs(dz) > 1)
                continue;

            uint occupantCount = min(gridCountsReadBuffer[cellIndex], maxAgentsPerCell);
            for (uint i = 0; i < occupantCount; i++)
            {
                uint otherIndex = gridAgentIndicesReadBuffer[cellIndex * maxAgentsPerCell + i];
                if (otherIndex == selfIndex || !IsAliveIndex(otherIndex))
                    continue;

                float2 otherPosition = agentPositionReadBuffer[otherIndex];
                float2 toOther = otherPosition - selfPosition;
                float distSqr = dot(toOther, toOther);
                float minDistance = selfRadius + GetAgentRadius(otherIndex);
                float minDistanceSqr = minDistance * minDistance;
                if (distSqr >= minDistanceSqr)
                    continue;

                float2 separationDelta = selfPosition - otherPosition;
                if (distSqr < 0.000001)
                {
                    separationDelta = FallbackDirection(selfIndex) * 0.001;
                    distSqr = dot(separationDelta, separationDelta);
                }

                float dist = max(sqrt(max(distSqr, 0.000001)), 0.0001);
                result.separation += (separationDelta / dist) * (minDistance - dist);
            }
        }
    }

    return result;
}

// Classifies ONE unit type per dispatch (classifyUnitTypeIndex). Animation time is
// advanced here exactly once per agent per frame because every agent belongs to exactly
// one unit type.
void AppendVisibleAgentForUnitType(uint index, inout AgentData agent)
{
    float3 offset = agent.position - lodCenter;
    offset.y = 0.0;
    float distSqr = dot(offset, offset);
    bool isNear = distSqr <= nearLodRadiusSqr;
    bool isMid = !isNear && distSqr <= midLodRadiusSqr;
    int animationInterval = isNear ? nearAnimationInterval : (isMid ? midAnimationInterval : farAnimationInterval);
    // Without farIncludeDead the mid->far boundary is a hard corpse pop line: bodies
    // render inside it and vanish beyond it as the camera moves.
    bool includeFar = farIncludeDead != 0 || IsAliveIndex(index);

    UpdateAnimationTime(index, agent, animationInterval);

    // The near ring is the only shadow-casting tier and the same visible list feeds
    // every camera and the ShadowCaster pass: culling it against the main camera's
    // frustum makes shadows of off-screen/behind-camera units pop at the screen edge.
    // Its instance count is bounded by pi*nearRadius^2*density, so exempting it from
    // both distance and frustum culling is cheap; mid/far stay culled.
    if (!isNear)
    {
        // Visibility budget: on km-scale battlefields agents beyond this range are
        // sub-pixel; dropping them caps the worst-case visible instance count.
        if (maxRenderDistanceSqr > 0.0 && distSqr > maxRenderDistanceSqr)
            return;

        if (!IsInsideFrustum(agent.position))
            return;
    }

    if (isNear)
        nearVisibleAgentIndices.Append(index);
    else if (isMid)
        midVisibleAgentIndices.Append(index);
    else if (includeFar)
        farVisibleAgentIndices.Append(index);
}
