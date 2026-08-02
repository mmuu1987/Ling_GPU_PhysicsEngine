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
        public float battleSeconds;
        public int attackerFlowRebuilds;
        public int defenderFlowRebuilds;
        public TeamSpatialTelemetry attackers;
        public TeamSpatialTelemetry defenders;
        public double sampleTime;
        public bool valid;
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
            if (teamId == 0)
                snapshot.attackerFlowRebuilds++;
            else
                snapshot.defenderFlowRebuilds++;
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
            spatialHashShader.SetBuffer(clearTeamSpatialStatsKernel, TeamSpatialStatsId, buffers.teamSpatialStatsBuffer);

            spatialHashShader.SetBuffer(buildTeamSpatialStatsKernel, AgentBufferId, buffers.agentBuffer);
            spatialHashShader.SetBuffer(buildTeamSpatialStatsKernel, AgentPositionReadBufferId, buffers.agentPositionReadBuffer);
            spatialHashShader.SetBuffer(buildTeamSpatialStatsKernel, HpReadBufferId, buffers.combatBuffers.hpReadBuffer);
            spatialHashShader.SetBuffer(buildTeamSpatialStatsKernel, TeamIdReadBufferId, buffers.combatBuffers.teamIdBuffer);
            spatialHashShader.SetBuffer(buildTeamSpatialStatsKernel, TeamSpatialStatsId, buffers.teamSpatialStatsBuffer);

            spatialHashShader.Dispatch(clearTeamSpatialStatsKernel, 1, 1, 1);
            spatialHashShader.Dispatch(buildTeamSpatialStatsKernel, Mathf.Max(1, (buffers.AgentCount + 63) / 64), 1, 1);
        }

        private void OnTeamSpatialReadback(AsyncGPUReadbackRequest request, float sampleTime)
        {
            readbackInFlight = false;
            if (request.hasError)
                return;

            int[] values = request.GetData<int>().ToArray();
            bool attackersValid = TryDecodeTeamSpatialStats(values, 0, out TeamSpatialTelemetry attackers);
            bool defendersValid = TryDecodeTeamSpatialStats(values, 1, out TeamSpatialTelemetry defenders);
            snapshot.attackers = attackers;
            snapshot.defenders = defenders;
            snapshot.aliveAttackers = attackersValid ? attackers.aliveCount : 0;
            snapshot.aliveDefenders = defendersValid ? defenders.aliveCount : 0;
            snapshot.sampleTime = sampleTime;
            // A completed sample is valid even when both teams have zero survivors;
            // victory/draw evaluation depends on observing that terminal state.
            snapshot.valid = true;
        }

        public static bool TryDecodeTeamSpatialStats(int[] values, int teamId, out TeamSpatialTelemetry team)
        {
            team = default;
            if (values == null || (teamId != 0 && teamId != 1))
                return false;

            int offset = teamId * 8;
            if (values.Length < offset + 7 || values[offset] <= 0)
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
                snapshot.gridOverflowPerFrame = stats[0];
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
            int aliveAttackers = 0;
            int aliveDefenders = 0;

            for (int i = 0; i < count; i++)
            {
                if (hpValues[i] <= 0)
                    continue;

                if (cachedTeamIds[i] == 0)
                    aliveAttackers++;
                else
                    aliveDefenders++;
            }

            snapshot.aliveAttackers = aliveAttackers;
            snapshot.aliveDefenders = aliveDefenders;
            snapshot.sampleTime = sampleTime;
            snapshot.valid = true;
        }
    }
}
