# 新兵种第 4 批：骷髅兽人、忍者、部落战士（2026-10-01）

> **历史专题 / 入口提示（2026-10-03）：** 本页保留该阶段原始记录；当前正式入口已由 Version08 取代，见[工程导航](../Engineering-20261003/README.md)与最新机器人交付记录。下文“当前”“下一步”及构建命令不作为现在的执行清单。旧手工测试仍暂停，其他模型转换未授权；技术结果与人工认可范围按原记录保留。


> 完成者：**Arena.ai Agent Mode**（AI 智能体）。Agent Mode 会调度多种大模型（Claude、ChatGPT、Gemini、Grok、Qwen、Kimi 等），本次具体由哪个底层模型完成无法确认，也不能披露。需求与决策来自用户。

## 结果一览

| 兵种 | 定位 | 身高 | 半径 | HP | 攻击 | 攻距 | 攻击间隔 | 移速 | 展示场人数 |
|---|---|---|---|---|---|---|---|---|---|
| 骷髅兽人 | 重装近战 | 2.1 m | 0.6 | 200 | 16 | 1.9 | 0.867 s | 2.4 | 50 对 80 名骑士 |
| 忍者 | 高速近战 | 1.8 m | 0.45 | 70 | 12 | 1.75 | 0.867 s | 6.5 | 75 对 80 |
| 部落战士 | 均衡近战 | 2.2 m（含羽冠） | 0.5 | 110 | 11 | 1.8 | 0.867 s | 3.5 | 80 对 80 |
| 参照：常规兽人 | | 2.0 m | 0.5 | 100 | 10 | 1.8 | 0.867 s | 3 | |
| 参照：剑盾骑士 | | 1.8 m | 0.55 | 100 | 10 | | | 6 | |

- 正式目录 **Version04** 共 24 个战场（V3 的 21 个 + 本批 3 个），新战场排在“外星巨人”之后，列表分组为“新兵种”：
  - `troops4-orcskull` 骷髅兽人
  - `troops4-ninja` 忍者突袭
  - `troops4-tribal` 部落战士
- 试玩包：`Builds/OfficialRoster-20261001-07`（开发版，与 -06 同样的启动方式）。
- 数值是首版功能值，**未做平衡**。三个展示场都处在胜负临界点附近，结果会随细节波动（见下文“实测”）。

## 来源

- 模型：Quaternius **Ultimate Monsters** 的 Big 系列 `Orc_Skull.fbx`、`Ninja.fbx`、`Tribal.fbx`，来自作者官方 Google Drive，许可证 **CC0 1.0**（`License.txt`，sha256 de990ef6…，与已有怪物包相同）。
- 位置：`Assets/CharacterPilotSource/QuaterniusTroops4/`；sha256 已写进测试（`Troops4CatalogTests.SourcesArePinnedOfficialCc0Files`）。
- 贴图：沿用已导入的 `QuaterniusMonsters/Atlas_Monsters.png`（同一套 UV 图集）。
- 原计划中的“兽人”换成了**部落战士**：`Orc.fbx` 早已导入，`roster-orc`（兽人）本来就是常规兵种；蘑菇王同理（`roster-mushroom`）。

## 做法

1. **准备**（`Troops4Builder.Prepare01`）：走常规怪物（兽人、雪怪、蘑菇怪）的 MonsterBatchBuilder 路径——去掉导入时的静态缩放、网格和绑定姿势归自己所有、逐帧比对原始蒙皮位置（误差 ≤0.25 mm），尺寸由管线在 ScalePivot 上按 targetBodyHeight 处理。动作：Idle / Run / Weapon / Death。三个模型都通过管线自动门槛（位置误差 1.6–1.9 mm，上限 2.5 mm；低模 1537–1590 顶点，预算 1600）。
2. **集成**（`Troops4Builder.Integrate01`）：
   - 新常规兵种池 `Troops4/Prepared01/Integrated/RegularPolicy.asset` = Version03 的 12 个常规模板 + 3 个新兵种 + “剑盾骑士军团”（80 人，守方）。Version03 原文件不动。
   - 对手必须是兵种池里的模板，否则玩家在配兵布阵里一改就会提示“此战场不提供该角色”；所以和大型兵种池里的“适配骑士”一样，单独提供一个 80 人的骑士军团模板（`roster-knight-legion`）。
   - 每个新兵种一个战场，复制自 `UnifiedRoster/Version03/Scenes/RegularBattlefield.unity`，兵种池换成新池；配兵布阵里可以自由选择全部 16 个模板。
   - 目录 = Giants3/Prepared02 的集成目录 + 3 个新条目 + 4 个新模板（`roster-orcskull`、`roster-ninja`、`roster-tribal`、`roster-knight-legion`）。
   - 每个兵种保留自己的移速（**没有**沿用 GiantBuilder 统一改成 3 的做法）。
