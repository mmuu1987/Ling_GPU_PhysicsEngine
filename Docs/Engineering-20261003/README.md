# 工程导航（2026-10-03更新）

> 新增方案B独立地形试装13：`Builds/TerrainPlanB-20261004-13/Start-Terrain.cmd`，Q版丘陵林地、真实高度/禁行及布阵高程图，256单位运行检查通过。11主流程与旧战场未替换。说明：`Docs/ProductPolish-20261003/TERRAIN-PLAN-B-20261004.md`。用户已改选B，不再等待方案A素材领取。

> 当前11版：选兵新增近战/远程筛选、当前兵种标记和本局生命/攻击；军团人数支持显式更新与草稿状态提示，阻止非法人数写入和失败后误跳转。10版相机/拖动保持。详见 `Docs/ProductPolish-20261003/LEGION-FLOW-11-20261004.md`。

> 历史10版（用户已确认基本可用）：用户已确认09透视和默认角度；本版仅修正拖动两轴方向，使模型跟随手势。14/14定向测试与独立EXE 33代表检查通过；待用户鼠标手感复查。说明：`Docs/UnitPreview-20261003/DRAG-DIRECTION-10-20261004.md`。下方09相机修复记录保持有效，旧拖动方向结论由10版替代。

> 历史09版修正相机重复GPU投影转换导致的反向深度，默认改为正面偏侧俯视；14/14定向测试及独立EXE 33代表检查通过，四实际角色与标准Camera轮廓一致。[当前说明](../UnitPreview-20261003/CAMERA-DEPTH-FIX-20261004.md)。08仅顶底标记通过不代表遮挡正确，旧根因/修复口径已被本轮纠正。
**初级可玩产品基本完成，当前转向已有功能与产品细节持续打磨。** 用户已经实际试玩；不是仍然只准备机器人入列或搭建基本闭环，也不等于M6/M7/V1全部签收。产品阶段见[阶段说明](../ProductPolish-20261003/README.md)，待办与发行门槛见[剩余工作](REMAINING-WORK.md)。

## 当前入口与依据

| 用途 | 位置 / 状态 |
|---|---|
| 当前试玩包 | 以[现役交付索引](CURRENT_DELIVERY.md)为准；历史ToyUI-11入口见下方归档 |
| 包身份 | `1b057a4b1d0b426fbf81c7b0b60c3a6a`，Windows x64 Development；32构建场景含菜单 |
| Unity工程 | 仓库根目录，Unity6000.3.14f1 |
| Unity入口 / 内容资产 | [Version08/LaunchMenu.unity](../../Assets/Game/OfficialRoster/Version08/LaunchMenu.unity) / [Catalog.asset](../../Assets/Game/OfficialRoster/Version08/Catalog.asset) |
| UI与去重实现 | [首轮UI](../ToyUI-20261003/IMPLEMENTATION.md)、[图鉴去重与02包](../ToyUI-20261003/ROSTER-DEDUP.md) |
| 当前工作交接 | [NEXT_TASK.md](../../NEXT_TASK.md)，仅本地，不纳入新提交 |
| 原包与证据 | Version08同步包、ToyUI-01不覆盖；[Version08资料](../Version08Delivery-20261003/README.md)按旧GUID保留 |
| 历史工程整合 | [CHANGE-PLAN](CHANGE-PLAN.md)、[INTEGRATION](INTEGRATION.md)，PR #25已合入，不是最新UI工作区变更清单 |

共用角色3D预览已交付04：图鉴/放大弹窗/军团详情共用组件，3/3定向PlayMode与独立EXE 33代表检查通过。本轮代码尚未提交，详见[预览组件与回执](../UnitPreview-20261003/README.md)。

Version08是内容目录版本，ToyUI-11是当前构建。图鉴显示33个代表；Catalog中仍有68个模板身份，原66条未撤回配置不是66个独立模型。29个战场可选、31个战场身份保留。`WarSandboxRosterChoices`仅去重展示；运行时完整`Templates`不被缩减，`ChoiceTemplates`用于合法范围内的新选兵，保留近战/远程用途。

