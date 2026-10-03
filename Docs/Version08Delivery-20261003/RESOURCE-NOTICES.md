# 资源、依赖与应附声明对应表

适用 Version08 GUID `eea0c353522542919393aa8b05eaaea0`。**来源成立、资源在工程、资源实际入包、声明已随最终包送达，是四个不同事实。** 当前为外置交付核对，不宣布完整发行手续完成。

当前包顶层未见 `ThirdPartyNotices`；不能把 09-30 包的 22 份声明当作已随本包附送。旧候选的 339/309 文件、15 包数量不复制为本包数字。当前包含 Burst `DoNotShip` 目录；留存原包，最终分发另建清单排除调试产物，不直接删除候选。

## 美术/字体对应

以下源件均是制作依据；运行包使用派生网格/材质/VAT/预览等，不将原始素材目录整包分发。实际构建路径与哈希见本轮机器清单，未确认项不靠文件名推断权属。

| 家族 / 当前内容链 | 许可依据 | 声明/发行动作 |
|---|---|---|
| 原男/女，RPG Tiny Hero Duo → 原 VAT/正式配置 | Dungeon Mason，Asset Store 225148；既有官方取得确认保留 | Standard Asset Store EULA；只能按适用嵌入范围，非 CC0，核开发席位，不重问订单 |
| Sazen 骷髅 → LegacySkeleton01 → Generated/LegacySkeleton01 | SazenGames，Asset Store 306857；既有官方取得确认保留 | 同类 EULA，不能将角色/脚本误标自有或 CC0 |
| UnityChan → ModelTrialSource/UnityChan → 原试点 VAT/统一库 | UCL2.02，源 License/UCL2.02 英日文本及标志指引 | 09-30 旧包“未含 UnityChan”不可延用。保留规定署名及原文/标志指引，并核最终可见位置 |
| KayKit 骑士/游侠/骷髅与动画 → Generated 各角色 | `CharacterPilotSource/KayKitKnight/{Characters,Animations}-License.txt`，KayKitRanger、KayKitSkeletons 同目录许可；Kay Lousberg，CC0 | 保留对应许可与来源；署名可推荐，不把自愿署名写成 CC0 强制义务 |
| 骑兵马、巨狼/公牛/蜘蛛派生件 → Cavalry01 / LargeBeasts | 各源目录 Animals-License.txt，Quaternius CC0 | 骑乘组合同时保留骑士/动画的 KayKit 来源；源目录中有 Spider 不等于其已入列 |
| 兽人/雪怪/蘑菇怪、恐龙、龙、巨兽等 | QuaterniusMonsters、NonhumanBatch2、QuaterniusDragons、QuaterniusGiants、QuaterniusGiants3 各 License.txt | 逐家族留来源原文；不要因某家族 CC0 将整个游戏 CC0 化 |
| Troops4 / Troops5 / PlatformerBatch6 | QuaterniusTroops4/5、QuaterniusPlatformerBatch6/License.txt；后者 SourceProvenance.json | 绿皮头/骷髅头隐藏但兼容资源仍可入包；不从许可表删掉 |
| 机器人 → Native03/Prepared04 → Generated/TowerDefenseRobot01 → Version08 | TowerDefenseRobotExpressive01/License-and-Credits.txt、SourceProvenance.json；固定提交 `8019f9267749ec7b9bb87b472b92e04a2dde4761` | Tomás Laulhé / Quaternius CC0；保留 Don McCurdy 的表情/转换/材质修改说明 |
| Perfect DOS VGA 437 | Zeh Fernando 作者直接许可记录：旧交付 LicenseEvidence/PerfectDOSVGA437-Author-Permission.md | 非标准 OFL/MIT；保留作者原文/出处，不改标公有领域 |

UnityChan 既有署名文本：`This work is provided under Unity-Chan License Terms. © Unity Technologies Japan/UCL`。最终发行前按 UCL2.02 原文与 Indication/Logo 指引落实，不以本表代替全部条款。

## 运行组件

| 组件 | 当前核对方法 | 应附材料 / 未决点 |
|---|---|---|
| Unity Player / Mono | 当前 boot GUID、Player DLL、MonoBleedingEdge、packages-lock；比对精确版本 | 旧证据的 Player/Windows/Mono/6000.3.14f1 专属 35 页声明；资格/席位与保护性终端条款仍需主体确认 |
| Autodesk.Fbx、BuildTestAssets、Unity.Formats.Fbx.Runtime | 以当前 Managed DLL 清单为准，不能因名字称“只编辑器不分发” | 同版本包许可与 FBX §1.1.5 原文；须在条款/About 类位置及相关版权文档落实 |
| WinPixEventRuntime.dll | 当前包 SHA256 与旧已鉴定同版本 DLL 比对 | 匹配后复用 1.0.240308001 MIT 及 ThirdPartyNotices，不自动套到未知版本 |
| D3D12Core.dll / d3d12SDKLayers.dll | 当前 SHA256 对精确 NuGet 1.618.1 依据 | DLL 专属二进制分发条件，**不是头文件 MIT** |
| libonigwrap / Oniguruma | 当前实际入包与旧字节对应依据比对 | MIT/BSD 通知；原映射是 Onigwrap 1.0.10，未知变化不得套旧结论 |
| Unity 包及传递依赖 | 当前 packages-lock、Managed DLL 和构建 Used Assets，三者含义分开 | 按匹配 PackageCache 的许可/嵌套声明留全文；lock 中存在不等于运行包包含全部功能 |

本轮已完成的精确核对见 [EVIDENCE-INDEX.md](EVIDENCE-INDEX.md)：10 个组件与旧鉴定字节相同，Burst 生成件不同；当前 15 包/27 份包级通知及其他材料共 68 份原文副本已放在本资料的 ThirdPartyNotices。构建确认 LargeSpider04、UnityChan 与隐藏头部兼容资产入包；不根据历史候选状态排除。

精确上游原文与旧版本鉴定见 `Docs/V1Candidate-20260930/许可复核结论_20260930.md`、`LicenseEvidence/README.md`、`版权声明候选.md`。以上是复用依据，不声称旧声明已经复制进本包。若最终删 DLL 或改构建配置，必须新构建、新 GUID、相应烟测。

## 发行未决事项

1. 最终分发目录/ZIP尚未组装：应附许可原文、UnityChan 指示、FBX 声明位置、引擎与系统通知需实际可达，不只是工程内链接。
2. 主体、名称/渠道、有效 Unity/开发席位及终端条款/接受方式待负责人决策；不索取无关私人账号信息。
3. 当前 Development 候选还是新正式构建待定；包的 DoNotShip 调试内容不得混作必要运行件。
4. 本轮为证据对应，不作无条件法律保证或替用户接受协议。
