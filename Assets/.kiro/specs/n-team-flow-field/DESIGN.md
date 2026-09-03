# N 阵营 × N 流场系统 - 设计文档

**状态**: 设计阶段  
**复杂度**: 高（引擎层架构改动）  
**预计工作量**: 3-4 小时  
**创建时间**: 2026-09-02

---

## 目标

突破当前双队限制（teamId 只允许 0/1），支持 N 个阵营的混战场景，每个阵营维护独立的流场。

---

## 当前架构约束（硬编码双队）

### 1. 配置层
- **ConfigValidator.cs:47-48**: `teamId != 0 && teamId != 1` 报错
- **影响**: 注册期阻止非 0/1 的 teamId

### 2. GPU 缓冲层
- **MassGpuBufferManager.cs:24**: `public ComputeBuffer flowFieldDirectionsBuffer`
- **单一缓冲**: 128×128 × 2 float（仅一张流场）
- **影响**: 无法容纳多张流场

### 3. Compute Shader 层
- **AgentDataCommon.hlsl:91**: `RWStructuredBuffer<float2> flowFieldDirections`
- **单一数组**: 所有队伍共享同一张流场数组
- **AgentRuntimeFlow.compute:1-8**: 硬编码 `Attacker` / `Defender` 两组 kernel（8 个）
- **影响**: kernel 名称绑死两队，无法泛化

### 4. 管线调度层
- **ComputePipelineOrchestrator.cs:46-49**: 硬编码两次 `DispatchRuntimeAttackerFlow` / `DispatchRuntimeDefenderFlow`
- **影响**: 调度逻辑无法扩展到 N 队

### 5. 常量上传
- **ComputePipelineOrchestrator.cs:152-153**: `attackerTeamId` / `defenderTeamId` 两个独立 uniform
- **影响**: 无法上传 N 个 teamId 数组

---

## 设计方案

### 核心思路

**数组化 + 泛化 kernel + 动态调度**

1. **流场缓冲数组化**: `flowFieldDirections[teamCount][cellCount]`（逻辑上二维，物理上一维连续）
2. **泛化 kernel**: 移除 `Attacker`/`Defender` 后缀，改为传入 `teamIndex` 参数
3. **动态调度**: `for (int teamIndex = 0; teamIndex < teamCount; teamIndex++)` 循环派发
4. **配置解禁**: 移除 `teamId != 0 && teamId != 1` 限制，允许 0~7（上限 8，可配置）

---

## 实现计划

### 阶段 1：配置层解禁（低风险）

**文件**: `MassEngine/UnitTypes/ConfigValidator.cs`

```csharp
// 修改前
if (config.teamId != 0 && config.teamId != 1)
    result.AddError("teamId must be 0 (attacker) or 1 (defender); ...");

// 修改后
const int MaxTeamCount = 8; // 可配置常量
if (config.teamId < 0 || config.teamId >= MaxTeamCount)
    result.AddError($"teamId must be in range [0, {MaxTeamCount}); ...");
```

**影响**: 仅配置验证，不触及运行时

---

### 阶段 2：GPU 缓冲数组化（中风险）

**文件**: `MassEngine/Core/MassGpuBufferManager.cs`

```csharp
// 新增字段
public int teamCount = 2; // 可配置，默认保持双队兼容

// 修改缓冲分配
public ComputeBuffer flowFieldDirectionsBuffer; // 保持名称，容量×teamCount
// Allocate()
int cellCount = flowFieldResolution * flowFieldResolution;
flowFieldDirectionsBuffer = new ComputeBuffer(
    cellCount * teamCount, // 容量扩展
    sizeof(float) * 2
);
```

**兼容性**: 现有双队场景 `teamCount = 2` 下内存布局不变

---

### 阶段 3：Shader 泛化 kernel（高风险）

**文件**: `MassEngine/FlowField/Shaders/AgentRuntimeFlow.compute`

#### 3.1 泛化 kernel 签名

