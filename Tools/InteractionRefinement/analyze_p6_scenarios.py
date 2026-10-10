from common_p6 import *
import sys,csv,statistics,math
sys.stdout.reconfigure(encoding='utf-8');dest=P6/'scenario-followup-01';assert not(dest/'analysis.json').exists();result={'renderRuns':{},'functional':{}}
for tag in ['rendered-load-02','rendered-load-03']:
 r=json.loads((P6/tag/'process.json').read_text(encoding='utf-8'));assert r['passed'];report=json.loads((P6/tag/'p6-rendered-load.json').read_text(encoding='utf-8'));assert len(report['rows'])==6
 for i,row in enumerate(report['rows'],1):
  with (P6/tag/f"render-{i:02d}-{row['mode']}.csv").open() as f:raw=list(csv.DictReader(f))
  wall=sorted(float(r['wallMs']) for r in raw);assert len(wall)==row['frames'];assert row['cameraFrames']>=max(10,len(wall)//2);assert row['alive']==2048
  assert row['groups']==(4 if row['mode']=='four' else 0)
  if row['mode']=='four':assert row['movingGroups']==4 and row['movingMembers']>=4 and row['selectedMeanDisplacement']>.2
  gpu=[float(x['gpuMs']) for x in raw if float(x['gpuMs'])>0];assert len(gpu)==row['gpuTimingSamples']
  values={'medianMs':wall[len(wall)//2],'p95Ms':wall[int((len(wall)-1)*.95)],'mainThreadMeanMs':statistics.mean(float(x['mainThreadNs']) for x in raw)/1e6,'gpuMeanMs':statistics.mean(gpu) if gpu else 0}
  for k,v in values.items():assert math.isclose(v,row[k],rel_tol=1e-8,abs_tol=1e-8),(tag,k,v,row[k])
 summary={mode:{key:statistics.mean(r[key] for r in report['rows'] if r['mode']==mode) for key in ['medianMs','p95Ms','gpuMeanMs','mainThreadMeanMs']} for mode in ['off','empty','four']}
 summary['emptyVsOffMedianPct']=(summary['empty']['medianMs']/summary['off']['medianMs']-1)*100;summary['fourVsOffMedianPct']=(summary['four']['medianMs']/summary['off']['medianMs']-1)*100;summary['fourVsOffGpuPct']=(summary['four']['gpuMeanMs']/summary['off']['gpuMeanMs']-1)*100 if summary['off']['gpuMeanMs'] else None
 result['renderRuns'][tag]={'summary':summary,'rawReport':report}
for name in ['four-green','four-authored-mix']:
 r=json.loads((P6/'scenarios-02'/('p6-'+name+'.json')).read_text(encoding='utf-8'));assert r['groups']==4 and r['teamIdsUnchanged'] and min(r['groupProgress'])>.2 and len(r['commands'])==13
 moves=[x for x in r['commands'] if x['kind']=='Move'];r['cachedMoveWorkerRangeMs']=[min(x['workerMs'] for x in moves[1:]),max(x['workerMs'] for x in moves[1:])];result['functional'][name]=r
assert result['functional']['four-green']['geometryInvalidated'];result['ranger']=(P6/'scenarios-02/p6-authored-ranger.txt').read_text(encoding='utf-8')
result['scope']='Camera-rendered Editor observations, not standalone-player/OS presentation guarantee. Four-group movement checked within each window, not proof of continuous motion for every member. Tracked memory is whole Editor, not local GPU peak. No equivalence inference from two processes.'
save(dest/'analysis.json',result);print(json.dumps(result,ensure_ascii=False,indent=2))
