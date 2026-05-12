using UnityEngine;

namespace MassGPUPhysics.Stage7
{
    public sealed class CombatBufferSet
    {
        public ComputeBuffer teamIdBuffer;
        public ComputeBuffer hpBuffer;
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

        public void ReleaseAll()
        {
            MassGpuBufferManager_Stage7.ReleaseBuffer(ref teamIdBuffer);
            MassGpuBufferManager_Stage7.ReleaseBuffer(ref hpBuffer);
            MassGpuBufferManager_Stage7.ReleaseBuffer(ref targetAgentIndexBuffer);
            MassGpuBufferManager_Stage7.ReleaseBuffer(ref attackCooldownBuffer);
            MassGpuBufferManager_Stage7.ReleaseBuffer(ref homePositionBuffer);
            MassGpuBufferManager_Stage7.ReleaseBuffer(ref pendingDamageReadBuffer);
            MassGpuBufferManager_Stage7.ReleaseBuffer(ref pendingDamageWriteBuffer);
        }
    }
}
