using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Stage 2 海量 GPU 实例化管理器。
///
/// 当前版本按 5 个方向做优化：
/// 1. 三档 LOD：near 使用完整 VAT + 光照 + 阴影，mid 使用轻量 VAT，far 使用 billboard。
/// 2. GPU 视锥剔除：Compute Shader 只把摄像机看得到的实例 Append 到绘制列表。
/// 3. 分级材质：不同距离使用不同 mesh/material/draw call。
/// 4. 动画降频：中远距离隔几帧才更新一次动画时间。
/// 5. 阴影收缩：只有 near 档投射阴影，避免 10 万个 VAT 角色都进 ShadowCaster pass。
/// </summary>
public class GPUInstancingManager_Stage2 : MonoBehaviour
{
    [StructLayout(LayoutKind.Sequential)]
    [System.Serializable]
    public struct AgentData
    {
        public Vector3 position;
        public Vector3 rotation;
        public Vector3 scale;
        public int currentState;
        public float currentAnimationTime;
    }

    [Header("Instancing")]
    [Min(1)] public int instanceCount = 100000;
    public Mesh instanceMesh;
    public Material instanceMaterial;
    public ComputeShader computeShader;

    [Header("LOD Meshes")]
    [Tooltip("Mid LOD mesh. Empty means reuse the full instance mesh.")]
    public Mesh midInstanceMesh;
    [Tooltip("Far LOD mesh. Empty means a runtime 4-vertex billboard quad is used.")]
    public Mesh farInstanceMesh;

    [Header("LOD Materials")]
    [Tooltip("Mid LOD material. Empty means reuse farInstanceMaterial, then instanceMaterial.")]
    public Material midInstanceMaterial;
    [Tooltip("Far LOD material. Recommended: BillboardInstancedAgent.")]
    public Material farInstanceMaterial;

    [Header("Spawn")]
    public Vector3 spawnArea = new Vector3(100f, 0f, 100f);

    [Header("LOD Distances")]
    [Tooltip("Near LOD radius. Only this band casts dynamic VAT shadows.")]
    [Min(0f)] public float shadowCastingRadius = 18f;
    [Tooltip("Mid LOD radius. Outside this distance instances use far billboard LOD.")]
    [Min(0f)] public float midLodRadius = 75f;
    [Tooltip("LOD center. Empty means camera position if available, otherwise world origin.")]
    public Transform lodCenter;

    [Header("Frustum Culling")]
    public bool enableFrustumCulling = true;
    public Camera cullingCamera;
    [Tooltip("Extra radius used when testing one instance against camera frustum planes.")]
    [Min(0f)] public float cullingRadius = 2f;

    [Header("Animation")]
    [Min(1f)] public float vatFrameCount = 30f;
    [Min(1f)] public float vatFrameRate = 30f;
    [Min(1)] public int nearAnimationInterval = 1;
    [Min(1)] public int midAnimationInterval = 2;
    [Min(1)] public int farAnimationInterval = 4;

    private static readonly int DeltaTimeId = Shader.PropertyToID("deltaTime");
    private static readonly int AnimationDurationId = Shader.PropertyToID("animationDuration");
    private static readonly int FrameIndexId = Shader.PropertyToID("frameIndex");
    private static readonly int LodCenterId = Shader.PropertyToID("lodCenter");
    private static readonly int NearLodRadiusSqrId = Shader.PropertyToID("nearLodRadiusSqr");
    private static readonly int MidLodRadiusSqrId = Shader.PropertyToID("midLodRadiusSqr");
    private static readonly int EnableFrustumCullingId = Shader.PropertyToID("enableFrustumCulling");
    private static readonly int CullingRadiusId = Shader.PropertyToID("cullingRadius");
    private static readonly int FrustumPlanesId = Shader.PropertyToID("frustumPlanes");
    private static readonly int NearAnimationIntervalId = Shader.PropertyToID("nearAnimationInterval");
    private static readonly int MidAnimationIntervalId = Shader.PropertyToID("midAnimationInterval");
    private static readonly int FarAnimationIntervalId = Shader.PropertyToID("farAnimationInterval");
    private static readonly int AgentBufferId = Shader.PropertyToID("agentBuffer");
    private static readonly int NearAgentIndicesId = Shader.PropertyToID("nearAgentIndices");
    private static readonly int MidAgentIndicesId = Shader.PropertyToID("midAgentIndices");
    private static readonly int FarAgentIndicesId = Shader.PropertyToID("farAgentIndices");
    private static readonly int VisibleAgentIndicesId = Shader.PropertyToID("visibleAgentIndices");
    private static readonly int VATFrameCountId = Shader.PropertyToID("_VATFrameCount");
    private static readonly int VATFrameRateId = Shader.PropertyToID("_VATFrameRate");

