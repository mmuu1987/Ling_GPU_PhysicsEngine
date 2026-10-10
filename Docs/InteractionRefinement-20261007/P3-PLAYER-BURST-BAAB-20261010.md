# P3-PERF-01：Burst Player/AOT 补测 BAAB（第二组）

日期：2026-10-10 00:03（UTC+08）
结论：**BAAB 两组全部通过门槛。** 加上之前的 ABBA，4 组里通过 3 组，唯一未过的是 B2/A2 的中位比（1.092），本轮两组中位比为 0.975 和 1.005，没有出现中位退化。Burst 仍默认关闭，P3-PERF-01 仍开放，等你决定是否默认开启。

## 条件

- 可执行文件：`outputs/P3PlayerBurst-20261009-01/WindowsPlayerBurst-01/P3BurstValidation.exe`（沿用上一轮，未重新打包，SHA 前后一致）。
- 顺序 B→A→A→B；场景、人数、预热、采样、分辨率、相机与上一轮完全相同；B = `--war-sandbox-nav-burst`（**测试参数**）。
- 门槛与上一轮相同，未修改。

## 结果

| 轮次 | 后端 | 帧数 | 中位 ms | P95 ms | P99 ms | 最大 ms | >33.33 ms | >50 ms |
|---|---|---|---|---|---|---|---|---|
| B3 | Burst | 2646 | 5.41 | 5.82 | 17.32 | 24.56 | 0 | 0 |
| A3 | 托管 | 2307 | 5.55 | 5.97 | 34.21 | 47.52 | 37 | 0 |
| A4 | 托管 | 2297 | 5.50 | 5.98 | 36.49 | 55.71 | 53 | 4 |
| B4 | Burst | 2583 | 5.53 | 5.89 | 17.09 | 26.62 | 0 | 0 |

| 组 | P99 比 ≤0.80 | 中位比 ≤1.05 | >33.33 ≤0.5× | >50 ≤max(A,3) | Burst 路径 | 托管对照 | 结论 |
|---|---|---|---|---|---|---|---|
| B3/A3 | 0.506 ✓ | 0.975 ✓ | 0 vs 37 ✓ | 0 vs 0 ✓ | ✓ | ✓ | 通过 |
| B4/A4 | 0.468 ✓ | 1.005 ✓ | 0 vs 53 ✓ | 0 vs 4 ✓ | ✓ | ✓ | 通过 |

## 两轮合计（8 次 Player 运行）

- 托管 4 次：>33.33 ms 为 71 / 43 / 37 / 53 次，P99 34.2–36.5 ms。
- Burst 4 次：>33.33 ms 为 2 / 0 / 0 / 0 次，>50 ms 全为 0，P99 16.6–19.0 ms。
- 中位帧时间：托管 4.96–5.60 ms，Burst 5.41–5.57 ms，无一致退化。
- 门槛：4 组过 3 组。

## 若决定默认开启，仍需完成

1. 正式改动（在隔离候选中）：把 Player 支持从临时宏变成正式代码，并把默认值改成开启、保留 `--war-sandbox-nav-managed` 作为回退开关。
2. 改动后重跑：EditMode 导航回归、托管/Burst 逐位一致性（Player 下也要验证）、Player ABBA。
3. 尚未覆盖：D3D12、其他战场、50,000 档、首次启动编译探针在慢机器上的表现。
4. 真人观感、UI、音频验收，由你来做。
5. 合入母工程、生成 38、提交推送，都需要你另行批准。

## 保护审计

母工程（HEAD、index、文件状态）未变；37 包未变；exe 未变；本轮未修改 P12 任何文件。

## 证据

- `outputs/P3PlayerBurst-20261009-01/validation-02-BAAB/journal.json`
- `outputs/P3PlayerBurst-20261009-01/validation-02-BAAB/player-*/frames.csv`、`result.json`、`Player.log`
- 脚本：`Tools/InteractionRefinement/p3_player_burst_baab_02.py`
- 上一轮：`Docs/InteractionRefinement-20261007/P3-PLAYER-BURST-AOT-ABBA-20261009.md`
