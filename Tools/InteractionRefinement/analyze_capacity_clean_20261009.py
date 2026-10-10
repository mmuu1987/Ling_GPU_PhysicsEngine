from pathlib import Path
import csv,json,statistics,math
R=Path(__file__).resolve().parents[2];C=R/'outputs/P9Capacity-20261009-01';B=R/'outputs/P12/Logs/InteractionRefinement-20261007/P8'
def stats(a):
 a=sorted(a);return {'n':len(a),'median':statistics.median(a),'p95':a[math.ceil(.95*len(a))-1],'max':max(a)} if a else None
report={'scope':'Single GameView standing Hold matrix. Original runtime r03 and resource assets unchanged. Same frozen measurement harness plus immutable camera/GPU audit. 3s warm +5s per window, forward/reverse; Editor diagnostic, NOT Player or battle acceptance. GPU invalid samples excluded.','excludedFromPrimary':'capacityclean2048-01 functionally passed but overlapped attempted historical asset search; use -02 with no parallel document scans.','tiers':{}}
for count,run in [(2048,'02'),(50000,'01')]:
 p=B/('cold-fix-fixed-capacityclean'+str(count)+'-'+run);proc=json.loads((p/'process.json').read_text());assert proc['passed'];audit=json.loads((p/'clean-audit.json').read_text());assert audit['unexpectedCallbacks']==0 and len(audit['cameras'])==1
 cameras={int(x['frame']):x for x in csv.DictReader((p/'camera-gpu-audit.csv').open())};windows=[]
 for w in csv.DictReader((p/'windows.csv').open()):
  raw=list(csv.DictReader((p/('window-'+w['window'].zfill(2)+'-'+w['mode']+'.csv')).open()));gpu=[float(x['gpu_ms']) for x in raw if x['gpu_ms'] and float(x['gpu_ms'])>0];matches=[cameras[int(x['frame'])] for x in raw if int(x['frame']) in cameras]
  windows.append({'summary':w,'wallMs':stats([float(x['wall_ms']) for x in raw]),'mainInclusiveMs':stats([float(x['main_ns'])/1e6 for x in raw if float(x['main_ns'])>0]),'gpuMs':stats(gpu),'gpuCoverage':len(gpu)/len(raw),'cameraMatchedFrames':len(matches),'gpuRecorderRaw':{k:{'positive':stats([float(x[k]) for x in matches if x.get(k) and float(x[k])>0]),'zeros':sum(float(x.get(k) or 0)==0 for x in matches)} for k in ['FrameTime.GPU','GPU Frame Time'] if k in audit['counterNames']}})
 modes={}
 for mode in ['off','empty','selected4','selected1000']:
  ws=[w for w in windows if w['summary']['mode']==mode];modes[mode]={'wallMs':statistics.mean(w['wallMs']['median'] for w in ws),'gpuMs':statistics.mean(w['gpuMs']['median'] for w in ws) if all(w['gpuMs'] for w in ws) else None,'wallWindows':[w['wallMs']['median'] for w in ws],'gpuCoverage':[w['gpuCoverage'] for w in ws]}
 report['tiers'][str(count)]={'run':proc['name'],'passed':proc['passed'],'protection':proc['motherProtection'],'audit':audit,'windows':windows,'modes':modes,'commands':list(csv.DictReader((p/'commands.csv').open())),'evidence':str(p)}
(C/'clean-matrix-summary.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps({n:{'modes':t['modes'],'counters':[{'mode':w['summary']['mode'],'raw':w['gpuRecorderRaw']} for w in t['windows']]} for n,t in report['tiers'].items()},ensure_ascii=False,indent=2))
