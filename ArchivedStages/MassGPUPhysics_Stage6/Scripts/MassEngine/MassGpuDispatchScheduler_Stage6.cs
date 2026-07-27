using UnityEngine;

using static MassGpuShaderPropertyIds_Stage6;

public sealed class MassGpuDispatchScheduler_Stage6
{
    public void DispatchSimulation(
        MassGpuShaderSet_Stage6 kernels,
        ComputeBuffer agentPositionReadBuffer,
        ComputeBuffer agentPositionWriteBuffer,
        ComputeBuffer pendingDamageReadBuffer,
        ComputeBuffer pendingDamageWriteBuffer,
        int gridThreadGroupsX,
        int agentThreadGroupsX,
        int flowFieldThreadGroupsX,
        int defenderFlowFieldThreadGroupsX,
        bool rebuildRuntimeAttackerFlowField,
        bool rebuildRuntimeDefenderFlowField)
    {
        BindPerFrameBuffers(
            kernels,
            agentPositionReadBuffer,
            agentPositionWriteBuffer,
            pendingDamageReadBuffer,
            pendingDamageWriteBuffer,
            rebuildRuntimeAttackerFlowField,
            rebuildRuntimeDefenderFlowField);

        kernels.SpatialHashShader.Dispatch(kernels.ClearGrid, gridThreadGroupsX, 1, 1);
        kernels.SpatialHashShader.Dispatch(kernels.BuildSpatialHash, agentThreadGroupsX, 1, 1);
        if (rebuildRuntimeAttackerFlowField)
        {
            kernels.RuntimeFlowShader.Dispatch(kernels.ClearRuntimeAttackerFlowResources, flowFieldThreadGroupsX, 1, 1);
            kernels.RuntimeFlowShader.Dispatch(kernels.BuildRuntimeAttackerTargetDensity, agentThreadGroupsX, 1, 1);
            kernels.RuntimeFlowShader.Dispatch(kernels.SelectRuntimeAttackerFlowTargets, 1, 1, 1);
            kernels.RuntimeFlowShader.Dispatch(kernels.GenerateRuntimeAttackerFlowField, flowFieldThreadGroupsX, 1, 1);
        }
        if (rebuildRuntimeDefenderFlowField)
        {
            kernels.RuntimeFlowShader.Dispatch(kernels.ClearRuntimeDefenderFlowResources, defenderFlowFieldThreadGroupsX, 1, 1);
            kernels.RuntimeFlowShader.Dispatch(kernels.BuildRuntimeDefenderTargetDensity, agentThreadGroupsX, 1, 1);
            kernels.RuntimeFlowShader.Dispatch(kernels.SelectRuntimeDefenderFlowTargets, 1, 1, 1);
            kernels.RuntimeFlowShader.Dispatch(kernels.GenerateRuntimeDefenderFlowField, defenderFlowFieldThreadGroupsX, 1, 1);
        }

        kernels.CombatSimulationShader.Dispatch(kernels.ClearPendingDamage, agentThreadGroupsX, 1, 1);
        kernels.CombatSimulationShader.Dispatch(kernels.SimulateCombatAndAccumulateDamage, agentThreadGroupsX, 1, 1);
        kernels.LodClassificationShader.Dispatch(kernels.ClassifyVisibleAgentsByTeam, agentThreadGroupsX, 1, 1);
    }

    private static void BindPerFrameBuffers(
        MassGpuShaderSet_Stage6 kernels,
        ComputeBuffer agentPositionReadBuffer,
        ComputeBuffer agentPositionWriteBuffer,
        ComputeBuffer pendingDamageReadBuffer,
        ComputeBuffer pendingDamageWriteBuffer,
        bool rebuildRuntimeAttackerFlowField,
        bool rebuildRuntimeDefenderFlowField)
    {
        kernels.SpatialHashShader.SetBuffer(kernels.BuildSpatialHash, AgentPositionReadBufferId, agentPositionReadBuffer);
        if (rebuildRuntimeAttackerFlowField)
            kernels.RuntimeFlowShader.SetBuffer(kernels.BuildRuntimeAttackerTargetDensity, AgentPositionReadBufferId, agentPositionReadBuffer);
        if (rebuildRuntimeDefenderFlowField)
            kernels.RuntimeFlowShader.SetBuffer(kernels.BuildRuntimeDefenderTargetDensity, AgentPositionReadBufferId, agentPositionReadBuffer);
        kernels.CombatSimulationShader.SetBuffer(kernels.ClearPendingDamage, PendingDamageBufferId, pendingDamageWriteBuffer);
        kernels.CombatSimulationShader.SetBuffer(kernels.SimulateCombatAndAccumulateDamage, AgentPositionReadBufferId, agentPositionReadBuffer);
        kernels.CombatSimulationShader.SetBuffer(kernels.SimulateCombatAndAccumulateDamage, AgentPositionBufferId, agentPositionWriteBuffer);
        kernels.CombatSimulationShader.SetBuffer(kernels.SimulateCombatAndAccumulateDamage, PendingDamageReadBufferId, pendingDamageReadBuffer);
        kernels.CombatSimulationShader.SetBuffer(kernels.SimulateCombatAndAccumulateDamage, PendingDamageBufferId, pendingDamageWriteBuffer);
    }
}
