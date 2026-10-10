# P12 导航共享候选 · 串行回归报告

2026-10-09 22:41–22:54（本机时间）· 用户授权“跑吧，开干” · 隔离工程 `outputs/P12`

> **结论：P12 导航共享候选（r03 + 两文件）6 组串行回归全部通过。** 本报告只说明回归未发现功能退化；**不是 P3 全范围签收，不是真人验收，不是发布候选**。未合入母工程。

## 被测对象
- `outputs/P12` = r03 + `Assets/MassEngine/Terrain/TerrainNavigationGrid.cs`、`Assets/MassEngine/Core/MassEngineManager.LocalOrders.cs` 两处导航共享改动（共享只读拓扑，求解工作数组独立）。
- 说明见 `outputs/P9FirstCommand-20261009-01/首次命令优化候选说明.md`；补丁 `first-command-navigation.patch`。
- Unity 6000.3.14f1，D3D11，i7-7700 / GTX 1060 3GB；Burst 默认关（仅 optin 组用测试参数 `--war-sandbox-nav-burst` 打开）。

## 结果

| 阶段 | 测试 | 结果 | 测试耗时 | 进程墙钟 |
|---|---|---|---:|---:|
| nav-editmode | EditMode `MassEngine.Tests.TerrainNavigationGridTests` | 23/23 Passed | 0.60 s | 71.9 s |
| first-command | PlayMode `P9FirstCommandTests`（共享拓扑/并行结果对照、场景生命周期与精确半径共享、障碍变化失效） | 3/3 Passed | 32.09 s | 57.6 s |
| managed-joint | `P9CombinedAcceptanceTests.P9SingleSessionRadiusDeploymentPlansOrdersNaturalResultAndSceneSwitch`（batchmode） | 1/1 Passed | 131.97 s | 158.1 s |
| managed-repeat | `P9CombinedAcceptanceTests.P9Repeated2048ScopedFeedbackAndResourceRelease`（渲染 GUI） | 1/1 Passed | 44.47 s | 82.9 s |
| optin-joint | 同 joint，Burst opt-in | 1/1 Passed | 128.60 s | 154.5 s |
| optin-repeat | 同 repeat，Burst opt-in，渲染 GUI | 1/1 Passed | 42.80 s | 77.6 s |

无编译错误、无 Shader 错误。P9 联合流程与此前基线一样为 4/4。

## 保护审计（运行前后对比）
- 母工程：git HEAD、`.git/index`、Assets/Packages/ProjectSettings 全部文件 size+mtime 不变 → **通过**。
- 现役 `Builds/CaptureDefault-20261006-37`：全量 SHA 不变 → **通过**。
- P12 `Assets/MassEngine`、`Assets/Game` 全量 SHA 不变（测试未改源码） → **通过**。
- 用户 LocalLow 数据 SHA 不变；Unity 自动改写的 P12 ProjectSettings、持久 TestResults.xml、GUI 运行的 Editor 偏好均按运行前字节恢复 → **通过**。
- 全程单进程串行；启动前确认无其他 Unity/游戏进程。

## 证据位置
- 汇总：`outputs/P12Regression-20261009-02/journal.json`，每阶段 `<阶段>.json`、`mother-before.json`。
- 原始日志/结果 XML：`outputs/P12/Logs/InteractionRefinement-20261007/P8/p12reg02-<阶段>/`（P8 隔离守卫只允许此根目录）。
- 运行脚本：`Tools/InteractionRefinement/p12_regression_20261009.py`（拒绝覆盖已有输出目录；新一轮需改输出编号）。
- `outputs/P12Regression-20261009-01`：首次尝试，因输出目录不在隔离根被守卫拒绝，0 个测试运行，保留作记录，不计入结果。

## 未覆盖 / 仍开放
- P3-PERF-01 周期性长帧（>33.33 ms 每 15 秒窗约 85 次）仍开放；本轮未做帧时 ABBA。
- P3-VISUAL-01 近景动画/抖动、P1/P2 半径圈与音频体验：需真人观察，不能代签。
- 独立冷态（清 ShaderCache）复核不在本轮；Editor 冷首用数秒停顿已证实不在 Player 复现（见 WindowsPlayerPrepared-07）。
- 未提交、未合并、未 push、未生成 38、37 未动。是否合入母工程由用户决定。
