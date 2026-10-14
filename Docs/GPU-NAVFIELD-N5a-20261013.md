# 导航 C · N5a 报告：局部命令接入 GPU 精细场（2026-10-13）

## 结论

- **局部 Move / Retreat 组现在可以走 GPU 2 m 精细场**，开关 `RuntimeFlowConfig.gpuNavLocalOrders`，**默认关闭**（需要同时开着 `gpuNavField` + `gpuNavSteering`）。旧的 CPU 局部流场一行没删。
- 实机对照（森林丘陵，150 人局部 Move 组穿北侧山口，各 1 次）：GPU 版**行进快约 10%**，**方向条纹基本消失**（轴对齐朝向 0.540 → 0.006），帧时间不变（12.07 ms）。
- 编译 0 错误 0 警告；过滤测试 **154/154 通过**（新增 4 个 N5a 测试，数值与 CPU 参考逐位一致）。
- Attack（攻击）和 Hold（坚守）**仍走 CPU**，按计划不动。

## 实机对照（pass 场景，2 万单位，同一组、同一目标）

| 指标 | 局部 CPU（旧） | 局部 GPU（N5a） |
|---|---|---|
| 116 s 时距目标中位距离 | 118.7 m | **84.3 m** |
| 108 s 内行进距离 | 约 305 m | **约 336 m** |
| 越过山脊人数（116 s） | 5 | **21** |
| 卡住人数 | 0 | 0 |
| 轴对齐朝向（越接近 0.133 越自然，越高条纹越重） | 0.540 | **0.006** |
| 平均帧时间 | 12.07 ms | 12.07 ms |
| GPU 显存（导航场合计） | 11.0 MB | 29.0 MB（+18 MB，本地层首次使用时才分配） |

说明：
- 组的大小定为 150 人，是因为游戏规则要求 Move 目标周围有一块**完全无障碍的方形到达区**（边长约 √人数 × 4 m）。森林丘陵山口北侧放不下 400 人以上的到达区，这是游戏原有规则，不是 N5a 引起的。
- 各只跑了 1 次，山口场景没有交战，不像追击场景那样有双峰波动，但仍建议您在 Editor 里自己再看一眼。

## Editor 里会遇到的一次性卡顿（非 bug）

第一次在 Editor 里同时用到"GPU 转向 + 局部命令"时，Unity 要现场编译 `AgentCombatSimulation` 的新变体组合，**整个 Editor 会卡约 30 秒**。之后有缓存，第二次进 Play 时提交在 1 秒内完成。正式构建里变体是预编译的，不会出现。
- 现有的局部命令预热（LocalOrderPrewarm）只覆盖 4 个变体，不含 GPU 转向组合。要不要把它加进预热，需要您决定（本次没改）。

## 做了什么

**规则**
- 只有 **Move / Retreat** 组用 GPU 层：目标点固定，下命令时算一次种子。
- Attack 每 0.25 s 刷新一次并换槽位，强制算种子会卡主线程，所以留在 CPU；Hold 没有流场。
- **种子没上传前一律用旧的 CPU 局部流场**，不强制等待，主线程不会卡。
- 局部命令的导航网格必须和精细场是**同一个通行等级**（间隙、原点、格子大小、分辨率、可走掩码全部一致），否则回退 CPU（计数 `LocalRejectedNavigation`）。体型更大的单位会自动走 CPU。
- 拥挤代价用**该组所属队伍**的拥挤数据（N4 规则：只算本队）。
- 只有被存活命令引用的槽位才会松弛；槽位换了新组，旧层立刻作废，直到新种子上传。

**文件**
- 新增 `Assets/MassEngine/FlowField/GpuNavField.Local.cs`：9 个本地层，懒分配；共用 1 个种子工作区，排队逐个计算。
- `GpuNavField.cs`：`Relax` 拆出通用的 `RelaxLayer`（行为不变），采样时额外绑定 `_GpuNavLocalDist` / `_GpuNavLocalMask`。
- `GpuNavField.compute`：拥挤索引改用独立的 `_NavCrowdOffset`（队伍层的值与之前相同）。
- `AgentDataCommon.hlsl`（`patch_n5.py`）：通用的 `GpuNavSteerCore`，在 `SampleLocalOrderFlow` 里为 MOVE_ONLY 组调用 `GpuNavSteerLocal`。原 N3 队伍转向逻辑不变。
- `LocalOrderChannel.cs`（`patch_n5cs.py`）：新增 `SlotCommitted` 回调和 `UsedSlotMask`。回调出错只记日志，不影响命令提交。
- `RuntimeFlowConfig.cs`：新字段 `gpuNavLocalOrders`（默认 false）。
- `MassEngineManager.GpuNavField.cs`：每帧同步开关和槽位掩码，并挂上提交回调。
- `GpuNavFieldTests.cs`：新增 4 个测试：
  - 种子前不采样、种子等于不动点；
  - 重新提交作废旧层，Attack / Hold 不走 GPU；
  - 只用本队拥挤（逐位一致）；
  - 网格不一致时回退 CPU。
- 探针 `NavBaselineProbe.cs`：新参数 `local`、`gpunavLocal`、`localMembers`、`localAhead`。会自动寻找放得下到达区的目标，命令被拒时 5 秒内提前退出。
- 远程备份在 `outputs/Round6/n5_bak`。

## 下一步（等您确认）

- **N5b**：全军命令改用"阵线目标"（垂直于 重心→目标 方向的一条线段）。
- **N5c**：重新调整行军侧移和拥堵等待。
- **N5d**：删除 LaneApproach，只在收尾时、经您确认后进行。
