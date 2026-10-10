# Ling GPU Physics Engine / War Sandbox

> **现役交付（2026-10-06）**：当前为据点默认开战修复37版，入口、GUID和验证边界统一以[现役交付索引](Docs/CURRENT_DELIVERY.md)为准。下方早期包与测试记录按各自日期保留为历史，不作为现役验收结论。

> 新增方案B独立地形试装13：`Builds/TerrainPlanB-20261004-13/Start-Terrain.cmd`，Q版丘陵林地、真实高度/禁行及布阵高程图，256单位运行检查通过。11主流程与旧战场未替换。说明：`Docs/ProductPolish-20261003/TERRAIN-PLAN-B-20261004.md`。用户已改选B，不再等待方案A素材领取。

> 历史11版（2026-10-04）：选兵新增近战/远程筛选、当前兵种标记和本局生命/攻击；军团人数支持显式更新与草稿状态提示，阻止非法人数写入和失败后误跳转。10版相机/拖动保持。详见 `Docs/ProductPolish-20261003/LEGION-FLOW-11-20261004.md`。

> 历史10版（用户已确认基本可用）：用户已确认09透视和默认角度；本版仅修正拖动两轴方向，使模型跟随手势。14/14定向测试与独立EXE 33代表检查通过；待用户鼠标手感复查。说明：`Docs/UnitPreview-20261003/DRAG-DIRECTION-10-20261004.md`。下方09相机修复记录保持有效，旧拖动方向结论由10版替代。

> 历史09版修正相机重复GPU投影转换导致的反向深度，默认改为正面偏侧俯视；14/14定向测试及独立EXE 33代表检查通过，四实际角色与标准Camera轮廓一致。[当前说明](Docs/UnitPreview-20261003/CAMERA-DEPTH-FIX-20261004.md)。08仅顶底标记通过不代表遮挡正确，旧根因/修复口径已被本轮纠正。
Unity 6 GPU 战争沙盒。Windows 离线单机；玩家在游戏内组建军团、布阵、保存方案、指挥或观战，完成结算、重开与换场。

## 当前阶段（产品定位始于2026-10-03；进度同步于2026-10-09）

**初级阶段的可玩游戏产品基本完成，进入功能、产品细节和功能细节的持续打磨。** 主要流程已有独立包可试玩；“基本完成”不等于体验成熟、全量人工验收或 V1 正式发行签收。用户已经开始实际试玩，后续以具体体验反馈驱动小步改进，不再以堆功能/角色数量作为主要进度。

阶段目标和边界见[产品打磨阶段说明](Docs/ProductPolish-20261003/README.md)。

## 最新源码进度（2026-10-10同步）

- 交互改造P1～P8已有分阶段实现和有边界自动检查；[P9联合验收](Docs/InteractionRefinement-20261007/P9-PROGRESS.md)已开始、未验收，真人检查仍待完成。
- 2026-10-10 引擎侧性能工作已经PR合并，main == feat == `7ffafd9`：导航拓扑共享与Burst流场求解默认开启（PR #29）、全花名册远景低模（PR #30）、动态流场后台求解（PR #31），另含1024m测试战场。数据与边界见[引擎性能记录](Docs/EnginePerf-20261010/README.md)。旧“Burst默认关闭、未合并（`085957ba`）”已被取代。
- 以上均**未出包**：现役仍为37版，无38版；测量来自隔离工程测试包，不是37包或用户包验收。性能专项按用户2026-10-10指示暂停。
- 车道O(1)候选第四次ABBA未达标、不采纳。P3/P9仍未关闭：冷Shader首次Attack约5秒/冷进场约44秒（Editor测得，Player未测）、P3-PERF-01、P3-VISUAL-01、P9端到端与真人检查仍待完成。

## 现役试玩入口

- 以[现役交付索引](Docs/CURRENT_DELIVERY.md)为唯一入口：37版、默认2048人、1920×1080全屏。
- 当前Unity入口为 `Assets/Game/Scenes/MainMenu.unity`，不按下方历史Version08入口构建。
- 本轮修复/验证见[37版记录](Docs/CaptureDefault-20261006/REPORT.md)，旧包完整保留。

## 历史11版试玩入口（2026-10-04）

- 本机入口：[Start-ToyUI.cmd](Builds/ToyUI-20261004-11/Start-ToyUI.cmd)，**1920×1080 全屏**；请保留完整包目录，不能只复制EXE。Builds不在Git中，其他机器需另取得构建包。
- 构建 GUID：`1b057a4b1d0b426fbf81c7b0b60c3a6a`，Windows x64 Development。原Version08同步包、ToyUI-01/02与中间03仍保留。
- Unity：`6000.3.14f1`，打开仓库根目录；入口为 [Version08/LaunchMenu.unity](Assets/Game/OfficialRoster/Version08/LaunchMenu.unity)，内容目录仍为Version08。
- 普通流程：**主菜单 → 战场选择 → 布阵 ⇄ 军团详情 → 战斗 → 结算/重玩**。详情返回保留本局草稿，不自动应用、保存或开战。
- 图鉴展示 **33个代表角色**；仍保留68个模板身份。29个可选战场、31个兼容战场身份。目录原66条未撤回配置不再当成66个独立角色宣传。
- 新增[共用角色3D预览](Docs/UnitPreview-20261003/README.md)：图鉴、查看模型弹窗、军团详情使用同一组件；拖动旋转、滚轮缩放、复位，左侧小头像仍为静态。
- 历史UI/去重实施与证据：[Q版UI首轮](Docs/ToyUI-20261003/IMPLEMENTATION.md)、[图鉴去重与02包](Docs/ToyUI-20261003/ROSTER-DEDUP.md)。

## 从哪里接手

| 内容 | 文档 |
|---|---|
| 当前阶段、体验目标和迭代方式 | [阶段说明](Docs/ProductPolish-20261003/README.md) |
| 产品范围与长期路线 | [GAME_DESIGN.md](GAME_DESIGN.md)、[ROADMAP.md](ROADMAP.md) |
| 工程结构/依赖（现役入口以索引为准） | [工程导航](Docs/Engineering-20261003/README.md) |
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

历史3D预览定向PlayMode **3/3**（含33代表GPU检查）与04独立EXE检查通过。此前去重EditMode **62/62**、UI PlayMode **1/1**保留为原证据；不等于现役全量测试、完整OS键鼠或所有场景自然结算通过。

工作边界见 [AGENTS.md](AGENTS.md)：保护pelican SVG及meta，显式路径暂存，通过PR整合；NEXT_TASK不纳入新提交。仅文档更新不运行Unity/GPU测试，也不自动提交推送。
