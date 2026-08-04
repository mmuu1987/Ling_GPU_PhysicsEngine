# Game — 战争沙盒（游戏层）

引擎之上的"这一款游戏"：阵营、场景、下令、观战。只依赖 `MassEngine` 程序集；
引擎永远不反向依赖这里。

## 内容

| 位置 | 内容 |
|---|---|
| `Scenes/WarSandbox.unity` | 主场景（原 Stage7_Test）：MassEngineSystem + 相机 |
| `Settings/` | 全部配置资产：Scenario/System/Shader/Simulation/Lod/RuntimeFlow/RuntimeCombat + 攻/防两套兵种配置（UnitTypeConfig + 六个子配置各一套） |
| `Scripts/ClickFlowTargetSetter.cs` | 点击地面 → `manager.SetFlowTargetOverride(teamId, point)`（运行时覆盖，不写资产）+ 可选自动开战 |
| `Scripts/ArmyOrder.cs` | 游戏层军团命令与运行时状态：进攻、移动、防守、撤退 |
| `Scripts/WarSandboxBattleController.cs` | 把军团意图接入引擎运行时导航 API，管理暂停、倍速、胜负与重开 |
| `Scripts/WarSandboxCommandHUD.cs` | 右侧运行时指挥面板与快捷键；移动命令消费下一次地面点击 |
| `Scripts/Gizmos/` | Scene 视图的阵型/流场/目标 Gizmo（ScenarioGizmos 挂场景物体上） |
| `Scripts/CameraControls/` | 观战相机。场景在用：MyCameraManager（+其依赖 LocalRotationAndScale）。备用整洁版套件：RigCameraManager + SceneViewCameraRig/Input/Settings/BoundsUtility + CameraMouseOrbit（原 *_Stage7 副本，2026-07-27 已去后缀改名，暂无场景引用） |
| `Editor/WarSandboxSampleCreator.cs` | 菜单 MassEngine/Create Sample Configs And Scene：一键生成可跑的示例场景与配置（非破坏式：已存在的资产不动） |
| `Editor/WarSandboxEditorWindow.cs` | 菜单 MassEngine/War Sandbox Editor：切换产品规模预设，编辑军团兵力、密度和阵型，以固定交战间距自动布阵并调用 Auto-Fit |
| `PerformanceBaseline.md` | 20k～400k 总单位的封版实测与产品档位 |

## 玩法（当前）

进 Play 后战场停在部署阶段。右侧面板选择攻方/守方并下令：

- `Enter` 双方开战：为攻守双方同时下达进攻命令；战后可一键再来一局
- `A` 进攻：启用该队动态敌情流场
- `M` 移动：下一次左键点击地面成为静态目标
- `H` 原地防守：关闭该队导航，但保留近身接战
- `R` 撤退：返回该队初始出生中心
- `Space` 暂停/继续；面板可切 0.5×/1×/2×/4×；重开恢复初始战场
- `F1` 跟随攻方、`F2` 跟随守方、`F3` 跟随双方战场、`F` 跟随当前选择阵营

部署阶段可选择两种规则：**歼灭战**沿用动态敌情流场，以消灭全部敌军取胜；**据点战**
会让双方默认向中央据点推进。据点半径内只有一方存在时开始占领，双方同时进入则争夺暂停，
空置时进度缓慢回中；任一方完成占领或提前歼灭敌军都会结束战斗。结算面板会记录胜因。

左下角战术地图显示双方实时范围、质心、命令目标与当前镜头位置；左键地图快速定位镜头，
右键直接给当前阵营下达移动命令。按下 `M` 等待目标时，左键地图也会作为命令落点。
按住 `Shift` 点击移动目标会追加航点；小地图绘制完整路线，军团质心抵达当前航点后自动
切换下一点。移动和撤退命令会在地面显示带阵营颜色的目标十字。镜头滚轮采用有界距离缩放，
飞行/平移也有单帧位移和世界坐标保护；异常输入不会再把 Transform 推到 NaN/Infinity
或极远坐标。右键飞行期间 A/M/H/R 不会误触发军团命令。

F/F1/F2/F3 使用低频 GPU 质心/范围遥测平滑跟随存活群体；右键、中键、Alt 或滚轮
会立即退出跟随，把控制权交还给玩家。

指挥面板实时显示攻守双方存活数/初始兵力；遥测确认任一方归零后自动暂停并显示胜负。当前一张流场对应一个阵营，所以“军团”
暂等同于攻/守两支大军；多军团独立导航需要未来的 groupId/多流场扩展。

调参建议通过 `MassEngine/War Sandbox Editor`，设置“初始交战间距”（默认 50m）后再点
`Auto-Fit 布阵与场景`。Auto-Fit 会根据人数、密度和阵面宽深比重新推导阵型纵深，
将 team 0/1 对称布置并保持指定的阵型边缘间距，同时配平 world/grid/flow；配置资产运行时严格只读。

需要切换规模时，可选择标准 1万、大型 5万、超大型 10万、压力测试 20万或自定义预设，
再点 `应用预设并 Auto-Fit`。预设按每个阵营的现有兵种比例分配总兵力，并作为一个 Undo 操作应用。

## 扩展这款游戏

- 新兵种：见 `../MassEngine/UnitTypes/README.md`（三步，零改引擎）
- 新指令类型/新阵营 UI：写在本层，通过 `MassEngineManager` 的公共 API
  （StartBattle/StopBattle/ResetScenario/SetFlowTargetOverride）驱动引擎
- 战争沙盒编辑器（长期目标）：本层将来放编辑器 UI 与关卡序列化，
  引擎侧无需改动

## 性能档位

默认产品档为 10k vs 10k（实测 113 FPS）；50k vs 50k 是约 30 FPS 的大型战役档；
100k/200k 每方属于压力与容量展示。详见 [PerformanceBaseline.md](PerformanceBaseline.md)。
