using UnityEngine;
using UnityEngine.Rendering;

namespace MassEngine
{
    /// <summary>Deterministic M6.1 fixture: plateau, ramps, cliffs, canyon and an authored exclusion.</summary>
    public static class TerrainPrototype
    {
        public static TerrainSurfaceAsset CreateAsset()
        {
            const int vertices = 257;
            const float extent = 720;
            float step = extent / (vertices - 1);
            var heights = new float[vertices * vertices];
            var blocked = new bool[(vertices - 1) * (vertices - 1)];
            for (int z = 0; z < vertices; z++)
                for (int x = 0; x < vertices; x++)
                {
                    float wx = -360 + x * step, wz = -360 + z * step;
                    float exitEnd = Mathf.Lerp(160, 68, Ramp(12, 24, Mathf.Abs(wz)));
                    float plateau = 48 * Ramp(-260, -160, wx) * (1 - Ramp(60, exitEnd, wx)) *
                        (1 - Ramp(68, 76, Mathf.Abs(wz)));
                    float canyon = 48 * Ramp(170, 178, wx) * (1 - Ramp(300, 308, wx)) *
                        Ramp(24, 32, Mathf.Abs(wz)) * (1 - Ramp(96, 104, Mathf.Abs(wz)));
                    heights[z * vertices + x] = Mathf.Max(plateau, canyon);
                    if (x < vertices - 1 && z < vertices - 1)
                        blocked[z * (vertices - 1) + x] = new Vector2(wx + step * .5f + 80, wz + step * .5f + 40).sqrMagnitude < 18 * 18;
                }
            var asset = ScriptableObject.CreateInstance<TerrainSurfaceAsset>();
            asset.name = "Plateau And Canyon";
            asset.Initialize("m61-plateau-canyon", 1, vertices, vertices, new Vector2(-360, -360),
                new Vector2(extent, extent), 40, heights, blocked);
            return asset;
        }

        public static Mesh CreateMesh(TerrainSurface surface)
        {
            var vertices = new Vector3[surface.Width * surface.Depth];
            var colors = new Color32[vertices.Length];
            var indices = new int[surface.CellCount * 6];
            for (int z = 0; z < surface.Depth; z++)
                for (int x = 0; x < surface.Width; x++)
                {
                    int i = z * surface.Width + x;
                    float height = surface.HeightAtVertex(x, z);
                    vertices[i] = new Vector3(surface.Origin.x + x * surface.Step.x, height, surface.Origin.y + z * surface.Step.y);
                    bool passable = surface.IsCellWalkable(Mathf.Min(x, surface.Width - 2), Mathf.Min(z, surface.Depth - 2));
                    colors[i] = passable ? Color.Lerp(new Color(.16f, .32f, .25f), new Color(.7f, .72f, .38f), Mathf.Clamp01(height / 48))
                        : new Color(.64f, .22f, .16f);
                    if (x == surface.Width - 1 || z == surface.Depth - 1) continue;
                    int t = (z * (surface.Width - 1) + x) * 6;
                    indices[t] = i; indices[t + 1] = i + surface.Width + 1; indices[t + 2] = i + 1;
                    indices[t + 3] = i; indices[t + 4] = i + surface.Width; indices[t + 5] = i + surface.Width + 1;
                }
            var mesh = new Mesh { name = "M6.1 Continuous Surface", indexFormat = IndexFormat.UInt32 };
            mesh.vertices = vertices; mesh.colors32 = colors; mesh.triangles = indices;
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }

        private static float Ramp(float from, float to, float value) => Mathf.Clamp01((value - from) / (to - from));
    }
}
