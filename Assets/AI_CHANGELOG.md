# AI 改动记录（只追加）

本文件是大模型接力的操作账本。任何代码、Shader、配置、场景或文档改动都必须在完成逻辑批次后立即追加。
不要删除或重写旧记录；需要纠正时，追加一条“更正”。

## 记录模板

```text
## YYYY-MM-DD HH:mm — 模型/会话标识 — 简短目标

- 修改文件：
  - path/to/file
- 行为变化：
  - ...
- 验证：
  - [通过/失败/未运行] 命令或人工测试
- 尚未验证/风险：
  - ...
- 下一步：
  - ...
```

## 2026-08-15 — Codex — UEBS2 思路的规则布阵、槽位、追击导航和友军密度

- 修改文件：
  - `MassEngine/UnitTypes/DefaultSpawnModule.cs`
  - `MassEngine/Core/MassGpuBufferManager.cs`
  - `MassEngine/Core/MassGpuShaderPropertyIds.cs`
  - `MassEngine/Core/ComputePipelineOrchestrator.cs`
  - `MassEngine/Core/Shaders/AgentDataCommon.hlsl`
  - `MassEngine/Simulation/Shaders/AgentCombatSimulation.compute`
  - `MassEngine/Tests/EditMode/MassEnginePropertyTests.cs`
  - `MassEngine/Tests/PlayMode/MassEngineGpuKernelTests.cs`
  - `MassEngine/Simulation/README.md`
- 行为变化：
  - 出生改为确定性规则阵列、末行居中和小幅微扰。
  - 攻守双方使用独立友军密度图，敌军不再充当密度避让墙。
  - 追击方向与流场方向按接敌距离混合。
  - 增加确定性环形接战点；分离修正移到强阻尼之后，接触限速提高到 18%。
- 验证：
  - [通过] EditMode/PlayMode csproj 串行构建，0 警告、0 错误。
  - [通过] 战斗 4 个 Compute kernel 使用 `fxc` 离线编译。
  - [通过] `git diff --check`。
  - [未运行] Unity Editor GPU/视觉行为验证。
- 尚未验证/风险：
  - 初版槽位只是确定性分配，没有真实占用反馈。
- 下一步：
  - 加入带占用感知的槽位，以及局部友军密度导航代价。

## 2026-08-15 — Codex — 槽位占用反馈和局部拥堵导航

- 修改文件：
  - `MassEngine/Core/CombatBufferSet.cs`
  - `MassEngine/Core/MassGpuBufferManager.cs`
  - `MassEngine/Core/MassGpuShaderPropertyIds.cs`
  - `MassEngine/Core/MassGpuShaderSet.cs`
  - `MassEngine/Core/ComputePipelineOrchestrator.cs`
  - `MassEngine/Core/Shaders/AgentDataCommon.hlsl`
  - `MassEngine/Simulation/Shaders/AgentCombatSimulation.compute`
  - `MassEngine/Tests/EditMode/MassEnginePropertyTests.cs`
  - `MassEngine/Tests/PlayMode/MassEngineGpuKernelTests.cs`
  - `MassEngine/Core/README.md`
  - `MassEngine/Simulation/README.md`
- 行为变化：
  - 每个目标新增 8 个带帧戳的槽位占用计数和每单位槽位分配。
  - 槽位明显过载时换位，并用迟滞维持稳定槽位。
  - 友军密度作为前/左前/右前的局部导航成本，不强制全流场重建。
  - 新增 GPU 测试契约：4 个攻击者挤同一槽位后至少一个被重定向。
- 验证：
  - [通过] EditMode/PlayMode csproj 串行构建，0 警告、0 错误。
  - [通过] 战斗 5 个 Compute kernel 使用 `fxc` 离线编译；去除了整数除法性能警告。
  - [通过] `git diff --check`。
  - [未运行] Unity Test Runner 和大规模视觉验证。
- 尚未验证/风险：
  - 槽位只有目标内部分流，目标选择仍不看总体负载。
- 下一步：
  - 在空间哈希邻域寻敌评分中加入目标负载与目标黏性，并保留残局兜底。

## 2026-08-16 — Codex — 建立跨模型接力文档和强制记录制度

- 修改文件：
  - `AGENTS.md`
  - `PROJECT_HANDOFF.md`
  - `AI_CHANGELOG.md`
- 行为变化：
  - 无运行时代码变化。
  - 后续模型被明确要求在每个逻辑改动批次后追加本文件，并同步接力状态。
- 验证：
  - [通过] 根据 Git 状态、最近 30 条提交和现有 README 核对当前分支、已合入阶段及未提交文件。
