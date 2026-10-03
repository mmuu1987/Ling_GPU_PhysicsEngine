# 第二角色专项：KayKit持弓游侠 · 修正02

> **历史专题 / 入口提示（2026-10-03）：** 本页保留该阶段原始记录；当前正式入口已由 Version08 取代，见[工程导航](../Engineering-20261003/README.md)与最新机器人交付记录。下文“当前”“下一步”及构建命令不作为现在的执行清单。旧手工测试仍暂停，其他模型转换未授权；技术结果与人工认可范围按原记录保留。


2026-09-30。用户已打开04并反馈“完全ok”，随后授权下一专项。本轮选择远程人形，复用现有抛物线/默认远程系统。新角色和配方已进入独立小场景，没有改战斗核心，没有扩为兵种平衡或100k性能专项。

## 打开方式

`Builds/RangerPilot-20260930-02/Start-Ranger.cmd`

GUID：`0a3ff2fb12ec4d1c852b276663e8cc52`。内部EXE仍沿用通用试点构建器的`KnightPilot.exe`文件名，但该CMD启动的是Ranger02数据，不是骑士包；建议使用CMD隔离设置。

默认64游侠对64内置近战对照；为了观察发射，对照方为本场景私有的2m/s移动速度，非全局修改/平衡结论。游侠射程/搜敌半径10m，速度12m/s，重力−9.8，伤害10，攻击周期约2.667s，发射相位0.53，出生高度1.3m。继承现有近似发射点与异步请求机制，不是逐帧骨骼点发射。

设置在本包PilotData；人工方案目录`RangerPilotPlans`，与旧KnightPilotPlans/CharacterPipelinePlans隔离。自动验证使用Logs下另一个专用目录。此试点不迁移旧角色方案。

## 配方入口

Unity：`MassEngine > Character pipeline > Open workflow`。
选择 **`Assets/Game/CharacterPipeline/Recipes/Ranger02.asset`**，后续复现换新输出名，不能覆盖已有Ranger02。
产物：`Assets/Game/CharacterPipeline/Generated/Ranger02/`。
`Ranger.asset`、Ranger01产物和01包是首次挂接方向错误的诊断历史，不是最终制作示例。

复用内容：同一配方事务、尺寸归一化、生产VAT烘焙、全帧位置检查、调色板Low、私有单位/场景/Catalog与运行闭环。本轮仍使用Ranger专用准备器做一次性动作拼接和挂点校准，不是任意模型一拖就全自动。准备完成后，Ranger02配方可以直接换新编号复现，不需改C#。新素材准备仍需要匹配骨架路径、材质和挂点；本轮同作者/同Medium骨架，不声称已验证跨作者重定向。

## 一个真正暴露并解决的管线边界

原作者`bow_withString`带单一`Draw` Blendshape。旧管线会拒绝，不能为通过而丢掉弓弦形变。

本轮只扩展Editor侧3个文件：CharacterRecipe、CharacterGeometry、CharacterPipeline。新增**显式白名单**`blendshapeAttachmentPaths`，仅接受：无骨骼/无bindposes、单一Blendshape、一个100%线性帧的附件。绑定名必须匹配，关键帧及实际采样权重必须处于0–100。多形变/蒙皮形变仍拒绝；原有无形变/4权重Generic规则不变。

独立位置参考为 `renderer→root × (原顶点 + 权重×形变增量)`；0/50/100与Unity原生BakeMesh逐点对照最大差0（浮点输出精度内）。旧骑士04全部759924位置样本重新核对仍通过，旧角色/配置/包不重绑。

弓使用原作者形变。手持箭用原箭网格新增一个单帧收拢形变，在未搭箭/松弦后收拢至不可见；只用于VAT动作呈现。**飞行弹道仍是原系统真实发射的独立实例**，不是手持箭从骨骼直接变成弹道，也不是纯装饰假齐射。

## 挂接校准与动画

