using UnityEngine;

[CreateAssetMenu(fileName = "PaintedFlowField_Stage5", menuName = "MassGPUPhysics/Stage5/Painted Flow Field")]
public sealed class PaintedFlowFieldAsset_Stage5 : ScriptableObject
{
    [Min(1)] public int resolutionX = 128;
    [Min(1)] public int resolutionZ = 128;
    public Vector2 origin = new Vector2(-100f, -100f);
    public Vector2 worldSize = new Vector2(200f, 200f);
    [Min(0.01f)] public float cellSize = 1.5625f;

    [SerializeField] private Vector4[] cells = new Vector4[128 * 128];

    public Vector4[] Cells => cells;

    public int CellCount => Mathf.Max(1, resolutionX * resolutionZ);

    public void ConfigureFromWorld(Vector2 newOrigin, Vector2 newWorldSize, float requestedCellSize)
    {
        origin = newOrigin;
        worldSize = new Vector2(Mathf.Max(0.01f, newWorldSize.x), Mathf.Max(0.01f, newWorldSize.y));
        cellSize = Mathf.Max(0.01f, requestedCellSize);
        resolutionX = Mathf.Max(1, Mathf.CeilToInt(worldSize.x / cellSize));
        resolutionZ = Mathf.Max(1, Mathf.CeilToInt(worldSize.y / cellSize));
        EnsureCellArray();
    }

    public void EnsureCellArray()
    {
        int expected = CellCount;
        if (cells != null && cells.Length == expected)
            return;

        var resized = new Vector4[expected];
        if (cells != null)
        {
            int copyCount = Mathf.Min(cells.Length, resized.Length);
            for (int i = 0; i < copyCount; i++)
                resized[i] = cells[i];
        }

        cells = resized;
    }

    public bool TryWorldToCell(Vector2 world, out int x, out int z)
    {
        Vector2 local = world - origin;
        x = Mathf.FloorToInt(local.x / Mathf.Max(cellSize, 0.0001f));
        z = Mathf.FloorToInt(local.y / Mathf.Max(cellSize, 0.0001f));
        return x >= 0 && z >= 0 && x < resolutionX && z < resolutionZ;
    }

    public Vector2 CellCenter(int x, int z)
    {
        return origin + new Vector2((x + 0.5f) * cellSize, (z + 0.5f) * cellSize);
    }

