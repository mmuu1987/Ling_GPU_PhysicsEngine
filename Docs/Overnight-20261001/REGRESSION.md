# 第 1 项：最终包自动回归 + 截图审查（替代人工试玩）

对象为最终包 `Builds/UnifiedRoster-20260930-13` 及当前工作区代码。

| 检查 | 结果 | 证据 |
|---|---|---|
| 实机 players11（真实 exe，7 个用例：dragon seed/reload、dragon-evolved、cavalry、giant-demon、giant-dino、all） | **7/7** | Logs/AgentDragonFire/batch-player-receipt-11.json |
| PlayMode：Projectile 全部 + MeleeCharge + 内核 + 巨人战场 + 方阵战场 | **120/120**（内核 67、弹道/特效/溅射 48、冲锋 3、巨人 1、方阵 1） | Logs/AgentCharge/reg-play-01-* |
| EditMode `MassEngine` | 431/433（失败的 2 项与已知一致，属于 M7.x 的 EditorBuildSettings，见 COMMIT-PLAN D3） | Logs/AgentCharge/reg-edit-01-* |

截图审查（players11 的 combat / catalog / settlement，以及 GiantBattlefieldTests 的近景截图）：
- 4 个战场都能正常进入交战、自然结算。巨人和巨龙贴图正确、动作正常，被击倒的骑士可见。
- 战场目录与战斗面板显示正常。
- 发现 1 个既有 UI 问题（不是今晚引入的，未修）：战斗界面底部的 Unity-Chan 许可文字与底栏按钮文字重叠，1280×720 下所有用例都能看到。
