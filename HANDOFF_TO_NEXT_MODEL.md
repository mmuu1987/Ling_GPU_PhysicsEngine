# 交接文档：Claude Code 会话因额度中断 → 下一模型接手

> 生成时间：2026-09-01 23:45（北京时间）
> 前任：Claude Code (claude-opus-5) 会话 `56f1439d-e85c-4387-98df-8a7a6c63b83b`
> 中断原因：中转站额度耗尽（API 返回 500 "Failed to validate API key"）
> 中断时刻：正在执行「A/B 两组功能拆分提交」的第 3 步（worktree 刚建好、改动还没分发）——**仓库处于拆分中间态，接手第一件事是把 stash 里的成果分发给两个分支**
> 姊妹工程：`E:\GitHub\Ling_GPU_TowerDefense` 也有独立交接文档（`HANDOFF_TO_NEXT_MODEL.md`），那边是 4 个游戏 bug 修复待收尾

---

## 一、工程定位与文档地图

`Ling_GPU_PhysicsEngine` 是 GPU 驱动的海量单位战斗沙盘（WarSandbox），目标规模 1 万~40 万单位。两层结构：`MassEngine` 是通用引擎层（空间哈希 / 双阵营流场 / 战斗与运动主 kernel / VAT+LOD 间接绘制 / 遥测），`Game` 是产品层（障碍、指令、HUD、战场方案）。铁律：引擎不反向依赖游戏层；逐帧仿真 100% 在 GPU 上，不给单位做 CPU 逐个更新。

性能已实测封版（`Game/PerformanceBaseline.md`）：**10 万/边 113 FPS，20 万/边 30 FPS，40 万/边 12 FPS**。玩法闭环走到 PR #12（布阵/暂停/倍速/重开、战斗结算报告、小地图与指令、Shift 多路径点、据点战、静态障碍绕行、相机跟随）。

⚠️ **鸡生蛋问题**：本工程的跨模型账本文档（`Assets/AGENTS.md`、`Assets/PROJECT_HANDOFF.md`、`Assets/AI_CHANGELOG.md`）本身是**未入库文件，目前都在 stash 里**（见下）。接手后要先把 stash 恢复出来才能读到它们。恢复前以本文档为准。

## 二、会话时间线（2026-09-01 晚）

| 时间（北京） | 事件 |
|---|---|
| 22:32 | 用户："这个工程你先了解一下，md文档都读一下，看一下进度到哪里，下一步要做啥" |
| 22:42 | 完成全量文档阅读（AGENTS/HANDOFF/CHANGELOG/roadmap/.kiro specs/方案设计/各 README），产出工程总结 + **四项建议** |
| 22:53 | 用户批准："按你的建议来搞吧1234四个事项一个一个来" |
| 22:57–23:07 | 第 1 项的离线验证：fxc 编译战斗 kernel、UAV/SRV 资源清点、测试 csproj 构建；结论"Unity 实测被阻塞"（**此结论有误，见第五节**） |
| 23:09–23:12 | 第 2 项执行中：检查 A组 改动性质 → **Step 1 `git stash -u` 全部改动** → Step 2 验证工作区已清理 → **Step 3 创建 B组 worktree（成功，1179 文件检出完毕）** |
| 23:19 | 下一步请求因额度耗尽失败，会话终止。**改动分发（Step 4+）未执行** |

## 三、当前 git 精确状态（接手先看这里）

主仓 `E:\GitHub\Ling_GPU_PhysicsEngine\Ling_GPU_PhysicsEngine`（即本文件所在目录）：

- 当前分支：`codex/WarSandbox-scenario-presets`，工作区**干净**，HEAD = `6f11290`（PR #12 合并提交）
- **`stash@{0}`**：`temp: all changes before split into scenario-presets and uebs2-engagement` —— **全部未提交成果都在这里（30 个文件，+1754/-61）**
- worktree `E:\GitHub\Ling_GPU_PhysicsEngine\ling-uebs2-worktree`：分支 `codex/WarSandbox-uebs2-engagement`，已建于 `6f11290`，**空的**（stash 内容尚未应用）