    public void PaintStroke(Vector2 previousWorld, Vector2 currentWorld, float radius, float speed01, float strength01, bool erase)
    {
        EnsureCellArray();

        Vector2 stroke = currentWorld - previousWorld;
        if (!erase && stroke.sqrMagnitude < 0.000001f)
            return;

        Vector2 direction = stroke.normalized;
        radius = Mathf.Max(cellSize * 0.5f, radius);
        speed01 = Mathf.Clamp01(speed01);
        strength01 = Mathf.Clamp01(strength01);

        Vector2 min = currentWorld - Vector2.one * radius;
        Vector2 max = currentWorld + Vector2.one * radius;
        int minX = Mathf.Clamp(Mathf.FloorToInt((min.x - origin.x) / cellSize), 0, resolutionX - 1);
        int maxX = Mathf.Clamp(Mathf.FloorToInt((max.x - origin.x) / cellSize), 0, resolutionX - 1);
        int minZ = Mathf.Clamp(Mathf.FloorToInt((min.y - origin.y) / cellSize), 0, resolutionZ - 1);
        int maxZ = Mathf.Clamp(Mathf.FloorToInt((max.y - origin.y) / cellSize), 0, resolutionZ - 1);

        float radiusSqr = radius * radius;
        for (int z = minZ; z <= maxZ; z++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                Vector2 center = CellCenter(x, z);
                float distSqr = (center - currentWorld).sqrMagnitude;
                if (distSqr > radiusSqr)
                    continue;

                float distance01 = Mathf.Sqrt(distSqr) / radius;
                float falloff = 1f - Mathf.SmoothStep(0f, 1f, distance01);
                float influence = Mathf.Clamp01(falloff * strength01);
                int index = ToIndex(x, z);

                if (erase)
                {
                    cells[index] = Vector4.Lerp(cells[index], Vector4.zero, influence);
                    if (cells[index].w < 0.02f)
                        cells[index] = Vector4.zero;
                    continue;
                }

                Vector4 old = cells[index];
                Vector2 oldDirection = new Vector2(old.x, old.y);
                if (oldDirection.sqrMagnitude <= 0.0001f)
                    oldDirection = direction;

                Vector2 mixedDirection = Vector2.Lerp(oldDirection.normalized, direction, influence).normalized;
                float mixedSpeed = Mathf.Lerp(old.z, speed01, influence);
                float mixedWeight = Mathf.Clamp01(Mathf.Max(old.w, influence));
                cells[index] = new Vector4(mixedDirection.x, mixedDirection.y, mixedSpeed, mixedWeight);
            }
        }
    }

    public void Clear()
    {
        EnsureCellArray();
        for (int i = 0; i < cells.Length; i++)
            cells[i] = Vector4.zero;
    }

    public void GenerateUniformDirection(float angleDegrees, float speed01, float weight01)
    {
        EnsureCellArray();

        float radians = angleDegrees * Mathf.Deg2Rad;
        Vector2 direction = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians)).normalized;
        Vector4 value = BuildPresetCell(direction, speed01, weight01);

        for (int i = 0; i < cells.Length; i++)
            cells[i] = value;
    }

    public void GenerateConvergeToPoint(Vector2 targetWorld, float speed01, float weight01, float stopRadius)
    {
        EnsureCellArray();

        stopRadius = Mathf.Max(0f, stopRadius);
        for (int z = 0; z < resolutionZ; z++)
        {
            for (int x = 0; x < resolutionX; x++)
            {
                Vector2 offset = targetWorld - CellCenter(x, z);
                int index = ToIndex(x, z);
                if (offset.sqrMagnitude <= stopRadius * stopRadius || offset.sqrMagnitude <= 0.000001f)
                {
                    cells[index] = Vector4.zero;
                    continue;
                }

                cells[index] = BuildPresetCell(offset.normalized, speed01, weight01);
            }
        }
    }

    public void Smooth(int iterations)
    {
        EnsureCellArray();
        iterations = Mathf.Max(1, iterations);
        var temp = new Vector4[cells.Length];

        for (int iteration = 0; iteration < iterations; iteration++)
        {
            for (int z = 0; z < resolutionZ; z++)
            {
                for (int x = 0; x < resolutionX; x++)
                {
                    Vector2 vectorSum = Vector2.zero;
                    float speedSum = 0f;
                    float weightSum = 0f;

                    for (int dz = -1; dz <= 1; dz++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = x + dx;
                            int nz = z + dz;
                            if (nx < 0 || nz < 0 || nx >= resolutionX || nz >= resolutionZ)
                                continue;

                            Vector4 sample = cells[ToIndex(nx, nz)];
                            float weight = sample.w * (dx == 0 && dz == 0 ? 2f : 1f);
                            if (weight <= 0.0001f)
                                continue;

                            vectorSum += new Vector2(sample.x, sample.y) * sample.z * weight;
                            speedSum += sample.z * weight;
                            weightSum += weight;
                        }
                    }

                    int index = ToIndex(x, z);
                    if (weightSum <= 0.0001f)
                    {
                        temp[index] = Vector4.zero;
                        continue;
                    }

                    Vector2 direction = vectorSum.sqrMagnitude > 0.0001f ? vectorSum.normalized : Vector2.zero;
                    temp[index] = new Vector4(direction.x, direction.y, Mathf.Clamp01(speedSum / weightSum), Mathf.Clamp01(weightSum / 10f));
                }
            }

            Vector4[] swap = cells;
            cells = temp;
            temp = swap;
        }
    }

    public void GenerateEdgePadding(int radiusCells, float maxSpeed01, float maxWeight01)
    {
        EnsureCellArray();
        radiusCells = Mathf.Max(1, radiusCells);
        maxSpeed01 = Mathf.Clamp01(maxSpeed01);
        maxWeight01 = Mathf.Clamp01(maxWeight01);

        var source = new Vector4[cells.Length];
        for (int i = 0; i < cells.Length; i++)
            source[i] = cells[i];

        int radiusSqr = radiusCells * radiusCells;
        for (int z = 0; z < resolutionZ; z++)
        {
            for (int x = 0; x < resolutionX; x++)
            {
                int index = ToIndex(x, z);
                if (IsValidFlow(source[index]))
                    continue;

                int nearestX = -1;
                int nearestZ = -1;
                int bestDistanceSqr = int.MaxValue;

                for (int dz = -radiusCells; dz <= radiusCells; dz++)
                {
                    for (int dx = -radiusCells; dx <= radiusCells; dx++)
                    {
                        if (dx == 0 && dz == 0)
                            continue;

                        int distanceSqr = dx * dx + dz * dz;
                        if (distanceSqr > radiusSqr || distanceSqr >= bestDistanceSqr)
                            continue;

                        int nx = x + dx;
                        int nz = z + dz;
                        if (nx < 0 || nz < 0 || nx >= resolutionX || nz >= resolutionZ)
                            continue;

                        if (!IsValidFlow(source[ToIndex(nx, nz)]))
                            continue;

                        bestDistanceSqr = distanceSqr;
                        nearestX = nx;
                        nearestZ = nz;
                    }
                }

                if (nearestX < 0)
                    continue;

                Vector2 toNearest = new Vector2(nearestX - x, nearestZ - z);
                if (toNearest.sqrMagnitude <= 0.0001f)
                    continue;

                float distance01 = Mathf.Sqrt(bestDistanceSqr) / radiusCells;
                float falloff = 1f - Mathf.SmoothStep(0f, 1f, distance01);
                float speed = maxSpeed01 * falloff;
                float weight = maxWeight01 * falloff;
                Vector2 direction = toNearest.normalized;
                cells[index] = new Vector4(direction.x, direction.y, speed, weight);
            }
        }
    }

    public Vector2[] BuildFlowVectors()
    {
        EnsureCellArray();
        var vectors = new Vector2[CellCount];
        for (int i = 0; i < vectors.Length; i++)
        {
            Vector4 cell = cells[i];
            Vector2 direction = new Vector2(cell.x, cell.y);
            if (direction.sqrMagnitude <= 0.0001f || cell.w <= 0.0001f || cell.z <= 0.0001f)
            {
                vectors[i] = Vector2.zero;
                continue;
            }

            vectors[i] = direction.normalized * Mathf.Clamp01(cell.z) * Mathf.Clamp01(cell.w);
        }

        return vectors;
    }

    public float[] BuildPreviewCosts()
    {
        EnsureCellArray();
        var costs = new float[CellCount];
        for (int i = 0; i < costs.Length; i++)
            costs[i] = 1f;
        return costs;
    }

    private int ToIndex(int x, int z)
    {
        return z * resolutionX + x;
    }

    private static Vector4 BuildPresetCell(Vector2 direction, float speed01, float weight01)
    {
        if (direction.sqrMagnitude <= 0.0001f)
            return Vector4.zero;

        direction.Normalize();
        return new Vector4(direction.x, direction.y, Mathf.Clamp01(speed01), Mathf.Clamp01(weight01));
    }

    private static bool IsValidFlow(Vector4 cell)
    {
        return cell.w > 0.02f && cell.z > 0.02f && new Vector2(cell.x, cell.y).sqrMagnitude > 0.0001f;
    }
}
