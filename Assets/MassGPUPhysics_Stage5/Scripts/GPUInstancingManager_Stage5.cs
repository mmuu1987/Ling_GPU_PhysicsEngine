using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Stage5 GPU instancing manager. Owns compute buffers, simulation dispatch, VAT material
/// parameter upload, and indirect LOD drawing for the mass-agent demo.
/// </summary>
public class GPUInstancingManager_Stage5 : MonoBehaviour
{
    public enum DefenderMovementMode
    {
        HoldPositionNoSeparation = 0,
        UseDefenderFlowField = 1
    }

    public enum RuntimeFlowPreviewMode
    {
        FlowDirection = 0,
        DensityTarget = 1
    }

    [System.Serializable]
    public struct TeamCombatSettings
    {
        [Tooltip("该阵营方阵中心位置。")]
        public Vector3 spawnCenter;
        [Tooltip("该阵营方阵占用范围。X/Z 越大队形越松散。")]
        public Vector3 spawnSize;
        [Tooltip("该阵营主动锁定敌人的半径。")]
        [Min(0.1f)] public float targetAcquireRadius;
        [Tooltip("该阵营攻击判定距离。")]
        [Min(0.05f)] public float attackRange;
        [Tooltip("该阵营每次攻击造成的伤害。")]
        [Min(1)] public int attackDamage;
        [Tooltip("该阵营攻击间隔，单位秒。")]
        [Min(0.01f)] public float attackInterval;
        [Tooltip("该阵营最大生命值。")]
        [Min(1)] public int maxHp;
        [Tooltip("该阵营最大水平移动速度。")]
        [Min(0.01f)] public float maxSpeed;
        [Tooltip("该阵营在 XZ 平面的碰撞半径。")]
        [Min(0.01f)] public float agentRadius;
        [Tooltip("该阵营重叠时的分离强度。")]
        [Min(0f)] public float separationStrength;
        [Tooltip("该阵营速度阻尼。")]
        [Range(0f, 20f)] public float velocityDamping;

        public static TeamCombatSettings Create(
            Vector3 spawnCenter,
            Vector3 spawnSize,
            float targetAcquireRadius,
            float attackRange,
            int attackDamage,
            float attackInterval,
            int maxHp,
            float maxSpeed,
            float agentRadius,
            float separationStrength,
            float velocityDamping)
        {
            return new TeamCombatSettings
            {
                spawnCenter = spawnCenter,
                spawnSize = spawnSize,
                targetAcquireRadius = targetAcquireRadius,
                attackRange = attackRange,
                attackDamage = attackDamage,
                attackInterval = attackInterval,
                maxHp = maxHp,
                maxSpeed = maxSpeed,
                agentRadius = agentRadius,
                separationStrength = separationStrength,
                velocityDamping = velocityDamping
            };
        }

        public void Normalize()
        {
            spawnSize.x = Mathf.Max(0.01f, spawnSize.x);
            spawnSize.z = Mathf.Max(0.01f, spawnSize.z);
            targetAcquireRadius = Mathf.Max(0.1f, targetAcquireRadius);
            attackRange = Mathf.Max(0.05f, attackRange);
            attackDamage = Mathf.Max(1, attackDamage);
            attackInterval = Mathf.Max(0.01f, attackInterval);
            maxHp = Mathf.Max(1, maxHp);
            maxSpeed = Mathf.Max(0.01f, maxSpeed);
            agentRadius = Mathf.Max(0.01f, agentRadius);
            separationStrength = Mathf.Max(0f, separationStrength);
            velocityDamping = Mathf.Clamp(velocityDamping, 0f, 20f);
        }
    }

    /// <summary>
    /// Agent data layout shared by C#, compute shaders, and rendering shaders.
    /// Keep this struct in sync with the HLSL AgentData definition.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [System.Serializable]
    public struct AgentData
    {
        /// <summary>World position. Simulation uses XZ for horizontal movement.</summary>
        public Vector3 position;

        /// <summary>Euler rotation in degrees. The simulation currently writes yaw.</summary>
        public Vector3 rotation;

        /// <summary>Per-agent scale.</summary>
        public Vector3 scale;

        /// <summary>Horizontal velocity used by the compute simulation.</summary>
        public Vector3 velocity;

        /// <summary>Animation/render state.</summary>
        public int currentState;

        /// <summary>Current VAT animation time in seconds.</summary>
        public float currentAnimationTime;
    }

    [Header("Instancing")]
    [Tooltip("实例总数量。数值越大 GPU 负载和显存占用越高；双阵营模式下会按 Attacker Count 划分攻防双方。")]
    [Min(1)] public int instanceCount = 100000;
    [Tooltip("VAT 数据配置资产。拖入后会自动应用 Mesh、VAT 贴图、帧数、帧率和四段动作窗口。")]
    public VATProfile_Stage5 vatProfile;
    [Tooltip("近距离使用的完整实例网格。使用 VAT Profile 时会自动填充。")]
    public Mesh instanceMesh;
    [Tooltip("近距离使用的材质，通常是带 VAT 的角色材质。Profile 不持有材质，需要在这里指定。")]
    public Material instanceMaterial;
    [Tooltip("Stage5 使用的 Compute Shader，负责空间哈希、寻敌、战斗、移动、动画时间和 LOD 分类。")]
    public ComputeShader computeShader;

    [Header("LOD Meshes")]
    [Tooltip("中距离 LOD 网格。为空时复用完整网格。")]
    public Mesh midInstanceMesh;
    [Tooltip("远距离 LOD 网格。为空时运行时创建 4 顶点 Billboard。")]
    public Mesh farInstanceMesh;

    [Header("LOD Materials")]
    [Tooltip("中距离 LOD 材质。为空时优先复用 Far 材质，再复用近距离材质。")]
    public Material midInstanceMaterial;
    [Tooltip("远距离 LOD 材质，建议使用 BillboardInstancedAgent 类型材质。")]
    public Material farInstanceMaterial;

    [Header("Defender Instancing Override")]
    [Tooltip("防守方 VAT 数据配置资产。为空时复用攻击方 VAT Profile/手动 VAT 设置。防守方 Mesh 顶点拓扑不同于攻击方时必须单独指定。")]
    public VATProfile_Stage5 defenderVatProfile;
    [Tooltip("防守方近距离完整实例网格。为空时复用攻击方完整网格。")]
    public Mesh defenderInstanceMesh;
    [Tooltip("防守方中距离 LOD 网格。为空时优先使用防守方 Profile Low LOD，再复用攻击方中模/完整网格。")]
    public Mesh defenderMidInstanceMesh;
    [Tooltip("防守方远距离 LOD 网格。为空时复用攻击方远模，攻击方也为空时运行时创建 Billboard。")]
    public Mesh defenderFarInstanceMesh;
    [Tooltip("防守方近距离材质。为空时复用攻击方近距离材质。")]
    public Material defenderInstanceMaterial;
    [Tooltip("防守方中距离材质。为空时优先复用防守方远距离材质，再复用防守方近距离材质。")]
    public Material defenderMidInstanceMaterial;
    [Tooltip("防守方远距离材质。为空时复用防守方中距离材质。")]
    public Material defenderFarInstanceMaterial;

    [Header("Spawn")]
    [Tooltip("单阵营调试生成区域。启用 Two-Team Combat 时，主要使用攻击方/防守方各自的 Spawn Center 和 Spawn Size。")]
    public Vector3 spawnArea = new Vector3(100f, 0f, 100f);
    [Tooltip("单阵营调试用：开启后个体更集中，便于观察碰撞和分离。双阵营方阵生成不会使用这个开关。")]
    public bool spawnClusterForCollisionDemo = true;
    [Tooltip("单阵营集中生成半径。越小越拥挤，越容易触发 Separation。双阵营方阵生成不会使用这个值。")]
    [Min(0.01f)] public float clusteredSpawnRadius = 60f;

    [Header("Stage 5 Two-Team Combat")]
    [Tooltip("是否启用双阵营战斗。开启后按攻击方/防守方方阵生成；关闭后走单阵营随机/集中生成逻辑。")]
    public bool enableTwoTeamCombat = true;
    [Tooltip("战斗是否已经开始。未开始时双方只站队播放 Idle，不寻敌、不移动、不攻击；可通过 StartBattle/StopBattle 控制。")]
    public bool battleStarted;
    [Tooltip("防守方开战后的移动模式。Hold Position No Separation：原地防守、不执行分离；Use Defender Flow Field：使用独立防守方流场推进。")]
    public DefenderMovementMode defenderMovementMode = DefenderMovementMode.HoldPositionNoSeparation;
    [Tooltip("攻击方数量。0 表示全部是防守方；等于 Instance Count 表示全部是攻击方。")]
    [Min(0)] public int attackerCount = 50000;
    [Header("Attacker Team")]
    public TeamCombatSettings attackerSettings = TeamCombatSettings.Create(
        new Vector3(-45f, 0f, 0f),
        new Vector3(35f, 0f, 80f),
        18f,
        1.35f,
        10,
        0.8f,
        100,
        6f,
        0.45f,
        18f,
        5f);

    [Header("Defender Team")]
    public TeamCombatSettings defenderSettings = TeamCombatSettings.Create(
        new Vector3(35f, 0f, 0f),
        new Vector3(35f, 0f, 80f),
        16f,
        1.35f,
        10,
        0.8f,
        100,
        6f,
        0.45f,
        18f,
        5f);

    [SerializeField, HideInInspector] private bool splitTeamSettingsInitialized;
    [Tooltip("攻击方方阵中心位置。默认建议放在左侧，让攻击方面向 +X。")]
    [HideInInspector]
    public Vector3 attackerSpawnCenter = new Vector3(-45f, 0f, 0f);
    [Tooltip("攻击方方阵占用范围。X/Z 越大队形越松散；个体太挤时优先调大这个值。")]
    [HideInInspector]
    public Vector3 attackerSpawnSize = new Vector3(35f, 0f, 80f);
    [Tooltip("防守方方阵中心位置。默认建议放在右侧，让防守方面向 -X。")]
    [HideInInspector]
    public Vector3 defenderSpawnCenter = new Vector3(35f, 0f, 0f);
    [Tooltip("防守方方阵占用范围。X/Z 越大队形越松散；防守方被挤压明显时可适当调大。")]
    [HideInInspector]
    public Vector3 defenderSpawnSize = new Vector3(35f, 0f, 80f);
    [Tooltip("攻击方寻敌半径。越大越早锁定目标并脱离纯流场；太大可能过早朝密集敌群转向。")]
    [HideInInspector]
    [Min(0.1f)] public float targetAcquireRadius = 18f;
    [Tooltip("攻击判定距离。稍微调大可减少贴身挤压和左右转；太大会显得隔空攻击。")]
    [HideInInspector]
    [Min(0.05f)] public float attackRange = 1.35f;
    [Tooltip("每次攻击造成的伤害。只影响扣血速度，不影响移动或朝向。")]
    [HideInInspector]
    [Min(1)] public int attackDamage = 10;
    [Tooltip("攻击间隔，单位秒。越小攻击越频繁；太小会让死亡速度过快。")]
    [HideInInspector]
    [Min(0.01f)] public float attackInterval = 0.8f;
    [Tooltip("个体最大生命值。影响能承受多少次攻击。")]
    [HideInInspector]
    [Min(1)] public int maxHp = 100;
    [Tooltip("防守方回到守点的允许半径。主要用于允许防守方移动/追击的模式。")]
    [Min(0f)] public float defenderGuardRadius = 1.5f;
    [Tooltip("防守方主动发现敌人的半径。Hold Position 模式下防守方只在攻击范围内接敌，此值主要给其他防守移动模式使用。")]
    [HideInInspector]
    [Min(0.1f)] public float defenderAggroRadius = 16f;
    [Tooltip("防守方离开出生守点的最大追击距离。仅在允许防守方移动/追击时生效。")]
    [Min(0.1f)] public float defenderMaxChaseDistance = 24f;
    [Tooltip("死亡动画播放时长，单位秒。死亡状态不循环，会停在 Death VAT 的末帧。")]
    [Min(0.01f)] public float deathClipDuration = 1.5f;
    [Header("Spatial Hash Collision")]
    [Tooltip("空间哈希格子大小。建议接近 Agent Radius * 2；太小格子多，太大单格人数多、邻域检测变粗。")]
    [Min(0.1f)] public float cellSize = 2f;
    [Tooltip("每个空间格最多记录的 Agent 数量。太低会漏掉拥挤区域的碰撞对象；太高会增加显存和计算量。")]
    [Min(1)] public int maxAgentsPerCell = 64;
    [Tooltip("Agent 在 XZ 平面的碰撞半径。越大越早互相排斥、队伍更松；太大容易产生挤压、横向滑动和转向抖动。")]
    [HideInInspector]
    [Min(0.01f)] public float agentRadius = 0.45f;
    [Tooltip("重叠时的分离强度。越大推开越快；太大时会让人群左右乱转或弹开。攻击方撞上静止防守方时可适当降低。")]
    [HideInInspector]
    [Min(0f)] public float separationStrength = 18f;
    [Tooltip("速度阻尼。越大速度衰减越快、人群更稳；太大也会削弱分离释放，导致挤在一起。")]
    [HideInInspector]
    [Range(0f, 20f)] public float velocityDamping = 5f;
    [Tooltip("Agent 最大水平移动速度。越大推进越快；和高分离强度叠加时更容易出现快速转向。")]
    [HideInInspector]
    [Min(0.01f)] public float maxSpeed = 6f;
    [Tooltip("XZ 模拟区域总尺寸。设为 (0,0) 时自动根据生成区域推导；X 对应世界 X，Y 对应世界 Z。")]
    public Vector2 simulationWorldSize = Vector2.zero;
    [Tooltip("双阵营模式下，当 Simulation World Size 为 (0,0) 时，根据攻击方和防守方生成区域自动推导模拟世界边界，避免开战后被边界夹回。")]
    public bool autoSizeSimulationWorldForTwoTeamCombat = true;
    [Tooltip("双阵营自动模拟边界在攻防生成包围盒外额外扩展的距离。")]
    [Min(0f)] public float combatSimulationBoundsPadding = 80f;
    [Tooltip("模拟边界内缩距离。Agent 到边界会被推回，避免跑出空间哈希范围。")]
    [Min(0f)] public float boundaryPadding = 2f;

    [Header("LOD Distances")]
    [Tooltip("近处 LOD 半径。范围内使用完整网格、VAT 动画、光照和阴影；越大画质越好但绘制成本更高。")]
    [Min(0f)] public float shadowCastingRadius = 18f;
    [Tooltip("中距离 LOD 半径。Shadow Casting Radius 到此范围内使用中模；超过后使用远处 Billboard。")]
    [Min(0f)] public float midLodRadius = 75f;
    [Tooltip("LOD 距离计算中心。为空时优先使用 Culling Camera，再使用 Main Camera，最后使用世界原点。")]
    public Transform lodCenter;

    [Header("Frustum Culling")]
    [Tooltip("是否启用视锥剔除。开启后视野外 Agent 不加入绘制列表，可减少渲染成本。")]
    public bool enableFrustumCulling = true;
    [Tooltip("用于视锥剔除的相机。为空时自动使用 Main Camera。")]
    public Camera cullingCamera;
    [Tooltip("视锥剔除包围半径。越大越保守，不容易误剔除；越小剔除更激进。")]
    [Min(0f)] public float cullingRadius = 2f;

