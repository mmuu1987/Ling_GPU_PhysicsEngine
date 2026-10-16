using System;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Rendering;

namespace MassEngine
{
    /// <summary>
    /// GPU nav field, N1 (shadow): one fine distance layer per team, relaxed on the GPU a few Jacobi passes per
    /// frame from the team's current goal (dynamic enemy density, point or area - the same rules as
    /// TerrainNavigationRuntime.Rebuild). Nothing reads these layers yet: agents keep the CPU direction field.
    /// Docs/GPU-NAVFIELD-N0-20261013.md. Admission: RuntimeFlowConfig.gpuNavField (default off) or
    /// MassEngineManager.TryEnableGpuNavField.
    /// N2: when a team's goal changes, an exact seed (GpuNavSeedJob, worker thread) replaces the layer: explicit
    /// goals within ForceSeedFrames frames, dynamic density after an async readback of the density slice.
    /// </summary>
    public sealed partial class GpuNavField : IDisposable
    {
        public GpuNavFieldGraph Graph { get; }
        public TerrainNavigationGrid Coarse { get; }
        public int CoarseResolution { get; }
        public int TeamCount { get; }
        public int PassesPerFrame { get; set; } = 2;
        /// <summary>
        /// N3 steering (RuntimeFlowConfig.gpuNavSteering, default off): agents of a team whose layer is valid steer by
        /// -grad d (AgentDataCommon.hlsl GpuNavSteer, keyword MASS_GPU_NAV); everything else keeps the CPU field.
        /// </summary>
        public bool SteeringEnabled { get; set; }
        /// <summary>
        /// N4 crowd cost (RuntimeFlowConfig.gpuNavCrowd, default off): each team's own agents per 2 m cell, smoothed with
        /// time constant CrowdTimeConstant, make that cell slower: costs x (1 + CrowdGain * min(count / CrowdFullPerCell, CrowdCap)).
        /// </summary>
        public bool CrowdEnabled { get; set; }
        public float CrowdGain { get; set; } = 2f;
        public float CrowdFullPerCell { get; set; } = 6f;
        public float CrowdCap { get; set; } = 1f;
        public float CrowdTimeConstant { get; set; } = 0.5f;
        /// <summary>Agents count by congestion: weight = clamp01(1 - locomotionSpeed / CrowdFreeSpeed); 0 = all count fully.</summary>
        public float CrowdFreeSpeed { get; set; } = 1.5f;
        public bool CrowdActive => CrowdEnabled && CrowdGain > 0f && CrowdFullPerCell > 0f && CrowdCap > 0f;
        /// <summary>Crowd cost prices explicit moves (Point / Area) only; attack (Density) goals keep the N3 field, combat logic owns the approach.</summary>
        public static bool CrowdAppliesTo(int goalMode) => goalMode == GpuNavGoal.Point || goalMode == GpuNavGoal.Area || goalMode == GpuNavGoal.Line;
        public int CrowdUpdates { get; private set; }
        public string CrowdError { get; private set; }
        public const string SamplingKeyword = "MASS_GPU_NAV";
        /// <summary>Bit t: team t's layer holds the seeded distance field of its current goal (teams 0..31).</summary>
        public uint ValidMask
        {
            get
            {
                uint mask = 0;
                for (int t = 0; t < TeamCount && t < 32; t++) if (valid[t] && goals[t].mode != GpuNavGoal.None) mask |= 1u << t;
                return mask;
            }
        }
        /// <summary>N3: eikonal (default; smooth -grad d) or graph (N1/N2 bitwise reference).</summary>
        public GpuNavMetric Metric { get; set; } = GpuNavMetric.Eikonal;
        public bool IsDisposed => disposed;
        public long GpuBytes { get; private set; }
        public double GridBuildMilliseconds { get; set; }
        public double GraphBuildMilliseconds { get; set; }
        public int PassCount { get; private set; }
        public int TickCount { get; private set; }

