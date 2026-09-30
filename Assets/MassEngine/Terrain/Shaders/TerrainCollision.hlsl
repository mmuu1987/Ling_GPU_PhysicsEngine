#ifndef MASS_TERRAIN_COLLISION_INCLUDED
#define MASS_TERRAIN_COLLISION_INCLUDED

#include "TerrainSurface.hlsl"

// Clip the swept segment to the finite heightfield's XZ rectangle (grid coordinates).
bool TerrainClipSweepAxis(float start, float travel, float extent, inout float enterT, inout float exitT)
{
    if (travel == 0.0)
        return start >= 0.0 && start <= extent;
    float t0 = -start / travel;
    float t1 = (extent - start) / travel;
    enterT = max(enterT, min(t0, t1));
    exitT = min(exitT, max(t0, t1));
    return enterT <= exitT;
}

// Each cell is split a-b-d / a-d-c, exactly like SampleTerrainSurface and the mesh.
// Clip to the triangle's diagonal half-space, then solve the linear signed height.
// This is an exact segment/triangle interval test, not point sampling along the ray.
bool TerrainTriangleSweep(float3 start, float3 travel, float2 gridStart, float2 gridTravel,
    int2 cell, float a, float2 heightGradient, bool lower, float enterT, float exitT, out float hitT)
{
    hitT = 2.0;
    float2 local = gridStart - cell;
    float diagonalStart = local.x - local.y;
    float diagonalTravel = gridTravel.x - gridTravel.y;
    if (!lower)
    {
        diagonalStart = -diagonalStart;
        diagonalTravel = -diagonalTravel;
    }
    if (diagonalTravel == 0.0)
    {
        if (diagonalStart < 0.0)
            return false;
    }
    else
    {
        float diagonalT = -diagonalStart / diagonalTravel;
        if (diagonalTravel > 0.0)
            enterT = max(enterT, diagonalT);
        else
            exitT = min(exitT, diagonalT);
    }
    if (enterT > exitT)
        return false;

    float enterHeight = a + dot(heightGradient, local + gridTravel * enterT);
    float enterClearance = start.y + travel.y * enterT - enterHeight;
    // Starting on or below the solid heightfield also counts as an immediate collision.
    if (enterClearance <= 0.0)
    {
        hitT = enterT;
        return true;
    }
    float exitHeight = a + dot(heightGradient, local + gridTravel * exitT);
    float exitClearance = start.y + travel.y * exitT - exitHeight;
    if (exitClearance > 0.0)
        return false;
    hitT = lerp(enterT, exitT, enterClearance / (enterClearance - exitClearance));
    return true;
}

// Earliest contact with the solid single-layer heightfield over previous->next.
// DDA visits EVERY crossed cell, even when a frame crosses a one-cell-wide ridge.
// No arbitrary step count or sampling spacing can turn a long sweep into a tunnel.
bool SweepTerrainSurface(float3 previous, float3 next, out float hitT)
{
    hitT = 2.0;
    if (any(!isfinite(previous)) || any(!isfinite(next)))
        return false;
    float3 travel = next - previous;
    int2 cellCount = _SurfaceDimensions - 1;
    float2 stepSize = _SurfaceBounds.zw / cellCount;
    float2 gridStart = (previous.xz - _SurfaceBounds.xy) / stepSize;
    float2 gridTravel = travel.xz / stepSize;
    float enterT = 0.0;
    float exitT = 1.0;
    if (!TerrainClipSweepAxis(gridStart.x, gridTravel.x, cellCount.x, enterT, exitT) ||
        !TerrainClipSweepAxis(gridStart.y, gridTravel.y, cellCount.y, enterT, exitT))
        return false;

    int2 cell = clamp((int2)floor(gridStart + gridTravel * enterT), int2(0, 0), cellCount - 1);
    int2 direction = int2(gridTravel.x > 0.0 ? 1 : (gridTravel.x < 0.0 ? -1 : 0),
                          gridTravel.y > 0.0 ? 1 : (gridTravel.y < 0.0 ? -1 : 0));
    // A segment crosses at most Width+Depth grid boundaries, including a zero-length
    // entry cell on a negative-direction grid edge. The bound scales with the actual grid.
    [loop]
    for (int visited = 0; visited < _SurfaceDimensions.x + _SurfaceDimensions.y; visited++)
    {
        float2 boundaryT = float2(1e30, 1e30);
        if (direction.x != 0)
            boundaryT.x = (cell.x + (direction.x > 0 ? 1 : 0) - gridStart.x) / gridTravel.x;
        if (direction.y != 0)
            boundaryT.y = (cell.y + (direction.y > 0 ? 1 : 0) - gridStart.y) / gridTravel.y;
        float cellExitT = min(exitT, min(boundaryT.x, boundaryT.y));

        int index = cell.y * _SurfaceDimensions.x + cell.x;
        float a = _SurfaceHeights[index];
        float b = _SurfaceHeights[index + 1];
        float c = _SurfaceHeights[index + _SurfaceDimensions.x];
        float d = _SurfaceHeights[index + _SurfaceDimensions.x + 1];
        float lowerT, upperT;
        bool lowerHit = TerrainTriangleSweep(previous, travel, gridStart, gridTravel,
            cell, a, float2(b - a, d - b), true, enterT, cellExitT, lowerT);
        bool upperHit = TerrainTriangleSweep(previous, travel, gridStart, gridTravel,
            cell, a, float2(d - c, c - a), false, enterT, cellExitT, upperT);
        if (lowerHit || upperHit)
        {
            hitT = min(lowerT, upperT);
            return true;
        }
        if (cellExitT >= exitT)
            return false;

        // Advance both axes only on an exact corner: a tolerance here could skip a
        // very short but real interval in an adjacent cell containing a sharp peak.
        if (boundaryT.x <= boundaryT.y)
            cell.x += direction.x;
        if (boundaryT.y <= boundaryT.x)
            cell.y += direction.y;
        enterT = cellExitT;
        if (any(cell < 0) || any(cell >= cellCount))
            return false;
    }
    return false;
}

#endif
