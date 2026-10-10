from common_p3 import *
import sys,csv,math,statistics as st
sys.stdout.reconfigure(encoding='utf-8');dest=P3/'perf-review-01';out=dest/'analysis.json';assert not out.exists()
seq=json.loads((dest/'sequence.json').read_text(encoding='utf-8'));assert seq['complete'] and len(seq['runs'])==8
sources=[]
def source(p):sources.append({'path':p.relative_to(ROOT).as_posix(),'bytes':p.stat().st_size,'sha256':sha(p)});return p
def q(v,p):return sorted(v)[math.ceil(len(v)*p)-1]
def corr(a,b):
 if len(a)<3:return None
 ma=st.mean(a);mb=st.mean(b);den=math.sqrt(sum((x-ma)**2 for x in a)*sum((y-mb)**2 for y in b));return sum((x-ma)*(y-mb) for x,y in zip(a,b))/den if den else None
old=['qualification-perf24-a','qualification-perf48-a','qualification-perf48-b','qualification-perf24-b'];new=[x['tag'] for x in seq['runs']];runs=[]
for tag in old+new:
 process=json.loads(source(P3/tag/'process.json').read_text(encoding='utf-8'));assert process['passed'] and process['exitCode']==0
 r=json.loads(source(P3/tag/'performance.json').read_text(encoding='utf-8'));assert r['passed'] and r['renderedEditor'] and r['cameraFixed'] and not r['synchronousAgentReadback'] and (r['width'],r['height'],r['units'])==(1920,1080,2048)
 with source(P3/tag/'frames.csv').open() as f:rows=list(csv.DictReader(f))
 assert len(rows)==r['frames']==r['renderedGameSamples'];seconds=[float(x['seconds']) for x in rows];assert 35<=seconds[0]<36 and 50<=seconds[-1]<52
 wall=[(b-a)*1000 for a,b in zip(seconds,seconds[1:])];assert all(math.isfinite(x) and x>0 for x in wall);assert all(int(b['frame'])-int(a['frame'])==1 for a,b in zip(rows,rows[1:]))
 unity=[float(x['frameMs']) for x in rows];gpu=[float(x['gpuTimingMs']) for x in rows if float(x['gpuTimingMs'])>0];assert len(gpu)==r['gpuSamples'] and math.isclose(q(unity,.95),r['frameP95Ms'],rel_tol=.001)
 if gpu:assert math.isclose(q(gpu,.5),r['gpuMedianMs'],rel_tol=.001)
 gaps=[seconds[i+1] for i,x in enumerate(wall) if x>50];over50=sum(x>50 for x in wall)
 item={'tag':tag,'block':'old' if tag in old else 'review','strength':int(r['separationStrength']),'frames':len(rows),'wallMedianMs':q(wall,.5),'wallP95Ms':q(wall,.95),'wallP99Ms':q(wall,.99),'wallMaxMs':max(wall),'samplesPerSecond':len(wall)/(seconds[-1]-seconds[0]),'over33msPct':100*sum(x>1000/30 for x in wall)/len(wall),'over50msPct':100*over50/len(wall),'over50msCount':over50,'longFrameGapMedianSeconds':st.median(b-a for a,b in zip(gaps,gaps[1:])) if len(gaps)>1 else None,'unityMedianMs':r['frameMedianMs'],'unityP95Ms':r['frameP95Ms'],'gpuMedianMs':r['gpuMedianMs'] if gpu else None,'gpuP95Ms':r['gpuP95Ms'] if gpu else None,'positiveGpuCoveragePct':len(gpu)/len(rows)*100,'gpu':r['gpu'],'vSync':r['vSyncCount'],'targetFrameRate':r['targetFrameRate']}
 # Inspect counter latency, not a causal attribution of stalls to CPU/GC/GPU.
 item['lagCorrelation']={}
 for lag in [-2,-1,0,1,2]:
  pairs=[(wall[i-1],float(rows[i+lag]['mainThreadMs'])) for i in range(1,len(rows)) if 0<=i+lag<len(rows)]
  item['lagCorrelation'][str(lag)]=corr([x for x,y in pairs],[y for x,y in pairs])
 if tag in new:
  env=json.loads(source(dest/(tag+'-environment.json')).read_text(encoding='utf-8'));cpu=[x['cpuBusyPct'] for x in env['systemCpuSamples']];assert len(cpu)>20
  before={p['Id']:p for p in env['before']['processCpuCumulative']};top=[]
  for p in env['after']['processCpuCumulative']:
   a=before.get(p['Id'])
   if a and p['ProcessName']==a['ProcessName'] and p['CPU'] is not None and a['CPU'] is not None:
    delta=p['CPU']-a['CPU']
    if delta>0:top.append({'name':p['ProcessName'],'pid':p['Id'],'cpuSecondsDuringWholeRun':delta})
  item['environment']={'cpuSamples':len(cpu),'wholeRunSystemCpuMedianPct':st.median(cpu),'wholeRunSystemCpuP95Pct':q(cpu,.95),'wholeRunSystemCpuMaxPct':max(cpu),'survivingProcessesByCpuDelta':sorted(top,key=lambda p:p['cpuSecondsDuringWholeRun'],reverse=True)[:8],'note':'Whole-run including launch/import/exit. Process deltas omit processes that ended. Cannot rule out subsecond interference or align a specific long frame.'}
 runs.append(item)
