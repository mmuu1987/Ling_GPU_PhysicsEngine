# P8：四类命令与全部玩家入口自动检查点

2026-10-08。用户授权“可以，我们进入P8吧”。**P8实现及下列有边界自动检查点通过，停在P8；不自动进入P9、不生成38包。**

状态：`P8_SCOPED_COMMAND_CHECKPOINT_PASSED_BOUNDED`。源码中的正常战斗入口已接通，不再需要P7开发预览。**37回退包未更新，不能用37验证P8。真人“指哪批哪批动”、全流程手感及广泛性能仍未签收。**

## 1. 当前操作与范围

1. 从当前源码启动战斗，先选择军团，再在非UI战场区域左键拖框。只选该军团当前存活成员，6物理像素阈值；投影选取可穿遮挡，选中环仍正常受深度遮挡。
2. **M／移动**进入选点；地面或目标模式中的小地图左键确认单一目标。小地图右键也走同一作用域。
3. **A／进攻**让所选成员主动接敌；**R／撤退**去本军团既有出生／后方锚点的合法到达区域；**H／防守**对应待命，停止主动行军追击，但保留分离、避让、近距离自卫、冷却与动画。
4. 空框就是零选中，Pending或错误也不能回落为整军团。要指挥全部，明确点击“整军团范围”，或按既有切换军团规则选择另一军团；下一条命令才改变命令，清选择本身不改变历史命令。
5. 局部移动仅单目标，Shift追加明确拒绝且旧命令保留。整军团已有航点继续可用；全军团新命令及合法全军团航点追加会覆盖该军团的局部覆盖，不波及其他军团。
6. Esc先取消选点并保留当前局部选区；非选点时清选区，均不清历史命令。统计、帮助、切选区本身不下令。正常整军团初始状态采用惰性选择资源分配，没有持续选择GPU回读。

## 2. 统一作用域与事务语义

按钮、A/M/H/R分发、地面射线落点、小地图左右键/追加和旧界面共同进入唯一解析层：WholeArmy、Local、Pending、Empty、Unavailable明确区分。选择本局buffer身份、军团、epoch、request、成员version与不可变成员副本用于验证；选区epoch不冒充命令通道epoch。

移动开始选点时冻结意图。期间切军团、改变选区/成员版本或换局后，旧选点拒绝，不把旧局部意图变成整军团。常规存活维护回读在途时可用最近已确认快照；新框选Pending仍严格禁止旧选区提交，提交层再检查实时HP。

局部下令先显示“校验/规划，尚未提交”；实际提交和GPU观察人数分别显示。死亡过滤、未被本命令覆盖的人数不会被描述为全部成功。失效旧回执不覆盖更新的整军团成功反馈；错误说明原因和旧命令保留，并避免被旧命令进度立即刷掉。

显式Controller整军团API仍是整军团，不读取HUD选区。未选成员的旧命令/默认军团目标不被局部下令改写。成员命令快照、当前选区、统计与导航组相互独立。

## 3. 四命令的底层行为

- 移动和撤退沿用MoveOnly与独立障碍导航，不暗改为攻击移动。撤退使用已有军团锚点，先验证最大所选半径、当前连通性和可容纳成员的分布到达区域。
- 攻击使用所选最大半径导航空间内的存活敌人多源场；每个所选连通分区均须有可达存活敌人，否则整次拒绝保旧。GPU沿用既有主动接敌与目标获取，不增加点名集火，也不复用其他成员的军团导航槽。
- 攻击场只在运行状态发起限频刷新：每0.25秒最多启动一个组、轮询最多4组；4组时一轮至少约1秒，受回读/规划时延影响，并非每帧即时重算。单个在途位置/HP异步快照和单规划任务；用户命令优先取消刷新，尚在退出的工作会明确busy拒绝，不无界并发。
- 刷新从当前命令映射取成员，不能把已移交的重叠成员拉回旧组。刷新失败保留旧路径并报告；新命令提交失败不改旧GPU命令。
- 仍为32-byte局部命令sidebuffer；64-byte AgentData、VAT、teamId不重用。4逻辑组＋1事务槽、每次4096成员、65536导航cells预算不变。
- 保留P7选择mask/count异步回读、批量环和拒旧机制。选择维护最多约4Hz；攻击刷新另有全体简化XZ位置/HP异步快照，**不是每帧同步读完整AgentData**，也不能声称完全不做全体简化数据回读。