        /// <summary>N2 seed on goal changes (default on). Off = N1 behaviour (relaxation only).</summary>
        public bool SeedEnabled { get; set; } = true;
        /// <summary>Explicit (point/area) seeds are force-completed this many frames after the goal change.</summary>
        public int ForceSeedFrames { get; set; } = 2;
        public int SeedUploads { get; private set; }
        public int SeedDiscarded { get; private set; }
        public int SeedForced { get; private set; }
        public int SeedBurst { get; private set; }
        public int LastSeedFrames { get; private set; } = -1;
        public int MaxSeedFrames { get; private set; } = -1;
        public int MaxDensitySeedFrames { get; private set; } = -1;
        public double LastSeedMilliseconds { get; private set; }
        public double MaxSeedMilliseconds { get; private set; }
        public int LastSeedUploadFrame { get; private set; } = -1;
        public long CpuBytes => seed != null ? seed.CpuBytes : 0;

        private readonly ComputeShader shader;
        private readonly int relaxKernel, fillKernel, crowdClearKernel, crowdCountKernel, crowdSmoothKernel;
        private ComputeBuffer walk, edges, distA, distB, crowdCount, crowdEma, distSmooth;
        private bool crowdWasActive;
        private readonly int[] current;          // 0: A holds the latest values, 1: B
        private readonly GpuNavGoal[] goals;
        private readonly bool[] valid;
        private bool disposed;
        private static readonly uint[] EmptyDensity = new uint[1];
        private ComputeBuffer dummyDensity;
        private GpuNavSeedWorkspace seed;
        private AsyncGPUReadbackRequest[] readbacks;
        private bool[] readbackPending;
        private int[] readbackFrame;

        static readonly int WalkId = Shader.PropertyToID("_NavWalk"), EdgeId = Shader.PropertyToID("_NavEdge"),
            DensityId = Shader.PropertyToID("_NavTargetDensity"), InId = Shader.PropertyToID("_NavDistIn"), OutId = Shader.PropertyToID("_NavDistOut"),
            ResId = Shader.PropertyToID("_NavRes"), LayerId = Shader.PropertyToID("_NavLayerOffset"), ModeId = Shader.PropertyToID("_NavGoalMode"),
            CellId = Shader.PropertyToID("_NavGoalCell"), CoarseResId = Shader.PropertyToID("_NavCoarseRes"), CoarseOffsetId = Shader.PropertyToID("_NavCoarseOffset"),
            RectId = Shader.PropertyToID("_NavGoalRect"), GridId = Shader.PropertyToID("_NavGrid"), FillId = Shader.PropertyToID("_NavFill"),
            FillCountId = Shader.PropertyToID("_NavFillCount"), MetricId = Shader.PropertyToID("_NavMetric"),
            SampleDistId = Shader.PropertyToID("_GpuNavDist"), SampleMaskId = Shader.PropertyToID("_GpuNavValidMask"),
            CrowdEmaId = Shader.PropertyToID("_NavCrowdEma"), CrowdCountRwId = Shader.PropertyToID("_NavCrowdCountRW"), CrowdEmaRwId = Shader.PropertyToID("_NavCrowdEmaRW"),
            AgentsId = Shader.PropertyToID("_NavAgents"), AgentTeamsId = Shader.PropertyToID("_NavAgentTeams"), AgentCountId = Shader.PropertyToID("_NavAgentCount"),
            TeamCountId = Shader.PropertyToID("_NavTeamCount"), CrowdGainId = Shader.PropertyToID("_NavCrowdGain"), CrowdInvFullId = Shader.PropertyToID("_NavCrowdInvFull"),
            CrowdCapId = Shader.PropertyToID("_NavCrowdCap"), CrowdAlphaId = Shader.PropertyToID("_NavCrowdAlpha"),
            CrowdInvFreeSpeedId = Shader.PropertyToID("_NavCrowdInvFreeSpeed"),
            CrowdOffsetId = Shader.PropertyToID("_NavCrowdOffset"),
            GoalBitsId = Shader.PropertyToID("_NavGoalBits"), GoalBitsOffsetId = Shader.PropertyToID("_NavGoalBitsOffset"),
            SampleLineMaskId = Shader.PropertyToID("_GpuNavLineMask"), SampleArrivalZoneId = Shader.PropertyToID("_GpuNavArrivalZone"),
            SampleLocalDistId = Shader.PropertyToID("_GpuNavLocalDist"), SampleLocalMaskId = Shader.PropertyToID("_GpuNavLocalMask"),
            SampleSmoothDistId = Shader.PropertyToID("_GpuNavDistSmooth"), SampleSmoothMaskId = Shader.PropertyToID("_GpuNavSmoothMask"),
            SmoothRwId = Shader.PropertyToID("_NavDistSmoothRW"), SmoothAlphaId = Shader.PropertyToID("_NavSmoothAlpha"),
            SmoothSnapId = Shader.PropertyToID("_NavSmoothSnap");

