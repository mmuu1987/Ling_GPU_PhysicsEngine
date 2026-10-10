from common_p2 import *
import sys,re,concurrent.futures,xml.etree.ElementTree as ET
sys.stdout.reconfigure(encoding='utf-8')
tag=sys.argv[1];assert re.fullmatch('[a-z0-9-]+',tag);destination=P2/(tag+'.json');assert not destination.exists()
assert not processes() and not(ROOT/'Temp/UnityLockfile').exists(),'Audit waits for owned/user Unity; never force close'
r={'phase':'P2','checkedAt':datetime.datetime.now().isoformat(),'project':project_check(full=True),'newBuildProduced':False,'p3Started':False,'humanMouseTest':False}
b=json.loads((BASE/'user-data.sha256.json').read_text(encoding='utf-8'));root=Path(b['root']);old={x['path']:x['sha256'] for x in b['files']};now={}
if root.exists():
 for p in root.rglob('*'):
  if p.is_file():
   rel=p.relative_to(root).as_posix()
   if ('warsandbox' in rel.casefold() or 'war-sandbox' in rel.casefold()) and p.suffix.lower() not in {'.log','.dmp'}:now[rel]=sha(p)
diff=[p for p in sorted(set(old)|set(now)) if old.get(p)!=now.get(p)];r['userData']={'before':len(old),'after':len(now),'deltas':diff,'passed':not diff}
keep=ROOT/'Builds/CaptureDefault-20261006-37';old={x['path']:x['sha256'] for x in json.loads((BASE/'current37.sha256.json').read_text(encoding='utf-8'))};files=[p for p in keep.rglob('*') if p.is_file()]
with concurrent.futures.ThreadPoolExecutor(max_workers=3) as pool:now=dict(pool.map(lambda p:(p.relative_to(keep).as_posix(),sha(p)),files))
diff=[p for p in sorted(set(old)|set(now)) if old.get(p)!=now.get(p)];r['current37']={'files':len(now),'deltas':diff,'passed':not diff,'mode':'fresh-full-sha256'}
r['git']={}
for label,args in [('git-head',['rev-parse','HEAD']),('git-staged-names',['diff','--cached','--name-status'])]:
 data=subprocess.check_output(['git','-c','core.quotepath=false']+args,cwd=ROOT,encoding='utf-8',timeout=60)
 r['git'][label+'-unchanged']=data==(BASE/(label+'.txt')).read_text(encoding='utf-8')
guids={};duplicates=[]
for p in (ROOT/'Assets').rglob('*.meta'):
 m=re.search(r'^guid:\s*([a-f0-9]{32})',p.read_text(encoding='utf-8',errors='replace'),re.M)
 if m:
  if m[1] in guids:duplicates.append([guids[m[1]],p.relative_to(ROOT).as_posix()])
  guids[m[1]]=p.relative_to(ROOT).as_posix()
r['duplicateGuids']=duplicates;r['changes']=json.loads((P2/'approved-changes.json').read_text(encoding='utf-8'))
intent=json.loads((P2/'intent.json').read_text(encoding='utf-8'));allowed=set(intent['modified']+intent['new']+[p+'.meta' for p in intent['new']])
r['battleAndAssetInvariant']=set(r['changes'])<=allowed and not any(p.endswith(('.asset','.unity')) for p in r['changes']) and r['project']['passed']
r['invariantMeaning']='Source edits limited to explicit P2 intent; no asset/scene rewrite. Runtime radius changes are intentional; no claim of unchanged battle radius.'
r['assetCountChecked']=sum(p.endswith('.asset') for p in json.loads((P2/'baseline.json').read_text(encoding='utf-8'))['files'])
r['runs']={}
for p in sorted(P2.glob('*/process.json')):
 d=json.loads(p.read_text(encoding='utf-8'));entry={k:d[k] for k in ['passed','exitCode','compileErrors','tests','failedTests','restoredAutoFiles','isolationMarkerSeen'] if k in d}
 log=(p.parent/'Editor.log').read_text(encoding='utf-8',errors='replace')
 entry['noAudioListenerWarnings']=sum('There are no audio listeners in the scene' in l for l in log.splitlines());entry['compilerWarnings']=sorted(set(re.findall(r'^.*warning CS\d+.*$',log,re.M)))
 entry['worldRuler']=[l.strip() for l in log.splitlines() if l.startswith('P1_WORLD_RULER ')]
 entry['uiPixels']=[l.strip() for l in log.splitlines() if l.startswith('P1_UI_PIXELS ')]
 entry['realSubjects']=[l.strip() for l in log.splitlines() if l.startswith('P1_REAL_SUBJECT ')]
 entry['gpuChain']=[l.strip() for l in log.splitlines() if l.startswith('P2_GPU_CHAIN ')]
 entry['uiChain']=[l.strip() for l in log.splitlines() if l.startswith('P2_UI_CHAIN ')]
 r['runs'][p.parent.name]=entry
r['screenshots']=[{'path':p.relative_to(ROOT).as_posix(),'bytes':p.stat().st_size,'sha256':sha(p)} for p in sorted(P2.rglob('*.png'))]
r['activeProcessesAfter']=processes();r['unityLockExists']=(ROOT/'Temp/UnityLockfile').exists();r['finishedAt']=datetime.datetime.now().isoformat()
r['protectionPassed']=r['project']['passed'] and r['userData']['passed'] and r['current37']['passed'] and all(r['git'].values()) and not duplicates and r['battleAndAssetInvariant']
r['qualifiedRuns']=['editmode-03','preview-03']
r['automatedRunsPassed']=all(r['runs'].get(n,{}).get('passed',False) for n in r['qualifiedRuns'])
r['pixelEvidenceQualified']=len(r['runs'].get('preview-03',{}).get('worldRuler',[]))==12 and len(r['runs'].get('preview-03',{}).get('uiPixels',[]))==4 and len(r['runs'].get('preview-03',{}).get('realSubjects',[]))>=2
r['valueChainQualified']=len(r['runs'].get('preview-03',{}).get('gpuChain',[]))==11 and len(r['runs'].get('preview-03',{}).get('uiChain',[]))==1
r['rejectedEvidence']={'preview-01':'23/24. GPU fixture accidentally called controller.PauseBattle, changing Setup to Paused; production boundary guard correctly rejected on-load application. Corrected fixture to pause manager only, matching actual scene-session path. No production guard was loosened.'}
r['status']='P2_AUTOMATED_PASS_PENDING_MANUAL_ACCEPTANCE' if r['protectionPassed'] and r['automatedRunsPassed'] and r['pixelEvidenceQualified'] and r['valueChainQualified'] else 'BLOCKED'
save(destination,r);print(json.dumps({k:v for k,v in r.items() if k not in ['changes','screenshots','runs']},ensure_ascii=False,indent=2),flush=True)

