from common_p4 import *
import sys,html,base64,re
sys.stdout.reconfigure(encoding='utf-8');a=json.loads((P4/'audit-01.json').read_text(encoding='utf-8'));assert a['passed'];assert not processes() and not(ROOT/'Temp/UnityLockfile').exists();doc=ROOT/'Docs/InteractionRefinement-20261007';assert not(doc/'P4-REPORT.md').exists();handoff=json.loads((P4/'handoff.json').read_text(encoding='utf-8'))
for n in ['IMPLEMENTATION.md','P3-QUALIFICATION-STATE.md','P3-DEFERRED-ISSUES.md']:
 p=doc/n;assert sha(p)==next(x['sha256'] for x in handoff['documents'] if x['path']==p.relative_to(ROOT).as_posix()),('Document changed externally',n)
rows=[]
for tag,r in a['runs'].items():
 t=r['tests'];rows.append('| '+tag+' | '+t['passed']+'/'+t['total']+' | '+t['duration']+'秒 | '+t['skipped']+' |')
play=next(tag for tag in a['runs'] if tag.startswith('playmode'));ed=next(tag for tag in a['runs'] if tag.startswith('editmode'));images=[];imagesDir=doc/'P4-images';assert not imagesDir.exists();imagesDir.mkdir()
for name in ['01-before.png','02-drag-preview.png','03-committed.png']:
 src=P4/play/'p4-evidence'/name;assert src.is_file();dst=imagesDir/name;dst.write_bytes(src.read_bytes());images.append(dst)