本批开发分支为`feat/mother-version08-sync`，交付资料检查点`55b4efd`和阶段文档检查点`532b801`已推送；用户随后授权将UI/去重代码、配套测试与构建工具提交并通过PR整合，具体提交与合并状态以Git/PR记录为准。NEXT_TASK、Builds/Logs及本地下载目录不纳入这批提交。原工作树迁移及清理已完成，继续在母工程开发。

## 最新已执行验证及边界

- 去重定向EditMode62/62、UI PlayMode1/1：`Logs/RosterDedup-20261003/run-01/`。
- 历史ToyUI-02构建成功（0错误、20警告），独立EXE自动回调检查退出0：`Logs/ToyUIBuild-20261003-02/`。已检查图鉴33项、详情/草稿返回、开战、暂停、手动结算，并保存实际帧。
- 29战场的合法候选列表检查不是逐场战斗；手动结算不是自然结算；自动回调不是OS输入。旧包的自然结算/跨进程/性能不移用为新包成绩。
- UI首轮720p及军团详情1080p截图、全屏启动参数不等于所有页面/纵横比/硬件已验收。用户真实试玩反馈与专项签收分开记录。
- 本次阶段文档更新只核对文档，不启动游戏、重跑测试或生成新包。

文档分工：`GAME_DESIGN.md`管产品目标/边界，`ROADMAP.md`管阶段和顺序，本页管入口与工程依据；[阶段说明](../ProductPolish-20261003/README.md)汇总现状，专题文档保留各次事实，NEXT_TASK只保留当前交接。旧“暂停/下一步/当前包”按当时日期理解。

## 内容依赖与保留

`Version05 → PlatformerBatch6/Prepared01/Integrated → Version06 → Version07 → Version08`。

Version07 负责内容筛选和真实全身预览；Version08 复用它并加入机器人。机器人路径是 `TowerDefenseRobotExpressive01 → RobotExpressivePilot/Version01/Native03 → Prepared04 → Generated/TowerDefenseRobot01 → Version08`。正式机器人仍引用隔离制作产物，不能按“旧目录”删除。其他 Native/Prepared 修订保留为诊断历史。

源件/许可在 `Assets/CharacterPilotSource`，烘焙结果在 `Assets/Game/CharacterPipeline/Generated`，玩法在 `Assets/Game/Scripts`，核心在 `Assets/MassEngine`。`ArchivedStages` 是不参与编译的引擎历史。本文不移动、删除或重烘这些目录。

## 制作入口：查阅用，不是待执行清单

所有名称均按当前代码核对。编号不是全工程通用版本号；已有输出不能重用。未来需要新包时，先提供新输出路径并保留历史，不通过删除旧输出绕过检查。

| 类型 / 方法 | 实际内容与输出 | 当前使用边界 |
|---|---|---|
| `ToyUiTrialBuilder.Build` | 历史ToyUI-11输出`Builds/ToyUI-20261004-11`；37版由`CaptureDefaultReplay.Build`输出 | 目录已存在会拒绝；下次先指定新输出和证据目录，不删除旧包绕过保护 |
| `RobotExpressiveAdmissionBuilder.Prepare08 / Build08` | Version08；`OfficialRoster-20261002-04` | 历史内容/包制作来源；不重跑Prepare08，不覆盖旧资产或固定输出 |
| `OfficialRosterBuilder.Prepare07 / Build10` | 委托 `OfficialRosterQualityBuilder.Prepare07 / Build07`；Version07，包 `20261002-02` | 质量修正历史入口，不会构建 Version08 |
| `OfficialRosterBuilder.Prepare01–06 / Build01–09` | 历史入口，当前代码主动拒绝 | 旧文档中的 Build08 **不是**机器人 Build08 |
| `PlatformerBatch6DeliveryBuilder.PrepareOfficial06` | 第六批正式化历史流程 | 依赖共享构建器；不能当作当前工作树复现旧版的保证 |
| `RobotExpressiveBuilder.Import01 / Prepare01 / Capture01 / Build01` | 机器人隔离转换，修订由其配置决定 | 原件、Native03、Prepared04 与已验证 VAT 保留；不是通用 Mod 导入器 |
| `Troops6Builder` | Birb/Bunny/Fish 原提案，从 Troops5 基础继承 | 来源历史限流且继承基础过时，未授权续做；不是已完成的 Platformer 三角色 |

