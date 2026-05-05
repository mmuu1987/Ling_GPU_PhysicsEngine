using UnityEngine;

public struct MassGpuKernelSet_Stage5
{
    public readonly int ClearGrid;
    public readonly int BuildSpatialHash;
    public readonly int ClearRuntimeAttackerFlowResources;
    public readonly int BuildRuntimeAttackerTargetDensity;
    public readonly int SelectRuntimeAttackerFlowTargets;
    public readonly int GenerateRuntimeAttackerFlowField;
    public readonly int ClearRuntimeDefenderFlowResources;
    public readonly int BuildRuntimeDefenderTargetDensity;
    public readonly int SelectRuntimeDefenderFlowTargets;
    public readonly int GenerateRuntimeDefenderFlowField;
    public readonly int ClearPendingDamage;
    public readonly int EvaluateStateAndAccumulateDamage;
    public readonly int ResolveDamageSimulateAndClassify;
    public readonly int ClassifyVisibleAgentsByTeam;

    private MassGpuKernelSet_Stage5(
        int clearGrid,
        int buildSpatialHash,
        int clearRuntimeAttackerFlowResources,
        int buildRuntimeAttackerTargetDensity,
        int selectRuntimeAttackerFlowTargets,
        int generateRuntimeAttackerFlowField,
        int clearRuntimeDefenderFlowResources,
        int buildRuntimeDefenderTargetDensity,
        int selectRuntimeDefenderFlowTargets,
        int generateRuntimeDefenderFlowField,
        int clearPendingDamage,
        int evaluateStateAndAccumulateDamage,
        int resolveDamageSimulateAndClassify,
        int classifyVisibleAgentsByTeam)
    {
        ClearGrid = clearGrid;
        BuildSpatialHash = buildSpatialHash;
        ClearRuntimeAttackerFlowResources = clearRuntimeAttackerFlowResources;
        BuildRuntimeAttackerTargetDensity = buildRuntimeAttackerTargetDensity;
        SelectRuntimeAttackerFlowTargets = selectRuntimeAttackerFlowTargets;
        GenerateRuntimeAttackerFlowField = generateRuntimeAttackerFlowField;
        ClearRuntimeDefenderFlowResources = clearRuntimeDefenderFlowResources;
        BuildRuntimeDefenderTargetDensity = buildRuntimeDefenderTargetDensity;
        SelectRuntimeDefenderFlowTargets = selectRuntimeDefenderFlowTargets;
        GenerateRuntimeDefenderFlowField = generateRuntimeDefenderFlowField;
        ClearPendingDamage = clearPendingDamage;
        EvaluateStateAndAccumulateDamage = evaluateStateAndAccumulateDamage;
        ResolveDamageSimulateAndClassify = resolveDamageSimulateAndClassify;
        ClassifyVisibleAgentsByTeam = classifyVisibleAgentsByTeam;
    }

    public static MassGpuKernelSet_Stage5 Find(ComputeShader computeShader)
    {
        return new MassGpuKernelSet_Stage5(
            computeShader.FindKernel("ClearGrid"),
            computeShader.FindKernel("BuildSpatialHash"),
            computeShader.FindKernel("ClearRuntimeAttackerFlowResources"),
            computeShader.FindKernel("BuildRuntimeAttackerTargetDensity"),
            computeShader.FindKernel("SelectRuntimeAttackerFlowTargets"),
            computeShader.FindKernel("GenerateRuntimeAttackerFlowField"),
            computeShader.FindKernel("ClearRuntimeDefenderFlowResources"),
            computeShader.FindKernel("BuildRuntimeDefenderTargetDensity"),
            computeShader.FindKernel("SelectRuntimeDefenderFlowTargets"),
            computeShader.FindKernel("GenerateRuntimeDefenderFlowField"),
            computeShader.FindKernel("ClearPendingDamage"),
            computeShader.FindKernel("EvaluateStateAndAccumulateDamage"),
            computeShader.FindKernel("ResolveDamageSimulateAndClassify"),
            computeShader.FindKernel("ClassifyVisibleAgentsByTeam"));
    }
}