    private readonly uint[] args = new uint[5];
    private readonly Plane[] frustumPlanes = new Plane[6];
    private readonly Vector4[] frustumPlaneVectors = new Vector4[6];

    private ComputeBuffer agentBuffer;
    private ComputeBuffer nearAgentIndexBuffer;
    private ComputeBuffer midAgentIndexBuffer;
    private ComputeBuffer farAgentIndexBuffer;
    private ComputeBuffer nearArgsBuffer;
    private ComputeBuffer midArgsBuffer;
    private ComputeBuffer farArgsBuffer;

    private MaterialPropertyBlock nearPropertyBlock;
    private MaterialPropertyBlock midPropertyBlock;
    private MaterialPropertyBlock farPropertyBlock;

    private Mesh runtimeMidMesh;
    private Mesh runtimeFarMesh;
    private Material runtimeNearMaterial;
    private Material runtimeMidMaterial;
    private Material runtimeFarMaterial;
    private Bounds renderBounds;
    private int csKernel;
    private int threadGroupsX;

    private float AnimationDuration => vatFrameCount / Mathf.Max(vatFrameRate, 0.0001f);

    private void Start()
    {
        InitializeBuffers();
    }

    private void InitializeBuffers()
    {
        if (instanceMesh == null || instanceMaterial == null || computeShader == null)
        {
            Debug.LogError("[GPUInstancingManager_Stage2] Missing Mesh, Material, or ComputeShader reference.");
            enabled = false;
            return;
        }

        instanceCount = Mathf.Max(1, instanceCount);
        midLodRadius = Mathf.Max(midLodRadius, shadowCastingRadius + 0.01f);

        runtimeNearMaterial = instanceMaterial;
        runtimeMidMaterial = midInstanceMaterial != null ? midInstanceMaterial :
            (farInstanceMaterial != null ? farInstanceMaterial : instanceMaterial);
        runtimeFarMaterial = farInstanceMaterial != null ? farInstanceMaterial : runtimeMidMaterial;

        runtimeMidMesh = midInstanceMesh != null ? midInstanceMesh : instanceMesh;
        runtimeFarMesh = farInstanceMesh != null ? farInstanceMesh : CreateBillboardQuadMesh();

        runtimeNearMaterial.enableInstancing = true;
        runtimeMidMaterial.enableInstancing = true;
        runtimeFarMaterial.enableInstancing = true;

        int stride = Marshal.SizeOf<AgentData>();
        agentBuffer = new ComputeBuffer(instanceCount, stride);
        nearAgentIndexBuffer = CreateAppendIndexBuffer();
        midAgentIndexBuffer = CreateAppendIndexBuffer();
        farAgentIndexBuffer = CreateAppendIndexBuffer();

        UploadInitialAgents();

        nearArgsBuffer = CreateArgsBuffer(instanceMesh);
        midArgsBuffer = CreateArgsBuffer(runtimeMidMesh);
        farArgsBuffer = CreateArgsBuffer(runtimeFarMesh);

        csKernel = computeShader.FindKernel("CSMain");
        computeShader.SetBuffer(csKernel, AgentBufferId, agentBuffer);
        computeShader.SetBuffer(csKernel, NearAgentIndicesId, nearAgentIndexBuffer);
        computeShader.SetBuffer(csKernel, MidAgentIndicesId, midAgentIndexBuffer);
        computeShader.SetBuffer(csKernel, FarAgentIndicesId, farAgentIndexBuffer);

        nearPropertyBlock = CreatePropertyBlock(nearAgentIndexBuffer);
        midPropertyBlock = CreatePropertyBlock(midAgentIndexBuffer);
        farPropertyBlock = CreatePropertyBlock(farAgentIndexBuffer);

        SyncVatMaterial(runtimeNearMaterial);
        SyncVatMaterial(runtimeMidMaterial);
        SyncVatMaterial(runtimeFarMaterial);

        threadGroupsX = Mathf.CeilToInt(instanceCount / 64f);
        renderBounds = new Bounds(Vector3.zero, new Vector3(
            spawnArea.x * 2f + 20f,
            Mathf.Max(120f, spawnArea.y * 2f + 20f),
            spawnArea.z * 2f + 20f));

        Debug.Log($"[GPUInstancingManager_Stage2] Initialized {instanceCount} instances with near/mid/far LOD.");
    }

