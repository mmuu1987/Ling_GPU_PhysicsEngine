# MassEngine

MassEngine 是以 Compute Shader 为核心的海量单位模拟引擎。逐帧行为主要在 GPU 完成；C# 只负责只读配置解析、buffer 生命周期、参数上传、管线调度、渲染提交与低频遥测。

## 模块

| 目录 | 职责 |
|---|---|
| `Core` | 数据契约、buffer 所有权、管线调度、场景入口 |
| `UnitTypes` | 兵种组合、模块接口和配置校验 |
| `Spatial` | 空间哈希和邻域查询 |
| `FlowField` | 按 teamId 分区的多军团流场、动态目标和静态障碍 |
| `Crowd` | 分离、密度压力和车道偏置参数 |
| `Simulation` | 战斗、状态与移动主 kernel |
| `Projectiles` | 远程发射请求、弹道积分和命中伤害 |
| `VatRender` | VAT 动画、LOD 分类和间接绘制 |
| `Diagnostics` | AsyncGPUReadback 遥测与 HUD |

游戏场景、编队和命令位于 `Assets/Game/`，不属于引擎程序集。

## 每帧管线

```text
CPU: 刷新兵种参数 -> 构造帧上下文 -> 发起绘制/低频回读
GPU: SpatialHash -> RuntimeFlow(按需) -> DensityMap(按需)
   -> Combat -> Projectile -> LOD -> 交换 position/damage/hp 双缓冲
```

弹道发射请求采用异步快照：提交回读后立即清空源计数器，避免回读等待期间的新请求被覆盖。弹道与战斗共享独立仿真时钟，暂停时不推进。

## 核心契约

1. 队伍身份只来自 `teamIdBuffer`，kernel 不得从索引区间推断阵营。
2. 兵种参数统一通过 `UnitTypeGpuSettings` 上传；C# 与 HLSL 字段顺序和 stride 必须一致。
3. `ScriptableObject` 是只读输入，运行时状态不得写回配置资产。
4. `MassGpuBufferManager` 是 GPU buffer 的唯一所有者；消费者只借用，不释放。
5. position、pendingDamage、hp 在帧末统一交换，新增 pass 必须明确读写哪一侧。

## 新增兵种

创建 `UnitTypeConfig` 及 Spawn/Movement/Flocking/Combat/Animation/Render 子配置，加入 `ScenarioConfig.unitTypes`。只有需要替换默认模块行为时才继承 `UnitTypeBase` 并覆写 `CreateModules()`；核心管线和 shader 不应按兵种增加分支。

## 验证

- EditMode：数据布局、配置、注册表、状态规则和调度顺序。
- PlayMode：真实 GPU kernel、伤害节奏、流场、LOD、弹道命中/过期/暂停。
- 运行时：`BattleTelemetryHUD` 查看存活数、空间统计、流场重建及溢出。
- 性能：以 `Assets/Game/PerformanceBaseline.md` 为基线重新采样，不以编辑器体感代替数据。

## 当前边界

- 流场和战斗身份按 `teamId` 支持多军团；游戏层当前以 `teamId` 直接对应军团，暂不支持结盟。
- LOD 会降低远处 Agent 的决策频率，因此结果受 LOD 中心影响，并非镜头无关的严格确定性模拟。
- 弹道池固定为 Agent 数量约 25%，满池时丢弃新请求并记录溢出。
- 弹道请求仍需回读三份动态数组；更大规模应改为 GPU 端压缩或完全 GPU 分配。
- 静态障碍目前是有限数量的 XZ 矩形；三维导航仍处于方案阶段。

各模块实现细节见其目录下的 `README.md`。
