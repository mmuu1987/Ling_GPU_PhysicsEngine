#ifndef MASS_CONGESTION_WAITING_INCLUDED
#define MASS_CONGESTION_WAITING_INCLUDED
// Only the combat kernel uses these helpers. Sharing its existing slot-assignment
// UAV keeps DX11's eight-UAV limit; each invocation owns only its own tail record.
StructuredBuffer<int> movementCommandRevisionBuffer;
struct CongestionWait
{
    float2 anchor;
    float2 forward;
    float observedSeconds;
    float remainingSeconds;
    float commandRevision;
    float rangedStatus; // 0 unknown, 1 clear, -1/-2 blocked with stable left/right preference
    float2 rangedAnchor;
    float rangedSeconds;
    int rangedTargetPlusOne;
};
uint CongestionOffset(uint index)
{
    uint count, stride; agentBuffer.GetDimensions(count, stride);
    return count + index * 12u;
}
CongestionWait ReadCongestion(uint index)
{
    uint b = CongestionOffset(index);
    CongestionWait s;
    s.anchor = asfloat(int2(engagementSlotAssignmentBuffer[b], engagementSlotAssignmentBuffer[b+1]));
    s.forward = asfloat(int2(engagementSlotAssignmentBuffer[b+2], engagementSlotAssignmentBuffer[b+3]));
    s.observedSeconds = asfloat(engagementSlotAssignmentBuffer[b+4]);
    s.remainingSeconds = asfloat(engagementSlotAssignmentBuffer[b+5]);
    s.commandRevision = asfloat(engagementSlotAssignmentBuffer[b+6]);
    s.rangedStatus = asfloat(engagementSlotAssignmentBuffer[b+7]);
    s.rangedAnchor = asfloat(int2(engagementSlotAssignmentBuffer[b+8], engagementSlotAssignmentBuffer[b+9]));
    s.rangedSeconds = asfloat(engagementSlotAssignmentBuffer[b+10]);
    s.rangedTargetPlusOne = engagementSlotAssignmentBuffer[b+11];
    return s;
}
void WriteCongestion(uint index, CongestionWait s)
{
    uint b = CongestionOffset(index);
    engagementSlotAssignmentBuffer[b] = asint(s.anchor.x);
    engagementSlotAssignmentBuffer[b+1] = asint(s.anchor.y);
    engagementSlotAssignmentBuffer[b+2] = asint(s.forward.x);
    engagementSlotAssignmentBuffer[b+3] = asint(s.forward.y);
    engagementSlotAssignmentBuffer[b+4] = asint(s.observedSeconds);
    engagementSlotAssignmentBuffer[b+5] = asint(s.remainingSeconds);
    engagementSlotAssignmentBuffer[b+6] = asint(s.commandRevision);
    engagementSlotAssignmentBuffer[b+7] = asint(s.rangedStatus);
    engagementSlotAssignmentBuffer[b+8] = asint(s.rangedAnchor.x);
    engagementSlotAssignmentBuffer[b+9] = asint(s.rangedAnchor.y);
    engagementSlotAssignmentBuffer[b+10] = asint(s.rangedSeconds);
    engagementSlotAssignmentBuffer[b+11] = s.rangedTargetPlusOne;
}
void ClearCongestion(uint index)
{
    CongestionWait s = (CongestionWait)0;
    WriteCongestion(index, s);
}
bool TickCongestion(uint index, inout AgentData agent, inout CongestionWait s, float dt)
{
    int team = teamIdReadBuffer[index];
    float revision = 0.0;
    if (team >= 0 && team < teamCount) revision = (float)movementCommandRevisionBuffer[team];
    if (s.commandRevision != revision)
    {
        s = (CongestionWait)0; s.commandRevision = revision;
        agent.velocity = 0.0;
        WriteCongestion(index, s);
        return true; // New order is honored even on a skipped LOD decision frame.
    }
    if (s.remainingSeconds <= 0.0) return false;
    s.remainingSeconds = max(0.0, s.remainingSeconds - dt);
    WriteCongestion(index, s);
    return s.remainingSeconds <= 0.0; // Retry immediately at expiry, not one LOD beat later.
}
bool ResolveCongestion(uint index, AgentData agent, UnitTypeSettings settings, float2 direction,
    bool friendlyAhead, bool eligible, float dtSim, inout CongestionWait s)
{
    if (!eligible || dot(direction, direction) < 0.0001)
    {
        s.observedSeconds = 0.0; s.remainingSeconds = 0.0; s.forward = 0.0;
        WriteCongestion(index, s); return false;
    }
    if (s.remainingSeconds > 0.0) return true;
    float2 forward = normalize(direction);
    // Direction is the CURRENT navigation path, not the straight line to a clicked
    // destination. Legitimate turns/detours restart the observation window.
    if (!friendlyAhead || s.observedSeconds <= 0.0 || dot(forward, s.forward) < 0.5)
    {
        s.anchor = agent.position.xz; s.forward = forward; s.observedSeconds = 0.0;
    }
    if (friendlyAhead)
    {
        s.observedSeconds += dtSim;
        if (s.observedSeconds >= 0.4)
        {
            float scale = max(0.01, max(abs(agent.scale.x), abs(agent.scale.z)));
            float minimumProgressSpeed = max(0.06 * scale, min(settings.maxSpeed * 0.2, 0.6 * scale));
            float netProgress = max(0.0, dot(agent.position.xz - s.anchor, s.forward));
            if (netProgress < minimumProgressSpeed * s.observedSeconds)
                s.remainingSeconds = 1.1 + 0.9 * Hash01(index ^ 0x6C8E9CF5u);
            s.anchor = agent.position.xz; s.forward = forward; s.observedSeconds = 0.0;
        }
    }
    WriteCongestion(index, s);
    return s.remainingSeconds > 0.0;
}
#endif
