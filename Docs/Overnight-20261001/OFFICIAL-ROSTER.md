# 新兵种接入正式游戏（Official Roster v1，2026-10-01）

## 结论

正式游戏的启动菜单和战场目录换成 `Assets/Game/OfficialRoster/Version01`：M7.1 的 5 个首发战场排在最前面，内容和顺序都不变；后面接上 13 个新兵种战场，共 18 个。首次启动仍然自动进入"开阔对冲 · 快速上手"（512 人）。`EditorBuildSettings` 已经指向新菜单和这 18 个战场。

| # | id | 正式名称 | 来源场景 |
|---|---|---|---|
| 6 | cavalry-mounted-knight | 骑兵冲锋 | Cavalry/Prepared07 |
| 7 | dragons-dragon | 飞龙来袭 | Dragons/Prepared13 |
| 8 | dragons-dragon-phalanx | 飞龙 vs 密集方阵 | Dragons/Prepared13 |
| 9 | dragons-dragon-evolved | 进化巨龙 | Dragons/Prepared13 |
| 10 | dragons-dragon-evolved-phalanx | 进化巨龙 vs 密集方阵 | Dragons/Prepared13 |
| 11 | giants-demon | 巨型恶魔 | Giants/Prepared02 |
| 12 | giants-dino | 巨型暴龙 | Giants/Prepared02 |
| 13 | nonhuman2-triceratops | 三角龙冲阵 | NonhumanBatch2/Prepared01 |
| 14 | nonhuman2-stegosaurus | 剑龙防线 | NonhumanBatch2/Prepared01 |
| 15 | nonhuman2-spider | 巨型蜘蛛 | NonhumanBatch2/Prepared01 |
| 16 | unified-regular | 自由编成 · 常规兵种 | UnifiedRoster/Version03 |
| 17 | unified-all-regular | 全兵种同场 | UnifiedRoster/Version03 |
| 18 | unified-large | 自由编成 · 大型兵种 | UnifiedRoster/Version03 |

"斧盾骷髅 · 模型展示"属于模型展示，不是对局，所以没有放进正式目录。

## 设计约束

- 没有覆盖任何已有内容。M71 的目录、各名册目录和所有场景都只读不写。战场条目通过 `CopyIdentity()` 复制，场景和规则直接引用原资产。显示名、简介和简报换成面向玩家的文字，去掉了"超大第一批""非人形第二批""受限"之类的内部标签。简介里的军团数和人数是 builder 打开各场景统计出来的，没有写死哪一方会赢。
- 模板：M71 的 24 个模板加上名册的 30 个，按 templateId 和 config 去重后共 54 个，冲突时直接报错。旧存档方案引用的模板都还能解析。
- 新兵种没有加进原来那 5 个战场的配兵名册。巨龙和巨人的碰撞半径约 4 m，要把模拟格子放大到 9 m 以上；原战场有 512 到 10 万人，放大格子会直接拖垮性能。所以大体型兵种继续放在各自独立的战场。
- 预览图来自 `OfficialRosterPreviewTests`（PlayMode）。按卡片 2.17:1 的比例渲染成 1280×592，因为 RawImage 会直接拉伸。取景对准新兵种和离它最近的敌军，截的是开战后约 2.5 秒。最终选用的图见 `Logs/OfficialRoster/previews/picks.txt`。

## 新增与修改的文件

- `Assets/Game/Editor/OfficialRosterBuilder.cs`
  - `Prepare01`：生成内容，并把 EditorBuildSettings 写成新菜单加 18 个战场；
  - `Build01` / `Build02`：打包 Windows 开发版，输出到新目录。
- `Assets/Game/OfficialRoster/Version01/`：Catalog.asset、LaunchMenu.unity（复制自 M71 菜单，session 指向新目录），以及 Previews 下 13 张预览图。
- `Assets/Game/Scripts/WarSandboxTerrainCycle.Official.cs`：新增可选的 `--terrain-cycle-phase=official-catalog`，只在开发版生效。主文件和 Presets 文件各加了几处分支，原有模式的行为不变。
- 测试：
  - `Assets/Game/Tests/EditMode/OfficialRosterCatalogTests.cs`（4 项）：目录和模板校验通过；M71 的 5 个战场排在最前且完全不变；新战场的名称、预览和模板来源正确；菜单和 EditorBuildSettings 都指向正式目录；
  - `Assets/MassEngine/Tests/PlayMode/OfficialRosterPreviewTests.cs`：只截预览图，不做断言。

## 验证结果

| 项目 | 结果 | 证据 |
|---|---|---|
| Prepare01 | 18 个战场，54 个模板，18 张预览 | Logs/AgentOfficial/official-prepare-01.log |
| EditMode（正式目录、M71 预设、目录测试） | 32/34。新增 4 项全部通过；2 项失败是之前就有的问题 | Logs/AgentCharge/official-edit-01-* |
| Windows 正式包 | `Builds/OfficialRoster-20261001-02`，19 个场景 | Logs/AgentOfficial/official-build-02.log |
| 实机冒烟（official-catalog） | 18/18 个战场都通过卡片按钮进入；兵力完整、简报正确、有预览；开战、重置、返回后资源释放 18 次；0 错误；源资产未变；设置文件没被写入；用时 145 秒 | Logs/AgentOfficial/smoke-01/receipt.json |

`Builds/OfficialRoster-20261001-01` 内容和 -02 相同，只是不含 official-catalog 冒烟模式，按"不删旧产物"的规则保留。

## 后续可打磨

- 卡片排序和分组：目前是两列网格，按顺序排；条目多了可以考虑加分类页签。
- 预览图目前是灰色地面上的自动截图，可以换成更有氛围的构图。
- 新兵种战场的平衡和细节，按计划以后再打磨。
