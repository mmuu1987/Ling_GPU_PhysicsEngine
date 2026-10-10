from common_p6 import *
import csv,statistics,sys,math
sys.stdout.reconfigure(encoding='utf-8');dest=P6/'performance-followup-01';assert not(dest/'analysis.json').exists()
result={'method':'Recompute raw CSV; window is comparison unit, no frame-as-independent p-value. Two processes are insufficient for equivalence acceptance. Microbenchmark hot loops do not bound indirect driver costs.','runs':{},'micro':{},'pairedBlocks':[]}
for tag in ['perf-before-02','perf-before-03']:
 receipt=json.loads((P6/tag/'process.json').read_text(encoding='utf-8'));assert receipt['passed'];report=json.loads((P6/tag/'p6-balanced-probe.json').read_text(encoding='utf-8'));assert len(report['rows'])==12
 for row in report['rows']:
  assert row['phase']=='Running' and row['alive']==2048 and row['snapshots']==row['acks']==0
  assert row['bytes']==(0 if row['mode']=='off' else 4071424)
  with (P6/tag/f"probe-{row['window']:02d}-{row['mode']}.csv").open() as f:raw=list(csv.DictReader(f))
  values=[float(r['wallMs']) for r in raw];ordered=sorted(values);assert len(raw)==row['frames'] and all(math.isfinite(x) and x>=0 for x in values)
  checks={'medianMs':ordered[len(ordered)//2],'p95Ms':ordered[int((len(ordered)-1)*.95)],'meanMs':statistics.mean(values),'mainThreadMeanMs':statistics.mean(float(r['mainThreadNs']) for r in raw)/1e6,'gcBytesMean':statistics.mean(float(r['gcBytes']) for r in raw)}
  for key,v in checks.items():assert math.isclose(v,row[key],rel_tol=1e-8,abs_tol=1e-8),(tag,key,v,row[key])
 sums={mode:{key:statistics.mean(r[key] for r in report['rows'] if r['mode']==mode) for key in ['medianMs','p95Ms','meanMs','mainThreadMeanMs','gcBytesMean']} for mode in ['off','empty','detached']}
 sums['emptyVsOffPct']=(sums['empty']['medianMs']/sums['off']['medianMs']-1)*100;sums['emptyVsDetachedPct']=(sums['empty']['medianMs']/sums['detached']['medianMs']-1)*100;sums['detachedVsOffPct']=(sums['detached']['medianMs']/sums['off']['medianMs']-1)*100
 result['runs'][tag]=sums;result['micro'][tag]=report['micro']
 for i in range(0,12,3):
  block={r['mode']:r['medianMs'] for r in report['rows'][i:i+3]};assert len(block)==3
  result['pairedBlocks'].append({'run':tag,'block':i//3,'emptyVsOffPct':(block['empty']/block['off']-1)*100,'emptyVsDetachedPct':(block['empty']/block['detached']-1)*100})
result['originalProtocolRepeats']={}
for tag in ['perf-original-repeat-01','perf-original-repeat-02']:
 receipt=json.loads((P6/tag/'process.json').read_text(encoding='utf-8'));assert receipt['passed'];report=json.loads((P6/tag/'p6-short-intervals.json').read_text(encoding='utf-8'));rows=report['rows'];assert len(rows)==4
 off=statistics.mean(r['medianMs'] for r in rows if r['mode']=='disabled');empty=statistics.mean(r['medianMs'] for r in rows if r['mode']=='enabled-empty');result['originalProtocolRepeats'][tag]={'offMedianMs':off,'emptyMedianMs':empty,'deltaPct':(empty/off-1)*100,'rows':rows}
result['productionUnchanged']=[]
before=json.loads((dest/'approved-before.json').read_text(encoding='utf-8'))
for path,r in before.items():
 if path=='Assets/Game/Tests/PlayMode/LocalOrdersSceneTests.cs':continue
 assert sha(ROOT/path)==r['expectedSha256'],path;result['productionUnchanged'].append(path)
save(dest/'analysis.json',result);print(json.dumps(result,ensure_ascii=False,indent=2))
