# 资源纳管恢复（2026-10-03）

根 `.gitignore` 的 Library 规则已改为 `/[Ll]ibrary/`，只忽略工程根 Unity 缓存。原来漏报的 **588 份兵种配置及 588 份 `.meta`，共 1,176 文件、561,921 字节**现已通过精确路径显式暂存；同时暂存 `.gitignore`，合计 **1,177 路径**。未提交、未推送，其他既有代码/资源/文档增量仍未暂存。

路径依据是上一轮的 [IGNORED-ASSETS.tsv](IGNORED-ASSETS.tsv)。它保留为修复前清单，文件名不表示这些资源现在仍被忽略。这些配置属于 UnifiedRoster、Dragons、Giants、Giants3、NonhumanBatch2、Troops4/5、PlatformerBatch6 的现有及历史制作目录；不是 Unity 缓存。

## 已完成的检查

- 588 组资产/meta 成对存在；恢复前后全部 1,176 文件 SHA256 相同，未重写 GUID、数值、布局或历史资源。
- Git 暂存路径精确等于 `.gitignore` 加上述清单；没有混入 NEXT_TASK、pelican SVG/meta、审计数据、其他增量或缓存。
- `git ls-files --others --ignored --exclude-standard -- Assets` 为空；工程根 Library/library、Temp、Builds、Logs 的忽略探针全部通过。
- 暂存 diff 的 2,352 条空白警告均来自原 Unity 序列化空字段后的尾空格；未发现其他类型 diff 警告。保留原件，不为消除格式提示批量重写资源。

## 扩大的静态依赖检查

从当前构建列表的全部场景、Version08 Catalog、GraphicsSettings、QualitySettings 和 Assets 内 Resources 出发，递归检查 YAML 及 meta 中的 GUID 引用，并核对包名在 `packages-lock.json` 中登记。

共访问 **989 个文件**、解析 **3,831 条引用边**；已定位的工程资源及 meta 全部在“已跟踪 + 可见未跟踪”的候选集合内，无同 GUID 多目标、缺失文件配对或仍被忽略的依赖。这里的候选集合包含尚未暂存的旧工作增量：其中 **99 个可达 Assets 文件尚未跟踪**，不能把恢复这 1,176 文件说成已经提交完整工程。

完整静态闭包检查仍为 **未通过**：有 **12 个不同 GUID、16 处引用**在当前磁盘无法定位，原始失败结果保留。它们不是本轮新增或被 Library 规则隐藏的兵种配置：

| 位置 | 数量 / 现象 | 当前处理 |
|---|---|---|
| `Assets/Settings/PC_Renderer.asset` | 7 个探针体积调试资源 GUID | 文件与 HEAD 相同（忽略换行差异），保持原件 |
| 项目与 URP 包各自的 `DefaultVolumeProfile.asset` | 相同的 4 个缺失脚本 GUID，共 8 处；名称为 OutlineVolumeComponent、TestAnimationCurveVolumeComponent、OasisFogVolumeComponent、TestVolume | 项目文件与 HEAD 相同，包内也保留相同引用；不凭静态文本删除组件 |
| URP `ScreenSpaceAmbientOcclusion.cs.meta` | 1 个 BlueNoise 默认引用 GUID | 包资源原样保留，不修改 PackageCache |

这些结果尚未证明实际编译或当前游戏受到影响；按既有缺口记录。下一轮新检出验证若出现对应的导入、编译或主路径故障，再依据实际错误处理。不能将名字像测试组件当成可随手删除的证明。

## 后续整合

先按[提交清单](CHANGE-PLAN.md)复核 A/B/C/S 的共享依赖，再显式暂存剩余需交付内容，确保当前 Version08 场景、制作器、运行时代码、测试与资产成套。`CHANGE-INVENTORY.tsv` 是上一轮的基线清单，当前还要包含文档整理与本次忽略规则/恢复资源。

最后在隔离的新检出或候选副本上验证导入/编译及必要主路径，使用新证据目录，不能覆盖旧包、玩家方案或测试证据。当前没有启动 Unity、生成新包或改变已暂停的人工试玩；没有宣称全部依赖闭包、独立检出构建或 V1 验收完成。

本机证据：`Logs/AssetTracking20261003/`，含恢复前指纹、旧忽略规则、初始暂存状态、依赖审计脚本/结果/引用边、HEAD 对照、空白检查和 `verification.json`。`dependency-report-01.json` 保留首次检查；最终 `dependency-report.json` 将恢复后仍未跟踪的实际依赖单列。
