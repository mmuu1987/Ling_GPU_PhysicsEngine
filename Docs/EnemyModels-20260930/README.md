# 敌方模型扩充第一批 · 3种独立模型

> **历史专题 / 入口提示（2026-10-03）：** 本页保留该阶段原始记录；当前正式入口已由 Version08 取代，见[工程导航](../Engineering-20261003/README.md)与最新机器人交付记录。下文“当前”“下一步”及构建命令不作为现在的执行清单。旧手工测试仍暂停，其他模型转换未授权；技术结果与人工认可范围按原记录保留。


2026-09-30。用户试玩Ranger02后表示“挺满意”，随后明确要求继续堆模型，优先敌方；本轮不另开混编、平衡或玩法逻辑专项。

## 新增什么

1. **斧盾骷髅**：独立Skeleton_Warrior身体、头盔/披风、斧与盾，复用现有近战逻辑。
2. **双刃骷髅**：独立Skeleton_Rogue身体、兜帽/披风、双刃，使用作者双持攻击动作，战斗仍为现有近战。
3. **持杖骷髅**：独立Skeleton_Mage身体、帽子与骷髅头法杖，使用作者施法动作，**复用已有抛物线投射物，不新增火球、召唤或魔法规则**。

是三个不同FBX角色，不是同一模型三套配色。同系列共享图集、Medium骨架，画风匹配现有Knight/Ranger；不是跨作者重定向或大型生物验证。工程已有SazenGames Skeleton保持原样，本批是新资源。

## 一个包，三个可选场景

启动：`E:\GitHub\_worktrees\WarSandboxBattlefieldRules\Builds\EnemyModels-20260930-01\Start-Enemies.cmd`

EXE：EnemyModels.exe，GUID `267619ea33a54ae5a6fca43fd4467f3f`。
默认进入斧盾展示；底部点**战场目录**，可以切换双刃、持杖；持杖卡片在下方，鼠标滚轮下翻。每场默认64新敌方外观对64既有骑士对照；新外观放在攻方以便操作观察，“敌方”是素材定位，不硬编码不可控制阵营。

设置通过CMD隔离到本包PilotData，人工方案目录`EnemyModelPlans`。三场使用不同稳定battlefieldId：enemy-model-warrior/rogue/mage；templateId：skeleton-warrior/rogue/mage。保存方案不会自动跨场景迁移，更不读取旧骑士/游侠个人方案。自动验证用Logs独立目录。

## 在工程中复用

Unity：`MassEngine > Character pipeline > Open workflow`，选择：
- `Assets/Game/CharacterPipeline/Recipes/SkeletonWarrior.asset`
- `Assets/Game/CharacterPipeline/Recipes/SkeletonRogue.asset`
- `Assets/Game/CharacterPipeline/Recipes/SkeletonMage03.asset`

后续生成换新outputName，不能覆盖旧产物。正式产物位于Generated/SkeletonWarrior01、SkeletonRogue01、SkeletonMage03；单模型createTrial=false，本批Collection负责三个目录卡片与场景聚合。

`Assets/Game/EnemyModelsBatch`保留准备好的Prefab、4动作、私有材质/模板和Collection01。新增EnemyModelsBuilder准备器、EnemyBatchRuntime命令行验收选择器；**没有修改既有C#、公共管线或战斗核心**。Mage发射证据也复用已有只读RangerFiringProbe，仅验证开关启用，正常试玩不做GPU读回采样。

## 尺寸、附件和质量

身体目标高度斧盾1.9m、其他1.8m；独立碰撞半径0.5/0.45/0.45m。身体高度不含声明的刚性头饰/武器，头盔/帽子会使可见总高更高，不把它当重复缩放。

原头盔、帽子、兜帽/披风确认为原骨骼子节点，连同武器一起明确列入attachmentPaths，没有丢掉原附件。通用Idle_A/Running_A/Death_A；攻击为Melee_1H_Attack_Chop、Melee_Dualwield_Attack_Slice、Ranged_Magic_Shoot。仅使用既有Idle/Move/Attack/Death四槽。

持杖初始挂左手且握姿不对；保留Mage01，先换右手为Mage02，再按Idle手坐标把杖长轴朝上校准为Mage03。原模型、身体骨骼和战斗逻辑未改，也没有覆写Warrior/Rogue。部分相机角度中，杖头会被帽子/身体遮住；请旋转镜头检查，不把单一视角当缺件。沿用现有VAT材质，不包含作者宣传图眼部辉光/背景特效。

