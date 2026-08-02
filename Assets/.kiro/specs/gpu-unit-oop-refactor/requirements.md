# Requirements Document

## Introduction

基于 MassGPUPhysics_Stage6 创建 MassGPUPhysics_Stage7，以兵种（UnitType）为核心单位，用面向对象思想重构整体架构。目标是消除 GPUInstancingManager_Stage6 上帝类，将职责拆分为独立、可扩展的模块类，同时保持 GPU compute shader 管线性能不退化。首版实现单兵种 OOP 骨架，架构上预留多兵种扩展接口。

## Glossary

- **Stage7_System**: MassGPUPhysics_Stage7 的整体运行时系统
- **UnitType_Module**: 兵种类型模块，封装单一兵种的配置、生成与 GPU 参数贡献的 OOP 基类/接口。注意：逐帧仿真 100% 在 GPU compute shader 上执行，模块的职责是"向管线贡献本兵种的参数与资源"（UnitTypeGpuSettings 通道），而不是在 CPU 上执行逐 Agent 行为（架构决策 A，见 design.md）
- **SpawnModule**: 兵种生成区域模块，负责配置和执行兵种实例的初始化生成（唯一的 CPU 一次性行为模块）
- **FlowFieldVisualizer**: 流场可视化能力：流场 kernel 在开关开启时写入预览纹理（FlowDirection / DensityTarget 两种模式），由 HUD 组件展示；开关关闭时 kernel 跳过全部预览写入
- **MovementModule**: 兵种移动参数模块，向 GPU 贡献 maxSpeed / velocityDamping / flowFieldWeight / flowFieldResponsiveness，并持有本兵种的流场目标声明
- **FlockingModule**: 聚散参数模块，向 GPU 贡献 separation / attraction / 密度压力等参数
- **AnimationModule**: 动画参数模块，向 GPU 贡献移动动画速度区间；VAT 片段帧数据在初始化时从 VAT profile 解析为运行时数据（ResolvedUnitTypeRuntime）
- **CombatModule**: 战斗参数模块，向 GPU 贡献寻敌半径、攻击范围/伤害/间隔与 maxHp
- **StateMachine**: Agent 行为状态模型（Idle/Move/Engage/Attack/Dead）。存活状态每帧按优先级 Attack > Engage > Move > Idle 重新推导，Dead 具有绝对优先级且为终态；C# 侧 AgentStateMachine 是该 GPU 语义的镜像规格
- **ComputePipeline**: GPU Compute Shader 调度管线（SpatialHash → RuntimeFlow → DensityMap → CombatSimulation → LodClassification）
- **ScriptableObject_Config**: Unity ScriptableObject 配置资产，用于数据驱动的兵种参数配置

## Requirements

### Requirement 1: OOP 架构重构与上帝类消除

**User Story:** 作为开发者，我希望 Stage7 将 GPUInstancingManager 的职责拆分为多个单一职责类，以便代码可维护、可扩展、可测试。

#### Acceptance Criteria

1. THE Stage7_System SHALL 将 Stage6 中 GPUInstancingManager_Stage6 的配置字段和逻辑拆分为独立的模块类，每个模块类承担单一职责。
2. THE Stage7_System SHALL 提供 UnitType_Module 基类或接口，定义兵种的生成、移动、战斗、动画等行为的扩展点。
3. THE Stage7_System SHALL 确保任何单个类的公共配置字段数量不超过 30 个。
4. THE Stage7_System SHALL 通过 ScriptableObject_Config 资产驱动兵种参数配置，将数据与逻辑分离。
5. WHEN 新增一个兵种类型时，THE Stage7_System SHALL 仅需创建新的 UnitType_Module 子类和对应的 ScriptableObject_Config 资产，无需修改核心管线代码。

### Requirement 2: 兵种生成区域

**User Story:** 作为设计师，我希望每个兵种拥有可配置的独立生成区域，以便灵活布置战场阵型。

#### Acceptance Criteria

