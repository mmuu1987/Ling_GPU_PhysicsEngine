# P3-PERF-01：Burst 后备在 Windows Player（AOT）下的 ABBA 验证

日期：2026-10-09 23:50（UTC+08）
结论一句话：**Burst 在 Player/AOT 下真实生效，长帧几乎消失；但按事先定的门槛，第二组 ABBA 的中位帧时间一项未过，正式结论为“1/2 组通过，未判定合格”。** Burst 仍默认关闭，P3-PERF-01 仍开放。

## 1. 做了什么

- 位置：隔离 P12（未碰母工程、37 包，未生成 38，未提交/推送）。
- 产物：`outputs/P3PlayerBurst-20261009-01/WindowsPlayerBurst-01/P3BurstValidation.exe`（Windows x64 Development，D3D11）。
- 构建结果：Succeeded，errors=0，warnings=1706，用时 15 分 18 秒，31 个目录场景随包。
- 为了让 Player 能走 Burst，**临时**把两份源码里的 `#if UNITY_EDITOR` 改成 `#if UNITY_EDITOR || WAR_SANDBOX_PLAYER_BURST`（TerrainNavigationRuntime.cs 13 处，TerrainNavigationGrid.cs 1 处），且只在这次构建里加 `WAR_SANDBOX_PLAYER_BURST` 宏。Editor 与普通 Player 构建不受影响。构建后已恢复原文件（SHA 校验一致）。
- 同一个可执行文件跑 4 次，只靠启动参数切换：A = 托管（不带参数，DefaultOff），B = `--war-sandbox-nav-burst`（**测试参数**）。
- 场景：forest-green22，2048 人，StartOrResumeBattle 后预热 35 秒，再采样 15 秒；1920×1080 窗口，vSync 0，不限帧率。使用游戏默认相机，**不是** Editor ABBA 用的固定宏观相机，所以绝对数值不能和 Editor 数据直接比，只能 A/B 互比。

## 2. 结果

| 轮次 | 后端 | 帧数 | 中位 ms | P95 ms | P99 ms | 最大 ms | >33.33 ms | >50 ms | 窗口内求解次数 | 平均每次求解+车道 ms |
|---|---|---|---|---|---|---|---|---|---|---|
| A1 | 托管 | 2225 | 5.60 | 6.23 | 36.19 | 52.78 | 71 | 2 | 84 | 32.0 |
| B1 | Burst | 2494 | 5.57 | 6.33 | 19.04 | 44.15 | 2 | 0 | 84 | 14.4 |
| B2 | Burst | 2627 | 5.42 | 5.80 | 16.58 | 23.98 | 0 | 0 | 84 | 12.5 |
| A2 | 托管 | 2561 | 4.96 | 5.60 | 34.39 | 38.54 | 43 | 0 | 84 | 29.4 |

Burst 路径证据（B1、B2 均成立）：采样开始和结束时准备状态都是 Ready；窗口内 Burst 求解 200→284，托管求解 0 次，托管原生回退 0 次；编译探针 1 次，耗时约 0.75 ms。托管对照（A1、A2）：Burst 求解 0 次，托管求解 200→284。

## 3. 门槛判定（沿用 Editor Burst ABBA 的门槛，未事后修改）

| 组 | P99 比 ≤0.80 | 中位比 ≤1.05 | >33.33 ≤0.5× | >50 ≤max(A,3) | Burst 路径 | 托管对照 | 结论 |
|---|---|---|---|---|---|---|---|
| B1/A1 | 0.526 ✓ | 0.996 ✓ | 2 vs 71 ✓ | 0 vs 2 ✓ | ✓ | ✓ | 通过 |
| B2/A2 | 0.482 ✓ | **1.092 ✗** | 0 vs 43 ✓ | 0 vs 0 ✓ | ✓ | ✓ | **未通过** |

说明：B2/A2 中位比不达标，是因为 A2 本身中位异常低（4.96 ms，另外三轮为 5.42–5.60 ms），B2 的中位实际比 A1 还低，两组 B 的平均帧时间也都低于对应 A（6.01 vs 6.74，5.71 vs 5.86）。这看起来是轮次间噪声，但按规则不能据此改判，正式记录为“未通过”。

## 4. 能确定的与不能确定的

能确定：
- Burst 在 Windows Player AOT 下编译并真实运行（非托管回退）。
- 结果一致：两组的长帧都大幅下降（>33.33 ms：71→2、43→0；P99 降约一半）。
- 托管路径在 Player 里同样存在长帧问题（A1 71 次、A2 43 次），说明问题不是 Editor 独有。

不能确定 / 未做：
- 中位帧时间没有退化这一项，只有一组证明，需要再跑一组顺序反过来的 BAAB 才能下结论（不用重新打包，用现有 exe 即可，约 6 分钟）。
- 只测了 D3D11、一个场景、一个相机位，未测 D3D12、其他战场、50,000 档。
- 真人观感、UI、音频验收未做，要由你来做。
- 托管/Burst 在 Player 下逐位一致性没有重新验证（Editor 下已验证过）。

## 5. 状态

- Burst：仍默认关闭；Player 支持只存在于这次临时构建，源码已还原，没有正式改动。
- P3-PERF-01：**仍开放**。
- 是否默认开启 Burst：等你决定；建议先补一组 BAAB。

## 6. 保护审计（全部通过）

临时 harness 与构建入口已删除；临时宏修改已还原；候选源码与候选清单未变；Assets、ProjectSettings、UserSettings、ShaderCache、构建目标设置、WarSandbox 预热状态均与原始一致；母工程（HEAD、index、文件状态）未变；37 包未变。

## 7. 证据

- `outputs/P3PlayerBurst-20261009-01/validation-01/journal.json`（完整日志）
- `outputs/P3PlayerBurst-20261009-01/validation-01/summary.json`
- `outputs/P3PlayerBurst-20261009-01/validation-01/player-*/frames.csv`、`result.json`、`Player.log`
- 脚本：`Tools/InteractionRefinement/p3_player_burst_abba_01.py`、`P3PlayerBurstFrameHarness.cs.txt`、`P3PlayerBurstBuild.cs.txt`
