// Shared declarations and helpers extracted from AgentComputeShader_Stage6.compute.
// Keep this file in sync with AgentDataContract_Stage6.md.

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

struct AgentData
{
    float3 position;
    float3 rotation;
    float3 scale;
    float3 velocity;
    int currentState;
    float currentAnimationTime;
};

RWStructuredBuffer<AgentData> agentBuffer;
StructuredBuffer<float2> agentPositionReadBuffer;
RWStructuredBuffer<float2> agentPositionBuffer;
RWStructuredBuffer<uint> gridCounts;
RWStructuredBuffer<uint> gridAgentIndices;
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

RWStructuredBuffer<int> hpBuffer;
StructuredBuffer<int> teamIdReadBuffer;
StructuredBuffer<int> hpReadBuffer;
RWStructuredBuffer<int> targetAgentIndexBuffer;
RWStructuredBuffer<float> attackCooldownBuffer;
StructuredBuffer<float3> homePositionReadBuffer;
RWStructuredBuffer<int> pendingDamageBuffer;
StructuredBuffer<int> pendingDamageReadBuffer;

AppendStructuredBuffer<uint> nearAttackerAgentIndices;
AppendStructuredBuffer<uint> midAttackerAgentIndices;
AppendStructuredBuffer<uint> farAttackerAgentIndices;
AppendStructuredBuffer<uint> nearDefenderAgentIndices;
AppendStructuredBuffer<uint> midDefenderAgentIndices;
AppendStructuredBuffer<uint> farDefenderAgentIndices;

float deltaTime;
float animationDuration;
uint frameIndex;

float3 lodCenter;
float nearLodRadiusSqr;
float midLodRadiusSqr;

int enableFrustumCulling;
float cullingRadius;
float4 frustumPlanes[6];

int nearAnimationInterval;
int midAnimationInterval;
int farAnimationInterval;

uint gridCellCount;
int2 gridResolution;
float2 gridOrigin;
float2 gridWorldSize;
float cellSize;
uint maxAgentsPerCell;

float attackerAgentRadius;
float defenderAgentRadius;
float attackerSeparationStrength;
float defenderSeparationStrength;
float attackerVelocityDamping;
float defenderVelocityDamping;
float attackerMaxSpeed;
float defenderMaxSpeed;
int attackerFlowTargetMode;
float4 attackerFlowTargetPoint;
float4 attackerFlowTargetArea;
int defenderFlowTargetMode;
float4 defenderFlowTargetPoint;
float4 defenderFlowTargetArea;
float boundaryPadding;

int flowFieldEnabled;
int2 flowFieldResolution;
float2 flowFieldOrigin;
float flowFieldCellSize;
float flowFieldWeight;
float flowFieldResponsiveness;
uint separationSkipInterval;
float attackerDensityAvoidanceStrength;
float defenderDensityAvoidanceStrength;
int attackerDensityComfortCount;
int defenderDensityComfortCount;
float attackerDensityPressureRange;
float defenderDensityPressureRange;
float attackerDensitySpeedPenalty;
float defenderDensitySpeedPenalty;
float attackerSpeedVariation;
float defenderSpeedVariation;
float attackerLaneBiasStrength;
float defenderLaneBiasStrength;
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
int attackerCount;
float attackerTargetAcquireRadius;
float defenderTargetAcquireRadius;
float attackerAttackRange;
float defenderAttackRange;
int attackerAttackDamage;
int defenderAttackDamage;
float attackerAttackInterval;
float defenderAttackInterval;
float defenderGuardRadius;
float defenderMaxChaseDistance;
float deathClipDuration;

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

