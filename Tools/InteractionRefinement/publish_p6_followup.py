from common_p6 import *
import sys,html,re
sys.stdout.reconfigure(encoding='utf-8');folder=P6/'performance-followup-01';assert not(folder/'publication.json').exists();a=json.loads((folder/'audit.json').read_text(encoding='utf-8'));v=json.loads((folder/'analysis.json').read_text(encoding='utf-8'));assert a['passed'] and a['productionUnchangedSinceCheckpoint'];assert not processes() and not(ROOT/'Temp/UnityLockfile').exists()
doc=ROOT/'Docs/InteractionRefinement-20261007';old=json.loads((P6/'publication.json').read_text(encoding='utf-8'))
for f in old['files']:assert sha(ROOT/f['path'])==f['sha256'],f['path']
backup=folder/'docs-before';backup.mkdir()
for n in ['IMPLEMENTATION.md','P3-QUALIFICATION-STATE.md']:(backup/n).write_bytes((doc/n).read_bytes())
rows=[];micro=[]
for tag,r in v['runs'].items():
 rows.append(f"| {tag} | {r['off']['medianMs']:.6f} | {r['empty']['medianMs']:.6f} | {r['detached']['medianMs']:.6f} | {r['emptyVsOffPct']:+.2f}% | {r['emptyVsDetachedPct']:+.2f}% |")
 for item in v['micro'][tag]:micro.append(f"| {tag} / {item['operation']} | {item['medianMicroseconds']:.4f} | {item['maxAllocatedBytes']} |")
