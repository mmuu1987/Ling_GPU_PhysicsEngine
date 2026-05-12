// Shared declarations and helpers extracted from AgentComputeShader_Stage6.compute.
// Keep this file in sync with AgentDataContract_Stage6.md.

#define STATE_IDLE 0
#define STATE_MOVE 1
#define STATE_ENGAGE 2
#define STATE_ATTACK 3
#define STATE_DEAD 4
#define DEFENDER_MODE_HOLD_POSITION 0
#define DEFENDER_MODE_FLOW_FIELD 1

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
int agentCount;
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
float boundaryPadding;

int flowFieldEnabled;
int2 flowFieldResolution;
float2 flowFieldOrigin;
float flowFieldCellSize;
float flowFieldWeight;
float flowFieldResponsiveness;
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

bool IsValidAgentIndex(uint index)
{
    return index < (uint)max(agentCount, 0);
}

float SafeDt()
{
    return min(deltaTime, 0.05);
}

void UpdateAnimationTime(inout AgentData agent, int interval, float duration, bool loop)
{
    interval = max(interval, 1);
    if ((frameIndex % (uint)interval) != 0)
        return;

    float safeDuration = max(duration, 0.0001);
    float nextTime = agent.currentAnimationTime + deltaTime * interval;
    agent.currentAnimationTime = loop ? fmod(nextTime, safeDuration) : min(nextTime, safeDuration);
}

void SetAgentState(inout AgentData agent, int state)
{
    if (agent.currentState != state)
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

int GetTeamId(uint index)
{
    return enableTwoTeamCombat != 0 ? teamIdReadBuffer[index] : 0;
}

bool IsDefenderTeam(int teamId)
{
    return enableTwoTeamCombat != 0 && teamId == 1;
}

float GetAgentRadiusForTeam(int teamId)
{
    return IsDefenderTeam(teamId) ? defenderAgentRadius : attackerAgentRadius;
}

float GetAgentRadius(uint index)
{
    return GetAgentRadiusForTeam(GetTeamId(index));
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
    return IsDefenderTeam(GetTeamId(index));
}

bool IsKnownEnemyTeam(int selfTeamId, int otherTeamId)
{
    return enableTwoTeamCombat != 0 && selfTeamId != otherTeamId;
}

bool IsDefenderWithinChaseDistance(uint selfIndex, bool selfIsDefender, float3 selfPosition)
{
    if (!selfIsDefender || defenderMovementMode == DEFENDER_MODE_HOLD_POSITION || defenderMovementMode == DEFENDER_MODE_FLOW_FIELD)
        return true;

    float chaseSqr = defenderMaxChaseDistance * defenderMaxChaseDistance;
    float2 fromHome = selfPosition.xz - homePositionReadBuffer[selfIndex].xz;
    return dot(fromHome, fromHome) <= chaseSqr;
}

bool TargetDistanceAllowed(
    bool selfIsDefender,
    float distSqr,
    float acquireRadiusSqr,
    float attackRangeSqr,
    bool defenderWithinChaseDistance)
{
    if (!selfIsDefender)
        return distSqr <= acquireRadiusSqr;

    if (defenderMovementMode == DEFENDER_MODE_HOLD_POSITION)
        return distSqr <= attackRangeSqr;

    if (defenderMovementMode == DEFENDER_MODE_FLOW_FIELD)
        return distSqr <= acquireRadiusSqr;

    return distSqr <= acquireRadiusSqr && defenderWithinChaseDistance;
}

bool TargetIsUsable(uint selfIndex, uint otherIndex, float distSqr, float3 selfPosition)
{
    if (selfIndex == otherIndex || !IsAliveIndexRw(otherIndex) || !IsEnemy(selfIndex, otherIndex))
        return false;

    int selfTeamId = GetTeamId(selfIndex);
    bool selfIsDefender = IsDefenderTeam(selfTeamId);
    float acquireRadius = GetTargetAcquireRadius(selfIndex);
    float attackRange = GetAttackRange(selfIndex);
    return TargetDistanceAllowed(
        selfIsDefender,
        distSqr,
        acquireRadius * acquireRadius,
        attackRange * attackRange,
        IsDefenderWithinChaseDistance(selfIndex, selfIsDefender, selfPosition));
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
    if (!IsValidAgentIndex(otherIndex) || !IsAliveIndexRw(otherIndex))
        return false;

    float2 delta = agentPositionReadBuffer[otherIndex] - self.position.xz;
    int selfTeamId = GetTeamId(selfIndex);
    int otherTeamId = GetTeamId(otherIndex);
    if (!IsKnownEnemyTeam(selfTeamId, otherTeamId))
        return false;

    bool selfIsDefender = IsDefenderTeam(selfTeamId);
    float acquireRadius = selfIsDefender ? defenderTargetAcquireRadius : attackerTargetAcquireRadius;
    float attackRange = selfIsDefender ? defenderAttackRange : attackerAttackRange;
    return TargetDistanceAllowed(
        selfIsDefender,
        dot(delta, delta),
        acquireRadius * acquireRadius,
        attackRange * attackRange,
        IsDefenderWithinChaseDistance(selfIndex, selfIsDefender, self.position));
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

NeighborhoodQueryResult QueryCombatNeighborhood(uint selfIndex, AgentData agent, float maxTargetRadius, bool searchForEnemy)
{
    float2 selfPosition = agent.position.xz;
    int2 homeCell = PositionXzToCell(selfPosition);
    int selfTeamId = GetTeamId(selfIndex);
    bool selfIsDefender = IsDefenderTeam(selfTeamId);
    float selfRadius = GetAgentRadiusForTeam(selfTeamId);
    float acquireRadiusSqr = maxTargetRadius * maxTargetRadius;
    float attackRange = selfIsDefender ? defenderAttackRange : attackerAttackRange;
    float attackRangeSqr = attackRange * attackRange;
    bool defenderWithinChaseDistance = IsDefenderWithinChaseDistance(selfIndex, selfIsDefender, agent.position);

    NeighborhoodQueryResult result;
    result.nearestEnemyIndex = -1;
    result.nearestEnemyDistSqr = acquireRadiusSqr;
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

                if (!IsAliveIndexRw(otherIndex))
                    continue;

                float2 otherPosition = agentPositionReadBuffer[otherIndex];
                float2 toOther = otherPosition - selfPosition;
                float distSqr = dot(toOther, toOther);
                int otherTeamId = GetTeamId(otherIndex);

                if (searchForEnemy &&
                    distSqr < result.nearestEnemyDistSqr &&
                    IsKnownEnemyTeam(selfTeamId, otherTeamId) &&
                    TargetDistanceAllowed(selfIsDefender, distSqr, acquireRadiusSqr, attackRangeSqr, defenderWithinChaseDistance))
                {
                    result.nearestEnemyDistSqr = distSqr;
                    result.nearestEnemyIndex = (int)otherIndex;
                }

                float minDistance = selfRadius + GetAgentRadiusForTeam(otherTeamId);
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

    UpdateAnimationTime(agent, animationInterval, duration, loop);

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

