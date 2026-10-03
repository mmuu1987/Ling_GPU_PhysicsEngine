# Ling GPU Physics Engine / War Sandbox

Unity 6 GPU 战争沙盒。Windows 离线单机；玩家在游戏内布阵、保存方案、指挥或观战，完成结算、重开与换场。

## 当前版本（2026-10-03）

**Version08：机器人正式入列。** 当前为 66 个可选兵种模板、29 个可选战场；保留 68 个模板身份、31 个战场身份用于兼容。模板数不等于独立模型数。

- 本机游戏入口：[Start-WarSandbox.cmd](Builds/OfficialRoster-20261002-04/Start-WarSandbox.cmd)。保留完整包目录；Builds 不在 Git 中，其他机器需另取得构建包。
- Unity：使用 `6000.3.14f1` 打开仓库根目录，入口为 [Version08/LaunchMenu.unity](Assets/Game/OfficialRoster/Version08/LaunchMenu.unity)。
- 构建 GUID：`c9915b6a69974ad0ab3edeb66c846fdf`。首次仍进入 512 人准备战场。
- [最新交付记录](Docs/RobotFormal-20261002/README.md)：机器人表现与入列获认可；人工试玩仍暂停，其他模型转换未授权，V1 尚未签收。

## 从哪里接手

| 要了解的内容 | 文档 |
|---|---|
| 当前工程、内容依赖、制作与验证入口 | [工程导航](Docs/Engineering-20261003/README.md) |
| 本地当前任务与限制 | [NEXT_TASK.md](NEXT_TASK.md) |
| 未提交增量、共享文件与提交边界 | [改动清单](Docs/Engineering-20261003/CHANGE-PLAN.md) |
| 产品范围与阶段 | [GAME_DESIGN.md](GAME_DESIGN.md)、[ROADMAP.md](ROADMAP.md) |
| 模型适配与真实预览要求 | [内容门禁](Docs/ContentQuality-20261002/CONTENT-SUITABILITY.md) |
| 历史交接 | [归档说明](Docs/HandoffHistory-20261003/README.md)（本地） |

当前工作服务 M7 内容收口。旧专题的“当前入口 / 下一步”按其日期理解；M73FarLod、RangedPlaytest、Version01–07 等不是当前发布入口，旧性能与测试结果不移作 Version08 成绩。

## 代码与验证

- [引擎层](Assets/MassEngine/README.md)：GPU 模拟、导航、近远程战斗、弹道、VAT/LOD 与资源生命周期；具体行为以模块 README 和代码为准。
- [游戏层](Assets/Game/README.md)：战场、军团命令、玩家布阵、方案库、三层数值覆盖与运行时 uGUI。
- `Assets/Game/Editor/CharacterPipeline`：内部角色制作；源件在 `Assets/CharacterPilotSource`，生成产物在 `Assets/Game/CharacterPipeline/Generated`。
- `ArchivedStages`：不参与编译的历史快照，保留工具移植来源。
- [性能历史](Assets/Game/PerformanceBaseline.md)：区分人数、硬件、构建 GUID 和测量方式，不代表当前版本已重新测量。

EditMode 程序集：`MassEngine.Tests`、`Game.Tests`；PlayMode：`MassEngine.PlayModeTests`、`Game.PlayModeTests`。GPU 测试需要图形设备，绝不使用 `-nographics`。测试前先读[入口与夹具风险](Docs/Engineering-20261003/README.md)。仅文档改动不启动 Unity 回归。

工作边界见 [AGENTS.md](AGENTS.md)：保护 pelican SVG 及 meta，显式路径暂存，通过 PR 整合。NEXT_TASK 按约定不列入新提交，但本工作树已经跟踪它；本轮保留现状。
