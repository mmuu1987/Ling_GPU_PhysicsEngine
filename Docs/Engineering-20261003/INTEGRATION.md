# Version08 工程整合验证（2026-10-03）

完整候选已从 Git 暂存区导出到全新目录，完成首次导入、编译、定向 EditMode、UI PlayMode、Windows 构建、机器人跨进程玩家流程与全部 29 场目录轮巡。原交付包未覆盖；尚未提交、推送或开 PR。

## 验证对象

- 分支 `feat/war-sandbox-battlefield-rules`；基线 HEAD `a76b4ca0a028d2d3778fc3c1c77f7947daab477b`。
- 实测候选树 `236af77b5ece7f825d51883732c43da2a23257f3`：2,195 个变更路径，8,099 个候选文件，3,677,324,967 字节。整合了原内容增量及误忽略的 588 个配置 + 588 个 meta。
- 导出方式为 `git checkout-index`，不是已提交版本的 clone。隔离工程 `E:/GitHub/_validation/WarSandboxV08-20261003-01`，没有复制旧 Library、Logs 或 Builds。
- Unity 6000.3.14f1；Windows / D3D11；GTX 1060 3GB；串行低负载，有图形设备。
- 使用 `RobotExpressiveAdmissionBuilder.Build08` 构建已有 V08 资源，没有调用 Prepare08 或重新烘焙；生成 Development Windows 包。
- 新包：隔离工程内 `Builds/OfficialRoster-20261002-04/Start-WarSandbox.cmd`；GUID `eea0c353522542919393aa8b05eaaea0`。
- 原工程普通游戏入口仍为原包，GUID `c9915b6a69974ad0ab3edeb66c846fdf`。

## 验证结果

| 检查 | 结果与范围 |
|---|---|
| 首次导入与编译 | 成功；未依赖原工程缓存或历史 Logs |
| EditMode | 52/52：正式机器人身份 10、UI 样式 19、兵种数值 23；零失败、零跳过 |
| UI PlayMode | 1/1：1280/640 目录、全身预览、缩放、66 模板兵种库和机器人部署 |
| Windows Build08 | 成功，退出 0 |
| Player seed | 通过，96 vs 64 自然结算、保存方案、重开恢复满员、返回目录 |
| Player reload | 通过，新进程读取 seed 方案、自然结算、重开和返回；方案哈希与来源运行/进程身份一致 |
| Player official-catalog | 29/29 可选战场进入、开战、重置和返回通过；31 个兼容身份保留 |

构建和三份玩家报告 GUID 一致；全部阶段退出 0，三份玩家报告均 completed/passed=true、errors=[]。

玩家自动化调用真实 uGUI 回调，使用独立设置、全局数值及方案目录。seed/reload 共用本次专用方案目录，未使用玩家个人数据。UI 截图由实际渲染生成，自动检查尺寸/非空；不是 OS 鼠标输入验收或全模型外观评审。

## 保护与导入差异

原工作树 Assets/ProjectSettings/Packages 对暂存区无未暂存差异；原 EditorBuildSettings、TimeManager 和原包 boot.config 的 SHA256 与运行前相同。包锁文件在隔离工程与原工程一致。

隔离工程最终只有四个已跟踪文件被 Unity 改写：早期 `RobotExpressivePilot/Version01/Native/Material0–2.mat` 新增 URP AssetVersion=10 元数据，以及 TimeManager 的序列化格式更新。构建时的 InputSystem 预载配置已由构建流程恢复。差异保留在隔离工程与本机 `isolated-source.diff`，未复制回原工程，未修改正式 Native03/Prepared04 资产。

验证结束后仅补写工程说明与交接；最终暂存树会因此不同于实测树，运行时代码、资源和项目设置不变。NEXT_TASK、历史交接全文、Logs、Builds 和本机审计文件不纳入本轮暂存；pelican SVG/meta 不在候选变更中。

## 保留的问题与范围

- 静态依赖扫描的 12 个未解析 GUID / 16 处引用仍保留失败记录（旧渲染配置及 URP 包）。实际导入与构建成功不能反过来宣称静态扫描全通过。
- 构建有 20 行 shader warning：16 行拥堵计算可能未初始化提示，以及 4 行整数除法/取模性能提示。未阻塞本次构建或已验证玩家流程，不为此改引擎。
- 玩家日志提示 targetAcquireRadius=25 受 4 格搜索上限限制（cellSize=3，轴向有效约 12m）。自然结算通过；保留为玩法/索敌后续核对项。
- 包解析器有最低传递版本提示，packages-lock 未改变；许可模块 access-token 提示未阻塞导入或构建。
- 本次是定向回归，不是全量测试；未运行依赖历史 Logs 的 GPU 四动作捕获 fixture，也未重跑有构建列表/固定证据路径副作用的旧 Platformer fixture。R1–R3 继续保留于导航和交接。
- 29 场检查只覆盖进入、开战、重置和返回，不能写成 29 场自然对局。没有新增人工试玩、100k 性能验收、其他模型转换或 M6/M7/V1 签收。

## 证据与接续

本机证据根 `Logs/Integration20261003-01`：`candidate.json`、`edit-01`、`ui-01`、`build-01`、`player-seed-01`、`player-reload-01`、`player-all-01`、`source-audit.json`、`plan-cross-process.json` 与 `final-verification.json`。每阶段保存命令、进程、原始日志与回执；Edit/UI 有 XML，玩家有结构化报告和截图。

本机运行器为 `run_isolated.py`，stage 顺序 `edit → ui → build → seed → reload → all`。输出 tag 不复用，Build08 要求隔离工程的包路径尚不存在。下次应使用新的导出工程与证据目录，不覆盖本次产物。

接下来按 [CHANGE-PLAN](CHANGE-PLAN.md) 进行完整候选审查、提交与 PR；共享接缝和资源依赖整体处理，不按历史目录机械拆成声称可独立构建的提交。本次停在已验证的暂存候选。
