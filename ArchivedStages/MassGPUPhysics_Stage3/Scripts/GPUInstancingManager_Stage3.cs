using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 第三阶段：海量 GPU Agent 管理器 —— 用 Compute Shader 在 GPU 上同时模拟和渲染十万级单位。
///
/// 【核心概念：为什么要把物理放到 GPU 上算？】
/// 传统 Unity 物理（PhysX）每个物体都在 CPU 上逐对象处理。当物体数量达到数万时，
/// CPU 的单线程/少线程瓶颈会非常明显。GPU 拥有数千个计算核心，用 Compute Shader
/// 可以把"每个 Agent 的碰撞/速度/动画"完全并行化，把 10 万个 Agent 的物理从
/// "根本跑不动"变成"轻松 60fps"。
///
/// 【本脚本的角色：C# 侧的"调度者"和"资源管理者"】
/// 这个脚本不包含任何逐 Agent 的物理/动画逻辑 —— 那些全部在
/// AgentComputeShader_Stage3.compute 里由 GPU 执行。C# 这边只负责：
/// - 分配和管理 GPU 显存（ComputeBuffer）
/// - 每帧上传少量全局参数（deltaTime、摄像机位置等）
/// - 按顺序 Dispatch Compute Shader 的三个 kernel
/// - 把 Compute Shader 算出的可见 Agent 数量转成绘制参数
/// - 用 DrawMeshInstancedIndirect 发出三档 LOD 绘制指令
///
/// 【每帧完整流水线，五步走：】
///
/// 第1步：ResetAppendCounters()
///   把 near/mid/far 三个 AppendBuffer 的内部计数器清零。
///   AppendBuffer 像是一个"GPU 端的可变长列表"，每帧要往里面追加新数据前必须重置。
///
/// 第2步：UploadFrameParameters()
///   把 deltaTime、摄像机位置、LOD 距离阈值、碰撞参数等传给 Compute Shader。
///   这些是对所有 Agent 都一样的全局数据（C# 叫它们 "uniforms"）。
///
/// 第3步：三个 Compute Shader Dispatch
///   Dispatch(ClearGrid)   → 清空空间哈希格子的计数
///   Dispatch(BuildHash)   → 每个 Agent 算出自己在哪个格子，原子写入索引表
///   Dispatch(Simulate)    → 查邻域格子做碰撞排斥 + 动画推进 + 视锥剔除 + LOD 分类
///   注意这三个 Dispatch 必须按顺序执行（Unity 的 Dispatch 在 GPU 上排队，不保证
///   并发顺序，但同一个 CommandBuffer 里的 Dispatch 会按提交顺序执行）。
///
/// 第4步：CopyVisibleCountsToArgs()
///   AppendBuffer 内部有一个 GPU 维护的计数器。Compute Shader 的 Append 操作会
///   原子递增这个计数器。CopyCount 把这个计数器值拷贝到间接绘制参数 buffer 的
///   instanceCount 字段。
///
/// 第5步：DrawLods()
///   用 Graphics.DrawMeshInstancedIndirect 绘制三档 LOD：
///   - Near（近处）：完整网格 + VAT 顶点动画 + 光照 + 投射阴影
///   - Mid（中距离）：简化网格 + VAT 动画 + 简化光照
///   - Far（远处）：Billboard 面片，始终面向相机
/// </summary>
public class GPUInstancingManager_Stage3 : MonoBehaviour
{
    /// <summary>
    /// AgentData：每个 GPU Agent 在显存中的数据格式（C#/Compute Shader/渲染 Shader 三方共享）。
    ///
    /// 【为什么需要精确对齐？】
    /// CPU（C#）用 Marshal.SizeOf 计算结构体字节数来分配 ComputeBuffer。
    /// GPU Compute Shader（HLSL）和渲染 Shader（HLSL）用同样的 struct 定义来读写。
    /// 如果三边的字节布局不一致（比如字段顺序不同、对齐填充不同），读到数据就是乱码。
    /// LayoutKind.Sequential 保证 C# 端字段按声明顺序紧密排列，和 HLSL struct 布局一一对应。
    ///
    /// 【数据在系统中的流转路径】
    /// CPU Start() ──SetData──▶ agentBuffer（GPU 显存）
    ///   └─ Compute Shader 读写（物理模拟、碰撞）
    ///   └─ 渲染 Shader 只读（setup() 构建 TRS 矩阵）
    ///        └─ DrawMeshInstancedIndirect 最终绘制到屏幕
    ///
    /// 【同步维护清单 —— 改一处必须改五处】
    ///   1. AgentComputeShader_Stage3.compute           （GPU 物理模拟读写）
    ///   2. InstancedAgentShader_Stage3.shader           （近处 VAT 渲染读取）
    ///   3. LitInstancedAgentShader_Stage3.shader        （带光照 VAT 渲染读取）
    ///   4. BillboardInstancedAgentShader_Stage3.shader  （远处 Billboard 渲染读取）
    ///   5. GPUInstancingManager_Stage3.cs（本文件）     （C# 端上传初始数据）
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [System.Serializable]
    public struct AgentData
    {
        /// <summary>世界空间坐标。碰撞检测只用 XZ 平面（2D 拓扑），Y 轴仅用于渲染高度。</summary>
        public Vector3 position;

        /// <summary>
        /// 欧拉角旋转（单位：度）。当前只用 Y 分量控制朝向（绕 Y 轴旋转让角色面对运动方向）。
        /// Compute Shader 根据 velocity.xz 方向自动计算 Y 角：atan2(vx, vz)。
        /// </summary>
        public Vector3 rotation;

        /// <summary>每个 Agent 的独立缩放。当前默认 (1,1,1)，保留以备后续不同体型。</summary>
        public Vector3 scale;

        /// <summary>
        /// 速度向量（第三阶段核心新增）。Compute Shader 碰撞排斥累积到 velocity.xz，
        /// 然后做阻尼衰减 + 限速 + 位置积分。velocity.y 强制归零（不做垂直运动）。
        /// </summary>
        public Vector3 velocity;

