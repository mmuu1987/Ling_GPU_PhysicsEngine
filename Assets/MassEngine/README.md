# MassEngine — GPU 海量单位模拟引擎

一个以 GPU compute shader 为核心的大规模单位战斗模拟引擎（50k+ Agent），
为"战争沙盒编辑器 + 战斗观赏器"提供底层能力。逐帧仿真 100% 在 GPU 上执行，
C# 层负责组装、参数上传、管线调度与渲染发起。

> 每个模块目录下都有自己的 `README.md`：职责、数据契约、公共 API、如何验证。
> 游戏层（阵营、下令、场景）在 `Assets/Game/`，不属于引擎。
> 历史演进版本（Stage1-6）已整体归档到仓库根目录 `ArchivedStages/`（不参与编译）。

## 分层与模块

```
┌──────────────────────── Assets/Game（游戏层）────────────────────────┐
│  WarSandbox 场景 · 阵营/兵种配置资产 · 点击下令 · Gizmo · 相机        │
└──────────────────────────────┬───────────────────────────────────────┘
                               │ 只依赖 MassEngine 程序集
┌──────────────────────────────▼───────────────────────────────────────┐
│                        Assets/MassEngine（引擎层）                    │
│                                                                       │
│  UnitTypes/    兵种组合层：IUnitType、模块接口、注册表、配置资产类     │
│  Diagnostics/  遥测（AsyncGPUReadback）+ 战斗/流场 HUD                │
│  ───────────────────────── 功能模块 ─────────────────────────         │
│  Spatial/      空间哈希（邻域查询的地基）                              │
│  FlowField/    双队伍流场（大规模导航）                                │
│  Crowd/        聚散参数（分离/密度压力/车道偏置）                      │
│  Simulation/   战斗+运动主 kernel（寻敌/伤害/位移积分）                │
│  VatRender/    VAT 动画解析 + LOD 分类 + 间接绘制                     │
│  ───────────────────────── 地基 ─────────────────────────             │
│  Core/         数据契约、缓冲所有权、管线调度、参数通道、场景入口       │
└───────────────────────────────────────────────────────────────────────┘
```

## 每帧数据流（一张图看懂）

```
CPU（每帧一次）                          GPU（调度顺序固定）
─────────────────                       ─────────────────────────────────
UnitTypeRegistry                        1. SpatialHash    清格 → 建格
  .FillGpuSettings()                    2. RuntimeFlow    (条件触发+节流)
  → unitTypeSettings 上传                    清 → 目标密度 → 选点 → 生成
MassEngineManager                       3. DensityMap     清 → 累积
  .CreateFrameContext()                 4. CombatSim      清伤害 → 主 kernel
  → uniform 上传                             (寻敌/攻击/聚散/位移/状态)
Orchestrator.DispatchFrame()            5. LodClassify    每兵种一次
RenderDispatcher.Draw()                 6. 双缓冲交换（position/damage/hp）
  每兵种 × LOD 间接绘制
```

## 三条不可违反的契约

1. **队伍身份只有一个真理源**：`teamIdBuffer`。任何 kernel 不得用 buffer
   索引区间推断队伍。
2. **兵种参数只走一条通道**：`UnitTypeGpuSettings`（112 字节，与 HLSL
   `UnitTypeSettings` 逐字段一致，有测试锁定 stride）每帧上传，Agent 经
   `unitTypeIndexBuffer` 映射到自己的参数。不存在按兵种的标量 uniform。
3. **配置资产严格只读**：ScriptableObject 是输入。运行时状态（点击目标、
   VAT 解析结果、每帧参数）住在运行时对象里；任何写回配置资产的代码都是缺陷。

## 新增一个兵种（开闭原则的验收标准）

1. 建配置资产：`UnitTypeConfig` + Spawn/Movement/Flocking/Animation/Combat/Render 子配置
2. （可选）继承 `UnitTypeBase`，在 `CreateModules()` 里替换任意模块实现
3. 把 `UnitTypeConfig` 加进 `ScenarioConfig.unitTypes`

