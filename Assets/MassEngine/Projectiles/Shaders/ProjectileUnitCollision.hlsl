// Runtime unit collision. ProjectileData and sphere entry are declared by the caller.
float projectileQueryRadius;

// C2 (2026-10-12): bodies larger than half a cell are stamped into largeCells by the spatial hash
// (every cell within body radius + half a cell), so the swept cell padding only has to cover normal
// bodies. Without the clamp one dragon on the map raised every projectile's padding to the dragon's
// radius times the largest spawn scale (~4 m): ~4x the cells per sweep for the whole army.
float ProjectileNormalQueryRadius()
{
    return min(max(0.0, projectileQueryRadius), 0.5 * max(cellSize, 0.0001));
}

void ConsiderProjectileUnit(uint index, ProjectileData proj, float3 start, float3 travel,
    inout float nearestT, inout int hitIndex)
{
    if ((int)index + 1 == proj.sourceAgentIndexPlusOne || hpReadBuffer[index] <= 0) return;
    if (teamIdReadBuffer[index] == proj.sourceTeamId) return; // area fire: allies never block a projectile
    UnitTypeSettings settings = GetUnitSettings(index);
    AgentData agent = agentBuffer[index];
    float2 xz = agentPositionReadBuffer[index];
    float ground = agent.position.y;
#ifdef MASS_TERRAIN_ENABLED
    float4 surface = SampleTerrainSurface(xz);
    if (surface.w < 0.0) return;
    ground = surface.x;
#endif
    ground += FlightAltitudeOf(index); // Flight: the capsule hangs at the flyer's altitude
    float height = 2.0 * max(0.05, settings.projectileTargetHeight) * max(0.01, abs(agent.scale.y));
    float bodyRadius = max(0.05, settings.agentRadius) * max(0.01, max(abs(agent.scale.x), abs(agent.scale.z)));
    float cap = min(bodyRadius, height * 0.5);
    float3 lower = float3(xz.x, ground + cap, xz.y);
    float3 upper = float3(xz.x, ground + height - cap, xz.y);
    float t;
    if (ProjectileCapsuleEntry(start, travel, lower, upper, bodyRadius + max(0.0, proj.hitRadius), t)
        && (t < nearestT || (t == nearestT && hitIndex >= 0 && (int)index < hitIndex)))
    {
        nearestT = t;
        hitIndex = (int)index;
    }
}

void SweepProjectileUnits(ProjectileData proj, float3 start, float3 end, uint agentCount,
    inout float nearestT, inout int hitIndex)
{
    float padding = ProjectileNormalQueryRadius() + max(0.0, proj.hitRadius);
    int2 first = PositionXzToCell(min(start.xz, end.xz) - padding);
    int2 last = PositionXzToCell(max(start.xz, end.xz) + padding);
    float3 travel = end - start;
    bool overflow = false;
    // Swept AABB covers all crossed cells and large bodies whose centres lie outside
    // the segment. Typical frame steps touch only a handful of cells; no fixed cap.
    [loop] for (int z = first.y; z <= last.y; z++)
    [loop] for (int x = first.x; x <= last.x; x++)
    {
        uint cell = CellToIndex(int2(x, z));
        uint count = gridCountsReadBuffer[cell];
        overflow = overflow || count > maxAgentsPerCell;
        [loop] for (uint slot = 0; slot < min(count, maxAgentsPerCell); slot++)
        {
            uint index = gridAgentIndicesReadBuffer[cell * maxAgentsPerCell + slot];
            if (index < agentCount) ConsiderProjectileUnit(index, proj, start, travel, nearestT, hitIndex);
        }
        // Large bodies whose centre lies outside the padded box: a body the segment can touch is
        // stamped in a visited cell (the point hitRadius from the segment toward its centre is in
        // the box and inside the body). Seeing one twice is harmless: the hit test only keeps a min.
        uint largeRecord = cell * LARGE_CELL_STRIDE;
        uint largeCount = largeCellsReadBuffer[largeRecord];
        overflow = overflow || largeCount > LARGE_SLOTS_PER_CELL;
        [loop] for (uint largeSlot = 0; largeSlot < min(largeCount, LARGE_SLOTS_PER_CELL); largeSlot++)
        {
            uint index = largeCellsReadBuffer[largeRecord + 1u + largeSlot];
            if (index < agentCount) ConsiderProjectileUnit(index, proj, start, travel, nearestT, hitIndex);
        }
    }
    // Rare overloaded buckets must not silently make bodies non-solid. Existing
    // spatialHashStats reports the overflow; pay an exact fallback only on affected
    // sweeps, never on the normal path. No candidate truncation without a diagnostic.
    if (overflow)
    {
        [loop] for (uint index = 0; index < agentCount; index++)
            ConsiderProjectileUnit(index, proj, start, travel, nearestT, hitIndex);
    }
}

