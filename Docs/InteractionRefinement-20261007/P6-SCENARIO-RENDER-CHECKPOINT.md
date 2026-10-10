# P6原型检查点：四组、正式混编与窗口化渲染补验

2026-10-07。用户授权继续留P6检查。**本轮补齐代表性场景功能与实际相机渲染／GPU帧时证据，可在下述明确边界内收尾P6最小原型检查点。未进入P7/P8，未制作38，也不是所有规模／设备的性能保证。**

状态：**P6_PROTOTYPE_CHECKPOINT_PASSED_BOUNDED**。此前“原型已实现、资格待补验”的状态由本报告取代；旧报告及失败记录原样保留。P3余项仍暂缓，P4/P5真人待验，正式48保持。

## 本轮决策

- 不再把此前单轮+12.59%当成确定的固定开销，也不继续围绕该数字无限排查。
- 生产代码没有改动。本轮新增测试、扩充受控GUI隔离与runner，证明的是原型在代表性场景能工作，而不是“修复了一个已定位的退化”。
- 仍不开放普通玩家入口。下一阶段若获授权才接P7成员框选；本轮不越级。
- 当前预算保持4逻辑组＋1备用槽，不上调。最大4096请求成员是API安全上限，不是本轮实测吞吐或任意地形都能容纳的承诺。

## 1. 四组同时移动与实际地形变化

真实Green地图与实际65536格导航；先暂停完成四个独立请求，再一起恢复，避免第一组已经走完才提交第四组。

| 场景 | 总成员 | 每组选择 | 四组在120固定模拟帧的目标方向平均前进m | 局部GPU逻辑分配B |
|---|---:|---:|---|---:|
| four-green | 2048 | 8×4 | 6.275 / 6.509 / 5.909 / 6.302 | 4071424 |
| four-authored-mix | 96 | 6×4 | 5.059 / 5.444 / 4.421 / 4.325 | 3938688 |

Green为2048总成员、4×8=32局部成员；剩余2016没有获得局部覆盖。四组每个所选成员都发生真实GPU位移；方向进度不是仅检查CPU请求状态。120帧是约2模拟秒，不能误写为墙钟2秒。

共同断言：
1. 四组独立移动；满4组时只改派一个成员、试图拆出第五组，被拒绝且旧序号保留。
2. 整组A改为Hold仍可提交，A保持局部待命／允许避让；B/C/D原序号保持且继续移动。
3. 两轮共8次整组反复换向，4组和备用槽重复使用，无串组；每个场景共13次成功提交，另保留一次预算拒绝。
4. teamId缓冲始终逐项相同。未选成员保持无局部覆盖；不将避让引起的被动位移错误要求为绝对静止。
5. Green通过真实SetStaticObstacles改变运行时障碍形状，下一批更新使旧局部导航通道失效，局部GPU逻辑占用归零。这不是只改测试布尔回调。
6. 混编场景最后经实际Controller下达整军团Hold，全部局部组／成员覆盖清零。

## 2. 正式兵种，不再只是两种fixture半径

使用正式Library里的male、knight、ranger资产。只克隆Scenario、UnitType与Spawn以安排试验人数、team和出生位置；movement、flocking、combat、animation、render均复用原正式资产引用，不降低半径、不改速度／伤害、不把48改回24。

96人混编：每队各16名male、16名knight、16名ranger。team0四个局部组各含2名male＋2名knight＋2名ranger，共24局部成员，其余72不分配局部覆盖。此测试证明正式混编功能，不等于2048人全远程交战性能。

另一个4人隔离场景验证正式游侠近敌策略：

`Authored ranger movement/combat/flocking/animation/render unchanged; Move moved and released target; Hold produced sourceAgentIndexPlusOne=3, sourceTeam=0, target=3; enemy hp 140 -> 130;teams unchanged`

不仅检查目标／总发射数，还回读ProjectileGpuData，核对sourceAgentIndexPlusOne=3、sourceTeamId=0、targetAgentIndex=3，并确认敌方hp下降。Move实际移动且不保留目标，Hold确实能自卫，不是停火。

## 3. 窗口化相机渲染与GPU帧时

Unity 6000.3.14f1、Direct3D11、NVIDIA GeForce GTX 1060 3GB；实际GameView为1054×543，vSync=0。启动参数请求1280×720不等于实际GameView分辨率，故以实际读数为准。固定模拟delta=1/60。

不是batchmode；以显式`--interaction-p6-owned-editor`及P6限定输出目录启用隔离，普通Editor会话不受影响。已保护/核对原Editor设置与布局文件，未关闭用户程序。

每个窗口重置同一场景、全军团Hold，准备off／empty／four状态；四组为32成员、目标距初始组中心约80m。暖机5秒、采样10秒，每进程6窗口。两轮顺序分别off/empty/four/four/empty/off和four/empty/off/off/empty/four，改变四组状态所处的时间位置。

- 以主游戏相机的自动render回调计数，不调用Camera.Render制造证据。各窗相机渲染计数与采样帧数相符。
- 四组窗口额外回读采样前后GPU坐标；每窗4组、32名局部成员均有超过0.2m的实际位移，不把空组或仅有CPU请求称为四组移动负载。
- FrameTimingManager取得有效非零GPU时间；原始CSV保留墙钟、Main Thread ns与GPU ms，并离线复算核对JSON。
- Main Thread包含Editor及等待；FrameTiming读取latest数据可能有帧延迟，不当作独立统计样本。相机渲染证据不等于OS compositor的present保证，也不是独立Player基准。

