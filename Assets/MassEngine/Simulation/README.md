# Simulation — 战斗与运动主 Kernel

引擎的心脏：每帧对每个 Agent 做一次完整决策——伤害结算、寻敌、攻击、
状态推导、聚散合力、位移积分。全部在
`Shaders/AgentCombatSimulation.compute` 的 `SimulateCombatAndAccumulateDamage` 中。

## LOD 降频模拟（decision cadence）

近/中/远 LOD 层的 Agent 分别每 1/2/4 帧（LodConfig.simulationInterval，可调）执行一次
**决策段**（邻域扫描/寻敌/攻击/转向）；其余帧走轻路径：只结算伤害、按缓存速度积分位置、
写回缓冲。关键保证：

- **降频不降率**：冷却/转向力/分离冲量用补偿步长 dtSim = interval × dt 积分，
  攻击 DPS 与移动速度和全帧率完全一致（PlayMode 有击杀时刻一致性黄金测试锁定）
- **位置每帧积分**（真实 dt + 缓存速度）——远景没有位移步进感，视觉无损
- **伤害结算/死亡判定/缓冲写回每帧执行**（双缓冲交换的正确性要求）
- **冷却为累积制**（cooldown += interval 保留余数），任何步长下攻击周期精确等于 attackInterval
- 错峰按 64 线程组对齐（IsSimulationActiveFrame），整组同分支，GPU 真省时间；
  寻敌按**决策通道计数**分批（ShouldSearchForLocalTarget），与降频节奏解耦，
  任何 interval 下寻敌频率一致
- **巡航速度跨步长一致**：阻尼与转向 lerp 不可交换，转向采用精确 N 步复合闭式解
  v' = α^N·v + gain·T（α = damp×(1−steer)，N=1 时与逐帧公式逐位等价）——
  1/4 帧率下行军速度与全帧率严格一致（黄金测试 LodScaledSimulationPreservesTravelSpeed）

## 每帧决策流（单 Agent 视角）

```
1. 结算上帧伤害：hp = hpRead快照 - pendingDamageRead → 写 hpWrite   [每帧]
2. hp≤0 → Dead（终态，清目标/冷却/速度）并返回                      [每帧]
   ── 非激活帧到此走轻路径：位置积分+写回后返回 ──
3. 目标维护：现有目标失效则按决策通道计数分批（每 4 个决策通道一批）从空间哈希重新寻敌
4. 攻击判定：距离≤attackRange（或曾攻击且≤退出半径）→ Attack，
   冷却归零时 InterlockedAdd 伤害进目标的 pendingDamageWrite
5. 无目标 → 队伍行为：攻方采流场（零向量时吸引力兜底直奔配置目标）；
   守方按模式：HOLD 原地驻守（接战迟滞见语义要点）/ FLOW_FIELD 采守方流场
   （索敌只受 aggro 半径限制；旧的 chase 追击距离参数已整链删除）
6. 状态推导：ResolveAliveState（Attack > Engage > Move > Idle）
7. 合力与积分：分离+密度避让+车道偏置 → 转向/限速 → 位移 → 边界反弹
```

## 语义要点

- **伤害量化**：冷却累积制且**每决策通道结算全部到期攻击**（上限 4 次/通道）——
  LOD 降频下快攻单位的 DPS 不随镜头距离变化；每次攻击恰好 attackDamage；
  PlayMode 测试断言"损失恒为 attackDamage 整数倍 + 击杀不早于节奏下限"。
- **hp 双缓冲**：邻居看到的是上帧快照——本帧被打死的目标仍会吸收本帧伤害，
  确定性的 1 帧过量伤害，换 dispatch 内零竞态。
- **守方接战迟滞**：HOLD 守方保留已交战目标至 AttackExitRange（与攻方镜像）——
  没有它，攻守双方在 attackRange 线两侧的拥挤抖动会造成系统性单方面换血。
- **FLOW_FIELD 守方按 aggro 半径索敌**（锚定出生点的追击链已删除：被流场调离
  出生点的守军曾因此永久无法索敌）。
- **HOLD 守方保留分离力**：会互相推开解穿插，但位移被钳制在
  home 周围 defenderGuardRadius 内。
- 攻击接触中强阻尼 + 接触限速（8% maxSpeed），战线不滑步。

## 参数

- `CombatConfig`（按兵种）：targetAcquireRadius / attackRange / attackDamage /
  attackInterval / maxHp
- `MovementConfig`（按兵种）：maxSpeed / velocityDamping / flowFieldWeight /
  flowFieldResponsiveness / 配置流场目标
- `RuntimeCombatConfig`（全局）：defenderGuardRadius / deathClipDuration（无 VAT profile 时的兜底）

## 如何验证

`Tests/PlayMode/MassEngineGpuKernelTests.cs` 三条黄金值测试就是本模块的行为规格：
伤害节奏、状态合法性与优先级、未开战冻结。改本 kernel 前先跑它们。
