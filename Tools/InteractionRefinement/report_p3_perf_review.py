from common_p3 import *
import sys,html,zipfile,hashlib
sys.stdout.reconfigure(encoding='utf-8');d=P3/'perf-review-01';m=json.loads((d/'analysis.json').read_text(encoding='utf-8'));audit=json.loads((d/'audit.json').read_text(encoding='utf-8'));assert audit['protectionPassed'] and audit['captureAndRegressionPassed'];assert not processes() and not(ROOT/'Temp/UnityLockfile').exists();doc=ROOT/'Docs/InteractionRefinement-20261007';assert not(doc/'P3-PERFORMANCE-REVIEW.md').exists()
# Verify raw evidence and the existing deduplicated archive before publishing.
for x in m['sourceFiles']:
 p=ROOT/x['path'];assert p.stat().st_size==x['bytes'] and sha(p)==x['sha256']
arc=ROOT/'Logs/WorkspaceArchive-20261007-01';assert sha(arc/'manifest.json')=='ed2adbf1e5abfb5b0d51b825ddcc246c86c166cc4a67d73acc84dd2604c98397';manifest=json.loads((arc/'manifest.json').read_text(encoding='utf-8'));assert len(manifest['files'])==462
with zipfile.ZipFile(arc/'extra-files.zip') as z:
 assert z.testzip() is None
 for x in manifest['files']:
  s=x['storage'];b=(ROOT/s['path']).read_bytes() if s['kind']=='existing-file' else z.read(s['entry']);assert len(b)==x['bytes'] and hashlib.sha256(b).hexdigest()==x['sha256']