    [Header("Animation")]
    [Tooltip("VAT 总帧数。使用 VAT Profile 时会自动填充；手动模式下需与 VAT 贴图数据一致。")]
    [Min(1f)] public float vatFrameCount = 30f;
    [Tooltip("VAT 全局帧率。使用 VAT Profile 时会自动填充；影响动画播放速度。")]
    [Min(1f)] public float vatFrameRate = 30f;
    [Tooltip("近处 Agent 每隔多少帧推进一次动画。1 表示每帧更新，动画最流畅。")]
    [Min(1)] public int nearAnimationInterval = 1;
    [Tooltip("中距离 Agent 动画降频间隔。值越大越省性能，但动画越不连续。")]
    [Min(1)] public int midAnimationInterval = 2;
    [Tooltip("远距离 Agent 动画降频间隔。远处通常可设更大，因为 Billboard 不需要太精细的动画。")]
    [Min(1)] public int farAnimationInterval = 4;

    [Header("Stage 5 VAT Clip Windows")]
    [Tooltip("Idle 动作在 VAT 贴图中的起始帧和帧数。使用 VAT Profile 时自动填充。")]
    public Vector2 idleClipFrameRange = new Vector2(0f, 30f);
    [Tooltip("Move/Engage 动作在 VAT 贴图中的起始帧和帧数。使用 VAT Profile 时自动填充。")]
    public Vector2 moveClipFrameRange = new Vector2(0f, 30f);
    [Tooltip("Attack 动作在 VAT 贴图中的起始帧和帧数。使用 VAT Profile 时自动填充。")]
    public Vector2 attackClipFrameRange = new Vector2(0f, 30f);
    [Tooltip("Death 动作在 VAT 贴图中的起始帧和帧数。使用 VAT Profile 时自动填充。")]
    public Vector2 deathClipFrameRange = new Vector2(0f, 30f);
    [Tooltip("Idle 动作播放帧率。使用 VAT Profile 时自动填充。")]
    public float idleClipFrameRate = 30f;
    [Tooltip("Move/Engage 动作播放帧率。使用 VAT Profile 时自动填充。")]
    public float moveClipFrameRate = 30f;
    [Tooltip("Attack 动作播放帧率。使用 VAT Profile 时自动填充。")]
    public float attackClipFrameRate = 30f;
    [Tooltip("Death 动作播放帧率。使用 VAT Profile 时自动填充。")]
    public float deathClipFrameRate = 30f;

    [Header("Stage 4 Flow Field Navigation")]
    [Tooltip("是否启用流场导航。关闭后攻击方不会沿 Painted Flow Field 推进；防守方流场模式也会回退为原地防守。")]
    public bool enableFlowFieldNavigation = true;
    [Tooltip("流场格子大小，用于创建或适配 Painted Flow Field。越小路径更细，但数据量和编辑成本更高。")]
    [Min(0.25f)] public float flowFieldCellSize = 2f;
    [Tooltip("跟随流场方向的响应速度。越大转向越快；太大在急弯或拥挤处容易左右摆动。")]
    [Min(0f)] public float flowFieldResponsiveness = 6f;
    [Tooltip("流场控制权重。0 不受流场影响，1 完全按流场期望速度推进；拥挤或摆头时可降到 0.5~0.8。")]
    [Range(0f, 1f)] public float flowFieldWeight = 1f;
    [Tooltip("攻击方使用的 Painted Flow Field。旧场景继续使用这个字段作为攻击方流场。")]
    public PaintedFlowFieldAsset_Stage5 paintedFlowFieldAsset;
    [Tooltip("防守方独立流场。仅当 Defender Movement Mode 为 Use Defender Flow Field 时使用；为空会回退为原地防守。")]
    public PaintedFlowFieldAsset_Stage5 defenderPaintedFlowFieldAsset;
    [Tooltip("是否在 Inspector 显示当前攻击方流场预览。")]
    public bool showFlowFieldPreview = true;
    [Tooltip("流场预览箭头采样间隔。值越大箭头越少，Inspector 更清爽。")]
    [Min(1)] public int flowFieldPreviewStride = 2;
    [Tooltip("运行时自动根据模拟世界范围创建攻击方流场网格。Painted Flow Field 只作为初始方向模板重采样，不再决定运行时流场尺寸。")]
    public bool autoSizeRuntimeAttackerFlowField = true;
    [Tooltip("自动运行时流场在模拟世界范围外额外扩展的距离。")]
    [Min(0f)] public float runtimeFlowFieldPadding = 40f;
    [Tooltip("自动运行时流场单轴最大分辨率。范围越大时会自动增大 Cell Size，避免流场图过大。")]
    [Min(16)] public int runtimeFlowFieldMaxResolution = 256;
    [Tooltip("运行时流场 RenderTexture 预览模式。Flow Direction 显示速度流向；Density Target 显示战区目标强度。")]
    public RuntimeFlowPreviewMode runtimeFlowPreviewMode = RuntimeFlowPreviewMode.FlowDirection;

    [Header("Stage 5 Runtime Dynamic Attacker Flow")]
    [Tooltip("开战后根据活着的防守方分布，定时重建攻击方运行时流场。关闭后完全使用 Painted Flow Field。")]
    public bool enableRuntimeDynamicAttackerFlowField = true;
    [Tooltip("动态攻击流场更新间隔，单位秒。越小响应越快，但 GPU readback 和 CPU 生成频率越高。")]
    [Min(0.1f)] public float dynamicFlowUpdateInterval = 0.5f;
    [Tooltip("把防守方阵线沿 Z 方向切成几个战区；每个仍有足够防守方的战区会成为一个流场目标源。")]
    [Range(1, 8)] public int dynamicFlowSectorCount = 5;
    [Tooltip("流场格子距离动态目标小于该半径时方向归零，避免目标点附近原地打转。")]
    [Min(0f)] public float dynamicFlowTargetStopRadius = 2f;
    [Tooltip("每个战区至少需要多少个活防守方才会成为流场目标。")]
    [Min(1)] public int dynamicFlowMinDefendersPerTarget = 8;

    [Header("Stage 5 Runtime Dynamic Defender Flow")]
    [Tooltip("开战后根据活着的攻击方分布，定时重建防守方运行时流场。仅在 Defender Movement Mode 为 Use Defender Flow Field 时生效。")]
    public bool enableRuntimeDynamicDefenderFlowField;
    [Tooltip("运行时自动根据模拟世界范围创建防守方流场网格。Defender Painted Flow Field 只作为初始方向模板重采样，不再决定运行时流场尺寸。")]
    public bool autoSizeRuntimeDefenderFlowField = true;
    [Tooltip("自动防守方运行时流场在模拟世界范围外额外扩展的距离。")]
    [Min(0f)] public float runtimeDefenderFlowFieldPadding = 40f;
    [Tooltip("自动防守方运行时流场单轴最大分辨率。范围越大时会自动增大 Cell Size，避免流场图过大。")]
    [Min(16)] public int runtimeDefenderFlowFieldMaxResolution = 256;
    [Tooltip("动态防守流场更新间隔，单位秒。越小响应越快，但 GPU 生成频率越高。")]
    [Min(0.1f)] public float dynamicDefenderFlowUpdateInterval = 0.5f;
    [Tooltip("把攻击方阵线沿 Z 方向切成几个战区；每个仍有足够攻击方的战区会成为防守方流场目标源。")]
    [Range(1, 8)] public int dynamicDefenderFlowSectorCount = 5;
    [Tooltip("防守方流场格子距离动态目标小于该半径时方向归零，避免目标点附近原地打转。")]
    [Min(0f)] public float dynamicDefenderFlowTargetStopRadius = 2f;
    [Tooltip("每个战区至少需要多少个活攻击方才会成为防守方流场目标。")]
    [Min(1)] public int dynamicDefenderFlowMinAttackersPerTarget = 8;

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
        public string source = "Fallback";
        public int dynamicTargetCount;
        public Vector2[] dynamicTargets = new Vector2[0];
        public int aliveDefenderCount;
        public float lastRuntimeUpdateTime = -1f;
        public bool isWaitingForRuntimeReadback;
        public RenderTexture runtimePreviewTexture;
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
    private static readonly int NearAttackerAgentIndicesId = Shader.PropertyToID("nearAttackerAgentIndices");
    private static readonly int MidAttackerAgentIndicesId = Shader.PropertyToID("midAttackerAgentIndices");
    private static readonly int FarAttackerAgentIndicesId = Shader.PropertyToID("farAttackerAgentIndices");
    private static readonly int NearDefenderAgentIndicesId = Shader.PropertyToID("nearDefenderAgentIndices");
    private static readonly int MidDefenderAgentIndicesId = Shader.PropertyToID("midDefenderAgentIndices");
    private static readonly int FarDefenderAgentIndicesId = Shader.PropertyToID("farDefenderAgentIndices");
    private static readonly int VisibleAgentIndicesId = Shader.PropertyToID("visibleAgentIndices");
    private static readonly int GridCountsId = Shader.PropertyToID("gridCounts");
    private static readonly int GridAgentIndicesId = Shader.PropertyToID("gridAgentIndices");
    private static readonly int GridCountsReadBufferId = Shader.PropertyToID("gridCountsReadBuffer");
    private static readonly int GridAgentIndicesReadBufferId = Shader.PropertyToID("gridAgentIndicesReadBuffer");
    private static readonly int GridCellCountId = Shader.PropertyToID("gridCellCount");
    private static readonly int GridResolutionId = Shader.PropertyToID("gridResolution");
    private static readonly int GridOriginId = Shader.PropertyToID("gridOrigin");
    private static readonly int GridWorldSizeId = Shader.PropertyToID("gridWorldSize");
    private static readonly int CellSizeId = Shader.PropertyToID("cellSize");
    private static readonly int MaxAgentsPerCellId = Shader.PropertyToID("maxAgentsPerCell");
    private static readonly int AttackerAgentRadiusId = Shader.PropertyToID("attackerAgentRadius");
    private static readonly int DefenderAgentRadiusId = Shader.PropertyToID("defenderAgentRadius");
    private static readonly int AttackerSeparationStrengthId = Shader.PropertyToID("attackerSeparationStrength");
    private static readonly int DefenderSeparationStrengthId = Shader.PropertyToID("defenderSeparationStrength");
    private static readonly int AttackerVelocityDampingId = Shader.PropertyToID("attackerVelocityDamping");
    private static readonly int DefenderVelocityDampingId = Shader.PropertyToID("defenderVelocityDamping");
    private static readonly int AttackerMaxSpeedId = Shader.PropertyToID("attackerMaxSpeed");
    private static readonly int DefenderMaxSpeedId = Shader.PropertyToID("defenderMaxSpeed");
    private static readonly int BoundaryPaddingId = Shader.PropertyToID("boundaryPadding");
    private static readonly int FlowFieldDirectionsId = Shader.PropertyToID("flowFieldDirections");
    private static readonly int FlowFieldEnabledId = Shader.PropertyToID("flowFieldEnabled");
    private static readonly int FlowFieldResolutionId = Shader.PropertyToID("flowFieldResolution");
    private static readonly int FlowFieldOriginId = Shader.PropertyToID("flowFieldOrigin");
    private static readonly int FlowFieldCellSizeId = Shader.PropertyToID("flowFieldCellSize");
    private static readonly int FlowFieldWeightId = Shader.PropertyToID("flowFieldWeight");
    private static readonly int FlowFieldResponsivenessId = Shader.PropertyToID("flowFieldResponsiveness");
    private static readonly int RuntimeAttackerTargetDensityId = Shader.PropertyToID("runtimeAttackerTargetDensity");
    private static readonly int RuntimeAttackerFlowStatsId = Shader.PropertyToID("runtimeAttackerFlowStats");
    private static readonly int RuntimeAttackerFlowTargetsId = Shader.PropertyToID("runtimeAttackerFlowTargets");
    private static readonly int RuntimeAttackerFlowPreviewTextureId = Shader.PropertyToID("runtimeAttackerFlowPreviewTexture");
    private static readonly int RuntimeDefenderTargetDensityId = Shader.PropertyToID("runtimeDefenderTargetDensity");
    private static readonly int RuntimeDefenderFlowStatsId = Shader.PropertyToID("runtimeDefenderFlowStats");
    private static readonly int RuntimeDefenderFlowTargetsId = Shader.PropertyToID("runtimeDefenderFlowTargets");
    private static readonly int RuntimeDefenderFlowPreviewTextureId = Shader.PropertyToID("runtimeDefenderFlowPreviewTexture");
    private static readonly int RuntimeFlowPreviewModeId = Shader.PropertyToID("runtimeFlowPreviewMode");
    private static readonly int RuntimeDynamicAttackerFlowEnabledId = Shader.PropertyToID("runtimeDynamicAttackerFlowEnabled");
    private static readonly int RuntimeDynamicDefenderFlowEnabledId = Shader.PropertyToID("runtimeDynamicDefenderFlowEnabled");
    private static readonly int DynamicFlowSectorCountId = Shader.PropertyToID("dynamicFlowSectorCount");
    private static readonly int DynamicFlowTargetStopRadiusId = Shader.PropertyToID("dynamicFlowTargetStopRadius");
    private static readonly int DynamicFlowMinDefendersPerTargetId = Shader.PropertyToID("dynamicFlowMinDefendersPerTarget");
    private static readonly int DynamicDefenderFlowSectorCountId = Shader.PropertyToID("dynamicDefenderFlowSectorCount");
    private static readonly int DynamicDefenderFlowTargetStopRadiusId = Shader.PropertyToID("dynamicDefenderFlowTargetStopRadius");
    private static readonly int DynamicDefenderFlowMinAttackersPerTargetId = Shader.PropertyToID("dynamicDefenderFlowMinAttackersPerTarget");
    private static readonly int DefenderFlowFieldDirectionsId = Shader.PropertyToID("defenderFlowFieldDirections");
    private static readonly int DefenderFlowFieldEnabledId = Shader.PropertyToID("defenderFlowFieldEnabled");
    private static readonly int DefenderFlowFieldResolutionId = Shader.PropertyToID("defenderFlowFieldResolution");
    private static readonly int DefenderFlowFieldOriginId = Shader.PropertyToID("defenderFlowFieldOrigin");
    private static readonly int DefenderFlowFieldCellSizeId = Shader.PropertyToID("defenderFlowFieldCellSize");
    private static readonly int DefenderMovementModeId = Shader.PropertyToID("defenderMovementMode");
    private static readonly int TeamIdBufferId = Shader.PropertyToID("teamIdBuffer");
    private static readonly int HpBufferId = Shader.PropertyToID("hpBuffer");
    private static readonly int TeamIdReadBufferId = Shader.PropertyToID("teamIdReadBuffer");
    private static readonly int HpReadBufferId = Shader.PropertyToID("hpReadBuffer");
    private static readonly int TargetAgentIndexBufferId = Shader.PropertyToID("targetAgentIndexBuffer");
    private static readonly int AttackCooldownBufferId = Shader.PropertyToID("attackCooldownBuffer");
    private static readonly int HomePositionReadBufferId = Shader.PropertyToID("homePositionReadBuffer");
    private static readonly int PendingDamageBufferId = Shader.PropertyToID("pendingDamageBuffer");
    private static readonly int PendingDamageReadBufferId = Shader.PropertyToID("pendingDamageReadBuffer");
    private static readonly int EnableTwoTeamCombatId = Shader.PropertyToID("enableTwoTeamCombat");
    private static readonly int BattleStartedId = Shader.PropertyToID("battleStarted");
    private static readonly int AttackerCountId = Shader.PropertyToID("attackerCount");
    private static readonly int AttackerTargetAcquireRadiusId = Shader.PropertyToID("attackerTargetAcquireRadius");
    private static readonly int DefenderTargetAcquireRadiusId = Shader.PropertyToID("defenderTargetAcquireRadius");
    private static readonly int AttackerAttackRangeId = Shader.PropertyToID("attackerAttackRange");
    private static readonly int DefenderAttackRangeId = Shader.PropertyToID("defenderAttackRange");
    private static readonly int AttackerAttackDamageId = Shader.PropertyToID("attackerAttackDamage");
    private static readonly int DefenderAttackDamageId = Shader.PropertyToID("defenderAttackDamage");
    private static readonly int AttackerAttackIntervalId = Shader.PropertyToID("attackerAttackInterval");
    private static readonly int DefenderAttackIntervalId = Shader.PropertyToID("defenderAttackInterval");
    private static readonly int DefenderGuardRadiusId = Shader.PropertyToID("defenderGuardRadius");
    private static readonly int DefenderMaxChaseDistanceId = Shader.PropertyToID("defenderMaxChaseDistance");
    private static readonly int DeathClipDurationId = Shader.PropertyToID("deathClipDuration");
    private static readonly int VATFrameCountId = Shader.PropertyToID("_VATFrameCount");
    private static readonly int VATFrameRateId = Shader.PropertyToID("_VATFrameRate");
    private static readonly int IdleClipStartFrameId = Shader.PropertyToID("_IdleClipStartFrame");
    private static readonly int IdleClipFrameCountId = Shader.PropertyToID("_IdleClipFrameCount");
    private static readonly int IdleClipFrameRateId = Shader.PropertyToID("_IdleClipFrameRate");
    private static readonly int MoveClipStartFrameId = Shader.PropertyToID("_MoveClipStartFrame");
    private static readonly int MoveClipFrameCountId = Shader.PropertyToID("_MoveClipFrameCount");
    private static readonly int MoveClipFrameRateId = Shader.PropertyToID("_MoveClipFrameRate");
    private static readonly int AttackClipStartFrameId = Shader.PropertyToID("_AttackClipStartFrame");
    private static readonly int AttackClipFrameCountId = Shader.PropertyToID("_AttackClipFrameCount");
    private static readonly int AttackClipFrameRateId = Shader.PropertyToID("_AttackClipFrameRate");
    private static readonly int DeathClipStartFrameId = Shader.PropertyToID("_DeathClipStartFrame");
    private static readonly int DeathClipFrameCountId = Shader.PropertyToID("_DeathClipFrameCount");
    private static readonly int DeathClipFrameRateId = Shader.PropertyToID("_DeathClipFrameRate");
    private static readonly int VATPosTexId = Shader.PropertyToID("_VATPosTex");
    private static readonly int VATNormTexId = Shader.PropertyToID("_VATNormTex");
    private static readonly int VATTexWidthId = Shader.PropertyToID("_VATTexWidth");
    private static readonly int VATTexHeightId = Shader.PropertyToID("_VATTexHeight");
    private static readonly int VATRowsPerFrameId = Shader.PropertyToID("_VATRowsPerFrame");

