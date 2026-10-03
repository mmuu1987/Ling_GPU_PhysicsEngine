# Core — 数据契约、缓冲所有权与管线调度

引擎的地基。其它所有模块都依赖这里定义的数据布局与调度顺序；反方向依赖不存在。

## 职责

| 文件 | 职责 |
|---|---|
| `AgentData.cs` | Agent 主结构体（**64 字节**，Sequential）：原有 position/rotation/scale/velocity/state/animTime + presentationState/locomotionSpeed；本轮由 56B 扩至 64B，不再与 Stage6 二进制兼容，C#/Compute/两份VAT shader同步，测试锁定stride |
| `UnitTypeGpuSettings.cs` | 按兵种 GPU 参数记录（**144 字节**），与 `Shaders/AgentDataCommon.hlsl` 的 `UnitTypeSettings` 逐字段一致。这是兵种参数进入 GPU 的**唯一通道** |
| `PipelineContexts.cs` | 每帧上下文：`PipelineFrameContext` + 嵌套的 Grid/TeamFlow/Lod 设置结构（组合式，单类型公共字段 ≤30） |
| `AgentStateMachine.cs` | GPU 状态语义的 C# 镜像规格（Dead 终态、优先级重推导），供测试/工具，不参与运行时 |
| `MassGpuBufferManager.cs` | **所有** ComputeBuffer/RenderTexture 的所有权：分配、零初始化、按兵种×LOD 分桶、三组双缓冲交换、统一释放 |
| `CombatBufferSet.cs` | compute-only 战斗缓冲（teamId/hp 读写对/target/cooldown/home/pendingDamage 读写对），与 AgentData 分离 |
| `ComputePipelineOrchestrator.cs` | 调度器：固定顺序派发 5 个阶段，uniform 上传，缺失 kernel 一次性报错；`IDispatchListener` 钩子供测试断言派发顺序 |
| `MassGpuShaderSet.cs` | 4 个 compute shader 引用 + kernel 索引；`IsValid`/`DescribeMissing` |
| `MassGpuShaderPropertyIds.cs` | 全部 Shader.PropertyToID 常量（分组注释） |
| `MassEngineManager.cs` | 场景入口 MonoBehaviour：生命周期、流场门控/节流、点击目标覆盖、遥测接线、LOD 模拟频率下发。编辑模式下 ContextMenu 只做配置校验不分配 GPU 资源 |
| `SimulationConfig.cs` / `MassEngineSystemConfig.cs` / `MassEngineShaderConfig.cs` | 全局配置资产类（世界尺寸/格子、系统配置聚合、shader 引用） |
| `Shaders/AgentDataCommon.hlsl` | 所有 kernel 共享的声明与工具函数（结构体、缓冲、采样、状态推导、邻域查询） |

## 游戏层运行时 API

- `StartBattle()` / `PauseBattle()`：继续或暂停；Pause 保留当前军团命令
- `StopBattle()`：停止并清除目标/导航覆盖
- `ResetScenario()`：重建初始 GPU 战场
- `SetFlowTargetOverride(teamId, point)`：设置队伍静态移动目标
- `SetTeamNavigationOverride(teamId, enabled, dynamicTargeting)`：运行时切换防守、静态移动或动态进攻条令

这些 API 只写 Manager 的运行时覆盖，不修改 `RuntimeFlowConfig`。

## 关键契约

- **调度顺序**（`DispatchFrame`）：SpatialHash → RuntimeFlow(条件) → DensityMap →
  EngagementSlotOccupancy → CombatSimulation → LodClassification(每兵种一次) → SwapSimulationBuffers。
- **双缓冲纪律**：hp / pendingDamage / agentPosition 均为"读上帧快照、写本帧目标、
  帧末交换"。绑定发生在每帧派发前，永远指向交换后的正确侧。
- **目标负载反馈**：战斗阶段复用 8 槽位占用的本帧汇总作为上一轮锁定负载，在既有空间哈希候选内做常数成本评分；不增加逐单位 CPU 工作或全局敌人扫描。
- **零初始化**：Allocate 后立即清零流场方向缓冲与网格计数；预览 RT 清为透明黑。
  任何 kernel 不得读到未定义显存。
- **重建守卫**：Manager 按分配签名（agentCount+gridCellCount+maxAgentsPerCell+
  flowFieldResolution+unitTypeCount）判断是否需要完整重建。

## 流场门控（Manager 内，三要素正交分解）