review=m['blocks']['review'];old=m['blocks']['old'];a=review['conditions']['24'];b=review['conditions']['48'];verdict='新八轮仍有复核项，性能未签收' if m['newBlockNeedsReview'] else '新八轮未触发预设复核线；旧异常保留且未归因，仍待真人验收'
def f(x):return '不可用' if x is None else f'{x:.3f}'
metrics=[('墙钟中位/ms','wallMedianMs'),('墙钟P95/ms','wallP95Ms'),('墙钟P99/ms','wallP99Ms'),('渲染样本/秒','samplesPerSecond'),('超过50ms的间隔占比/%','over50msPct'),('Unity中位/ms','unityMedianMs'),('UnityP95/ms','unityP95Ms'),('可用GPU中位/ms','gpuMedianMs'),('可用GPUP95/ms','gpuP95Ms')]
summary='\n'.join('| '+label+' | '+f(a[key])+' | '+f(b[key])+' | '+f(review['deltasPct'][key])+'% |' for label,key in metrics)
rows='\n'.join('| '+r['tag']+' | '+str(r['strength'])+' | '+f(r['wallMedianMs'])+' | '+f(r['wallP95Ms'])+' | '+f(r['wallP99Ms'])+' | '+str(r['over50msCount'])+' | '+f(r['samplesPerSecond'])+' |' for r in m['runs'] if r['block']=='review')
noise='；'.join(str(x['strength'])+' '+x['metric']+f" 跨轮范围/最小值={x['rangeOverMinPct']:.1f}%" for x in review['repeatNoise']) or '无'
flags='、'.join(review['over15PctMetrics']) or '无'
envRows='\n'.join('| '+r['tag']+' | '+f(r['environment']['wholeRunSystemCpuMedianPct'])+' | '+f(r['environment']['wholeRunSystemCpuP95Pct'])+' | '+f(r['environment']['wholeRunSystemCpuMaxPct'])+' |' for r in m['runs'] if r['block']=='review')
pairRows='\n'.join('| '+str(i+1)+' | '+f(x['wallMedianDeltaPct'])+'% | '+f(x['wallP95DeltaPct'])+'% |' for i,x in enumerate(m['adjacentPairs']))
text=f'''# P3 尾部帧时复核 · 2026-10-07

**{verdict}。正式48保持；P3整阶段不自动签收，未P4、未出38。**

## 做了什么

保持生产源代码、资产、Shader、人数、半径、模型缩放不变。沿用同一已验证Editor观察器和runner，运行8轮顺序48/24/24/48/24/48/48/24，24与48各4轮。每轮重新启动自有Editor，经正式UI入口进入Green默认2048人场景，1920×1080；35秒热身后记录15秒接敌窗口。观察器不额外回读Agent、不截图。两条件都使用测试自有AudioListener；这不是生产音频修复，也不是发行包性能测试。

记录系统CPU负载（Windows GetSystemTimes，1Hz，缓冲后落盘）及始末进程CPU累计量，没有关闭其他程序、改变电源方案、改限帧或降低画质。新批有低频系统采样，旧四轮没有，故两批分开展示，不冒充完全同批重复。

## 新八轮结果

每条件4轮分位数取算术均值；不把所有帧混成一个大样本，不丢弃任何差轮。正GPU样本不覆盖每帧且可能延迟。

| 指标 | 24 | 48 | 48相对24 |
|---|---:|---:|---:|
{summary}

墙钟间隔来自相邻Game相机渲染结束回调的realtime时间戳，逐轮确认帧号连续；不是显示器真正呈现时刻。长帧也保留在统计中。

| 轮次 | 强度 | 墙钟中位/ms | 墙钟P95/ms | 墙钟P99/ms | >50ms数量 | 渲染样本/秒 |
|---|---:|---:|---:|---:|---:|---:|
{rows}

预先设置间+15%复核线：{flags}。

同条件跨轮>20%噪声标记：{noise}。

四个相邻24/48配对（前后顺序各半），仅用于观察时序敏感性，不是显著性证明：

| 配对 | 墙钟中位变化 | 墙钟P95变化 |
|---|---:|---:|
{pairRows}

## 旧四轮没有被丢弃

旧批的墙钟中位均值5.205→5.345ms、P95均值7.600→8.570ms；48的Unity P95两轮差异31.3%，墙钟P95也有>20%差异。这些记录仍在原目录；新结果不改写旧批，也不能被称为已经修复旧异常。

旧批两组均有约50ms以上长间隔。不能仅凭GC分配字节把长帧说成GC回收，也不能把同一CSV行的Main Thread/GPU值当成与墙钟间隔严格同步。分析文件提供前后两帧计数器滞后相关检查，相关性不等于根因。

## 新增定位线索：周期性工作，而不是直接归咎于48

旧4轮与新8轮，每轮15秒窗口都恰好有85个超过50ms的间隔；各轮这些长帧之间的间距中位数为约0.172–0.179秒。它不是只在48出现的孤立坏轮。

逐帧时序复算发现：墙钟间隔与**下一行**Main Thread计数器的相关系数为0.9979–1.0000，而不是与同一行相配。这个计数器存在约一帧的采集延后。由此不能直接说主线程“计算”了50ms，主线程等待也可能计入其中，更不能凭同一行GPU约5ms就宣布排除了GPU/同步等待。

只读源码检查发现两个后续诊断候选，**尚未实验证实根因**：

1. **CPU地形流场重建**：源码默认值及ForestTerrain资产中的动态流场配置为0.35秒（本批未导出运行时配置），代码对军团初次截止时间错开；双军团周期可能形成约0.175秒的交替工作。`TerrainNavigationRuntime.Upload`同步执行`CreateFlowField`及可选LaneApproach，再上传结果。经理已有`TerrainCompletedFields`、`TerrainTotalSolveMilliseconds`等CPU计数器，可直接用于下一轮逐帧对齐，不必回读Agent。
2. **HUD刷新**：`RefreshLegacyRuntimeUI`每0.15秒刷新。UI已有节点复用和变更判断，不能凭周期相近就说它每次销毁重建。

下一轮优先把已有CPU流场耗时增量、完成次数与长帧时间戳对齐，再区分PlayerLoop、EditorLoop、UI和等待。`ComputePipelineProfiler`部分标记目前只是占位，不能把未包裹实际工作的0读数当零成本。没有修改流场频率或调度；不在P3顺手推进P6导航改造。线索和源码SHA保存在`source-inspection.json`。

## 环境记录的边界

下表系统CPU为整个单轮，含启动、导入、退出与runner收尾，**不是严格对齐35–50秒计时窗口的背景负载**。不能据此排除亚秒干扰；GPU时钟、温度及OS呈现链路未采样。未修改电源方案，逐轮原始电源方案文本保存在环境记录中。

| 轮次 | 系统CPU中位/% | 系统CPUP95/% | 系统CPU最大/% |
|---|---:|---:|---:|
{envRows}

始末CPU差值只覆盖两次快照均存在的进程；已退出的Unity不在该差值名单中。环境记录保留在电脑，不将整个进程清单搬回工作空间。没有证据就不归咎于某个应用、驱动、GC或分离算法。

## 保护与后续

- 八轮采集全部通过；原始CSV、Unity分位数、真实时钟间隔及来源SHA独立核验。
- 全量保护审计通过：9627基线文件与12项批准变更、4份用户数据、37包387文件SHA、HEAD/暂存区/GUID；最终无Unity/工程锁。
- 本轮没有修改C#或生产资产，因此没有重复冒充“新增652项测试”；沿用此前同字节源码下652/652及41/41通过记录，当前项目全量SHA重新核验。
- 462项清理归档重新核验，清单SHA未变。更改的状态/计划文档先保存原字节备份；旧报告和原始轮次保留。
- **结论：{verdict}。** 若仍有复核项，下一步是单独CPU/Editor调度诊断，重点区分PlayerLoop、EditorLoop、同步等待和回收；不再仅重复均值对比。额外诊断须与本批性能数字分开，不用关日志、降画质或删除慢帧来“过线”。
- 真人仍需在当前工程确认疏密、动画/抖动及P1/P2编辑交互；37是未更新的回退包。阶段未验收，不自动进入P4。

权威证据：`Logs/InteractionRefinement-20261007/P3/perf-review-01/`下的scope.json、sequence.json、analysis.json、audit.json及publication.json。逐轮原始证据在P3根目录的`perf-review-XX-24/48`目录。
'''
# Preserve previously published bytes before changing the navigation/state documents.
previous=json.loads((P3/'qualification-published.json').read_text(encoding='utf-8'));before=d/'docs-before';assert not before.exists();before.mkdir();replaced=[]
for name in ['IMPLEMENTATION.md','P3-QUALIFICATION-STATE.md']:
 p=doc/name;expected=next(x['sha256'] for x in previous['files'] if x['path']==p.relative_to(ROOT).as_posix());assert sha(p)==expected,'Document changed outside this checkpoint; stop'
 data=p.read_bytes();(before/name).write_bytes(data);replaced.append({'path':p.relative_to(ROOT).as_posix(),'oldSha256':expected,'backup':(before/name).relative_to(ROOT).as_posix()})
