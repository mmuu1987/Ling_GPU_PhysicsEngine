# 流场条纹：通用方案（距离场梯度 + 双线性采样）留档 & 当前定向修正

日期：2026-10-11　状态：**通用方案仅留档，未实施**；当前实施的是定向补丁 `TerrainLaneApproach1024`（见 §5）。

---

## 1. 现象

1024 地图（流场 256×256，格 4 m）在歼灭模式、动态追敌（Attack 无目标点）下，大军团推进时出现**4 m 宽的条纹**：一整行（或列）的单位整体斜着漂向邻行，留下空带，邻行挤成一条。512 图（格 2 m）上 LaneApproach33 已基本消除，1024 上重新出现。

## 2. 诊断证据（StripeDiagnose1024，3 次快照，Annihilation，120k 单位）

条纹格 = 动态敌方流场中**仍为斜向**（|x|、|y| 都 > 1e-4）的可走格，且位于基数方向格之间。

| 快照 | 斜向格 | (a) 轴误判 | (b) 本行/列无敌方目标 |
|---|---|---|---|
| 105022 | 261 | 86 | 175 |
| 105041 | 321 | 160 | 161 |
| 105056 | 358 | 231 | 127 |

离线复算：规则 A（两轴都试）+ 规则 B（±8 m 车道容差）可把三次快照中所有条纹格转成基数方向。截图：`snap-20261011-105022.png`（本地 `~/b39/stripe/sim/`）。

已排除：密度梯度、ConfiguredFlowTarget、Attack 中心吸附、阻挡格。

## 3. 根因

1. **Dijkstra 方向本身是 8 方向量化的**：每格方向指向代价最低的邻格中心，只有 0°/45°/90°… 八种取值。面对宽阔战线时，大片区域的"真实"方向介于两者之间，于是出现斜向/横向相间的格子。
2. **GPU 取最近格（nearest）**：`AgentDataCommon.hlsl::SampleFlowDirection` 用 `PositionToFlowFieldCell` → `floor` 取单格方向，没有插值。整行单位拿到同一个斜向值，就整行漂移。
3. **LaneApproach33 在 1024 上覆盖不全**：
   - (a) 轴由**全体敌方格质心**决定：`|toward.x| >= |toward.y|` 选横轴，否则纵轴。战线 1 km 宽时，远离质心的行被判成"纵向"，横向的同行目标就不被采用，斜向保留。
   - (b) 只认**同一行/列**的真实目标；敌方前沿参差时，很多行根本没有敌方格，于是不处理。格子从 2 m 变成 4 m 后，"本行恰好有敌人"的概率更低。

## 4. 通用方案（留档，暂不实施）

思路：不再在 8 方向量化的结果上打补丁，而是让方向场本身连续。

### 4.1 设计
1. **距离场**：沿用现有 Dijkstra，但把每格的**代价/距离**（而不仅是方向）作为输出保存，`float distance[cell]`（不可达 = +inf）。
2. **梯度方向**：每格方向 = −∇distance，用中心差分（遇阻挡/不可达邻格时退化为单侧差分，两侧都不可用时回退到原 Dijkstra 邻格方向）。可选 Eikonal / Fast Marching 替代 Dijkstra，减少 8 方向的"菱形"偏差。
3. **GPU 双线性采样**：`SampleFlowDirection` 在 4 个邻格方向间做双线性插值；任一角格不可走或方向为零时，该角权重置零并重新归一化；全部无效则回到当前最近格逻辑。
4. **保留安全回退**：现有"一格扫掠清空才用直行方向，否则走邻格中心"的规则不变。

### 4.2 涉及文件
- `Assets/MassEngine/Terrain/TerrainNavigationRuntime.cs`：`TerrainNavigationSolveJob`（输出距离）、`TerrainNavigationDynamicJob`、托管 `CreateFlowField`；新增梯度 pass（托管 + Burst 两份，逐位一致）。
- `TerrainNavigationGrid`：`CreateFlowField` 返回方向前加梯度步骤。
- `Assets/MassEngine/Core/Shaders/AgentDataCommon.hlsl`：`SampleFlowDirection` 双线性；`Combat.compute` 三处调用不变。
- `RuntimeFlowConfig.cs`：新开关（如 `terrainGradientFlow`），默认关，Flow1024 打开。
- LaneApproach33 / 1024：梯度方案开启时应关闭（它们是基数化补丁，和连续场目标冲突）。

