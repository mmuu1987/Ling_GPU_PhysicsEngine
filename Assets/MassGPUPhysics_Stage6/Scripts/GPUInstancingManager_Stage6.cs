using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Stage6 GPU instancing manager. Owns compute buffers, simulation dispatch, VAT material
/// parameter upload, and indirect LOD drawing for the mass-agent demo.
/// </summary>
public sealed class GPUInstancingManager_Stage6 : MonoBehaviour
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

    [Header("Stage6 Config Assets")]
    [Tooltip("Apply assigned Stage6 team config assets before runtime buffers are created.")]
    public bool applyConfigAssetsOnStart = true;
    [Tooltip("When enabled, team Spawn Config unit counts drive Instance Count and Attacker Count.")]
    public bool applyConfigUnitCounts = true;
    public Stage6TeamConfig_Stage6 attackerTeamConfig;
    public Stage6TeamConfig_Stage6 defenderTeamConfig;

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
    [HideInInspector] public VATProfile_Stage5 vatProfile;
    [Tooltip("近距离使用的完整实例网格。使用 VAT Profile 时会自动填充。")]
    [HideInInspector] public Mesh instanceMesh;
    [Tooltip("近距离使用的材质，通常是带 VAT 的角色材质。Profile 不持有材质，需要在这里指定。")]
    [HideInInspector] public Material instanceMaterial;
    [Tooltip("Stage6 使用的 Compute Shader，负责空间哈希、寻敌、战斗、移动、动画时间和 LOD 分类。")]
    [Header("Compute Shaders")]
    [HideInInspector] public ComputeShader computeShader;
    public ComputeShader spatialHashShader;
    public ComputeShader runtimeFlowShader;
    public ComputeShader combatSimulationShader;
    public ComputeShader lodClassificationShader;

    [Header("LOD Meshes")]
    [Tooltip("中距离 LOD 网格。为空时复用完整网格。")]
    [HideInInspector] public Mesh midInstanceMesh;
    [Tooltip("远距离 LOD 网格。为空时运行时创建 4 顶点 Billboard。")]
    [HideInInspector] public Mesh farInstanceMesh;

    [Header("LOD Materials")]
    [Tooltip("中距离 LOD 材质。为空时优先复用 Far 材质，再复用近距离材质。")]
    [HideInInspector] public Material midInstanceMaterial;
    [Tooltip("远距离 LOD 材质，建议使用 BillboardInstancedAgent 类型材质。")]
    [HideInInspector] public Material farInstanceMaterial;

    [Header("Defender Instancing Override")]
    [Tooltip("防守方 VAT 数据配置资产。为空时复用攻击方 VAT Profile/手动 VAT 设置。防守方 Mesh 顶点拓扑不同于攻击方时必须单独指定。")]
    [HideInInspector] public VATProfile_Stage5 defenderVatProfile;
    [Tooltip("防守方近距离完整实例网格。为空时复用攻击方完整网格。")]
    [HideInInspector] public Mesh defenderInstanceMesh;
    [Tooltip("防守方中距离 LOD 网格。为空时优先使用防守方 Profile Low LOD，再复用攻击方中模/完整网格。")]
    [HideInInspector] public Mesh defenderMidInstanceMesh;
    [Tooltip("防守方远距离 LOD 网格。为空时复用攻击方远模，攻击方也为空时运行时创建 Billboard。")]
    [HideInInspector] public Mesh defenderFarInstanceMesh;
    [Tooltip("防守方近距离材质。为空时复用攻击方近距离材质。")]
    [HideInInspector] public Material defenderInstanceMaterial;
    [Tooltip("防守方中距离材质。为空时优先复用防守方远距离材质，再复用防守方近距离材质。")]
    [HideInInspector] public Material defenderMidInstanceMaterial;
    [Tooltip("防守方远距离材质。为空时复用防守方中距离材质。")]
    [HideInInspector] public Material defenderFarInstanceMaterial;

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
    [HideInInspector, Min(0)] public int attackerCount = 50000;
    [Header("Attacker Team")]
    [HideInInspector] public TeamCombatSettings attackerSettings = TeamCombatSettings.Create(
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
    [HideInInspector] public TeamCombatSettings defenderSettings = TeamCombatSettings.Create(
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

    [SerializeField, HideInInspector] internal bool splitTeamSettingsInitialized;
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
    [HideInInspector, Min(0f)] public float defenderGuardRadius = 1.5f;
    [Tooltip("防守方主动发现敌人的半径。Hold Position 模式下防守方只在攻击范围内接敌，此值主要给其他防守移动模式使用。")]
    [HideInInspector]
    [Min(0.1f)] public float defenderAggroRadius = 16f;
    [Tooltip("防守方离开出生守点的最大追击距离。仅在允许防守方移动/追击时生效。")]
    [HideInInspector, Min(0.1f)] public float defenderMaxChaseDistance = 24f;
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
    [HideInInspector] public Vector2 idleClipFrameRange = new Vector2(0f, 30f);
    [Tooltip("Move/Engage 动作在 VAT 贴图中的起始帧和帧数。使用 VAT Profile 时自动填充。")]
    [HideInInspector] public Vector2 moveClipFrameRange = new Vector2(0f, 30f);
    [Tooltip("Attack 动作在 VAT 贴图中的起始帧和帧数。使用 VAT Profile 时自动填充。")]
    [HideInInspector] public Vector2 attackClipFrameRange = new Vector2(0f, 30f);
    [Tooltip("Death 动作在 VAT 贴图中的起始帧和帧数。使用 VAT Profile 时自动填充。")]
    [HideInInspector] public Vector2 deathClipFrameRange = new Vector2(0f, 30f);
    [Tooltip("Idle 动作播放帧率。使用 VAT Profile 时自动填充。")]
    [HideInInspector] public float idleClipFrameRate = 30f;
    [Tooltip("Move/Engage 动作播放帧率。使用 VAT Profile 时自动填充。")]
    [HideInInspector] public float moveClipFrameRate = 30f;
    [Tooltip("Attack 动作播放帧率。使用 VAT Profile 时自动填充。")]
    [HideInInspector] public float attackClipFrameRate = 30f;
    [Tooltip("Death 动作播放帧率。使用 VAT Profile 时自动填充。")]
    [HideInInspector] public float deathClipFrameRate = 30f;

    [Header("Stage 4 Flow Field Navigation")]
    [Tooltip("是否启用流场导航。关闭后攻击方不会沿 Painted Flow Field 推进；防守方流场模式也会回退为原地防守。")]
    public bool enableFlowFieldNavigation = true;
    [Tooltip("流场格子大小，用于创建或适配 Painted Flow Field。越小路径更细，但数据量和编辑成本更高。")]
    [HideInInspector, Min(0.25f)] public float flowFieldCellSize = 2f;
    [Tooltip("跟随流场方向的响应速度。越大转向越快；太大在急弯或拥挤处容易左右摆动。")]
    [HideInInspector, Min(0f)] public float flowFieldResponsiveness = 6f;
    [Tooltip("流场控制权重。0 不受流场影响，1 完全按流场期望速度推进；拥挤或摆头时可降到 0.5~0.8。")]
    [HideInInspector, Range(0f, 1f)] public float flowFieldWeight = 1f;
    [Tooltip("攻击方使用的 Painted Flow Field。旧场景继续使用这个字段作为攻击方流场。")]
    [HideInInspector] public PaintedFlowFieldAsset_Stage6 paintedFlowFieldAsset;
    [Tooltip("防守方独立流场。仅当 Defender Movement Mode 为 Use Defender Flow Field 时使用；为空会回退为原地防守。")]
    [HideInInspector] public PaintedFlowFieldAsset_Stage6 defenderPaintedFlowFieldAsset;
    [Tooltip("是否在 Inspector 显示当前攻击方流场预览。")]
    public bool showFlowFieldPreview = true;
    [Tooltip("流场预览箭头采样间隔。值越大箭头越少，Inspector 更清爽。")]
    [Min(1)] public int flowFieldPreviewStride = 2;
    [Tooltip("运行时自动根据模拟世界范围创建攻击方流场网格。Painted Flow Field 只作为初始方向模板重采样，不再决定运行时流场尺寸。")]
    [HideInInspector] public bool autoSizeRuntimeAttackerFlowField = true;
    [Tooltip("自动运行时流场在模拟世界范围外额外扩展的距离。")]
    [HideInInspector, Min(0f)] public float runtimeFlowFieldPadding = 40f;
    [Tooltip("自动运行时流场单轴最大分辨率。范围越大时会自动增大 Cell Size，避免流场图过大。")]
    [HideInInspector, Min(16)] public int runtimeFlowFieldMaxResolution = 256;
    [Tooltip("运行时流场 RenderTexture 预览模式。Flow Direction 显示速度流向；Density Target 显示战区目标强度。")]
    public RuntimeFlowPreviewMode runtimeFlowPreviewMode = RuntimeFlowPreviewMode.FlowDirection;

    [Header("Stage 5 Runtime Dynamic Attacker Flow")]
    [Tooltip("开战后根据活着的防守方分布，定时重建攻击方运行时流场。关闭后完全使用 Painted Flow Field。")]
    [HideInInspector] public bool enableRuntimeDynamicAttackerFlowField = true;
    [Tooltip("动态攻击流场更新间隔，单位秒。越小响应越快，但 GPU readback 和 CPU 生成频率越高。")]
    [HideInInspector, Min(0.1f)] public float dynamicFlowUpdateInterval = 0.5f;
    [Tooltip("把防守方阵线沿 Z 方向切成几个战区；每个仍有足够防守方的战区会成为一个流场目标源。")]
    [HideInInspector, Range(1, 8)] public int dynamicFlowSectorCount = 5;
    [Tooltip("流场格子距离动态目标小于该半径时方向归零，避免目标点附近原地打转。")]
    [HideInInspector, Min(0f)] public float dynamicFlowTargetStopRadius = 2f;
    [Tooltip("每个战区至少需要多少个活防守方才会成为流场目标。")]
    [HideInInspector, Min(1)] public int dynamicFlowMinDefendersPerTarget = 8;

    [Header("Stage 5 Runtime Dynamic Defender Flow")]
    [Tooltip("开战后根据活着的攻击方分布，定时重建防守方运行时流场。仅在 Defender Movement Mode 为 Use Defender Flow Field 时生效。")]
    [HideInInspector] public bool enableRuntimeDynamicDefenderFlowField;
    [Tooltip("运行时自动根据模拟世界范围创建防守方流场网格。Defender Painted Flow Field 只作为初始方向模板重采样，不再决定运行时流场尺寸。")]
    [HideInInspector] public bool autoSizeRuntimeDefenderFlowField = true;
    [Tooltip("自动防守方运行时流场在模拟世界范围外额外扩展的距离。")]
    [HideInInspector, Min(0f)] public float runtimeDefenderFlowFieldPadding = 40f;
    [Tooltip("自动防守方运行时流场单轴最大分辨率。范围越大时会自动增大 Cell Size，避免流场图过大。")]
    [HideInInspector, Min(16)] public int runtimeDefenderFlowFieldMaxResolution = 256;
    [Tooltip("动态防守流场更新间隔，单位秒。越小响应越快，但 GPU 生成频率越高。")]
    [HideInInspector, Min(0.1f)] public float dynamicDefenderFlowUpdateInterval = 0.5f;
    [Tooltip("把攻击方阵线沿 Z 方向切成几个战区；每个仍有足够攻击方的战区会成为防守方流场目标源。")]
    [HideInInspector, Range(1, 8)] public int dynamicDefenderFlowSectorCount = 5;
    [Tooltip("防守方流场格子距离动态目标小于该半径时方向归零，避免目标点附近原地打转。")]
    [HideInInspector, Min(0f)] public float dynamicDefenderFlowTargetStopRadius = 2f;
    [Tooltip("每个战区至少需要多少个活攻击方才会成为防守方流场目标。")]
    [HideInInspector, Min(1)] public int dynamicDefenderFlowMinAttackersPerTarget = 8;

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

    private MassGpuRuntime_Stage6 runtime;
    private MassGpuRuntime_Stage6 Runtime => runtime ??= new MassGpuRuntime_Stage6(this);
    public FlowFieldPreviewSnapshot FlowFieldPreview => Runtime.FlowFieldPreview;


    private void Start()
    {
        Runtime.Initialize();
    }

    private void Update()
    {
        runtime?.Tick();
    }

    private void OnDisable()
    {
        runtime?.Release();
        runtime = null;
    }

    public void StartBattle()
    {
        Runtime.StartBattle();
    }

    public void StopBattle()
    {
        Runtime.StopBattle();
    }

    public void ResetBattleStarted()
    {
        Runtime.ResetBattleStarted();
    }

    public void ApplyConfigAssetsToManager()
    {
        Runtime.ApplyConfigAssetsToManager();
    }

    [ContextMenu("Stage6/Rebuild Flow Field")]
    public void RebuildFlowField()
    {
        Runtime.RebuildFlowField();
    }

    [ContextMenu("Stage6/Rebuild Flow Field Preview")]
    public void RebuildFlowFieldPreview()
    {
        Runtime.RebuildFlowFieldPreview();
    }

    public bool TryApplyVatProfile(bool logWarnings = true)
    {
        return Runtime.TryApplyVatProfile(logWarnings);
    }

    public bool ApplyVatProfileToAssignedMaterials(bool logWarnings = true)
    {
        return Runtime.ApplyVatProfileToAssignedMaterials(logWarnings);
    }

    public string GetVatProfileStatus()
    {
        return Runtime.GetVatProfileStatus();
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
        Runtime.MigrateLegacyTeamSettingsIfNeeded();

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
