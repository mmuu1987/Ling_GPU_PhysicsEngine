# 导航 C · N5d 收尾准备：LaneApproach 删除方案（2026-10-13）

## 结论

- **开着 GPU 导航时，LaneApproach 已经完全不起作用。** 补跑了一次 11 万人追击战，只关 LaneApproach，其余设置相同。结果和开着时几乎一样：

| 指标 | LaneApproach 开（chase-n4c） | **关（chase-nolane）** |
|---|---|---|
| 帧耗时 | 69.39 ms | 69.67 ms |
| 轴向朝向比例（越低越好，随机约 0.133） | 0.159 | 0.158 |
| 开战时间 | 9.3 s | 9.3 s |
| 第 90 秒存活 | 103355 | 103291 |
| 第 90 秒交战 | 1559 | 1560 |

  截图也一样，没有条纹。原因是 GPU 导航给出方向后直接返回，后面的 LaneApproach 根本执行不到。山口和平原从 N3 起一直是在关 LaneApproach 的条件下测的。
- **但现在还不能直接删。** 游戏实际用的 `Flow1024.asset` 里，GPU 导航是**关**的，LaneApproach33 和 1024 是**开**的。现在删 LaneApproach，默认游戏会退回到有条纹的旧行为。
- **所以必须分两步：** 先让 GPU 导航成为默认，你在编辑器验收后，再删 LaneApproach。

## 第 1 步：GPU 导航设为默认（需要你确认）

- 修改 `Flow1024.asset`：打开 `gpuNavField`、`gpuNavSteering`、`gpuNavCrowd`、`gpuNavFormationLine`、`gpuNavArrivalFollow`，同时关闭 LaneApproach33 和 1024。
- `gpuNavLocalOrders`（局部命令走 GPU）也一起打开。它在 N5a 测过：山口的中位距离 118.7 → 84.3 m。
- 只改这一个资源文件，代码不动。想退回，把这几个勾改回去就行。
- 改完后你在编辑器里玩几局验收：追击、山口、平原移动、局部命令。

## 第 2 步：删除 LaneApproach（第 1 步验收后才做）

**LaneApproach1024（整套删除）：**
- `Terrain/TerrainLaneApproach1024.cs`（94 行）；
- `TerrainNavigationRuntime.cs` 里 Burst 内联的 `ApplyLane1024` / `FindLane1024`；
- 配置字段 `terrainLaneApproach1024`；
- 测试 `TerrainLaneApproach1024Tests.cs`（11 个测试）；
- 探针里的引用。

**LaneApproach33（只删运行时部分）：**
- 运行时调用、Burst 内联的 lane 循环、着色器里的 `terrainLaneApproach33` 分支、配置字段 `terrainLaneApproach33`；
- `TerrainLaneApproach33.cs` 这个类**保留**：4 个冻结测试（TerrainNavigationGrid、TerrainCardinalRouteCache、TerrainDynamicAsyncSolve、BurstDependencyRepair）直接调用它作为旧行为参照；
- 建议把它挪进测试程序集，标注为"冻结参照"，测试文件本身一行不改。

**不动的部分：**
- `Tests/PlayMode` 下的 P9 着色器副本：冻结副本，留着；
- `StripeDiagnose1024.cs`：只读字段做诊断输出，改成不再读这个字段，或者整个归档。

**3 个实验配置（Experiments/NavigationApproach 下的 Autumn、Green、Winter-Flow）：**
- 它们开着 LaneApproach33、没开 GPU 导航；删除后会失去这个修正；
- 它们不属于正式战场，建议同时给它们打开 GPU 导航，或者就不管。

## 之后

- 删除旧方向场路径（CPU Dijkstra 方向场）是更后面的收尾，届时另外再征求你确认。