    private readonly Plane[] frustumPlanes = new Plane[6];
    private readonly Vector4[] frustumPlaneVectors = new Vector4[6];

    private ComputeBuffer agentBuffer;
    private ComputeBuffer gridCountsBuffer;
    private ComputeBuffer gridAgentIndicesBuffer;
    private ComputeBuffer flowFieldDirectionsBuffer;
    private ComputeBuffer defenderFlowFieldDirectionsBuffer;
    private ComputeBuffer runtimeAttackerTargetDensityBuffer;
    private ComputeBuffer runtimeAttackerFlowStatsBuffer;
    private ComputeBuffer runtimeAttackerFlowTargetsBuffer;
    private ComputeBuffer runtimeDefenderTargetDensityBuffer;
    private ComputeBuffer runtimeDefenderFlowStatsBuffer;
    private ComputeBuffer runtimeDefenderFlowTargetsBuffer;
    private ComputeBuffer teamIdBuffer;
    private ComputeBuffer hpBuffer;
    private ComputeBuffer targetAgentIndexBuffer;
    private ComputeBuffer attackCooldownBuffer;
    private ComputeBuffer homePositionBuffer;
    private ComputeBuffer pendingDamageBuffer;

    private ComputeBuffer nearAttackerAgentIndexBuffer;
    private ComputeBuffer midAttackerAgentIndexBuffer;
    private ComputeBuffer farAttackerAgentIndexBuffer;
    private ComputeBuffer nearDefenderAgentIndexBuffer;
    private ComputeBuffer midDefenderAgentIndexBuffer;
    private ComputeBuffer farDefenderAgentIndexBuffer;

    private ComputeBuffer nearAttackerArgsBuffer;
    private ComputeBuffer midAttackerArgsBuffer;
    private ComputeBuffer farAttackerArgsBuffer;
    private ComputeBuffer nearDefenderArgsBuffer;
    private ComputeBuffer midDefenderArgsBuffer;
    private ComputeBuffer farDefenderArgsBuffer;

    private MaterialPropertyBlock nearAttackerPropertyBlock;
    private MaterialPropertyBlock midAttackerPropertyBlock;
    private MaterialPropertyBlock farAttackerPropertyBlock;
    private MaterialPropertyBlock nearDefenderPropertyBlock;
    private MaterialPropertyBlock midDefenderPropertyBlock;
    private MaterialPropertyBlock farDefenderPropertyBlock;

    private Mesh runtimeAttackerNearMesh;
    private Mesh runtimeAttackerMidMesh;
    private Mesh runtimeAttackerFarMesh;
    private Mesh runtimeDefenderNearMesh;
    private Mesh runtimeDefenderMidMesh;
    private Mesh runtimeDefenderFarMesh;
    private Mesh runtimeGeneratedFarMesh;
    private Material runtimeAttackerNearMaterial;
    private Material runtimeAttackerMidMaterial;
    private Material runtimeAttackerFarMaterial;
    private Material runtimeDefenderNearMaterial;
    private Material runtimeDefenderMidMaterial;
    private Material runtimeDefenderFarMaterial;
    private RenderTexture runtimeAttackerFlowPreviewTexture;
    private RenderTexture runtimeDefenderFlowPreviewTexture;

    private Bounds renderBounds;

    private MassGpuKernelSet_Stage5 kernels;
    private readonly MassGpuDispatchScheduler_Stage5 dispatchScheduler = new MassGpuDispatchScheduler_Stage5();

    private int agentThreadGroupsX;
    private int gridThreadGroupsX;

    private int gridResolutionX;
    private int gridResolutionZ;
    private int gridCellCount;
    private Vector2 activeWorldSize;
    private Vector2 gridOrigin;
    private int flowFieldResolutionX = 1;
    private int flowFieldResolutionZ = 1;
    private Vector2 flowFieldOrigin;
    private float activeFlowFieldCellSize = 2f;
    private int defenderFlowFieldResolutionX = 1;
    private int defenderFlowFieldResolutionZ = 1;
    private Vector2 defenderFlowFieldOrigin;
    private float activeDefenderFlowFieldCellSize = 2f;
    private float nextDynamicFlowUpdateTime;
    private float nextDefenderDynamicFlowUpdateTime;
    private bool runtimeDynamicAttackerFlowActive;
    private bool runtimeDynamicDefenderFlowActive;
    private float lastRuntimeDynamicFlowUpdateTime = -1f;
    private float lastRuntimeDynamicDefenderFlowUpdateTime = -1f;

    private float AnimationDuration => vatFrameCount / Mathf.Max(vatFrameRate, 0.0001f);
    private int FlowFieldThreadGroupsX => Mathf.CeilToInt(Mathf.Max(1, flowFieldResolutionX * flowFieldResolutionZ) / 64f);
    private int DefenderFlowFieldThreadGroupsX => Mathf.CeilToInt(Mathf.Max(1, defenderFlowFieldResolutionX * defenderFlowFieldResolutionZ) / 64f);

    private void Start()
    {
        MigrateLegacyTeamSettingsIfNeeded();
        attackerSettings.Normalize();
        defenderSettings.Normalize();
        InitializeBuffers();
    }

    public void StartBattle()
    {
        battleStarted = true;
        nextDynamicFlowUpdateTime = Time.time;
        nextDefenderDynamicFlowUpdateTime = Time.time;
    }

    public void StopBattle()
    {
        battleStarted = false;
        runtimeDynamicAttackerFlowActive = false;
        runtimeDynamicDefenderFlowActive = false;
        RestorePaintedAttackerFlowField("Battle stopped; attacker flow field restored to painted fallback.");
        RestorePaintedDefenderFlowField("Battle stopped; defender flow field restored to painted fallback.");
    }

    public void ResetBattleStarted()
    {
        battleStarted = false;
        runtimeDynamicAttackerFlowActive = false;
        runtimeDynamicDefenderFlowActive = false;
        RestorePaintedAttackerFlowField("Battle reset; attacker flow field restored to painted fallback.");
        RestorePaintedDefenderFlowField("Battle reset; defender flow field restored to painted fallback.");
    }

    private void InitializeBuffers()
    {
        if (!TryApplyVatProfile(true))
        {
            enabled = false;
            return;
        }

        if (instanceMesh == null || instanceMaterial == null || computeShader == null)
        {
            Debug.LogError("[GPUInstancingManager_Stage5] Missing Mesh, Material, or ComputeShader reference.");
            enabled = false;
            return;
        }

        instanceCount = Mathf.Max(1, instanceCount);
        midLodRadius = Mathf.Max(midLodRadius, shadowCastingRadius + 0.01f);
        RecalculateGridSettings();

        runtimeAttackerNearMesh = instanceMesh;
        runtimeAttackerMidMesh = midInstanceMesh != null ? midInstanceMesh : instanceMesh;
        runtimeGeneratedFarMesh = farInstanceMesh == null ? MassGpuDrawUtility_Stage5.CreateBillboardQuadMesh() : null;
        runtimeAttackerFarMesh = farInstanceMesh != null ? farInstanceMesh : runtimeGeneratedFarMesh;
        runtimeDefenderNearMesh = defenderInstanceMesh != null ? defenderInstanceMesh : runtimeAttackerNearMesh;
        runtimeDefenderMidMesh = defenderMidInstanceMesh != null ? defenderMidInstanceMesh :
            (defenderVatProfile != null && TryGetProfileMidMesh(defenderVatProfile, out Mesh defenderProfileMidMesh) ? defenderProfileMidMesh : runtimeAttackerMidMesh);
        runtimeDefenderFarMesh = defenderFarInstanceMesh != null ? defenderFarInstanceMesh : runtimeAttackerFarMesh;

        runtimeAttackerNearMaterial = instanceMaterial;
        runtimeAttackerMidMaterial = midInstanceMaterial != null ? midInstanceMaterial :
            (farInstanceMaterial != null ? farInstanceMaterial : instanceMaterial);
        runtimeAttackerFarMaterial = farInstanceMaterial != null ? farInstanceMaterial : runtimeAttackerMidMaterial;
        runtimeDefenderNearMaterial = defenderInstanceMaterial != null ? defenderInstanceMaterial : runtimeAttackerNearMaterial;
        runtimeDefenderMidMaterial = defenderMidInstanceMaterial != null ? defenderMidInstanceMaterial :
            (defenderFarInstanceMaterial != null ? defenderFarInstanceMaterial : runtimeDefenderNearMaterial);
        runtimeDefenderFarMaterial = defenderFarInstanceMaterial != null ? defenderFarInstanceMaterial : runtimeDefenderMidMaterial;

        EnableInstancing(runtimeAttackerNearMaterial);
        EnableInstancing(runtimeAttackerMidMaterial);
        EnableInstancing(runtimeAttackerFarMaterial);
        EnableInstancing(runtimeDefenderNearMaterial);
        EnableInstancing(runtimeDefenderMidMaterial);
        EnableInstancing(runtimeDefenderFarMaterial);

        agentBuffer = new ComputeBuffer(instanceCount, Marshal.SizeOf<AgentData>());
        gridCountsBuffer = new ComputeBuffer(gridCellCount, sizeof(uint));
        gridAgentIndicesBuffer = new ComputeBuffer(gridCellCount * maxAgentsPerCell, sizeof(uint));
        teamIdBuffer = new ComputeBuffer(instanceCount, sizeof(int));
        hpBuffer = new ComputeBuffer(instanceCount, sizeof(int));
        targetAgentIndexBuffer = new ComputeBuffer(instanceCount, sizeof(int));
        attackCooldownBuffer = new ComputeBuffer(instanceCount, sizeof(float));
        homePositionBuffer = new ComputeBuffer(instanceCount, sizeof(float) * 3);
        pendingDamageBuffer = new ComputeBuffer(instanceCount, sizeof(int));
        nearAttackerAgentIndexBuffer = MassGpuDrawUtility_Stage5.CreateAppendIndexBuffer(instanceCount);
        midAttackerAgentIndexBuffer = MassGpuDrawUtility_Stage5.CreateAppendIndexBuffer(instanceCount);
        farAttackerAgentIndexBuffer = MassGpuDrawUtility_Stage5.CreateAppendIndexBuffer(instanceCount);
        nearDefenderAgentIndexBuffer = MassGpuDrawUtility_Stage5.CreateAppendIndexBuffer(instanceCount);
        midDefenderAgentIndexBuffer = MassGpuDrawUtility_Stage5.CreateAppendIndexBuffer(instanceCount);
        farDefenderAgentIndexBuffer = MassGpuDrawUtility_Stage5.CreateAppendIndexBuffer(instanceCount);
        BuildAndUploadFlowField();
        CreateRuntimeDynamicFlowResources();
        nextDynamicFlowUpdateTime = Time.time + Mathf.Max(0.1f, dynamicFlowUpdateInterval);
        nextDefenderDynamicFlowUpdateTime = Time.time + Mathf.Max(0.1f, dynamicDefenderFlowUpdateInterval);

        UploadInitialAgents();

        nearAttackerArgsBuffer = MassGpuDrawUtility_Stage5.CreateArgsBuffer(runtimeAttackerNearMesh);
        midAttackerArgsBuffer = MassGpuDrawUtility_Stage5.CreateArgsBuffer(runtimeAttackerMidMesh);
        farAttackerArgsBuffer = MassGpuDrawUtility_Stage5.CreateArgsBuffer(runtimeAttackerFarMesh);
        nearDefenderArgsBuffer = MassGpuDrawUtility_Stage5.CreateArgsBuffer(runtimeDefenderNearMesh);
        midDefenderArgsBuffer = MassGpuDrawUtility_Stage5.CreateArgsBuffer(runtimeDefenderMidMesh);
        farDefenderArgsBuffer = MassGpuDrawUtility_Stage5.CreateArgsBuffer(runtimeDefenderFarMesh);

        kernels = MassGpuKernelSet_Stage5.Find(computeShader);

        BindComputeBuffers(kernels.ClearGrid);
        BindComputeBuffers(kernels.BuildSpatialHash);
        BindComputeBuffers(kernels.ClearRuntimeAttackerFlowResources);
        BindComputeBuffers(kernels.BuildRuntimeAttackerTargetDensity);
        BindComputeBuffers(kernels.SelectRuntimeAttackerFlowTargets);
        BindComputeBuffers(kernels.GenerateRuntimeAttackerFlowField);
        BindComputeBuffers(kernels.ClearRuntimeDefenderFlowResources);
        BindComputeBuffers(kernels.BuildRuntimeDefenderTargetDensity);
        BindComputeBuffers(kernels.SelectRuntimeDefenderFlowTargets);
        BindComputeBuffers(kernels.GenerateRuntimeDefenderFlowField);
        BindComputeBuffers(kernels.ClearPendingDamage);
        BindComputeBuffers(kernels.EvaluateStateAndAccumulateDamage);
        BindComputeBuffers(kernels.ResolveDamageSimulateAndClassify);
        BindComputeBuffers(kernels.ClassifyVisibleAgentsByTeam);

        nearAttackerPropertyBlock = MassGpuDrawUtility_Stage5.CreatePropertyBlock(agentBuffer, nearAttackerAgentIndexBuffer, AgentBufferId, VisibleAgentIndicesId);
        midAttackerPropertyBlock = MassGpuDrawUtility_Stage5.CreatePropertyBlock(agentBuffer, midAttackerAgentIndexBuffer, AgentBufferId, VisibleAgentIndicesId);
        farAttackerPropertyBlock = MassGpuDrawUtility_Stage5.CreatePropertyBlock(agentBuffer, farAttackerAgentIndexBuffer, AgentBufferId, VisibleAgentIndicesId);
        nearDefenderPropertyBlock = MassGpuDrawUtility_Stage5.CreatePropertyBlock(agentBuffer, nearDefenderAgentIndexBuffer, AgentBufferId, VisibleAgentIndicesId);
        midDefenderPropertyBlock = MassGpuDrawUtility_Stage5.CreatePropertyBlock(agentBuffer, midDefenderAgentIndexBuffer, AgentBufferId, VisibleAgentIndicesId);
        farDefenderPropertyBlock = MassGpuDrawUtility_Stage5.CreatePropertyBlock(agentBuffer, farDefenderAgentIndexBuffer, AgentBufferId, VisibleAgentIndicesId);

        SyncRuntimeVatBindings();

        agentThreadGroupsX = Mathf.CeilToInt(instanceCount / 64f);
        gridThreadGroupsX = Mathf.CeilToInt(gridCellCount / 64f);

        Vector2 gridCenter = gridOrigin + activeWorldSize * 0.5f;
        renderBounds = new Bounds(new Vector3(gridCenter.x, 0f, gridCenter.y), new Vector3(
            activeWorldSize.x + 40f,
            Mathf.Max(120f, spawnArea.y * 2f + 20f),
            activeWorldSize.y + 40f));

        Debug.Log($"[GPUInstancingManager_Stage5] Initialized {instanceCount} instances, grid {gridResolutionX}x{gridResolutionZ}, max {maxAgentsPerCell}/cell.");
    }

