#ifndef MASS_TERRAIN_NAVIGATION_INCLUDED
#define MASS_TERRAIN_NAVIGATION_INCLUDED
#include "TerrainSurface.hlsl"
StructuredBuffer<uint> _NavigationWalkable;

int2 TerrainNavCell(float2 p) { return (int2)floor((p - flowFieldOrigin) / flowFieldCellSize); }
bool TerrainNavCellOpen(int2 cell)
{
    if (any(cell < 0) || any(cell >= flowFieldResolution)) return false;
#if defined(MASS_LOCAL_ORDERS) && defined(MASS_TERRAIN_ENABLED)
#ifdef LP_STANCE
    return _LocalOrderMask[localSelfSlot*_LocalOrderCellCount+cell.y*flowFieldResolution.x+cell.x]!=0;
#else
    if(localSelfSlot>=0)return _LocalOrderMask[localSelfSlot*_LocalOrderCellCount+cell.y*flowFieldResolution.x+cell.x]!=0;
#endif
#endif
    return _NavigationWalkable[cell.y * flowFieldResolution.x + cell.x] != 0;
}
bool TerrainNavPointOpen(float2 p)
{
    if (!all(isfinite(p))) return false;
    // CPU cells use double subtraction; GPU float subtraction can round a point
    // just inside a blocked cell onto the adjacent open cell (e.g. z=33.9999962,
    // origin=-256, cellSize=2). Enclose the subtraction/division roundoff and
    // require every possibly occupied cell to be open. Never relax the CPU mask.
    // This is a numerical guard, not a change to agent radius or nav clearance.
    float magnitude = max(1.0, max(max(abs(p.x), abs(p.y)),
                                  max(abs(flowFieldOrigin.x), abs(flowFieldOrigin.y))));
    float guard = max(flowFieldCellSize * 1e-6, magnitude * 9.5367431640625e-7);
    float2 relative = p - flowFieldOrigin;
    int2 lo = (int2)floor((relative - guard) / flowFieldCellSize);
    int2 hi = (int2)floor((relative + guard) / flowFieldCellSize);
    // Fail closed if a future grid is too fine to resolve reliably in float.
    if (any(hi - lo > 1)) return false;
#if defined(LP_STANCE) && LP_RANGED
    // Keep the four conservative cell checks in their original order.
    // A real loop avoids cloning the local/global-mask branch into every inlined caller.
    [loop] for (int corner = 0; corner < 4; ++corner)
    {
        int2 cell = corner == 0 ? lo : (corner == 1 ? hi :
            (corner == 2 ? int2(lo.x, hi.y) : int2(hi.x, lo.y)));
        if (!TerrainNavCellOpen(cell)) return false;
    }
    return true;
#else
    return TerrainNavCellOpen(lo) && TerrainNavCellOpen(hi)
        && TerrainNavCellOpen(int2(lo.x, hi.y))
        && TerrainNavCellOpen(int2(hi.x, lo.y));
#endif
}

// Supercover DDA over the inflated navigation mask. Tied crossings require both
// cardinal neighbours, exactly as in the CPU graph; large steps cannot jump a wall.
bool TerrainSegmentClear(float2 start, float2 end)
{
    if (!TerrainNavPointOpen(start) || !TerrainNavPointOpen(end)) return false;
    int2 cell = TerrainNavCell(start), goal = TerrainNavCell(end);
    float2 delta = end - start;
    int2 step = int2(delta.x > 0 ? 1 : (delta.x < 0 ? -1 : 0), delta.y > 0 ? 1 : (delta.y < 0 ? -1 : 0));
    float2 nextBoundary = flowFieldOrigin + (cell + int2(step.x > 0 ? 1 : 0, step.y > 0 ? 1 : 0)) * flowFieldCellSize;
    float2 tMax = float2(step.x == 0 ? 1e30 : (nextBoundary.x - start.x) / delta.x,
                        step.y == 0 ? 1e30 : (nextBoundary.y - start.y) / delta.y);
    float2 tDelta = float2(step.x == 0 ? 1e30 : flowFieldCellSize / abs(delta.x),
                          step.y == 0 ? 1e30 : flowFieldCellSize / abs(delta.y));
    [loop] for (int i = 0; i < flowFieldResolution.x + flowFieldResolution.y + 2; ++i)
    {
        if (all(cell == goal)) return true;
        if (abs(tMax.x - tMax.y) <= 1e-6)
        {
            if (!TerrainNavCellOpen(cell + int2(step.x, 0)) || !TerrainNavCellOpen(cell + int2(0, step.y))) return false;
            cell += step; tMax += tDelta;
        }
        else if (tMax.x < tMax.y) { cell.x += step.x; tMax.x += tDelta.x; }
        else { cell.y += step.y; tMax.y += tDelta.y; }
        if (!TerrainNavCellOpen(cell)) return false;
    }
    return false;
}

