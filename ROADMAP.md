# 路线图

长期方向与阶段划分。**临时的任务交接写在 `NEXT_TASK.md`（不进 git，一次性）**，
两者分工：这份文件回答「接下来往哪走」，`NEXT_TASK.md` 回答「上一个人停在哪、手上还捏着什么」。

工作方式的约定见 [AGENTS.md](AGENTS.md)，最要紧的一条是：初级阶段推进主线优先，
不阻塞主线的细节记进本文件而不是当场停下来修。

## 现状（2026-09-05）

引擎核心进入稳定期，后续只修正确性阻塞项，主要开发转向 `Assets/Game` 的战争沙盒闭环。
这条决策来自 [Assets/Game/PerformanceBaseline.md](Assets/Game/PerformanceBaseline.md)。

- 主线分支 `main`，最近一次合流是 PR #14（UEBS2 交战 + GPU 弹道曳光渲染）
- 测试基线：PlayMode 29 / EditMode 63 全绿
- 默认档口径：**50k vs 50k**（100k 单位，约 30 FPS），与 `WarSandbox.unity` 实配一致

## 阶段 1：口径与视觉完成度（进行中，剩下的都不阻塞）

- ~~默认档规模口径漂移~~ 已统一（2026-09-05）：文档跟随场景，默认档 = 50k vs 50k
- ~~守方远程能力半改状态~~ 已定（2026-09-04）：`DefenderUnitConfig_Combat.asset`
  `projectileRange: 20` + `targetAcquireRadius: 20`，守方成为名副其实的远程兵
- ~~清掉无引用的备用相机套件~~ 已删（2026-09-05）：`RigCameraManager` + `SceneViewCamera*` 六个脚本，
  删前验过 GUID 与类名双向零引用；`CameraControls/` 保留 `MyCameraManager`（场景在用）、
  `CameraMotionSafety`、`LocalRotationAndScale`
- **弹道视觉完成度**（延后）：目前是单 pass 半透明四边形，没有拖尾贴图、发光、命中特效、
  音效、屏幕震动，见 [Assets/MassEngine/Projectiles/README.md](Assets/MassEngine/Projectiles/README.md)
  的已知限制。这是「能不能拿给人看」的门槛，纯游戏层/美术工作，不动引擎契约
- **`trailLength` 发射时固定写 1**（随手可带）：长度只由 `trailLengthScale` / `trailMinLength` 控制，
  不随速度或兵种变化。改一处就能让不同兵种的曳光有辨识度
- **不阻塞的真机确认**：守方 20/20 之后蓝色曳光数量是否与黄色接近、邻域搜索开销按 R² 上升
  约 6.25× 之后的帧时间。batchmode 只证明了能跑通（含加载真实场景的 `WarSandboxSmokeTests`）

## 阶段 2：多军团独立导航（当前优先，游戏层最大功能缺口）

现状是**一张流场对应一个阵营**，所以「军团」暂等同于攻/守两支大军
（[Assets/Game/README.md](Assets/Game/README.md) 自己点明了）。引擎侧的边界是
「流场和战斗身份固定支持 team 0/1，N 队伍尚未实现」——「多军团」和这条边界是同一件事，得一起设计。

这是战争沙盒从「演示」走向「游戏」的关键一步，也是**唯一值得动引擎数据契约的功能需求**。

**方案已写**：[Assets/方案设计/多军团独立导航方案.md](Assets/方案设计/多军团独立导航方案.md)
（2026-09-05）。它定下的四条结论：

- **不新增 `groupId`**，把 `teamId` 的取值域从 `{0,1}` 放宽到 `{0..N-1}`。「军团」= 一个 team。
  这样 `IsEnemy`（全引擎最热的谓词之一）一行都不用改，代价是这一轮不支持结盟
- **buffer 按 group 分段**（`group * cellCount + cell`），不是每 group 一个 buffer。
  照抄 spatial hash 已有的 `teamSlot * gridCellCount + cellIndex` 模式
- **显存不是障碍**：每军团约 5.4 MiB（实配 720² 世界 / cellSize 3 / 流场 256²），16 支军团 87 MiB。
  真正的成本是寻敌要遍历 N-1 个敌方桶，以及流场重建带宽 × N
- **分五步增量落地**，第 1 步（把硬编码的 2 参数化）的验收标准是 `groupCount = 2` 时逐帧一致

## 阶段 3：引擎技术债（只在规模需求真的出现时做）

按性价比排序，都不阻塞玩法。**默认全部不做**，除非某条真的挡住了阶段 2。

- **弹道发射请求仍需回读三份动态数组**，更大规模应改为 GPU 端压缩或完全 GPU 分配
  （[Assets/MassEngine/README.md](Assets/MassEngine/README.md) 当前边界）—— 与刚做完的弹道渲染同属一块
- **弹道池固定为 Agent 数的 25%**，满池丢弃新请求并记溢出。远程兵占比高的编成会先撞上这个
- **三维导航**：静态障碍目前只有有限数量的 XZ 矩形，方案已写在
  `Assets/方案设计/流场三维扩展方案.md`，仍是方案阶段
- **LOD 非确定性**：远处 agent 降频导致结果受 LOD 中心影响，不是镜头无关的严格确定性模拟。
  只有做回放、录像或联机时才必须解决
- **`GRID OVERFLOW`**：50k 档约 400/帧，溢出单位当帧不参与完整分离查询，可能局部穿插。
  战斗寻敌已改分阵营格规避了「友军挤走敌军」，剩下的是纯拥挤视觉问题
- **暂停后一两帧多出几条曳光**：已进入异步 readback 管线的发射请求仍会落盘，几帧内自限。
  已定不改（真机确认不影响观感）；若将来要求严格冻结，改法是在 `MassEngineManager.Update` 里
  用 `battleStarted` 门控 `ProcessLaunchRequests`，代价是丢弃在途请求，属行为变更需重新确认
