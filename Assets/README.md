# Assets — 从这里开始

**游戏入口：[`Game/Scenes/MainMenu.unity`](Game/Scenes/MainMenu.unity)**  
Unity 菜单：**Game → Start Here → Play Main Menu**。本轮只整理 Assets 顶层，不改变已经整理好的游戏入口。

## 顶层只按六类放置

| 目录 | 放什么 | 不放什么 |
|---|---|---|
| **Game/** | 正式场景、游戏代码、游戏内容及历史实验 | 不把第三方原始包散放在这里 |
| **MassEngine/** | GPU 仿真、流场、渲染等引擎实现 | 不放游戏入口和素材包 |
| **Art/** | 模型源、角色制作源、VAT 烘焙数据及导出产物 | 不将同名数据直接覆盖或合并 |
| **ThirdParty/** | 完整的第三方素材包与其许可、示例和工具 | 不拆散包内目录，不删除许可 |
| **Project/** | 项目级 Editor、Resources、渲染设置与输入配置 | 不放游戏业务脚本 |
| **Documentation/** | 设计方案和阶段记录 | 不是游戏启动入口 |

```text
Assets/
├─ Game/                          游戏入口与游戏层
├─ MassEngine/                    引擎层，位置不变
├─ Art/
│  ├─ Source/CharacterPilot/       原 CharacterPilotSource
│  ├─ Source/ModelTrials/          原 ModelTrialSource
│  └─ VAT/
│     ├─ Characters/              原 VAT_Data
│     └─ Idle/                    原 Idle，保留独立资源身份
├─ ThirdParty/                    五个散落素材包 + 原 KenneyNaturePlanB
├─ Project/
│  ├─ Editor/                     项目级导入与导出工具
│  ├─ Resources/                  保留 Unity 特殊目录名和内部加载键
│  ├─ Settings/                   URP、Renderer、Volume 等设置
│  └─ Input/                      InputSystem_Actions.inputactions
└─ Documentation/Design/          原“方案设计”，原文件保留
```

旧 `Assets/Scenes/SampleScene.unity` 现在位于 `Game/Experiments/ProjectTemplate/SampleScene.unity`，不是正式游戏入口。

## 约定

- 在 Unity 内移动资源并保留 `.meta`／GUID；除了 GUID 引用，也要同步更新硬编码路径。
- `Editor`、`Resources` 是 Unity 特殊目录名，已保留；不能随意改名。项目级脚本仍使用原程序集，不引入新的 `.asmdef`。
- 不合并两个旧目录中的同名 VAT 文件；相同文件名不代表相同资源或相同 GUID。
- FBX 导出工具的新默认位置为 `Art/Exports/XunXian/`，按需创建，不再在 Assets 根目录产生输出文件夹。
- 素材包升级或重新导入可能重新创建厂商的旧根目录：先检查变化，再使用本次路径对照迁移，不直接覆盖现有包。
- 隐藏的工具目录不属于 Unity 资源分类，本轮未删除；也未删除历史资源、已有构建包或既有未提交改动。

[游戏代码导航](Game/README.md) · [顶层整理记录与路径对照](../Docs/AssetsLayout-20261007/README.md)