```
enabled  = 该队流场开关
reason   = 有目标（点击覆盖 > 配置目标）或动态寻的开启
cadence  = dirty（目标变更/初始化/StartBattle）立即重建
         | 动态模式按 dynamicFlowUpdateInterval 节流
         | rebuildRuntimeFlowEveryFrame 强制每帧
无目标且无动态时：dirty 仍会派发一次 Generate（GPU 侧显式清零，杜绝幽灵目标）
```

## 场景物理账本（ScenarioPhysics）

参数之间存在物理耦合：兵力 ↔ 阵地面积 ↔ 世界 ↔ 格子 ↔ 流场。`ScenarioPhysics.Evaluate`
是这本账的唯一权威：全局堆积密度、阵地越界、格子溢出、流场覆盖，任何一项越界都在
初始化时给出**带具体建议数值**的警告（而不是让超载场景以"莫名卡死"的方式报错）。
编辑器菜单 `MassEngine/Auto-Fit Scenario` 先按默认 50m 阵前间距重排 team 0/1 出生中心，
再用同一本账一键把 world/grid/flow 写成自洽值
（编辑器写资产合法、带 Undo；运行时只读契约不变）。

## 如何验证

- `Tests/EditMode`：stride 测试、字段预算、派发顺序（DispatchRecorder）、双缓冲交换
- 改动 `UnitTypeGpuSettings` 字段时**必须**同步 `AgentDataCommon.hlsl` 的
  `UnitTypeSettings` 并保持 16 字节对齐——stride 测试会拦住不一致

## 性能特征

- uniform 上传与缓冲绑定每帧全量执行（安全优先；如需进一步优化可做绑定缓存）
- settings 上传 = 兵种数 × 144B，可忽略
- frustum 平面数组为字段缓存，Update 路径零 GC 分配

## 2026-09-29 表现与弹道契约

- 战术 `currentState` 保持原枚举；`presentationState` 仅选 Idle/Move/Attack/Death，`locomotionSpeed` 来自最终位移。
- AgentData 56→64B，每10万单位增加约0.8MB GPU记录；不增加逐帧整表回读。
- UnitTypeGpuSettings 仍144B：六个预留槽改为移动参考速度/停走阈值/出手进度/发射与瞄准高度。只对该GPU契约允许36个数据字段，其余类型预算仍30。
- ProjectileGpuData 仍64B：预留8B中4B改为 sourceAgentIndexPlusOne（0未知，正数为索引+1）。
- Manager缓存出生scale和无地形时的脚底高度，用于CPU发射；运行中动态缩放/飞行不是本轮支持范围，改变这些数据需重建场景。
- 暂停保留目标、冷却、姿态与速度缓存；初始单位本来就是Idle，重置由ResetScenario执行，暂停不再冒充重置。

## 拥堵等待状态的存储契约（2026-09-29）

保持AgentData=64B、UnitTypeGpuSettings=144B、VAT布局和attackCooldownBuffer不变。DX11战斗kernel已有8个UAV，不再加第9个。

`CombatBufferSet.engagementSlotAssignmentBuffer`现在有`N * (1 + 12)`个32位word：前N项仍是原交战槽位int，后面每单位12项保存拥堵观察/等待与远程射界记录（浮点按位存入int）。尾部仅对应单位的战斗线程读写，不做双缓冲；初始上传清零尾部，死亡/重开清理，Release随原buffer释放。不要把该buffer.count当作兵力数，应使用AgentCount。

尾部顺序：anchor.xz、pathForward.xz、observedSeconds、remainingSeconds、commandRevision（原0～6偏移不变）；rangedStatus（占原reserved）、rangedAnchor.xz、rangedSeconds、rangedTargetPlusOne（最后一项原生int）。C# `CongestionWordsPerAgent=12`与HLSL步长同步，偏移`N + index*12`。相对原8word增加16B/单位，十万人另增约1.6MB；总尾部约4.8MB。这只是容量算术，不是FPS结论。

另有每军团一个int的只读`movementCommandRevisionBuffer`，由接受新命令时增量上传；版本循环于1～16777215，以便在GPU浮点记录中精确保存。常规运行不添加CPU逐单位回读，回读仅在测试探针中启用。

2026-09-30：正式Manager将兵种/寿命/间隔的有界弹道容量传入Allocate；默认底层API仍兼容N/4。详见Projectiles README。Agent64B、UnitType144B、弹体64B均未变；战斗kernel没有新增第9个UAV。