        /// <summary>
        /// Far-field smoothing (2026-10-16): time constant (s) of the per-cell EMA of density-goal team layers that
        /// GpuNavSteer samples beyond 60 m of the goal. 0 = off (the live layer everywhere).
        /// </summary>
        public float FarSmoothTime = 0.75f;
        /// <summary>A cell whose live distance differs from the smoothed one by more than this (m) snaps.</summary>
        public float FarSmoothSnap = 40f;
        private int smoothKernel;
        private bool[] smoothPrimed;
        /// <summary>Teams whose smoothed layer is primed and current.</summary>
        public uint SmoothMask
        {
            get
            {
                uint mask = 0u;
                if (smoothPrimed != null)
                    for (int t = 0; t < TeamCount && t < 32; t++) if (smoothPrimed[t] && valid[t]) mask |= 1u << t;
                return mask;
            }
        }

        /// <param name="coarse">The coarse grid this field shadows; Tick runs only while the terrain runtime uses it. Null (tests) = no Tick.</param>
        public GpuNavField(ComputeShader shader, GpuNavFieldGraph graph, int teamCount, TerrainNavigationGrid coarse = null)
        {
            this.shader = shader != null ? shader : throw new ArgumentNullException(nameof(shader));
            Graph = graph ?? throw new ArgumentNullException(nameof(graph));
            Coarse = coarse;
            if (teamCount <= 0) throw new ArgumentOutOfRangeException(nameof(teamCount));
            if ((graph.Resolution & 1) != 0) throw new ArgumentException("Graph resolution must be even (twice the coarse grid).");
            if (coarse != null && (coarse.ResolutionX * 2 != graph.Resolution || coarse.ResolutionZ * 2 != graph.Resolution))
                throw new ArgumentException("Graph must be twice the coarse resolution.");
            CoarseResolution = graph.Resolution / 2;
            relaxKernel = shader.FindKernel("NavFieldRelax");
            fillKernel = shader.FindKernel("NavFieldFill");
            crowdClearKernel = shader.FindKernel("NavCrowdClear");
            crowdCountKernel = shader.FindKernel("NavCrowdCount");
            crowdSmoothKernel = shader.FindKernel("NavCrowdSmooth");
            smoothKernel = shader.FindKernel("NavFieldSmooth");
            TeamCount = teamCount;
            smoothPrimed = new bool[teamCount];
            current = new int[teamCount];
            goals = new GpuNavGoal[teamCount];
            valid = new bool[teamCount];
            int n = graph.CellCount;
            try
            {
                walk = new ComputeBuffer(n, 4); walk.SetData(graph.Walkable);
                edges = new ComputeBuffer(2 * n, 4); edges.SetData(graph.Edges);
                var fill = GpuNavFieldReference.Filled(n * teamCount, GpuNavFieldGraph.Unreachable);
                distA = new ComputeBuffer(n * teamCount, 4); distA.SetData(fill);
                distB = new ComputeBuffer(n * teamCount, 4); distB.SetData(fill);
                distSmooth = new ComputeBuffer(n * teamCount, 4); distSmooth.SetData(fill);
                dummyDensity = new ComputeBuffer(1, 4); dummyDensity.SetData(EmptyDensity);
                crowdCount = new ComputeBuffer(n * teamCount, 4); crowdCount.SetData(new uint[n * teamCount]);
                crowdEma = new ComputeBuffer(n * teamCount, 4); crowdEma.SetData(new float[n * teamCount]);
                seed = new GpuNavSeedWorkspace(graph, teamCount);
                AllocateLineState();
                readbacks = new AsyncGPUReadbackRequest[teamCount]; readbackPending = new bool[teamCount]; readbackFrame = new int[teamCount];
            }
            catch { Dispose(); throw; }
            GpuBytes = (long)n * 4 * 3 + (long)n * teamCount * 20 + (long)GoalBitWords * teamCount * 4;
        }