```hlsl
// 移除前（8 个硬编码 kernel）
#pragma kernel ClearRuntimeAttackerFlowResources
#pragma kernel BuildRuntimeAttackerTargetDensity
// ...

// 移除后（4 个泛化 kernel）
#pragma kernel ClearRuntimeFlowResources
#pragma kernel BuildRuntimeTargetDensity
#pragma kernel SelectRuntimeFlowTargets
#pragma kernel GenerateRuntimeFlowField
```

#### 3.2 添加 uniform

```hlsl
int activeTeamIndex; // 当前处理的队伍索引（C# 每次派发前上传）
int teamCount;       // 总队伍数
```

#### 3.3 数组化全局缓冲

```hlsl
// AgentDataCommon.hlsl
RWStructuredBuffer<float2> flowFieldDirections; // 容量 = cellCount * teamCount
RWStructuredBuffer<uint> runtimeTargetDensity;  // 容量 = cellCount * teamCount
RWStructuredBuffer<int> runtimeFlowStats;       // 容量 = 4 * teamCount
RWStructuredBuffer<float2> runtimeFlowTargets;  // 容量 = 8 * teamCount
```

#### 3.4 索引计算辅助函数

```hlsl
// 获取当前队伍的流场数组偏移
uint GetTeamFlowFieldOffset(int teamIndex)
{
    return teamIndex * FlowFieldCellCount();
}

uint GetTeamFlowStatsOffset(int teamIndex)
{
    return teamIndex * 4;
}

uint GetTeamFlowTargetsOffset(int teamIndex)
{
    return teamIndex * 8;
}

// 读取指定队伍的流场方向
float2 SampleFlowFieldForTeam(float2 position, int teamIndex)
{
    uint baseOffset = GetTeamFlowFieldOffset(teamIndex);
    int2 cell = PositionToFlowFieldCell(position);
    uint cellIndex = FlowFieldCellToIndex(cell);
    return flowFieldDirections[baseOffset + cellIndex];
}
```

#### 3.5 重写 kernel 主体

```hlsl
[numthreads(64, 1, 1)]
void ClearRuntimeFlowResources(uint3 id : SV_DispatchThreadID)
{
    uint cellCount = FlowFieldCellCount();
    uint baseOffset = GetTeamFlowFieldOffset(activeTeamIndex);
    
    if (id.x < cellCount)
        runtimeTargetDensity[baseOffset + id.x] = 0;
    
    if (id.x == 0)
    {
        uint statsOffset = GetTeamFlowStatsOffset(activeTeamIndex);
        runtimeFlowStats[statsOffset + 0] = 0;
        runtimeFlowStats[statsOffset + 1] = 0;
        runtimeFlowStats[statsOffset + 2] = 0;
        runtimeFlowStats[statsOffset + 3] = 0;
    }
    
    if (id.x < 8)
    {
        uint targetsOffset = GetTeamFlowTargetsOffset(activeTeamIndex);
        runtimeFlowTargets[targetsOffset + id.x] = float2(0, 0);
    }
}

[numthreads(64, 1, 1)]
void BuildRuntimeTargetDensity(uint3 id : SV_DispatchThreadID)
{
    // 检查动态流场是否启用（需按队伍独立配置，暂时用全局）
    if (!runtimeDynamicFlowEnabled || !enableTwoTeamCombat || !battleStarted)
        return;
    
    uint count, stride;
    agentBuffer.GetDimensions(count, stride);
    
    // 当前队伍的目标 = 所有非本队的存活单位
    if (id.x >= count || teamIdReadBuffer[id.x] == activeTeamIndex || !IsAliveIndex(id.x))
        return;
    
    float2 position = agentPositionReadBuffer[id.x];
    int2 cell = PositionXzToFlowFieldCell(position);
    uint cellIndex = FlowFieldCellToIndex(cell);
    uint baseOffset = GetTeamFlowFieldOffset(activeTeamIndex);
    
    uint ignoredDensity;
    InterlockedAdd(runtimeTargetDensity[baseOffset + cellIndex], 1u, ignoredDensity);
    
    // 累积全局质心（fallback）
    uint statsOffset = GetTeamFlowStatsOffset(activeTeamIndex);
    int ignoredStats;
    InterlockedAdd(runtimeFlowStats[statsOffset + 0], 1, ignoredStats);
    InterlockedAdd(runtimeFlowStats[statsOffset + 1], (int)round(position.x), ignoredStats);
    InterlockedAdd(runtimeFlowStats[statsOffset + 2], (int)round(position.y), ignoredStats);
}
```