    private void RecalculateGridSettings()
    {
        cellSize = Mathf.Max(0.1f, cellSize);
        maxAgentsPerCell = Mathf.Max(1, maxAgentsPerCell);

        MassSpatialHashGridSettings_Stage5 grid;
        if (ShouldAutoSizeCombatSimulationWorld())
        {
            Bounds combatBounds = CalculateCombatSpawnBounds();
            float padding = Mathf.Max(0f, combatSimulationBoundsPadding) + Mathf.Max(0f, boundaryPadding);
            Vector2 min = new Vector2(combatBounds.min.x - padding, combatBounds.min.z - padding);
            Vector2 max = new Vector2(combatBounds.max.x + padding, combatBounds.max.z + padding);
            grid = MassSpatialHashGridSettings_Stage5.FromBounds(min, max, cellSize);
        }
        else
        {
            grid = MassSpatialHashGridSettings_Stage5.Calculate(
                simulationWorldSize,
                spawnArea,
                boundaryPadding,
                cellSize);
        }

        activeWorldSize = grid.WorldSize;
        gridResolutionX = grid.ResolutionX;
        gridResolutionZ = grid.ResolutionZ;
        gridCellCount = grid.CellCount;
        gridOrigin = grid.Origin;
    }

    private bool ShouldAutoSizeCombatSimulationWorld()
    {
        return enableTwoTeamCombat &&
               autoSizeSimulationWorldForTwoTeamCombat &&
               simulationWorldSize.x <= 0f &&
               simulationWorldSize.y <= 0f;
    }

    private void SyncVatClipWindows(Material material)
    {
        if (material == null)
            return;

        material.SetFloat(IdleClipStartFrameId, Mathf.Max(0f, idleClipFrameRange.x));
        material.SetFloat(IdleClipFrameCountId, Mathf.Max(1f, idleClipFrameRange.y));
        material.SetFloat(IdleClipFrameRateId, Mathf.Max(1f, idleClipFrameRate));
        material.SetFloat(MoveClipStartFrameId, Mathf.Max(0f, moveClipFrameRange.x));
        material.SetFloat(MoveClipFrameCountId, Mathf.Max(1f, moveClipFrameRange.y));
        material.SetFloat(MoveClipFrameRateId, Mathf.Max(1f, moveClipFrameRate));
        material.SetFloat(AttackClipStartFrameId, Mathf.Max(0f, attackClipFrameRange.x));
        material.SetFloat(AttackClipFrameCountId, Mathf.Max(1f, attackClipFrameRange.y));
        material.SetFloat(AttackClipFrameRateId, Mathf.Max(1f, attackClipFrameRate));
        material.SetFloat(DeathClipStartFrameId, Mathf.Max(0f, deathClipFrameRange.x));
        material.SetFloat(DeathClipFrameCountId, Mathf.Max(1f, deathClipFrameRange.y));
        material.SetFloat(DeathClipFrameRateId, Mathf.Max(1f, deathClipFrameRate));
    }

    public bool TryApplyVatProfile(bool logWarnings = true)
    {
        if (!TryApplyAttackerVatProfile(logWarnings))
            return false;

        return TryApplyDefenderVatProfile(logWarnings);
    }

    private bool TryApplyAttackerVatProfile(bool logWarnings)
    {
        if (vatProfile == null)
            return true;

        if (!vatProfile.IsValid(out string error))
        {
            if (logWarnings)
                Debug.LogError($"[GPUInstancingManager_Stage5] VAT Profile '{vatProfile.name}' is invalid: {error}", this);

            return false;
        }

        instanceMesh = vatProfile.cleanMesh;
        if (TryGetProfileMidMesh(vatProfile, out Mesh profileMidMesh))
            midInstanceMesh = profileMidMesh;

        vatFrameCount = Mathf.Max(1, vatProfile.totalFrameCount);
        vatFrameRate = Mathf.Max(1, vatProfile.frameRate);
        idleClipFrameRange = vatProfile.idle.ToRange();
        moveClipFrameRange = vatProfile.move.ToRange();
        attackClipFrameRange = vatProfile.attack.ToRange();
        deathClipFrameRange = vatProfile.death.ToRange();
        idleClipFrameRate = Mathf.Max(1, vatProfile.idle.frameRate);
        moveClipFrameRate = Mathf.Max(1, vatProfile.move.frameRate);
        attackClipFrameRate = Mathf.Max(1, vatProfile.attack.frameRate);
        deathClipFrameRate = Mathf.Max(1, vatProfile.death.frameRate);
        return true;
    }

    private bool TryApplyDefenderVatProfile(bool logWarnings)
    {
        if (defenderVatProfile == null)
            return true;

        if (!defenderVatProfile.IsValid(out string error))
        {
            if (logWarnings)
                Debug.LogError($"[GPUInstancingManager_Stage5] Defender VAT Profile '{defenderVatProfile.name}' is invalid: {error}", this);

            return false;
        }

        defenderInstanceMesh = defenderVatProfile.cleanMesh;
        if (TryGetProfileMidMesh(defenderVatProfile, out Mesh profileMidMesh))
            defenderMidInstanceMesh = profileMidMesh;

        return true;
    }

    public bool ApplyVatProfileToAssignedMaterials(bool logWarnings = true)
    {
        if (!TryApplyVatProfile(logWarnings))
            return false;

        if (vatProfile != null && vatProfile.IsValid(out string ignoredError))
        {
            MassGpuDrawUtility_Stage5.SyncVatMaterial(instanceMaterial, vatFrameCount, vatFrameRate, VATFrameCountId, VATFrameRateId);
            MassGpuDrawUtility_Stage5.SyncVatMaterial(midInstanceMaterial, vatFrameCount, vatFrameRate, VATFrameCountId, VATFrameRateId);
            MassGpuDrawUtility_Stage5.SyncVatMaterial(farInstanceMaterial, vatFrameCount, vatFrameRate, VATFrameCountId, VATFrameRateId);
            SyncVatClipWindows(instanceMaterial);
            SyncVatClipWindows(midInstanceMaterial);
            SyncVatClipWindows(farInstanceMaterial);

            SyncVatMaterialLayout(instanceMaterial, vatProfile.positionTexture, vatProfile.normalTexture, vatProfile.textureWidth, vatProfile.textureHeight, vatProfile.rowsPerFrame);
            SyncVatMaterialLayoutForLod(midInstanceMaterial, vatProfile, 1);
            SyncVatMaterialLayoutForLod(farInstanceMaterial, vatProfile, 2);
        }

        if (defenderVatProfile != null && defenderVatProfile.IsValid(out string ignoredDefenderError))
        {
            SyncVatMaterialProfileMetadata(defenderInstanceMaterial, defenderVatProfile);
            SyncVatMaterialProfileMetadata(defenderMidInstanceMaterial, defenderVatProfile);
            SyncVatMaterialProfileMetadata(defenderFarInstanceMaterial, defenderVatProfile);
            SyncVatMaterialLayout(defenderInstanceMaterial, defenderVatProfile.positionTexture, defenderVatProfile.normalTexture, defenderVatProfile.textureWidth, defenderVatProfile.textureHeight, defenderVatProfile.rowsPerFrame);
            SyncVatMaterialLayoutForLod(defenderMidInstanceMaterial, defenderVatProfile, 1);
            SyncVatMaterialLayoutForLod(defenderFarInstanceMaterial, defenderVatProfile, 2);
        }

        return true;
    }

    public string GetVatProfileStatus()
    {
        string attackerStatus = vatProfile == null
            ? "No VAT Profile assigned. Manual VAT fields are used for attacker/default rendering."
            : vatProfile.IsValid(out string error)
            ? $"VAT Profile ready: {vatProfile.name}"
            : $"VAT Profile invalid: {error}";

        if (defenderVatProfile == null)
            return attackerStatus + "\nDefender uses attacker/default rendering.";

        string defenderStatus = defenderVatProfile.IsValid(out string defenderError)
            ? $"Defender VAT Profile ready: {defenderVatProfile.name}"
            : $"Defender VAT Profile invalid: {defenderError}";
        return attackerStatus + "\n" + defenderStatus;
    }

    private static void EnableInstancing(Material material)
    {
        if (material != null)
            material.enableInstancing = true;
    }

    private void SyncRuntimeVatBindings()
    {
        SyncVatMaterialGroup(runtimeAttackerNearMaterial, runtimeAttackerMidMaterial, runtimeAttackerFarMaterial);
        SyncVatMaterialGroup(runtimeDefenderNearMaterial, runtimeDefenderMidMaterial, runtimeDefenderFarMaterial);

        SyncVatClipWindows(runtimeAttackerNearMaterial);
        SyncVatClipWindows(runtimeAttackerMidMaterial);
        SyncVatClipWindows(runtimeAttackerFarMaterial);
        SyncVatClipWindows(runtimeDefenderNearMaterial);
        SyncVatClipWindows(runtimeDefenderMidMaterial);
        SyncVatClipWindows(runtimeDefenderFarMaterial);

        SyncVatProfileToMaterials();
        SyncVatProfileToPropertyBlocks();
        SyncDefenderVatProfileToMaterials();
        SyncDefenderVatProfileToPropertyBlocks();
    }

    private void SyncVatMaterialGroup(Material nearMaterial, Material midMaterial, Material farMaterial)
    {
        MassGpuDrawUtility_Stage5.SyncVatMaterial(nearMaterial, vatFrameCount, vatFrameRate, VATFrameCountId, VATFrameRateId);
        MassGpuDrawUtility_Stage5.SyncVatMaterial(midMaterial, vatFrameCount, vatFrameRate, VATFrameCountId, VATFrameRateId);
        MassGpuDrawUtility_Stage5.SyncVatMaterial(farMaterial, vatFrameCount, vatFrameRate, VATFrameCountId, VATFrameRateId);
    }

    private void SyncVatProfileToMaterials()
    {
        if (vatProfile == null || !vatProfile.IsValid(out string ignoredError))
            return;

        SyncVatMaterialLayout(runtimeAttackerNearMaterial, vatProfile.positionTexture, vatProfile.normalTexture, vatProfile.textureWidth, vatProfile.textureHeight, vatProfile.rowsPerFrame);
        SyncVatMaterialLayoutForLod(runtimeAttackerMidMaterial, vatProfile, 1);
        SyncVatMaterialLayoutForLod(runtimeAttackerFarMaterial, vatProfile, 2);
    }

    private void SyncVatProfileToPropertyBlocks()
    {
        if (vatProfile == null || !vatProfile.IsValid(out string ignoredError))
            return;

        SyncVatPropertyBlock(nearAttackerPropertyBlock, vatProfile.positionTexture, vatProfile.normalTexture, vatProfile.textureWidth, vatProfile.textureHeight, vatProfile.rowsPerFrame);
        SyncVatPropertyBlockProfileMetadata(nearAttackerPropertyBlock, vatProfile);

        SyncVatPropertyBlockForLod(midAttackerPropertyBlock, vatProfile, 1);
        SyncVatPropertyBlockForLod(farAttackerPropertyBlock, vatProfile, 2);

        SyncVatPropertyBlockProfileMetadata(midAttackerPropertyBlock, vatProfile);
        SyncVatPropertyBlockProfileMetadata(farAttackerPropertyBlock, vatProfile);
    }

    private void SyncDefenderVatProfileToMaterials()
    {
        VATProfile_Stage5 profile = defenderVatProfile != null ? defenderVatProfile : vatProfile;
        if (profile == null || !profile.IsValid(out string ignoredError))
            return;

        SyncVatMaterialProfileMetadata(runtimeDefenderNearMaterial, profile);
        SyncVatMaterialProfileMetadata(runtimeDefenderMidMaterial, profile);
        SyncVatMaterialProfileMetadata(runtimeDefenderFarMaterial, profile);
        SyncVatMaterialLayout(runtimeDefenderNearMaterial, profile.positionTexture, profile.normalTexture, profile.textureWidth, profile.textureHeight, profile.rowsPerFrame);
        SyncVatMaterialLayoutForLod(runtimeDefenderMidMaterial, profile, 1);
        SyncVatMaterialLayoutForLod(runtimeDefenderFarMaterial, profile, 2);
    }

    private void SyncDefenderVatProfileToPropertyBlocks()
    {
        VATProfile_Stage5 profile = defenderVatProfile != null ? defenderVatProfile : vatProfile;
        if (profile == null || !profile.IsValid(out string ignoredError))
            return;

        SyncVatPropertyBlock(nearDefenderPropertyBlock, profile.positionTexture, profile.normalTexture, profile.textureWidth, profile.textureHeight, profile.rowsPerFrame);
        SyncVatPropertyBlockProfileMetadata(nearDefenderPropertyBlock, profile);

        SyncVatPropertyBlockForLod(midDefenderPropertyBlock, profile, 1);
        SyncVatPropertyBlockForLod(farDefenderPropertyBlock, profile, 2);

        SyncVatPropertyBlockProfileMetadata(midDefenderPropertyBlock, profile);
        SyncVatPropertyBlockProfileMetadata(farDefenderPropertyBlock, profile);
    }

    private static void SyncVatPropertyBlockProfileMetadata(MaterialPropertyBlock block, VATProfile_Stage5 profile)
    {
        if (block == null || profile == null)
            return;

        block.SetFloat(VATFrameCountId, Mathf.Max(1, profile.totalFrameCount));
        block.SetFloat(VATFrameRateId, Mathf.Max(1, profile.frameRate));
        SyncVatPropertyBlockClipWindow(block, IdleClipStartFrameId, IdleClipFrameCountId, IdleClipFrameRateId, profile.idle);
        SyncVatPropertyBlockClipWindow(block, MoveClipStartFrameId, MoveClipFrameCountId, MoveClipFrameRateId, profile.move);
        SyncVatPropertyBlockClipWindow(block, AttackClipStartFrameId, AttackClipFrameCountId, AttackClipFrameRateId, profile.attack);
        SyncVatPropertyBlockClipWindow(block, DeathClipStartFrameId, DeathClipFrameCountId, DeathClipFrameRateId, profile.death);
    }

