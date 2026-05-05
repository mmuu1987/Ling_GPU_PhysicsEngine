using UnityEngine;

public struct MassGpuKernelSet_Stage3
{
    public readonly int ClearGrid;
    public readonly int BuildSpatialHash;
    public readonly int SimulateAndClassify;

    private MassGpuKernelSet_Stage3(int clearGrid, int buildSpatialHash, int simulateAndClassify)
    {
        ClearGrid = clearGrid;
        BuildSpatialHash = buildSpatialHash;
        SimulateAndClassify = simulateAndClassify;
    }

    public static MassGpuKernelSet_Stage3 Find(ComputeShader computeShader)
    {
        return new MassGpuKernelSet_Stage3(
            computeShader.FindKernel("ClearGrid"),
            computeShader.FindKernel("BuildSpatialHash"),
            computeShader.FindKernel("SimulateAndClassify"));
    }
}