        /// <summary>动画状态机 ID（预留字段）。第三阶段暂不分支处理，后续战斗系统使用。</summary>
        public int currentState;

        /// <summary>
        /// VAT（顶点动画贴图）当前播放时间，单位秒。
        /// Compute Shader 每帧推进此值（可降频跳帧），渲染 Shader setup() 读取它换算 VAT 帧号。
        /// </summary>
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
    [Tooltip("When enabled, agents spawn closer to the center so the collision pass is immediately visible.")]
    public bool spawnClusterForCollisionDemo = true;
    [Min(0.01f)] public float clusteredSpawnRadius = 60f;

    [Header("Spatial Hash Collision")]
    [Tooltip("空间哈希单元格尺寸。建议接近 agentRadius * 2，这样两个相邻 Agent 必定在同一格或相邻格。越大格子越少但每个格子里 Agent 越多，越小则反之。")]
    [Min(0.1f)] public float cellSize = 2f;
    [Tooltip("每个格子最多保存多少个 Agent 索引。过载部分会被丢弃不参与碰撞，以保证显存上限固定。超载意味着该区域 Agent 过于密集，少量丢弃不影响视觉。")]
    [Min(1)] public int maxAgentsPerCell = 64;
    [Tooltip("Agent 在 XZ 平面上的圆形碰撞半径。两个 Agent 中心距 < agentRadius*2 时产生排斥。")]
    [Min(0.01f)] public float agentRadius = 0.45f;
    [Tooltip("重叠时排斥速度的强度。越大散开越快，但太大可能过度抖动。建议 10~30。")]
    [Min(0f)] public float separationStrength = 18f;
    [Tooltip("速度阻尼系数，每帧速度乘以 (1 - damping*dt)，让排斥后的群体逐渐稳定下来。")]
    [Range(0f, 20f)] public float velocityDamping = 5f;
    [Tooltip("Agent 在碰撞模拟中的最大水平移动速度。防止排斥力过大导致 Agent 瞬间飞出屏幕。")]
    [Min(0.01f)] public float maxSpeed = 6f;
    [Tooltip("XZ 模拟区域总尺寸。设为 (0,0) 则自动根据 spawnArea 推导。x 对应世界 X，y 对应世界 Z。")]
    public Vector2 simulationWorldSize = Vector2.zero;
    [Tooltip("模拟边界内缩距离。Agent 触碰到边界会被弹回，避免位置积分到网格外导致查找失败。")]
    [Min(0f)] public float boundaryPadding = 2f;

    [Header("LOD Distances")]
    [Tooltip("近处 LOD 半径：在此范围内的 Agent 使用完整网格 + VAT 动画 + 光照 + 投射阴影。")]
    [Min(0f)] public float shadowCastingRadius = 18f;
    [Tooltip("中距离 LOD 半径：在 shadowCastingRadius~midLodRadius 之间用简化网格。超出则用 Billboard。")]
    [Min(0f)] public float midLodRadius = 75f;
    [Tooltip("LOD 计算的参考中心点。为空时自动使用摄像机位置（如果有）否则世界原点。")]
    public Transform lodCenter;

    [Header("Frustum Culling")]
    public bool enableFrustumCulling = true;
    public Camera cullingCamera;
    [Tooltip("视锥剔除时围绕 Agent 位置的额外球形半径。Agent 在这个球体内任意一点都不在视锥外时才剔除。值越大越保守（越不会错误剔除）。")]
    [Min(0f)] public float cullingRadius = 2f;

    [Header("Animation")]
    [Min(1f)] public float vatFrameCount = 30f;
    [Min(1f)] public float vatFrameRate = 30f;
    [Tooltip("近处 Agent 每多少帧推进一次动画时间。1 表示每帧都推进，动画最流畅。")]
    [Min(1)] public int nearAnimationInterval = 1;
    [Tooltip("中距离 Agent 的动画降频间隔。例如 2 表示每 2 帧推进一次（播放速度减半后补回）。")]
    [Min(1)] public int midAnimationInterval = 2;
    [Tooltip("远距离 Agent 的动画降频间隔。例如 4 表示每 4 帧推进一次，远处用 Billboard 不需要精细动画。")]
    [Min(1)] public int farAnimationInterval = 4;

    [Header("Stage 4 Flow Field Navigation")]
    public bool enableFlowFieldNavigation = true;
    [Tooltip("Painted flow field grid cell size used when fitting/creating an asset.")]
    [Min(0.25f)] public float flowFieldCellSize = 2f;
    [Tooltip("How quickly velocity turns toward the sampled flow direction.")]
    [Min(0f)] public float flowFieldResponsiveness = 6f;
    [Tooltip("0 disables flow steering; 1 uses the full desired velocity from the flow field.")]
    [Range(0f, 1f)] public float flowFieldWeight = 1f;
    [Tooltip("Hand-painted or preset-generated velocity field asset used by Stage4 navigation.")]
    public PaintedFlowFieldAsset_Stage4 paintedFlowFieldAsset;
    [Tooltip("Show the cached painted flow field preview in the Inspector.")]
    public bool showFlowFieldPreview = true;
    [Tooltip("Draw one preview arrow for every N cells.")]
    [Min(1)] public int flowFieldPreviewStride = 2;

    [System.Serializable]
    public sealed class FlowFieldPreviewSnapshot
    {
        public bool isValid;
        public bool isEnabled;
        public int resolutionX = 1;
        public int resolutionZ = 1;
        public Vector2 origin;
        public Vector2 worldSize;
        public float cellSize = 2f;
        public Vector2 target;
        public int blockedCellCount;
        public Vector2[] directions = new[] { Vector2.zero };
        public float[] costs = new[] { 0f };
        public string status = "Preview has not been built.";
    }

