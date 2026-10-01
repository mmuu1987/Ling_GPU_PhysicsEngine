# 当前：兵种属性配置已实施并推送（2026-10-01）

- 完成者：Arena.ai Agent Mode（AI 智能体；底层模型由平台调度，未披露）。需求和决策来自用户，流程图和 D1–D3 界面稿经用户确认后才实施。
- 提交 `f21e4aa`（分支 feat/war-sandbox-battlefield-rules，已推送）。设计与实现文档：`Assets/方案设计/兵种属性配置_设计与实现_20261001.md`。
- 三层数值：本局（方案 statOverrides）> 全局（persistentDataPath/WarSandboxUnitOverrides.json，按 templateId）> 官方（资源只读），逐字段 clamp；没有覆盖文件时与旧版一致。
- 入口：布阵页"兵种属性"→ 兵种档案（D1）/ 推送到全局（D2）；目录页"兵种库"（D3）；战斗 HUD 显示"自定义数值"。
- 测试：EditMode WarSandboxUnitStatsTests 23/23；PlayMode 端到端通过；EditMode 全量 458/460、PlayMode 全量 151/158，失败的都是既有构建列表问题（用户决定不处理）。证据见 Logs/AgentUnitStats。
- 待办：人工手动验收（文档第 7 节）；修复构建列表后重跑 SceneEntryTests。

---
历史保留：

# 当前：第一套CC0派生骑兵（2026-09-30）

- 用户认可前批并授权骑兵；许可选择被跳过后再次“继续，开干”，按不增加同许可义务的保守默认选已有CC0骑士+官方CC0马，新制作骑乘姿态。0AD只读研究，不导入CC BY-SA，不购买。
- 正式Builds/UnifiedCavalry-20260930-02/Start-Roster.cmd，GUID `af554201e4844a8da1739eec95309906`；01上下文诊断保留。MountedKnight01配方/产物，Assets/Game/Cavalry/Prepared01/Integrated02新场加入统一库，旧17入口原样保留。
- 马高1.8m、骑手站立比例1.95m、组合3.02m；马背鞍位跟随、双腿IK坐姿(109–126°)、脚蹬、简易鞍具/缰绳，4动作组合烘焙。派生制作，不称原厂配套动画；整体倒地，不上下马/落马/冲锋/击飞。
- Full10872/Low2106/145帧@24FPS，纹理约36.25MiB≤48预算；1576440位置采样，重放约.000763mm、VAT1.087mm。约束是骨骼目标，不等于网格无穿插。48结果+16位置参考，共用shader/UV/法线。
- 私有骑乘场2v16默认，3v16保存测试；圆半径1.8、双方中心距2.85，不套巨兽5.85。旧大型菜单分开，手动仍UnifiedRosterPlans。
- 02场创建Unity退出0，外层脚本误要求完整准备marker而false，原回执留存；只读核对实际2模板/互相触及距离通过后构建，不重烘焙或凑标记。五串行运行（骑兵seed/reload，旧spider/regular/all）回执Logs/AgentCavalry，报告Docs/Cavalry-20260930。
- 无旧C#/引擎/管线修改，无100k/commit/push/法律V1，新骑兵人类观感待确认。

---
历史保留：

# 当前：非人形第二批扩展统一库（2026-09-30）

- 用户试玩UnifiedRoster01反馈“没啥问题”，授权继续2–3非人形、直接并入统一库，蜘蛛限时复查，不新玩法。
- 新三角龙Triceratops01、剑龙Stegosaurus01、复查通过蜘蛛LargeSpider04。官方CC0只取得两FBX+许可，蜘蛛旧原件只读复用；已有C#/公共管线/核心全不改。
- 蜘蛛一次只读Bake/LBS/Half定点检查，在最终坐标直接编码2.38856mm且最坏点等于数学Half预测。旧recipe把已居中/落地的ScalePivot再次清零/补回，导致额外量化；新sizeRootPath为空、ground=false、height实际值，不改尺寸/动作/门禁，旧失败保留。
- Full/Low2458/758、4196/1358、5318/1097，141/163/165帧@24FPS；1907996位置样本最大1.379/1.963/2.389mm。144结果+48位置参考；同shader/UV/法线，非原材质独立验收。
- 新入口Builds/UnifiedNonhuman2-20260930-01/Start-Roster.cmd GUID `fdb11ea9d9404c7795132978181e4a47`。常规12保留，大型5身体（6模板含骑士）=17身体；新3场可下拉切选，也保留旧所有入口。仍大型攻方/适配骑士、保守圆/中心距离、11m新私有网格，非任意互斗/精确碰撞。
- 手动继续UnifiedRosterPlans；旧正常场/模板身份与引用保留，无自动迁移/改写，自动Logs隔离。八串行用例：新3各seed/reload，旧regular/all回归，详细roster-evidence。
- 项目Assets/Game/NonhumanBatch2/Prepared01/Integrated；最终Recipes/Triceratops,Stegosaurus,LargeSpider04。报告Docs/NonhumanBatch2-20260930，回执Logs/AgentNonhumanBatch2。老用户文件/所有包保护，人类新3观感待确认，无100k/commit/push/法律V1放行。

---
历史保留：

# 当前：新旧角色统一接入（2026-09-30）

- 用户问原工程角色是否能用于对战并授权“那你搞一下”。本轮不下载模型/不转新玩法：复用原男/女/UnityChan，补齐Sazen骷髅，统一常规12种可选+大型2身体受限场，共14身体；PBR/Polyart/适配骑士不虚增数量。
- 入口`Builds/UnifiedRoster-20260930-01/Start-Roster.cmd` GUID `24b3d685ec344d6e94e9639b997bad9d`；Unity场景Assets/Game/UnifiedRoster/Version03/Scenes/Menu.unity。自由编成初始2种、真实菜单12；全角色示例12种同场，36v36默认。大型场仅狼/牛/适配骑士，大兽限攻方，非14任意混战。
- 新可选WarSandboxRosterPolicy；仅新场启用模板库、安全默认数/阵型、半径/网格/密度/人数限制。旧无policy保留Scenario-only与1000新增默认。改4游戏层文件(RuntimeDeployment,Draft,HUD,UGUI)，无战斗/空间/渲染内核/烘焙器改动。
- 新Sazen骷髅源6FBX私有复制，原件/meta不动；4动作/独立atlas与单位，Full11987/Low2499/112帧@24，源最大.01077mm、VAT1.400mm，48结果+16参考。绿眼颜色固化非Bloom。初两曲线复制版本未过门禁保留；实采变换后通过。场景接线后续用EnsureControls只补新场，不重做骨骼。
- 7小用例串行：自由列表、12类混合示例、大型限制各seed/reload，旧斧盾回归；roster-evidence逐一UI选角/安全默认/新增撤销取消/非法候选不换GPU/所有liveMesh绑定。通用报告旧movement/turn文字在本轮指菜单预检查，不宣称重测横移。
- 手动UnifiedRosterPlans，设置PilotData，旧方案不自动迁移。说明Docs/UnifiedRoster-20260930，回执Logs/AgentUnifiedRoster。UnityChanUCL署名/原文必须保留，原AssetStore模型非CC0，无法律/V1/100k/提交推送放行。旧包/源/七类方案保护，人工整合观感待确认。

---
历史交接保留：

# 当前：大型兽形第一批（2026-09-30）

