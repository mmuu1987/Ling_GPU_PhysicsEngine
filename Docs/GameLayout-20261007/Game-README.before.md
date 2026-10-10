# Game — 战争沙盒（游戏层）

> **现役交付（2026-10-06）**：当前为据点默认开战修复37版，入口、GUID和验证边界统一以[现役交付索引](../../Docs/CURRENT_DELIVERY.md)为准。下方早期包与测试记录按各自日期保留为历史，不作为现役验收结论。

> 新增方案B独立地形试装13：`Builds/TerrainPlanB-20261004-13/Start-Terrain.cmd`，Q版丘陵林地、真实高度/禁行及布阵高程图，256单位运行检查通过。11主流程与旧战场未替换。说明：`Docs/ProductPolish-20261003/TERRAIN-PLAN-B-20261004.md`。用户已改选B，不再等待方案A素材领取。

> 历史11版（2026-10-04）：选兵新增近战/远程筛选、当前兵种标记和本局生命/攻击；军团人数支持显式更新与草稿状态提示，阻止非法人数写入和失败后误跳转。10版相机/拖动保持。详见 `Docs/ProductPolish-20261003/LEGION-FLOW-11-20261004.md`。

> 历史10版（用户已确认基本可用）：用户已确认09透视和默认角度；本版仅修正拖动两轴方向，使模型跟随手势。14/14定向测试与独立EXE 33代表检查通过；待用户鼠标手感复查。说明：`Docs/UnitPreview-20261003/DRAG-DIRECTION-10-20261004.md`。下方09相机修复记录保持有效，旧拖动方向结论由10版替代。

> 历史09版修正相机重复GPU投影转换导致的反向深度，默认改为正面偏侧俯视；14/14定向测试及独立EXE 33代表检查通过，四实际角色与标准Camera轮廓一致。[当前说明](../../Docs/UnitPreview-20261003/CAMERA-DEPTH-FIX-20261004.md)。08仅顶底标记通过不代表遮挡正确，旧根因/修复口径已被本轮纠正。
> **历史阶段记录（2026-10-03）：初级可玩产品基本完成，进入已有功能与产品细节打磨。** 试玩入口 `Builds/ToyUI-20261004-11/Start-ToyUI.cmd`（1920×1080全屏），GUID `1b057a4b1d0b426fbf81c7b0b60c3a6a`。Unity入口仍为 `Assets/Game/OfficialRoster/Version08/LaunchMenu.unity`。阶段目标见[产品打磨说明](../../Docs/ProductPolish-20261003/README.md)，代码/证据入口见[工程导航](../../Docs/Engineering-20261003/README.md)。用户已试玩，未决专项验收与V1门禁未自动关闭。

## 当前游戏层与展示契约

- 普通启动：主菜单→目录→布阵⇄军团详情→战斗→结算；显式自动化启动参数不代表普通玩家入口。
- 军团详情编辑组成、人数、归属与属性；布阵编辑位置/阵型。返回只保留草稿和镜头/选择，不自动应用、保存或开战；方案与属性页按需打开。
- 当前Q版浅色呈现入口：`WarSandboxFrontEnd.Toy.cs`、`WarSandboxDeploymentHUD.Legion.cs`、`WarSandboxCommandHUD.Toy.cs`；共享控件为 `WarSandboxUGUI`。部分旧方法只作非活动参考，不将其布局当作现役界面。
- 图鉴通过 `WarSandboxRosterChoices.Library` 将66条未撤回配置展示为33个代表；目录68个模板身份不删除。29个可选战场/31个身份保留。
- `WarSandboxRuntimeDeployment.ChoiceTemplates` 只用于新选兵呈现，按当前合法集合去重并保留近战/远程用途；完整 `Templates` 继续用于配置与方案校验，不以图鉴可见性改写旧军团或存档。
- 三层数值仍按模板身份生效。代表模型的全局修改不自动传播到同外观旧变体；不要隐式迁移编号、归属或参数。
- 共用 `ui.ModelPreview(id, rect, exactConfig, fallback)` 接入图鉴/放大弹窗/军团详情；渲染服务与RawImage交互控件分层，使用真实VAT，详情传实际模板，隐藏释放资源。用法见[3D预览组件](../../Docs/UnitPreview-20261003/README.md)。
- 历史3D预览3/3定向PlayMode及04独立EXE（33代表）通过。此前62/62去重EditMode与1/1 UI PlayMode仍属[02去重记录](../../Docs/ToyUI-20261003/ROSTER-DEDUP.md)，不移用为04全量成绩；非OS输入/全场自然结算验收。

以下按日期保留实现历史；旧入口、旧“下一步”和旧包成绩不覆盖上述现状。



## 第一套派生骑兵（2026-09-30）

已有CC0骑士+官方CC0马，新制作坐姿/鞍位跟随/四动作组合VAT，统一库新增剑盾骑兵入口。正式UnifiedCavalry-20260930-02/Start-Roster.cmd，工程Assets/Game/Cavalry/Prepared01/Integrated02。骑乘与巨兽接敌上下文分开；不是新上下马/冲锋系统或原厂成套动画。详见[骑兵说明](../../Docs/Cavalry-20260930/README.md)。既有代码/核心/旧包不动，人工骑乘观感待确认。

## 非人形第二批直接扩展统一库（2026-09-30）

前版统一库用户已试玩认可。新三角龙、剑龙、蜘蛛04加入扩大后的大型菜单，旧12常规/混合示例保留，合计17不同身体；不是另造孤立单角色入口。新包UnifiedNonhuman2-20260930-01/Start-Roster.cmd，项目Assets/Game/NonhumanBatch2/Prepared01/Integrated。仍受大型队伍/网格/占地限制，不新技能/核心/100k。详情[本批说明](../../Docs/NonhumanBatch2-20260930/README.md)。新三种人类观感待确认，旧包/方案原样保留。

## 新旧统一角色库（2026-09-30）

原男/女/UnityChan复用既有战斗资产，原Sazen骷髅补齐四动作，与新增角色进入同一12种常规布阵菜单；另有受限大型场。入口Builds/UnifiedRoster-20260930-01/Start-Roster.cmd，工程Menu在Assets/Game/UnifiedRoster/Version03/Scenes。不是只注册Catalog：初始2种也能选12；全角色示例12类实际同场和保存重载。新RosterPolicy只在新场启用，旧无policy行为保留。详见[整合说明](../../Docs/UnifiedRoster-20260930/README.md)。UnityChanUCL及原AssetStore许可不变；非全局玩法/性能/法律V1签收。