    [System.NonSerialized] private FlowFieldPreviewSnapshot flowFieldPreview = new FlowFieldPreviewSnapshot();
    public FlowFieldPreviewSnapshot FlowFieldPreview
    {
        get
        {
            if (flowFieldPreview == null)
                flowFieldPreview = new FlowFieldPreviewSnapshot();

            return flowFieldPreview;
        }
    }

    // ─────────────────────────────────────────────────────────────
    // Shader.PropertyToID 预先将字符串转为整数 ID，避免每帧字符串哈希查找。
    // 这些字符串必须和 Compute Shader / 渲染 Shader 中声明的变量名完全一致。
    // 例如 Compute Shader 里写了 float deltaTime;，这里就要用 "deltaTime"。
    // ─────────────────────────────────────────────────────────────
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
    private static readonly int GridCountsId = Shader.PropertyToID("gridCounts");
    private static readonly int GridAgentIndicesId = Shader.PropertyToID("gridAgentIndices");
    private static readonly int GridCellCountId = Shader.PropertyToID("gridCellCount");
    private static readonly int GridResolutionId = Shader.PropertyToID("gridResolution");
    private static readonly int GridOriginId = Shader.PropertyToID("gridOrigin");
    private static readonly int GridWorldSizeId = Shader.PropertyToID("gridWorldSize");
    private static readonly int CellSizeId = Shader.PropertyToID("cellSize");
    private static readonly int MaxAgentsPerCellId = Shader.PropertyToID("maxAgentsPerCell");
    private static readonly int AgentRadiusId = Shader.PropertyToID("agentRadius");
    private static readonly int SeparationStrengthId = Shader.PropertyToID("separationStrength");
    private static readonly int VelocityDampingId = Shader.PropertyToID("velocityDamping");
    private static readonly int MaxSpeedId = Shader.PropertyToID("maxSpeed");
    private static readonly int BoundaryPaddingId = Shader.PropertyToID("boundaryPadding");
    private static readonly int FlowFieldDirectionsId = Shader.PropertyToID("flowFieldDirections");
    private static readonly int FlowFieldEnabledId = Shader.PropertyToID("flowFieldEnabled");
    private static readonly int FlowFieldResolutionId = Shader.PropertyToID("flowFieldResolution");
    private static readonly int FlowFieldOriginId = Shader.PropertyToID("flowFieldOrigin");
    private static readonly int FlowFieldCellSizeId = Shader.PropertyToID("flowFieldCellSize");
    private static readonly int FlowFieldWeightId = Shader.PropertyToID("flowFieldWeight");
    private static readonly int FlowFieldResponsivenessId = Shader.PropertyToID("flowFieldResponsiveness");
    private static readonly int VATFrameCountId = Shader.PropertyToID("_VATFrameCount");
    private static readonly int VATFrameRateId = Shader.PropertyToID("_VATFrameRate");

    // CPU 侧临时数组：把 Unity 的 Plane 结构体转成 Vector4 后上传给 Compute Shader 做视锥剔除。
    // Unity Plane 格式为 (normal.x, normal.y, normal.z, distance)。
    // Compute Shader 中判断可见性：dot(plane.xyz, position) + plane.w < -cullingRadius → 剔除。
    private readonly Plane[] frustumPlanes = new Plane[6];
    private readonly Vector4[] frustumPlaneVectors = new Vector4[6];

    // ─────────────────────────────────────────────────────────────
    // ComputeBuffer 是 C# 访问 GPU 显存的句柄，可以理解为"GPU 端的数组"。
    // agentBuffer：完整 Agent 数据（位置、旋转、速度、动画时间等）。
    //   Compute Shader 每帧读写它（做碰撞模拟），渲染 Shader 只读它（构建变换矩阵）。
    // ─────────────────────────────────────────────────────────────
    private ComputeBuffer agentBuffer;

    // 空间哈希网格数据。
    // gridCountsBuffer[cell] = 该格子里当前写入了多少个 Agent
    // gridAgentIndicesBuffer[cell * maxAgentsPerCell + slot] = 具体的 Agent 索引编号
    // 例如：第 42 号格子的第 3 个槽位存的是 Agent #7891。
    private ComputeBuffer gridCountsBuffer;
    private ComputeBuffer gridAgentIndicesBuffer;
    private ComputeBuffer flowFieldDirectionsBuffer;

    // ─────────────────────────────────────────────────────────────
    // AppendBuffer（追加缓冲区）：GPU 端的可变长列表。
    // Compute Shader 用 .Append(id) 往里面追加数据，内部用原子计数器维护当前长度。
    // 只保存可见 Agent 的 uint 索引（4 字节），不复制完整的 60+ 字节 AgentData。
    // 渲染时 unity_InstanceID 通过 visibleAgentIndices 表映射回真实 Agent 下标。
    // ─────────────────────────────────────────────────────────────
    private ComputeBuffer nearAgentIndexBuffer;
    private ComputeBuffer midAgentIndexBuffer;
    private ComputeBuffer farAgentIndexBuffer;

    // 三个 LOD 绘制所需的间接参数 buffer（每个 5 uint）。
    // 每帧 CopyCount 会把 AppendBuffer 的计数器值写入 argsBuffer[1]（instanceCount）。
    private ComputeBuffer nearArgsBuffer;
    private ComputeBuffer midArgsBuffer;
    private ComputeBuffer farArgsBuffer;

    // MaterialPropertyBlock：给每个 draw call 绑定不同的 GPU buffer。
    // 同一个材质只能有一套全局参数，但通过 PropertyBlock 可以给不同 draw call
    // 绑定不同的 visibleAgentIndices，使 near/mid/far 各自渲染各自的可见列表。
    private MaterialPropertyBlock nearPropertyBlock;
    private MaterialPropertyBlock midPropertyBlock;
    private MaterialPropertyBlock farPropertyBlock;

