# Spatial — 空间哈希网格

所有邻域查询（寻敌、分离力）的地基。均匀网格 + 每格定长槽位，每帧全量重建。

## 数据契约

| 缓冲 | 布局 | 写入者 | 读取者 |
|---|---|---|---|
| `gridCounts` | `uint[cellCount]`，每格占用数 | `BuildSpatialHash`（原子加） | Simulation 主 kernel |
| `gridAgentIndices` | `uint[cellCount × maxAgentsPerCell]` | `BuildSpatialHash` | Simulation 主 kernel |

- 格子坐标：`cell = floor((posXZ - gridOrigin) / cellSize)`，越界钳制到边缘格。
- 只有**存活**（上帧 hp 快照 > 0）的 Agent 入格——死者自动从所有邻域查询消失（1 帧延迟）。
- 格满（超过 `maxAgentsPerCell`）时溢出的 Agent 静默丢弃本帧登记：
  拥挤战场请调大 `SimulationConfig.maxAgentsPerCell`（默认 64）。

## Kernel

`Shaders/AgentSpatialHash.compute`：`ClearGrid`（每格一线程清零）、
`BuildSpatialHash`（每 Agent 一线程，原子分配槽位）。

## 参数（SimulationConfig）

- `simulationWorldSize` / `cellSize`：决定网格分辨率 = ceil(world/cell)²
- `cellSize` 经验值：≈ 2×AgentRadius 到寻敌半径之间；太小→格数暴涨，太大→每格候选过多

## 查询模式（供其它模块参考）

`AgentDataCommon.hlsl` 的 `QueryCombatNeighborhood`：以自身格为中心扫
`(2r+1)²` 个格（分离固定 r=1，寻敌 r 由 C# 按 targetAcquireRadius/cellSize
推导、上限 4 并在超限时警告一次）。

## 如何验证

PlayMode GPU 测试依赖本模块寻敌成功（对峙阵型必然进入 Attack）；
邻域正确性由伤害/接战行为间接验证。
