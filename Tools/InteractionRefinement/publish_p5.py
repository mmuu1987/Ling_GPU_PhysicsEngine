from common_p5 import *
import sys,html,base64,re
sys.stdout.reconfigure(encoding='utf-8');a=json.loads((P5/'audit-01.json').read_text(encoding='utf-8'));assert a['passed'];assert not processes() and not(ROOT/'Temp/UnityLockfile').exists();doc=ROOT/'Docs/InteractionRefinement-20261007';assert not(doc/'P5-REPORT.md').exists();handoff=json.loads((P5/'handoff.json').read_text(encoding='utf-8'))
for r in handoff['p4Publication']['files']:assert sha(ROOT/r['path'])==r['sha256'],r['path']
rows=[]
for tag,r in a['runs'].items():
 t=r['tests'];rows.append('| '+tag+' | '+t['passed']+'/'+t['total']+' | '+t['duration']+'秒 | '+t['skipped']+' |')
play=next(tag for tag in a['runs'] if tag.startswith('playmode'));images=[];imageDir=doc/'P5-images';assert not imageDir.exists();imageDir.mkdir()
for n in ['01-before.png','02-resize-preview.png','03-resize-committed.png']:
 src=P5/play/'p5-evidence'/n;dst=imageDir/n;dst.write_bytes(src.read_bytes());images.append(dst)
