# Burst 后备 PR 前评审 — 2026-10-09

> 后续整合：修复已追加到新本地分支 `p3/burst-nav-dependency-fix-20261009`，代码 `6da7797e`、文档 `54fab89c`；未推送/合并，原085957ba未被改写。见 [分支整合记录](P3-BURST-BRANCH-INTEGRATION-20261009.md)。下文“未提交”等描述按原报告阶段理解。

> 后续进展：两项依赖阻塞已在隔离修订版中修复并完成六轮定向验证，见 [依赖修复报告](P3-BURST-DEPENDENCY-REPAIR-20261009.md)。原085957ba提交未被修改，所以下文针对原提交的发现仍有效；不得混同“隔离修复验证通过”和“原分支已更新/合并”。

## 结论：当前提交不具备独立合并条件

评审对象：`085957ba7e838d23379cbc8fe6249540c86f31d9`，父提交 `aea369904b87aca537a2d2ba32c2d449167df6ac`。

已通过只读 Git 对象检查确认提交内容及本地 `origin/p3/burst-nav-fallback-20261008` 跟踪引用。未 fetch，未查询远端当前 PR 状态。落库回执记录推送成功，但不代表已经创建或合并 PR。

**发现两项编译依赖阻塞。历史工作区测试结果保留有效，但不能作为该独立提交可编译/可合并的证明。** 本轮未启动 Unity，以下是静态审查及历史证据核对，不是新的编译失败运行记录。

## B1 — 缺失车道类（阻塞）

- 提交版 `Assets/MassEngine/Terrain/TerrainNavigationRuntime.cs:230` 引用 `TerrainLaneApproach33.Apply(...)`。
- `git grep -n TerrainLaneApproach33 <commit> -- Assets` 仅命中这一处引用，没有类定义；提交树中也没有 `TerrainLaneApproach33.cs`。
- 当前母工作区存在该类（`Assets/MassEngine/Terrain/TerrainLaneApproach33.cs:4`），但它不在被评审提交中。
- 默认关闭 Burst 或 `laneApproach33=false` 不能跳过 C# 对该符号的编译解析。

修复要求：明确该 PR 的基线。若保留车道接线，应纳入必要的已采纳车道实现、原 `.meta` 及针对性测试，或先合入相应前置提交；不能依赖母工作区未跟踪文件，也不能为凑两文件提交删除现役工作区功能。

## B2 — 缺失 Burst/Collections 程序集引用（阻塞）

- 提交版 `Assets/MassEngine/MassEngine.asmdef` 的 `references` 为空。
- 同一提交在 `UNITY_EDITOR` 下直接引用 `Unity.Burst` 与 `Unity.Collections` 类型。
- 历史 150 项回归的真实候选配置并非这个空引用配置：
  `Logs/InteractionRefinement-20261007/P3/burst-optin-finalshape-20261008-01/candidate/Assets/MassEngine/MassEngine.asmdef`
  明确包含 `Unity.Burst` 和 `Unity.Collections`。
- 执行器 `Tools/InteractionRefinement/p3_burst_optin_finalshape_20261008.py:44,69` 将候选 asmdef 和代码一起临时部署，之后恢复。

修复要求：把必要的 asmdef 引用纳入可复现提交；核对现有锁定包依赖能否在干净环境解析。manifest 没有直接声明不等于包一定不存在，不据此擅自升级或新增包版本。`UNITY_EDITOR` 只能隔离 Player 代码，不解决 Editor 的程序集引用问题。

## C1 — 评审范围与测试基线需明确

提交相对父节点不仅添加 Burst，还包含原本仅在工作区采纳的方向输出查表、堆比较优化、车道开关及接线。所谓“默认托管不变”应相对已采纳工作区基线理解，不能描述成相对 `aea3699` 零实现变更。

150/150 原始执行回执已核对：
`Logs/InteractionRefinement-20261007/P8/p3-optin-finalshape-regression-01/process.json`。
其执行器汇总在：
`Logs/InteractionRefinement-20261007/P3/burst-optin-finalshape-20261008-01/summary.json`。

这些运行使用临时完整候选和工作区测试，不是仅 checkout 两文件提交后的验证。该提交没有新增测试文件；需要明确纳入的测试和基线依赖，不能把“160次历史用例执行”写成当前分支独立复验通过。

## 其他发现

- 实际 Git 提交时间为 `2026-10-09T00:36:09+08:00`；“20261008”仍可作为任务/目录标签保留，但不应当作实际提交日期。
- `P3-BURST-OPTIN-AND-TAIL-20261008.md` 原列的 `P3/p3-optin-finalshape-regression-01` 路径不存在；实际用例输出位于上述 P8 路径。
- Runtime 内 `P3PreparationGate` 的“Temporary independent diagnostic / Not called by Runtime”注释已过时，实际由 Runtime 准备流程调用。建议依赖修复时一并更正注释，不需重构算法。
- 默认关闭、managed 参数优先、Editor 限定、同步 IJob.Run 等接线在静态代码中可见。未对整个求解器做穷尽正确性证明；本报告不授权启用、合并或打包。

## P9 证据核对

读取 `Logs/InteractionRefinement-20261007/P9/initial-regression-20261008.json`：
- critical、radius、deployment、scoped-scene 四套既有定向运行退出码均为 0。
- 汇总明确写明 `existing_targeted_suites_passed_not_P9_acceptance`，不是联合端到端验收。

仍缺：同会话半径取消/保存及重入 → 部署平移/尺寸/撤销 → 隔离方案保存/重载 → 框选四命令 → 自然结算 → 再战/正式场景切换；固定2048人的重复性能/反馈延迟与资源增长检查；720p/1080p真人键鼠验收。Player/AOT与候选出包另行决定。

## 下一步执行顺序

1. 先确定独立提交基线及最小依赖/测试文件集，补齐上述两个阻塞；不得整体搬入7000+工作区改动。
2. 在隔离且可复现的源码环境验证完整修订版，而非临时补依赖后仅提交两文件。只读审查发现的阻塞应先解决，当前不重复性能ABBA。
3. 保留默认关闭，补编译、托管回归、opt-in/managed优先、禁用/强同步及资源释放等定向验证；严守测试隔离和保护门。
4. 结果满足后再进入PR创建/评审/合并决策。P9端到端和人工验收独立跟踪，不由该PR代签。

## 本轮操作边界

只读 Git 提交/树/引用、文档、脚本及历史回执；新建本评审报告并更新交接提示。没有切分支、改代码/asmdef/Packages、启动Unity、重跑历史脚本、修改缓存、暂存、提交、推送、默认启用Burst或重建37。没有重新进行全量保护哈希审计。
