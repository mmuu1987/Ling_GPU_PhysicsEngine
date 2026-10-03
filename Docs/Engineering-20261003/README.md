# 工程导航（2026-10-03）

当前工作是 M7 内容收口。最新有效内容为 **Version08**，机器人模型表现及正式入列已获用户认可；人工试玩仍暂停，其他模型转换未授权，M6/M7/V1 整体验收未由本次入列代签。

## 当前入口与依据

| 用途 | 位置 / 状态 |
|---|---|
| 普通游戏 | [Start-WarSandbox.cmd](../../Builds/OfficialRoster-20261002-04/Start-WarSandbox.cmd)，本机已有包 |
| Unity 工程 | 仓库根目录；Unity 6000.3.14f1 |
| Unity 首场景 | [Version08/LaunchMenu.unity](../../Assets/Game/OfficialRoster/Version08/LaunchMenu.unity) |
| 内容目录 | [Version08/Catalog.asset](../../Assets/Game/OfficialRoster/Version08/Catalog.asset) |
| 当前交付与验收范围 | [机器人正式入列](../RobotFormal-20261002/README.md) |
| 当前工作交接 | [NEXT_TASK.md](../../NEXT_TASK.md)，仅当前状态 |
| 原交接全文 | [历史归档说明](../HandoffHistory-20261003/README.md) |
| 未提交增量 | [改动与提交清单](CHANGE-PLAN.md) |

**工程整合已接续：** 误忽略的 1,176 个资源文件/meta、原工作增量与文档已组成完整暂存候选。从 Git index 导出到全新隔离目录，未复制旧 Library/Logs/Builds；验证结果与候选树见[整合验证](INTEGRATION.md)。[资源纳管恢复](ASSET-TRACKING.md)及[原漏报清单](IGNORED-ASSETS.tsv)保留发现时证据。

包 GUID：`c9915b6a69974ad0ab3edeb66c846fdf`。可选战场 **29**、可选模板 **66**；兼容身份分别 **31 / 68**。模板数包含复用模型的不同配置，不等于独立身体模型数。绿皮头、骷髅头隐藏于新选择，旧 ID/revision、资源和方案解析保留。

文档分工：`GAME_DESIGN.md` 定产品范围，`ROADMAP.md` 定阶段；本页集中入口，专题文档保留各次实施证据。旧文档的“当前”“下一步”按其日期理解，不覆盖最新用户决定。历史性能和全量测试成绩不能移作 Version08 成绩。

## 内容依赖与保留

`Version05 → PlatformerBatch6/Prepared01/Integrated → Version06 → Version07 → Version08`。

Version07 负责内容筛选和真实全身预览；Version08 复用它并加入机器人。机器人路径是 `TowerDefenseRobotExpressive01 → RobotExpressivePilot/Version01/Native03 → Prepared04 → Generated/TowerDefenseRobot01 → Version08`。正式机器人仍引用隔离制作产物，不能按“旧目录”删除。其他 Native/Prepared 修订保留为诊断历史。

源件/许可在 `Assets/CharacterPilotSource`，烘焙结果在 `Assets/Game/CharacterPipeline/Generated`，玩法在 `Assets/Game/Scripts`，核心在 `Assets/MassEngine`。`ArchivedStages` 是不参与编译的引擎历史。本文不移动、删除或重烘这些目录。

## 制作入口：查阅用，不是待执行清单

所有名称均按当前代码核对。编号不是全工程通用版本号；已有输出不能重用。未来需要新包时，先提供新输出路径并保留历史，不通过删除旧输出绕过检查。

