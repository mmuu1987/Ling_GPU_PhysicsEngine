#ifndef MASS_RANGED_CLEARANCE_INCLUDED
#define MASS_RANGED_CLEARANCE_INCLUDED
#include "../../Projectiles/Shaders/ProjectileGeometry.hlsl"
#include "../../Projectiles/Shaders/ProjectileUnitCollision.hlsl"
#ifdef MASS_TERRAIN_ENABLED
#include "../../Terrain/Shaders/TerrainCollision.hlsl"
#endif

// Must match ProjectileBallistics.cs; native parity tests exercise both implementations.
float RangedClearance(float sourceHalf, float sourceScale, float targetHalf, float targetScale, float horizontalDistance)
{
    float body = 2.0 * max(max(0.05, sourceHalf) * max(0.01, abs(sourceScale)),
        max(0.05, targetHalf) * max(0.01, abs(targetScale)));
    return clamp(max(body * 1.25, horizontalDistance * body * 0.2), 0.5, 12.0);
}
float RangedFlightTime(float3 origin, float3 aim, float speed, float gravity, float lifetime, float clearance)
{
    if (speed <= 0.0 || lifetime <= 0.0) return 0.0;
    float time = (gravity < -0.01 ? length(aim.xz-origin.xz) : length(aim-origin)) / speed;
    if (time > lifetime || time < 0.00001) return 0.0;
    if (gravity < -0.01 && clearance > 0.0)
    {
        float apex = max(origin.y, aim.y) + clearance;
        float loft = sqrt(2.0 * (apex-origin.y) / -gravity) + sqrt(2.0 * (apex-aim.y) / -gravity);
        time = max(time, min(loft, lifetime * 0.95));
    }
    return time;
}

bool RangedPathClear(uint index, AgentData source, int target, UnitTypeSettings settings)
{
    AgentData victim = agentBuffer[target];
    UnitTypeSettings targetSettings = GetUnitSettings((uint)target);
    float2 targetXZ = agentPositionReadBuffer[target];
    float3 origin = source.position;
    float3 aim = float3(targetXZ.x, victim.position.y, targetXZ.y);
#ifdef MASS_TERRAIN_ENABLED
    float4 a = SampleTerrainSurface(origin.xz), b = SampleTerrainSurface(targetXZ);
    if (a.w < 0.0 || b.w < 0.0) return false;
    origin.y = a.x; aim.y = b.x;
#endif
    origin.y += max(0.05, settings.projectileOriginHeight) * max(0.01, abs(source.scale.y));
    aim.y += max(0.05, targetSettings.projectileTargetHeight) * max(0.01, abs(victim.scale.y));
    float2 direction = aim.xz - origin.xz;
    float distance = length(direction);
    if (distance < 0.001) return false;
    float radius = max(0.05, settings.agentRadius) * max(abs(source.scale.x), abs(source.scale.z));
    origin.xz += direction / distance * min(radius, distance * 0.25);
    float clearance = RangedClearance(settings.projectileTargetHeight, source.scale.y,
        targetSettings.projectileTargetHeight, victim.scale.y, distance);
    float time = RangedFlightTime(origin, aim, settings.projectileSpeed, settings.projectileGravity,
        settings.projectileMaxLifetime, clearance);
    if (time <= 0.0) return false;
    float3 velocity = (aim-origin) / time;
    if (settings.projectileGravity < -0.01) velocity.y -= 0.5 * settings.projectileGravity * time;
    ProjectileData shot = (ProjectileData)0;
    shot.sourceAgentIndexPlusOne = (int)index + 1;
    shot.hitRadius = settings.projectileHitRadius;
    float padding = max(0.0, projectileQueryRadius) + max(0.0, shot.hitRadius);
    uint agentCount, stride; agentBuffer.GetDimensions(agentCount, stride);
    int segments = clamp((int)ceil(distance / max(0.5, cellSize)), 4, 16);
    float3 start = origin;
    uint inspected = 0;
    [loop] for (int step = 1; step <= segments; step++)
    {
        float t = time * (float)step / (float)segments;
        float3 end = origin + velocity*t + float3(0, 0.5*settings.projectileGravity*t*t, 0);
        float nearest = 2.0; int hit = -1;
#ifdef MASS_TERRAIN_ENABLED
        float terrainT;
        if (SweepTerrainSurface(start, end, terrainT)) nearest = terrainT;
#else
        if (min(start.y, end.y) <= 0.0) return false;
#endif
        int2 first = PositionXzToCell(min(start.xz,end.xz)-padding);
        int2 last = PositionXzToCell(max(start.xz,end.xz)+padding);
        // Readiness probes must never fall back to scanning the whole army. An
        // overloaded/very large query waits; actual released shots retain exact collision.
        if ((last.x-first.x+1)*(last.y-first.y+1) > 64) return false;
        [loop] for (int z=first.y; z<=last.y; z++)
        [loop] for (int x=first.x; x<=last.x; x++)
        {
            uint cell = CellToIndex(int2(x,z));
            uint count = gridCountsReadBuffer[cell];
            if (count > maxAgentsPerCell || inspected + count > 1024u) return false;
            inspected += count;
            [loop] for (uint slot=0; slot<count; slot++)
            {
                uint other = gridAgentIndicesReadBuffer[cell*maxAgentsPerCell+slot];
                if (other < agentCount) ConsiderProjectileUnit(other, shot, start, end-start, nearest, hit);
            }
        }
        if (nearest <= 1.0)
        {
            if (hit < 0) return false;
            return teamIdReadBuffer[hit] != teamIdReadBuffer[index]; // An intervening enemy is a real valid hit.
        }
        start = end;
    }
    return true;
}