    // 运行时确定的最终 LOD 资源。如果 Inspector 中对应项为空则使用 fallback 策略。
    // - runtimeMidMesh：中距离用简化网格，为空则复用完整网格
    // - runtimeFarMesh：远距离用 Billboard 四边形，为空则运行时创建
    // - runtimeNearMaterial：永远是 instanceMaterial（Inspector 中拖入的材质）
    // - runtimeMidMaterial：中距离材质，为空则优先复用 farMaterial，再兜底 instanceMaterial
    // - runtimeFarMaterial：远距离 Billboard 材质，推荐 BillboardInstancedAgent
    private Mesh runtimeMidMesh;
    private Mesh runtimeFarMesh;
    private Material runtimeNearMaterial;
    private Material runtimeMidMaterial;
    private Material runtimeFarMaterial;

    // ─────────────────────────────────────────────────────────────
    // renderBounds：DrawMeshInstancedIndirect 需要一个大包围盒。
    // Unity 用它做 draw call 级别的视锥剔除——如果整个包围盒都不在视野内，
    // 整批 draw call 都会被跳过（不管里面有多少 Agent）。所以这里要设得足够大。
    // ─────────────────────────────────────────────────────────────
    private Bounds renderBounds;

    // Compute Shader kernel 集合与调度器。
    // 后续 FlowField / Logic / Combat pass 会从这里接入，而不是继续堆在 Update 里。
    private MassGpuKernelSet_Stage3 kernels;
    private readonly MassGpuDispatchScheduler_Stage3 dispatchScheduler = new MassGpuDispatchScheduler_Stage3();

    // ─────────────────────────────────────────────────────────────
    // Dispatch 的线程组数量。
    // Compute Shader 中声明了 [numthreads(64, 1, 1)]，即每个线程组有 64 个线程。
    // 所以 Dispatch(n, 1, 1) 总共启动 n*64 个线程。
    // agentThreadGroupsX = ceil(instanceCount / 64)：保证每个 Agent 至少有一个线程
    // gridThreadGroupsX = ceil(gridCellCount / 64)：保证每个格子至少有一个线程
    // ─────────────────────────────────────────────────────────────
    private int agentThreadGroupsX;
    private int gridThreadGroupsX;

    // 空间哈希网格的运行时参数（由 RecalculateGridSettings 计算）。
    private int gridResolutionX;
    private int gridResolutionZ;
    private int gridCellCount;
    private Vector2 activeWorldSize;
    private Vector2 gridOrigin;
    private int flowFieldResolutionX = 1;
    private int flowFieldResolutionZ = 1;
    private Vector2 flowFieldOrigin;
    private float activeFlowFieldCellSize = 2f;

    /// <summary>
    /// 动画总时长（秒）= 总帧数 / 帧率。
    /// 例如 vatFrameCount=30, vatFrameRate=30 → 动画总长 1 秒。
    /// </summary>
    private float AnimationDuration => vatFrameCount / Mathf.Max(vatFrameRate, 0.0001f);

    private void Start()
    {
        InitializeBuffers();
    }

