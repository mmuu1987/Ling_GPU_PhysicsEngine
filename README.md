# Ling GPU Physics Engine / War Sandbox

Unity 6 GPU 战争沙盒。Windows 离线单机；玩家在游戏内组建军团、布阵、保存方案、指挥或观战，完成结算、重开与换场。

## 当前阶段（2026-10-03）

**初级阶段的可玩游戏产品基本完成，进入功能、产品细节和功能细节的持续打磨。** 主要流程已有独立包可试玩；“基本完成”不等于体验成熟、全量人工验收或 V1 正式发行签收。用户已经开始实际试玩，后续以具体体验反馈驱动小步改进，不再以堆功能/角色数量作为主要进度。

阶段目标和边界见[产品打磨阶段说明](Docs/ProductPolish-20261003/README.md)。

## 当前试玩入口

- 本机入口：[Start-ToyUI.cmd](Builds/ToyUI-20261003-02/Start-ToyUI.cmd)，**1920×1080 全屏**；请保留完整包目录，不能只复制EXE。Builds不在Git中，其他机器需另取得构建包。
- 构建 GUID：`726fdd6090014c2f97ec2c4e7f913ee5`，Windows x64 Development。原Version08同步包和ToyUI-01仍保留。
- Unity：`6000.3.14f1`，打开仓库根目录；入口为 [Version08/LaunchMenu.unity](Assets/Game/OfficialRoster/Version08/LaunchMenu.unity)，内容目录仍为Version08。
- 普通流程：**主菜单 → 战场选择 → 布阵 ⇄ 军团详情 → 战斗 → 结算/重玩**。详情返回保留本局草稿，不自动应用、保存或开战。
- 图鉴展示 **33个代表角色**；仍保留68个模板身份。29个可选战场、31个兼容战场身份。目录原66条未撤回配置不再当成66个独立角色宣传。
- 最新实施与证据：[Q版UI首轮](Docs/ToyUI-20261003/IMPLEMENTATION.md)、[图鉴去重与02包](Docs/ToyUI-20261003/ROSTER-DEDUP.md)。

## 从哪里接手

| 内容 | 文档 |
|---|---|
| 当前阶段、体验目标和迭代方式 | [阶段说明](Docs/ProductPolish-20261003/README.md) |
| 产品范围与长期路线 | [GAME_DESIGN.md](GAME_DESIGN.md)、[ROADMAP.md](ROADMAP.md) |
| 当前入口、依赖、验证与制作边界 | [工程导航](Docs/Engineering-20261003/README.md) |
| 打磨待办与独立发行门槛 | [剩余工作](Docs/Engineering-20261003/REMAINING-WORK.md) |
| 当前工作区状态和下一步 | [NEXT_TASK.md](NEXT_TASK.md)，仅本地交接 |
| 模型适配与真实预览要求 | [内容门禁](Docs/ContentQuality-20261002/CONTENT-SUITABILITY.md) |
| PR #25历史整合范围 | [历史改动清单](Docs/Engineering-20261003/CHANGE-PLAN.md)，不是当前UI未提交清单 |

历史文档的“当前入口/下一步”按其记录日期理解。旧GUID的自然结算、跨进程、全量测试或性能成绩，不直接转成当前试玩包的结果。

## 代码与验证

- [引擎层](Assets/MassEngine/README.md)：GPU模拟、导航、战斗、弹道、VAT/LOD与资源生命周期。
- [游戏层](Assets/Game/README.md)：军团、战场、指挥、草稿/方案、三层数值与运行时UI。
- `Assets/Game/Editor/CharacterPipeline`：内部角色制作；源件在 `Assets/CharacterPilotSource`，生成产物在 `Assets/Game/CharacterPipeline/Generated`。
- `ArchivedStages` 是不参与编译的历史快照；[性能历史](Assets/Game/PerformanceBaseline.md)按设备、人数、GUID和测量方式区分。

最新去重相关定向EditMode **62/62**、UI PlayMode **1/1**、新EXE自动回调流程检查通过；不等于现役全量测试、完整OS键鼠或所有场景自然结算通过。

工作边界见 [AGENTS.md](AGENTS.md)：保护pelican SVG及meta，显式路径暂存，通过PR整合；NEXT_TASK不纳入新提交。仅文档更新不运行Unity/GPU测试，也不自动提交推送。
