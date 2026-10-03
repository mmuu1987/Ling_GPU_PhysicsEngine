# 第六批实际接入：蟹怪、绿皮小怪、骷髅头（2026-10-02）

> **历史专题 / 入口提示（2026-10-03）：** 本页保留该阶段原始记录；当前正式入口已由 Version08 取代，见[工程导航](../Engineering-20261003/README.md)与最新机器人交付记录。下文“当前”“下一步”及构建命令不作为现在的执行清单。旧手工测试仍暂停，其他模型转换未授权；技术结果与人工认可范围按原记录保留。


## 结论与当前边界

原计划 Birb／Bunny／Fish 的 Drive FBX 与作者 Blend 输入仍受配额限制。本轮没有绕过配额，也没有用不同模型冒充它们；改从**作者本人提供的另一个官方 CC0 免费包**取得三个新的地面敌人。

三个真实模型已通过原姿态转换和 VAT 管线门禁，生成独立单位、四动作、色板和三张战场；新集成库共 **26 个条目、22 个常规模板**。资源／目录检查 **8/8 通过**。

**最新质量修正：当前入口已为 Version07，`Builds/OfficialRoster-20261002-02/Start-WarSandbox.cmd`，GUID `123ae118f18948b39d72750931f5b7ce`。** 原Version06 / 30身份及`20261002-01`包不改写；新版保留身份但正式显示28战场，撤出绿皮小怪/骷髅头的新正式选择，并增加实际游戏模型全身静态预览。用户人工试玩与进一步扩充仍暂停，不再以旧清单为当前下一步。技术测试不等于美术合格；标准、实际证据与剩余问题见 [质量修正记录](../ContentQuality-20261002/README.md)。

以下第六批接入/Version06成果按历史保留：该包GUID `e6079bf250524a299c663e6cd558d176`，此前30场冒烟与跨进程方案验证通过。Version01–06工程资源保留；此前已按明确授权清理的`20261001-01`至`-08`包和部分根ZIP未恢复。本轮没有再次清理旧包或要求人工测试。

## 实际来源

- 作者页面：<https://quaternius.itch.io/ultimate-platformer-pack>，明确 CC0，提供免费“ No thanks, just take me to the downloads ”入口。
- 未登录、未付款、未购买 Patreon 资源；只使用作者提供的普通匿名免费下载。
- 文件：`Ultimate Platformer Pack by Quaternius.zip`，公开 upload ID `4975456`。
- ZIP：18,863,631 字节，SHA-256 `2d0cac0f3cb58f6845f779a4c6b4a92be6fa27d118ee0b976ead55c6834a53d4`。
- 只选择 `Ultimate Platformer Pack - Dec 2021/Enemies/FBX/` 下 Crab、Enemy、Skull 和根许可，不执行或导入整个包中的其他内容。
- 原始 `License.txt` SHA-256：`de990ef6fc68cffd7fd1ae342c4d0c823b541b8848d8f76bca5d3339f4de6f6e`。标题与原文字节不改写。

| 输入 | 字节 | 固定 SHA-256 |
|---|---:|---|
| Crab.fbx | 559036 | edc99b2dc232e2b96a571b9d182fa8de38e269efa18d551efcfc86f4ff0ae49b |
| Enemy.fbx | 530588 | ac2d8fb64dedde1c21e674096fe905a3ceaa7c39b25cc70326cc79beecad7867 |
| Skull.fbx | 401500 | 05007750febc24323f400b7e7cdada6d124b6646831e07884323cc10cd1f6daa |

工程原件：`Assets/CharacterPilotSource/QuaterniusPlatformerBatch6/`。`SourceProvenance.json` 明确记录 `author_itch_free_archive`，不声称从 Google Drive 下载了原计划的三个 FBX。

## 制作与门禁

