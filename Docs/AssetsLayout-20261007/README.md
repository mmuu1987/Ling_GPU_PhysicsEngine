# Assets 顶层整理记录（2026-10-07）

## 结果

可见一级资源目录从 **17 个整理为 6 个**：Game、MassEngine、Art、ThirdParty、Project、Documentation。
游戏入口保持 `Assets/Game/Scenes/MainMenu.unity`，Unity 的 `Game → Start Here → Play Main Menu` 菜单仍可用。

- 完成 15 项资源移动，更新 50 个文件中的硬编码路径、相对文档链接或入口说明。
- 9,492 个纳入清单的 Assets 文件通过迁移后 SHA-256 检查；仅清单内的文本修改被允许。
- 原有 4,992 个 `.meta` 字节保持一致；Unity 验证 4,992 项资源身份与新路径一致。
- 381 个原有 C# 脚本的程序集归属不变；1 个 Resources 文件的加载键不变。
- 13 个检查根（现役场景、模板场景、渲染设置和输入配置）的 GUID 依赖集合不变。
- Build Settings 文件字节不变，四个现役场景路径和 GUID 不变；InputActionAsset 与项目渲染设置导入成功。
- Unity 批处理验证退出码 0。四场景共检查 7,813 个 GameObject，缺失脚本 0。
- 运行／制作代码、项目设置和 Tools 的检查范围内，没有残留旧顶层资源路径；迁移工具中的原路径规范和 `.meta/AssetOrigin` 来源信息不属于失效加载引用。

## 保留和边界

不删除资源、不合并同名 VAT、不重建 GUID、不改程序集定义、不覆盖原有未提交修改；没有 Git add/commit/reset。
`Art/VAT/Characters` 和 `Art/VAT/Idle` 中同名文件保留不同身份，不能仅凭名字当成重复文件删掉。
第三方包按整包移动，内部目录、许可、包文件和 AssetOrigin 来源信息保留；更新厂商包时可能重新出现原根目录，应先审核后再归类。
`.kiro` 等隐藏工具目录没有删除或迁移，不属于可见 Unity 资源分类。
没有修改玩法，没有运行完整 GPU 对局或性能回归，也没有生成或替换任何 EXE。

## 目录约定

- Game：游戏业务、入口场景与游戏内容；原有整理结果保留。
- MassEngine：引擎源码；根位置不变，只同步相关制作工具中的资产路径。
- Art：Source/CharacterPilot、Source/ModelTrials、VAT/Characters、VAT/Idle；XunXian 导出默认改到 Art/Exports/XunXian，按需生成。
- ThirdParty：五个原散落素材包及既有 KenneyNaturePlanB，保留包名便于追踪来源。
- Project：Editor、Resources、Settings、Input；Editor 和 Resources 的 Unity 特殊目录名保留。
- Documentation/Design：原“方案设计”；改过的路径／链接均有原文备份。
- 旧模板场景放入 Game/Experiments/ProjectTemplate；项目模板默认场景路径随之更新，不改变正式 Build Settings。

## 路径对照

| 原路径 | 新路径 |
|---|---|
| `Assets/CharacterPilotSource` | `Assets/Art/Source/CharacterPilot` |
| `Assets/ModelTrialSource` | `Assets/Art/Source/ModelTrials` |
| `Assets/VAT_Data` | `Assets/Art/VAT/Characters` |
| `Assets/Idle` | `Assets/Art/VAT/Idle` |
| `Assets/Polytope Studio` | `Assets/ThirdParty/Polytope Studio` |
| `Assets/Pure Poly` | `Assets/ThirdParty/Pure Poly` |
| `Assets/RPG Tiny Hero Duo` | `Assets/ThirdParty/RPG Tiny Hero Duo` |
| `Assets/Supercyan Free Forest Sample` | `Assets/ThirdParty/Supercyan Free Forest Sample` |
| `Assets/TriForge Assets` | `Assets/ThirdParty/TriForge Assets` |
| `Assets/Editor` | `Assets/Project/Editor` |
| `Assets/Resources` | `Assets/Project/Resources` |
| `Assets/Settings` | `Assets/Project/Settings` |
| `Assets/Scenes` | `Assets/Game/Experiments/ProjectTemplate` |
| `Assets/方案设计` | `Assets/Documentation/Design` |
| `Assets/InputSystem_Actions.inputactions` | `Assets/Project/Input/InputSystem_Actions.inputactions` |

## 证据与撤回依据

`Logs/AssetsLayout-20261007/`：

- `plan.json`：精确移动、修改文件、修改前后哈希。
- `manifest.before.json`、`identities.before.json`：文件保护清单与原 GUID。
- `backups/`、`patches/`：修改前原文与核对后的文本。
- `unity-before.json`：程序集、Resources 加载键与依赖集合基线。
- `apply.json`、`validate.json`、`audit-after.json`：本轮执行和验证结果。
- `build-settings.before`、`project-settings.before.json`：项目设置保护记录。
- `git-status.before-apply.txt`、`git-status.after.txt`：未提交状态记录（不是 Git 提交）。

如需撤回，请按 GUID 与移动清单在 Unity 中逆序移回，并按备份恢复本轮文本修改；不要使用 `git reset --hard`，否则会丢失整理前已有的修改。
历史日志和旧报告没有冒充新验证结果；上一次 Game 目录整理的哈希报告仍对应当时的快照，本轮结果请看本目录。