下表是每种状态两窗结果的平均；全部单位ms，未换算／宣传为玩家FPS。

| 运行／状态 | 窗中位间隔 | 窗p95间隔 | Main Thread均值 | GPU均值 |
|---|---:|---:|---:|---:|
| rendered-load-02 / off | 11.8252 | 13.1831 | 12.0677 | 9.6585 |
| rendered-load-02 / empty | 11.8550 | 13.0581 | 12.0122 | 9.6593 |
| rendered-load-02 / four | 11.9104 | 14.7357 | 12.3782 | 9.8713 |
| rendered-load-03 / off | 11.9657 | 14.5336 | 12.3736 | 9.9339 |
| rendered-load-03 / empty | 11.9037 | 13.2240 | 12.1034 | 9.7295 |
| rendered-load-03 / four | 11.9157 | 13.2361 | 12.1421 | 9.7328 |

| 运行 | empty/off中位差 | four/off中位差 | four/off GPU均值差 |
|---|---:|---:|---:|
| rendered-load-02 | +0.25% | +0.72% | +2.20% |
| rendered-load-03 | -0.52% | -0.42% | -2.02% |

本环境未见稳定的空覆盖或四组明显退化信号，支持保持原型4组预算。p95和单窗波动全部保留，不能仅挑中位数宣称不存在尾帧。四组运动会改变几何／可见性，此对照是场景负载观察，不是单变量GPU核净开销测定；没有做跨机器统计等效结论。

rendered-load-01为先行6窗口，已有渲染与GPU证据但未记录逐窗移动人数，保留作预检，不混入最终两轮主汇总。最终02/03新增移动计数后完整复验。

## 4. 更新成本与资源边界

65536 cells，单物理槽flow＋mask为786432 bytes；5槽3932160 bytes。双订单与回执按68×N bytes：2048人总4071424 bytes≈3.883MiB；96人总3938688 bytes。是实际ComputeBuffer逻辑分配，不是驱动显存峰值。

最终场景结果里的首次Move worker时间：Green 348.449ms、正式混编 364.126ms。Green复用网格Move范围31.639–37.633ms；混编31.225–35.612ms。这些在后台worker完成，不能伪报为主线程帧阻塞，也不能承诺第一条命令即时执行；CPU等待／已提交／GPU接受仍须分开。

每条请求的snapshot、worker和upload时间均在场景JSON中。窗口记录trackedMemoryMax是整个Editor的Unity追踪内存，不是局部通道独占内存或GPU峰值。CPU导航缓存估算与原型资源上限沿用原报告，不拿估算充当实测峰值。

## 5. 最终候选回归与保护

| 运行 | 通过/总数 | 跳过 |
|---|---:|---:|
| scenarios-02 | 3/3 | 0 |
| rendered-load-02 | 1/1 | 0 |
| rendered-load-03 | 1/1 | 0 |
| local-06 | 5/5 | 0 |
| legacy-kernels-03 | 67/67 | 0 |
| deployment-02 | 6/6 | 0 |
| editmode-05 | 715/715 | 0 |

新增三场景再次通过；旧局部GPU、旧内核、P4/P5交互与715项EditMode均在最终候选下复验。NUnit通过是断言／记录通过，并不扩大上述性能边界。本轮无失败轮；所有更早失败仍保留。

本轮受控载荷10/11/12只改LocalOrdersSceneTests.cs和P6测试隔离器，生产Core/HLSL、正式兵种资产、AgentData64、teamId、VAT与存档格式均未改。9659基线＋24批准路径全量核对通过；其余22个已批准路径相对本轮开始保持原字节。用户4份数据、37包387文件、正式48、HEAD／stage、GUID、462归档保持，0归档引用迁移。

原P6与性能第1轮报告不改；总计划与阶段状态的旧版本保存到本轮docs-before。结束时无Unity／工程锁，没有强关用户进程、删除未知锁或产生38包。

## 6. 检查点边界与后续

**P6最小原型检查点在当前实测边界内通过，可收尾；不再以未稳定复现的12.6%继续阻塞。** 这是“固定成员独立命令／导航”的检查点，不是已经做好框选或四命令全部UI入口。

明确不承诺：4096成员高频改令吞吐、全军大规模远程交战、所有地图／硬件／分辨率／长时稳定性、驱动故障注入、独立Player／OS present性能。这些保留为规模与发布资格边界，不伪装成已测，也不因它们无限扩大本轮最小原型任务。

若后续进入P7，沿用本轮4组限额、异步状态和失败保旧合同；只做军团内存活成员选择与显示，不把当前选区变成历史命令成员，也不自动推进P8。需要用户授权后才开始。

电脑位置：
- `Docs/InteractionRefinement-20261007/P6-SCENARIO-RENDER-CHECKPOINT.md/html`
- `Logs/InteractionRefinement-20261007/P6/scenario-followup-01/`：preflight、approved-before、analysis、audit、publication与finalization。
- `P6/scenarios-01/02/`：功能证据；`P6/rendered-load-01/02/03/`：窗口化记录与CSV；其余最终测试见上表。
