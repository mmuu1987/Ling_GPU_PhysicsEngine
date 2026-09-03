# 实现计划：Movement Separation Optimization

## 概述

本计划为 Stage7 GPU 大规模单位模拟系统实现三项移动行为优化：Density Map 拥挤回避、Separation 跳帧执行、以及 Wander 微随机偏移。改动涵盖 C# pipeline/config 层和 HLSL compute shader，遵循现有架构模式。

**实现状态（2026-09-03）**：
- ✅ **Density Map 拥挤回避**：已实现（友军独立密度图系统）
- ❌ **Separation 跳帧执行**：未实现，登记为放弃
- ❌ **Wander 微随机偏移**：未实现，登记为放弃

详见 [IMPLEMENTATION_STATUS.md](./IMPLEMENTATION_STATUS.md)

## Tasks

- [x] 1. 配置与 Shader Property ID 新增（部分完成：仅密度系统）
  - [x] 1.1 在 FlockingConfig 中添加新字段（部分）
    - ✅ 已添加 `densityAvoidanceStrength` 及扩展的密度模型参数
    - ❌ `separationSkipInterval` 和 `wanderMaxAngle` 未实现
    - 文件：`Scripts/Config/FlockingConfig.cs`
    - _Requirements: 5.1, 5.2, 5.3_

  - [x] 1.2 添加新的 Shader Property ID（部分）
    - ✅ 密度系统相关的 Property ID 已添加
    - ❌ `SeparationSkipIntervalId` 和 `WanderMaxAngleId` 未实现
    - 文件：已在相关管理类中实现
    - _Requirements: 5.4, 5.5_

  - [x] 1.3 在 UnitTypeGpuSettings 中添加新字段并更新 FromConfig（部分）
    - ✅ 密度相关字段已添加
    - ❌ `separationSkipInterval` 和 `wanderMaxAngle` 未实现
    - 文件：`Scripts/Core/Stage7Contexts.cs`
    - _Requirements: 5.1, 5.2, 5.3, 5.4_

- [x] 2. Buffer 分配与 Kernel 注册（密度图部分已完成）
  - [x] 2.1 在 MassGpuBufferManager_Stage7 中创建 densityMapTexture
    - ✅ 已添加 `densityMapTexture` 字段并在 Allocate/ReleaseAll 中管理
    - 文件：`Scripts/Pipeline/MassGpuBufferManager_Stage7.cs`
    - _Requirements: 1.4_

  - [x] 2.2 在 MassGpuShaderSet_Stage7 中注册 ClearDensityMap 和 BuildDensityMap kernel 索引
    - ✅ ClearDensityMap 和 BuildDensityMap kernel 已注册
    - 文件：已集成到 shader 管理系统
    - _Requirements: 1.1_

  - [x] 2.3 在 PipelineFrameContext 中添加密度图调度字段
    - ✅ 密度图调度逻辑已集成到管线
    - 文件：`Scripts/Core/Stage7Contexts.cs`
    - _Requirements: 1.2, 1.3_

- [x] 3. Compute Shader Kernel 实现（仅密度图部分）
  - [x] 3.1 在 AgentDataCommon_Stage6.hlsl 中添加 HLSL uniform 声明（部分）
    - ✅ 已添加密度图相关的 uniform 声明
    - ❌ `separationSkipInterval` 和 `wanderMaxAngle` 未添加
    - 文件：`Shaders/AgentDataCommon_Stage6.hlsl`
    - _Requirements: 5.5_

  - [x] 3.2 在 AgentCombatSimulation_Stage6.compute 中实现 ClearDensityMap kernel
    - ✅ ClearDensityMap kernel 已实现
    - 文件：`Shaders/AgentCombatSimulation_Stage6.compute`
    - _Requirements: 1.6_

  - [x] 3.3 在 AgentCombatSimulation_Stage6.compute 中实现 BuildDensityMap kernel
    - ✅ BuildDensityMap kernel 已实现
    - 文件：`Shaders/AgentCombatSimulation_Stage6.compute`
    - _Requirements: 1.4, 1.5_

  - [x] 3.4 在 AgentDataCommon_Stage6.hlsl 中实现密度梯度和回避力计算
    - ✅ 已实现 `ComputeDensityPressure` 函数（实际实现采用压力模型而非梯度模型）
    - 文件：`Shaders/AgentDataCommon_Stage6.hlsl`
    - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5_

  - [ ] 3.5 在 SimulateCombatAndAccumulateDamage 中实现 separation 跳帧逻辑
    - ❌ **未实现，登记为放弃**
    - 理由：当前性能基线已达标（10万/边 113 FPS），跳帧优化优先级低
    - 文件：`Shaders/AgentCombatSimulation_Stage6.compute`
    - _Requirements: 3.1, 3.2, 3.3, 3.4_

  - [ ] 3.6 在 AgentDataCommon_Stage6.hlsl 中实现 WanderAngle 和 ApplyWander 函数
    - ❌ **未实现，登记为放弃**
    - 理由：行进轨迹整齐度在当前规模下可接受，wander 为体验优化项
    - 文件：`Shaders/AgentDataCommon_Stage6.hlsl`
    - _Requirements: 4.2, 4.3, 4.4_

  - [x] 3.7 在 SimulateCombatAndAccumulateDamage kernel 中集成密度回避（部分）
    - ✅ 密度压力系统已集成到主 kernel
    - ❌ wander 部分未实现
    - 文件：`Shaders/AgentCombatSimulation_Stage6.compute`
    - _Requirements: 2.1, 2.6, 4.1, 4.5, 4.6_

