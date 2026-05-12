# Design Document: GPU Unit OOP Refactor (Stage7)

## Overview

本设计文档描述 MassGPUPhysics_Stage7 的 OOP 重构架构。核心目标是将 Stage6 的 `GPUInstancingManager_Stage6` 上帝类（~400 行配置字段）拆分为以兵种（UnitType）为核心的模块化体系，同时严格保持 GPU compute shader 管线的执行顺序和性能特征不退化。

### 设计原则

1. **单一职责**：每个模块类只负责一个明确的功能域
2. **数据驱动**：所有兵种参数通过 ScriptableObject 配置资产注入
3. **开闭原则**：新增兵种只需扩展，不需修改核心管线
4. **性能零退化**：GPU 管线调度顺序、AgentData 56 字节步幅、缓冲区分离策略完全保持

## Architecture

### 整体架构图

```
┌─────────────────────────────────────────────────────────────────┐
│                    MassGpuSystemManager_Stage7                    │
│              (MonoBehaviour, 场景入口, 替代上帝类)                 │
│         持有 UnitTypeRegistry + ComputePipelineOrchestrator       │
└───────────────┬─────────────────────────────────┬───────────────┘
                │                                 │
    ┌───────────▼───────────┐         ┌───────────▼───────────┐
    │   UnitTypeRegistry    │         │ ComputePipelineOrch.  │
    │  管理所有 UnitType     │         │  调度 GPU 管线        │
    │  实例的注册与生命周期   │         │  保持固定执行顺序      │
    └───────────┬───────────┘         └───────────┬───────────┘
                │                                 │
    ┌───────────▼───────────────────────────────────────────────┐
    │                    IUnitType (接口)                         │
    │  定义兵种的完整行为契约                                      │
    ├────────────────────────────────────────────────────────────┤
    │  + SpawnModule        : ISpawnModule                        │
    │  + MovementModule     : IMovementModule                    │
    │  + FlockingModule     : IFlockingModule                    │
    │  + AnimationModule    : IAnimationModule                   │
    │  + CombatModule       : ICombatModule                      │
    │  + Config             : UnitTypeConfig (ScriptableObject)  │
    └────────────────────────────────────────────────────────────┘
```

### GPU Compute Pipeline 调度顺序（不变）

```
每帧调度顺序（与 Stage6 完全一致）:
┌──────────────────────────────────────────────────────────┐
│ 1. SpatialHash                                           │
│    ├─ ClearGrid                                          │
│    └─ BuildSpatialHash                                   │
│                                                          │
│ 2. RuntimeFlow (条件触发)                                 │
│    ├─ ClearRuntimeFlowResources                          │
│    ├─ BuildRuntimeTargetDensity                          │
│    ├─ SelectRuntimeFlowTargets                           │
│    └─ GenerateRuntimeFlowField                           │
│                                                          │
│ 3. CombatSimulation                                      │
│    ├─ ClearPendingDamage                                 │
│    └─ SimulateCombatAndAccumulateDamage                   │
│                                                          │
│ 4. LodClassification                                     │
│    └─ ClassifyVisibleAgentsByTeam                         │
└──────────────────────────────────────────────────────────┘
```

## Components

### 1. MassGpuSystemManager_Stage7 (场景入口)

替代 `GPUInstancingManager_Stage6`，职责精简为：
- 持有 `UnitTypeRegistry` 和 `ComputePipelineOrchestrator`
- 提供 MonoBehaviour 生命周期钩子（Start/Update/OnDisable）
- 暴露战斗控制 API（StartBattle/StopBattle/ResetScenario）
- Inspector 上仅保留全局配置（模拟世界大小、LOD 距离、视锥剔除等）

