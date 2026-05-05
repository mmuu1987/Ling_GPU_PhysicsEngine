using UnityEngine;

public sealed class MassGpuDispatchScheduler_Stage5
{
    public void DispatchSimulation(
        ComputeShader computeShader,
        MassGpuKernelSet_Stage5 kernels,
        int gridThreadGroupsX,
        int agentThreadGroupsX,
        int flowFieldThreadGroupsX,
        bool rebuildRuntimeAttackerFlowField)
    {
        computeShader.Dispatch(kernels.ClearGrid, gridThreadGroupsX, 1, 1);
        computeShader.Dispatch(kernels.BuildSpatialHash, agentThreadGroupsX, 1, 1);
        if (rebuildRuntimeAttackerFlowField)
        {
            computeShader.Dispatch(kernels.ClearRuntimeDefenderDensity, flowFieldThreadGroupsX, 1, 1);
            computeShader.Dispatch(kernels.BuildRuntimeDefenderDensity, agentThreadGroupsX, 1, 1);
            computeShader.Dispatch(kernels.SelectRuntimeFlowTargets, 1, 1, 1);
            computeShader.Dispatch(kernels.GenerateRuntimeAttackerFlowField, flowFieldThreadGroupsX, 1, 1);
        }

        computeShader.Dispatch(kernels.ClearPendingDamage, agentThreadGroupsX, 1, 1);
        computeShader.Dispatch(kernels.EvaluateStateAndAccumulateDamage, agentThreadGroupsX, 1, 1);
        computeShader.Dispatch(kernels.ResolveDamageSimulateAndClassify, agentThreadGroupsX, 1, 1);
        computeShader.Dispatch(kernels.ClassifyVisibleAgentsByTeam, agentThreadGroupsX, 1, 1);
    }
}
