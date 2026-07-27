using UnityEngine;

public sealed class MassGpuRuntimeContext_Stage6
{
    public readonly Plane[] frustumPlanes = new Plane[6];
    public readonly Vector4[] frustumPlaneVectors = new Vector4[6];
    public readonly GPUInstancingManager_Stage6.FlowFieldPreviewSnapshot flowFieldPreview = new GPUInstancingManager_Stage6.FlowFieldPreviewSnapshot();

    public Mesh runtimeAttackerNearMesh;
    public Mesh runtimeAttackerMidMesh;
    public Mesh runtimeAttackerFarMesh;
    public Mesh runtimeDefenderNearMesh;
    public Mesh runtimeDefenderMidMesh;
    public Mesh runtimeDefenderFarMesh;
    public Mesh runtimeGeneratedFarMesh;

    public Material runtimeAttackerNearMaterial;
    public Material runtimeAttackerMidMaterial;
    public Material runtimeAttackerFarMaterial;
    public Material runtimeDefenderNearMaterial;
    public Material runtimeDefenderMidMaterial;
    public Material runtimeDefenderFarMaterial;

    public Bounds renderBounds;
    public MassGpuShaderSet_Stage6 kernels;

    public int agentThreadGroupsX;
    public int gridThreadGroupsX;
    public float runtimeVatFrameCount = 1f;
    public float runtimeVatFrameRate = 30f;
    public int gridResolutionX;
    public int gridResolutionZ;
    public int gridCellCount;
    public Vector2 activeWorldSize;
    public Vector2 gridOrigin;
    public int flowFieldResolutionX = 1;
    public int flowFieldResolutionZ = 1;
    public Vector2 flowFieldOrigin;
    public float activeFlowFieldCellSize = 2f;
    public int defenderFlowFieldResolutionX = 1;
    public int defenderFlowFieldResolutionZ = 1;
    public Vector2 defenderFlowFieldOrigin;
    public float activeDefenderFlowFieldCellSize = 2f;
    public float nextDynamicFlowUpdateTime;
    public float nextDefenderDynamicFlowUpdateTime;
    public bool runtimeDynamicAttackerFlowActive;
    public bool runtimeDynamicDefenderFlowActive;
    public float lastRuntimeDynamicFlowUpdateTime = -1f;
    public float lastRuntimeDynamicDefenderFlowUpdateTime = -1f;
}
