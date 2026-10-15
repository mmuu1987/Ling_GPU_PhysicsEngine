#ifndef MASS_RANGED_AREA_FIRE_INCLUDED
#define MASS_RANGED_AREA_FIRE_INCLUDED
// Ranged area fire (2026-10-14, option B / 丙): ranged units do not search for individual
// enemies. Every frame BuildDensityMap also bins the two battle teams into coarse blocks
// (~RANGED_AREA_BLOCK_METERS square, whole flow cells) and keeps up to RANGED_AREA_BLOCK_SLOTS
// sampled members per block. A ranged unit aims at the nearest enemy block within its range
// and takes one sampled member as its nominal target; the launcher adds scatter around it.
// Allies never stop a projectile (ProjectileUnitCollision), so there is no readiness probe.
// Layout: rangedBlockBuffer[((teamSlot * blocksY + by) * blocksX + bx) * STRIDE + k],
// k = 0 member count this frame, k = 1..SLOTS sampled agent indices. teamSlot 0 = attacker, 1 = defender.
#define RANGED_AREA_BLOCK_METERS 32.0
#define RANGED_AREA_BLOCK_SLOTS 8u
#define RANGED_AREA_BLOCK_STRIDE 9u
// Only long-range units fire by area (the approved 500 m roster). Short-range shooters such as the
// dragons and the unchanged 对照/密集方阵 variants keep direct targeting (allies still never block).
#define RANGED_AREA_MIN_RANGE 100.0

RWStructuredBuffer<uint> rangedBlockBuffer;      // written by ClearDensityMap / BuildDensityMap
// The same buffer, read-only in the simulate kernels (they are already at the D3D11.0 limit of 8 UAVs).
StructuredBuffer<uint> rangedBlockReadBuffer;

int RangedAreaBlockCells()
{
    // At least 2x2 flow cells per block: the clear pass and the C# allocation rely on it.
    return max(2, (int)round(RANGED_AREA_BLOCK_METERS / max(flowFieldCellSize, 0.0001)));
}

int2 RangedAreaBlockDims(int blockCells)
{
    return (flowFieldResolution + blockCells - 1) / blockCells;
}

int RangedAreaTeamSlot(int team)
{
    return team == attackerTeamId ? 0 : (team == defenderTeamId ? 1 : -1);
}

bool RangedAreaFireUnit(uint index, UnitTypeSettings settings)
{
#if RANGED_AREA_FIRE
    return settings.projectileRange >= RANGED_AREA_MIN_RANGE && RangedAreaTeamSlot(teamIdReadBuffer[index]) >= 0;
#else
    return false;
#endif
}

uint RangedAreaBlockBase(int teamSlot, int2 block, int2 dims)
{
    return ((uint)(teamSlot * dims.y + block.y) * (uint)dims.x + (uint)block.x) * RANGED_AREA_BLOCK_STRIDE;
}

// ClearDensityMap thread (one per flow cell): clears the member counts. With >= 2x2 cells per
// block the 2 * blocks count words never exceed the flow cell count.
void RangedAreaClearBlocks(int2 cell)
{
    uint length, stride;
    rangedBlockBuffer.GetDimensions(length, stride);
    int2 dims = RangedAreaBlockDims(RangedAreaBlockCells());
    uint word = (uint)cell.y * (uint)flowFieldResolution.x + (uint)cell.x;
    if (word < (uint)(2 * dims.x * dims.y))
    {
        uint at = word * RANGED_AREA_BLOCK_STRIDE;
        if (at < length) rangedBlockBuffer[at] = 0u;
    }
}

// BuildDensityMap thread: one living agent, already binned into its flow cell.
void RangedAreaAddMember(uint index, int team, int2 cell)
{
    int teamSlot = RangedAreaTeamSlot(team);
    if (teamSlot < 0) return;
    uint length, stride;
    rangedBlockBuffer.GetDimensions(length, stride);
    int blockCells = RangedAreaBlockCells();
    int2 dims = RangedAreaBlockDims(blockCells);
    int2 block = clamp(cell / blockCells, 0, dims - 1);
    uint at = RangedAreaBlockBase(teamSlot, block, dims);
    if (at + RANGED_AREA_BLOCK_STRIDE > length) return;
    uint before;
    InterlockedAdd(rangedBlockBuffer[at], 1u, before);
    // Reservoir sample: every member of the block is equally likely to be kept.
    uint slot = before;
    if (before >= RANGED_AREA_BLOCK_SLOTS)
        slot = (uint)(Hash01(index * 0x9E3779B9u ^ frameIndex * 0x85EBCA6Bu) * (float)(before + 1u));
    if (slot < RANGED_AREA_BLOCK_SLOTS) rangedBlockBuffer[at + 1u + slot] = index;
}

