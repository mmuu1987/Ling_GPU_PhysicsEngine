# 未提交增量与提交准备

基线：`feat/war-sandbox-battlefield-rules`，HEAD `a76b4ca0a028d2d3778fc3c1c77f7947daab477b`。**当前已整合暂存原工作增量、1,176 个恢复资源文件/meta 与本轮文档；尚未提交、推送或开 PR。** 以下 A/B/C/S 是原工作增量的审查分组，不是当前暂存列表。

原 Git 可见的 991 个候选路径见 [CHANGE-INVENTORY.tsv](CHANGE-INVENTORY.tsv)：状态取自文档整理前，逐文件列出资源和 `.meta`。另有 [IGNORED-ASSETS.tsv](IGNORED-ASSETS.tsv) 中的 1,176 个资源文件/meta 已于同日修复忽略规则后暂存；该 TSV 是历史漏报清单。文档整理自身见本页末尾，运行器和原始日志属于本机证据。

## 已修复：资源被缓存规则误排除

当前结果见[资源纳管恢复](ASSET-TRACKING.md)：只调整根 Library 规则，1,176 文件/meta 全部恢复可见并显式暂存，根缓存仍被忽略。下面保留问题发现时的背景；其“后续应”步骤已执行，后续完整候选整合与隔离验证见 [整合验证](INTEGRATION.md)。

根 `.gitignore:2` 的 `[Ll]ibrary/` 未限定根目录，导致 `Assets/Game/.../Library/` 中的正式兵种资产也不被 Git 显示。范围涉及 UnifiedRoster、Dragons、Giants、Giants3、NonhumanBatch2、Troops4/5、PlatformerBatch6。例：`Assets/Game/UnifiedRoster/Version03/Library/male.asset.meta`、`Assets/Game/PlatformerBatch6/Prepared01/Integrated/Library/troops6-skull.asset.meta`；`git check-ignore -v` 均指向该规则。

Version08 的直接引用中有 39 个不同资产 GUID 依赖这些被忽略的 meta；本机全部找到，另外三处 URP 引用在 PackageCache 中找到。后续应将 Unity 根 Library 缓存规则限定在根目录，核对并显式纳管必要资源及 meta，继续保持根缓存不入库。再核对依赖闭包、安排新检出验证，不能只提交普通清单中的 991 个路径。忽略文件包含历史内容，不能凭此次枚举擅自删除或全部强制暂存。

资源恢复阶段发现旧渲染设置及包中的 12 个未解析 GUID，原始静态检查失败证据保留。随后已将 A/B/C/S 增量纳入完整候选，从 Git index 导出隔离工程进行实际导入、构建与玩家验证，结果见 [整合验证](INTEGRATION.md)。

## 按依赖顺序审查

| 组 | 内容与主要路径 | 依赖 / 提交注意 |
|---|---|---|
| A 第六批接入 | `Assets/CharacterPilotSource/QuaterniusPlatformerBatch6`；`Generated/Platformer{Crab,Enemy,Skull}01`；三份 Recipes；`PlatformerBatch6`；Version06；两个 Platformer 制作器；准备/目录/方案/战场测试；`Troops6Builder` 与准备测试；`Docs/PlatformerBatch6-20261002`、`Docs/Troops6-20261002` | FBX 嗅探依赖共享 `CharacterPipeline.cs`；原 Birb/Bunny/Fish 只完成准备，不宣称源件已取得 |
| B 内容质量与预览 | Version07；`OfficialRosterQualityBuilder.cs`、`PlatformerSourceReview.cs`；`WarSandboxFrontEnd.UnitPreview.cs`；内容质量 EditMode/PlayMode；`Docs/ContentQuality-20261002` | 依赖 A；隐藏不删身份，旧资源仍是兼容依赖；需共享目录/前端/UGUI 改动 |
| C 机器人转换与入列 | `TowerDefenseRobotExpressive01`；`Generated/TowerDefenseRobot01`；`RobotExpressivePilot`；Version08；三个 RobotExpressive 管线文件；Admission/OverviewFix；机器人测试与两个专题文档目录 | 依赖 B；原件、各准备修订、VAT、隔离候选和正式资源成套保留；最终 Build Settings 指向 V08 |
| S 共享接缝 | 下表列出的现有文件和隔离工具 | 混合了多个阶段，不能靠文件名机械拆成可独立编译的提交 |

