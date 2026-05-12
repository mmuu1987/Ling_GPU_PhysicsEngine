# 实现计划：Movement Separation Optimization

## 概述

本计划为 Stage7 GPU 大规模单位模拟系统实现三项移动行为优化：Density Map 拥挤回避、Separation 跳帧执行、以及 Wander 微随机偏移。改动涵盖 C# pipeline/config 层和 HLSL compute shader，遵循现有架构模式。

## Tasks

- [ ] 1. 配置与 Shader Property ID 新增
  - [ ] 1.1 在 FlockingConfig 中添加新字段
    - 添加 `separationSkipInterval`（int，[Min(1)]，默认值 1）
    - 添加 `wanderMaxAngle`（float，[Range(0f, 30f)]，默认值 5）
    - 添加 `densityAvoidanceStrength`（float，[Min(0f)]，默认值 2）
    - 文件：`Scripts/Config/FlockingConfig.cs`
    - _Requirements: 5.1, 5.2, 5.3_

  - [ ] 1.2 添加新的 Shader Property ID
    - 添加 `SeparationSkipIntervalId` → `"separationSkipInterval"`
    - 添加 `WanderMaxAngleId` → `"wanderMaxAngle"`
    - 添加 `DensityAvoidanceStrengthId` → `"densityAvoidanceStrength"`
    - 添加 `DensityMapId` → `"densityMap"`
    - 文件：`Scripts/Pipeline/MassGpuShaderPropertyIds_Stage7.cs`
    - _Requirements: 5.4, 5.5_

  - [ ] 1.3 在 UnitTypeGpuSettings 中添加新字段并更新 FromConfig
    - 添加 `separationSkipInterval`、`wanderMaxAngle`、`densityAvoidanceStrength` 字段
    - 在 `FromConfig` 中从 FlockingConfig 映射并做适当 clamp
    - 文件：`Scripts/Core/Stage7Contexts.cs`
    - _Requirements: 5.1, 5.2, 5.3, 5.4_

- [ ] 2. Buffer 分配与 Kernel 注册
  - [ ] 2.1 在 MassGpuBufferManager_Stage7 中创建 densityMapTexture
    - 添加 `public RenderTexture densityMapTexture` 字段
    - 创建 128×128 RenderTexture，格式 `RenderTextureFormat.RInt`，`enableRandomWrite = true`
    - 在 `Allocate()` 方法中分配
    - 在 `ReleaseAll()` 方法中释放
    - 文件：`Scripts/Pipeline/MassGpuBufferManager_Stage7.cs`
    - _Requirements: 1.4_

  - [ ] 2.2 在 MassGpuShaderSet_Stage7 中注册 ClearDensityMap 和 BuildDensityMap kernel 索引
    - 添加 `public readonly int ClearDensityMap` 和 `public readonly int BuildDensityMap` 字段
    - 通过 `FindKernelOrInvalid(combatSimulationShader, "ClearDensityMap")` 和 `FindKernelOrInvalid(combatSimulationShader, "BuildDensityMap")` 初始化
    - 文件：`Scripts/Pipeline/MassGpuShaderSet_Stage7.cs`
    - _Requirements: 1.1_

  - [ ] 2.3 在 PipelineFrameContext 中添加密度图调度字段
    - 添加 `rebuildDensityMap`（bool）、`densityMapThreadGroupsX`（int）、`densityMapThreadGroupsY`（int）
    - 文件：`Scripts/Core/Stage7Contexts.cs`
    - _Requirements: 1.2, 1.3_

