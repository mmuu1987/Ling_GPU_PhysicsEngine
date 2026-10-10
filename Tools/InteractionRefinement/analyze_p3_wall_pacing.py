from common_p3 import *
import csv,statistics as st,math,sys
sys.stdout.reconfigure(encoding='utf-8');out=P3/'qualification-wall-pacing-01.json';assert not out.exists();runs=[]
def q(v,p):return sorted(v)[math.ceil(len(v)*p)-1]
for tag in ['qualification-perf24-a','qualification-perf48-a','qualification-perf48-b','qualification-perf24-b']:
 p=P3/tag/'frames.csv'
 with p.open(encoding='utf-8') as f:rows=list(csv.DictReader(f))
 seconds=[float(x['seconds']) for x in rows];delta=[1000*(y-x) for x,y in zip(seconds,seconds[1:])];assert all(d>0 and math.isfinite(d) for d in delta)
 gaps=[int(b['frame'])-int(a['frame']) for a,b in zip(rows,rows[1:])];assert set(gaps)=={1}
 r=json.loads((P3/tag/'performance.json').read_text(encoding='utf-8'));span=seconds[-1]-seconds[0];unitySum=sum(float(x['frameMs']) for x in rows);assert abs(unitySum-span*1000)<250
 runs.append({'tag':tag,'strength':r['separationStrength'],'samples':len(rows),'intervals':len(delta),'elapsed':span,'sumUnityFrameMs':unitySum,'renderedSamplesPerSecond':len(delta)/span,'wallMedianMs':q(delta,.5),'wallP95Ms':q(delta,.95),'wallP99Ms':q(delta,.99),'wallMaxMs':max(delta),'unityFrameP95Ms':r['frameP95Ms'],'maxFrameIdGap':max(gaps),'sourceSha256':sha(p)})
comparison={str(s):{k:st.mean(r[k] for r in runs if r['strength']==s) for k in ['wallMedianMs','wallP95Ms','renderedSamplesPerSecond']} for s in [24,48]};deltas={k:100*(comparison['48'][k]/comparison['24'][k]-1) for k in comparison['24']};flags=[k+' exceeds +15% wall-clock review line' for k in ['wallMedianMs','wallP95Ms'] if deltas[k]>15];noise=[]
for s in [24,48]:
 for k in ['wallMedianMs','wallP95Ms']:
  v=[r[k] for r in runs if r['strength']==s]
  if max(v)>min(v)*1.2:noise.append(str(s)+' '+k+' run-to-run spread >20%')
r={'status':'WALL_CLOCK_INTERVALS_RECOMPUTED','runs':runs,'comparison':comparison,'deltasPct':deltas,'reviewFlags':flags,'noiseFlags':noise,'interpretation':'Differences of realtime timestamps at consecutive Game-camera end-context callbacks. Every adjacent frame ID is consecutive. Not OS display presentation timing. Editor scheduling is included. Mean of each condition\'s two run metrics, not a confidence interval.'};save(out,r);print(json.dumps(r,ensure_ascii=False,indent=2))
