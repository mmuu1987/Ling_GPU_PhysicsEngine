# 超大兵种第三批（续）：巨型蓝魔、外星巨人（2026-10-01）

> **完成者**：Arena.ai Agent Mode（AI 编程智能体；底层模型由平台调度，智能体自己无法确认，也不能披露）。**需求与决策**：用户。这一批选雪人、蓝魔、外星人，其中外星人做远程；Drive 配额恢复后由智能体重新下载，不使用替代模型。
> 前一篇：`GIANT-YETI.md`（巨型雪人、Version02）。证据在 `Logs/AgentMonsters3/`、`Logs/OfficialRoster/shots-05/`（Logs 不入库）。

## 结论
- 新兵种 **巨型蓝魔**（`roster-giant-bluedemon`，近战）、**外星巨人**（`roster-giant-alien`，**远程直射**），战场分别是 `giants-bluedemon` 和 `giants-alien`。
- 正式目录 **Version03**：共 21 个战场、57 个模板，EditorBuildSettings 已指向它。Version01 和 Version02 保留只读。
- 试玩包：`Builds/OfficialRoster-20261001-04/Start-WarSandbox.cmd`，GUID `ef3fc893b3284aef86f55aa99c376fbe`。旧包都保留。

## 来源
- Drive 配额恢复后，从 Quaternius 官网链接的公开 Google Drive 匿名下载（Ultimate Monsters → Big → FBX）：
  | 文件 | 字节 | sha256 |
  |---|---|---|
  | BlueDemon.fbx | 3,387,292 | 2e84978f…c15f7b |
  | Alien.fbx | 3,455,452 | 060c6e50…f716d5 |
  | License.txt（CC0 1.0） | 374 | de990ef6…4de6f6e（与上一批巨人逐字节相同） |
- 存放位置：`Assets/CharacterPilotSource/QuaterniusGiants3/`，新文件只新建、不覆盖。贴图复用已有的 `Atlas_Monsters.png`（只读）。完整回执：`Logs/AgentMonsters3/source-transfer-receipt.json`。

## 做法
- 只用到上一步已预留的 `GiantBatch3Builder.PrepareBlueDemonAlien01` 和 `IntegrateBlueDemonAlien02`，**Builder 代码没有修改**。集成结果在 `Assets/Game/Giants3/Prepared02/Integrated`：在 Prepared01 的基础上增加 2 个战场，共 17 个条目。
- 外星巨人的攻击动作是 `Weapon`（0.867s），弹道参数为射程 16、索敌 22、弹速 22、无重力、无溅射，从胸口高度在攻击动作中段发射。用的都是引擎已有字段，**核心没改**。
- `OfficialRosterBuilder`：当前版本切到 Version03，新增 `Prepare03` 和 `Build04`。`Prepare01`、`Prepare02` 改为直接报错，避免覆盖已有版本。预览图从 Version01、Version02 逐字节复用，新增的 2 张来自 `shots-05`。

## 尺寸与数值（首版，未做平衡）
| | 巨型蓝魔 | 外星巨人 |
|---|---|---|
| 实际身高（受 Half 精度预算自动收缩） | 3.24m（请求 7.2m） | 5.16m（请求 6.5m） |
| 碰撞半径 | 3.2 | 3.3 |
| 顶点 Full / Low | 3221 / 1979 | 4227 / 1947 |
| 精度误差（上限 2.5mm） | 2.03mm | 2.18mm |
| HP / 攻击 / 移速 | 1500 / 85 / 3 | 1100 / 45 / 3 |
| 攻击方式 | 近战挥棒（距离 4.25） | 远程能量弹（射程 16） |

## 实战（2 只对骑士，固定步长 1/30s）
| 骑士数 | 巨型蓝魔 | 外星巨人 |
|---|---|---|
| 16（已发布战场） | 巨人胜，37.5s，掉血 350/3000 | 巨人胜，42.0s，**一滴血没掉**：骑士在冲到近前前就全部被射倒；第一次命中骑士在 15.3s |
| 24 | 巨人胜，掉血 760 | 巨人胜，掉血 250；骑士在 21.1s 第一次打中外星巨人 |
| 48 | 骑士胜，剩 21 人 | 骑士胜，剩 26 人 |
| 96 / 144 | 骑士胜，剩 82 / 132 人 | 骑士胜，剩 82 / 132 人 |

和雪人、恶魔、暴龙一样，平衡点约 30–40 名骑士，展示场保持碾压。网格溢出始终为 0。

## 测试
| 项 | 结果 |
|---|---|
| PlayMode `GiantBatch3BattlefieldTests` 新增 2 项 | 2/2。远程额外断言：射程 > 10、无溅射，骑士先被命中、外星巨人后挨打；扫描中 48 人以上骑士必须能伤到外星巨人（证明近战够得着）。第一次运行时，"骑士必须打到巨人"这条近战门槛对远程不适用，已按上面的方式修正 |
| PlayMode `CaptureOfficialV3Previews` | 1/1，输出 shots-05 |
| EditMode 全量 | 460/462。目录测试改为对 Version01 和 Version02 都检查"保留且被扩展、旧模板仍可解析"；失败的 2 项是既有的构建列表问题 |
| PlayMode `MassEngine.Game.Tests` | 13/20。失败的 7 项是既有的 SceneEntryTests 问题；属性配置端到端测试已改用 Version03 菜单，通过 |
| 试玩包冒烟（official-catalog） | 21/21 个战场经卡片进入、开战、重置、返回，资源释放 21 次，0 错误，用时 161s |

截图：`Logs/AgentMonsters3/giant-{bluedemon,alien}-{start,fight,end}.png`。外星巨人的能量弹拖尾清晰，蓝魔手持木棒，贴图正确。

## 后续可选
- 外星巨人在 16 人展示场里一滴血没掉。如果希望骑士能摸到它，可以调成：骑士 24 人，或者外星巨人射程 12。
- 三只巨人都受 Half 精度预算限制，没能达到 6–8m（蓝魔只有 3.24m）。要更高，需要改公共 VAT 精度，而这是之前约定不动的。
