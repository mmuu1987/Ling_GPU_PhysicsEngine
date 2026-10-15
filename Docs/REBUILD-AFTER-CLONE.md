# 拉取代码后重建本地生成内容（2026-10-15）

仓库只提交代码、配置和文档。模型、贴图、网格 `.asset`、`.anim`、生成的地图场景都只留在本机（见 `.gitignore`）。
在新机器上克隆，或者本机的生成目录丢了、被清掉以后，按下面的顺序在 Unity 编辑器里运行菜单，就能重建这些内容。

## 前提：这些文件生成不出来，要从原机器拷贝

- `Assets/ThirdParty/`：第三方美术包，整个目录不在 git 里。
- 所有模型和贴图源文件（`*.fbx`、`*.png` 等），以及角色管线生成的 `Assets/Game/Authoring/CharacterPipeline/Generated/**/Full.asset`。
- 其他列在 `.gitignore` 的“大文件”“网格 .asset / .anim”两节里的文件。

上面这些都不在仓库里时，下面的菜单也跑不通（会提示找不到模型或预制体）。

## 菜单顺序

每一步都可以重复运行；要等上一步跑完（编辑器不再显示进度条）再点下一步。运行时不要在 Play 模式。

| 步骤 | 菜单 | 生成的内容（都不提交） | 说明 |
|---|---|---|---|
| 1 | `Tools/程序化道具/生成道具库（自然类试点）` | `Assets/Game/Content/ProcProps/` | 地图生成要用的道具 |
| 2 | `Tools/战场 1024/生成全部（2 张）` | `Assets/Game/Content/Battlefields/Maps1024/`、`Assets/ThirdParty/_Derived/Maps1024` | 翠绿平原、森林丘陵 |
| 3 | `Tools/战场 1024/程序化地图/生成全部（5 张，较慢）` | `Maps1024/` 下 5 张程序化地图 | 乱石荒原、秋枫林地、雪松高地、石林峡谷、密林小径 |
| 4 | `Tools/角色/男战士：重烘加剑 + 重建中远景（只生成新资产）` | `Assets/Game/Content/Characters/MaleWarrior-20261012/` | **必须在第 9 步之前**：带剑男战士的模型数据，远景修复要用 |
| 5 | `Tools/巨兽/生成巨兽暴龙（配置与放大模型）` | `Characters/Kaiju/Dino/Generated/`（放大后的网格和 VAT） | **必须在第 6 步之前**：同时生成 2048 阵容规则 `RosterPolicy2048`，模板才会用到它 |
| 6 | `Tools/战场 2048/准备模板（配置与场景）` | 2048 配置和模板场景 | |
| 7 | `Tools/程序化建筑/生成建筑库` | `Assets/Game/Content/ProcBuildings/` | 第 8 步摆放村镇要用 |
| 8 | `Tools/战场 2048/生成全部（4 张，较慢）` | `Assets/Game/Content/Battlefields/Maps2048/`、`Assets/ThirdParty/_Derived/Maps2048` | 丘陵会战、山脊隘口、峡谷台地、断崖高原 |
| 9 | `Tools/角色/远景修复：女角色重建远景 + 男对手改引用` | `Assets/Game/Content/FarLod/Batch-20261015-01/` | 女角色远景 300 面；男对手改用第 4 步的带剑模型 |

不用重建的：`Assets/Game/Content/VertexAO/` 是已搁置的顶点 AO 试验，游戏不使用。

## 跑完后的检查（可选）

- `Tools/战场 2048/接入检查（自动进 Play，自动退出）`：2048 地图能进、能开战。
- `Tools/巨兽/对战检查（自动进 Play，自动退出）`：巨兽对战、踩踏和截图，日志在 `Logs/Kaiju/smoke.txt`。
- 编辑器 Console 里没有导入错误或编译错误。

## 常见问题

- **第 9 步报找不到 `MaleSword_VAT.asset`**：说明第 4 步没跑或者没跑完，先跑第 4 步。
- **2048 地图里选不到巨兽**：第 5 步排在第 6 步之后了。重跑第 5 步，它会把 2048 场景重新指向 `RosterPolicy2048`。
- **第一次进战场很慢**：编辑器第一次编译着色器，几十秒到几分钟都正常，之后就快了。
