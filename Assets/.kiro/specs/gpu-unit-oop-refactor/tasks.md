# Implementation Plan: GPU Unit OOP Refactor (Stage7)

## Overview

将 Stage6 的 GPUInstancingManager 上帝类拆分为以兵种（UnitType）为核心的模块化 OOP 架构。实现路径：先搭建目录结构和核心接口/数据模型，再逐模块实现 SpawnModule → MovementModule → FlockingModule → AnimationModule → CombatModule → FlowFieldVisualizer，最后通过 ComputePipelineOrchestrator 和 MassGpuSystemManager 将所有模块串联起来。

## Tasks

- [x] 1. 搭建 Stage7 目录结构与核心数据模型
  - [x] 1.1 创建 MassGPUPhysics_Stage7 目录结构和核心类型定义
    - 创建 `MassGPUPhysics_Stage7/Scripts/` 目录
    - 创建 `AgentData` 结构体（56 字节，LayoutKind.Sequential），字段与 Stage6 完全一致
    - 创建 `AgentState` 枚举（Idle=0, Move=1, Engage=2, Attack=3, Dead=4）
    - 创建 `VATClipParams` 结构体
    - _Requirements: 9.2, 10.1_

  - [x]* 1.2 编写 AgentData 步幅属性测试
    - **Property 1: AgentData 步幅不变量**
    - 验证 `Marshal.SizeOf<AgentData>()` 始终返回 56
    - **Validates: Requirements 9.2**

  - [x] 1.3 创建 ScriptableObject 配置资产类
    - 创建 `UnitTypeConfig` ScriptableObject（顶层兵种配置）
    - 创建 `SpawnConfig` ScriptableObject（生成区域配置）
    - 创建 `MovementConfig` ScriptableObject（移动配置）
    - 创建 `FlockingConfig` ScriptableObject（聚散配置）
    - 创建 `AnimationConfig` ScriptableObject（动画配置）
    - 创建 `CombatConfig` ScriptableObject（战斗配置）
    - 创建 `RenderConfig` ScriptableObject（渲染配置）
    - 创建 `ScenarioConfig_Stage7` ScriptableObject（场景配置，持有多个 UnitTypeConfig 引用）
    - _Requirements: 1.4, 2.1, 4.1, 5.4, 6.3, 7.6_

  - [x]* 1.4 编写公共配置字段上限属性测试
    - **Property 2: 公共配置字段上限**
    - 通过反射验证所有模块类的公共字段数量 ≤ 30
    - **Validates: Requirements 1.3**

- [x] 2. 实现核心接口与基类
  - [x] 2.1 创建模块接口定义
    - 创建 `IUnitType` 接口（定义兵种完整行为契约）
    - 创建 `ISpawnModule` 接口
    - 创建 `IMovementModule` 接口
    - 创建 `IFlockingModule` 接口
    - 创建 `IAnimationModule` 接口
    - 创建 `ICombatModule` 接口
    - 创建 `IFlowFieldVisualizer` 接口
    - _Requirements: 1.1, 1.2_

  - [x] 2.2 实现 UnitTypeBase 抽象基类
    - 实现 `UnitTypeBase` 抽象类，提供默认模块组装逻辑
    - 实现 `Initialize`、`OnBuffersBound`、`Release` 生命周期方法
    - 实现 `CreateModules` 虚方法，默认创建各 Default 模块实例
    - _Requirements: 1.2, 1.5_

  - [x] 2.3 实现 UnitTypeRegistry
    - 实现兵种注册、buffer offset 分配、批量初始化和释放
    - 确保 TotalAgentCount 正确累加
    - _Requirements: 1.1, 1.5_

  - [x] 2.4 创建 PipelineFrameContext 和 UnitTypeInitContext 结构体
    - 定义管线帧上下文数据结构
    - 定义 UnitType 初始化上下文数据结构
    - _Requirements: 9.1_

- [ ] 3. Checkpoint - 确保核心接口和数据模型编译通过
  - Ensure all tests pass, ask the user if questions arise.

- [x] 4. 实现 SpawnModule
  - [x] 4.1 实现 DefaultSpawnModule
    - 实现 `ISpawnModule` 接口
    - 根据 SpawnConfig 的 center 和 size 在指定区域内随机生成 Agent
    - 支持攻击方和防守方独立生成区域
    - 生成的 Agent 初始状态为 Idle，速度为零
    - _Requirements: 2.1, 2.2, 2.3, 2.4_

  - [x]* 4.2 编写生成区域包含性属性测试
    - **Property 3: 生成区域包含性**
    - 验证所有生成的 Agent 位置在 [center - size/2, center + size/2] 范围内
    - 验证生成数量恰好等于 count
    - **Validates: Requirements 2.2**