- 用户从数量扩充转到大型角色，明确选择大型兽形。正式2种：巨狼LargeWolf02、重型公牛LargeBull02。约6m地面跨度，高3.04/3.41m，保守圆3.6/4m，不是旧人形放大。蜘蛛候选未过门禁，暂缓不入目录。
- 包`Builds/LargeBeasts-20260930-01/Start-Beasts.cmd` GUID `a587838cc5834bf5b6bec123fde0b8ec`；目录2新+6旧敌方共8。默认8大型对32骑士，保存测试12/32；LargeBeastPlans与旧目录隔离，设置包内PilotData。
- 多材质无UV素材转私有线性色板，按原顶点映射核对；最终尺寸/原点在Half编码前处理，不放宽2.5mm。狼3815/1186/164帧、公牛4736/1344/158帧@24FPS，源→准备后最大约.00657mm，VAT最大2.066mm。
- 新私有Simulation9m网格覆盖两半径、手动稀疏阵型、中心近战4.65/5.05m（双方对照配置），不改核心/全局默认。真实横移3s+重置，再自然战斗，观测停速/HP/软圈重叠；不承诺精确碰撞/零穿插/全地形脚底。原动作脚底最小Y约−4至−5cm，保留记录。
- 既有WarSandboxModelTrialSmoke仅人数参数+可选预检查钩子，默认64/96、钩子空；旧斧盾回归验证。新增LargeBeastBuilder、Probe、CaseRouter，不重做玩法。
- 早期Wolf01误差2.826mm拒绝，前移定尺寸后通过；Spider02/03攻击3.78mm仍拒绝。只读NativeBake/LBS约.00197mm一致，不能武断归因Half；失败回执和候选保留，别将Spider诊断配方当成成品。
- 明细Docs/LargeBeasts-20260930；回执Logs/AgentLargeBeasts。全部串行小用例，无100k/提交推送/法律V1，人工大型观感待确认。

---
历史交接保留：

# 当前：敌方模型第二批（2026-09-30）

- 用户继续授权扩模型；维持模型优先，不转混编/玩法专项。新兽人Orc、雪怪Yeti、蘑菇怪MushroomKing，官方Quaternius CC0指定文件，只下3FBX+atlas+license。
- 本包`Builds/EnemyModelsBatch2-20260930-01/Start-Enemies.cmd`，GUID `fc7b8ee3a5d44fd1bedeea55c4e25100`，目录6种（新3+旧3骷髅只读引用），向下滚动选后几项。新手动EnemyModelBatch2Plans，不迁移旧方案。
- 不同作者43骨骼，自带Idle/Run/Weapon或Punch/Death；非Humanoid重定向。100倍静态层级通过新准备器消解到顶点/bindposes并按源姿态采样，真实scale动画仍不支持。原件→准备后865800样本最大约0.01154mm≤0.25；准备后→VAT最大1.887mm≤2.5。
- Full/Low Orc4382/1525、Yeti3349/1581、Mushroom3369/1557；各78帧@24FPS，Low1600、每模型32MiB预算。144GPU结果+48位置参考，共用shader/UV/法线，不冒称原材质独立验收。新图集32×32，不套旧4×4。
- 根路径Assets/Game/EnemyModelsBatch2/Prepared02；最终Recipes/Orc,Yeti,MushroomKing；Generated各01。前期01编译索引、01b隐藏preview clip重复、01c同进程recently-deleted GUID路径拒绝均保留；只清自有未完成准备目录，最终01d新路径成功。严禁覆盖/重跑历史准备器。
- 新近战3种两进程保存/重载+旧斧盾1回归，七小用例串行；报告Docs/EnemyBatch2-20260930，回执Logs/AgentEnemyBatch2。现有C#/管线/核心/旧场景不改。无100k/commit/push/法律/V1放行，本批人工观感待用户。
- 官方目录License标题为Ultimate Platformer，原样留存；官方UltimateMonsters CC0页面与直链目录佐证资源归属，不单拿错标题当证明。旧包/五类旧方案/设置保护。

---
历史交接保留：

# 当前：敌方模型扩充第一批（2026-09-30）

- 用户试玩Ranger02“挺满意”。用户随后明确要求继续堆模型，认为现有玩法足够；选择先扩敌方。后续不要未经要求切回混编/玩法建议，优先扩模型。
- 本批3独立身体：斧盾SkeletonWarrior01、双刃SkeletonRogue01、持杖SkeletonMage03。同风格官方CC0 Free1.1，9份选定新源；不买付费、不碰旧SazenGames。已有动画FBX只读复用。
- 一个包`Builds/EnemyModels-20260930-01/Start-Enemies.cmd`，GUID `267619ea33a54ae5a6fca43fd4467f3f`；底部战场目录换3场，各64新模型对64骑士。人工EnemyModelPlans，设置包内PilotData；自动Logs隔离，不迁移旧方案。
- 无旧C#、管线或战斗核心改动，只新增准备器/验收路由器和私有资产。法杖01/02握姿检查历史留存，03右手+Idle长轴校准；不能覆盖已有生成目录。
- Full/Low：8695/1942、7109/1996、6835/1960；24FPS，92/95/89帧。总2083610位置样本≤2.5mm，3×48GPU结果+3×16位置参考。Low2000/每种32MiB预算。
- 每模型两进程保存/重载/自然结算/重开/返回，持杖只读实测现有弹道/HP；具体回执Logs/AgentEnemyModels，报告Docs/EnemyModels-20260930。待用户三模型观感，不宣称物理键鼠/性能/平衡/法律/V1验收。
- 非阻塞旧UI残留：目录预览可能横向拉伸、旧Knight pilot页脚仍在；3D比例/绑定正确。持杖卡片在第二行需滚轮下翻，源码已确认catalog-scroll。留待下一次例行文案/缩略图整理，不重做已通过的玩法UI。
- 不100k、不新增技能、不提交推送。既有Knight04/Ranger02/旧包/个人存档受保护。

---
以下历史保留：

# 当前交接：第二角色Ranger02持弓试点（2026-09-30）

- 用户已实际打开04测试并反馈“完全ok”，授权下一专项；选远程人形复用现有抛物线系统，非核心重做/平衡/100k。
- KayKit Ranger Free2.0+Ranged Free1.1官方CC0，8份新源，只读复用既有General/Movement。相同作者/Medium骨架，不冒称跨作者泛化。
- 旧管线拒绝弓Blendshape，新增显式白名单的零骨骼/单形变/单100%帧附件参考与绑定验证，默认其他类型仍拒绝；只扩展CharacterRecipe/Geometry/Pipeline三份Editor源码，不改战斗核心。0/50/100原生位置核对及旧骑士04全759924样本回归通过。
- 01近景发现弓反向拉伸和箭轴错误，保留诊断；02按手坐标校准弓长轴162.87°、峰值76.41%、箭尾锚点/轴向。弦中心至slot约6.2cm，仅近似动作/发射，不是零残差骨骼发射。
- 非阻塞模板文案：实际玩家截图仍有旧“Knight pilot”页脚，内部EXE也沿用KnightPilot.exe；入口卡/绑定/数据均为Ranger02。按工程约定留到下次例行文案整理，不为此再构建一轮。
- 最终入口 `Builds/RangerPilot-20260930-02/Start-Ranger.cmd`，GUID `0a3ff2fb12ec4d1c852b276663e8cc52`；recipe Ranger02.asset，output Generated/Ranger02。Full9521/Low1293/142帧@24FPS，31.0625MiB纹理≤32；1351982位置样本最大约1.283mm。
- battlefield ranger-pilot / template kaykit-ranger revision1；手动RangerPilotPlans，自动Logs隔离。10m射程/搜敌、gravity−9.8、2.667s周期、相位0.53，私有慢速近战对照2m/s；真实对局对照方可赢，不调强角色凑结果。
- 运行/弹道/保护回执Logs/AgentRangerPilot，说明Docs/RangerPilot-20260930；48GPU图+16位置参考+4释放近景。旧包/04/主候选保护，人类Ranger观感仍待确认。无100k/commit/push/法律/V1放行。

