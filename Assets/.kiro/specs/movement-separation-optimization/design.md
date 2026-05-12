# Design Document: Movement Separation Optimization

## 概述

本设计为 Stage7 GPU 大规模单位模拟系统新增三项移动行为优化：Density Map 拥挤回避、Separation 跳帧执行、以及 Wander 微随机偏移。所有改动集中在 compute shader 层和 C# pipeline 调度层，不影响渲染管线。

---

## 架构总览

### Pipeline 调度顺序（改动后）

```
ClearGrid → BuildSpatialHash → [RuntimeFlow] → ClearDensityMap → BuildDensityMap → ClearPendingDamage → SimulateCombatAndAccumulateDamage → ClassifyVisibleAgentsByTeam
```

新增的 `ClearDensityMap` 和 `BuildDensityMap` 两个 kernel 插入在 RuntimeFlow 之后、CombatSimulation 之前。

---

## 组件设计

### 1. Density Map Compute Kernel

#### 1.1 资源声明

```hlsl
// 在 AgentDataCommon_Stage6.hlsl 中新增
RWTexture2D<uint> densityMap;       // 128×128, RenderTextureFormat.RInt, enableRandomWrite
float densityAvoidanceStrength;     // 从 FlockingConfig 传入
```

C# 端创建 RenderTexture：

```csharp
// MassGpuBufferManager_Stage7 中新增
public RenderTexture densityMapTexture; // 128×128, RenderTextureFormat.RInt, enableRandomWrite=true
```

#### 1.2 ClearDensityMap Kernel

```hlsl
#pragma kernel ClearDensityMap

[numthreads(8, 8, 1)]
void ClearDensityMap(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= (uint)flowFieldResolution.x || id.y >= (uint)flowFieldResolution.y)
        return;

    densityMap[int2(id.x, id.y)] = 0;
}
```

- Thread groups: `ceil(128/8) × ceil(128/8) = 16×16`
- Dispatch 为 2D: `Dispatch(kernel, 16, 16, 1)`

#### 1.3 BuildDensityMap Kernel

```hlsl
#pragma kernel BuildDensityMap

[numthreads(64, 1, 1)]
void BuildDensityMap(uint3 id : SV_DispatchThreadID)
{
    uint count, stride;
    agentBuffer.GetDimensions(count, stride);
    if (id.x >= count || !IsAliveIndex(id.x))
        return;

    float3 pos = agentBuffer[id.x].position;
    int2 cell = PositionToFlowFieldCell(pos);

    // 使用 InterlockedAdd 进行原子累加
    uint original;
    InterlockedAdd(densityMap[cell], 1u, original);
}
```

- Thread groups: `ceil(agentCount / 64)`
- 复用 `PositionToFlowFieldCell()` 确保与 flow field 使用相同的坐标映射

#### 1.4 Kernel 放置位置

两个 kernel 放在 `AgentCombatSimulation_Stage6.compute` 文件中（与 ClearPendingDamage 同文件），因为它们共享相同的 buffer 绑定上下文。

---

### 2. Density Avoidance in Combat Simulation

#### 2.1 梯度采样函数

在 `AgentDataCommon_Stage6.hlsl` 中新增：

```hlsl
float2 SampleDensityGradient(float3 position)
{
    int2 cell = PositionToFlowFieldCell(position);
    
    // 采样当前 cell 密度，若为 0 则无需计算梯度
    uint centerDensity = densityMap[cell];
    if (centerDensity == 0)
        return 0.0;
    
    // 采样 4 邻域（±x, ±z），边界 clamp
    int2 cellL = int2(max(0, cell.x - 1), cell.y);
    int2 cellR = int2(min(flowFieldResolution.x - 1, cell.x + 1), cell.y);
    int2 cellD = int2(cell.x, max(0, cell.y - 1));
    int2 cellU = int2(cell.x, min(flowFieldResolution.y - 1, cell.y + 1));
    
    float densityL = (float)densityMap[cellL];
    float densityR = (float)densityMap[cellR];
    float densityD = (float)densityMap[cellD];
    float densityU = (float)densityMap[cellU];
    
    // 有限差分梯度（指向密度增大方向）
    float2 gradient = float2(densityR - densityL, densityU - densityD) * 0.5;
    
    return gradient;
}

float2 ComputeDensityAvoidanceForce(float3 position)
{
    float2 gradient = SampleDensityGradient(position);
    float gradLenSqr = dot(gradient, gradient);
    if (gradLenSqr <= 0.0001)
        return 0.0;
    
    // 回避力 = 梯度反方向 × 强度
    return -normalize(gradient) * densityAvoidanceStrength;
}
```