## 大型兽形第一批（2026-09-30）

用户选择大型兽形，正式巨狼/公牛两种约6m四足。Start-Beasts.cmd位于Builds/LargeBeasts-20260930-01；目录包含此前6敌方。默认8大型对32骑士，私有9m网格、稀疏阵型和可互相触及的中心近战配置。源/姿态/LOD与移动转向、自然交战、保存重载验证见[本批说明](../../Docs/LargeBeasts-20260930/README.md)。Spider未过几何门禁，不列正式模型；碰撞是保守圆近似，不承诺零穿插。核心不改，验收器默认兼容64/96；本批人类观感待确认。

## 敌方第二批：兽人／雪怪／蘑菇怪（2026-09-30）

模型优先继续。本包EnemyModelsBatch2-20260930-01/Start-Enemies.cmd聚合6场（新3怪物+上一批3骷髅只读引用）。不同作者43骨骼、自带动作，通过本批单位缩放准备器和原始逐帧位置对照后，复用原管线/近战。配方Orc、Yeti、MushroomKing，各独立资产和场景身份。详见[第二批说明](../../Docs/EnemyBatch2-20260930/README.md)。没有既有C#/核心修改；人类观感待确认，非玩法/平衡/100k专项。

## 敌方模型扩充第一批（2026-09-30）

用户认可Ranger02后明确优先模型数量，本批新增斧盾、双刃、持杖三个不同骷髅模型，复用现有近战/投射逻辑。入口`Builds/EnemyModels-20260930-01/Start-Enemies.cmd`，战场目录可切三场。最终配方SkeletonWarrior、SkeletonRogue、SkeletonMage03；每种具备四动作/LOD/独立场景与保存重载证据。详见[敌方模型专项](../../Docs/EnemyModels-20260930/README.md)。没有既有C#/核心改动；本批真人观感待确认。

## 第二角色持弓游侠：Ranger02（2026-09-30）

04已获用户实际测试“完全ok”。当前新试点 `Builds/RangerPilot-20260930-02/Start-Ranger.cmd`，使用配方 `Assets/Game/CharacterPipeline/Recipes/Ranger02.asset`。同一Medium骨架的不同远程角色；只新增有界形变附件支持和私有资产/只读证据，不改战斗核心。01为挂接诊断历史，02才是校准后入口。详情 [游侠专项](../../Docs/RangerPilot-20260930/README.md)。真人Ranger观感待确认，非兵种平衡/100k/V1放行。

## 角色接入流程第一版（2026-09-30）

新制作入口 `MassEngine > Character pipeline > Open workflow`，骑士示例 `Select Knight example`。使用已准备Generic模型、显式附件与四动作，尺寸/预算检查、全帧位置门禁、私有配置/试点、失败回滚均走同一核心。新Knight04是独立复现，不替换03或正式主包。说明 [角色接入流程](../../Docs/CharacterPipeline-20260930/README.md)。已有Knight04禁止覆盖，后续选择新输出名；真人窗口点击/观感待确认。

## 2026-09-30 骑士头颈修正03（覆盖此前02外观判断）

当前独立骑士入口为 `Builds/CharacterPilot-Knight-20260930-03/Start-Knight.cmd`。源模型完整；准备层级缩放与BakeMesh变换叠加导致旧Full部件重复缩小，已在私有配方修复。114帧×6666顶点独立蒙皮对照最大约1.194mm，Low1337不增预算。详情 [头颈修复](../../Docs/KnightNeck-20260930/README.md)。01/02保留，非主候选替换，人工观感待确认。

引擎之上的"这一款游戏"：阵营、场景、下令、观战。只依赖 `MassEngine` 程序集；
引擎永远不反向依赖这里。

产品总方向见 [根目录产品总策划案](../../GAME_DESIGN.md)；本文只说明游戏层当前实现和使用方式。

当前人工试玩候选包为 `Builds/M7HumanPlaytest-20260928.zip`，解压后从 `Start-WarSandbox.cmd` 正常启动；操作说明和空白反馈表随包提供。与M73FarLod同GUID、运行文件逐字节一致，五预设烟测通过；人工体验尚待填写，见 [试玩包准备](../方案设计/M7人工试玩包准备.md)。

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
| `Editor/WarSandboxEditorWindow*.cs` + `WarSandboxEditor.uss` | 菜单 MassEngine/War Sandbox Editor：五页引导式制作工具，大字输入、逐项讲解、军团编排、布阵预览、方案保存与试跑 |
| `Editor/WarSandboxDeploymentPlan.cs` | 编辑器部署快照：保存/恢复多军团编成、阵型、手工位置及配平后的空间尺寸，支持 Undo |
| `PerformanceBaseline.md` | 20k～400k 总单位的封版实测与产品档位 |

## 玩法（当前）

进 Play 后战场停在部署阶段。右侧面板选择军团并下令：

- `Enter` 所有已部署军团开战：为每支军团下达默认命令；战后可一键再来一局
- `A` 进攻：启用该队动态敌情流场
- `M` 移动：下一次左键点击地面成为静态目标
- `H` 原地防守：关闭该队导航，但保留近身接战
- `R` 撤退：返回该队初始出生中心
- `Space` 暂停/继续；面板可切 0.5×/1×/2×/4×；重开恢复初始战场
- `F1` 跟随攻方、`F2` 跟随守方、`F3` 跟随双方战场、`F` 跟随当前选择阵营

部署阶段可选择两种规则：**歼灭战**沿用动态敌情流场，以消灭全部敌军取胜；**据点战**
会让所有已部署军团默认向中央据点推进。据点半径内只有一支军团存在时开始占领，
多支军团同时进入则争夺暂停，空置时进度缓慢回中；任一军团完成占领或提前歼灭敌军都会结束战斗。
结算面板会记录胜因和获胜军团。

左下角战术地图显示各军团实时范围、质心、命令目标与当前镜头位置；左键地图快速定位镜头，
右键直接给当前军团下达移动命令。按下 `M` 等待目标时，左键地图也会作为命令落点。
按住 `Shift` 点击移动目标会追加航点；小地图绘制完整路线，军团质心抵达当前航点后自动
切换下一点。移动和撤退命令会在地面显示带阵营颜色的目标十字。镜头滚轮采用有界距离缩放，
飞行/平移也有单帧位移和世界坐标保护；异常输入不会再把 Transform 推到 NaN/Infinity
或极远坐标。右键飞行期间 A/M/H/R 不会误触发军团命令。

F/F1/F2/F3 使用低频 GPU 质心/范围遥测平滑跟随存活群体；右键、中键、Alt 或滚轮
会立即退出跟随，把控制权交还给玩家。

