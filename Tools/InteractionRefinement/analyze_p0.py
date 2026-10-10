from common_p0 import *
import collections,re,sys,math
sys.stdout.reconfigure(encoding='utf-8')
units=json.loads((BASE/'serialized-unit-radius-inventory.json').read_text(encoding='utf-8'));scenes=json.loads((BASE/'formal-scene-links.json').read_text(encoding='utf-8'))
formal=set(p for s in scenes for p in s['serializedUnitAssets'])
radii=[]
for u in units:
 try:r=float(u.get('serializedFlocking',{}).get('agentRadius'));radii.append((r,u))
 except (TypeError,ValueError):pass
shared=collections.defaultdict(list)
for u in units:
 if u.get('flocking'):shared[u['flocking']].append(u['asset'])
summary={'sourceOnly':True,'runtimeOverridesNotVerifiedByThisInventory':True,'unitAssetCount':len(units),'explicitRadiusCount':len(radii),'uniqueFlockingConfigs':len(shared),'radiusHistogram':dict(sorted(collections.Counter(str(r) for r,u in radii).items())),'formalSerializedTemplates':[u for u in units if u['asset'] in formal],'smallestRadiusExamples':[u for r,u in sorted(radii,key=lambda x:x[0])[:4]],'largestRadiusExamples':[u for r,u in sorted(radii,key=lambda x:x[0],reverse=True)[:4]],'sharedFlockingConfigs':[{'path':p,'users':a} for p,a in shared.items() if len(a)>1], 'sourceFacts':{'defaultSpawnScale':'DefaultSpawnModule.cs assigns Vector3.one','previewScale':'UnitModelPreviewRenderer.cs assigns Vector3.one','previewShadow':'Derived from visual bounds; not agentRadius','scalingDifference':'Contact separation uses horizontal scale, separate minimum-distance branch reads configured radii; no causal crowding conclusion','formalSceneCount':len(scenes)}}
save(P0/'radius-source-analysis.json',summary)
print(json.dumps({k:v for k,v in summary.items() if k not in ['sharedFlockingConfigs','smallestRadiusExamples','largestRadiusExamples']},ensure_ascii=False,indent=2),flush=True)
