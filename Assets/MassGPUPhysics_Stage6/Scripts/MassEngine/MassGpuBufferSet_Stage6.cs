using UnityEngine;

public sealed class MassGpuBufferSet_Stage6
{
    public ComputeBuffer agentBuffer;
    public ComputeBuffer agentPositionReadBuffer;
    public ComputeBuffer agentPositionWriteBuffer;
    public ComputeBuffer gridCountsBuffer;
    public ComputeBuffer gridAgentIndicesBuffer;
    public ComputeBuffer flowFieldDirectionsBuffer;
    public ComputeBuffer defenderFlowFieldDirectionsBuffer;
    public ComputeBuffer runtimeAttackerTargetDensityBuffer;
    public ComputeBuffer runtimeAttackerFlowStatsBuffer;
    public ComputeBuffer runtimeAttackerFlowTargetsBuffer;
    public ComputeBuffer runtimeDefenderTargetDensityBuffer;
    public ComputeBuffer runtimeDefenderFlowStatsBuffer;
    public ComputeBuffer runtimeDefenderFlowTargetsBuffer;
    public ComputeBuffer teamIdBuffer;
    public ComputeBuffer hpBuffer;
    public ComputeBuffer targetAgentIndexBuffer;
    public ComputeBuffer attackCooldownBuffer;
    public ComputeBuffer homePositionBuffer;
    public ComputeBuffer pendingDamageReadBuffer;
    public ComputeBuffer pendingDamageWriteBuffer;

    public ComputeBuffer nearAttackerAgentIndexBuffer;
    public ComputeBuffer midAttackerAgentIndexBuffer;
    public ComputeBuffer farAttackerAgentIndexBuffer;
    public ComputeBuffer nearDefenderAgentIndexBuffer;
    public ComputeBuffer midDefenderAgentIndexBuffer;
    public ComputeBuffer farDefenderAgentIndexBuffer;

    public ComputeBuffer nearAttackerArgsBuffer;
    public ComputeBuffer midAttackerArgsBuffer;
    public ComputeBuffer farAttackerArgsBuffer;
    public ComputeBuffer nearDefenderArgsBuffer;
    public ComputeBuffer midDefenderArgsBuffer;
    public ComputeBuffer farDefenderArgsBuffer;

    public MaterialPropertyBlock nearAttackerPropertyBlock;
    public MaterialPropertyBlock midAttackerPropertyBlock;
    public MaterialPropertyBlock farAttackerPropertyBlock;
    public MaterialPropertyBlock nearDefenderPropertyBlock;
    public MaterialPropertyBlock midDefenderPropertyBlock;
    public MaterialPropertyBlock farDefenderPropertyBlock;

    public RenderTexture runtimeAttackerFlowPreviewTexture;
    public RenderTexture runtimeDefenderFlowPreviewTexture;

    public void ResetAppendCounters()
    {
        nearAttackerAgentIndexBuffer.SetCounterValue(0);
        midAttackerAgentIndexBuffer.SetCounterValue(0);
        farAttackerAgentIndexBuffer.SetCounterValue(0);
        nearDefenderAgentIndexBuffer.SetCounterValue(0);
        midDefenderAgentIndexBuffer.SetCounterValue(0);
        farDefenderAgentIndexBuffer.SetCounterValue(0);
    }

    public void CopyVisibleCountsToArgs()
    {
        ComputeBuffer.CopyCount(nearAttackerAgentIndexBuffer, nearAttackerArgsBuffer, sizeof(uint));
        ComputeBuffer.CopyCount(midAttackerAgentIndexBuffer, midAttackerArgsBuffer, sizeof(uint));
        ComputeBuffer.CopyCount(farAttackerAgentIndexBuffer, farAttackerArgsBuffer, sizeof(uint));
        ComputeBuffer.CopyCount(nearDefenderAgentIndexBuffer, nearDefenderArgsBuffer, sizeof(uint));
        ComputeBuffer.CopyCount(midDefenderAgentIndexBuffer, midDefenderArgsBuffer, sizeof(uint));
        ComputeBuffer.CopyCount(farDefenderAgentIndexBuffer, farDefenderArgsBuffer, sizeof(uint));
    }

    public void SwapSimulationBuffers()
    {
        ComputeBuffer positionTemp = agentPositionReadBuffer;
        agentPositionReadBuffer = agentPositionWriteBuffer;
        agentPositionWriteBuffer = positionTemp;

        ComputeBuffer damageTemp = pendingDamageReadBuffer;
        pendingDamageReadBuffer = pendingDamageWriteBuffer;
        pendingDamageWriteBuffer = damageTemp;
    }

    public void ReleaseAll()
    {
        ReleaseBuffer(ref agentBuffer);
        ReleaseBuffer(ref agentPositionReadBuffer);
        ReleaseBuffer(ref agentPositionWriteBuffer);
        ReleaseBuffer(ref gridCountsBuffer);
        ReleaseBuffer(ref gridAgentIndicesBuffer);
        ReleaseBuffer(ref flowFieldDirectionsBuffer);
        ReleaseBuffer(ref defenderFlowFieldDirectionsBuffer);
        ReleaseBuffer(ref runtimeAttackerTargetDensityBuffer);
        ReleaseBuffer(ref runtimeAttackerFlowStatsBuffer);
        ReleaseBuffer(ref runtimeAttackerFlowTargetsBuffer);
        ReleaseBuffer(ref runtimeDefenderTargetDensityBuffer);
        ReleaseBuffer(ref runtimeDefenderFlowStatsBuffer);
        ReleaseBuffer(ref runtimeDefenderFlowTargetsBuffer);
        ReleaseBuffer(ref teamIdBuffer);
        ReleaseBuffer(ref hpBuffer);
        ReleaseBuffer(ref targetAgentIndexBuffer);
        ReleaseBuffer(ref attackCooldownBuffer);
        ReleaseBuffer(ref homePositionBuffer);
        ReleaseBuffer(ref pendingDamageReadBuffer);
        ReleaseBuffer(ref pendingDamageWriteBuffer);
        ReleaseBuffer(ref nearAttackerAgentIndexBuffer);
        ReleaseBuffer(ref midAttackerAgentIndexBuffer);
        ReleaseBuffer(ref farAttackerAgentIndexBuffer);
        ReleaseBuffer(ref nearDefenderAgentIndexBuffer);
        ReleaseBuffer(ref midDefenderAgentIndexBuffer);
        ReleaseBuffer(ref farDefenderAgentIndexBuffer);
        ReleaseBuffer(ref nearAttackerArgsBuffer);
        ReleaseBuffer(ref midAttackerArgsBuffer);
        ReleaseBuffer(ref farAttackerArgsBuffer);
        ReleaseBuffer(ref nearDefenderArgsBuffer);
        ReleaseBuffer(ref midDefenderArgsBuffer);
        ReleaseBuffer(ref farDefenderArgsBuffer);
    }

    public static void ReleaseBuffer(ref ComputeBuffer buffer)
    {
        if (buffer == null)
            return;

        buffer.Release();
        buffer = null;
    }
}
