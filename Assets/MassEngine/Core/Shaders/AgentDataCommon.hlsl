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

struct AgentData
{
    float3 position;
    float3 rotation;
    float3 scale;
    float3 velocity;
    int currentState;
    float currentAnimationTime;
};

// Mirror of MassEngine.UnitTypeGpuSettings (112 bytes, sequential layout).
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
    int padding3;
    int teamId;
    int padding0;
    int padding1;
    int padding2;
};

RWStructuredBuffer<AgentData> agentBuffer;
StructuredBuffer<float2> agentPositionReadBuffer;
RWStructuredBuffer<float2> agentPositionBuffer;
RWStructuredBuffer<uint> gridCounts;
RWStructuredBuffer<uint> gridAgentIndices;
// [0] = agents dropped this frame because their cell was full (visibility for the
// silent failure mode where overflow victims vanish from neighborhood queries).
RWStructuredBuffer<int> spatialHashStats;
StructuredBuffer<uint> gridCountsReadBuffer;
StructuredBuffer<uint> gridAgentIndicesReadBuffer;
RWStructuredBuffer<float2> flowFieldDirections;
RWStructuredBuffer<float2> defenderFlowFieldDirections;
Texture2D<uint> densityMap;
RWTexture2D<uint> densityMapWrite;
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
RWStructuredBuffer<float> attackCooldownBuffer;
StructuredBuffer<float3> homePositionReadBuffer;
RWStructuredBuffer<int> pendingDamageBuffer;
StructuredBuffer<int> pendingDamageReadBuffer;

// LOD classification runs once per unit type; only the buffers of the unit type
// currently being classified are bound.
int classifyUnitTypeIndex;
AppendStructuredBuffer<uint> nearVisibleAgentIndices;
AppendStructuredBuffer<uint> midVisibleAgentIndices;
AppendStructuredBuffer<uint> farVisibleAgentIndices;

float deltaTime;
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

    float2 direction = flowFieldDirections[FlowFieldCellToIndex(PositionToFlowFieldCell(position))];
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

    float2 direction = defenderFlowFieldDirections[DefenderFlowFieldCellToIndex(PositionToDefenderFlowFieldCell(position))];
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

float2 SampleDensityGradientCell(int2 cell)
{
    float densityL = (float)SampleDensityCell(cell + int2(-1, 0));
    float densityR = (float)SampleDensityCell(cell + int2(1, 0));
    float densityD = (float)SampleDensityCell(cell + int2(0, -1));
    float densityU = (float)SampleDensityCell(cell + int2(0, 1));
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

float SampleAheadDensity(int2 cell, float2 desiredDirection)
{
    // Single result variable: keeps fxc's flow analysis happy after inlining
    // (the early-return form triggered a spurious uninitialized-variable warning).
    float result = (float)SampleDensityCell(cell);
    int2 ahead = DirectionToCellStep(desiredDirection);
    if (ahead.x != 0 || ahead.y != 0)
    {
        int2 side = DirectionToCellStep(float2(-desiredDirection.y, desiredDirection.x));
        float aheadCenter = (float)SampleDensityCell(cell + ahead);
        float aheadLeft = (float)SampleDensityCell(cell + ahead + side);
        float aheadRight = (float)SampleDensityCell(cell + ahead - side);
        result = (aheadCenter * 2.0 + aheadLeft + aheadRight) * 0.25;
    }

    return result;
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
    float centerDensity = (float)SampleDensityCell(cell) / cellArea;
    float centerPressure = saturate((centerDensity - comfort) / range);
    float aheadPressure = saturate((SampleAheadDensity(cell, desiredDirection) / cellArea - comfort) / range);
    float pressure = max(centerPressure, aheadPressure);
    result.pressure = pressure;
    result.centerPressure = centerPressure;
    result.aheadPressure = aheadPressure;

    float penalty = saturate(settings.densitySpeedPenalty);
    result.speedScale = saturate(1.0 - pressure * penalty);

    float strength = max(0.0, settings.densityAvoidanceStrength);
    float2 gradient = SampleDensityGradientCell(cell);
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
    return GetUnitSettings(index).attackRange;
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
    float attackRange = GetAttackRange(selfIndex);
    return attackRange + max(0.25, GetAgentRadius(selfIndex) * 0.75);
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
    int nearestEnemyIndex;
    float nearestEnemyDistSqr;
    float2 separation;
};

NeighborhoodQueryResult QueryCombatNeighborhood(uint selfIndex, AgentData agent, float maxTargetRadius, bool searchForEnemy)
{
    float2 selfPosition = agent.position.xz;
    int2 homeCell = PositionXzToCell(selfPosition);
    float selfRadius = GetAgentRadius(selfIndex);
    int queryCellRadius = searchForEnemy ? GetLocalTargetSearchCellRadius(maxTargetRadius) : 1;

    NeighborhoodQueryResult result;
    result.nearestEnemyIndex = -1;
    result.nearestEnemyDistSqr = maxTargetRadius * maxTargetRadius;
    result.separation = 0.0;

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
            uint occupantCount = min(gridCountsReadBuffer[cellIndex], maxAgentsPerCell);

            for (uint i = 0; i < occupantCount; i++)
            {
                uint otherIndex = gridAgentIndicesReadBuffer[cellIndex * maxAgentsPerCell + i];
                if (otherIndex == selfIndex || !IsAliveIndex(otherIndex))
                    continue;

                float2 otherPosition = agentPositionReadBuffer[otherIndex];
                float2 toOther = otherPosition - selfPosition;
                float distSqr = dot(toOther, toOther);

                if (searchForEnemy &&
                    distSqr < result.nearestEnemyDistSqr &&
                    TargetIsUsable(selfIndex, otherIndex, distSqr, agent.position, false))
                {
                    result.nearestEnemyDistSqr = distSqr;
                    result.nearestEnemyIndex = (int)otherIndex;
                }

                if (abs(dx) > 1 || abs(dz) > 1)
                    continue;

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
