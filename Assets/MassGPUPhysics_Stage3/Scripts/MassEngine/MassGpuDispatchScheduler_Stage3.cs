using UnityEngine;

public sealed class MassGpuDispatchScheduler_Stage3
{
    public void DispatchSimulation(
        ComputeShader computeShader,
        MassGpuKernelSet_Stage3 kernels,
        int gridThreadGroupsX,
        int agentThreadGroupsX)
    {
        computeShader.Dispatch(kernels.ClearGrid, gridThreadGroupsX, 1, 1);
        computeShader.Dispatch(kernels.BuildSpatialHash, agentThreadGroupsX, 1, 1);
        computeShader.Dispatch(kernels.SimulateAndClassify, agentThreadGroupsX, 1, 1);
    }
}