---
以下历史交接保留：

# 当前交接：角色接入配方流程第一版与Knight04（2026-09-30）

- 用户授权固化制作流程。新增Editor/CharacterPipeline配方、前置检查、独立蒙皮门禁、Low策略、事务回滚、私有试点和薄窗口；复用生产Baker/Binder/GPU工具，旧核心和旧角色未重绑。
- 入口 `MassEngine > Character pipeline > Open workflow`，示例 `Select Knight example`；配方 `Assets/Game/CharacterPipeline/Recipes/Knight.asset`。支持已准备Generic/最多4权重/无Blendshape，显式附件及单个直接子级均匀尺寸根；不支持的情况拒绝，不声称自动重定向。
- 新Knight04：Full6666/Low1336、114帧、17.8125MiB纹理；759924位置样本最大约1.194mm，04与03 Full最大差0.03052mm；48GPU图+16源位置参考，后者同shader共用UV/法线，不冒称独立原材质验证。
- 首次测试写死Low1337而失败，属于测试前提错误；实际1336仍符合1400预算，因子0.99999994的聚类边界差。未改算法凑数/未覆盖04；失败日志保留，只读补验通过。故意失败回滚与100mm坏位置拒绝通过。
- 包 `Builds/CharacterPipeline-Knight-20260930-04/Start-Knight.cmd`，GUID `3d6f7918074743de81660dead2658cb2`；battlefield character-pipeline-knight / template pipeline-knight revision1。手动新方案目录CharacterPipelinePlans，不迁移03存档；运行/保护以Logs/AgentCharacterPipeline回执为准。
- 03用户反馈“目前来看还行”为初步观感认可；04窗口实际点击/外观待人验收。无第二角色、100k、commit/push、法律/V1放行。共用LOD与既有搜索/网格提示不改。后续制作选新输出名，不重跑已有04或旧一次性Prepare。
- 文档 `Docs/CharacterPipeline-20260930/README.md`；下一步先看新入口/04结果，再决定第二种不同角色。

---
以下历史记录保留：

# 当前交接：骑士头颈缩放缺陷已修复到独立03（2026-09-30）

- 用户指出 LOD0 脖子断开，之前48图自动检查漏掉了这个视觉缺陷。已实测定位：准备层级缩放和BakeMesh后的变换重复缩小蒙皮部件；不是源模型缺面或Low减面。
- 私有单位缩放烘焙→整个Full统一归一化→保色块Low；未下移头或补几何。114帧×6666顶点独立蒙皮对照，最大误差约1.194mm；预算Full6666/Low1337保持，48张新GPU采样另存。
- 当前试点入口 `Builds/CharacterPilot-Knight-20260930-03/Start-Knight.cmd`，GUID `37bb596ded554f60b4ddd8d0a77331d8`，revision3。01/02保留为历史，旧外观完整性判断由本次纠正。
- 细节 `Docs/KnightNeck-20260930/README.md`、同机位图 `修复结果.html`；最终运行/保护结果见 `Logs/AgentKnightNeck` 回执。人工观感未签收，无100k/commit/push/法律/V1放行。
- 旧主包、近战/共有LOD/镜头与个人数据不改；搜索范围和网格提示不扩展为专项。不要重跑有新路径保护的Prepare/Build覆盖现有03。

---
以下是02及此前历史记录（02存在已确认的头颈缺陷）：

# 当前新增交接：KayKit骑士独立试点02已完成自动闭环，待人工观感（2026-09-30）

- 用户授权“一名新角色完整接入试点”。作者官方Free2.0模型/Free1.1动作，CC0；只选Knight及剑盾/中型四动作，不上传第三方/不支付。
- 最终入口 `Builds/CharacterPilot-Knight-20260930-02/Start-Knight.cmd`，GUID `e4214860d91f447d886e80be0d42c0b1`。64v64默认；模板`kaykit-knight-v1` revision2，独立Scenario+Catalog，不替换正式阵容。
- Full6666/Low1337/114帧；私有atlas色块LOD修正，真实GPU12组48图通过。初始01误继承远程参数已保留诊断；02两个私有Combat均为近战，独立进程PID 19544/788，96v64自然结算15.66/16.11s，满编重开和返回释放通过。
- 仍有非阻塞现役参数提示：交战网格溢出标记、搜敌25m受shader约12m轴向上限；不扩大为引擎专项。小场景缩放上限70，Far仅强制GPU路径验证，真人近中LOD/体型/挂件/操作待验收。
- 三个新脚本（两个Editor配方+一个独立场景限帧/存档隔离组件），未改旧核心/旧配置；旧源和包保护以`Logs/AgentCharacterPilot/final-protection-audit.json`为准。无100k、无提交/推送、无V1/许可代签。
- 完整说明 `Docs/CharacterPilot-20260930/README.md`；旧主候选仍M7RangedPlaytest GUID01c3512de1eb461c8923cbe7054ea84a，29.43/29.70FPS结论不变。

---
此前许可/发行与正式候选交接保留：

# 当前交接：来源与发行许可已深入复核，剩余为发行整合/终端条款（2026-09-30）

- 用户明确要求确认许可，并选择两组资源为本人/团队官方取得。已查Dungeon Mason 225148 / SazenGames 306857官方产品页与Standard Asset Store EULA；两页标Extension Asset，实际团队席位要符合条件，不要求私人账号/订单。
- DOS字体找到作者Zeh官网2012-12-15 comment-7896明确“free license.. use it as you please”，已归档；不再仅凭下载站标签，亦不冒充OFL/MIT。
- 官方6000.3.14f1 Player/Windows/Mono 35页TPN已补。WinPix 1.0.240308001 DLL与微软NuGet完全同字节，是MIT版本。D3D12两DLL与1.618.1 NuGet完全同字节；微软LICENSE.txt适用bin，MIT LICENSE-CODE只适用头文件，不能照Unity汇总表直接写DLL=MIT。
- FBX分发与§1.1.5声明要求已明确，当前有Managed绑定/辅助程序集但未见libfbxsdk原生库；不写整个SDK全部入包/也不据此抹掉匹配包声明。
- 新发现玩家包libonigwrap.dll来自collab-proxy2.12.4，缓存同字节，Win64导入开启；排除10664B PE签名表及校验和/证书目录后其余540672B与Onigwrap1.0.10同字节。已补该版本/commit的MIT与Oniguruma等第三方通知。旧15包22声明存在，但不是完整运行许可账本。
- 结果入口`Docs/V1Candidate-20260930/许可复核结论_20260930.md`，LicenseEvidence保存原文；旧包与ZIP没有改变。剩余：实际开发席位/Unity许可资格、最终声明随包、正式主体与Unity/Microsoft终端保护条款/接受方式、FBX声明位置；再收真人记录并由用户签收V1。
- 本轮只有文档/只读核对，无游戏/Unity/重构建/压测/删DLL/提交/推送。证据`Logs/AgentLicenseCheck`；保护以final-protection-audit.json为准。旧包仍RangedPlaytest GUID01c3512de1eb461c8923cbe7054ea84a，性能29.43/29.70FPS两窗<30的结论不变。

---
以下为此前交接（许可待办状态已由上述复核推进）：