orig=[]
for tag,r in v['originalProtocolRepeats'].items():orig.append(f"| {tag} | {r['offMedianMs']:.6f} | {r['emptyMedianMs']:.6f} | {r['deltaPct']:+.2f}% |")
runs='\n'.join('| '+k+' | '+r['tests']['passed']+'/'+r['tests']['total']+' | '+r['tests']['skipped']+' |' for k,r in a['runs'].items())
text=f'''# P6空覆盖性能排查 · 第1轮

2026-10-07。用户授权“留在P6排查”。**本轮只增加测量fixture与工具，生产代码未改；没有把测量通过当成性能签收，未进入P7/P8或38。**

## 结论与决策

1. 原报告单次20秒ABBA的+12.59%是当轮真实观测，但不应当作已经证实、稳定存在的固定开销。
2. 新增正反顺序三状态对照，在两个独立Unity进程中完成24个10秒窗口。空覆盖相对关闭的两轮结果见下表；CPU空钩子热循环合计约0.57微秒，不能直接解释旧约100微秒的间隔差。
3. “保留GPU池但脱离调度”没有显示稳定收益，现有证据不支持把差异直接归因于空通道调度，也不足以确定GPU池是根因。
4. 因此**不做猜测式生产优化**，保留既有功能实现。接下来仍需真实呈现环境的重复对照／更细CPU等待和GPU时间，不能宣布已修复或无回退。

## 三状态实验

- off：关闭原型，释放局部池，调度引用为空。
- empty：实际原型启用但0覆盖，正常调用Tick/Bind/AfterDispatch。
- detached：保持同样4071424 bytes逻辑GPU池与Manager持有的通道，只临时断开pipeline引用；不调用上述钩子。仅测试控制，finally恢复，无普通入口。

每进程12窗口，每窗3秒暖机＋10秒取样。次序off/empty/detached/detached/empty/off/detached/off/empty/empty/off/detached，第二进程完全反转；三状态各出现4次，窗口位置和相同。预分配样本列表、每窗采样前统一GC，CSV写出在采样窗外。与旧方法不完全相同，因此另原样复跑旧方法两次，避免只用新方法否定旧观测。

每窗断言战斗Running、2048存活、0局部组、0快照请求、0回执请求，并检查局部keyword关闭。不是通过暂停模拟、杀死成员或减少人数换取速度。

下列数值均为“每窗中位间隔再取平均”，单位毫秒；不是FPS。

| 进程 | off | empty | detached | empty/off | empty/detached |
|---|---:|---:|---:|---:|---:|
{chr(10).join(rows)}

ProfilerRecorder的Main Thread与GC Allocated In Frame均成功取得。Main Thread是主线程总时间，包含编辑器／等待等，并非纯游戏逻辑CPU占用；计数器可能滞后一帧。逐帧CSV保留墙钟、Main Thread ns和GC bytes，离线重算中位、p95、均值及两个计数器均值，与记录JSON核对一致。

整帧GC指标包含编辑器、场景及测试框架；不能将其全部归因于P6。微基准的零分配也不能代替整帧内存资格。

### CPU分段微基准

9批×2048次热循环，报告每次调用的批次中位微秒；空操作作为计时底噪对照。分配栏是单批当前线程增加字节的最大值，不是GPU／全进程内存。

| 操作 | 每次微秒 | 单批最大托管分配B |
|---|---:|---:|
{chr(10).join(micro)}

context-validity已包含在Tick中，不重复相加。该实验只测直接热调用，不能排除驱动在后续dispatch中的间接成本，也不能将不到1微秒的数值机械外推为所有真实帧的上界。

## 原20秒ABBA方法复现

原P6DisabledAndEmptyChannelShortABBAIntervals方法未改：两次独立Unity启动，每窗20秒、暖机3秒，关闭／开启空覆盖／开启空覆盖／关闭。无新增Recorder，也无新探针的预分配与统一GC。只保留原方法原有汇总JSON，不伪称它有新增逐帧CSV。

| 进程 | off平均中位ms | empty平均中位ms | 差异 |
|---|---:|---:|---:|
{chr(10).join(orig)}

旧scene-03的+12.59%和上述所有结果都保留，不挑选有利轮次。窗口／进程才是对照单位，不能把数十万帧当独立样本制造显著性。两个三状态进程及两个原方法进程不足以证明性能等效。上述结果仍是batchmode协程墙钟／主线程记录，不保证present或实际绘制，**不等于玩家FPS和GPU timestamp**。

## 测试及失败记录

| 本轮最终运行 | 通过/总数 | 跳过 |
|---|---:|---:|
{runs}

perf-before-01首次长探针被Unity测试框架默认180秒超时终止，保存了11个完整窗口及其CSV，列为失败／部分资料，不纳入完整两轮汇总。随后只给该长测量方法增加Timeout(360000)，完成12窗口与写盘；**没有提高生产命令10秒超时**。没有删除失败结果，也没有将NUnit条件断言通过视为性能通过。

生产代码与此前检查点逐文件一致，因此本轮不重复宣称“修复了空路径”。新增的测量代码仅在Editor测试程序集；旧原型GPU五项和EditMode全套再次回归。此前旧内核67、P4/P5六项的结果属于上一检查点，不冒称本轮重跑。

## 保护、交接与下一步

全量9659基线＋24批准路径核对通过。相对上一P6检查点仅LocalOrdersSceneTests.cs改变，载荷08/09有before/diff/收据，0归档引用迁移；其余23个已批准路径保持原字节。正式male/knight separationStrength=48、用户4份数据、37包、HEAD／暂存区、GUID、462归档保持。P3余项和P4/P5真人状态不变。

报告与原始证据在电脑：
- `Docs/InteractionRefinement-20261007/P6-PERFORMANCE-FOLLOWUP-01.md/html`
- `Logs/InteractionRefinement-20261007/P6/performance-followup-01/`：preflight、approved-before、analysis、audit与publication。
- `P6/perf-before-01/`：超时部分结果；`perf-before-02/03/`：完整正反顺序24窗口与CSV。
- `P6/perf-original-repeat-01/02/`：原方法复现；`local-05/`与`editmode-04/`：本轮功能回归。

下一步仍限P6：在有实际呈现保证的独立环境重复off/empty/四组活跃，采集CPU等待与GPU时间；补生产male/knight/远程混编、动态几何和资源峰值。除非进一步证据定位问题，否则不针对未稳定复现的单次百分比盲改导航、分组或正式48参数。

状态仍为 **P6_FUNCTIONAL_PROTOTYPE_PENDING_QUALIFICATION**。本轮是诊断收窄，不是P6全部验收／P7放行。
'''
name='P6-PERFORMANCE-FOLLOWUP-01';assert not(doc/(name+'.md')).exists();(doc/(name+'.md')).write_text(text,encoding='utf-8')
parts=[];table=False
for line in text.splitlines():
 if line.startswith('|'):
  if not table:parts.append('<table>');table=True
  if not re.fullmatch(r'[|:\-\s]+',line):parts.append('<tr>'+''.join('<td>'+html.escape(c.strip())+'</td>' for c in line.strip('|').split('|'))+'</tr>')
  continue
 if table:parts.append('</table>');table=False
 value=re.sub(r'\*\*(.+?)\*\*',r'<strong>\1</strong>',re.sub(r'`([^`]+)`',r'<code>\1</code>',html.escape(line)))
 if line.startswith('### '):parts.append('<h3>'+value[4:]+'</h3>')
 elif line.startswith('## '):parts.append('<h2>'+value[3:]+'</h2>')
 elif line.startswith('# '):parts.append('<h1>'+value[2:]+'</h1>')
 elif line.strip():parts.append('<p>'+value+'</p>')
