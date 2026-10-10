# Game 目录整理记录（2026-10-07）

## 入口定位

- 整理前现役入口：`Assets/Game/Contact34/LaunchMenu.unity`。依据为 `Docs/CURRENT_DELIVERY.md`、37 版构建工具、菜单会话组件和 Catalog，而非仅凭文件夹名称。
- 整理前项目 Build Settings 仍以 `OfficialRoster/Version08/LaunchMenu.unity` 为首场景，与现役交付不一致。
- 整理后入口：**`Assets/Game/Scenes/MainMenu.unity`**；保留原场景 GUID。
- 正式 Build Settings：MainMenu、Green、Autumn、Winter；不删除旧场景，只取消其默认构建入口身份。
- 新增 Unity 菜单 `Game → Start Here`，支持打开／运行入口、定位 Catalog 和 README；不会在导入时自动换场景或启动游戏。

## 范围

- 按既有迁移方案分类游戏目录，核对后修正了其验证器预备补丁中错误的 Catalog 目标路径。
- 保留 Scripts、Editor、Tests、Resources 和程序集边界；游戏逻辑不重构，旧内容不删除。
- `Assets/MassEngine`、模型源、第三方包维持目录位置；其中原有硬编码游戏资源路径随迁移同步更新。
- 不改既有模型／纹理／VAT 数据内容，不覆盖已有试玩包，不做 Git add/commit/reset/checkout。
- 原有大量未提交改动仍保留。Git 中目录移动会呈现旧路径删除、新路径新增；请不要将这些误认为资源被清理删除。

## 代码阅读路线

`Scenes/MainMenu.unity` → `WarSandboxSceneSession.Awake/Start` 与 `WarSandboxFrontEnd` → `TryEnterBattlefield` / `Catalog.asset` → `WarSandboxRuntimeDeployment` 与布阵界面 → `WarSandboxBattleController` 与命令 HUD。

Unity 生命周期是游戏启动机制；`WarSandboxRuntimeBootstrap` 是旧场景兼容补齐机制，不是可双击启动的程序入口。

## 原始内容保护与过程记录

- 迁移前核对 7,765 个原有 Game 文件的 SHA-256，保留全部 Assets 的原始 `.meta` 哈希及未提交状态清单。
- 逐文件硬编码路径修改均有原文备份；原始 Build Settings 单独备份。
- 既有迁移器的批处理存在目录导入顺序问题：第一次在首个移动前停止；后续批次移动部分目录后停止，未完成文本补丁。相关失败日志保留，不抹去。
- 恢复前重新按原路径／新路径核对全部原文件；随后使用原 GUID 定位资源，只完成尚未完成的移动，并在依赖的移动之间同步导入，而非盲目重跑或覆盖目标目录。
- 整理前 Game README 原文保存在 `Game-README.before.md`。历史文档中记录的旧路径保留历史意义，请按下表找新位置；历史 Logs 脚本没有批量改写为新版本证据。

## 验证结果

- Game 根目录从 **55 个子目录整理为 11 个**；完成 49 项资源移动／重命名。
- 7,765 个原有 Game 文件均在目标路径通过哈希核对（仅计划内路径补丁及有原文备份的 README 更新被允许）。
- 全部 4,981 个原始 Assets `.meta` 内容保持一致；移动资源的 GUID 没有重建。
- 223 项计划内路径补丁均与预备内容一致；检查范围内未发现残留旧资源路径。
- 独立 Unity 批处理验证退出码为 0；四个现役场景成功导入并作为预览场景检查，检查 7813 个 GameObject，缺失脚本数为 0。
- 主菜单 Session／FrontEnd／Catalog 对应正确，三个战场的 Deployment 均引用现役 Catalog，Build Settings 与四场景 GUID 校验通过。
- 未运行 GPU 对局／性能回归，未生成新版 EXE，未提交 Git。不能将本次目录与引用验证当作完整玩法验收。

## 证据位置