# 当前交接：V1补充资料已准备，待人工记录与发行许可确认（2026-09-30）

- 当前入口不变：`Builds/M7RangedPlaytest-20260930/Start-WarSandbox.cmd`，GUID `01c3512de1eb461c8923cbe7054ea84a`。没有新构建或新游戏运行，不覆盖旧包。
- 新资料：`Docs/V1Candidate-20260930/README.md`，共七份；涵盖操作/保存/日志、版本性能边界、资源许可登记、版权声明候选、空白人工记录与放行表。旧包“100k未测”等是历史状态，补充说明当前29.43/29.70FPS，两窗均<30；不冒称稳定30。
- 新核对：339包文件/338清单条目、309源运行副本、boot GUID、原ZIP83,355,961字节/SHA一致；2524构建资源记录、142 Managed DLL、15包版本、22声明哈希对应。`Logs/AgentDeliveryPrep/content-audit.json` passed。不是新压力测试。
- 目录实际340文件=339出厂文件+1既有Feedback日志。日志按基线保留，初次目录差异记录留档，不为清单通过删除用户反馈。
- 具体许可待办R-01/R-02：RPG Tiny Hero Duo派生美术、SazenGames两辅助脚本的取得主体/许可；R-03：构建报告中的PerfectDOSVGA437字体权威依据（Inter声明不可替代）；R-04：随包FBX DLL的适用条款及§1.1.5要求声明位置；R-05：Unity运行时/Mono/D3D12/WinPix适用发行依据/通知。文件清单通过≠许可放行；不搜个人付款/订单。
- 已抽取FBX原文声明，但未修改游戏About/EULA，不声称已满足放置义务。保留全部旧22份声明，不擅自删除DLL、换字体或重打包。
- 下一步：至少一位非开发者填写剩余首次体验/地形实际键鼠/结算/听感/远景记录；用户/负责人补最小来源与许可确认，再决定V1签收。不重开已经接受的拥堵/UI/远程专项，不自动优化补0.3–0.6FPS。
- 保护结果以`Logs/AgentDeliveryPrep/final-protection-audit.json`为准；基线1377源/文本、2046旧包/ZIP、3个人文件。原代码/资源/设置、旧候选、个人数据必须不变。本轮只文档与核对证据，无commit/push，V1未签收。
- 工作树仍为`E:/GitHub/_worktrees/WarSandboxBattlefieldRules`，不要在主工程写入；连接已恢复，不重复诊断旧端点。目标`Assets/方案设计/M7V1交付准备_目标与进度_20260930.md`。

---
此前交接：

# 当前交接：用户手动性能采样已复核，当前100k为29.43/29.70FPS（2026-09-30）

用户反馈“测完了”，已只读读取并独立复算 `Logs/AgentPerformanceCheck/manual-20260930-005510`，本次没有启动新Player/压测。实际用户包GUID `01c3512de1eb461c8923cbe7054ea84a`，PID20012、exit0、约184.72秒，8窗/4次释放完整，33418采样帧全部满足双焦点与实际渲染，CSV SHA匹配，8张720p截图存在且两张100k交战图已查看。

- 100k交战 **29.43/29.70FPS**，均值33.977/33.674ms，p95 38.279/37.533ms、p99 39.061/38.081ms；GPU29.863/29.552ms、100%有效样本。正常Present、原镜头、完整HUD、1倍速，低优先级2worker；100k指初始人数、允许正常伤亡。接近约30，但不能写成两窗均≥30达标，更不是全工况保证。
- 历史FarLod31.07/30.59FPS，本次低5.28%/2.93%；记录镜头/分辨率/初始人数一致，版本与优先级/worker/背景条件不同，不能因果归给远程。山地平均1.284/1.298ms，p99约17.9ms。
- 4个菜单回收点私有提交首尾+23.910MiB，native used+0.436MiB、managed used+0.109MiB、graphics estimate不增；混合场景预热后的既有容差无超出。ScenarioConfig首次山地访问1→2随后稳定，与探针源资产字典保留两类场景一致。短观察不证明长期无泄漏/平台期；报告retainedResourcesStable=false为未执行稳定性流程默认值，不是本轮内存失败。
- 当前包不换、运行源码/资源不改、没有提交/推送。此前两次前台失败记录保留；本次手动采样有效，前台条件不再是当前阻塞。不要再自动补跑。
- 复算证据 `manual-20260930-005510/independent-audit.json`；手动后保护审计 `Logs/AgentPerformanceCheck/manual-final-protection-audit.json`（以auditPassed为准）；完整目标/结果 `Assets/方案设计/M7当前版本短时性能与稳定性_目标与进度_20260930.md`。

## 下一步建议

按工程约定，个位数变化先记录，不为补0.3–0.6FPS深改引擎或降低画质/人数。可以进入V1资料准备（资源授权、依赖、操作说明、已知问题及剩余真人验收），但不代签V1或声称两窗≥30。若用户要求严格数值≥30，再另开有界性能专项。普通试玩继续 `Builds/M7RangedPlaytest-20260930/Start-WarSandbox.cmd`。

---
以下为此前记录（历史状态，不覆盖上述最新结论）：

# 当前交接：性能采样受前台条件阻塞，已停止（2026-09-30）

用户允许短测100k，并在第一次启动前台校验失败后明确允许再试一次、仍失败则停止。两次均已停止；不要自动开第三次测量或抢焦点/关闭用户浏览器。

- 实测对象是当前已认可的 `Builds/M7RangedPlaytest-20260930/WarSandbox.exe`，GUID `01c3512de1eb461c8923cbe7054ea84a`；309个运行文件SHA核对通过，没有重编译或改包/玩法。
- 512/384人小规模预检通过24项，2829真实渲染帧、2次释放，30FPS功能限帧，不是性能结果。
- desktop-01在探针初始化前用窗口句柄判前台失败，未进入采样。经用户授权的desktop-02等待初始化并改为前台PID检查：游戏21500、前台4328（后续查询为Chrome），不满足条件，因此停止。激活API返回true不足以证明前台。原始失败都保留，没有杀Chrome或继续加压。
- **有效性能窗口0，当前100k FPS未知；不是已测0FPS，也不是游戏性能失败。ABBA和短时菜单内存对照尚未完成。** 不拿旧GUID31.07/30.59FPS顶替、不把小规模通过当性能放行，不代签V1。
- 全部自有Player已退出，末次WarSandbox进程数0；源码/个人3份文件/旧包保护最终审计见 `Logs/AgentPerformanceCheck/final-protection-audit.json`。
- 手动入口已准备、仅做PowerShell语法检查，未执行：`Logs/AgentPerformanceCheck/Run-Manual-Performance.cmd`。用户双击并保持控制台/游戏前台；每次全新隔离输出，300秒总时限、100k访场75秒，仍按2worker/低优先级和逐帧双焦点要求执行，无自动重试。执行结束后需独立复算CSV才有性能结论。

目标与详细记录：`Assets/方案设计/M7当前版本短时性能与稳定性_目标与进度_20260930.md`。证据：`Logs/AgentPerformanceCheck`。此前完整闭环84项/四进程/五场自然结算/15次释放仍通过，当前普通试玩入口不变。下一步优先解决可用前台测量窗口或由用户手动启动，不顺手优化引擎或扩展功能，不提交/推送。

---
此前交接：

# 当前交接：完整对局闭环与跨重启保存验证通过（2026-09-30）

