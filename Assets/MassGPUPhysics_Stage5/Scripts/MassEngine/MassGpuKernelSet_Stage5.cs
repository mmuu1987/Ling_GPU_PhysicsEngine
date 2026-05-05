using UnityEngine;

public struct MassGpuKernelSet_Stage5
{
    public readonly int ClearGrid;
    public readonly int BuildSpatialHash;
    public readonly int ClearRuntimeDefenderDensity;
    public readonly int BuildRuntimeDefenderDensity;
    public readonly int SelectRuntimeFlowTargets;
    public readonly int GenerateRuntimeAttackerFlowField;
    public readonly int ClearPendingDamage;
    public readonly int EvaluateStateAndAccumulateDamage;
    public readonly int ResolveDamageSimulateAndClassify;
    public readonly int ClassifyVisibleAgentsByTeam;

    private MassGpuKernelSet_Stage5(
        int clearGrid,
        int buildSpatialHash,
        int clearRuntimeDefenderDensity,
        int buildRuntimeDefenderDensity,
        int selectRuntimeFlowTargets,
        int generateRuntimeAttackerFlowField,
        int clearPendingDamage,
        int evaluateStateAndAccumulateDamage,
        int resolveDamageSimulateAndClassify,
        int classifyVisibleAgentsByTeam)
    {
        ClearGrid = clearGrid;
        BuildSpatialHash = buildSpatialHash;
        ClearRuntimeDefenderDensity = clearRuntimeDefenderDensity;
        BuildRuntimeDefenderDensity = buildRuntimeDefenderDensity;
        SelectRuntimeFlowTargets = selectRuntimeFlowTargets;
        GenerateRuntimeAttackerFlowField = generateRuntimeAttackerFlowField;
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
            computeShader.FindKernel("ClearRuntimeDefenderDensity"),
            computeShader.FindKernel("BuildRuntimeDefenderDensity"),
            computeShader.FindKernel("SelectRuntimeFlowTargets"),
            computeShader.FindKernel("GenerateRuntimeAttackerFlowField"),
            computeShader.FindKernel("ClearPendingDamage"),
            computeShader.FindKernel("EvaluateStateAndAccumulateDamage"),
            computeShader.FindKernel("ResolveDamageSimulateAndClassify"),
            computeShader.FindKernel("ClassifyVisibleAgentsByTeam"));
    }
}