    private static void SyncVatMaterialProfileMetadata(Material material, VATProfile_Stage5 profile)
    {
        if (material == null || profile == null)
            return;

        material.SetFloat(VATFrameCountId, Mathf.Max(1, profile.totalFrameCount));
        material.SetFloat(VATFrameRateId, Mathf.Max(1, profile.frameRate));
        SyncVatMaterialClipWindow(material, IdleClipStartFrameId, IdleClipFrameCountId, IdleClipFrameRateId, profile.idle);
        SyncVatMaterialClipWindow(material, MoveClipStartFrameId, MoveClipFrameCountId, MoveClipFrameRateId, profile.move);
        SyncVatMaterialClipWindow(material, AttackClipStartFrameId, AttackClipFrameCountId, AttackClipFrameRateId, profile.attack);
        SyncVatMaterialClipWindow(material, DeathClipStartFrameId, DeathClipFrameCountId, DeathClipFrameRateId, profile.death);
    }

    private static void SyncVatMaterialClipWindow(
        Material material,
        int startFrameId,
        int frameCountId,
        int frameRateId,
        VATProfile_Stage5.VATClipWindow clip)
    {
        material.SetFloat(startFrameId, Mathf.Max(0, clip.startFrame));
        material.SetFloat(frameCountId, Mathf.Max(1, clip.frameCount));
        material.SetFloat(frameRateId, Mathf.Max(1, clip.frameRate));
    }

    private static void SyncVatPropertyBlockClipWindow(
        MaterialPropertyBlock block,
        int startFrameId,
        int frameCountId,
        int frameRateId,
        VATProfile_Stage5.VATClipWindow clip)
    {
        block.SetFloat(startFrameId, Mathf.Max(0, clip.startFrame));
        block.SetFloat(frameCountId, Mathf.Max(1, clip.frameCount));
        block.SetFloat(frameRateId, Mathf.Max(1, clip.frameRate));
    }

    private static void SyncVatMaterialLayout(Material material, Texture positionTexture, Texture normalTexture, int textureWidth, int textureHeight, int rowsPerFrame)
    {
        if (material == null)
            return;

        material.SetTexture(VATPosTexId, positionTexture);
        material.SetTexture(VATNormTexId, normalTexture);
        material.SetFloat(VATTexWidthId, Mathf.Max(1, textureWidth));
        material.SetFloat(VATTexHeightId, Mathf.Max(1, textureHeight));
        material.SetFloat(VATRowsPerFrameId, Mathf.Max(1, rowsPerFrame));
    }

    private static void SyncVatPropertyBlock(MaterialPropertyBlock block, Texture positionTexture, Texture normalTexture, int textureWidth, int textureHeight, int rowsPerFrame)
    {
        if (block == null)
            return;

        block.SetTexture(VATPosTexId, positionTexture);
        block.SetTexture(VATNormTexId, normalTexture);
        block.SetFloat(VATTexWidthId, Mathf.Max(1, textureWidth));
        block.SetFloat(VATTexHeightId, Mathf.Max(1, textureHeight));
        block.SetFloat(VATRowsPerFrameId, Mathf.Max(1, rowsPerFrame));
    }

    private static bool TryGetProfileMidMesh(VATProfile_Stage5 profile, out Mesh mesh)
    {
        mesh = null;
        if (profile == null)
            return false;

        if (profile.HasMidLod)
        {
            mesh = profile.midLodMesh;
            return true;
        }

        if (profile.HasLowLod)
        {
            mesh = profile.lowLodMesh;
            return true;
        }

        return false;
    }

    private static void SyncVatMaterialLayoutForLod(Material material, VATProfile_Stage5 profile, int lodLevel)
    {
        if (profile == null)
            return;

        if (TryGetVatLayoutForLod(profile, lodLevel, out Texture positionTexture, out Texture normalTexture, out int textureWidth, out int textureHeight, out int rowsPerFrame))
            SyncVatMaterialLayout(material, positionTexture, normalTexture, textureWidth, textureHeight, rowsPerFrame);
    }

    private static void SyncVatPropertyBlockForLod(MaterialPropertyBlock block, VATProfile_Stage5 profile, int lodLevel)
    {
        if (profile == null)
            return;

        if (TryGetVatLayoutForLod(profile, lodLevel, out Texture positionTexture, out Texture normalTexture, out int textureWidth, out int textureHeight, out int rowsPerFrame))
            SyncVatPropertyBlock(block, positionTexture, normalTexture, textureWidth, textureHeight, rowsPerFrame);
    }

    private static bool TryGetVatLayoutForLod(
        VATProfile_Stage5 profile,
        int lodLevel,
        out Texture positionTexture,
        out Texture normalTexture,
        out int textureWidth,
        out int textureHeight,
        out int rowsPerFrame)
    {
        if (lodLevel == 1 && profile.HasMidLod)
        {
            positionTexture = profile.midLodPositionTexture;
            normalTexture = profile.midLodNormalTexture;
            textureWidth = profile.midLodTextureWidth;
            textureHeight = profile.midLodTextureHeight;
            rowsPerFrame = profile.midLodRowsPerFrame;
            return true;
        }

        if (lodLevel > 0 && profile.HasLowLod)
        {
            positionTexture = profile.lowLodPositionTexture;
            normalTexture = profile.lowLodNormalTexture;
            textureWidth = profile.lowLodTextureWidth;
            textureHeight = profile.lowLodTextureHeight;
            rowsPerFrame = profile.lowLodRowsPerFrame;
            return true;
        }

        if (lodLevel > 1 && profile.HasMidLod)
        {
            positionTexture = profile.midLodPositionTexture;
            normalTexture = profile.midLodNormalTexture;
            textureWidth = profile.midLodTextureWidth;
            textureHeight = profile.midLodTextureHeight;
            rowsPerFrame = profile.midLodRowsPerFrame;
            return true;
        }

        positionTexture = profile.positionTexture;
        normalTexture = profile.normalTexture;
        textureWidth = profile.textureWidth;
        textureHeight = profile.textureHeight;
        rowsPerFrame = profile.rowsPerFrame;
        return profile.IsValid(out string ignoredError);
    }

    private void BindComputeBuffers(int kernel)
    {
        computeShader.SetBuffer(kernel, AgentBufferId, agentBuffer);

        if (kernel == kernels.ClearGrid)
        {
            computeShader.SetBuffer(kernel, GridCountsId, gridCountsBuffer);
            return;
        }

        if (kernel == kernels.BuildSpatialHash)
        {
            computeShader.SetBuffer(kernel, HpReadBufferId, hpBuffer);
            computeShader.SetBuffer(kernel, GridCountsId, gridCountsBuffer);
            computeShader.SetBuffer(kernel, GridAgentIndicesId, gridAgentIndicesBuffer);
            return;
        }

        if (kernel == kernels.ClearRuntimeAttackerFlowResources)
        {
            computeShader.SetBuffer(kernel, RuntimeAttackerTargetDensityId, runtimeAttackerTargetDensityBuffer);
            computeShader.SetBuffer(kernel, RuntimeAttackerFlowStatsId, runtimeAttackerFlowStatsBuffer);
            computeShader.SetBuffer(kernel, RuntimeAttackerFlowTargetsId, runtimeAttackerFlowTargetsBuffer);
            return;
        }

        if (kernel == kernels.BuildRuntimeAttackerTargetDensity)
        {
            computeShader.SetBuffer(kernel, HpReadBufferId, hpBuffer);
            computeShader.SetBuffer(kernel, RuntimeAttackerTargetDensityId, runtimeAttackerTargetDensityBuffer);
            computeShader.SetBuffer(kernel, RuntimeAttackerFlowStatsId, runtimeAttackerFlowStatsBuffer);
            return;
        }

        if (kernel == kernels.SelectRuntimeAttackerFlowTargets)
        {
            computeShader.SetBuffer(kernel, RuntimeAttackerTargetDensityId, runtimeAttackerTargetDensityBuffer);
            computeShader.SetBuffer(kernel, RuntimeAttackerFlowStatsId, runtimeAttackerFlowStatsBuffer);
            computeShader.SetBuffer(kernel, RuntimeAttackerFlowTargetsId, runtimeAttackerFlowTargetsBuffer);
            return;
        }

        if (kernel == kernels.GenerateRuntimeAttackerFlowField)
        {
            computeShader.SetBuffer(kernel, FlowFieldDirectionsId, flowFieldDirectionsBuffer);
            computeShader.SetBuffer(kernel, RuntimeAttackerTargetDensityId, runtimeAttackerTargetDensityBuffer);
            computeShader.SetBuffer(kernel, RuntimeAttackerFlowStatsId, runtimeAttackerFlowStatsBuffer);
            computeShader.SetBuffer(kernel, RuntimeAttackerFlowTargetsId, runtimeAttackerFlowTargetsBuffer);
            computeShader.SetTexture(kernel, RuntimeAttackerFlowPreviewTextureId, runtimeAttackerFlowPreviewTexture);
            return;
        }

        if (kernel == kernels.ClearRuntimeDefenderFlowResources)
        {
            computeShader.SetBuffer(kernel, RuntimeDefenderTargetDensityId, runtimeDefenderTargetDensityBuffer);
            computeShader.SetBuffer(kernel, RuntimeDefenderFlowStatsId, runtimeDefenderFlowStatsBuffer);
            computeShader.SetBuffer(kernel, RuntimeDefenderFlowTargetsId, runtimeDefenderFlowTargetsBuffer);
            return;
        }

        if (kernel == kernels.BuildRuntimeDefenderTargetDensity)
        {
            computeShader.SetBuffer(kernel, HpReadBufferId, hpBuffer);
            computeShader.SetBuffer(kernel, RuntimeDefenderTargetDensityId, runtimeDefenderTargetDensityBuffer);
            computeShader.SetBuffer(kernel, RuntimeDefenderFlowStatsId, runtimeDefenderFlowStatsBuffer);
            return;
        }

        if (kernel == kernels.SelectRuntimeDefenderFlowTargets)
        {
            computeShader.SetBuffer(kernel, RuntimeDefenderTargetDensityId, runtimeDefenderTargetDensityBuffer);
            computeShader.SetBuffer(kernel, RuntimeDefenderFlowStatsId, runtimeDefenderFlowStatsBuffer);
            computeShader.SetBuffer(kernel, RuntimeDefenderFlowTargetsId, runtimeDefenderFlowTargetsBuffer);
            return;
        }

        if (kernel == kernels.GenerateRuntimeDefenderFlowField)
        {
            computeShader.SetBuffer(kernel, DefenderFlowFieldDirectionsId, defenderFlowFieldDirectionsBuffer);
            computeShader.SetBuffer(kernel, RuntimeDefenderTargetDensityId, runtimeDefenderTargetDensityBuffer);
            computeShader.SetBuffer(kernel, RuntimeDefenderFlowStatsId, runtimeDefenderFlowStatsBuffer);
            computeShader.SetBuffer(kernel, RuntimeDefenderFlowTargetsId, runtimeDefenderFlowTargetsBuffer);
            computeShader.SetTexture(kernel, RuntimeDefenderFlowPreviewTextureId, runtimeDefenderFlowPreviewTexture);
            return;
        }
 
        if (kernel == kernels.ClearPendingDamage)
        {
            computeShader.SetBuffer(kernel, PendingDamageBufferId, pendingDamageBuffer);
            return;
        }

        if (kernel == kernels.EvaluateStateAndAccumulateDamage)
        {
            computeShader.SetBuffer(kernel, GridCountsReadBufferId, gridCountsBuffer);
            computeShader.SetBuffer(kernel, GridAgentIndicesReadBufferId, gridAgentIndicesBuffer);
            computeShader.SetBuffer(kernel, TeamIdReadBufferId, teamIdBuffer);
            computeShader.SetBuffer(kernel, HpReadBufferId, hpBuffer);
            computeShader.SetBuffer(kernel, TargetAgentIndexBufferId, targetAgentIndexBuffer);
            computeShader.SetBuffer(kernel, AttackCooldownBufferId, attackCooldownBuffer);
            computeShader.SetBuffer(kernel, HomePositionReadBufferId, homePositionBuffer);
            computeShader.SetBuffer(kernel, PendingDamageBufferId, pendingDamageBuffer);
            return;
        } 

        if (kernel == kernels.ResolveDamageSimulateAndClassify)
        {
            computeShader.SetBuffer(kernel, GridCountsReadBufferId, gridCountsBuffer);
            computeShader.SetBuffer(kernel, GridAgentIndicesReadBufferId, gridAgentIndicesBuffer);
            computeShader.SetBuffer(kernel, TeamIdReadBufferId, teamIdBuffer);
            computeShader.SetBuffer(kernel, HpBufferId, hpBuffer);
            computeShader.SetBuffer(kernel, TargetAgentIndexBufferId, targetAgentIndexBuffer);
            computeShader.SetBuffer(kernel, AttackCooldownBufferId, attackCooldownBuffer);
            computeShader.SetBuffer(kernel, HomePositionReadBufferId, homePositionBuffer);
            computeShader.SetBuffer(kernel, PendingDamageReadBufferId, pendingDamageBuffer);
            computeShader.SetBuffer(kernel, FlowFieldDirectionsId, flowFieldDirectionsBuffer);
            computeShader.SetBuffer(kernel, DefenderFlowFieldDirectionsId, defenderFlowFieldDirectionsBuffer);
            return;
        }

        if (kernel == kernels.ClassifyVisibleAgentsByTeam)
        {
            computeShader.SetBuffer(kernel, TeamIdReadBufferId, teamIdBuffer);
            computeShader.SetBuffer(kernel, HpReadBufferId, hpBuffer);
            computeShader.SetBuffer(kernel, NearAttackerAgentIndicesId, nearAttackerAgentIndexBuffer);
            computeShader.SetBuffer(kernel, MidAttackerAgentIndicesId, midAttackerAgentIndexBuffer);
            computeShader.SetBuffer(kernel, FarAttackerAgentIndicesId, farAttackerAgentIndexBuffer);
            computeShader.SetBuffer(kernel, NearDefenderAgentIndicesId, nearDefenderAgentIndexBuffer);
            computeShader.SetBuffer(kernel, MidDefenderAgentIndicesId, midDefenderAgentIndexBuffer);
            computeShader.SetBuffer(kernel, FarDefenderAgentIndicesId, farDefenderAgentIndexBuffer);
        }
    }