text=f'''# P4 检查点：部署色块直接平移

2026-10-07。**P4已实现并通过本轮自动化检查，真人鼠标/键盘体验待验；未进入P5及后续，未制作38。**

## P3按用户决定暂缓

用户明确要求把P3余项记录后进入P4。已保存[P3已知事项与暂缓决定](P3-DEFERRED-ISSUES.md)。正式48保持；长帧、近景观感及P1/P2真人/音频事项不被写成已修复，也不再阻挡本次P4开发。原始数据、旧报告和37回退包完整保留。

## 这次可以做什么

在战前部署图上，按住一个编成色块并拖动，超过6个物理屏幕像素后出现半透明位置预览。抓取点与色块中心的偏移保留，不会把中心吸到鼠标；只移动这个编成，不移动整个军团，也不改人数、密度、比例、模型缩放或单位半径。

- 拖动过程中不写正式草稿、不新建撤销记录、不重建GPU部署。使用一个轻量预览矩形；地图布局在本次手势期间保持不变。
- 合法松手通过现有完整草稿、占地、障碍、地形和半径校验后，只写一次草稿。一次撤销恢复原位置，重做恢复新位置。
- 非法松手不提交，并保留原因；校验不是只检查中心点。沿用整份草稿校验，因此其他编成尚有错误时，也可能拒绝本次位置提交，需先修正。
- Esc、失焦、移出地图内容区域、屏幕/缩放/侧栏布局变化、切换页面/编成、打开模态、草稿修改/撤销/载入，均取消旧手势。移出后再移回，不恢复旧手势。
- 短点击仍选择编成；旧“点击放置”模式保留，但只接受短点击。松手不会额外触发旧PointerClick再放一次。
- **未更新的数值输入必须先显式更新。** 拖动不会替你提交或覆盖输入文本；无效文本同样保留。
- 一切只作用于战前草稿，不发送战斗移动命令。最终应用/开战继续走原入口。

会话身份不是单独列表下标，而是“草稿对象+单调修订号+下标+完整条目快照”；任何草稿变动都令旧手势失效。没有新增存档字段或迁移格式。

## 自动检查结果

| 运行 | 通过/总数 | NUnit执行时间 | 跳过 |
|---|---:|---:|---:|
{chr(10).join(rows)}

新增20个EditMode用例覆盖：抓取偏移、0/5/6像素阈值、连续预览不写草稿、同军团条目隔离、草稿替换/修订/撤销后的旧身份拒绝、布局/世界变化、出界取消、输入无效值、一次提交及撤销重做、越界/重叠拒绝、关闭草稿后拒绝。

3个真实PlayMode流程走保留uGUI指针组件的Down/Drag/Up：

1. 80次预览事件不写草稿，松手一次提交；撤销重做；检查另一编成不变、GPU缓冲在显式应用前不变。应用后核对1024名目标编成士兵的真实GPU出生XZ处于新占地内，总数2048、scale1，随后实际开始战斗。
2. 失焦、移出再进入、尺寸/缩放变化、打开模态、未更新文本、短点击不串写。
3. 中心仍在地图内但完整占地越界，以及真实Green禁行脚印被拒绝；旧点击放置只写一次，没有旧PointerClick监听器重放。

这些是实际组件事件与GPU数据断言，**不是OS鼠标键盘人工验收或性能基准**。Esc键的真实操作手感、其他设备/布局和所有地形组合仍需人工/后续验证。

## 真实界面截图

测试捕获实际Unity画面，不是生成图。只用于观察地图与预览，不替代GPU断言或真人输入。

### 拖动前
![拖动前](P4-images/01-before.png)

### 拖动预览（此时草稿与GPU尚未提交）
![拖动预览](P4-images/02-drag-preview.png)

### 提交并重做后的地图
![提交后](P4-images/03-committed.png)

## 保留的失败与修正记录

- editmode-01：旧P3隔离器拒绝P4输出目录，测试尚未执行；新增P4专用opt-in隔离器，没有放宽P3边界。
- editmode-02：671/672；一个投影断言误用浮点向量精确相等，改为0.1毫米数值误差界限。没有放宽占地或地形校验。
- playmode-01：2/3；拖动/GPU及越界流程通过，模态取消夹具在地图隐藏后错误查找“当前可见地图”发松手，改为向原捕获对象发送，并正确等待非缩放UI刷新。
- 上述失败原样保留；最终通过运行使用当前同一批源码。另补上了点击放置模式在手势中切换时的取消检查。

## 保护与范围

P4基线9637文件及{a['project']['approvedChanges']}个批准source/meta路径全量核验通过。原有源文件只改5处：RuntimeDeployment、UGUI指针入口、DeploymentHUD主体/地图/刷新；新建平移会话、指针组件、HUD partial、测试及专用隔离器。

正式两份48资产、生产Shader、导航和存档格式均未更改；用户4份数据与37包387文件SHA不变，HEAD/暂存区不变，无重复GUID；结束时无Unity/工程锁。没有强关用户进程。

462项清理归档全部复验。对本轮需要修改的两处既有引用，已迁移到SHA一致的P4受控before备份；原清单和迁移收据保留。当前清单SHA：`{a['archive']['manifestSha256']}`。

## 建议真人试用

请在当前工程进入战前布阵（不是未更新的37包）：

- 抓色块边缘拖动，确认不跳中心；松手后撤销/重做，确认只动当前编成。
- 把中心留在地图内、让边缘越界，确认位置退回并显示原因；再试拖进障碍/深灰禁行区。
- 拖动中按Esc、移出地图、切出窗口，确认没有落点残留。
- 输入错误数字后直接拖动，确认文本不丢失，并要求先更新。
- 使用旧点击放置，再应用开战，确认实际部署与地图一致。

P5尺寸手柄/密度比例联动、P6导航、P7军团内框选、P8局部命令及新手引导均未启动。本轮不打38包。

证据：`Logs/InteractionRefinement-20261007/P4/`，包括scope、changes、最终测试收据、p4-evidence、audit-01及publication。
'''
(doc/'P4-REPORT.md').write_text(text,encoding='utf-8')
# Self-contained report with real captures embedded, viewable without external resources.
parts=[];table=False
for line in text.splitlines():
 if line.startswith('|'):
  if not table:parts.append('<div class="scroll"><table>');table=True
  if not re.fullmatch(r'[|:\-\s]+',line):parts.append('<tr>'+''.join('<td>'+html.escape(c.strip())+'</td>' for c in line.strip('|').split('|'))+'</tr>')
  continue
 if table:parts.append('</table></div>');table=False
 if line.startswith('!['):
  match=re.search(r'\((P4-images/[^)]+)\)',line);data=(doc/match[1]).read_bytes();parts.append('<img alt="Unity实测截图" src="data:image/png;base64,'+base64.b64encode(data).decode()+'">');continue
 def inline(s):return re.sub(r'\*\*(.+?)\*\*',r'<strong>\1</strong>',re.sub(r'`([^`]+)`',r'<code>\1</code>',html.escape(s)))
 if line.startswith('#'):
  level=min(4,len(line)-len(line.lstrip('#')));parts.append(f'<h{level}>'+inline(line[level:].strip())+f'</h{level}>')
 elif line:parts.append('<p>'+inline(line)+'</p>')
