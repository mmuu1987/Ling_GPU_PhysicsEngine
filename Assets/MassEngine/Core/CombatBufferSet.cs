using UnityEngine;

namespace MassEngine
{
    /// <summary>
    /// Compute-only combat buffers, kept separate from AgentData (Requirement 9.3).
    /// pendingDamage and hp are double buffered: kernels read last frame's snapshot and
    /// write this frame's values; MassGpuBufferManager swaps both at end of frame.
    /// </summary>
    public sealed class CombatBufferSet
    {
        public ComputeBuffer teamIdBuffer;
        public ComputeBuffer hpReadBuffer;
        public ComputeBuffer hpWriteBuffer;
        public ComputeBuffer targetAgentIndexBuffer;
        public ComputeBuffer engagementSlotAssignmentBuffer;
        public ComputeBuffer engagementSlotOccupancyBuffer;
        public ComputeBuffer attackCooldownBuffer;
        public ComputeBuffer homePositionBuffer;
        public ComputeBuffer pendingDamageReadBuffer;
        public ComputeBuffer pendingDamageWriteBuffer;
        public ComputeBuffer launchRequestBuffer;
        public ComputeBuffer movementCommandRevisionBuffer;
        // DX11 combat already uses all eight UAV slots. Keep slot assignments in the
        // first N ints and an owner-only 12-word congestion/ranged record per agent in the tail.
        // AgentData/VAT layouts and the attack cooldown buffer remain unchanged.
        public const int CongestionWordsPerAgent = 12;
        private int[] movementCommandRevisions;

        public void InitializeMovementCommands(int teamCount)
        {
            movementCommandRevisions = new int[teamCount];
            movementCommandRevisionBuffer = new ComputeBuffer(teamCount, sizeof(int));
            movementCommandRevisionBuffer.SetData(movementCommandRevisions);
        }

        public void NotifyMovementCommand(int teamId)
        {
            if (movementCommandRevisionBuffer == null || movementCommandRevisions == null ||
                teamId < 0 || teamId >= movementCommandRevisions.Length) return;
            // Exactly representable in the GPU float state; same-point reissues count too.
            movementCommandRevisions[teamId] = movementCommandRevisions[teamId] % 16777215 + 1;
            movementCommandRevisionBuffer.SetData(movementCommandRevisions, teamId, teamId, 1);
        }

        public void SwapPendingDamage()
        {
            ComputeBuffer temp = pendingDamageReadBuffer;
            pendingDamageReadBuffer = pendingDamageWriteBuffer;
            pendingDamageWriteBuffer = temp;
        }

        public void SwapHp()
        {
            ComputeBuffer temp = hpReadBuffer;
            hpReadBuffer = hpWriteBuffer;
            hpWriteBuffer = temp;
        }

        public void ReleaseAll()
        {
            MassGpuBufferManager.ReleaseBuffer(ref teamIdBuffer);
            MassGpuBufferManager.ReleaseBuffer(ref hpReadBuffer);
            MassGpuBufferManager.ReleaseBuffer(ref hpWriteBuffer);
            MassGpuBufferManager.ReleaseBuffer(ref targetAgentIndexBuffer);
            MassGpuBufferManager.ReleaseBuffer(ref engagementSlotAssignmentBuffer);
            MassGpuBufferManager.ReleaseBuffer(ref engagementSlotOccupancyBuffer);
            MassGpuBufferManager.ReleaseBuffer(ref attackCooldownBuffer);
            MassGpuBufferManager.ReleaseBuffer(ref homePositionBuffer);
            MassGpuBufferManager.ReleaseBuffer(ref pendingDamageReadBuffer);
            MassGpuBufferManager.ReleaseBuffer(ref pendingDamageWriteBuffer);
            MassGpuBufferManager.ReleaseBuffer(ref launchRequestBuffer);
            MassGpuBufferManager.ReleaseBuffer(ref movementCommandRevisionBuffer);
            movementCommandRevisions = null;
        }
    }
}