        /// <summary>Resolves a team's goal with the same rules as TerrainNavigationRuntime.Rebuild.</summary>
        public GpuNavGoal ResolveGoal(PipelineFrameContext context, int team)
        {
            var goal = new GpuNavGoal { mode = GpuNavGoal.None, cell = -1, coarseResolution = CoarseResolution };
            if (context.teamFlows == null || team >= context.teamFlows.Length) return goal;
            var flow = context.teamFlows[team];
            if (!flow.enabled || flow.targetMode != 1) ResetLine(team);
            if (!flow.enabled) return goal;
            if (flow.targetMode == 0)
            {
                if (context.battleStarted && flow.dynamicFlowEnabled) goal.mode = GpuNavGoal.Density;
            }
            else if (flow.targetMode == 1)
            {
                goal.mode = GpuNavGoal.Point;
                goal.cell = SnapWalkable(FineCell(new Vector2(flow.targetPoint.x, flow.targetPoint.z)), new Vector2(flow.targetPoint.x, flow.targetPoint.z));
                return ResolveLine(team, flow.targetPoint, goal); // N5b: formation line when enabled
            }
            else
            {
                goal.mode = GpuNavGoal.Area;
                goal.rect = new Vector4(flow.targetAreaCenter.x, flow.targetAreaCenter.z, flow.targetAreaSize.x * .5f, flow.targetAreaSize.z * .5f);
            }
            return goal;
        }

        /// <summary>
        /// The fine mask is stricter than the coarse one (fine AND coarse), so a target the coarse grid accepts can sit on a
        /// blocked fine cell. Use the nearest walkable fine cell within 2 cells (one coarse cell); -1 = no goal (as the CPU solver).
        /// </summary>
        public int SnapWalkable(int cell, Vector2 p)
        {
            if (cell < 0 || Graph.Walkable[cell] != 0) return cell;
            int r = Graph.Resolution, cx = cell % r, cz = cell / r, best = -1; float bestD = float.MaxValue;
            for (int dz = -2; dz <= 2; dz++)
                for (int dx = -2; dx <= 2; dx++)
                {
                    int x = cx + dx, z = cz + dz;
                    if (x < 0 || z < 0 || x >= r || z >= r || Graph.Walkable[z * r + x] == 0) continue;
                    float wx = Graph.Origin.x + (x + .5f) * Graph.CellSize - p.x, wz = Graph.Origin.y + (z + .5f) * Graph.CellSize - p.y;
                    float d = wx * wx + wz * wz;
                    if (d < bestD) { bestD = d; best = z * r + x; }
                }
            return best;
        }

        public int FineCell(Vector2 p)
        {
            int r = Graph.Resolution;
            double fx = ((double)p.x - Graph.Origin.x) / Graph.CellSize, fz = ((double)p.y - Graph.Origin.y) / Graph.CellSize;
            if (double.IsNaN(fx) || double.IsNaN(fz) || fx < 0 || fz < 0 || fx >= r || fz >= r) return -1;
            return (int)Math.Floor(fz) * r + (int)Math.Floor(fx);
        }

