# P3-PERF-01：导航 Burst 默认开启候选（2026-10-10）

## 结论
- 在隔离 P12 上完成“默认开启 Burst”候选，Editor 回归 8/8 阶段全部通过，Player ABBA 2/2 组通过，Player 内托管/Burst **逐位一致**（0 处不一致）。
- **建议采纳该默认值**，作为 P12 候选的一部分；`P3-PERF-01` 在 **D3D11 / forest-green22 / 2048 人** 范围内可视为通过，全范围仍开放（见未覆盖）。
- 未合入母工程、未提交、未 push，未生成 38。

## 代码改动（仅 2 个文件）
| 文件 | 改动 |
|---|---|
| `Assets/MassEngine/Terrain/TerrainNavigationRuntime.cs` | 移除 13 处 `#if UNITY_EDITOR` 门控（含原 `#else` 的“Player 强制托管”分支）；`BurstReserveRequested => !reserveManagedOnly`（默认开启）；`--war-sandbox-nav-burst` 仍接受（无操作，`BurstArgumentPresent` 可查询）；托管回退原因仅剩 `ManagedOverride` |
| `Assets/MassEngine/Terrain/TerrainNavigationGrid.cs` | 移除 1 处 `#if UNITY_EDITOR` 门控 |

- 不再需要 `WAR_SANDBOX_PLAYER_BURST` 宏或任何构建期 define。
- 保留：异步编译准备门控（Waiting 期间走托管）、不可用/强制同步编译时回退托管、`--war-sandbox-nav-managed` 强制托管。
- 原文件、候选文件及 SHA：`outputs/P3BurstDefault-20261010-01/{original,candidate}/`、`candidate-manifest.json`（accepted=false）。回退只需把 original 两文件拷回 P12。

## Editor 回归（`regression-01/journal.json`）
| 阶段 | 结果 |
|---|---|
| TerrainNavigationGridTests（默认） | 通过 |
| Burst 门控 Policy+Runtime（默认） | 14/14；原生求解 5 次、托管原生 0、逐位一致、工作区创建/释放 2/2 |
| Burst 门控 Policy+Runtime（`--war-sandbox-nav-managed`） | 14/14；状态 Unavailable/ManagedOverride、原生求解 0 |
| P9FirstCommandTests（默认） | 3/3 |
| P9Combined joint / repeat（默认） | 1/1、1/1 |
| P9Combined joint / repeat（托管回退） | 1/1、1/1 |
- 母工程 HEAD/index/文件、37 包、P12 资源、用户数据均未变。

## Player ABBA（`player-validation-01/journal.json`）
Windows x64 Development Player，D3D11，forest-green22，2048 人，35 s 预热 + 15 s 采样，1920×1080，vSync 0。B = 不加任何参数（默认），A = `--war-sandbox-nav-managed`。

| 运行 | median ms | P95 | P99 | max | >33.33 ms | >50 ms |
|---|---|---|---|---|---|---|
| A1 托管 | 5.044 | 5.320 | 33.823 | 51.0 | 37 | 1 |
| B1 默认 | 5.260 | 5.718 | 18.097 | 30.4 | 0 | 0 |
| B2 默认 | 5.233 | 5.724 | 18.051 | 27.8 | 0 | 0 |
| A2 托管 | 5.277 | 5.806 | 38.744 | 63.5 | 69 | 6 |

| 组 | P99 比 | median 比 | 结果 |
|---|---|---|---|
| B1/A1 | 0.535 | 1.043 | 通过 |
| B2/A2 | 0.466 | 0.992 | 通过 |

- 门槛：P99 ≤ 0.80×、median ≤ 1.05×、>33.33 ms ≤ 0.5×、>50 ms ≤ max(A,3)，加路径计数与一致性。
- 路径：B 组 PreparationState=Ready、Burst 求解 284、托管 0；A 组 ManagedOverride、托管 284、Burst 0；托管原生回退 0。
- Player 内逐位一致：每个 B 运行 6 组目标（两队实际目标×实际半径/0 半径、中心单点×0/6），共 393,216 格方向，0 处不一致，全部真实走 Burst。
- 注意 B1/A1 median 比 1.043，接近 1.05 门槛；结合历史 6 组对比（5 组通过，唯一失败为 median 1.092），median 整体无稳定退化，但存在 ±5% 级运行间波动。

## 恢复与保护审计
临时 harness/构建入口已删除；ProjectSettings、ShaderCache、warmup 状态、用户设置、目标偏好文件均恢复原样；候选源与 manifest 未被测试改动；母工程、37 包未变。

## 未覆盖（P3 全范围仍开放）
- D3D12、其他战场、50,000 档；
- 冷启动首帧 Burst 编译准备在 Player 中的耗时分布（本次只确认 Ready）；
- 真人视觉/UI/音频验收；
- 合入母工程（等待你的决定）。

## 产物
- Player：`outputs/P3BurstDefault-20261010-01/WindowsPlayerBurstDefault-01/P3BurstDefaultValidation.exe`
- 脚本：`Tools/InteractionRefinement/p3_burst_default_{make,regression,player,summarize}_01.py`