1. THE SpawnModule SHALL 支持通过 ScriptableObject_Config 配置生成区域的中心点和范围。
2. THE SpawnModule SHALL 在初始化阶段根据配置将指定数量的 Agent 生成到对应区域内。
3. WHEN 生成区域配置发生变化时，THE SpawnModule SHALL 在下次初始化时使用新配置生成 Agent。
4. THE SpawnModule SHALL 支持为攻击方和防守方分别配置独立的生成区域参数。

### Requirement 3: 流场可视化

**User Story:** 作为开发者，我希望在运行时可视化流场数据，以便调试和验证流场导航行为。

#### Acceptance Criteria

1. THE FlowFieldVisualizer SHALL 在流场生成 kernel 中将流场数据写入预览 RenderTexture，并由 HUD 组件（FlowFieldPreviewHUD）在运行时展示。
2. THE FlowFieldVisualizer SHALL 提供运行时开关（RuntimeFlowConfig.runtimeFlowPreviewEnabled），允许启用或禁用可视化。
3. WHEN 可视化开关关闭时，THE 流场 kernel SHALL 跳过全部预览纹理写入（flowPreviewEnabled uniform 门控），不消耗额外的 GPU 写带宽。
4. THE FlowFieldVisualizer SHALL 支持流场方向模式和密度目标模式两种预览（RuntimeFlowConfig.runtimeFlowPreviewMode）。

### Requirement 4: 兵种移动 — 流场推进与寻敌接战

**User Story:** 作为设计师，我希望兵种通过流场进行大规模推进，移动过程中自动检测敌人并接战，以便实现自然的战场推进行为。

#### Acceptance Criteria

1. THE MovementModule SHALL 接受一个目标点或一个目标区域作为流场生成的输入。
2. THE MovementModule SHALL 通过流场导航驱动 Agent 的大规模推进移动。
3. WHEN Agent 处于 Move 状态时，THE MovementModule SHALL 根据运行时流场方向和权重驱动 Agent 速度。
4. THE MovementModule SHALL 通过 ScriptableObject_Config 配置流场响应速度、流场权重和最大移动速度。
5. WHILE Agent 沿流场推进时，THE CombatModule SHALL 持续扫描邻域寻敌，发现敌人后由状态机切换为 Engage 状态。

### Requirement 5: 聚散行为模块

**User Story:** 作为开发者，我希望聚散逻辑作为独立行为模块存在，以便独立调参和复用。

#### Acceptance Criteria

1. THE FlockingModule SHALL 作为独立模块实现，与 MovementModule 和 CombatModule 解耦。
2. THE FlockingModule SHALL 通过调整 separation 强度控制 Agent 之间的排斥力。
3. THE FlockingModule SHALL 通过目标点吸引力（attractionStrength）控制 Agent 向已配置流场目标直线接近的兜底转向：当流场在该位置未给出方向（尚未生成或超出覆盖范围）且该队伍配置了流场目标时，Agent 以 attractionStrength 权重直接朝目标转向。
4. THE FlockingModule SHALL 通过 ScriptableObject_Config 配置 separation 强度和吸引力参数。
5. WHEN 任一兵种参数模块的参数在运行时被修改时，THE Stage7_System SHALL 通过每帧刷新并上传 UnitTypeGpuSettings 使新参数在下一帧生效。

### Requirement 6: VAT 动画切换

**User Story:** 作为开发者，我希望动画模块根据 Agent 状态自动切换 VAT 动画片段，以便不同行为状态有对应的视觉表现。

#### Acceptance Criteria

1. THE AnimationModule SHALL 根据 StateMachine 的当前状态映射到对应的 VAT 动画片段（Idle、Move、Attack、Death）。
2. WHEN Agent 状态从一个状态切换到另一个状态时，THE AnimationModule SHALL 切换到对应状态的 VAT 动画片段。
3. THE Stage7_System SHALL 在初始化时从 RenderConfig 引用的 VAT profile 解析每个动画片段的帧范围、帧率与时长（ResolvedUnitTypeRuntime），并按兵种上传各片段时长；AnimationConfig 仅承载移动动画速度区间等调参项。
4. WHEN Agent 进入 Dead 状态时，THE AnimationModule SHALL 播放死亡动画一次后停留在末帧。
5. THE Stage7_System SHALL 按 LOD 距离降低动画更新频率（全局 LodConfig 的 near/mid/far AnimationInterval）。

