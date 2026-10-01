# 新兵种第 5 批：仙人掌枪手、毒蛙群、猴獴战士（2026-10-01）

> 完成者：**Arena.ai Agent Mode**（AI 智能体）。Agent Mode 会调度多种大模型（Claude、ChatGPT、Gemini、Grok、Qwen、Kimi 等），本次具体由哪个底层模型完成无法确认，也不能披露。需求与决策来自用户。

## 结果一览

| 兵种 | 定位 | 身高 | 半径 | HP | 攻击 | 射程 / 攻距 | 攻击间隔 | 移速 | 展示场人数 |
|---|---|---|---|---|---|---|---|---|---|
| 仙人掌枪手 | 远程直射 | 1.9 m | 0.5 | 100 | 16 | 16 m / 1.8 m | 0.867 s | 3.0 m/s | 80 对 80 名骑士 |
| 毒蛙群 | 轻装近战 | 1.6 m | 0.45 | 70 | 10 | 近战 1.75 m | 0.867 s | 5.0 m/s | 95 对 80 |
| 猴獴战士 | 均衡近战 | 2.0 m | 0.55 | 120 | 12 | 近战 1.85 m | 0.867 s | 4.0 m/s | 60 对 80 |
| 参照：剑盾骑士军团 | 冲锋近战 | 1.8 m | 0.55 | 100 | 10 | 近战 1.7 m | 0.867 s | 6.0 m/s | 80（守方） |

- 正式目录 **Version05** 共 **27** 个战场（V4 的 24 个 + 本批 3 个），新战场排在“部落战士”之后，列表分组为“新兵种”：
  - `troops5-cactoro` 仙人掌枪手
  - `troops5-frog` 毒蛙群
  - `troops5-monkroose` 猴獴战士
- 试玩包：`Builds/OfficialRoster-20261001-08`（开发版，与 `-07` 同样的启动方式）。
- 数值是首版功能值，**未做深度平衡**。三个展示场都处在胜负临界点附近，对抗激烈（见下文“实测”）。

## 来源

- 模型：Quaternius **Ultimate Monsters** 的 Big 系列 `Cactoro.fbx`、`Frog.fbx`、`Monkroose.fbx`，来自作者官方 Google Drive，许可证 **CC0 1.0**（`License.txt`，sha256 `de990ef6…`，与已有怪物包相同）。
- 本地与工程路径：`Assets/CharacterPilotSource/QuaterniusTroops5/`；sha256 已写入 EditMode 测试固定：
  - `Cactoro.fbx`: `109561ce7e6db75f5f5238215fed0fbfceb171151db6dae5d02997523b8a95ae`
  - `Frog.fbx`: `77c8cb364de1f63605fb5411c70237d02aa416a6ae118f2db3c7e88c7e5be897`
  - `Monkroose.fbx`: `00c225a500b15bd360cd903bcc0a0f84439395eed7e84bfe413e058f4125af4c`
- 贴图：沿用怪物包统一图集 `QuaterniusMonsters/Atlas_Monsters.png`。

## 做法与技术设计

1. **准备**（`Troops5Builder.Prepare01`）：
   - 沿用成熟的常规怪物路径（MonsterBatchBuilder / Troops4Builder 路径），剔除导入静态缩放，由管线依据 targetBodyHeight 缩放与着地。
   - 动作：Idle / Run / Weapon / Death。
   - 三个模型均通过管线自动门槛（低模 1530–1540 顶点，误差 ≤1.65 mm，预算 1600）。
2. **远程弹道设计（仙人掌枪手）**：
   - GPU 弹道无制导，若使用抛物线（带重力弧线），面对 6 m/s 狂奔而来的骑兵冲锋，抛物弹会落空在身后；
   - 因而仙人掌配置为**平射尖刺**（`projectileGravity = 0`，速度 18 m/s，射程 16 m）；
   - 发射高度略高于头部（`projectileOriginHeight = 2.09 m`），后排越过前排友军胶囊直射袭来的骑士，落地前完成两轮齐射；
   - 单体攻击无溅射伤害，贴身近战依然有基本反击。
3. **集成**（`Troops5Builder.Integrate01`）：
   - 兵种池继承并扩展自第 4 批策略：16 个已有模板 + 3 个新兵种 = **19 个模板**。
   - 对手依然是兵种池内的“剑盾骑士军团”（80人），保证在配兵布阵中自由替换。
   - 目录 = 第 4 批集成目录（20项） + 3项新战场 = **23 个条目**。
   - 各兵种保留专属移速配置（3.0 / 5.0 / 4.0 m/s）。
4. **正式目录与发布**（`OfficialRosterBuilder.Prepare05` → `Build08`）：
   - Version01–04 保持只读，旧预览完全复用；
   - 新条目预览取自 `OfficialRosterPreviewTests.CaptureOfficialV5Previews`（shots-07 交战帧）；
   - 前端代码 `WarSandboxFrontEnd.CatalogGroup` 保持支持，将 `troops` 前缀归入“新兵种”卡片。

## 实测战果（PlayMode，固定 1/30 s 步长，默认进攻命令）

| 轮次 | 仙人掌枪手（远程） | 毒蛙群（轻装） | 猴獴战士（均衡） |
|---|---|---|---|
| **发布值** | 80 人 HP100/攻16/速3/射程16m<br>骑士胜，剩 40 人（造成 6112 伤害） | 95 人 HP70/攻10/速5<br>骑士险胜，剩 26 人（造成 6470 伤害） | 60 人 HP120/攻12/速4<br>骑士胜，剩 42 人（造成 5228 伤害） |

- 仙人掌枪手在 3.6 秒首次命中（`firstHit: troops`），抢在接敌前消耗骑士队列；
- 三场对局双方均打出大量伤害（骑士阵亡 40–54 人不等），战斗均在 20–27 秒内平稳结束，`peakGridOverflow` 均为 0。
- 截图见 `Logs/OfficialRoster/shots-07/`。

## 测试与冒烟

- **EditMode**：**495/497**（新增 `Troops5CatalogTests` 7 项用例全部通过，2 项为已知构建列表/远处渲染基准测试）。
- **PlayMode**：`Troops5BattlefieldTests`（3 战场闭环测试）全部通过；`OfficialRosterPreviewTests.CaptureOfficialV5Previews` 全部通过。
- **`-08` 试玩包冒烟**：
  - `official-catalog`：**27 个战场**逐一从卡片进入运行无异常，全部通过；
  - `readability`：49/49 项界面可读性检查全部通过；
  - `playability-seed`：设置存档与按键测试通过。