text=f'''# P5检查点：当前编成尺寸手柄与密度／比例联动

2026-10-07。**用户授权进入P5；本轮实现与自动检查通过，B阶段真人体验待验。P3余项继续暂缓，正式48保持。未进入P6及后续，未制作38。**

## 操作

1. 在战前部署图短点色块，或点该军团“布阵”，选中当前编成。出现四边／四角手柄。
2. 拖色块中央仍是P4平移；拖边或角调整尺寸。命中半径11物理像素、视觉手柄8物理像素；中央保留平移区域，避免小色块只能缩放不能移动。
3. 单边拖动固定对侧边；角拖动固定对角。Shift锁定**按下时实际占地**的宽深比；Shift＋单边时固定对侧中点，横向居中扩展。
4. 右侧中心、尺寸或密度／比例同步显示只读预览，手柄跟随实际预览边界。**此时尚未提交，不写草稿、不增加撤销、不重建GPU。** 松手才做完整校验并写一次。
5. 未更新的输入仍阻止手势并保留原文。数值更新与鼠标采用共同尺寸转换及排布检查；不静默保存方案。
6. Esc、失焦、移出地图、布局／页面／选择／模式／草稿变化时取消。P4短点选择和旧点击放置互斥保留。

## 数学与数据契约

世界X是纵深D，世界Z是正面宽W：

- 面积 = D × W
- 密度 = N / (D × W)
- 宽深比 = W / D
- N不随拖动变化，模型缩放／真实半径不变；中心随固定对侧边或对角规则变化，原始Y不变。

样例1024人、密度0.4、比例2.2：面积2560m²，D≈34.112115m，W≈75.04665m。实测右上角增加D=14m、W=18m后：D≈48.11211、W≈93.04665，密度≈0.2287414、比例≈1.933955；对侧边界保持。

密度范围0.05–1.5，比例0.1–10。拖拽遇到参数范围时将**真实预览矩形**限制到可表示尺寸，数字与矩形不分离。跨过对侧边不翻转；若原占地可排布，则沿原形状到受限尺寸的路径搜索局部可排布最小附近。不是全局最优装箱求解。

普通缩小若实际行列不能容纳，会显示拒绝原因且不提交。检查复用DefaultSpawnModule的实际行列数量，考虑确定性内部抖动及P2解析出的有效半径；当前已验证默认生成模块scale=1。未知自定义生成模块拒绝猜测。未修改生产生成器、导航或半径政策。

完整草稿、边界、静态障碍、地形、模板和既有重叠规则仍须通过，不新增同队特例或改变敌我规则。其他编成原本有错误时，也可能拒绝本次提交，沿用保守完整草稿校验。

### 旧手工占地与数值输入

- 没有明确修改形状时，不重写旧manualSize、density、aspect；仅选中、打开侧栏或不变的“更新”不转换。
- 明确编辑手工尺寸、人数影响占地，或用尺寸手柄后，转换为自动占地：manualSize清零，密度／比例成为唯一尺寸来源。
- 手工尺寸输入须同时为正；不可表示、过密、不能排下或完整校验失败时保留原输入，不偷偷钳制输入数字。
- 一次撤销恢复旧手工条目的全部原值（含旧manualSize.y），没有增加存档字段或迁移格式。
- 尺寸转换回实际SpawnConfig尺寸使用绝对1毫米容差；GPU回读边界断言容差1厘米。不会缩人数或模型来通过检查。

## 自动检查

| 运行 | 通过/总数 | NUnit执行时间 | 跳过 |
|---|---:|---:|---:|
{chr(10).join(rows)}

31个新增EditMode用例包括8种边角、8种Shift比例数学、极大／反向跨边、手工记录往返与撤销、N改变、无效数字、实际生成行列与半径拒绝、完整占地／重叠／陈旧身份、单次提交。原672项全部回归。

PlayMode为3项新P5＋3项P4复验：

- 在真实Green场景经实际保留uGUI指针组件派发80次拖动预览；草稿／GPU不变。松手一次提交、撤销重做、另一编成不变。
- 显式应用后回读1024名目标士兵，核对XZ外边界与新占地一致，总人数2048、scale1；随后实际开战。
- 实际InputField填1.5，更新被行列／半径校验拒绝，原文保留，未更新字段阻止手柄。失焦不提交；局部半径1.1m被实际解析并拒绝不相容占地。
- 1920×1080逻辑布局下旧手工条目未编辑保持，中心仍可作为平移目标；明确拖拽转自动，一次撤销原样恢复。
- P4平移、取消、完整占地拒绝及旧点击放置互斥再次通过。

**边界：这些是自动组件事件、逻辑分辨率与GPU断言，不是OS真人输入，也不是性能基准。** Shift实际键盘手感、极小色块易用性、原生720p／1080p窗口、交互载入各历史方案后的编辑体验，以及所有地形组合仍待真人／后续复核。未将P4或P5真人项目写成已通过。

## 真实Unity截图

### 调整前
![调整前](P5-images/01-before.png)

### 尺寸预览（尚未写草稿）
![尺寸预览](P5-images/02-resize-preview.png)

### 提交并重做后
![提交后](P5-images/03-resize-committed.png)

## 保护与交付位置

9649文件P5基线，全量SHA核对通过；本轮17个批准source/meta路径。既有生产源只改6处：共享uGUI、RuntimeDeployment、HUD主体／地图／侧栏／指针会话接入；另更新旧P4测试的隔离参数兼容。新增数学转换、HUD尺寸partial、EditMode／PlayMode测试与专用隔离器及各meta。

正式48、用户4份数据、37包387文件、HEAD及暂存区保持；没有重复GUID、没有强关用户程序。462项归档完整，清单SHA：`{a['archive']['manifestSha256']}`。本轮没有归档引用迁移，旧P4报告与原始证据保留。失败/跳过均为0，不删除任何历史失败。

证据位于`Logs/InteractionRefinement-20261007/P5/`：baseline、scope、changes/01-resize、editmode-01、playmode-01、audit-01、publication与finalization。

请在**当前Unity工程**试玩，37包未更新。本轮结束于B阶段自动检查点，不继续P6导航、P7框选、P8局部命令或新手引导，不打38。

P3余项见`P3-DEFERRED-ISSUES.md`；授权进入P5不代表长帧、动画／真人或音频事项已修复。
'''
(doc/'P5-REPORT.md').write_text(text,encoding='utf-8')
parts=[];table=False
for line in text.splitlines():
 if line.startswith('|'):
  if not table:parts.append('<div class="scroll"><table>');table=True
  if not re.fullmatch(r'[|:\-\s]+',line):parts.append('<tr>'+''.join('<td>'+html.escape(c.strip())+'</td>' for c in line.strip('|').split('|'))+'</tr>')
  continue
 if table:parts.append('</table></div>');table=False
 if line.startswith('!['):
  match=re.search(r'\((P5-images/[^)]+)\)',line);data=(doc/match[1]).read_bytes();parts.append('<img alt="Unity实测截图" src="data:image/png;base64,'+base64.b64encode(data).decode()+'">');continue
 def inline(s):return re.sub(r'\*\*(.+?)\*\*',r'<strong>\1</strong>',re.sub(r'`([^`]+)`',r'<code>\1</code>',html.escape(s)))
 if line.startswith('#'):
  level=min(4,len(line)-len(line.lstrip('#')));parts.append(f'<h{level}>'+inline(line[level:].strip())+f'</h{level}>')
 elif line:parts.append('<p>'+inline(line)+'</p>')
