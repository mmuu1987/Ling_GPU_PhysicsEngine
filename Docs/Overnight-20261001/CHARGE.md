# 第 6 项：骑兵冲锋（冲锋加伤 + 更快移速）

## 结论
- 骑兵（`剑盾骑兵 · 冲锋（3.5m）`）移速 3.5 → **6 m/s**（步兵 3 m/s），奔跑动画播放倍速上限 1.15 → 1.75，减轻滑步。
- **冲锋**：骑兵以不低于 60% 最高速度（≥3.6 m/s）冲到敌人面前时，**第一击 3 倍伤害（30 → 90）**，之后恢复常规 30/击；拉开距离再冲可再次触发。
- 包：`Builds/UnifiedRoster-20260930-12`；骑兵集成 `Assets/Game/Cavalry/Prepared06/Integrated`（`CavalryBuilder.Prepare06`），总集成 `Assets/Game/Dragons/Prepared12/Integrated`（`DragonBuilder.IntegrateFire09` / `BuildFire09`）。
- 模型沿用已验收的 `Generated/MountedKnight04`，未重新烘焙。

## 核心扩展（可选，默认等于旧行为）
引擎原来没有任何冲锋机制；`UnitTypeSettings` 的 144 字节已无空槽，所以没有改结构体步长，而是沿用溅射/落点特效的做法：

| 文件 | 改动 |
|---|---|
| `Simulation/CombatConfig.cs` | 新增 `chargeDamageMultiplier`（默认 1 = 关闭）、`chargeMinSpeedFraction`（默认 0.6） |
| `UnitTypes/UnitTypeRegistry.cs` | `FillMeleeCharge(Vector2[])`：每兵种 (倍率, 速度门槛)；远程兵种强制 1；全部为 1 时返回 false |
| `Simulation/MeleeChargeParams.cs`（新） | 每兵种 8 字节的小缓冲；只在有冲锋兵种时分配；内容不变不重复上传 |
| `Core/MassEngineManager.cs` | `SyncMeleeCharge()`，Release 时释放 |
| `Core/ComputePipelineOrchestrator.cs` | 派发 `SimulateCombatAndAccumulateDamage` 前开关键字 `MASS_MELEE_CHARGE`，派发后立即关闭（compute 资产是共享的，内核测试直接用它） |
| `Simulation/Shaders/AgentCombatSimulation.compute` | `multi_compile_local __ MASS_MELEE_CHARGE`；近战结算处：`!wasAttacking && |velocity| ≥ 门槛×maxSpeed` 时首击额外加 `(倍率-1)×attackDamage` |

- 没有兵种开启冲锋时：不分配缓冲、关键字关闭、运行原内核变体，与旧版一致。
- 原版备份：`Logs/AgentCharge/pre-edit/`（附 SHA256SUMS.txt）。

## 测试（`Assets/MassEngine/Tests/PlayMode/MeleeChargeTests.cs`，3/3 通过）
1. `DefaultsKeepLegacyBehaviour`：新字段默认 1 / 0.6。
2. `FirstHitOnArrivalIsMultiplied`：1 名骑兵从 30m 外冲向 HP 10 万的坚守假人，逐帧记录掉血：

| 用例 | 各击伤害 | 首击时间 | 冲锋缓冲 |
|---|---|---|---|
| 旧行为 ×1，3.5 m/s | 30, 30, 30 | 12.4 s | 未分配 |
| ×3，6 m/s | **90**, 30, 30 | 7.3 s | 已分配 |
| ×3，门槛 2×maxSpeed（达不到） | 30, 30, 30 | 7.3 s | 已分配 |

   同时每帧断言共享 compute 资产上的关键字没有残留。
3. `ShippedChargeBattlefield`：正式骑兵场景（2 骑兵 vs 16 步兵，双方默认进攻命令）打到结束：

| 用例 | 胜方 | 用时 | 剩余 | 冲锋命中 |
|---|---|---|---|---|
| 旧骑兵（3.5 m/s，无冲锋） | 步兵 | 39.1 s | 步兵 7 | 0 |
| 新骑兵（6 m/s，×3 冲锋） | 步兵 | 36.5 s | 步兵 **4** | 2（每名骑兵 1 次） |

回归：PlayMode 内核套件 66/67、Projectile 58/58（与之前相同）；EditMode 429/433（4 项失败均为既有问题，见 HANDOFF）；实机 players09 在 -12 上 5/5。

## 待用户决定
- 2 骑兵仍打不过 16 步兵（只多杀 3 人）。原因：AI 交战后不会主动脱离再冲，所以每名骑兵一场只冲锋一次。可选方案：
  a. 保持现状（冲锋是"开场一击"）；
  b. 提高倍率或骑兵数量；
  c. 做"打一下就后撤再冲"的骑兵 AI（需要改核心行为，工作量大，建议单独立项）。