void UpdateAnimationTime(uint index, inout AgentData agent, int interval, float duration, bool loop)
{
    interval = max(interval, 1);
    if ((frameIndex % (uint)interval) != 0)
        return;

    float safeDuration = max(duration, 0.0001);
    float animationSpeed = 1.0;
    if (agent.currentState == STATE_MOVE || agent.currentState == STATE_ENGAGE)
    {
        bool defender = enableTwoTeamCombat != 0 && teamIdReadBuffer[index] == 1;
        float maxMoveSpeed = max(0.01, defender ? defenderMaxSpeed : attackerMaxSpeed);
        float speed01 = saturate(length(agent.velocity.xz) / maxMoveSpeed);
        animationSpeed = lerp(0.85, 1.15, speed01);
    }

    float nextTime = agent.currentAnimationTime + deltaTime * interval * animationSpeed;
    agent.currentAnimationTime = loop ? fmod(nextTime, safeDuration) : min(nextTime, safeDuration);
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

float2 SampleFlowDirection(float3 position)
{
    if (flowFieldEnabled == 0 || flowFieldWeight <= 0.0)
        return 0.0;

    float2 direction = flowFieldDirections[FlowFieldCellToIndex(PositionToFlowFieldCell(position))];
    float lengthSqr = dot(direction, direction);
    if (lengthSqr <= 0.0001)
        return 0.0;

    if (lengthSqr > 1.0)
        direction *= rsqrt(lengthSqr);

    return direction;
}

float2 SampleDefenderFlowDirection(float3 position)
{
    if (defenderFlowFieldEnabled == 0 || flowFieldWeight <= 0.0)
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

bool UsesDefenderCrowdSettings(uint index)
{
    return enableTwoTeamCombat != 0 && teamIdReadBuffer[index] == 1;
}

float GetDensityAvoidanceStrength(uint index)
{
    return UsesDefenderCrowdSettings(index) ? defenderDensityAvoidanceStrength : attackerDensityAvoidanceStrength;
}

float GetDensityComfortCount(uint index)
{
    return (float)(UsesDefenderCrowdSettings(index) ? defenderDensityComfortCount : attackerDensityComfortCount);
}

float GetDensityPressureRange(uint index)
{
    return UsesDefenderCrowdSettings(index) ? defenderDensityPressureRange : attackerDensityPressureRange;
}

float GetDensitySpeedPenalty(uint index)
{
    return UsesDefenderCrowdSettings(index) ? defenderDensitySpeedPenalty : attackerDensitySpeedPenalty;
}

float GetSpeedVariation(uint index)
{
    return UsesDefenderCrowdSettings(index) ? defenderSpeedVariation : attackerSpeedVariation;
}

float GetLaneBiasStrength(uint index)
{
    return UsesDefenderCrowdSettings(index) ? defenderLaneBiasStrength : attackerLaneBiasStrength;
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
    int2 ahead = DirectionToCellStep(desiredDirection);
    if (ahead.x == 0 && ahead.y == 0)
        return (float)SampleDensityCell(cell);

    int2 side = DirectionToCellStep(float2(-desiredDirection.y, desiredDirection.x));
    float aheadCenter = (float)SampleDensityCell(cell + ahead);
    float aheadLeft = (float)SampleDensityCell(cell + ahead + side);
    float aheadRight = (float)SampleDensityCell(cell + ahead - side);
    return (aheadCenter * 2.0 + aheadLeft + aheadRight) * 0.25;
}

struct DensityPressureSample
{
    float2 avoidance;
    float speedScale;
    float pressure;
};

DensityPressureSample ComputeDensityPressure(uint selfIndex, float3 position, float2 desiredDirection)
{
    DensityPressureSample result;
    result.avoidance = 0.0;
    result.speedScale = 1.0;
    result.pressure = 0.0;

    int2 cell = PositionToFlowFieldCell(position);
    float comfort = max(0.0, GetDensityComfortCount(selfIndex));
    float range = max(0.01, GetDensityPressureRange(selfIndex));
    float centerDensity = (float)SampleDensityCell(cell);
    float centerPressure = saturate((centerDensity - comfort) / range);
    float aheadPressure = saturate((SampleAheadDensity(cell, desiredDirection) - comfort) / range);
    float pressure = max(centerPressure, aheadPressure);
    result.pressure = pressure;

    float penalty = saturate(GetDensitySpeedPenalty(selfIndex));
    result.speedScale = saturate(1.0 - pressure * penalty);

    float strength = max(0.0, GetDensityAvoidanceStrength(selfIndex));
    float2 gradient = SampleDensityGradientCell(cell);
    float gradLenSqr = dot(gradient, gradient);
    if (pressure > 0.0 && strength > 0.0 && gradLenSqr > 0.0001)
        result.avoidance = -normalize(gradient) * strength * pressure;

    return result;
}

float AgentSpeedMultiplier(uint agentId, uint selfIndex)
{
    float variation = saturate(GetSpeedVariation(selfIndex));
    return 1.0 + SignedHash01(agentId ^ 0x9E3779B9u) * variation;
}

float2 ApplyStableLaneBias(uint agentId, uint selfIndex, float2 direction, float pressure)
{
    float lenSqr = dot(direction, direction);
    if (lenSqr <= 0.0001)
        return direction;

    float strength = saturate(GetLaneBiasStrength(selfIndex));
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

bool UsesDefenderSettings(uint index)
{
    return enableTwoTeamCombat != 0 && teamIdReadBuffer[index] == 1;
}

float GetAgentRadius(uint index)
{
    return UsesDefenderSettings(index) ? defenderAgentRadius : attackerAgentRadius;
}

float GetSeparationStrength(uint index)
{
    return UsesDefenderSettings(index) ? defenderSeparationStrength : attackerSeparationStrength;
}

float GetVelocityDamping(uint index)
{
    return UsesDefenderSettings(index) ? defenderVelocityDamping : attackerVelocityDamping;
}

float GetMaxSpeed(uint index)
{
    return UsesDefenderSettings(index) ? defenderMaxSpeed : attackerMaxSpeed;
}

float GetTargetAcquireRadius(uint index)
{
    return UsesDefenderSettings(index) ? defenderTargetAcquireRadius : attackerTargetAcquireRadius;
}

float GetAttackRange(uint index)
{
    return UsesDefenderSettings(index) ? defenderAttackRange : attackerAttackRange;
}

int GetAttackDamage(uint index)
{
    return UsesDefenderSettings(index) ? defenderAttackDamage : attackerAttackDamage;
}

float GetAttackInterval(uint index)
{
    return UsesDefenderSettings(index) ? defenderAttackInterval : attackerAttackInterval;
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

bool IsAliveIndex(uint index)
{
    return hpReadBuffer[index] > 0;
}

bool IsAliveIndexRw(uint index)
{
    return hpBuffer[index] > 0;
}

bool IsEnemy(uint selfIndex, uint otherIndex)
{
    if (enableTwoTeamCombat == 0)
        return false;

    return teamIdReadBuffer[selfIndex] != teamIdReadBuffer[otherIndex];
}

bool IsDefender(uint index)
{
    return enableTwoTeamCombat != 0 && teamIdReadBuffer[index] == 1;
}

bool TargetIsUsable(uint selfIndex, uint otherIndex, float distSqr, float3 selfPosition)
{
    if (selfIndex == otherIndex || !IsAliveIndexRw(otherIndex) || !IsEnemy(selfIndex, otherIndex))
        return false;

    if (IsDefender(selfIndex))
    {
        if (defenderMovementMode == DEFENDER_MODE_HOLD_POSITION)
        {
            float defenderRange = GetAttackRange(selfIndex);
            return distSqr <= defenderRange * defenderRange;
        }

        float defenderAcquireRadius = GetTargetAcquireRadius(selfIndex);
        float aggroSqr = defenderAcquireRadius * defenderAcquireRadius;
        if (defenderMovementMode == DEFENDER_MODE_FLOW_FIELD)
            return distSqr <= aggroSqr;

        float chaseSqr = defenderMaxChaseDistance * defenderMaxChaseDistance;
        float2 fromHome = selfPosition.xz - homePositionReadBuffer[selfIndex].xz;
        return distSqr <= aggroSqr && dot(fromHome, fromHome) <= chaseSqr;
    }

    float acquireRadius = GetTargetAcquireRadius(selfIndex);
    float acquireSqr = acquireRadius * acquireRadius;
    return distSqr <= acquireSqr;
}

int FindNearestEnemy(uint selfIndex, AgentData self, float maxRadius)
{
    float2 selfPosition = self.position.xz;
    int2 homeCell = PositionXzToCell(selfPosition);
    float bestDistSqr = maxRadius * maxRadius;
    int bestIndex = -1;

    [unroll]
    for (int dz = -1; dz <= 1; dz++)
    {
        [unroll]
        for (int dx = -1; dx <= 1; dx++)
        {
            int2 cell = homeCell + int2(dx, dz);
            if (cell.x < 0 || cell.y < 0 || cell.x >= gridResolution.x || cell.y >= gridResolution.y)
                continue;

            uint cellIndex = CellToIndex(cell);
            uint occupantCount = min(gridCountsReadBuffer[cellIndex], maxAgentsPerCell);

            for (uint i = 0; i < occupantCount; i++)
            {
                uint otherIndex = gridAgentIndicesReadBuffer[cellIndex * maxAgentsPerCell + i];
                float2 delta = agentPositionReadBuffer[otherIndex] - selfPosition;
                float distSqr = dot(delta, delta);

                if (distSqr < bestDistSqr && TargetIsUsable(selfIndex, otherIndex, distSqr, self.position))
                {
                    bestDistSqr = distSqr;
                    bestIndex = (int)otherIndex;
                }
            }
        }
    }

    return bestIndex;
}

bool CurrentTargetIsValid(uint selfIndex, AgentData self, int targetIndex)
{
    if (targetIndex < 0)
        return false;

    uint otherIndex = (uint)targetIndex;
    float2 delta = agentPositionReadBuffer[otherIndex] - self.position.xz;
    return TargetIsUsable(selfIndex, otherIndex, dot(delta, delta), self.position);
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

float2 AccumulateSeparation(uint selfIndex, AgentData agent)
{
    float2 selfPosition = agent.position.xz;
    int2 homeCell = PositionXzToCell(selfPosition);
    float selfRadius = GetAgentRadius(selfIndex);
    float2 separation = 0.0;

    [unroll]
    for (int dz = -1; dz <= 1; dz++)
    {
        [unroll]
        for (int dx = -1; dx <= 1; dx++)
        {
            int2 cell = homeCell + int2(dx, dz);
            if (cell.x < 0 || cell.y < 0 || cell.x >= gridResolution.x || cell.y >= gridResolution.y)
                continue;

            uint cellIndex = CellToIndex(cell);
            uint occupantCount = min(gridCountsReadBuffer[cellIndex], maxAgentsPerCell);

            for (uint i = 0; i < occupantCount; i++)
            {
                uint otherIndex = gridAgentIndicesReadBuffer[cellIndex * maxAgentsPerCell + i];
                if (otherIndex == selfIndex || !IsAliveIndexRw(otherIndex))
                    continue;

                float2 delta = selfPosition - agentPositionReadBuffer[otherIndex];
                float distSqr = dot(delta, delta);
                float minDistance = selfRadius + GetAgentRadius(otherIndex);
                float minDistanceSqr = minDistance * minDistance;
                if (distSqr >= minDistanceSqr)
                    continue;

                if (distSqr < 0.000001)
                {
                    delta = FallbackDirection(selfIndex) * 0.001;
                    distSqr = dot(delta, delta);
                }

                float dist = max(sqrt(max(distSqr, 0.000001)), 0.0001);
                separation += (delta / dist) * (minDistance - dist);
            }
        }
    }

    return separation;
}

struct NeighborhoodQueryResult
{
    int nearestEnemyIndex;
    float nearestEnemyDistSqr;
    float2 separation;
};

NeighborhoodQueryResult QueryCombatNeighborhood(uint selfIndex, AgentData agent, float maxTargetRadius)
{
    float2 selfPosition = agent.position.xz;
    int2 homeCell = PositionXzToCell(selfPosition);
    float selfRadius = GetAgentRadius(selfIndex);

    NeighborhoodQueryResult result;
    result.nearestEnemyIndex = -1;
    result.nearestEnemyDistSqr = maxTargetRadius * maxTargetRadius;
    result.separation = 0.0;

    [unroll]
    for (int dz = -1; dz <= 1; dz++)
    {
        [unroll]
        for (int dx = -1; dx <= 1; dx++)
        {
            int2 cell = homeCell + int2(dx, dz);
            if (cell.x < 0 || cell.y < 0 || cell.x >= gridResolution.x || cell.y >= gridResolution.y)
                continue;

            uint cellIndex = CellToIndex(cell);
            uint occupantCount = min(gridCountsReadBuffer[cellIndex], maxAgentsPerCell);

            for (uint i = 0; i < occupantCount; i++)
            {
                uint otherIndex = gridAgentIndicesReadBuffer[cellIndex * maxAgentsPerCell + i];
                if (otherIndex == selfIndex)
                    continue;

                float2 otherPosition = agentPositionReadBuffer[otherIndex];
                float2 toOther = otherPosition - selfPosition;
                float distSqr = dot(toOther, toOther);

                if (distSqr < result.nearestEnemyDistSqr && TargetIsUsable(selfIndex, otherIndex, distSqr, agent.position))
                {
                    result.nearestEnemyDistSqr = distSqr;
                    result.nearestEnemyIndex = (int)otherIndex;
                }

                if (!IsAliveIndexRw(otherIndex))
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

void AppendVisibleAgent(uint index, inout AgentData agent, float duration, bool loop, bool includeFar)
{
    bool isDefender = enableTwoTeamCombat != 0 && teamIdReadBuffer[index] == 1;
    float3 offset = agent.position - lodCenter;
    offset.y = 0.0;
    float distSqr = dot(offset, offset);
    bool isNear = distSqr <= nearLodRadiusSqr;
    bool isMid = !isNear && distSqr <= midLodRadiusSqr;
    int animationInterval = isNear ? nearAnimationInterval : (isMid ? midAnimationInterval : farAnimationInterval);

    UpdateAnimationTime(index, agent, animationInterval, duration, loop);

    if (!IsInsideFrustum(agent.position))
        return;

    if (isNear)
    {
        if (isDefender)
            nearDefenderAgentIndices.Append(index);
        else
            nearAttackerAgentIndices.Append(index);
    }
    else if (isMid)
    {
        if (isDefender)
            midDefenderAgentIndices.Append(index);
        else
            midAttackerAgentIndices.Append(index);
    }
    else if (includeFar)
    {
        if (isDefender)
            farDefenderAgentIndices.Append(index);
        else
            farAttackerAgentIndices.Append(index);
    }
}

