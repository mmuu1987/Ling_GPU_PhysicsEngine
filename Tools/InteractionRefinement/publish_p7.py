from common_p7 import *
import sys,html,re,base64
sys.stdout.reconfigure(encoding='utf-8');assert not(P7/'publication.json').exists();assert not processes() and not(ROOT/'Temp/UnityLockfile').exists()
a=json.loads((P7/'audit-02.json').read_text(encoding='utf-8'));assert a['passed'];prior=json.loads((LOG/'P6/scenario-followup-01/publication.json').read_text(encoding='utf-8'))
for r in prior['files']:assert sha(ROOT/r['path'])==r['sha256']
doc=ROOT/'Docs/InteractionRefinement-20261007';backup=P7/'publication-docs-before';backup.mkdir()
for n in ['IMPLEMENTATION.md','P3-QUALIFICATION-STATE.md']:(backup/n).write_bytes((doc/n).read_bytes())
runs='\n'.join('| '+tag+' | '+r['tests']['passed']+'/'+r['tests']['total']+' | '+r['tests']['skipped']+' |' for tag,r in a['runs'].items());total=sum(int(r['tests']['total']) for r in a['runs'].values())
evidence=lambda n:(P7/'selection-04'/(n+'.txt')).read_text(encoding='utf-8')
text=f'''# P7：军团内GPU框选与批量选中环检查点

2026-10-07。用户明确授权“行吧，进入P7吧”。**P7开发验证检查点通过，停在P7；未进入P8，也未制作38包。**

状态：`P7_DEVELOPMENT_SELECTION_CHECKPOINT_PASSED`。这里的“通过”指下面列出的自动逻辑、GPU、图像和回归检查点，不冒充真人鼠标易用性或任意规模性能签收。普通试玩仍不开放局部选择入口：P8必须先统一全部命令作用域。

## 1. 已实现

- 先选当前军团，再在非UI战场区域左键拖框。6物理像素阈值；反向拖动使用同一归一化矩形。
- GPU按真实teamId、hp和士兵地面中心屏幕投影筛选；只选当前军团存活成员，排除镜头后方、近远裁剪面外和视口外。
- 采用投影选取，允许选到遮挡后成员；不增加逐兵Collider或逐兵射线。选中环仍使用正常深度测试，可能被地形遮挡，不能拿环的可见数量替代选区数量。
- 松手时冻结view、projection、pixelRect、选框与裁剪距离。异步处理期间相机移动不改变这次投影。快速请求使用一组在途回读＋最新待处理矩形；等待旧回读时保留的是松手相机参数，实际成员位置来自随后GPU处理时刻，不伪称完全冻结松手瞬间的世界状态。
- 选区拥有独立epoch、team、request和成员version。重新框选立即进入Pending，不暴露旧选区供新命令使用；旧结果、切军团和换buffer代次的返回都丢弃。
- 替换式选择；空框是Empty，不回退WholeArmy。切军团、Esc和生命周期清理只改选区，不修改历史局部命令。
- GPU按当前hp剔除死亡成员；常规存活刷新间隔0.25秒，异步结果到达后更新数量/成员版本。渲染器另查当前hp，死亡环不必等待CPU回读才隐藏。数量属于最近确认的GPU样本，存在采样与回读延迟。
- 一个网格＋间接实例化draw绘制所有选中环，无逐成员GameObject。环中心直接读取当前agentBuffer，半径来自实际单位GPU设置和scale；不改VAT、teamId、64-byte AgentData或战斗内核。
- 显示整军团／确认中／局部已选N与军团存活M／零选中／不可用，多个历史命令序号显示“混合命令”，不伪报统一目标。

## 2. 输入和命令安全

优先级：模态/UI → 部署编辑 → 目标选点 → 框选。相机右键/中键/Alt/滚轮动作不与框选共享捕获；拖框时相机输入仅通过独立捕获查询阻止，不使用解锁其他系统输入的方式。失焦、帮助、待确认操作、切军团、离开/改变视口和结束/重开释放捕获、丢弃旧请求。

开发预览仅由显式`BeginSelectionPreviewForDevelopment`启用；没有新增普通UI开关、快捷键、序列化开关或启动自动入口，非Development Player拒绝启用。预览期间四个uGUI命令按钮置灰，按钮回调、热键共用方法和小地图共用的移动/追加方法全部拒绝下令。GPU确认错误保持不可用，不悄悄转回整军团指挥。

直接用于测试的Controller整军团API保持原有语义。P7没有给玩家接通移动/攻击/撤退/待命的局部作用域，这是P8工作，不是被漏掉的普通入口。

## 3. 实体身份与历史命令快照

选中ID是agentBuffer的稳定槽索引，不是LOD可见实例序号。检查UploadInitialData、模拟双buffer交换与可见列表路径：本局内没有实体压缩重排；重新分配/Reset时以manager buffer对象＋agentBuffer对象身份失效整代选区。LOD列表变化不改变实体身份。

P7选择epoch与P6局部命令通道epoch是两套生命周期标识，不能互相代用。后续P8必须验证选区仍属当前局/军团，再把成员副本和version交给当前P6通道，不能把后来的框选当作旧命令成员。

实际开发接桥证据（非玩家入口）：

`{evidence('p7-selection-command-snapshot')}`

不仅比较显示文字：GPU确认的选择数量、不可变成员副本、P6 receipt成员快照、memberVersion、已提交人数及实际GPU执行人数均作一致性断言；随后取消选择仍保留已发命令。

## 4. GPU、真实图像与资源记录

受控GUI Editor，D3D11／GTX1060 3GB。投影测试使用交错team0/team1、死亡/镜头后方/远面外的可控GPU数据；另有真实Green 2048场景的历史局部命令与选择接桥。控制fixture不是正式混编大军性能结论。

`{evidence('p7-projection')}`

`{evidence('p7-render')}`

选中环图像来自自动相机渲染到256×256独立RenderTexture；隔离到专用测试layer，非生成图。先验证三个环的真实像素，再只移动agentBuffer中的一名成员、不重新框选，验证环已出现在新位置。这里没有用Draw调用计数冒充可见渲染，也没有用这张隔离图冒充正常战场HUD截图。

![真实GPU选中环](../../Logs/InteractionRefinement-20261007/P7/selection-04/p7-gpu-rings.png)

![移动GPU位置后](../../Logs/InteractionRefinement-20261007/P7/selection-04/p7-gpu-rings-moved.png)

`{evidence('p7-liveness-cost')}`

2048总成员时：选择mask与索引8×N bytes、两计数8 bytes、间接参数20 bytes，共16412 bytes逻辑ComputeBuffer容量。不包含mesh/material、驱动内部append计数/分配开销或显存峰值。每组异步读回4×N＋8＝8200 bytes。仅在开发预览打开时存在；关闭释放。主动框选可额外触发处理，常规存活维护不是逐帧回读。

不逐帧同步GetData全部agent，也不调用WaitForCompletion/WaitAllRequests。测试夹具中的同步GPU数据读写只用于建立确定场景和验证结果，不进入生产选择器。

## 5. 测试与回归

| 最终采用运行 | 通过/总数 | 跳过 |
|---|---:|---:|
{runs}

合计{total}次测试执行，不是{total}个互不重复的新测试。730 EditMode包含原715＋15新选择状态/投影用例。6项P7 GPU/UI/渲染测试覆盖交错军团、死亡、捕获矩阵、快速连框、team切换、buffer reset、空选、批量环及其移动、真实uGUI禁用与强制回调、历史命令和成员快照接桥。

`{evidence('p7-input')}`

`{evidence('p7-orders')}`

输入验证采用生产pointer处理器的合成帧和真实uGUI按钮回调，不是OS真人鼠标操作。窄窗口可读性、不同DPI下手感、长时间频繁拖框、超大军团和跨硬件性能仍待后续人工/发布资格复核。未声称P7帧时性能等效；P6既有性能边界照旧。

旧回归覆盖局部GPU5项、旧核67项、P4/P5交互6项和P6实际场景3项。完成回归后只在测试文件追加选择到P6 receipt的一致性断言，再跑selection-04；audit-02确认相对audit-01只有该测试文件改变，生产候选没有改变。没有冒称此前回归重跑了第二遍。

## 6. 失败记录与修正

- selection-01：1/6。五项受控夹具把manager.enabled设false，触发OnDisable释放buffer，造成空引用；改用enableGpuDispatch=false冻结调度而保留buffer。这是夹具设置错误，未修改引擎Release语义。
- selection-02：5/6。初始环像素已成功，但RenderTexture沿用了小的normalized viewport，移动后固定像素区域断言失败。测试改为显式全纹理视口/aspect=1后继续验证，不是移动了一个假CPU图标。
- selection-03：6/6。作为完整初验保留；selection-04增加实际成员快照接桥后再次6/6。
- 开发预览遇到初始化错误时保持命令入口禁用；缺少明确军团时提示先选军团。没有扩大普通玩家入口。

所有失败、日志与图像保留；没有提高超时掩盖这些失败。

## 7. 保护和下一步

9675基线＋27批准source/meta核验。既有生产变更限HUD四文件与相机输入一文件；四个既有测试只兼容P7隔离参数。新增选择状态、GPU选择器、HUD partial、三个shader/include资产、两个测试及P7隔离器，共九个新文件及meta。

MassEngine生产Core/模拟HLSL、正式兵种资产、48参数、存档格式与37包保持；P3暂缓项和P4/P5真人待验没有被冒充解决。用户4份数据、37包387文件、HEAD/stage、GUID和462项归档审计通过，无归档引用迁移。旧P6报告不改；计划/状态原版本留publication-docs-before。结束时无Unity/锁的终核见finalization.json。

**停在P7开发验证检查点。下一阶段需用户授权后再进入P8，统一按钮、热键、小地图和局部四命令。** 不自动生成38、不开始Onboarding，不重复既有Assets/Builds整理。

电脑报告：`Docs/InteractionRefinement-20261007/P7-REPORT.md/html`；证据：`Logs/InteractionRefinement-20261007/P7/`；工具：`Tools/InteractionRefinement/`。
'''
md=doc/'P7-REPORT.md';assert not md.exists();md.write_text(text,encoding='utf-8')
parts=[];table=False
for line in text.splitlines():
 if line.startswith('|'):
  if not table:parts.append('<table>');table=True
  if not re.fullmatch(r'[|:\-\s]+',line):parts.append('<tr>'+''.join('<td>'+html.escape(c.strip())+'</td>' for c in line.strip('|').split('|'))+'</tr>')
  continue
 if table:parts.append('</table>');table=False
 if line.startswith('!['):
  path=P7/'selection-04'/('p7-gpu-rings-moved.png' if 'moved' in line else 'p7-gpu-rings.png');parts.append('<figure><img width="256" height="256" src="data:image/png;base64,'+base64.b64encode(path.read_bytes()).decode()+'"><figcaption>'+('仅改变GPU位置后' if 'moved' in line else '初始三个选中环')+'</figcaption></figure>');continue
 value=re.sub(r'\*\*(.+?)\*\*',r'<strong>\1</strong>',re.sub(r'`([^`]+)`',r'<code>\1</code>',html.escape(line)))
 if line.startswith('## '):parts.append('<h2>'+value[3:]+'</h2>')
 elif line.startswith('# '):parts.append('<h1>'+value[2:]+'</h1>')
 elif line.strip():parts.append('<p>'+value+'</p>')
