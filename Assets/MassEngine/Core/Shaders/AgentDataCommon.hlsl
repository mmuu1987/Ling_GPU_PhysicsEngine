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
// Per-team stance, uploaded in teamStanceReadBuffer and indexed by raw teamId. Hold is 0
// so an un-uploaded buffer freezes everyone rather than marching teams that should not.
// GuardHome has no producer yet; it keeps the leash branch reachable for a future order.
#define TEAM_STANCE_HOLD 0
#define TEAM_STANCE_ADVANCE 1
#define TEAM_STANCE_GUARD_HOME 2
#define TEAM_STANCE_MOVE_ONLY 3
#define TEAM_STANCE_HOLD_HERE 4
#define FLOW_TARGET_NONE 0
#define FLOW_TARGET_POINT 1
#define FLOW_TARGET_AREA 2
// Per-team stride of the runtime flow scratch buffers. Stats: [0] = cells sampled,
// [1]/[2] = enemy position sum at 1m precision, [3] = sectors that met the quorum.
// Targets: one float4 per sector, so this doubles as the sector-count ceiling.
#define FLOW_STATS_PER_TEAM 4
#define FLOW_TARGETS_PER_TEAM 8
// Hard upper bound for the local target search radius (cells). The effective radius is
// min(localTargetSearchCellRadius, ceil(targetAcquireRadius / cellSize)) and is driven
// from C# so a configured acquire radius is never silently truncated without a warning.
#define LOCAL_TARGET_SEARCH_MAX_CELL_RADIUS 4
// Ranged area fire (Simulation/Shaders/RangedAreaFire.hlsl): ranged targets stay valid out to
// the full projectile range; ranged units pick them from coarse enemy blocks, not a local search.
#define RANGED_AREA_FIRE 1
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
    int presentationState;
    float locomotionSpeed;
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
    // Consumed by the CPU projectile launcher; kept here to mirror the 144-byte stride.
    float projectileTrailLength;
    int teamId;
    float moveReferenceSpeed;
    float moveStopSpeed;
    float moveStartSpeed;
    // Former reserved slots; C# / HLSL layout remains 36 x 4 = 144 bytes.
    float attackReleasePhase;
    float projectileOriginHeight;
    float projectileTargetHeight;
};

