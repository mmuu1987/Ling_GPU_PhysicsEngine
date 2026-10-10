# 引擎侧性能工作记录（2026-10-10）

本页汇总2026-10-10经PR合并到main的引擎侧性能改动与测量。**全部未出包**：现役仍为[37版](../CURRENT_DELIVERY.md)，没有38版。
测量来自隔离工程 `outputs/P12` 的Windows Player测试包，不是37包、用户包或人工验收，不改变产品人数/画质口径。
用户2026-10-10指示性能专项暂停（“性能优化这块先这样吧”）。

## Git

main == feat/mother-version08-sync == `7ffafd9`，均经PR标准合并，无强推。

| PR | 合并提交 | 内容 |
|---|---|---|
| #29 | `9c58a3e` | `f2ec159` 导航拓扑共享 + Burst流场求解默认开启；`d8f743f`/`2419a32` 母工程状态同步（含非法坐标防护），mesh `.asset/.anim` 停止跟踪（本地保留）；`f3e22d7` 1024m测试战场 `forest-green22-1024`（20万布阵） |
| — | `52c8677` | `Tools/InteractionRefinement` 可复用runner/harness/证据json；一次性脚本归档到 `outputs/Cleanup-20261010-01/archived-temp-scripts` |
| #30 | `54728f6` | 全花名册远景低模：VAT全帧采样QEM减面工具 + 32个profile + UnityChanVAT 600面；177个RenderConfig改指向新远景，近/中景与材质不变 |
| #31 | `7ffafd9` | 动态流场整条求解移到后台Burst任务，结果下一Tick生效 |

## 测试条件

i7-7700 / GTX1060 3GB，D3D11，1920×1080，默认战斗镜头；1024测试图只刷Male+Knight两种兵。母工程图形API顺序D3D11→Vulkan保持不变。

## 导航共享与Burst（PR #29）

- 首次规划约330ms → 约31ms（军团间共享导航拓扑，Burst默认开启）。
- 拆解（`outputs/P3Profile1024-20261010-01/attempt-02`、`outputs/P3GpuBreakdown1024-20261010-01`）：20万帧128.1ms/GPU122.2ms，只算不画9.4ms、只画不算134.4ms，瓶颈为远景兵种几何吞吐（Far与Mid同网格，每实例2000+三角形），不是像素填充、阴影或CPU。

## 远景低模（PR #30）

预算由用户定稿：步兵/中型300、大型400～500三角形（300为下限）。例外：UnityChanVAT 600（发片/裙子300面会塌）；LegacySkeleton01保留旧远景。偏细组（SkeletonWarrior/Rogue、Ranger02、Stegosaurus01、MountedKnight04，IoU约.78～.82）用户接受。

| 单位 | 指标 | 旧远景 | 低模（修色后） |
|---|---|---:|---:|
| 10万 | 帧中位 | 78.8ms | 18.7ms |
| 10万 | GPU中位 | 75.6ms | 14.2ms |
| 20万 | 帧中位 | 125.7ms | 32.9ms |
| 20万 | GPU中位 | 120.6ms | 25.8ms |

证据：`outputs/P3FarLod-20261010-07-batch`（34角色对比图与IoU）、`outputs/P3GpuFarLod1024-20261010-03-full`。其余31个角色未入此基准，用户决定不再做混编测量。
已知局限：QEM按绝对距离计价，细长多部件（骨头、发片、斗篷、武器）会先被压薄。

## 动态流场后台求解（PR #31）

20万单位，同一状态前后对照（`outputs/P3CpuNav1024-20261010-01`）：

| 指标 | 改前 | 改后 |
|---|---:|---:|
| Update P95 / 最大 | 23.8 / 34.1ms | 1.78 / 2.97ms |
| Update >10ms 帧数 | 323 | 0 |
| 主线程CPU P95 | 26.2ms | 7.0ms |
| 每帧GC P95 | 738KB | 5KB |
| 只算不画 P95 | 22.6ms | 14.8ms |
| 帧中位 | 31.56ms | 31.45ms（GPU瓶颈，未变） |

异步求解433次，作废0，强制等待0，最大延迟1个Tick，全部Burst、托管回退0。EditMode 73/73；同输入下与同步求解逐位一致。
行为变化（用户接受）：动态目标流场晚1个Tick生效；显式命令仍立即求解。

## 仍开放（未处理）

- 冷Shader首次Attack约5秒、冷进场约44秒：Editor测得，Player未测。
- 20万帧时仍由GPU决定（模拟compute约9～10ms + 绘制）；继续优化需用户重新开启性能专项。
- P3-PERF-01、P3-VISUAL-01、P9端到端与真人检查、H1/H2/E2/R1发行门槛。
- 以上改动需要新试玩包才能到用户手里；是否构建38版由用户决定。
