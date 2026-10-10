from pathlib import Path
import json,re,base64,html
root=Path(__file__).resolve().parents[2]/'Docs/InteractionRefinement-20261007';audit=json.loads((root/'P1-AUDIT.json').read_text())
assert audit['status']=='P1_AUTOMATED_PASS_PENDING_MANUAL_ACCEPTANCE'
assert not audit['activeProcessesAfter'] and not audit['unityLockExists']
review=json.loads((root/'P1-UI-PIXEL-REVIEW.json').read_text());assert review['passed']
runs=audit['runs'];edit=runs['editmode-03'];gpu=runs['preview-03'];assert edit['passed'] and gpu['passed']
errors=[float(re.search(r'relativeErrorP95=([\d.]+)',x)[1]) for x in gpu['worldRuler']]
maximum=max(errors)*100
oldchanges=sum(x['originalSha256'] is not None for x in audit['changes'].values());newchanges=len(audit['changes'])-oldchanges
uipixels='\n'.join('- `'+x['file']+'`：窄颜色匹配 '+str(x['strictInkPixels'])+' 像素，全部在模型视口内；椭圆边界拟合 P95 残差 '+format(x['ellipseRelativeResidualP95']*100,'.3f')+'%。' for x in review['runs'])
real='\n'.join('- `'+x.replace('P1_REAL_SUBJECT ','')+'`' for x in gpu['realSubjects'])
warning=sum(runs[n]['noAudioListenerWarnings'] for n in audit['qualifiedRuns'])
report=f'''# P1 阶段报告：半径只读展示与真实尺度预览圈

日期：2026-10-07  
状态：**实现、自动化与保护审计通过；真人鼠标验收仍待完成。**  
本轮停在 P1 检查点。P2 半径编辑、布阵拖拽、军团内框选均未开启，也没有生成新包。

## 1. 已交付

- 兵种图鉴新增**只读物理基准半径、直径、来源**；没有新增可编辑半径字段，也不会因浏览图鉴写入数值文件。
- 内嵌与放大模型预览都有独立的**深金色接触半径圈**。最后一轮加深了圈和图例的颜色，改善浅背景上的可见性，几何尺寸没有改变。
- 圆心固定在实际 Agent 根锚点，不随剑、翅膀或模型包围盒中心移动；默认取景同时容纳模型与圆环。
- 拖动旋转／滚轮缩放仍使用原有预览交互。旋转或改变相机距离不会改半径、模型资源或战斗单位。
- 阴影仍是阴影；圆环使用自己的 Mesh、Material、Shader，不修改官方 VAT 材质。
- 静态回退会说明“无半径圈”；未验证的自定义模块不猜测数值，不绘制虚假的替代圈。

**查看方式：** 在当前 Unity 源码的主菜单进入“兵种图鉴”，查看只读行，再点“查看模型”。37 包保持原样，不包含这次源码修改。

## 2. 半径语义已经厘清，但没有擅改算法

当前 262 个兵种配置均使用 `DefaultSwordUnit`。读取链路是：

`FlockingConfig.agentRadius → DefaultFlockingModule → BuildGpuSettings → GPU 消费者`

图鉴直接使用现有构建器的结果，包含最低值钳制和缺配置默认值。预览默认 1×，与当前默认生成模块一致。

| 内容 | 当前真实含义 |
|---|---|
| 图鉴基准半径 | 默认模块上传的 `agentRadius`，不是由模型宽度或阴影推算 |
| 深金色圈 | 接触路径的名义体半径：基准半径 × 最大水平缩放（该路径有 0.01 缩放下限） |
| 普通分离／静态障碍 | 并不全部采用上面的缩放公式 |
| 投射物目标体积 | 有自己的 0.05 半径下限及缩放规则 |
| 软接触 | 允许穿入一定距离再施加分离，不是严格不重叠的硬碰撞器 |

所以界面明确写了**“非硬碰撞边界 / 攻击范围”**。本阶段没有统一这些消费者，也没有把运行时拥挤归咎于半径。

完整路径、钳制差异、地形清距缓存、烘焙／生成／渲染坐标对应关系见 [P1-RADIUS-MAP.md](P1-RADIUS-MAP.md)。

## 3. 最新有效验证

本次环境：Unity 6000.3.14f1、Direct3D 11、NVIDIA GTX 1060 3GB。未把该结果扩展为跨平台验证。

| 验证 | 结果 | 边界 |
|---|---|---|
| 完整 EditMode 回归 | **{edit['tests']['passed']}/{edit['tests']['total']}，失败 0、跳过 0** | 原有 625 项 + 6 项新半径测试 |
| 新旧预览 GPU 专项 | **{gpu['tests']['passed']}/{gpu['tests']['total']}，失败 0、跳过 0** | 原预览 13 项 + 新半径／UI／资源 9 项，不是全部 PlayMode 测试 |
| 已知世界长度 | **6 个样本 × 2 个尺寸，12 次通过** | 214×320、512×512 的真实离屏 GPU 预览 |
| 实际 UGUI 展示 | **4 张有效截图** | 1280×720、1920×1080 的图鉴与放大视图；不是 OS 窗口／独占全屏验收 |
| 换模型／关闭释放 | **6 轮通过** | 每轮切换两模型后关闭；预览 Mesh、Material、RT 回到计数基线，旧缓冲引用释放 |
| 正式角色样本 | **4 个通过** | 男战士、游侠、蜘蛛、龙；模板及群体配置序列化结果不变 |

### 尺度不是凭目测

- 已知基准半径覆盖 0.45、0.55、1、4.8 米；包括对称、明显偏置的不对称几何。
- 非 1 水平缩放 `(2,1,0.5)`、`(0.5,1,1.5)` 对 0.55 米基准分别得到 **1.1 米、0.825 米**的接触圈。
- 用**独立 Unity Camera** 把着色像素反投影到世界 Y=0 平面，测量它距 Agent 原点的距离；不是仅比较两个同源数值。
- 12 次测量中，着色像素相对中心线半径的 P95 偏差最大 **{maximum:.4f}%**；环形笔画自身半宽为半径的 1.2%，该结果落在笔画范围附近，测试上限为 2%。不是说物理参数被放大了这个比例。
- 独立 Camera 与预览矩阵的归一化投影差异在 `1e-5` 断言内；单位环顶点中心线半径在 `1e-6` 断言内。128 段圆环仍是多边形近似，不声称无限精度曲线。
- 旋转、默认取景、缩放／复位检查通过；主动放大允许裁切，但不会改世界半径。

### 实际界面的像素检查

不仅检查控件存在，还要求截图非空、颜色丰富。随后按实际 Shader 输出的 sRGB `(132,72,13)`、每通道 ±1 进行窄颜色复核，并核对这些像素位于**真实 RawImage 区域内的椭圆边界**：

{uipixels}

PNG 哈希与最终审计匹配，复核数据见 `P1-UI-PIXEL-REVIEW.json`。Unity 日志里的宽容筛色计数可能包含角色相近颜色，不当作纯圆环像素数。上述椭圆复核确认实际界面确实显示了圈；米制尺寸仍由前述独立相机／世界平面实验验证。

### 正式角色记录

{real}

取景 `frame.center` 可以偏离零；物理圈的 `anchor` 始终为 Agent 根锚点。这正是本阶段特意区分的两件事。

## 4. 保护审计与不变性

最终审计：`Logs/InteractionRefinement-20261007/P1/final-audit-02.json`。

- P1 起点是已完成 P0 修复的 **9,609 文件基线**，没有覆盖原 P0 基线或丢失那 8 项已批准修复。
- P1 只有 **{oldchanges} 个既有文件修改、{newchanges} 个新增自有文件（含 .meta）**；无意外修改或新增文件。
- `Assets/MassEngine` 战斗代码、正式场景、单位配置、模型、原有材质、Packages／ProjectSettings 均与 P1 起点匹配；圈是 UI 独立资源。
- 37 回退包 **387 个文件**重新完整 SHA-256 核对，零差异。
- 用户受保护数据 **4 → 4**，没有内容差异或新冒出的对应存储文件。
- Git HEAD／暂存文件清单未变；没有 reset、stash、stage、commit；无重复 GUID。
- 审计结束无 Unity／游戏进程和项目锁。没有强关用户进程，没有出 38 包。

“不改战斗”的证据是受控文件哈希、回归和预览资源／资产不变性断言；**没有把这轮离屏预览测试当成一次新的万人战斗或 FPS 测试**。P0 的实战 GPU 与性能记录保持独立。

## 5. 失败尝试与截图资格说明

1. `editmode-01` 首次沿用了 P0 运行器的参数。P0 隔离守卫拒绝 P1 日志目录，测试未执行；编译无错误，保护检查通过。已新增只服务 P1 的显式隔离辅助脚本，未修改 P0 辅助代码。
2. `radius-01` 的 8 项测试通过，但最初四张 UI 图全黑：测试夹具相机层掩码没有包含 UI。人工检查图片后将这些 UI 图**明确作废并保留记录**，增加非空、颜色数量、RawImage 内圆环像素断言；随后复测通过。该错误不影响该轮数值圆环与资源测试。
3. `preview-02` 已得到合格 UI 图；检查发现浅金色细圈和图例对比度不够，因此只调整独立圈／图例颜色。最终重新执行完整 `editmode-03` 和 `preview-03`，以本报告的最新结果为准。

初轮失败日志与作废图片没有删除，也没有把“测试工具退出成功”直接当作有效截图。

## 6. 当前仍未验收的部分

- **真人鼠标拖拽／滚轮手感、真实窗口尺寸切换、独占全屏**尚未完成；离屏 720p／1080p 截图不替代这些项目。
- 最后完整编译没有错误；已有的两类 CS0108 隐藏成员警告（`Contact35Probe.camera`、`UnitModelPreviewWidget.renderer`）仍保留，未宣称日志全净。
- P0 留下的 AudioListener 警告与首轮 GUI 全局布局预快照缺失说明仍保留，不借 P1 宣称解决。P1 最终两轮日志中的无 AudioListener 警告计数为 **{warning}**；这也不是音频验收结果。
- 未开放半径编辑、未新增保存字段、未分析出拥挤根因、未改部署操作或选择／局部命令。
- 引导改进继续延期。没有新增地图、模式或单位内容。

## 7. 下一阶段边界

P1 的显示坐标与尺度已有自动化证据；阶段状态仍区分自动通过与真人确认。下一步是方案中的 **P2：编辑—保存—运行时生效**，不是直接修群聚或开始拖阵。

进入 P2 时要先确定安全编辑范围，并覆盖官方／全局／本局来源、缺字段兼容、非法值、只修改运行时副本、重进战场实际 GPU 参数、恢复默认以及地形清距缓存。现有消费者的缩放差异已经记录，不能顺手全局替换算法。本次没有提前执行 P2。

## 8. 文件与回退

- `P1-REPORT.md`：本报告。
- `P1-RADIUS-MAP.md`：来源与单位映射。
- `P1-验收预览.html`：自包含截图摘要，可离线打开。
- `P1-AUDIT.json`：最终审计副本。
- `P1-UI-PIXEL-REVIEW.json`：窄颜色与椭圆边界截图复核。
- `P1截图/`：最新 20 张合格图片（4 张 UI、4 张真实角色、12 张数值夹具）。数值夹具是诊断几何，不是正式兵种或拥挤证据。

远端逐次修改记录在 `Logs/InteractionRefinement-20261007/P1/changes/01–04` 对应目录，原文件备份在 `P1/backups/`。回退应在关闭 Unity 并核对当前哈希后，只恢复 P1 的 6 个既有文件并移除其新增自有文件及 .meta；不重置整个仓库，不恢复掉 P0 修复，不触碰单位配置、用户数据或 37 包。**尚未执行回退。**
'''
(root/'P1-REPORT.md').write_text(report)
def img(name):
 return 'data:image/png;base64,'+base64.b64encode((root/'P1截图'/name).read_bytes()).decode()