    private void BuildAndUploadFlowField()
    {
        ReleaseBuffer(ref flowFieldDirectionsBuffer);
        ReleaseBuffer(ref defenderFlowFieldDirectionsBuffer);

        if (!enableFlowFieldNavigation)
        {
            CreateEmptyAttackerFlowFieldBuffer();
            CreateEmptyDefenderFlowFieldBuffer();
            CacheDisabledFlowFieldPreview();
            return;
        }

        ConfigureRuntimeAttackerFlowFieldGrid();
        UploadRuntimeAttackerFlowField();

        if (defenderMovementMode != DefenderMovementMode.UseDefenderFlowField)
        {
            UploadEmptyDefenderFlowField("Defender movement mode is Hold Position No Separation.");
        }
        else if (enableRuntimeDynamicDefenderFlowField)
        {
            ConfigureRuntimeDefenderFlowFieldGrid();
            UploadRuntimeDefenderFlowField();
        }
        else if (defenderPaintedFlowFieldAsset != null)
        {
            UploadDefenderPaintedFlowField();
        }
        else
        {
            UploadEmptyDefenderFlowField("No defender painted flow field asset assigned; defenders fall back to holding position.");
        }
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
        if (paintedFlowFieldAsset == null && !autoSizeRuntimeAttackerFlowField)
        {
            CacheMissingPaintedFlowFieldPreview();
            return;
        }

        if (Application.isPlaying && autoSizeRuntimeAttackerFlowField)
        {
            ConfigureRuntimeAttackerFlowFieldGrid();
            CacheRuntimeInitialFlowFieldPreview("Runtime auto-sized flow field preview rebuilt.");
        }
        else
        {
            CachePaintedFlowFieldPreview(Application.isPlaying ? "Runtime painted flow field preview rebuilt." : "Editor painted flow field preview rebuilt.");
        }
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

        Debug.Log($"[GPUInstancingManager_Stage5] Stage5 painted flow field {flowFieldResolutionX}x{flowFieldResolutionZ}, asset {paintedFlowFieldAsset.name}.");
    }

    private void ConfigureRuntimeAttackerFlowFieldGrid()
    {
        if (!autoSizeRuntimeAttackerFlowField && paintedFlowFieldAsset != null)
        {
            flowFieldResolutionX = paintedFlowFieldAsset.resolutionX;
            flowFieldResolutionZ = paintedFlowFieldAsset.resolutionZ;
            flowFieldOrigin = paintedFlowFieldAsset.origin;
            activeFlowFieldCellSize = paintedFlowFieldAsset.cellSize;
            return;
        }

        float padding = Mathf.Max(0f, runtimeFlowFieldPadding);
        Vector2 worldSize = new Vector2(
            Mathf.Max(0.25f, activeWorldSize.x + padding * 2f),
            Mathf.Max(0.25f, activeWorldSize.y + padding * 2f));
        flowFieldOrigin = gridOrigin - new Vector2(padding, padding);

        float requestedCellSize = Mathf.Max(0.25f, flowFieldCellSize);
        int maxResolution = Mathf.Max(16, runtimeFlowFieldMaxResolution);
        float resolutionCellSize = Mathf.Max(worldSize.x / maxResolution, worldSize.y / maxResolution);
        activeFlowFieldCellSize = Mathf.Max(requestedCellSize, resolutionCellSize);
        flowFieldResolutionX = Mathf.Max(1, Mathf.CeilToInt(worldSize.x / activeFlowFieldCellSize));
        flowFieldResolutionZ = Mathf.Max(1, Mathf.CeilToInt(worldSize.y / activeFlowFieldCellSize));
    }

    private void ConfigureRuntimeDefenderFlowFieldGrid()
    {
        if (!autoSizeRuntimeDefenderFlowField && defenderPaintedFlowFieldAsset != null)
        {
            defenderFlowFieldResolutionX = defenderPaintedFlowFieldAsset.resolutionX;
            defenderFlowFieldResolutionZ = defenderPaintedFlowFieldAsset.resolutionZ;
            defenderFlowFieldOrigin = defenderPaintedFlowFieldAsset.origin;
            activeDefenderFlowFieldCellSize = defenderPaintedFlowFieldAsset.cellSize;
            return;
        }

        float padding = Mathf.Max(0f, runtimeDefenderFlowFieldPadding);
        Vector2 worldSize = new Vector2(
            Mathf.Max(0.25f, activeWorldSize.x + padding * 2f),
            Mathf.Max(0.25f, activeWorldSize.y + padding * 2f));
        defenderFlowFieldOrigin = gridOrigin - new Vector2(padding, padding);

        float requestedCellSize = Mathf.Max(0.25f, flowFieldCellSize);
        int maxResolution = Mathf.Max(16, runtimeDefenderFlowFieldMaxResolution);
        float resolutionCellSize = Mathf.Max(worldSize.x / maxResolution, worldSize.y / maxResolution);
        activeDefenderFlowFieldCellSize = Mathf.Max(requestedCellSize, resolutionCellSize);
        defenderFlowFieldResolutionX = Mathf.Max(1, Mathf.CeilToInt(worldSize.x / activeDefenderFlowFieldCellSize));
        defenderFlowFieldResolutionZ = Mathf.Max(1, Mathf.CeilToInt(worldSize.y / activeDefenderFlowFieldCellSize));
    }

    private Bounds CalculateCombatSpawnBounds()
    {
        Bounds bounds = CreateXZBounds(attackerSettings.spawnCenter, attackerSettings.spawnSize);
        bounds.Encapsulate(CreateXZBounds(defenderSettings.spawnCenter, defenderSettings.spawnSize));

        if (!enableTwoTeamCombat)
            bounds.Encapsulate(CreateXZBounds(Vector3.zero, spawnArea));

        return bounds;
    }

    private static Bounds CreateXZBounds(Vector3 center, Vector3 size)
    {
        Vector3 safeSize = new Vector3(Mathf.Max(0.01f, size.x), 1f, Mathf.Max(0.01f, size.z));
        return new Bounds(new Vector3(center.x, 0f, center.z), safeSize);
    }

    private void UploadRuntimeAttackerFlowField()
    {
        Vector2[] flowVectors = BuildRuntimeInitialAttackerFlowVectors();
        flowFieldDirectionsBuffer = new ComputeBuffer(flowVectors.Length, sizeof(float) * 2);
        flowFieldDirectionsBuffer.SetData(flowVectors);
        CacheRuntimeInitialFlowFieldPreview("Runtime attacker flow grid uploaded.", flowVectors);

        Debug.Log($"[GPUInstancingManager_Stage5] Stage5 runtime attacker flow field {flowFieldResolutionX}x{flowFieldResolutionZ}, cell {activeFlowFieldCellSize:0.###}, origin {flowFieldOrigin}.");
    }

    private void UploadRuntimeDefenderFlowField()
    {
        Vector2[] flowVectors = BuildRuntimeInitialDefenderFlowVectors();
        defenderFlowFieldDirectionsBuffer = new ComputeBuffer(flowVectors.Length, sizeof(float) * 2);
        defenderFlowFieldDirectionsBuffer.SetData(flowVectors);

        Debug.Log($"[GPUInstancingManager_Stage5] Stage5 runtime defender flow field {defenderFlowFieldResolutionX}x{defenderFlowFieldResolutionZ}, cell {activeDefenderFlowFieldCellSize:0.###}, origin {defenderFlowFieldOrigin}.");
    }

    private Vector2[] BuildRuntimeInitialAttackerFlowVectors()
    {
        int count = Mathf.Max(1, flowFieldResolutionX * flowFieldResolutionZ);
        Vector2[] vectors = new Vector2[count];
        Vector2 fallbackTarget = new Vector2(defenderSettings.spawnCenter.x, defenderSettings.spawnCenter.z);
        Vector2[] paintedVectors = null;

        if (paintedFlowFieldAsset != null)
        {
            paintedFlowFieldAsset.EnsureCellArray();
            paintedVectors = paintedFlowFieldAsset.BuildFlowVectors();
        }

        for (int z = 0; z < flowFieldResolutionZ; z++)
        {
            for (int x = 0; x < flowFieldResolutionX; x++)
            {
                int index = z * flowFieldResolutionX + x;
                Vector2 center = RuntimeFlowCellCenter(x, z);
                Vector2 direction = paintedVectors != null
                    ? SamplePaintedFlowDirection(center, paintedVectors)
                    : Vector2.zero;

                if (direction.sqrMagnitude <= 0.0001f)
                {
                    Vector2 toTarget = fallbackTarget - center;
                    direction = toTarget.sqrMagnitude > 0.0001f ? toTarget.normalized : Vector2.zero;
                }

                vectors[index] = direction;
            }
        }

        return vectors;
    }

    private Vector2[] BuildRuntimeInitialDefenderFlowVectors()
    {
        int count = Mathf.Max(1, defenderFlowFieldResolutionX * defenderFlowFieldResolutionZ);
        Vector2[] vectors = new Vector2[count];
        Vector2 fallbackTarget = new Vector2(attackerSettings.spawnCenter.x, attackerSettings.spawnCenter.z);
        Vector2[] paintedVectors = null;

        if (defenderPaintedFlowFieldAsset != null)
        {
            defenderPaintedFlowFieldAsset.EnsureCellArray();
            paintedVectors = defenderPaintedFlowFieldAsset.BuildFlowVectors();
        }

        for (int z = 0; z < defenderFlowFieldResolutionZ; z++)
        {
            for (int x = 0; x < defenderFlowFieldResolutionX; x++)
            {
                int index = z * defenderFlowFieldResolutionX + x;
                Vector2 center = RuntimeDefenderFlowCellCenter(x, z);
                Vector2 direction = paintedVectors != null
                    ? SampleDefenderPaintedFlowDirection(center, paintedVectors)
                    : Vector2.zero;

                if (direction.sqrMagnitude <= 0.0001f)
                {
                    Vector2 toTarget = fallbackTarget - center;
                    direction = toTarget.sqrMagnitude > 0.0001f ? toTarget.normalized : Vector2.zero;
                }

                vectors[index] = direction;
            }
        }

        return vectors;
    }

    private Vector2 RuntimeFlowCellCenter(int x, int z)
    {
        return flowFieldOrigin + new Vector2((x + 0.5f) * activeFlowFieldCellSize, (z + 0.5f) * activeFlowFieldCellSize);
    }

    private Vector2 RuntimeDefenderFlowCellCenter(int x, int z)
    {
        return defenderFlowFieldOrigin + new Vector2((x + 0.5f) * activeDefenderFlowFieldCellSize, (z + 0.5f) * activeDefenderFlowFieldCellSize);
    }

    private Vector2 SamplePaintedFlowDirection(Vector2 world, Vector2[] paintedVectors)
    {
        if (paintedFlowFieldAsset == null || paintedVectors == null || paintedVectors.Length == 0)
            return Vector2.zero;

        Vector2 local = world - paintedFlowFieldAsset.origin;
        float cellSize = Mathf.Max(0.0001f, paintedFlowFieldAsset.cellSize);
        int x = Mathf.FloorToInt(local.x / cellSize);
        int z = Mathf.FloorToInt(local.y / cellSize);
        if (x < 0 || z < 0 || x >= paintedFlowFieldAsset.resolutionX || z >= paintedFlowFieldAsset.resolutionZ)
            return Vector2.zero;

        return paintedVectors[z * paintedFlowFieldAsset.resolutionX + x];
    }

    private Vector2 SampleDefenderPaintedFlowDirection(Vector2 world, Vector2[] paintedVectors)
    {
        if (defenderPaintedFlowFieldAsset == null || paintedVectors == null || paintedVectors.Length == 0)
            return Vector2.zero;

        Vector2 local = world - defenderPaintedFlowFieldAsset.origin;
        float cellSize = Mathf.Max(0.0001f, defenderPaintedFlowFieldAsset.cellSize);
        int x = Mathf.FloorToInt(local.x / cellSize);
        int z = Mathf.FloorToInt(local.y / cellSize);
        if (x < 0 || z < 0 || x >= defenderPaintedFlowFieldAsset.resolutionX || z >= defenderPaintedFlowFieldAsset.resolutionZ)
            return Vector2.zero;

        return paintedVectors[z * defenderPaintedFlowFieldAsset.resolutionX + x];
    }

    private void UploadDefenderPaintedFlowField()
    {
        defenderPaintedFlowFieldAsset.EnsureCellArray();
        Vector2[] flowVectors = defenderPaintedFlowFieldAsset.BuildFlowVectors();
        defenderFlowFieldResolutionX = defenderPaintedFlowFieldAsset.resolutionX;
        defenderFlowFieldResolutionZ = defenderPaintedFlowFieldAsset.resolutionZ;
        defenderFlowFieldOrigin = defenderPaintedFlowFieldAsset.origin;
        activeDefenderFlowFieldCellSize = defenderPaintedFlowFieldAsset.cellSize;

        defenderFlowFieldDirectionsBuffer = new ComputeBuffer(flowVectors.Length, sizeof(float) * 2);
        defenderFlowFieldDirectionsBuffer.SetData(flowVectors);

        Debug.Log($"[GPUInstancingManager_Stage5] Stage5 defender painted flow field {defenderFlowFieldResolutionX}x{defenderFlowFieldResolutionZ}, asset {defenderPaintedFlowFieldAsset.name}.");
    }

    private void UploadEmptyFlowField(string status)
    {
        CreateEmptyAttackerFlowFieldBuffer();
        CacheMissingPaintedFlowFieldPreview(status);
        Debug.LogWarning($"[GPUInstancingManager_Stage5] Stage5 painted flow field disabled: {status}");
    }

    private void UploadEmptyDefenderFlowField(string status)
    {
        CreateEmptyDefenderFlowFieldBuffer();
        if (defenderMovementMode == DefenderMovementMode.UseDefenderFlowField)
            Debug.LogWarning($"[GPUInstancingManager_Stage5] Stage5 defender painted flow field disabled: {status}");
    }

    private void CreateEmptyAttackerFlowFieldBuffer()
    {
        flowFieldResolutionX = 1;
        flowFieldResolutionZ = 1;
        flowFieldOrigin = gridOrigin;
        activeFlowFieldCellSize = Mathf.Max(0.25f, flowFieldCellSize);
        flowFieldDirectionsBuffer = new ComputeBuffer(1, sizeof(float) * 2);
        flowFieldDirectionsBuffer.SetData(new[] { Vector2.zero });
    }

    private void CreateEmptyDefenderFlowFieldBuffer()
    {
        defenderFlowFieldResolutionX = 1;
        defenderFlowFieldResolutionZ = 1;
        defenderFlowFieldOrigin = gridOrigin;
        activeDefenderFlowFieldCellSize = Mathf.Max(0.25f, flowFieldCellSize);
        defenderFlowFieldDirectionsBuffer = new ComputeBuffer(1, sizeof(float) * 2);
        defenderFlowFieldDirectionsBuffer.SetData(new[] { Vector2.zero });
    }

    private void CreateRuntimeDynamicFlowResources()
    {
        ReleaseBuffer(ref runtimeAttackerTargetDensityBuffer);
        ReleaseBuffer(ref runtimeAttackerFlowStatsBuffer);
        ReleaseBuffer(ref runtimeAttackerFlowTargetsBuffer);
        ReleaseBuffer(ref runtimeDefenderTargetDensityBuffer);
        ReleaseBuffer(ref runtimeDefenderFlowStatsBuffer);
        ReleaseBuffer(ref runtimeDefenderFlowTargetsBuffer);
        ReleaseRuntimeFlowPreviewTextures();

        int attackerCellCount = Mathf.Max(1, flowFieldResolutionX * flowFieldResolutionZ);
        runtimeAttackerTargetDensityBuffer = new ComputeBuffer(attackerCellCount, sizeof(uint));
        runtimeAttackerFlowStatsBuffer = new ComputeBuffer(4, sizeof(int));
        runtimeAttackerFlowTargetsBuffer = new ComputeBuffer(8, sizeof(float) * 4);

        runtimeAttackerFlowPreviewTexture = new RenderTexture(flowFieldResolutionX, flowFieldResolutionZ, 0, RenderTextureFormat.ARGBFloat)
        {
            enableRandomWrite = true,
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            name = "RuntimeDynamicAttackerFlowPreview_Stage5"
        };
        runtimeAttackerFlowPreviewTexture.Create();

        int defenderCellCount = Mathf.Max(1, defenderFlowFieldResolutionX * defenderFlowFieldResolutionZ);
        runtimeDefenderTargetDensityBuffer = new ComputeBuffer(defenderCellCount, sizeof(uint));
        runtimeDefenderFlowStatsBuffer = new ComputeBuffer(4, sizeof(int));
        runtimeDefenderFlowTargetsBuffer = new ComputeBuffer(8, sizeof(float) * 4);

        runtimeDefenderFlowPreviewTexture = new RenderTexture(defenderFlowFieldResolutionX, defenderFlowFieldResolutionZ, 0, RenderTextureFormat.ARGBFloat)
        {
            enableRandomWrite = true,
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            name = "RuntimeDynamicDefenderFlowPreview_Stage5"
        };
        runtimeDefenderFlowPreviewTexture.Create();

        FlowFieldPreview.runtimePreviewTexture = runtimeAttackerFlowPreviewTexture;
    }