| 类型 / 方法 | 实际内容与输出 | 当前使用边界 |
|---|---|---|
| `RobotExpressiveAdmissionBuilder.Prepare08 / Build08` | Version08；`OfficialRoster-20261002-04` | 当前正式包的制作来源；原工程不覆盖，隔离验证可在全新输出目录执行 Build08 |
| `OfficialRosterBuilder.Prepare07 / Build10` | 委托 `OfficialRosterQualityBuilder.Prepare07 / Build07`；Version07，包 `20261002-02` | 质量修正历史入口，不会构建 Version08 |
| `OfficialRosterBuilder.Prepare01–06 / Build01–09` | 历史入口，当前代码主动拒绝 | 旧文档中的 Build08 **不是**机器人 Build08 |
| `PlatformerBatch6DeliveryBuilder.PrepareOfficial06` | 第六批正式化历史流程 | 依赖共享构建器；不能当作当前工作树复现旧版的保证 |
| `RobotExpressiveBuilder.Import01 / Prepare01 / Capture01 / Build01` | 机器人隔离转换，修订由其配置决定 | 原件、Native03、Prepared04 与已验证 VAT 保留；不是通用 Mod 导入器 |
| `Troops6Builder` | Birb/Bunny/Fish 原提案，从 Troops5 基础继承 | 来源历史限流且继承基础过时，未授权续做；不是已完成的 Platformer 三角色 |

未来新增内容遵循[内容门禁](../ContentQuality-20261002/CONTENT-SUITABILITY.md)：来源与完整原件、体态、实际四动作/材质、角色适配、真实预览逐项确认，再决定是否正式入列。

## 验证入口与证据

文档整理阶段只核对既有证据；随后开展的隔离导入、构建与运行回归见[整合验证](INTEGRATION.md)。原 Version08 的 [final-release.json](../../Logs/AgentRobotFormal20261002/final-release.json)、`edit-03`（10/10）、`regression-01`（53/53）、`ui-02`（1/1）及 `robot-seed-02 / robot-reload-01 / official-all-01` 回执相互一致；三个玩家报告同 GUID、退出 0、完成且零错误。29 场是进入/开战/重置/返回冒烟，不是 29 场自然结算。机器人 seed/reload 才包含自然结算与跨进程方案恢复。

测试程序集：EditMode 为 `MassEngine.Tests`、`Game.Tests`；PlayMode 为 `MassEngine.PlayModeTests`、`Game.PlayModeTests`。需要复测时，使用**本次待验证的工程目录**作 `-projectPath`（本次为隔离导出目录），串行、低负载、图形设备开启；不加 `-nographics`，`-runTests` 不加 `-quit`。先检查测试的资源和输出副作用，再用全新证据目录与隔离玩家数据运行。

历史 Version07 回归需要其场景列表 fixture，并在结束后恢复当前 Version08 设置；不能直接将其失败解释为引擎故障。既有全量记录的两项 EditMode、七项游戏层场景入口失败也不是本轮新测结果。根 AGENTS 中 29/63 是早期测试数量，不是现役全量基线。

| 已记录风险 | 触发时再处理 |
|---|---|
| R1：`Troops6Builder` 继承 Troops5 / 19 模板 | 原提案恢复制作前，重新设计继承关系，保留当前全部身份及隐藏决定；不能仅照旧建议切到 V6 就结束 |
| R2：`PlatformerBatch6PlanCycleTests` 自身未可靠恢复原构建列表 | 直接运行前补足跨域/失败路径恢复；现有外部运行器的备份不能省略 |
| R3：`PlatformerBatch6BattlefieldTests` 写固定证据文件 | 重跑前改成独立 run ID 与拒绝覆盖写入 |

详细诊断见[历史 review](../PlatformerBatch6-20261002/REVIEW-20261002.md)。以上未修复，不认定为当前普通游戏阻塞。暗色单位对比、UnityChan 风格差异、蟹怪连续动作适配留在内容待办；不自动扩展为引擎改造、100k 压测或新的模型批次。

## 本轮收口验证

交接原文按字节归档；新增文档链接、入口与目录计数核对；Git 可见的 6,785 个既有非 Markdown 文件进行前后 SHA256 对照，变化为零。该保护集合不包含被忽略的资源、Logs 或 Builds。Version08 的 270 次序列化 GUID 引用核对包含磁盘 Assets 与包缓存，已定位全部引用；这只是直接引用核对，不代替全项目依赖闭包或新检出构建。

结果保存到本机 `Logs/DocumentationCleanup20261003/verification.json`。首次只扫描 Git 可见 meta 的未解析结果保留于 `verification-initial.json`，补查被忽略的 Assets 与 URP 包后全部定位；没有改写模型迁就检查。Logs/Builds 不入 Git，其他机器可能没有本地证据或可执行包；正式专题说明随仓库保留。
