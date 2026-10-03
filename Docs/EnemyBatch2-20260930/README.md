# 敌方模型第二批：兽人、雪怪、蘑菇怪

> **历史专题 / 入口提示（2026-10-03）：** 本页保留该阶段原始记录；当前正式入口已由 Version08 取代，见[工程导航](../Engineering-20261003/README.md)与最新机器人交付记录。下文“当前”“下一步”及构建命令不作为现在的执行清单。旧手工测试仍暂停，其他模型转换未授权；技术结果与人工认可范围按原记录保留。


用户继续授权扩模型，不转玩法/混编专项，不回头打磨旧UI小项。本批新增3个身体/轮廓不同的怪物，并将上一批三种骷髅只读收录到新目录，**一个包可选6种敌方模型**。旧第一批包仍保留。

## 启动与选择

`E:\GitHub\_worktrees\WarSandboxBattlefieldRules\Builds\EnemyModelsBatch2-20260930-01\Start-Enemies.cmd`

GUID `fc7b8ee3a5d44fd1bedeea55c4e25100`。默认兽人场景；底部**战场目录**切换雪怪、蘑菇怪，以及上一批斧盾/双刃/持杖骷髅。目录需鼠标滚轮下翻查看后几项。

每场默认64展示角色对64骑士；新外观置于攻方，便于直接操作观察，不新增不可控制的敌方阵营规则。新3种全部复用近战；兽人用作者Weapon动作，雪怪/蘑菇怪用Punch，没有新技能、AI或战斗核心改动。

CMD设置在本包PilotData；手动方案`EnemyModelBatch2Plans`，与旧EnemyModelPlans、KnightPilotPlans、CharacterPipelinePlans、RangerPilotPlans隔离。新场景ID enemy-batch2-orc/yeti/mushroom，模板ID monster-orc/yeti/mushroom，revision1。旧3场保留原身份；本轮不自动迁移旧个人方案。

## 工程资产

Unity仍从 `MassEngine > Character pipeline > Open workflow` 选择：
- `Assets/Game/CharacterPipeline/Recipes/Orc.asset`
- `Assets/Game/CharacterPipeline/Recipes/Yeti.asset`
- `Assets/Game/CharacterPipeline/Recipes/MushroomKing.asset`

生成结果在Generated/Orc01、Yeti01、MushroomKing01。重做请换新outputName，不覆盖01。准备好的Prefab/自有Mesh/动作/材质在`Assets/Game/EnemyModelsBatch2/Prepared02`，其中Collection01聚合六场目录。

只新增MonsterBatchBuilder准备器，继续调用原CharacterPipeline；运行时复用现有EnemyBatchRuntime与ModelTrialSmoke。**没有修改已有C#、公共管线、战斗核心或旧场景资产。** 本批原件采用不同于KayKit的43骨骼结构，使用作者自己的兼容动画，不做Humanoid自动重定向。

## 不同作者素材的尺度整理

导入FBX有100倍静态层级（fileScale0.01），蘑菇怪腿部还带约万分之一量级的非均匀数值噪声。原严格流程拒绝直接输入是正确保护，不能靠放宽全局检查、丢掉真实形变或缩头去过关。

本批准备器仅在新资产内：
1. 保存原始世界静止姿态，转成单位缩放层级；变换自有网格顶点/法线，调整bindposes，保留拓扑、UV和权重。
2. 依作者原动作采样世界位置/旋转，生成新层级的局部曲线。只允许静态导入缩放及相对rest小于0.02%的数值噪声；真实缩放动画仍不接受。
3. 按24FPS所有拟烘焙帧，把**导入原件的独立LBS位置**和准备后模型逐顶点核对，准备层门禁0.25mm；然后照常运行原VAT全帧2.5mm门禁。

全3种均78帧，Idle24、Move14、Attack21、Death19。两个层次各865800顶点帧对照。准备后曲线的元采样率保留源30FPS，VAT实际24FPS；验证是对应采样点，不声称所有连续时刻无误差。

| 模型 | Full/Low | 原件→准备后最大差 | 准备后→VAT最大差 | 位置+法线纹理 |
|---|---:|---:|---:|---:|
| 兽人 | 4382/1525 | 0.0016mm | 1.584mm | 12.188MiB |
| 雪怪 | 3349/1581 | 0.0022mm | 1.886mm | 6.423MiB |
| 蘑菇怪 | 3369/1557 | 0.0115mm | 1.164mm | 6.447MiB |