- 入口：`Assets/Game/Editor/CharacterPipeline/PlatformerBatch6Builder.cs`；输出在 `Assets/Game/PlatformerBatch6/Prepared01/`。
- 原件均为 Generic、无刚性附件和 Blendshape；骨骼分别 7 / 7 / 5，原始四动作选 `Idle`、`Walk`、`Bite_InPlace`、`Death`。
- 复用已验证的静态导入尺度规范化和四动作位置对照。原模型多材质纯色，用已有大型兽形方法生成本批私有 4×4 线性色板，保留材质分界、顶点映射、权重、法线和动画；不强套旧怪物图集。
- 原姿态 → 私有准备模型仍执行 0.25mm 门槛；准备模型 → VAT 仍执行 2.5mm 门槛，没有改公共管线或放宽容差。
- 第一次 `Prepare01` 已完成蟹怪，但小怪 Full 只有 1491 顶点，低模预算 1600 不满足“必须小于 Full”的既有规则。此为新配方参数不匹配；保留失败回执和通过的蟹怪，只将小怪／骷髅头预算调为 1000，`CompleteRemaining01` 续做，不重烘或覆盖蟹怪。

| 模型 / 产物 | 身高 / 半径 | Full / Low | VAT 最大误差 | 帧数 / 实际位置对照 |
|---|---|---|---|---|
| 蟹怪 / PlatformerCrab01 | 0.75m / 0.75m | 2489 / 1585 | 0.649mm | 100 / 248900 |
| 绿皮小怪 / PlatformerEnemy01 | 1.0m / 0.75m | 1491 / 998 | 0.834mm | 100 / 149100 |
| 骷髅头 / PlatformerSkull01 | 1.05m / 0.75m | 1358 / 964 | 0.821mm | 100 / 135800 |

每种 48 张 GPU 图片、16 张独立位置参考；共 144 / 48。参考仍共享拓扑、UV、法线和 GPU shader，不能冒充原生源材质独立验收。已查看实际近景待机预览；最终人类观感未签收。

## 战场和数值

- 新库：`Assets/Game/PlatformerBatch6/Prepared01/Integrated/Catalog.asset`。
- 策略只读继承第 5 批 19 模板，增加三个模板到 22；旧模板身份、上限与骑士军团配置不变。
- 新条目 `troops6-crab` / `troops6-enemy` / `troops6-skull`；模板 `roster-platformer-crab` / `roster-platformer-enemy` / `roster-platformer-skull`。
- Unity 场景：同目录 `crabBattlefield.unity` / `enemyBattlefield.unity` / `skullBattlefield.unity`。仅使用已有地面近战，无飞行、跳跃、技能、毒伤或新内核。
- 初始提案：蟹怪 HP90/攻12/速3.5/100人；小怪 HP110/攻12/速4.5/75人；骷髅头 HP85/攻10/速5/95人，均对旧 80 骑士军团。首版功能值，不做深度平衡承诺。

## 本轮验证

- 原件传输、导入／四动作检查、续做烘焙、库与场景整合均完成；阶段回执核对 3,051 条既有受保护路径无变化。
- `PlatformerBatch6CatalogTests`：**8/8 通过，0 失败、0 跳过**；独立固定输入指纹、原管线报告／精度、私有配置与色板、策略扩展、旧模板身份、场景 YAML 接线和初始部署校验。运行器 3,050 条受保护路径无变化。
- `PlatformerBatch6BattlefieldTests`：**PlayMode 1/1 通过，覆盖三个真实自然对局**；三场双方均造成伤害、自然结束、峰值网格溢出为 0。使用 1/30s 固定步长、正常默认进攻，不改兵力／HP、遥测或固定胜者。

| 战场 | 自然胜者 | 模拟秒 | 剩余本批 / 骑士 | 本批掉血 / 骑士掉血 | 峰值网格溢出 |
|---|---|---:|---|---|---:|
| crab | troops | 20.5 | 49 / 0 | 5690 / 8000 | 0 |
| enemy | troops | 19.0 | 41 / 0 | 5000 / 8000 | 0 |
| skull | troops | 16.5 | 63 / 0 | 4430 / 8000 | 0 |
- 证据：`Logs/AgentPlatformer6/`；真实烘焙报告在各 `Assets/Game/CharacterPipeline/Generated/Platformer*01/PipelineReport.json`，图片目录由报告的 `evidence` 给出。

## 历史下一步（已完成，勿重复执行）