    private ComputeBuffer CreateAppendIndexBuffer()
    {
        return new ComputeBuffer(instanceCount, sizeof(uint), ComputeBufferType.Append);
    }

    private MaterialPropertyBlock CreatePropertyBlock(ComputeBuffer visibleIndexBuffer)
    {
        var block = new MaterialPropertyBlock();
        block.SetBuffer(AgentBufferId, agentBuffer);
        block.SetBuffer(VisibleAgentIndicesId, visibleIndexBuffer);
        return block;
    }

    private void UploadInitialAgents()
    {
        var initialData = new AgentData[instanceCount];
        float animDuration = AnimationDuration;

        for (int i = 0; i < instanceCount; i++)
        {
            initialData[i] = new AgentData
            {
                position = new Vector3(
                    Random.Range(-spawnArea.x, spawnArea.x),
                    Random.Range(-spawnArea.y, spawnArea.y),
                    Random.Range(-spawnArea.z, spawnArea.z)),
                rotation = new Vector3(0f, Random.Range(0f, 360f), 0f),
                scale = Vector3.one,
                currentState = 0,
                currentAnimationTime = Random.Range(0f, animDuration)
            };
        }

        agentBuffer.SetData(initialData);
    }

    private void SyncVatMaterial(Material material)
    {
        if (material == null)
            return;

        material.SetFloat(VATFrameCountId, vatFrameCount);
        material.SetFloat(VATFrameRateId, vatFrameRate);
    }

    private ComputeBuffer CreateArgsBuffer(Mesh mesh)
    {
        var buffer = new ComputeBuffer(1, args.Length * sizeof(uint), ComputeBufferType.IndirectArguments);
        args[0] = (uint)mesh.GetIndexCount(0);
        args[1] = 0;
        args[2] = (uint)mesh.GetIndexStart(0);
        args[3] = (uint)mesh.GetBaseVertex(0);
        args[4] = 0;
        buffer.SetData(args);
        return buffer;
    }

