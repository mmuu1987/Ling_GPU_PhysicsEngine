from pathlib import Path
import json,csv,datetime,hashlib,re
R=Path(__file__).resolve().parents[2];P=R/'outputs/P11';D=R/'outputs/P9ColdAttack-20261009-01'
def date(s):return datetime.datetime.fromisoformat(s.replace('Z','+00:00')).timestamp()
keys=['label','kind','members','attempts','callback_ms','observed_ms','readback_ms','plan_ms','upload_ms','max_yield_gap_ms','navigation_refreshes','observed_frame_delta']
result={}
for name in ['warm-02','cold-01','restored-01']:
 V=P/'Logs/InteractionRefinement-20261007/P8'/('cold-attack-'+name)
 process=json.loads((V/'process.json').read_text(encoding='utf-8'));assert process['passed']
 summary=[dict(zip(keys,row)) for row in csv.reader((V/'cold-summary.csv').read_text(encoding='utf-8').splitlines()) if row]
 for row in summary:
  for k in keys[2:]:row[k]=float(row[k])
 trace=list(csv.DictReader((V/'trace-joint-session-Attack.csv').read_text(encoding='utf-8-sig').splitlines()))
 pairs=[(float(b['elapsed_ms'])-float(a['elapsed_ms']),a,b) for a,b in zip(trace,trace[1:]) if a['phase']=='before-yield' and b['phase']=='after-yield']
 gap,a,b=max(pairs,key=lambda x:x[0]);start=date(a['utc']);end=date(b['utc'])
 samples=[json.loads(l) for l in (V/'process-cpu.jsonl').read_text(encoding='utf-8-sig').splitlines() if l.strip()]
 left=max((x for x in samples if date(x['utc'])<=start),key=lambda x:date(x['utc']));right=min((x for x in samples if date(x['utc'])>=end),key=lambda x:date(x['utc']))
 def processes(x):
  p=x['processes'];return p if isinstance(p,list) else [p]
 pm={(p['pid'],p['started']):p for p in processes(left)};cpu=[]
 for p in processes(right):
  old=pm.get((p['pid'],p['started']))
  if old is not None:cpu.append({'pid':p['pid'],'name':p['name'],'delta_cpu_ms':p['cpu_ms']-old['cpu_ms']})
 lines=(V/'Editor.log').read_text(encoding='utf-8',errors='replace').splitlines()
 hits=[{'line':i+1,'text':l} for i,l in enumerate(lines) if 'Shader warning in' in l or 'P9_COLD' in l or 'plan-applied-gpu' in l]
 result[name]={'passed':True,'runPath':str(V),'summary':summary,'largestTraceGap':{'ms':gap,'before':a,'after':b},'cpuBracket':{'start':left['utc'],'end':right['utc'],'wall_ms':(date(right['utc'])-date(left['utc']))*1000,'processes':cpu,'note':'0.5s polling; bracketing CPU deltas, not exact profiler samples'},'logEvidence':hits}
cache=json.loads((D/'cache-aba.json').read_text(encoding='utf-8'))
result['cache']={'originalRestored':cache['cacheRestoredExactly'],'originalStillExactAfterRestoredRun':cache['original']==cache['afterRestoredRun'],'recompiledBytesMatchOriginal':{f:cache['original'][f]['sha256']==v['sha256'] for f,v in cache['generated'].items()},'files':cache['originalFiles'],'bytes':cache['originalBytes']}
start=date(result['cold-01']['largestTraceGap']['before']['utc']);end=date(result['cold-01']['largestTraceGap']['after']['utc'])
result['cache']['writtenInsideColdStall']={f:v for f,v in cache['generated'].items() if start<=v['mtime_ns']/1e9<=end}
for f,v in result['cache']['writtenInsideColdStall'].items():
 data=(D/'combat-cache-generated-cold'/f).read_bytes();v['readableResourceNames']=[t.decode('ascii') for t in re.findall(rb'[A-Za-z_][A-Za-z0-9_]{4,}',data) if b'local' in t.lower() or b'agent' in t.lower()][:40]
(D/'analysis.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
compact={k:{'attack':v['summary'][0],'secondAttack':v['summary'][-1],'gap':v['largestTraceGap'],'cpu':v['cpuBracket'],'warnings':v['logEvidence']} for k,v in result.items() if k!='cache'};compact['cache']=result['cache'];(D/'analysis-compact.json').write_text(json.dumps(compact,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps(compact,ensure_ascii=False,indent=2))
