#ifndef MASS_LOCAL_PARTITION_INCLUDED
#define MASS_LOCAL_PARTITION_INCLUDED
// Membership bitset: bit (index & 31) of 32-bit word (index >> 5) marks an agent admitted to a partitioned local order.
// 4096 int4 = 16384 words = 524288 agents, exactly 64 KiB in a separate cbuffer; no additional UAV/SRV or dummy resource.
// One warp-coherent read per agent (32 consecutive agents share a word); no per-group or per-order member limit.
cbuffer MassLocalPartitionMembers { int4 _LocalPartitionBits[4096]; };
int _LocalPartitionCount; // words in use; 0 = partition inactive
bool LocalPartitionContains(uint index)
{
    uint word = index >> 5;
    if (_LocalPartitionCount <= 0 || word >= (uint)min(_LocalPartitionCount, 16384)) return false;
    uint bits = (uint)_LocalPartitionBits[word >> 2][word & 3];
    return ((bits >> (index & 31)) & 1u) != 0;
}
#if defined(LP_ATTACKMELEE)
#define LP_STANCE 1
#define LP_RANGED 0
#define SimulateCombatAndAccumulateDamage SimulateLocalAttackMelee
#elif defined(LP_ATTACKRANGED)
#define LP_STANCE 1
#define LP_RANGED 1
#define SimulateCombatAndAccumulateDamage SimulateLocalAttackRanged
#elif defined(LP_MOVEMELEE)
#define LP_STANCE 3
#define LP_RANGED 0
#define SimulateCombatAndAccumulateDamage SimulateLocalMoveMelee
#elif defined(LP_MOVERANGED)
#define LP_STANCE 3
#define LP_RANGED 1
#define SimulateCombatAndAccumulateDamage SimulateLocalMoveRanged
#elif defined(LP_HOLDMELEE)
#define LP_STANCE 4
#define LP_RANGED 0
#define SimulateCombatAndAccumulateDamage SimulateLocalHoldMelee
#elif defined(LP_HOLDRANGED)
#define LP_STANCE 4
#define LP_RANGED 1
#define SimulateCombatAndAccumulateDamage SimulateLocalHoldRanged
#endif
#ifdef LP_STANCE
#ifndef MASS_LOCAL_ORDERS
#define MASS_LOCAL_ORDERS 1
#endif
#ifndef MASS_TERRAIN_ENABLED
#define MASS_TERRAIN_ENABLED 1
#endif
#endif
#endif
