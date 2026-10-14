# GPU 导航场（方案 C / N 阶段）N0 设计文档

日期：2026-10-13　状态：**设计稿，未实施，等待批准**　精度：**2 m**（用户已定）

## 0. 结论

- 新建一张 **2 m GPU 距离场**（1024 地图为 512×512）。用"值迭代"在 GPU 上增量松弛，每帧只做少量遍。
- **CPU Dijkstra 保留**，改做"粗场种子"：256×256、4 m，沿用现有 Burst Job 和 1 Tick 时延。新命令下达后，细场在 1–2 帧内全图有效，没有 UEBS2 那种"场慢慢长过来"的延迟。
- 兵员改为在细场上做 **距离梯度 + 双线性采样**（即 `FLOWFIELD-GRADIENT-PLAN-20261011.md` 第 4 节方案），取代"8 方向 + 最近格"。
- **N4 才加入人群代价**：拥挤格变贵，后排自动分流。
- 全程放在开关后面，默认关闭；旧路径不动，验收后才删除。碰撞仍用现有 4 m 掩码（见 3.6）。
- 显存：典型约 14 MB，最坏约 50 MB（见第 5 节）。GPU 每帧耗时**尚未测量**，N1 实测。

## 1. 现状（只列与本方案相关的部分）

| 环节 | 现在怎么做 | 文件 |
|---|---|---|
| 网格 | `TerrainNavigationGrid`：按 clearance 膨胀的可走掩码，8 邻接，边代价 = 地表 3D 边长 | `Terrain/TerrainNavigationGrid.cs` |
| 队伍场 | 每队一张 Dijkstra 方向场 `flowFieldDirections[team*cells]`（`float2`，8 方向量化） | `TerrainNavigationRuntime.cs` |
| 显式命令 | `targetMode` 1（点）或 2（区域）：同步 CPU 解算，立即上传 | 同上 `Rebuild` |
| 动态追敌 | GPU 敌方密度 → AsyncGPUReadback → Burst `TerrainNavigationDynamicJob`（取目标、Dijkstra、LaneApproach33/1024）→ 上传；每队约 0.35 s 一次，轮转，时延约 1 Tick | 同上 `Tick` |
| 本地命令 | `LocalOrderChannel`：9 槽位，每槽一张方向场加一张**按半径区分**的掩码；`MaxCells=65536` | `Core/LocalOrderChannel.cs`、`LocalOrderPlan.cs` |
| 兵员采样 | `SampleFlowDirection` / `SampleLocalOrderFlow`：取所在格方向 → 走向邻格中心，前提是 `TerrainSegmentClear` 通过；场为 0 = 原地，不做直线回退 | `Core/Shaders/AgentDataCommon.hlsl:780–840` |
| 碰撞 | `_NavigationWalkable`（与流场同分辨率）：`TerrainNavPointOpen`、`TerrainSegmentClear`、大体型探针 | `Terrain/Shaders/TerrainNavigation.hlsl` |

已知问题：
- 4 m 条纹（8 方向量化 + 最近格）；
- 靠 LaneApproach33/1024 补丁压住；
- 拥挤时不会绕行（代价里没有人群）。

## 2. UEBS2 参考（只借思路，不抄常数）

细节见 `~/work/ue/NOTES-flowfield.md`。
- 1 m 网格，`R16` 势场，每队一层，每个命令目标一层。
- 原地 Bellman-Ford 松弛，每帧 1 遍，轮转处理。
- 每格代价 = 基础值 + 每人一个增量（人群代价）；另有"老化项"让过时的低值慢慢升高。
- 兵员比较周围 8 格挑最小值，不插值。
- 弱点：
  - 新目标的场要一圈圈长出去，开局需要烘焙；
  - 老化项让场整体偏高，一直在漂；
  - 原地松弛的结果与线程执行顺序有关（不确定）。

## 3. 设计

### 3.1 数据（2 m 细场，`R = 512`）

| 缓冲 | 格式 | 每份大小 | 份数 |
|---|---|---|---|
| `NavDist[layer]` ping-pong | `float`（不可达 = 1e30） | 1 MB | 每个**活动**层 ×2 |
| `NavEdge[class]` 静态边代价 | `uint2` = 4 个 half（右、上、右上、左上），阻挡 = inf | 2 MB | 每个 clearance 类 1 份 |
| `NavCrowd` 人群占用 | `uint`；N4 再加 EMA 的 `float` | 1 MB（N4 为 2 MB） | 每队 1 份（见 3.5） |
| `NavSeed[layer]` 粗场距离 | `float`，256² | 256 KB | 每个活动层 1 份 |

层的分配：
- `teamCount` 个队伍层：动态追敌或显式命令，与现在的 `teamFlows` 一一对应；
- 加最多 9 个本地命令层（与 `PhysicalSlots` 对应）；
- 只有活动层分配和派发，空槽位不占计算。