// One team's flow configuration, uploaded once per frame for every team.
//
// These used to be uniforms (attackerFlowTargetMode / defenderFlowTargetPoint / ...). They
// cannot stay uniforms past two teams: the combat kernel reads each agent's OWN team
// parameters, while a uniform would only ever hold the values of whichever team was
// dispatched last. That would silently steer every team with one team's target.
struct TeamFlowParams
{
    // x = targetMode (FLOW_TARGET_*), y = flow field enabled, z = dynamic targeting enabled,
    // w = minimum enemies a sector needs before it becomes a target.
    int4 modes;
    // xy = configured target point (world xz), z = dynamic target stop radius,
    // w = sector count.
    float4 target;
    // xy = configured target area center (world xz), zw = area size (world xz).
    float4 area;
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
// One fixed-size record per team, used by low-frequency telemetry. Per-team layout:
// alive count, sum X, sum Z, min X, min Z, max X, max Z, observation-zone count.
// Total length is teamCount * TEAM_STATS_SLOTS_PER_TEAM (mirrors MassGpuBufferManager).
#define TEAM_STATS_SLOTS_PER_TEAM 8
RWStructuredBuffer<int> teamSpatialStats;
int telemetryObservationZoneEnabled;
float4 telemetryObservationZone;
StructuredBuffer<uint> gridCountsReadBuffer;
StructuredBuffer<uint> gridAgentIndicesReadBuffer;
StructuredBuffer<uint> teamGridCountsReadBuffer;
StructuredBuffer<uint> teamGridAgentIndicesReadBuffer;
// C1 large bodies (2026-10-12, UEBS-style footprint stamping). Agents whose body radius exceeds
// half a grid cell cannot be seen reliably through the 3x3 cell scan, so BuildSpatialHash also
// stamps each of them into EVERY cell its reach disk (body radius + half a cell) touches. A normal
// agent then finds every large body it can touch in its OWN cell's large slots: one extra count read,
// no second grid and no wider scan. The cost sits on the few large agents, which write many cells.
// Layout: one record of LARGE_CELL_STRIDE uints per cell, [0] = stamp count, [1..8] = agent indices
// (count and slots share one buffer because BuildSpatialHash must stay within D3D11.0's 8 UAVs).
// Frame stats reuse the formerly reserved spatialHashStats slots: [1] large agents stamped this
// frame, [2] largest large body radius (asint; positive floats order like ints). Eight slots hold
// every overlap the roster can produce (bodies >= 3.2 m: at most ~5 reach disks touch one 3 m cell).
#define LARGE_SLOTS_PER_CELL 8u
#define LARGE_CELL_STRIDE 9u
#define SPATIAL_STATS_LARGE_COUNT 1u
#define SPATIAL_STATS_LARGE_MAX_RADIUS_BITS 2u
// Stamp reach is clamped to this many cells around the home cell (covers body radii up to ~16 m at 3 m cells).
#define LARGE_STAMP_MAX_CELL_RADIUS 6
// A large agent scans normal agents within this many cells (radius + half cell up to ~15 m at 3 m cells).
// Kaiju B (2026-10-15): 3 -> 5 so bodies up to 4.5 cells (13.5 m at 3 m cells) are covered.
#define LARGE_BODY_SCAN_MAX_CELL_RADIUS 5
// A large agent scans other large agents' home cells within this many cells.
// Kaiju B (2026-10-15): 4 -> 9 so two 4.5-cell bodies (r1 + r2 < 9 cells) still find each other.
#define LARGE_PAIR_SCAN_MAX_CELL_RADIUS 9
// Pair push shares by body area only when a large body is involved; capped so a crushed small body
// never receives more than this multiple of its usual separation response.
#define LARGE_PUSH_SHARE_MAX 2.0
// A large agent ignores the follow/queue rules of friends whose radius is below this fraction of its own.
#define LARGE_IGNORE_FRIEND_RADIUS_RATIO 0.5
RWStructuredBuffer<uint> largeCells;
StructuredBuffer<uint> largeCellsReadBuffer;
// The spatial hash stats buffer, bound read-only to kernels that consume the large-body stats.
StructuredBuffer<int> spatialHashStatsReadBuffer;
// Team-partitioned flow field: one float2 per cell per team, addressed as
// teamId * FlowFieldCellCount() + cellIndex (see FlowFieldTeamCellIndex). Every team
// shares one grid - same resolution, origin and cell size - which is what lets the cell
// index stay team-independent.
RWStructuredBuffer<float2> flowFieldDirections;
StructuredBuffer<float2> flowFieldDirectionsReadBuffer;
Texture2D<uint> densityMap;
RWTexture2D<uint> densityMapWrite;
Texture2D<uint> attackerDensityMap;
Texture2D<uint> defenderDensityMap;
RWTexture2D<uint> attackerDensityMapWrite;
RWTexture2D<uint> defenderDensityMapWrite;
// Team-partitioned runtime flow scratch, all indexed off flowTeamId by the flow kernels.
// Strides: cellCount, FLOW_STATS_PER_TEAM, FLOW_TARGETS_PER_TEAM.
RWStructuredBuffer<uint> runtimeFlowTargetDensity;
RWStructuredBuffer<int> runtimeFlowStats;
RWStructuredBuffer<float4> runtimeFlowTargets;
// Rebound per dispatch to the team being rebuilt, so the shader keeps a single texture
// however many teams exist.
RWTexture2D<float4> runtimeFlowPreviewTexture;
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
// Flight (2026-10-16): render lift of the classified type (0 = ground). Flyers are drawn this high, so the frustum test
// must look there too, or they pop in/out at the screen edges.
float classifyFlightAltitude;
AppendStructuredBuffer<uint> nearVisibleAgentIndices;
AppendStructuredBuffer<uint> midVisibleAgentIndices;
AppendStructuredBuffer<uint> farVisibleAgentIndices;

// LOD hysteresis + crossfade (2026-10-13, 中景闪动修复 B). One word per agent:
// bits 0-1 tier (0 near, 1 mid, 2 far), bits 2-3 previous tier, bit 4 = classified once,
// bits 16-31 crossfade progress 0..65535 (65535 = new tier fully shown). Read by the agent
// shaders (_MassLodState) to dither the outgoing / incoming mesh with complementary patterns.
RWStructuredBuffer<uint> agentLodStateBuffer;
int lodCrossfadeEnabled;   // per unit-type dispatch: its shaders dither, so draw both tiers while fading
float lodHysteresisRatio;  // a finer tier is kept until this many times its ring radius (1 = hard ring)
float lodCrossfadeSeconds; // 0 = switch instantly
#define LOD_STATE_CLASSIFIED 16u

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
// Corpse despawn. A dead agent's currentAnimationTime doubles as its corpse age, so no
// extra per-agent state is needed. 0 linger = corpses never despawn (legacy behaviour).
// Mirrored by CorpseLifetime.cs.
float corpseLingerSeconds;
// Presentation hold (LodConfig). 0 = shown clip follows the logic state immediately.
float presentationHoldSeconds;
float attackPresentationHoldSeconds;
// Round 6 (2026-10-12) locomotion presentation (LodConfig "站定 / 起步"). All 0 = legacy behaviour
// (raw kernel dispatch, tests). Start/stop are floors over the per-type moveStartSpeed/moveStopSpeed,
// in m/s at body scale 1. minStand/minWalk: a shown Idle stays planted at least minStandSeconds
// (unless clearly pushed to run), a shown Run is not cut to Idle within minWalkSeconds unless the
// body has (nearly) stopped.
float locomotionStartSpeedFloor;
float locomotionStopSpeedFloor;
float minStandSeconds;
float minWalkSeconds;
// Round 6 option b (LodConfig): floor over the per-type moveAnimationSpeedMin, so a slowly moving
// body still visibly steps instead of gliding on a near-frozen run cycle. 0 = per-type value only.
float moveAnimationSpeedFloor;
float corpseSinkSeconds;
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

// The one flow grid every team shares. Per-team resolution/origin would mean per-team
// cell indices, which is the simplification this whole layer is built on.
int2 flowFieldResolution;
float2 flowFieldOrigin;
float flowFieldCellSize;
// One record per team, indexed by raw teamId, length teamCount. Replaced the pairs of
// attacker*/defender* flow uniforms.
StructuredBuffer<TeamFlowParams> teamFlowParamsReadBuffer;
// Which team the flow kernels are rebuilding this dispatch. One team per dispatch, so the
// kernels can keep their 1D thread mapping over cells and read a single team's parameters.
// Meaningless to the combat kernel - agents there read their own team's record instead.
int flowTeamId;

#define MAX_STATIC_OBSTACLES 8
int staticObstacleCount;
float staticObstaclePadding;
float4 staticObstacleRects[MAX_STATIC_OBSTACLES];

// Whether this frame resolves combat at all - not how many teams are on the field. It was
// named enableTwoTeamCombat back when two teams were the only arrangement the pipeline had,
// which read as a team-count switch long after IsEnemy became "any team but mine". A
// crowd-only frame leaves it at 0, and then nobody is an enemy and nobody holds.
int combatEnabled;
int battleStarted;
// Setup preview (2026-10-15): 1 while the battle has not started yet; Idle clips still play (pose only, nothing moves).
int idleAnimateStopped;
int attackerTeamId;
int defenderTeamId;
// How many teams the team-partitioned buffers (teamGridCounts / teamGridAgentIndices /
// teamSpatialStats / teamStanceReadBuffer) were sized for. Any teamId outside
// [0, teamCount) must be skipped, never clamped: clamping would silently merge a stray
// team into team 0's bucket.
int teamCount;
// One TEAM_STANCE_* value per team, indexed by raw teamId. This replaced the single
// defenderMovementMode uniform: stance is a property of a team, not of "the defender".
StructuredBuffer<int> teamStanceReadBuffer;
int localTargetSearchCellRadius;
float defenderGuardRadius;

#if defined(MASS_TERRAIN_ENABLED)
#include "LocalOrders.hlsl"
#include "../../Terrain/Shaders/TerrainNavigation.hlsl"
#endif

UnitTypeSettings GetUnitSettings(uint agentIndex)
{
    return unitTypeSettings[unitTypeIndexReadBuffer[agentIndex]];
}

// Flight (2026-10-15): altitude per unit type in metres (CombatConfig.flightAltitude, 0 = ground unit).
// Only the opt-in variants bind it (combat: MASS_MELEE_CHARGE, projectiles: MASS_PROJECTILE_FLIGHT); everyone else is ground.
#if defined(MASS_MELEE_CHARGE) || defined(MASS_PROJECTILE_FLIGHT)
StructuredBuffer<float> unitTypeFlight;
float FlightAltitudeOf(uint agentIndex)
{
    return max(0.0, unitTypeFlight[unitTypeIndexReadBuffer[agentIndex]]);
}
#else
float FlightAltitudeOf(uint agentIndex)
{
    return 0.0;
}
#endif

bool IsFlyingIndex(uint agentIndex)
{
    return FlightAltitudeOf(agentIndex) > 0.0;
}

bool IsInsideFrustumRadius(float3 position, float radius)
{
    if (enableFrustumCulling == 0)
        return true;

    [unroll]
    for (int i = 0; i < 6; i++)
    {
        if (dot(frustumPlanes[i].xyz, position) + frustumPlanes[i].w < -radius)
            return false;
    }

    return true;
}

bool IsInsideFrustum(float3 position)
{
    return IsInsideFrustumRadius(position, cullingRadius);
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
    // C3a: a large body (scaled radius > half a spatial cell) keeps its whole scaled body out of the
    // rectangles; normal bodies keep the unscaled type radius exactly as before.
    float scaledRadius = radius * max(0.01, max(abs(agent.scale.x), abs(agent.scale.z)));
    if (scaledRadius > 0.5 * max(cellSize, 0.0001))
        radius = scaledRadius;
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

// Corpse age at which a body is dropped from every visible list. 0 = never despawn.
float GetCorpseDespawnSeconds()
{
    return corpseLingerSeconds > 0.0 ? corpseLingerSeconds + max(corpseSinkSeconds, 0.0) : 0.0;
}

// A dead agent whose corpse age reached the despawn point. It stays in agentBuffer (its
// slot is never recycled) but is submitted for nothing: no draw, no animation step, and
// no combat/locomotion pass either - the classification and combat kernels both return
// on this test before they touch the record. Because nothing writes a retired body again,
// LOWERING corpseLingerSeconds at runtime no longer clamps an already-retired age back
// down to the new ceiling; the comparison below still holds, so the body stays retired.
bool IsCorpseDespawned(AgentData agent)
{
    return corpseLingerSeconds > 0.0
        && agent.currentState == STATE_DEAD
        && agent.currentAnimationTime >= GetCorpseDespawnSeconds();
}

// Start/stop hysteresis of the locomotion presentation AND the body deadzone (R1), m/s at this
// body scale. Round 6 (2026-10-12): the per-type defaults (0.12 / 0.05) let bodies creep at
// 0.1-0.5 m/s with the run clip at its 0.2x floor - 59% of Run-shown frames moved slower than
// 0.6 m/s (auto sample base1), which reads as drifting. The LodConfig floors raise both.
void ResolveLocomotionThresholds(UnitTypeSettings settings, float bodyScale, out float stopSpeed, out float startSpeed)
{
    float stop = max(max(0.0, settings.moveStopSpeed), max(0.0, locomotionStopSpeedFloor));
    float start = max(max(settings.moveStartSpeed, locomotionStartSpeedFloor), stop + 0.01);
    stopSpeed = stop * bodyScale;
    startSpeed = start * bodyScale;
}

// Inside the minimum stand time only a clear push (this many times the start speed) wakes the
// body / the run clip.
#define STAND_BREAK_FACTOR 2.5

// Called once for each simulated agent AFTER all position constraints. Tactical
// state is deliberately not changed when pursuit is blocked. shownSeconds = how long the
// current shown clip has been shown (StoreAgent keeps it in the presentation word).
void UpdateLocomotionPresentationDwell(inout AgentData agent, float3 previousPosition, UnitTypeSettings settings, float shownSeconds)
{
    if (deltaTime <= 0.0) return;
    float bodyScale = max(0.01, abs(agent.scale.y));
    float stopSpeed, startSpeed;
    ResolveLocomotionThresholds(settings, bodyScale, stopSpeed, startSpeed);
    bool walkWindow = agent.presentationState == STATE_MOVE && shownSeconds < minWalkSeconds;
    bool standWindow = agent.presentationState == STATE_IDLE && shownSeconds < minStandSeconds;
    if (walkWindow) stopSpeed = min(stopSpeed, max(0.0, settings.moveStopSpeed) * bodyScale);
    float actualSpeed = length(agent.position - previousPosition) / deltaTime;
    // Stop promptly; otherwise smooth the crowd's small positional corrections.
    agent.locomotionSpeed = actualSpeed <= stopSpeed ? 0.0 :
        lerp(agent.locomotionSpeed, actualSpeed, saturate(deltaTime * 12.0));
    int visual = agent.currentState;
    if (visual != STATE_ATTACK && visual != STATE_DEAD)
    {
        float threshold = agent.presentationState == STATE_MOVE ? stopSpeed : startSpeed;
        if (standWindow) threshold = max(threshold, startSpeed * STAND_BREAK_FACTOR);
        visual = agent.locomotionSpeed > threshold ? STATE_MOVE : STATE_IDLE;
    }
    if (visual != agent.presentationState)
    {
        // Death time is corpse age; SetAgentState owns its reset, not this selector.
        if (visual != STATE_DEAD) agent.currentAnimationTime = 0.0;
        agent.presentationState = visual;
    }
}

void UpdateLocomotionPresentation(inout AgentData agent, float3 previousPosition, UnitTypeSettings settings)
{
    UpdateLocomotionPresentationDwell(agent, previousPosition, settings, 1e9);
}

// Advances the VAT time accumulator. Looping states wrap at their OWN clip duration so
// the wrap point is phase-aligned with the clip (no visual pop); Dead keeps counting
// past the end of the death clip because that accumulator doubles as the corpse age.
void UpdateAnimationTime(uint index, inout AgentData agent, int interval)
{
    if (battleStarted == 0 && (idleAnimateStopped == 0 || agent.currentState != STATE_IDLE || agent.presentationState != STATE_IDLE)) return;
    interval = max(interval, 1);
    if ((frameIndex % (uint)interval) != 0)
        return;

    UnitTypeSettings settings = GetUnitSettings(index);
    // Ranged phase is written by the per-frame combat clock, including skipped
    // decision frames. Never advance it again here or let render LOD change its rate.
    if (agent.currentState == STATE_ATTACK && settings.projectileRange > 0.01) return;
    int visual = agent.currentState >= STATE_ATTACK ? agent.currentState : agent.presentationState;
    float duration = GetClipDurationForState(settings, visual);
    bool loop = agent.currentState != STATE_DEAD;

    float animationSpeed = 1.0;
    if (visual == STATE_MOVE)
    {
        float reference = settings.moveReferenceSpeed > 0.0 ? settings.moveReferenceSpeed : settings.maxSpeed;
        reference = max(0.01, reference * max(0.01, abs(agent.scale.y)));
        float minimumRate = max(settings.moveAnimationSpeedMin, moveAnimationSpeedFloor);
        animationSpeed = clamp(agent.locomotionSpeed / reference,
            minimumRate, max(minimumRate, settings.moveAnimationSpeedMax));
    }

    float nextTime = agent.currentAnimationTime + deltaTime * interval * animationSpeed;
    if (loop)
    {
        agent.currentAnimationTime = fmod(nextTime, duration);
        return;
    }

    // Dead: run past the death clip so the linger/sink schedule has an age to read. The
    // VAT sampler clamps the death clip to its last frame, so the pose still holds. The
    // ceiling keeps the float bounded: the despawn point, or the clip end when corpses
    // are configured to stay forever.
    float ceiling = corpseLingerSeconds > 0.0 ? GetCorpseDespawnSeconds() : duration;
    agent.currentAnimationTime = min(nextTime, ceiling);
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

// Alive-to-alive logic change. The clip clock belongs to the SHOWN clip: the
// presentation selector (UpdateLocomotionPresentation, run by StoreAgent in the same
// kernel) restarts it when the shown clip actually changes. Resetting here as well
// restarted a held Attack swing on every Attack->Engage->Attack logic blip.
void SetCrowdState(inout AgentData agent, int state)
{
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

// Where a team's slice of the flow field starts. Teams share the grid, so this is just a
// stride, but going through one helper keeps every kernel honest about the partitioning.
uint FlowFieldTeamCellIndex(int teamId, uint cellIndex)
{
    return (uint)max(0, teamId) * FlowFieldCellCount() + cellIndex;
}

// A team with no allocated record navigates by nothing rather than by team 0's field: an
// out-of-range teamId must never be clamped into another team's slice.
TeamFlowParams GetTeamFlowParams(int teamId)
{
    if (teamId >= 0 && teamId < teamCount)
        return teamFlowParamsReadBuffer[teamId];

    TeamFlowParams disabled;
    disabled.modes = int4(FLOW_TARGET_NONE, 0, 0, 0);
    disabled.target = float4(0.0, 0.0, 0.0, 0.0);
    disabled.area = float4(0.0, 0.0, 0.0, 0.0);
    return disabled;
}

// Effective command params never change the raw team id used by enemy tests/statistics.
TeamFlowParams GetAgentFlowParams(uint index)
{
#if defined(MASS_LOCAL_ORDERS) && defined(MASS_TERRAIN_ENABLED)
    if(HasLocalOrder(index))
    {
        LocalAgentOrder o=LocalOrderFor(index);TeamFlowParams p;
        p.modes=o.stance==TEAM_STANCE_ADVANCE?int4(FLOW_TARGET_NONE,1,1,0):int4(FLOW_TARGET_POINT,o.stance==TEAM_STANCE_MOVE_ONLY?1:0,0,0);
        p.target=float4(o.arrival,o.stopRadius,0);p.area=0;return p;
    }
#endif
    return GetTeamFlowParams(teamIdReadBuffer[index]);
}
#if defined(MASS_TERRAIN_ENABLED) && defined(MASS_GPU_NAV)
// N3/N5 (Docs/GPU-NAVFIELD-N0-20261013.md): steer by -grad of a 2 m GPU distance layer (eikonal metric).
// Team layers (N3) and local Move/Retreat slot layers (N5). Opt-in (RuntimeFlowConfig.gpuNavSteering / gpuNavLocalOrders);
// the keyword is enabled only around the combat dispatch. False = the CPU flow field keeps deciding.
StructuredBuffer<float> _GpuNavDist;      // team layers (latest in A), team * (2R)^2
uint _GpuNavValidMask;
StructuredBuffer<float> _GpuNavLocalDist; // local slot layers (latest in A), slot * (2R)^2
uint _GpuNavLocalMask;
uint _GpuNavLineMask;                     // N5b: teams whose GPU goal is a formation line (no point-terminal hand-off)
float4 _GpuNavArrivalZone[8];            // N5c: arrival-zone depth (m) per team, team t = [t >> 2][t & 3]; 0 = off
static float gGpuNavTeamDist = 1e30;     // N5c: goal distance at the agent's cell from its last GpuNavSteer (team layer) call
#define GPU_NAV_UNREACHABLE 1e29
static const int2 GpuNavOffsets[8] = { int2(1, 0), int2(0, 1), int2(1, 1), int2(-1, 1), int2(-1, 0), int2(0, -1), int2(-1, -1), int2(1, -1) };

float GpuNavAt(bool local, uint layer, int2 c, int res)
{
    if (c.x < 0 || c.y < 0 || c.x >= res || c.y >= res) return 1e30;
    uint i = layer + (uint)(c.y * res + c.x);
    if (local) return _GpuNavLocalDist[i];
    return _GpuNavDist[i];
}

// stopRadius < 0: no stop rule (local slots stop through their own arrival rules).
// pointTerminal: hand the last two coarse cells back to the CPU (exact point approach).
bool GpuNavSteerCore(bool local, uint layer, float2 p, float stopRadius, bool pointTerminal, out float2 dir)
{
    dir = 0;
    int res = flowFieldResolution.x * 2;
    float h = flowFieldCellSize * 0.5;
    float2 lp = (p - flowFieldOrigin) / h;
    int2 c = clamp((int2)floor(lp), 0, res - 1);
    float dc = GpuNavAt(local, layer, c, res);
    if (dc >= GPU_NAV_UNREACHABLE) return false;
    if (pointTerminal && dc <= 2.0 * flowFieldCellSize) return false;
    if (stopRadius >= 0.0 && dc <= stopRadius) return true; // inside the stop radius: no flow
    // Bilinear gradient between the four surrounding fine cell centres.
    float2 q = lp - 0.5;
    int2 b = (int2)floor(q);
    float2 t = q - (float2)b;
    float d00 = GpuNavAt(local, layer, b, res), d10 = GpuNavAt(local, layer, b + int2(1, 0), res);
    float d01 = GpuNavAt(local, layer, b + int2(0, 1), res), d11 = GpuNavAt(local, layer, b + int2(1, 1), res);
    if (max(max(d00, d10), max(d01, d11)) < GPU_NAV_UNREACHABLE)
    {
        float2 g = float2(lerp(d10 - d00, d11 - d01, t.y), lerp(d01 - d00, d11 - d10, t.x));
        float gg = dot(g, g);
        if (gg > 1e-8)
        {
            float2 candidate = -g * rsqrt(gg);
            if (TerrainSegmentClear(p, p + candidate * h)) { dir = candidate; return true; }
        }
    }
    // Beside blocked / unreachable cells: the steepest lower neighbour centre, only along a clear segment.
    float best = dc; int2 next = c;
    [unroll] for (int k = 0; k < 8; k++)
    {
        int2 n = c + GpuNavOffsets[k];
        float dn = GpuNavAt(local, layer, n, res);
        if (dn < best) { best = dn; next = n; }
    }
    if (next.x == c.x && next.y == c.y) return false;
    float2 centre = flowFieldOrigin + ((float2)next + 0.5) * h;
    if (!TerrainSegmentClear(p, centre)) return false;
    float2 offset = centre - p;
    dir = dot(offset, offset) > 0.000001 ? normalize(offset) : 0;
    return true;
}

bool GpuNavSteer(int teamId, float2 p, out float2 dir)
{
    dir = 0;
    if (teamId < 0 || teamId >= 32 || ((_GpuNavValidMask >> (uint)teamId) & 1u) == 0u) return false;
    TeamFlowParams flow = GetTeamFlowParams(teamId);
    int res = flowFieldResolution.x * 2;
    gGpuNavTeamDist = GpuNavAt(false, (uint)teamId * (uint)(res * res), clamp((int2)floor((p - flowFieldOrigin) / (flowFieldCellSize * 0.5)), 0, res - 1), res);
    bool lineGoal = ((_GpuNavLineMask >> (uint)teamId) & 1u) != 0u;
    return GpuNavSteerCore(false, (uint)teamId * (uint)(res * res), p, flow.target.z, flow.modes.x == FLOW_TARGET_POINT && !lineGoal, dir);
}

// N5c: inside the team's arrival zone (goal distance <= depth of the block that forms there): plain marchers follow,
// never shove into the arrived ranks. Valid after SampleFlowDirection of the same agent.
bool GpuNavInArrivalZone(int teamId)
{
    if (teamId < 0 || teamId >= 32) return false;
    float zone = _GpuNavArrivalZone[(uint)teamId >> 2][(uint)teamId & 3u];
    return zone > 0.0 && gGpuNavTeamDist <= zone;
}

bool GpuNavSteerLocal(int slot, float2 p, out float2 dir)
{
    dir = 0;
    if (slot < 0 || slot >= 32 || ((_GpuNavLocalMask >> (uint)slot) & 1u) == 0u) return false;
    int res = flowFieldResolution.x * 2;
    return GpuNavSteerCore(true, (uint)slot * (uint)(res * res), p, -1.0, false, dir);
}
#endif

#if defined(MASS_LOCAL_ORDERS) && defined(MASS_TERRAIN_ENABLED)
float2 SampleLocalOrderFlow(uint index,float3 position)
{
    LocalAgentOrder o=LocalOrderFor(index);if((o.stance!=TEAM_STANCE_MOVE_ONLY && o.stance!=TEAM_STANCE_ADVANCE) || GetUnitSettings(index).flowFieldWeight<=0)return 0;
    if(!TerrainNavPointOpen(position.xz))return 0;
    float2 to=o.arrival-position.xz;float d2=dot(to,to);
    if(o.stance==TEAM_STANCE_MOVE_ONLY && d2<=o.stopRadius*o.stopRadius)return 0;
    // Only the clear terminal region uses local distributed arrival steering. Never cross an obstacle by chasing a point.
    if(o.stance==TEAM_STANCE_MOVE_ONLY && d2<=o.arrivalRegionRadius*o.arrivalRegionRadius && TerrainSegmentClear(position.xz,o.arrival))return normalize(to);
#if defined(MASS_GPU_NAV)
    // N5: Move/Retreat groups outside their arrival region follow the GPU slot layer when it is seeded.
    if(o.stance==TEAM_STANCE_MOVE_ONLY){float2 gpuNavDirection;if(GpuNavSteerLocal(o.slotPlusOne-1,position.xz,gpuNavDirection))return gpuNavDirection;}
#endif
    int2 here=TerrainNavCell(position.xz);uint cell=here.y*flowFieldResolution.x+here.x;
    float2 direction=_LocalOrderFlow[(o.slotPlusOne-1)*_LocalOrderCellCount+cell];if(dot(direction,direction)<.0001)return 0;
    int2 next=here+(int2)sign(direction);float2 center=flowFieldOrigin+(next+.5)*flowFieldCellSize;
    if(!TerrainSegmentClear(position.xz,center))return 0;float2 offset=center-position.xz;return dot(offset,offset)>.000001?normalize(offset):0;
}
#endif


float2 SampleFlowDirection(uint index, int teamId, float3 position)
{
#if defined(MASS_TERRAIN_ENABLED) && defined(MASS_GPU_NAV)
    gGpuNavTeamDist = 1e30;
#endif
#if defined(MASS_LOCAL_ORDERS) && defined(MASS_TERRAIN_ENABLED)
    if(HasLocalOrder(index))return SampleLocalOrderFlow(index,position);
#endif
    if (teamId < 0 || teamId >= teamCount)
        return 0.0;

    if (teamFlowParamsReadBuffer[teamId].modes.y == 0 || GetUnitSettings(index).flowFieldWeight <= 0.0)
        return 0.0;

    uint cell = FlowFieldTeamCellIndex(teamId, FlowFieldCellToIndex(PositionToFlowFieldCell(position)));
    float2 direction = flowFieldDirectionsReadBuffer[cell];
    float lengthSqr = dot(direction, direction);
#if defined(MASS_TERRAIN_ENABLED)
    int2 here = TerrainNavCell(position.xz);
    if (!TerrainNavCellOpen(here)) return 0;
#if defined(MASS_GPU_NAV)
    {
        float2 gpuNavDirection;
        if (GpuNavSteer(teamId, position.xz, gpuNavDirection)) return gpuNavDirection;
    }
#endif
    if (lengthSqr <= 0.0001)
    {
        // Only an explicitly reachable SAME cell can finish at its exact target;
        // zero elsewhere means unreachable/awaiting a field, never straight-line fallback.
        TeamFlowParams p = GetTeamFlowParams(teamId);
        if (p.modes.x == FLOW_TARGET_POINT && all(here == TerrainNavCell(p.target.xy)))
        {
            float2 offset = p.target.xy - position.xz;
            return dot(offset, offset) > max(p.target.z * p.target.z, 0.0001) ? normalize(offset) : 0;
        }
        return 0;
    }
    // Safe neighbour-centre route (never unconditional interpolation); the integrator still checks every segment.
    int2 next = here + (int2)sign(direction);
    float2 nextCenter = flowFieldOrigin + (next + .5) * flowFieldCellSize;
    if (!TerrainSegmentClear(position.xz, nextCenter)) return 0;
    float2 offset = nextCenter - position.xz;
    return dot(offset, offset) > 0.000001 ? normalize(offset) : 0;
#else
    if (lengthSqr <= 0.0001)
        return 0.0;

    if (lengthSqr > 1.0)
        direction *= rsqrt(lengthSqr);

    return direction;
#endif
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

// Planar body radius including the immutable spawn scale (same expression the crowd rules use).
float AgentBodyRadius(uint index)
{
    float3 scale = agentBuffer[index].scale;
    return GetAgentRadius(index) * max(0.01, max(abs(scale.x), abs(scale.z)));
}

// Large = cannot be found reliably through the 3x3 scan: two normal bodies (each <= half a cell)
// always interact within one cell, a larger body may not.
bool IsLargeBodyRadius(float bodyRadius)
{
    return bodyRadius > 0.5 * max(cellSize, 0.0001);
}

float LargeStampReach(float bodyRadius)
{
    return bodyRadius + 0.5 * max(cellSize, 0.0001);
}

// C2 (2026-10-12): combat reach toward a large body is measured to its surface. Ranges are authored
// from the attacker's centre against a normal-sized target (the attacker's own body is already inside
// its attackRange: dragon 4.45 m for a 3.4 m body), so only a LARGE target adds the part of its body
// beyond a normal one. Every normal target adds exactly 0: small-vs-small rules are unchanged.
// The same bonus feeds acquire, hold, reach, entry/exit, ranged cycle, scoring, slots and the
// contact retarget, so all of them agree on where a large body's edge is.
#define LARGE_TARGET_REFERENCE_RADIUS 0.55
// A large target's perimeter takes more attackers than the 8 engagement sectors; the load cap
// follows the geometric ring capacity up to this many (normal targets keep ENGAGEMENT_SLOT_COUNT).
#define LARGE_TARGET_MAX_ATTACKERS 24.0

float TargetReachBonus(uint targetIndex)
{
    // No large body stamped this frame (the common case): skip the per-target body read.
    if (spatialHashStatsReadBuffer[SPATIAL_STATS_LARGE_COUNT] <= 0)
        return 0.0;
    float bodyRadius = AgentBodyRadius(targetIndex);
    return IsLargeBodyRadius(bodyRadius) ? bodyRadius - LARGE_TARGET_REFERENCE_RADIUS : 0.0;
}

// Centre distance minus the target's large-body bonus (= centre distance for normal targets).
float TargetReachDistance(uint targetIndex, float centreDistance)
{
    return max(0.0, centreDistance - TargetReachBonus(targetIndex));
}

// Cells a centre within `distance` of a point can be away from that point's cell.
int CellReach(float distance, int maxCells)
{
    return clamp((int)floor(max(0.0, distance) / max(cellSize, 0.0001)) + 1, 1, maxCells);
}

// Writes a large agent into the large slots of every cell its reach disk touches (BuildSpatialHash only).
void StampLargeAgent(uint index, float2 position, float bodyRadius)
{
    int ignored;
    InterlockedAdd(spatialHashStats[SPATIAL_STATS_LARGE_COUNT], 1, ignored);
    InterlockedMax(spatialHashStats[SPATIAL_STATS_LARGE_MAX_RADIUS_BITS], asint(bodyRadius), ignored);

    float reach = LargeStampReach(bodyRadius);
    float reachSqr = reach * reach;
    float size = max(cellSize, 0.0001);
    int2 homeCell = PositionXzToCell(position);
    int cellRadius = CellReach(reach, LARGE_STAMP_MAX_CELL_RADIUS);
    [loop]
    for (int dz = -cellRadius; dz <= cellRadius; dz++)
    {
        [loop]
        for (int dx = -cellRadius; dx <= cellRadius; dx++)
        {
            int2 cell = homeCell + int2(dx, dz);
            if (cell.x < 0 || cell.y < 0 || cell.x >= gridResolution.x || cell.y >= gridResolution.y)
                continue;

            float2 cellMin = gridOrigin + float2(cell) * size;
            float2 closest = clamp(position, cellMin, cellMin + size);
            float2 gap = position - closest;
            // The home cell always qualifies, so the pair scan's home-cell de-duplication never misses.
            if (dot(gap, gap) > reachSqr && (dx != 0 || dz != 0))
                continue;

            uint record = CellToIndex(cell) * LARGE_CELL_STRIDE;
            uint slot;
            InterlockedAdd(largeCells[record], 1u, slot);
            if (slot < LARGE_SLOTS_PER_CELL)
                largeCells[record + 1u + slot] = index;
        }
    }
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

#define FACING_SMOOTH_SECONDS 0.15
void SmoothFaceDirectionDt(inout AgentData agent, float2 direction, float deadZoneDistance, float maxDegreesPerSecond, float dt)
{
    float lenSqr = dot(direction, direction);
    float deadZoneSqr = deadZoneDistance * deadZoneDistance;
    if (lenSqr <= max(0.0001, deadZoneSqr))
        return;

    float targetYaw = degrees(atan2(direction.x, direction.y));
    float deltaYaw = NormalizeAngleDeltaDegrees(targetYaw - agent.rotation.y);
    float maxStep = max(1.0, maxDegreesPerSecond) * dt;
    // Ease toward the wanted yaw (frame-rate independent) inside the turn-rate cap. A pure
    // rate cap passed every per-frame wobble of the wanted direction straight through:
    // close to an engagement slot the slot direction swings with tiny position changes
    // (2026-10-11 sample: 43% of running Engage turns reversed the next frame, ~9 deg each).
    // dt 0 (zero-budget forced decision) still turns nothing.
    float eased = deltaYaw * (1.0 - exp(-dt / FACING_SMOOTH_SECONDS));
    agent.rotation.y += clamp(eased, -maxStep, maxStep);
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
    if (combatEnabled == 0)
        return false;

    return teamIdReadBuffer[selfIndex] != teamIdReadBuffer[otherIndex];
}

int TeamStanceOf(uint index)
{
#if defined(MASS_LOCAL_ORDERS) && defined(MASS_TERRAIN_ENABLED)
    if(HasLocalOrder(index))return LocalOrderFor(index).stance;
#endif
    int teamId = teamIdReadBuffer[index];
    // A teamId outside the allocated range has no stance record. Advance matches what such
    // an agent did before stances existed: it fell through to the attacker branch.
    if (teamId < 0 || teamId >= teamCount)
        return TEAM_STANCE_ADVANCE;

    return teamStanceReadBuffer[teamId];
}

// Gated on combatEnabled so a crowd-only frame (combat disabled) leaves nobody
// holding, exactly as the old defender predicate did.
bool IsHoldStance(uint index)
{
    int stance = TeamStanceOf(index);
    return combatEnabled != 0 && (stance == TEAM_STANCE_HOLD || stance == TEAM_STANCE_HOLD_HERE);
}

// retainExisting: true when validating an agent's CURRENT target (hysteresis applies),
// false for fresh acquisition (strict gates). Hold defenders keep an engaged target out
// to AttackExitRange, mirroring the attacker-side hysteresis — without it, crowd jitter
// across the exact attackRange line produced systematic one-sided trades.
bool TargetIsUsable(uint selfIndex, uint otherIndex, float distSqr, float3 selfPosition, bool retainExisting)
{
    // Explicit Move/Retreat must release existing enemies as well as reject new
    // acquisition; otherwise front ranks keep fighting while only the rear obeys.
    if (TeamStanceOf(selfIndex) == TEAM_STANCE_MOVE_ONLY) return false;

    if (selfIndex == otherIndex || !IsAliveIndex(otherIndex) || !IsEnemy(selfIndex, otherIndex))
        return false;

    // Flight: a ground melee unit can never target a flying unit; ranged units and other flyers can.
    if (IsFlyingIndex(otherIndex) && !IsFlyingIndex(selfIndex) && !(GetUnitSettings(selfIndex).projectileRange > 0.01))
        return false;

    // C2: ranges reach a large target's surface, not its centre (0 for normal targets).
    float reachBonus = TargetReachBonus(otherIndex);
    if (IsHoldStance(selfIndex))
    {
        float holdRange = (retainExisting ? AttackExitRange(selfIndex) : GetAttackRange(selfIndex)) + reachBonus;
        return distSqr <= holdRange * holdRange;
    }

    // Everyone not holding acquires on aggro radius alone - advancing defenders used to
    // take a separate branch with identical arithmetic. No spawn-anchored chase leash
    // here: it broke the doctrine outright (defenders relocated by their flow field could
    // never acquire targets again); leashing belongs to an explicit order, not to this.
    float acquireRadius = GetTargetAcquireRadius(selfIndex);
#if RANGED_AREA_FIRE
    acquireRadius = max(acquireRadius, GetUnitSettings(selfIndex).projectileRange);
#endif
    acquireRadius += reachBonus;
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
#if defined(MASS_TERRAIN_ENABLED)
    return TargetIsUsable(selfIndex, otherIndex, TerrainDistanceSquared(self.position, agentPositionReadBuffer[otherIndex]), self.position, retainExisting);
#else
    return TargetIsUsable(selfIndex, otherIndex, dot(delta, delta), self.position, retainExisting);
#endif
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
    float2 contactSeparation;
    bool friendlyAhead;
    float aheadGap; // free distance along travelForward to the nearest friendly body, 1e6 = none
    float2 friendPress; // sum of unit directions to OVERLAPPING friends, weighted 0 at touch .. 1 at 15% overlap
    float2 friendSpread; // smooth-kernel sum of unit directions to friends within 2 body distances (R4)
    float leftGap;  // same along travelForward rotated +45 deg (sidestep probe)
    float rightGap; // same along travelForward rotated -45 deg
    int nearestEnemyIndex;     // nearest living enemy in the 3x3 contact cells (round 6 contact retarget), -1 = none
    float nearestEnemyDistSqr; // its planar reach distance squared (centre distance; a large enemy from its surface, C2)
};

// Round 6 (2026-10-12) crowd packing, ONE knob 0..1 (0 = round 5 spacing). Friends treat each other
// as CROWD_FRIEND_SPACING x their body distance for every crowd rule (follow gap, contact press,
// spread, separation, overlap settling), so they stand closer without any rule fighting another.
// Enemies keep full body distance. Also scales the R4 spill gain and the melee attack entry range
// (AgentCombatSimulation). 2026-10-12 auto sample base1: nearest ally p50 0.97 m for 1.10 m bodies.
#define CROWD_PACKING 0.5
#define CROWD_FRIEND_SPACING (1.0 - 0.2 * CROWD_PACKING)

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
    // C2: a large target is ringed at its body (scaled) radius and its reach-extended range.
    float reachBonus = TargetReachBonus(targetIndex);
    bool largeTarget = reachBonus > 0.0;
    float targetRadius = largeTarget ? reachBonus + LARGE_TARGET_REFERENCE_RADIUS : max(0.05, GetAgentRadius(targetIndex));
    float attackRange = max(0.1, GetAttackRange(selfIndex) + reachBonus);
    float engagementRadius = min(max((selfRadius + targetRadius) * 0.8, attackRange * 0.72), attackRange * 0.86);
    float circumference = 6.2831853 * max(engagementRadius, selfRadius + targetRadius);
    float geometricCapacity = floor(circumference / max(0.1, selfRadius * 2.0));
    return clamp(geometricCapacity, 1.0, largeTarget ? LARGE_TARGET_MAX_ATTACKERS : (float)ENGAGEMENT_SLOT_COUNT);
}

float TargetLoadRatio(uint selfIndex, uint targetIndex)
{
    return (float)CurrentTargetLoad(targetIndex) / TargetLoadCapacity(selfIndex, targetIndex);
}

float TargetSelectionScore(uint selfIndex, uint targetIndex, float distSqr)
{
    float distanceScore = TargetReachDistance(targetIndex, sqrt(max(0.0, distSqr))) / max(0.1, GetTargetAcquireRadius(selfIndex));
    float loadRatio = TargetLoadRatio(selfIndex, targetIndex);
    float loadPenalty = saturate(loadRatio) * 0.30 + max(0.0, loadRatio - 1.0) * 0.15;

    // A stable per-agent affinity breaks up synchronized switching when many agents
    // observe the same one-frame-old load snapshot. Distance still dominates when
    // candidates are meaningfully separated.
    float affinity = Hash01(selfIndex ^ (targetIndex * 0x9E3779B9u)) * 0.45;
    return distanceScore + loadPenalty + affinity;
}

// One body neighbour's contribution to every crowd rule. Called for normal neighbours from the
// 3x3 (or, for a large agent, wider) cell scan and for large neighbours from the large slots, so a
// pair is always evaluated by exactly one path. Behaviour for two normal bodies is unchanged:
// pushShare is exactly 1 and every friend rule applies.
void AccumulateBodyNeighbor(uint selfIndex, uint otherIndex, float2 selfPosition, float selfRadius, float bodyRadius,
    float otherBodyRadius, int selfTeamId, float2 travelForward, float2 probeLeft, float2 probeRight,
    inout NeighborhoodQueryResult result)
{
    // Flight: ground and air bodies never touch (no push, no queueing, no melee contact).
    if (IsFlyingIndex(selfIndex) != IsFlyingIndex(otherIndex))
        return;

    float2 otherPosition = agentPositionReadBuffer[otherIndex];
    float2 toOther = otherPosition - selfPosition;
    float distSqr = dot(toOther, toOther);
    // Positions use the frame snapshot. Scale is immutable spawn data;
    // no neighbor velocity/state is sampled while that neighbor is writing.
    bool isFriend = teamIdReadBuffer[otherIndex] == selfTeamId;
    float spacing = isFriend ? CROWD_FRIEND_SPACING : 1.0;
    float bodyDistance = (bodyRadius + otherBodyRadius) * spacing;

    // C1: when a large body is involved, the pair's push is split by body area (a dragon barely
    // moves, the soldier is pushed out), and a large agent does not queue behind much smaller friends.
    bool largePair = IsLargeBodyRadius(bodyRadius) || IsLargeBodyRadius(otherBodyRadius);
    float pushShare = 1.0;
    bool friendRules = isFriend;
    if (largePair)
    {
        float selfArea = bodyRadius * bodyRadius;
        float otherArea = otherBodyRadius * otherBodyRadius;
        pushShare = min(LARGE_PUSH_SHARE_MAX, 2.0 * otherArea / max(0.0001, selfArea + otherArea));
        friendRules = isFriend && otherBodyRadius >= bodyRadius * LARGE_IGNORE_FRIEND_RADIUS_RATIO;
    }

    if (!isFriend)
    {
        // C2: nearest by reach distance, so a dragon's flank counts as close as a soldier's chest.
        float reachDistSqr = distSqr;
        if (IsLargeBodyRadius(otherBodyRadius))
        {
            float reachDistance = max(0.0, sqrt(distSqr) - (otherBodyRadius - LARGE_TARGET_REFERENCE_RADIUS));
            reachDistSqr = reachDistance * reachDistance;
        }
        if (reachDistSqr < result.nearestEnemyDistSqr)
        {
            result.nearestEnemyDistSqr = reachDistSqr;
            result.nearestEnemyIndex = (int)otherIndex;
        }
    }
    float forwardDistance = dot(toOther, travelForward);
    float sideDistance = abs(toOther.x * travelForward.y - toOther.y * travelForward.x);
    if (friendRules && forwardDistance > 0.001 &&
        forwardDistance < bodyDistance + max(0.15, bodyRadius * 0.5) && sideDistance < bodyDistance * 0.85)
        result.friendlyAhead = true;
    // Free distance until this body would touch a FRIENDLY body when moving straight
    // along travelForward: rear ranks must not shove the ranks ahead. Enemies are
    // excluded on purpose: closing on the enemy ends at attack range (> body contact)
    // through the attack logic, and charges must keep their arrival speed.
    if (friendRules && forwardDistance > 0.001 && sideDistance < bodyDistance * 0.9)
        result.aheadGap = min(result.aheadGap,
            forwardDistance - sqrt(max(0.0, bodyDistance * bodyDistance - sideDistance * sideDistance)));
    if (friendRules)
    {
        // Pressing into an overlapping friend from any side is removed after steering
        // (ApplyFriendlyContact); continuous in distance, zero until bodies touch.
        if (distSqr < bodyDistance * bodyDistance && distSqr > 0.000001)
        {
            float friendDist = sqrt(distSqr);
            result.friendPress += (toOther / friendDist) * saturate((bodyDistance - friendDist) / (0.15 * bodyDistance));
        }
        // R4: smooth crowding vector (weight 1 at the centre .. 0 at two body distances).
        // Audit fix (C1): a large pair is only guaranteed to meet within the larger body's stamp
        // reach (home-cell slots and the body scan cover that far; the large-large pair scan covers
        // at least both radii), so the weight reaches 0 there instead of jumping in when the pair
        // first becomes visible. Both caps stay above bodyDistance.
        float spreadRadius = bodyDistance * 2.0;
        if (largePair)
        {
            float guaranteed = LargeStampReach(max(bodyRadius, otherBodyRadius));
            if (IsLargeBodyRadius(bodyRadius) && IsLargeBodyRadius(otherBodyRadius))
                guaranteed = max(guaranteed, bodyRadius + otherBodyRadius);
            spreadRadius = min(spreadRadius, guaranteed);
        }
        if (distSqr < spreadRadius * spreadRadius && distSqr > 0.000001)
        {
            float spreadDist = sqrt(distSqr);
            float spreadWeight = 1.0 - spreadDist / spreadRadius;
            result.friendSpread += (toOther / spreadDist) * (spreadWeight * spreadWeight);
        }
        float leftAlong = dot(toOther, probeLeft);
        float leftSide = abs(toOther.x * probeLeft.y - toOther.y * probeLeft.x);
        if (leftAlong > 0.001 && leftSide < bodyDistance * 0.9)
            result.leftGap = min(result.leftGap, leftAlong - sqrt(max(0.0, bodyDistance * bodyDistance - leftSide * leftSide)));
        float rightAlong = dot(toOther, probeRight);
        float rightSide = abs(toOther.x * probeRight.y - toOther.y * probeRight.x);
        if (rightAlong > 0.001 && rightSide < bodyDistance * 0.9)
            result.rightGap = min(result.rightGap, rightAlong - sqrt(max(0.0, bodyDistance * bodyDistance - rightSide * rightSide)));
    }
    if (distSqr < bodyDistance * bodyDistance * 0.81)
    {
        float2 away = distSqr > 0.000001 ? -toOther * rsqrt(distSqr) : FallbackDirection(selfIndex);
        result.contactSeparation += away * (bodyDistance * 0.9 - sqrt(max(0.0, distSqr))) * pushShare;
    }
    // Large pairs separate on full body radii (scale included); normal pairs keep the unscaled
    // type radii they always used.
    float minDistance = (largePair ? (bodyRadius + otherBodyRadius) : (selfRadius + GetAgentRadius(otherIndex))) * spacing;
    float minDistanceSqr = minDistance * minDistance;
    if (distSqr >= minDistanceSqr)
        return;

    float2 separationDelta = selfPosition - otherPosition;
    if (distSqr < 0.000001)
    {
        separationDelta = FallbackDirection(selfIndex) * 0.001;
        distSqr = dot(separationDelta, separationDelta);
    }

    float dist = max(sqrt(max(distSqr, 0.000001)), 0.0001);
    result.separation += (separationDelta / dist) * (minDistance - dist) * pushShare;
}

NeighborhoodQueryResult QueryCombatNeighborhood(uint selfIndex, AgentData agent, float maxTargetRadius, bool searchForEnemy, float2 travelForward)
{
    float2 selfPosition = agent.position.xz;
    int2 homeCell = PositionXzToCell(selfPosition);
    float selfRadius = GetAgentRadius(selfIndex);
    int searchCellRadius = searchForEnemy ? GetLocalTargetSearchCellRadius(maxTargetRadius) : 0;

    NeighborhoodQueryResult result;
    result.bestEnemyIndex = -1;
    result.bestEnemyScore = 1e20;
    result.separation = 0.0;
    result.contactSeparation = 0.0;
    result.friendlyAhead = false;
    result.aheadGap = 1e6;
    result.friendPress = 0.0;
    result.friendSpread = 0.0;
    result.leftGap = 1e6;
    result.rightGap = 1e6;
    result.nearestEnemyIndex = -1;
    result.nearestEnemyDistSqr = 1e20;
    float2 probeLeft = float2(travelForward.x - travelForward.y, travelForward.x + travelForward.y) * 0.70710678;
    float2 probeRight = float2(travelForward.x + travelForward.y, travelForward.y - travelForward.x) * 0.70710678;
    float bodyRadius = selfRadius * max(0.01, max(abs(agent.scale.x), abs(agent.scale.z)));
    // C1: a large agent scans normal bodies out to its own reach; everyone else keeps the 3x3.
    bool selfLarge = IsLargeBodyRadius(bodyRadius);
    int bodyCellRadius = selfLarge ? CellReach(LargeStampReach(bodyRadius), LARGE_BODY_SCAN_MAX_CELL_RADIUS) : 1;
    int queryCellRadius = max(searchCellRadius, bodyCellRadius);
    // Kaiju (2026-10-15): a large body's attack reach can lie past the 4-cell local target search, so a large
    // agent sweeps enemies out to its body scan radius (<= LARGE_BODY_SCAN_MAX_CELL_RADIUS). Normal agents unchanged.
    int enemyCellRadius = (selfLarge && searchCellRadius > 0) ? queryCellRadius : searchCellRadius;
    // Every team except the agent's own is hostile (no alliance concept yet), so the sweep
    // walks all teamCount-1 opposing buckets instead of one fixed slot. This is the only
    // place where widening the team dimension actually costs more work per agent. A teamId
    // outside [0, teamCount) has no bucket of its own and disables the sweep rather than
    // reading past the end of the team grid.
    int selfTeamId = teamIdReadBuffer[selfIndex];
    uint teamSlotCount = (uint)max(0, teamCount);
    bool sweepEnemies = searchForEnemy && selfTeamId >= 0 && (uint)selfTeamId < teamSlotCount;

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
            if (sweepEnemies && abs(dx) <= enemyCellRadius && abs(dz) <= enemyCellRadius)
            {
                [loop]
                for (uint enemyTeamSlot = 0; enemyTeamSlot < teamSlotCount; enemyTeamSlot++)
                {
                    if (enemyTeamSlot == (uint)selfTeamId)
                        continue;

                    uint enemyCellIndex = enemyTeamSlot * gridCellCount + cellIndex;
                    uint enemyCount = min(teamGridCountsReadBuffer[enemyCellIndex], maxAgentsPerCell);
                    for (uint i = 0; i < enemyCount; i++)
                    {
                        uint otherIndex = teamGridAgentIndicesReadBuffer[enemyCellIndex * maxAgentsPerCell + i];
                        if (!IsAliveIndex(otherIndex))
                            continue;

                        float2 toOther = agentPositionReadBuffer[otherIndex] - selfPosition;
#if defined(MASS_TERRAIN_ENABLED)
                        float distSqr = TerrainDistanceSquared(agent.position, agentPositionReadBuffer[otherIndex]);
#else
                        float distSqr = dot(toOther, toOther);
#endif
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
            }

            if (abs(dx) > bodyCellRadius || abs(dz) > bodyCellRadius)
                continue;

            uint occupantCount = min(gridCountsReadBuffer[cellIndex], maxAgentsPerCell);
            for (uint i = 0; i < occupantCount; i++)
            {
                uint otherIndex = gridAgentIndicesReadBuffer[cellIndex * maxAgentsPerCell + i];
                if (otherIndex == selfIndex || !IsAliveIndex(otherIndex))
                    continue;

                float otherBodyRadius = AgentBodyRadius(otherIndex);
                // Large bodies are handled once, through the large slots below.
                if (IsLargeBodyRadius(otherBodyRadius))
                    continue;

                AccumulateBodyNeighbor(selfIndex, otherIndex, selfPosition, selfRadius, bodyRadius, otherBodyRadius,
                    selfTeamId, travelForward, probeLeft, probeRight, result);
            }
        }
    }

    // C1 large bodies. Nothing to do on maps without any (one stats read).
    if (spatialHashStatsReadBuffer[SPATIAL_STATS_LARGE_COUNT] <= 0)
        return result;

    if (!selfLarge)
    {
        // Every large body this agent can touch stamped this agent's own cell.
        uint homeRecord = CellToIndex(homeCell) * LARGE_CELL_STRIDE;
        uint largeCount = min(largeCellsReadBuffer[homeRecord], LARGE_SLOTS_PER_CELL);
        for (uint i = 0; i < largeCount; i++)
        {
            uint otherIndex = largeCellsReadBuffer[homeRecord + 1u + i];
            if (otherIndex == selfIndex || !IsAliveIndex(otherIndex))
                continue;

            AccumulateBodyNeighbor(selfIndex, otherIndex, selfPosition, selfRadius, bodyRadius, AgentBodyRadius(otherIndex),
                selfTeamId, travelForward, probeLeft, probeRight, result);
        }
        return result;
    }

    // A large agent meets other large agents through their HOME cell only (each is stamped there
    // exactly once), scanning far enough for the largest body on the field.
    float largestRadius = asfloat(spatialHashStatsReadBuffer[SPATIAL_STATS_LARGE_MAX_RADIUS_BITS]);
    int pairCellRadius = CellReach(bodyRadius + largestRadius, LARGE_PAIR_SCAN_MAX_CELL_RADIUS);
    [loop]
    for (int pz = -pairCellRadius; pz <= pairCellRadius; pz++)
    {
        [loop]
        for (int px = -pairCellRadius; px <= pairCellRadius; px++)
        {
            int2 cell = homeCell + int2(px, pz);
            if (cell.x < 0 || cell.y < 0 || cell.x >= gridResolution.x || cell.y >= gridResolution.y)
                continue;

            uint cellIndex = CellToIndex(cell);
            uint record = cellIndex * LARGE_CELL_STRIDE;
            uint largeCount = min(largeCellsReadBuffer[record], LARGE_SLOTS_PER_CELL);
            for (uint i = 0; i < largeCount; i++)
            {
                uint otherIndex = largeCellsReadBuffer[record + 1u + i];
                if (otherIndex == selfIndex || !IsAliveIndex(otherIndex))
                    continue;
                if (CellToIndex(PositionXzToCell(agentPositionReadBuffer[otherIndex])) != cellIndex)
                    continue;

                AccumulateBodyNeighbor(selfIndex, otherIndex, selfPosition, selfRadius, bodyRadius, AgentBodyRadius(otherIndex),
                    selfTeamId, travelForward, probeLeft, probeRight, result);
            }
        }
    }

    return result;
}

// Classifies ONE unit type per dispatch (classifyUnitTypeIndex). Animation time is
// advanced here exactly once per agent per frame because every agent belongs to exactly
// one unit type.
// C4 (2026-10-12): a large body (IsLargeBodyRadius) reads as large on screen from much farther
// away and overhangs its centre point. Its LOD rings grow with body size relative to an ordinary
// 0.55 m body, up to this factor; its frustum test uses this multiple of the body radius (wings,
// tail, height). Small agents keep the global rings and cullingRadius unchanged.
#define LARGE_LOD_DISTANCE_SCALE_MAX 4.0
#define LARGE_CULL_RADIUS_FACTOR 2.0

// LOD hysteresis + crossfade (2026-10-13). Moving inward switches at the ring; moving outward
// keeps the finer tier until lodHysteresisRatio x ring, so a body standing on the 30 m ring no
// longer flips near/mid mesh, shader and shadow every frame. A switch starts a crossfade; a
// switch back during the fade continues from the current mix instead of popping.
void ResolveLodTier(uint index, float lodDistSqr, int rawTier, out int tier, out int prevTier, out float fade)
{
    uint state = agentLodStateBuffer[index];
    tier = rawTier;
    prevTier = rawTier;
    fade = 1.0;
    if ((state & LOD_STATE_CLASSIFIED) != 0u)
    {
        int cur = (int)(state & 3u);
        int prev = (int)((state >> 2) & 3u);
        float f = (float)(state >> 16) * (1.0 / 65535.0);
        float h = max(1.0, lodHysteresisRatio);
        float h2 = h * h;
        int wanted = rawTier;
        if (rawTier > cur)
        {
            if (cur == 0 && lodDistSqr <= nearLodRadiusSqr * h2) wanted = 0;
            else if (cur == 1 && lodDistSqr <= midLodRadiusSqr * h2) wanted = 1;
            else if (cur == 0 && rawTier == 2 && lodDistSqr <= midLodRadiusSqr * h2) wanted = 1;
        }
        if (wanted != cur)
        {
            bool reverse = f < 1.0 && wanted == prev;
            prev = cur;
            cur = wanted;
            f = lodCrossfadeSeconds > 0.0 ? (reverse ? 1.0 - f : 0.0) : 1.0;
        }
        else
        {
            f = lodCrossfadeSeconds > 0.0 ? saturate(f + deltaTime / lodCrossfadeSeconds) : 1.0;
        }
        tier = cur;
        prevTier = prev;
        fade = f;
    }
    agentLodStateBuffer[index] = (uint)tier | ((uint)prevTier << 2) | LOD_STATE_CLASSIFIED | ((uint)(saturate(fade) * 65535.0 + 0.5) << 16);
}

void AppendLodTier(uint index, int tier, bool culled, bool includeFar)
{
    // The near ring is exempt from culling (it is the shadow-casting tier, see below).
    if (tier == 0)
        nearVisibleAgentIndices.Append(index);
    else if (culled)
        return;
    else if (tier == 1)
        midVisibleAgentIndices.Append(index);
    else if (includeFar)
        farVisibleAgentIndices.Append(index);
}

void AppendVisibleAgentForUnitType(uint index, inout AgentData agent)
{
    float3 offset = agent.position - lodCenter;
    offset.y = 0.0;
    float distSqr = dot(offset, offset);
    float bodyRadius = GetAgentRadius(index) * max(0.01, max(abs(agent.scale.x), abs(agent.scale.z)));
    bool largeBody = IsLargeBodyRadius(bodyRadius);
    float lodScale = largeBody ? clamp(bodyRadius / LARGE_TARGET_REFERENCE_RADIUS, 1.0, LARGE_LOD_DISTANCE_SCALE_MAX) : 1.0;
    float lodDistSqr = distSqr / (lodScale * lodScale);
    bool isNear = lodDistSqr <= nearLodRadiusSqr;
    bool isMid = !isNear && lodDistSqr <= midLodRadiusSqr;
    int lodTier, lodPrevTier;
    float lodFade;
    ResolveLodTier(index, lodDistSqr, isNear ? 0 : (isMid ? 1 : 2), lodTier, lodPrevTier, lodFade);
    isNear = lodTier == 0;
    isMid = lodTier == 1;
    bool lodFading = lodCrossfadeEnabled != 0 && lodFade < 1.0 && lodPrevTier != lodTier;
    int animationInterval = isNear ? nearAnimationInterval : (isMid ? midAnimationInterval : farAnimationInterval);
    // Without farIncludeDead the mid->far boundary is a hard corpse pop line: bodies
    // render inside it and vanish beyond it as the camera moves.
    bool includeFar = farIncludeDead != 0 || IsAliveIndex(index);

    UpdateAnimationTime(index, agent, animationInterval);

    // Despawned corpses leave the render path entirely: no draw, no shadow, no animation
    // fetch. Reached only on the frame the age first reaches its ceiling - from the next
    // frame the classification kernel returns before it even calls this.
    if (IsCorpseDespawned(agent))
        return;

    // The near ring is the only shadow-casting tier and the same visible list feeds
    // every camera and the ShadowCaster pass: culling it against the main camera's
    // frustum makes shadows of off-screen/behind-camera units pop at the screen edge.
    // Its instance count is bounded by pi*nearRadius^2*density, so exempting it from
    // both distance and frustum culling is cheap; mid/far stay culled.
    // Visibility budget: on km-scale battlefields agents beyond this range are sub-pixel;
    // dropping them caps the worst-case visible instance count. Applies to mid/far only.
    bool culled = maxRenderDistanceSqr > 0.0 && distSqr > maxRenderDistanceSqr;
    if (!culled && (!isNear || lodFading))
    {
        float visibleRadius = largeBody ? max(cullingRadius, bodyRadius * LARGE_CULL_RADIUS_FACTOR) : cullingRadius;
        culled = !IsInsideFrustumRadius(agent.position + float3(0.0, max(0.0, classifyFlightAltitude), 0.0), visibleRadius);
    }

    AppendLodTier(index, lodTier, culled, includeFar);
    if (lodFading)
        AppendLodTier(index, lodPrevTier, culled, includeFar);
}






