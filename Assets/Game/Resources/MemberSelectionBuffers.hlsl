// Read-only P7 layout mirrors AgentData 64B and UnitTypeGpuSettings 144B; never the LOD instance ordering.
#ifndef MEMBER_SELECTION_BUFFERS
#define MEMBER_SELECTION_BUFFERS
struct AgentData
{
    float3 position;
    float3 rotation;
    float3 scale;
    float3 velocity;
    int currentState;
    float currentAnimationTime;
    int presentationState;
    float locomotionSpeed;
};

// Mirror of MassEngine.UnitTypeGpuSettings (144 bytes, sequential layout).
struct UnitTypeSettings
{
    float agentRadius;
    float separationStrength;
    float velocityDamping;
    float maxSpeed;
    float targetAcquireRadius;
    float attackRange;
    float attackInterval;
    int attackDamage;
    float densityAvoidanceStrength;
    float densityPressureRangePerSqm;
    float densitySpeedPenalty;
    float speedVariation;
    float laneBiasStrength;
    float flowFieldWeight;
    float flowFieldResponsiveness;
    float attractionStrength;
    float idleClipDuration;
    float moveClipDuration;
    float attackClipDuration;
    float deathClipDuration;
    float moveAnimationSpeedMin;
    float moveAnimationSpeedMax;
    float densityComfortPerSqm;
    float projectileRange;
    float projectileSpeed;
    float projectileGravity;
    float projectileHitRadius;
    float projectileMaxLifetime;
    // Consumed by the CPU projectile launcher; kept here to mirror the 144-byte stride.
    float projectileTrailLength;
    int teamId;
    float moveReferenceSpeed;
    float moveStopSpeed;
    float moveStartSpeed;
    // Former reserved slots; C# / HLSL layout remains 36 x 4 = 144 bytes.
    float attackReleasePhase;
    float projectileOriginHeight;
    float projectileTargetHeight;
};


#endif