### stash 内容 → 归属映射表

**A组 · 可复用战场方案**（应落在 `codex/WarSandbox-scenario-presets`，即主仓当前分支）：
```
Assets/Game/Scripts/WarSandboxScenarioPreset.cs (+303, 新)
Assets/Game/Editor/WarSandboxScenarioPresetAuthoring.cs (+79, 新)
Assets/Game/Editor/WarSandboxEditorWindow.cs (+116)
Assets/Game/Tests/EditMode/WarSandboxScenarioPresetTests.cs (+143, 新)
Assets/Game/README.md
（上述新文件均带 .meta）
```
功能：把整张战场（布阵、Simulation/RuntimeFlow/RuntimeCombat 参数、规则、路径点、障碍）存成资产再载入，支持 Undo，Play Mode 禁用。

**B组 · UEBS2 思路的接战与拥挤改进**（应落在 worktree 的 `codex/WarSandbox-uebs2-engagement`，改动集中在引擎层）：
```
Assets/MassEngine/Core/Shaders/AgentDataCommon.hlsl (+179/-)
Assets/MassEngine/Simulation/Shaders/AgentCombatSimulation.compute (+178/-)
Assets/MassEngine/Core/CombatBufferSet.cs
Assets/MassEngine/Core/ComputePipelineOrchestrator.cs
Assets/MassEngine/Core/MassGpuBufferManager.cs
Assets/MassEngine/Core/MassGpuShaderPropertyIds.cs
Assets/MassEngine/Core/MassGpuShaderSet.cs
Assets/MassEngine/Core/README.md
Assets/MassEngine/Simulation/README.md
Assets/MassEngine/Tests/EditMode/MassEnginePropertyTests.cs
Assets/MassEngine/Tests/PlayMode/MassEngineGpuKernelTests.cs
Assets/MassEngine/UnitTypes/DefaultSpawnModule.cs
```
功能：规则布阵取随机散点、友军独立密度图（敌我不再互为密度约束）、追击方向与流场按距离混合、每目标 8 个环向战斗槽位 + 高频占用反馈、局部凸包引导导弹、接触分离顺序修复、目标负载均衡评分 + 残局免死。附带一个真 bug 修复：**战斗 kernel 原先用 10 个 UAV 超过 D3D11.0 的 8 个上限，Unity 会静默跳过 Dispatch；改成只读 SRV 别名后降到 7 个**。

**用户手调参数（不能还原，恢复为未提交的本地改动）**：
```
Assets/Game/Settings/AttackerUnitConfig_Spawn.asset
Assets/Game/Settings/DefenderUnitConfig_Spawn.asset
Assets/Game/Settings/RuntimeFlowConfig.asset
Assets/Game/Settings/SimulationConfig.asset
```
（双方各 5 万、世界 720×720、流场格 3m）

**账本文档（建议单独一个 docs 提交，落在主分支）**：
```
Assets/AGENTS.md (+26, 新) / Assets/AI_CHANGELOG.md (+198, 新) / Assets/PROJECT_HANDOFF.md (+192, 新)（均含 .meta）
```

### 恢复步骤建议（在两分支都验证提交完成之前，**不要 `git stash drop`**）

```bash
# 1) 主仓：恢复全部改动（apply 不删 stash，保留备份）
git stash apply stash@{0}
# 2) 按路径提交账本文档（docs 提交）与 A组（一个功能一个提交，AGENTS.md 禁止混提）
#    B组 的 12 个文件用 git restore 恢复回 HEAD（它们已改由 worktree 承接）
#    4 个 .asset 保持未提交（或询问用户是否入库）
# 3) worktree：从 stash 提取 B组（worktree 与主仓共享 stash）
cd ../ling-uebs2-worktree
git checkout stash@{0} -- Assets/MassEngine/Core/Shaders/AgentDataCommon.hlsl  # 等 12 个文件逐个 checkout
git add <按路径> && git commit
```

## 四、四项建议的执行状态

