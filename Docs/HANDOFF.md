# HANDOFF：角色库 / 骑兵 / 巨龙 / 火球溅射（截至 2026-10-01）

> **历史专题 / 入口提示（2026-10-03）：** 本页保留该阶段原始记录；当前正式入口已由 Version08 取代，见[工程导航](Engineering-20261003/README.md)与最新机器人交付记录。下文“当前”“下一步”及构建命令不作为现在的执行清单。旧手工测试仍暂停，其他模型转换未授权；技术结果与人工认可范围按原记录保留。


新对话先读本文件，再读 `Docs/Overnight-20261001/PROGRESS.md` 了解最新进展。

## 项目硬规则
- 不 push，不 `git add .`，不代签 V1。10-01 用户已授权按功能分组提交（commit），见 `Docs/Overnight-20261001/COMMIT-PLAN.md` 末尾的执行记录。
- 不覆盖已有构建包和生成目录，新产物用新名字（Prepared0N、Generated/X0N、Builds/…-NN）。
- 测试串行、低负载，不跑 10 万人压测。
- 不改 Assets/pelican-cycling.svg。
- 不改旧 C#、公共管线和核心代码。例外：用户批准过"最小可选扩展"，要求新增字段默认值等于旧行为，并配测试。
- Unity：`D:/soft/Unity6/6000.3.14f1/Editor/Unity.exe`，以 batchmode 运行。
  - PlayMode GPU 测试不能加 `-nographics`；用 `-runTests` 时不要加 `-quit`。

## 当前可用构建
`Builds/UnifiedRoster-20260930-14`（10-01 按用户决定制作：骑兵伤害 45 能赢 2 对 16；进化巨龙方阵 150 人；巨人保持不变。集成链为 Cavalry/Prepared07 → Dragons/Prepared13 → Giants/Prepared02）。
- -13：巨人首版，骑兵伤害 30，方阵 160 人。原先对 -13 的描述：`Builds/UnifiedRoster-20260930-13`（启动 `Start-Roster.cmd`），在 -12 的基础上加了两个地面巨人战场（巨型恶魔 / 巨型暴龙 vs 16 骑士），详见 `Docs/Overnight-20261001/GIANTS.md`。
- -12：内容与 -13 相同，只是没有巨人。原先对 -12 的描述：包含火球落点火圈、橙红拖尾、两个"巨龙 vs 密集方阵"战场，以及骑兵冲锋（6 m/s，首击 ×3）。以下旧包已被取代：
- -11：骑兵没有冲锋；
- -08：没有方阵战场；-09、-10：方阵战场的简介写死了胜方，与实测不符；
- -06：没有落点特效；
- -07：落点特效使用 HDR 配色，没有 Bloom 时显示为淡黄色，看不清；
- -04：高抛火球，打不中移动目标；
- -05：带皮鞍、飞龙无身体。

## 数据链
- 骑兵：`Generated/MountedKnight04`
  - 马高 3.5m，骑手 1.95m，鞍位前移 0.62m，无鞍、无马镫，保留缰绳。
  - 集成目录 `Assets/Game/Cavalry/Prepared06/Integrated`，编辑器方法 `CavalryBuilder.Prepare06`（带冲锋：maxSpeed 6、moveAnimationSpeedMax 1.75、chargeDamageMultiplier 3）。Prepared05 是无冲锋的旧版。详见 `Docs/Overnight-20261001/CHARGE.md`。
- 巨龙：集成目录 `Assets/Game/Dragons/Prepared12/Integrated`，方法 `DragonBuilder.IntegrateFire09` / `BuildFire09`（与 Fire08 相同，只是骑兵换成 Cavalry/Prepared06）（模型沿用 `PrepareYoung03` 的产物；方阵战场由 `IntegratePhalanx` / `AddPhalanx` 生成，详见 `Docs/Overnight-20261001/PHALANX.md`）。
  - Prepared05–07、09–11 是旧集成；Prepared08 是失败的半成品。都保留不删。

| 战场 | 模型 | 翼展 | 碰撞半径 | HP / 攻击 | 溅射 | 出手高度 |
|---|---|---|---|---|---|---|
| 飞龙 | `Dragon_EvolvedYoung03`（进化巨龙模型缩到 78%） | 5.8m | 3.4 | 1000 / 60 | 3m | 2.66 |
| 进化巨龙 | `Dragon_Evolved02` | 7.4m | 4.2 | 1500 / 90 | 3.5m | 3.4 |

  - 两者火球参数相同：射程 14，索敌 20，弹速 18，直线（重力 0），hitRadius 0.6，拖尾长 3.5。
  - Quaternius 原版 `Dragon` 是一颗长翅膀的头（没有身体），不要再用。
- 巨人：`Generated/Demon01`、`Generated/Dino01`，集成目录 `Assets/Game/Giants/Prepared01/Integrated`（基于 Dragons/Prepared12，额外加 2 个战场和 2 个模板），方法 `GiantBuilder.PrepareAll01` / `Integrate01` / `Build01`。源文件在 `Assets/CharacterPilotSource/QuaterniusGiants/`（CC0）。测试：`GiantBattlefieldTests`。
- 菜单链：巨龙 Catalog 包含骑兵 Prepared06 场景，以及 NonhumanBatch2 等旧战场。

