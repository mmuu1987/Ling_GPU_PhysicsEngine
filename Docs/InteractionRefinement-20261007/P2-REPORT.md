# P2：半径编辑、保存与实际生效报告

日期：2026-10-07。保护审计完成时间：2026-10-07T15:42:47.622969。

**状态：自动化与保护审计通过，真人操作验收待完成。** P1 的真人验收也仍未补签。用户继续授权推进 P2，不等于此前真人验收通过。

## 结论

半径已接通 **待编辑 → 即时预览 → 明确保存 → 入场／应用校验 → 运行时克隆 → GPU 上传**。不仅是显示滑块。

最后一轮将图鉴真实输入／保存回调产生的文件字节直接交给新加载的 Green 场景：预览 0.6 米、文件 `agentRadius=0.6`、GPU 缓冲实际读取 0.6 米一致。不是分别构造两份相同测试值后宣称贯通。

实现边界：

- 追加 `AgentRadius` / `agentRadius`，不移动原 0–10 索引；显式读写 FlockingConfig。
- 全局按模板 ID，保持局部 > 全局 > 官方；同模型模板不自动同步。
- 圈随待编辑值变化；放弃不落盘；恢复官方需要明确保存／应用。
- 运行时只复制需要修改的群体配置，不写源资产。半径修改不热更新正在进行的战斗。
- 存储支持 0.05–4.8 米，但当前战场还须通过网格、实际行列／抖动、扩展占地和地形准入；拒绝时不自动改人数、模型、伤害、军团或网格。
- 修正半径工作流中的地形清距高水位残留：先释放旧注册表／GPU，再在明确部署边界设置候选清距；从未使用该工作流的旧清距路径不改。

## 验证结果

| 验证 | 最终结果 | 证据 |
|---|---:|---|
| 完整 EditMode 回归（Game.Tests + MassEngine.Tests） | **652/652**，失败／跳过均 0 | `editmode-04`，131.857 秒 |
| 预览与半径链 PlayMode 专项 | **24/24**，失败／跳过均 0 | `preview-04`，45.280 秒 |
| 原有世界尺度投影检查 | 12 条 | 复用 P1 独立相机／平面尺验证 |
| 真实图鉴输入、保存、放弃、恢复 | 通过 | InputField／按钮回调 + 文件读取 + 真实 UI 截图 |
| 界面实际保存文件 → 新场景 → GPU | 通过 | `actual-ui-saved-file-new-battle` |
| 实际 GPU／导航值链 | 12 个检查点 | 每次读取缓冲、核对 2048 人、缩放为 1 和其余 GPU 配置字段 |
| 7 张 UI 截图窄色／椭圆轨迹复核 | 通过 | `P2-UI-PIXEL-REVIEW.json` |
| 真实用户保存文件 | **4 份前后 SHA 不变** | 新增文件也纳入检查 |
| 37 回退包 | **387 份文件全量 SHA 不变** | 未重打包、未覆盖 |
| 工程基线保护 | **9619 文件 + 21 项授权变更** | 无越界新增／改动，2378 个既有 `.asset` 不变 |
| Git / GUID / 进程 | 通过 | HEAD、暂存区不变，重复 GUID 0，最终 Unity 进程／工程锁均无 |

上述 PlayMode 是专项集合，不冒称工程全部 PlayMode。测试使用 Unity 6000.3.14f1、D3D11、GTX 1060 3GB；不是独立发布包性能或长时间大战斗验收。

本轮最终 PlayMode 收到 **39 条“缺少 AudioListener”警告**，如实保留，未在本轮归因或修复。自动测试通过不等于音频体验通过；P0 的音频 caveat 也未关闭。已有编译警告按原日志保留，不包装为此次解决的问题。

## 实际 GPU 值链

所有下列检查点均为 Green 场景的真实缓冲读取；总人数 2048，逐类型的 GPU 配置除目标半径外与基线一致，全部 AgentData 缩放为 (1,1,1)。军团字段的复制不变另由数据测试与方案样本核对。