(doc/'P7-REPORT.html').write_text('<!doctype html><html lang="zh-CN"><meta charset="utf-8"><title>P7开发选择检查点</title><style>body{max-width:1080px;margin:40px auto;padding:0 24px;color:#24364a;background:#f5f8fa;font:16px/1.8 system-ui}h1,h2{color:#165b68}h2{margin-top:36px}table{border-collapse:collapse;width:100%;background:white}td{border:1px solid #cddbe0;padding:8px}code{overflow-wrap:anywhere;font-size:13px}figure{display:inline-block;margin:12px 24px 12px 0}strong{color:#765014}</style>'+''.join(parts)+'</html>',encoding='utf-8')
p=doc/'IMPLEMENTATION.md';s=p.read_text(encoding='utf-8');s=re.sub(r'^状态：.*$','状态：**P7开发选择检查点通过：GPU军团内框选、代次/版本、批量选中环及输入安全已实现并自动验证。普通试玩入口仍关闭，未P8或38；下一阶段等用户授权。P4/P5真人待验、P3暂缓、正式48与37保持。**',s,count=1,flags=re.M);s=s.replace('原型检查点通过（本轮实测边界），未授权P7','原型检查点通过（实测边界）；已授权并完成P7开发检查点');s=re.sub(r'^(\| P7 \|.*)\| 未开始 \|$',r'\1| 开发选择检查点通过；普通入口待P8，真人待验 |',s,flags=re.M);s=s.replace('最新执行证据：','最新执行证据：[P7报告](P7-REPORT.md) · [P6场景渲染检查点](P6-SCENARIO-RENDER-CHECKPOINT.md)。历史证据：',1);s=s.replace('后续用户分别授权了 P0、P1、P2 和当前 P3','后续用户已按阶段授权推进至 P7');s=re.sub(r'^(\| P3 \|.*?)\| 进行中：.*?\|$',r'\1| 余项按用户决定暂缓，见P3-DEFERRED-ISSUES.md；正式48保持，不代表全部验收通过 |',s,flags=re.M);s+='\n\n## P7开发选择检查点（2026-10-07）\n\n用户已授权P7。见[P7报告](P7-REPORT.md)。状态P7_DEVELOPMENT_SELECTION_CHECKPOINT_PASSED：GPU选择、异步代次/成员版本、死亡剔除、批量环、相机/UI取消链及实际命令快照一致性通过；P7仅开发预览，旧玩家命令入口在预览中禁用。自动测试不冒充真人或任意规模性能签收。未进入P8/38，下一阶段需用户授权。P6历史报告保留，先前“未P7”是历史状态。\n';p.write_text(s,encoding='utf-8')
(doc/'P3-QUALIFICATION-STATE.md').write_text('# 当前交互整改状态\n\nP7开发选择检查点通过，见[P7报告](P7-REPORT.md)。军团内GPU框选、选区版本、批量环和输入安全已自动验证；普通试玩仍不开放半成品局部入口，下一步P8统一四命令作用域需用户授权。未P8/38或Onboarding。P6保持有边界通过；P4/P5真人待验、P3余项暂缓、正式48及37回退包保持。\n',encoding='utf-8')
for e in a['evidence']:assert sha(ROOT/e['path'])==e['sha256']
for path,r in a['approvedChanges'].items():assert sha(ROOT/path)==r['expectedSha256']
files=[md,doc/'P7-REPORT.html',doc/'IMPLEMENTATION.md',doc/'P3-QUALIFICATION-STATE.md'];r={'status':'P7_DEVELOPMENT_SELECTION_CHECKPOINT_PASSED','developmentOnly':True,'publicSelectionEntryEnabled':False,'humanInputAccepted':False,'universalPerformanceAccepted':False,'p8Started':False,'newBuildProduced':False,'files':[{'path':p.relative_to(ROOT).as_posix(),'bytes':p.stat().st_size,'sha256':sha(p)} for p in files],'evidenceVerified':len(a['evidence']),'sourceMetaVerified':27,'testExecutions':total,'project':project_check(),'userData':user_check(),'activeProcesses':processes(),'lock':(ROOT/'Temp/UnityLockfile').exists(),'finishedAt':datetime.datetime.now().isoformat()};r['passed']=r['project']['passed'] and r['userData']['passed'] and not r['activeProcesses'] and not r['lock'];save(P7/'publication.json',r);assert r['passed'];print(json.dumps(r,ensure_ascii=False,indent=2))
