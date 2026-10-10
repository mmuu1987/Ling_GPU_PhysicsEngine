# Burst 依赖修复与独立工程验证（2026-10-09）

> 后续整合：修复已追加到新本地分支 `p3/burst-nav-dependency-fix-20261009`，代码 `6da7797e`、文档 `54fab89c`；未推送/合并，原085957ba未被改写。见 [分支整合记录](P3-BURST-BRANCH-INTEGRATION-20261009.md)。下文“未提交”等描述按原报告阶段理解。

## 结论

**两项依赖阻塞已在隔离修订版中修复，并完成实际 Unity 编译及定向回归。原提交/分支尚未更新，不应把本结果记成原两文件提交已修复或 PR 已合并。**

基线是 `085957ba7e838d23379cbc8fe6249540c86f31d9` 的 Git 导出，完整保留该提交的 `Assets / Packages / ProjectSettings`；隔离工程为 `outputs/B9`，不是母工作区部署。默认仍关闭 Burst，Player/AOT、性能资格与 P9/真人验收没有因此通过。

## 实际修复（11文件，含meta）

- 补入现有已采纳的 `TerrainLaneApproach33.cs` 及原meta，保留GUID，不替换为已拒绝的O(1)候选。
- `MassEngine.asmdef` 补充 `Unity.Burst`、`Unity.Collections`；测试asmdef补充相同引用。
- Runtime只修正过时注释；求解、参数默认值、调度、浮点模式均不修改。
- 纳入现有42项车道路由/后处理测试文件，并新增可交付的Runtime GPU/生命周期测试及准备状态机测试。测试来源及实际数量以XML和清单为准。
- Runtime测试补充有界的重建就绪等待；诊断输出参数改为可选，避免普通Test Runner因缺参数失败。
- 修复文件统一UTF-8/LF，原GUID保持。manifest保存每文件基线/最终SHA-256。

锁定文件本来已有Burst 1.8.29、Collections 2.6.5，**没有升级或修改Packages**。首次导入过程中使用同版本包源缓存补齐三个渲染包，并核对已下载Burst包源；逐文件哈希见 `package-source-cache-receipt.json`。没有复制母工程的编译程序集、AssetDatabase或项目JIT。

## 最终验证（validation-03）

Unity 6000.3.14f1，Windows Editor，真实D3D11设备；不使用-nographics。前一轮结果单独保留，不累加冒充更多独立测试。

|配置|通过/总数|运行证据|
|---|---:|---|
|默认关闭|79/79|原生求解 0；工作区 0/0/0|
|显式开启 Burst|79/79|原生求解 5；工作区 2/2/0|
|托管参数优先|1/1|原生求解 0；工作区 0/0/0|
|编译器禁用|1/1|原生求解 0；工作区 0/0/0|
|强制同步拒绝|1/1|原生求解 0；工作区 0/0/0|
|无诊断输出参数（普通 Test Runner 兼容）|1/1|不提供专用输出参数仍通过|

合计 **162次用例执行，79个不同用例**；失败0、跳过0。不是项目全量测试。

默认配置的用例构成：
- `MassEngine.Tests.BurstDependencyRepairPolicyTests`：13
- `MassEngine.Tests.BurstDependencyRepairRuntimeTests`：1
- `MassEngine.Tests.TerrainCardinalRouteCacheTests`：42
- `MassEngine.Tests.TerrainNavigationGridTests`：23

点目标、区域目标、动态密度目标的实际GPU上传结果逐位对照；双队每Tick一个、未完成回读时释放、重建和原生工作区收支均在Runtime用例内检查。显式开启时见到真实原生求解，未将托管后备当作Burst通过。禁用/强同步不是本轮新注入的真实编译失败；后者仍仅保留历史证据。

**没有做新的ABBA、整场FPS、加载手感或真实UI失败恢复验证。** 原有冷启动/长帧限制不撤销；本轮测试的启动耗时和探测耗时不作为性能验收。

## 可复现性与保护

- 最终补丁已通过 `git -c core.autocrlf=false apply --check`，并在仅含精确基线文件的一次性目录实际应用；11文件SHA-256全部与最终已验证源码一致。见 `patch-apply-check.json`。

- 核对导出工程的7508个已提交项目文件以及11个修复文件；源码和Packages来源审计通过。
- 母工程保护核对897个文件（含37包及所列源码/配置），HEAD/index保持原值；不是整个磁盘或全部用户数据的全量哈希审计。
- 隔离导入自动更新了3个RobotExpressive材质文件（Material0/1/2.mat），没有源码/Packages漂移或额外Assets文件；清单见 `source-provenance.json`。这3个材质不进入修复补丁，也没有写回母工程。
- 不切母分支、不暂存/提交/推送、不改母工程源码或asmdef、不默认启用Burst、不打包38，37包保持原字节。

## 过程记录（保留，不包装成成功）

1. 最初完整仓库导出在历史文档附件处触发Windows长路径错误。改为短路径并导出完整Unity项目三目录；没有删除原失败现场。
2. validation-01在正式测试前被本轮主动停止：夹具提取误选了两个partial块中的首块，包含历史捕获依赖。只终止了按PID和项目路径核对过的本轮隔离Editor；不记作产品编译失败，日志和取消回执保留。修复提取后再运行。
3. 首次包源预检因Burst包已经出现而安全退出、未覆盖它；后续改为核对已有包的同版本逐文件内容，只复制缺失包源。
4. validation-02五配置通过；随后去掉测试专用CLI依赖并规范文本换行，再进行validation-03六轮最终复验。本报告只汇总最终版。

5. 打包时的首份手工拼接diff未正确表达无末尾换行，应用检查拒绝；改用Git原生diff，并以单命令core.autocrlf=false固定LF后通过实际应用及11文件逐字节对拍；未修改用户Git全局配置，保留1项末尾空行警告。原失败补丁留档，不进入最终修复包；不改变已验证源码。

## 交付物与下一步

- `outputs/BurstDependencyRepair-20261009-01/dependency-repair.patch`：相对原提交的修复补丁。
- 同目录 `repair-files/`、`repair-manifest.json`：明确文件集及哈希，不包含其他母工作区改动。
- 同目录 `BurstDependencyRepair-20261009.zip`：小型源码修复包和验证摘要，不是游戏发行包。
- `validation-03/`：六轮XML、Editor日志、命令、GPU计数与最终母工程保护结果。

下一步可按该明确文件集进入独立分支/PR整合，不再使用“只提交原两文件”的方案。原提交仍有已记录的依赖缺口，只有应用完整修复并验证的修订版才具备这轮定向证据。合并/推送/默认启用仍未执行。

P9的同会话端到端、资源增长与720p/1080p真人验收继续开放；本轮独立旧基线工程不含母工作区全部后续交互改造，不能替代P9验证。