```csharp
public sealed class MassGpuSystemManager_Stage7 : MonoBehaviour
{
    [Header("Scenario")]
    public ScenarioConfig_Stage7 scenarioConfig;

    [Header("Global Simulation")]
    public Vector2 simulationWorldSize;
    public float boundaryPadding = 2f;

    [Header("LOD")]
    public float shadowCastingRadius = 18f;
    public float midLodRadius = 75f;
    public Transform lodCenter;

    [Header("Frustum Culling")]
    public bool enableFrustumCulling = true;
    public Camera cullingCamera;
    public float cullingRadius = 2f;

    // 内部组件
    private UnitTypeRegistry unitTypeRegistry;
    private ComputePipelineOrchestrator pipelineOrchestrator;
    private MassGpuBufferManager_Stage7 bufferManager;
}
```

### 2. IUnitType 接口与 UnitTypeBase 基类

```csharp
/// <summary>
/// 兵种类型接口。每个兵种实现此接口，封装自身的完整行为。
/// </summary>
public interface IUnitType
{
    UnitTypeConfig Config { get; }
    int TeamId { get; }
    int UnitCount { get; }
    int BufferOffset { get; }

    ISpawnModule SpawnModule { get; }
    IMovementModule MovementModule { get; }
    IFlockingModule FlockingModule { get; }
    IAnimationModule AnimationModule { get; }
    ICombatModule CombatModule { get; }

    void Initialize(UnitTypeInitContext context);
    void OnBuffersBound(MassGpuBufferManager_Stage7 buffers);
    void Release();
}

/// <summary>
/// 兵种基类，提供默认模块组装逻辑。
/// </summary>
public abstract class UnitTypeBase : IUnitType
{
    public UnitTypeConfig Config { get; private set; }
    public int TeamId => Config.teamId;
    public int UnitCount => Config.spawnConfig.unitCount;
    public int BufferOffset { get; private set; }

    public ISpawnModule SpawnModule { get; protected set; }
    public IMovementModule MovementModule { get; protected set; }
    public IFlockingModule FlockingModule { get; protected set; }
    public IAnimationModule AnimationModule { get; protected set; }
    public ICombatModule CombatModule { get; protected set; }

    protected virtual void CreateModules()
    {
        SpawnModule = new DefaultSpawnModule(Config.spawnConfig);
        MovementModule = new DefaultMovementModule(Config.movementConfig);
        FlockingModule = new DefaultFlockingModule(Config.flockingConfig);
        AnimationModule = new DefaultAnimationModule(Config.animationConfig);
        CombatModule = new DefaultCombatModule(Config.combatConfig);
    }
}
```

### 3. SpawnModule

```csharp
public interface ISpawnModule
{
    SpawnConfig Config { get; }
    void GenerateAgents(AgentData[] buffer, int offset, int count);
}

public sealed class DefaultSpawnModule : ISpawnModule
{
    public SpawnConfig Config { get; }

    public DefaultSpawnModule(SpawnConfig config)
    {
        Config = config;
    }

    /// <summary>
    /// 在指定区域内生成 Agent。所有生成的 Agent 位置保证在
    /// [center - size/2, center + size/2] 范围内。
    /// </summary>
    public void GenerateAgents(AgentData[] buffer, int offset, int count)
    {
        Vector3 center = Config.spawnCenter;
        Vector3 halfSize = Config.spawnSize * 0.5f;

        for (int i = 0; i < count; i++)
        {
            buffer[offset + i] = new AgentData
            {
                position = new Vector3(
                    center.x + Random.Range(-halfSize.x, halfSize.x),
                    center.y,
                    center.z + Random.Range(-halfSize.z, halfSize.z)),
                rotation = Vector3.zero,
                scale = Vector3.one,
                velocity = Vector3.zero,
                currentState = 0, // Idle
                currentAnimationTime = 0f
            };
        }
    }
}
```

### 4. MovementModule（流场推进）

