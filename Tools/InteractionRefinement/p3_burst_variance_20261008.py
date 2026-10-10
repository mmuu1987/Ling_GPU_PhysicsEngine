from p3_density_runtime_20261008 import *
import re
sys.stdout.reconfigure(encoding='utf-8')
OUT=LOG/'P3'/'burst-variance-20261008-01'
assert not OUT.exists() and not processes() and not(ROOT/'Temp/UnityLockfile').exists()
assert project_check(full=True)['passed'] and user_check()['passed']
D=LOG/'P3'/'burst-production-20261008-01'
d=json.loads((D/'decision.json').read_text(encoding='utf-8'));assert not d['adopted'] and d['protectionPassed']
OUT.mkdir()
save(OUT/'scope.json',{'scope':'Read-only analysis of eight existing rendered windows; no Unity, candidate activation, new benchmark or changed acceptance gates. Fixed 3-second buckets and existing recorder channels. Logs unaligned unless explicit matching timestamp exists.','limitations':'Recorder.LastValue and latest GPU samples are not guaranteed aligned to callback interval; GPU profiler fallback uses latest completed frame minus4. GC allocated bytes are not GC collection/pause duration. Main Thread wall time is not pure compute. Zero unavailable timing is excluded. No CPU clock/OS process telemetry.'})
reserve={'status':'feasible_reserved_not_adopted','userDirection':'2026-10-08 用户要求标记Burst可行作为后备；其他方向实在无解时再用。','conditionalUse':True,'currentlyEnabled':False,'formalGatePassed':False,'correctnessAndDisabledFallbackPassed':True,'candidateSnapshot':str((D/'candidate').relative_to(ROOT)),'testSnapshot':str((D/'support-candidate').relative_to(ROOT)),'registry':str((D/'registry.json').relative_to(ROOT)),'decision':str((D/'decision.json').relative_to(ROOT)),'extraNativeMiBPerGrid':4.31,'remaining':'Record explicit tradeoff before activation; retain original failed verdict. New validation for intended scope, cold-start/player/AOT still pending. No packaging authorization implied.'}
save(D/'fallback-reserve.json',reserve)
allrows=[]
for tag in ['p3-burst-render-'+str(i) for i in range(1,5)]+['p3-burst-production-abba-'+str(i) for i in range(1,5)]:
 folder=LOG/'P3'/tag
 rows=list(csv.DictReader((folder/'frames.csv').open(encoding='utf-8')))
 receipt=json.loads((folder/'process.json').read_text(encoding='utf-8'))
 base=float(rows[0]['seconds']); intervals=[]
 for a,b in zip(rows,rows[1:]):
  x={k:float(v) for k,v in b.items()};x['intervalMs']=1000*(float(b['seconds'])-float(a['seconds']));x['relativeSeconds']=float(b['seconds'])-base;intervals.append(x)
 def stats(xs):
  z={'samples':len(xs),'over33ms':sum(x['intervalMs']>1000/30 for x in xs)}
  for k in ['intervalMs','mainThreadMs','renderThreadMs','cpuTimingMs','gpuTimingMs','gcBytes']:
   vals=[x[k] for x in xs if k in x and math.isfinite(x[k]) and (x[k]>=0 if k=='gcBytes' else x[k]>0)]
   z[k]={'valid':len(vals),'median':statistics.median(vals) if vals else None,'p95':sorted(vals)[min(len(vals)-1,math.ceil(.95*len(vals))-1)] if vals else None,'max':max(vals) if vals else None}
  return z
 buckets=[dict(seconds=[i*3,(i+1)*3],**stats([x for x in intervals if i*3<=x['relativeSeconds']<(i+1)*3])) for i in range(5)]
 log=(folder/'Editor.log').read_text(encoding='utf-8',errors='replace').splitlines()
 patterns={'compile':r'compil|reloadassembl|domain reload','gcOrUnload':r'garbage|\bGC\b|unloading|unused assets','errorOrException':r'\bexception\b|\berror\b','shader':r'shader.*compil|compil.*shader','focus':r'focus|background'}
 hits={k:[{'line':i+1,'text':line[:600]} for i,line in enumerate(log) if re.search(p,line,re.I)] for k,p in patterns.items()}
 save(OUT/(tag+'-log-hits.json'),hits)
 allrows.append({'tag':tag,'columns':list(rows[0]),'performanceReceipt':receipt.get('performance',{}),'wholeWindow':stats(intervals),'buckets':buckets,'logHits':{k:len(v) for k,v in hits.items()},'logAlignment':'No causal attribution from unaligned log text; see bounded line excerpts.'})
save(OUT/'analysis.json',allrows)
check=project_check(full=True);assert check['passed'] and user_check()['passed'] and not processes() and not(ROOT/'Temp/UnityLockfile').exists()
save(OUT/'completion.json',{'completed':True,'project':check,'userData':user_check(),'sourceChanged':False,'unityStarted':False,'fallbackReserved':True,'formalVerdictUnchanged':True})
for r in allrows:
 print(r['tag'], 'GPU source',r['performanceReceipt'].get('gpuTimingSource'), 'logHits',r['logHits'],flush=True)
 for b in r['buckets']:print(b['seconds'],{k:b[k]['median'] for k in ['intervalMs','mainThreadMs','renderThreadMs','gpuTimingMs','gcBytes']},flush=True)
print('READONLY_ANALYSIS_COMPLETE',flush=True)
