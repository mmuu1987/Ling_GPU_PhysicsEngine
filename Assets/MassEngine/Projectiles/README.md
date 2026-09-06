# Projectiles

该模块为远程兵种提供 GPU 弹道积分、生命周期、命中检测和伤害累积。`projectileRange == 0` 的兵种继续走原有近战路径。

## 数据流

```text
Combat kernel 写 launchRequestBuffer
  -> CPU 异步读取请求/位置/目标快照并立即清空请求源
  -> ProjectileGpuManager 批量写入环形弹道池
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
maxLifetime + trailLength + padding
```

`targetAgentIndex == -1` 表示空槽。命中使用上一位置到当前位置的线段扫掠检测，避免高速弹道穿透。

## 配置

远程参数位于 `CombatConfig`：

- `projectileRange`：0 为近战，大于 0 为远程有效射程
- `projectileSpeed`：初速度
- `projectileGravity`：0 为直线，负值（如 `-9.8`）为向下重力
- `projectileHitRadius`：命中半径
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
  **不是** CPU 侧的 `ProjectileGpuManager.ActiveCount`（那是保守估算，命中后会晚于 GPU 释放）。
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

## 生命周期与容量

- buffer 由 `MassGpuBufferManager` 创建和释放，`ProjectileGpuManager` 只借用。
- 默认容量约为 `max(1, agentCount / 4)`；零 Agent 时不分配。
- Manager 使用单一环形游标，每批最多两次连续 `SetData`。
- CPU 以最大生命周期作为保守占用窗口；池满时丢弃新弹道、增加 `OverflowCount`，不会覆盖可能仍活跃的槽位。
- 每次最多消费 4096 个请求，避免突发请求无限放大主线程工作量。

## CPU/GPU 通信

动态请求处理使用三份 `AsyncGPUReadback`：发射计数、Agent 位置和目标索引。兵种索引与队伍信息来自初始化时的 CPU 缓存。该路径不会同步阻塞 GPU，但在几十万单位规模仍有明显带宽与扫描成本；后续优化方向是 GPU 压缩请求或 GPU 端槽位分配。

## 验证

- EditMode：64 字节布局、buffer 分配和释放。
- PlayMode：完整请求消费、实际命中伤害、生命周期释放、近战兼容、暂停冻结。
- PlayMode（渲染契约）：indirect instance count 非零、命中/过期当帧移出活跃列表、暂停数帧列表与位置稳定、
  清空后归零且能再次开战、缺渲染资源时只警告一次且仿真继续。断言的是 GPU 活跃索引、indirect args 和生命周期，
  不做像素级截图比较。
- PlayMode（冒烟）：`WarSandboxSmokeTests` 加载 `Assets/Game/Scenes/WarSandbox.unity`，开战→暂停→继续→重置→再开战，
  全程无异常日志且弹道能真正进入 indirect draw args。
- 运行时关注 `TotalLaunched`、`ActiveCount` 和 `OverflowCount`。

## 已知限制

- 只检测指定目标，不处理途中命中或范围伤害。
- 发射后不制导，目标位置变化可能导致落空。
- 尚无风阻、风场和复杂碰撞体。
- 曳光是单 pass 半透明四边形，没有拖尾贴图、发光、命中特效、音效和屏幕震动。
- `trailLength` 由兵种 `CombatConfig.projectileTrailLength` 提供，渲染时再由共享的
  `trailLengthScale` / `trailMinLength` 做统一缩放和下限保护。
- 曳光 `ZWrite Off`，互相之间不做深度排序；地形和 Agent 仍能正常遮挡它们。
- CPU 活跃数是保守估算，提前命中的槽位可能等到最大生命周期后才再次计入可用容量。
- 暂停战斗后的一两帧内，已经进入异步 readback 管线的发射请求仍会落盘成新弹道，屏幕上可能多出几条曳光。暂停期间不再产生新请求，所以这一漂移几帧内自行停止；弹道位置和生命周期从暂停那一刻就已经冻结。
