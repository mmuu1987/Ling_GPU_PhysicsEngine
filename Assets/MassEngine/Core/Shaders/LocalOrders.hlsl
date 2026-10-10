#ifndef MASS_LOCAL_ORDERS_INCLUDED
#define MASS_LOCAL_ORDERS_INCLUDED
#if defined(MASS_LOCAL_ORDERS) && defined(MASS_TERRAIN_ENABLED)
struct LocalAgentOrder { int slotPlusOne; int sequence; int stance; int epoch; float2 arrival; float arrivalRegionRadius; float stopRadius; };
StructuredBuffer<LocalAgentOrder> _LocalOrders;
StructuredBuffer<float2> _LocalOrderFlow;
StructuredBuffer<uint> _LocalOrderMask;
RWStructuredBuffer<uint> _LocalOrderAck;
int _LocalOrderEpoch, _LocalOrderCellCount;
// Per-invocation register state, set only by the combat kernel; no shared team state or UAV indirection.
static int localSelfSlot = -1;
LocalAgentOrder LocalOrderFor(uint index) {
    LocalAgentOrder o=_LocalOrders[index];
#ifdef LP_STANCE
    o.stance=LP_STANCE;
#endif
    return o;
}
bool HasLocalOrder(uint index) {
#ifdef LP_STANCE
    return true; // Only reachable after the raw order guard in the selected kernel.
#else
    LocalAgentOrder o=_LocalOrders[index];return o.slotPlusOne>0 && o.epoch==_LocalOrderEpoch;
#endif
}
#endif
#endif

