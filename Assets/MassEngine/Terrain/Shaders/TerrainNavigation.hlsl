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

void TerrainIntegrate(inout AgentData agent, float dt)
{
    float3 before = agent.position;
    float horizontal = length(agent.velocity.xz), speed = length(agent.velocity);
    float4 sample = SampleTerrainSurface(before.xz);
    if (sample.w < .5 || horizontal <= 1e-6 || dt <= 0) { agent.velocity = 0; TerrainAnchor(agent); return; }
    float2 direction = agent.velocity.xz / horizontal;
    float3 tangent = normalize(float3(direction.x, dot(direction, sample.yz), direction.y)) * speed;
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

void TerrainConstrainPosition(float3 previousPosition, inout AgentData agent)
{
    if (!TerrainSegmentClear(previousPosition.xz, agent.position.xz))
    { agent.position = previousPosition; agent.velocity = 0; }
    TerrainAnchor(agent);
}
#endif


