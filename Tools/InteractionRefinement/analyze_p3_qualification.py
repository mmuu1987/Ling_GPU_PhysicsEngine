from common_p3 import *
import csv,gzip,struct,math,statistics as st,sys
sys.stdout.reconfigure(encoding='utf-8')
out=P3/'qualification-analysis-01.json';assert not out.exists()
tags=['qualification-kernels-01','qualification-motion-01','qualification-move-01','qualification-hold-01','qualification-retreat-01','qualification-perf24-a','qualification-perf48-a','qualification-perf48-b','qualification-perf24-b','qualification-editmode-01']
receipts={};files=[]
def load(p):
 files.append({'path':p.relative_to(ROOT).as_posix(),'bytes':p.stat().st_size,'sha256':sha(p)});return json.loads(p.read_text(encoding='utf-8'))
for tag in tags:
 d=load(P3/tag/'process.json');assert d['passed'] and d['finishedAt'];receipts[tag]={k:d[k] for k in ['passed','tests','performance','observationSummary'] if k in d}

def quantile(values,q):
 a=sorted(values);return a[max(0,min(len(a)-1,math.ceil(len(a)*q)-1))]
kernels=[load(p) for p in sorted((P3/'qualification-kernels-01').glob('corridor-*.json'))]+[load(p) for p in sorted((P3/'qualification-kernels-01').glob('melee-*.json'))];assert len(kernels)==6 and all(x['passed'] for x in kernels)
motions=[];folder=P3/'qualification-motion-01/crowding-evidence'
for p in sorted(folder.glob('*-hold-motion-r*.json')):
 r=load(p);assert r['passed'] and r['frames']==1200 and len(r['trackedIds'])==64 and len(set(r['trackedIds']))==64
 cp=p.with_suffix('.csv');files.append({'path':cp.relative_to(ROOT).as_posix(),'bytes':cp.stat().st_size,'sha256':sha(cp)})
 with cp.open(encoding='utf-8') as f:rows=list(csv.DictReader(f))
 assert len(rows)==1200 and [int(x['frame']) for x in rows]==list(range(1200))
 mean=st.mean(float(x['meanSpeed']) for x in rows);maxstep=max(float(x['maxStep']) for x in rows);rev=sum(int(x['reverseEvents']) for x in rows);moving=sum(int(x['movingPresentation']) for x in rows)
 assert math.isclose(mean,r['meanSpeed'],rel_tol=2e-4,abs_tol=1e-7) and math.isclose(maxstep,r['maxStep'],rel_tol=1e-5,abs_tol=1e-7) and rev==r['reverseEvents'] and moving==r['movingPresentationAgentFrames']
 bp=p.with_suffix('.bin.gz');files.append({'path':bp.relative_to(ROOT).as_posix(),'bytes':bp.stat().st_size,'sha256':sha(bp)})
 raw=gzip.decompress(bp.read_bytes());record=4+64*20;assert len(raw)==1200*record;prev=None;trackedmax=0;trackedReversals=0;speeds=[]
 for frame in range(1200):
  offset=frame*record;assert struct.unpack_from('<i',raw,offset)[0]==frame
  values=[struct.unpack_from('<ffffi',raw,offset+4+i*20) for i in range(64)]
  for i,(x,z,vx,vz,state) in enumerate(values):
   assert all(math.isfinite(n) for n in [x,z,vx,vz]);assert 0<=state<=4;speeds.append(math.hypot(vx,vz))
   if prev:
    px,pz,pvx,pvz,_=prev[i];step=math.hypot(x-px,z-pz);trackedmax=max(trackedmax,step)
    if math.hypot(vx,vz)>.05 and math.hypot(pvx,pvz)>.05 and vx*pvx+vz*pvz<-.5*math.hypot(vx,vz)*math.hypot(pvx,pvz):trackedReversals+=1
  prev=values
 assert trackedmax<=maxstep+1e-5
 motions.append({**r,'csvMeanMatches':True,'frameP95SpeedMedian':quantile([float(x['p95Speed']) for x in rows],.5),'trackedIndependentMaxStep':trackedmax,'trackedIndependentReversals':trackedReversals,'trackedSpeedP95':quantile(speeds,.95),'trackedSamples':len(speeds),'reverseEventsPerAgentSecond':rev/(1024*20),'movingPresentationPct':moving/(1024*1200)*100})
assert len(motions)==4
motionComparison={}
for variant in ['reference-24','official-48']:
 a=[r for r in motions if r['variant']==variant];assert len(a)==2
 motionComparison[variant]={k:st.mean(r[k] for r in a) for k in ['meanSpeed','maxStep','reverseEventsPerAgentSecond','movingPresentationPct','trackedSpeedP95']}
