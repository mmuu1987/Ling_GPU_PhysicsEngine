# 现役交付索引

现役包记录：2026-10-06；文档同步：2026-10-10。本页是当前包、入口、GUID与验证边界的唯一索引；各历史报告仍保留原日期和原GUID。

## 当前：据点默认开战修复37版

- 入口：`Builds/CaptureDefault-20261006-37/Start-Game.cmd`
- 完整本机路径：`E:\GitHub\Ling_GPU_PhysicsEngine\Ling_GPU_PhysicsEngine\Builds\CaptureDefault-20261006-37\Start-Game.cmd`
- GUID：`883f210b17e4424b95593f4a094efa70`。
- Windows x64 Development，Unity6000.3.14f1；默认2048，启动脚本1920×1080独占全屏。保留完整目录，不只复制EXE。
- Unity入口（2026-10-07目录整理后）：`Assets/Game/Scenes/MainMenu.unity`；原 `Contact34/LaunchMenu.unity` 保留原GUID移动并改名。现役仍为 MainMenu/Green/Autumn/Winter 四场景，游戏逻辑和已有37版包未因整理重建。详见 [目录整理记录](GameLayout-20261007/README.md)。
- 源码：本地脏工作区，基于既有34后续状态；本轮既有产品代码只改 `WarSandboxBattleController.cs`，另有新增测试/显式诊断工具；未commit/push。

## 本轮证据（只绑定本GUID）

- 修复前Editor真实GPU复现：90秒，约65秒持续争夺，MoveOnly/MoveOnly、无攻击、HP和人数不变。
- 修复后默认据点战：Editor约73.39秒自然歼灭结算809/0；独立EXE约70.32秒自然歼灭结算763/0。自然首轮不追加人工指令、不改HP、不强制胜利。
- EditMode72/72（新增16+既有56）；小三队为每队64人的契约覆盖，不是三队GPU自然局。
- 同一EXE六项GPU检查通过：默认据点/战后衔接、移动、防守、撤退、开阔、障碍；默认据点衔接29项。六进程运行时异常列表为空。
- 构建0错误/20警告，四场景材质门禁保留，许可证完整；905保护项除批准的控制器修改外没有意外变化。
- 机器可读汇总：`Logs/CaptureDefault-20261006/delivery-verified/delivery.json`。
- [修复与完整验证记录](CaptureDefault-20261006/REPORT.md)。

## 仍待人工确认

`osInputTest=false`。EXE检查使用1280×720窗口参数与API/uGUI回调，不是OS真实键鼠、1080p人工手感或连续画面验收。35轮前台项需用户在机器前配合，不自动重试；不宣称全量、高人数或V1发行验收。

下一步：试玩37版普通据点流程，根据一个具体问题继续小步迭代；场景/人数/内容扩张仍暂停。非法坐标防护现已完成源码与定向测试，并已随2026-10-10母工程状态同步进入main，但尚未进入任何包，见下方源码增量。

## 后续源码进度（2026-10-10同步，不属于37包验收）

- 10月7日交互改造的P1～P8已有分阶段实现与有边界自动检查；[P9](InteractionRefinement-20261007/P9-PROGRESS.md)于10月8日开始自动部分，仍未验收。
- 2026-10-10经PR合并到main（main == feat == `7ffafd9`）：PR #29 `9c58a3e` 导航拓扑共享、Burst流场求解默认开启、1024m测试战场；PR #30 `54728f6` 全花名册远景低模；PR #31 `7ffafd9` 动态流场后台Burst求解（晚1个Tick生效）。旧“[Burst后备](InteractionRefinement-20261007/P3-BURST-LANDING-COMMIT-20261008.md)默认关闭、未合并（085957ba）”已被取代。数据与边界见[引擎性能记录](EnginePerf-20261010/README.md)。
- 上述测量用隔离工程P12的Windows Player测试包（1024测试图），不是37包、用户包或人工验收；不改变产品人数/画质口径。性能专项按用户指示暂停。
- [车道O(1)第四次ABBA](InteractionRefinement-20261007/P3-LANE-ABBA4-20261008.md)未达门槛，候选不采纳。P3/P9、完整端到端和真人验收仍待收尾；冷Shader首次Attack约5秒/冷进场约44秒只在Editor测得，Player未测。
- 上述是源码/专项记录进展，不更新37包，不代表新增EXE测试或38版交付。本页“本轮证据”及“源码增量”中的本轮均指各自注明的历史日期。

## 旧包与历史证据

> **2026-10-07 存储清理**：经用户确认，已删除清理清单中的 38 个旧构建包，完整保留第 37 版。以下旧包路径仅作为历史引用，不表示旧 EXE 仍在本机。历史报告与验证证据未删除；详见 [清理记录](BuildCleanup-20261007/README.md)。

- 34：`Builds/ContactCommands-20261006-34/Start-Game.cmd`，GUID `3fd896b5b3d94e0daca67c9d5ed573ce`，旧包已于2026-10-07清理，历史记录保留。历史五项EXE通行检查实际合计975,056，不是逾百万。
- 36：只有Editor衔接检查，没有36版EXE；历史占点用例使用对手Hold，不可替代37的默认双方开战证据。
- 33-r2及更早包已于2026-10-07清理。`AdvanceLanes-20261005-33`为历史验收失败包，不作为修复交付。
- 11、02、Version08等旧“当前入口”和旧测试成绩，只按各自记录日期理解。

## 源码增量：非法坐标防护（2026-10-06，未打包）

- 已在游戏层命令目标解析入口拒绝NaN及正负无穷；拒绝时不改路线、姿态、其他军团状态，也不误开战/恢复运行。
- 新增49项输入契约；连同默认据点16项、控制器56项、地形13项，共134/134 EditMode通过。修复前48项非法输入失败、1项正常输入对照通过。
- 仅控制器新增8行及新增测试，没有修改引擎/场景/人数；测试不向GPU提交非法目标，没有新增EXE或GPU对局验收。
- **本次增量尚未打包；上方37版包不包含它。** 37目录387个文件及其余保护项保持原字节；以后必要出包时再纳入，不自动生成38版。
- 源码SHA-256：`7a51930a574aa4672adff67377208e751913706bb799d493a1aaef3392e87f91`。未commit/push。
- [源码修复记录](CommandInputSafety-20261006/REPORT.md)；证据 `Logs/CommandInputSafety-20261006/`。
