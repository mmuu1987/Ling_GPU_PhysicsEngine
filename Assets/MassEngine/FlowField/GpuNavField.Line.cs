using System;
using System.Collections.Generic;
using UnityEngine;

namespace MassEngine
{
    /// <summary>
    /// N5b (Docs/GPU-NAVFIELD-N5b-20261013.md): a team point order steers to a formation line instead of one cell.
    /// Opt-in (RuntimeFlowConfig.gpuNavFormationLine, needs gpuNavField + gpuNavSteering). The line is built once per
    /// order from the team's spatial telemetry (centroid + bounds, an existing GPU reduction read back every 0.5 s):
    /// perpendicular to centroid -> target, as wide as the army's current frontage, centred half the army's depth
    /// beyond the target so the re-formed block is centred on the click. It grows cell by cell from the target and stops
    /// at the first blocked fine cell, so it is contiguous and straight-line connected to the target.
    /// No telemetry / no direction = the original point goal.
    /// </summary>
    public sealed partial class GpuNavField
    {
        public bool FormationLineEnabled { get; set; }
        public float LineMinWidth { get; set; } = 8f;
        public float LineMaxWidth { get; set; } = 400f;
        public float LineMaxOffset { get; set; } = 60f;
        /// <summary>Ground per agent of a settled block (m^2): re-formed depth = alive * this / frontage. Measured ~0.58..0.64 (N5b).</summary>
        public float LinePackingArea { get; set; } = 0.6f;
        /// <summary>Team spatial telemetry (manager: BattleTelemetry snapshot). Null = point goals.</summary>
        public Func<int, TeamSpatialTelemetry> TeamSpatial { get; set; }
        public int LineGoals { get; private set; }
        public int LineFallbacks { get; private set; }
        /// <summary>Frames a point order waits for valid telemetry before it settles for the point goal.</summary>
        public int LineTelemetryWaitFrames { get; set; } = 60;

        private Vector3[] lineTarget;
        private GpuNavGoal[] lineGoal;
        private bool[] lineSettled;
        private int[] lineFirstFrame;
        private ComputeBuffer goalBits;
        private uint[][] uploadedBits;
        private int GoalBitWords => (Graph.CellCount + 31) / 32;

        /// <summary>Bit t: team t's GPU goal is a formation line (the shader then skips the point-terminal hand-off).</summary>
        public uint LineMask
        {
            get
            {
                uint mask = 0;
                for (int t = 0; t < TeamCount && t < 32; t++) if (goals[t].mode == GpuNavGoal.Line) mask |= 1u << t;
                return mask;
            }
        }

        private void AllocateLineState()
        {
            lineTarget = new Vector3[TeamCount];
            lineGoal = new GpuNavGoal[TeamCount];
            lineSettled = new bool[TeamCount];
            lineFirstFrame = new int[TeamCount];
            for (int t = 0; t < TeamCount; t++) lineFirstFrame[t] = -1;
            uploadedBits = new uint[TeamCount][];
            goalBits = new ComputeBuffer(Mathf.Max(1, GoalBitWords * TeamCount), 4);
            goalBits.SetData(new uint[Mathf.Max(1, GoalBitWords * TeamCount)]);
        }

        private void ReleaseLineState() { goalBits?.Release(); goalBits = null; }

        /// <summary>Uploads a team's line bits once per goal object (Tick, SeedNow and SetGoalForTest all pass here).</summary>
        private void EnsureGoalBits(int team, in GpuNavGoal goal)
        {
            if (goal.mode != GpuNavGoal.Line || goal.bits == null || ReferenceEquals(uploadedBits[team], goal.bits)) return;
            goalBits.SetData(goal.bits, 0, team * GoalBitWords, GoalBitWords);
            uploadedBits[team] = goal.bits;
        }

        /// <summary>The point goal of a team order, or its cached formation line (built once per target).</summary>
        private GpuNavGoal ResolveLine(int team, Vector3 targetPoint, in GpuNavGoal pointGoal)
        {
            if (!FormationLineEnabled || pointGoal.cell < 0) { ResetLine(team); return pointGoal; }
            if (lineFirstFrame[team] < 0 || lineTarget[team] != targetPoint)
            {
                lineTarget[team] = targetPoint; lineSettled[team] = false; lineFirstFrame[team] = Time.frameCount;
            }
            if (lineSettled[team]) return lineGoal[team];
            var spatial = TeamSpatial != null ? TeamSpatial(team) : default;
            if (spatial.valid && spatial.aliveCount > 0)
            {
                var target = new Vector2(targetPoint.x, targetPoint.z);
                var centroid = new Vector2(spatial.centroid.x, spatial.centroid.z);
                var half = new Vector2(spatial.bounds.extents.x, spatial.bounds.extents.z);
                if (BuildLineGoal(pointGoal.cell, target, centroid, half, spatial.aliveCount, out var line)) { lineGoal[team] = line; LineGoals++; }
                else { lineGoal[team] = pointGoal; LineFallbacks++; }
                lineSettled[team] = true;
                return lineGoal[team];
            }
            if (Time.frameCount - lineFirstFrame[team] >= LineTelemetryWaitFrames)
            { lineGoal[team] = pointGoal; lineSettled[team] = true; LineFallbacks++; return pointGoal; }
            return pointGoal; // telemetry not ready yet: point goal meanwhile, retried next frame
        }