```csharp
public interface IMovementModule
{
    MovementConfig Config { get; }

    /// <summary>
    /// 计算给定 Agent 的期望速度向量。
    /// 根据流场方向和权重计算大规模推进速度。
    /// 移动中寻敌由 CombatModule 负责，不在此模块处理。
    /// </summary>
    Vector3 ComputeDesiredVelocity(Vector3 agentPosition,
        Vector2 flowDirection, float flowWeight);
}

public sealed class DefaultMovementModule : IMovementModule
{
    public MovementConfig Config { get; }

    public Vector3 ComputeDesiredVelocity(Vector3 agentPosition,
        Vector2 flowDirection, float flowWeight)
    {
        Vector3 flowVelocity = new Vector3(flowDirection.x, 0f, flowDirection.y)
            * Config.maxSpeed * flowWeight;
        return flowVelocity;
    }
}
```

### 5. FlockingModule（聚散独立模块）

```csharp
public interface IFlockingModule
{
    FlockingConfig Config { get; }

    /// <summary>
    /// 计算分离力。对于任何两个重叠的 Agent，separation 强度越大，排斥力越大。
    /// </summary>
    Vector3 ComputeSeparationForce(Vector3 agentPosition, Vector3 neighborPosition,
        float agentRadius, float separationStrength);

    /// <summary>
    /// 计算吸引力。产生指向目标的力。
    /// </summary>
    Vector3 ComputeAttractionForce(Vector3 agentPosition, Vector3 targetPosition,
        float attractionStrength);
}

public sealed class DefaultFlockingModule : IFlockingModule
{
    public FlockingConfig Config { get; }

    public Vector3 ComputeSeparationForce(Vector3 agentPosition, Vector3 neighborPosition,
        float agentRadius, float separationStrength)
    {
        Vector3 diff = agentPosition - neighborPosition;
        diff.y = 0f;
        float dist = diff.magnitude;
        float overlap = agentRadius * 2f - dist;
        if (overlap <= 0f) return Vector3.zero;
        return diff.normalized * overlap * separationStrength;
    }

    public Vector3 ComputeAttractionForce(Vector3 agentPosition, Vector3 targetPosition,
        float attractionStrength)
    {
        Vector3 toTarget = targetPosition - agentPosition;
        toTarget.y = 0f;
        if (toTarget.sqrMagnitude < 0.001f) return Vector3.zero;
        return toTarget.normalized * attractionStrength;
    }
}
```

### 6. AnimationModule

```csharp
public interface IAnimationModule
{
    AnimationConfig Config { get; }

    /// <summary>
    /// 根据状态返回对应的 VAT 片段参数（帧起始、帧数、帧率）。
    /// </summary>
    VATClipParams GetClipForState(int agentState);

    /// <summary>
    /// 推进动画时间。Dead 状态到达末帧后不再增长。
    /// </summary>
    float AdvanceAnimationTime(float currentTime, int agentState, float deltaTime, int lodLevel);
}

public struct VATClipParams
{
    public float startFrame;
    public float frameCount;
    public float frameRate;
    public bool loop;
}

public sealed class DefaultAnimationModule : IAnimationModule
{
    public AnimationConfig Config { get; }

    public VATClipParams GetClipForState(int agentState)
    {
        return agentState switch
        {
            0 => new VATClipParams { startFrame = Config.idleClipFrameRange.x,
                frameCount = Config.idleClipFrameRange.y, frameRate = Config.idleClipFrameRate, loop = true },
            1 or 2 => new VATClipParams { startFrame = Config.moveClipFrameRange.x,
                frameCount = Config.moveClipFrameRange.y, frameRate = Config.moveClipFrameRate, loop = true },
            3 => new VATClipParams { startFrame = Config.attackClipFrameRange.x,
                frameCount = Config.attackClipFrameRange.y, frameRate = Config.attackClipFrameRate, loop = true },
            4 => new VATClipParams { startFrame = Config.deathClipFrameRange.x,
                frameCount = Config.deathClipFrameRange.y, frameRate = Config.deathClipFrameRate, loop = false },
            _ => new VATClipParams { startFrame = 0, frameCount = 1, frameRate = 30, loop = true }
        };
    }

    public float AdvanceAnimationTime(float currentTime, int agentState, float deltaTime, int lodLevel)
    {
        int interval = lodLevel switch
        {
            0 => Config.nearAnimationInterval,
            1 => Config.midAnimationInterval,
            _ => Config.farAnimationInterval
        };

        // LOD 降频：仅在间隔帧推进
        float effectiveDelta = deltaTime * interval;
        VATClipParams clip = GetClipForState(agentState);
        float clipDuration = clip.frameCount / Mathf.Max(clip.frameRate, 0.001f);

        float newTime = currentTime + effectiveDelta;

        if (!clip.loop)
        {
            // Dead 状态：到达末帧后停止
            return Mathf.Min(newTime, clipDuration);
        }

        // 循环动画
        return newTime % clipDuration;
    }
}
```