    private void InitializeBuffers()
    {
        if (instanceMesh == null || instanceMaterial == null || computeShader == null)
        {
            Debug.LogError("[GPUInstancingManager_Stage3] Missing Mesh, Material, or ComputeShader reference.");
            enabled = false;
            return;
        }

        instanceCount = Mathf.Max(1, instanceCount);
        midLodRadius = Mathf.Max(midLodRadius, shadowCastingRadius + 0.01f);
        RecalculateGridSettings();

        // ── LOD 资源 fallback 规则 ──
        // near 必须用 Inspector 中拖入的 instanceMaterial（完整光照 + VAT + 阴影）
        // mid：优先用 midInstanceMaterial，没有则看 farInstanceMaterial，再没有就用 instanceMaterial
        // far：优先用 farInstanceMaterial，没有则复用 mid 材质
        runtimeNearMaterial = instanceMaterial;
        runtimeMidMaterial = midInstanceMaterial != null ? midInstanceMaterial :
            (farInstanceMaterial != null ? farInstanceMaterial : instanceMaterial);
        runtimeFarMaterial = farInstanceMaterial != null ? farInstanceMaterial : runtimeMidMaterial;

        // mid mesh 优先用指定的 midInstanceMesh，没有则复用完整网格
        // far mesh 优先用指定的 farInstanceMesh，没有则运行时创建一个 4 顶点 Billboard 四边形
        runtimeMidMesh = midInstanceMesh != null ? midInstanceMesh : instanceMesh;
        runtimeFarMesh = farInstanceMesh != null ? farInstanceMesh : MassGpuDrawUtility_Stage3.CreateBillboardQuadMesh();

        // 启用 GPU Instancing（对 SRP Batcher 兼容的材质也需要显式打开）
        runtimeNearMaterial.enableInstancing = true;
        runtimeMidMaterial.enableInstancing = true;
        runtimeFarMaterial.enableInstancing = true;

        // ── GPU 显存分配 ──
        // agentBuffer：存所有 Agent 的完整数据，大小 = instanceCount * sizeof(AgentData)
        // gridCountsBuffer：空间哈希每个格子的 Agent 计数，大小 = gridCellCount * 4 字节
        // gridAgentIndicesBuffer：空间哈希索引表，大小 = gridCellCount * maxAgentsPerCell * 4 字节
        // near/mid/far 索引 buffer：Append 模式，大小 = instanceCount * 4 字节（最坏情况全可见）
        agentBuffer = new ComputeBuffer(instanceCount, Marshal.SizeOf<AgentData>());
        gridCountsBuffer = new ComputeBuffer(gridCellCount, sizeof(uint));
        gridAgentIndicesBuffer = new ComputeBuffer(gridCellCount * maxAgentsPerCell, sizeof(uint));
        nearAgentIndexBuffer = MassGpuDrawUtility_Stage3.CreateAppendIndexBuffer(instanceCount);
        midAgentIndexBuffer = MassGpuDrawUtility_Stage3.CreateAppendIndexBuffer(instanceCount);
        farAgentIndexBuffer = MassGpuDrawUtility_Stage3.CreateAppendIndexBuffer(instanceCount);
        BuildAndUploadFlowField();

        // 把 CPU 端随机初始化的 Agent 数据上传到 GPU（只执行一次）
        UploadInitialAgents();

        // 创建间接绘制参数 buffer（每个 LOD 一个）
        nearArgsBuffer = MassGpuDrawUtility_Stage3.CreateArgsBuffer(instanceMesh);
        midArgsBuffer = MassGpuDrawUtility_Stage3.CreateArgsBuffer(runtimeMidMesh);
        farArgsBuffer = MassGpuDrawUtility_Stage3.CreateArgsBuffer(runtimeFarMesh);

        // 获取 Compute Shader 中三个 kernel 的入口索引
        kernels = MassGpuKernelSet_Stage3.Find(computeShader);

        // 把 ComputeBuffer 绑定到对应的 kernel 上（GPU 端通过名字+索引访问）
        BindComputeBuffers(kernels.ClearGrid);
        BindComputeBuffers(kernels.BuildSpatialHash);
        BindComputeBuffers(kernels.SimulateAndClassify);

        // 为每个 LOD draw call 创建 MaterialPropertyBlock，绑定各自的可见 Agent 索引表
        nearPropertyBlock = MassGpuDrawUtility_Stage3.CreatePropertyBlock(agentBuffer, nearAgentIndexBuffer, AgentBufferId, VisibleAgentIndicesId);
        midPropertyBlock = MassGpuDrawUtility_Stage3.CreatePropertyBlock(agentBuffer, midAgentIndexBuffer, AgentBufferId, VisibleAgentIndicesId);
        farPropertyBlock = MassGpuDrawUtility_Stage3.CreatePropertyBlock(agentBuffer, farAgentIndexBuffer, AgentBufferId, VisibleAgentIndicesId);

        // 同步 VAT 动画参数到所有材质（帧数、帧率）
        MassGpuDrawUtility_Stage3.SyncVatMaterial(runtimeNearMaterial, vatFrameCount, vatFrameRate, VATFrameCountId, VATFrameRateId);
        MassGpuDrawUtility_Stage3.SyncVatMaterial(runtimeMidMaterial, vatFrameCount, vatFrameRate, VATFrameCountId, VATFrameRateId);
        MassGpuDrawUtility_Stage3.SyncVatMaterial(runtimeFarMaterial, vatFrameCount, vatFrameRate, VATFrameCountId, VATFrameRateId);

        // 计算 Dispatch 的线程组数（上取整保证覆盖所有 Agent/格子）
        agentThreadGroupsX = Mathf.CeilToInt(instanceCount / 64f);
        gridThreadGroupsX = Mathf.CeilToInt(gridCellCount / 64f);

        // DrawMeshInstancedIndirect 需要 Bounds 做粗粒度剔除。
        // 这里给得比模拟区域大一圈，避免 Agent 被碰撞推开后超出 Bounds 导致整批不画。
        renderBounds = new Bounds(Vector3.zero, new Vector3(
            activeWorldSize.x + 40f,
            Mathf.Max(120f, spawnArea.y * 2f + 20f),
            activeWorldSize.y + 40f));

        Debug.Log($"[GPUInstancingManager_Stage3] Initialized {instanceCount} instances, grid {gridResolutionX}x{gridResolutionZ}, max {maxAgentsPerCell}/cell.");
    }

    /// <summary>
    /// 根据 spawnArea 和 cellSize 计算空间哈希网格的运行时参数。
    /// 网格以世界原点为中心，覆盖范围由 activeWorldSize 决定。
    /// </summary>
    private void RecalculateGridSettings()
    {
        cellSize = Mathf.Max(0.1f, cellSize);
        maxAgentsPerCell = Mathf.Max(1, maxAgentsPerCell);

        MassSpatialHashGridSettings_Stage3 grid = MassSpatialHashGridSettings_Stage3.Calculate(
            simulationWorldSize,
            spawnArea,
            boundaryPadding,
            cellSize);

        activeWorldSize = grid.WorldSize;
        gridResolutionX = grid.ResolutionX;
        gridResolutionZ = grid.ResolutionZ;
        gridCellCount = grid.CellCount;
        gridOrigin = grid.Origin;
    }

    /// <summary>
    /// 把 ComputeBuffer 绑定到指定 kernel。
    /// 绑定后 Compute Shader 的全局变量名就能直接读写这些 buffer。
    /// </summary>
    private void BindComputeBuffers(int kernel)
    {
        // 三个 kernel 都需要读写 Agent 数据与空间哈希 buffer
        computeShader.SetBuffer(kernel, AgentBufferId, agentBuffer);
        computeShader.SetBuffer(kernel, GridCountsId, gridCountsBuffer);
        computeShader.SetBuffer(kernel, GridAgentIndicesId, gridAgentIndicesBuffer);

        // 只有 SimulateAndClassify kernel 会往 near/mid/far 可见列表里 Append
        // ClearGrid 和 BuildSpatialHash 不需要这些 buffer
        if (kernel == kernels.SimulateAndClassify)
        {
            computeShader.SetBuffer(kernel, NearAgentIndicesId, nearAgentIndexBuffer);
            computeShader.SetBuffer(kernel, MidAgentIndicesId, midAgentIndexBuffer);
            computeShader.SetBuffer(kernel, FarAgentIndicesId, farAgentIndexBuffer);
            computeShader.SetBuffer(kernel, FlowFieldDirectionsId, flowFieldDirectionsBuffer);
        }
    }

    private void BuildAndUploadFlowField()
    {
        ReleaseBuffer(ref flowFieldDirectionsBuffer);

        if (!enableFlowFieldNavigation)
        {
            flowFieldResolutionX = 1;
            flowFieldResolutionZ = 1;
            flowFieldOrigin = gridOrigin;
            activeFlowFieldCellSize = Mathf.Max(0.25f, flowFieldCellSize);
            flowFieldDirectionsBuffer = new ComputeBuffer(1, sizeof(float) * 2);
            flowFieldDirectionsBuffer.SetData(new[] { Vector2.zero });
            CacheDisabledFlowFieldPreview();
            return;
        }

        activeFlowFieldCellSize = Mathf.Max(0.25f, flowFieldCellSize);
        if (paintedFlowFieldAsset == null)
        {
            UploadEmptyFlowField("No painted flow field asset assigned.");
            return;
        }

        UploadPaintedFlowField();
    }