    private static Mesh CreateBillboardQuadMesh()
    {
        var mesh = new Mesh
        {
            name = "Runtime Far LOD Billboard Quad"
        };

        mesh.SetVertices(new[]
        {
            new Vector3(-0.5f, 0f, 0f),
            new Vector3(0.5f, 0f, 0f),
            new Vector3(-0.5f, 1f, 0f),
            new Vector3(0.5f, 1f, 0f)
        });
        mesh.SetUVs(0, new[]
        {
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(0f, 1f),
            new Vector2(1f, 1f)
        });
        mesh.SetIndices(new[] { 0, 2, 1, 2, 3, 1 }, MeshTopology.Triangles, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    private void Update()
    {
        if (agentBuffer == null)
            return;

        ResetAppendCounters();
        UploadFrameParameters();
        computeShader.Dispatch(csKernel, threadGroupsX, 1, 1);
        CopyVisibleCountsToArgs();
        DrawLods();
    }

    private void ResetAppendCounters()
    {
        nearAgentIndexBuffer.SetCounterValue(0);
        midAgentIndexBuffer.SetCounterValue(0);
        farAgentIndexBuffer.SetCounterValue(0);
    }

    private void UploadFrameParameters()
    {
        Vector3 center = GetLodCenter();
        float nearRadiusSqr = shadowCastingRadius * shadowCastingRadius;
        float midRadiusSqr = midLodRadius * midLodRadius;

        computeShader.SetFloat(DeltaTimeId, Time.deltaTime);
        computeShader.SetFloat(AnimationDurationId, AnimationDuration);
        computeShader.SetInt(FrameIndexId, Time.frameCount);
        computeShader.SetVector(LodCenterId, center);
        computeShader.SetFloat(NearLodRadiusSqrId, nearRadiusSqr);
        computeShader.SetFloat(MidLodRadiusSqrId, midRadiusSqr);
        computeShader.SetInt(EnableFrustumCullingId, enableFrustumCulling ? 1 : 0);
        computeShader.SetFloat(CullingRadiusId, cullingRadius);
        computeShader.SetInt(NearAnimationIntervalId, nearAnimationInterval);
        computeShader.SetInt(MidAnimationIntervalId, midAnimationInterval);
        computeShader.SetInt(FarAnimationIntervalId, farAnimationInterval);

        UpdateFrustumPlanes();
        computeShader.SetVectorArray(FrustumPlanesId, frustumPlaneVectors);
    }

    private Vector3 GetLodCenter()
    {
        if (lodCenter != null)
            return lodCenter.position;

        Camera activeCamera = GetActiveCullingCamera();
        return activeCamera != null ? activeCamera.transform.position : Vector3.zero;
    }

    private Camera GetActiveCullingCamera()
    {
        return cullingCamera != null ? cullingCamera : Camera.main;
    }

    private void UpdateFrustumPlanes()
    {
        Camera activeCamera = GetActiveCullingCamera();
        if (!enableFrustumCulling || activeCamera == null)
        {
            for (int i = 0; i < frustumPlaneVectors.Length; i++)
                frustumPlaneVectors[i] = Vector4.zero;
            return;
        }

        GeometryUtility.CalculateFrustumPlanes(activeCamera, frustumPlanes);
        for (int i = 0; i < frustumPlanes.Length; i++)
        {
            Plane plane = frustumPlanes[i];
            Vector3 normal = plane.normal;
            frustumPlaneVectors[i] = new Vector4(normal.x, normal.y, normal.z, plane.distance);
        }
    }

    private void CopyVisibleCountsToArgs()
    {
        ComputeBuffer.CopyCount(nearAgentIndexBuffer, nearArgsBuffer, sizeof(uint));
        ComputeBuffer.CopyCount(midAgentIndexBuffer, midArgsBuffer, sizeof(uint));
        ComputeBuffer.CopyCount(farAgentIndexBuffer, farArgsBuffer, sizeof(uint));
    }

    private void DrawLods()
    {
        Graphics.DrawMeshInstancedIndirect(
            instanceMesh, 0, runtimeNearMaterial, renderBounds, nearArgsBuffer, 0,
            nearPropertyBlock, ShadowCastingMode.On, true, gameObject.layer);

        Graphics.DrawMeshInstancedIndirect(
            runtimeMidMesh, 0, runtimeMidMaterial, renderBounds, midArgsBuffer, 0,
            midPropertyBlock, ShadowCastingMode.Off, true, gameObject.layer);

        Graphics.DrawMeshInstancedIndirect(
            runtimeFarMesh, 0, runtimeFarMaterial, renderBounds, farArgsBuffer, 0,
            farPropertyBlock, ShadowCastingMode.Off, true, gameObject.layer);
    }

    private void OnDisable()
    {
        ReleaseBuffers();
    }

    private void ReleaseBuffers()
    {
        ReleaseBuffer(ref agentBuffer);
        ReleaseBuffer(ref nearAgentIndexBuffer);
        ReleaseBuffer(ref midAgentIndexBuffer);
        ReleaseBuffer(ref farAgentIndexBuffer);
        ReleaseBuffer(ref nearArgsBuffer);
        ReleaseBuffer(ref midArgsBuffer);
        ReleaseBuffer(ref farArgsBuffer);

        if (farInstanceMesh == null && runtimeFarMesh != null)
        {
            Destroy(runtimeFarMesh);
            runtimeFarMesh = null;
        }
    }

    private static void ReleaseBuffer(ref ComputeBuffer buffer)
    {
        if (buffer == null)
            return;

        buffer.Release();
        buffer = null;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        instanceCount = Mathf.Max(1, instanceCount);
        shadowCastingRadius = Mathf.Max(0f, shadowCastingRadius);
        midLodRadius = Mathf.Max(midLodRadius, shadowCastingRadius + 0.01f);
        cullingRadius = Mathf.Max(0f, cullingRadius);
        vatFrameCount = Mathf.Max(1f, vatFrameCount);
        vatFrameRate = Mathf.Max(1f, vatFrameRate);
        nearAnimationInterval = Mathf.Max(1, nearAnimationInterval);
        midAnimationInterval = Mathf.Max(1, midAnimationInterval);
        farAnimationInterval = Mathf.Max(1, farAnimationInterval);
    }
#endif
}
