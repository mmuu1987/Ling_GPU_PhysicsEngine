# Projectiles

该模块为远程兵种提供 GPU 弹道积分、生命周期、命中检测和伤害累积。`projectileRange == 0` 的兵种继续走原有近战路径。

## 数据流

```text
Combat kernel 写 launchRequestBuffer
  -> CPU 异步读取请求/位置/目标快照并立即清空请求源
  -> ProjectileGpuManager 按确认空闲槽位批量散写弹道池
  -> Projectile kernel 积分、扫掠碰撞、写 pendingDamage
  -> CollectActiveProjectiles kernel 压缩活跃槽位到 activeProjectileIndexBuffer
  -> CopyCount 写 projectileDrawArgsBuffer 的 instance count
  -> ProjectileGpuRenderDispatcher 一次 DrawMeshInstancedIndirect 画曳光
  -> 下一帧 Combat kernel 结算伤害
```

弹道调度位于 Combat 之后、LOD 之前，`CollectActiveProjectiles` 紧跟在 `SimulateProjectiles` 之后，
所以本帧释放的槽位本帧就不再渲染。战斗暂停时弹道不移动，生命周期也不推进，但活跃列表照常重建：
池内容没变，列表结果一致，已有曳光冻结在屏幕上而不是闪灭。

## 数据契约

`ProjectileGpuData` 固定 64 字节，字段顺序必须与 `ProjectileSimulation.compute` 的 `ProjectileData` 完全一致：

```text
position + launchTime
velocity + damage
targetAgentIndex + sourceTeamId + hitRadius + gravity
maxLifetime + trailLength + sourceAgentIndexPlusOne + padding
```

`targetAgentIndex == -1` 表示空槽。命中使用上一位置到当前位置的线段扫掠检测，避免高速弹道穿透。

## 配置

远程参数位于 `CombatConfig`：

- `projectileRange`：0 为近战，大于 0 为远程有效射程
- `projectileSpeed`：直射时为速度；抛射时为水平速度上限；有界越顶净空可延长飞行时间，仍受最大寿命约束
- `projectileGravity`：0 为直线，负值（如 `-9.8`）为向下重力
- `projectileHitRadius`：单位碰撞模式下为弹体容差半径，叠加在目标身体胶囊外；不是人物半径
- `projectileMaxLifetime`：最大仿真时间
- `projectileTrailLength`：该兵种曳光的基础长度，渲染时再乘以共享的 `trailLengthScale`

这些值通过 `UnitTypeGpuSettings` 统一上传，不使用逐兵种 scalar uniform。

渲染参数位于独立的 `ProjectileRenderConfig`（`MassEngine/Projectile Render Config`），
由 `MassEngineSystemConfig.projectileRenderConfig` 引用：

- `renderProjectiles`：总开关，关掉只停止绘制，不影响仿真
- `mesh`：留空则用内置的单位 quad（不是错误路径），只在需要自定义曳光形状时才指定
- `material`：必需，指向 `ProjectileTrail.mat`
- `trailWidth` / `trailLengthScale` / `trailMinLength`：曳光宽度、按 `trailLength` 缩放的长度和长度下限
- `teamColors`：按 `sourceTeamId` 直接索引的调色盘（第 i 项 = 阵营 i），超出长度的阵营复用最后一项，空列表退到白色；
  允许 HDR（分量 > 1），曳光是细的半透明线条，亮度就是可读性
- `shadowCasting` / `receiveShadows`：默认都关，曳光是纯叠加视觉

配置资产在运行时只读，dispatcher 不回写任何字段。

## 渲染

`ProjectileGpuRenderDispatcher` 每帧发出一次 `Graphics.DrawMeshInstancedIndirect`：

- instance count 只来自 `projectileDrawArgsBuffer`，由 `ComputeBuffer.CopyCount` 从 append buffer 的计数器写入，
  **不是** CPU 侧的 `ProjectileGpuManager.ActiveCount`（那是CPU租约数，包含待上传和异步回收延迟）。
