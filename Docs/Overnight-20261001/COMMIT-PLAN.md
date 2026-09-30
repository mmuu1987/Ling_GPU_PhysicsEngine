# 提交方案清单（第 4b 项，**只是方案，未执行任何 git 操作**）

生成时间 2026-10-01。依据：`Logs/AgentGiants/git-status-01.tsv`（完整 status 与 mtime）、`git-needed-01.tsv`（依赖分类）、`dependency-closure-01.txt`（-13 包依赖闭包）、`git-mixed-01.tsv`（混改文件）、`git-big-01.txt`（>20MB 文件）。

## 现状
- 分支 `feat/war-sandbox-battlefield-rules`，比 origin 超前 9 个提交；最后一个提交是 `d7f0a5c`（M5.2 复审）。
- 未提交 **344 项**：65 个修改，279 个未跟踪（未跟踪目录已折叠计数）。范围覆盖 M5.3 → M6 地形 → M7.1–7.3 → 角色管线 / 骑兵 / 巨龙 → 通宵 7 项。
- `Logs/`、`Builds/`、`Library/` 已被 .gitignore 忽略，不会误加。

## 需要你先拍板的 4 件事（会影响怎么提交）
**D1 大文件：** VAT 数据 `Generated/*/Full.asset` 和骑兵 `Idle.anim` 都以 YAML 文本序列化，单个 20–58MB，其中 **6 个超过 50MB**（GitHub 会警告；目前都低于 100MB 硬上限）。仓库 `.gitattributes` 没有 LFS 规则（本机已装 git-lfs 3.7.1），而且 `.asset` 被标成 `text eol=lf`。
- 方案 a：对 `Assets/Game/CharacterPipeline/Generated/**/Full.asset`、`**/*.anim` 等启用 LFS（改 .gitattributes，单独提交）。
- 方案 b：不提交生成物，只提交 builder 和源模型，靠重新生成恢复（耗时，且需要 Unity）。
- 方案 c：直接提交，仓库会增加约 0.7–2GB。

**D2 被取代的旧版本：** 未跟踪资产共 **1968MB**，其中 -13 包依赖闭包实际用到的只有 **727MB**。以下目录不在闭包里，共约 1.2GB：
- `Generated/`：MountedKnight01–03、Ranger01、SkeletonMage01–02、Dragon02；
- `Cavalry/`：Prepared01–04（Prepared05 只被引用了 6 个小文件）；
- `Dragons/`：Prepared01、03、04、06–11；
- `UnifiedRoster/`：LegacySkeleton、Version02；
- `LargeBeasts/Prepared01`；
- `CharacterPilotPlayable/`：NeckFix03、Prepared 以及根目录下 43MB 的文件；
- `RangerPilot/`：Alignment02、Templates。

它们作为证据保留在磁盘上（按规则不删）。建议**不提交**（或者如果你同意，另外归档）。
注意：`CharacterPilotSource/` 里的源 FBX 虽不在运行时闭包里，但 builder 要用，**建议提交**（共 62MB，CC0 / KayKit 许可，License.txt 都在）。

**D3 M7.x 的构建场景：** `ProjectSettings/EditorBuildSettings.asset`（09-27 修改）换成了 M71 LaunchPresets 场景，因此 2 个 Game EditMode 测试失败（`WarSandboxCatalogTests.Shipping…`、`WarSandboxLaunchPresetTests.QuickDefault…110kContent`）。需要确认：是那条线还没做完，还是测试需要跟着更新。在确认之前，**这个文件先不提交**。

**D4 跨功能混改的 6 个核心文件**（已用 `.orig` 与 HEAD 对比确认，冲锋改动之前它们就已带有未提交的改动）：
- `Core/MassEngineManager.cs`
- `Core/ComputePipelineOrchestrator.cs`
- `Simulation/CombatConfig.cs`
- `Simulation/Shaders/AgentCombatSimulation.compute`
- `UnitTypes/UnitTypeRegistry.cs`
- `Tests/PlayMode/MassEngineGpuKernelTests.cs`

