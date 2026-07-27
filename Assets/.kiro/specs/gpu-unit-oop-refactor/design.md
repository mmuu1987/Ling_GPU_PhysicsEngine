# Design Document: MassEngine（原 Stage7 GPU Unit OOP Refactor）

> 2026-07 重构版。本文档描述当前实际实现的架构；历史版本中"CPU 行为模块"的设计
> 已被架构决策 A 取代（见下文），不再是本系统的规格。
>
> **目录布局已于 2026-07-26 全量搬迁**：引擎位于 `Assets/MassEngine/`
> （Core/Spatial/FlowField/Crowd/Simulation/VatRender/UnitTypes/Diagnostics/Tests，
> 每模块含 README），游戏层位于 `Assets/Game/`，Stage1-7 历史目录整体归档至仓库根
> `ArchivedStages/`。文中旧路径（MassGPUPhysics_Stage7/...）按此映射阅读；
> 模块级权威文档以各目录 README 为准。命名空间 MassGPUPhysics.Stage7 → MassEngine。

## Overview

MassGPUPhysics_Stage7 是一个 GPU-driven 的大规模战斗模拟器：50k+ Agent 的逐帧仿真
100% 在 4 个 compute shader 上执行，C# 层负责组装场景、上传参数、调度管线与发起渲染。

### 架构决策 A：模块 = GPU 参数贡献者

历史设计把 MovementModule / FlockingModule / CombatModule / AnimationModule 定义为 CPU
行为模块（ComputeDesiredVelocity / FindNearestEnemy 等逐 Agent 方法）。在 50k Agent 的
GPU-driven 架构下，逐 Agent 的 CPU 逻辑要么需要每帧回读（与 compute-only 缓冲策略冲突并
引入多帧延迟），要么意味着每秒数百万次虚方法调用——两者都不可行。实现出来的结果是一层
永不执行的影子代码，测试全部指向影子层，绿灯不代表任何运行时事实。

Stage7 现在的模块契约（`IUnitParameterContributor`）：

- 模块拥有一个功能域（移动/聚散/战斗/动画）的**配置到 GPU 参数的映射**，通过
  `Contribute(ref UnitTypeGpuSettings)` 把本兵种参数写入每帧上传的 StructuredBuffer。
- SpawnModule 是唯一的 CPU 行为模块（一次性初始摆放）。
- 新增兵种 = 新建 `UnitTypeBase` 子类（可替换任意模块实现）+ 一组 ScriptableObject
  配置资产。核心管线只遍历 `UnitTypeRegistry`，不需要修改（开闭原则，Requirement 1.5）。

### 设计原则

1. **单一职责**：每个类只负责一个功能域。
2. **数据驱动且单向**：ScriptableObject 配置是只读输入。运行时状态（点击目标、解析后的
   VAT 数据、每帧参数）存放于运行时对象，**任何代码不得写回配置资产**。
3. **单一真理源**：队伍身份只来自 `teamIdBuffer`；兵种参数只走 `unitTypeSettings`
   StructuredBuffer；不存在按 buffer 索引区间推断身份的代码路径。
4. **可观测**：AsyncGPUReadback 遥测（存活数/战斗时长/流场重建计数）+ 流场预览 HUD，
   使"系统是否在跑"永远可以被直接回答。

## Architecture

```
┌────────────────────────────────────────────────────────────────────┐
│                     MassGpuSystemManager_Stage7                     │
│   场景入口：门控/节流、流场目标覆盖、遥测、生命周期                    │
└───────┬──────────────┬───────────────┬──────────────┬──────────────┘
        │              │               │              │
┌───────▼─────┐ ┌──────▼───────┐ ┌─────▼──────┐ ┌─────▼─────────────┐
│ UnitType    │ │ MassGpuBuffer│ │ ComputePipe│ │ MassGpuRender     │
│ Registry    │ │ Manager      │ │ lineOrch.  │ │ Dispatcher        │
│ 注册/offset │ │ 缓冲所有权    │ │ 调度顺序    │ │ 每兵种×LOD 间接绘制│
│ /settings   │ │ 按兵种×LOD   │ │ 一次性kernel│ │ 预填 MPB，零反射   │
│ 聚合        │ │ 分桶         │ │ 缺失日志    │ │                   │
└───────┬─────┘ └──────────────┘ └────────────┘ └───────────────────┘
        │
┌───────▼────────────────────────────────────────────────┐
│ IUnitType (UnitTypeBase)                                │
│  + SpawnModule    : ISpawnModule（CPU 一次性生成）        │
│  + MovementModule : IMovementModule  ┐                  │
│  + FlockingModule : IFlockingModule  │ IUnitParameter   │
│  + CombatModule   : ICombatModule    │ Contributor      │
│  + AnimationModule: IAnimationModule ┘                  │
│  + RenderRuntime  : ResolvedUnitTypeRuntime（VAT 解析）  │
│  + BuildGpuSettings() → UnitTypeGpuSettings             │
└─────────────────────────────────────────────────────────┘
```