- `ProjectileTrail.shader` 用 procedural instancing：`setup()` 里 `activeProjectileIndices[unity_InstanceID]`
  取到槽位，直接从 `projectileBuffer` 读位置/速度/阵营/`trailLength`，手工写 `unity_ObjectToWorld` 与
  `unity_WorldToObject`。空闲槽位不会进入活跃列表，因此永远不会到达顶点阶段。
- 四边形沿飞行轴 billboard（`cross(dir, toCam)`，退化时回退到 `cross(dir, up)` 再回退 `(1,0,0)`），
  局部 +x 是弹头、`uv.x` 向尾部淡出，所以侧视也不会退化成看不见的薄边。
- 没有 GameObject、Transform、粒子系统，渲染路径上也没有 `GetData` 或新增 `AsyncGPUReadback`。
- draw args 的 mesh 部分只在 mesh 或 args buffer 变化时写一次；`SetArgs` 会顺带把 instance count 归零，
  所以那一帧主动跳过绘制——比放过一次过期计数便宜。
- 缺 material、缺 mesh 或 buffer 未分配时只打印一次警告并跳过绘制，仿真不受影响。
- `Bounds` 沿用 `MassEngineManager.ResolveRenderBounds()`，高度 ±60m，覆盖整个战场和合理弹道高度。
- `Release()` 销毁内置 quad 并清空缓存，`ResetScenario` / 重新分配 / 组件禁用后不残留曳光也不泄漏 buffer。

## 落点特效（可选，2026-10-01）

溅射弹（`splashRadius > 0`，例如巨龙火球）的可视化。**默认关闭**，旧场景和旧兵种的行为、内核变体、绘制次数都不变。

- 开启条件（两者都满足才分配资源）：
  - 场景 `ProjectileRenderConfig.impactMaterial` 非空（材质用 `Shaders/ProjectileImpact.shader`）；
  - 至少一个已注册兵种的 `CombatConfig.projectileSplashRadius > 0`。
- 数据：`ProjectileImpactFx` 持有容量 128 的环形缓冲（`ImpactData` 32 字节：位置、半径、时间戳、地面高度）和一个计数器。
  `ProjectileSimulation.compute` 在关键字 `MASS_PROJECTILE_IMPACT_FX` 下，每次溅射结算追加一条；满了就覆盖最旧的。
  关键字关闭时缓冲不声明，也就不需要绑定。
- 关键字：`ProjectileSimulation.compute` 是跨场景共享的资源实例，所以编排器每次派发前都会显式 `Bind` 或 `Unbind`。
- 绘制：`ProjectileGpuRenderDispatcher.DrawImpacts` 每帧一次 `DrawMeshInstancedProcedural`，实例数等于环形容量。
  着色器按 `_ProjectileImpactNow - time` 计算年龄，空槽和过期槽退化为零尺寸；没有回读，也没有 CPU 计数。
  - 地面火圈（UniversalForward pass，alpha 混合）：直径等于 2 × 溅射半径，约 0.15 × 时长内从 0.6 扩到 1.0 倍半径，
    也就是画面上的圆盘和实际受伤范围一致。
  - 闪光（SRPDefaultUnlit pass，加色混合）：持续时长的一半。URP 中同一个 LightMode 只渲染第一个 pass，所以第二个 pass 不能也用 UniversalForward。
  - 时间基准：`projectileSimulationTime`，也就是内核的 `currentTime`，暂停时特效冻结。
  - 默认颜色为 LDR 橙红：项目没有开启 Bloom，HDR 值会被截断成淡黄白色。
- 拖尾：`ProjectileTrail.shader` 的 fire 模式。溅射弹宽度取 `max(trailWidth, _SplashTrailWidth)`（默认 0.8），
  颜色为橙红色主体加弹头处的短亮芯。非溅射弹完全走原来的曳光路径。
- `impactDuration`：默认 0.6；巨龙场景（DragonBuilder `IntegrateFire05`）设为 0.9。
- 未接入：`unified-large` 自定义战场没有 impactMaterial，因此不显示特效（伤害照常）。

## 生命周期与容量（2026-09-30）