        /// <summary>Per frame, after DispatchRuntimeFlow (the team density slices are current).</summary>
        public void Tick(MassGpuBufferManager buffers, PipelineFrameContext context)
        {
            if (disposed || buffers == null || Coarse == null || context.terrain == null || context.terrain.Navigation != Coarse || context.teamFlows == null) return;
            TickCount++;
            int teams = Mathf.Min(TeamCount, Mathf.Min(context.teamFlows.Length, Mathf.Max(1, buffers.TeamCount)));
            if (CrowdActive)
                UpdateCrowd(buffers.agentBuffer, buffers.combatBuffers.teamIdBuffer, context.totalAgentCount, context.deltaTime);
            else crowdWasActive = false;
            for (int team = 0; team < teams; team++)
            {
                var goal = ResolveGoal(context, team);
                bool changed = !goal.Equals(goals[team]);
                goals[team] = goal;
                if (changed) { valid[team] = false; smoothPrimed[team] = false; BeginSeed(buffers, team, goal); }
                PollSeed(team);
                if (goal.mode == GpuNavGoal.None)
                {
                    if (changed) Fill(team, GpuNavFieldGraph.Unreachable);
                    continue;
                }
                for (int p = 0; p < PassesPerFrame; p++) Relax(buffers, team);
                // Agents sample buffer A: leave every team's latest values there (one extra pass when odd).
                if (current[team] != 0) Relax(buffers, team);
                if (goal.mode == GpuNavGoal.Density && FarSmoothTime > 0f) SmoothLayer(team, context.deltaTime);
                else smoothPrimed[team] = false;
            }
            TickLocal(buffers);
        }

        /// <summary>
        /// N4: counts alive agents per team and fine cell, then blends the counts into the EMA (alpha = 1 - exp(-dt / tau)).
        /// The first update after (re)activation starts the EMA from the current counts.
        /// </summary>
        public void UpdateCrowd(ComputeBuffer agents, ComputeBuffer teamIds, int agentCount, float deltaTime)
        {
            if (disposed) throw new ObjectDisposedException(nameof(GpuNavField));
            if (agents == null || teamIds == null || agentCount <= 0) return;
            if (agents.stride != 64) { CrowdError = "agent stride " + agents.stride + " != 64 (NavAgent mirror)"; return; }
            int n = Graph.CellCount * TeamCount;
            agentCount = Mathf.Min(agentCount, Mathf.Min(agents.count, teamIds.count));
            shader.SetInt(FillCountId, n);
            shader.SetBuffer(crowdClearKernel, CrowdCountRwId, crowdCount);
            shader.Dispatch(crowdClearKernel, (n + 63) / 64, 1, 1);
            shader.SetInt(ResId, Graph.Resolution);
            shader.SetVector(GridId, new Vector4(Graph.Origin.x, Graph.Origin.y, Graph.CellSize, 0));
            shader.SetInt(AgentCountId, agentCount);
            shader.SetInt(TeamCountId, TeamCount);
            shader.SetFloat(CrowdInvFreeSpeedId, CrowdFreeSpeed > 0f ? 1f / CrowdFreeSpeed : 0f);
            shader.SetBuffer(crowdCountKernel, AgentsId, agents);
            shader.SetBuffer(crowdCountKernel, AgentTeamsId, teamIds);
            shader.SetBuffer(crowdCountKernel, CrowdCountRwId, crowdCount);
            shader.Dispatch(crowdCountKernel, (agentCount + 63) / 64, 1, 1);
            float tau = Mathf.Max(1e-3f, CrowdTimeConstant);
            float alpha = crowdWasActive ? 1f - Mathf.Exp(-Mathf.Max(0f, deltaTime) / tau) : 1f;
            shader.SetFloat(CrowdAlphaId, alpha);
            shader.SetBuffer(crowdSmoothKernel, CrowdCountRwId, crowdCount);
            shader.SetBuffer(crowdSmoothKernel, CrowdEmaRwId, crowdEma);
            shader.Dispatch(crowdSmoothKernel, (n + 63) / 64, 1, 1);
            crowdWasActive = true;
            CrowdUpdates++;
            CrowdError = null;
        }

        /// <summary>Tests: overwrite the smoothed crowd (all team layers, team * cells).</summary>
        public void WriteCrowd(float[] ema)
        {
            if (disposed) throw new ObjectDisposedException(nameof(GpuNavField));
            crowdEma.SetData(ema); crowdWasActive = true;
        }