以下为整合完成时的历史续做计划：补保存重载／菜单入口和预览，再接正式Version06并构建独立试玩包。**这些步骤现已完成，见下方发布记录；不要再次运行Prepare或覆盖既有输出。** 原Birb／Bunny／Fish提案继续作为待正常来源的独立提案保留，不把本批三个名字回填成它们。

正式Version01–05的工程资产、当前有效试玩包、个人存档和 `Assets/pelican-cycling.svg` 及 `.meta` 保持保护；旧可运行包的先前授权清理见当前交接，不授权后续自动清理。尚未提交或推送。需要提交时显式选择本批路径，不使用 `git add .` 或直接推 `main`。

## Version06 发布与验证补充

- 新集成菜单：`Assets/Game/PlatformerBatch6/Prepared01/Integrated/Menu.unity`，保持独立，不覆盖旧入口。
- 双进程方案循环：`plan-seed-02`（Unity PID15284）与 `plan-reload-02`（PID1416）均 PlayMode 1/1 通过；三个真实方案的90/65/85兵力、本局HP108/132/102、稳定模板ID与文件指纹跨进程保持，载入后分别实际完成自然对局。只用 `Logs/AgentPlatformer6/plan-cycle-02` 隔离数据，未触碰玩家目录。
- 首次 `plan-seed-01` 失败为测试前提问题：在 Play 内修改 EditorBuildSettings 不会更新已启动播放器的场景表。已用 IPrebuildSetup 在进入 Play 前接线，并由运行器恢复原设置。未为错误测试修改 SceneSession 或引擎；失败证据保留。
- 正式目录 Version06：30场；三张新预览来自此前真实默认部署对局图，只做1280×720到1280×592中央裁切；旧预览逐字节复用。`OfficialRosterCatalogTests` 发布阶段9/9，随后防覆盖修复检查10/10，含Version01–05保留与模板身份兼容；这是两轮既有记录，不是本轮新测。
- `OfficialRosterBuilder.Prepare06 / Build09` 成功，输出 `Builds/OfficialRoster-20261002-01`。旧版本准备入口拒绝覆盖，EditorBuildSettings 有意指向新正式菜单。Build回执受保护路径无意外变化。
- 新独立程序 `official-catalog`：实际经 uGUI 卡片进入全部30场、开战、重置、返回，30次释放，零错误，约239秒；不是30场完整对局或性能采样。
- 新独立程序 `readability`：49项720p／640×480界面、射线与输入回调检查通过；不是实际OS键鼠操作或人类观感验收。
- 开发工具新增显式 `--war-sandbox-review-global-file=` 隔离全局数值存储，普通启动无此参数则原行为不变；冒烟还隔离方案与设置，设置文件未写入。
- 证据：`Logs/AgentPlatformer6/official-prepare-06-01*`、`official-edit-06-01*`、`official-build-06-01*`、`smoke-official06-01/`、`smoke-readability06-01/`。
- 发布阶段没有提交或推送。当前已由用户明确选择先人工试玩；后续若另行授权模型扩充，使用新输出名和当前完整继承基础，不重复烘焙已通过的三个角色。

## 2026-10-02 接手复核与人工试玩（当前下一步）

- 接手review：[REVIEW-20261002.md](REVIEW-20261002.md)。本轮为静态源码审查与已有XML／回执核对，未运行Unity或修改C#、资源、试玩包、个人数据。
- 既有修复后EditMode记录为OfficialRosterCatalogTests 10/10、PlatformerBatch6CatalogTests 9/9、Troops6PreparationTests 8/8；与早先制作／发布阶段结果分开记录。
- 三项续做／复跑风险已记录但未修：原候选入口仍继承V5；方案fixture恢复构建列表依赖外部运行器；战场fixture固定输出名会覆盖历史证据。不应将记录风险写成已完成修复。
- 用户已选择先人工试玩，执行 [MANUAL-PLAYTEST-20261002.md](MANUAL-PLAYTEST-20261002.md)，反馈真实键鼠、三种新角色动作、自然对局及新编号方案跨重启载入。状态仍是待人工执行，未代签验收。
- 不重复已经完成的菜单／保存重载／Version06发布，不自动新建下一批，不重烘已通过角色，不启动无关Mod或100k压测，不继续继承历史删除授权。
