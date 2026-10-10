# P0 基线修复记录（2026-10-07）

本记录说明修复边界与证据，不代表 P0 已整体验收通过。P1 尚未开始。

## 原始红灯与处理原则

- `editmode-01`：625 项，601 通过、24 失败、0 跳过。
- `editmode-critical-01`：独立进程重现同一组 15 项指令失败（71 项中 56 通过）。
- 原始清单、失败 XML、首轮完整 SHA 审计均保留，不回写为“通过”。
- 修复前后使用同一个原始项目清单；批准的修改以路径和预期 SHA 单独登记。原清单不被新结果覆盖。

## R1：现有整军指令解析

修改 `Assets/MassEngine/Core/MassEngineManager.cs`：

1. 已有导航覆盖优先于旧的攻击方／防守方默认姿态。
2. 显式移动／撤退输出 MoveOnly；显式待命输出 HoldHere；主动交战输出 Advance。
3. 默认据点推进仍保留交战许可，固定目标本身不等于 MoveOnly，避免重新造成占点双方不打人的死局。
4. 无运行时覆盖时保留原始默认逻辑；不改 GPU 枚举值、结构布局、阵营或导航缓冲区。

修复后 `editmode-critical-02` 为 **71/71 通过**。这是已有整军行为修复，不是局部指挥实现。

## R2：六项过时的测试契约／证据位置

| 原问题 | 校正后的检查 |
|---|---|
| V07/V08 历史入口要求占据 Build Settings | 历史菜单绑定、目录与历史场景仍必须存在，但不得替代现役入口 |
| 旧 EntryBuilder 被当成正式目录 | 明确验证 MainMenu、Green、Autumn、Winter 的完整顺序、启用状态、正式 Catalog 绑定、条目与模板有效性 |
| 机器人历史截图原路径不存在 | 指向原始 `RetiredWorktree-20261003-01` 归档，四种状态各 12 张及附加图片断言不变 |
| 旧测试硬编码 UI 最小缩放 0.85 | 对照真实 UGUI 的既有 0.5 缩放与屏幕矩形，保留位置／尺寸检查并校验右边距 |
| Launch 预设渲染引用必须与源资产同一对象 | 根据既有 M7.3 Far LOD 构建器，验证远景 ≤128 顶点、近／中景网格、有效纹理、材质与阴影设置不被改变 |

没有改动正式场景、Build Settings、目录或渲染资产来迎合测试。

## R3：恢复真实 VAT 测试输入

- 原始模型／动画在现役 Assets 中确实缺失，并非直接改一个文件夹名即可解决。
- 从本项目已有 Git 对象 `aea369904b87aca537a2d2ba32c2d449167df6ac` 只读提取真实素材及完整引用依赖；没有下载或虚构模型／动画。
- 恢复 **34 个素材，含 meta／目录记录共 79 个文件，24,475,022 字节**。
- 位置为 `Assets/MassEngine/Tests/Editor/LegacyHumanoid/`，明确 Editor 专用，不新增可选角色、关卡或编成。
- 使用独立新 GUID，并仅对素材内部的 Unity GUID 引用作一致映射，不让历史旧 GUID 悄悄重新影响现有场景；FBX／图片载荷保持原始字节。
- Shader 与 AssetVersion 依赖核实来自当前 URP 包，已有 Albedo 只读复用。
- 真实 Humanoid 动作变化与浮点 16/17 帧边界断言全部保留。`editmode-02` 中这三项均已通过。

## 修复过程中的自校正

`editmode-02` 为 621/625：剩余四项是本次加强测试时暴露的测试口径／夹具问题，而不是继续沿用原 24 项失败结论。

- 新 UI 夹具不能在 EditMode 直接调用运行时延迟 Destroy；现已在测试内立即释放自己创建的原生对象，然后注销实例，没有改生产 UGUI 的生命周期，也没有用 LogAssert 忽略错误。
- Far LOD 构建器在原 Profile 没有 MID 时会把原 LOW 用作 MID。测试现按该已存在的分支，逐项检查网格、纹理及布局，而不是假设 MID 一定存在。
- 原失败日志保留，随后启动 `editmode-03` 全量重跑；结果以该目录的 `process.json` / `results.xml` 为准。

## 证据索引

全部位于 `Logs/InteractionRefinement-20261007/P0/`：

- `repairs/01-command-stance/`～`04-test-fixture-lifecycle/`：精确补丁、修改前原字节备份、检查结果。
- `approved-source-changes.json`：原文件 hash 与批准的新 hash；不替换原始 baseline。
- `fixture-recovery-plan.json` / `fixture-recovery.json`：源提交、原始 hash、GUID 映射、复用依赖。
- `approved-new-files.json`：新增测试素材清单。
- `editmode-01/`、`editmode-critical-01/`、`editmode-critical-02/`、`editmode-02/`、`editmode-03/`：逐轮结果。

历史 GPU 图片仅作历史技术证据，不冒充本次 GPU／性能／真人操作验收。

## 结束补记

`editmode-03` 已完成：625/625 通过、0 跳过。正式场景 GPU 功能回放及三次实际 1080p 渲染 CPU/GPU 采样随后通过；最初 batch 性能集合已明确不予采用。结束保护审计通过。完整结果、残余警告和真人验收边界见 `P0-REPORT.md`。
