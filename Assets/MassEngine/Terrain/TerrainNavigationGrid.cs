using System;
using System.Collections.Generic;
using UnityEngine;

namespace MassEngine
{
    /// <summary>
    /// Immutable, conservative XZ navigation over a TerrainSurface snapshot. A nav cell is
    /// usable only when its whole closed AABB, expanded by Clearance, is usable. Touching
    /// blocked terrain/obstacles counts as blocked; boundaryPadding additionally insets the
    /// surface's world bounds. Coarse cells can therefore reject otherwise usable narrow paths.
    /// </summary>
    /// <remarks>
    /// Flow vectors encode ONE next neighbour, not a continuously integrable velocity. Decode
    /// its signed X/Z offset, then steer from the agent's actual position to that neighbour's
    /// CellCenter. All diagonal edges require both side cells, so this segment stays in usable
    /// cells even when the agent is off-center. Do not bilinearly interpolate vectors, overshoot
    /// the neighbour center, or skip cells in a large movement step. Collision/flocking/knockback
    /// still need their own swept movement validation. Zero means stop, never direct-target fallback.
    /// CreateFlowField reuses work arrays and is deliberately not concurrent/reentrant.
    /// </remarks>
    public sealed class TerrainNavigationGrid
    {
        public TerrainSurface Surface { get; }
        public Vector2 Origin { get; }
        public float CellSize { get; }
        public int ResolutionX { get; }
        public int ResolutionZ { get; }
        public int CellCount { get; }
        public float Clearance { get; }

        // First four edges are stored once; the last four are their reverses.
        private static readonly int[] StepX = { 1, 0, 1, -1, -1, 0, -1, 1 };
        private static readonly int[] StepZ = { 0, 1, 1, 1, 0, -1, -1, -1 };
        private readonly uint[] walkable;
        private readonly int[] components;
        private readonly byte[] neighbours;
        private readonly double[] edgeCosts;
        private readonly double[] distances;
        private readonly int[] nextCells;
        private readonly int[] heap;
        private readonly int[] heapPositions;
        private readonly double maximumX;
        private readonly double maximumZ;
        private int heapCount;
        private Vector2[] uniformOutputDirections;

        public TerrainNavigationGrid(TerrainSurface surface, Vector2 origin, float cellSize,
            int resolutionX, int resolutionZ, float clearance, float boundaryPadding,
            StaticObstacleRect[] obstacles = null, float obstaclePadding = 0)
        {
            if (surface == null) throw new ArgumentNullException(nameof(surface));
            if (!Finite(origin) || !TerrainSurface.Finite(cellSize) || cellSize <= 0 ||
                resolutionX <= 0 || resolutionZ <= 0 || (long)resolutionX * resolutionZ > int.MaxValue / 4)
                throw new ArgumentException("Navigation bounds must be finite and dimensions positive.");
            ValidatePadding(clearance, nameof(clearance));
            ValidatePadding(boundaryPadding, nameof(boundaryPadding));
            ValidatePadding(obstaclePadding, nameof(obstaclePadding));
            maximumX = origin.x + (double)cellSize * resolutionX;
            maximumZ = origin.y + (double)cellSize * resolutionZ;
            if (maximumX > float.MaxValue || maximumZ > float.MaxValue ||
                (float)maximumX <= origin.x || (float)maximumZ <= origin.y ||
                (float)(origin.x + (double)cellSize * .5) <= origin.x ||
                (float)(origin.y + (double)cellSize * .5) <= origin.y ||
                (float)(maximumX - cellSize * .5) >= (float)maximumX ||
                (float)(maximumZ - cellSize * .5) >= (float)maximumZ)
                throw new ArgumentException("Navigation cells must have representable centers and bounds.");

            var obstacleSnapshot = obstacles == null ? Array.Empty<StaticObstacleRect>() :
                (StaticObstacleRect[])obstacles.Clone();
            foreach (var obstacle in obstacleSnapshot)
                if (!obstacle.IsValid) throw new ArgumentException("Navigation obstacles must be valid rectangles.", nameof(obstacles));

            Surface = surface;
            Origin = origin;
            CellSize = cellSize;
            ResolutionX = resolutionX;
            ResolutionZ = resolutionZ;
            CellCount = resolutionX * resolutionZ;
            Clearance = clearance;
            walkable = new uint[CellCount];
            components = new int[CellCount];
            neighbours = new byte[CellCount];
            edgeCosts = new double[CellCount * 4];
            distances = new double[CellCount];
            nextCells = new int[CellCount];
            heap = new int[CellCount];
            heapPositions = new int[CellCount];

            for (int z = 0; z < resolutionZ; z++)
                for (int x = 0; x < resolutionX; x++)
                    walkable[z * resolutionX + x] = CoversUsableSurface(x, z, boundaryPadding,
                        obstacleSnapshot, obstaclePadding) ? 1u : 0u;
            BuildEdges();
            BuildComponents();
            PrepareUniformOutputDirections();
        }