float RangedAreaDistanceSqr(float3 position, uint other)
{
#if defined(MASS_TERRAIN_ENABLED)
    return TerrainDistanceSquared(position, agentPositionReadBuffer[other]);
#else
    float2 delta = agentPositionReadBuffer[other] - position.xz;
    return dot(delta, delta);
#endif
}

// Nearest enemy block within range (block centre distance), then one usable sampled member.
// Rings are walked outwards and the walk stops one ring after the first hit (a ring is a
// Chebyshev distance, so the next ring can still hold a closer centre). Returns -1 if none.
int RangedAreaPickTarget(uint self, float3 position, float range)
{
    int enemySlot = 1 - RangedAreaTeamSlot(teamIdReadBuffer[self]);
    if (enemySlot < 0 || enemySlot > 1) return -1;
    uint length, stride;
    rangedBlockReadBuffer.GetDimensions(length, stride);
    uint agentCount, agentStride;
    agentBuffer.GetDimensions(agentCount, agentStride);
    int blockCells = RangedAreaBlockCells();
    int2 dims = RangedAreaBlockDims(blockCells);
    float blockSize = (float)blockCells * max(flowFieldCellSize, 0.0001);
    int2 home = clamp(PositionToFlowFieldCell(position) / blockCells, 0, dims - 1);
    float reach = range + blockSize * 0.7072;
    int maxRing = min(max(dims.x, dims.y), (int)ceil(range / blockSize) + 1);
    float bestSqr = 3.0e38;
    int2 best = int2(-1, -1);
    int foundRing = -1;
    [loop] for (int ring = 0; ring <= maxRing; ring++)
    {
        if (foundRing >= 0 && ring > foundRing + 1) break;
        uint perimeter = ring == 0 ? 1u : 8u * (uint)ring;
        uint side = max(1u, 2u * (uint)ring);
        [loop] for (uint i = 0u; i < perimeter; i++)
        {
            uint s = i / side;
            int o = (int)(i - s * side);
            int2 d = int2(0, 0);
            if (ring > 0)
            {
                if (s == 0u) d = int2(-ring + o, -ring);
                else if (s == 1u) d = int2(ring, -ring + o);
                else if (s == 2u) d = int2(ring - o, ring);
                else d = int2(-ring, ring - o);
            }
            int2 block = home + d;
            if (any(block < 0) || any(block >= dims)) continue;
            uint at = RangedAreaBlockBase(enemySlot, block, dims);
            if (at + RANGED_AREA_BLOCK_STRIDE > length || rangedBlockReadBuffer[at] == 0u) continue;
            float2 centre = flowFieldOrigin + ((float2)block + 0.5) * blockSize;
            float2 delta = centre - position.xz;
            float distSqr = dot(delta, delta);
            if (distSqr > reach * reach || distSqr >= bestSqr) continue;
            bestSqr = distSqr; best = block;
            if (foundRing < 0) foundRing = ring;
        }
    }
    if (best.x < 0) return -1;
    uint bestAt = RangedAreaBlockBase(enemySlot, best, dims);
    uint members = min(rangedBlockReadBuffer[bestAt], RANGED_AREA_BLOCK_SLOTS);
    uint start = (uint)(Hash01(self * 0x27D4EB2Du ^ frameIndex * 0x165667B1u) * (float)members);
    [loop] for (uint k = 0; k < members; k++)
    {
        uint other = rangedBlockReadBuffer[bestAt + 1u + (start + k) % members];
        if (other >= agentCount) continue;
        if (TargetIsUsable(self, other, RangedAreaDistanceSqr(position, other), position, false))
            return (int)other;
    }
    return -1;
}

// Decision-time target for an area-fire unit. A live current target is kept unless the
// nearest block is materially closer (half a block), so volleys do not churn every beat.
int RangedAreaRetarget(uint self, float3 position, UnitTypeSettings settings, int current)
{
    int pick = RangedAreaPickTarget(self, position, settings.projectileRange);
    if (pick < 0 || current < 0 || pick == current) return pick >= 0 ? pick : current;
    float margin = 0.5 * (float)RangedAreaBlockCells() * max(flowFieldCellSize, 0.0001);
    float currentDistance = sqrt(RangedAreaDistanceSqr(position, (uint)current));
    float pickDistance = sqrt(RangedAreaDistanceSqr(position, (uint)pick));
    return pickDistance + margin < currentDistance ? pick : current;
}
#endif