#### 2.2 在 SimulateCombatAndAccumulateDamage 中集成

在现有的 `desiredDirection` 计算之后、velocity 更新之前叠加密度回避力：

```hlsl
// 在 desiredDirection 确定后
float2 densityAvoidance = ComputeDensityAvoidanceForce(agent.position);
desiredDirection += densityAvoidance;

// 归一化（如果需要）
float dirLenSqr = dot(desiredDirection, desiredDirection);
if (dirLenSqr > 1.0)
    desiredDirection *= rsqrt(dirLenSqr);
```

注意：密度回避力仅叠加到方向上，不修改 `flowFieldDirections` 缓冲区。

---

### 3. Separation Skip-Frame

#### 3.1 GPU 端实现

在 `AgentDataCommon_Stage6.hlsl` 中新增 uniform：

```hlsl
uint separationSkipInterval;  // 从 C# 传入，最小值 1
```

在 `SimulateCombatAndAccumulateDamage` kernel 中修改 separation 调用：

```hlsl
// 原代码：
// agent.velocity.xz += neighborhood.separation * GetSeparationStrength(id.x) * dt;

// 改为：
float2 separationForce = 0.0;
if ((frameIndex % separationSkipInterval) == 0)
{
    separationForce = neighborhood.separation;
}
agent.velocity.xz += separationForce * GetSeparationStrength(id.x) * dt;
```

注意：`QueryCombatNeighborhood` 仍然每帧执行（因为它同时负责寻找最近敌人），但 separation 结果仅在满足帧条件时使用。

#### 3.2 设计考量

- `separationSkipInterval = 1` 时行为与当前完全一致
- 跳帧时不缓存上一帧的 separation 值（直接设为零），避免额外 buffer 开销
- 由于 `QueryCombatNeighborhood` 已经同时计算了 separation 和 nearestEnemy，跳帧仅跳过 separation 的 **应用**，不跳过邻域查询本身

---

### 4. Wander Offset

#### 4.1 Hash 函数

复用现有 `FallbackDirection` 的 LCG 风格 hash，但混入 `frameIndex` 以产生时间变化：

```hlsl
float WanderAngle(uint agentId, uint frame, float maxAngleDeg)
{
    // 混合 agentId 和 frameIndex 产生伪随机种子
    uint seed = agentId * 1664525u + 1013904223u;
    seed ^= frame * 2654435761u;
    seed = seed * 1664525u + 1013904223u;
    
    // 映射到 [-1, 1] 范围
    float normalized = ((float)(seed & 0xFFFFu) / 65535.0) * 2.0 - 1.0;
    
    // 缩放到 [-maxAngleDeg, +maxAngleDeg] 并转为弧度
    return radians(normalized * maxAngleDeg);
}
```

#### 4.2 方向旋转

```hlsl
float2 ApplyWander(float2 direction, float angleRad)
{
    float cosA = cos(angleRad);
    float sinA = sin(angleRad);
    return float2(
        direction.x * cosA - direction.y * sinA,
        direction.x * sinA + direction.y * cosA
    );
}
```

#### 4.3 在 SimulateCombatAndAccumulateDamage 中集成

在 velocity 更新之前、方向确定之后施加 wander：

```hlsl
// 在 desiredDirection + densityAvoidance 计算完成后
if (agent.currentState != STATE_DEAD && dot(agent.velocity.xz, agent.velocity.xz) > 0.0001)
{
    float wanderAngleRad = WanderAngle(id.x, frameIndex, wanderMaxAngle);
    // 对最终移动方向施加微旋转
    if (dot(desiredDirection, desiredDirection) > 0.0001)
        desiredDirection = ApplyWander(desiredDirection, wanderAngleRad);
}
```

#### 4.4 GPU Uniform

```hlsl
float wanderMaxAngle;  // 单位：度，从 C# 传入
```

---

### 5. Pipeline Integration

#### 5.1 ComputePipelineOrchestrator 改动