- Idle使用作者Ranged_Bow_Idle，Move/Death复用已有官方General/Movement源只读。Attack由作者Draw和Release顺序拼接，仍以Idle/Move/Attack/Death四槽接入，没有新增Walk/Run前提。
- 第一次近景发现弓弦朝外、箭轴竖向；数学几何门禁不会替代这个视觉检查。01保留，重新生成02，未覆盖01。
- 02以左右手实际坐标校准：弓绕自身长轴约162.87°，拉弦方向与持箭手方向点积−0.950→0.994；峰值使用作者形变范围的76.41%，没有改原弓网格/形变数据。
- 箭按长轴瞄准握弓手，尾端定在右手socket；没改身体骨骼或下移头部。校准姿态的弦中心至socket约6.2cm，这是挂点中心代理距离，不是手指表面接触证明。动作/发射只做近似配合，GPU回读还有延迟，不声称逐帧同步。
- 142帧、24FPS采样。Full9521/Low1293≤1400，位置+法线纹理31.0625MiB≤32MiB预算（不是总显存）。弓、身体、手持箭合计1351982个顶点帧位置独立核对，最大约1.283mm≤2.5mm。
- 48张强制LOD/动作结果、16张独立位置参考、4张近景释放阶段图。参考图仍共用VAT shader/UV/法线，不是独立原材质验证。Low可简化面部/细弦，真实镜头下是否接受仍需人工看。

## 真实运行与弹道证据

两个独立进程自动回调验证：选择新角色、96v64保存、跨进程加载、真实自然结算、满编重开、返回和GPU/运行副本释放。旁挂只读Probe读取真实弹道池/来源索引/间接绘制参数/敌方HP，不写发射请求、不注入伤害、结果或临时齐射。

| 进程 | 自然战斗时长 | 存活[游侠,对照] | 最大发射计数 | 最大绘制数 | 发射者源索引数 | 最大观测敌方HP损失 |
|---|---:|---|---:|---:|---:|---:|
| seed | 37.63s | [0, 38] | 422 | 31 | 92 | 3540 |
| reload | 35.15s | [0, 41] | 397 | 32 | 88 | 3490 |

发射计数为所观测运行周期中的最大值，来源索引集合涵盖正常局及短重开观察，不把重复采样条目当发射次数。负重力及同一弹道竖直速度随时间改变均有实测。以具体`ranged-evidence.json`为准。

对照方获胜是正常真实结果，不为证明接入而调强游侠。上述不是物理键鼠验收、兵种配平结论或100k性能证据。30FPS只是功能测试限帧设置。

## 官方来源与许可

- [KayKit Adventurers官方页](https://kaylousberg.itch.io/kaykit-adventurers)：Free2.0，Ranger及弓箭，CC0。
- [KayKit Character Animations官方页](https://kaylousberg.itch.io/kaykit-character-animations)：Free1.1，远程动作，CC0。
- 使用官方匿名免费入口，无账号/付费/模型上传。ZIP SHA与骑士阶段对应官方版本一致：Adventurers `abe48f4763fba0896bab486ee9e6d08ca6b5b3884b9601f235c8847ae94dc479`；Animations `65882f31f905ad2e953819648a59287cdeab8f623908d5ef701971d3758be20f`。
- 只新导入8份必要源文件，见selected-source-inventory.json；旧General/Movement两份FBX只读复用。官方落地页、下载回执与License保留。本地大ZIP及传输临时包在验证后已清理。
- Ranger模型/动画CC0不代表整个游戏CC0。随包保留RangerLicenses、既有KnightLicenses、ThirdPartyNotices、ExistingLicenseReview；不是法律/EULA/V1放行。

## 范围与保护

保护结果见Logs/AgentRangerPilot/final-protection-audit.json；旧核心/战斗/共享LOD不改，01/02/03/已接受04及旧主候选与受保护个人数据保留。仅新增私有资产/配方/准备器/只读Probe与所述3个Editor扩展，文档更新通过比对后写入。

当前平地小场景；共享近/中阈值30/120、镜头上限70不变，Far仅强制绘制。碰撞/搜索范围与旧引擎提示不扩成优化专项。未添加第三角色、压测、提交或推送。此Ranger最终人工观感仍待用户试玩。