if table:parts.append('</table></div>')
page='<!doctype html><html lang="zh-CN"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>P5尺寸手柄检查点</title><style>body{margin:0;background:#edf2f3;color:#193444;font:16px/1.8 system-ui}main{max-width:1120px;margin:auto;padding:32px;background:white}h1{color:#146580}h2{margin-top:38px;padding-top:12px;border-top:1px solid #dce7ea}p{overflow-wrap:anywhere}img{display:block;width:100%;height:auto;border:1px solid #cfdee3;border-radius:8px}table{border-collapse:collapse;width:100%}td{padding:9px;border-bottom:1px solid #dce7ea}.scroll{overflow-x:auto}code{font-size:13px;background:#edf2f3}strong{color:#155775}@media(max-width:600px){main{padding:16px}}</style><main>'+''.join(parts)+'</main></html>'
(doc/'P5-REPORT.html').write_text(page,encoding='utf-8')
backup=P5/'publication-docs-before';backup.mkdir()
for n in ['IMPLEMENTATION.md','P3-QUALIFICATION-STATE.md']:(backup/n).write_bytes((doc/n).read_bytes())
p=doc/'IMPLEMENTATION.md';s=p.read_text(encoding='utf-8');s=re.sub(r'^状态：.*$','状态：**P5尺寸手柄与参数联动已实现并通过自动检查；B阶段真人体验待验。P3余项继续暂缓，正式48保持；未P6及后续，未38。**',s,count=1,flags=re.M);s=s.replace('| P5 | 尺寸手柄与密度／比例联动 | P4 通过 | 地图与实际部署一致，非法占地不提交 | 未开始 |','| P5 | 尺寸手柄与密度／比例联动 | 用户明确授权；P4自动通过、真人待验 | 地图与实际部署一致，非法占地不提交 | 已实现／703项全回归及6项P4/P5流程通过，真人待验 |');s+='\n\n## P5检查点（2026-10-07）\n\n用户授权进入P5；实施与自动检查完成，见[P5报告](P5-REPORT.md)。P4/P5真人体验仍待验，原历史状态由本节与文首最新状态取代；P3已知事项未宣称修复。未P6及后续，未38。\n';p.write_text(s,encoding='utf-8')
(doc/'P3-QUALIFICATION-STATE.md').write_text('# 最新阶段：P5自动检查点\n\n用户授权进入P5；尺寸手柄与参数联动已实现，703/703 EditMode、6/6 P4/P5真实UI/GPU流程通过。见P5-REPORT.md/HTML。B阶段真人体验待验；P3已知问题按先前决定暂缓，正式48保持。未P6及后续，未38。\n',encoding='utf-8')
paths=[doc/n for n in ['P5-REPORT.md','P5-REPORT.html','IMPLEMENTATION.md','P3-QUALIFICATION-STATE.md','P3-DEFERRED-ISSUES.md']]+images
r={'status':'P5_AUTOMATED_CHECKPOINT_PENDING_MANUAL','publishedAt':datetime.datetime.now().isoformat(),'files':[{'path':p.relative_to(ROOT).as_posix(),'bytes':p.stat().st_size,'sha256':sha(p)} for p in paths],'project':project_check(),'userData':user_check(),'activeProcesses':processes(),'lock':(ROOT/'Temp/UnityLockfile').exists(),'archiveManifestSha256':a['archive']['manifestSha256'],'p3DeferredNotFixed':True,'p4HumanAccepted':False,'p5HumanAccepted':False,'p6Started':False,'newBuildProduced':False};save(P5/'publication.json',r);assert r['project']['passed'] and r['userData']['passed'] and not r['activeProcesses'] and not r['lock'];print(json.dumps({k:v for k,v in r.items() if k!='files'},ensure_ascii=False,indent=2))
