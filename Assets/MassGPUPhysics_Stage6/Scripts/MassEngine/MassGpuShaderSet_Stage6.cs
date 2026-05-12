using UnityEngine;

public struct MassGpuShaderSet_Stage6
{
    public readonly ComputeShader SpatialHashShader;
    public readonly ComputeShader RuntimeFlowShader;
    public readonly ComputeShader CombatSimulationShader;
    public readonly ComputeShader LodClassificationShader;

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
    public readonly int SimulateCombatAndAccumulateDamage;
    public readonly int ClassifyVisibleAgentsByTeam;

    private MassGpuShaderSet_Stage6(
        ComputeShader spatialHashShader,
        ComputeShader runtimeFlowShader,
        ComputeShader combatSimulationShader,
        ComputeShader lodClassificationShader,
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
        int simulateCombatAndAccumulateDamage,
        int classifyVisibleAgentsByTeam)
    {
        SpatialHashShader = spatialHashShader;
        RuntimeFlowShader = runtimeFlowShader;
        CombatSimulationShader = combatSimulationShader;
        LodClassificationShader = lodClassificationShader;

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
        SimulateCombatAndAccumulateDamage = simulateCombatAndAccumulateDamage;
        ClassifyVisibleAgentsByTeam = classifyVisibleAgentsByTeam;
    }

    public bool IsValid =>
        SpatialHashShader != null &&
        RuntimeFlowShader != null &&
        CombatSimulationShader != null &&
        LodClassificationShader != null;

    public static bool HasRequiredShaders(
        ComputeShader spatialHashShader,
        ComputeShader runtimeFlowShader,
        ComputeShader combatSimulationShader,
        ComputeShader lodClassificationShader)
    {
        return spatialHashShader != null &&
               runtimeFlowShader != null &&
               combatSimulationShader != null &&
               lodClassificationShader != null;
    }

    public static MassGpuShaderSet_Stage6 Find(
        ComputeShader spatialHashShader,
        ComputeShader runtimeFlowShader,
        ComputeShader combatSimulationShader,
        ComputeShader lodClassificationShader)
    {
        return new MassGpuShaderSet_Stage6(
            spatialHashShader,
            runtimeFlowShader,
            combatSimulationShader,
            lodClassificationShader,
            spatialHashShader.FindKernel("ClearGrid"),
            spatialHashShader.FindKernel("BuildSpatialHash"),
            runtimeFlowShader.FindKernel("ClearRuntimeAttackerFlowResources"),
            runtimeFlowShader.FindKernel("BuildRuntimeAttackerTargetDensity"),
            runtimeFlowShader.FindKernel("SelectRuntimeAttackerFlowTargets"),
            runtimeFlowShader.FindKernel("GenerateRuntimeAttackerFlowField"),
            runtimeFlowShader.FindKernel("ClearRuntimeDefenderFlowResources"),
            runtimeFlowShader.FindKernel("BuildRuntimeDefenderTargetDensity"),
            runtimeFlowShader.FindKernel("SelectRuntimeDefenderFlowTargets"),
            runtimeFlowShader.FindKernel("GenerateRuntimeDefenderFlowField"),
            combatSimulationShader.FindKernel("ClearPendingDamage"),
            combatSimulationShader.FindKernel("SimulateCombatAndAccumulateDamage"),
            lodClassificationShader.FindKernel("ClassifyVisibleAgentsByTeam"));
    }

}