// Optional impact splash (proj.splashRadius > 0; 0 = legacy single-target, returns immediately).
// Living enemies whose XZ body circle overlaps the radius take the full shot damage once.
// The direct-hit victim (already damaged) and the shooter are skipped; allies never take damage.
float SplashBodyRadius(uint index)
{
    UnitTypeSettings settings = GetUnitSettings(index);
    AgentData agent = agentBuffer[index];
    return max(0.05, settings.agentRadius) * max(0.01, max(abs(agent.scale.x), abs(agent.scale.z)));
}

void ConsiderSplashVictimBody(uint index, ProjectileData proj, float3 impact, int directHit, float bodyRadius)
{
    if ((int)index == directHit || (int)index + 1 == proj.sourceAgentIndexPlusOne) return;
    if (hpReadBuffer[index] <= 0 || teamIdReadBuffer[index] == proj.sourceTeamId) return;
    // Flight: splash stays in its layer - a hit on a flyer bursts in the air, anything else on the ground.
    bool airBurst = directHit >= 0 && IsFlyingIndex((uint)directHit);
    if (IsFlyingIndex(index) != airBurst) return;
    float2 offset = agentPositionReadBuffer[index] - impact.xz;
    float reach = proj.splashRadius + bodyRadius;
    if (dot(offset, offset) <= reach * reach)
        InterlockedAdd(pendingDamageBuffer[index], (int)proj.damage);
}

void ConsiderSplashVictim(uint index, ProjectileData proj, float3 impact, int directHit)
{
    ConsiderSplashVictimBody(index, proj, impact, directHit, SplashBodyRadius(index));
}

// C2: a large victim is taken from its stamps, once: only in the visited cell holding the point of the
// splash disc nearest its centre (that point is inside the body whenever the two overlap, so the cell
// is stamped and inside the visited box).
void ConsiderLargeSplashVictims(int2 visited, ProjectileData proj, float3 impact, int directHit, uint agentCount)
{
    uint record = CellToIndex(visited) * LARGE_CELL_STRIDE;
    uint largeCount = min(largeCellsReadBuffer[record], LARGE_SLOTS_PER_CELL);
    [loop] for (uint largeSlot = 0; largeSlot < largeCount; largeSlot++)
    {
        uint index = largeCellsReadBuffer[record + 1u + largeSlot];
        if (index >= agentCount) continue;
        float2 centre = agentPositionReadBuffer[index];
        float2 toVictim = centre - impact.xz;
        float victimDistance = length(toVictim);
        float2 anchor = victimDistance > proj.splashRadius ? impact.xz + toVictim * (proj.splashRadius / victimDistance) : centre;
        int2 anchorCell = PositionXzToCell(anchor);
        if (anchorCell.x == visited.x && anchorCell.y == visited.y)
            ConsiderSplashVictimBody(index, proj, impact, directHit, SplashBodyRadius(index));
    }
}

void ApplyProjectileSplash(ProjectileData proj, float3 impact, int directHit, uint agentCount)
{
    if (!(proj.splashRadius > 0.0)) return;
    float reach = proj.splashRadius + ProjectileNormalQueryRadius();
    int2 first = PositionXzToCell(impact.xz - reach);
    int2 last = PositionXzToCell(impact.xz + reach);
    bool overflow = false;
    [loop] for (int oz = first.y; oz <= last.y; oz++)
    [loop] for (int ox = first.x; ox <= last.x; ox++)
        overflow = overflow || gridCountsReadBuffer[CellToIndex(int2(ox, oz))] > maxAgentsPerCell
            || largeCellsReadBuffer[CellToIndex(int2(ox, oz)) * LARGE_CELL_STRIDE] > LARGE_SLOTS_PER_CELL;
    if (overflow)
    {
        // Same exact fallback policy as the sweep: overloaded buckets never hide victims.
        [loop] for (uint index = 0; index < agentCount; index++)
            ConsiderSplashVictim(index, proj, impact, directHit);
        return;
    }
    [loop] for (int z = first.y; z <= last.y; z++)
    [loop] for (int x = first.x; x <= last.x; x++)
    {
        uint cell = CellToIndex(int2(x, z));
        uint count = gridCountsReadBuffer[cell];
        [loop] for (uint slot = 0; slot < count; slot++)
        {
            uint index = gridAgentIndicesReadBuffer[cell * maxAgentsPerCell + slot];
            // Grid cells are clamped (not hashed): each (x,z) is visited once and each agent lives in one cell,
            // so no victim is counted twice. count <= maxAgentsPerCell here (overflow took the exact path).
            if (index >= agentCount) continue;
            // Normal bodies here; a large body is counted once from its stamps below.
            float bodyRadius = SplashBodyRadius(index);
            if (!IsLargeBodyRadius(bodyRadius))
                ConsiderSplashVictimBody(index, proj, impact, directHit, bodyRadius);
        }
        ConsiderLargeSplashVictims(int2(x, z), proj, impact, directHit, agentCount);
    }
}