### 7. CombatModule

```csharp
public interface ICombatModule
{
    CombatConfig Config { get; }

    /// <summary>
    /// 在邻域中寻找最近的有效敌方目标。
    /// 忽略同阵营和 Dead 状态的 Agent。
    /// </summary>
    int FindNearestEnemy(int agentIndex, int agentTeamId,
        SpatialHashQuery query, int[] teamIds, int[] hpValues);

    /// <summary>
    /// 判断目标是否在攻击范围内。
    /// </summary>
    bool IsInAttackRange(Vector3 agentPosition, Vector3 targetPosition, float attackRange);

    /// <summary>
    /// 计算本帧应累积的伤害值。
    /// </summary>
    int ComputeDamage(float cooldownRemaining, float attackInterval, int attackDamage);
}
```

### 8. FlowFieldVisualizer（运行时流场可视化）

```csharp
public interface IFlowFieldVisualizer
{
    bool IsEnabled { get; set; }
    FlowFieldPreviewMode PreviewMode { get; set; }

    /// <summary>
    /// 将流场数据渲染到 RenderTexture。
    /// 开关关闭时不执行任何 GPU 操作。
    /// </summary>
    void Render(ComputeBuffer flowFieldBuffer, RenderTexture target,
        int resolutionX, int resolutionZ);

    void Release();
}

public enum FlowFieldPreviewMode
{
    FlowDirection = 0,
    DensityTarget = 1
}
```

### 9. ComputePipelineOrchestrator（管线调度器）

```csharp
/// <summary>
/// GPU Compute 管线调度器。严格保持 Stage6 的调度顺序。
/// 各模块通过注册 shader 参数的方式参与管线，不改变调度逻辑。
/// </summary>
public sealed class ComputePipelineOrchestrator
{
    private readonly MassGpuShaderSet_Stage7 kernels;
    private readonly MassGpuBufferManager_Stage7 buffers;

    public void DispatchFrame(PipelineFrameContext frameContext)
    {
        // 1. SpatialHash（不变）
        DispatchSpatialHash(frameContext);

        // 2. RuntimeFlow（条件触发，不变）
        if (frameContext.rebuildAttackerFlow)
            DispatchRuntimeAttackerFlow(frameContext);
        if (frameContext.rebuildDefenderFlow)
            DispatchRuntimeDefenderFlow(frameContext);

        // 3. CombatSimulation（不变）
        DispatchCombatSimulation(frameContext);

        // 4. LodClassification（不变）
        DispatchLodClassification(frameContext);
    }
}
```

## Data Models

### AgentData（56 字节，不变）

```csharp
[StructLayout(LayoutKind.Sequential)]
public struct AgentData
{
    public Vector3 position;           // 12 bytes
    public Vector3 rotation;           // 12 bytes
    public Vector3 scale;              // 12 bytes
    public Vector3 velocity;           // 12 bytes
    public int currentState;           // 4 bytes
    public float currentAnimationTime; // 4 bytes
}
// Total: 56 bytes — 与 Stage6 完全一致
```

### ScriptableObject 配置体系

