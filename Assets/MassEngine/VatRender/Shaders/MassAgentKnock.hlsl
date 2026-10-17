#ifndef MASS_AGENT_KNOCK_INCLUDED
#define MASS_AGENT_KNOCK_INCLUDED

// 炸飞 / knock-fly (2026-10-17, render only). The LOD classification kernel latches, once per
// death, whether the body was killed inside a blast of a unit type with CombatConfig.knockFlyHeight
// > 0 and writes _MassKnock[agent] = (landing offset x, landing offset z, apex height, 1 + whole tumble turns).
// Here the body follows a parabola to that offset and tumbles one full turn on the way; the corpse
// then stays at the landing point. agentBuffer is never changed: combat, positions and the
// corpse-despawn timing are exactly the same as without the effect.
// _MassKnockOn (global) = 0 whenever no knock buffer is bound; the buffer is then never read.
#define MASS_KNOCK_GRAVITY 20.0

float _MassKnockOn;
#ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
    StructuredBuffer<float4> _MassKnock;
#endif

float4 MassKnockFetch(uint agentIndex, int state)
{
#ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
    if (_MassKnockOn > 0.5 && state == 4)
        return _MassKnock[agentIndex];
#endif
    return float4(0.0, 0.0, 0.0, 0.0);
}

// World offset at this corpse age; angle = tumble angle in radians (0 when not tumbling).
float3 MassKnockOffset(float4 knock, float age, out float angle)
{
    angle = 0.0;
    float h = knock.z;
    if (h <= 0.0)
        return float3(0.0, 0.0, 0.0);
    float flight = 2.0 * sqrt(2.0 * h / MASS_KNOCK_GRAVITY);
    float t = saturate(age / max(flight, 0.0001));
    float turns = max(1.0, round(knock.w - 1.0));
    angle = t < 1.0 ? 6.2831853 * turns * t : 0.0;
    return float3(knock.x * t, 4.0 * h * t * (1.0 - t), knock.y * t);
}

// Rotates the already built instance matrices about the horizontal axis across the flight
// direction, around pivot (world). Head goes over toward the flight direction.
void MassKnockTumble(float4 knock, float angle, float3 pivot)
{
#ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
    if (angle == 0.0)
        return;
    float2 dir = knock.xy;
    float len = length(dir);
    float3 a = len > 0.0001 ? float3(dir.y, 0.0, -dir.x) / len : float3(1.0, 0.0, 0.0);
    float s = sin(angle);
    float c = cos(angle);
    float k = 1.0 - c;
    float3x3 r = float3x3(
        c + k * a.x * a.x,         k * a.x * a.y - s * a.z,  k * a.x * a.z + s * a.y,
        k * a.y * a.x + s * a.z,   c + k * a.y * a.y,        k * a.y * a.z - s * a.x,
        k * a.z * a.x - s * a.y,   k * a.z * a.y + s * a.x,  c + k * a.z * a.z);
    float3 tp = pivot - mul(r, pivot);
    float4x4 m = float4x4(
        r[0].x, r[0].y, r[0].z, tp.x,
        r[1].x, r[1].y, r[1].z, tp.y,
        r[2].x, r[2].y, r[2].z, tp.z,
        0.0, 0.0, 0.0, 1.0);
    float3x3 rt = transpose(r);
    float3 ti = -mul(rt, tp);
    float4x4 mi = float4x4(
        rt[0].x, rt[0].y, rt[0].z, ti.x,
        rt[1].x, rt[1].y, rt[1].z, ti.y,
        rt[2].x, rt[2].y, rt[2].z, ti.z,
        0.0, 0.0, 0.0, 1.0);
    unity_ObjectToWorld = mul(m, unity_ObjectToWorld);
    unity_WorldToObject = mul(unity_WorldToObject, mi);
#endif
}

#endif