- 尚未验证/风险：
  - 当前两组功能仍未提交，Unity Editor 行为测试仍需后续模型/用户执行。
- 下一步：
  - 实施目标负载均衡；实施前先保全当前工作区并重新跑基线验证。

## 2026-08-16 13:00 — Codex — 修复 D3D11 战斗 Kernel 超出 UAV 上限

- 修改文件：
  - `MassEngine/Core/Shaders/AgentDataCommon.hlsl`
  - `MassEngine/Simulation/Shaders/AgentCombatSimulation.compute`
  - `MassEngine/Core/MassGpuShaderPropertyIds.cs`
  - `MassEngine/Core/ComputePipelineOrchestrator.cs`
  - `PROJECT_HANDOFF.md`
  - `AI_CHANGELOG.md`
- 行为变化：
  - `SimulateCombatAndAccumulateDamage` 不再把只读的攻方流场、守方流场和槽位占用绑定为 UAV。
  - 新增对应 `StructuredBuffer` SRV 别名；生成流场和累计槽位的 Kernel 仍使用原 `RWStructuredBuffer`，数据布局和算法不变。
  - 战斗 Kernel 的 UAV 数量从 10 降为 7，Unity D3D11.0 不再需要跳过 Dispatch。
- 验证：
  - [通过] 读取 Unity `Editor.log` 定位原始错误：10 UAV 超过 D3D11.0 的 8 UAV 上限，Dispatch 被跳过。
  - [通过] `fxc` 资源绑定表：17 个 SRV、7 个 UAV（`u0`～`u6`）。
  - [通过] 全部 5 个战斗 Compute Kernel 离线编译。
  - [通过] `MassEngine.Tests.csproj` 与 `MassEngine.PlayModeTests.csproj` 串行构建，0 警告、0 错误。
- 尚未验证/风险：
  - [待用户/Unity] 当前 Unity 会话需要完成资源重新导入并重新进入 Play Mode，确认 Console 不再出现旧的 10 UAV 报错。
- 下一步：
  - 清空 Console 后重新 Play 一次；确认战斗计时、移动和伤害继续更新，再恢复目标负载均衡工作。

## 2026-08-16 — Codex — 实施目标负载均衡与残局兜底

- 修改文件：
  - `MassEngine/Core/Shaders/AgentDataCommon.hlsl`
  - `MassEngine/Simulation/Shaders/AgentCombatSimulation.compute`
  - `MassEngine/Tests/PlayMode/MassEngineGpuKernelTests.cs`
  - `MassEngine/Core/README.md`
  - `MassEngine/Simulation/README.md`
- 行为变化：
  - 复用每个目标 8 个交战槽位的上一帧计数，按目标总负载估算当前负载比；容量由攻击距离与双方半径估算并限制在 1～8 个槽位。
  - 空间哈希邻域寻敌从纯最近距离改为归一化距离 + 负载惩罚 + 稳定单位/目标偏好评分。
  - 只有当前目标过载、搜索处于既有节拍且候选显著更优时才换目标，保留目标黏性；候选只剩一个敌人时不因负载过高拒绝锁定。
  - 新增真实 GPU 测试契约：过载目标应把部分攻击者分流到第二目标，第一目标仍保留部分锁定；一个目标死亡后唯一幸存目标仍必须可被锁定。
- 验证：
  - [通过] `dotnet build MassEngine.Tests.csproj --no-restore -v:minimal`，0 警告、0 错误。
  - [通过] `dotnet build MassEngine.PlayModeTests.csproj --no-restore -v:minimal`，0 警告、0 错误。
  - [通过] `fxc` 离线编译战斗 5 个 kernel：`ClearPendingDamage`、`ClearDensityMap`、`BuildDensityMap`、`BuildEngagementSlotOccupancy`、`SimulateCombatAndAccumulateDamage`。
  - [通过] `SimulateCombatAndAccumulateDamage` 离线资源表仍为 7 个 UAV，未重新触发 D3D11.0 的 8 UAV 上限。
  - [通过] `git diff --check`；仅有已有的 LF→CRLF 提示。
- 尚未验证/风险：
  - [未运行] Unity Test Runner GPU 测试；本机未发现项目要求的 Unity `6000.3.14f1` Editor。
  - 负载反馈仍有一帧延迟；稳定偏好用于抑制同步换目标，实际 10k～200k 视觉分布和性能需在 Unity 中观察。
- 下一步：
  - 在 Unity 中重新导入 Shader，清空 Console 后 Play；运行新增目标负载 GPU 测试并观察多目标分流、残局击杀、LOD 和 10k/50k/100k/200k 性能。

## 2026-08-17 22:56 — Codex — 输出 reverse-skill 仓库分析文档