plan=(doc/'IMPLEMENTATION.md').read_text(encoding='utf-8');plan+='\n\n## P3 尾部帧时复核（2026-10-07）\n\n8轮平衡对照已完成，全量保护通过。'+verdict+'。正式48保持，未P4/38。详见[P3性能复核](P3-PERFORMANCE-REVIEW.md)及perf-review-01证据目录。\n'
(doc/'IMPLEMENTATION.md').write_text(plan,encoding='utf-8');(doc/'P3-QUALIFICATION-STATE.md').write_text('# P3最新状态：尾部帧时复核\n\n'+verdict+'。正式48保持；P3未自动签收，未P4/38。\n\n最新报告P3-PERFORMANCE-REVIEW.md及P3-PERFORMANCE-REVIEW.html；证据Logs/InteractionRefinement-20261007/P3/perf-review-01。旧P3-QUALIFICATION报告保留为上一检查点。之前状态/计划原字节在perf-review-01/docs-before。\n',encoding='utf-8');(doc/'P3-PERFORMANCE-REVIEW.md').write_text(text,encoding='utf-8')
# Minimal self-contained semantic HTML; no external assets.
import re
def inline(s):return re.sub(r'\*\*(.+?)\*\*',r'<strong>\1</strong>',re.sub(r'`([^`]+)`',r'<code>\1</code>',html.escape(s)))
parts=[];table=False
for l in text.splitlines():
 if l.startswith('|'):
  if not table:parts.append('<div class="scroll"><table>');table=True
  if not re.fullmatch(r'[|:\-\s]+',l):parts.append('<tr>'+''.join('<td>'+inline(x.strip())+'</td>' for x in l.strip('|').split('|'))+'</tr>')
  continue
 if table:parts.append('</table></div>');table=False
 if l.startswith('#'):
  level=min(4,len(l)-len(l.lstrip('#')));parts.append(f'<h{level}>'+inline(l[level:].strip())+f'</h{level}>')
 elif l:parts.append('<p>'+inline(l)+'</p>')
if table:parts.append('</table></div>')
page='<!doctype html><html lang="zh-CN"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>P3尾部帧时复核</title><style>body{margin:0;background:#eef2f0;color:#193a32;font:16px/1.8 system-ui}main{max-width:1100px;margin:auto;background:white;padding:32px}h1{color:#174d3f}h2{margin-top:38px;border-top:1px solid #dce6e0;padding-top:15px}p{overflow-wrap:anywhere}table{border-collapse:collapse;width:100%;font-size:14px}td{padding:9px;border-bottom:1px solid #dce6e0;white-space:nowrap}tr:first-child{background:#edf3ef;font-weight:bold}.scroll{overflow-x:auto}code{background:#eff3f0;font-size:13px}strong{color:#805317}@media(max-width:600px){main{padding:16px}}</style><main>'+''.join(parts)+'</main></html>'
(doc/'P3-PERFORMANCE-REVIEW.html').write_text(page,encoding='utf-8')
paths=[doc/n for n in ['P3-PERFORMANCE-REVIEW.md','P3-PERFORMANCE-REVIEW.html','IMPLEMENTATION.md','P3-QUALIFICATION-STATE.md']]
r={'status':'PUBLISHED','verdict':verdict,'needsReview':m['newBlockNeedsReview'],'p3Accepted':False,'publishedAt':datetime.datetime.now().isoformat(),'files':[{'path':p.relative_to(ROOT).as_posix(),'bytes':p.stat().st_size,'sha256':sha(p)} for p in paths],'replacedDocuments':replaced,'archiveFilesVerified':462,'archiveManifestSha256':sha(arc/'manifest.json'),'project':project_check(),'userData':user_check(),'activeProcesses':processes(),'lock':(ROOT/'Temp/UnityLockfile').exists()};assert r['project']['passed'] and r['userData']['passed'] and not r['activeProcesses'] and not r['lock'];save(d/'publication.json',r);print(json.dumps(r,ensure_ascii=False,indent=2))
