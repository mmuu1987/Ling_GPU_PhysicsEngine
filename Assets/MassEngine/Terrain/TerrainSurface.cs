using System;
using UnityEngine;

namespace MassEngine
{
    /// <summary>Immutable, single-valued world-space surface. See Terrain/README.md.</summary>
    public sealed class TerrainSurface
    {
        public string Id { get; }
        public int Version { get; }
        public int Width { get; }
        public int Depth { get; }
        public Vector2 Origin { get; }
        public Vector2 Size { get; }
        public Vector2 Step { get; }
        public float MaxSlopeDegrees { get; }
        public int CellCount => (Width - 1) * (Depth - 1);
        public long GpuBytes => ((long)heights.Length + CellCount) * sizeof(float);
        private readonly float[] heights;
        private readonly uint[] walkable;

        public TerrainSurface(string id, int version, int width, int depth, Vector2 origin,
            Vector2 size, float maxSlopeDegrees, float[] vertexHeights, bool[] blockedCells)
        {
            if (string.IsNullOrWhiteSpace(id) || id != id.Trim() || version < 1)
                throw new ArgumentException("Terrain needs a stable id and positive content version.");
            if (width < 2 || depth < 2 || width > 2049 || depth > 2049)
                throw new ArgumentException("Terrain vertex dimensions must be between 2 and 2049.");
            if (!Finite(origin.x) || !Finite(origin.y) || !Finite(size.x) || !Finite(size.y) ||
                size.x < .01f || size.y < .01f || !Finite(origin.x + size.x) || !Finite(origin.y + size.y) ||
                origin.x + size.x <= origin.x || origin.y + size.y <= origin.y)
                throw new ArgumentException("Terrain bounds must be finite and have positive representable size.");
            if (!Finite(maxSlopeDegrees) || maxSlopeDegrees < 0 || maxSlopeDegrees >= 89)
                throw new ArgumentException("Terrain slope limit must be finite and in [0, 89) degrees.");
            if (vertexHeights == null || vertexHeights.Length != width * depth ||
                blockedCells == null || blockedCells.Length != (width - 1) * (depth - 1))
                throw new ArgumentException("Terrain height and blocked-cell arrays do not match the grid.");
            foreach (float value in vertexHeights)
                if (!Finite(value) || Mathf.Abs(value) > 100000f)
                    throw new ArgumentException("Terrain heights must be finite world meters within +/-100000.");
            Id = id; Version = version; Width = width; Depth = depth; Origin = origin; Size = size;
            MaxSlopeDegrees = maxSlopeDegrees;
            Step = new Vector2(size.x / (width - 1), size.y / (depth - 1));
            heights = (float[])vertexHeights.Clone();
            walkable = new uint[CellCount];
            float limit = Mathf.Tan(maxSlopeDegrees * Mathf.Deg2Rad);
            for (int z = 0; z < depth - 1; z++)
                for (int x = 0; x < width - 1; x++)
                {
                    GetGradients(x, z, out Vector2 lower, out Vector2 upper);
                    int cell = z * (width - 1) + x;
                    walkable[cell] = !blockedCells[cell] && lower.sqrMagnitude <= limit * limit + 1e-6f &&
                        upper.sqrMagnitude <= limit * limit + 1e-6f ? 1u : 0u;
                }
        }

        public float HeightAtVertex(int x, int z) => heights[z * Width + x];
        public bool IsCellWalkable(int x, int z) => x >= 0 && z >= 0 && x < Width - 1 && z < Depth - 1 &&
            walkable[z * (Width - 1) + x] != 0;

        public bool TrySample(Vector2 worldXZ, out TerrainSurfaceSample sample)
        {
            sample = default;
            Vector2 local = worldXZ - Origin;
            if (!Finite(worldXZ.x) || !Finite(worldXZ.y) || local.x < 0 || local.y < 0 ||
                local.x > Size.x || local.y > Size.y) return false;
            Vector2 grid = new Vector2(local.x / Step.x, local.y / Step.y);
            int x = Mathf.Min(Mathf.FloorToInt(grid.x), Width - 2);
            int z = Mathf.Min(Mathf.FloorToInt(grid.y), Depth - 2);
            float u = Mathf.Clamp01(grid.x - x), v = Mathf.Clamp01(grid.y - z);
            GetGradients(x, z, out Vector2 lower, out Vector2 upper);
            Vector2 gradient = u >= v ? lower : upper;
            float height = HeightAtVertex(x, z) + gradient.x * u * Step.x + gradient.y * v * Step.y;
            sample = new TerrainSurfaceSample(new Vector3(worldXZ.x, height, worldXZ.y), gradient, IsCellWalkable(x, z));
            return true;
        }

        private void GetGradients(int x, int z, out Vector2 lower, out Vector2 upper)
        {
            float a = HeightAtVertex(x, z), b = HeightAtVertex(x + 1, z);
            float c = HeightAtVertex(x, z + 1), d = HeightAtVertex(x + 1, z + 1);
            lower = new Vector2((b - a) / Step.x, (d - b) / Step.y);
            upper = new Vector2((d - c) / Step.x, (c - a) / Step.y);
        }

        internal float[] CopyHeights() => (float[])heights.Clone();
        internal uint[] CopyWalkable() => (uint[])walkable.Clone();
        internal static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }

    public readonly struct TerrainSurfaceSample
    {
        public readonly Vector3 Position;
        public readonly Vector2 Gradient;
        public readonly bool Walkable;
        public Vector3 Normal => new Vector3(-Gradient.x, 1, -Gradient.y).normalized;

        public TerrainSurfaceSample(Vector3 position, Vector2 gradient, bool walkable)
        { Position = position; Gradient = gradient; Walkable = walkable; }

        public Vector3 SurfaceVelocity(Vector2 horizontalVelocity)
        {
            var tangent = new Vector3(horizontalVelocity.x, Vector2.Dot(Gradient, horizontalVelocity), horizontalVelocity.y);
            return tangent.sqrMagnitude > 0 ? tangent.normalized * horizontalVelocity.magnitude : Vector3.zero;
        }
    }
}