m24=motionComparison['reference-24'];m48=motionComparison['official-48'];motionFlags=[]
if m48['maxStep']>m24['maxStep']*1.25+.005:motionFlags.append('Mean run maximum step increased beyond relative25% +5mm review margin')
if m48['reverseEventsPerAgentSecond']>m24['reverseEventsPerAgentSecond']*1.25+.1:motionFlags.append('Direction-reversal proxy increased materially')
performance=[]
for tag in tags:
 if '-perf' not in tag:continue
 r=load(P3/tag/'performance.json');assert r['passed'] and r['renderedEditor'] and r['cameraFixed'] and not r['synchronousAgentReadback'] and r['units']==2048 and r['width']==1920 and r['height']==1080
 p=P3/tag/'frames.csv';files.append({'path':p.relative_to(ROOT).as_posix(),'bytes':p.stat().st_size,'sha256':sha(p)})
 with p.open(encoding='utf-8') as f:rows=list(csv.DictReader(f))
 assert len(rows)==r['frames'] and len(rows)>=100 and r['renderedGameSamples']==len(rows)
 assert len({int(x['frame']) for x in rows})==len(rows)
 frames=[float(x['frameMs']) for x in rows];gpu=[float(x['gpuTimingMs']) for x in rows if float(x['gpuTimingMs'])>0]
 assert all(math.isfinite(x) and x>0 for x in frames)
 assert len(gpu)==r['gpuSamples'];r['positiveGpuCoveragePct']=100*len(gpu)/len(rows)
 assert math.isclose(quantile(frames,.5),r['frameMedianMs'],rel_tol=.001) and math.isclose(quantile(frames,.95),r['frameP95Ms'],rel_tol=.001)
 r['tag']=tag;r['framesIndependentlyChecked']=True;r['firstSampleSeconds']=float(rows[0]['seconds']);r['lastSampleSeconds']=float(rows[-1]['seconds']);assert 35<=r['firstSampleSeconds']<36 and 50<=r['lastSampleSeconds']<52
 if gpu:assert math.isclose(quantile(gpu,.5),r['gpuMedianMs'],rel_tol=.001)
 performance.append(r)
assert [r['separationStrength'] for r in performance]==[24,48,48,24]
perfComparison={};gpuAvailable=all(r['gpuTimingAvailable'] and r['gpuSamples']>0 for r in performance)
for strength in [24,48]:
 a=[r for r in performance if r['separationStrength']==strength];assert len(a)==2
 perfComparison[str(strength)]={k:st.mean(r[k] for r in a) for k in ['frameMedianMs','frameP95Ms','mainThreadMedianMs','mainThreadP95Ms','gcBytesPerSample']}
 if gpuAvailable:perfComparison[str(strength)].update({k:st.mean(r[k] for r in a) for k in ['gpuMedianMs','gpuP95Ms']})
flags=[];deltas={}
for k in ['frameMedianMs','frameP95Ms']+(['gpuMedianMs','gpuP95Ms'] if gpuAvailable else []):
 a=perfComparison['24'][k];b=perfComparison['48'][k];deltas[k]=100*(b/a-1)
 if b>a*1.15:flags.append(k+' exceeded +15% review line')
noise=[]
for strength in [24,48]:
 for metric in ['frameMedianMs','frameP95Ms']:
  values=[r[metric] for r in performance if r['separationStrength']==strength]
  if max(values)>min(values)*1.2:noise.append(str(strength)+' '+metric+' run-to-run spread >20%')
commands={}
for kind in ['move','hold','retreat']:
 r=load(P3/('qualification-'+kind+'-01')/'observations/receipt.json');assert r['passed'] and r['units']==2048;commands[kind]=r
result={'status':'QUALIFICATION_OBSERVATIONS_COMPLETE','p3StageAccepted':False,'sourceFiles':files,'runs':receipts,'kernels':kernels,'motionWindows':motions,'motionComparison':motionComparison,'motionReviewFlags':motionFlags,'performanceRuns':performance,'performanceComparison':perfComparison,'performanceDeltasPct':deltas,'gpuTimingComparisonAvailable':gpuAvailable,'performanceReviewFlags':flags,'runNoiseFlags':noise,'commands':commands,'interpretation':'Instrumented hold-motion windows are separate from real-clock rendered performance. Hold-motion proxy is not a human visual judgment. Tiny corridor fixture does not prove all terrain bottlenecks or large-army throughput. Two performance repetitions per setting do not establish statistical significance.'}
result['needsReview']=bool(flags or motionFlags or noise or not gpuAvailable)
save(out,result);print(json.dumps({k:v for k,v in result.items() if k in ['status','needsReview','motionComparison','motionReviewFlags','performanceComparison','performanceDeltasPct','gpuTimingComparisonAvailable','performanceReviewFlags','runNoiseFlags']},ensure_ascii=False,indent=2))