用户本轮授权“那你做吧，长程任务来做，认真来做”。已完成完整对局/跨进程保存功能验证，没有发现必须修改正式玩法的阻塞问题。目标及完整结果：`Assets/方案设计/M7完整对局闭环与跨重启保存_目标与进度_20260930.md`。

- 84/84定向EditMode通过：方案40、布阵18、可玩性/设置14、控制器10、地形2。补1项设置原子替换失败保护测试。
- Loop03 GUID `2f0b634a26424e8aaa2f9c646897de63`，两组seed→reload、4个串行独立进程全部exit0；5场自然结算、6次方案应用、15次释放、10248真实渲染帧。368–896人、960×540、30FPS限帧、低优先级2worker，不是性能测量。
- 模板/军团/人数/阵形/坐标、规则、战场/地形身份逐字段恢复；坏档/缺档/错误战场和未确认覆盖拒绝，草稿/GPU不变。山地A/B、三/四军团与据点均自然结算；手动结束不虚构胜者；重开满兵、返回布阵896人，结果快照冻结。
- 设置模态暂停/恢复正确；音量.27/静音跨真实进程恢复，重启阶段播放0次且设置文件哈希不变。3张原生截图已查看。历史据点墙紫色仍为非阻塞，不在本轮改美术。
- 只改开发探针、测试和文档；修的是旧探针的历史场景ID/重负载分支与构建脚本前缀，不是游戏逻辑。未改GPU引擎/默认参数/场景/已认可UI。首发目录轻模式使用 `launch-mountain`，平地切到512人，未跑100k/110k。
- 原始失败（构建名前缀、旧场景ID）保留；最终证据 `Logs/AgentLoopValidation/run-02/complete.json`；最终源码/个人数据/旧包审计 `Logs/AgentLoopValidation/final-source-audit.json`（以passed为准），补丁/清单在同目录。

## 当前入口与后续

继续使用用户已认可的 `Builds/M7RangedPlaytest-20260930/Start-WarSandbox.cmd`，GUID `01c3512de1eb461c8923cbe7054ea84a`；本轮无需替换试玩包。验证跑的是独立Loop03构建，不谎称直接复测旧包EXE。旧包与个人数据保留，人工交付清单已纠正过时入口，未代勾人工验收。

建议下一轮另行安排“当前版本短时性能/稳定性对照”，再整理V1授权/操作说明/已知项与剩余真人签收。这里不自动启动100k/长时压测，不代签V1，不提交/推送。远程基本效果、拥堵和UI既有用户认可保持原范围。

---
以下为此前交接：

> **2026-09-30 用户试玩反馈：原话“我测试了下，基本效果可以了，先这样吧，下一步该干嘛”。远程专项的当前基本效果获认可，停止继续打磨；不推断试玩规模、FPS、全体型/长时稳定性，也不扩张为V1整体签收。下一步建议转入完整对局闭环与跨重启保存查缺补漏，只补实际缺口；随后另排当前版本短时性能/稳定性对照。用户本次询问计划，尚未启动下一专项、100k测试或新构建。以下“等用户反馈”为此前交接状态。**

# 当前交接：远程有效发射专项候选已完成，等用户次日体验（2026-09-30）

- 用户睡前明确授权方案1后排越顶抛射、2有限侧移、4数据正常后才改视觉，按目标文档独立长程执行。目标 `Assets/方案设计/M7远程有效发射专项_目标与进度_20260930.md` 已持续记录红绿实验与边界。V1收尾仍延期。
- **新候选：`Builds/M7RangedPlaytest-20260930/Start-WarSandbox.cmd`**。源构建Ranged02，GUID **01c3512de1eb461c8923cbe7054ea84a**。旧UI/拥堵/运动包保留，旧UI包重新verify通过。
- 确认并修复CPU按最长寿命假满池：8人微型场景旧生成2/拒绝26/GPU在飞0/CPU占2，真实空槽回收后生成20/回收18/拒绝0。真正容量瓶颈另有同场景对照生成6→16；容量按数量×寿命/间隔预算，每单位最多8槽、全池262144（64B弹体数据16MiB上限，另有元数据），不无限扩大。紧凑回读仅用过的索引高水位；满池保留4096快照尾部；代次/租约水位不覆盖活弹。
- CPU/HLSL同式有界高弧，保留直射及真实友军/地形碰撞；缓存错峰射界预检，初次两侧候选选择稳定方向，约2秒/0.5～4米主动侧移预算，随后Idle等待并保留慢速接触修正。新命令/Hold/Move取消旧搜索。共享UAV尾部8→12word，原等待0～6偏移不变，无第9个UAV；Agent64B/UnitType144B/弹体64B不变。
- 不改默认射程、伤害、射速、阵形、动作或UI；看过两张真实图，已有曳光清楚，因此不做视觉增强。
- 验证：预算Edit3/3；GPU定向74个不同用例最终均有通过记录（首轮73/74，旧寿命fixture在地板上先碰撞，修前提后2/2复验）；11项拥堵、粗攻击同步/LOD/暂停、直射/地形/死亡/重开、安全回收、大请求尾部均覆盖。原生多排24/24生成，两后排均发射；受阻枪口侧移约0.705m后发射；20米紧邻高弧真实命中不误伤。
- 独立程序23项检查、464渲染帧、2次释放、errors空：96人平地约5秒551请求/551生成，观察后排47/48；48人地形279/279，后排24/24；实际损血2870/1460，容量暂停/直接拒绝全0，暂停/重开通过，源资产/方案/个人设置不变。30FPS功能模式，不是FPS测量。Ranged01只因探针宽深比反了被布阵规则拒绝；Ranged02只改探针，不放宽规则。
- 包339文件/309运行文件同字节、6项打包工具测试、新旧包verify；ZIP83,355,961字节，SHA256 **82296d789633f05831b5591e3a73ed7b1b9723a68ef55af4463ad8a6887df998**。实际运行源构建，包以逐文件同字节核验，不冒称又运行了一遍ZIP解压包。
- 证据 `Logs/AgentRangedSpecialist/`；包审计 `Logs/M7RangedPlaytest-20260930/package-audit.json`；最终源文件审计另见final-source-audit.json。未commit/push，无100k/长时soak/浏览器；串行低优先级2worker。
- **下一步只等用户体验反馈**：射程内后排出箭、受阻调整/停等、新命令是否顺畅、100k是否新增卡顿。预算/射界检查有成本，100k性能未测；射程外深排仍受原规则，任意巨型资产与动态遮挡/运动目标命中率未全面覆盖。不自动进入下一专项/V1，不假报人工接受。

---
以下为历史上下文，不代表当前停留阶段：

> **2026-09-30 长程授权：用户选择后排越顶抛射、受阻有限侧移、确认真实发射正常后再改善视觉，并授权按目标文档独立推进。用户睡觉，今晚不参与测试；按小规模串行低负载策略执行，不等同于放开100k压测或代签人工观感。目标/阶段进度见 Assets/方案设计/M7远程有效发射专项_目标与进度_20260930.md。V1收尾继续暂缓。**

> **2026-09-29 优先级改为第二个专项：远程攻击有效发射/弹道可见度与后排参与。用户明确“ 不急不急 ”，暂缓V1收尾；用户100000人实测观察到大量发射动作但可见弹道少，担忧前排停射挡住后排，并询问其他解决办法。当前只完成源码初查和方案讨论，未改玩法/配置、未启动测试/玩家/构建。源码确认：标准远程双方射程20；进入射程即可Attack，不预检射界；实际弹体遇友军会被拦截；弹道池CPU以最大寿命保守估算占用，池满丢新弹体。它们是排查依据，不是已测定根因；截图不足以判定比例。建议先对照发射请求/真实生成/实际在飞/友军或地形拦截/池溢出，再按结果处理后排抛射和有限侧向补位；保留已获认可的拥堵等待、现有动作资源及直射支持，不先用假弹幕或大规模运动重写掩盖问题。**

