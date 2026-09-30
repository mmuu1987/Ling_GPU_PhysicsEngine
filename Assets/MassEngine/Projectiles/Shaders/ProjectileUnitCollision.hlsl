// Runtime unit collision. ProjectileData and sphere entry are declared by the caller.
float projectileQueryRadius;

void ConsiderProjectileUnit(uint index, ProjectileData proj, float3 start, float3 travel,
    inout float nearestT, inout int hitIndex)
{
    if ((int)index + 1 == proj.sourceAgentIndexPlusOne || hpReadBuffer[index] <= 0) return;
    UnitTypeSettings settings = GetUnitSettings(index);
    AgentData agent = agentBuffer[index];
    float2 xz = agentPositionReadBuffer[index];
    float ground = agent.position.y;
#ifdef MASS_TERRAIN_ENABLED
    float4 surface = SampleTerrainSurface(xz);
    if (surface.w < 0.0) return;
    ground = surface.x;
#endif
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
    float padding = max(0.0, projectileQueryRadius) + max(0.0, proj.hitRadius);
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
void ConsiderSplashVictim(uint index, ProjectileData proj, float3 impact, int directHit)
{
    if ((int)index == directHit || (int)index + 1 == proj.sourceAgentIndexPlusOne) return;
    if (hpReadBuffer[index] <= 0 || teamIdReadBuffer[index] == proj.sourceTeamId) return;
    UnitTypeSettings settings = GetUnitSettings(index);
    AgentData agent = agentBuffer[index];
    float bodyRadius = max(0.05, settings.agentRadius) * max(0.01, max(abs(agent.scale.x), abs(agent.scale.z)));
    float2 offset = agentPositionReadBuffer[index] - impact.xz;
    float reach = proj.splashRadius + bodyRadius;
    if (dot(offset, offset) <= reach * reach)
        InterlockedAdd(pendingDamageBuffer[index], (int)proj.damage);
}

void ApplyProjectileSplash(ProjectileData proj, float3 impact, int directHit, uint agentCount)
{
    if (!(proj.splashRadius > 0.0)) return;
    float reach = proj.splashRadius + max(0.0, projectileQueryRadius);
    int2 first = PositionXzToCell(impact.xz - reach);
    int2 last = PositionXzToCell(impact.xz + reach);
    bool overflow = false;
    [loop] for (int oz = first.y; oz <= last.y; oz++)
    [loop] for (int ox = first.x; ox <= last.x; ox++)
        overflow = overflow || gridCountsReadBuffer[CellToIndex(int2(ox, oz))] > maxAgentsPerCell;
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
            if (index < agentCount)
                ConsiderSplashVictim(index, proj, impact, directHit);
        }
    }
}