Low预算1600、每模型纹理上限32MiB。最终144张GPU动作/LOD结果+48张独立位置参考；48参考图独立计算的是准备后模型位置，使用同shader/UV/法线。原件→准备后另有上述数值对照，不冒称原材质独立渲染或完整视觉签收。

尺寸：兽人2m/半径0.5m，雪怪2.4m/0.65m，蘑菇怪1.8m/0.5m。高度以蒙皮静止包围计（含原模型中已蒙皮持物），不等于骨骼身高。私有move max/referenceSpeed均3；雪怪近战距离2.2m，其余1.8m；只是本展示配置，不是大型生物全面适配或兵种配平。

采用原图集、32×32色块划分和导入材质0.8基色，沿用工程VAT光照，不复刻宣传图灯光；本批体型较圆润、色彩与KayKit不同，最终风格是否接受留给用户。未新增眼部发光/特殊材质。

## 实际运行

7个小用例串行：每个新模型seed/reload各一次，再在新目录跑一轮旧斧盾骷髅回归。真实uGUI卡片/模板选择、96v64方案保存/跨进程重载、自然结算、满编重开、返回释放、源JSON/方案/设置不变。没有注入结果或扣血。

| 场景/进程 | 自然战斗 | 存活[展示角色,骑士] | 通过 |
|---|---:|---|---|
| orc seed | 16.61s | [60, 0] | True |
| orc reload | 16.57s | [64, 0] | True |
| yeti seed | 18.11s | [58, 0] | True |
| yeti reload | 18.61s | [53, 0] | True |
| mushroom seed | 15.55s | [67, 0] | True |
| mushroom reload | 16.58s | [61, 0] | True |
| warrior seed | 12.11s | [80, 0] | True |

全部是近战测试，回执rangedEvidencePassed=false表示没有启用远程探针，不是失败。默认30FPS是功能限帧，不是性能测量；不做100k。

## 中途错误与保留记录

没有把失败记录改成成功：初次01为准备器曲线数组索引编译错误；01b误选Unity隐藏preview clip导致Single不唯一；01c在回滚自有未完成准备目录后，同进程复用路径被Unity“recently deleted GUID”保护拒绝。均无旧源文件变化，也尚无成功角色产物。

修正索引/过滤preview clip后，改用真正新路径Prepared02，保留严格“不覆盖”检查；最终01d成功。先前失败回执/日志和仅自有未完成目录清理清单保留。不要重跑历史Prepare/Resume入口，后续用现有配方新编号复现。

## 官方来源和许可

- [Quaternius Ultimate Monsters官方页](https://quaternius.com/packs/ultimatemonsters.html)明确该资源CC0，并直接链接其公开Google Drive目录。
- 正常匿名下载3个指定FBX、Atlas_Monsters.png、License.txt，另留Color Guide供图集检查。没取得整套50模型、没登录/付费/上传用户资源；没有访问旧RPG Characters受限下载或绕过配额。
- 逐文件Drive ID、SHA和字节数在download-receipts.json；正式导入5文件清单在selected-source-inventory.json。原始FBX不改写。
- **原官方目录License.txt抬头写“Ultimate Platformer Pack”**，原样保留，不擅改抬头。资源归属/许可同时以官方UltimateMonsters页面及其直接指向的目录佐证；不拿错标题文本单独冒充本资源证明。
- 模型/自带动画CC0不是整款游戏CC0。旧模型和引擎各自的notices随包保留，非EULA/法律/V1发行放行。

## 保护与剩余范围

旧Knight04、Ranger02、第一批EnemyModels01与更早包/主候选/五类旧方案/设置保护。最终审计见Logs/AgentEnemyBatch2/final-protection-audit.json；新例外仅本批新目录/准备器，更新三份交接文档。

新缩略图用宽幅留边避免拉伸，不修改旧UI；旧三张缩略图/旧Knight pilot页脚文案仍可能沿用。Far仍仅强制绘制，30/120阈值与镜头上限70不变。平地小场景，不是所有地形、任意骨架、完整键鼠或大型生物验收。未增加玩法、平衡专项、100k、提交或推送；本批三个怪物人工观感待你确认。
