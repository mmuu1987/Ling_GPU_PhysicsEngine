# KayKit 剑盾骑士：完整接入试点

> **历史专题 / 入口提示（2026-10-03）：** 本页保留该阶段原始记录；当前正式入口已由 Version08 取代，见[工程导航](../Engineering-20261003/README.md)与最新机器人交付记录。下文“当前”“下一步”及构建命令不作为现在的执行清单。旧手工测试仍暂停，其他模型转换未授权；技术结果与人工认可范围按原记录保留。


**结论：一名新角色已接入独立可玩场景，最终02近战版本的自动闭环通过；等待你的模型观感/实际操作验收。** 2026-09-30。

## 从这里启动

工作树：`E:\GitHub\_worktrees\WarSandboxBattlefieldRules`

**`Builds\CharacterPilot-Knight-20260930-02\Start-Knight.cmd`**

双击上述CMD，默认64骑士对64内置士兵，1280×720窗口，30FPS功能预览上限。场景默认不自动开战；可配兵、保存、开始、自然结算、重开和返回菜单。CMD隔离设置到本包 `PilotData/settings.json`；人工方案用 `KnightPilotPlans`，不是原 `WarSandboxPlans`。直接启动EXE仍沿用默认设置路径，建议用CMD。

**旧正式候选入口不变**，仍是 `Builds/M7RangedPlaytest-20260930/Start-WarSandbox.cmd`。本轮不把骑士塞进原正式阵容、不替换旧模型/配置/包、不运行十万人、不提交或推送。

## 接入成果

|项目|最终结果|
|---|---|
|角色|KayKit Knight；候选比较了Knight、Ranger、Mage，选不引入新战斗类型的剑盾骑士|
|来源/许可|作者Kay Lousberg的Adventurers Free 2.0 + Character Animations Free 1.1，两个包内原始许可均为CC0；只取免费层|
|源文件|只选6个FBX、1张atlas、2份原许可进入项目，共8,075,544字节；未导入整个角色库|
|预处理|Generic骨架；四动作各240条曲线明确匹配；Sword/Shield绑定真实handslot.r/l；子层级缩放约0.7078；绑定姿态1.8m、Idle约1.62m；采样足尖朝向符合引擎+Z|
|四动作|Idle_A / Running_A / Melee_1H_Attack_Chop / Death_A；真实攻击与死亡，不是静态替代|
|VAT|生产VatBaker；30fps、114帧；Full6666顶点，Low1337顶点/2299三角形；Near=Full、Mid/Far=Low，各配对应VAT|
|纹理预算|Full位置+法线4096×228 RGBAHalf，14.25MiB；Low2048×114，3.5625MiB。合计17.8125MiB是引用VAT纹理像素预算，不是整个程序实测显存|
|LOD修正|原空间聚类Low会平均到atlas其他颜色；新增仅用于Knight的4×4色块保留Editor配方，未改通用减面器或旧角色|
|配置与注册|独立unit/spawn/combat/render/movement/animation；两个私有Combat明确projectileRange=0；Scenario与Catalog均登记骑士|
|持久身份|战场 `kaykit-knight-pilot`；模板 `kaykit-knight-v1`、revision **2**|
|新包GUID|`e4214860d91f447d886e80be0d42c0b1`；新Unity进程重读并构建，构建前后源文件哈希无变化|

