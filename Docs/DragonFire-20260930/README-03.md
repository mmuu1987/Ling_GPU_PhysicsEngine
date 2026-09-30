# 第三轮试玩反馈：飞龙换有身体的模型 + 去掉马鞍（2026-09-30）

**当前可用构建：`Builds/UnifiedRoster-20260930-06`**（启动 `Start-Roster.cmd`）。-04、-05 已被取代。

## 飞龙"没身子"
- 原因：Quaternius 原版 `Dragon` 设计上就是一颗长翅膀的头，没有身体，官方预览图也是这样，不是烘焙问题。
- 处理（用户选方案 2）：飞龙战场改用有身体的 `Dragon_Evolved` 模型，按 78% 缩放重新烘焙，产物为 `Generated/Dragon_EvolvedYoung03`。
  - 翼展 5.80m、身高 3.3m、碰撞半径 3.4m、VAT 误差 1.94mm，自动检查通过。
  - 飞龙数值不变：HP 1000、攻击 60、溅射 3m。火球出手高度按比例调为 2.66m。
- 进化巨龙沿用 `Dragon_Evolved02` 模型：翼展 7.4m，HP 1500，攻击 90，溅射 3.5m。
- 编辑器方法：`DragonBuilder.PrepareYoung03` / `IntegrateFire03` / `BuildFire03`。集成目录为 `Assets/Game/Dragons/Prepared05`。

## 马鞍
- 鞍毯、鞍座、前后鞍桥全部去掉。马镫原本挂在马鞍上，也一并去掉。缰绳保留。
- 骑手仍保持上一轮前移 0.62m 的位置。
- 产物 `Generated/MountedKnight04`，集成目录 `Assets/Game/Cavalry/Prepared05`，方法 `CavalryBuilder.Prepare05`。整体高度 4.219m，VAT 误差 1.956mm。

## 测试
- 实机串行 5 个用例全部通过：飞龙首轮/重载、进化巨龙、骑兵、全兵种。见 `Logs/AgentDragonFire/batch-player-receipt-05.json`。
- 所有 Unity 批处理回执中，受保护的 3046 个文件均无改动。
- 未 commit、未 push。