- [ ] 3. Compute Shader Kernel 实现
  - [ ] 3.1 在 AgentDataCommon_Stage6.hlsl 中添加 HLSL uniform 声明
    - 添加 `RWTexture2D<uint> densityMap;`
    - 添加 `float densityAvoidanceStrength;`
    - 添加 `uint separationSkipInterval;`
    - 添加 `float wanderMaxAngle;`
    - 文件：`Shaders/AgentDataCommon_Stage6.hlsl`
    - _Requirements: 5.5_

  - [ ] 3.2 在 AgentCombatSimulation_Stage6.compute 中实现 ClearDensityMap kernel
    - 添加 `#pragma kernel ClearDensityMap`
    - 实现 `[numthreads(8, 8, 1)]` kernel，使用 `flowFieldResolution` 做边界检查后将所有 cell 清零
    - 文件：`Shaders/AgentCombatSimulation_Stage6.compute`
    - _Requirements: 1.6_

  - [ ] 3.3 在 AgentCombatSimulation_Stage6.compute 中实现 BuildDensityMap kernel
    - 添加 `#pragma kernel BuildDensityMap`
    - 实现 `[numthreads(64, 1, 1)]` kernel，遍历存活 agent 并对密度图 cell 执行 `InterlockedAdd`
    - 复用 `PositionToFlowFieldCell()` 进行坐标映射
    - 文件：`Shaders/AgentCombatSimulation_Stage6.compute`
    - _Requirements: 1.4, 1.5_

  - [ ] 3.4 在 AgentDataCommon_Stage6.hlsl 中实现 SampleDensityGradient 和 ComputeDensityAvoidanceForce
    - 实现 4 邻域有限差分梯度采样，边界 clamp
    - 当中心 cell 密度为 0 时提前返回零向量
    - 计算回避力为 `-normalize(gradient) * densityAvoidanceStrength`
    - 文件：`Shaders/AgentDataCommon_Stage6.hlsl`
    - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5_

  - [ ] 3.5 在 SimulateCombatAndAccumulateDamage 中实现 separation 跳帧逻辑
    - 用 `if ((frameIndex % separationSkipInterval) == 0)` 条件包裹 separation force 应用
    - 非匹配帧将 separation force 设为零
    - 保持 `QueryCombatNeighborhood` 每帧执行（用于最近敌人检测）
    - 文件：`Shaders/AgentCombatSimulation_Stage6.compute`
    - _Requirements: 3.1, 3.2, 3.3, 3.4_

  - [ ] 3.6 在 AgentDataCommon_Stage6.hlsl 中实现 WanderAngle 和 ApplyWander 函数
    - 实现 LCG 风格 hash，混合 agentId 和 frameIndex
    - 将 hash 输出映射到 `[-wanderMaxAngle, +wanderMaxAngle]` 度并转为弧度
    - 实现 cos/sin 矩阵的 2D 旋转
    - 文件：`Shaders/AgentDataCommon_Stage6.hlsl`
    - _Requirements: 4.2, 4.3, 4.4_

  - [ ] 3.7 在 SimulateCombatAndAccumulateDamage kernel 中集成密度回避和 wander
    - 在 `desiredDirection` 计算后叠加 `ComputeDensityAvoidanceForce` 结果
    - 在密度回避后，对存活且速度非零的 agent 施加 `ApplyWander`
    - 跳过 STATE_DEAD agent 和零速度 agent 的 wander
    - 确保不写入 `flowFieldDirections` 缓冲区
    - 文件：`Shaders/AgentCombatSimulation_Stage6.compute`
    - _Requirements: 2.1, 2.6, 4.1, 4.5, 4.6_

- [ ] 4. 检查点 - 验证 Shader 编译
  - 确保所有 shader 编译无错误，如有问题请询问用户。

- [ ] 5. Pipeline Orchestrator 集成
  - [ ] 5.1 在 ComputePipelineOrchestrator 中添加 DispatchDensityMap 方法
    - 实现 `DispatchDensityMap(PipelineFrameContext)` 方法
    - ClearDensityMap 使用 2D dispatch（16×16 groups 对应 128×128 纹理）
    - BuildDensityMap 使用 1D dispatch（agentThreadGroupsX）
    - 添加 `Dispatch2D` 辅助方法（或内联 2D dispatch 调用）
    - 文件：`Scripts/Pipeline/ComputePipelineOrchestrator.cs`
    - _Requirements: 1.1_

  - [ ] 5.2 将 DispatchDensityMap 接入 DispatchFrame 并绑定 buffer
    - 在 runtime flow dispatch 之后、`DispatchCombatSimulation` 之前调用 `DispatchDensityMap`，受 `frameContext.rebuildDensityMap` 控制
    - 在 `BindCombatBuffers()` 中为 ClearDensityMap、BuildDensityMap、SimulateCombatAndAccumulateDamage 绑定 `densityMapTexture`
    - 文件：`Scripts/Pipeline/ComputePipelineOrchestrator.cs`
    - _Requirements: 1.1, 1.2, 1.3_

  - [ ] 5.3 在 UploadFrameConstants 中上传新参数
    - 添加 `SetInt(SeparationSkipIntervalId, ...)`、`SetFloat(WanderMaxAngleId, ...)`、`SetFloat(DensityAvoidanceStrengthId, ...)`
    - 使用 attacker settings 值并做适当 clamp
    - 文件：`Scripts/Pipeline/ComputePipelineOrchestrator.cs`
    - _Requirements: 5.4_

  - [ ] 5.4 在 MassGpuSystemManager_Stage7 中设置 rebuildDensityMap
    - 计算 `rebuildDensityMap = rebuildRuntimeFlowEveryFrame || rebuildAttackerFlow || rebuildDefenderFlow`
    - 设置 `densityMapThreadGroupsX = 16`、`densityMapThreadGroupsY = 16`（ceil(128/8)）
    - 文件：`Scripts/MassGpuSystemManager_Stage7.cs`
    - _Requirements: 1.2, 1.3_

