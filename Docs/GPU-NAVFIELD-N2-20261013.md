# GPU 导航场 N2 报告：种子 + 加载期构建（2026-10-13）

## 结论
- **N2 验收通过**：显式命令（Move）后**第 1 帧**种子就上传，全图 142080 格与精确解一致；验收标准是 2 帧内。
- **种子就是 GPU 的不动点，逐位相同**：上传后 GPU 再算一轮，变化格数为 0。EditMode 测试和真实地图都验证了这一点。原因是种子用 float Dijkstra，与 GPU 使用同一套 half 边代价、同样的 float 加法；边代价 ≥1 m，所以不动点唯一。
- **N1 冷启动 5.8 秒的问题已解决**：显式命令从 653 轮缩短到 1 帧。
- **追击（动态 Density 目标）**：开局种子最多 5 帧到位，主要耗在异步回读密度上。之后 GPU 场跟踪移动的敌军，三次对比的最大误差都是 4 m（一个粗格）。
- **"数到无穷"问题已解决**：每次种子都会把与目标不连通区域的残留值清成 Unreachable（测试用例：全图先写满过期值 3.0，种子后与参考逐位一致）。因此**不再需要单独的连通分量标签**。
- **加载期构建**：新增配置开关 `RuntimeFlowConfig.gpuNavField`，默认关闭。打开后，细场随地形导航在加载阶段构建（约 0.8 秒），导航重建（部署 Apply）时同步重建。战斗中最大帧时间从 N1 探针中途启用时的 1492 ms 降到 169 ms。
- **仍是影子模式**：士兵寻路完全不变。开关默认关闭；关闭时每帧只多一次引用比较。

## 改动（工作树，未提交）
新文件：
- `Assets/MassEngine/FlowField/GpuNavFieldSeed.cs`：`GpuNavSeedJob`（Burst，float Dijkstra，工作线程）和 `GpuNavSeedWorkspace`（原生细图 + 每队工作区，CPU 约 12.3 MB/2 队）。

修改我自己的 N1 文件：
- `GpuNavField.cs`：目标变化时触发种子。
  - Point / Area：立即排作业，最迟 `ForceSeedFrames`（2）帧强制完成；
  - Density：先异步回读该队的密度切片，再排作业；
  - 作业完成后若目标未变，上传到当前层。
  - 新增统计：SeedUploads / Discarded / Forced / Burst / MaxSeedFrames / MaxDensitySeedFrames；新增 `SeedEnabled`（默认开）和 `SeedNow`（测试 / 诊断用）。
- `MassEngineManager.GpuNavField.cs`：`SyncGpuNavField()`。启用条件是开发者调用 `TryEnableGpuNavField`，或配置开关打开且已指定着色器。导航换掉时自动重建，条件不满足时自动释放；构建失败不会每帧重试。
- `GpuNavFieldTests.cs`：+3 个用例（Point / Area 种子与不动点逐位一致且再算一轮不变、Density 种子）。

对现有文件的追加（字节级脚本，唯一匹配断言，原件备份在 `outputs/Round6/n2_bak/`）：
- `RuntimeFlowConfig.cs`：在末尾新增 `[Header("GPU Nav Field")]`，以及 `gpuNavField`（默认 false）和 `gpuNavPassesPerFrame`（默认 2，范围 1–64）。
- `MassEngineShaderConfig.cs`：+`gpuNavFieldShader`。
- `MassEngineManager.Terrain.cs`：`EnsureTerrainResources` 末尾 +`SyncGpuNavField();`。
- `Assets/Game/Settings/ShaderConfig.asset` 和 `MassEngineShaderConfig.asset`：各加一行 `gpuNavFieldShader` 引用（只是引用，开关仍关闭）。

## 验证
- 编译：0 error；新增 0 warning。
- EditMode（过滤）：`GpuNavFieldTests` 9/9；名字含 Terrain 的测试加上 GpuNavField 共 129/129。
- Play 1：过山口，2 万人，60 秒，走配置开关路径。逐帧数据如下：

| 下令后帧 | 种子上传 | 可达格 | 与精确解不一致 | 再算一轮变化格 |
|---|---|---|---|---|
| 1 | 0 | 9 | 142071 | |
| 2 | 1（Burst，未强制） | 142080 | **0** | **0** |
| 3–8 | 1 | 142080 | 0 | |

- Play 2：追击，2 万人，40 秒。Density 种子 2 次（两队各 1 次），最多 5 帧到位，全部 Burst。跟踪对比如下：

| 时间 | 敌军目标粗格数 | 可达格（GPU / 精确） | 最大误差 |
|---|---|---|---|
| 1.0 s | 963 | 142080 / 142080 | 4 m |
| 10.6 s | 948 | 142080 / 142080 | 4 m |
| 30.3 s | 657 | 142080 / 142080 | 4 m |

- 帧时间：过山口平均 16.65 ms（N0b 基线 16.6）；追击平均 18.31 ms。最大帧时间来自探针自己的同步 Dijkstra 对比，每次约 0.5 秒，不属于运行时开销。
- 数据目录：远端 `Logs/NavBaseline/pass-n2-20261013-070017/`、`chase-n2-20261013-070405/`。

## 已知限制
- Density 模式下，开局种子到位前的约 5 帧里，场只在敌军附近有效。这段时间影子模式不受影响；到 N3 采样时，这些帧需要回退到 CPU 流场。
- 部署 Apply 触发导航重建时，打开开关会额外花约 0.8 秒构建细网格。之后可以优化，比如复用粗网格的计算。
- 种子作业单独耗时未单独测出。实测从排作业到上传 ≤1 帧，即作业在一帧内完成。

## 下一步：N3（默认关闭开关）
1. eikonal 松弛核与当前核并行做影子对比（目标：圆形等值线，消除 8 方向条纹）。
2. 采样 −∇d 替代 CPU 方向场；Density 种子未到位时回退 CPU 流场；停止半径在采样阶段处理。
3. 验收：行为一致、条纹、过山口、关闭 LaneApproach。
