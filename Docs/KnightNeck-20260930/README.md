# 骑士 LOD0 头颈断开：03 修复记录

日期：2026-09-30。用户指出“lod0的脖子好像是没了”；此前我漏看了真实的头/躯干间隙。01/02 的可见性、颜色和运动检查不能证明身体连接正确，旧报告的外观结论以本次纠正为准。

## 结果与入口

- 新独立包：`Builds/CharacterPilot-Knight-20260930-03/Start-Knight.cmd`。
- GUID：`37bb596ded554f60b4ddd8d0a77331d8`，骑士模板 revision3；旧 01、02 和已接受主包保留。
- 首先看同目录 `修复结果.html` 的同机位 LOD0 对照，再近距离看 Idle、Move、Attack、Death。最终人工观感仍待用户确认。
- Full 6666 顶点，Low 1337 顶点 / 2299 三角形，114 帧；预算未扩大。

## 根因，而非补脖子

原始模型完整。源模型的 BakeMesh 与独立骨骼权重/绑定矩阵计算一致；准备后的层级 `ScalePivot=.7077963` 中，实测 `BakeMesh(mesh,false)` 再乘 renderer→root 矩阵会让各蒙皮部件重复缩小。各自局部原点不同，头和躯干出现间隙，武器附件则不受同样缩小影响。旧 VAT 忠实保存了错误的准备阶段几何。不是 LOD 减面问题，也不是作者漏做脖子。

修复在**私有骑士配方**内：临时实例单位缩放 → 用现有生产 VatBaker 烘焙 Full → 对完整位置纹理、clean mesh 和 bounds 做一次统一缩放/平移 → 再生成保色块 Low。未单独下移头部、补面、换骨架，也未修改通用 VatBaker 或战斗核心。

新资产：`Assets/Game/CharacterPilotPlayable/NeckFix03/KnightCanonicalFull.asset` 与 `KnightNeckSafeVAT.asset`。旧资产保留，不删除旧 profile 的被引用子资产。

## 全帧几何门禁

独立线性蒙皮使用原准备层级的 bones、bindposes、boneWeights 和 renderer→root；剑盾使用刚性挂点变换。每个动作每一帧、每个 Full 顶点均与最终纹理读回对照，总计 **759,924** 个位置样本。下表单位为毫米；它不是只在几张截图上量距离。

| 动作 | 帧数 | 旧最大误差 mm | 新最大误差 mm |
|---|---:|---:|---:|
| Idle | 32 | 280.29 | 1.169 |
| Move | 25 | 295.82 | 1.194 |
| Attack | 32 | 309.84 | 1.180 |
| Death | 25 | 582.31 | 1.185 |

门限 2.5mm，实测最大约 1.194mm（RGBAHalf 存储量化）。Low 是有损减面，不套用 Full 的逐顶点误差合同。Low 色块跨区聚类 0、非有限纹理样本 0。

## GPU 与玩家运行

- 强制 LOD0/1/2 × 四动作 × 四采样，共 48 张真实 GPU 图；12 组可见/非粉色/非冻结检查通过。
- 另留 Idle、Attack、Dead 的同镜头、同灯光、同采样时间前后对照，共 6 张。报告的并排放大图使用相同裁切，不是重新生成的模型示意图。
- 数学对照与截图互补：不把“有运动/非粉色”当作颈部正确的充分条件。
- 玩家闭环和保护审计结果另见 `验证摘要.json`；原始报告在 `Logs/AgentKnightNeck`。自动回调不代表真人键鼠操作验收。

## 修改边界与复现

新增只读诊断 `KnightNeckProbe.cs`、私有修复构建器 `KnightNeckFixBuilder.cs`；旧私有 reducer 只增加可选报告路径，避免覆盖旧证据。只重绑骑士 Render、改私有 Catalog/菜单的 revision3。数值、近战、镜头、共有 LOD 距离、旧角色/主场景未改。

配方 `Prepare`/`Build` 要求全新输出路径；本次已执行，**不要直接重跑到现有 03**。将来重试须使用新资产/包/日志编号。原始诊断与旧包都作为历史保留。修复器依赖现有准备层级的正向均匀缩放，不能推广为非均匀/负缩放通用修复。

## 仍然保留的边界

本轮没有 100k 压测、提交或推送；串行低优先级，功能测试配置上限 30FPS，不是性能达标证明。近/中阈值 30/120 不变，试点缩放上限 70，Far 仅强制渲染验证。网格溢出提示、25m 搜敌设置受到约 12m 轴向 shader 搜索上限仍保留，不扩张到新专项。

KayKit 模型/动作 CC0；完整游戏仍适用其他依赖条款。随包保留 KnightLicenses、ThirdPartyNotices、ExistingLicenseReview。既有许可资料及 02 报告是历史，不代表整款游戏 CC0 或已完成 EULA/法律/V1 放行。`humanAcceptance=false`。


版本兼容边界：本轮验证03方案在03中的跨进程保存/重载；未验收02方案到03（revision3）的迁移，也没有改写已有人工KnightPilotPlans。