> **2026-09-29 用户确认：原话“ui没啥好说的，一遍过”。本轮 UI 可读性与操作引导专项人工通过，不再重复打磨或要求用户重验同一专项；不扩张为 M6/M7/V1 整体签收。下一步建议按既定 V1 交付清单查缺补漏，优先核对当前候选的布阵/方案/设置跨重启及结算闭环，只补缺口，不重跑已通过的大套件或100k压测。用户本次询问下一步，尚未启动新的验证/构建任务。**

# 当前交接：UI 可读性与操作引导候选已交付（2026-09-29）

- 用户在小规模移动/连续命令验收完成后明确说“辛苦了，继续下一步吧”，授权推进UI可读性与操作引导。本轮完成小步呈现/选点交互调整，不动已获认可的移动/拥堵等待算法。
- **看新UI请用 `Builds/M7UiReadabilityPlaytest-20260929/Start-WarSandbox.cmd`**；旧 `M7CongestionPlaytest-20260929` 完整保留，可对比。下面上一轮“无需换包”仅指当时纯命令验证，不适用于本轮新增UI。
- 改动：固定已选军团/当前命令/目标/后续航点卡，显式“已选”按钮文字，顶栏常驻开停按钮；持续选点提示随军团切换更新，取消只退出选点不清路线；开局目标独立呈现，帮助说明更完整且与技术信息互斥。
- 暂停语义未改：替换活动命令会继续运行；向已有移动路线追加不解除暂停；无已有路线时Shift仍建立新命令。界面明确提示，不伪装成新的暂停排队规则。
- **验证通过**：EditMode 8/8（5项新HUD用例+3项路点既有回归），uGUI PlayMode 10/10；独立程序49条检查、退出0、errors空；1280×720与640×480，512人平地+384人山地，4张离屏图已读取核对、2次切场释放。
- 按钮检查使用实际uGUI射线命中+pointerClick；小地图回调显式传Shift，Esc取消方法经定向测试覆盖。不是完整Windows物理输入或首次玩家五分钟体验验收，UI人工观感仍待用户反馈。
- 新源构建 `Builds/M7MotionReview-20260929-Ui01`，GUID **fb7603aeea77464c91bc6aa6bb6b29cd**；新包ZIP **83,307,856字节**，SHA256 **c94fd21201035c171855851d0a64be2a5ea5c8e442861db63e3b0fd5bba9daf1**。6/6打包工具测试、309运行文件逐项同构建、339总文件、清单/ZIP验证通过；不是另行启动了ZIP解压路径。
- 证据 `Logs/AgentUiReadability/`（edit-01.xml、ui-01.xml、build-receipt.json、player-01/report.json、player-audit-01.json、四张PNG、final-source-audit.json）；包审计 `Logs/M7UiReadabilityPlaytest-20260929/package-audit.json`；说明 `Assets/方案设计/M7界面可读性与操作引导_20260929.md`。
- 串行/低优先级/2worker；30FPS功能上限、隔离方案/声音设置，无浏览器/100k/全量GPU/新FPS或长时soak。没有改控制器、GPU内核、移动/等待、VAT、兵种、场景/配置；没有commit/push。旧包、受保护设置、Resources.meta和SVG边界保持。
- 下一步优先收用户对“选中谁/命令状态/航点与暂停/取消选点”的反馈。小窗口次要指令仍需滚动，建议1280×720起使用；不立即扩展引擎或全UI重构。聚团中心转身/动作跳变仍为非阻塞已知项；不以本轮代签M7/V1。

---

# 当前交接：小规模地形与连续命令已验证（2026-09-29）

- 用户认可拥堵等待观感后，授权下一步做山地/窄通道、移动中改目标、Hold/Retreat、追加航点和暂停恢复的小规模验证。此轮已完成，未复现需改玩法的阻塞问题。
- **继续使用 `Builds/M7CongestionPlaytest-20260929/Start-WarSandbox.cmd`；不用换包。** 该版用户主观认可，不是新的FPS或M7/V1整体签收。
- 新增仅显式 `commands` 模式运行的开发探针与3项EditMode用例。未修改玩法控制器、引擎移动/等待/GPU内核、动画资源、兵种配置或场景；本轮没有commit/push。
- 实测：命令层 **10/10**、真实GPU地形/队列 **14/14**；独立EXE **24条检查通过**、退出0、errors空。平地512人；山地384人中西/东各128人，全员上坡过线并接近目标，全员撤退下坡过线且中心回到出生目标10米内；中军Hold。
- 上坡阶段29.78秒达标；下坡全员约30.81秒过线、36.16秒同时满足中心到家范围。不是整场耗时或性能测量。没有只凭“军团中心到了”放过后排；两场景暂停位置差均0，计时器/HP不变；非法悬崖替换/追加保留原命令和队列。
- 新验证构建 `Builds/M7MotionReview-20260929-Commands01`，GUID **2277cd4d62ab4e788b8ec7e7a6c05dc2**；仅供验证，未打新试玩包。证据 `Logs/AgentCommandValidation/{edit-01.xml,gpu-01.xml,build-receipt.json,player-01/report.json,player-audit-01.json,final-source-audit.json}`。详细记录 `Assets/方案设计/M7地形与连续命令验证_20260929.md`。
- 测试串行、低优先级/2worker；玩家进程30FPS功能上限、两张离屏图、独立方案/设置路径。未跑100k、浏览器、全量套件、桌面FPS或新的长时稳定性测试。已通过的EditMode没有因外层脚本序列化兼容问题重跑；TimeManager原始字节已恢复。
- 局部剧烈转身/Idle-Move过渡继续视为用户认为不碍事的非阻塞表现项，原因未从截图确认；保留现有等待机制。未来巨型单位/任意地形/全命令组合不在这次证明范围。
- **主线建议：移动暂时收口，下一阶段先整理UI可读性与操作引导。** 本轮没有修改UI；不扩大成再一轮运动/弹道架构重写或重复压测。

---

> **2026-09-29 用户实测反馈：拥堵等待专项观感明显改善，用户另测100000人场景也认可效果、表示没有此前漂移感。此为本专项主观体验认可，不是新FPS测量或M7/V1整体签收。聚团中心仍有明显转动/闪转/动作跳变，用户认为不碍事，记为非阻塞表现项；先保留当前等待机制，不因此重开大范围改造。新候选仍为 Builds/M7CongestionPlaytest-20260929/Start-WarSandbox.cmd。**

# 任务交接：人工试玩候选包已准备并复核，待实际反馈

> 2026-09-28晚接力。其他模型已完成独立候选包、玩家说明/反馈表/依赖记录及新目录烟测，但未更新本交接。本轮发现并复核这些产物，补齐状态；没有重编、重打包、启动游戏或更改候选包。M7.3性能确认已完成，当前下一项是实际试玩反馈与资源取得主体确认，不重复优化或代签V1。

## 工作树与硬边界