- `Logs/GameLayout/plan.json`：完整移动与路径补丁计划。
- `Logs/GameLayout/manifest.before.json`：7,765 个原文件的原始哈希／目标路径。
- `Logs/GameLayout/backups/` 与 `patches/`：原始文本与预备路径补丁。
- `Logs/GameLayout/Review20261007/git-status.before.txt`：开始时已有未提交修改。
- `Logs/GameLayout/Review20261007/asset-metas.before.json`：原 `.meta` 哈希。
- `Logs/GameLayout/Review20261007/recovery-audit.json`：部分移动后原始文件完整性核对。
- `Logs/GameLayout/Review20261007/recovery-apply.json`：按 GUID 恢复迁移的结果。
- `Logs/GameLayout/Review20261007/review.after.json`：目标路径文件哈希、meta 和陈旧路径核对。
- `Logs/GameLayout/Review20261007/scene-check.json`：四场景、脚本、Catalog 与入口检查。
- `Logs/GameLayout/validate.json` 与 `Review20261007/validate-process.json`：Unity 验证及批处理退出状态。

如需撤回，应根据已完成移动清单逆序使用 Unity AssetDatabase 移回，再恢复本轮路径补丁与 Build Settings；不要使用 Git reset --hard，否则会丢失整理前已有的大量未提交修改。

## 旧目录 → 新目录

以下路径均相对 `Assets/Game/`。最后一项是在 Contact34 已移到 Scenes 后执行的场景改名。

| 原路径 | 新路径 |
|---|---|
| `Scenes` | `Experiments/LegacyScenes` |
| `Cavalry` | `Content/Characters/Cavalry` |
| `CharacterPilotPlayable` | `Content/Characters/CharacterPilotPlayable` |
| `Dragons` | `Content/Characters/Dragons` |
| `EnemyModelsBatch` | `Content/Characters/EnemyModelsBatch` |
| `EnemyModelsBatch2` | `Content/Characters/EnemyModelsBatch2` |
| `Giants` | `Content/Characters/Giants` |
| `Giants3` | `Content/Characters/Giants3` |
| `LargeBeasts` | `Content/Characters/LargeBeasts` |
| `M54TrialPlayable` | `Content/Characters/M54TrialPlayable` |
| `NonhumanBatch2` | `Content/Characters/NonhumanBatch2` |
| `OfficialRoster` | `Content/Characters/OfficialRoster` |
| `PlatformerBatch6` | `Content/Characters/PlatformerBatch6` |
| `RangerPilot` | `Content/Characters/RangerPilot` |
| `RobotExpressivePilot` | `Content/Characters/RobotExpressivePilot` |
| `Troops4` | `Content/Characters/Troops4` |
| `Troops5` | `Content/Characters/Troops5` |
| `UnifiedRoster` | `Content/Characters/UnifiedRoster` |
| `CharacterPipeline` | `Authoring/CharacterPipeline` |
| `Art20` | `Content/Battlefields/WoodlandArt` |
| `Art21` | `Content/Battlefields/WoodlandDetail` |
| `Camera24` | `Content/Battlefields/CameraTerrain` |
| `M61TerrainPrototype` | `Content/Battlefields/TerrainPrototype` |
| `M71LaunchPresets` | `Content/Battlefields/LaunchPresets` |
| `M73RenderBudget` | `Content/Battlefields/RenderBudget` |
| `Seasons22` | `Content/Battlefields/SeasonalWoodland` |
| `Seasons22B` | `Content/Battlefields/WinterContrast` |
| `TerrainScale15` | `Content/Battlefields/ForestTerrain` |
| `TightFormation17` | `Content/Battlefields/FormationSetup` |
| `Advance33` | `Experiments/NavigationApproach` |
| `Commands25` | `Experiments/CommandFeedback` |
| `Deploy32` | `Experiments/DeploymentChecks` |
| `Details26` | `Experiments/BattleDetails` |
| `Flow23` | `Experiments/MenuFlow` |
| `Help30` | `Experiments/FirstSteps` |
| `M62TerrainPlayable` | `Experiments/MountainPlayable` |
| `Plans28` | `Experiments/PlanLibrary` |
| `Results27` | `Experiments/BattleResults` |
| `Roster31` | `Experiments/RosterClarity` |
| `Scenery18` | `Experiments/ForestScenery` |
| `Scenery19` | `Experiments/MaterialFix` |
| `Season22` | `Experiments/SeasonPrototype` |
| `Settings29` | `Experiments/SettingsSafety` |
| `Stress200K` | `Experiments/Stress200K` |
| `TerrainAtmosphere14` | `Experiments/TerrainAtmosphere` |
| `TerrainPlanB12` | `Experiments/ForestPrototype` |
| `Previews` | `Content/UI/Previews` |
| `Contact34` | `Scenes` |
| `Scenes/LaunchMenu.unity` | `Scenes/MainMenu.unity` |
