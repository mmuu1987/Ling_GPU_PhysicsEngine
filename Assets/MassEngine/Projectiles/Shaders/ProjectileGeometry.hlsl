#ifndef MASS_PROJECTILE_GEOMETRY_INCLUDED
#define MASS_PROJECTILE_GEOMETRY_INCLUDED
// Earliest sphere entry, not closest approach: terrain behind the sphere's front
// must not swallow a hit just because it lies before the sphere's centre.
bool ProjectileSphereEntry(float3 start, float3 travel, float3 centre, float radius, out float hitT)
{
    hitT = 2.0;
    float3 offset = start - centre;
    float c = dot(offset, offset) - radius * radius;
    if (c <= 0.0)
    {
        hitT = 0.0;
        return true;
    }
    float a = dot(travel, travel);
    float b = dot(offset, travel);
    if (a == 0.0 || b >= 0.0)
        return false;
    float discriminant = b * b - a * c;
    if (discriminant < 0.0)
        return false;
    hitT = c / (-b + sqrt(discriminant));
    return hitT <= 1.0;
}

// 弹道数据结构（与 C# ProjectileGpuData 对齐，64 字节）
struct ProjectileData
{
    float3 position;        // 当前位置 (12 bytes)
    float launchTime;       // 发射时间 (4 bytes)

    float3 velocity;        // 当前速度 (12 bytes)
    float damage;           // 伤害值 (4 bytes)

    int targetAgentIndex;   // 目标索引（-1 = 空闲槽位）(4 bytes)
    int sourceTeamId;       // 发射方队伍 (4 bytes)
    float hitRadius;        // 命中半径 (4 bytes)
    float gravity;          // 重力加速度 (4 bytes)

    float maxLifetime;      // 最大飞行时间 (4 bytes)
    float trailLength;      // 曳光长度 (4 bytes)
    int sourceAgentIndexPlusOne;
    float splashRadius;     // 原 padding 槽位：落点溅射半径，0 = 单体（默认）(4 bytes)
};

bool ProjectileCapsuleEntry(float3 start, float3 travel, float3 lower, float3 upper, float radius, out float hitT)
{
    hitT = 2.0;
    float t;
    if (ProjectileSphereEntry(start, travel, lower, radius, t)) hitT = min(hitT, t);
    if (ProjectileSphereEntry(start, travel, upper, radius, t)) hitT = min(hitT, t);
    // Vertical cylinder between the two hemisphere centres, including initial overlap.
    float2 offset = start.xz - lower.xz;
    float c = dot(offset, offset) - radius * radius;
    if (c <= 0.0 && start.y >= lower.y && start.y <= upper.y) hitT = 0.0;
    float a = dot(travel.xz, travel.xz);
    float b = dot(offset, travel.xz);
    float d = b * b - a * c;
    if (a > 1e-12 && d >= 0.0)
    {
        t = (-b - sqrt(d)) / a;
        float y = start.y + travel.y * t;
        if (t >= 0.0 && t <= 1.0 && y >= lower.y && y <= upper.y) hitT = min(hitT, t);
    }
    return hitT <= 1.0;
}


#endif