float TerrainDistanceSquared(float3 selfPosition, float2 targetXZ)
{
    float4 sample = SampleTerrainSurface(targetXZ);
    if (sample.w < 0) return 1e30;
    float3 delta = float3(targetXZ.x, sample.x, targetXZ.y) - selfPosition;
    return dot(delta, delta);
}

void TerrainRestoreIntentVelocity(inout AgentData agent)
{
    float horizontal = length(agent.velocity.xz);
    float speed = length(agent.velocity);
    agent.velocity = horizontal > 1e-6 ? float3(agent.velocity.x / horizontal * speed, 0, agent.velocity.z / horizontal * speed) : 0;
}

void TerrainAnchor(inout AgentData agent)
{
    float4 sample = SampleTerrainSurface(agent.position.xz);
    if (sample.w >= 0) agent.position.y = sample.x;
}

// C3a (2026-10-12): edge probes for LARGE bodies (body radius > half a spatial cell). The navigation
// mask is inflated only by the normal clearance - inflating it by a dragon's radius would close every
// narrow path for the whole army - so a large body checks points on its own edge instead: a ring at
// R - LARGE_TERRAIN_PROBE_INSET (an open point has at least the clearance floor of free space around
// it) and, when that ring is wider than half a nav cell, a second ring at half the radius so a blocked
// row between the centre and the outer ring is not skipped. A move may not put more probes into
// blocked cells, nor press further toward the blocked side; the into-wall part is removed (the body
// slides along the edge) and only if that is still refused does it stop. Normal bodies pass 0 and
// keep the exact centre-only behaviour. The flow field still ignores size (C3b, not done): a large
// body sent into a passage narrower than itself slides along the mouth and waits there.
#define LARGE_TERRAIN_PROBE_INSET 0.45
static const float2 LargeTerrainProbeDirections[8] =
{
    float2(1.0, 0.0), float2(0.70710678, 0.70710678), float2(0.0, 1.0), float2(-0.70710678, 0.70710678),
    float2(-1.0, 0.0), float2(-0.70710678, -0.70710678), float2(0.0, -1.0), float2(0.70710678, -0.70710678)
};

// Number of blocked edge probes; blockedDirection = sum of unit directions toward them.
int LargeBodyTerrainProbe(float2 centre, float bodyRadius, out float2 blockedDirection)
{
    blockedDirection = 0.0;
    int blocked = 0;
    float outer = max(0.0, bodyRadius - LARGE_TERRAIN_PROBE_INSET);
    bool inner = outer > 0.5 * flowFieldCellSize;
    [loop] for (int i = 0; i < 8; i++)
    {
        float2 probe = LargeTerrainProbeDirections[i];
        bool open = TerrainNavPointOpen(centre + probe * outer);
        if (open && inner) open = TerrainNavPointOpen(centre + probe * (outer * 0.5));
        if (!open) { blocked++; blockedDirection += probe; }
    }
    return blocked;
}

// A large-body move is refused when it adds blocked probes, or keeps the same number while pressing
// toward the side that was already blocked. Fewer blocked probes (backing out) is always allowed.
bool LargeBodyMoveAllowed(int blockedBefore, float2 directionBefore, int blockedAfter, float2 move)
{
    if (blockedAfter == 0 || blockedAfter < blockedBefore) return true;
    if (blockedAfter > blockedBefore) return false;
    return dot(move, directionBefore) <= 1e-5;
}

// Removes the component of `move` pointing into the blocked side (zero when nothing is known).
float2 LargeBodySlide(float2 move, float2 directionBefore, float2 directionAfter)
{
    float2 wall = directionBefore + directionAfter;
    float wallLength = length(wall);
    if (wallLength <= 1e-4) return 0.0;
    wall /= wallLength;
    return move - wall * max(0.0, dot(move, wall));
}