        /// <summary>
        /// Shares only the immutable navigation topology. Each returned grid owns its
        /// Dijkstra scratch arrays, so separate grids may solve concurrently.
        /// Does not rebuild terrain coverage, edges, or connectivity; no prewarming.
        /// </summary>
        public TerrainNavigationGrid CreateIndependentSolver() => new TerrainNavigationGrid(this);

        private TerrainNavigationGrid(TerrainNavigationGrid source)
        {
            Surface = source.Surface; Origin = source.Origin; CellSize = source.CellSize;
            ResolutionX = source.ResolutionX; ResolutionZ = source.ResolutionZ;
            CellCount = source.CellCount; Clearance = source.Clearance;
            maximumX = source.maximumX; maximumZ = source.maximumZ;
            walkable = source.walkable; components = source.components;
            neighbours = source.neighbours; edgeCosts = source.edgeCosts;
            uniformOutputDirections = source.uniformOutputDirections;
            distances = new double[CellCount]; nextCells = new int[CellCount];
            heap = new int[CellCount]; heapPositions = new int[CellCount];
        }

        /// <summary>Every step must be an existing legal cardinal graph edge, including terrain/obstacle clearance.</summary>
        public bool HasClearCardinalRoute33(int from,int to)
        {
            if(from<0||to<0||from>=CellCount||to>=CellCount||walkable[from]==0||walkable[to]==0)return false;
            if(from==to)return true;
            int step,bit;
            if(from/ResolutionX==to/ResolutionX){step=to>from?1:-1;bit=step>0?0:4;}
            else if(from%ResolutionX==to%ResolutionX){step=to>from?ResolutionX:-ResolutionX;bit=step>0?1:5;}
            else return false;
            for(int cell=from;cell!=to;cell+=step)if((neighbours[cell]&(1<<bit))==0)return false;
            return true;
        }

        public uint[] CopyWalkable() => (uint[])walkable.Clone();

        // Runtime owns/disposes the native snapshot; the managed grid remains independently usable.
        internal TerrainNavigationBurstWorkspace CreateBurstWorkspace() =>
            new TerrainNavigationBurstWorkspace(this, walkable, neighbours, edgeCosts, uniformOutputDirections);

        /// <summary>Range only, including blocked cells. Positive grid edges are exclusive.</summary>
        public bool TryGetCell(Vector2 position, out int cell)
        {
            cell = -1;
            if (!Finite(position) || position.x < Origin.x || position.y < Origin.y ||
                position.x >= maximumX || position.y >= maximumZ) return false;
            int x = (int)Math.Floor(((double)position.x - Origin.x) / CellSize);
            int z = (int)Math.Floor(((double)position.y - Origin.y) / CellSize);
            cell = z * ResolutionX + x;
            return true;
        }

        public bool IsWalkable(Vector2 position) => TryGetCell(position, out int cell) && walkable[cell] != 0;

        /// <summary>
        /// Checks every nav cell touched by the complete closed footprint (not just sample points).
        /// Size is the full XZ extent, may be zero, and must be finite/nonnegative. Clearance is
        /// already in the nav mask and is NOT applied a second time here.
        /// </summary>
        public bool IsFootprintWalkable(Vector2 center, Vector2 size)
        {
            if (!Finite(center) || !Finite(size) || size.x < 0 || size.y < 0) return false;
            double minX = center.x - (double)size.x * .5, maxX = center.x + (double)size.x * .5;
            double minZ = center.y - (double)size.y * .5, maxZ = center.y + (double)size.y * .5;
            if (minX < Origin.x || minZ < Origin.y || maxX > maximumX || maxZ > maximumZ) return false;
            ClosedCellRange(minX, maxX, Origin.x, CellSize, ResolutionX, out int x0, out int x1);
            ClosedCellRange(minZ, maxZ, Origin.y, CellSize, ResolutionZ, out int z0, out int z1);
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                    if (walkable[z * ResolutionX + x] == 0) return false;
            return true;
        }

        public bool AreConnected(Vector2 a, Vector2 b)
        {
            int component = ComponentAt(a);
            return component >= 0 && component == ComponentAt(b);
        }

        /// <summary>Returns a stable zero-based component id, or -1 for blocked/outside positions.</summary>
        public int ComponentAt(Vector2 position) => TryGetCell(position, out int cell) ? components[cell] : -1;