```csharp
public void DispatchFrame(PipelineFrameContext frameContext)
{
    // ... existing setup ...
    
    DispatchSpatialHash(frameContext);
    
    if (frameContext.rebuildAttackerFlow)
        DispatchRuntimeAttackerFlow(frameContext);
    if (frameContext.rebuildDefenderFlow)
        DispatchRuntimeDefenderFlow(frameContext);
    
    // 新增：Density Map 生成
    if (frameContext.rebuildDensityMap)
        DispatchDensityMap(frameContext);
    
    DispatchCombatSimulation(frameContext);
    DispatchLodClassification(frameContext);
    buffers.SwapSimulationBuffers();
}

private void DispatchDensityMap(PipelineFrameContext context)
{
    // ClearDensityMap: 2D dispatch (16×16 groups for 128×128 texture)
    Dispatch2D(shaders.CombatSimulationShader, shaders.ClearDensityMap, 
               Mathf.Max(1, context.densityMapThreadGroupsX), 
               Mathf.Max(1, context.densityMapThreadGroupsY), 
               "ClearDensityMap");
    
    // BuildDensityMap: 1D dispatch per agent
    Dispatch(shaders.CombatSimulationShader, shaders.BuildDensityMap, 
             Mathf.Max(1, context.agentThreadGroupsX), 
             "BuildDensityMap");
}
```

#### 5.2 PipelineFrameContext 新增字段

```csharp
public bool rebuildDensityMap;          // 是否本帧重建密度图
public int densityMapThreadGroupsX;     // ceil(128/8) = 16
public int densityMapThreadGroupsY;     // ceil(128/8) = 16
```

#### 5.3 rebuildDensityMap 条件

```csharp
// 在 MassGpuSystemManager_Stage7 中
bool rebuildDensityMap = rebuildRuntimeFlowEveryFrame || rebuildAttackerFlow || rebuildDefenderFlow;
```

- 当 `rebuildRuntimeFlowEveryFrame = true` 时，每帧重建
- 当 `rebuildRuntimeFlowEveryFrame = false` 时，仅在 flow field 重建帧执行

#### 5.4 MassGpuShaderSet_Stage7 新增 Kernel 索引

```csharp
public readonly int ClearDensityMap;
public readonly int BuildDensityMap;

// 在构造函数中：
ClearDensityMap = FindKernelOrInvalid(combatSimulationShader, "ClearDensityMap");
BuildDensityMap = FindKernelOrInvalid(combatSimulationShader, "BuildDensityMap");
```

---

### 6. Config Changes

#### 6.1 FlockingConfig 新增字段

```csharp
[CreateAssetMenu(menuName = "MassGPUPhysics/Stage7/Flocking Config")]
public sealed class FlockingConfig : ScriptableObject
{
    [Min(0.01f)] public float agentRadius = 0.45f;
    [Min(0f)] public float separationStrength = 18f;
    [Min(0f)] public float attractionStrength = 1f;
    
    // 新增字段
    [Min(1)] public int separationSkipInterval = 1;
    [Range(0f, 30f)] public float wanderMaxAngle = 5f;
    [Min(0f)] public float densityAvoidanceStrength = 2f;
}
```

#### 6.2 MassGpuShaderPropertyIds_Stage7 新增

```csharp
public static readonly int SeparationSkipIntervalId = Shader.PropertyToID("separationSkipInterval");
public static readonly int WanderMaxAngleId = Shader.PropertyToID("wanderMaxAngle");
public static readonly int DensityAvoidanceStrengthId = Shader.PropertyToID("densityAvoidanceStrength");
public static readonly int DensityMapId = Shader.PropertyToID("densityMap");
```

#### 6.3 UnitTypeGpuSettings 新增字段

```csharp
public int separationSkipInterval;
public float wanderMaxAngle;
public float densityAvoidanceStrength;

// 在 FromConfig 中：
separationSkipInterval = flocking != null ? Mathf.Max(1, flocking.separationSkipInterval) : 1,
wanderMaxAngle = flocking != null ? Mathf.Clamp(flocking.wanderMaxAngle, 0f, 30f) : 5f,
densityAvoidanceStrength = flocking != null ? Mathf.Max(0f, flocking.densityAvoidanceStrength) : 2f,
```

#### 6.4 UploadFrameConstants 新增

```csharp
shaders.SetInt(SeparationSkipIntervalId, Mathf.Max(1, context.attackerSettings.separationSkipInterval));
shaders.SetFloat(WanderMaxAngleId, Mathf.Clamp(context.attackerSettings.wanderMaxAngle, 0f, 30f));
shaders.SetFloat(DensityAvoidanceStrengthId, Mathf.Max(0f, context.attackerSettings.densityAvoidanceStrength));
```

