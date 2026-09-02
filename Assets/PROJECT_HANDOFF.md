# Ling GPU PhysicsEngine / WarSandbox 接力文档

更新时间：2026-08-16（Asia/Shanghai）  
工作区：`E:\GitHub\Ling_GPU_PhysicsEngine\Ling_GPU_PhysicsEngine\Assets`  
当前分支：`codex/WarSandbox-scenario-presets`  
当前 HEAD：`6f11290`（与 `main`、`origin/main` 一致，PR #12 已合并）

## 1. 用户目标与项目定位

项目正在把 Unity MassEngine 做成可承载 1 万～40 万单位的 GPU 大规模战争沙盒。用户当前认可的方向是：

- 继续参考 UEBS2 的高层流程：规则布阵、低拥堵接战、保持导航意图、友军密度分离、目标负载均衡。
- 不复制 UEBS2 反编译代码，只借鉴数据流、职责拆分和行为结果。参考目录曾指定为：
  `F:\Game\Ultimate.Epic.Battle.Simulator.Build.17640858\Ultimate.Epic.Battle.Simulator.Build.17640858\Ultimate.Epic.Battle.Simulator.Build.17640858\_analysis\decompiled-source`。
- 保持 GPU 驱动和 20 万级规模，不用逐单位 CPU AI 换取效果。
- 用户会在 Unity Editor 中实际验证；模型完成实现后应给出简短、明确的观察清单。

## 2. 已完成并合入 main 的主要阶段

以下内容已经通过历史提交或 PR 合入，不应重复实现：

- MassEngine Stage 1～7：GPU 空间哈希、动态流场、战斗/伤害、LOD/VAT、遥测、生命周期恢复、TDR/渲染/API 防护。
- WarSandbox 基础闭环：部署、开战、暂停、倍速、重开、歼灭结算、响应式 HUD、规模预设和 Auto-Fit。
- 相机：俯仰角重入修复、滚轮/Shift 速度保护、战术聚焦，以及跟随存活群体质心和范围的 F/F1/F2/F3。
- PR #7：实时战术小地图。
- PR #8：战斗结算报告，以及“少量幸存防守方无法被发现/击杀”的寻敌修复。
- PR #9：通过小地图下达移动命令。
- PR #10：Shift 追加多航点路线。
- PR #11：据点战、双方占领和结算。
- PR #12：静态障碍和导航绕行。

相关历史提交（由新到旧）：

- `6f11290` Merge PR #12 / `71f1abf` static obstacle navigation
- `65edd9f` Merge PR #11 / `fe664c1` control point battle mode
- `903dcd9` Merge PR #10 / `e120ab5` waypoint routes
- `9b31470` Merge PR #9 / `50edd2d` minimap orders
- `5a3efb7` Merge PR #8 / `71e6d17`, `049cc20` battle report and endgame targeting
- `3dc9d2e` Merge PR #7 / `0b969a0` tactical minimap
- `bd8cb5b` Merge PR #6 / `532a991` live camera follow
- `46bbb49` Merge PR #5 / Stage 7 and validated WarSandbox tuning

## 3. 当前未提交改动——绝对不要丢弃

当前工作区不是干净的，包含两组尚未提交的完整功能，以及用户实测参数。后续模型必须先检查 diff，不能 reset、checkout 或覆盖。

### A. WarSandbox 可复用战场方案

当前分支名称对应此功能，但 HEAD 尚无该功能提交。现有未提交文件：

- `Game/Scripts/WarSandboxScenarioPreset.cs`（新文件及 `.meta`）
- `Game/Editor/WarSandboxScenarioPresetAuthoring.cs`（新文件及 `.meta`）
- `Game/Tests/EditMode/WarSandboxScenarioPresetTests.cs`（新文件及 `.meta`）
- `Game/Editor/WarSandboxEditorWindow.cs`
- `Game/README.md`

功能意图：保存/载入完整战场快照，包括 Scenario/System 引用、兵种部署、Simulation、RuntimeFlow、RuntimeCombat、游戏模式、据点和静态障碍；应用支持 Undo，Play Mode 禁止操作。

这组代码已有 Capture/Apply 往返测试，但本接力会话没有在真实 Unity Editor 中重新运行。

### B. UEBS2 思路的接战与拥堵改造

现有未提交实现：