## 4. 暂停与生命周期

暂停时合法局部命令在实际提交后遵守原有改令恢复规则；快照/规划尚未提交不提前恢复，拒绝不恢复。提交后的手动播放状态版本优先，迟到事务不能撤销后来一次手动暂停。整军团已有路线Shift追加保留原来的暂停行为。

结算拒绝新命令并释放局部通道/选择；含命令快照和选择回读在途的结算及场景卸载都验证失效旧回执、释放逻辑资源、不能迟到恢复。返回布阵确认取消保留历史与既有暂停恢复；确认后清选择、组和导航资源，再开战从干净整军团范围开始。

返回布阵与卸载证据：

`Real uGUI tools-edit and confirmation cancel/confirm callbacks: cancel preserves local history and legacy resume state; confirm returns Setup and frees selection/channel. New battle reselects cleanly. Scene unload with pending local snapshot and selection readback disposes both, invalidates pending receipt; no old GPU selection can cross scene generation. Isolated stat/plan paths; no save performed. Not human OS navigation acceptance.`

Dispose及逻辑buffer持有归零有自动断言，不等于测得驱动显存立即回收，也不替代长稳/反复加载泄漏资格。

## 5. 最终采用的自动验证

|运行标签|通过/执行|跳过|
|---|---:|---:|
| scoped-scene-05 | 6/6 | 0 |
| scoped-kernels-03 | 4/4 | 0 |
| selection-01 | 6/6 | 0 |
| local-01 | 5/5 | 0 |
| legacy-kernels-01 | 67/67 | 0 |
| deployment-01 | 6/6 | 0 |
| scenarios-01 | 3/3 | 0 |
| editmode-02 | 747/747 | 0 |

合计 **844项执行，0失败、0跳过**。这是所列最终有效运行的合计，不把早期失败或反复跑同一套的次数累计进通过数，也不称整个项目所有PlayMode测试都已覆盖。

- GPU四命令：攻击前进/伤害、撤退释放攻击目标、再攻击、绕障攻击、限频刷新保持序号、新待命不被旧刷新撤销且停止远处追击；四种命令同场、同军团相反方向、撤退进入验证区域、重叠只移交交集。
- 真实Green：GPU框出9名成员；四按钮、热键共同路径、地面射线、小地图真实EventTrigger回调等逐次核对回执成员和GPU命令槽，所有未选槽不被改变，军团默认命令仍为Hold。
- 空框、Pending、目标冻结失效、非法/范围外目标、局部追加拒绝、暂停成功/拒绝/后续手动暂停、整军团航点、帮助/清选择、结算/重开/布阵/场景卸载。
- P7选择6项、P6独立命令5项、旧GPU内核67项、部署平移/尺寸6项、Green四组/正式male-knight-ranger混编/游侠自卫3项及747项EditMode回归。
- 最后一次补验仅增加返回布阵及卸载测试，生产代码未再改；scoped-scene-05重新跑该场景套件6项。此前完整列明回归不是又重复跑了一遍。

这些包括真实GPU、实际uGUI回调和生产输入处理函数的合成事件，不是OS键盘鼠标真人操作，也不冒充真人签收。

## 6. 影像与描述性成本样本

[四命令抽帧MP4](P8-media/P8-command-capture.mp4) · [GIF](P8-media/P8-command-capture.gif)。原始逐帧PNG及实际墙钟时间线在 `Logs/InteractionRefinement-20261007/P8/scoped-scene-05/p8-frames/`。

