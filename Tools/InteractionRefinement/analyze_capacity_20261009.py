from pathlib import Path
import json,csv,statistics,math,re
R=Path(__file__).resolve().parents[2];C=R/'outputs/P9Capacity-20261009-01';base=R/'outputs/P12/Logs/InteractionRefinement-20261007/P8'
def stats(a):
 a=sorted(a)
 return {'n':len(a),'median':statistics.median(a),'p95':a[math.ceil(len(a)*.95)-1],'mean':statistics.mean(a),'max':max(a)} if a else None
report={'scope':'r03 unchanged; Editor D3D11 GUI 1280x720, standing global Hold and local Hold; 3s warmup + 5s samples per window, forward/reverse four states. NOT battle, Player FPS, independent cold-cache or long-run qualification. Original effective density 0.4/m2; original two unit types, proportions, combat/render settings. Fixed camera at (0,400,-260), looking at origin. VSync off, targetFrameRate=-1, real time. Source/byte budgets not total VRAM. Command timers include production snapshot/planning/upload/observation; synchronous test ID census excluded. First command per rebuilt channel, existing shader cache; not navigation-cache warmed.', 'tiers':{}}
for n in [2048,50000]:
 p=base/('cold-fix-fixed-capacity'+str(n)+'-01');proc=json.loads((p/'process.json').read_text(encoding='utf-8'));assert proc['passed'],proc
 windows=[]
 for w in csv.DictReader((p/'windows.csv').open()):
  samples=list(csv.DictReader((p/('window-'+w['window'].zfill(2)+'-'+w['mode']+'.csv')).open()))
  w.update({'wallMs':stats([float(x['wall_ms']) for x in samples]),'mainMs':stats([float(x['main_ns'])/1e6 for x in samples if float(x['main_ns'])>0]),'gpuMs':stats([float(x['gpu_ms']) for x in samples if x['gpu_ms']]),'allocatedStart':samples[0]['allocated_bytes'],'allocatedEnd':samples[-1]['allocated_bytes']});windows.append(w)
 modes={}
 for m in ['off','empty','selected4','selected1000']:
  ws=[w for w in windows if w['mode']==m];modes[m]={'wallMedianAverageMs':statistics.mean(w['wallMs']['median'] for w in ws),'gpuMedianAverageMs':statistics.mean(w['gpuMs']['median'] for w in ws if w['gpuMs']),'mainMedianAverageMs':statistics.mean(w['mainMs']['median'] for w in ws if w['mainMs']),'wallWindowMedians':[w['wallMs']['median'] for w in ws],'gpuWindowMedians':[w['gpuMs']['median'] for w in ws if w['gpuMs']]}
 commands=list(csv.DictReader((p/'commands.csv').open()))
 report['tiers'][str(n)]={'processPassed':proc['passed'],'protection':proc['motherProtection'],'windows':windows,'modeSummary':modes,'commands':commands,'shaderCompilerSamples': 'See process-cpu.jsonl; no cold cache purges performed','rawEvidence':str(p)}
(C/'standing-comparison.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps({n:{'modes':t['modeSummary'],'commands':t['commands'],'visible':[w['visible_instances_end'] for w in t['windows']],'overflow':[w['overflow_end'] for w in t['windows']]} for n,t in report['tiers'].items()},ensure_ascii=False,indent=2))