        public Vector2 CellCenter(int cell)
        {
            if (cell < 0 || cell >= CellCount) throw new ArgumentOutOfRangeException(nameof(cell));
            return new Vector2((float)(Origin.x + (cell % ResolutionX + .5) * CellSize),
                (float)(Origin.y + (cell / ResolutionX + .5) * CellSize));
        }

        /// <summary>
        /// Multi-source, indexed-heap Dijkstra using 3D length along the piecewise triangular
        /// surface. Invalid/outside/blocked targets are ignored, never projected. Target cells,
        /// unreachable cells, and cells at path distance &lt;= stopRadius return zero. The radius
        /// uses path distance to target CELL centers, not Euclidean proximity through a wall.
        /// Each returned array is independent; only that result allocation occurs per call.
        /// </summary>
        public Vector2[] CreateFlowField(IReadOnlyList<Vector2> targets, float stopRadius)
        {
            ValidatePadding(stopRadius, nameof(stopRadius));
            var result = new Vector2[CellCount];
            heapCount = 0;
            for (int cell = 0; cell < CellCount; cell++)
            {
                distances[cell] = double.PositiveInfinity;
                nextCells[cell] = -1;
                heapPositions[cell] = -1;
            }
            if (targets == null) return result;
            for (int i = 0; i < targets.Count; i++)
            {
                if (!TryGetCell(targets[i], out int target) || walkable[target] == 0 || distances[target] == 0) continue;
                distances[target] = 0;
                nextCells[target] = target;
                PushOrDecrease(target);
            }
            while (heapCount > 0)
            {
                int cell = PopMinimum();
                for (int direction = 0; direction < 8; direction++)
                {
                    if ((neighbours[cell] & (1 << direction)) == 0) continue;
                    int adjacent = cell + StepZ[direction] * ResolutionX + StepX[direction];
                    if (heapPositions[adjacent] == -2) continue;
                    double cost = direction < 4 ? edgeCosts[cell * 4 + direction] :
                        edgeCosts[adjacent * 4 + direction - 4];
                    double candidate = distances[cell] + cost;
                    if (candidate >= distances[adjacent]) continue;
                    distances[adjacent] = candidate;
                    nextCells[adjacent] = cell;
                    PushOrDecrease(adjacent);
                }
            }
            for (int cell = 0; cell < CellCount; cell++)
            {
                int next = nextCells[cell];
                if (next < 0 || next == cell || distances[cell] <= stopRadius) continue;
                if (uniformOutputDirections != null)
                {
                    int dx = next % ResolutionX - cell % ResolutionX;
                    int dz = next / ResolutionX - cell / ResolutionX;
                    result[cell] = uniformOutputDirections[(dz + 1) * 3 + dx + 1];
                }
                else
                {
                    Vector2 delta = CellCenter(next) - CellCenter(cell);
                    double length = Math.Sqrt((double)delta.x * delta.x + (double)delta.y * delta.y);
                    result[cell] = new Vector2((float)(delta.x / length), (float)(delta.y / length));
                }
            }
            return result;
        }

