# P9 冷态 Attack 64.7 秒：根因诊断

2026-10-09 · 状态：**异常已复现并定位；尚未修复，P3/P9 不关闭。**

## 结论

主要耗时是 **Unity Editor 在首次启用局部命令时，同步编译 `AgentCombatSimulation` 的 `MASS_LOCAL_ORDERS` 计算着色器变体**，不是 CPU 攻击规划运行了 64 秒，也不是本轮启用了 Burst。

在生产代码不变的 P11 隔离工程中，只改变该计算着色器的项目内磁盘缓存，就得到以下 A/B/A 对照。三次均为默认 managed、同一诊断夹具、2048 人、框选 4 人，以及原联合流程首次 Attack 前的同一路径。

| 指标 | A：原暖缓存 | B：仅战斗 compute 缓存冷态 | A：恢复原缓存 |
|---|---:|---:|---:|
| 首次 Attack 至观察到 Committed | 370.28 ms | **65,097.14 ms** | 359.35 ms |
| 按钮回调同步耗时 | 5.77 ms | 5.48 ms | 5.60 ms |
| receipt 回读阶段 | 4.92 ms | 10.10 ms | 4.85 ms |
| receipt CPU 规划阶段 | 328.23 ms | **332.84 ms** | 338.26 ms |
| receipt 上传阶段 | 2.20 ms | 2.26 ms | 2.26 ms |
| 最大单次 yield 等待 | 29.62 ms | **64,746.80 ms** | 9.32 ms |
| 同场四命令后第二次 Attack | 29.72 ms | 29.77 ms | 28.96 ms |

上述计时嵌套且包含异步工作，**不能相加当作互斥的阶段分解**。回读计时是已有 receipt 的阶段墙钟时间，不是 GPU 硬件计时。

## 为什么可以确定是编译，而不只是猜测

1. **可逆对照成立。** 仅移走 `P11/Library/ShaderCache/compute/AgentCombatSimulation79e5`，长停顿重现；恢复原缓存，长停顿消失。没有清理母工程、全局或驱动缓存。
2. **单帧停顿被捕获。** 冷态从 frame 32481 到 32482，协程等待约 64.75 秒。恢复后命令为 `Committed`，CPU 规划仅 332.84 ms、上传仅 2.26 ms，导航刷新次数为 0。
3. **编译进程消耗与停顿吻合。** 覆盖这次长停顿的 65.44 秒采样区间内，`UnityShaderCompiler` 累积 CPU 时间增长 **64.67 秒**；Unity 进程增长约 1.30 秒。两个暖态对应停顿的采样区间内，编译进程 CPU 增量均为 0。采样间隔约 500 ms，因此这些是包围区间的 CPU 差值，不是精确到函数的 Profiler 计时。
4. **对应变体产物在停顿末尾落盘。** `6dd3bd32fbbc6a3221af37b63a20e90e.bin`，332,864 字节，包含 `_LocalOrders`、`_LocalOrderFlow`、`_LocalOrderMask` 等资源名称；随后生成 `ObserveLocalOrders` 对应产物。冷态重新生成的 12 个缓存文件，内容 SHA256 全部与原暖缓存一致。
5. **生产调用顺序支持这一时序。** `LocalOrderChannel.Tick()` 先完成规划、上传并设置 `Committed`；首次有活动局部组后，`Bind()` 启用 `MASS_LOCAL_ORDERS`，随后执行计算着色器。协程要等这一帧返回，才观察到接受状态。因此旧 `accepted_ms` 包含了首用编译停顿，不等同于 CPU 规划时间。

旧日志中的 shader warning 只是线索：**暖态也会重现这些警告，但没有编译 CPU 增量**。不能把“出现警告”单独当成编译耗时证据，也不能据此认定消除警告就能解决慢编译。

历史 `managed-joint-03` 的 64,719.7563 ms 没有阶段计时。本轮同路径复现为 65,097.1437 ms；不能把本轮各阶段数字倒填成历史那一次的精确分解。

## 仍然开放的问题与修复方向

- **定位不等于修复。** 本轮未修改生产代码，没有把暖态测试通过当作冷态问题消失。
- 优先针对该 Editor 变体的编译成本，以及可控的预编译／预热路径做小范围修复验证，避免首条玩家命令触发。若移动到准备阶段，也必须保留冷启动总耗时测量与明确反馈，不能把 65 秒隐藏起来。
- shader 缓存暖态下，首次局部命令仍有约 **328–338 ms 的 CPU 规划**；同场后续 Attack 规划约 25 ms。这是另一项首次准备成本，应单独拆分导航上下文建立与流场计算，不能归为同一个 65 秒问题。
- 本轮没有测量“先发 Hold”等其他首命令；从关键词机制可提出其也可能受影响的假说，但不作为已经验证的结果。
- 本结论限定 **Unity 6000.3.14f1、D3D11 Editor、GTX 1060 3GB**。Player 构建时的 shader 编译和运行时驱动首用是不同路径，不能直接外推 37 包或 Player 必然卡 65 秒。Player/AOT、真人 720p/1080p、P3/P9 整体验收仍开放。
- Burst 保持默认关闭。没有证据支持通过开启 Burst 来解决此次着色器编译停顿。

## 执行与保护

- `warm-02`、`cold-01`、`restored-01` 均 1/1 通过；这里的通过表示诊断流程及功能检查完成，**不是时延达标**。
- `warm-01` 因诊断输出目录不符合既有 P8 隔离器白名单，在测试开始前被拒绝退出。只修正运行器输出路径；保留失败日志，没有放宽隔离器。
- 新增的两个诊断夹具文件已移出 P11 的 Assets 并归档；原验收夹具未改。
- 原战斗 shader 缓存 12 文件、596,544 字节，内容及时间戳均精确恢复；冷态生成件另行保留。
- 全量 SHA256 收尾审计已通过：母工程及 37 包 10,094 个保护文件无变更／增删，HEAD/index 不变，7 个用户文件一致；P11 的 9,711 个来源文件及原夹具精确一致，无额外未知文件，无活动 Unity／Player。详见配套 `P9冷态攻击保护审计.json`。
- 不出 38、不提交／合并新改造、不代签真人或 Player 验收。

## 证据位置

Windows 项目根：`E:\GitHub\Ling_GPU_PhysicsEngine\Ling_GPU_PhysicsEngine`

- `outputs/P9ColdAttack-20261009-01/analysis.json`：阶段、逐帧长停顿、CPU 采样及日志交叉分析。
- 同目录 `cache-aba.json`、`cleanup.json`、`final-audit.json`：缓存迁移恢复与保护证明。
- 同目录 `diagnostic-fixture.cs.txt`、`diagnostic-harness.json`、`removed-diagnostic-harness/`：测试侧诊断源码与哈希。
- `outputs/P11/Logs/InteractionRefinement-20261007/P8/cold-attack-{warm-02,cold-01,restored-01}/`：各次 `Editor.log`、`results.xml`、`process-cpu.jsonl`、逐命令 trace CSV 及 `cold-summary.csv`。

原始 `cold-summary.csv` 无标题行，字段依次是：label、kind、members、attempts、callback_ms、observed_ms、readback_ms、plan_ms、upload_ms、max_yield_gap_ms、navigation_refreshes、observed_frame_delta。末列是帧号差，不是毫秒。
