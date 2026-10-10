from pathlib import Path
import json,csv,math,statistics,sys
sys.stdout.reconfigure(encoding='utf-8')
R=Path(__file__).resolve().parents[2];O=R/'outputs/P9Combined-20261009-01';P=R/'outputs/P11';L=P/'Logs/InteractionRefinement-20261007/P8'
names=['managed-joint-04','managed-repeat-01','optin-joint-01','optin-repeat-01']
runs={n:json.loads((O/(n+'.json')).read_text(encoding='utf-8')) for n in names}
assert all(r['passed'] and r['motherProtection']['passed'] for r in runs.values())
def quantile(a,p):
 a=sorted(a);i=(len(a)-1)*p;lo=int(i);hi=math.ceil(i);return a[lo]+(a[hi]-a[lo])*(i-lo)
def metrics(a):return {'count':len(a),'medianMs':statistics.median(a),'p95Ms':quantile(a,.95),'p99Ms':quantile(a,.99),'maxMs':max(a),'meanMs':statistics.mean(a),'over33ms':sum(x>33 for x in a),'over33Percent':sum(x>33 for x in a)*100/len(a)}
result={'status':'editor_automated_functional_and_ownership_checks_passed_performance_findings_open','mergedBaseline':'080c5e498f0004c8d54fbe745970fb11e36d3597','combinedOverlay':True,'runs':{},'renderedRepeats':{},'naturalBattles':{},'coldFirstUse':{'run':'managed-joint-03','command':'Attack','acceptanceMilliseconds':64719.7563,'classification':'Observed first-use Editor latency; cause not established by these measurements. Preserved failed run, not replaced by warmed passing runs.'},'humanAcceptance':'pending','PlayerAOT':'not tested','newABBA':'not performed','defaultBurstEnabled':False,'newBuild':False,'productionCodeModified':False}
for n,r in runs.items():
 result['runs'][n]={'passed':r['passed'],'durationSeconds':float(r['tests']['duration']),'tests':r['tests'],'backendProof':[s for s in r['stages'] if 'backend-proof' in s or 'navigation-warmup' in s],'motherProtection':r['motherProtection'],'persistentTestResultsRestored':r['persistentTestResultsRestored'],'harnessUnchanged':r['harnessUnchanged'],'productionCodeAndPackagesUnchanged':r['productionCodeAndPackagesUnchanged']}
 if 'externalPreferencesRestored' in r:result['runs'][n]['externalPreferencesRestored']=r['externalPreferencesRestored']
 if '-joint-' in n:result['naturalBattles'][n]=json.loads((L/n/'natural-result.json').read_text(encoding='utf-8-sig'))
 else:
  loops=[]
  for repeat in range(1,4):
   rows=list(csv.DictReader((L/n/f'repeat-{repeat}-frames.csv').open(encoding='utf-8-sig')));assert len(rows)==300
   a=[float(x['wall_ms']) for x in rows]
   loops.append({'repeat':repeat,'frames':metrics(a),'burstSolves':max(int(x['burst_solves']) for x in rows),'managedSolves':max(int(x['managed_solves']) for x in rows),'peakLocalBytes':max(int(x['local_bytes']) for x in rows),'peakSelectionBytes':max(int(x['selection_bytes']) for x in rows)})
  resources=[{k:int(v) for k,v in x.items()} for x in csv.DictReader((L/n/'repeat-resources.csv').open(encoding='utf-8-sig'))];assert len(resources)==3 and all(x['active_native_workspaces']==0 for x in resources)
  delta={k:resources[2][k]-resources[1][k] for k in resources[1] if k!='repeat'}
  flags=[]
  if delta['unity_allocated_after_unload']>max(16*1024**2,resources[1]['unity_allocated_after_unload']*.05):flags.append('allocated_memory_growth')
  for key,limit in [('rendertextures',2),('materials',20),('gameobjects',20)]:
   if delta[key]>limit:flags.append(key+'_growth')
  delays=[{'label':row[0],'command':row[1],'selected':int(row[2]),'acceptedMs':float(row[3]),'framesUntilGpuVerification':int(row[4]),'statusAtVerification':row[5]} for row in csv.reader((L/n/'command-latency.csv').open(encoding='utf-8-sig')) if row]
  bycommand={cmd:metrics([x['acceptedMs'] for x in delays if x['command']==cmd]) for cmd in ['Attack','Hold','Retreat','Move']}
  result['renderedRepeats'][n]={'loops':loops,'resourcesAfterUnload':resources,'resourceDeltaThirdMinusSecond':delta,'resourceGrowthFlags':flags,'commandMeasurements':delays,'commandMetrics':bycommand,'measurementScope':'Rendered Windows Editor D3D11 1280x720; 2048 allocated agents; whole-army navigation warmup followed by Hold + selected-member commands. Fixed sample camera/settings. Not whole-battle Player FPS or ABBA; member selection counts and simulation trajectories are not identical replays.'}
(O/'analysis.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
compact={'status':result['status'],'runs':{k:round(v['durationSeconds'],2) for k,v in result['runs'].items()},'naturalBattles':result['naturalBattles'],'repeatMetrics':{k:{'frames':[x['frames'] for x in v['loops']],'resources':v['resourcesAfterUnload'],'resourceDelta':v['resourceDeltaThirdMinusSecond'],'growthFlags':v['resourceGrowthFlags'],'commands':v['commandMetrics']} for k,v in result['renderedRepeats'].items()}}
(O/'analysis-compact.json').write_text(json.dumps(compact,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps(compact,ensure_ascii=False,indent=2))
