# Movement Separation Optimization - 实现状态

**状态更新时间**: 2026-09-03  
**规格文档**: tasks.md  
**架构迭代**: Stage6 → Stage7 架构重构后，部分功能已在新架构中实现  
**决策状态**: 密度系统已实现，separation 跳帧和 wander 已登记为放弃

---

## 实现状态总览

| 功能模块 | 规格状态 | 实际状态 | 备注 |
|---|---|---|---|
| **Density Map 拥挤回避** | 已规划 | ✅ **已实现** | 友军独立密度图系统 |
| **Separation 跳帧执行** | 已规划 | ❌ **未实现** | 规格存在但代码未落地 |
| **Wander 微随机偏移** | 已规划 | ❌ **未实现** | 规格存在但代码未落地 |

---

## ✅ 已实现功能（Density Map）

### 配置层
- `FlockingConfig.densityAvoidanceStrength` ([MassEngine/Crowd/FlockingConfig.cs:11](../../Crowd/FlockingConfig.cs))
- `FlockingConfig.densityComfortPerSqm` / `densityPressureRangePerSqm` / `densitySpeedPenalty`

### GPU 层
- `ClearDensityMap` kernel ([MassEngine/Simulation/Shaders/AgentCombatSimulation.compute:2](../../Simulation/Shaders/AgentCombatSimulation.compute))
- `BuildDensityMap` kernel ([MassEngine/Simulation/Shaders/AgentCombatSimulation.compute:3](../../Simulation/Shaders/AgentCombatSimulation.compute))
- `ComputeDensityPressure` 函数 ([MassEngine/Core/Shaders/AgentDataCommon.hlsl:677](../../Core/Shaders/AgentDataCommon.hlsl))

### 实现差异
规格文档基于 Stage6 架构编写，实际实现在 Stage7 架构重构后完成：
- **友军独立密度图**：引擎只统计友军密度（敌我不互为密度约束），简化了规格中的"接战衰减"逻辑
- **密度压力模型**：采用 comfort + pressure range 的双参数模型，而非规格中的单一梯度
- **集成方式**：密度避让已集成进主 kernel，但**不是**规格描述的独立 `ComputeDensityAvoidanceForce` 函数形式

---

## ❌ 未实现功能（已登记为放弃）

### 1. Separation 跳帧执行
**规格**: tasks.md § 3.5  
**状态**: ❌ **已登记为放弃**  
**原因**: 
- 当前性能基线已达标（10万/边 113 FPS，20万/边 30 FPS，40万/边 12 FPS）
- 跳帧优化的性能收益相对有限（估计 5-10% FPS 提升）
- 增加实现和测试复杂度，对战斗逻辑引入新的帧间依赖
- 若未来性能瓶颈明确定位在 separation 计算，可重新评估

**配置参数**: `separationSkipInterval` 未添加  
**代码**: 跳帧逻辑未实现  
**全代码库搜索**: `grep -r "separationSkipInterval"` 零命中（已验证 2026-09-03）

### 2. Wander 微随机偏移
**规格**: tasks.md § 3.6  
**状态**: ❌ **已登记为放弃**  
**原因**:
- 当前规模下单位行进轨迹整齐度可接受
- wander 为体验优化项，非核心功能
- 大规模场景下（10万+单位）视觉上已有足够的混沌感
- 若未来有明确的游戏性/视觉需求，可作为独立体验优化项实施

**配置参数**: `wanderMaxAngle` 未添加  
**代码**: `WanderAngle` 和 `ApplyWander` 函数未实现  
**全代码库搜索**: `grep -r "wanderMaxAngle"` 零命中（已验证 2026-09-03）

---

## 下一步行动

### ✅ 已完成：归档规格并明确状态
1. ✅ tasks.md 已更新，勾选所有已实现的密度系统任务
2. ✅ 明确标注 separation 跳帧和 wander 为"已登记为放弃"
3. ✅ IMPLEMENTATION_STATUS.md 已更新，补充放弃原因和验证记录
4. ✅ 全代码库验证：`separationSkipInterval` 和 `wanderMaxAngle` 零命中

### 后续建议
- 若未来性能出现瓶颈，优先使用 profiler 定位具体热点，再决定是否实施 separation 跳帧
- 若游戏性设计需要更自然的单位行为，可将 wander 作为独立体验优化项重新评估
- 密度系统当前工作良好，无需进一步改动

---

## 参考文档

- [MassEngine/Crowd/README.md](../../Crowd/README.md) - 已同步修正"接战衰减"过时描述
- [MassEngine/Simulation/README.md](../../Simulation/README.md) - 战斗与运动主 kernel 说明
- [Game/PerformanceBaseline.md](../../../Game/PerformanceBaseline.md) - 当前性能基线