    public void RebuildFlowFieldPreview()
    {
        RecalculateGridSettings();

        if (!enableFlowFieldNavigation)
        {
            CacheDisabledFlowFieldPreview();
            return;
        }

        activeFlowFieldCellSize = Mathf.Max(0.25f, flowFieldCellSize);
        if (paintedFlowFieldAsset == null)
        {
            CacheMissingPaintedFlowFieldPreview();
            return;
        }

        CachePaintedFlowFieldPreview(Application.isPlaying ? "Runtime painted flow field preview rebuilt." : "Editor painted flow field preview rebuilt.");
    }

    private void UploadPaintedFlowField()
    {
        paintedFlowFieldAsset.EnsureCellArray();
        Vector2[] flowVectors = paintedFlowFieldAsset.BuildFlowVectors();
        flowFieldResolutionX = paintedFlowFieldAsset.resolutionX;
        flowFieldResolutionZ = paintedFlowFieldAsset.resolutionZ;
        flowFieldOrigin = paintedFlowFieldAsset.origin;
        activeFlowFieldCellSize = paintedFlowFieldAsset.cellSize;

        flowFieldDirectionsBuffer = new ComputeBuffer(flowVectors.Length, sizeof(float) * 2);
        flowFieldDirectionsBuffer.SetData(flowVectors);
        CachePaintedFlowFieldPreview("Runtime painted flow field uploaded.");

        Debug.Log($"[GPUInstancingManager_Stage3] Stage4 painted flow field {flowFieldResolutionX}x{flowFieldResolutionZ}, asset {paintedFlowFieldAsset.name}.");
    }

    private void UploadEmptyFlowField(string status)
    {
        flowFieldResolutionX = 1;
        flowFieldResolutionZ = 1;
        flowFieldOrigin = gridOrigin;
        activeFlowFieldCellSize = Mathf.Max(0.25f, flowFieldCellSize);
        flowFieldDirectionsBuffer = new ComputeBuffer(1, sizeof(float) * 2);
        flowFieldDirectionsBuffer.SetData(new[] { Vector2.zero });
        CacheMissingPaintedFlowFieldPreview(status);
        Debug.LogWarning($"[GPUInstancingManager_Stage3] Stage4 painted flow field disabled: {status}");
    }

    private void CachePaintedFlowFieldPreview(string status)
    {
        if (paintedFlowFieldAsset == null)
            return;

        paintedFlowFieldAsset.EnsureCellArray();
        FlowFieldPreviewSnapshot preview = FlowFieldPreview;
        preview.isValid = true;
        preview.isEnabled = true;
        preview.resolutionX = paintedFlowFieldAsset.resolutionX;
        preview.resolutionZ = paintedFlowFieldAsset.resolutionZ;
        preview.origin = paintedFlowFieldAsset.origin;
        preview.worldSize = paintedFlowFieldAsset.worldSize;
        preview.cellSize = paintedFlowFieldAsset.cellSize;
        preview.target = GetPaintedFlowFieldCenter();
        preview.blockedCellCount = 0;
        preview.directions = paintedFlowFieldAsset.BuildFlowVectors();
        preview.costs = paintedFlowFieldAsset.BuildPreviewCosts();
        preview.status = status;
    }

    private void CacheMissingPaintedFlowFieldPreview(string status = "No painted flow field asset assigned.")
    {
        FlowFieldPreviewSnapshot preview = FlowFieldPreview;
        preview.isValid = true;
        preview.isEnabled = false;
        preview.resolutionX = 1;
        preview.resolutionZ = 1;
        preview.origin = gridOrigin;
        preview.worldSize = activeWorldSize;
        preview.cellSize = Mathf.Max(0.25f, flowFieldCellSize);
        preview.target = Vector2.zero;
        preview.blockedCellCount = 0;
        preview.directions = new[] { Vector2.zero };
        preview.costs = new[] { 0f };
        preview.status = status;
    }

    private void CacheDisabledFlowFieldPreview()
    {
        FlowFieldPreviewSnapshot preview = FlowFieldPreview;
        preview.isValid = true;
        preview.isEnabled = false;
        preview.resolutionX = 1;
        preview.resolutionZ = 1;
        preview.origin = gridOrigin;
        preview.worldSize = activeWorldSize;
        preview.cellSize = Mathf.Max(0.25f, flowFieldCellSize);
        preview.target = Vector2.zero;
        preview.blockedCellCount = 0;
        preview.directions = new[] { Vector2.zero };
        preview.costs = new[] { 0f };
        preview.status = "Flow field navigation is disabled.";
    }

    private Vector2 GetPaintedFlowFieldCenter()
    {
        if (paintedFlowFieldAsset == null)
            return Vector2.zero;

        return paintedFlowFieldAsset.origin + paintedFlowFieldAsset.worldSize * 0.5f;
    }

    [ContextMenu("Stage4/Rebuild Flow Field")]
    public void RebuildFlowField()
    {
        if (!Application.isPlaying)
        {
            Debug.Log("[GPUInstancingManager_Stage3] Flow field is uploaded when Play Mode starts.");
            return;
        }

        if (agentBuffer == null || computeShader == null)
            return;

        BuildAndUploadFlowField();
        BindComputeBuffers(kernels.SimulateAndClassify);
    }

    /// <summary>
    /// 在 CPU 端随机生成所有 Agent 的初始数据，然后一次性上传到 GPU。
    /// 这步只在 Start() 时执行一次，之后所有修改都由 Compute Shader 在 GPU 上完成。
    /// </summary>
    private void UploadInitialAgents()
    {
        AgentData[] initialData = MassAgentSpawnUtility_Stage3.BuildInitialAgents(
            instanceCount,
            spawnArea,
            spawnClusterForCollisionDemo,
            clusteredSpawnRadius,
            AnimationDuration);

        // SetData 把整个数组从 CPU 内存拷贝到 GPU 显存（通过 PCIe 总线，数据量大，只执行一次）
        agentBuffer.SetData(initialData);
    }

