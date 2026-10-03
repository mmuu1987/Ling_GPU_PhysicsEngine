# Version07 质量修正（2026-10-02）

> **历史专题 / 入口提示（2026-10-03）：** 本页保留该阶段原始记录；当前正式入口已由 Version08 取代，见[工程导航](../Engineering-20261003/README.md)与最新机器人交付记录。下文“当前”“下一步”及构建命令不作为现在的执行清单。旧手工测试仍暂停，其他模型转换未授权；技术结果与人工认可范围按原记录保留。


## 结论

按照用户批准的顺序已完成：原件完整性对照 → 两个不合适头部候选非破坏性退出正式选择 → 指定的战场目录详情右侧真实全身静态图 → 内容适配门禁与现有候选初筛。**没有新增模型或重烘旧角色，人工试玩仍暂停。**

新入口：`Builds/OfficialRoster-20261002-02/Start-WarSandbox.cmd`。

- 内容：`Assets/Game/OfficialRoster/Version07`；GUID **`123ae118f18948b39d72750931f5b7ce`**，Windows64 Development。
- 原 Version06 与 `Builds/OfficialRoster-20261002-01`逐文件哈希保持，旧源FBX、生成角色、既有配置和Version01–06只读。
- 新版保留30个旧战场身份，正式卡片显示28个；保留67个模板身份，正式兵种库显示65个。身份保留是兼容要求，**不是新增数量验收**。
- 隐藏`troops6-enemy/skull`和`roster-platformer-enemy/skull`，不删除资源、不改变revision、不限制旧身份解析。隐藏场景仍在构建内用于兼容。
- 蟹怪保留现有地面近战候选，不开发额外飞行/技能。

## 真实预览

- 用户第4张截图标出的战场目录详情区：左侧继续显示战场总览，右侧显示当前条目实际配置的代表单位全身图。
- 点击条目同步图片，箭头切换实际部署/本场可选代表兵种，点击图片或`+`放大全身静态图。
- 兵种库大窗口增加同图侧栏；小窗口通过“查看模型”打开大图。
- 使用已有游戏Mesh/材质/贴图/Full-LOD GPU VAT待机帧；统一三分之四视角和光照，透明边距、等比适配，没有AI概念图或重新绘制。
- 33个实际渲染资源支持65个可见模板（相同渲染配置复用）；每个新画像448×560。另保存Full四动作各3时刻、正面/侧面、中远LOD待机图。
- 首轮UI回调通过后，实看640 PNG发现旧布局节流裁切；已增加尺寸变化即时重绘，并修正放大按钮宽度。最终`ui-02`图确认1280/640布局与全身放大。

## 原件与适配

原件全部三角形与游戏完整LOD：Crab 3624/3624、Enemy1968/1968、Skull1804/1804。实际原件/游戏图片均已查看：绿皮与骷髅原件就是头部主导造型，不是漏导身体。

技术检查不能自动签收艺术。标准与判断分别见：

- [CONTENT-SUITABILITY.md](CONTENT-SUITABILITY.md)：完整原件、轮廓、动作语义、材质、角色、真实预览。
- [VISUAL-REVIEW.md](VISUAL-REVIEW.md)：代理画面复核、撤回理由、蟹怪/毒蛙/猴獴动作初筛和剩余暗色对比/风格差异。

## 本轮实际证据

全部在新的`Logs/AgentContentQuality/`，未覆盖旧证据。

| 项目 | 实际结果 | 证据 |
|---|---|---|
| 原件与现有游戏Full-LOD | 3种、48张有效源/游戏图；Unity exit0；4750受保护路径不变 | `source-03/source-review.json`、`receipt.json` |
| 新目录与图 | Prepare07成功；33实际渲染资源；30身份/28卡片、67模板/65可见 | `prepare-01/quality-report.json` |
| 准备阶段保护确认 | 只有授权的EditorBuildSettings指向V07；31场景精确匹配，其余旧包/模型不变 | `prepare-confirm-01/receipt.json` |
| 最终定向EditMode | **43/43，0失败/跳过**，4872路径不变 | `edit-02/results.xml`、`receipt.json` |
| 最终真实UI PlayMode | **1/1**（多场景/按钮/大小布局/图同步/旧ID检查），0失败/跳过；4872路径不变 | `ui-02/results.xml`、10张PNG、`receipt.json` |
| 新独立程序 | Build07成功、旧包及资源不变 | `build-01/receipt.json` |
| 新程序正式入口冒烟 | **28/28**通过卡片进入、开战、重置、返回；28次释放；PID16348 exit0、completed/passed、errors空；隔离设置未写 | `player-01/receipt.json`、`report/report.json` |

失败/中间证据保留：`source-01/02`为原件捕获fixture的URP-Unlit启动问题，改用临时中性几何shader才取到有效图；没有改源模型。`prepare-01` Unity exit0且制作成功，但运行器继承清单误把授权的Build Settings改动判失败；修正运行器并独立确认，只读确认，没有再次Prepare/烘焙。`ui-01`绿灯不能代替画面复核，其小窗口问题已据PNG修正。

## 边界与下一步

- 自动回调/截图/渲染/冒烟不是物理键鼠、连续观感、全部自然对局、深度平衡、100k性能或V1签收。
- 人工试玩与进一步扩充仍暂停，不要求用户重新做旧三角色清单。
- 继续按具体内容问题收口：暗色单位对比、连续动作/角色适配与跨风格问题；没有把所有旧候选统一标为“美术合格”。
- 既有review R1（候选继承19模板基线）、R2（旧方案fixture Build Settings外部恢复）、R3（旧战场fixture固定证据路径）未在本轮顺手修复，也未运行可能覆盖历史证据的旧fixture。
- 尚未提交/推送，不使用`git add .`，不动`Assets/pelican-cycling.svg`和`.meta`或`.local-mcp-audit`。所有本轮自有Unity/玩家任务已结束。