- 修改文件：
  - `E:\GitHub\reverse-skill-analysis.md`
  - `AI_CHANGELOG.md`
- 行为变化：
  - 无运行时代码、Shader、配置或场景变化。
  - 新增一份独立 Markdown 报告，整理 reverse-skill 的定位、路由实现、模块结构、Codex 使用方式、自动安装行为、供应链风险和验证边界。
- 验证：
  - [通过] 确认目标文件此前不存在，并使用只读检查核对报告引用的仓库结构与官方 Codex 文档链接。
  - [待完成] 写入后检查文件存在、标题和 Git 工作区状态。
- 尚未验证/风险：
  - reverse-skill 自带完整路由回归测试在本次分析环境中超过两分钟未完成；报告已明确标注该限制。
- 下一步：
  - 检查生成文件可读性；如后续要实际接入 reverse-skill，应先决定使用独立工作区、项目外部知识库或精选原生技能方案。

## 2026-08-17 22:58 — Codex — 更正 reverse-skill 报告验证状态

- 修改文件：
  - `AI_CHANGELOG.md`
- 行为变化：
  - 更正上一条记录中的待验证状态；报告文件已经成功写入并完成可读性检查。
- 验证：
  - [通过] `E:\GitHub\reverse-skill-analysis.md` 存在，大小 12,476 字节，UTF-8 中文标题和正文可正常读取。
  - [通过] `git status --short` 复查；除新增的日志记录外，未改变或丢弃 Unity 工作区既有未提交内容。
- 尚未验证/风险：
  - 无新增风险；reverse-skill 自带完整回归测试的超时限制仍以报告正文为准。
- 下一步：
  - 无。

## 2026-08-29 — Antigravity — 创设海量怪潮 GPU 塔防新工程并融合 UEBS2 核心数据流

- 新建工程目录：`E:\GitHub\Ling_GPU_TowerDefense`
- 迁移/新增文件：
  - `MassEngine/` 核心底层完整迁移（Core/Spatial/FlowField/Simulation/VatRender/UnitTypes）及 Tiny Hero 角色 VAT 动画资产。
  - `MassEngine/Projectiles/ProjectileGpuData.cs`、`ProjectileGpuManager.cs` 与 `ProjectileSimulation.compute`（借鉴 UEBS2 ProjectileManage 架构的 GPU 弹道管线与 AoE 溅射命中）。
  - `MassEngine/Spawner/WaveAgentSpawnerGpu.cs` 与 `SpawnInactive.compute`（借鉴 UEBS2 GPUAI_SpawnInactive 的零分配波次休眠与毫秒级唤醒）。
  - `TowerDefense/Scripts/Core/BaseCore.cs` 与 `TDGameManager.cs`（基地核心生命、胜负判定与全系统协调）。
  - `TowerDefense/Scripts/Economy/EconomyManager.cs`（金币经济与击杀奖励）。
  - `TowerDefense/Scripts/Towers/`（`TowerBase`、`MachineGunTower`、`CannonTower`、`LaserTower`、`WallObstacle` 防御塔体系）。
  - `TowerDefense/Scripts/Waves/WaveDataSO.cs` 与 `WaveController.cs`（四级波次配置与多路出兵调度）。
  - `TowerDefense/Scripts/Building/GridPlacementController.cs`（网格吸附、建造预览与静态障碍物同步）。
  - `TowerDefense/Scripts/UI/TDHUDController.cs`（响应式运行时 IMGUI 战术监控与建造操作界面）。
  - `TowerDefense/Scripts/Editor/TowerDefenseSampleSceneCreator.cs`（菜单一键生成演示场景与配置）。
  - `README.md`（新工程架构、操作与运行指南）。
- 行为变化：
  - 成功独立构建基于 Unity 6 URP 的纯 GPU 10万级海量怪潮塔防工程。
  - 实现了基于 GPU 的千发弹幕物理与空间哈希碰撞、重型火炮 AoE 爆炸、零 GC 动态刷怪与玩家迷宫建造。
- 验证：
  - [通过] Windows Kits `fxc.exe` 对所有新增 Compute Kernel（`SimulateProjectiles`、`ClearProjectiles`、`SpawnInactiveAgents`）进行 Direct3D CS 5.0 离线编译，全部成功，0 错误 0 警告。
  - [通过] Unity 6 (6000.3.14f1) 路径探测与 Package/ProjectSettings 依赖配平。
- 尚未验证/风险：
  - 需在 Unity Editor 中实际打开 `E:\GitHub\Ling_GPU_TowerDefense` 点击菜单生成并 Play 验证视觉与粒子调优。
- 下一步：
  - 用户打开新工程并在 Unity 中运行验证体验。