void TerrainIntegrate(inout AgentData agent, float dt, float largeBodyRadius)
{
    float3 before = agent.position;
    float horizontal = length(agent.velocity.xz), speed = length(agent.velocity);
    float4 sample = SampleTerrainSurface(before.xz);
    if (sample.w < .5 || horizontal <= 1e-6 || dt <= 0) { agent.velocity = 0; TerrainAnchor(agent); return; }
    float2 direction = agent.velocity.xz / horizontal;
    float3 tangent = normalize(float3(direction.x, dot(direction, sample.yz), direction.y)) * speed;
    if (largeBodyRadius > 0.0)
    {
        // Mask reads only (no texture sampling) inside this branch; the surface step below stays
        // unconditional (FXC miscompiles texture-sampling loops called under a condition).
        float2 directionBefore, directionAfter;
        int blockedBefore = LargeBodyTerrainProbe(before.xz, largeBodyRadius, directionBefore);
        int blockedAfter = LargeBodyTerrainProbe(before.xz + tangent.xz * dt, largeBodyRadius, directionAfter);
        if (!LargeBodyMoveAllowed(blockedBefore, directionBefore, blockedAfter, tangent.xz))
        {
            float2 slid = LargeBodySlide(direction, directionBefore, directionAfter);
            float slidLength = length(slid);
            if (slidLength < 0.05) { agent.velocity = 0; return; } // head-on into the edge
            float2 slidDirection = slid / slidLength;
            speed *= slidLength;
            tangent = normalize(float3(slidDirection.x, dot(slidDirection, sample.yz), slidDirection.y)) * speed;
            blockedAfter = LargeBodyTerrainProbe(before.xz + tangent.xz * dt, largeBodyRadius, directionAfter);
            if (!LargeBodyMoveAllowed(blockedBefore, directionBefore, blockedAfter, tangent.xz))
            { agent.velocity = 0; return; }
        }
    }
    float2 offset = tangent.xz * dt;
    float budget = speed * dt;
    float3 candidate = before;
#if defined(LP_STANCE) && LP_RANGED
    [loop]
#else
    [unroll]
#endif
    for (int attempt = 0; attempt < 4; ++attempt)
    {
        candidate.xz = before.xz + offset;
        float4 next = SampleTerrainSurface(candidate.xz);
        if (next.w < .5) { agent.velocity = 0; return; }
        candidate.y = next.x;
        float actual = length(candidate - before);
        if (actual <= budget + 1e-5) break;
        offset *= budget / max(actual, 1e-6);
    }
    if (length(candidate - before) > budget + 1e-4 || !TerrainSegmentClear(before.xz, candidate.xz))
    { agent.velocity = 0; return; }
    agent.position = candidate;
    // Keep an actual tangent velocity. Decision frames restore horizontal intent once;
    // light LOD frames preserve this magnitude instead of attenuating it again each step.
    agent.velocity = tangent;
}

void TerrainIntegrate(inout AgentData agent, float dt)
{
    TerrainIntegrate(agent, dt, 0.0);
}

void TerrainConstrainPosition(float3 previousPosition, inout AgentData agent, float largeBodyRadius)
{
    if (!TerrainSegmentClear(previousPosition.xz, agent.position.xz))
    { agent.position = previousPosition; agent.velocity = 0; }
    else if (largeBodyRadius > 0.0)
    {
        // C3a: post-integration moves (home clamp) obey the same edge rule; keep the along-edge
        // part when possible, otherwise stay at the previous position.
        float2 move = agent.position.xz - previousPosition.xz;
        if (dot(move, move) > 1e-10)
        {
            float2 directionBefore, directionAfter;
            int blockedBefore = LargeBodyTerrainProbe(previousPosition.xz, largeBodyRadius, directionBefore);
            int blockedAfter = LargeBodyTerrainProbe(agent.position.xz, largeBodyRadius, directionAfter);
            if (!LargeBodyMoveAllowed(blockedBefore, directionBefore, blockedAfter, move))
            {
                float2 slid = LargeBodySlide(move, directionBefore, directionAfter);
                float2 target = previousPosition.xz + slid;
                blockedAfter = LargeBodyTerrainProbe(target, largeBodyRadius, directionAfter);
                if (dot(slid, slid) > 1e-10 && LargeBodyMoveAllowed(blockedBefore, directionBefore, blockedAfter, slid)
                    && TerrainSegmentClear(previousPosition.xz, target))
                    agent.position.xz = target;
                else
                { agent.position = previousPosition; agent.velocity = 0; }
            }
        }
    }
    TerrainAnchor(agent);
}

void TerrainConstrainPosition(float3 previousPosition, inout AgentData agent)
{
    TerrainConstrainPosition(previousPosition, agent, 0.0);
}
#endif



