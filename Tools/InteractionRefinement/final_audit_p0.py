from common_p0 import *
import sys,concurrent.futures
sys.stdout.reconfigure(encoding='utf-8')
assert not processes(),'An Editor/player was reopened; do not audit while files may change'
assert not(ROOT/'Temp/UnityLockfile').exists()
result={'phase':'P0','status':'BLOCKED_BY_EXISTING_TEST_FAILURES','checkedAt':datetime.datetime.now().isoformat(),'project':project_check(full=True)}
base=json.loads((BASE/'user-data.sha256.json').read_text(encoding='utf-8'));root=Path(base['root']);known={r['path']:r['sha256'] for r in base['files']};actual={}
if root.exists():
 for p in root.rglob('*'):
  if p.is_file():
   rel=p.relative_to(root).as_posix()
   if ('warsandbox' in rel.casefold() or 'war-sandbox' in rel.casefold()) and p.suffix.lower() not in {'.log','.dmp'}:actual[rel]=sha(p)
deltas=[p for p in sorted(set(known)|set(actual)) if known.get(p)!=actual.get(p)]
result['userData']={'scope':base['scope'],'filesBefore':len(known),'filesAfter':len(actual),'deltas':deltas,'passed':not deltas}
keep=ROOT/'Builds/CaptureDefault-20261006-37';original=json.loads((BASE/'current37.sha256.json').read_text(encoding='utf-8'));old={r['path']:r['sha256'] for r in original};paths=[p for p in keep.rglob('*') if p.is_file()]
with concurrent.futures.ThreadPoolExecutor(max_workers=3) as pool:current=dict(pool.map(lambda p:(p.relative_to(keep).as_posix(),sha(p)),paths))
changes=[p for p in sorted(set(old)|set(current)) if old.get(p)!=current.get(p)]
result['current37']={'mode':'full-sha256','files':len(current),'deltas':changes,'passed':not changes}
result['ownedTestFiles']=[{'path':p,'sha256':sha(ROOT/p)} for p in sorted(ALLOWED_NEW)]
for label,args in [('git-head',['rev-parse','HEAD']),('git-staged-names',['diff','--cached','--name-status'])]:
 r=subprocess.run(['git','-c','core.quotepath=false']+args,cwd=ROOT,capture_output=True,encoding='utf-8',check=True,timeout=60)
 result[label+'-matches-baseline']=r.stdout==(BASE/(label+'.txt')).read_text(encoding='utf-8')
manifest=json.loads((BASE/'project-files.sha256.json').read_text(encoding='utf-8'))
result['vatFixtureBasenameSearch']={name:[r['path'] for r in manifest if r['path'].rsplit('/',1)[-1].casefold()==name.casefold()] for name in ['MaleCharacterPBR.prefab','Idle_Normal_SwordAndShield.fbx','Die01_SwordAndShield.fbx','Attack01_SwordAndShiled.fbx']}
result['activeProcessesAfter']=processes();result['unityLockExists']=(ROOT/'Temp/UnityLockfile').exists()
result['protectionPassed']=result['project']['passed'] and result['userData']['passed'] and result['current37']['passed'] and result['git-head-matches-baseline'] and result['git-staged-names-matches-baseline']
result['fullSuite']={k:json.loads((P0/'editmode-01/process.json').read_text(encoding='utf-8'))['tests'][k] for k in ['total','passed','failed','skipped','duration']}
result['criticalRepeat']={k:json.loads((P0/'editmode-critical-01/process.json').read_text(encoding='utf-8'))['tests'][k] for k in ['total','passed','failed','skipped','duration']}
result['gpuFunctionalRun']=False;result['performanceMeasured']=False;result['humanMouseTest']=False;result['newBuildProduced']=False;result['featureChanges']=False;result['p1Started']=False
save(P0/'final-audit.json',result);print(json.dumps(result,ensure_ascii=False,indent=2),flush=True)
