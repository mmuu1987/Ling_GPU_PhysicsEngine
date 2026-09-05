using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace MassEngine
{
    public struct TeamSpatialTelemetry
    {
        public int aliveCount;
        public Vector3 centroid;
        public Bounds bounds;
        public int observationZoneCount;
        public bool valid;
    }

    /// <summary>
    /// Latest completed telemetry sample. Counts come from AsyncGPUReadback and lag the
    /// simulation by the readback latency (a few frames).
    /// </summary>
    public struct BattleTelemetrySnapshot
    {
        public int aliveAttackers;
        public int aliveDefenders;
        public int totalAgents;
        public int gridOverflowPerFrame;
        public int peakGridOverflowPerFrame;
        public float battleSeconds;
        public int attackerFlowRebuilds;
        public int defenderFlowRebuilds;
        // teamId 0 and 1 kept as named fields because every existing HUD and test reads
        // them; they mirror the first two entries of the per-team arrays below.
        public TeamSpatialTelemetry attackers;
        public TeamSpatialTelemetry defenders;
        /// <summary>Survivors per teamId. Filled on both sample paths, including the legacy fallback.</summary>
        public int[] aliveByTeam;
        /// <summary>
        /// Flow rebuilds per teamId, grown on demand. attackerFlowRebuilds/defenderFlowRebuilds
        /// mirror entries 0 and 1; a third army's rebuilds only show up here.
        /// </summary>
        public int[] flowRebuildsByTeam;
        /// <summary>
        /// Spatial stats per teamId. Only the team-spatial-stats path fills this; the legacy hp
        /// fallback leaves it null, which is why alive counts live in their own array.
        /// </summary>
        public TeamSpatialTelemetry[] teams;
        public double sampleTime;
        public bool valid;

        public int GetAliveCount(int teamId)
        {
            return aliveByTeam != null && teamId >= 0 && teamId < aliveByTeam.Length ? aliveByTeam[teamId] : 0;
        }

        public TeamSpatialTelemetry GetTeam(int teamId)
        {
            return teams != null && teamId >= 0 && teamId < teams.Length ? teams[teamId] : default;
        }

        /// <summary>How many teamIds this sample covers. Zero before the first readback lands.</summary>
        public int TeamCount
        {
            get { return aliveByTeam != null ? aliveByTeam.Length : 0; }
        }
    }

    /// <summary>
    /// Async (non-blocking) battle observability: per-team alive counts via
    /// AsyncGPUReadback of the hp snapshot + team id buffers, plus CPU-side counters for
    /// flow field rebuilds. This is the instrument that answers "is the system actually
    /// running?" — the class of question this engine previously could not answer at all.
    /// </summary>
    public sealed class BattleTelemetry
    {
        private static readonly int TeamSpatialStatsId = Shader.PropertyToID("teamSpatialStats");
        private static readonly int AgentBufferId = Shader.PropertyToID("agentBuffer");
        private static readonly int AgentPositionReadBufferId = Shader.PropertyToID("agentPositionReadBuffer");
        private static readonly int HpReadBufferId = Shader.PropertyToID("hpReadBuffer");
        private static readonly int TeamIdReadBufferId = Shader.PropertyToID("teamIdReadBuffer");
        private static readonly int ObservationZoneEnabledId = Shader.PropertyToID("telemetryObservationZoneEnabled");
        private static readonly int ObservationZoneId = Shader.PropertyToID("telemetryObservationZone");
        private static readonly int TeamCountId = Shader.PropertyToID("teamCount");

        private readonly float sampleInterval;
        private readonly ComputeShader spatialHashShader;
        private readonly int clearTeamSpatialStatsKernel = -1;
        private readonly int buildTeamSpatialStatsKernel = -1;
        private float nextSampleTime;
        private bool readbackInFlight;
        private bool statsReadbackInFlight;
        private int[] cachedTeamIds;
        // Battle time excludes paused (StopBattle) periods: accumulated + current run.
        private float accumulatedBattleSeconds;
        private float runStartTime = -1f;
        private bool battleRunning;
        private bool observationZoneEnabled;
        private Vector3 observationZoneCenter;
        private float observationZoneRadius = 1f;

        private BattleTelemetrySnapshot snapshot;

        public BattleTelemetrySnapshot Snapshot { get { return snapshot; } }

        /// <summary>
        /// True when a stats readback returned data without the allocation sentinel:
        /// GPU buffer memory was wiped (device reset/TDR). The manager reinitializes
        /// the scenario when it sees this.
        /// </summary>
        public bool DeviceResetSuspected { get; private set; }

        public BattleTelemetry(float sampleInterval = 0.5f)
            : this(null, sampleInterval)
        {
        }

        public BattleTelemetry(ComputeShader spatialHashShader, float sampleInterval = 0.5f)
        {
            this.sampleInterval = Mathf.Max(0.1f, sampleInterval);
            this.spatialHashShader = spatialHashShader;
            if (spatialHashShader != null &&
                spatialHashShader.HasKernel("ClearTeamSpatialStats") &&
                spatialHashShader.HasKernel("BuildTeamSpatialStats"))
            {
                clearTeamSpatialStatsKernel = spatialHashShader.FindKernel("ClearTeamSpatialStats");
                buildTeamSpatialStatsKernel = spatialHashShader.FindKernel("BuildTeamSpatialStats");
            }
        }

        public void NotifyBattleStarted(float time)
        {
            if (battleRunning)
                return;

            battleRunning = true;
            runStartTime = time;
        }

        public void NotifyBattleStopped(float time)
        {
            if (!battleRunning)
                return;

            accumulatedBattleSeconds += Mathf.Max(0f, time - runStartTime);
            battleRunning = false;
        }

        public void NotifyReset()
        {
            accumulatedBattleSeconds = 0f;
            runStartTime = -1f;
            battleRunning = false;
            snapshot = default;
            cachedTeamIds = null;
        }

        public void NotifyFlowRebuild(int teamId)
        {
            if (teamId < 0)
                return;

            if (snapshot.flowRebuildsByTeam == null || snapshot.flowRebuildsByTeam.Length <= teamId)
                System.Array.Resize(ref snapshot.flowRebuildsByTeam, teamId + 1);
            snapshot.flowRebuildsByTeam[teamId]++;

            // Teams 0 and 1 keep their named counters because every existing HUD and test reads
            // those; before per-team flow fields every other team was miscounted as the defender.
            if (teamId == 0)
                snapshot.attackerFlowRebuilds++;
            else if (teamId == 1)
                snapshot.defenderFlowRebuilds++;
        }

        public void ConfigureObservationZone(Vector3 center, float radius, bool enabled)
        {
            observationZoneCenter = center;
            observationZoneRadius = Mathf.Max(0.1f, radius);
            observationZoneEnabled = enabled;
        }

        /// <summary>Kicks a readback when the sample interval elapsed. Never blocks.</summary>
        public void Tick(MassGpuBufferManager buffers, float time)
        {
            if (buffers == null || !buffers.IsAllocated || readbackInFlight || time < nextSampleTime)
                return;

            nextSampleTime = time + sampleInterval;
            snapshot.totalAgents = buffers.AgentCount;
            snapshot.battleSeconds = accumulatedBattleSeconds + (battleRunning ? Mathf.Max(0f, time - runStartTime) : 0f);

            if (CanSampleTeamSpatialStats(buffers))
            {
                DispatchTeamSpatialStats(buffers);
                readbackInFlight = true;
                AsyncGPUReadback.Request(buffers.teamSpatialStatsBuffer, request => OnTeamSpatialReadback(request, time));
            }
            else
            {
                // Compatibility fallback for custom/legacy spatial shaders. It keeps
                // alive counts working, but live camera bounds require the new kernels.
                if (cachedTeamIds == null || cachedTeamIds.Length != buffers.AgentCount)
                {
                    int[] teamIds = new int[buffers.AgentCount];
                    buffers.combatBuffers.teamIdBuffer.GetData(teamIds);
                    cachedTeamIds = teamIds;
                }

                readbackInFlight = true;
                ComputeBuffer hpBuffer = buffers.combatBuffers.hpReadBuffer;
                AsyncGPUReadback.Request(hpBuffer, request => OnHpReadback(request, time));
            }

            if (!statsReadbackInFlight && buffers.spatialHashStatsBuffer != null)
            {
                statsReadbackInFlight = true;
                AsyncGPUReadback.Request(buffers.spatialHashStatsBuffer, OnStatsReadback);
            }
        }

        private bool CanSampleTeamSpatialStats(MassGpuBufferManager buffers)
        {
            return spatialHashShader != null &&
                   clearTeamSpatialStatsKernel >= 0 &&
                   buildTeamSpatialStatsKernel >= 0 &&
                   buffers.teamSpatialStatsBuffer != null;
        }

        private void DispatchTeamSpatialStats(MassGpuBufferManager buffers)
        {
            // Telemetry dispatches these kernels itself, outside ComputePipelineOrchestrator, so
            // it must upload teamCount too - at 0 both kernels would bail out on every thread.
            spatialHashShader.SetInt(TeamCountId, Mathf.Max(1, buffers.TeamCount));
            spatialHashShader.SetBuffer(clearTeamSpatialStatsKernel, TeamSpatialStatsId, buffers.teamSpatialStatsBuffer);

            spatialHashShader.SetBuffer(buildTeamSpatialStatsKernel, AgentBufferId, buffers.agentBuffer);
            spatialHashShader.SetBuffer(buildTeamSpatialStatsKernel, AgentPositionReadBufferId, buffers.agentPositionReadBuffer);
            spatialHashShader.SetBuffer(buildTeamSpatialStatsKernel, HpReadBufferId, buffers.combatBuffers.hpReadBuffer);
            spatialHashShader.SetBuffer(buildTeamSpatialStatsKernel, TeamIdReadBufferId, buffers.combatBuffers.teamIdBuffer);
            spatialHashShader.SetBuffer(buildTeamSpatialStatsKernel, TeamSpatialStatsId, buffers.teamSpatialStatsBuffer);
            spatialHashShader.SetInt(ObservationZoneEnabledId, observationZoneEnabled ? 1 : 0);
            spatialHashShader.SetVector(
                ObservationZoneId,
                new Vector4(observationZoneCenter.x, observationZoneCenter.z, observationZoneRadius, 0f));

            spatialHashShader.Dispatch(clearTeamSpatialStatsKernel, Mathf.Max(1, (buffers.TeamStatsSlotCount + 63) / 64), 1, 1);
            spatialHashShader.Dispatch(buildTeamSpatialStatsKernel, Mathf.Max(1, (buffers.AgentCount + 63) / 64), 1, 1);
        }

        private void OnTeamSpatialReadback(AsyncGPUReadbackRequest request, float sampleTime)
        {
            readbackInFlight = false;
            if (request.hasError)
                return;

            int[] values = request.GetData<int>().ToArray();
            // The buffer is sized teamCount * TeamStatsSlotsPerTeam, so its length is the only
            // team count this callback needs; it stays correct when the layout is widened.
            int teamCount = values.Length / MassGpuBufferManager.TeamStatsSlotsPerTeam;
            EnsureTeamArrays(teamCount);
            for (int teamId = 0; teamId < teamCount; teamId++)
            {
                // A team with no survivors decodes as invalid (count 0), which zeroes its slot
                // rather than leaving the previous sample's stats behind.
                TryDecodeTeamSpatialStats(values, teamId, out snapshot.teams[teamId]);
                snapshot.aliveByTeam[teamId] = snapshot.teams[teamId].valid ? snapshot.teams[teamId].aliveCount : 0;
            }

            snapshot.attackers = snapshot.GetTeam(0);
            snapshot.defenders = snapshot.GetTeam(1);
            snapshot.aliveAttackers = snapshot.GetAliveCount(0);
            snapshot.aliveDefenders = snapshot.GetAliveCount(1);
            snapshot.sampleTime = sampleTime;
            // A completed sample is valid even when both teams have zero survivors;
            // victory/draw evaluation depends on observing that terminal state.
            snapshot.valid = true;
        }

        /// <summary>
        /// Sizes the per-team arrays for this sample. Reallocates only on a team-count change, so
        /// a steady-state readback does not allocate every half second.
        /// </summary>
        private void EnsureTeamArrays(int teamCount)
        {
            int length = Mathf.Max(0, teamCount);
            if (snapshot.aliveByTeam == null || snapshot.aliveByTeam.Length != length)
                snapshot.aliveByTeam = new int[length];
            if (snapshot.teams == null || snapshot.teams.Length != length)
                snapshot.teams = new TeamSpatialTelemetry[length];
        }

        public static bool TryDecodeTeamSpatialStats(int[] values, int teamId, out TeamSpatialTelemetry team)
        {
            team = default;
            if (values == null || teamId < 0)
                return false;

            int offset = teamId * MassGpuBufferManager.TeamStatsSlotsPerTeam;
            if (values.Length < offset + MassGpuBufferManager.TeamStatsSlotsPerTeam || values[offset] <= 0)
                return false;

            int count = values[offset];
            float centerX = (float)values[offset + 1] / count;
            float centerZ = (float)values[offset + 2] / count;
            float minX = values[offset + 3];
            float minZ = values[offset + 4];
            float maxX = values[offset + 5];
            float maxZ = values[offset + 6];
            if (!IsFinite(centerX) || !IsFinite(centerZ) || minX > maxX || minZ > maxZ)
                return false;

            team.aliveCount = count;
            team.centroid = new Vector3(centerX, 0f, centerZ);
            float extentX = Mathf.Max(Mathf.Abs(minX - centerX), Mathf.Abs(maxX - centerX));
            float extentZ = Mathf.Max(Mathf.Abs(minZ - centerZ), Mathf.Abs(maxZ - centerZ));
            team.bounds = new Bounds(
                team.centroid,
                new Vector3(Mathf.Max(1f, extentX * 2f), 30f, Mathf.Max(1f, extentZ * 2f)));
            team.observationZoneCount = values[offset + 7];
            team.valid = true;
            return true;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private void OnStatsReadback(AsyncGPUReadbackRequest request)
        {
            statsReadbackInFlight = false;
            if (request.hasError)
                return;

            NativeArray<int> stats = request.GetData<int>();
            if (stats.Length > 0)
            {
                snapshot.gridOverflowPerFrame = stats[0];
                snapshot.peakGridOverflowPerFrame = Mathf.Max(snapshot.peakGridOverflowPerFrame, stats[0]);
            }
            if (stats.Length > 3 && stats[3] != MassGpuBufferManager.DeviceResetSentinel)
                DeviceResetSuspected = true;
        }

        private void OnHpReadback(AsyncGPUReadbackRequest request, float sampleTime)
        {
            readbackInFlight = false;
            if (request.hasError || cachedTeamIds == null)
                return;

            NativeArray<int> hpValues = request.GetData<int>();
            int count = Mathf.Min(hpValues.Length, cachedTeamIds.Length);

            // Widest teamId present decides the array length: this path has no buffer layout to
            // read it from, and a scenario's teamIds are contiguous from 0 by construction.
            int teamCount = 0;
            for (int i = 0; i < cachedTeamIds.Length; i++)
                teamCount = Mathf.Max(teamCount, cachedTeamIds[i] + 1);

            EnsureTeamArrays(teamCount);
            for (int teamId = 0; teamId < teamCount; teamId++)
                snapshot.aliveByTeam[teamId] = 0;

            for (int i = 0; i < count; i++)
            {
                if (hpValues[i] <= 0)
                    continue;

                int teamId = cachedTeamIds[i];
                if (teamId >= 0 && teamId < teamCount)
                    snapshot.aliveByTeam[teamId]++;
            }

            snapshot.aliveAttackers = snapshot.GetAliveCount(0);
            snapshot.aliveDefenders = snapshot.GetAliveCount(1);
            snapshot.sampleTime = sampleTime;
            snapshot.valid = true;
        }
    }
}
