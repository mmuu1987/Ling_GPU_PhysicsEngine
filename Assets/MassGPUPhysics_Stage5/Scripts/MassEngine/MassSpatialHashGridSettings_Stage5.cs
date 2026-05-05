using UnityEngine;

public readonly struct MassSpatialHashGridSettings_Stage5
{
    public readonly int ResolutionX;
    public readonly int ResolutionZ;
    public readonly int CellCount;
    public readonly Vector2 WorldSize;
    public readonly Vector2 Origin;

    private MassSpatialHashGridSettings_Stage5(int resolutionX, int resolutionZ, Vector2 worldSize, Vector2 origin)
    {
        ResolutionX = resolutionX;
        ResolutionZ = resolutionZ;
        CellCount = resolutionX * resolutionZ;
        WorldSize = worldSize;
        Origin = origin;
    }

    public static MassSpatialHashGridSettings_Stage5 Calculate(
        Vector2 requestedWorldSize,
        Vector3 spawnArea,
        float boundaryPadding,
        float cellSize)
    {
        cellSize = Mathf.Max(0.1f, cellSize);

        Vector2 worldSize = new Vector2(
            requestedWorldSize.x > 0f ? requestedWorldSize.x : spawnArea.x * 2f + boundaryPadding * 2f,
            requestedWorldSize.y > 0f ? requestedWorldSize.y : spawnArea.z * 2f + boundaryPadding * 2f);

        worldSize.x = Mathf.Max(worldSize.x, cellSize);
        worldSize.y = Mathf.Max(worldSize.y, cellSize);

        int resolutionX = Mathf.Max(1, Mathf.CeilToInt(worldSize.x / cellSize));
        int resolutionZ = Mathf.Max(1, Mathf.CeilToInt(worldSize.y / cellSize));
        Vector2 origin = worldSize * -0.5f;

        return new MassSpatialHashGridSettings_Stage5(resolutionX, resolutionZ, worldSize, origin);
    }

    public static MassSpatialHashGridSettings_Stage5 FromBounds(Vector2 min, Vector2 max, float cellSize)
    {
        cellSize = Mathf.Max(0.1f, cellSize);

        Vector2 worldSize = new Vector2(
            Mathf.Max(cellSize, max.x - min.x),
            Mathf.Max(cellSize, max.y - min.y));

        int resolutionX = Mathf.Max(1, Mathf.CeilToInt(worldSize.x / cellSize));
        int resolutionZ = Mathf.Max(1, Mathf.CeilToInt(worldSize.y / cellSize));

        return new MassSpatialHashGridSettings_Stage5(resolutionX, resolutionZ, worldSize, min);
    }
}