if table:parts.append('</table></div>')
page='<!doctype html><html lang="zh-CN"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>P4部署平移检查点</title><style>body{margin:0;background:#edf2f3;color:#193444;font:16px/1.8 system-ui}main{max-width:1100px;margin:auto;padding:32px;background:white}h1{color:#146580}h2{margin-top:38px;padding-top:12px;border-top:1px solid #dce7ea}p{overflow-wrap:anywhere}img{display:block;width:100%;height:auto;border:1px solid #cfdee3;border-radius:8px}table{border-collapse:collapse;width:100%}td{padding:9px;border-bottom:1px solid #dce7ea}.scroll{overflow-x:auto}code{font-size:13px;background:#edf2f3}strong{color:#155775}@media(max-width:600px){main{padding:16px}}</style><main>'+''.join(parts)+'</main></html>'
(doc/'P4-REPORT.html').write_text(page,encoding='utf-8')
backup=P4/'publication-docs-before';backup.mkdir()
for n in ['IMPLEMENTATION.md','P3-QUALIFICATION-STATE.md']:(backup/n).write_bytes((doc/n).read_bytes())
p=doc/'IMPLEMENTATION.md';s=p.read_text(encoding='utf-8');s=s.replace('P4部署平移进行中','P4部署平移已实现并通过自动检查，待真人体验');s=s.replace('| 位置拖动、取消、撤销和校验正确 | 进行中 |','| 位置拖动、取消、撤销和校验正确 | 已实现/自动检查通过，待真人 |');s+='\n\n## P4 检查点（2026-10-07）\n\n部署色块直接平移已落地；全量EditMode及真实UI/GPU检查通过。详情见[P4报告](P4-REPORT.md)。P3余项按用户决定暂缓。P4真人鼠标/键盘仍待验，未P5及后续，未38。\n';p.write_text(s,encoding='utf-8')
(doc/'P3-QUALIFICATION-STATE.md').write_text('# 当前阶段：P4自动检查点\n\nP3按用户决定暂缓已知事项，见P3-DEFERRED-ISSUES.md；正式48保持。P4部署平移已实现并通过自动检查，最新P4-REPORT.md/HTML；真人操作待验，未P5及后续，未38。证据Logs/InteractionRefinement-20261007/P4。\n',encoding='utf-8')
paths=[doc/n for n in ['P4-REPORT.md','P4-REPORT.html','IMPLEMENTATION.md','P3-QUALIFICATION-STATE.md','P3-DEFERRED-ISSUES.md']]+images
r={'status':'P4_AUTOMATED_CHECKPOINT_PENDING_MANUAL','publishedAt':datetime.datetime.now().isoformat(),'files':[{'path':p.relative_to(ROOT).as_posix(),'bytes':p.stat().st_size,'sha256':sha(p)} for p in paths],'project':project_check(),'userData':user_check(),'activeProcesses':processes(),'lock':(ROOT/'Temp/UnityLockfile').exists(),'archiveManifestSha256':a['archive']['manifestSha256'],'p3DeferredNotFixed':True,'p4HumanAccepted':False,'p5Started':False,'newBuildProduced':False};save(P4/'publication.json',r);assert r['project']['passed'] and r['userData']['passed'] and not r['activeProcesses'] and not r['lock'];print(json.dumps({k:v for k,v in r.items() if k!='files'},ensure_ascii=False,indent=2))
