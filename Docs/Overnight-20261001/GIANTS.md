# 第 7 项：更多超大单位（地面巨人，CC0）

## 来源
- Quaternius **Ultimate Monsters**，Big 包里的 `Demon.fbx`、`Dino.fbx`。官方页面：https://quaternius.com/packs/ultimatemonsters.html ，文件从官方链接的公开 Google Drive 匿名下载。
- 授权：CC0。随包附带的 `License.txt` 与之前怪物、巨龙批次的那份逐字节相同（sha256 de990ef6…）。
- 源文件放在 `Assets/CharacterPilotSource/QuaterniusGiants/`，另附 License.txt。贴图复用 `QuaterniusMonsters/Atlas_Monsters.png`，只读不改。
- 导入回执：`Logs/AgentGiants/source-transfer-receipt.json`，逐个文件记录 sha256。

## 做法
- 新建 `Assets/Game/Editor/CharacterPipeline/GiantBuilder.cs`。`PrepareOne` 从 DragonBuilder 原样复制，只改了源目录、动作名（Idle / Walk 或 Run / Punch / Death）、数值和日志名。**DragonBuilder 与公共管线一行都没改。**
- 方法：
  - `GiantBuilder.PrepareAll01` → `Generated/Demon01`、`Generated/Dino01`，均自动验收通过；
  - `Integrate01` → `Assets/Game/Giants/Prepared01/Integrated`，基于 Dragons/Prepared12，额外加入 2 个巨人战场和 2 个名册模板；
  - `Build01` → `Builds/UnifiedRoster-20260930-13`。

## 尺寸（受 VAT ±7.9m 坐标预算限制）
| 单位 | 请求身高 | 实际身高 | 横向跨度 / 身长 | 碰撞半径 | 攻击距离 |
|---|---|---|---|---|---|
| 巨型恶魔 Demon01 | 7.2m | **4.66m** | 7.8m / 8.0m | 4.2 | 5.25 |
| 巨型暴龙 Dino01 | 6.5m | **4.86m** | 7.2m / 8.2m | 3.9 | 4.95 |

挥拳、倒地等动作会把模型顶点甩得很远，全动作最远点受 7.9m 限制，所以身高被自动收缩。按最长尺寸算仍在 6–8m 档，与进化巨龙（翼展 7.4m）同级；身高约为骑士的 2.6 倍。想要更高就得改 VAT 编码，这违反"不改公共管线"的约定，所以没做。

## 数值（首版，仅保证功能可用，未做平衡）
- 恶魔：HP 1800，攻击 80，移速 3；
- 暴龙：HP 1400，攻击 70，移速 4。
- 两者都是单体近战，没有范围伤害。攻击间隔等于 Punch 动作的时长。

## 实测（PlayMode `GiantBattlefieldTests`，固定 1/30 秒步长，双方都下默认进攻命令）
已发布战场（2 巨人 vs 16 名适配骑士），两边结果都是**巨人全胜**：恶魔方共损失 330/3600 HP，暴龙方共损失 340/2800 HP。战斗在 37 秒结束，格子溢出为 0。

骑士人数扫描（`Logs/AgentGiants/giants-sweep.json`）：
| 骑士数 | 恶魔：胜方 / 剩余 | 暴龙：胜方 / 剩余 |
|---|---|---|
| 24 | 巨人 / 2 只巨人，共损失 650 HP | 巨人 / 2 只巨人，共损失 630 HP |
| 48 | 骑士 / 剩 14 人 | 骑士 / 剩 20 人 |
| 96 | 骑士 / 剩 78 人 | 骑士 / 剩 81 人 |
| 144 | 骑士 / 剩 129 人 | 骑士 / 剩 131 人 |

**平衡点约在 30–40 名骑士之间**，所以已发布的 16 人场明显一边倒。**需要用户决定**（见 PROGRESS"待定"）：
- A：巨人场改成 2 vs 32 骑士，新做集成目录 Prepared02；
- B：把巨人 HP 降到约 900–1000；
- C：保持现状，作为"碾压展示"场。

## 验证
- 截图审查：`Logs/AgentGiants/giant-*-start/fight/end.png`。模型贴图正确、动作正常，倒地的骑士可见。
- 实机：players11 在 -13 包上 **7/7 通过**，用例为 dragon seed/reload、dragon-evolved、cavalry、giant-demon、giant-dino、all。
  - players10 中 giant-demon 失败，原因是审计脚本的期望 id 映射里没有巨人，运行时实际上是正确的。已在 11 轮修正映射，10 轮证据保留。
