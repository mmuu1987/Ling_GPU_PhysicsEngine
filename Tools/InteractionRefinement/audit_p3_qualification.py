from common_p3 import *
import sys,re,concurrent.futures
sys.stdout.reconfigure(encoding='utf-8');dest=P3/'qualification-audit-01.json';assert not dest.exists();assert not processes() and not(ROOT/'Temp/UnityLockfile').exists()
a={'phase':'P3','checkedAt':datetime.datetime.now().isoformat(),'project':project_check(full=True),'humanAcceptance':False,'userAcceptedBalanceTradeoff':True,'configurationApplied':True,'p3StageAccepted':False,'newBuildProduced':False}
b=json.loads((BASE/'user-data.sha256.json').read_text(encoding='utf-8'));root=Path(b['root']);old={x['path']:x['sha256'] for x in b['files']};now={}
if root.exists():
 for p in root.rglob('*'):
  if p.is_file():
   rel=p.relative_to(root).as_posix()
   if ('warsandbox' in rel.casefold() or 'war-sandbox' in rel.casefold()) and p.suffix.lower() not in {'.log','.dmp'}:now[rel]=sha(p)
d=[p for p in sorted(set(old)|set(now)) if old.get(p)!=now.get(p)];a['userData']={'before':len(old),'after':len(now),'deltas':d,'passed':not d}
keep=ROOT/'Builds/CaptureDefault-20261006-37';old={x['path']:x['sha256'] for x in json.loads((BASE/'current37.sha256.json').read_text(encoding='utf-8'))};paths=[p for p in keep.rglob('*') if p.is_file()]
with concurrent.futures.ThreadPoolExecutor(max_workers=3) as pool:now=dict(pool.map(lambda p:(p.relative_to(keep).as_posix(),sha(p)),paths))
d=[p for p in sorted(set(old)|set(now)) if old.get(p)!=now.get(p)];a['current37']={'files':len(now),'deltas':d,'passed':not d,'mode':'fresh-full-sha256'}
a['git']={}
for label,args in [('git-head',['rev-parse','HEAD']),('git-staged-names',['diff','--cached','--name-status'])]:
 value=subprocess.check_output(['git','-c','core.quotepath=false']+args,cwd=ROOT,encoding='utf-8',timeout=60);a['git'][label+'-unchanged']=value==(BASE/(label+'.txt')).read_text(encoding='utf-8')
a['changes']=json.loads((P3/'approved-changes.json').read_text(encoding='utf-8'));base=json.loads((P3/'baseline.json').read_text(encoding='utf-8'))['files'];a['existingSourceAndAssetsUnchanged']=not(set(a['changes'])&set(base)) and a['project']['passed']
guids={};duplicates=[]
for p in (ROOT/'Assets').rglob('*.meta'):
 m=re.search(r'^guid:\s*([a-f0-9]{32})',p.read_text(encoding='utf-8',errors='replace'),re.M)
 if m:
  if m[1] in guids:duplicates.append([guids[m[1]],p.relative_to(ROOT).as_posix()])
  guids[m[1]]=p.relative_to(ROOT).as_posix()
expectedAssets={'Assets/Game/Content/Characters/UnifiedRoster/Version03/Library/male_Flocking.asset','Assets/Game/Content/Characters/UnifiedRoster/Version03/Library/knight_Flocking.asset'}
existingChanges=set(a['changes'])&set(base);a['onlyTwoApprovedExistingAssetsChanged']=existingChanges==expectedAssets
scalarChecks={}
for rel in expectedAssets:
 old=(P3/'changes/06-user-adopted-separation/before'/rel).read_bytes();now=(ROOT/rel).read_bytes();scalarChecks[rel]=old.count(b'separationStrength: 24')==1 and old.replace(b'separationStrength: 24',b'separationStrength: 48')==now and __import__('hashlib').sha256(old).hexdigest()==base[rel]
a['exactScalarChanges']=scalarChecks;a['existingProductionCodeAndScenesUnchanged']=a['onlyTwoApprovedExistingAssetsChanged'] and a['project']['passed']
a['duplicateGuids']=duplicates;a['runs']={} 
for tag in ['qualification-kernels-01','qualification-motion-01','qualification-move-01','qualification-hold-01','qualification-retreat-01','qualification-perf24-a','qualification-perf48-a','qualification-perf48-b','qualification-perf24-b','qualification-editmode-01']:
 d=json.loads((P3/tag/'process.json').read_text(encoding='utf-8'));entry={k:d[k] for k in ['passed','exitCode','compileErrors','tests','failedTests','restoredAutoFiles','isolationMarkerSeen'] if k in d};log=(P3/tag/'Editor.log').read_text(encoding='utf-8',errors='replace');entry['noAudioListenerWarnings']=log.count('There are no audio listeners in the scene');a['runs'][tag]=entry
 if tag=='qualification-motion-01':
  e=json.loads((P3/tag/'crowding-evidence/evidence.json').read_text(encoding='utf-8'));entry['captureComplete']=e['captureComplete'];entry['samples']=len(e['samples']);entry['instrumentedNotPerformance']=not e['performanceQualified']
a['screenshots']=[{'path':p.relative_to(ROOT).as_posix(),'bytes':p.stat().st_size,'sha256':sha(p)} for p in sorted(P3.rglob('*.png'))]
a['activeProcessesAfter']=processes();a['unityLockExists']=(ROOT/'Temp/UnityLockfile').exists();a['finishedAt']=datetime.datetime.now().isoformat()
a['protectionPassed']=a['project']['passed'] and a['userData']['passed'] and a['current37']['passed'] and all(a['git'].values()) and not duplicates and a['onlyTwoApprovedExistingAssetsChanged'] and all(a['exactScalarChanges'].values()) and a['existingProductionCodeAndScenesUnchanged'] and not a['activeProcessesAfter'] and not a['unityLockExists']
a['captureAndRegressionPassed']=all(r['passed'] for r in a['runs'].values()) and all(a['runs'][tag]['captureComplete'] for tag in ['qualification-motion-01'])
a['status']='P3_QUALIFICATION_RUNS_AND_PROTECTION_PASSED_PENDING_REVIEW' if a['protectionPassed'] and a['captureAndRegressionPassed'] else 'BLOCKED'
save(dest,a);print(json.dumps({k:v for k,v in a.items() if k not in ['changes','screenshots','runs']},ensure_ascii=False,indent=2));sys.exit(0 if a['status']!='BLOCKED' else 1)