```csharp
/// <summary>
/// 兵种顶层配置。一个 UnitTypeConfig 对应一个兵种。
/// </summary>
[CreateAssetMenu(menuName = "MassGPUPhysics/Stage7/UnitTypeConfig")]
public sealed class UnitTypeConfig : ScriptableObject
{
    [Header("Identity")]
    public string unitTypeName;
    public int teamId;

    [Header("Sub-Configs")]
    public SpawnConfig spawnConfig;
    public MovementConfig movementConfig;
    public FlockingConfig flockingConfig;
    public AnimationConfig animationConfig;
    public CombatConfig combatConfig;
    public RenderConfig renderConfig;
}

[CreateAssetMenu(menuName = "MassGPUPhysics/Stage7/SpawnConfig")]
public sealed class SpawnConfig : ScriptableObject
{
    public int unitCount = 50000;
    public Vector3 spawnCenter;
    public Vector3 spawnSize = new Vector3(35f, 0f, 80f);
}

[CreateAssetMenu(menuName = "MassGPUPhysics/Stage7/MovementConfig")]
public sealed class MovementConfig : ScriptableObject
{
    public float maxSpeed = 6f;
    public float flowFieldResponsiveness = 6f;
    [Range(0f, 1f)] public float flowFieldWeight = 1f;
    public float velocityDamping = 5f;
}

[CreateAssetMenu(menuName = "MassGPUPhysics/Stage7/FlockingConfig")]
public sealed class FlockingConfig : ScriptableObject
{
    public float agentRadius = 0.45f;
    public float separationStrength = 18f;
    public float attractionStrength = 1f;
}

[CreateAssetMenu(menuName = "MassGPUPhysics/Stage7/AnimationConfig")]
public sealed class AnimationConfig : ScriptableObject
{
    public Vector2 idleClipFrameRange = new Vector2(0f, 30f);
    public Vector2 moveClipFrameRange = new Vector2(0f, 30f);
    public Vector2 attackClipFrameRange = new Vector2(0f, 30f);
    public Vector2 deathClipFrameRange = new Vector2(0f, 30f);
    public float idleClipFrameRate = 30f;
    public float moveClipFrameRate = 30f;
    public float attackClipFrameRate = 30f;
    public float deathClipFrameRate = 30f;
    public int nearAnimationInterval = 1;
    public int midAnimationInterval = 2;
    public int farAnimationInterval = 4;
}

[CreateAssetMenu(menuName = "MassGPUPhysics/Stage7/CombatConfig")]
public sealed class CombatConfig : ScriptableObject
{
    public float targetAcquireRadius = 18f;
    public float attackRange = 1.35f;
    public int attackDamage = 10;
    public float attackInterval = 0.8f;
    public int maxHp = 100;
}
```

### 状态机枚举与转换规则

```csharp
public enum AgentState
{
    Idle = 0,
    Move = 1,
    Engage = 2,
    Attack = 3,
    Dead = 4
}

/// <summary>
/// 状态优先级：Dead(4) > Attack(3) > Engage(2) > Move(1) > Idle(0)
/// 数值越大优先级越高。
/// </summary>
public static class AgentStateMachine
{
    /// <summary>
    /// 合法状态转换表。
    /// </summary>
    private static readonly Dictionary<AgentState, HashSet<AgentState>> ValidTransitions = new()
    {
        { AgentState.Idle,    new() { AgentState.Move } },
        { AgentState.Move,    new() { AgentState.Engage, AgentState.Idle } },
        { AgentState.Engage,  new() { AgentState.Attack, AgentState.Move } },
        { AgentState.Attack,  new() { AgentState.Dead, AgentState.Engage } },
        { AgentState.Dead,    new() { } } // Dead 是终态
    };

    /// <summary>
    /// 尝试状态转换。只有合法转换才会成功。
    /// </summary>
    public static bool TryTransition(AgentState current, AgentState requested, out AgentState result)
    {
        if (ValidTransitions.TryGetValue(current, out var valid) && valid.Contains(requested))
        {
            result = requested;
            return true;
        }
        result = current;
        return false;
    }

    /// <summary>
    /// 从多个并发请求中选择优先级最高的合法转换。
    /// </summary>
    public static AgentState ResolveConflict(AgentState current, params AgentState[] requests)
    {
        AgentState best = current;
        foreach (var req in requests)
        {
            if (TryTransition(current, req, out var candidate) && (int)candidate > (int)best)
                best = candidate;
        }
        return best;
    }
}
```

