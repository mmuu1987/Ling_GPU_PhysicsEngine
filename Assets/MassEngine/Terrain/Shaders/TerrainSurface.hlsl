#ifndef MASS_TERRAIN_SURFACE_INCLUDED
#define MASS_TERRAIN_SURFACE_INCLUDED
StructuredBuffer<float> _SurfaceHeights;
StructuredBuffer<uint> _SurfaceWalkable;
int2 _SurfaceDimensions;
float4 _SurfaceBounds; // origin XZ, size XZ

// Returns height, gradient X, gradient Z, walkability (-1 means outside).
float4 SampleTerrainSurface(float2 worldXZ)
{
    float2 local = worldXZ - _SurfaceBounds.xy;
    if (any(!isfinite(worldXZ)) || any(local < 0) || any(local > _SurfaceBounds.zw))
        return float4(0, 0, 0, -1);
    float2 stepSize = _SurfaceBounds.zw / (_SurfaceDimensions - 1);
    float2 grid = local / stepSize;
    int2 cell = min((int2)floor(grid), _SurfaceDimensions - 2);
    float2 uv = saturate(grid - cell);
    int i = cell.y * _SurfaceDimensions.x + cell.x;
    float a = _SurfaceHeights[i], b = _SurfaceHeights[i + 1];
    float c = _SurfaceHeights[i + _SurfaceDimensions.x], d = _SurfaceHeights[i + _SurfaceDimensions.x + 1];
    float2 gradient = uv.x >= uv.y ? float2(b - a, d - b) / stepSize : float2(d - c, c - a) / stepSize;
    float height = a + dot(gradient, uv * stepSize);
    return float4(height, gradient, _SurfaceWalkable[cell.y * (_SurfaceDimensions.x - 1) + cell.x]);
}

float3 TerrainSurfaceVelocity(float2 velocity, float2 gradient)
{
    float3 tangent = float3(velocity.x, dot(velocity, gradient), velocity.y);
    return dot(tangent, tangent) > 0 ? normalize(tangent) * length(velocity) : 0;
}
#endif
