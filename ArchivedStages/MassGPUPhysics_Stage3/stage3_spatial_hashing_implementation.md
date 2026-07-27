# 第三阶段 GPU 空间哈希实施方案

## 目标

第三阶段会把第二阶段的 VAT 实例化演示推进为一个纯 GPU 的群体排斥碰撞原型。Agent 仍然通过 Indirect Instancing 渲染，但每一帧会先在 GPU 上构建空间哈希，再基于邻近 Agent 做柔性的圆形碰撞排斥。

本文件夹是自包含的实施范围。第二阶段的资源和脚本不参与本阶段改动。

## 运行时流水线

1. 清空网格
   - 将每个空间格子的 `gridCounts[cell]` 重置为 `0`。
   - 网格覆盖 XZ 平面的模拟区域。

2. 构建空间哈希
   - 每个 Agent 根据自身位置计算所在的 XZ 网格坐标。
   - 使用 `InterlockedAdd` 在对应格子中申请一个写入槽位。
   - 槽位里只存 Agent 的索引，不复制完整 Agent 数据。
   - 每个格子使用固定容量 `maxAgentsPerCell`。如果某个格子过载，超出的索引会被丢弃，以保证显存占用可控。

3. 模拟碰撞并分类可见实例
   - 每个 Agent 查询自己所在格子以及周围 8 个相邻格子。
   - 邻近 Agent 被视为 XZ 平面上的圆形碰撞体。
   - 如果两者距离小于 `agentRadius * 2`，就累积一个分离速度。
   - 速度会经过阻尼、限速、位置积分，并被限制在模拟边界内。
   - 同一个 pass 继续保留第二阶段的行为：视锥剔除、LOD 分组和动画时间推进。

4. 间接绘制 LOD
   - near、mid、far 三个 Append Buffer 的计数会复制到 Indirect Draw Args。
   - 现有第三阶段材质继续读取同一个 `agentBuffer`。

## 实现说明

- `AgentData` 新增了 `velocity` 字段；所有第三阶段渲染 shader 必须使用相同的数据布局。
- `GPUInstancingManager_Stage3` 仍是 MonoBehaviour 主入口，但部分纯运行时辅助逻辑已经拆出：
  - `MassSpatialHashGridSettings_Stage3`：计算空间哈希网格分辨率、总格子数、世界尺寸和原点。
  - `MassAgentSpawnUtility_Stage3`：生成初始 `AgentData` 数组。
  - `MassGpuDrawUtility_Stage3`：创建 AppendBuffer、IndirectArguments buffer、LOD PropertyBlock、运行时 billboard mesh，并同步 VAT 材质参数。
- 空间哈希使用密集缓冲：
  - `gridCounts`：长度为 `gridCellCount` 的 uint 数组。
  - `gridAgentIndices`：长度为 `gridCellCount * maxAgentsPerCell` 的 uint 数组。
- 本阶段刻意采用固定容量网格。这样可以避免 GPU 侧动态分配，也让每帧 dispatch 更可预测。
- 模拟 pass 会原地读写 `agentBuffer`。对于当前原型，轻微的执行顺序差异可以接受。后续整合阶段如果需要更确定性的碰撞积分，可以再引入 ping-pong 双缓冲。

## Inspector 默认参数

- `cellSize`：建议接近 `agentRadius * 2`，默认值为 `2`。
- `agentRadius`：默认值为 `0.45`。
- `maxAgentsPerCell`：默认值为 `64`。如果大量 Agent 生成在很小区域内，可以调高这个值。
- `separationStrength`：控制重叠 Agent 被推开的速度。
- `velocityDamping`：用于稳定群体运动，避免排斥后持续抖动或滑行。
- `simulationWorldSize`：留空或为零时，会根据 spawn area 自动推导模拟范围。

## 验收标准

- Agent 可以从重叠或拥挤的集群中散开，并且不依赖 CPU 物理。
- Unity Profiler 中不应出现按 Agent 遍历的 CPU 循环。
- 绘制仍然使用 Indirect Draw，并继续按 near、mid、far 三档 LOD 分组。
- 第三阶段不依赖修改第二阶段文件。
