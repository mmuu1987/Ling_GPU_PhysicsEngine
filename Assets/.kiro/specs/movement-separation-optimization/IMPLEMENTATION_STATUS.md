# Movement Separation Optimization - 实现状态

**状态更新时间**: 2026-09-02  
**规格文档**: tasks.md  
**架构迭代**: Stage6 → Stage7 架构重构后，部分功能已在新架构中实现

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

## ❌ 未实现功能

### 1. Separation 跳帧执行
**规格**: tasks.md § 3.5  
**状态**: 配置参数 `separationSkipInterval` 未添加，跳帧逻辑未实现  
**影响**: 每帧都执行完整分离计算，大规模场景下有性能优化空间

### 2. Wander 微随机偏移
**规格**: tasks.md § 3.6  
**状态**: 配置参数 `wanderMaxAngle` 未添加，LCG hash 与旋转逻辑未实现  
**影响**: 单位行进轨迹缺少微抖动，整齐划一感较强

---

## 下一步行动

### 选项 A：归档规格（推荐）
1. 将 tasks.md 重命名为 `tasks-archived.md`
2. 添加本文档 `IMPLEMENTATION_STATUS.md` 说明"密度系统已实现，跳帧与 wander 未实现"
3. 理由：密度系统已落地且工作良好，跳帧与 wander 可作为性能/体验优化项单独立项

### 选项 B：补全剩余功能
1. 实现 `separationSkipInterval` 配置与跳帧逻辑（tasks.md § 3.5）
2. 实现 `wanderMaxAngle` 配置与 wander 函数（tasks.md § 3.6）
3. 补全对应测试（tasks.md § 7.3 ~ 7.5）

### 选项 C：修订规格
1. 更新 tasks.md，勾选已实现的密度系统相关 checkbox
2. 将 separation 跳帧与 wander 拆分为独立规格文档
3. 添加实际实现与规格差异的说明

---

## 参考文档

- [MassEngine/Crowd/README.md](../../Crowd/README.md) - 已同步修正"接战衰减"过时描述
- [MassEngine/Simulation/README.md](../../Simulation/README.md) - 战斗与运动主 kernel 说明
- [Game/PerformanceBaseline.md](../../../Game/PerformanceBaseline.md) - 当前性能基线