1. **规则布阵**：`MassEngine/UnitTypes/DefaultSpawnModule.cs`
   - 随机矩形散点改为确定性行列阵列。
   - 最后一行居中，加入少量确定性微扰。
   - 保持自动阵型面积、宽深比和可复现性。

2. **友军密度分离**：总密度图之外新增攻方/守方独立密度图。
   - `MassGpuBufferManager.cs`
   - `MassGpuShaderPropertyIds.cs`
   - `ComputePipelineOrchestrator.cs`
   - `AgentDataCommon.hlsl`
   - `AgentCombatSimulation.compute`
   - 敌军不再被当作需要躲避的密度墙。

3. **追击保留导航方向**：直接追击和队伍流场按距离混合；远处更多保留导航，接战前更多朝交战点收敛。

4. **交战槽位占用反馈**：
   - 每个目标固定 8 个环形槽位。
   - `engagementSlotAssignmentBuffer` 保存单位当前目标/槽位。
   - `engagementSlotOccupancyBuffer` 使用 24 位帧戳 + 8 位饱和计数，避免每帧清空 `agentCount × 8`。
   - 单位优先保持当前槽位；槽位明显拥挤才切换。
   - 20 万单位新增 GPU 内存约 7.2 MB（占用 6.4 MB + 分配 0.8 MB）。

5. **局部拥堵导航代价**：每帧比较前方、左前、右前的友军密度，在不重建整张流场的情况下偏向低负载通道。

6. **接触分离顺序修复**：原分离速度在攻击强阻尼前加入，LOD 降频时可能被清零；现改为状态速度求解后加入。接触限速从 8% 提至 18%，允许重叠逐渐释放。

7. **D3D11 UAV 上限修复（2026-08-16）**：战斗 Kernel 曾因 10 个 UAV 超过 D3D11.0 的 8 个上限而被 Unity 跳过。流场方向和槽位占用在模拟阶段改用 SRV 只读别名，生产 Kernel 仍使用原 RW 缓冲；离线资源表确认 `SimulateCombatAndAccumulateDamage` 现为 7 个 UAV。

涉及的其余文件：

- `MassEngine/Core/CombatBufferSet.cs`
- `MassEngine/Core/MassGpuShaderSet.cs`
- `MassEngine/Core/README.md`
- `MassEngine/Simulation/README.md`
- `MassEngine/Tests/EditMode/MassEnginePropertyTests.cs`
- `MassEngine/Tests/PlayMode/MassEngineGpuKernelTests.cs`

### C. 当前用户调出的资产参数

这些数值是工作区现状，不得擅自还原：

- 双方各 50,000 人。
- 出生中心约为 X = -136.8034 / +136.8034。
- `SimulationConfig.simulationWorldSize = 720 × 720`。
- `RuntimeFlowConfig.flowFieldCellSize = 3`，origin = `(-360, -360)`。
- `maxAgentsPerCell = 18` 保持不变。

对应文件：

- `Game/Settings/AttackerUnitConfig_Spawn.asset`
- `Game/Settings/DefenderUnitConfig_Spawn.asset`
- `Game/Settings/RuntimeFlowConfig.asset`
- `Game/Settings/SimulationConfig.asset`

## 4. 最近验证状态

上一模型完成过以下验证：

- `dotnet build MassEngine.Tests.csproj --no-restore -v:minimal`：0 警告、0 错误。
- `dotnet build MassEngine.PlayModeTests.csproj --no-restore -v:minimal`：0 警告、0 错误。
- 使用 Windows Kits `fxc` 编译战斗 Compute Shader 的 5 个 kernel：全部成功。
  - `ClearPendingDamage`
  - `ClearDensityMap`
  - `BuildDensityMap`
  - `BuildEngagementSlotOccupancy`
  - `SimulateCombatAndAccumulateDamage`
- `git diff --check`：通过，仅有 Git 的 LF→CRLF 提示。
- 2026-08-16 资源绑定复查：`SimulateCombatAndAccumulateDamage` 的 D3D11 资源表为 17 个 SRV、7 个 UAV，已低于 8 UAV 上限；两个测试 csproj 再次串行构建为 0 警告、0 错误。

注意：

