# Requirements Document

## Introduction

本功能为 GPU 大规模单位模拟系统（Stage7）新增三项移动行为优化：
1. **Density Map（密度图）**：生成与 flow field 同分辨率（128×128）的 RenderTexture，用于引导 agent 远离拥挤区域
2. **Separation Skip-Frame（分离力跳帧）**：将 separation push-apart 计算从每帧执行改为每 N 帧执行，中间帧不施加分离力
3. **Wander（微随机偏移）**：在移动方向上叠加微小随机角度扰动，使行走路径更自然

## Glossary

- **Combat_Simulation_Kernel**: GPU compute shader 中执行状态转换、伤害计算和移动/分离逻辑的 kernel（SimulateCombatAndAccumulateDamage）
- **Density_Map**: 128×128 RenderTexture，每个像素存储对应 flow field cell 内的 agent 数量
- **Density_Avoidance_Force**: 基于 Density_Map 梯度计算的方向力，引导 agent 远离高密度区域
- **Flow_Field**: 128×128 结构化缓冲区，存储每个 cell 的导航方向向量
- **Separation_Force**: 基于 spatial hash 3×3 邻域的重叠检测推开力
- **Skip_Frame_Interval**: 控制 Separation_Force 执行频率的帧间隔参数 N
- **Wander_Offset**: 施加在移动方向上的微小随机角度偏移量
- **Pipeline_Orchestrator**: C# 端调度 GPU kernel 执行顺序的编排器（ComputePipelineOrchestrator）
- **rebuildRuntimeFlowEveryFrame**: 已有的布尔标志，控制 flow field 是否每帧重建
- **frameIndex**: 已传入 GPU 的全局帧计数器

## Requirements

### Requirement 1: Density Map 生成

**User Story:** 作为系统开发者，我希望生成一张密度图来反映 agent 的空间分布，以便后续用于拥挤回避计算

#### Acceptance Criteria

1. THE Pipeline_Orchestrator SHALL 在 flow field 生成之后、Combat_Simulation_Kernel 之前调度 Density_Map 生成 kernel
2. WHEN rebuildRuntimeFlowEveryFrame 为 true 时，THE Density_Map kernel SHALL 每帧执行一次
3. WHEN rebuildRuntimeFlowEveryFrame 为 false 时，THE Density_Map kernel SHALL 仅在 flow field 重建帧执行
4. THE Density_Map SHALL 使用与 Flow_Field 相同的分辨率（128×128）和 cellSize
5. THE Density_Map kernel SHALL 遍历所有存活 agent，对每个 agent 所在 cell 的像素值执行原子加 1
6. THE Density_Map kernel SHALL 在累加前将整张 RenderTexture 清零

### Requirement 2: Density Map 回避力叠加

**User Story:** 作为系统开发者，我希望 agent 能根据密度图梯度获得一个额外的回避力，使其自动远离拥挤区域

#### Acceptance Criteria

1. THE Combat_Simulation_Kernel SHALL 在计算最终移动方向时，将 Density_Avoidance_Force 作为附加力叠加到 Flow_Field 方向之上
2. THE Combat_Simulation_Kernel SHALL 通过采样当前 agent 所在 cell 及相邻 cell 的 Density_Map 值计算密度梯度
3. THE Density_Avoidance_Force 方向 SHALL 为密度梯度的反方向（从高密度指向低密度）
4. THE Density_Avoidance_Force SHALL 受一个可配置的强度参数（densityAvoidanceStrength）缩放
5. WHEN 当前 cell 密度值为 0 时，THE Combat_Simulation_Kernel SHALL 不施加 Density_Avoidance_Force
6. THE Density_Avoidance_Force SHALL 独立于 Flow_Field 方向计算，不修改 Flow_Field 缓冲区中的数据

### Requirement 3: Separation 跳帧执行

**User Story:** 作为系统开发者，我希望 separation 计算可以每 N 帧执行一次而非每帧执行，以降低 GPU 开销

#### Acceptance Criteria

1. THE Combat_Simulation_Kernel SHALL 接收一个 Skip_Frame_Interval 参数（整数 N，最小值为 1）
2. WHEN frameIndex 对 Skip_Frame_Interval 取模结果为 0 时，THE Combat_Simulation_Kernel SHALL 执行完整的 Separation_Force 计算
3. WHEN frameIndex 对 Skip_Frame_Interval 取模结果不为 0 时，THE Combat_Simulation_Kernel SHALL 将 Separation_Force 设为零向量
4. WHILE Skip_Frame_Interval 等于 1 时，THE Combat_Simulation_Kernel SHALL 每帧执行 Separation_Force 计算（等同于当前行为）
5. THE Skip_Frame_Interval 参数 SHALL 通过 C# 端 FlockingConfig 暴露为可配置字段

### Requirement 4: Wander 微随机偏移

**User Story:** 作为系统开发者，我希望 agent 的移动方向带有微小随机扰动，使大量单位行走时不会完全同步

#### Acceptance Criteria

1. THE Combat_Simulation_Kernel SHALL 在最终移动方向确定后、速度更新前施加 Wander_Offset
2. THE Wander_Offset SHALL 为一个基于 agent ID 和 frameIndex 生成的伪随机角度值
3. THE Wander_Offset 角度范围 SHALL 受一个可配置的最大角度参数（wanderMaxAngle，单位为度）限制
4. THE Combat_Simulation_Kernel SHALL 将当前移动方向绕 Y 轴旋转 Wander_Offset 角度得到最终方向
5. WHEN agent 处于 STATE_DEAD 状态时，THE Combat_Simulation_Kernel SHALL 不施加 Wander_Offset
6. WHEN agent 当前速度为零向量时，THE Combat_Simulation_Kernel SHALL 不施加 Wander_Offset
7. THE wanderMaxAngle 参数 SHALL 通过 C# 端配置暴露为可调字段，默认值范围为 3-10 度

### Requirement 5: 配置参数管理

**User Story:** 作为系统开发者，我希望所有新增参数都通过 ScriptableObject 配置管理，保持与现有架构一致

#### Acceptance Criteria

1. THE FlockingConfig SHALL 新增 separationSkipInterval 字段（int，最小值 1，默认值 1）
2. THE FlockingConfig SHALL 新增 wanderMaxAngle 字段（float，范围 0-30 度，默认值 5）
3. THE FlockingConfig SHALL 新增 densityAvoidanceStrength 字段（float，最小值 0，默认值 2）
4. THE Pipeline_Orchestrator SHALL 在 UploadFrameConstants 中将新增参数传递到 GPU
5. THE 新增 GPU uniform 变量 SHALL 遵循现有命名规范（camelCase，与 C# 端 property ID 对应）