    private void ReleaseRuntimeFlowPreviewTextures()
    {
        ReleaseRuntimeFlowPreviewTexture(ref runtimeAttackerFlowPreviewTexture);
        ReleaseRuntimeFlowPreviewTexture(ref runtimeDefenderFlowPreviewTexture);
    }

    private void ReleaseRuntimeFlowPreviewTexture(ref RenderTexture texture)
    {
        if (texture == null)
            return;

        texture.Release();
        if (Application.isPlaying)
            Destroy(texture);
        else
            DestroyImmediate(texture);
        texture = null;
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
        preview.source = "Painted";
        preview.dynamicTargetCount = 0;
        preview.dynamicTargets = new Vector2[0];
        preview.aliveDefenderCount = 0;
        preview.lastRuntimeUpdateTime = lastRuntimeDynamicFlowUpdateTime;
        preview.isWaitingForRuntimeReadback = false;
        preview.runtimePreviewTexture = runtimeAttackerFlowPreviewTexture;
    }

    private void CacheRuntimeInitialFlowFieldPreview(string status, Vector2[] directions = null)
    {
        FlowFieldPreviewSnapshot preview = FlowFieldPreview;
        preview.isValid = true;
        preview.isEnabled = true;
        preview.resolutionX = flowFieldResolutionX;
        preview.resolutionZ = flowFieldResolutionZ;
        preview.origin = flowFieldOrigin;
        preview.worldSize = new Vector2(flowFieldResolutionX * activeFlowFieldCellSize, flowFieldResolutionZ * activeFlowFieldCellSize);
        preview.cellSize = activeFlowFieldCellSize;
        preview.target = new Vector2(defenderSettings.spawnCenter.x, defenderSettings.spawnCenter.z);
        preview.blockedCellCount = 0;
        preview.directions = directions ?? BuildRuntimeInitialAttackerFlowVectors();
        preview.costs = BuildRuntimeFlowPreviewCosts();
        preview.status = status;
        preview.source = autoSizeRuntimeAttackerFlowField ? "Runtime Auto Sized" : "Painted";
        preview.dynamicTargetCount = 0;
        preview.dynamicTargets = new Vector2[0];
        preview.aliveDefenderCount = 0;
        preview.lastRuntimeUpdateTime = lastRuntimeDynamicFlowUpdateTime;
        preview.isWaitingForRuntimeReadback = false;
        preview.runtimePreviewTexture = runtimeAttackerFlowPreviewTexture;
    }

    private float[] BuildRuntimeFlowPreviewCosts()
    {
        int count = Mathf.Max(1, flowFieldResolutionX * flowFieldResolutionZ);
        float[] costs = new float[count];
        for (int i = 0; i < costs.Length; i++)
            costs[i] = 1f;

        return costs;
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
        preview.source = "Fallback";
        preview.dynamicTargetCount = 0;
        preview.dynamicTargets = new Vector2[0];
        preview.aliveDefenderCount = 0;
        preview.lastRuntimeUpdateTime = lastRuntimeDynamicFlowUpdateTime;
        preview.isWaitingForRuntimeReadback = false;
        preview.runtimePreviewTexture = runtimeAttackerFlowPreviewTexture;
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
        preview.source = "Fallback";
        preview.dynamicTargetCount = 0;
        preview.dynamicTargets = new Vector2[0];
        preview.aliveDefenderCount = 0;
        preview.lastRuntimeUpdateTime = lastRuntimeDynamicFlowUpdateTime;
        preview.isWaitingForRuntimeReadback = false;
        preview.runtimePreviewTexture = runtimeAttackerFlowPreviewTexture;
    }

    private Vector2 GetPaintedFlowFieldCenter()
    {
        if (paintedFlowFieldAsset == null)
            return Vector2.zero;

        return paintedFlowFieldAsset.origin + paintedFlowFieldAsset.worldSize * 0.5f;
    }

    [ContextMenu("Stage5/Rebuild Flow Field")]
    public void RebuildFlowField()
    {
        if (!Application.isPlaying)
        {
            Debug.Log("[GPUInstancingManager_Stage5] Flow field is uploaded when Play Mode starts.");
            return;
        }

        if (agentBuffer == null || computeShader == null)
            return;

        runtimeDynamicAttackerFlowActive = false;
        runtimeDynamicDefenderFlowActive = false;
        BuildAndUploadFlowField();
        CreateRuntimeDynamicFlowResources();
        nextDynamicFlowUpdateTime = Time.time + Mathf.Max(0.1f, dynamicFlowUpdateInterval);
        nextDefenderDynamicFlowUpdateTime = Time.time + Mathf.Max(0.1f, dynamicDefenderFlowUpdateInterval);
        BindComputeBuffers(kernels.ClearRuntimeAttackerFlowResources);
        BindComputeBuffers(kernels.BuildRuntimeAttackerTargetDensity);
        BindComputeBuffers(kernels.SelectRuntimeAttackerFlowTargets);
        BindComputeBuffers(kernels.GenerateRuntimeAttackerFlowField);
        BindComputeBuffers(kernels.ClearRuntimeDefenderFlowResources);
        BindComputeBuffers(kernels.BuildRuntimeDefenderTargetDensity);
        BindComputeBuffers(kernels.SelectRuntimeDefenderFlowTargets);
        BindComputeBuffers(kernels.GenerateRuntimeDefenderFlowField);
        BindComputeBuffers(kernels.ResolveDamageSimulateAndClassify);
    }

    private bool ShouldUseRuntimeDynamicAttackerFlowField()
    {
        return Application.isPlaying &&
               enableRuntimeDynamicAttackerFlowField &&
               enableFlowFieldNavigation &&
               enableTwoTeamCombat &&
               battleStarted &&
               runtimeAttackerTargetDensityBuffer != null &&
               runtimeAttackerFlowStatsBuffer != null &&
               runtimeAttackerFlowTargetsBuffer != null &&
               runtimeAttackerFlowPreviewTexture != null;
    }

    private bool ShouldUseRuntimeDynamicDefenderFlowField()
    {
        return Application.isPlaying &&
               enableRuntimeDynamicDefenderFlowField &&
               enableFlowFieldNavigation &&
               enableTwoTeamCombat &&
               battleStarted &&
               defenderMovementMode == DefenderMovementMode.UseDefenderFlowField &&
               defenderFlowFieldDirectionsBuffer != null &&
               runtimeDefenderTargetDensityBuffer != null &&
               runtimeDefenderFlowStatsBuffer != null &&
               runtimeDefenderFlowTargetsBuffer != null &&
               runtimeDefenderFlowPreviewTexture != null;
    }

    private bool ConsumeRuntimeDynamicAttackerFlowRebuildRequest()
    {
        FlowFieldPreview.isWaitingForRuntimeReadback = false;

        if (!ShouldUseRuntimeDynamicAttackerFlowField())
        {
            if (runtimeDynamicAttackerFlowActive)
            {
                runtimeDynamicAttackerFlowActive = false;
                RestorePaintedAttackerFlowField("Runtime dynamic attacker flow disabled; restored painted fallback.");
            }
            return false;
        }

        if (Time.time < nextDynamicFlowUpdateTime)
            return false;

        runtimeDynamicAttackerFlowActive = true;
        lastRuntimeDynamicFlowUpdateTime = Time.time;
        nextDynamicFlowUpdateTime = Time.time + Mathf.Max(0.1f, dynamicFlowUpdateInterval);
        CacheRuntimeGpuFlowPreview(
            "Runtime GPU Sector Flow",
            $"Runtime GPU attacker flow rebuilt every {dynamicFlowUpdateInterval:0.###}s. Preview is rendered on GPU.");
        return true;
    }

    private bool ConsumeRuntimeDynamicDefenderFlowRebuildRequest()
    {
        if (!ShouldUseRuntimeDynamicDefenderFlowField())
        {
            if (runtimeDynamicDefenderFlowActive)
            {
                runtimeDynamicDefenderFlowActive = false;
                RestorePaintedDefenderFlowField("Runtime dynamic defender flow disabled; restored painted fallback.");
            }
            return false;
        }

        if (Time.time < nextDefenderDynamicFlowUpdateTime)
            return false;

        runtimeDynamicDefenderFlowActive = true;
        lastRuntimeDynamicDefenderFlowUpdateTime = Time.time;
        nextDefenderDynamicFlowUpdateTime = Time.time + Mathf.Max(0.1f, dynamicDefenderFlowUpdateInterval);
        return true;
    }

    private void CacheRuntimeGpuFlowPreview(string source, string status)
    {
        FlowFieldPreviewSnapshot preview = FlowFieldPreview;
        preview.isValid = true;
        preview.isEnabled = enableFlowFieldNavigation && flowFieldDirectionsBuffer != null;
        preview.resolutionX = flowFieldResolutionX;
        preview.resolutionZ = flowFieldResolutionZ;
        preview.origin = flowFieldOrigin;
        preview.worldSize = new Vector2(flowFieldResolutionX * activeFlowFieldCellSize, flowFieldResolutionZ * activeFlowFieldCellSize);
        preview.cellSize = activeFlowFieldCellSize;
        preview.target = Vector2.zero;
        preview.blockedCellCount = 0;
        preview.status = status;
        preview.source = source;
        preview.dynamicTargetCount = 0;
        preview.dynamicTargets = new Vector2[0];
        preview.aliveDefenderCount = 0;
        preview.lastRuntimeUpdateTime = lastRuntimeDynamicFlowUpdateTime;
        preview.isWaitingForRuntimeReadback = false;
        preview.runtimePreviewTexture = runtimeAttackerFlowPreviewTexture;
    }

    private void RestorePaintedAttackerFlowField(string status)
    {
        if (!Application.isPlaying || computeShader == null || agentBuffer == null)
            return;

        runtimeDynamicAttackerFlowActive = false;
        ReleaseBuffer(ref flowFieldDirectionsBuffer);

        if (!enableFlowFieldNavigation)
        {
            CreateEmptyAttackerFlowFieldBuffer();
            CacheDisabledFlowFieldPreview();
        }
        else if (paintedFlowFieldAsset != null)
        {
            ConfigureRuntimeAttackerFlowFieldGrid();
            UploadRuntimeAttackerFlowField();
            FlowFieldPreview.status = status;
        }
        else
        {
            UploadEmptyFlowField(status);
            FlowFieldPreview.source = "Fallback";
        }

        BindComputeBuffers(kernels.SelectRuntimeAttackerFlowTargets);
        BindComputeBuffers(kernels.GenerateRuntimeAttackerFlowField);
        BindComputeBuffers(kernels.ResolveDamageSimulateAndClassify);
    }

    private void RestorePaintedDefenderFlowField(string status)
    {
        if (!Application.isPlaying || computeShader == null || agentBuffer == null)
            return;

        runtimeDynamicDefenderFlowActive = false;
        ReleaseBuffer(ref defenderFlowFieldDirectionsBuffer);

        if (!enableFlowFieldNavigation || defenderMovementMode != DefenderMovementMode.UseDefenderFlowField)
        {
            CreateEmptyDefenderFlowFieldBuffer();
        }
        else if (enableRuntimeDynamicDefenderFlowField)
        {
            ConfigureRuntimeDefenderFlowFieldGrid();
            UploadRuntimeDefenderFlowField();
        }
        else if (defenderPaintedFlowFieldAsset != null)
        {
            UploadDefenderPaintedFlowField();
        }
        else
        {
            UploadEmptyDefenderFlowField(status);
        }

        BindComputeBuffers(kernels.SelectRuntimeDefenderFlowTargets);
        BindComputeBuffers(kernels.GenerateRuntimeDefenderFlowField);
        BindComputeBuffers(kernels.ResolveDamageSimulateAndClassify);
    }

    private void UploadInitialAgents()
    {
        MassAgentSpawnUtility_Stage5.CombatSpawnData initialData = MassAgentSpawnUtility_Stage5.BuildInitialCombatData(
            instanceCount,
            enableTwoTeamCombat,
            attackerCount,
            spawnArea,
            spawnClusterForCollisionDemo,
            clusteredSpawnRadius,
            attackerSettings.spawnCenter,
            attackerSettings.spawnSize,
            defenderSettings.spawnCenter,
            defenderSettings.spawnSize,
            attackerSettings.maxHp,
            defenderSettings.maxHp,
            AnimationDuration);

        agentBuffer.SetData(initialData.Agents);
        teamIdBuffer.SetData(initialData.TeamIds);
        hpBuffer.SetData(initialData.Hp);
        targetAgentIndexBuffer.SetData(initialData.TargetAgentIndices);
        attackCooldownBuffer.SetData(initialData.AttackCooldowns);
        homePositionBuffer.SetData(initialData.HomePositions);
        pendingDamageBuffer.SetData(initialData.PendingDamage);
    }

    private void Update()
    {
        if (agentBuffer == null)
            return;

        ResetAppendCounters();
        UploadFrameParameters();
        bool rebuildRuntimeAttackerFlow = ConsumeRuntimeDynamicAttackerFlowRebuildRequest();
        bool rebuildRuntimeDefenderFlow = ConsumeRuntimeDynamicDefenderFlowRebuildRequest();
        dispatchScheduler.DispatchSimulation(
            computeShader,
            kernels,
            gridThreadGroupsX,
            agentThreadGroupsX,
            FlowFieldThreadGroupsX,
            DefenderFlowFieldThreadGroupsX,
            rebuildRuntimeAttackerFlow,
            rebuildRuntimeDefenderFlow);
        CopyVisibleCountsToArgs();
        DrawLods();
    }

    private void ResetAppendCounters()
    {
        nearAttackerAgentIndexBuffer.SetCounterValue(0);
        midAttackerAgentIndexBuffer.SetCounterValue(0);
        farAttackerAgentIndexBuffer.SetCounterValue(0);
        nearDefenderAgentIndexBuffer.SetCounterValue(0);
        midDefenderAgentIndexBuffer.SetCounterValue(0);
        farDefenderAgentIndexBuffer.SetCounterValue(0);
    }