- [ ] 6. 检查点 - 验证 C# 编译
  - 确保所有 C# 脚本编译无错误，如有问题请询问用户。

- [ ] 7. 测试与验证
  - [ ]* 7.1 编写密度图累加正确性的 property test
    - **Property 1: Density Map Accumulation Correctness**
    - **Validates: Requirements 1.5, 1.6**
    - 测试对于已知 agent 位置集合，CPU 端等价的 BuildDensityMap 逻辑产生正确的每 cell 计数
    - 文件：`Tests/EditMode/Stage7PropertyTests.cs`

  - [ ]* 7.2 编写密度梯度与回避力的 property test
    - **Property 2: Density Gradient and Avoidance Force Correctness**
    - **Validates: Requirements 2.1, 2.2, 2.3, 2.4**
    - 测试梯度计算匹配有限差分公式，力方向为梯度反方向

  - [ ]* 7.3 编写 separation 跳帧条件执行的 property test
    - **Property 4: Separation Skip-Frame Conditional Execution**
    - **Validates: Requirements 3.2, 3.3**
    - 测试 separation force 仅在 `frameIndex % skipInterval == 0` 时非零

  - [ ]* 7.4 编写 wander hash 确定性与边界的 property test
    - **Property 5: Wander Hash Determinism and Bounds**
    - **Validates: Requirements 4.2, 4.3**
    - 测试 WanderAngle 对任意 (agentId, frameIndex) 对产生确定性结果且在 `[-maxAngle, +maxAngle]` 范围内

  - [ ]* 7.5 编写 wander 旋转保持方向幅值的 property test
    - **Property 6: Wander Rotation Preserves Direction Magnitude**
    - **Validates: Requirements 4.4**
    - 测试 ApplyWander 输出幅值等于输入幅值（浮点容差内）

  - [ ]* 7.6 编写 FlockingConfig 新字段默认值与 clamp 的 unit test
    - 验证 `separationSkipInterval` 默认值为 1，clamp 最小值 1
    - 验证 `wanderMaxAngle` 默认值为 5，clamp 范围 [0, 30]
    - 验证 `densityAvoidanceStrength` 默认值为 2，clamp 最小值 0
    - _Requirements: 5.1, 5.2, 5.3_

- [ ] 8. 最终检查点 - 确保所有测试通过
  - 确保所有测试通过，如有问题请询问用户。

## Notes

- 标记 `*` 的任务为可选项，可跳过以加速 MVP 交付
- 每个任务引用具体 requirements 以确保可追溯性
- 检查点确保 shader 和 C# 编译后的增量验证
- Property tests 验证设计文档中的通用正确性属性
- 密度图使用与现有 flow field 相同的坐标系（128×128，共享 `PositionToFlowFieldCell`）
- `separationSkipInterval`、`wanderMaxAngle`、`densityAvoidanceStrength` 当前对 attacker 和 defender 使用相同值（取 attacker 配置）；后续可扩展为分队伍独立配置

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1", "1.2"] },
    { "id": 1, "tasks": ["1.3", "2.1", "2.2", "2.3"] },
    { "id": 2, "tasks": ["3.1"] },
    { "id": 3, "tasks": ["3.2", "3.3", "3.4", "3.5", "3.6"] },
    { "id": 4, "tasks": ["3.7"] },
    { "id": 5, "tasks": ["5.1"] },
    { "id": 6, "tasks": ["5.2", "5.3", "5.4"] },
    { "id": 7, "tasks": ["7.1", "7.2", "7.3", "7.4", "7.5", "7.6"] }
  ]
}
```