- [x] 4. 检查点 - 验证 Shader 编译
  - ✅ 密度系统相关 shader 编译通过并运行正常

- [x] 5. Pipeline Orchestrator 集成（密度图部分）
  - [x] 5.1 在 ComputePipelineOrchestrator 中添加 DispatchDensityMap 方法
    - ✅ 密度图调度逻辑已集成到管线
    - 文件：`Scripts/Pipeline/ComputePipelineOrchestrator.cs`
    - _Requirements: 1.1_

  - [x] 5.2 将 DispatchDensityMap 接入 DispatchFrame 并绑定 buffer
    - ✅ 密度图 dispatch 已集成到帧调度流程
    - 文件：`Scripts/Pipeline/ComputePipelineOrchestrator.cs`
    - _Requirements: 1.1, 1.2, 1.3_

  - [x] 5.3 在 UploadFrameConstants 中上传新参数（部分）
    - ✅ 密度系统参数已上传
    - ❌ `separationSkipInterval` 和 `wanderMaxAngle` 未实现
    - 文件：`Scripts/Pipeline/ComputePipelineOrchestrator.cs`
    - _Requirements: 5.4_

  - [x] 5.4 在 MassGpuSystemManager_Stage7 中设置 rebuildDensityMap
    - ✅ 密度图重建逻辑已设置
    - 文件：`Scripts/MassGpuSystemManager_Stage7.cs`
    - _Requirements: 1.2, 1.3_

- [x] 6. 检查点 - 验证 C# 编译
  - ✅ 所有 C# 脚本编译通过

- [ ] 7. 测试与验证（大部分未实现）
  - [ ]* 7.1 编写密度图累加正确性的 property test
    - ❌ 测试未编写，但功能已通过实际运行验证
    - **Property 1: Density Map Accumulation Correctness**
    - **Validates: Requirements 1.5, 1.6**
    - 文件：`Tests/EditMode/Stage7PropertyTests.cs`

  - [ ]* 7.2 编写密度梯度与回避力的 property test
    - ❌ 测试未编写，但功能已通过实际运行验证
    - **Property 2: Density Gradient and Avoidance Force Correctness**
    - **Validates: Requirements 2.1, 2.2, 2.3, 2.4**

  - [ ]* 7.3 编写 separation 跳帧条件执行的 property test
    - ❌ 功能未实现，测试不适用
    - **Property 4: Separation Skip-Frame Conditional Execution**
    - **Validates: Requirements 3.2, 3.3**

  - [ ]* 7.4 编写 wander hash 确定性与边界的 property test
    - ❌ 功能未实现，测试不适用
    - **Property 5: Wander Hash Determinism and Bounds**
    - **Validates: Requirements 4.2, 4.3**

  - [ ]* 7.5 编写 wander 旋转保持方向幅值的 property test
    - ❌ 功能未实现，测试不适用
    - **Property 6: Wander Rotation Preserves Direction Magnitude**
    - **Validates: Requirements 4.4**

  - [ ]* 7.6 编写 FlockingConfig 新字段默认值与 clamp 的 unit test
    - ❌ 针对 `separationSkipInterval` 和 `wanderMaxAngle` 的测试不适用
    - ✅ 密度系统参数已验证
    - _Requirements: 5.1, 5.2, 5.3_

- [x] 8. 最终检查点 - 确保所有测试通过
  - ✅ 密度系统已通过实际运行验证（10万/边 113 FPS，20万/边 30 FPS，40万/边 12 FPS）

## Notes

- 标记 `*` 的任务为可选项，可跳过以加速 MVP 交付
- 每个任务引用具体 requirements 以确保可追溯性
- 检查点确保 shader 和 C# 编译后的增量验证
- Property tests 验证设计文档中的通用正确性属性
- 密度图使用与现有 flow field 相同的坐标系（128×128，共享 `PositionToFlowFieldCell`）
- `separationSkipInterval`、`wanderMaxAngle`、`densityAvoidanceStrength` 当前对 attacker 和 defender 使用相同值（取 attacker 配置）；后续可扩展为分队伍独立配置

**实现差异（2026-09-03）**：
- ✅ **密度系统已实现但有架构差异**：实际采用友军独立密度图 + 压力模型，而非原规格的梯度回避模型
- ❌ **Separation 跳帧未实现**：当前性能基线已达标（10万/边 113 FPS），跳帧优化优先级低，登记为放弃
- ❌ **Wander 微随机偏移未实现**：行进轨迹整齐度可接受，wander 为体验优化项，登记为放弃

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