1. **在 Unity 里实测 B组** —— 前任结论是"被阻塞"，**这个结论错了**，见下节。测试清单（前任原话提炼）：重导入 shader → 清 Console → Play，确认旧 10-UAV 报错不再出现；跑新增的目标负载 GPU 测试；先 8~28 人金测，再上 10k/50k/100k/200k。观察重点：多人围攻是否绕向不同方向而非持续抖动；前排槽位卡住后后排走不走相邻通道；静态障碍贴近时会不会困在局部密度偏置切角；目标死亡后换目标是否够快。
2. **拆分提交** —— 进行到一半（stash + worktree 已就绪，分发未做），按第三节完成。
3. **修文档漂移** —— 未开始。两处：`MassEngine/Crowd/README.md` 还写着"接战状态规避自动衰减×0.35"，代码已改成友军独立密度并保留完整规避；`.kiro/specs/movement-separation-optimization/tasks.md` 全未勾选但密度图部分（task 1~? 主体）早已实现并改名；真正没落地的是 `separationSkipInterval` 跳帧与 `wanderMaxAngle` 朝向偏移（全工程 grep 零命中）——要么补实现，要么明确登记为放弃。
4. **已登记的功能待办** —— 未动。N 阵营 × N 流场（`flowFieldDirections` 数组化，`gpu-unit-oop-refactor` item 13，也是 teamId 只能 0/1 的根因）；战斗槽位固定 8 个、未按目标半径动态/扩展；普通"移动指令"偏迟钝（交互价值有提升空间）。

## 五、重要勘误：Unity 编辑器其实装了 ⚠️

前任会话只搜了 `C:\Program Files\Unity\Hub\Editor`，得出"本机没有 6000.3.14f1、Unity 实测完全被阻塞"的结论。**这是误判**——已验证编辑器就在：

```
D:\soft\Unity6\6000.3.14f1\Editor\Unity.exe   （存在，版本与本工程 ProjectVersion.txt 要求完全一致）
```

（TowerDefense 姊妹工程一整天都在用这个编辑器跑测试。）也就是说**第 1 项 B组 实测并没有被阻塞**，恢复 stash 拆分完成后就可以直接做。跑 batch 测试前记得关闭 Unity 编辑器实例（项目锁）。

## 六、离线验证已通过的部分（B组 的"编译层"信心）

- 两个测试 csproj（MassEngine.Tests / MassEngine.PlayModeTests）串行构建：0 警告 0 错误
- `SimulateCombatAndAccumulateDamage` kernel 用 Windows Kits `fxc.exe`（CS 5.0）离线编译通过
- 资源绑定清点：`AgentDataCommon.hlsl` 声明 27 个 RW + 17 个只读资源；战斗 kernel 实写 7 个 UAV（hp/target/slot/cooldown/pendingDamage/agent/position），低于 D3D11.0 上限
- **但要注意**：以上全部只是"编译通过"。Unity Test Runner 与视觉行为从未跑过（这正是第 1 项要做的）

## 七、工程约定（摘自 AGENTS.md，恢复 stash 后以原文为准）

- 禁止丢弃工作区里的未提交功能（这就是为什么必须完成拆分而不是 revert）
- A、B 两组是两件事，禁止塞进同一个提交；当前分支名只对应 A组
- 提交按路径 stage，禁止 `git add -A` 类操作
- 每个完成的逻辑批次立即追加 `AI_CHANGELOG.md`
- 账本最后一条是 2026-08-29（内容：另起 TowerDefense 姊妹工程）——本工程未提交功能自 8/16 起搁置约两周，拆分提交后记得补 changelog

## 八、原始记录（需要深挖时）

- 会话转录：`C:\Users\Administrator\.claude\projects\e--GitHub-Ling-GPU-PhysicsEngine-Ling-GPU-PhysicsEngine-Assets\56f1439d-e85c-4387-98df-8a7a6c63b83b.jsonl`（233 行，无压缩摘要；行 63 是完整工程分析与四项建议，行 192 是第 1 项结论，行 231-233 是断点）
- 同目录 `fba56ab2-*.jsonl`（9 行）只是额度断后用户发的测试消息，无内容
- stash 原始内容：`git stash show -p stash@{0}`（tracked 部分）与 `git show stash@{0}^3`（untracked 部分）