- [x] 5. 实现 MovementModule（流场推进）
  - [x] 5.1 实现 DefaultMovementModule
    - 实现 `IMovementModule` 接口
    - 实现流场推进模式：根据流场方向和权重计算期望速度
    - 移动中寻敌由 CombatModule 负责，MovementModule 只负责流场驱动
    - _Requirements: 4.1, 4.2, 4.3, 4.4_

  - [x]* 5.2 编写流场导航速度方向一致性属性测试
    - **Property 4: 流场导航速度方向一致性**
    - 验证期望速度在 XZ 平面与 flowDir 方向一致
    - **Validates: Requirements 4.2, 4.3**

  - [x]* 5.3 编写流场权重比例正确性属性测试
    - **Property 5: 流场权重比例正确性**
    - 验证权重越大期望速度越大
    - **Validates: Requirements 4.3, 4.4**

- [x] 6. 实现 FlockingModule（聚散行为）
  - [x] 6.1 实现 DefaultFlockingModule
    - 实现 `IFlockingModule` 接口
    - 实现 ComputeSeparationForce：基于重叠距离和 separationStrength 计算排斥力
    - 实现 ComputeAttractionForce：计算指向目标的吸引力
    - 参数通过 FlockingConfig ScriptableObject 配置
    - _Requirements: 5.1, 5.2, 5.3, 5.4, 5.5_

  - [x]* 6.2 编写分离力单调性属性测试
    - **Property 6: 分离力单调性**
    - 验证 separationStrength 增大时分离力大小单调不减
    - **Validates: Requirements 5.2**

  - [x]* 6.3 编写吸引力方向正确性属性测试
    - **Property 7: 吸引力方向正确性**
    - 验证吸引力方向指向目标（与 T-A 的点积 > 0）
    - **Validates: Requirements 5.3**

- [x] 7. 实现 AnimationModule（VAT 动画切换）
  - [x] 7.1 实现 DefaultAnimationModule
    - 实现 `IAnimationModule` 接口
    - 实现 GetClipForState：根据 AgentState 映射到对应 VAT 片段参数
    - 实现 AdvanceAnimationTime：推进动画时间，Dead 状态到末帧后停止，其余循环
    - 支持按 LOD 距离降低动画更新频率
    - _Requirements: 6.1, 6.2, 6.3, 6.4, 6.5_

  - [x]* 7.2 编写状态-动画映射正确性属性测试
    - **Property 8: 状态-动画映射正确性**
    - 验证所有有效 AgentState 返回正确的 VATClipParams，Dead 的 loop=false
    - **Validates: Requirements 6.1, 6.2**

  - [x]* 7.3 编写 Dead 动画时间上界属性测试
    - **Property 9: Dead 动画时间上界**
    - 验证 Dead 状态动画时间不超过死亡片段总时长
    - **Validates: Requirements 6.4**

- [ ] 8. Checkpoint - 确保所有独立模块编译通过并通过属性测试
  - Ensure all tests pass, ask the user if questions arise.

- [x] 9. 实现 CombatModule（战斗逻辑）
  - [x] 9.1 实现 DefaultCombatModule
    - 实现 `ICombatModule` 接口
    - 实现 FindNearestEnemy：基于空间哈希邻域查询，忽略同阵营和 Dead
    - 实现 IsInAttackRange：判断目标是否在攻击范围内
    - 实现 ComputeDamage：根据冷却时间计算本帧伤害
    - _Requirements: 7.1, 7.2, 7.3, 7.6_

  - [x]* 9.2 编写寻敌排除不变量属性测试
    - **Property 10: 寻敌排除不变量（Dead 与同阵营）**
    - 验证寻敌结果排除同阵营和 Dead 状态的 Agent
    - **Validates: Requirements 7.2, 10.4**

  - [x]* 9.3 编写伤害累积线性正确性属性测试
    - **Property 11: 伤害累积线性正确性**
    - 验证 N 次完整攻击冷却后累积伤害 = N × attackDamage
    - **Validates: Requirements 7.3**

  - [x]* 9.4 编写 HP 归零触发死亡属性测试
    - **Property 12: HP 归零触发死亡**
    - 验证 HP ≤ 0 时 currentState 被设为 Dead(4)
    - **Validates: Requirements 7.5**

- [x] 10. 实现 StateMachine（状态机与优先级）
  - [x] 10.1 实现 AgentStateMachine 静态类
    - 实现 ValidTransitions 合法转换表
    - 实现 TryTransition：只允许合法转换
    - 实现 ResolveConflict：从多个并发请求中选择优先级最高的合法转换
    - Dead 为终态，不允许任何转出
    - _Requirements: 10.1, 10.2, 10.3, 10.4_

  - [x]* 10.2 编写状态转换合法性属性测试
    - **Property 13: 状态转换合法性**
    - 验证 TryTransition 成功当且仅当 R 在 S 的合法转换集合中
    - **Validates: Requirements 10.2**

  - [x]* 10.3 编写并发状态请求优先级决定属性测试
    - **Property 14: 并发状态请求优先级决定**
    - 验证 ResolveConflict 返回所有合法转换中优先级最高的状态
    - **Validates: Requirements 10.3**

