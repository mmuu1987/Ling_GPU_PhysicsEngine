# Simulation — 战斗与运动主 Kernel

引擎的心脏：每帧对每个 Agent 做一次完整决策——伤害结算、寻敌、攻击、
状态推导、聚散合力、位移积分。全部在
`Shaders/AgentCombatSimulation.compute` 的 `SimulateCombatAndAccumulateDamage` 中。

## LOD 降频模拟（decision cadence）

近/中/远 LOD 层的 Agent 分别每 1/2/4 帧（LodConfig.simulationInterval，可调）执行一次
**决策段**（邻域扫描/寻敌/近战/转向）；其余帧走轻路径：结算伤害、推进远程周期、按缓存速度积分位置、
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
4. 攻击判定：
   - 近战模式（projectileRange ≤ 0.01）：距离≤attackRange 时 InterlockedAdd 伤害
   - 远程模式（projectileRange > 0.01）：距离≤projectileRange 时写入 launchRequestBuffer
   - 冷却归零时触发攻击，冷却累积制保证 DPS 一致
5. 无目标 → 队伍行为：攻方采流场（零向量时吸引力兜底直奔配置目标）；
   守方按模式：HOLD 原地驻守（接战迟滞见语义要点）/ FLOW_FIELD 采守方流场
   （索敌只受 aggro 半径限制；旧的 chase 追击距离参数已整链删除）