未来新增内容遵循[内容门禁](../ContentQuality-20261002/CONTENT-SUITABILITY.md)：来源与完整原件、体态、实际四动作/材质、角色适配、真实预览逐项确认，再决定是否正式入列。

## 历史验证与后续运行约定

文档整理阶段只核对既有证据；随后开展的隔离导入、构建与运行回归见[整合验证](INTEGRATION.md)。原 Version08 的 [final-release.json](../../Logs/RetiredWorktree-20261003-01/Logs/AgentRobotFormal20261002/final-release.json)、`edit-03`（10/10）、`regression-01`（53/53）、`ui-02`（1/1）及 `robot-seed-02 / robot-reload-01 / official-all-01` 回执相互一致；三个玩家报告同 GUID、退出 0、完成且零错误。29 场是进入/开战/重置/返回冒烟，不是 29 场自然结算。机器人 seed/reload 才包含自然结算与跨进程方案恢复。旧证据根现为本机 `Logs/RetiredWorktree-20261003-01/Logs`，其中原绝对路径只记录历史运行位置。

测试程序集：EditMode 为 `MassEngine.Tests`、`Game.Tests`；PlayMode 为 `MassEngine.PlayModeTests`、`Game.PlayModeTests`。需要复测时，使用**本次待验证的工程目录**作 `-projectPath`（当前为母工程根目录，不沿用已移除的隔离工作树），串行、低负载、图形设备开启；不加 `-nographics`，`-runTests` 不加 `-quit`。先检查测试的资源和输出副作用，再用全新证据目录与隔离玩家数据运行。

历史 Version07 回归需要其场景列表 fixture，并在结束后恢复当前 Version08 设置；不能直接将其失败解释为引擎故障。既有全量记录的两项 EditMode、七项游戏层场景入口失败也不是本轮新测结果。根 AGENTS 中 29/63 是早期测试数量，不是现役全量基线。

| 已记录风险 | 触发时再处理 |
|---|---|
| R1：`Troops6Builder` 继承 Troops5 / 19 模板 | 原提案恢复制作前，重新设计继承关系，保留当前全部身份及隐藏决定；不能仅照旧建议切到 V6 就结束 |
| R2：`PlatformerBatch6PlanCycleTests` 自身未可靠恢复原构建列表 | 直接运行前补足跨域/失败路径恢复；现有外部运行器的备份不能省略 |
| R3：`PlatformerBatch6BattlefieldTests` 写固定证据文件 | 重跑前改成独立 run ID 与拒绝覆盖写入 |

详细诊断见[历史 review](../PlatformerBatch6-20261002/REVIEW-20261002.md)。以上未修复，不认定为当前普通游戏阻塞。暗色单位对比、UnityChan 风格差异、蟹怪连续动作适配留在内容待办；不自动扩展为引擎改造、100k 压测或新的模型批次。

## 历史文档收口验证（UI改造之前）

交接原文按字节归档；新增文档链接、入口与目录计数核对；Git 可见的 6,785 个既有非 Markdown 文件进行前后 SHA256 对照，变化为零。该保护集合不包含被忽略的资源、Logs 或 Builds。Version08 的 270 次序列化 GUID 引用核对包含磁盘 Assets 与包缓存，已定位全部引用；这只是直接引用核对，不代替全项目依赖闭包或新检出构建。

结果保存到本机 `Logs/DocumentationCleanup20261003/verification.json`。首次只扫描 Git 可见 meta 的未解析结果保留于 `verification-initial.json`，补查被忽略的 Assets 与 URP 包后全部定位；没有改写模型迁就检查。Logs/Builds 不入 Git，其他机器可能没有本地证据或可执行包；正式专题说明随仓库保留。