- [x] 11. 实现 FlowFieldVisualizer（流场可视化）
  - [x] 11.1 实现 DefaultFlowFieldVisualizer
    - 实现 `IFlowFieldVisualizer` 接口
    - 实现 Render 方法：将流场数据渲染到 RenderTexture
    - 支持 FlowDirection 和 DensityTarget 两种预览模式
    - 开关关闭时不执行任何 GPU 操作
    - _Requirements: 3.1, 3.2, 3.3, 3.4_

- [x] 12. 实现 GPU 管线调度与缓冲区管理
  - [x] 12.1 实现 MassGpuBufferManager_Stage7
    - 管理所有 ComputeBuffer 的创建和释放
    - 包含 AgentData 主缓冲区和 CombatBufferSet 战斗缓冲区
    - 实现双缓冲 pendingDamage 的 swap 逻辑
    - 实现 ReleaseAll 统一释放
    - _Requirements: 9.2, 9.3_

  - [x] 12.2 实现 ComputePipelineOrchestrator
    - 实现 DispatchFrame 方法，严格保持 SpatialHash → RuntimeFlow → CombatSimulation → LodClassification 调度顺序
    - 实现各阶段的 Dispatch 方法
    - 实现 Compute Shader 引用为 null 时的跳过和错误日志
    - _Requirements: 9.1, 9.3_

  - [x] 12.3 实现 MassGpuShaderSet_Stage7（Shader 引用集合）
    - 集中管理所有 Compute Shader 引用和 kernel index
    - 从 Stage6 复制 shader 文件到 Stage7 目录（保持不变）
    - _Requirements: 9.1_

- [x] 13. 实现 MassGpuSystemManager_Stage7（场景入口）
  - [x] 13.1 实现 MassGpuSystemManager_Stage7 MonoBehaviour
    - 替代 GPUInstancingManager_Stage6 作为场景入口
    - 持有 UnitTypeRegistry 和 ComputePipelineOrchestrator
    - 实现 Start/Update/OnDisable 生命周期
    - 暴露 StartBattle/StopBattle/ResetScenario API
    - Inspector 仅保留全局配置（世界大小、LOD 距离、视锥剔除等）
    - _Requirements: 1.1, 1.3, 9.4_

  - [x] 13.2 实现 ConfigValidator 配置验证
    - 验证 UnitTypeConfig 完整性
    - 缺失子配置时记录警告并使用默认值
    - unitCount ≤ 0 时报错
    - _Requirements: 1.4_

- [x] 14. 集成串联与首版单兵种骨架
  - [x] 14.1 创建首版单兵种 UnitType 实现类
    - 创建 `DefaultSwordUnit : UnitTypeBase`（首版单兵种实现）
    - 在 CreateModules 中组装所有默认模块
    - 验证单兵种从生成到战斗的完整流程可运行
    - _Requirements: 1.2, 1.5_

  - [x] 14.2 创建 Stage7 示例 ScriptableObject 配置资产
    - 创建攻击方 UnitTypeConfig 资产（含 SpawnConfig、MovementConfig 等子配置）
    - 创建防守方 UnitTypeConfig 资产
    - 创建 ScenarioConfig_Stage7 资产引用两个兵种配置
    - _Requirements: 1.4, 2.4_

  - [x] 14.3 创建 Stage7 测试场景
    - 创建 `MassGPUPhysics_Stage7/Scene/Stage7_Test.unity` 场景
    - 配置 MassGpuSystemManager_Stage7 组件
    - 挂载 ScenarioConfig 和全局参数
    - 复制 Stage6 的 Shader 和 VAT 资产引用
    - _Requirements: 9.1, 9.4_

- [ ] 15. Final Checkpoint - 确保所有模块集成正确，编译通过
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- 所有代码使用 C#（Unity 2021+ 兼容语法）
- GPU Compute Shader 文件从 Stage6 直接复制，不做修改，保持管线性能零退化
- 首版实现单兵种骨架（DefaultSwordUnit），架构预留多兵种扩展（只需新增 UnitTypeBase 子类 + ScriptableObject 配置）
- Property-based tests 使用 NUnit + 自定义随机输入生成（Unity Test Framework）
- 每个 Checkpoint 确保增量验证，避免后期集成问题

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1", "1.3"] },
    { "id": 1, "tasks": ["1.2", "1.4", "2.1"] },
    { "id": 2, "tasks": ["2.2", "2.3", "2.4"] },
    { "id": 3, "tasks": ["4.1", "5.1", "6.1", "7.1", "10.1", "11.1"] },
    { "id": 4, "tasks": ["4.2", "5.2", "5.3", "6.2", "6.3", "7.2", "7.3", "10.2", "10.3"] },
    { "id": 5, "tasks": ["9.1"] },
    { "id": 6, "tasks": ["9.2", "9.3", "9.4"] },
    { "id": 7, "tasks": ["12.1", "12.2", "12.3"] },
    { "id": 8, "tasks": ["13.1", "13.2"] },
    { "id": 9, "tasks": ["14.1", "14.2"] },
    { "id": 10, "tasks": ["14.3"] }
  ]
}
```


