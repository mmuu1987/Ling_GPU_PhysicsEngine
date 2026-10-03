# 第 6 批准备：鸟人、兔人、鱼人（2026-10-02）

> **历史专题 / 入口提示（2026-10-03）：** 本页保留该阶段原始记录；当前正式入口已由 Version08 取代，见[工程导航](../Engineering-20261003/README.md)与最新机器人交付记录。下文“当前”“下一步”及构建命令不作为现在的执行清单。旧手工测试仍暂停，其他模型转换未授权；技术结果与人工认可范围按原记录保留。


> **状态补充：** 本文是原候选等待来源的记录。用户继续推进后，作者另一官方CC0包的蟹怪／绿皮小怪／骷髅头已真实烘焙、集成并通过对局；当前成果见 [实际接入记录](../PlatformerBatch6-20261002/README.md)。二者不是相同模型，不相互冒充。

## 当前结论

**第六批制作入口已准备；官方源文件下载被 Google Drive 配额阻塞。没有烘焙、没有战场集成、没有新试玩包。**

正式入口仍为 `Assets/Game/OfficialRoster/Version05`（27 个战场），试玩包仍为 `Builds/OfficialRoster-20261001-08`。本轮没有修改 `OfficialRosterBuilder`，不把缺资源的 Version06 设为当前入口。

## 本批候选与范围

沿用此前用户的“模型／兵种数量优先”，从同一作者资源包中选择尚未进入 Version05 的三种身体。三者先使用现有地面近战逻辑；不增加飞行、跳跃、毒伤、技能、内核或新玩法。

| 官方源名 | 暂定名称 / ID | 输出名 | 初始体型 / 半径 | 数值草案（HP / 攻击 / 移速 / 展示人数） |
|---|---|---|---|---|
| Birb.fbx | 鸟人 / roster-birb | Birb01 | 1.7m / 0.45m | 75 / 9 / 5.5m/s / 100 |
| Bunny.fbx | 兔人 / roster-bunny | Bunny01 | 1.8m / 0.45m | 90 / 11 / 6.5m/s / 80 |
| Fish.fbx | 鱼人 / roster-fish | Fish01 | 2.0m / 0.60m | 150 / 15 / 2.8m/s / 55 |

以上是**未验证的初始提案**，不是发布数值或平衡结果。动作、实际尺寸、蒙皮和 VAT 精度都尚未经过本批验收。

## 来源核对与下载阻塞

- 作者页面：<https://quaternius.com/packs/ultimatemonsters.html>，页面将此包标为 CC0。
- 该页面实际链接的公开根目录：<https://drive.google.com/drive/folders/18m4KpzpEzhC9wl7jzr6dUc0N8Jozr79C>。
- 根目录 → Big → FBX：<https://drive.google.com/drive/folders/1JvPqInPVfD3HW-iHpSQJtVAM-XIjB9Aa>。
- 从公开目录元数据核对的文件身份：
  - Birb.fbx：`1niXx4k7g6jYGsjRyqqTFXUw6ooPwYIuB`
  - Bunny.fbx：`1nb1QIWzgIZ38Vk8bTTal2N723IK9wC1c`
  - Fish.fbx：`1kq1uEeXs9Njmh_dhl-V8weddPXpapptA`
- 本会话环境与开发机的正常匿名下载都返回 HTTP 200、`text/html`、`Google Drive - Quota exceeded`，不是 FBX。没有绕过配额、没有持续重试，也没有下载付费或来源不明的替代资源。
- 开发机证据：`Logs/AgentTroops6/source-download-01.json`，三个文件 `accepted=false`，`published=false`；源资源目录没有发布。
- 同包已有的原始 `License.txt` 指纹为 `de990ef6fc68cffd7fd1ae342c4d0c823b541b8848d8f76bca5d3339f4de6f6e`。其原文标题仍是 Ultimate Platformer Pack，不改写标题；包关联由作者页面与同级 Drive 目录建立。新下载成功时只复用这份同字节原文。此结论仅针对新候选来源，不代表全工程发行许可签收。

## 已准备的入口

- `Assets/Game/Editor/CharacterPipeline/Troops6Builder.cs`
  - `ValidateSources()`：在改导入设置或新建资源之前确认源文件存在、不是 HTML、获取回执为成功、公开文件身份正确、内容与回执 SHA-256 一致、原始许可指纹正确。
  - `Inspect01()`：待源文件取得后核对 Generic 导入、骨架、四动作和原始尺寸。动作缺失会拒绝，不以“跳过”冒充成功。
  - `Prepare01()`：复用第 5 批已验证的常规怪物规范化／四动作／角色管线，仅写本批新路径。实际源模型仍须通过原姿态 0.25mm 与 VAT 2.5mm 等既有门槛。
  - `Integrate01()`：模型报告通过后扩展第 5 批只读目录和 19 模板策略；缺烘焙结果时不会先创建一个半成品集成目录。
  - 不提供删除历史产物的入口，不改正式目录或旧版本资源。
- `Assets/Game/Tests/EditMode/Troops6PreparationTests.cs`：只覆盖准备阶段的身份／路径、已有策略半径约束、HTTP-200 HTML 拒绝、FBX 头部预检、缺源时零输出、Version05 保留。
- 下载与受控检查脚本位于 `Logs/AgentTroops6/`。每次尝试使用新的证据编号，保留 01 的失败回执，不覆盖旧记录。

FBX 头部识别只是拒绝错误网页的预检，不是完整 FBX 验证；获取回执指纹也不能替代独立固定指纹和真实模型验收。

## 本轮验证

准备阶段编译与 EditMode 检查：**8/8 通过，0 失败、0 跳过，Unity 退出码 0**。运行器清单中 3,051 条受保护路径的指纹未变化，`changedSourceFiles=[]`。仅检查准备代码，**不代表模型、GPU 烘焙、对局或发布验收**。

日志／结果／保护回执：`Logs/AgentTroops6/preparation-edit-01*`。运行器只启动本工作树的一次 Unity，使用图形设备、不加 `-nographics`，恢复本次产生的 TimeManager 序列化变动，并核对既有受保护文件；不停止其他 Unity 工程。

## 源文件可用后的续做顺序

1. 正常重新下载本批三个官方文件。使用新尝试编号（02 等），不要覆盖 `source-download-01.json`。或由用户提供同来源的原始文件后重新做获取与许可核对。三者全部合格才发布到 `Assets/CharacterPilotSource/QuaterniusTroops6/`；不把 HTML 改扩展名当 FBX。
2. 将成功获取的 SHA-256 固定到独立源文件测试；确认 `SourceProvenance.json` 和原始许可。不要把当前失败网页的指纹写成模型指纹。
3. `Troops6Builder.Inspect01` → 检查四动作与尺度。然后 `Prepare01`，读取三个独立报告；不为未通过的模型放宽精度门槛或改旧模型。
4. `Integrate01`，补本批真实 GPU／对局／保存重载测试与新三场预览。预计扩展到 22 个常规模板、26 个集成条目，**尚未实际生成**。
5. 三模型与新场闭环通过后，再扩展 `OfficialRosterBuilder` 的 Version06 / Prepare06，保留 Version01–05 与旧预览，制作新日期的独立试玩包。预计正式目录 30 个战场，**不是当前发布状态**。
6. 只对新内容做必要验证和旧入口回归，不重开已验收的玩法、Mod 或 100k 性能专项。旧试玩包、个人存档、`Assets/pelican-cycling.svg` 及 `.meta` 不动。

本轮没有提交或推送。提交时只显式添加本批路径，不能 `git add .` 或直接推 `main`。