### 4.3 风险
- **冻结参考测试**：`TerrainNavigationGridTests:1143`、`TerrainCardinalRouteCacheTests:344` 等对方向逐位比对，开关关闭时必须保持完全一致；开关打开要新写参考。
- **贴墙/窄口**：梯度在阻挡边缘会指向墙内，需要单侧差分 + 扫掠检查；窄口（森林丘陵 56 m 山口）要专门验证。
- **GPU 成本**：每单位多 3 次 buffer 读取，200k 单位约 +600k 次读，需在 Annihilation 测一次帧时间（属性能项，需用户同意再测）。
- **行为改变面大**：所有行军模式（Move/Retreat/Attack）都受影响，不是只修条纹。
- 异步一 Tick 延迟不受影响。

### 4.4 测试
- 托管 vs Burst 梯度逐位一致（沿用 `TerrainDynamicAsyncSolveTests` 的 World 夹具）。
- 开关关闭：现有冻结测试全部不变。
- 解析场景：单点目标的径向场、直线战线的平行场，误差角 < 某阈值；阻挡格周围方向不指向阻挡。
- GPU：KernelTests 增加双线性采样用例（角格无效时的重新归一化）。
- 编辑器验证：StripeDiagnose1024 斜向格统计不再有意义，改统计"相邻格方向夹角 > 30° 的比例"。

### 4.5 迁移路径
1. 加开关，默认关 → 只做距离输出 + 梯度托管实现 + 单测。
2. Burst 版 + 逐位一致测试。
3. HLSL 双线性采样（同一开关下发 shader 关键字/参数）。
4. Flow1024 打开，关闭 `terrainLaneApproach33` / `terrainLaneApproach1024`，编辑器对比条纹与窄口行为。
5. 稳定后再考虑 512 场景（已删除，未来新图默认 1024）及删除两个 LaneApproach 补丁。

## 5. 当前选择：定向补丁 `TerrainLaneApproach1024`

### 5.1 为什么现在不用通用方案
- 通用方案改变所有行军模式的方向场和 GPU 采样，影响面大、需要重建参考测试和性能测量；条纹只出现在动态追敌的斜向残留格。
- 诊断表明条纹格**全部**属于 (a)(b) 两类，定向规则离线即可全部消除。
- 坦白说：这是在 LaneApproach33 之上**再加一层补丁**，不是根治；根治是 §4。

### 5.2 规则（LaneApproach33 之后执行，33 本身不改）
- 只处理当前**斜向**的格（|x|、|y| 都 > 1e-4）；零向量、基数方向、33 已改写的格不动。
- **A 两轴都试**：候选轴 = 斜向的两个分量 H=(sign x, 0)、V=(0, sign y)，不再用全局质心选轴。
- **B 车道容差**：每个轴在偏移 0、±1 … ±K 的行（H）/列（V）里找目标，K = max(1, round(8 m / 格宽))，1024 图上 K=2。
- 目标 = 该车道里**严格位于前方**的第一个真实敌方格（同列/同行齐平的不算）。
- 合格条件（与 33 相同的安全规则）：
  - 距离 ≥ near = max(格宽×4, stopRadius + 格宽)；
  - 移动**留在本车道**：从本格到"本行∩目标列"（H）或"本列∩目标行"（V）的整条基数路线 `HasClearCardinalRoute33` 通过。
- 选择：偏移小者优先 → 距离近者优先 → 同偏移 +侧优先；轴之间同理，最后 H 优先。
- 写入对应的基数单位向量。

### 5.3 改动
- 新增 `Assets/MassEngine/Terrain/TerrainLaneApproach1024.cs`（托管）。
- `TerrainNavigationRuntime.cs`：`LaneApproach1024` 属性、构造参数（追加在最后，默认 false）；托管 `Upload` 在 33 之后调用；Burst `TerrainNavigationDynamicJob` 新增 `lane1024/laneTolerance` 和 `ApplyLane1024`，33 循环只在 `lane` 时运行（关闭 1024 时逐位不变）。
- `RuntimeFlowConfig.terrainLaneApproach1024`；`MassEngineManager.Terrain.cs` 重建条件和构造传参；`Flow1024.asset` 打开（三张 1024 图共用）。
- 测试 `Assets/MassEngine/Tests/EditMode/TerrainLaneApproach1024Tests.cs`：语义单测 + Burst 异步 vs 托管逐位一致。

### 5.4 验证
- 编辑器跑 EditMode：`TerrainLaneApproach1024Tests`、`TerrainDynamicAsyncSolveTests`、`TerrainNavigationGridTests`、`TerrainCardinalRouteCacheTests`。
- 进 1024 图歼灭模式看条纹；可再点 StripeDiagnose1024，斜向格应明显下降。
- 关掉 `Flow1024.terrainLaneApproach1024` 即恢复旧行为。