- 实现 E:/GitHub/_worktrees/WarSandboxBattlefieldRules，feat/war-sandbox-battlefield-rules，HEAD d7f0a5c；原工程只更新NEXT_TASK索引。
- 大量M5～M7继承增量未提交/推送/开PR，不git add .，不推main；不动Assets/pelican-cycling.svg及.meta。不自动派代理。
- Logs、Builds、NEXT_TASK不提交。不执行Prepare/PrepareAndBuild/ImportPreviewsAndBuild或CreateVerifyAndBuild覆盖既有内容。
- ProjectSettings/EditorBuildSettings.asset是继承修改，不能顺手还原；新增Assets/Resources.meta也是本轮开始时即存在。本轮未改任何游戏代码、场景、素材或项目配置。
- 当前没有目标工程的Unity/游戏/打包进程；本轮唯一校验进程已退出0。

## 给实际试玩者的产物

- **Builds/M7HumanPlaytest-20260928.zip**，83,254,221字节（约79.4MiB）。旁边有.zip.sha256。
- SHA-256：**b2ba3f1c3d175ce231ac33031707c3fa72d5f9bacc439a0ef60cfb85cf37b262**。
- 已解压普通入口：**Builds/M7HumanPlaytest-20260928/Start-WarSandbox.cmd**。
- GUID **90c14fa91efa4ae7b3758eff6a9e572f**；309个运行文件与已测Builds/M73FarLod逐字节相同，仍是Windows 64位Development候选包，不是已签收V1。
- 普通启动不带探针参数，进入512人准备战场；每次在Feedback/新建独立Player日志。第一次普通启动前Feedback目录可以不存在，启动脚本会创建。
- 包含README、操作说明、空白试玩记录、版本/已知问题、资源与依赖说明、dependency-inventory.json、22份ThirdPartyNotices和package-manifest.json。
- 游戏方案/声音设置仍使用普通游戏的本机保存路径；测试时用新槽位，勿覆盖重要方案。实际玩家首次体验时间从其主动启动算起，不拿自动烟测用时代替。
- 原M73FarLod/预算/桌面/稳定性/M72/M71包及全部原始证据保留，不覆盖已交给试玩者的目录。

## 打包与包内烟测（继承成果）

- Tools/package_playtest.py采用明确的运行目录清单，拒绝覆盖输出；Docs/Playtest是随包文档和启动器模板。
- 包内339文件，manifest记录其余338文件的大小/SHA256；309为运行文件。原始制作素材、Measure-Desktop.cmd和顶层DoNotShip目录未复制。
- Logs/M7HumanPlaytest/package-smoke/report.json：PID21292，退出0、completed/passed=true、errors空、同GUID；五个预设进入/开战/重开/退出、5次释放通过，11张720p图。使用隔离方案/静音设置目录，不使用玩家默认目录。
- package-audit.json / final-audit.json保留打包和烟测证据，humanAcceptance=pending、v1Released=false。没有重跑性能、GPU战斗或长时soak。
- 依赖清单记录2521条Used Assets资源条目、15个软件包与22份声明原文；列出脚本不等于该脚本实际挂载于首发场景。

## 本轮独立复核

- `python Tools/package_playtest.py --verify` 通过338文件完整性校验。
- 新增 `Logs/M7HumanPlaytest/recheck_handoff.py` / `handoff-recheck.json`：核验309源运行文件、339个ZIP成员与目录逐字节一致、CRC/SHA256、无重复/额外归档成员、64份迁移后源内容哈希、11张烟测PNG尺寸、普通启动器无探针参数以及随包依赖声明。
- 回执passed=true、candidateModified=false、newGameRunOrBuildPerformed=false；人工与授权放行仍未确认。
- 当前候选目录的试玩记录仍空白，Feedback下没有文件。不能据此断言其他解压副本无人试玩；若用户已有记录，应先收集反馈。
- 这些静态清单检查适用于未修改候选产物。玩家回填试玩记录或生成Feedback日志是预期变化，不能为过校验恢复空白或删日志；复核原产物用保留ZIP，或仅比对运行文件。

## 当前真正待办

1. 至少一位非开发者正常启动候选包，填写包内试玩记录.md，记录首次用时/胜者胜因理解，以及山地指挥、方案往返、结算、听感、远景接受度。未尝试项如实填。
2. 收集记录、截图及对应Feedback日志；若反馈在别处，请用户提供，不自行搜索私人目录。只修实际阻断操作/理解的问题，不凭空再开优化任务。
3. 项目拥有者确认RPG Tiny Hero Duo及构建报告中SazenGames Skeleton辅助脚本的取得主体与适用许可记录；本地清单没有足以确认主体的凭证，依赖声明齐全不等于授权放行。不索取账号、付款等不必要敏感信息。
4. 人工通过、来源确认后再冻结V1候选、整理最终说明/已知问题/授权与依赖记录。若裁剪辅助程序集或生成非Development包，必须新GUID及必要烟测；不能沿用旧GUID冒称同构建。
5. M5用户签收不重做；M6/M7人工项和V1必须明确确认。紫色据点材质、侧栏遮挡、远景辨识度与更长内存趋势继续非阻塞记录；Mod搁置。

## 已完成性能与回归摘要（无需重复）

- M73FarLod desktop-2完整前台ABBA，PID20140退出0；27661采样帧均满足Unity/Windows双焦点检查，100k交战31.07/30.59 FPS，p95 36.033/36.268ms。在i7-7700/GTX1060 3GB、720p既定镜头/时段满足平均约30目标，不保证所有镜头/1080p/全时段。
- Male远景994→98顶点，近/中景GPU逐像素不变；36组外观、79项相关EditMode、四预设自然结算/方案往返/满兵重开通过，Far轮廓代价仍待人工接受。
- 山地动态求解已每Tick最多一张、军团轮转；416 EditMode/85 GPU通过，12轮60次切场120次重开资源计数不增，私有提交首尾+21.238MiB未超原门槛，更长趋势非阻塞。
- M73FarLod/final-audit.json明确完整前台对照与两次均值>=30通过，V1=false；desktop-1失焦失败和29.08 FPS窗口保留。

## 文档与命令

- 最新产物说明：Assets/方案设计/M7人工试玩包准备.md（本轮已补接力复核）。
- 人工标准：Assets/方案设计/M7人工试玩与V1交付清单.md；模板 Docs/Playtest/试玩记录.md。
- 新打包仅在确有更改时用 `python Tools/package_playtest.py --name <未使用的新名字>`；模板变更后生成新候选，不覆盖旧包。
- 游戏确需重建才用WarSandboxLaunchPresetsBuilder.BuildM73FarLodWindows。Unity D:/soft/Unity6/6000.3.14f1/Editor/Unity.exe，禁止-nographics，按项目日志判断锁。之前TypeDbJsonGenerator OOM可参考M73FarLod/create-verify-build.log的仅子进程GC限制；不改系统页文件或关用户程序。

---

## 2026-09-29 动作与弹道反馈修正（已实施，未打新包）

