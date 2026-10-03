using System;
using UnityEngine;

namespace MassEngine
{
    [CreateAssetMenu(menuName = "MassEngine/Terrain Surface")]
    public sealed class TerrainSurfaceAsset : ScriptableObject
    {
        [SerializeField] private string terrainId;
        [SerializeField] private int contentVersion = 1;
        [SerializeField] private Vector2 origin;
        [SerializeField] private Vector2 size;
        [SerializeField] private int width, depth;
        [SerializeField] private float maxSlopeDegrees = 40;
        [SerializeField, HideInInspector] private float[] heights;
        [SerializeField, HideInInspector] private bool[] blockedCells;

        public string Id => terrainId;
        public int Version => contentVersion;

        public void Initialize(string id, int version, int columns, int rows, Vector2 worldOrigin,
            Vector2 worldSize, float slopeLimit, float[] vertexHeights, bool[] blocked)
        {
            // Validate before replacing any authored data. Arrays never escape the asset.
            _ = new TerrainSurface(id, version, columns, rows, worldOrigin, worldSize, slopeLimit, vertexHeights, blocked);
            terrainId = id; contentVersion = version; width = columns; depth = rows;
            origin = worldOrigin; size = worldSize; maxSlopeDegrees = slopeLimit;
            heights = (float[])vertexHeights.Clone(); blockedCells = (bool[])blocked.Clone();
        }

        public bool TryCreateSurface(out TerrainSurface surface, out string error)
        {
            surface = null; error = null;
            try { surface = new TerrainSurface(terrainId, contentVersion, width, depth, origin, size, maxSlopeDegrees, heights, blockedCells); }
            catch (ArgumentException ex) { error = ex.Message; }
            return surface != null;
        }
    }
}