指挥面板实时显示每支军团的存活数/初始兵力和据点兵力；遥测确认胜负条件后自动暂停并显示胜者。
每个军团拥有独立流场、战术姿态和运行时目标，`teamId` 直接对应军团，不额外引入 `groupId`。

调参通过 `MassEngine/War Sandbox Editor`。窗口分为五个独立页面，底部有上一页 / 下一步；
宽窗口使用左侧导航，窄窗口改为顶部换行导航，页面内容独立滚动。默认 16 号字，顶部可选
14 / 16 / 18 号并记住偏好；“逐项讲解”默认开启，显示含义、推荐值、示例和下一步。
关闭讲解只收起辅助说明，关键操作提醒仍保留。

### 第一次使用

1. **开始使用**：连接当前场景，或点“打开默认战场”；通常不用展开手动配置。
2. **军团配兵**：选择要修改的编成，例如把人数改为 1,000。一次只显示一个编成，
   同军团编号互为友军，不同编号敌对。密度推荐 0.5、宽深比推荐 2；推荐阵型按钮会关闭手动占地。
3. **战场布阵**：点“按当前兵力自动布阵”，检查俯视预览的重叠 / 越界提示。
   点击色块可切到对应编成的手动位置编辑，也可在 Scene 中定位全军或当前编成。
4. **保存与试跑**：保存配置与当前战场场景；点击“保存并进入试跑”后，点击 Game 画面，
   按 Enter 开战、Space 暂停。点击 Unity 顶部停止按钮后再回来修改。
5. **帮助中心**：可搜索军团 / 编成、密度 / 宽深比、远程混编、坐标、保存方案等问题，
   每条说明都有返回对应操作页的入口。

布阵页的“阵前间距”默认 50m。Auto-Fit 会根据人数、密度和阵面宽深比重新推导阵型纵深，
将 team 0/1 对称布置并保持指定的阵型边缘间距，同时配平 world/grid/flow；配置资产运行时严格只读。

需要切换规模时，可选择标准 1万、大型 5万、超大型 10万、压力测试 20万或自定义预设，
在布阵页展开规模预设，再点 `应用所选人数，并自动布阵`。预设按军团 0 / 1 现有兵种比例分配总兵力，
额外军团人数保持原值，并作为一个 Undo 操作应用。自动布阵作用于窗口当前选择的 Manager，
会保存配置资产；它不会在切页、改人数、载入方案时自动执行。

在非 Play Mode 下，“军团配兵 → 新增编成”列出 `Assets/Game` 中可用的 `UnitTypeConfig` 模板，
也可从其他文件夹手动选择。新编成默认 1,000 人，可以修改，再添加到已有军团或添加独立军团。
独立军团使用当前最大编号加一（上限 7）；添加到现有军团使用清单选择，不要求手输编号。
复制会创建独立的 `UnitTypeConfig` 和 `SpawnConfig` 资产，因此新编成可单独调整兵力、出生中心、阵型和
`teamId`，移动/战斗/动画/渲染等模板引用仍保持共享；“移除”只从当前 `ScenarioConfig` 编成列表中解绑，
不会删除资产，并可用 Undo 恢复。模板、SpawnConfig 或 ScenarioConfig 未保存时，操作会拒绝且不写入部分结果。
连续新增会始终沿用用户选定的复制来源，不会自动把刚生成的副本改成下一次模板。

部署方案通过“保存与试跑”的 `另存新方案` 保存为独立资产；选择方案后可覆盖或载入。
它保存兵种引用与顺序、teamId、兵力、出生中心、密度、宽深比、手动脚印、交战间距，以及当前世界/网格/流场尺寸。
载入精确恢复这些数值，不会重新 Auto-Fit 或移动手工位置；一次 Undo 可撤销整次载入，
确认后用 `保存配置与当前场景` 将恢复的配置写入磁盘。Play Mode 及进入 Play Mode 期间禁止配置写入，
仍可切页、调整文字、阅读帮助和查看预览。增删、修改参数、自动布阵与方案载入保留 Undo。

试跑入口复用引擎的兵种校验：实现类不存在或未实现兵种接口时，显示具体编成和原因并禁止试跑，
避免开战后才发现编成被跳过。此时仍可编辑和保存待修复的配置；可选子配置的缺失警告不阻止试跑，
继续沿用引擎默认值。点击试跑时会再次检查当前配置。在 Inspector 中修改配置后，
返回战争沙盒窗口会刷新当前页和试跑状态，无需手动切页。

方案不是兵种资源包或战斗存档：战斗/移动/动画/渲染配置仍由原有兵种资产共享，规则、障碍和运行时命令暂不包含。
兵种和 SpawnConfig 引用必须存在，且当前 Manager 必须配有 Scenario、Simulation 和 RuntimeFlow 配置；
检查不通过时不写入任何配置，也不会覆盖已有的有效方案。

## 战场规则归档（M3.1）

规则资产位于运行时 Game 程序集：`Scripts/WarSandboxBattlefieldConfig.cs`。
保存模式、据点中心/半径/占领时长、障碍开关/避让间距和最多 8 个 XZ 矩形；不保存军团部署、命令或胜负状态。

1. 在“战场布阵 → 战场规则与障碍”中操作。没有 Controller 的场景先点击创建按钮；
   已有 Controller 但缺指挥面板时可补齐 HUD。创建/绑定支持一次 Undo，不依赖场景名。
2. 选择规则资产不会立即载入；点击“载入规则”才替换当前 Controller 的规则，并绑定定义。
   载入与覆盖各支持单步 Undo；完整校验失败时不修改任何规则或部署数据。
3. 修改场景规则会解除旧定义绑定，形成场景草稿，不会改写原规则资产。
   “另存规则”创建独立资产；“覆盖规则”经确认后更新所选资产。两者都会绑定保存后的定义。
   新建资产的文件保留，Undo 撤销场景绑定；已有资产的覆盖可撤销内容。
4. 最后到“保存与试跑”保存场景，再进入 Play。规则归档与部署方案是两个独立操作，互不覆盖。

两份样例在 Settings 中：BattlefieldRules_A_Annihilation（歼灭、无障碍）和
BattlefieldRules_B_ControlPoint（据点半径 24m、占领 15s、双墙）。未自动改写已有场景绑定。
样例测试检查默认出生区域、据点与墙的间距及世界边界；实际可玩性仍需人工验收。