### 3.2 松弛：值迭代（Jacobi，ping-pong）

```
d'[c] = 0                                      c 是目标格
d'[c] = min_n ( d[n] + w(c,n) )                其他可走格（不含自身旧值）
d'[c] = 1e30                                   不可走格
w(c,n) = edge[c→n] * (1 + crowdGain * crowd[n])      N4 之前 crowdGain = 0
```

- **会升也会降。** 过时的低值每遍至少升高一个最小边代价（约 2 m）；过高的值以每遍 1 格的速度被纠正。不动点就是精确最短路，不需要 UEBS2 的老化项，也没有整体漂移。
- **确定性。** 读 A 写 B；`min` 与执行顺序无关，结果逐位可复现，方便写测试。
- **单独的着色器文件** `GpuNavField.compute`，每个 kernel 最多 2 个 UAV，不受战斗着色器 D3D11 8 UAV 上限的影响。
- **每帧预算：** `passesPerFrame`（默认 2）× 活动层数。按层轮转；收到新种子的层会先连续跑 `seedPasses`（默认 6）遍。

### 3.3 种子：CPU 粗场（我们的技术）

- Dijkstra Job 本来就算出了 `distances`，只是没输出。改成把距离一并上传（256² `float`）。方向输出保留不变，旧路径完全不受影响。
- 上传后，`Seed` kernel 把粗场距离双线性放大到细场，作为 `d` 的初值；目标格写 0，不可走格写 1e30。
- 粗细之间的误差靠 3.2 的值迭代在几遍之内消掉。这一点要在 N2 实测，验收标准见第 6 节。
- **种子来源：**
  - 显式命令和本地命令：现有的同步解算（立即）；
  - 动态追敌：现有的异步 Job（约 1 Tick，每队约 0.35 s）。
- **双缓冲切换：** 动态层的新种子在后台层松弛 `seedPasses` 遍后再切换给兵员，避免每 0.35 s 跳变一次。

### 3.4 兵员采样（`AgentDataCommon.hlsl`）

- 方向 = −∇d：中心差分加双线性。
  - 遇到不可走或不可达的邻格，退化为单侧差分；
  - 两侧都不可用时，退回"走向最低邻格中心"。
- **安全规则不变：** 只有一格长度的扫掠通过 `TerrainSegmentClear`，才采用连续方向；否则走邻格中心；还不行就返回 0。不做直线回退。
- **停止规则不变：**
  - `d <= stopRadius` → 0；
  - 本地命令到达区内，仍按 `LocalOrderPlan` 分配的槽位直线走，防止互相穿插换位。
- **开关方式：** 用 uniform `_GpuNavFieldOn` 分支，不加 keyword。现有 8 个变体不翻倍，ShaderCheck 时间不变。

### 3.5 人群代价（N4）

- `NavCrowd`：每帧一个 kernel 按队伍把兵员计数到 2 m 格（`InterlockedAdd`），再做 EMA 平滑。
- **推荐只算本队人数：** 追敌场的目标格本来就是敌人；移动时堵路的主要是自己人。待你确认。
- **防振荡措施：**
  - EMA 时间常数约 0.5 s；
  - 每格加价有上限（`crowdCostCap`）；
  - 增益从 0 往上调。
  - 风险：两条路轮流变挤，兵员来回摆，见第 7 节。

### 3.6 碰撞掩码保持 4 m

- 细场的可走集合 = 2 m 可走 **且** 所在 4 m 格也可走。这样保证细场永远不会把兵引进碰撞检测不让过的缝隙。
- 代价：障碍边缘的细节收益要打折扣（细场在 4 m 不可走格里不会给路线）。
- 换成 2 m 碰撞掩码属于独立的行为改动，会影响冻结测试、大体型探针和窄口，列为可选的 N6，**不在本次范围内**。

### 3.7 本地命令

- `LocalOrderPlan` 的同步 CPU 解算保留，用作种子；槽位分配和到达规则不变。
- 每个槽位的细场层使用该槽位半径对应的 `NavEdge[class]`。
- `MaxCells=65536` 继续只约束粗缓冲；细缓冲另设 `MaxFineCells = 262144`。
- 按位分组派发（partition）不变：仍然依据每个兵员的命令，与场的分辨率无关。

### 3.8 静态边代价的构建（CPU）

- 由 2 m 的 `TerrainNavigationGrid`（与现在同一套构建代码，只换分辨率）导出，打包成 half 后上传。
- 每个 clearance 类的构建时间约为 4 m 的 4 倍，具体数值**待测**。
- 为了避免"第一次下达某种体型的命令时卡一下"：开战时按名单里已有的半径预先构建所有类别。

## 4. 新增或改动的文件（预估）