### GPU Compute Pipeline 调度顺序

```
每帧（ComputePipelineOrchestrator.DispatchFrame）:
1. SpatialHash            ClearGrid → BuildSpatialHash
2. RuntimeFlow (条件)     Clear → BuildTargetDensity → SelectTargets → Generate
                          攻/防各一组；重建节流见"流场门控"
3. DensityMap             ClearDensityMap → BuildDensityMap（每帧；Stage7 新增阶段）
4. CombatSimulation       ClearPendingDamage → SimulateCombatAndAccumulateDamage
5. LodClassification      ClassifyVisibleAgentsForUnitType × 每兵种一次
6. SwapSimulationBuffers  position / pendingDamage / hp 三组双缓冲交换
```

测试可通过 `IDispatchListener`（orchestrator 构造参数）记录派发意图并断言顺序。

## 关键机制

### 按兵种的 GPU 参数通道（UnitTypeGpuSettings）

- C# `UnitTypeGpuSettings`（112 字节，Sequential）与 HLSL `struct UnitTypeSettings`
  逐字段一致；有属性测试锁定 stride。
- 每帧 `UnitTypeRegistry.FillGpuSettings(cache)` → `UploadUnitTypeSettings`，因此运行时
  修改任何配置资产的参数都在下一帧生效（Requirement 5.5）。
- 旁路缓冲 `unitTypeIndexBuffer`（每 Agent 一个 int）把 Agent 映射到 settings 槽位，
  刻意不进 AgentData 以保住 56 字节步幅（Requirement 9.2）。
- HLSL 侧一律 `GetUnitSettings(index)`；不存在 attacker*/defender* 标量参数 uniform。

### 队伍身份与流场

- 队伍唯一真理：`teamIdReadBuffer`。RuntimeFlow 的密度核用
  `teamIdReadBuffer[id.x] != attackerTeamId/defenderTeamId` 选择目标，Agent 在缓冲中的
  排列顺序不携带任何语义。
- 管线当前维护两张队伍流场（攻 team 0 / 防 team 1）；同队的多个兵种共享本队流场。
  `ConfigValidator` 对 teamId ∉ {0,1} 的兵种显式报错拒绝注册（而不是静默错误模拟）。
  N 队 N 流场是未来扩展项：需要把 flowFieldDirections 换成数组化缓冲并按队伍派发。
- 队伍流场目标解析顺序：manager 上的运行时覆盖（点击）> 该队第一个声明了配置目标的
  兵种 MovementModule.Target。

### 流场门控与节流（正交分解）

```
enabled  = RuntimeFlowConfig.flowFieldEnabled / defenderFlowFieldEnabled
reason   = 存在目标（覆盖或配置） || runtimeDynamic*FlowEnabled
cadence  = dirty（目标变更/刚初始化）立即重建
         | 动态目标按 dynamicFlowUpdateInterval 节流重建（默认 0.35s）
         | rebuildRuntimeFlowEveryFrame 强制每帧
```

静态目标只在 dirty 时重建一次。SelectRuntime*FlowTargets 已并行化（2026-07-27）：
每扇区一个 64 线程组做 groupshared 归约，残局兜底移入 Generate（需跨扇区视野，
stats[3]==0 触发）；节流参数保留为带宽旋钮而非刚需。DensityMap 与流场重建解耦，
每帧重建（拥挤压力每帧消费它）。

### LOD 降频模拟（2026-07-26 新增）

近/中/远层 Agent 的决策段每 1/2/4 帧执行一次（LodConfig.near/mid/farSimulationInterval）；
非激活帧只做伤害结算+死亡判定+缓存速度位置积分+写回（双缓冲正确性要求）。冷却为累积制、
分离冲量用 dtSim=interval×dt 补偿；转向不能线性放大步长（阻尼×lerp 跨步长不可交换，
线性补偿曾导致降频巡航快 ~19%），采用精确 N 步复合闭式解 v'=α^N·v+gain·T
（α=damp×(1−steer)，N=1 逐位等价原公式）。DPS 与行军速度均有 PlayMode 黄金测试锁定
（LodScaledSimulationPreservesKillCadence ±20 帧 / LodScaledSimulationPreservesTravelSpeed
偏差 <15%）。错峰按 64 线程组对齐避免 warp 分歧。
代价：战局对 lodCenter（相机位置）不再严格确定；远层目标获取延迟最高 interval×4 帧。

### hp / pendingDamage / position 双缓冲

所有 kernel 读上一帧快照（`hpReadBuffer` / `pendingDamageReadBuffer` /
`agentPositionReadBuffer`）；战斗 kernel 把本帧结果写入写目标缓冲；帧末统一交换。
本帧被杀死的 Agent 对邻居在下一帧才"消失"——确定性的 1 帧延迟，换取 dispatch 内零竞态。

### 状态模型（与 requirements.md R10 一致）