拆分办法：`Logs/AgentCharge/pre-edit/<名>.orig` 正好是"冲锋之前"的状态。先把 .orig 复制到原位置，`git add` 这个文件，随前面的提交一起提交；然后恢复当前版本，再随 C10 或 C11 提交。也可以直接用 `git add -p`。

## 建议的提交顺序（每步都用显式路径 `git add <paths>`，**不要用 `git add .`**）
路径中的 `ME/` 指 `Assets/MassEngine/`，`G/` 指 `Assets/Game/`。每个目录的 `.meta` 要一起加。

| # | 提交 | 主要路径 | 备注 |
|---|---|---|---|
| C1 | feat: M5.3–M5.4 VAT 外观回归与模型试玩 | G/Editor/{WarSandboxVatRegressionBuilder,UnityChanTrialImporter,WarSandboxModelTrialBuilder}.cs、G/Scripts/WarSandboxVatPerformance.cs、ME/Editor/{VatAppearanceRegression,UnitTypeBinder}.cs、对应 EditMode 测试、G/Editor/Game.Editor.asmdef、G/M54TrialPlayable、Assets/ModelTrialSource（44MB，UnityChan 许可）、方案设计/M5.3*、M5.4* | M54 目录里有 38.9MB 的 UnityChanVAT.asset，受 D1 约束 |
| C2 | feat: M6.1–M6.3 连续地形 | ME/Terrain/、ME/Core/{PipelineContexts,MassEngineManager.Terrain}.cs、ME/FlowField/README.md、Terrain*Tests、G/Scripts/{TerrainSurfaceQueries,WarSandboxTerrain*（Prototype/Validation/PlayableSmoke）}、CameraControls/MyCameraManager.cs、ClickFlowTargetSetter.cs、StaticObstaclePresenter.cs、LocalPlanStore.cs、G/Editor/WarSandboxTerrain*Builder.cs、G/M61*、G/M62*、方案设计/M6.* | |
| C3 | feat: M7.1–M7.2 启动预设、设置、音频、结算反馈、内存 | G/Scripts/{WarSandboxSettingsStore,Audio,BattleFeedback,FrontEnd(.Settings),UGUI,SceneSession,RuntimeBootstrap,BattlefieldCatalog,ArmyOrder,ProcessMemory}.cs、G/Editor/WarSandboxLaunchPresetsBuilder.cs、G/M71LaunchPresets、Assets/Resources.meta、对应测试、方案设计/M7.1*、M7.2* | EditorBuildSettings 等 D3 决定后再提交 |
| C4 | feat: M7.3 远景 LOD、动作/弹道反馈、拥堵等待、远程有效射击 | ME/Editor/VatLodReducer.cs、G/Editor/WarSandboxFarLodBuilder.cs、G/M73RenderBudget、G/Settings/*_Combat.asset、ME/Core/{AgentData,UnitTypeGpuSettings,MassGpuShaderPropertyIds,CombatBufferSet,MassGpuBufferManager}.cs、Shaders/AgentDataCommon.hlsl、ME/VatRender/*、ME/Simulation/{DefaultCombatModule.cs,Shaders/CongestionWaiting.hlsl,Shaders/RangedClearance.hlsl}、ME/Projectiles/{ProjectileBallistics,ProjectileGpuManager.Pool,ProjectilePoolBudget}.cs、TerrainCycle.*、CommandHUD*、BattleController、Tools/、对应测试、ME/Core,Simulation README、方案设计/M7.3*、M7* | 混改文件用 .orig 版本（D4） |
| C5 | feat: M7 可玩性与长时间复测 | G/Scripts/WarSandboxTerrainCycle.{Playability,Presets,LoopValidation,Performance}.cs、WarSandboxTerrainCycle.cs、WarSandboxPlayabilityTests、G/PerformanceBaseline.md、方案设计/M7V1*、M7完整对局*、M7当前版本*、M7远程* | |
| C6 | feat: 角色管线与统一角色库 | G/Editor/CharacterPipeline/（**不含** Cavalry/Dragon/GiantBuilder）、G/Editor/Knight*.cs、WarSandboxCharacterPilotBuilder.cs、G/Scripts/{CharacterPipelinePreviewRuntime,WarSandboxCharacterPilotRuntime,EnemyBatchRuntime,LargeBeast*,UnifiedRoster*,NonhumanBatchCaseRouter,RangerFiringProbe,WarSandboxModelTrialSmoke,WarSandboxRosterPolicy,WarSandboxDeployment*,WarSandboxRuntimeDeployment}.cs、Assets/CharacterPilotSource（除 Quaternius Dragons/Giants）、G/CharacterPipeline（按 D2 只取闭包内的版本）、G/UnifiedRoster/Version03、EnemyModelsBatch*、LargeBeasts/Prepared02、NonhumanBatch2、RangerPilot 闭包文件、CharacterPilotPlayable/Settings、G/README.md、方案设计/兵种模型制作管线.md | 体积最大，受 D1、D2 约束 |
| C7 | feat(MassEngine): 火球溅射（可选字段，默认 0） | ME/Projectiles/{ProjectileGpuData,ProjectileGpuManager}.cs、Shaders/{ProjectileGeometry,ProjectileUnitCollision}.hlsl、CombatConfig 中与溅射相关的块、ProjectileSplashGpuTests | 备份在 Logs/AgentDragonFire（若有） |
| C8 | feat(MassEngine): 落点特效与拖尾宽度（可选，默认 null） | ME/Projectiles/{ProjectileRenderConfig,ProjectileGpuRenderDispatcher,ProjectileImpactFx}.cs、Shaders/{ProjectileSimulation.compute,ProjectileTrail.shader,ProjectileImpact.shader}、ProjectileImpactFx*Tests、Projectiles/README.md、ComputePipelineOrchestrator 中 DrawImpacts 相关部分 | |
| C9 | feat(Game): 骑兵与巨龙内容 | G/Editor/CharacterPipeline/{CavalryBuilder,DragonBuilder}.cs、Assets/CharacterPilotSource/QuaterniusDragons、G/Cavalry/Prepared05(闭包部分)+Prepared06、G/Dragons/Prepared02、05、12、Generated/{MountedKnight04,Dragon_Evolved02,Dragon_EvolvedYoung03}、DragonPhalanxBalanceTests | |
| C10 | feat(MassEngine): 骑兵冲锋（chargeDamageMultiplier 默认 1） | ME/Simulation/MeleeChargeParams.cs，6 个混改文件的"当前减 .orig"部分，MeleeChargeTests | D4 |
| C11 | fix(test): 夹具状态泄漏与 orchestrator 空 shader NRE | MassEngineGpuKernelTests.cs 的 SetUp 重置、ComputePipelineOrchestrator 的 `if (combat != null)` | 用 `git add -p` 挑出这两处 |
| C12 | feat(Game): 超大第二批：巨型恶魔 / 暴龙 | G/Editor/CharacterPipeline/GiantBuilder.cs、Assets/CharacterPilotSource/QuaterniusGiants、Generated/{Demon01,Dino01}、G/Giants/Prepared01、GiantBattlefieldTests | |
| C13 | docs: 交接与通宵报告 | Docs/、NEXT_TASK.md、ROADMAP.md、GAME_DESIGN.md、README.md、方案设计/流场三维扩展方案.md | 先看一遍 Docs/ 下 127 个文件，里面有截图 |

## 不要提交
- `Assets/InitTestScene*.unity` 及其 `.meta`（5 对）：Unity 测试运行器的残留临时场景，建议删掉。
- `.local-mcp-audit/`：MCP 审计本地文件，建议加进 .gitignore。
- D2 中列出的旧版本目录（除非你决定归档）。
- `ProjectSettings/EditorBuildSettings.asset`：等 D3 决定。

## 每次提交前的校验
1. `git diff --cached --stat`，确认路径和体积。
2. 编辑器能编译通过（C4、C7、C8、C10 之后各跑一次）：
   - PlayMode：`Projectile;MeleeChargeTests;MassEngineGpuKernelTests`（今晚最终状态为 120/120，见 reg-play-01）；
   - EditMode：`MassEngine`（431/433，另外 2 项属于 D3）。
3. 按功能拆出的中间提交可能单独编译不过（比如 C7 依赖 C4 的 Projectile 池）。**要么严格按上面的顺序提交，要么把 C7、C8、C10、C11 合成一个"引擎可选扩展"提交**，这样最省事。
