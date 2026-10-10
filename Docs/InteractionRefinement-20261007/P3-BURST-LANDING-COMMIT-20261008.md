# P3 Burst 后备正式落库(2026-10-08)

用户明确授权("落库")。按分支/PR 流程执行,未合并、未改默认行为。

> **2026-10-09评审补充：已落库不等于可独立合并。** 提交缺失车道类与Burst/Collections程序集引用，历史验证包含未随两文件提交落库的依赖；静态阻塞与后续要求见 [PR前评审](P3-BURST-PR-REVIEW-20261009.md)。下方回归/ABBA数据仍为既有临时候选证据，不是本提交的干净环境验收。实际Git提交时间为2026-10-09 00:36:09 +08:00；文件名20261008保留任务标签。

## 后续依赖修复（2026-10-09）

隔离修订版已补齐依赖并通过最终162次定向用例执行（79个不同用例）；11文件补丁实际应用后的哈希与验证源码一致。详见 [修复与验证报告](P3-BURST-DEPENDENCY-REPAIR-20261009.md)。原分支/提交未更新，未合并、默认关闭；下方提交清单仍描述原085957ba。

## 提交内容
- 分支:`p3/burst-nav-fallback-20261008`(基于 aea3699,已推送 origin 并建立跟踪)
- 提交:`085957ba7e838d23379cbc8fe6249540c86f31d9`,仅 2 个文件,+405/−11:
  - `Assets/MassEngine/Terrain/TerrainNavigationGrid.cs`(落库 sha d8ab8647…,在已采纳二叉堆/输出基线上叠加 Editor 限定 Burst 内核)
  - `Assets/MassEngine/Terrain/TerrainNavigationRuntime.cs`(落库 sha be8697fa…,默认关闭接线)
- PR 创建入口:https://github.com/mmuu1987/Ling_GPU_PhysicsEngine/pull/new/p3/burst-nav-fallback-20261008(**创建与合并由用户决定**)

## 行为契约(随提交信息一并声明)
- 默认无参数:零 Burst 工作区/零探测,完全原托管求解(150/150 回归)
- `--war-sandbox-nav-burst` 显式开启;`--war-sandbox-nav-managed` 同时出现时托管优先
- 编译器禁用/强制同步/真实编译失败(BC1021):拒绝分配并回落托管,输出与托管逐位一致
- 非 Editor 代码零 Burst 引用(落库前再次独立扫描确认);求解算法/严格浮点/目标顺序/刷新/round-robin 不变,无并行导航

## 既有验证依据(不重复声明为新验证)
- 150 项回归、等价穷举、真实 Runtime/场景/渲染 160 次用例执行
- 正式 ABBA 通过:B1/A1 P99 比例 0.670(27.46/40.98ms),B2/A2 0.738(28.47/38.57ms),>33.33ms 帧 2/6 对 86/85,四门槛双对全过(P3-BURST-RUNTIME-QUALIFICATION-20261008.md)
- 本日细分段诊断(P3-SOLVE-DETAIL-20261008.md)确认 Dijkstra 主循环 21.1ms 是长帧主导,与 Burst 后备针对性一致

## 执行安全记录
- 前置断言全过:母分支 feat/mother-version08-sync、HEAD aea3699、暂存区为空、分支不存在、无 Unity 进程/锁、git user 已配置
- 仅 `git add -- <两个明确路径>`;提交前后双重校验暂存集与提交文件集恰为 2 文件、父提交为 aea3699
- 操作后切回母分支,两文件按操作前字节恢复(采纳态,preSha:Grid 1c9143a7…/Runtime 2c234982…),暂存区为空
- 落库后全量保护审计:passed=true,changed=[],unexpectedNew=[],用户 4 文件通过,无进程
- 用户 7000+ 未提交变更全程未触碰;悬空提交 62f464b 未动
- 回执:`Logs/InteractionRefinement-20261007/P3/burst-landing-20261008-02/landing-executed.json`(含操作前字节备份 pre/)

## 仍未关闭
默认启用 Burst 与否、PR 创建/合并、Player/AOT、人工验收、P3-PERF-01/P9 收尾均待用户决定。