### Requirement 7: 战斗模块 — 寻敌、攻击、伤害结算

**User Story:** 作为设计师，我希望战斗逻辑封装为独立模块，以便独立配置攻击参数和扩展战斗机制。

#### Acceptance Criteria

1. THE CombatModule SHALL 基于空间哈希邻域查询寻找最近的敌方 Agent 作为攻击目标。
2. THE CombatModule SHALL 忽略同阵营和 Dead 状态的 Agent 进行寻敌。
3. WHEN Agent 进入攻击范围时，THE CombatModule SHALL 按攻击间隔对目标累积伤害。
4. THE CombatModule SHALL 通过双缓冲 pendingDamage 机制在 GPU 上并行结算伤害。
5. WHEN 目标 Agent 的 HP 降至零或以下时，THE CombatModule SHALL 将目标状态切换为 Dead。
6. THE CombatModule SHALL 通过 ScriptableObject_Config 配置攻击范围、攻击伤害、攻击间隔和最大生命值。

### Requirement 8: 移动中寻敌

**User Story:** 作为设计师，我希望 Agent 在移动过程中持续扫描邻域，发现敌人即接战，以便实现自然的遭遇战行为。

#### Acceptance Criteria

1. WHILE Agent 处于 Move 状态，THE CombatModule SHALL 持续扫描空间哈希邻域检测敌方 Agent。
2. WHEN 移动中的 Agent 在寻敌半径内发现敌方 Agent 时，THE StateMachine SHALL 将该 Agent 状态切换为 Engage。寻敌的空间哈希搜索格半径由 targetAcquireRadius / cellSize 推导并受 shader 上限（4 格）约束；超出上限时 THE Stage7_System SHALL 输出一次警告而非静默截断。
3. THE StateMachine SHALL 确保移动和战斗检测并行执行，由状态优先级决定最终行为。
4. WHEN Agent 处于 Engage 状态且目标丢失或死亡时，THE StateMachine SHALL 将 Agent 状态回退为 Move。

### Requirement 9: GPU Compute Pipeline 保持

**User Story:** 作为开发者，我希望 OOP 重构不改变 GPU compute shader 管线的执行顺序和性能特征，以便重构后性能不退化。

#### Acceptance Criteria

1. THE ComputePipeline SHALL 保持 SpatialHash → RuntimeFlow（条件触发）→ DensityMap → CombatSimulation → LodClassification（每兵种一次）的调度顺序。（DensityMap 为 Stage7 新增阶段，是相对 Stage6 帧时间对比时的已知偏差项。）
2. THE ComputePipeline SHALL 保持与 Stage6 相同的 AgentData 内存布局和 56 字节步幅。
3. THE ComputePipeline SHALL 保持 compute-only 战斗缓冲区与 AgentData 分离的策略；其中 hp 与 pendingDamage 均为读快照/写目标双缓冲，帧末交换。
4. THE Stage7_System SHALL 在相同硬件和相同 Agent 数量下，帧率不低于 Stage6 的 90%。

### Requirement 10: 状态机与优先级

**User Story:** 作为开发者，我希望状态机明确定义状态转换规则和优先级，以便移动和战斗行为协调一致。

#### Acceptance Criteria

1. THE StateMachine SHALL 维护五个状态：Idle、Move、Engage、Attack、Dead。
2. THE StateMachine SHALL 采用逐帧重推导模型：存活状态（Idle/Move/Engage/Attack）每帧根据战斗态势按优先级重新推导，任意存活状态之间不存在边约束；Dead 当且仅当 HP ≤ 0 时进入，且为终态（不允许任何转出）。C# 侧 AgentStateMachine 是该模型的镜像规格，与 HLSL 的 ResolveAliveState 保持一致。
3. WHEN 多个行为条件同时成立时，THE StateMachine SHALL 按优先级（Dead > Attack > Engage > Move > Idle）决定最终状态。
4. WHEN Agent 进入 Dead 状态时，THE StateMachine SHALL 阻止该 Agent 参与后续寻敌、攻击和碰撞逻辑。