Controller 在 Awake 或首次规则使用时校验并复制定义，显式运行时应用只允许 Setup。
本局数组、重开快照与原资产分别持有独立数组；Setup 中的临时模式/障碍选择不会被每帧覆盖。
重开恢复本局已选定义的初始规则，不重新读取已被其他地方修改的共享资产；未绑定定义的旧场景仍保留旧行为。
非法定义会阻止开战并在 HUD 显示具体字段。禁用障碍仍保留布局，保存内置布局时归档实际的两堵墙。
制作窗口和编辑器规则接口在 Play Mode 及进入 Play Mode 期间禁止写入配置。

M3.1 不包含玩家存档或运行时军团编辑；后续场景入口见下节，玩家方案闭环仍属 M4。

## 战场入口（M3.2）

`Scenes/WarSandboxMenu.unity` 是构建的首场景。首次启动自动进入目录的默认战场，保持 Setup，
玩家明确下令后才开战。底部“战场目录”返回选择界面；交战或暂停中返回需确认，取消后恢复此前状态。
目录有“开阔会战”和“双墙据点”两个条目，使用真实场景截图作为缩略图。
两者复用 WarSandbox 场景及其默认部署，只选择不同规则，没有复制或改写原 Scenario/Spawn。
此旧入口沿用现有大规模部署；独立轻量入口与 M7.2 全军团结算见下文。

- `WarSandboxBattlefieldCatalog` 保存稳定 ID、名称、构建场景路径、规则与预览图。
  Inspector 提供 SceneAsset 选择便利，运行时只读路径，不引用 UnityEditor 或部署方案接口。
- `WarSandboxSceneSession` 在加载前完整校验并复制请求；重复加载被拒绝。
  通过 Single 模式切场，不同时保留两个模拟场景；菜单只持久保留会话与界面，不保留旧 Controller。
- 新场景 Awake/OnEnable 完成后，在 sceneLoaded 中暂停 Manager、检查默认配置/GPU/相机并应用规则，
  在 Start/Update 接收开战命令之前完成接线。加载/确认/失败期间 Controller 与 HUD 的命令入口被锁住。
- 加载前失败不卸载原场景。加载后配置失败会禁用模拟，显示具体原因，可返回目录并重试，
  不声称恢复已经卸载的上一局。返回会清除军团选择、命令、胜负、速度和规则请求，释放 GPU 资源。
- 从其他地方直接加载战场时不继承上一项规则。原有直接打开场景的 bootstrap 触发范围保持不变。

制作入口：`MassEngine/Create Battlefield Entry` 只在缺失时创建目录/菜单，并把所需场景加入构建。
已有目录或菜单不覆盖。目录 Inspector 可独立校验，构建前还会检查被场景引用的目录依赖。
手工修改某个预设的共享部署前仍需显式复制部署资产；规则隔离不等于 Spawn 隔离。

Windows 开发构建：菜单 `MassEngine/Build Windows Sandbox`，产物为 `Builds/WarSandbox/WarSandbox.exe`。
`--war-sandbox-menu` 可用于直接查看目录；默认启动仍进入已部署战场。
开发构建专用的 `--war-sandbox-smoke --war-sandbox-smoke-output <目录>` 执行换场、失败恢复和 GPU 释放检查，
输出 JSON 报告和桌面/窄窗口/战场截图，断言图片非空白且尺寸正确。
烟测先等待开屏结束，并执行一次实际窗口尺寸切换，避免隐藏启动时抓到未呈现的首帧。
该烟测不依赖 NUnit 或 UnityEditor，非开发构建不包含烟测入口。

追加 `--war-sandbox-full-battles` 会执行完整对局检查：按原始部署与正常速度等待两套预设自然结算，
每局最多等待 600 秒真实时间，不改血量、兵力或遥测，也不代替玩家下额外战术命令。
报告增加每局模式、胜因、胜者、真实/模拟耗时及各军团初始/存活人数，并保存结算画面。
结算后检查普通命令不会复活旧局、结果保持不变、显式重开恢复满兵和规则，返回释放场景/GPU。

战斗终局仅允许查看、重开或返回；进攻、移动、防守、撤退和继续命令不能重新启动已结算的模拟。
“再来一局”先重置再下默认命令；“返回部署”清除结算和本局损失，回到 Setup。
短时烟测不等于完整对局；两者都不代替人工视觉验收或 V1 首次试玩验收。

### 完整对局验证（M3.3）

2026-09-07 在 Windows 开发构建中，以现有 50k/50k/10k 三方部署和正常速度，仅下达开战默认命令：

| 预设 | 胜因与胜者 | 模拟时长 | 自动报告存活人数（攻 / 守 / 第三军团） |
|---|---|---|---|
| 开阔会战 | 攻方歼灭胜 | 360.8 秒 | 438 / 0 / 0 |
| 双墙据点 | 守方占领胜 | 315.8 秒 | 22798 / 31938 / 3295 |

两局均通过结果保持、终局命令限制、满兵重开、规则/资产隔离、返回与 GPU 释放检查。
结果来自真实 GPU 对局，不修改兵力、伤害或遥测；不保证每次运行产生同一胜者或人数。
证据在 Logs/PlayableBattles/player-full-1/report.json 与同目录结算截图。
EditMode 185/185、GPU PlayMode 43/43；该轮没有修改引擎、旧战场或部署资产。
上述为早期验收口径。M7.1 已制作独立轻量预设，M7.2 已补齐并验证全军团冻结统计及 uGUI 结算表；见后文。

## 游戏内战前布阵（M4.1）

独立程序的右侧指挥面板新增“配兵布阵”；开战后使用“返回布阵”，运行中的战斗需确认结束，取消则恢复原暂停/运行状态。
布阵界面支持以下操作：

- 从当前战场内置编成的兵种模板中选择、增加编成和军团、移除编成或整个军团，支持 32 步撤销/重做。
- 修改军团、人数、中心 X/Z、密度/宽深比或手动宽深；“更新编成”更新草稿和部署图。
- 部署图显示阵营脚印、障碍和据点；选择脚印后启用放置工具，点击地图设置中心，和数值输入使用同一数据。
- “应用布阵”重建预览战场并保持 Setup；“应用并开战”验证并应用后开战；“取消修改”恢复应用前的布阵。
- 窄窗口在“编成”和“部署图”之间切换，属性区独立滚动；编辑时战斗快捷键与观战相机输入被隔离。

草稿允许暂时不合法，以便调整和撤销；应用必须通过人数、有限数值、阵型密度、边界留白、脚印重叠和障碍校验。
错误时不写入部分结果、不重分配 GPU、不自动移动布阵。当前上限为 32 个编成、8 个引擎军团槽和全场 400k 人，
上限只用于限制非法分配，不代表性能承诺；V1 首发内容仍按 2～4 团设计。