- 最后一轮机器上没有找到匹配的 Unity Editor，因此新增 GPU PlayMode 测试是“已编译但未在 Unity Test Runner 实跑”。
- `fxc` 无法读取原本带 UTF-8 BOM 的 `AgentSpatialHash.compute` 和 `AgentRuntimeFlow.compute`，会在首字符报错；不要把它误判为本轮 HLSL 逻辑错误，也不要仅为迁就旧 `fxc` 批量改编码。
- 不要并行构建两个测试 csproj；它们会竞争同一个 `obj/Debug/MassEngine.dll`。应串行运行。

## 5. 当前已知限制和风险

- **目标负载已参与寻敌选择，但尚未完成 Unity 实测。**现在会在空间哈希邻域内按距离、上一帧槽位总负载和稳定偏好评分；当前目标过载且候选显著更优时才切换。
- 槽位占用是上一帧分配的反馈，存在一帧延迟，这是为避免昂贵同步而接受的设计。
- 槽位固定为 8 个，尚未按目标半径、攻击者半径或攻击距离动态扩缩。
- 局部密度导航是三方向采样覆盖，不是完整的动态 integration field；普通流场行军在强障碍附近仍需实测侧移是否会切角。
- 当前友军密度实现明确面向两个阵营；新增第三阵营前必须扩展纹理/索引策略。
- `MassEngine/Crowd/README.md` 仍写着“接战状态避让 ×0.35”，但代码已经改成友军独立密度并保持完整避让；这是待修正文档项。
- 用户此前评价普通“移动命令”略显鸡肋，功能可用但交互和战术价值仍可提升。

## 6. 目标负载均衡完成情况与下一步验证

用户在本次交接前已同意继续做：**目标负载均衡**；本轮已完成代码实现和离线验证。

本轮已实现：

1. 复用当前 8 槽位占用之和，得到每个敌人的上一帧锁定负载，没有新增大缓冲。
2. 本地寻敌使用“归一化距离 + 负载惩罚 + 稳定单位/目标偏好”评分。
3. 负载容量按目标/攻击者半径和攻击距离估算，并限制在 1～8 个可用槽位。
4. 保留残局兜底：附近只剩一个敌人时，即使负载过高也继续允许锁定。
5. 目标切换使用既有搜索节拍和 0.18 评分迟滞，降低来回换目标。
6. 新增真实 GPU 测试：
   - 多个攻击者面对两个防守者时，锁定应分布而非全部集中。
   - 只剩一个防守者时仍能被发现并击杀。
   - 既有 LOD 黄金测试继续覆盖不同决策间隔下的 DPS 与击杀节奏。

下一步验证顺序：

1. 在 Unity `6000.3.14f1` 中重新导入 Shader，清空 Console 后 Play，确认不再出现旧 UAV 报错。
2. 运行新增 GPU 测试；重点验证双目标分流、单目标残局和不同 LOD 下目标稳定性。
3. 先做 8～128 单位黄金测试，再让用户测试 10k/50k/100k/200k。

性能边界：不得对每个单位扫描全体敌人；只能在现有空间哈希邻域候选中加入 O(1) 的负载评分。

## 7. 推荐的人工验证清单

后续每轮接战/导航修改完成后，请用户重点观察：

1. 多人围攻单体时是否绕向不同方向，是否发生持续绕圈或左右抖动。
2. 前排堵塞后，后排是否走相邻低密度通道，而不是全部灌入同一缝隙。
3. 目标死亡后是否快速换到仍存活敌人。
4. 残局零星单位是否能正常被发现、命中并触发结算。
5. 静态障碍附近是否因局部密度偏转出现切墙/卡墙。
6. 1×、2×、4× 和近/中/远 LOD 下行为是否一致。
7. 10k、50k、100k、200k 的帧率、GRID OVERFLOW 和视觉拥挤程度。

## 8. 后续模型的工作纪律

- 开始前读本文件、`AGENTS.md`、`AI_CHANGELOG.md` 和 `git status --short`。
- 每完成一个逻辑改动批次，立即向 `AI_CHANGELOG.md` 追加记录。
- 报告验证时区分：C# 编译、Shader 离线编译、Unity Test Runner、用户视觉验证；不得把“编译通过”写成“运行行为已验证”。
- 保留用户资产调参和无关工作；不要批量格式化或修复行尾。
- 需要提交/推送时，先确认应如何拆分当前两组未提交功能，禁止把所有脏改动未经说明塞进一个提交。
