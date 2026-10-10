# Game — 从这里开始

## 游戏入口

**当前 Unity 入口：[`Scenes/MainMenu.unity`](Scenes/MainMenu.unity)**

- 在 Unity 双击该场景后点击 Play；也可以使用菜单 **Game → Start Here → Play Main Menu**。
- 只想打开场景：**Game → Start Here → Open Main Menu**。若当前场景有未保存修改，会先询问是否保存，不会静默丢弃。
- 正式构建场景顺序：`MainMenu → Green → Autumn → Winter`。后三个是可选战场，并非顺序关卡。
- 当前战场目录：[`Scenes/Catalog.asset`](Scenes/Catalog.asset)，菜单 **Game → Start Here → Select Battlefield Catalog** 可定位。
- 普通玩家流程：主菜单 → 选择战场 → 布阵／军团详情 → 战斗 → 结算／返回菜单。

**已有独立试玩包没有移动或重建：**项目根目录 `Builds/CaptureDefault-20261006-37/Start-Game.cmd`。源码与旧包的差异、旧包 GUID 及验证边界仍以[现役交付索引](../../Docs/CURRENT_DELIVERY.md)为准。

> 原入口 `Contact34/LaunchMenu.unity` 已保留原 GUID 移至 `Scenes/MainMenu.unity`。旧 `OfficialRoster/Version08/LaunchMenu.unity` 不是当前入口；它已归入 `Content/Characters/OfficialRoster/`。

## 目录导航

| 目录 | 用途 |
|---|---|
| `Scenes/` | **正式入口、三个现役战场、Catalog 与对应 Flow/System 配置** |
| `Content/Characters/` | 角色、兵种、角色制作产物及其版本资料 |
| `Content/Battlefields/` | 地形、植被、季节、阵型及共享战场资源 |
| `Content/UI/Previews/` | 界面预览图片 |
| `Scripts/` | 游戏运行时代码；保留原程序集边界和脚本身份 |
| `Editor/` | Unity 编辑器菜单、制作工具、构建与验证工具；不是游戏入口 |
| `Authoring/CharacterPipeline/` | 角色制作管线资源 |
| `Experiments/` | 历史原型、阶段验证场景、压力测试；不作为默认入口 |
| `Resources/` | 通过 Resources 加载的内容；内部相对路径保持不变 |
| `RuntimeDependencies/` | 运行时依赖内容 |
| `Settings/` | 游戏配置 |
| `Shaders/` | 游戏层着色器 |
| `Tests/` | EditMode／PlayMode 测试 |

`Content` 和 `Experiments` 是分类，不是“全部现役”或“可以删除”的标记。历史场景仍可能共享角色、地形或配置，不要按目录名字直接删资源。

## 从哪段代码看起

本项目使用 Unity 场景和 MonoBehaviour 生命周期，没有自定义的 `Program.Main()`。

1. **菜单与跨场景会话**：`Scripts/WarSandboxSceneSession.cs`，`Awake()` 保存菜单路径并维护会话；`TryEnterBattlefield()` 根据 Catalog 验证并异步加载战场，`TryReturnToMenu()` 返回入口。
2. **主菜单界面**：`Scripts/WarSandboxFrontEnd.cs` 及同名分部文件，当前玩具风界面重点看 `.Toy.cs`。
3. **布阵和军团编辑**：`Scripts/WarSandboxRuntimeDeployment.cs`、`WarSandboxDeploymentHUD.*.cs`。
4. **战斗状态及命令**：`Scripts/WarSandboxBattleController.cs`、`WarSandboxCommandHUD.*.cs`。
5. **GPU 引擎实现**：`Assets/MassEngine/`，不是另一套游戏入口。
6. `WarSandboxRuntimeBootstrap.cs` 为旧 WarSandbox 命名场景补齐组件，不是需要手工打开的启动文件。

## 整理约定

- 新游戏入口或现役战场放入 `Scenes/`，不要再以交付序号新建 `Game/XXX34` 一类根目录。
- 角色资源放 `Content/Characters/`；地形与环境资源放 `Content/Battlefields/`；实验放 `Experiments/`。
- 对 Unity 资源使用 AssetDatabase 移动，保留 `.meta`／GUID；同时核对代码字符串、Catalog 场景路径和 Build Settings，不能只依赖 GUID。
- 不移动 `Resources` 内部路径，不改变 `.asmdef` 编译范围，不把旧实验当作可直接删除的内容。
- 引擎仍在 `Assets/MassEngine/`；素材源与 VAT 归入 `Assets/Art/`，素材包归入 `Assets/ThirdParty/`，项目级渲染／输入／Editor 工具归入 `Assets/Project/`。顶层导航见 [`Assets/README.md`](../README.md)。

## 历史记录与迁移证据

- [整理前 Game README（原文保留）](../../Docs/GameLayout-20261007/Game-README.before.md)
- [目录整理记录和旧路径对照](../../Docs/GameLayout-20261007/README.md)
- [现役交付索引](../../Docs/CURRENT_DELIVERY.md)
- `Logs/GameLayout/` 保存迁移计划、原文件哈希、路径修改备份及 Unity 验证回执。

本轮只整理目录和入口导航，不增加玩法、不清理已有未提交修改、不提交 Git，也不将旧版本的性能／试玩结论视为本轮验证结果。