运行时职责分离：

- `WarSandboxDeploymentDraft` 保存值快照和有界撤销历史，不修改模板；脚印与出生模块共用 `SpawnConfig.ResolveSpawnSize`。
- `WarSandboxRuntimeDeployment` 管理应用、取消和恢复。编辑时暂停模拟并关闭 dispatch，保留上一版 GPU 分配以便取消；
  应用通过现有 `ResetScenario` 路径释放/重建，分配失败时恢复上一版，仍保留可修正的草稿。
- `WarSandboxDeploymentInstance` 为应用后的 Scenario、每个 UnitType 与 Spawn 分别持有独立运行时副本，
  战斗、移动、动画和渲染模板仍只读共享。替换副本或离开战场时，先释放 GPU 对它们的引用，再销毁副本。
- 重开恢复应用过的完整兵力、布阵与当时的规则，清空战果和命令；返回布阵从该配置开始，不读取伤亡后的 GPU 状态。

未保存的运行时布阵会随退出/换战场丢弃，未应用的草稿离开前会提示确认；本地方案库见下一节。
地形表面编辑与导航适配仍属于 M5，未在此阶段实现。
既有 M2 制作工具保持 Editor-only，运行时没有调用其资产写入接口；原场景、部署资产和 GPU 数据布局不变。

开发构建可在入口烟测参数上追加 `--war-sandbox-deployment`，覆盖桌面/窄窗口截图、非法草稿拒绝、
384 人混编的真实自然结算、满兵重开、改为 352 人再次开战，以及源资产隔离和 GPU/配置副本释放。
该小规模场景由烟测通过玩家布阵接口临时创建，不覆盖原有 11 万单位样例，不改兵种伤害/血量或注入战果。

## 本地方案库（M4.2）

布阵界面右上角的“本地方案”提供另存、覆盖确认、列表刷新和载入确认；编号支持 A/B 等独立槽位，名称支持中文。
默认文件目录为 Application.persistentDataPath/WarSandboxPlans。方案列表只读取该目录的 JSON 文件，不操作项目配置资产。
保存提交有效输入并校验全部部署；加载只替换草稿，不立即重建 GPU，规则和部署一起进入一次撤销/重做操作。
取消修改不改变已应用战场或磁盘文件；应用成功后，载入的规则成为满兵重开的基线。

- 格式 schemaVersion = 1；只保存战场 ID/内容版本、地形 ID/版本、世界尺寸/边界留白、兵种模板 ID/修订、部署数值与规则。
  位置保留 X/Y/Z，不在存档中固化平面高度；没有 Unity 对象引用、InstanceID、GPU 运行状态或战斗进度。
- BattlefieldCatalog.templates 把稳定编号映射到只读兵种资产；模板重命名或列表重排不改变引用。
  不兼容的模板修改需递增 revision，战场/地形契约变更需递增对应版本；复制模板应分配新编号。
- 使用框架 DataContractJsonSerializer，所有数据字段必填；文件上限 2 MiB，路径槽位受限并拒绝 Windows 设备名。
  截断、缺字段、非法数据、模板缺失/修订不匹配、战场/地形版本或空间契约变化均拒绝，不静默换模板、改地形或挪动部署。
  跨战场文件要求先进入其所属战场；本阶段不自动跳转场景。内置解析器忽略首个完整 JSON 对象后的附加内容，暂列非阻塞格式收紧项。
- 写入同目录临时文件并刷盘，随后 Move/Replace；未确认不覆盖，替换失败保留旧文件并清理临时文件，不回退为先删旧文件。
  载入失败保留原草稿、撤销历史、规则及现有 GPU 分配；坏文件仍出现在列表中，选择载入会给出原因。

开发烟测参数 --war-sandbox-plan-seed / --war-sandbox-plan-reload 配合 --war-sandbox-plan-directory <独立测试目录>，
分别在两个进程内写入/应用 A/B 与重启后载入 B 开战，避免覆盖玩家默认目录；输出由原 --war-sandbox-smoke-output 指定。
2026-09-08 验证：EditMode 241/241、GPU PlayMode 46/46、Windows 构建及两进程烟测通过，证据在 Logs/LocalPlans。
M4.3 仍需双方案自然结算和完整重启循环；本次短时开战与程序化截图不替代完整对局或人工界面验收。

## 运行时 uGUI（2026-09-14）

玩家界面迁移至 uGUI：场景目录、加载/失败/确认、指挥与全军团结算、配兵布阵、方案库。
`WarSandboxUGUI` 维护各呈现器拥有的 Canvas、Button、InputField、ScrollRect 和输入事件；切页复用节点，
低频更新数值，不在每帧销毁/重建层级。不修改场景资产，中文字体沿用 Windows 系统字体回退。
战斗命令/存档/部署仍调用原 Controller、RuntimeDeployment 和 PlanStore，不改 GPU 数据布局与模板资产。

- 主操作用强调色，数据与说明分层；技术信息默认折叠，容量告警仍可见。
- 桌面布阵左右分栏，窄窗口切换编成/预览；列表和属性独立滚动。
- UI 射线拦截指挥点击和镜头输入；文本输入期间不触发战斗快捷键；普通按钮不消费 Enter/Space 导航。
- 目标标记暂保留原屏幕绘制；旧 IMGUI 面板方法只留迁移参考，不再作为运行时调用入口。
- 场景 Canvas 随拥有者释放；常驻入口与场景 HUD 共用现存 EventSystem，不生成持久模拟对象。

M4.3 开发构建验证增加 `--war-sandbox-plan-cycle`，与 `--war-sandbox-plan-seed` / `--war-sandbox-plan-reload`
分别组合运行两个进程，使用同一个独立测试存档目录。每个进程均载入 A/B、自然结算、满兵重开、换场并校验配置/GPU 释放。
按钮烟测通过真实 uGUI Button 回调触发，输入经 InputField 回调传递；不冒充人工鼠标/键盘视觉验收。

## M5.3 隔离性能验证

`Editor/WarSandboxVatRegressionBuilder.cs` 生成专用 Windows Development Player：临时复制菜单并注入
`Scripts/WarSandboxVatPerformance.cs`，不覆盖原场景、配置或构建清单。提供持久重烘 Male 的资产路径后，
通过 `--vat-performance` 显式启动原版/重烘 ABBA 采样；正常游戏构建不会自行运行该探针。

