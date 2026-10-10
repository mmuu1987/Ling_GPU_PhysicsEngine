# Burst修复整合记录：后续已通过PR #28合并

> 2026-10-09最新：已推送并合入feat/mother-version08-sync，合并提交080c5e498f0004c8d54fbe745970fb11e36d3597。详见 [合并报告](P3-BURST-PUBLISH-MERGE-20261009.md)。本地母dirty tree未pull覆盖，默认关闭。以下为先前仅本地整合阶段的历史快照；其中未推送/未合并/不push约束不再描述当前发布状态。

# Burst 修复：独立本地分支整合完成（2026-10-09）

## 结论

已在独立工作树 `outputs/B10` 完成两次本地提交，分支 `p3/burst-nav-dependency-fix-20261009` 干净。母工作区没有切分支、暂存或提交；没有推送、远端PR创建、合并、默认启用Burst或出包。

|项目|结果|
|---|---|
|基线|`085957ba7e838d23379cbc8fe6249540c86f31d9`|
|代码修复提交|`6da7797ec01377e30434792461cc5305cf07dca0`|
|验证文档提交 / 分支头|`54fab89c2d76d7bb1e1ab617d0f462360aca504a`|
|修复提交范围|11文件，含meta；501行新增、3行删除|
|文档提交范围|2文件：README与机器可读验证摘要|
|相对母分支本地aea3699|12个代码/配置/meta文件 + 2文档文件|
|母工程保护|897项文件及HEAD/index核对通过|
|当前状态|仅本地；未推送、未创建远端PR、未合并|

## 内容与验证关系

- 11个已提交修复blob的SHA-256全部匹配上一轮最终验证manifest。
- 文档提交后再次核对修复blob；代码没有变动，工作树干净。
- 因此沿用上一轮162次用例执行 / 79个不同用例的定向证据；**本轮没有重新跑Unity，不把内容一致性核对冒充新测试。**
- 随分支提交的资料：`Docs/BurstDependencyIntegration-20261009/README.md` 与 `validation.json`。摘要明确默认关闭、Editor限定、独立编译/定向验证范围，以及未通过的Player/AOT/P9/人工门槛。
- 相对母分支的整体差异仍包括原085957ba中的Grid/Runtime和托管优化，评审不能只看最后11文件修复。

## 整合中处理的差异

仓库文本属性在Windows工作树使用原生换行，补丁应用后asmdef原始字节与LF验证清单不同。逐文件确认只差CRLF/LF后，仅在本轮独立工作树统一为已验证LF字节；Git命令局部指定core.eol=lf，未改用户全局配置。首次阻断记录和换行核对记录保留，最终提交blob一致。

## 保护与边界

- 母分支仍为 `feat/mother-version08-sync`，HEAD仍为aea3699；其暂存区原字节和受保护文件未变。
- 现役37包未改，Burst默认关闭，没有38版。
- 新增本地分支与worktree管理记录是本轮有意的Git变化，不将其表述为整个.git目录完全未变。
- 原 `p3/burst-nav-fallback-20261008` 没有被改写；修复在新分支上追加，不重写历史。
- 本轮没有fetch，建议PR目标 `feat/mother-version08-sync` 只依据已核对本地基线，发布前需重新核对远端。

## PR准备与后续

PR正文已准备在 `outputs/BurstBranchIntegration-20261009-01/PR_DESCRIPTION.md`，尚未创建远端PR。发布/合并与默认启用分别处理；既有“不push”边界本轮未解除。

P9端到端、真实UI失败恢复、Player/AOT和720p/1080p人工验收继续开放。此次整合不包含母工作区全部后续交互改造，不能替代P9。

## 证据

- `outputs/BurstBranchIntegration-20261009-01/result.json`：代码提交与保护回执。
- 同目录 `final.json`：两提交、完整范围、最终状态。
- 同目录 `initial-eol-block.json`、`eol-normalization.json`：阻断和解决记录。
- 同目录 `mother-before.json`、`mother-after.json`：母工作区保护清单。