        /// <summary>Diagnostics and tests: synchronous readback of the smoothed crowd (team * cells).</summary>
        public void ReadCrowd(float[] destination)
        {
            if (disposed) throw new ObjectDisposedException(nameof(GpuNavField));
            crowdEma.GetData(destination);
        }

        /// <summary>Combat dispatch: enables MASS_GPU_NAV and binds the main kernel when steering is on. Returns whether it did.</summary>
        public static bool BeginSampling(GpuNavField field, in MassGpuShaderSet shaders, bool terrain)
        {
            var shader = shaders.CombatSimulationShader;
            if (!terrain || field == null || field.disposed || !field.SteeringEnabled || shader == null) return false;
            shader.EnableKeyword(SamplingKeyword);
            field.BindSampling(shader, shaders.SimulateCombatAndAccumulateDamage);
            return true;
        }

        /// <summary>Binds one simulation kernel (main or local-partition) for GpuNavSteer.</summary>
        public void BindSampling(ComputeShader shader, int kernel)
        {
            if (disposed || shader == null || kernel < 0) return;
            shader.SetBuffer(kernel, SampleDistId, distA);
            shader.SetInt(SampleMaskId, unchecked((int)ValidMask));
            shader.SetBuffer(kernel, SampleLocalDistId, localA != null ? localA : dummyDensity);
            shader.SetInt(SampleLocalMaskId, unchecked((int)LocalValidMask));
            shader.SetBuffer(kernel, SampleSmoothDistId, distSmooth);
            shader.SetInt(SampleSmoothMaskId, unchecked((int)SmoothMask));
            shader.SetInt(SampleLineMaskId, unchecked((int)LineMask));
            BindArrivalZones(shader);
        }

        /// <summary>The compute asset is shared with kernel tests: never leave the keyword on.</summary>
        public static void EndSampling(in MassGpuShaderSet shaders, bool active)
        {
            if (active && shaders.CombatSimulationShader != null) shaders.CombatSimulationShader.DisableKeyword(SamplingKeyword);
        }

        /// <summary>Extra passes on a team's current goal (N1 timing / convergence checks).</summary>
        public void RunPasses(MassGpuBufferManager buffers, int team, int count)
        {
            if (disposed || team < 0 || team >= TeamCount || goals[team].mode == GpuNavGoal.None) return;
            for (int p = 0; p < count; p++) Relax(buffers, team);
        }

        public GpuNavGoal LastGoal(int team) => goals[team];

        private void BeginSeed(MassGpuBufferManager buffers, int team, in GpuNavGoal goal)
        {
            var t = seed.teams[team];
            if (t.scheduled) { t.handle.Complete(); t.scheduled = false; SeedDiscarded++; }
            readbackPending[team] = false; // a still-running request is simply ignored
            if (!SeedEnabled || goal.mode == GpuNavGoal.None) return;
            t.goalChangedFrame = Time.frameCount;
            if (goal.mode == GpuNavGoal.Density)
            {
                int coarseCells = CoarseResolution * CoarseResolution;
                var density = buffers != null ? buffers.runtimeFlowTargetDensityBuffer : null;
                if (density == null || density.count < (team + 1) * coarseCells || !SystemInfo.supportsAsyncGPUReadback) return;
                readbacks[team] = AsyncGPUReadback.Request(density, coarseCells * 4, team * coarseCells * 4);
                readbackPending[team] = true; readbackFrame[team] = Time.frameCount;
                return;
            }
            Schedule(team, goal);
        }

        private void Schedule(int team, in GpuNavGoal goal)
        {
            var t = seed.teams[team];
            int count = seed.WriteGoals(Graph, goal, t);
            t.goal = goal;
            t.handle = seed.Job(t, count, Metric).Schedule();
            t.scheduled = true; t.scheduledFrame = Time.frameCount; t.scheduledTime = Time.realtimeSinceStartupAsDouble;
            Unity.Jobs.JobHandle.ScheduleBatchedJobs();
        }