        /// <summary>The team is not on a point order: the next point order builds a fresh line.</summary>
        private void ResetLine(int team) { if (team < 0 || team >= TeamCount) return; lineSettled[team] = false; lineFirstFrame[team] = -1; }

        /// <summary>
        /// Builds the formation line of an order to `target` (snapped fine cell targetCell) for an army at `centroid`
        /// with axis-aligned half extents `half` (frontage) and `alive` agents (re-formed depth = alive * LinePackingArea / frontage;
        /// the line sits half that beyond the target so the settled block is centred on it). False = no direction or no walkable start.
        /// </summary>
        public bool BuildLineGoal(int targetCell, Vector2 target, Vector2 centroid, Vector2 half, int alive, out GpuNavGoal goal)
        {
            goal = default;
            if (targetCell < 0 || targetCell >= Graph.CellCount || Graph.Walkable[targetCell] == 0) return false;
            float h = Graph.CellSize, step = h * .5f;
            Vector2 u = target - centroid; float len = u.magnitude;
            if (len < 2f * h || float.IsNaN(len)) return false;
            u /= len;
            var v = new Vector2(-u.y, u.x);
            float width = Mathf.Clamp(2f * (Mathf.Abs(v.x) * half.x + Mathf.Abs(v.y) * half.y), LineMinWidth, Mathf.Max(LineMinWidth, LineMaxWidth));
            float depth = Mathf.Max(0, alive) * Mathf.Max(0f, LinePackingArea) / width;
            float offset = Mathf.Clamp(depth * .5f, 0f, Mathf.Max(0f, LineMaxOffset));
            int r = Graph.Resolution;
            var start = new Vector2(Graph.Origin.x + (targetCell % r + .5f) * h, Graph.Origin.y + (targetCell / r + .5f) * h);
            float forward = 0f;
            for (float k = step; k <= offset + 1e-3f; k += step)
            {
                int c = FineCell(start + u * k);
                if (c < 0 || Graph.Walkable[c] == 0) break;
                forward = k;
            }
            var centre = start + u * forward;
            var set = new SortedSet<int> { FineCell(centre) };
            float tMax = Walk(centre, v, width * .5f, step, set), tMin = -Walk(centre, -v, width * .5f, step, set);
            var cells = new int[set.Count]; set.CopyTo(cells);
            var bits = new uint[GoalBitWords];
            foreach (int c in cells) bits[c >> 5] |= 1u << (c & 31);
            goal = new GpuNavGoal
            {
                mode = GpuNavGoal.Line, cell = targetCell, coarseResolution = CoarseResolution,
                rect = new Vector4(centre.x, centre.y, v.x, v.y), line = new Vector4(tMin, tMax, u.x, u.y), bits = bits, cells = cells, depth = depth,
            };
            return true;
        }

        // ---- N5c: arrival zone (RuntimeFlowConfig.gpuNavArrivalFollow)

        public bool ArrivalFollowEnabled { get; set; }
        /// <summary>Zone = this x the estimated depth of the block that settles at the goal.</summary>
        public float ArrivalZoneScale { get; set; } = 1.5f;
        private readonly Vector4[] arrivalZones = new Vector4[8];

        /// <summary>
        /// Goal distance (m) within which a team's plain marchers follow instead of shoving (0 = off). Line: 1.5 x the
        /// line's estimated block depth; point: 1.5 x the radius of a disc holding the alive agents at LinePackingArea.
        /// </summary>
        public float ArrivalZone(int team)
        {
            if (!ArrivalFollowEnabled || team < 0 || team >= TeamCount) return 0f;
            var goal = goals[team];
            float depth = 0f;
            if (goal.mode == GpuNavGoal.Line) depth = goal.depth;
            else if (goal.mode == GpuNavGoal.Point && TeamSpatial != null)
            {
                var spatial = TeamSpatial(team);
                if (spatial.valid && spatial.aliveCount > 0) depth = Mathf.Sqrt(spatial.aliveCount * Mathf.Max(0f, LinePackingArea) / Mathf.PI);
            }
            return Mathf.Max(0f, depth * ArrivalZoneScale);
        }

        private void BindArrivalZones(ComputeShader shader)
        {
            for (int t = 0; t < 32; t++) arrivalZones[t >> 2][t & 3] = ArrivalZone(t);
            shader.SetVectorArray(SampleArrivalZoneId, arrivalZones);
        }

        private float Walk(Vector2 from, Vector2 dir, float limit, float step, SortedSet<int> set)
        {
            float reached = 0f;
            for (float k = step; k <= limit + 1e-3f; k += step)
            {
                int c = FineCell(from + dir * k);
                if (c < 0 || Graph.Walkable[c] == 0) break;
                set.Add(c); reached = k;
            }
            return reached;
        }
    }
}
