# 骑兵鞍位 + 巨龙火球溅射（2026-09-30 第二轮试玩反馈）

## 结论
- 可用构建：`Builds/UnifiedRoster-20260930-05`（启动 `Start-Roster.cmd`）
- `Builds/UnifiedRoster-20260930-04` 是火球第一版（高抛弹道，打不中移动目标），**已作废，不要使用**。

## 1. 骑兵
- 产物：`Generated/MountedKnight03`，集成目录：`Assets/Game/Cavalry/Prepared04`
- 骑手前移 0.62m，从马躯干骨骼（z=-0.97，偏臀部）挪到马肩隆后方（z≈-0.35）。依据见 `Logs/AgentCavalrySeat/horse-profile.txt`。
- 蓝色平板（原来是鞍毯）已去掉，换成棕色皮鞍：深红棕窄边鞍毯、皮革鞍座、前鞍桥、后鞍桥。尺寸随马体缩放。
- 马体 3.5m、骑手 1.95m 均不变。整体高度 4.219m（原 4.289m，新鞍位处背线更低），VAT 误差 1.956mm（上限 2.5mm），自动检查通过。
- `CavalryBuilder` 的新开关默认关闭，旧产出可原样复现。

## 2. 巨龙火球
- 引擎核心新增可选字段 `CombatConfig.projectileSplashRadius`，默认 0，所有旧单位行为不变（用户已批准）。
- 溅射规则：
  - 火球停下时结算，命中单位和落地都算，超时消失不算。
  - 半径内每个存活敌人受一次全额伤害；直接命中的目标不重复受伤。
  - 无友伤，不伤射手，不伤尸体。
- 巨龙参数：

| | 飞龙 | 进化巨龙 |
|---|---|---|
| 溅射半径 | 3.0m | 3.5m |
| 射程 | 14m | 14m |
| 索敌半径 | 20m | 20m |
| 弹速 | 18，直线（重力 0） | 18，直线（重力 0） |
| 出手高度 | 3.8m | 3.4m |
| HP / 攻击 | 不变 | 不变 |

- 第一版用了重力 -4，引擎自动高抛，顶点约 10m，飞行约 2.8s，骑士早已跑出溅射范围。击杀 16 名骑士用时 55s，比近战还慢，因此作废。
- 第二版改为直线火球，飞行约 0.8s，没打中也会飞到地面触发溅射。击杀用时：飞龙 37.7s、进化巨龙 39.2s，与原近战版（37.2s / 40.1s）持平。
- 没有爆炸粒子特效，只有拖尾（拖尾宽度是全局设置，未改）。
- 冒烟场景的 16 名守方骑士站得很分散，很难观察到一发打多人。群体溅射由 GPU 测试直接验证。

## 3. 测试
- 溅射及弹道 PlayMode：40/40 通过，新增 `ProjectileSplashGpuTests` 5 项。
- 实机串行 5 个用例（飞龙首轮/重载、进化巨龙、骑兵、全兵种）：全部通过，见 `Logs/AgentDragonFire/batch-player-receipt-04.json`。
- 既有失败，与本次改动无关：
  - 内核套件 66/67：`UnclearableBodyWaitsWithinBudget…` 与执行顺序有关，原版代码同样以相同数值失败，已做基线对照。
  - EditMode 166/168：`MassEnginePropertyTests` 两项 NRE，位于未改动的 `ComputePipelineOrchestrator.cs:396`。
- `batch-player-receipt-03.json` 是脚本路径写错造成的空跑失败，没有启动游戏，已用 04 重跑。

## 改动文件
- 核心（用户批准的最小扩展）：`CombatConfig.cs`、`ProjectileGpuData.cs`、`ProjectileGpuManager.cs`、`ProjectileGeometry.hlsl`、`ProjectileUnitCollision.hlsl`、`ProjectileSimulation.compute`、`MassEngineManager.cs`、`UnitTypeRegistry.cs`。原版备份在 `Logs/AgentDragonFire/pre-edit/`。
- 新增测试：`Assets/MassEngine/Tests/PlayMode/ProjectileSplashGpuTests.cs`
- 编辑器：`CavalryBuilder.cs`（Prepare04 / HorseProfile）、`DragonBuilder.cs`（IntegrateFire01/02、BuildFire01/02）
- 未 commit、未 push。
