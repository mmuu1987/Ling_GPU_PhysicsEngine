# 超大兵种第三批：巨型雪人（2026-10-01）

> **完成者**：Arena.ai Agent Mode（AI 编程智能体；底层模型由平台调度，智能体自己无法确认，也不能披露）。**需求与决策**：用户。这一批选 3 个（雪人、蓝魔、外星人）；雪人做成新的超大兵种，原来的雪怪保留；蓝魔和外星人等 Drive 配额恢复后再下载。
> **证据**：`Logs/AgentMonsters3/`、`Logs/OfficialRoster/shots-04/`（Logs 不入库）。

## 结论
- 新兵种 **巨型雪人**（模板 `roster-giant-yeti`），战场 `giants-yeti`，已接入正式游戏目录 **Version02**，共 19 个战场。
- 试玩包：`Builds/OfficialRoster-20261001-03/Start-WarSandbox.cmd`，GUID `61b4a770fb1c44faa27d84ed1020174f`。旧包都保留。
- **蓝魔、外星人没做**：官方 Google Drive 的这两个文件提示"下载配额已满"（按文件全局计算，沙盒和本机都试过）。Poly Pizza 镜像有 Cloudflare 人机验证，没有强行绕过。代码已经预留好（见下文），源文件到位后直接运行即可。

## 来源与授权
- 复用项目中已有的 `Assets/CharacterPilotSource/QuaterniusMonsters/Yeti.fbx`（Quaternius Ultimate Monsters，CC0，之前怪物批次导入，带 License.txt）。贴图复用 `Atlas_Monsters.png`。
- **源文件和 .meta 都没改**：Builder 会先检查导入设置是否已满足要求（Generic、可读、标准材质、BlendShape），满足就不重新导入；如果不满足会直接报错，不会去修改原来雪怪共用的 .meta。

## 做法
- 新建 `Assets/Game/Editor/CharacterPipeline/GiantBatch3Builder.cs`。`PrepareOne` 从 GiantBuilder 复制，只做了以下改动：每个单位独立的源路径、动作可设候选（`Walk/Run`、`Weapon/Punch`）、可选远程配置（为外星人预留）、导入设置只检查不修改。**GiantBuilder、DragonBuilder、公共管线和引擎一行都没改。**
- 方法：
  - `Inspect01`：只读，列出动作名和导入设置，结果在 `Logs/AgentMonsters3/inspect-01.txt`；
  - `PrepareYeti01` → `Generated/GiantYeti01`，自动验收通过；
  - `IntegrateYeti01` → `Assets/Game/Giants3/Prepared01/Integrated`，基于 Giants/Prepared02（巨龙、密集方阵 150、加强骑兵、恶魔、暴龙），加 1 个战场，巨型雪人同时加入大型兵种名册；
  - 预留：`PrepareBlueDemonAlien01`、`IntegrateBlueDemonAlien02`（叠加在 Prepared01 之上，生成 Prepared02）。
- `OfficialRosterBuilder`：当前版本 `Root` 切到 **Version02**，新增 `Prepare02` / `Build03`。`Prepare01` 改为直接报错，因为 Version01 已生成，保留只读。Version01 的预览图逐字节复用；巨型雪人排在巨型暴龙后面。EditorBuildSettings 指向 Version02 菜单和 19 个战场。

## 尺寸与数值（首版，未做平衡）
| 项 | 值 |
|---|---|
| 实际身高 | **4.47m**（请求 7.2m，受 VAT Half 精度预算 2.35mm 限制自动收缩，与巨型恶魔 4.66m 同级） |
| 横向 / 身长（含挥拳、倒地） | 7.6m / 8.2m |
| 碰撞半径 / 攻击距离 | 4.1 / 5.15 |
| 顶点 Full / Low | 3349 / 1997 |
| 动作 | Idle / Walk / Punch / Death |
| HP / 攻击 / 移速 | 1600 / 75 / 3 m/s（攻击间隔 = Punch 时长 0.867s，单体近战） |

## 测试
| 项 | 结果 |
|---|---|
| PlayMode `GiantBatch3BattlefieldTests`（新增） | 2/2。已发布战场（2 只对 16 名骑士）雪人全胜，用时 37s，雪人共损失 330/3200 HP，网格溢出 0 |
| 骑士人数扫描 | 24 人：雪人胜；48 人：骑士胜，剩 11 人；96 人：骑士胜，剩 78 人；144 人：骑士胜，剩 130 人。平衡点约 30–40 人，与巨人一致（保持碾压展示） |
| EditMode `OfficialRosterCatalogTests` | 5/5（新增"Version01 保持不变、当前目录在其基础上扩展、旧模板仍可解析"） |
| EditMode 全量 | 459/461，失败的 2 项是既有的构建列表问题 |
| PlayMode `MassEngine.Game.Tests` | 13/20，失败的 7 项是既有的 SceneEntryTests 构建列表问题；属性配置端到端测试已改用 Version02 菜单，通过 |
| 试玩包冒烟（official-catalog） | 19/19 个战场经卡片进入、开战、重置、返回，资源释放 19 次，0 错误，源资产没变，设置文件没被写入，用时 146s |

截图：`Logs/AgentMonsters3/giant-yeti-{start,fight,end}.png`，贴图和动作都正常。目录卡片预览图：`Version02/Previews/giants-yeti.png`。

## 蓝魔 / 外星人：配额恢复后的步骤
1. 从官方 Drive（Ultimate Monsters → Big → FBX）下载 `BlueDemon.fbx`、`Alien.fbx` 和根目录的 `License.txt`，校验后放进 `Assets/CharacterPilotSource/QuaterniusGiants3/`。
2. 运行 `Inspect01` 确认动作名。外星人的攻击动作暂定 `Weapon`（没有就用 `Punch`），远程配置为射程 16、弹速 22、无溅射。
3. 依次运行 `PrepareBlueDemonAlien01`、`IntegrateBlueDemonAlien02`，然后新建正式目录 Version03（`SourceCatalog` 指向 Giants3/Prepared02），再跑测试、打包、冒烟。

## 备注
- 发现一个既有问题：GiantBuilder 的数值行先设 `maxSpeed=Speed[i]`，随后又统一改成 3，所以夜间文档里写的"暴龙移速 4"实际是 3。本批沿用这个写法（雪人也是 3），没有改动 GiantBuilder。