    private void UploadFrameParameters()
    {
        Vector3 center = GetLodCenter();

        computeShader.SetFloat(DeltaTimeId, Time.deltaTime);
        computeShader.SetFloat(AnimationDurationId, AnimationDuration);
        computeShader.SetInt(FrameIndexId, Time.frameCount);

        computeShader.SetVector(LodCenterId, center);
        computeShader.SetFloat(NearLodRadiusSqrId, shadowCastingRadius * shadowCastingRadius);
        computeShader.SetFloat(MidLodRadiusSqrId, midLodRadius * midLodRadius);
        computeShader.SetInt(EnableFrustumCullingId, enableFrustumCulling ? 1 : 0);
        computeShader.SetFloat(CullingRadiusId, cullingRadius);
        computeShader.SetInt(NearAnimationIntervalId, nearAnimationInterval);
        computeShader.SetInt(MidAnimationIntervalId, midAnimationInterval);
        computeShader.SetInt(FarAnimationIntervalId, farAnimationInterval);

        computeShader.SetInt(GridCellCountId, gridCellCount);
        computeShader.SetInts(GridResolutionId, gridResolutionX, gridResolutionZ);
        computeShader.SetVector(GridOriginId, new Vector4(gridOrigin.x, gridOrigin.y, 0f, 0f));
        computeShader.SetVector(GridWorldSizeId, new Vector4(activeWorldSize.x, activeWorldSize.y, 0f, 0f));
        computeShader.SetFloat(CellSizeId, cellSize);
        computeShader.SetInt(MaxAgentsPerCellId, maxAgentsPerCell);
        computeShader.SetFloat(AttackerAgentRadiusId, attackerSettings.agentRadius);
        computeShader.SetFloat(DefenderAgentRadiusId, defenderSettings.agentRadius);
        computeShader.SetFloat(AttackerSeparationStrengthId, attackerSettings.separationStrength);
        computeShader.SetFloat(DefenderSeparationStrengthId, defenderSettings.separationStrength);
        computeShader.SetFloat(AttackerVelocityDampingId, attackerSettings.velocityDamping);
        computeShader.SetFloat(DefenderVelocityDampingId, defenderSettings.velocityDamping);
        computeShader.SetFloat(AttackerMaxSpeedId, attackerSettings.maxSpeed);
        computeShader.SetFloat(DefenderMaxSpeedId, defenderSettings.maxSpeed);
        computeShader.SetFloat(BoundaryPaddingId, boundaryPadding);
        computeShader.SetInt(FlowFieldEnabledId, enableFlowFieldNavigation && flowFieldDirectionsBuffer != null ? 1 : 0);
        computeShader.SetInts(FlowFieldResolutionId, flowFieldResolutionX, flowFieldResolutionZ);
        computeShader.SetVector(FlowFieldOriginId, new Vector4(flowFieldOrigin.x, flowFieldOrigin.y, 0f, 0f));
        computeShader.SetFloat(FlowFieldCellSizeId, activeFlowFieldCellSize);
        computeShader.SetFloat(FlowFieldWeightId, flowFieldWeight);
        computeShader.SetFloat(FlowFieldResponsivenessId, flowFieldResponsiveness);
        computeShader.SetInt(RuntimeFlowPreviewModeId, (int)runtimeFlowPreviewMode);
        computeShader.SetInt(RuntimeDynamicAttackerFlowEnabledId, ShouldUseRuntimeDynamicAttackerFlowField() ? 1 : 0);
        computeShader.SetInt(RuntimeDynamicDefenderFlowEnabledId, ShouldUseRuntimeDynamicDefenderFlowField() ? 1 : 0);
        computeShader.SetInt(DynamicFlowSectorCountId, dynamicFlowSectorCount);
        computeShader.SetFloat(DynamicFlowTargetStopRadiusId, dynamicFlowTargetStopRadius);
        computeShader.SetInt(DynamicFlowMinDefendersPerTargetId, dynamicFlowMinDefendersPerTarget);
        computeShader.SetInt(DynamicDefenderFlowSectorCountId, dynamicDefenderFlowSectorCount);
        computeShader.SetFloat(DynamicDefenderFlowTargetStopRadiusId, dynamicDefenderFlowTargetStopRadius);
        computeShader.SetInt(DynamicDefenderFlowMinAttackersPerTargetId, dynamicDefenderFlowMinAttackersPerTarget);
        bool defenderFlowEnabled = enableFlowFieldNavigation &&
                                   defenderMovementMode == DefenderMovementMode.UseDefenderFlowField &&
                                   defenderFlowFieldDirectionsBuffer != null;
        computeShader.SetInt(DefenderMovementModeId, defenderFlowEnabled ? (int)DefenderMovementMode.UseDefenderFlowField : (int)DefenderMovementMode.HoldPositionNoSeparation);
        computeShader.SetInt(DefenderFlowFieldEnabledId, defenderFlowEnabled ? 1 : 0);
        computeShader.SetInts(DefenderFlowFieldResolutionId, defenderFlowFieldResolutionX, defenderFlowFieldResolutionZ);
        computeShader.SetVector(DefenderFlowFieldOriginId, new Vector4(defenderFlowFieldOrigin.x, defenderFlowFieldOrigin.y, 0f, 0f));
        computeShader.SetFloat(DefenderFlowFieldCellSizeId, activeDefenderFlowFieldCellSize);
        computeShader.SetInt(EnableTwoTeamCombatId, enableTwoTeamCombat ? 1 : 0);
        computeShader.SetInt(BattleStartedId, battleStarted ? 1 : 0);
        computeShader.SetInt(AttackerCountId, Mathf.Clamp(attackerCount, 0, instanceCount));
        computeShader.SetFloat(AttackerTargetAcquireRadiusId, attackerSettings.targetAcquireRadius);
        computeShader.SetFloat(DefenderTargetAcquireRadiusId, defenderSettings.targetAcquireRadius);
        computeShader.SetFloat(AttackerAttackRangeId, attackerSettings.attackRange);
        computeShader.SetFloat(DefenderAttackRangeId, defenderSettings.attackRange);
        computeShader.SetInt(AttackerAttackDamageId, attackerSettings.attackDamage);
        computeShader.SetInt(DefenderAttackDamageId, defenderSettings.attackDamage);
        computeShader.SetFloat(AttackerAttackIntervalId, attackerSettings.attackInterval);
        computeShader.SetFloat(DefenderAttackIntervalId, defenderSettings.attackInterval);
        computeShader.SetFloat(DefenderGuardRadiusId, defenderGuardRadius);
        computeShader.SetFloat(DefenderMaxChaseDistanceId, defenderMaxChaseDistance);
        computeShader.SetFloat(DeathClipDurationId, deathClipDuration);

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
        ComputeBuffer.CopyCount(nearAttackerAgentIndexBuffer, nearAttackerArgsBuffer, sizeof(uint));
        ComputeBuffer.CopyCount(midAttackerAgentIndexBuffer, midAttackerArgsBuffer, sizeof(uint));
        ComputeBuffer.CopyCount(farAttackerAgentIndexBuffer, farAttackerArgsBuffer, sizeof(uint));
        ComputeBuffer.CopyCount(nearDefenderAgentIndexBuffer, nearDefenderArgsBuffer, sizeof(uint));
        ComputeBuffer.CopyCount(midDefenderAgentIndexBuffer, midDefenderArgsBuffer, sizeof(uint));
        ComputeBuffer.CopyCount(farDefenderAgentIndexBuffer, farDefenderArgsBuffer, sizeof(uint));
    }

    private void DrawLods()
    {
        DrawLod(runtimeAttackerNearMesh, runtimeAttackerNearMaterial, nearAttackerArgsBuffer, nearAttackerPropertyBlock, ShadowCastingMode.On);
        DrawLod(runtimeAttackerMidMesh, runtimeAttackerMidMaterial, midAttackerArgsBuffer, midAttackerPropertyBlock, ShadowCastingMode.Off);
        DrawLod(runtimeAttackerFarMesh, runtimeAttackerFarMaterial, farAttackerArgsBuffer, farAttackerPropertyBlock, ShadowCastingMode.Off);
        DrawLod(runtimeDefenderNearMesh, runtimeDefenderNearMaterial, nearDefenderArgsBuffer, nearDefenderPropertyBlock, ShadowCastingMode.On);
        DrawLod(runtimeDefenderMidMesh, runtimeDefenderMidMaterial, midDefenderArgsBuffer, midDefenderPropertyBlock, ShadowCastingMode.Off);
        DrawLod(runtimeDefenderFarMesh, runtimeDefenderFarMaterial, farDefenderArgsBuffer, farDefenderPropertyBlock, ShadowCastingMode.Off);
    }

    private void DrawLod(Mesh mesh, Material material, ComputeBuffer argsBuffer, MaterialPropertyBlock propertyBlock, ShadowCastingMode shadowCastingMode)
    {
        if (mesh == null || material == null || argsBuffer == null)
            return;

        Graphics.DrawMeshInstancedIndirect(
            mesh, 0, material, renderBounds, argsBuffer, 0,
            propertyBlock, shadowCastingMode, true, gameObject.layer);
    }

    private void OnDisable()
    {
        ReleaseBuffers();
    }

    private void ReleaseBuffers()
    {
        runtimeDynamicAttackerFlowActive = false;
        runtimeDynamicDefenderFlowActive = false;

        ReleaseBuffer(ref agentBuffer);
        ReleaseBuffer(ref gridCountsBuffer);
        ReleaseBuffer(ref gridAgentIndicesBuffer);
        ReleaseBuffer(ref flowFieldDirectionsBuffer);
        ReleaseBuffer(ref defenderFlowFieldDirectionsBuffer);
        ReleaseBuffer(ref runtimeAttackerTargetDensityBuffer);
        ReleaseBuffer(ref runtimeAttackerFlowStatsBuffer);
        ReleaseBuffer(ref runtimeAttackerFlowTargetsBuffer);
        ReleaseBuffer(ref runtimeDefenderTargetDensityBuffer);
        ReleaseBuffer(ref runtimeDefenderFlowStatsBuffer);
        ReleaseBuffer(ref runtimeDefenderFlowTargetsBuffer);
        ReleaseBuffer(ref teamIdBuffer);
        ReleaseBuffer(ref hpBuffer);
        ReleaseBuffer(ref targetAgentIndexBuffer);
        ReleaseBuffer(ref attackCooldownBuffer);
        ReleaseBuffer(ref homePositionBuffer);
        ReleaseBuffer(ref pendingDamageBuffer);
        ReleaseBuffer(ref nearAttackerAgentIndexBuffer);
        ReleaseBuffer(ref midAttackerAgentIndexBuffer);
        ReleaseBuffer(ref farAttackerAgentIndexBuffer);
        ReleaseBuffer(ref nearDefenderAgentIndexBuffer);
        ReleaseBuffer(ref midDefenderAgentIndexBuffer);
        ReleaseBuffer(ref farDefenderAgentIndexBuffer);
        ReleaseBuffer(ref nearAttackerArgsBuffer);
        ReleaseBuffer(ref midAttackerArgsBuffer);
        ReleaseBuffer(ref farAttackerArgsBuffer);
        ReleaseBuffer(ref nearDefenderArgsBuffer);
        ReleaseBuffer(ref midDefenderArgsBuffer);
        ReleaseBuffer(ref farDefenderArgsBuffer);
        ReleaseRuntimeFlowPreviewTextures();

        if (runtimeGeneratedFarMesh != null)
        {
            Destroy(runtimeGeneratedFarMesh);
            runtimeGeneratedFarMesh = null;
        }
    }

    private static void ReleaseBuffer(ref ComputeBuffer buffer)
    {
        if (buffer == null)
            return;

        buffer.Release();
        buffer = null;
    }

    private void MigrateLegacyTeamSettingsIfNeeded()
    {
        if (splitTeamSettingsInitialized)
            return;

        attackerSettings = TeamCombatSettings.Create(
            attackerSpawnCenter,
            attackerSpawnSize,
            targetAcquireRadius,
            attackRange,
            attackDamage,
            attackInterval,
            maxHp,
            maxSpeed,
            agentRadius,
            separationStrength,
            velocityDamping);

        defenderSettings = TeamCombatSettings.Create(
            defenderSpawnCenter,
            defenderSpawnSize,
            Mathf.Max(0.1f, defenderAggroRadius),
            attackRange,
            attackDamage,
            attackInterval,
            maxHp,
            maxSpeed,
            agentRadius,
            separationStrength,
            velocityDamping);

        splitTeamSettingsInitialized = true;
    }

#if UNITY_EDITOR


    private void OnGUI()
    {
        if (GUI.Button(new Rect(0f, 0f, 100f, 100f), "test"))
        {
            StartBattle();
        }
    }
    private void OnValidate()
    {
        TryApplyVatProfile(false);
        MigrateLegacyTeamSettingsIfNeeded();

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
        attackerSettings.Normalize();
        defenderSettings.Normalize();
        agentRadius = Mathf.Max(0.01f, agentRadius);
        clusteredSpawnRadius = Mathf.Max(0.01f, clusteredSpawnRadius);
        maxSpeed = Mathf.Max(0.01f, maxSpeed);
        boundaryPadding = Mathf.Max(0f, boundaryPadding);
        combatSimulationBoundsPadding = Mathf.Max(0f, combatSimulationBoundsPadding);
        flowFieldCellSize = Mathf.Max(0.25f, flowFieldCellSize);
        runtimeFlowFieldPadding = Mathf.Max(0f, runtimeFlowFieldPadding);
        runtimeFlowFieldMaxResolution = Mathf.Max(16, runtimeFlowFieldMaxResolution);
        flowFieldResponsiveness = Mathf.Max(0f, flowFieldResponsiveness);
        flowFieldWeight = Mathf.Clamp01(flowFieldWeight);
        flowFieldPreviewStride = Mathf.Max(1, flowFieldPreviewStride);
        dynamicFlowUpdateInterval = Mathf.Max(0.1f, dynamicFlowUpdateInterval);
        dynamicFlowSectorCount = Mathf.Clamp(dynamicFlowSectorCount, 1, 8);
        dynamicFlowTargetStopRadius = Mathf.Max(0f, dynamicFlowTargetStopRadius);
        dynamicFlowMinDefendersPerTarget = Mathf.Max(1, dynamicFlowMinDefendersPerTarget);
        runtimeDefenderFlowFieldPadding = Mathf.Max(0f, runtimeDefenderFlowFieldPadding);
        runtimeDefenderFlowFieldMaxResolution = Mathf.Max(16, runtimeDefenderFlowFieldMaxResolution);
        dynamicDefenderFlowUpdateInterval = Mathf.Max(0.1f, dynamicDefenderFlowUpdateInterval);
        dynamicDefenderFlowSectorCount = Mathf.Clamp(dynamicDefenderFlowSectorCount, 1, 8);
        dynamicDefenderFlowTargetStopRadius = Mathf.Max(0f, dynamicDefenderFlowTargetStopRadius);
        dynamicDefenderFlowMinAttackersPerTarget = Mathf.Max(1, dynamicDefenderFlowMinAttackersPerTarget);
        attackerCount = Mathf.Clamp(attackerCount, 0, instanceCount);
        targetAcquireRadius = Mathf.Max(0.1f, targetAcquireRadius);
        attackRange = Mathf.Max(0.05f, attackRange);
        attackDamage = Mathf.Max(1, attackDamage);
        attackInterval = Mathf.Max(0.01f, attackInterval);
        maxHp = Mathf.Max(1, maxHp);
        defenderGuardRadius = Mathf.Max(0f, defenderGuardRadius);
        defenderAggroRadius = Mathf.Max(0.1f, defenderAggroRadius);
        defenderMaxChaseDistance = Mathf.Max(0.1f, defenderMaxChaseDistance);
        deathClipDuration = Mathf.Max(0.01f, deathClipDuration);
        idleClipFrameRange = ClampClipRange(idleClipFrameRange);
        moveClipFrameRange = ClampClipRange(moveClipFrameRange);
        attackClipFrameRange = ClampClipRange(attackClipFrameRange);
        deathClipFrameRange = ClampClipRange(deathClipFrameRange);
        idleClipFrameRate = Mathf.Max(1f, idleClipFrameRate);
        moveClipFrameRate = Mathf.Max(1f, moveClipFrameRate);
        attackClipFrameRate = Mathf.Max(1f, attackClipFrameRate);
        deathClipFrameRate = Mathf.Max(1f, deathClipFrameRate);
    }

    private static Vector2 ClampClipRange(Vector2 range)
    {
        return new Vector2(Mathf.Max(0f, range.x), Mathf.Max(1f, range.y));
    }
#endif
}
