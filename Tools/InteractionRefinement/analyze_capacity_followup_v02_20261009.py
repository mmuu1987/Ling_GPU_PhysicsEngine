from pathlib import Path
import csv,json,statistics,collections
R=Path(__file__).resolve().parents[2];C=R/'outputs/P9Capacity-20261009-01';base=R/'outputs/P12/Logs/InteractionRefinement-20261007/P8';V=base/'cold-fix-fixed-capacityviewport-02';W=base/'cold-fix-fixed-capacitywarm-01'
proc=json.loads((V/'process.json').read_text());warmproc=json.loads((W/'process.json').read_text());assert proc['passed'] and warmproc['passed'],(proc.get('cases'),warmproc.get('cases'))
out={'viewportProcess':proc,'warmProcess':warmproc,'viewport':json.loads((V/'viewport.json').read_text()),'viewportTiming':[],'warmCommands':list(csv.DictReader((W/'warm-commands.csv').open()))}
for w in out['viewport']['windows']:
 rows=list(csv.DictReader((V/(w['phase']+'.csv')).open()));out['viewportTiming'].append({'phase':w['phase'],'frames':len(rows),'medianMs':{k:statistics.median([float(x[k]) for x in rows if x[k]]) for k in rows[0] if k.endswith('_ms')},'wallP95Ms':sorted(float(x['wall_ms']) for x in rows)[int((len(rows)-1)*.95)]})
profile=list(csv.DictReader((V/'cpu-profile-inclusive.csv').open()));groups=collections.defaultdict(list)
for p in profile:groups[(p['thread'],p['name'])].append(float(p['inclusive_ms']))
out['cpuInclusiveSummary']=[{'thread':t,'name':n,'framesPresent':len(v),'medianMsWhenPresent':statistics.median(v),'meanMsWhenPresent':statistics.mean(v)} for (t,n),v in groups.items()]
out['cpuInclusiveSummary'].sort(key=lambda x:x['medianMsWhenPresent'],reverse=True)
out['measurementCorrections']={'gpuZeroInvalid':'GPU frame values of 0 after SceneView closure are unavailable, not zero GPU cost. Positive GPU timing with two views is not established as total GPU time across both views.', 'cameraRowAliasing':'The viewport.json camera row frame counts from earlier windows were incremented during the next warmup because the harness retained mutable camera rows. Use immutable process stages for per-window counts; camera identities and LOD counts remain valid.', 'cpuCoverage':'First 32 raw thread indices only; same-named worker threads merged by exported name. Do not attribute their totals to main thread; use Main Thread records only for stated CPU conclusions.'}
for item in out['viewportTiming']:
 rows=list(csv.DictReader((V/(item['phase']+'.csv')).open()));vals=[float(x['gpu_ms']) for x in rows if x['gpu_ms'] and float(x['gpu_ms'])>0];item['gpuValidSamples']=len(vals);item['medianMs']['gpu_ms']=statistics.median(vals) if vals else None
out['immutableCameraStageRecords']=proc['stages']
out['cpuScope']='Separate binary capture after SceneView closure. Inclusive per-thread samples >=0.1ms, top60 per thread/frame; non-additive. Missing names explicitly retained; presence-conditioned medians are not whole-frame averages. Not used as unprofiled performance baseline.'
(C/'followup-summary.json').write_text(json.dumps(out,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps({'viewportTiming':out['viewportTiming'],'cameras':out['viewport'],'cpuTop':out['cpuInclusiveSummary'][:35],'warmCommands':out['warmCommands']},ensure_ascii=False,indent=2))
