# Projectiles

该模块为远程兵种提供 GPU 弹道积分、生命周期、命中检测和伤害累积。`projectileRange == 0` 的兵种继续走原有近战路径。

## 数据流

```text
Combat kernel 写 launchRequestBuffer
  -> CPU 异步读取请求/位置/目标快照并立即清空请求源
  -> ProjectileGpuManager 批量写入环形弹道池
  -> Projectile kernel 积分、扫掠碰撞、写 pendingDamage
  -> 下一帧 Combat kernel 结算伤害
```

弹道调度位于 Combat 之后、LOD 之前。战斗暂停时弹道不移动，生命周期也不推进。

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

这些值通过 `UnitTypeGpuSettings` 统一上传，不使用逐兵种 scalar uniform。

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
- 运行时关注 `TotalLaunched`、`ActiveCount` 和 `OverflowCount`。

## 已知限制

- 只检测指定目标，不处理途中命中或范围伤害。
- 发射后不制导，目标位置变化可能导致落空。
- 尚无弹道渲染、风阻、风场和复杂碰撞体。
- CPU 活跃数是保守估算，提前命中的槽位可能等到最大生命周期后才再次计入可用容量。
