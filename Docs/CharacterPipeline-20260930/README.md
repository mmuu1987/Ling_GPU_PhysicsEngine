# 角色接入流程第一版：同骑士04复现

2026-09-30。结论：新增一个由配方驱动的Editor入口，复用现有生产烘焙/绑定/GPU绘制工具，已生成全新Knight04资产与独立玩家包。没有增加第二个角色、没有修改通用引擎或战斗核心。03及旧包保留。真人窗口操作与最终外观仍待确认。

## 入口与重复使用

Unity菜单：`MassEngine > Character pipeline > Open workflow`。骑士示例：`MassEngine > Character pipeline > Select Knight example`。

配方：`Assets/Game/CharacterPipeline/Recipes/Knight.asset`。
本次产物：`Assets/Game/CharacterPipeline/Generated/Knight04/`。

1. 选择配方，或点击“新建空白配方资产”。输入**已准备好的Generic蒙皮模型**，指定现有Idle/Move/Attack/Death；动画路径必须已经匹配。模型方向应为+Z。
2. 可指定一个**直接子级**为尺寸根；所有渲染器须在其内部，只允许该节点携带正向均匀缩放。空路径表示没有尺寸根、其余节点均单位尺度。所有启用的刚性MeshRenderer附件必须逐一填写精确相对路径，不会猜测手部挂点。
3. 设定目标可见蒙皮绑定高度、是否按绑定姿态脚底归零；**碰撞半径单独配置**。本次目标1.8m、半径0.55m，继承既有骑士数值，不自动修改攻击距离、阵型密度、运动节奏或镜头。
4. 选择Low策略与预算。骑士使用4×4 PaletteGrid，1400顶点上限；任意纹理不能冒充调色板。FullOnly和StandardClustering接已有接口，但本轮端到端验证的是PaletteGrid骑士，不把其他策略/陌生角色写成已全部验收。
5. 先选**从未使用的新输出名**，再点击“仅检查”，通过后生成。`Knight04`已经存在，正确行为就是拒绝覆盖；下次可用`Knight05`等新名字，无需改C#脚本。
6. 打开输出的检查图/人工清单。通过自动门禁仅得到`ready-for-human-review`，不自动替人签收。
7. 可选生成独立试点：给出已有菜单、战场和单入口Catalog模板。核心会复制场景、创建新的Scenario/Catalog及兵种子配置，只绑定新副本。不会重绑现役阵容。示例菜单/战场模板来自03；这不是从零生成任意游戏场景的工具。

旧Knight一次性Prepare/Finalize/Build脚本保留作历史诊断，**新制作请走上面的入口，不重跑旧脚本覆盖旧证据**。旧通用VAT窗口没有被重写；本轮的缩放保护在新配方入口内。

## 生成与失败处理

单位尺寸临时实例 → 生产VatBaker Full → 对完整位置纹理、clean mesh、bounds统一缩放/平移一次 → 全帧独立位置比较 → Low → 私有兵种与GPU检查 → 可选私有试点 → 配方快照/报告。

- 新输出目录先保留本次GUID，任何异常只删除仍属于本次创建的目录；旧输入/旧绑定不动。
- 异常日志保留在独立Logs目录，不把失败结果伪装成成功；已经成功的目录不能被重跑覆盖。
- 复制Unit及Spawn/Movement/Flocking/Animation/Combat/Render，材质仍可只读引用模板；不修改共有材质。
- `RecipeSnapshot.asset`保存本次配方，报告记录源prefab GUID/依赖指纹与尺寸/预算/门禁；本次独立包另有完整构建源SHA记录。
- 当前为同步、有限预算的Editor操作；不要同时启动多个烘焙/构建。第一次脚本/着色器编译可能较慢，生成过程中等待完成。

## 明确拒绝的输入

非均匀/负缩放、未声明的非单位层级尺度、尺寸根/模型根动画、动画尺度变化、未匹配路径或非Transform/Object交换动画、缺少动作、未声明/重复/外部附件、缺失骨骼/无效权重、超过4权重、Blendshape、Humanoid重定向、模型上执行脚本、超预算或已存在的输出。