- 用户授权按长程任务修改；仍在本工作树 `feat/war-sandbox-battlefield-rules`，没有切分支、提交、推送或覆盖旧包。
- 已实现独立 Idle/Move 表现、实际位移／体型参考步频、远程前摇与粗同步、暂停续接、尺寸化发射／瞄准高度、统一现役远程弧线、沿途身体与地形最早碰撞。
- 默认友军挡弹不受伤，原目标死亡不立即销毁已生成的在飞弹体；首发与混编阻挡会影响战斗节奏，仍需真人确认。
- AgentData 56→64B，C#/Compute/两份VAT shader/手工外观捕获工具同步；UnitTypeGpuSettings仍144B、ProjectileGpuData仍64B。四段VAT素材/M73远景资源未重做。
- 保留旧的地形基础、异步回读回调与generation取消修复；未触碰旧试玩候选文件。
- 最终定向验证：PlayMode GPU **52/52**，EditMode **9/9**，零跳过。EditMode含当前M7近/远景十次128×128内存绘制，不是浏览器截图或重烘。
- 首轮37/39的失败记录保留；分别为Hold测试前提错误和实际暂停冷却重置，均已修正并严格复测。低帧率50ms安全步长也有新增回归。
- 完整变更、参数、限制与证据：`Assets/方案设计/M7动作与弹道反馈修正_20260929.md`。
- 证据：`Logs/AgentMotionProjectile/gpu-final.xml`、`edit-final.xml` 及同名日志；CAS原始备份在同目录backup-*。
- 未跑全套测试、十万人新性能基准、完整人工试玩；旧M73成绩不能当本版本性能证据。没有新构建，不把旧ZIP当作当前源码。
- 桶溢出时碰撞有精确全表回退，正确但昂贵；要结合溢出遥测确认正式规模，不能承诺全工况帧率。
- 下一步：当前Unity源码打开 `Assets/Game/M71LaunchPresets/LaunchMenu.unity`，先512人上手局／山地局人工看待机、慢移、出手与前排遮挡；观感确认后限量验证规模成本，再另名出候选包。
- 保留全部继承的未提交内容，不能整体add/reset/clean；所有测试进程本轮结束时应已退出。未经进一步要求不推送、不覆盖旧候选。


## 2026-09-29 接续：复审、新 EXE 与独立程序自测完成

- 用户授权 review、另建 EXE、自测；没有 commit/push，旧包不覆盖。报告：Assets/方案设计/M7复审与独立程序验证_20260929.md；维护步骤：Docs/ReviewedWindowsBuild.md。
- 修复已清空 GPU 计数后，4096 CPU 批次截断丢失尾部的问题：保留快照/游标，分次消费；新增请求留在 GPU 下次捕获；清空/重建废弃尾部。单次预算与池满计数丢弃策略不变，批量下释放延迟仍存在。
- 新安全 BuildReviewedWindows 入口拒绝覆盖；新增 motion-review 30FPS 四小预设模式，不跑100k、不改旧性能模式；打包器显式 source/GUID/log/current-notes，独立审计，6项快速回归。
- 实际 GPU 定向35/35（含2个新增队列测试），Windows Python6/6。前阶段52+9不与本轮重叠项相加。
- 最终 Build源：Builds/M7MotionReview-20260929-02，GUID789fd1c8abda46f7a9b79c7cdb8d980a；构建exit0，142个审计文件构建前后未变。
- 独立PID20260、exit0、completed/passed=true/errors空、9197离屏世界帧。512/384/512/768人自然结算约107/112/21/45秒，四次方案往返、满员重开、再次开战、回菜单和资源释放通过；隔离设置未写。非桌面/100k性能验收。
- 最终包：Builds/M7MotionPlaytest-20260929.zip，83,279,054字节；SHA256 ce57e5a49e1aef9b7be1ae933968cae61a779bea2a92279a82411d691be4456d；普通入口为同名目录/Start-WarSandbox.cmd。309运行文件与已测源构建相同，338清单条目/339包文件，CRC/SHA和复核通过。
- 首个EXE自测在自然首局后误报空白图，失败证据保留在Logs/AgentReviewBuild/player-01。第二版复现稀疏16色、全像素194色，改为先保存图、稀疏不足再全像素检查，原24色/5%洋红门槛不降。Build01不是最终候选。
- 所有原始证据：Logs/AgentReviewBuild；新包审计Logs/M7MotionPlaytest-20260929。构建GC参数只作用于自有子树，不改系统。所有本轮Unity/玩家/打包进程已结束。
- 据点仍有约2.57%抽样洋红像素；紫色材质、侧栏遮挡、远景可读性未修，优先收集人工体验。友军挡弹/前摇后的平衡待评估，不以自动胜负代签；不沿用旧31FPS成绩。


## 2026-09-29 最新接续：拥堵等待专项

- 用户要求只专精“拥堵后停几秒”，不再只调动画。报告Assets/方案设计/M7拥堵等待专项_20260929.md。
- 已实现约0.4秒净推进观察+近前方友军确认；1.1～2.0秒错峰等待，关闭主动推进/密度侧推，Idle，仅保留低速重叠修正。新命令按军团版本取消（含同点重发），暂停冻结，死亡/重开清理，攻击优先。
- AgentData仍64B；原engagementSlotAssignmentBuffer前N为slot，尾段每agent8words存拥堵记录，避免第9个DX11 UAV；增加32B/单位。新增每军团只读命令版本buffer。详见Core/Simulation README；不要用slot buffer.count当兵力数。
- 远景额外命令决策不重复推进近战/转向时间，保护零预算pow(0,0)。19/19定向GPU（11专项+8邻接），9/9契约/真实近远VAT；中间14/18不重复累加。
- 新源构建Builds/M7MotionReview-20260929-Congestion01，GUID319c4613a5a04e388f56c7342aa5d3c0。实际512人正常场景、真实Move/Hold命令，未注入等待或改兵种。PID4496，exit0/errors空；跟踪187号6次Idle采样，位移0.066m；14个等待者新命令后全部取消，4秒新方向平均推进11.667m；Hold/重开/释放通过。
- player报告maximumWaiting=2只覆盖首个样本寻找阶段，不是整场峰值；转向前等待者14。测试仅本次拥堵移动，不冒用旧四局自然结算或FPS结果。
- 包Builds/M7CongestionPlaytest-20260929.zip，83,291,015字节；SHA256 b6ebdfbb96810d002deddd1203179577a8fe00f209e491062f8013ae016ac189；309运行文件与已测源构建相同，339包文件，清单/CRC/SHA完成。普通入口同名目录Start-WarSandbox.cmd。旧MotionPlaytest-20260929和9月28日包保留。
- 证据Logs/AgentCongestion，新包审计Logs/M7CongestionPlaytest-20260929；全部自有Unity/玩家/打包任务已结束，无commit/push。未改弹道、场景/兵种素材，未跑十万人或浏览器。
- 下一步让用户同样操作复测：排队停等、通道放行、重新点目标。不要拿自动通过代替观感，也不宣称解决所有密度/地形/大体型；维持此专项范围。


## 2026-09-29 用户回测：拥堵等待方向得到认可

- 用户原话要点：“感官上好很多了”；另开100000人局“效果也是可以的，没有那种漂移感了，感觉顺眼了很多”。本专项可记为用户主观认可，不再描述成用户尚未测试。
- 截图为512人开阔局，攻方256/256、守方256/256，移动命令，1x，约00:57；用户圈出聚团中央少数单位有剧烈转动、较大闪转或动作切换。静态截图不能判定具体动态根因，不把其猜测直接当成已定位的Idle/Move缺陷。
- 用户明确认为“不碍事”。优先级：非阻塞表现项，保留当前停等/错峰/命令取消行为，不继续无目的调参或重写寻路。
- 若后续单独处理，先用短视频/小规模复现区分朝向反复修正与Idle/Move快速切换，再考虑朝向迟滞/转速限制或表现状态最短驻留；避免粗暴延长所有单位的等待或削弱新命令响应。此处只是后续排查方向，尚未实施。
- 十万人反馈是用户真实试玩的主观效果，不包含FPS、帧时间、硬件负载或长时稳定性证据，不替代性能基线。M7/V1整体验收、其他既有问题状态不变。
- 本次仅更新NEXT_TASK反馈交接，没有修改游戏代码/资产，也没有启动Unity、游戏、压力测试或浏览器。
