from pathlib import Path
import json,csv,statistics,collections
R=Path(__file__).resolve().parents[2];C=R/'outputs/P9Capacity-20261009-01';V=R/'outputs/P12/Logs/InteractionRefinement-20261007/P8/cold-fix-fixed-capacitybreakdown-03'
r=json.loads((V/'breakdown.json').read_text());proc=json.loads((V/'process.json').read_text());assert len(r['windows'])==8
out={'runPassed':proc['passed'],'failure':proc['cases'][0]['message'],'note':'8 contrast windows completed with assertions; end-of-test raw CPU capture failed. Do not label whole test passed. ProfilerRecorder data and contrast windows are retained as partial evidence. Inclusive markers non-additive.','windows':[]}
units=dict(zip(r['markers'],r['markerUnits']))
for w in r['windows']:
 rows=list(csv.DictReader((V/('breakdown-'+str(w['window']).zfill(2)+'-'+w['phase']+'.csv')).open()))
 med={key:statistics.median([float(x[key]) for x in rows if x[key] and float(x[key])>=0]) for key in rows[0] if any(x[key] and float(x[key])>=0 for x in rows)}
 markerMs={k:v/1e6 for k,v in med.items() if units.get(k)=='TimeNanoseconds' and v>10000}
 out['windows'].append({'phase':w['phase'],'window':w['window'],'frames':len(rows),'timings':{k:v for k,v in med.items() if k.endswith('_ms')},'markerMedianMs':markerMs,'counts':{k:v for k,v in med.items() if units.get(k)=='Count' and v},'lod':w['lods'],'alive':w['alive'],'overflow':w['overflow']})
(C/'breakdown-partial-summary.json').write_text(json.dumps(out,ensure_ascii=False,indent=2),encoding='utf-8')
for w in out['windows']:print(json.dumps({k:v for k,v in w.items() if k!='lod'},ensure_ascii=False))
print('LOD',json.dumps(out['windows'][0]['lod'],ensure_ascii=False))
