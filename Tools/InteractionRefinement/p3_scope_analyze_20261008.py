from p3_density_runtime_20261008 import *
sys.stdout.reconfigure(encoding='utf-8')
O=LOG/'P3'/'thread-scope-20261008-01';F=LOG/'P3'/'p3-thread-scope-1'
d=json.loads((O/'summary.json').read_text(encoding='utf-8'));assert d['protectionPassed'] and d['diagnosticRestored']
w=list(csv.DictReader((F/'wait-probe.csv').open(encoding='utf-8')));cpu=list(csv.DictReader((F/'os-counters.csv').open(encoding='utf-8')))
frequency=int(w[0]['frequency']);assert all(int(x['frequency'])==frequency for x in w+cpu)
start,end=int(w[0]['clock']),int(w[-1]['clock']);assert start<end
rows=[]
for a,b in zip(cpu,cpu[1:]):
 t0,t1=int(a['qpc']),int(b['qpc'])
 if t0<start or t1>end:continue
 dt=(t1-t0)/frequency;assert .5<dt<3
 delta=lambda k:int(b[k])-int(a[k])
 total=delta('systemKernel100ns')+delta('systemUser100ns');idle=delta('systemIdle100ns');owned=delta('unityKernel100ns')+delta('unityUser100ns')
 assert total>0 and 0<=idle<=total and owned>=0
 rows.append({'relativeStartSeconds':(t0-start)/frequency,'relativeEndSeconds':(t1-start)/frequency,'systemBusyPercent':100*(total-idle)/total,'unityPercentOfSystemCpuCapacity':100*owned/total,'unityCpuCoreEquivalents':owned/1e7/dt,'counterReadCostMs':1000*int(b['readCostTicks'])/frequency})
assert len(rows)>=10
save(O/'aligned-os-intervals.json',rows)
summary={'measurementSeconds':(end-start)/frequency,'completeOneSecondIntervals':len(rows),'logicalCpuCount':d['waitProbes'][0]['osStatus']['logicalCpuCount'],'cpu':{k:{'median':statistics.median(r[k] for r in rows),'min':min(r[k] for r in rows),'max':max(r[k] for r in rows)} for k in ['systemBusyPercent','unityPercentOfSystemCpuCapacity','unityCpuCoreEquivalents','counterReadCostMs']},'threadScope':d['waitProbes'][0],'notPerformanceQualification':True,'alignment':'Only complete CPU intervals within first-last diagnostic QPC ticks; native QueryPerformanceFrequency equals Unity Stopwatch.Frequency. Not a per-frame causal join. ReadCost is just counter API calls, not total sampler overhead.','limitations':'No GPU clock/OS process enumeration/per-core CPU/main-thread CPU clocks. Low total CPU does not exclude one busy core, frequency changes, scheduling stalls, or GPU contention. Previous Burst/ABBA windows were different sessions.'}
save(O/'analysis.json',summary);print(json.dumps(summary,ensure_ascii=False,indent=2),flush=True)
assert project_check(full=True)['passed'] and user_check()['passed'] and not processes() and not(ROOT/'Temp/UnityLockfile').exists()