float RangedSideBudget(AgentData agent, UnitTypeSettings settings)
{
    float radius = max(0.05, settings.agentRadius) * max(0.01,max(abs(agent.scale.x),abs(agent.scale.z)));
    return clamp(radius*4.0, 0.5, 4.0);
}

bool ResolveRangedClearance(uint index, AgentData agent, int target, UnitTypeSettings settings,
    int simInterval, float dtSim, bool allowReposition, inout CongestionWait state)
{
    if (state.rangedTargetPlusOne != target+1)
    {
        state.rangedTargetPlusOne = target+1;
        state.rangedStatus = 0.0; state.rangedSeconds = 0.0;
        state.rangedAnchor = agent.position.xz;
    }
    uint period = max(1u, 24u / (uint)max(1,simInterval));
    bool refresh = ((frameIndex / (uint)max(1,simInterval) + (index * 17u)) % period) == 0u;
    if (state.rangedStatus == 0.0 || refresh)
    {
        bool clear = RangedPathClear(index, agent, target, settings);
        if (clear) state.rangedStatus = 1.0;
        else if (state.rangedStatus >= 0.0)
        {
            state.rangedStatus = (Hash01(index ^ 0xB5297A4Du) < 0.5) ? -1.0 : -2.0;
            if (allowReposition)
            {
                float2 toTarget = agentPositionReadBuffer[target]-agent.position.xz;
                if (dot(toTarget,toTarget)>0.0001)
                {
                    float2 side = normalize(float2(-toTarget.y,toTarget.x)) * (RangedSideBudget(agent,settings)*0.6);
                    AgentData left=agent, right=agent;
                    left.position.xz+=side; right.position.xz-=side;
                    bool leftClear=true, rightClear=true;
#ifdef MASS_TERRAIN_ENABLED
                    leftClear=TerrainSegmentClear(agent.position.xz,left.position.xz);
                    rightClear=TerrainSegmentClear(agent.position.xz,right.position.xz);
#endif
                    if (leftClear) leftClear=RangedPathClear(index,left,target,settings);
                    if (rightClear) rightClear=RangedPathClear(index,right,target,settings);
                    if (leftClear != rightClear) state.rangedStatus=leftClear ? -1.0 : -2.0;
                }
            }
            state.rangedAnchor = agent.position.xz; state.rangedSeconds = 0.0;
        }
    }
    if (state.rangedStatus < 0.0)
    {
        state.rangedSeconds += dtSim;
        if (state.rangedSeconds >= 2.0 || length(agent.position.xz-state.rangedAnchor) >= RangedSideBudget(agent,settings))
            state.rangedStatus = (state.rangedStatus == -1.0 || state.rangedStatus == -3.0) ? -3.0 : -4.0;
    }
    WriteCongestion(index, state);
    return state.rangedStatus > 0.0;
}
float2 RangedSideDirection(AgentData agent, float2 target, UnitTypeSettings settings, CongestionWait state)
{
    if (state.rangedSeconds >= 2.0 || length(agent.position.xz-state.rangedAnchor) >= RangedSideBudget(agent,settings)) return 0.0;
    float2 toTarget = target-agent.position.xz;
    if (dot(toTarget,toTarget)<0.0001) return 0.0;
    float2 side = normalize(float2(-toTarget.y,toTarget.x));
    return state.rangedStatus == -1.0 ? side : -side;
}
// Applied on ordinary AND skipped-LOD integration. Momentum must not carry a
// blocked shooter past the side-step budget while its next decision is deferred.
void LimitRangedReposition(inout AgentData agent, UnitTypeSettings settings, CongestionWait state, float dt)
{
    if (state.rangedStatus >= 0.0) return;
    float scale = max(0.01,max(abs(agent.scale.x),abs(agent.scale.z)));
    float remaining = max(0.0, RangedSideBudget(agent,settings)-length(agent.position.xz-state.rangedAnchor));
    float speed = state.rangedSeconds < 2.0 ? max(0.08*scale, min(0.8*scale, remaining/max(dt,0.0001))) : 0.08*scale;
    float actual = length(agent.velocity.xz);
    if (actual > speed) agent.velocity.xz *= speed / max(actual,0.0001);
}
#endif