路径简写只用于阅读；显式暂存应使用 TSV 中的完整路径，不将花括号当作 PowerShell 展开语法。

## 需要整体复核的共享文件

| 文件（仓库相对路径） | 当前变更用途 |
|---|---|
| `Assets/Game/Editor/CharacterPipeline/CharacterPipeline.cs` | FBX 错误网页头部拒绝，多个准备入口共用 |
| `Assets/Game/Editor/OfficialRosterBuilder.cs` | 第六批条目、历史入口拒绝、Version07 委托 |
| `Assets/Game/Tests/EditMode/OfficialRosterCatalogTests.cs` | 历史目录保留与当前构建器验证 |
| `Assets/Game/Scripts/WarSandboxBattlefieldCatalog.cs` | 可见性与真实预览元数据，保留旧身份解析 |
| `Assets/Game/Scripts/WarSandboxFrontEnd.cs` | 当前目录呈现、模型预览和尺寸变化处理 |
| `Assets/Game/Scripts/WarSandboxFrontEnd.UnitLibrary.cs` | 隐藏筛选与兵种库真实预览 |
| `Assets/Game/Scripts/WarSandboxRuntimeDeployment.cs` | 新选择过滤，保留旧单位兼容 |
| `Assets/Game/Scripts/WarSandboxTerrainCycle.Official.cs` | 正式卡片与身份总数分开验证 |
| `Assets/Game/Scripts/WarSandboxUGUI.cs` | 等比预览控件 |
| `Assets/MassEngine/Editor/VatAppearanceRegressionCapture.cs` | 真实模型捕获支持 |
| `Assets/Game/Scripts/PlatformerBatch6ReviewIsolation.cs`（含 `.meta`） | 显式验证数据隔离，多阶段使用 |
| `ProjectSettings/EditorBuildSettings.asset` | 最终 Version08 菜单与兼容场景；不是可随手还原的测试噪声 |

推荐后续先按 A/B/C/S 组审阅，再将这批相互依赖的内容作为完整集成提交；本轮文档可另一个提交。若要求逐阶段提交，需要重建共享文件的阶段版本，并逐个验证各提交，不能仅按目录依次 `git add` 就宣称每个提交可构建。本轮不重建历史代码，也不为拆提交重复烘焙。

## 提交排除与保留

- `Assets/pelican-cycling.svg` 与 `.meta`：绝不改动、删除或加入提交。
- `.local-mcp-audit`、`Logs`、`Builds`：本地审计/证据/程序，不暂存，不清理。
- `NEXT_TASK.md` 与 `Docs/HandoffHistory-20261003`：本地交接及其完整原文归档，不列入提交；NEXT_TASK 已被 Git 跟踪，保留跟踪关系，不执行取消跟踪或 `assume-unchanged`。
- 不删除历史版本、失败准备目录、源许可或个人方案。10 月 2 日旧包清理授权不是继续删除其他目录的授权。
- `TimeManager.asset` 本轮无修改；以后测试造成等价序列化变化时，对照运行前原件恢复，不能覆盖继承的其他配置改动。
- 提交只用显式路径，随后检查暂存差异；不 `git add .`，不直接推 main，后续整合走 PR。

## 本轮文档改动

新增 `Docs/Engineering-20261003` 下的导航、提交计划、普通改动 TSV 与被忽略资源 TSV；更新根 README、ROADMAP、GAME_DESIGN 的入口/阶段说明；给旧专题 README、旧 HANDOFF、维护构建说明、暂停的人工清单和 review 加历史提示；游戏层 README 增加当前导航。原专题正文与历史数据保留。

当前交接缩短为一页，旧全文另存于本地归档。它们不进入上面的提交候选。准确本轮修改路径与保护结果见 `Logs/DocumentationCleanup20261003/verification.json`，不是新的 Unity 测试或正式验收回执。