存活状态每帧由 `ResolveAliveState(inAttackHold, hasEngageTarget, hasMoveDirection)` 按
优先级 Attack > Engage > Move > Idle 重新推导；Dead 当且仅当 hp ≤ 0，绝对优先且终态。
C# `AgentStateMachine` 是该语义的镜像规格（供测试与工具推理），不承担运行时职责。

### VAT 与渲染

- `VatProfileReader`（反射、仅初始化时执行一次）把 VAT profile 解析为
  `ResolvedUnitTypeRuntime`：每 LOD 的 mesh/材质/阴影设置、各 clip 时长、预填好的
  MaterialPropertyBlock。渲染路径零反射、零逐帧 VAT 重绑定。
- VAT LOD mesh 强制与其烘焙纹理配对；作者配置了冲突 mesh 时以 profile 配对为准并输出
  警告——绝不写回 RenderConfig 资产。
- 动画时间按当前状态的 clip 时长回绕（循环状态无相位跳变），Dead 停在死亡 clip 末帧；
  各 clip 时长按兵种经 settings 上传。
- LOD 分类每兵种一个 dispatch（绑定该兵种的 3 个 append buffer），UAV 数量与兵种数无关；
  渲染按兵种 × LOD 发起 DrawMeshInstancedIndirect。

### 近景材质依赖说明（已解决）

近景 shader 已随目录搬迁迁入 `MassEngine/VatRender/Shaders/LitInstancedAgentShader.shader`
（GUID 保持 ed9e468a…，材质引用未断）；Stage6 目录已归档。

## Error Handling

- `ConfigValidator.Validate` 是纯校验：SpawnConfig 缺失/unitCount≤0/teamId 非法/类名无法
  解析 → Error 并跳过该兵种；其余子配置缺失 → Warning，运行时用内建默认值，**不写回资产**。
- ShaderSet 无效时 Initialize 记录一次 Error 并关闭 enableGpuDispatch；缺失 kernel 的
  派发日志带一次性标志，不逐帧刷屏。
- 缓冲分配后立即清零流场方向缓冲；重建守卫比较完整分配签名
  （agentCount + gridCellCount + maxAgentsPerCell + flowFieldResolution + unitTypeCount）。
- OnEnable/OnDisable 与 Initialize/Release 配对；RenderTexture 释放区分
  Play/Editor（Destroy/DestroyImmediate）。

## Correctness Properties（当前测试套件）

EditMode（Tests/EditMode/Stage7PropertyTests.cs，验证 CPU 侧契约）：

- P1 AgentData stride == 56；UnitTypeGpuSettings stride == 112 且 16 字节对齐。
- P2 命名空间内所有类型（class+struct，无后缀过滤）公共实例字段 ≤ 30。
- P3 生成区域包含性 + 初始 Idle。
- P4 三兵种（同队两个 + 敌队一个）各自的参数完整到达 settings 数组；offset 连续；
  unitTypeIndex/teamId/hp 填充正确。
- P5 运行时改配置 → 下一次 FillGpuSettings 反映（Requirement 5.5）。
- P6 非法配置值被钳制；null 配置产生可用默认值。
- P7 registry 通过类名实例化自定义子类；teamId 非法被拒绝且有 Error 日志；SpawnConfig
  缺失是 Error 且校验不改变资产。
- P8 状态模型镜像：Dead 终态、存活状态自由重推导、Resolve 与 GPU 优先级表一致。
- P9 派发顺序 SpatialHash → Flow → Density → Combat → 每兵种 LOD（经 IDispatchListener），
  跨帧稳定，缺失 kernel 只报一次。
- P10 combat 缓冲与 AgentData 分离；hp/pendingDamage/position 双缓冲交换正确；
  可见索引/args 缓冲随兵种数扩展。

PlayMode（Tests/PlayMode/Stage7GpuKernelTests.cs，真实 GPU 派发 + 回读；无计算能力的
环境自动跳过）：

- G1 对峙阵型下伤害在 GPU 上按攻击间隔量化累积（损失恒为 attackDamage 的整数倍），
  帧预算内出现死亡；hp≤0 ⇔ state Dead 且速度归零。
- G2 全程观测到的状态迁移全部合法（Dead 终态），且交战双方进入过 Attack。
- G3 battleStarted=0 时零位移、零伤害、全员 Idle。

## 已知偏差与技术债

- 帧时间基线（Requirement 9.4 的 90% 对比）需在编辑器中用 10000v10000 实测；注意
  Stage7 相对 Stage6 的已知偏差项：DensityMap 阶段、每帧 settings 上传（N×112B，可忽略）、
  LOD 分类按兵种多次派发（每次全 Agent 扫描，兵种数小时可忽略）。
- Shader 文件名仍带 _Stage6 后缀：改名会变更 GUID/引用，收益低于风险，暂保留。
- SelectRuntimeFlowTargets 仍是单线程 kernel；在高 flowFieldResolution 或每帧重建模式下
  是主要瓶颈，已由节流参数控制暴露面，未来可并行化（分 sector reduction）。