本批为骨骼/细杆外形给Low **2000顶点预算**，不强行套旧人形1400；每种纹理上限仍32MiB，24FPS采样。预算配置在配方中，不是为某个精确顶点数修改算法。

| 模型 | Full顶点 | Low顶点 | 总帧 | 最大Full位置误差 | 位置+法线纹理 |
|---|---:|---:|---:|---:|---:|
| 斧盾骷髅 | 8695 | 1942 | 92 | 1.833mm | 20.125MiB |
| 双刃骷髅 | 7109 | 1996 | 95 | 1.289mm | 14.844MiB |
| 持杖骷髅 | 6835 | 1960 | 89 | 1.700mm | 13.906MiB |

总共2083610个顶点帧位置对照，均在2.5mm门禁内。最终144张GPU动作/LOD图+48张独立位置参考图；每模型Idle/Move/Attack/Death×3LOD×4采样。参考共用shader/UV/法线，只独立计算位置，非原材质独立渲染。视图和Low取舍最终仍需人看。

## 小对局和保存重载

六个独立进程**串行**运行：每模型seed/reload各一次。使用真实uGUI回调选择对应目录卡片和模板；96v64方案保存/跨进程重载，自然结算，原样重开恢复满编，返回释放GPU和运行副本，原始Scenario/模板/方案字节/设置不变。没有注入结果或伤害。

| 模型/进程 | 自然战斗时长 | 存活[新模型,骑士] | 通过 |
|---|---:|---|---|
| 斧盾骷髅 seed | 12.57s | [75, 0] | True |
| 斧盾骷髅 reload | 12.61s | [74, 0] | True |
| 双刃骷髅 seed | 13.08s | [75, 0] | True |
| 双刃骷髅 reload | 13.09s | [74, 0] | True |
| 持杖骷髅 seed | 21.60s | [50, 0] | True |
| 持杖骷髅 reload | 22.06s | [43, 0] | True |

Mage另采样真实弹道池、来源索引、间接绘制和敌方HP；详见ranged-evidence.json。不是仅播放施法姿势。近似发射相位0.55，复用已有异步发射/抛物线，非逐帧骨骼点同步。输赢均保留实际结果，不为通过而改兵种强度。30FPS是限帧配置，非性能测量或100k结论。

## 官方资源与许可

- [KayKit Skeletons官方页](https://kaylousberg.itch.io/kaykit-skeletons)：Free1.1，CC0，官方匿名免费取得。没买Extra/SOURCE，不取得付费Golem/Necromancer。
- 免费包upload15363145，8177445字节，SHA256 `21fbad59ee6cc1d7bed12d0e425acab8ebe564b8620bbc1d017aedb29dd8a3d2`。
- 只选9份源：3角色、4附件、共享图集、License。selected-source-inventory.json逐文件哈希；原资源在Assets/CharacterPilotSource/KayKitSkeletons。
- General/Movement/Melee读取此前官方CC0 Animations1.1的Knight目录；Ranged读取Ranger目录，不重复导入这些大FBX，不改旧源。其[官方动画页](https://kaylousberg.itch.io/kaykit-character-animations)和既有回执/许可保留。
- 与旧角色/引擎依赖对应的 notices 随包保留。本批CC0不等于全游戏CC0，不是EULA/法律/V1发行放行。

## 保护与剩余边界

旧Knight04、用户满意的Ranger02、诊断历史和主候选不覆盖；四类旧个人方案及设置受基线保护。最终审计见Logs/AgentEnemyModels/final-protection-audit.json。只新增源/配方/私有资产/准备器/选择器，更新三份交接文档。

不新增玩法，不做混编专项、细致配平、100k、提交或推送。Far只强制绘制验证，现有阈值30/120与镜头上限70沿用。当前展示平地小场景；不是所有地形、任意骨架或完整键鼠人工验收。本批三模型真人观感待你确认。


非阻塞显示说明：目前目录缩略图沿用旧UI比例，可能有横向拉伸；页脚也有旧Knight pilot文案残留。实际三维模型和绑定不是旧骑士，也没有因此改身体比例。按工程初期原则留作后续文案/缩略图整理，不为这些小项再开重构。两近战验收记录的rangedEvidencePassed=false表示未启用远程探针，不是本模型失败。