官方来源：[Adventurers](https://kaylousberg.itch.io/kaykit-adventurers)、[Character Animations](https://kaylousberg.itch.io/kaykit-character-animations)。原始许可、官方upload ID、下载包SHA256和9份源文件哈希已归档。CC0只适用于本次新角色/动作；原游戏资产、Unity和其他依赖仍各有条款。这个内部试点不是整体发行许可放行。

## 实际验证

- **12组动作×LOD、48张真实GPU绘制截图**：实际运行时mesh/material/MPB与间接绘制；可见、非粉色、各段有运动。每档强制绘制，不是假CPU预览，也不是自然镜头LOD分类器的完整证明。
- 最终02的两个独立进程，使用全新 `plans-02` / `player-seed-02` / `player-reload-02`，而不是个人存档。

|阶段|进程PID|布阵|自然结算真实时间|存活|重开|
|---|---:|---|---:|---|---|
|Seed|19544|96骑士 / 64对手|15.66s|[65, 0]|恢复[96,64]|
|Reload|788|同一保存方案跨进程恢复|16.11s|[60, 0]|恢复[96,64]|

两场均默认命令、正常速度、自然歼灭结算，无注入胜负/伤害。真实UI回调覆盖模板选择、数量修改、保存/加载/应用、开始、结算、重开与返回；seed额外验证非法数量拒绝。6组/5组检查记录通过，方案字节及源配置不变，GPU与运行时副本释放；Player exit0、无记录的Error、未超时。**这是UI回调自动化，不是人用键鼠通关。**

## 复核中修正了什么

1. 原Low虽然可见、有动作，但发生调色板串色；保留初次图片，改用1337顶点色块保留Low后重新绘制48张验证。
2. 第一包01继承了当前Attacker模板的 `projectileRange=20`。两次UI闭环通过仍不等于剑盾近战正确；截图复核后改两个私有Combat为0、模板revision升为2，另构建02并重跑两进程。01包/日志保留为诊断，不作为最终近战验收。
3. 自动脚本会点击全景，旧镜头对小军团过远；只调整独立场景的初始镜头和缩放范围，没有改已认可的通用镜头/UI系统。

## 保留的限制与待办

- **人工未验收**：请看身高比例、脚底、剑盾挂点、四动作与近中LOD切换是否自然。低模面部/边缘有简化，不宣称和Full逐像素一致。
- 小场景镜头最大距离70；现役全局Far阈值120，所以本轮Far证据来自强制GPU绘制，不声称已做自然远景分类器/物理滚轮验收。
- 仍沿用现役模拟/LOD系统参数。最终交战截图出现“技术信息 !”（源码对应网格溢出计数非0）；日志也提示 `targetAcquireRadius=25` 受既有4格搜索上限限制、轴向约12m。两场仍自然完成，未为了抹掉警告修改引擎或共享配置；这些是后续私有配置/密集度调校项，**不宣称零警告/零溢出**。
- 没有跑本角色的十万人、长时间稳定性或平台性能验收。旧正式候选29.43/29.70FPS记录不变；这里的30FPS仅为节约负载的上限。
- 整体V1发行声明/终端条款、开发席位和用户V1签收仍沿用原待办。`humanAcceptance=false`，`v1Released=false`。

## 工程入口

- 源：`Assets/CharacterPilotSource/KayKitKnight/`
- 最终VAT：`Assets/Game/CharacterPilotPlayable/KnightPaletteSafeVAT.asset`
- 配置：`Assets/Game/CharacterPilotPlayable/Settings/TrialScenario.asset`、`TrialCatalog.asset`
- 场景：同目录 `ModelTrialMenu.unity` / `ModelTrialBattlefield.unity`
- 配方：`Assets/Game/Editor/WarSandboxCharacterPilotBuilder.cs`、`KnightAtlasLodBuilder.cs`
- 独立限帧/方案隔离组件：`Assets/Game/Scripts/WarSandboxCharacterPilotRuntime.cs`
- 原始记录：`Logs/AgentCharacterPilot/`；最终运行以 `build-receipt-02.json`、`player-*-audit-02.json` 为准。原“01”过程不能混作最终证据。

配方是**固定单角色、分阶段**的试点，不是通用万能导入器。已有输出受防覆盖保护；不要重跑当前Finalize入口覆盖已保存结果，再制作用新目录/版本。中间 `KayKitKnightVAT.asset` 仍承载共享Full子资产，**不要当作废文件删除**；当前渲染真正绑定的是 `KnightPaletteSafeVAT.asset`。

保护审计结果见 `保护审计.json`（若不存在则仍在整理，不可自行推断通过）。完整手动步骤见 `开始验收.md`。