- 正式Manager按 `ProjectilePoolBudget` 预算：每个远程单位 `ceil(maxLifetime/attackInterval)+1` 槽、每单位最多8槽，保留原 `N/4` 下限，总数硬上限262144（仅64B弹体数据最多16MiB，另有索引/回读/CPU元数据）。不修改射速、伤害、射程或默认阵形；极端自定义配置仍可能排队。
- 无场景预算的底层Allocate调用保留原N/4默认值，测试可显式指定容量。小场景对照证明：回收修复后真正满池仍可限制持续抛射，因此不是只把固定容量随意调大。
- 借用的弹道池仍由 `MassGpuBufferManager` 释放；Manager只拥有空槽快照和批量上传暂存buffer。
- GPU实际命中/碰地/超时后，异步空槽快照回收CPU租约；代次防止旧战场回调，租约水位防止旧空槽结果回收刚发出的新弹体。失败回读不释放任何租约。
- 优先复用刚释放槽位；仅回读实际使用过的索引高水位，不按预算上限回读整池，也不回读64B弹体记录。稀疏槽位通过最多4096条一批的上传+scatter kernel写入，不产生逐箭SetData。
- 正式请求消费每次最多4096条；满池保留当前计数及尾部，后续继续排空，不覆盖在飞弹体。旧快照排空前GPU继续累计新请求。清空/重开/人数变化仍废弃旧请求代次。
- `TotalRequested` 是已经复制到CPU快照的请求数，不含GPU尚未采样的计数；`TotalLaunched` 是实际生成数；`ActiveCount` 是租约数，不是绘制数；`ReclaimedSlots` 是已确认回收数。
- `CapacityDeferralCount` 统计满池**暂停消费事件**，不是丢弹数量；`OverflowCount` 只计直接Launch API在满池下的拒绝。渲染仍只认GPU append/indirect args。

## CPU/GPU 通信

动态请求处理保留三份 `AsyncGPUReadback`：发射计数、Agent 位置和目标索引。兵种索引与队伍信息来自初始化时的 CPU 缓存。另有最多一份在途的紧凑空槽回读。生产路径不会同步阻塞 GPU，但在几十万单位规模仍有明显带宽与扫描成本；后续优化方向是 GPU 压缩请求或 GPU 端槽位分配。

三份结果各自在完成回调中复制到复用的 CPU 缓存，再等齐消费。不能等三份句柄都完成后才 `GetData`：
先完成的原生回读数据可能已过有效帧，在独立程序中会持续丢失发射请求。清空、重新初始化、人数改变及 Dispose
通过代次废弃旧回调，避免旧战场的请求写进新池；生产路径不做同步等待。

## 验证

- EditMode：64 字节布局、buffer 分配和释放。
- PlayMode：完整请求消费、实际命中伤害、生命周期释放、近战兼容、暂停冻结。
- PlayMode（渲染契约）：indirect instance count 非零、命中/过期当帧移出活跃列表、暂停数帧列表与位置稳定、
  清空后归零且能再次开战、缺渲染资源时只警告一次且仿真继续。断言的是 GPU 活跃索引、indirect args 和生命周期，
  不做像素级截图比较。
- PlayMode（冒烟）：`WarSandboxSmokeTests` 加载 `Assets/Game/Scenes/WarSandbox.unity`，开战→暂停→继续→重置→再开战，
  全程无异常日志且弹道能真正进入 indirect draw args。
- 大批量回归：5000+1 请求分次排空、后索引来源正确、排空期间新增 7 请求保留、无重复消费，以及清空部分排空快照后允许新发射。
- 落点特效（PlayMode）：
  - `ProjectileImpactFxGpuTests`（6 项）：溅射记录位置/半径/时间戳、单体弹不记录、特效变体下伤害不变、环形覆盖、默认值等于旧行为、着色器可编译；
  - `ProjectileImpactFxVisualTests`：通过真实 URP 相机渲染并统计火焰色像素，覆盖空、近、远、过期四种情况，证据图为 `Logs/AgentImpactFx/visual-*.png`；
  - `ProjectileImpactFxSceneTests`：加载 `Dragons/Prepared07/Integrated/dragonBattlefield.unity` 开战，在前 3 次命中时各截一张概览图和近景图，证据图为 `Logs/AgentImpactFx/scene-*.png`。
    batchmode 下 `WaitForEndOfFrame` 永远不会返回，所以测试都是先排队绘制，再手动 `camera.Render()`。
