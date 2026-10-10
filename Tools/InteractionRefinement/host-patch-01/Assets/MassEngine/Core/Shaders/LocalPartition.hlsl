#ifndef MASS_LOCAL_PARTITION_INCLUDED
#define MASS_LOCAL_PARTITION_INCLUDED
// 4 admitted groups * 4096 members. Sorted IDs, independent of army size.
// Exactly 64 KiB in a separate cbuffer; no additional UAV/SRV or dummy resource.
cbuffer MassLocalPartitionMembers { int4 _LocalPartitionIds[4096]; };
int _LocalPartitionCount;
bool LocalPartitionContains(uint index)
{
    if (_LocalPartitionCount <= 0) return false;
    int lo=0, hi=min(_LocalPartitionCount,16384);
    [loop] while(lo<hi) {
        int mid=lo+(hi-lo)/2;
        uint member=(uint)_LocalPartitionIds[mid>>2][mid&3];
        if(member==index) return true;
        if(member<index) lo=mid+1; else hi=mid;
    }
    return false;
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
