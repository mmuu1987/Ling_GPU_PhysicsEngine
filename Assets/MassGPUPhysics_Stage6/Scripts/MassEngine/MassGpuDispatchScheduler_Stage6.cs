using UnityEngine;

public sealed class MassGpuDispatchScheduler_Stage6
{
    public void DispatchSimulation(
        MassGpuShaderSet_Stage6 kernels,
        int gridThreadGroupsX,
        int agentThreadGroupsX,
        int flowFieldThreadGroupsX,
        int defenderFlowFieldThreadGroupsX,
        bool rebuildRuntimeAttackerFlowField,
        bool rebuildRuntimeDefenderFlowField)
    {
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
        kernels.CombatSimulationShader.Dispatch(kernels.EvaluateStateAndAccumulateDamage, agentThreadGroupsX, 1, 1);
        kernels.CombatSimulationShader.Dispatch(kernels.ResolveDamageSimulateAndClassify, agentThreadGroupsX, 1, 1);
        kernels.LodClassificationShader.Dispatch(kernels.ClassifyVisibleAgentsByTeam, agentThreadGroupsX, 1, 1);
    }
}