38张真实GameView抽帧，包含拖框、确认、移动、待命、撤退、攻击。固定模拟步长1/60，每12帧采一次；阶段间夹有成本采样，播放时长也对选区展示做了延长，**这是自动事件驱动的抽帧演示，不是连续实时录像、真人指挥录像或AI生成画面**。

![移动阶段](P8-media/P8-move.png)

![原图选中环局部放大](P8-media/P8-selected-detail.png)

原始选中图裁切放大，仅方便检查细环，不是更高分辨率来源。实际战场区域检测到37个青色阈值像素；这只证明环在画面中可见，不拿像素数替代9名成员的GPU证据。

现场信息：`Authored Green 2048-agent rendered GameView. Actual GPU selection and real uGUI button callbacks; production ground-raycast helper. Fixed captureDeltaTime=1/60; PNG sampled every12 simulation frames, timeline preserves actual wall times. Camera positioned for evidence; no OS mouse or human acceptance. Cost windows 240 frames each, sequential unbalanced descriptive samples, not a regression acceptance/peak VRAM/large-scale claim. Logical buffer byte counts exclude native driver/mesh/material overhead. GPU timing zero means unavailable. Separation48 asset unchanged. GPU=NVIDIA GeForce GTX 1060 3GB API=Direct3D11 reportedVRAMMiB=2965 GameView=1054x543`

|窗口|样本|墙钟秒|Update间隔中位ms|Unity CPU帧中位ms|Unity GPU帧中位ms|选择/命令逻辑GPU bytes|
|---|---:|---:|---:|---:|---:|---:|
| selected-attack | 240 | 2.69 | 10.97 | 10.95 | 7.41 | 16412 / 4071424 |
| selected-hold | 240 | 2.65 | 10.93 | 10.91 | 7.36 | 16412 / 4071424 |
| whole-lazy | 240 | 2.83 | 11.33 | 11.34 | 7.64 | 0 / 0 |

每窗口240帧，顺序采集、工作量不同且时间短；FrameTiming可能滞后/重复并含Editor共同开销。因此不能据此计算/签收P8的净开销或等效性能，Update间隔不是OS present/FPS，逻辑buffer bytes也不是峰值显存。2048总人数与约9人局部样本不证明全员局部、任意规模/设备/长稳可用。CSV、回读计数、资源公式及原始图像保留，广泛性能资格和真人体验待验。

## 7. 失败记录与保护

- scoped-scene-01：新增测试漏填Controller移动API的append参数，编译失败；修正测试调用，未当作通过。
- scoped-scene-02：4/5通过，录制用倾斜相机却按Y=0构造测试选框，实际地形高度使其框空。改为真实GPU世界位置，并显式聚焦GameView；没有改生产投影算法掩盖测试。03、04及最终05通过，原失败日志/图像保留。
- 9693份基线＋30份已批准源码/meta记录核对；完整37包387文件、正式male/knight分离48、HEAD/暂存区、462份旧归档引用、用户4项数据保护通过。没有强关用户程序、删除未知锁、stage/commit或更新37。
- P3余项仍按用户决定暂缓；P4/P5真人待验不被代签；旧报告及历史结论保留。未进入P9/38/Onboarding。

## 8. 交接与待验

电脑项目根：`E:/GitHub/Ling_GPU_PhysicsEngine/Ling_GPU_PhysicsEngine`。

源码以当前P8批准哈希为准；测试/差异/备份/成本与失败证据位于 `Logs/InteractionRefinement-20261007/P8/`，实施/运行工具位于 `Tools/InteractionRefinement/`，本报告及媒体位于 `Docs/InteractionRefinement-20261007/`。

**现在停在P8等反馈。** 人工确认重点：指哪批哪批动、误点/UI穿透、镜头/框选手感、暂停与返回布阵体验；连续真人录屏、更多规模/混编攻击表现及广泛性能未签收。进入P9、局部航点或新出包均需另行授权。