assert len({(r['gpu'],r['vSync'],r['targetFrameRate']) for r in runs})==1, 'Rendering settings changed across rounds'
gpuAvailable=all(r['gpuMedianMs'] is not None for r in runs if r['block']=='review')
metrics=['wallMedianMs','wallP95Ms','wallP99Ms','samplesPerSecond','over33msPct','over50msPct','unityMedianMs','unityP95Ms','gpuMedianMs','gpuP95Ms'];blocks={}
for block in ['old','review']:
 selected=[r for r in runs if r['block']==block];conditions={};noise=[]
 for strength in [24,48]:
  a=[r for r in selected if r['strength']==strength];conditions[str(strength)]={k:st.mean(r[k] for r in a) if all(r[k] is not None for r in a) else None for k in metrics};conditions[str(strength)]['n']=len(a)
  for metric in ['wallMedianMs','wallP95Ms','unityMedianMs','unityP95Ms']:
   values=[r[metric] for r in a];spread=100*(max(values)/min(values)-1)
   if spread>20:noise.append({'strength':strength,'metric':metric,'rangeOverMinPct':spread})
 deltas={k:100*(conditions['48'][k]/conditions['24'][k]-1) if conditions['24'][k] and conditions['48'][k] is not None else None for k in metrics}
 flags=[k for k in ['wallMedianMs','wallP95Ms','unityMedianMs','unityP95Ms','gpuMedianMs','gpuP95Ms'] if deltas[k] is not None and deltas[k]>15]
 blocks[block]={'conditions':conditions,'deltasPct':deltas,'over15PctMetrics':flags,'repeatNoise':noise}
pairs=[]
for i in range(0,8,2):
 pair=[next(r for r in runs if r['tag']==new[j]) for j in [i,i+1]];a=next(r for r in pair if r['strength']==24);b=next(r for r in pair if r['strength']==48);pairs.append({'tags':[r['tag'] for r in pair],'wallMedianDeltaPct':100*(b['wallMedianMs']/a['wallMedianMs']-1),'wallP95DeltaPct':100*(b['wallP95Ms']/a['wallP95Ms']-1)})
result={'status':'P3_PERFORMANCE_REVIEW_ANALYZED','runs':runs,'blocks':blocks,'adjacentPairs':pairs,'sourceFiles':sources,'newBlockNeedsReview':bool(blocks['review']['over15PctMetrics'] or blocks['review']['repeatNoise'] or not gpuAvailable),'oldNoiseRetained':True,'p3Accepted':False,'limitations':['Editor only, test-owned listener in both conditions; no production change.','No observer agent readback in timing window; production behavior unchanged.','GPU samples are incomplete and delayed; Main Thread counter is also not assumed to align with same CSV row.','System CPU telemetry spans entire launch/run/exit, not strictly synchronized to measured window; no GPU-clock or OS presentation trace.','Four new repetitions per strength are not a proof of equivalence or significance. No outliers removed.','Long frames and P99 reported even when P95 looks favorable. Human animation judgment remains open.']};save(out,result);print(json.dumps({k:result[k] for k in ['status','blocks','adjacentPairs','newBlockNeedsReview']},ensure_ascii=False,indent=2))