## 核心扩展：火球溅射（`CombatConfig.projectileSplashRadius`，默认 0）
- 改动文件：`CombatConfig.cs`、`ProjectileGpuData.cs`、`ProjectileGpuManager.cs`、`ProjectileGeometry.hlsl`、`ProjectileUnitCollision.hlsl`、`ProjectileSimulation.compute`、`MassEngineManager.cs`、`UnitTypeRegistry.cs`。
- 原版备份在 `Logs/AgentDragonFire/pre-edit/`；溅射半径占用弹体原来的 padding 槽位，结构体仍为 64 字节。
- 结算规则：
  - 弹体停下时结算，命中单位或落地都算，超时消失不算；
  - 敌方存活单位 XZ 距离 ≤ 半径 + agentRadius×scale 时，受一次全额伤害；
  - 直接命中的目标不重复受伤；无友伤，不伤射手和尸体；
  - 格子溢出时退回精确全量遍历。
- 测试：`Assets/MassEngine/Tests/PlayMode/ProjectileSplashGpuTests.cs`，共 5 项；溅射与弹道相关合计 40/40 通过。
- 注意：重力小于 0 时引擎会自动高抛（`ProjectileBallistics.Clearance`），飞行时间很长，巨龙必须用重力 0。

## 核心扩展：火球落点特效（`ProjectileRenderConfig.impactMaterial`，默认 null）
- 新文件：`Projectiles/ProjectileImpactFx.cs`、`Projectiles/Shaders/ProjectileImpact.shader`。
- 改动文件：`ProjectileRenderConfig.cs`（新增 impactMaterial、impactDuration）、`ProjectileGpuRenderDispatcher.cs`（DrawImpacts）、`ComputePipelineOrchestrator.cs`（ImpactFx）、`MassEngineManager.cs`（SyncProjectileImpactFx）、`ProjectileSimulation.compute`（`MASS_PROJECTILE_IMPACT_FX`）、`ProjectileTrail.shader`（fire 模式、`_SplashTrailWidth`）。
- 原版备份：`Logs/AgentImpactFx/pre-edit/`，附 sha256 清单。
- 设计与验证详见 `Assets/MassEngine/Projectiles/README.md` 的"落点特效"章节。
- 测试：PlayMode `Projectile` 过滤 58/58 通过；实机 players07 在 -08 包上 5/5 通过。
- 注意：游戏 smoke 流程里 `combat-contact.png` 在首个伤亡时截图，常常错过 0.9 秒的火圈，看不到不代表没画。要看特效，请用 `ProjectileImpactFxSceneTests` 的截图。

## 核心扩展：骑兵冲锋（`CombatConfig.chargeDamageMultiplier`，默认 1）
- 新文件：`Simulation/MeleeChargeParams.cs`。改动文件：`CombatConfig.cs`、`UnitTypeRegistry.cs`（FillMeleeCharge）、`MassEngineManager.cs`（SyncMeleeCharge）、`ComputePipelineOrchestrator.cs`（MeleeCharge）、`AgentCombatSimulation.compute`（`MASS_MELEE_CHARGE`）。
- 规则：近战兵种在上一决策帧未处于攻击状态、且速度 ≥ `chargeMinSpeedFraction`×maxSpeed 时，本次命中额外加 (倍率−1)×attackDamage。关键字只在派发期间打开，派发后立即关闭。
- 原版备份：`Logs/AgentCharge/pre-edit/`。测试：`Assets/MassEngine/Tests/PlayMode/MeleeChargeTests.cs`（3 项）。

## 既有失败
- 已修复（第 5 项）：内核 `UnclearableBodyWaitsWithinBudget…`（夹具 SetUp 补齐字段重置）、`MassEnginePropertyTests` 两项 NRE（orchestrator 补一行判空）。内核套件现为 67/67。
- 仍在失败：EditMode 的 Game 测试 `WarSandboxCatalogTests.ShippingCatalogAndEntrySceneAreIncludedAndHaveUniqueIds`、`WarSandboxLaunchPresetTests.QuickDefault…110kContent`。原因是工作区里 09-27/09-29 就已存在、尚未提交的 M7.x 修改（`EditorBuildSettings.asset` 换成 M71 LaunchPresets 场景，Settings 下 `*_Render`/`*_Combat` 资产有改动），不是本轮造成的，也没有动。

## 脚本
- `Logs/AgentCavalrySeat/run-unity.cjs <CavalryBuilder|DragonBuilder|GiantBuilder> <method> <marker> <tag>`
  - 会校验 3046 个受保护文件；tag 不可重复使用。
- `Logs/AgentDragonFire/run-tests.cjs <PlayMode|EditMode> <filter> <tag>`：tag 不可重复使用。
- `Logs/AgentCharge/run-tests.cjs <PlayMode|EditMode> <filter> <tag> [看门狗秒数]`：同上，日志写在 Logs/AgentCharge。
- PlayMode 测试的 `UnityTearDown` 只能在本测试确实加载过战场时卸载场景；纯 `[Test]` 之后卸载会把测试运行器的场景一起卸掉，整个运行卡死。另外，Unity 日志是缓冲写入的，被看门狗杀掉时末尾会丢失；需要可靠的检查点时，用 File.AppendAllText 写单独的文件。
- 实机测试：`Logs/AgentDragonFire/players0N.cjs`，最新为 12，对应包 -14（11 对应 -13），共 7 个用例：原 5 个加 giant-demon/giant-dino 两个 seed；审计里的期望 id 映射已支持 `giant-*`。可以用本地的 mkplayers.py 生成下一轮脚本。复制时要把 receipt、log、Builds 路径和 -0N 后缀全部替换。
- 用例：dragon seed/reload、dragon-evolved seed、cavalry seed、giant-demon seed、giant-dino seed、all seed。
- 平衡和方阵测试：`Assets/MassEngine/Tests/PlayMode/DragonPhalanxBalanceTests.cs`，只改运行时复制的配置。注意：测试里要自己下达攻击命令，否则守方处于 Hold 姿态不会移动。