    /// <summary>
    /// 每帧主循环：按顺序执行 GPU 物理模拟和绘制的五步流水线。
    ///
    /// 【为什么 Dispatch 三个 kernel 后调用 DrawMeshInstancedIndirect 不会"等待 GPU 完成"？】
    /// Unity 的 ComputeShader.Dispatch 和 Graphics.DrawMeshInstancedIndirect 都只是
    /// 往 GPU 命令队列里追加指令，不会阻塞 CPU 等待 GPU 执行完。
    /// GPU 会按顺序执行这些命令（同一帧的命令保证顺序），所以绘制时 Compute Shader
    /// 已经完成了对 agentBuffer 的修改。
    /// </summary>
    private void Update()
    {
        if (agentBuffer == null)
            return;

        // 流水线步骤不能交换！
        // 1. 可见列表清零（不然上一帧数据会堆积）
        // 2. 上传本帧参数
        // 3. ClearGrid：清空空间格子计数
        // 4. BuildSpatialHash：根据当前位置重建空间哈希
        // 5. SimulateAndClassify：查邻域做碰撞 + 动画 + 剔除 + LOD 分类
        // 6. 把 AppendBuffer 的 GPU 计数器拷贝到 indirect args
        // 7. 发出三档 LOD 的绘制指令
        ResetAppendCounters();
        UploadFrameParameters();
        dispatchScheduler.DispatchSimulation(computeShader, kernels, gridThreadGroupsX, agentThreadGroupsX);
        CopyVisibleCountsToArgs();
        DrawLods();
    }

    /// <summary>把三个 AppendBuffer 的 GPU 内部计数器重置为 0，准备接收本帧的可见 Agent 索引。</summary>
    private void ResetAppendCounters()
    {
        nearAgentIndexBuffer.SetCounterValue(0);
        midAgentIndexBuffer.SetCounterValue(0);
        farAgentIndexBuffer.SetCounterValue(0);
    }

    /// <summary>
    /// 每帧把所有全局参数上传到 Compute Shader。
    /// 这些数据对所有 Agent 都是一样的（uniform），只需要传一次。
    /// 注意：LOD 半径传的是平方值（radius²），因为 Compute Shader 中比较距离时
    /// 直接用 dot(offset, offset) 与 radiusSqr 对比，省去开平方运算。
    /// </summary>
    private void UploadFrameParameters()
    {
        Vector3 center = GetLodCenter();

        // ── 通用帧参数 ──
        computeShader.SetFloat(DeltaTimeId, Time.deltaTime);
        computeShader.SetFloat(AnimationDurationId, AnimationDuration);
        computeShader.SetInt(FrameIndexId, Time.frameCount);

        // ── LOD 和视锥剔除参数 ──
        // 传平方值给 GPU，避免每个 Agent 都做 sqrt
        computeShader.SetVector(LodCenterId, center);
        computeShader.SetFloat(NearLodRadiusSqrId, shadowCastingRadius * shadowCastingRadius);
        computeShader.SetFloat(MidLodRadiusSqrId, midLodRadius * midLodRadius);
        computeShader.SetInt(EnableFrustumCullingId, enableFrustumCulling ? 1 : 0);
        computeShader.SetFloat(CullingRadiusId, cullingRadius);
        computeShader.SetInt(NearAnimationIntervalId, nearAnimationInterval);
        computeShader.SetInt(MidAnimationIntervalId, midAnimationInterval);
        computeShader.SetInt(FarAnimationIntervalId, farAnimationInterval);

        // ── 空间哈希与碰撞参数 ──
        computeShader.SetInt(GridCellCountId, gridCellCount);
        computeShader.SetInts(GridResolutionId, gridResolutionX, gridResolutionZ);
        computeShader.SetVector(GridOriginId, new Vector4(gridOrigin.x, gridOrigin.y, 0f, 0f));
        computeShader.SetVector(GridWorldSizeId, new Vector4(activeWorldSize.x, activeWorldSize.y, 0f, 0f));
        computeShader.SetFloat(CellSizeId, cellSize);
        computeShader.SetInt(MaxAgentsPerCellId, maxAgentsPerCell);
        computeShader.SetFloat(AgentRadiusId, agentRadius);
        computeShader.SetFloat(SeparationStrengthId, separationStrength);
        computeShader.SetFloat(VelocityDampingId, velocityDamping);
        computeShader.SetFloat(MaxSpeedId, maxSpeed);
        computeShader.SetFloat(BoundaryPaddingId, boundaryPadding);
        computeShader.SetInt(FlowFieldEnabledId, enableFlowFieldNavigation && flowFieldDirectionsBuffer != null ? 1 : 0);
        computeShader.SetInts(FlowFieldResolutionId, flowFieldResolutionX, flowFieldResolutionZ);
        computeShader.SetVector(FlowFieldOriginId, new Vector4(flowFieldOrigin.x, flowFieldOrigin.y, 0f, 0f));
        computeShader.SetFloat(FlowFieldCellSizeId, activeFlowFieldCellSize);
        computeShader.SetFloat(FlowFieldWeightId, flowFieldWeight);
        computeShader.SetFloat(FlowFieldResponsivenessId, flowFieldResponsiveness);

        UpdateFrustumPlanes();
        computeShader.SetVectorArray(FrustumPlanesId, frustumPlaneVectors);
    }

    /// <summary>
    /// 获取 LOD 距离计算的参考中心点。
    /// 优先级：Inspector 中指定的 lodCenter > cullingCamera > Camera.main > 世界原点。
    /// </summary>
    private Vector3 GetLodCenter()
    {
        if (lodCenter != null)
            return lodCenter.position;

        Camera activeCamera = GetActiveCullingCamera();
        return activeCamera != null ? activeCamera.transform.position : Vector3.zero;
    }

