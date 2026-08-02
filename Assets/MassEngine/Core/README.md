# Core — 数据契约、缓冲所有权与管线调度

引擎的地基。其它所有模块都依赖这里定义的数据布局与调度顺序；反方向依赖不存在。

## 职责

| 文件 | 职责 |
|---|---|
| `AgentData.cs` | Agent 主结构体（**56 字节**，Sequential）：position/rotation/scale/velocity/state/animTime。与 Stage6 布局二进制兼容，测试锁定 stride |
| `UnitTypeGpuSettings.cs` | 按兵种 GPU 参数记录（**112 字节**），与 `Shaders/AgentDataCommon.hlsl` 的 `UnitTypeSettings` 逐字段一致。这是兵种参数进入 GPU 的**唯一通道** |
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
  CombatSimulation → LodClassification(每兵种一次) → SwapSimulationBuffers。
- **双缓冲纪律**：hp / pendingDamage / agentPosition 均为"读上帧快照、写本帧目标、
  帧末交换"。绑定发生在每帧派发前，永远指向交换后的正确侧。
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
- settings 上传 = 兵种数 × 112B，可忽略
- frustum 平面数组为字段缓存，Update 路径零 GC 分配