6. 状态推导：ResolveAliveState（Attack > Engage > Move > Idle）
7. 合力与积分：分离+密度避让+车道偏置 → 转向/限速 → 位移 → 边界反弹
```

## 语义要点

- **伤害量化**：冷却累积制且**每决策通道结算全部到期攻击**（上限 4 次/通道）——
  LOD 降频下快攻单位的 DPS 不随镜头距离变化；每次攻击恰好 attackDamage；
  PlayMode 测试断言「损失恒为 attackDamage 整数倍」，并逐帧校验全军伤害不超过节拍上界
  （agents × damage × (elapsed / interval + 1)）——与攻击者如何分配目标无关，任何「忽略 attackInterval、每帧都打」的回归都当帧失败。
- **hp 双缓冲**：邻居看到的是上帧快照——本帧被打死的目标仍会吸收本帧伤害，
  确定性的 1 帧过量伤害，换 dispatch 内零竞态。
- **守方接战迟滞**：HOLD 守方保留已交战目标至 AttackExitRange（与攻方镜像）——
  没有它，攻守双方在 attackRange 线两侧的拥挤抖动会造成系统性单方面换血。
- **FLOW_FIELD 守方按 aggro 半径索敌**（锚定出生点的追击链已删除：被流场调离
  出生点的守军曾因此永久无法索敌）。
- **HOLD 守方保留分离力**：会互相推开解穿插，但位移被钳制在
  home 周围 defenderGuardRadius 内。
- 攻击接触中强阻尼 + 接触限速（18% maxSpeed），既抑制战线滑步，也为友军分离保留脱离重叠的速度余量。
- 每个目标维护 8 个带帧戳的交战槽位占用计数；单位优先保留当前槽位，明显过载时才切换。
- 本地寻敌按归一化距离、上一帧目标槽位总负载和稳定的单位/目标偏好评分；仅当当前目标过载且候选显著更优时切换，附近只剩一个敌人时不受负载上限阻断。
- 切换阈值是**每 agent 独立**的迟滞余量（`0.18 + Hash01(agentIndex) * 0.55`），不是全军共享常量。共享常量下整组会在同一个寻敌拍上做出相同判断：满载槽位的 0.45 惩罚大于同批候选之间的距离差，于是全军作为一个整体在两个目标之间每 `LOCAL_TARGET_SEARCH_INTERVAL` 帧翻转一次，永不收敛。分散阈值让最容易换的先走，它们腾出的占用把负载比压回 1.0 以下，其余单位就地留下。余量由 agent 索引派生，因此确定、无额外 buffer 读取，也不破坏 warp 内的分支一致性。
- 友军密度图作为流场的逐帧局部拥堵代价，在不重建整张流场的情况下选择相邻低负载通道。

## 参数

- `CombatConfig`（按兵种）：targetAcquireRadius / attackRange / attackDamage /
  attackInterval / maxHp / projectileRange / projectileSpeed / projectileGravity /
  projectileHitRadius / projectileMaxLifetime
- `MovementConfig`（按兵种）：maxSpeed / velocityDamping / flowFieldWeight /
  flowFieldResponsiveness / 配置流场目标
- `RuntimeCombatConfig`（全局）：defenderGuardRadius / deathClipDuration（无 VAT profile 时的兜底）

## 如何验证

M6.2 地形变体 `MASS_TERRAIN_ENABLED` 对近战/射程使用三维距离；近战接触还要求地表导航段可通行。
决策帧与 LOD 轻帧均走同一贴面积分/禁行穿格检查，速度按三维步长计；暂停、死亡也锚定地表。
平面变体保留原路径。`MassEngineGpuKernelTests.Terrain.cs` 通过真实流水线检验上/下坡、绕行、低频模拟、
三维近战距离与释放；共享地形契约见 [Terrain](../Terrain/README.md)。

`Tests/PlayMode/MassEngineGpuKernelTests.cs` 三条黄金值测试就是本模块的行为规格：
伤害节奏、状态合法性与优先级、未开战冻结。改本 kernel 前先跑它们。

## 2026-09-29 远程粗同步与暂停

近战仍按决策通道补偿结算。远程改为每帧轻量推进原有冷却，正常周期只在出手阶段累积一次请求；单帧最多补4次，余数继续保留。
`attackReleasePhase` 默认0.55；首次进入Attack补前摇，后续以attackInterval保持持续节奏。VAT时间由同一冷却映射，异步CPU生成仍允许数帧延迟。
进入/退出攻击、死亡、暂停不借用新的表现状态改变寻敌/队伍条令。暂停保留周期并冻结动画，继续不会获得免费首箭。
新增回归在 `MassEngineGpuKernelTests.Presentation.cs`，具体测试结果见本轮交接，不复用历史全绿数字。

远程计时沿用引擎 SafeDt 的单步50ms上限，与近战/移动一致；严重掉帧时不让远程相对其他单位变相加速。

## 2026-09-29 拥堵等待（专项）

- 非 Attack、非 Hold 且有行进路径的单位：现有邻域扫描确认近处前方友军，沿当前路径方向观察约0.4秒的**净推进**，不是把左右抖动的路程累加。
- 净推进过小则等待1.1～2.0秒，按单位索引稳定错峰；关闭主动推进/密度侧推，仅保留限速0.08×出生横向scale的真实重叠修正，并明确显示Idle。
- 每帧按SafeDt倒计时，时间到再尝试；前方持续阻塞会重新确认后再次等待。路径大幅转向重置观察窗口，没有前方友军或有正常推进不会只因人多而等待。
- 接受新军团命令时调用`MassEngineManager.NotifyMovementCommand(teamId)`；同点重发也递增该队版本。只唤醒该军团，拒绝的命令不更新版本，追加未激活路点不打断当前等待；切换实际路段会更新。
- 版本变化/等待到期可以打断LOD轻帧，但额外决策的dtSim与转向补偿预算为0，不重复结算近战时间；还避免了零预算时的pow(0,0)。正常移动由下一次常规决策继续推进。
- Pause冻结等待；Attack优先；死亡、Hold、无行进方向和重开清理等待。此状态不覆盖战术Move/Engage，也不抹去目标命令。
- `CongestionWaiting.hlsl`集中实现策略。距离/推进阈值考虑出生体型，但仍受现有局部邻域、桶容量和软分离限制，不是全局交通预约或大型生物的完整碰撞改造。
- 测试：`MassEngineGpuKernelTests.Congestion.cs`；独立玩家`--terrain-cycle-phase=congestion`在真实512人场景下发Move/Hold命令观察自然拥堵。参数使用与motion-review相同的全新隔离输出/方案/设置路径；30FPS功能模式，不是性能成绩。

## 2026-09-30 远程射界与有限侧移

- 射程内先检查CPU同式的抛物线/直线是否具备有效射界；受阻不持续空播Attack。原有攻击前摇、间隔、50ms安全上限与LOD每帧计时仍保留，侧移不产生额外攻击预算。
- 自动进攻时，初次受阻会在两侧有限候选位置预检射界，只有一侧可射则优先该侧，否则用稳定索引方向；后续不反复左右换向。只尝试约2秒，主动侧移半径为4倍身体半径、钳制0.5～4米，速度上限0.8×横向scale。
- 达到时间/距离预算即停主动推进，保留0.08×scale以内的真实接触修正并显示Idle；继续错峰检查射界，环境变通畅可恢复射击。慢速解穿插不计入主动侧移距离承诺。
- 侧移遵守原拥堵等待和地形导航/禁行；Hold不被自动侧移拉走。明确Move/路径命令使用自身流场，不受旧自动侧移预算阻止；新命令、换目标、出射程、死亡和重开清理相应缓存。暂停冻结。
- 首次预检/有限两侧候选与缓存重检都有固定候选预算；超预算保守受阻。这不是全局寻路找射位、全军换排或边跑边无限发射。
- 原拥堵等待0～6字偏移与语义保持；共享UAV尾部扩为12字，避免DX11新增UAV。原生回归覆盖拥堵11项、远程节拍/暂停/LOD、抛射/直射/真实碰撞、侧移后射击及新Hold/Move取消旧搜索。