(doc/(name+'.html')).write_text('<!doctype html><html lang="zh-CN"><meta charset="utf-8"><title>P6性能排查第1轮</title><style>body{max-width:1080px;margin:40px auto;padding:0 24px;background:#f7f9fb;color:#24364a;font:16px/1.85 system-ui,sans-serif}h1,h2{color:#145882}h2{margin-top:38px}table{width:100%;border-collapse:collapse;background:white}td{border:1px solid #d4dde5;padding:8px}code{overflow-wrap:anywhere;font-size:13px}strong{color:#8a411a}</style>'+''.join(parts)+'</html>',encoding='utf-8')
p=doc/'IMPLEMENTATION.md';s=p.read_text(encoding='utf-8');s+='\n\n## P6空覆盖排查第1轮（2026-10-07）\n\n用户授权继续留P6。见[P6性能排查](P6-PERFORMANCE-FOLLOWUP-01.md)。增加三状态正反顺序24窗口、CPU热循环、ProfilerRecorder及原ABBA方法两次复现，生产代码未改；单次12.59%不得视为稳定固定成本。仍需真实呈现／四组活跃与生产混编资格，未P7/P8、未38；48及P3暂缓保持。\n';p.write_text(s,encoding='utf-8')
(doc/'P3-QUALIFICATION-STATE.md').write_text('# 当前交互整改状态\n\nP6功能原型已落地；空覆盖性能继续定位，第1轮三状态及原方法复测见[P6性能排查](P6-PERFORMANCE-FOLLOWUP-01.md)。生产代码未改，未宣称修复／性能等效。真实呈现、四组负载与生产混编待验，未P7/P8或38。P4/P5真人待验，P3余项暂缓、正式48保持。\n',encoding='utf-8')
files=[doc/(name+'.md'),doc/(name+'.html'),doc/'IMPLEMENTATION.md',doc/'P3-QUALIFICATION-STATE.md']
for e in a['evidence']:assert sha(ROOT/e['path'])==e['sha256']
for path,r in a['approvedChanges'].items():assert sha(ROOT/path)==r['expectedSha256']
for f in old['files']:
 p=backup/Path(f['path']).name if Path(f['path']).name in ['IMPLEMENTATION.md','P3-QUALIFICATION-STATE.md'] else ROOT/f['path'];assert sha(p)==f['sha256']
r={'status':'P6_FUNCTIONAL_PROTOTYPE_PENDING_QUALIFICATION','diagnosisOnly':True,'productionChanged':False,'performanceAccepted':False,'p7Started':False,'newBuildProduced':False,'files':[{'path':p.relative_to(ROOT).as_posix(),'bytes':p.stat().st_size,'sha256':sha(p)} for p in files],'evidenceVerified':len(a['evidence']),'sourceMetaVerified':len(a['approvedChanges']),'project':project_check(),'userData':user_check(),'activeProcesses':processes(),'lock':(ROOT/'Temp/UnityLockfile').exists(),'finishedAt':datetime.datetime.now().isoformat()};r['passed']=r['project']['passed'] and r['userData']['passed'] and not r['activeProcesses'] and not r['lock'];save(folder/'publication.json',r);assert r['passed'];print(json.dumps(r,ensure_ascii=False,indent=2))