### Compute-Only 战斗缓冲区（与 Stage6 一致）

```csharp
// 独立于 AgentData 的 compute-only 缓冲区，不影响渲染 shader
public sealed class CombatBufferSet
{
    public ComputeBuffer teamIdBuffer;           // int per agent
    public ComputeBuffer hpBuffer;               // int per agent
    public ComputeBuffer targetAgentIndexBuffer;  // int per agent
    public ComputeBuffer attackCooldownBuffer;    // float per agent
    public ComputeBuffer homePositionBuffer;      // float3 per agent
    public ComputeBuffer pendingDamageReadBuffer; // int per agent (双缓冲)
    public ComputeBuffer pendingDamageWriteBuffer;// int per agent (双缓冲)
}
```

## Interfaces

### 模块间通信接口

```csharp
/// <summary>
/// 管线帧上下文，传递给各模块的每帧数据。
/// </summary>
public struct PipelineFrameContext
{
    public float deltaTime;
    public int totalAgentCount;
    public int agentThreadGroupsX;
    public int gridThreadGroupsX;
    public bool rebuildAttackerFlow;
    public bool rebuildDefenderFlow;
    public bool battleStarted;
    public Vector3 lodCenterPosition;
    public Vector4[] frustumPlanes;
}

/// <summary>
/// UnitType 初始化上下文。
/// </summary>
public struct UnitTypeInitContext
{
    public int bufferOffset;
    public int totalAgentCount;
    public MassGpuBufferManager_Stage7 bufferManager;
    public ComputePipelineOrchestrator pipeline;
}
```

### UnitTypeRegistry

```csharp
/// <summary>
/// 管理所有已注册的 UnitType 实例。
/// 负责分配 buffer offset 和协调初始化顺序。
/// </summary>
public sealed class UnitTypeRegistry
{
    private readonly List<IUnitType> registeredTypes = new();

    public IReadOnlyList<IUnitType> RegisteredTypes => registeredTypes;
    public int TotalAgentCount => registeredTypes.Sum(t => t.UnitCount);

    public void Register(IUnitType unitType) { ... }
    public void InitializeAll(MassGpuBufferManager_Stage7 buffers, ComputePipelineOrchestrator pipeline) { ... }
    public void ReleaseAll() { ... }
}
```

## Error Handling

### 配置验证

```csharp
public static class ConfigValidator
{
    /// <summary>
    /// 验证 UnitTypeConfig 的完整性。
    /// 缺失子配置时记录警告并使用默认值。
    /// </summary>
    public static ValidationResult Validate(UnitTypeConfig config)
    {
        var result = new ValidationResult();

        if (config.spawnConfig == null)
            result.AddWarning("SpawnConfig is null, using defaults");
        if (config.combatConfig == null)
            result.AddWarning("CombatConfig is null, using defaults");
        if (config.spawnConfig != null && config.spawnConfig.unitCount <= 0)
            result.AddError("unitCount must be > 0");

        return result;
    }
}
```

### 缓冲区生命周期

- 所有 ComputeBuffer 在 `MassGpuBufferManager_Stage7.ReleaseAll()` 中统一释放
- `OnDisable` 时调用 Release，防止内存泄漏
- 双缓冲 swap 操作在每帧 Dispatch 前执行，确保读写一致性

### GPU 管线错误恢复

- Compute Shader 引用为 null 时，跳过对应 Dispatch 并记录错误日志
- Buffer 大小不匹配时（如 instanceCount 变化），触发完整重建流程
- 流场资产缺失时，回退为直接寻路模式

## Correctness Properties