---

### 阶段 4：管线调度泛化（中风险）

**文件**: `MassEngine/Core/ComputePipelineOrchestrator.cs`

```csharp
// 移除硬编码双队派发
// 删除: DispatchRuntimeAttackerFlow() / DispatchRuntimeDefenderFlow()

// 新增泛化派发
private void DispatchRuntimeFlowForTeam(int teamIndex, PipelineFrameContext context)
{
    var teamFlow = context.GetTeamFlowContext(teamIndex); // 需扩展 PipelineFrameContext
    if (!teamFlow.rebuildThisFrame)
        return;
    
    int flowGroups = Mathf.Max(1, teamFlow.threadGroupsX);
    
    // 上传当前队伍索引
    shaders.SetInt(ActiveTeamIndexId, teamIndex);
    
    Dispatch(shaders.RuntimeFlowShader, shaders.ClearRuntimeFlowResources, 
        flowGroups, "ClearRuntimeFlowResources[" + teamIndex + "]");
    Dispatch(shaders.RuntimeFlowShader, shaders.BuildRuntimeTargetDensity, 
        Mathf.Max(1, context.agentThreadGroupsX), "BuildRuntimeTargetDensity[" + teamIndex + "]");
    Dispatch(shaders.RuntimeFlowShader, shaders.SelectRuntimeFlowTargets, 
        Mathf.Clamp(teamFlow.sectorCount, 1, 8), "SelectRuntimeFlowTargets[" + teamIndex + "]");
    Dispatch(shaders.RuntimeFlowShader, shaders.GenerateRuntimeFlowField, 
        flowGroups, "GenerateRuntimeFlowField[" + teamIndex + "]");
}

// DispatchFrame() 中循环调用
public void DispatchFrame(PipelineFrameContext frameContext)
{
    // ...
    DispatchSpatialHash(frameContext);
    
    // 泛化流场派发
    for (int teamIndex = 0; teamIndex < frameContext.teamCount; teamIndex++)
        DispatchRuntimeFlowForTeam(teamIndex, frameContext);
    
    // ...
}
```

---

### 阶段 5：PipelineFrameContext 扩展（中风险）

**文件**: `MassEngine/Core/PipelineContexts.cs`

```csharp
public struct TeamFlowContext
{
    public bool rebuildThisFrame;
    public int threadGroupsX;
    public int sectorCount;
    public bool dynamicFlowEnabled;
}

public struct PipelineFrameContext
{
    // 移除
    // public TeamFlowContext attackerFlow;
    // public TeamFlowContext defenderFlow;
    
    // 新增
    public int teamCount;
    public TeamFlowContext[] teamFlowContexts; // 长度 = teamCount
    
    public TeamFlowContext GetTeamFlowContext(int teamIndex)
    {
        if (teamFlowContexts == null || teamIndex < 0 || teamIndex >= teamFlowContexts.Length)
            return default;
        return teamFlowContexts[teamIndex];
    }
    
    // 兼容性访问器（可选，便于渐进迁移）
    public TeamFlowContext attackerFlow => GetTeamFlowContext(0);
    public TeamFlowContext defenderFlow => GetTeamFlowContext(1);
}
```

---

### 阶段 6：Shader Property IDs 扩展

**文件**: `MassEngine/Core/MassGpuShaderPropertyIds.cs`

```csharp
// 新增
public static readonly int ActiveTeamIndexId = Shader.PropertyToID("activeTeamIndex");
public static readonly int TeamCountId = Shader.PropertyToID("teamCount");

// 保留（兼容性，运行时仍可能需要标识"进攻方"）
public static readonly int AttackerTeamIdId = Shader.PropertyToID("attackerTeamId");
public static readonly int DefenderTeamIdId = Shader.PropertyToID("defenderTeamId");
```

---

### 阶段 7：MassGpuShaderSet 扩展