| 文件 | 改动 | 阶段 |
|---|---|---|
| `MassEngine/FlowField/Shaders/GpuNavField.compute`（新） | Seed / Relax / Crowd / Clear 四个 kernel | N1、N2、N4 |
| `MassEngine/FlowField/GpuNavField.cs`（新） | 层分配、轮转预算、种子上传、统计 | N1、N2 |
| `MassEngine/FlowField/GpuNavFieldReference.cs`（新） | 托管参考实现，用于测试 | N1 |
| `RuntimeFlowConfig.cs` | `gpuNavFieldEnabled`（默认关）、`gpuNavCellSize=2`、`passesPerFrame`、`seedPasses`、`crowdGain`、`crowdCostCap` | N1、N4 |
| `TerrainNavigationRuntime.cs` | Job 额外输出 `distances`，附加上传；不改变方向结果 | N2 |
| `LocalOrderChannel.cs` / `LocalOrderPlan.cs` | 细场层的生命周期、种子 | N2、N3 |
| `AgentDataCommon.hlsl` | `SampleFlowDirection` 的梯度分支（受 uniform 控制） | N3 |
| 调试视图 | 细场热力图叠加，复用现有 flow preview 通道 | N1 |

## 5. 显存（1024 地图，512²）

| 场景 | 距离层 | 边代价 | 人群 | 合计 |
|---|---|---|---|---|
| 典型：2 队、2 个活动槽位、2 个体型类 | 8 MB | 4 MB | 2 MB | 约 14 MB |
| 最坏：4 队、9 个槽位、10 个体型类 | 26 MB | 20 MB | 8 MB | 约 54 MB |

现有缓冲保持不变，旧路径仍需要它们。删除旧路径后可以回收：本地命令约 7 MB，方向场约 2 MB。如果改用 R16 距离，层的占用减半，但长路径精度不够，**不推荐**。

## 6. 阶段与验收（每阶段你在 Editor 验收后才进入下一阶段）

| 阶段 | 内容 | 兵员行为 | 验收 |
|---|---|---|---|
| **N0b** | 测量现状基线（需要你同意 Play） | 不变 | 记录：<br>- 解算耗时（`LastSolve`/`MaxSolve`）<br>- 条纹统计<br>- 命令时延<br>- GPU 帧时间 |
| **N1** | GPU 细场旁路运行，无种子；仅调试视图 | 不变 | 静态目标收敛后与托管参考逐位一致；与 CPU 粗场的距离差 < 1 个粗格；测出 GPU 每遍耗时 |
| **N2** | CPU 粗场种子加双缓冲切换 | 不变 | 新命令后 ≤ 2 帧细场全图有效（可达格无 1e30）；动态层切换时没有可见跳变 |
| **N3** | 兵员切到细场（开关），关闭 LaneApproach33/1024 | **改变** | Move/Retreat/Attack 与现状对照；条纹指标为"相邻格方向夹角 > 30° 的比例"；ForestHills 56 m 山口；到达不穿插 |
| **N4** | 人群代价 | **改变** | 被前方友军堵住时向两侧分流；在 Annihilation 场景中观察是否来回摆 |
| **N5** | 回头借鉴 | 改变 | 逐项验收：<br>- 队伍命令改为编队线目标；<br>- 行军侧溢、拥堵等待重新调参；<br>- 删除不再需要的 LaneApproach |
| 收尾 | 删除旧方向场路径 | — | 你确认后再删 |

可选，不在范围内：
- N6：碰撞掩码改为 2 m；
- 按体型区分的场：与暂缓中的 C3b 相关，**仍暂缓**。

## 7. 风险

1. **人群代价振荡**：两条路线轮流变挤。对策是 EMA、上限、小增益；N4 单独验收，必要时关闭。
2. **种子误差**：在粗细掩码差异大的地方（窄口、障碍边缘），收敛需要更多遍。对策是加大 `seedPasses`，统计"每遍改变的格子数"作为收敛指标。
3. **GPU 耗时未知**：N1 先测。如果超出预算，可以减少每帧遍数、只松弛有兵员的区域（UEBS2 的窗口做法），或改用 R16。
4. **CPU 构建 2 m 网格**：开战加载变慢，按体型类数线性增长。N1 测出后再决定是否异步构建。
5. **测试面广**：
   - 开关关闭时，现有冻结测试必须逐位不变；
   - 开关打开时要新写参考；
   - N3 起生产着色器有改动，提交时同步 14 个 P9 拷贝；
   - 只跑过滤后的测试，不跑全量 PlayMode。
6. **Editor 稳定性**：连续多次 Play 容易崩溃，每阶段的验收会话尽量少。

## 8. 待你确认的点

1. **N0b 基线测量**：需要 1–2 次 Play 会话（Annihilation 加 ForestHills）。是否同意？
2. **人群代价只算本队**（推荐），还是算所有人？可以留到 N4 再定。
3. **碰撞掩码在 N1–N5 期间保持 4 m**（推荐），2 m 碰撞放到可选的 N6。

批准本文档后，从 N0b（如果同意）或 N1 开始。