不是自动绑定陌生骨架、自动重定向、自动布置武器、自动网格修补、非均匀缩放支持或大型生物战斗适配。本轮3.6m仅做前置参数独立性检查，没有生成/试玩大型单位。

## 04几何与图像证据

Full **6666** / Low **1336** 顶点（2297三角形），114帧；位置+法线纹理合计 **17.8125MiB**，不是总GPU内存。全部759,924个Full顶点帧样本与独立骨骼蒙皮计算比较：

| 动作 | 帧数 | 最大位置误差 mm |
|---|---:|---:|
| Idle | 32 | 1.170 |
| Move | 25 | 1.194 |
| Attack | 32 | 1.180 |
| Death | 25 | 1.185 |

门限2.5mm，最大约1.194mm。04与已修好的03 Full位置最大差 **0.03052mm**；Full三角索引和UV逐项一致。

48张真实GPU结果图（3强制LOD×4动作×4采样）和16张源位置参考图。参考位置由独立bones/bindposes/weights计算，再通过**同一VAT GPU shader绘制**；拓扑/UV/法线共用，不能冒称独立原材质或原生SkinnedMeshRenderer验收。它是位置几何对照，不是AI生成示意图。报告图片为相同裁切放大。

## 故障门禁与一次测试前提纠正

缺动作、错路径、漏附件、非均匀/负缩放、动画缩放、Blendshape、超预算、非法输出路径均验证拒绝。故意在Full落盘后抛异常，确认新目录及meta回滚；故意将位置改坏100mm，独立几何门禁拦住；成功04不能再次覆盖。

初次脚本末尾把Low数量写死为1337而失败，**不是烘焙核心失败**：新配方按目标1.8m重算因子0.99999994，聚类边界少1顶点，实际1336、预算1400、跨色块0、非有限样本0。按工程约定修正测试前提，未为凑1337修改算法或重做已成功的04。首轮失败回执/日志保留；随后只读验证通过。`verification-04.json`含先前实际执行的检查与后续验证，不把它称作22个独立NUnit测试（其中含重复覆盖检查与说明）。

## 独立玩家包

入口：`Builds/CharacterPipeline-Knight-20260930-04/Start-Knight.cmd`。
GUID：`3d6f7918074743de81660dead2658cb2`。
新Catalog：`character-pipeline-knight`；新模板：`pipeline-knight` revision1，不改变03的ID/revision。

默认64v64，配置限帧30FPS。自动测试使用Logs下隔离方案/设置；手动试点方案使用`CharacterPipelinePlans`，不复用旧`KnightPilotPlans`；启动CMD的设置在本包PilotData。04是独立试点命名空间，不做旧03方案迁移。

运行与保护结果见`验证摘要.json`及`Logs/AgentCharacterPipeline`回执。未把自动回调当物理键鼠验收；无100k、提交或推送。共享LOD阈值30/120、私有镜头上限70不改：Far仅强制GPU路径，不能声称自然跨距已测。既有网格溢出/25m搜敌受约12m轴向上限提示未扩成新专项。

## 源码位置

`Assets/Game/Editor/CharacterPipeline/`：Recipe（配方）、Geometry（前置/独立位置）、Pipeline（事务核心）、PaletteLod（显式色块策略）、Trial（新场景副本）、Window（薄入口）、Verification（本次有界复现）。
`Assets/Game/Scripts/CharacterPipelinePreviewRuntime.cs`：仅新试点的限帧与方案隔离，不改战斗逻辑。

## 人工待办与下一步

检查新窗口的实际点击流程；近看头颈/肩膀、手与剑盾、落脚和四动作；检查LOD色块/自然镜头切换。通过后再选第二种不同角色验证复用，当前不采购/下载新角色。

Knight/动作仍是KayKit CC0；完整游戏依赖各自许可。保留随包KnightLicenses、ThirdPartyNotices和ExistingLicenseReview；没有法律/EULA/V1放行。


补充操作边界：图像门禁要求四动作都有可观测运动，静态姿势可能被判为冻结；这版不把静态动作豁免当作已支持。回滚针对正常捕获的异常；断电、强杀或进程崩溃可能留下未完成的新目录，不能据此声称磁盘级事务。没有成功PipelineReport的产物不得打包，后续选新名字或人工核查该新目录，不覆盖旧版本。