        private void PollSeed(int team)
        {
            var t = seed.teams[team];
            if (readbackPending[team] && readbacks[team].done)
            {
                readbackPending[team] = false;
                if (!readbacks[team].hasError && goals[team].mode == GpuNavGoal.Density)
                {
                    var goal = goals[team];
                    goal.density = readbacks[team].GetData<uint>().ToArray();
                    goal.coarseResolution = CoarseResolution;
                    Schedule(team, goal);
                }
            }
            if (!t.scheduled) return;
            bool dynamic = t.goal.mode == GpuNavGoal.Density;
            bool force = !dynamic && Time.frameCount - t.goalChangedFrame >= ForceSeedFrames;
            if (!t.handle.IsCompleted && !force) return;
            if (!t.handle.IsCompleted) SeedForced++;
            t.handle.Complete(); t.scheduled = false;
            double ms = (Time.realtimeSinceStartupAsDouble - t.scheduledTime) * 1000.0;
            // Still the same goal? (density: still in dynamic mode; the GPU passes track its later movement)
            bool same = dynamic ? goals[team].mode == GpuNavGoal.Density : t.goal.Equals(goals[team]);
            if (!same) { SeedDiscarded++; return; }
            Upload(team, t);
            valid[team] = true;
            int frames = Time.frameCount - t.goalChangedFrame;
            LastSeedFrames = frames; LastSeedMilliseconds = ms; LastSeedUploadFrame = Time.frameCount;
            if (ms > MaxSeedMilliseconds) MaxSeedMilliseconds = ms;
            if (dynamic) { if (frames > MaxDensitySeedFrames) MaxDensitySeedFrames = frames; }
            else if (frames > MaxSeedFrames) MaxSeedFrames = frames;
        }

        private void Upload(int team, GpuNavSeedWorkspace.Team t)
        {
            int n = Graph.CellCount;
            (current[team] == 0 ? distA : distB).SetData(t.distances, 0, team * n, n);
            SeedUploads++;
            if (t.proof[0] != 0) SeedBurst++;
        }

        /// <summary>Synchronous seed of a team's layer (tests / diagnostics). Returns whether Burst ran it.</summary>
        public bool SeedNow(int team, GpuNavGoal goal)
        {
            if (disposed) throw new ObjectDisposedException(nameof(GpuNavField));
            var t = seed.teams[team];
            if (t.scheduled) { t.handle.Complete(); t.scheduled = false; }
            goals[team] = goal;
            int count = seed.WriteGoals(Graph, goal, t);
            seed.Job(t, count, Metric).Run();
            Upload(team, t);
            valid[team] = true;
            return t.proof[0] != 0;
        }

        private void Relax(MassGpuBufferManager buffers, int team)
        {
            var input = current[team] == 0 ? distA : distB;
            var output = current[team] == 0 ? distB : distA;
            int n = Graph.CellCount;
            EnsureGoalBits(team, goals[team]);
            RelaxLayer(buffers, goals[team], input, output, team * n, team * n, team, team * GoalBitWords);
            current[team] ^= 1;
            PassCount++;
        }

