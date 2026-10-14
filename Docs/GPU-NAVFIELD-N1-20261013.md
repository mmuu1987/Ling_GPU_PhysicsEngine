# GPU 导航场 N0b 基线 + N1 影子场报告（2026-10-13）

## 结论
- **N1 验收通过**：GPU 细场（512² @ 2 m）在真实地图上与 CPU 参考**逐位一致**（单 pass，全图 262144 格，差异 0）；收敛后与精确 Dijkstra 全图一致（误差 ≤1e-3）。
- **影子模式，零行为改变**：只有开发者调用 `TryEnableGpuNavField` 才启用；默认不创建任何资源，单位寻路完全不变。
- **成本**：GPU 每 pass ≈ 0.034 ms（512²，含派发）；显存 7 MB（2 队）。启用时 CPU 建细网格 794 ms + 建图 112 ms，**必须放在加载阶段**（N2 处理）。
- **两个影响后续设计的发现**：
  1. 冷启动收敛需要 653 pass（≈5.8 s @ 2 pass/帧）≈ 最长路径格数 → **N2 种子是必需的**（或提高每帧 pass 数：pass 很便宜，32 pass/帧 ≈ 1.1 ms 可在 ~20 帧内收敛）。
  2. 等值线是**八边形**（8 邻接图距离的固有形状）→ 直接取 −∇d 仍只有约 8 个方向，条纹不会消失 → **N3 的松弛核需换成 eikonal 迎风更新**（GPU Fast Iterative Method，同样是 Jacobi ping-pong，缓冲区不变），影子模式下先做对比。
- 细可走掩码 = 粗掩码 ×4（142080 = 35520×4）：按已定方案（碰撞掩码 N1–N5 保持 4 m），2 m 细场带来的是**代价/梯度分辨率**，不是更细的形状。

## N0b 基线（同一 Editor 会话；导航 256² @ 4 m，lane33/lane1024 开）
| 场景 | 结果 |
|---|---|
| 追击 110k | dt 平均 69.8 ms，p50 43.8，p95 131.4，最大 408.6；392 个场全异步 Burst，合计 37.9 ms，单次最大 4.0 ms；最大排队 331.6 ms；下令→50% 移动 1.1 s，→接敌 8.8 s；>30° 邻格夹角比例 0.038–0.050 |
| 过山口 20k | dt 平均 16.6 ms，p95 20.6；**显式 Move 在主线程同步求解 20.7 ms**；115 s 中位距离 482→242 m；仅 40 人越岭；卡住峰值 668；**截图显示全军挤成一条细长蛇形队列**（N4 目标） |

结论：导航 CPU 成本可忽略，帧时间由其它工作主导；痛点是显式命令的同步尖峰、1 Tick 动态延迟、8 方向条纹、山口单列。

## N1 内容（工作树，未提交）
新文件：
- `Assets/MassEngine/FlowField/GpuNavFieldGraph.cs` — 细图（walkable + 每格 4 个 half 边代价）、目标描述 `GpuNavGoal`、CPU 参考 `GpuNavFieldReference`（与核函数同运算顺序的 Relax / Converge / Dijkstra）。
- `Assets/MassEngine/FlowField/GpuNavField.cs` — GPU 资源与每帧 Tick（每队一层 ping-pong 距离，2 pass/帧）；目标规则与 `TerrainNavigationRuntime.Rebuild` 一致；Point 目标若落在细掩码阻挡格，就近取 2 格内可走格（CPU 求解器在阻挡格上会得到空场，细掩码更严，故加此吸附）。
- `Assets/MassEngine/FlowField/Shaders/GpuNavField.compute` — `NavFieldRelax`（8×8）、`NavFieldFill`。每核 ≤2 UAV。
- `Assets/MassEngine/Core/MassEngineManager.GpuNavField.cs` — `TryEnableGpuNavField` / `DisableGpuNavField`。
- `Assets/MassEngine/Tests/EditMode/GpuNavFieldTests.cs` — 6 个用例。

对现有文件的追加式修改（字节级脚本，唯一匹配断言，原件备份在 `outputs/Round6/n1_bak/`）：
- `TerrainNavigationGrid.cs`：+`CopyGraph(out byte[], out double[])`（只读拷贝）。
- `ComputePipelineOrchestrator.cs`：+`FineNavigation` 属性；`DispatchRuntimeFlow` 之后 `FineNavigation?.Tick(...)`（为 null 时无任何开销）。
- `MassEngineManager.cs`：`Release()` 中 +`DisableGpuNavField()`。

## 验证
- 编译：0 error / 0 warning。
- EditMode（过滤）：`GpuNavFieldTests` 6/6 通过（含 GPU 逐位用例 Point/Area，真实跑在 GPU 上，未跳过）；冻结的 `TerrainNavigationGridTests` 23/23 通过。
- Play（ForestHills，20k，60 s，`gpunav=1`）：
  - 逐位：单 pass 差异格 0。
  - 收敛：t=1.0 s 已达 13350 格，2.7 s 71756，3.8 s 130336，4.8 s 141176，**5.8 s 全部 142080 格与 Dijkstra 一致**，之后保持 0 误差。
  - 帧时间：dt 平均 16.78 ms，p95 19.79（跨会话不可严格比较；最大 1492 ms 是探针中途启用 + 诊断用 Dijkstra 的一次性成本）。
- 数据：远端 `Logs/NavBaseline/pass-gpunav-20261013-004202/`（summary、gpunav.csv、热力图 gpunav-converged/end.png）。

## 已知限制（留给后续阶段）
- 与所有目标都不连通的孤立区域，若残留有限值，会缓慢“数到无穷”而不是立即变不可达。冷启动全部为 Unreachable 时不出现；目标跨连通分量移动时会出现 → N2 用静态图的连通分量标签处理。
- Density 目标在 N1 只做 GPU 端，未与 CPU 动态场逐格对比（真实对比需要 N2 的种子路径）。
- 停止半径（targetStopRadius）属于采样阶段（N3）。

## 下一步
- N2：加载期建细图；目标变化时用种子在 ≤2 帧内给出有效场；连通分量标签。
- N3（默认关闭开关）：eikonal 松弛核影子对比 → 采样 −∇d。