注意：当前设计中 `separationSkipInterval`、`wanderMaxAngle`、`densityAvoidanceStrength` 对 attacker 和 defender 使用相同值（取 attacker 配置）。如需分队伍独立配置，可后续扩展为 `attackerSeparationSkipInterval` / `defenderSeparationSkipInterval` 模式。

#### 6.5 Buffer 绑定

在 `BindCombatBuffers()` 中为 `SimulateCombatAndAccumulateDamage`、`ClearDensityMap`、`BuildDensityMap` 绑定 `densityMapTexture`：

```csharp
SetTexture(shaders.CombatSimulationShader, shaders.ClearDensityMap, DensityMapId, buffers.densityMapTexture);
SetTexture(shaders.CombatSimulationShader, shaders.BuildDensityMap, DensityMapId, buffers.densityMapTexture);
SetTexture(shaders.CombatSimulationShader, shaders.SimulateCombatAndAccumulateDamage, DensityMapId, buffers.densityMapTexture);
```

---

## 数据流图

```
┌─────────────────┐     ┌──────────────────┐     ┌─────────────────────────────────┐
│  SpatialHash    │────▶│  RuntimeFlow     │────▶│  DensityMap                     │
│  (positions)    │     │  (flow dirs)     │     │  ClearDensityMap → BuildDensityMap│
└─────────────────┘     └──────────────────┘     └───────────────┬─────────────────┘
                                                                  │
                                                                  ▼
                                                  ┌─────────────────────────────────┐
                                                  │  CombatSimulation               │
                                                  │  - Read densityMap (gradient)   │
                                                  │  - Skip-frame separation        │
                                                  │  - Wander offset                │
                                                  │  - Write velocity/position      │
                                                  └─────────────────────────────────┘
```

---

## 错误处理

| 场景 | 处理方式 |
|------|----------|
| `densityMapTexture` 创建失败 | 跳过 density map dispatch，`densityAvoidanceStrength` 视为 0 |
| `separationSkipInterval < 1` | C# 端 clamp 到 1，GPU 端 `max(1u, separationSkipInterval)` |
| `wanderMaxAngle` 为 0 | 不施加 wander（`WanderAngle` 返回 0） |
| Agent 位于 flow field 边界外 | `PositionToFlowFieldCell` 已有 clamp 逻辑，梯度采样安全 |
| 密度图全为 0 | `SampleDensityGradient` 在 `centerDensity == 0` 时提前返回 |

---

## Correctness Properties

*A property is a characteristic or behavior that should hold true across all valid executions of a system—essentially, a formal statement about what the system should do. Properties serve as the bridge between human-readable specifications and machine-verifiable correctness guarantees.*

### Property 1: Density Map Accumulation Correctness

*For any* set of alive agents with known positions, after executing ClearDensityMap followed by BuildDensityMap, each cell in the density map SHALL contain exactly the count of alive agents whose position maps to that cell via `PositionToFlowFieldCell`.

**Validates: Requirements 1.5, 1.6**

### Property 2: Density Gradient and Avoidance Force Correctness

*For any* density map configuration where the center cell has non-zero density, the computed density avoidance force SHALL equal `-normalize(gradient) * densityAvoidanceStrength`, where gradient is the finite-difference `(densityR - densityL, densityU - densityD) * 0.5` from the 4-neighbor sampling.

**Validates: Requirements 2.1, 2.2, 2.3, 2.4**

### Property 3: Flow Field Immutability Under Density Avoidance

*For any* computation frame that includes density avoidance force calculation, the contents of the `flowFieldDirections` buffer SHALL remain identical before and after the combat simulation kernel execution.

**Validates: Requirements 2.6**

### Property 4: Separation Skip-Frame Conditional Execution

*For any* frameIndex and separationSkipInterval (≥1), the separation force applied to an agent's velocity SHALL be non-zero (when overlapping neighbors exist) if and only if `frameIndex % separationSkipInterval == 0`.

**Validates: Requirements 3.2, 3.3**

### Property 5: Wander Hash Determinism and Bounds

*For any* (agentId, frameIndex, wanderMaxAngle) triple, the `WanderAngle` function SHALL produce a deterministic angle value within the range `[-wanderMaxAngle, +wanderMaxAngle]` degrees.

**Validates: Requirements 4.2, 4.3**

### Property 6: Wander Rotation Preserves Direction Magnitude

*For any* non-zero 2D direction vector and any wander angle, applying `ApplyWander` SHALL produce a result vector with the same magnitude as the input (within floating-point tolerance).

**Validates: Requirements 4.4**
