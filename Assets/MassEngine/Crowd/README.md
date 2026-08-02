# Crowd — 聚散行为参数

大规模人群的"手感"层：分离防穿插、密度压力防死挤、车道偏置防排队僵直。

## 模块形态（诚实说明）

本模块拥有**参数与语义定义**；GPU 实现物理上位于
`Simulation/Shaders/AgentCombatSimulation.compute`（主 kernel 的聚散段）与
`Core/Shaders/AgentDataCommon.hlsl`（`ComputeDensityPressure` /
`ApplyStableLaneBias` / `QueryCombatNeighborhood` 的分离累积）。
拆成独立 pass 意味着位置/速度多读写一轮，当前不值得。

## 行为与参数（FlockingConfig，全部按兵种经 settings 通道生效）

| 参数 | 语义 |
|---|---|
| `agentRadius` | 碰撞半径；分离在两半径之和内线性增强 |
| `separationStrength` | 分离力强度（攻击接触中自动降为 0.18 倍防止推散战线） |
| `densityComfortPerSqm` / `densityPressureRangePerSqm` | 密度压力起算密度与归一化范围（**每平方米**，与流场格尺寸解耦；0.6/1.2 默认 ≈ 行军密度以上开始避让、堆积极限时打满）。接战状态避让自动衰减 ×0.35 |
| `densityAvoidanceStrength` | 沿密度梯度的避让力 + 前向拥堵侧移（**只偏转不反向**：上限 0.8×期望方向模长，后排援军不会被人堆吓得掉头跑） |
| `densitySpeedPenalty` | 拥堵减速上限（0~1） |
| `speedVariation` | 个体速度抖动（打破整齐划一） |
| `laneBiasStrength` | 稳定车道偏置（同向人流自动分股） |
| `attractionStrength` | 吸引力兜底：流场无方向且有配置目标时直线朝目标转向的权重 |

运行时改任何参数 → 下一帧生效（settings 每帧上传）。

## 如何验证

EditMode 的钳制测试覆盖非法值；行为验证靠场景目检 +
`BattleTelemetryHUD` 的平均推进观察（拥堵时兵线应变宽而不是原地死挤）。