2026-09-23：110k 默认部署、固定相机、1280×720、VSync 关闭，暖机后分别采 Setup 和交战时窗；
原始帧时 CSV、分位数、硬件、截图和资产/GPU 释放检查保存在 `Logs/M53Regression/player/`。
重烘平均帧时约 +3.2%，按非阻塞差异记录。历史 100k 编辑器约 30 FPS 不是此构建的同条件基线。
命令及数值见 [M5.3 回归记录](../方案设计/M5.3重烘回归记录.md)，制作步骤见
[兵种模型制作管线](../方案设计/兵种模型制作管线.md)。M5.4 已于 2026-09-26 获用户人工验收，M5 关闭。

## M5.4 独立模型试验

`Editor/UnityChanTrialImporter.cs` 将可信的 UnityChan 输入整理成独立图集/网格/Prefab，
`Editor/WarSandboxModelTrialBuilder.cs` 复用现役 Baker/Binder，生成 `M54TrialPlayable/` 的独立兵种、
Scenario、Catalog、菜单和战场。`Game.Editor` 单向引用 `MassEngine.Editor`，不让编辑器依赖进入 Player。
源素材/许可在 `Assets/ModelTrialSource/`，原工程和默认 110k 内容保持不动。

`Scripts/WarSandboxModelTrialSmoke.cs` 只在 `--model-trial-smoke` 时启用：seed/reload 两个独立进程通过
真实 uGUI 回调及业务 API 检查配兵、非法部署拒绝、保存载入、自然结算、满兵重开与返回释放。
正常试玩不运行探针；2026-09-26 用户确认已运行并验收。2026-09-25 两进程通过，完整 EditMode 327/327、GPU PlayMode 55/55 全绿，后续代码复核为333/333与55/55。

本机试玩：`Builds/M54Trial/Start-Trial.cmd`；供人工解压试玩的完整包：
`Builds/M54Trial_20260926_review.zip`（含许可和 `开始验收.md`）。详细数据、简化与复现见
[M5.4 验收记录](../方案设计/M5.4试验模型验收记录.md)。人工保存的**编号用 `M54_Manual`，名称可中文**，不要覆盖旧方案。
构建器从 `Assets/方案设计/M5.4试玩验收清单.md` 复制清单并生成启动器；固定资产名称已存在时拒绝覆盖。
交付后复核及333/333、55/55回归见 [代码复核记录](../方案设计/M5.4代码复核记录.md)。

## M6.1 连续地表原型

独立场景 `M61TerrainPrototype/TerrainPrototype.unity` 展示48米高地、坡道、峡谷与禁行区。
菜单 `MassEngine/Terrain Prototype/Create M6.1 Scene` 只创建新目录；已有产物拒绝覆盖。
`Build Windows Viewer` 构建独立观察程序，不修改默认战场、目录或 Build Settings。
双击 `Builds/M61TerrainPrototype/Start-Terrain.cmd`，右键+WASD飞行，滚轮缩放。

`WarSandboxTerrainPrototype` 的预算探针须显式传 `--terrain-benchmark --terrain-output=<独立输出目录>`；
100k/110k指表面查询数量，不是完整战斗。CPU/GPU采样和网格对齐，实际单位导航/战斗在M6.2接通。
2026-09-26 M6.1已通过346/346 EditMode、57/57 GPU PlayMode与独立构建；最终预算证据在
`Logs/M61Validation/player-v3/`。隐藏窗口下显式离屏绘制并检查非空画面，首轮黑屏帧时已作废。
约定、命令与实测见 [M6.1记录](../方案设计/M6.1连续地表原型.md)。

## M6.2 山地可玩整合

独立入口 `M62TerrainPlayable/TerrainMenu.unity` 复用 M6.1 地表，配置三军各 128 人的近战/远程混编。
`MassEngine/Terrain Prototype/Create M6.2 Playable` 生成新资源；`Build M6.2 Windows Playable` 构建到
`Builds/M62TerrainPlayable/Start-TerrainBattle.cmd`。目录还保留原 11 万单位平面战场入口。

Manager 持有真实地形提供者并验证版本、世界原点/尺寸；单位贴地与导航、三维交战/弹道阻挡已接入。
`WarSandboxTerrainValidation` 在应用前校验完整脚印，错误部署不替换当前配置；不可达移动保留旧命令。
点击、指示标记、镜头和本地方案身份验证消费同一快照，地形缺失不能静默降级为平面。
`--terrain-smoke --terrain-smoke-output=<目录>` 启用独立程序的实际 GPU 爬坡与释放验证，正常试玩不运行探针。
实现与证据口径见 [M6.2记录](../方案设计/M6.2山地可玩整合.md)。

## M6.3 地形闭环

`Build M6.3 Windows Validation` 构建 `Builds/M63TerrainValidation/Start-TerrainBattle.cmd`，复用同一山地/平面目录。
`WarSandboxTerrainCycle` 仅在 `--terrain-cycle` 下运行，`--terrain-cycle-phase=seed|reload|performance` 分别验证
实际UI保存/载入、不同进程方案恢复和离屏性能。输出目录必须新建且为绝对路径，Seed方案目录亦须新建并与玩家存档隔离。
Seed/Reload检查不同进程、相同构建GUID与方案哈希，正常速度自然结算、满兵重开、再战、切换110k平面后回到默认384人山地。
正常启动不改变镜头或存档；显式性能模式才渲染至离屏纹理并等待GPU完成，数据不冒充桌面FPS。
本轮389/389 EditMode、81/81 GPU PlayMode全绿。完整证据、复现参数及人工试玩范围见 [M6.3记录](../方案设计/M6.3地形闭环验收.md)。

## M7.1 首发预设与轻量入口

`M71LaunchPresets/LaunchMenu.unity` 统一四种玩法，首次进入 512 人的开阔对冲准备战场。
目录另有 384 人山地绕行、512 人中央据点、768 人三方混编，以及两军各 50k 的标准规模选项。
各预设显示人数、规则和准备阶段操作说明；目录图片由真实准备画面生成。

每个预设拥有独立 Scenario/Unit/Spawn/Rules 与稳定模板 ID，复用既有模型和战斗配置；
布阵、保存、载入继续走现有运行时副本链路。原 110k 场景及 M6 山地入口保留。
`MassEngine/Launch Presets/Build M7.1 Windows` 输出 `Builds/M71LaunchPresets/Start-WarSandbox.cmd`。

