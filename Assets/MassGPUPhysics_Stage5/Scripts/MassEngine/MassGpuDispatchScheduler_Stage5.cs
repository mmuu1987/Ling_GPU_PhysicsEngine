using UnityEngine;

public sealed class MassGpuDispatchScheduler_Stage5
{
    public void DispatchSimulation(
        ComputeShader computeShader,
        MassGpuKernelSet_Stage5 kernels,
        int gridThreadGroupsX,
        int agentThreadGroupsX,
        int flowFieldThreadGroupsX,
        int defenderFlowFieldThreadGroupsX,
        bool rebuildRuntimeAttackerFlowField,
        bool rebuildRuntimeDefenderFlowField)
    {
        computeShader.Dispatch(kernels.ClearGrid, gridThreadGroupsX, 1, 1);
        computeShader.Dispatch(kernels.BuildSpatialHash, agentThreadGroupsX, 1, 1);
        if (rebuildRuntimeAttackerFlowField)
        {
            computeShader.Dispatch(kernels.ClearRuntimeAttackerFlowResources, flowFieldThreadGroupsX, 1, 1);
            computeShader.Dispatch(kernels.BuildRuntimeAttackerTargetDensity, agentThreadGroupsX, 1, 1);
            computeShader.Dispatch(kernels.SelectRuntimeAttackerFlowTargets, 1, 1, 1);
            computeShader.Dispatch(kernels.GenerateRuntimeAttackerFlowField, flowFieldThreadGroupsX, 1, 1);
        }
        if (rebuildRuntimeDefenderFlowField)
        {
            computeShader.Dispatch(kernels.ClearRuntimeDefenderFlowResources, defenderFlowFieldThreadGroupsX, 1, 1);
            computeShader.Dispatch(kernels.BuildRuntimeDefenderTargetDensity, agentThreadGroupsX, 1, 1);
            computeShader.Dispatch(kernels.SelectRuntimeDefenderFlowTargets, 1, 1, 1);
            computeShader.Dispatch(kernels.GenerateRuntimeDefenderFlowField, defenderFlowFieldThreadGroupsX, 1, 1);
        }

        computeShader.Dispatch(kernels.ClearPendingDamage, agentThreadGroupsX, 1, 1);
        computeShader.Dispatch(kernels.EvaluateStateAndAccumulateDamage, agentThreadGroupsX, 1, 1);
        computeShader.Dispatch(kernels.ResolveDamageSimulateAndClassify, agentThreadGroupsX, 1, 1);
        computeShader.Dispatch(kernels.ClassifyVisibleAgentsByTeam, agentThreadGroupsX, 1, 1);
    }
}