```text
P2_GPU_CHAIN actual-ui-saved-file-new-battle radius=0.6 count=2048 clearance=0.6 unrelatedSettingsEqual=true scaleOne=true
P2_GPU_CHAIN global-load radius=0.6 count=2048 clearance=0.6 unrelatedSettingsEqual=true scaleOne=true
P2_GPU_CHAIN running-no-hot-apply radius=0.6 count=2048 clearance=0.6 unrelatedSettingsEqual=true scaleOne=true
P2_GPU_CHAIN reset-keeps-applied radius=0.6 count=2048 clearance=0.6 unrelatedSettingsEqual=true scaleOne=true
P2_GPU_CHAIN rejected-grid-density-keeps-gpu radius=0.6 count=2048 clearance=0.6 unrelatedSettingsEqual=true scaleOne=true
P2_GPU_CHAIN cancel-keeps-applied radius=0.6 count=2048 clearance=0.6 unrelatedSettingsEqual=true scaleOne=true
P2_GPU_CHAIN local-beats-global radius=0.7 count=2048 clearance=0.7 unrelatedSettingsEqual=true scaleOne=true
P2_GPU_CHAIN clear-local-follows-global radius=0.65 count=2048 clearance=0.65 unrelatedSettingsEqual=true scaleOne=true
P2_GPU_CHAIN restore-official radius=0.55 count=2048 clearance=0.55 unrelatedSettingsEqual=true scaleOne=true
P2_GPU_CHAIN reset-after-restoration radius=0.55 count=2048 clearance=0.55 unrelatedSettingsEqual=true scaleOne=true
P2_GPU_CHAIN reenter-official radius=0.55 count=2048 clearance=0.55 unrelatedSettingsEqual=true scaleOne=true
P2_GPU_CHAIN reenter-saved radius=0.6 count=2048 clearance=0.6 unrelatedSettingsEqual=true scaleOne=true
```

关键区别：

- 战中将全局保存为 0.65，当前 GPU 仍保持已应用的 0.6；普通重置同样保持 0.6。
- 拒绝 1.6 的网格不相容、1.4 的间距不相容时，原 GPU 缓冲对象和已应用半径保留。
- 局部 0.7 优先于全局 0.65；清除局部并应用才回落到 0.65。
- 恢复官方并应用后，半径与地形清距回到 0.55；随后重置本局仍为 0.55；重新加载场景也没有沿用 0.7 的旧清距。
- 再保存 0.6 并重新加载，重新解析后 GPU 为 0.6。

地形清距仍采用有效模板的最大值并保留旧有 0.45 下限，不是每名单位拥有独立清距。

## 覆盖的失败／兼容分支

非法值（NaN、正负无穷、负数、0、低于 0.05、高于 4.8）、精度往返、旧文件缺字段、局部优先级、同模板克隆复用与释放、其他模板不联动、撤销／重做、取消不落盘、原子写入失败保留原件、保存撤销、局部方案保存与重载、网格／间距拒绝、扩展占地越界／重叠、旧清距路径不变、显式恢复绕开旧高水位，均有针对性测试。

隔离样本：

- `P2证据/p2-evidence/library-saved-0.6.json`：来自真实图鉴保存回调的原始文件，新场景直接读取过。
- `P2证据/p2-evidence/global-radius-0.6.json`：独立 GPU 流程的全局样本，模板 `roster-male`。
- `P2证据/p2-evidence/Plans/P2Radius.json`：局部半径 0.7 的方案，两个编成各 1024 人、军团 0／1、密度 0.4；已保存、读取、解析并应用验证。

这些样本未复制到用户默认保存目录。完整兼容与安全回退步骤见 **P2-COMPATIBILITY.md**。

## 如实保留的失败记录