- 运行时关注 `TotalLaunched`、`ActiveCount` 和 `OverflowCount`。

## 已知限制

运行时由调度器启用 `MASS_PROJECTILE_UNITS`：平面/山地统一使用地表+兵种高度；
`projectileOriginHeight` 为发射高度，`projectileTargetHeight` 为瞄准高度/身体半高，均乘出生scale.y。
前向偏移近似取射向目标的方向与身体半径，不是骨骼枪口。新建的大型模型需按实际尺寸配置半高、半径和移动参考速度，不能仅换模型。
目标死亡/变阵不抹除已经生成的在飞弹体；命中、触地或超时才释放。无效地形源/目标仍拒绝发射。
抛物线位置用 v*dt+0.5*g*dt²，和CPU求解器一致。三种现役远程模板统一为12m/s水平速度、-9.8重力、0.08弹体半径；直射gravity=0仍支持。

单位胶囊与地形按最早接触排序；友军挡弹不受伤、途中敌军可先于原目标受伤，发射者排除。
候选来自扫掠段外扩最大身体半径后的空间格，使用与构格一致的位置快照，不做全军常规扫描。
**桶溢出**时，仅受影响的扫掠退回精确全表检查，现有spatialHashStats会报告溢出；这条异常路径正确但昂贵，需避免高密度容量不足。
独立的旧目标/地形探针可不启用该keyword以隔离地形数学回归；真实运行与新的端到端测试启用单位碰撞。

- 仍不处理范围伤害、骨骼/网格精确碰撞，运动单位碰撞使用一帧位置快照。
- 每帧扫掠是一条弦，不解析整条抛物线与地形的精确交点；大步长可能偏保守。
- 发射后不制导，目标位置变化可能导致落空。
- 尚无风阻、风场和复杂碰撞体。
- 曳光是单 pass 半透明四边形，没有拖尾贴图、发光、命中特效、音效和屏幕震动。
- `trailLength` 由兵种 `CombatConfig.projectileTrailLength` 提供，渲染时再由共享的
  `trailLengthScale` / `trailMinLength` 做统一缩放和下限保护。
- 曳光 `ZWrite Off`，互相之间不做深度排序；地形和 Agent 仍能正常遮挡它们。
- CPU租约晚于实际GPU释放一个异步回收窗口；总预算不是实际在飞数，也不是内存总占用。
- 暂停后，已经进入异步回读/CPU快照队列的请求仍可能落盘成新弹道，位置和生命周期不推进。常规小批次只有少数帧；超过 4096 的集中齐射可能分更多帧排空，不能承诺固定一两帧。暂停期间 GPU 不再产生新请求。
- 保留超额请求避免静默丢失，但大批量下会增加释放延迟；这仍是粗略同步、有限吞吐方案，不是无限容量或帧精确同步。

## 越顶净空与射界

CPU `ProjectileBallistics` 与GPU `RangedClearance.hlsl` 使用同一飞行时间公式，有原生GPU数学一致性测试。
净空随身体半高/出生scale及水平距离增长，限制0.5～12米；高弧飞行时间不超过寿命约束。近射和20米紧密前排分别有实际胶囊碰撞A/B测试，不默认穿友军或地形。零重力保持直射。
战斗kernel会预检整条有界采样轨迹，沿用实际球/胶囊与地形扫掠；可命中途中敌人，友军/地形先接触则受阻。
最多16段、每段最多64格、每次预检最多1024候选读取；桶溢出/超预算保守等待，绝不为预检全军扫描。已释放弹体仍保留原精确溢出fallback。
初次/换目标立即判断，随后约24仿真帧错峰更新缓存；移动障碍可能在缓存间隔中进入射线，因此真实碰撞仍是最终裁决，不承诺零友军拦截。
射界受阻时的有界侧移、Hold/Move优先级及存储见Simulation/Core README。尚未做100k性能/全尺寸巨型生物验收。