`--terrain-cycle-phase=presets` 检查四个预设的方案往返、自然结算、满兵重开/再战和退出释放；
`presets-ui` 检查最终目录图片、说明及五个入口。两者均须显式 `--terrain-cycle` 和独立输出/方案目录。
本轮范围、证据与后续门槛见 [M7.1记录](../方案设计/M7.1首发预设与轻量入口.md)。
2026-09-27：396/396 EditMode、81/81 GPU PlayMode通过，四预设自然结算/方案往返和满兵重开通过。
最终包GUID `fffef718c60f40aa9d90deded77e1c2c`，720p/1080p五入口验证通过，64份场景/配置哈希一致；
据点墙体的紫色材质等非阻塞表现缺口留待后续表现工作，未将自动验证当成人工试玩或V1放行。

## M7.2 结算、反馈与设置

`MassEngine/Launch Presets/Build M7.2 Windows` 输出M7.2试玩包 `Builds/M72Playability/Start-WarSandbox.cmd`，继续使用上述场景和五个预设。

`WarSandboxBattleResult` 独立冻结全部实际参战军团的 ID、名称、初始人数和存活，损失由初始减存活得出；uGUI 从快照显示统计。
保留原攻守方字段兼容已有读取方。结束后可满兵重开、回到战前布阵或选择其他战场。
运行/暂停时可主动结束本局，使用 `Ended / ManualEnd`，胜者为 -1，不伪装成平局或歼灭胜利。

`WarSandboxBattleFeedback` 接收控制器事件，`WarSandboxAudio` 用一个跨场景 2D 音源播放命令、拒绝、开战、据点变化和结束提示。
五个短音效由本地波形生成；没有逐士兵音源，据点进度逐帧变化和暂停恢复不会重复播放开战提示。
底部和目录的“设置”提供音量、静音、试听与恢复默认；设置窗口暂停正在运行的战斗并阻止命令，关闭后恢复原有运行状态。
`WarSandboxSettingsStore` 保存到 `Application.persistentDataPath/WarSandboxSettings.json`，默认 65% 音量、未静音。
显式“保存并返回”使用临时文件原子替换；无效文件回退默认值并显示错误，读取不覆盖原文件。

410/410 EditMode、82/82 GPU PlayMode 通过。独立包验证与边界见 [M7.2记录](../方案设计/M7.2结算反馈与设置.md)。
开发探针新增 `playability-seed` / `playability-reload`，使用独立方案目录和 `--war-sandbox-settings-file=` 隔离音量文件，正常启动不执行。

## M7.3 参考机基线与持续切换

`MassEngine/Launch Presets/Build M7.3 Windows` 输出 `Builds/M73Stability/Start-WarSandbox.cmd`，正常试玩内容不变。
显式 `--terrain-cycle-phase=release-performance` 对100k平面/384人山地做ABBA采样；`stability` 执行五预设×四轮的布阵应用、重开与切场。
均需 `--terrain-cycle`、新的绝对输出路径与未使用的隔离 `--war-sandbox-settings-file=`；不写玩家设置或执行方案存盘。
采样包含实际UI和GPU完成同步，不代表桌面FPS。进程内存使用Windows `GetProcessMemoryInfo`，无效/零值不能通过稳定性门槛。
416/416 EditMode、82/82 GPU PlayMode及最终同构建双进程通过，20次切场/40次重开未超既定资源容差。
四轮私有提交仍增加19.602MiB，默认档30FPS目标也未验收；方法、原始数据和后续范围见 [M7.3记录](../方案设计/M7.3参考机采样与持续切换.md)。

后续优化包为 `Builds/M73Budget/Start-WarSandbox.cmd`，构建菜单 `Build M7.3 Budget Windows`。动态地形流场回读当帧保存稀疏快照，按军团轮转每Tick最多求解一张；显式命令仍即时生效。
同口径山地交战p99从约64～65ms降至26.8ms，最高就绪排队约80ms；416/416 EditMode、85/85 GPU PlayMode、四预设自然结算/重开通过。
`stability --terrain-cycle-rounds=12` 完成60次布阵应用/切场、120次重开，资源计数不增，私有提交首尾+21.238MiB且未超原64MiB门槛；不声称严格内存平台期。
独立 `Build M7.3 Desktop Windows` / `Builds/M73Desktop/Measure-Desktop.cmd` 为普通窗口采样入口，显式开启FrameTiming、构建后恢复工程设置，使用正常相机/Present、完整uGUI和隔离输出，无逐帧强制回读。采样须前台可见，失焦会失败。
证据、延迟代价和复现见 [山地错峰与长时复测](../方案设计/M7.3山地错峰与长时复测.md)。

普通窗口ABBA已完成：100k交战13.97/13.74 FPS，GPU平均68.828/70.046ms，约30 FPS目标未达成；山地交战p99为19.708/19.938ms。
8窗采样、GPU有效值、前台状态、8张截图和64份内容哈希核验通过；下一项先拆分GPU绘制与计算成本，见 [普通窗口验证](../方案设计/M7.3普通窗口帧率验证.md)。

GPU拆分后已新增首发专用Male远景VAT：994→98顶点，原近/中景共享且逐像素相同。仅10个首发单位的渲染引用改变，历史场景及原配置保留。
当前入口`Builds/M73FarLod/Start-WarSandbox.cmd`，测量入口`Measure-Desktop.cmd`；新采样同时检查Unity焦点和Windows前台PID。
36组GPU外观、79项相关EditMode检查通过。旧desktop-1失焦失败及29.08 FPS窗口保留；同包desktop-2完整前台ABBA通过，两组100k交战31.07/30.59 FPS，p95 36.033/36.268ms，64份迁移后内容哈希不变。
指定参考机720p既定镜头/时段的平均约30 FPS目标已确认，不承诺每帧或全工况。见 [GPU拆分与远景预算](../方案设计/M7.3GPU拆分与远景预算.md)；下一项为 [人工试玩与V1交付清单](../方案设计/M7人工试玩与V1交付清单.md)，不代签M7/V1。

## 扩展这款游戏

- 新兵种：见 `../MassEngine/UnitTypes/README.md`（三步，零改引擎）
- 新指令类型/新阵营 UI：写在本层，通过 `MassEngineManager` 的公共 API
  （StartBattle/StopBattle/ResetScenario/SetFlowTargetOverride）驱动引擎
- 战争沙盒编辑器：M2 已获用户验收，规则/障碍归档与场景目录已经实现；
  M3.1～M3.3 未修改引擎与 GPU 数据契约，两套预设完整对局验证通过，人工视觉验收随M4于2026-09-14关闭

## 性能档位