`preview-01` 为 23/24：测试夹具误调用 Controller.PauseBattle，把 Setup 变成 Paused，生产代码的入场边界保护正确拒绝。随后只修正夹具，改为与真实 SceneSession 一致的 manager.PauseBattle；没有放宽生产保护条件。失败结果未删除，后续成功结果也不覆盖它。

`editmode-01`、`editmode-02` 和较早成功预览属于中间检查。最终权威为 **final-audit-02 / editmode-04 / preview-04**。

## 仍需真人验收／未宣称完成

1. 实际鼠标／键盘操作、滑块手感、文字可读性与滚动／侧栏布局。自动测试调用真实控件回调，但不等于真人命中与拖动测试。
2. 从真实主菜单点击进入、返回、再战的完整操作体验。自动化已经覆盖真实场景卸载／重载、公开入场应用入口、开战和重置；不冒称已完成主菜单真人导航。
3. 独立包、长时间战斗、密集接敌与性能新对照未在 P2 重做。P0 历史结果仅作历史基线。
4. **拥挤原因尚未在 P3 复现／定位，不能把半径编辑完成当成拥挤修复。**

建议真人试验先备份自己的保存目录，再选“原有·男战士”：0.55 → 0.6，看圈、放弃、再保存并进入 Green；验证方案局部 0.7、清除局部和恢复官方。若只检查界面，不必保存。操作结束按明确的保存／应用边界还原，不把“清空待编辑值”误当已还原运行状态。

## 变更、保护与下一步

独立 P2 基线包含已交付 P1 的当前源码；修改前备份 13 个既有文件。四个新增 C# 文件及其 meta 连同修改文件合计 21 个受控路径。源码补丁均有逐次日志和目标 SHA。`.asset`、场景、Compute Shader、AgentData 布局未改。

- `Assets/Game/Editor/InteractionRefinementP2.cs`
- `Assets/Game/Editor/InteractionRefinementP2.cs.meta`
- `Assets/Game/Scripts/UnitModelPreviewRenderer.cs`
- `Assets/Game/Scripts/UnitModelPreviewWidget.cs`
- `Assets/Game/Scripts/UnitPreviewRadius.cs`
- `Assets/Game/Scripts/WarSandboxDeploymentDraft.cs`
- `Assets/Game/Scripts/WarSandboxDeploymentHUD.UnitStats.cs`
- `Assets/Game/Scripts/WarSandboxFrontEnd.UnitLibrary.cs`
- `Assets/Game/Scripts/WarSandboxFrontEnd.UnitPreview.cs`
- `Assets/Game/Scripts/WarSandboxRadiusPolicy.cs`
- `Assets/Game/Scripts/WarSandboxRadiusPolicy.cs.meta`
- `Assets/Game/Scripts/WarSandboxRuntimeDeployment.cs`
- `Assets/Game/Scripts/WarSandboxTerrainValidation.cs`
- `Assets/Game/Scripts/WarSandboxUGUI.cs`
- `Assets/Game/Scripts/WarSandboxUnitStats.cs`
- `Assets/Game/Tests/EditMode/UnitRadiusEditingTests.cs`
- `Assets/Game/Tests/EditMode/UnitRadiusEditingTests.cs.meta`
- `Assets/Game/Tests/PlayMode/UnitPreviewRadiusPlayModeTests.cs`
- `Assets/Game/Tests/PlayMode/UnitRadiusEditingPlayModeTests.cs`
- `Assets/Game/Tests/PlayMode/UnitRadiusEditingPlayModeTests.cs.meta`
- `Assets/MassEngine/Core/MassEngineManager.Terrain.cs`

远端证据：`Logs/InteractionRefinement-20261007/P2/`。报告：`Docs/InteractionRefinement-20261007/`。

**停在 P2 检查点，不自动进入 P3、部署拖拽、局部框选或出包。没有 38，37 保留可玩。** 新手引导仍暂缓。下一开发步骤仍是单独的 P3 拥挤复现与根因核查，而不是继续扩大半径试图掩盖问题。
