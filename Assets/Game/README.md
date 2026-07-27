# Game — 战争沙盒（游戏层）

引擎之上的"这一款游戏"：阵营、场景、下令、观战。只依赖 `MassEngine` 程序集；
引擎永远不反向依赖这里。

## 内容

| 位置 | 内容 |
|---|---|
| `Scenes/WarSandbox.unity` | 主场景（原 Stage7_Test）：MassEngineSystem + 相机 |
| `Settings/` | 全部配置资产：Scenario/System/Shader/Simulation/Lod/RuntimeFlow/RuntimeCombat + 攻/防两套兵种配置（UnitTypeConfig + 六个子配置各一套） |
| `Scripts/ClickFlowTargetSetter.cs` | 点击地面 → `manager.SetFlowTargetOverride(teamId, point)`（运行时覆盖，不写资产）+ 可选自动开战 |
| `Scripts/Gizmos/` | Scene 视图的阵型/流场/目标 Gizmo（ScenarioGizmos 挂场景物体上） |
| `Scripts/CameraControls/` | 观战相机。场景在用：MyCameraManager（+其依赖 LocalRotationAndScale）。备用整洁版套件：RigCameraManager + SceneViewCameraRig/Input/Settings/BoundsUtility + CameraMouseOrbit（原 *_Stage7 副本，2026-07-27 已去后缀改名，暂无场景引用） |
| `Editor/WarSandboxSampleCreator.cs` | 菜单 MassEngine/Create Sample Configs And Scene：一键生成可跑的示例场景与配置（非破坏式：已存在的资产不动） |

## 玩法（当前）

进 Play（scene 里 battleStarted 默认开）→ 攻方沿流场压向守方 → 接战互殴。
鼠标点地面 = 给攻方（teamId 按组件配置）下移动令。
调参：直接改 `Settings/` 下资产，绝大多数参数下一帧生效。

## 扩展这款游戏

- 新兵种：见 `../MassEngine/UnitTypes/README.md`（三步，零改引擎）
- 新指令类型/新阵营 UI：写在本层，通过 `MassEngineManager` 的公共 API
  （StartBattle/StopBattle/ResetScenario/SetFlowTargetOverride）驱动引擎
- 战争沙盒编辑器（长期目标）：本层将来放编辑器 UI 与关卡序列化，
  引擎侧无需改动
