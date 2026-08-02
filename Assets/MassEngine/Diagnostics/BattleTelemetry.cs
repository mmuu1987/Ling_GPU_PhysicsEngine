using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace MassEngine
{
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
        private readonly float sampleInterval;
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
        {
            this.sampleInterval = Mathf.Max(0.1f, sampleInterval);
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

            // Team ids are immutable after upload; cache them once per allocation.
            if (cachedTeamIds == null || cachedTeamIds.Length != buffers.AgentCount)
            {
                int[] teamIds = new int[buffers.AgentCount];
                buffers.combatBuffers.teamIdBuffer.GetData(teamIds);
                cachedTeamIds = teamIds;
            }

            readbackInFlight = true;
            ComputeBuffer hpBuffer = buffers.combatBuffers.hpReadBuffer;
            AsyncGPUReadback.Request(hpBuffer, request => OnHpReadback(request, time));

            if (!statsReadbackInFlight && buffers.spatialHashStatsBuffer != null)
            {
                statsReadbackInFlight = true;
                AsyncGPUReadback.Request(buffers.spatialHashStatsBuffer, OnStatsReadback);
            }
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