3. **正式目录**（`OfficialRosterBuilder.Prepare04`）：Version01–03 只读保留，旧预览逐字节复用；新预览来自 `OfficialRosterPreviewTests.CaptureOfficialV4Previews`（shots-06 的交战帧）。
4. **前端**：`WarSandboxFrontEnd.CatalogGroup` 把 `troops4-` 前缀归入“新兵种”（唯一的运行时代码改动）。

### 没有改动的内容

- “自由编成 · 常规兵种”（`unified-regular`）和“全兵种同场”保持 V3 原样：正式目录测试要求旧版本条目的场景和文字不变。新兵种通过本批 3 个战场进入游戏，这些战场的配兵布阵可以自由组合全部常规兵种。

## 实测（PlayMode，固定 1/30 s 步长，默认进攻命令）

| 轮次 | 骷髅兽人 | 忍者 | 部落战士 |
|---|---|---|---|
| 01 首版值 | 50 人 HP180/攻14：骑士胜，剩 35 | 90 人 速5：忍者胜，剩 5 | HP110/攻11/速3.2：骑士胜，剩 22 |
| 02 | HP200/攻16：兽人胜，剩 6 | 速6.5：忍者胜，剩 33 | 攻12/速3.5：部落胜，剩 26 |
| **03 发布值** | 同上：兽人胜，剩 21 | 人数 90→75：骑士胜，剩 40 | 攻11/速3.5：骑士胜，剩 13 |

- 骷髅兽人数值在 02 和 03 之间没变，结果却从剩 6 变成剩 21，说明这几场对细节很敏感；本批不再继续调。
- 三场都在 17–39 秒内结束，双方都造成伤害，网格无溢出（`peakGridOverflow` 均为 0）。
- 证据：`Logs/AgentMonsters3/troops4-shipped.json`、`-02`、`-03`，截图为 `troops4-shipped-03-*-fight.png`。

## 测试与冒烟

- EditMode（全量 `MassEngine`）：**487/489**，失败的 2 项是已知的构建列表问题（用户决定不处理）。新增 `Troops4CatalogTests`（7 项），`OfficialRosterCatalogTests` 新增 Version03 保留用例。
- PlayMode：`Troops4BattlefieldTests` 通过；`OfficialRosterPreviewTests.CaptureOfficialV4Previews` 通过。
- `-07` 冒烟：official-catalog（**24** 个战场都通过目录卡片进入）、readability（49/49）、playability-seed，全部通过。

## 重新生成

```
Troops4Builder.Inspect01          # 导入设置 + 动作名 + 包围盒
Troops4Builder.Prepare01          # 生成 Generated/OrcSkull01、Ninja01、Tribal01（不覆盖）
Troops4Builder.DiscardIntegrated01 # 仅在重调数值时：通过 AssetDatabase 丢弃本批的 Integrated
Troops4Builder.Integrate01        # 必须在新的编辑器会话里运行（见下）
OfficialRosterBuilder.Prepare04 → Build07
```

- 坑：在外部删除 Integrated 目录后，同一个 Unity 会话里 `AssetPathToGUID` 仍会返回“刚删除”资源的 GUID，VAT 的“只建不覆盖”检查会拒绝重建。做法是先用 `DiscardIntegrated01` 丢弃，再在下一次启动的 Unity 里运行 `Integrate01`。
- Generated 模板保存的是首版值（HP180/70/110 等）；发布值由 `Integrate01` 写进兵种库里的单位（`Integrated/Library/troops4-*.asset`）。

## 后续可做

- 打磨平衡（多次运行取胜率，而不是看单局结果）。
- 把新兵种加进“全兵种同场”，或者出一个 V5 版的“自由编成 · 常规兵种”（需要新的条目 id，旧条目保持不变）。
- 蘑菇王的特殊能力（需要扩展核心）。