def card(file,title,caption,cl=''):
 return f'<figure class="{cl}"><button class="picture" onclick="show(this)"><img src="{img(file)}" alt="{html.escape(title)}"></button><figcaption><strong>{title}</strong><span>{caption}</span></figcaption></figure>'
mainimages=card('library-1920x1080.png','兵种图鉴 · 只读半径与来源','当前模板值，不写入保存文件；点图查看原尺寸。')+card('expanded-1920x1080.png','放大预览 · 同一世界尺度','圆心采用 Agent 根锚点，圈与模型一起旋转取景。')
subjects=''.join(card(f'real-roster-{key}.png',title,caption,'subject') for key,title,caption in [('male','男战士 · r = 0.55 m','默认角色；直径 1.1 m'),('ranger','游侠 · r = 0.45 m','非对称武器不改变半径'),('spider','蜘蛛 · r = 4.8 m','大体型；不把阴影当物理范围'),('dragon','龙 · r = 3.4 m','翅膀影响取景，不决定圈大小')])
htmltext=f'''<!doctype html><html lang="zh-CN"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>P1 · 半径显示验收</title><style>
*{{box-sizing:border-box}}body{{margin:0;background:#edf1f2;color:#20333c;font:15px/1.75 system-ui,"Microsoft YaHei",sans-serif}}header{{background:#163443;color:white;padding:48px max(24px,calc((100vw - 1180px)/2)) 36px}}.eyebrow{{font-size:12px;letter-spacing:2px;color:#8cbdc8}}h1{{font-size:36px;line-height:1.3;margin:15px 0}}header p{{color:#c3d5dc;max-width:820px}}.status{{display:inline-block;background:#285361;color:#d5f4ed;border:1px solid #548887;padding:5px 12px;border-radius:20px;font-size:13px}}main{{max-width:1230px;margin:auto;padding:28px 24px 55px}}.metrics{{display:grid;grid-template-columns:repeat(4,1fr);gap:14px;margin-bottom:30px}}.metric,section{{background:#fff;border:1px solid #d7e0e4;border-radius:12px}}.metric{{padding:18px 22px}}.metric b{{display:block;font-size:30px;color:#23617a;line-height:1.4}}.metric span{{font-size:13px;color:#667a83}}section{{padding:26px;margin:20px 0}}h2{{font-size:21px;margin:0 0 12px}}p{{margin:10px 0}}.note{{border-left:4px solid #955114;padding:10px 15px;background:#fff8e9;margin-top:18px}}.gallery{{display:grid;grid-template-columns:1fr 1fr;gap:20px}}figure{{margin:0;min-width:0}}.picture{{display:block;padding:0;border:1px solid #d5dfe3;border-radius:8px;overflow:hidden;background:#a4beca;width:100%;cursor:zoom-in}}img{{width:100%;display:block}}figcaption{{padding:12px 2px}}figcaption strong{{display:block;font-size:15px}}figcaption span{{font-size:12px;color:#70808a}}.subjects{{grid-template-columns:repeat(4,1fr)}}.subject .picture{{background:#a4beca}}.two{{display:grid;grid-template-columns:1fr 1fr;gap:30px}}ul{{padding-left:20px;margin:10px 0}}li{{margin:6px 0}}.muted{{color:#6b7d87;font-size:13px}}code{{background:#eff3f5;padding:2px 6px;border-radius:4px;font:12px ui-monospace,monospace}}footer{{font-size:12px;color:#657985;padding:12px 0}}dialog{{padding:10px;background:#e7eef1;border:0;border-radius:10px;max-width:97vw;max-height:96vh}}dialog::backdrop{{background:#081924d9}}dialog img{{max-width:94vw;max-height:88vh;width:auto;height:auto;object-fit:contain}}dialog button{{float:right;padding:4px 14px;border:0;background:#163443;color:white;cursor:pointer;margin-bottom:8px}}@media(max-width:800px){{h1{{font-size:29px}}.metrics,.subjects{{grid-template-columns:repeat(2,1fr)}}.gallery:not(.subjects),.two{{grid-template-columns:1fr}}section{{padding:18px}}}}@media print{{body{{background:white}}header{{padding:20px;color:black;background:white}}.picture{{border:1px solid #bbb}}dialog{{display:none}}}}
</style></head><body><header><div class="eyebrow">INTERACTION REFINEMENT / P1 / 2026.10.07</div><h1>半径看得见，战斗参数不变。</h1><p>兵种图鉴新增只读半径、直径和来源。独立深金色圆环按实际接触路径的尺度绘制，不再靠阴影或模型外观猜范围。</p><div class="status">P1 自动化通过 · 真人鼠标验收待完成</div></header><main><div class="metrics"><div class="metric"><b>631 / 631</b><span>完整 EditMode 回归</span></div><div class="metric"><b>22 / 22</b><span>新旧预览 GPU 专项</span></div><div class="metric"><b>12</b><span>世界长度 / 投影核对</span></div><div class="metric"><b>0</b><span>用户数据与 37 包差异</span></div></div><section><h2>实际界面</h2><div class="gallery">{mainimages}</div><div class="note"><b>圈不是攻击范围，也不是不可穿透的硬碰撞边界。</b><br>默认生成比例与图鉴预览均为 1×。普通分离、障碍和投射物仍保留各自原有算法；P1 没有统一或改动它们。</div></section><section><h2>小体型、大体型和非对称外形</h2><div class="gallery subjects">{subjects}</div><p class="muted">真实 VAT 模型离屏渲染，非生成图片。包围盒决定取景，根锚点决定圈心，配置与接触路径决定半径。</p></section><section><div class="two"><div><h2>怎么证明尺度正确</h2><ul><li>用独立 Unity Camera 将着色像素反投影至世界地面，量它到 Agent 原点的距离。</li><li>覆盖 0.45 / 0.55 / 1 / 4.8 m，以及非 1 水平缩放。</li><li>12 次测量的像素半径 P95 偏差最高 {maximum:.4f}%，包含 ±1.2% 的环形笔画宽度。</li><li>6 轮切换并关闭预览，原生预览资源计数回到基线。</li></ul></div><div><h2>哪些没有动</h2><ul><li>战斗半径、单位资源、模型尺度、正式场景和保存格式。</li><li>用户 4 份受保护数据、37 回退包 387 个文件，完整审计零差异。</li><li>半径编辑、部署拖拽、框选与局部命令尚未开始。</li><li>没有生成新包；37 仍是原来的可玩回退。</li></ul></div></div></section><section><h2>验收边界与下一步</h2><p>这些是实际 GPU 离屏图像与组件测试。1280×720、1920×1080 截图不等于真人鼠标、OS 窗口切换或独占全屏验收；P0 的音频／布局记录限制仍保留。</p><p>本轮停在 P1 检查点。下一步按方案是 P2 的“编辑—保存—重进战场—还原”链路，不会凭这张圈开始改拥挤算法。</p><p class="muted">首轮全黑 UI 截图已作废保留；最终版本增加了非空、颜色丰富度和真实 RawImage 圆环像素检查。完整经过见 P1-REPORT.md。</p></section><footer>证据：P1/preview-03、P1/editmode-03、P1/final-audit-02.json。详细来源见 P1-RADIUS-MAP.md。所有截图与样式均内嵌，此文件可离线查看。</footer></main><dialog id="zoom"><button onclick="document.getElementById('zoom').close()">关闭 ×</button><img alt="原尺寸截图"></dialog><script>function show(b){{const d=document.getElementById('zoom');d.querySelector('img').src=b.querySelector('img').src;d.showModal()}}document.getElementById('zoom').addEventListener('click',e=>{{if(e.target.tagName==='DIALOG')e.target.close()}})</script></body></html>'''
(root/'P1-验收预览.html').write_text(htmltext)
print('Report and self-contained review created',len(report),len(htmltext))