        /// <summary>One Jacobi pass of one layer: input -> output at layerOffset; crowd of crowdOffset; density slice of densityTeam.</summary>
        private void RelaxLayer(MassGpuBufferManager buffers, in GpuNavGoal goal, ComputeBuffer input, ComputeBuffer output, int layerOffset, int crowdOffset, int densityTeam, int bitsOffset = 0)
        {
            shader.SetBuffer(relaxKernel, GoalBitsId, goalBits);
            shader.SetInt(GoalBitsOffsetId, bitsOffset);
            int r = Graph.Resolution;
            shader.SetInt(ResId, r);
            shader.SetInt(LayerId, layerOffset);
            shader.SetInt(CrowdOffsetId, crowdOffset);
            shader.SetInt(ModeId, goal.mode);
            shader.SetInt(MetricId, (int)Metric);
            shader.SetInt(CellId, goal.cell);
            int coarseCells = CoarseResolution * CoarseResolution;
            shader.SetInt(CoarseResId, CoarseResolution);
            shader.SetInt(CoarseOffsetId, densityTeam * coarseCells);
            shader.SetVector(RectId, goal.rect);
            shader.SetVector(GridId, new Vector4(Graph.Origin.x, Graph.Origin.y, Graph.CellSize, 0));
            var density = buffers != null ? buffers.runtimeFlowTargetDensityBuffer : null;
            bool densityOk = density != null && density.count >= (densityTeam + 1) * coarseCells;
            if (goal.mode == GpuNavGoal.Density && !densityOk) shader.SetInt(ModeId, GpuNavGoal.None);
            shader.SetBuffer(relaxKernel, DensityId, densityOk ? density : dummyDensity);
            shader.SetBuffer(relaxKernel, WalkId, walk);
            shader.SetBuffer(relaxKernel, CrowdEmaId, crowdEma);
            shader.SetFloat(CrowdGainId, CrowdActive && CrowdAppliesTo(goal.mode) ? CrowdGain : 0f);
            shader.SetFloat(CrowdInvFullId, CrowdFullPerCell > 0f ? 1f / CrowdFullPerCell : 0f);
            shader.SetFloat(CrowdCapId, CrowdCap);
            shader.SetBuffer(relaxKernel, EdgeId, edges);
            shader.SetBuffer(relaxKernel, InId, input);
            shader.SetBuffer(relaxKernel, OutId, output);
            int groups = (r + 7) / 8;
            shader.Dispatch(relaxKernel, groups, groups, 1);
        }

        /// <summary>Far-field smoothing: blends the team's latest layer (A) into its smoothed layer; the first call primes it.</summary>
        private void SmoothLayer(int team, float deltaTime)
        {
            int n = Graph.CellCount;
            float alpha = smoothPrimed[team] ? 1f - Mathf.Exp(-Mathf.Max(0f, deltaTime) / Mathf.Max(1e-3f, FarSmoothTime)) : 1f;
            shader.SetInt(LayerId, team * n);
            shader.SetInt(FillCountId, n);
            shader.SetFloat(SmoothAlphaId, alpha);
            shader.SetFloat(SmoothSnapId, FarSmoothSnap);
            shader.SetBuffer(smoothKernel, InId, distA);
            shader.SetBuffer(smoothKernel, SmoothRwId, distSmooth);
            shader.Dispatch(smoothKernel, (n + 63) / 64, 1, 1);
            smoothPrimed[team] = true;
        }

        private void Fill(int team, float value)
        {
            int n = Graph.CellCount;
            shader.SetInt(LayerId, team * n);
            shader.SetFloat(FillId, value);
            shader.SetInt(FillCountId, n);
            foreach (var target in new[] { distA, distB })
            {
                shader.SetBuffer(fillKernel, OutId, target);
                shader.Dispatch(fillKernel, (n + 63) / 64, 1, 1);
            }
        }

        /// <summary>Synchronous readback of a team's latest layer (diagnostics and tests only).</summary>
        public void ReadDistances(int team, float[] destination)
        {
            if (disposed) throw new ObjectDisposedException(nameof(GpuNavField));
            int n = Graph.CellCount;
            if (destination == null || destination.Length < n) throw new ArgumentException("destination");
            (current[team] == 0 ? distA : distB).GetData(destination, 0, team * n, n);
        }

        /// <summary>Replaces a team's latest layer (tests; the N2 seed upload uses the same slot).</summary>
        public void WriteDistances(int team, float[] source)
        {
            if (disposed) throw new ObjectDisposedException(nameof(GpuNavField));
            int n = Graph.CellCount;
            (current[team] == 0 ? distA : distB).SetData(source, 0, team * n, n);
        }

        /// <summary>Test hook: sets a team's goal without a frame context.</summary>
        public void SetGoalForTest(int team, GpuNavGoal goal) => goals[team] = goal;

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            seed?.Dispose(); seed = null;
            walk?.Release(); walk = null;
            edges?.Release(); edges = null;
            distA?.Release(); distA = null;
            distB?.Release(); distB = null;
            distSmooth?.Release(); distSmooth = null;
            dummyDensity?.Release(); dummyDensity = null;
            crowdCount?.Release(); crowdCount = null;
            crowdEma?.Release(); crowdEma = null;
            DisposeLocal();
            ReleaseLineState();
        }
    }
}