核心管线零改动。`Tests/EditMode` 的三兵种测试就是这条承诺的可执行验证。

## 状态模型

存活状态（Idle/Move/Engage/Attack）每帧按优先级 **Attack > Engage > Move > Idle**
重新推导，存活状态之间没有边约束；**Dead 当且仅当 HP ≤ 0，绝对优先且为终态**。
C# 侧 `AgentStateMachine` 是该 GPU 语义的镜像规格（供测试与工具），不承担运行时职责。

## 如何验证引擎在正确工作

- **EditMode 测试**（`Tests/EditMode`，具体数目见 Test Runner）：数据布局、字段预算、
  注册表、参数通道、状态模型镜像、派发顺序与门控、双缓冲交换、物理账本全项
- **PlayMode GPU 测试**（`Tests/PlayMode`）：真实派发 kernel 并回读——伤害节奏、
  LOD 降频击杀/行军双一致性、渲染桶计数与能见度上限、幽灵目标清场、暂停冻结
- **运行时遥测**：把 `BattleTelemetryHUD` 挂在 manager 旁边，看双方存活数、
  战斗时长、流场重建计数
- 批处理运行：`Unity -runTests -testPlatform EditMode|PlayMode
  -assemblyNames MassEngine.Tests|MassEngine.PlayModeTests`

## 已知边界（诚实清单）

- 当前维护**两张**队伍流场（攻 0 / 防 1），teamId 只允许 0/1（非法值注册期报错）。
  N 队 N 流场需数组化 `flowFieldDirections`，属未来扩展。
- `SelectRuntimeFlowTargets` 是单线程 kernel，`dynamicFlowUpdateInterval`
  （默认 0.35s）是它的显式节流旋钮；每帧重建模式下它是主要瓶颈。
- **LOD 降频模拟已内建**：近/中/远层决策频率 1/2/4（LodConfig 可调），DPS/速度经 dt
  补偿与全帧率一致（有黄金测试）；位置仍每帧积分，视觉无步进。这是大规模（20 万+/边）
  的主性能杠杆。注意：模拟节奏依赖 lodCenter（相机），战局结果不具备镜头无关的严格确定性。
- 战斗主 kernel（Simulation）同时包含聚散与运动积分——Crowd 模块拥有的是
  **参数**，其 GPU 实现物理上位于 `AgentCombatSimulation.compute` 与
  `Core/Shaders/AgentDataCommon.hlsl`（拆成多 pass 会引入额外带宽开销，当前不拆）。
- hp/pendingDamage/position 三组双缓冲：邻居对"本帧死亡"的感知有确定性的
  1 帧延迟，换取 dispatch 内零竞态。
- 性能基线（对比 Stage6 的 90% 承诺）尚待在编辑器中用 10000v10000 实测。

## 文档索引

| 模块 | 一句话 |
|---|---|
| [Core/README.md](Core/README.md) | 数据契约、缓冲所有权、管线调度、场景入口 |
| [Spatial/README.md](Spatial/README.md) | 空间哈希网格：所有邻域查询的地基 |
| [FlowField/README.md](FlowField/README.md) | 双队伍流场：大规模导航与动态目标 |
| [Crowd/README.md](Crowd/README.md) | 聚散行为参数：分离/密度压力/车道 |
| [Simulation/README.md](Simulation/README.md) | 战斗与运动主 kernel |
| [VatRender/README.md](VatRender/README.md) | VAT 动画、LOD、间接绘制 |
| [UnitTypes/README.md](UnitTypes/README.md) | 兵种组合层与配置体系 |
| [Diagnostics/README.md](Diagnostics/README.md) | 遥测与 HUD |
| [../Game/README.md](../Game/README.md) | 游戏层：战争沙盒 |