*A property is a characteristic or behavior that should hold true across all valid executions of a system—essentially, a formal statement about what the system should do. Properties serve as the bridge between human-readable specifications and machine-verifiable correctness guarantees.*

### Property 1: AgentData 步幅不变量

*For any* 构建配置，`AgentData` 结构体的 `Marshal.SizeOf` 应始终返回 56 字节，与 Stage6 保持完全一致。

**Validates: Requirements 9.2**

### Property 2: 公共配置字段上限

*For any* Stage7 系统中的模块类，其公共配置字段数量不超过 30 个。

**Validates: Requirements 1.3**

### Property 3: 生成区域包含性

*For any* 有效的 SpawnConfig（中心点 center、范围 size、数量 count），SpawnModule 生成的所有 Agent 位置 p 应满足：`center.x - size.x/2 ≤ p.x ≤ center.x + size.x/2` 且 `center.z - size.z/2 ≤ p.z ≤ center.z + size.z/2`，且生成数量恰好等于 count。

**Validates: Requirements 2.2**

### Property 4: 流场导航速度方向一致性

*For any* Agent 位置和非零流场方向向量 flowDir，MovementModule 计算出的期望速度在 XZ 平面上的方向应与 flowDir 方向一致（点积 > 0）。

**Validates: Requirements 4.2, 4.3**

### Property 5: 流场权重比例正确性

*For any* 非零流场方向向量 flowDir 和两个权重值 w1 < w2（均在 (0,1] 范围内），使用 w2 计算的期望速度大小应大于等于使用 w1 计算的期望速度大小。

**Validates: Requirements 4.3, 4.4**

### Property 6: 分离力单调性

*For any* 两个重叠的 Agent（距离 < 2 * agentRadius），separationStrength 值 s1 < s2 时，使用 s2 计算的分离力大小应大于等于使用 s1 计算的分离力大小。

**Validates: Requirements 5.2**

### Property 7: 吸引力方向正确性

*For any* Agent 位置 A 和目标位置 T（A ≠ T），FlockingModule 计算的吸引力方向应指向目标（与 T-A 的点积 > 0）。

**Validates: Requirements 5.3**

### Property 8: 状态-动画映射正确性

*For any* 有效的 AgentState 值（0-4），AnimationModule.GetClipForState 应返回对应状态的 VAT 片段参数，且 Dead 状态的 clip.loop 为 false，其余状态为 true。

**Validates: Requirements 6.1, 6.2**

### Property 9: Dead 动画时间上界

*For any* 处于 Dead 状态的 Agent，无论经过多少帧的 AdvanceAnimationTime 调用，动画时间不应超过死亡片段的总时长（frameCount / frameRate）。

**Validates: Requirements 6.4**

### Property 10: 寻敌排除不变量（Dead 与同阵营）

*For any* 寻敌操作的结果 targetIndex，如果 targetIndex ≥ 0，则该目标的 teamId 必须不等于发起者的 teamId，且该目标的 HP 必须 > 0（即不是 Dead 状态）。

**Validates: Requirements 7.2, 10.4**

### Property 11: 伤害累积线性正确性

*For any* 处于攻击范围内的 Agent 对，经过 N 次完整攻击冷却周期后，目标累积的 pendingDamage 应等于 N × attackDamage。

**Validates: Requirements 7.3**

### Property 12: HP 归零触发死亡

*For any* Agent，当其 HP 在伤害结算后降至 ≤ 0 时，其 currentState 应被设置为 Dead(4)。

**Validates: Requirements 7.5**

### Property 13: 状态转换合法性

*For any* 当前状态 S 和请求状态 R，`TryTransition(S, R)` 成功当且仅当 R 在 S 的合法转换集合中。Dead 状态不允许任何转出转换。

**Validates: Requirements 10.2**

### Property 14: 并发状态请求优先级决定

*For any* 当前状态 S 和一组并发请求 {R1, R2, ...Rn}，`ResolveConflict` 的结果应等于所有合法转换中数值最大（优先级最高）的状态。

**Validates: Requirements 10.3**