        // Cell centers are rounded to float. Only use a nine-entry direction table
        // when EVERY adjacent center spacing is identical on each axis. Large origins
        // and fractional cell sizes can violate that condition; retain exact old math then.
        private void PrepareUniformOutputDirections()
        {
            uniformOutputDirections = null;
            float spacingX = ResolutionX > 1 ? CellCenter(1).x - CellCenter(0).x : CellSize;
            float spacingZ = ResolutionZ > 1 ? CellCenter(ResolutionX).y - CellCenter(0).y : CellSize;
            if (!(spacingX > 0) || !(spacingZ > 0)) return;
            for (int x = 1; x < ResolutionX; x++)
                if (CellCenter(x).x - CellCenter(x - 1).x != spacingX) return;
            for (int z = 1; z < ResolutionZ; z++)
                if (CellCenter(z * ResolutionX).y - CellCenter((z - 1) * ResolutionX).y != spacingZ) return;
            var directions = new Vector2[9];
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dz == 0) continue;
                    // Same float subtraction result and double normalization as the old loop.
                    // Explicit positive zero matches subtraction of identical finite centers.
                    float x = dx == 0 ? 0f : dx > 0 ? spacingX : -spacingX;
                    float z = dz == 0 ? 0f : dz > 0 ? spacingZ : -spacingZ;
                    double length = Math.Sqrt((double)x * x + (double)z * z);
                    directions[(dz + 1) * 3 + dx + 1] = new Vector2((float)(x / length), (float)(z / length));
                }
            uniformOutputDirections = directions;
        }

        private bool CoversUsableSurface(int x, int z, float boundaryPadding,
            StaticObstacleRect[] obstacles, float obstaclePadding)
        {
            double minX = Origin.x + (double)x * CellSize - Clearance;
            double maxX = Origin.x + (double)(x + 1) * CellSize + Clearance;
            double minZ = Origin.y + (double)z * CellSize - Clearance;
            double maxZ = Origin.y + (double)(z + 1) * CellSize + Clearance;
            if (minX < (double)Surface.Origin.x + boundaryPadding || minZ < (double)Surface.Origin.y + boundaryPadding ||
                maxX > (double)Surface.Origin.x + Surface.Size.x - boundaryPadding ||
                maxZ > (double)Surface.Origin.y + Surface.Size.y - boundaryPadding) return false;

            // Enumerate ALL underlying source cells, including a blocked cell touched at an edge.
            ClosedCellRange(minX, maxX, Surface.Origin.x, Surface.Step.x, Surface.Width - 1, out int x0, out int x1);
            ClosedCellRange(minZ, maxZ, Surface.Origin.y, Surface.Step.y, Surface.Depth - 1, out int z0, out int z1);
            for (int sz = z0; sz <= z1; sz++)
                for (int sx = x0; sx <= x1; sx++)
                    if (!Surface.IsCellWalkable(sx, sz)) return false;
            foreach (var obstacle in obstacles)
            {
                double halfX = (double)obstacle.size.x * .5 + obstaclePadding;
                double halfZ = (double)obstacle.size.y * .5 + obstaclePadding;
                if (maxX >= obstacle.center.x - halfX && minX <= obstacle.center.x + halfX &&
                    maxZ >= obstacle.center.y - halfZ && minZ <= obstacle.center.y + halfZ) return false;
            }
            return true;
        }

        private void BuildEdges()
        {
            for (int cell = 0; cell < CellCount; cell++)
            {
                if (walkable[cell] == 0) continue;
                int x = cell % ResolutionX, z = cell / ResolutionX;
                for (int direction = 0; direction < 4; direction++)
                {
                    int nx = x + StepX[direction], nz = z + StepZ[direction];
                    if (nx < 0 || nz < 0 || nx >= ResolutionX || nz >= ResolutionZ) continue;
                    int adjacent = nz * ResolutionX + nx;
                    if (walkable[adjacent] == 0) continue;
                    if (StepX[direction] != 0 && StepZ[direction] != 0 &&
                        (walkable[z * ResolutionX + nx] == 0 || walkable[nz * ResolutionX + x] == 0)) continue;
                    double cost = SurfaceEdgeLength(CellCenter(cell), CellCenter(adjacent));
                    if (double.IsInfinity(cost) || double.IsNaN(cost) || cost <= 0) continue;
                    edgeCosts[cell * 4 + direction] = cost;
                    neighbours[cell] |= (byte)(1 << direction);
                    neighbours[adjacent] |= (byte)(1 << (direction + 4));
                }
            }
        }

        // Split at every terrain grid line, then at that cell's fixed lower-left/upper-right
        // diagonal. Midpoint gradients come from TrySample, not a second height/normal formula.
        // Merely measuring endpoint height difference would miss ridges between nav centers.
        private double SurfaceEdgeLength(Vector2 start, Vector2 end)
        {
            double sx = ((double)start.x - Surface.Origin.x) / Surface.Step.x;
            double sz = ((double)start.y - Surface.Origin.y) / Surface.Step.y;
            double dx = ((double)end.x - start.x) / Surface.Step.x;
            double dz = ((double)end.y - start.y) / Surface.Step.y;
            double deltaX = (double)end.x - start.x, deltaZ = (double)end.y - start.y;
            double horizontalSquared = deltaX * deltaX + deltaZ * deltaZ;
            double nextX = dx == 0 ? double.PositiveInfinity :
                ((dx > 0 ? Math.Floor(sx) + 1 : Math.Ceiling(sx) - 1) - sx) / dx;
            double nextZ = dz == 0 ? double.PositiveInfinity :
                ((dz > 0 ? Math.Floor(sz) + 1 : Math.Ceiling(sz) - 1) - sz) / dz;
            double stepX = dx == 0 ? double.PositiveInfinity : 1 / Math.Abs(dx);
            double stepZ = dz == 0 ? double.PositiveInfinity : 1 / Math.Abs(dz);
            double t = 0, length = 0;
            while (t < 1)
            {
                double finish = Math.Min(1, Math.Min(nextX, nextZ));
                double middle = (t + finish) * .5;
                double diagonal = dx == dz ? double.PositiveInfinity :
                    (Math.Floor(sx + dx * middle) - Math.Floor(sz + dz * middle) - sx + sz) / (dx - dz);
                if (diagonal > t && diagonal < finish)
                {
                    length += FaceLength(start, deltaX, deltaZ, horizontalSquared, t, diagonal);
                    length += FaceLength(start, deltaX, deltaZ, horizontalSquared, diagonal, finish);
                }
                else length += FaceLength(start, deltaX, deltaZ, horizontalSquared, t, finish);
                t = finish;
                if (nextX <= finish) nextX += stepX;
                if (nextZ <= finish) nextZ += stepZ;
            }
            return length;
        }

        private double FaceLength(Vector2 start, double dx, double dz, double horizontalSquared, double from, double to)
        {
            double middle = (from + to) * .5;
            var point = new Vector2((float)(start.x + dx * middle), (float)(start.y + dz * middle));
            if (!Surface.TrySample(point, out var sample)) return double.PositiveInfinity;
            double dy = sample.Gradient.x * dx + sample.Gradient.y * dz;
            return (to - from) * Math.Sqrt(horizontalSquared + dy * dy);
        }

        private void BuildComponents()
        {
            for (int cell = 0; cell < CellCount; cell++) components[cell] = -1;
            int component = 0;
            for (int seed = 0; seed < CellCount; seed++)
            {
                if (walkable[seed] == 0 || components[seed] >= 0) continue;
                int read = 0, count = 1;
                heap[0] = seed; // Constructor-only flood fill reuses the later Dijkstra heap storage.
                components[seed] = component;
                while (read < count)
                {
                    int cell = heap[read++];
                    for (int direction = 0; direction < 8; direction++)
                    {
                        if ((neighbours[cell] & (1 << direction)) == 0) continue;
                        int adjacent = cell + StepZ[direction] * ResolutionX + StepX[direction];
                        if (components[adjacent] >= 0) continue;
                        components[adjacent] = component;
                        heap[count++] = adjacent;
                    }
                }
                component++;
            }
        }

        private void PushOrDecrease(int cell)
        {
            int position = heapPositions[cell];
            if (position < 0) position = heapCount++;
            // Priorities do not change during one sift. Keep the same distance/id order,
            // but load the moving node's priority once instead of at each comparison.
            double cellDistance = distances[cell];
            while (position > 0)
            {
                int parent = (position - 1) / 2;
                int parentCell = heap[parent];
                double parentDistance = distances[parentCell];
                if (!(cellDistance < parentDistance || (cellDistance == parentDistance && cell < parentCell))) break;
                heap[position] = parentCell;
                heapPositions[parentCell] = position;
                position = parent;
            }
            heap[position] = cell;
            heapPositions[cell] = position;
        }

        private int PopMinimum()
        {
            int minimum = heap[0];
            int last = heap[--heapCount];
            heapPositions[minimum] = -2;
            if (heapCount == 0) return minimum;
            double lastDistance = distances[last];
            int position = 0;
            while (position * 2 + 1 < heapCount)
            {
                int child = position * 2 + 1;
                int childCell = heap[child];
                double childDistance = distances[childCell];
                if (child + 1 < heapCount)
                {
                    int rightCell = heap[child + 1];
                    double rightDistance = distances[rightCell];
                    if (rightDistance < childDistance || (rightDistance == childDistance && rightCell < childCell))
                    {
                        child++;
                        childCell = rightCell;
                        childDistance = rightDistance;
                    }
                }
                if (!(childDistance < lastDistance || (childDistance == lastDistance && childCell < last))) break;
                heap[position] = childCell;
                heapPositions[childCell] = position;
                position = child;
            }
            heap[position] = last;
            heapPositions[last] = position;
            return minimum;
        }

        private bool Before(int a, int b) => distances[a] < distances[b] ||
            (distances[a] == distances[b] && a < b);

        private static void ClosedCellRange(double minimum, double maximum, double origin, double step,
            int count, out int first, out int last)
        {
            first = Math.Max(0, Math.Min(count - 1, (int)Math.Ceiling((minimum - origin) / step) - 1));
            last = Math.Max(0, Math.Min(count - 1, (int)Math.Floor((maximum - origin) / step)));
        }

        private static bool Finite(Vector2 value) => TerrainSurface.Finite(value.x) && TerrainSurface.Finite(value.y);

        private static void ValidatePadding(float value, string name)
        {
            if (!TerrainSurface.Finite(value) || value < 0) throw new ArgumentOutOfRangeException(name);
        }
    }
}

