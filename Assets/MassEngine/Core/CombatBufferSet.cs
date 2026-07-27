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
        public ComputeBuffer attackCooldownBuffer;
        public ComputeBuffer homePositionBuffer;
        public ComputeBuffer pendingDamageReadBuffer;
        public ComputeBuffer pendingDamageWriteBuffer;

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
            MassGpuBufferManager.ReleaseBuffer(ref attackCooldownBuffer);
            MassGpuBufferManager.ReleaseBuffer(ref homePositionBuffer);
            MassGpuBufferManager.ReleaseBuffer(ref pendingDamageReadBuffer);
            MassGpuBufferManager.ReleaseBuffer(ref pendingDamageWriteBuffer);
        }
    }
}