    /// <summary>
    /// 获取视锥剔除用的摄像机。
    /// 优先级：Inspector 中指定的 cullingCamera > Camera.main。
    /// </summary>
    private Camera GetActiveCullingCamera()
    {
        return cullingCamera != null ? cullingCamera : Camera.main;
    }

    /// <summary>
    /// 从摄像机计算 6 个视锥平面，转为 Compute Shader 可读的 float4 数组。
    /// 视锥是一个平截头体（frustum），由 6 个平面围成：近、远、左、右、上、下。
    /// 每个平面用 (nx, ny, nz, d) 表示，点在平面内侧等价于 dot(normal, pos) + d > 0。
    /// </summary>
    private void UpdateFrustumPlanes()
    {
        Camera activeCamera = GetActiveCullingCamera();
        if (!enableFrustumCulling || activeCamera == null)
        {
            // 禁用剔除时清零旧数据，避免审查代码时困惑
            for (int i = 0; i < frustumPlaneVectors.Length; i++)
                frustumPlaneVectors[i] = Vector4.zero;
            return;
        }

        // GeometryUtility.CalculateFrustumPlanes 返回 Unity Plane 结构体数组
        // Plane 格式：dot(normal, position) + distance
        GeometryUtility.CalculateFrustumPlanes(activeCamera, frustumPlanes);
        for (int i = 0; i < frustumPlanes.Length; i++)
        {
            Plane plane = frustumPlanes[i];
            Vector3 normal = plane.normal;
            frustumPlaneVectors[i] = new Vector4(normal.x, normal.y, normal.z, plane.distance);
        }
    }

    /// <summary>
    /// 把 AppendBuffer 的 GPU 内部计数器拷贝到 IndirectArguments buffer 的 instanceCount 字段。
    /// CopyCount(src, dst, offset) 从 src 的计数器读到 dst 的第 offset 字节处。
    /// 这里 offset=sizeof(uint)=4，即写到 args[1] 位置（instanceCount）。
    /// </summary>
    private void CopyVisibleCountsToArgs()
    {
        // sizeof(uint) = 4 字节偏移，对应 args[1]（也就是 instanceCount）
        ComputeBuffer.CopyCount(nearAgentIndexBuffer, nearArgsBuffer, sizeof(uint));
        ComputeBuffer.CopyCount(midAgentIndexBuffer, midArgsBuffer, sizeof(uint));
        ComputeBuffer.CopyCount(farAgentIndexBuffer, farArgsBuffer, sizeof(uint));
    }

    /// <summary>
    /// 发出三档 LOD 的 GPU Instancing 间接绘制指令。
    /// DrawMeshInstancedIndirect 让 GPU 直接从 buffer 里读 args（无需 CPU 指定实例数）。
    /// - Near：投射阴影（ShadowCastingMode.On），使用完整网格
    /// - Mid：不投射阴影，使用简化网格
    /// - Far：不投射阴影，使用 Billboard 四边形
    /// </summary>
    private void DrawLods()
    {
        // 参数：mesh, subMeshIndex, material, bounds, indirectArgsBuffer, argsOffset, propertyBlock, shadowMode, receiveShadows, layer
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

    /// <summary>
    /// 释放所有 GPU 显存资源（ComputeBuffer）。
    /// ComputeBuffer 不会自动被 GC 回收，必须显式调用 Release()。
    /// 泄露会导致显存不足、编辑器卡顿或崩溃。
    /// </summary>
    private void ReleaseBuffers()
    {
        ReleaseBuffer(ref agentBuffer);
        ReleaseBuffer(ref gridCountsBuffer);
        ReleaseBuffer(ref gridAgentIndicesBuffer);
        ReleaseBuffer(ref flowFieldDirectionsBuffer);
        ReleaseBuffer(ref nearAgentIndexBuffer);
        ReleaseBuffer(ref midAgentIndexBuffer);
        ReleaseBuffer(ref farAgentIndexBuffer);
        ReleaseBuffer(ref nearArgsBuffer);
        ReleaseBuffer(ref midArgsBuffer);
        ReleaseBuffer(ref farArgsBuffer);

        // 如果 far mesh 是运行时由脚本创建的（不是用户拖入的资产），由脚本负责销毁
        // 如果是 Inspector 中指定的资产文件，则不能销毁资产本体
        if (farInstanceMesh == null && runtimeFarMesh != null)
        {
            Destroy(runtimeFarMesh);
            runtimeFarMesh = null;
        }
    }

    /// <summary>安全释放单个 ComputeBuffer，防止重复释放。</summary>
    private static void ReleaseBuffer(ref ComputeBuffer buffer)
    {
        if (buffer == null)
            return;

        buffer.Release();
        buffer = null;
    }

#if UNITY_EDITOR
    /// <summary>
    /// 编辑器专用：Inspector 中参数变化时做钳制（clamp），防止非法值导致 GPU 崩溃。
    /// 不分配/释放 GPU 资源，避免编辑器里频繁改值触发的显存抖动。
    /// </summary>
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
        cellSize = Mathf.Max(0.1f, cellSize);
        maxAgentsPerCell = Mathf.Max(1, maxAgentsPerCell);
        agentRadius = Mathf.Max(0.01f, agentRadius);
        clusteredSpawnRadius = Mathf.Max(0.01f, clusteredSpawnRadius);
        maxSpeed = Mathf.Max(0.01f, maxSpeed);
        boundaryPadding = Mathf.Max(0f, boundaryPadding);
        flowFieldCellSize = Mathf.Max(0.25f, flowFieldCellSize);
        flowFieldResponsiveness = Mathf.Max(0f, flowFieldResponsiveness);
        flowFieldWeight = Mathf.Clamp01(flowFieldWeight);
        flowFieldPreviewStride = Mathf.Max(1, flowFieldPreviewStride);
    }
#endif
}