**文件**: `MassEngine/Core/MassGpuShaderSet.cs`

```csharp
// 移除旧 kernel 索引
// public readonly int ClearRuntimeAttackerFlowResources;
// public readonly int BuildRuntimeAttackerTargetDensity;
// ...

// 新增泛化 kernel 索引
public readonly int ClearRuntimeFlowResources;
public readonly int BuildRuntimeTargetDensity;
public readonly int SelectRuntimeFlowTargets;
public readonly int GenerateRuntimeFlowField;

// 构造函数中
ClearRuntimeFlowResources = FindKernelOrInvalid(runtimeFlowShader, "ClearRuntimeFlowResources");
BuildRuntimeTargetDensity = FindKernelOrInvalid(runtimeFlowShader, "BuildRuntimeTargetDensity");
SelectRuntimeFlowTargets = FindKernelOrInvalid(runtimeFlowShader, "SelectRuntimeFlowTargets");
GenerateRuntimeFlowField = FindKernelOrInvalid(runtimeFlowShader, "GenerateRuntimeFlowField");
```

---

### 阶段 8：战斗 Simulation 适配

**文件**: `MassEngine/Core/Shaders/AgentDataCommon.hlsl`

```hlsl
// 当前（硬编码访问单一流场）
float2 SampleFlowField(float2 position)
{
    float2 direction = flowFieldDirections[FlowFieldCellToIndex(PositionToFlowFieldCell(position))];
    return direction;
}

// 修改后（按 teamId 读取对应流场）
float2 SampleFlowField(float2 position, int teamIndex)
{
    return SampleFlowFieldForTeam(position, teamIndex);
}

// 在 SimulateCombatAndAccumulateDamage 中
int myTeamId = teamIdReadBuffer[id.x];
float2 flowDirection = SampleFlowField(position, myTeamId);
```

---

## 测试策略

### 1. EditMode 测试
- **缓冲容量**: 断言 `flowFieldDirectionsBuffer.count == cellCount * teamCount`
- **kernel 存在性**: 断言新 kernel 索引 >= 0
- **派发顺序**: 断言 N 队时 `ClearRuntimeFlowResources[0..N-1]` 按序调用

### 2. PlayMode 测试
- **三队混战场景**: 0 号队 vs 1 号队 vs 2 号队，各 100 单位
- **流场独立性**: 断言每队朝各自配置目标移动，不互相干扰
- **性能基线**: 3 队场景帧时间 ≤ 2 队场景 × 1.5

### 3. 兼容性测试
- **现有双队场景**: 所有 PlayMode 测试在 `teamCount = 2` 下零改动通过

---

## 风险与缓解

### 风险 1：内存开销线性增长
- **影响**: 8 队时流场缓冲 × 8
- **缓解**: 
  - 默认保持 `teamCount = 2`
  - 流场分辨率可按队伍数动态降级（8 队时 128→64）
  - 惰性分配：只为有单位的队伍分配流场

### 风险 2：GPU 调度开销
- **影响**: 8 队时每帧 32 次 dispatch（4 kernel × 8 队）
- **缓解**:
  - 节流：队伍流场仍按 0.35s 间隔重建
  - 批处理：考虑改为单 kernel + indirect dispatch（后续优化）

### 风险 3：现有测试失效
- **影响**: 硬编码 `attackerTeamId = 0` / `defenderTeamId = 1` 的测试
- **缓解**:
  - 保留兼容性访问器 `attackerFlow` / `defenderFlow`
  - 渐进迁移：先让新旧 API 共存

---

## 后续扩展

1. **N 队战术 AI**: 每队独立的进攻/防守策略
2. **动态结盟**: 运行时修改敌友关系（需额外的敌对矩阵）
3. **流场共享**: 盟友共用一张流场（减少内存/计算）

---

## 参考

- [MassEngine/README.md](../../MassEngine/README.md) - 已知边界清单
- [MassEngine/FlowField/README.md](../../MassEngine/FlowField/README.md) - 双队流场文档
- [MassEngine/Core/README.md](../../MassEngine/Core/README.md) - 管线调度规格