历史默认产品档为 **50k vs 50k**（100k编辑器约30FPS）；现役 `WarSandbox.unity` 三军样例为 **110k**，
不能继续把旧数字当当前独立包帧率。10k vs 10k 是历史轻量档，100k/200k每方属于压力与容量展示。
M7.1的512人上手局是专用配方；M7.3远景优化后的100k标准入口已取得参考机720p平均约30 FPS的完整前台对照，范围和高分位帧时以上述最新记录为准。
历次口径见 [PerformanceBaseline.md](PerformanceBaseline.md)，本轮山地/平面分别见 [M6.3记录](../方案设计/M6.3地形闭环验收.md)。

## 小规模地形与连续命令验证（2026-09-29）

在用户认可真实拥堵等待观感后，新增显式 `--terrain-cycle --terrain-cycle-phase=commands` 开发探针；
须提供新的绝对 `--terrain-cycle-output=`、隔离 `--terrain-cycle-plans=` 和 `--war-sandbox-settings-file=`。
正常启动不执行，功能测试限制30FPS，只使用512人平地及384人山地，不作为性能或桌面输入验收。
命令层10/10、真实GPU地形/队列14/14通过；独立程序24条检查通过，包括暂停中追加/恢复、自动航点、换目标、Hold/Retreat、拒绝悬崖目标、重开与释放。
山地西/东两军各128人全部通过上坡、下坡检查线并接近对应目标，而不是只检查军团中心。
本轮只加探针/测试/记录，未改游戏移动、等待、动画、场景或配置；继续使用已获用户观感认可的 `Builds/M7CongestionPlaytest-20260929`。
验证构建、证据和限制见 [地形与连续命令验证](../方案设计/M7地形与连续命令验证_20260929.md)。

## UI 可读性与操作引导（2026-09-29）

当前UI试玩入口 `Builds/M7UiReadabilityPlaytest-20260929/Start-WarSandbox.cmd`，保留原拥堵等待版，不覆盖。
HUD固定显示已选军团、命令、目标和后续航点数；顶栏常驻开战/暂停/继续，小窗口收起面板后仍可操作。
选点提示同步受令军团，Esc/取消按钮只退出选点，不撤销已有路线；开局目标与操作提示更明确，帮助与技术信息互斥。
暂停中替换活动命令会继续运行，追加已有路线不会；没有已有路线时Shift也会建立新命令。这是既有控制器语义，未改玩法。
8/8 EditMode、10/10 uGUI PlayMode、独立程序49条检查及6/6打包工具测试通过；1280×720/640×480、512/384人场景、四张实际界面图已检查。
显式开发探针 `--terrain-cycle --terrain-cycle-phase=readability` 使用新的绝对输出/方案/设置路径，30FPS功能上限，正常启动不执行。
只是UI事件/射线和回调验证，不是完整OS输入、新手人工通关或FPS测量；移动/拥堵/GPU/资源配置未变。
细节与证据见 [UI可读性与操作引导](../方案设计/M7界面可读性与操作引导_20260929.md)。

## 远程有效发射专项候选（2026-09-30）

试玩入口 `Builds/M7RangedPlaytest-20260930/Start-WarSandbox.cmd`，旧UI/拥堵候选保留。
GPU空槽异步回收、有限配置容量预算、体型/距离净空高抛与真实射界预检；受阻自动进攻单位有限侧移，Hold/明确Move仍优先，既有拥堵等待/UI保留。
不修改默认射程/伤害/射速/阵形、动作资源或曳光视觉。预算数据池上限16MiB（另有元数据），不等于100k性能已验收。
预算Edit3项、74个不同GPU用例最终均有通过记录；独立Ranged02程序96/48人、23项检查、后排实际射手47/48与24/24、无容量暂停/拒绝，真实截图已核对。
开发探针 `--terrain-cycle --terrain-cycle-phase=ranged` 必须使用全新隔离输出/方案/设置路径，30FPS功能上限；仅创建合法运行时布阵副本，不注入弹体/伤害，不改变原场景资产。正常启动不运行探针。
详细目标、红绿实验、边界、GUID/包哈希及待人工项目见 [远程有效发射目标与进度](../方案设计/M7远程有效发射专项_目标与进度_20260930.md)。未代签100k性能、全体型或V1验收。


## 完整对局闭环与跨重启保存验证（2026-09-30）

在用户认可远程基本效果后，完成84/84定向EditMode和两组独立进程seed→reload：5场自然结算、6次方案应用、15次场景释放、10248真实渲染帧。山地两方案及四军团方案按模板/军团/人数/阵形/坐标、规则、战场/地形身份逐字段恢复；拒绝坏档/缺档/错误战场及未确认覆盖。设置恢复音量0.27/静音，模态暂停保持正确，重启静音阶段播放计数0；结算冻结、满兵重开、返回布阵通过。
只补开发探针/测试/文档，未改正式玩法；继续使用用户已认可的 `Builds/M7RangedPlaytest-20260930/Start-WarSandbox.cmd`，不需要为验证换包。
验证构建是另一GUID `2f0b634a26424e8aaa2f9c646897de63`（`Builds/M7MotionReview-20260930-Loop03`），不能声称本轮直接跑了旧试玩EXE。
显式 `--terrain-cycle-light` 仅用于 `seed/reload/playability-seed/playability-reload`：30FPS、最多896单位、只3张必要截图；山地用当前 `launch-mountain`，平地切换为512人，避免历史探针11万人分支。须提供各自隔离的绝对输出/方案/设置路径。`Logs/AgentLoopValidation/run-02/complete.json` 为四进程证据，最终保护审计见同目录上级 `final-source-audit.json`。
详见 [完整对局与跨重启目标/结果](../方案设计/M7完整对局闭环与跨重启保存_目标与进度_20260930.md)。这是功能验证，不代签真人听感/OS输入/100k性能/长时稳定性/V1；历史09-28性能数字不能移作当前远程版成绩。


## KayKit Knight 单角色试点（2026-09-30）

新增独立 `Builds/CharacterPilot-Knight-20260930-02/Start-Knight.cmd`；作者CC0模型/动作，64v64默认，Full6666/Low1337、114帧，独立Scenario+Catalog（模板revision2），不改正式阵容。两个新进程验证96v64保存重载、自然结算、满编重开与返回释放通过；12组48张真实GPU动作/LOD图通过。私有atlas色块减面避免串色，核心引擎未改。
01诊断包曾继承远程参数，最终交付为02近战包。保留现役网格溢出/搜敌上限提示，未做100k/人工观感/自然Far切换验收；30FPS只是功能限帧。详见[试点报告](../../Docs/CharacterPilot-20260930/README.md)。
