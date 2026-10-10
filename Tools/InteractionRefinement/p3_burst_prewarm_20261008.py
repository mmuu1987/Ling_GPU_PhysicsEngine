"""One bounded mitigation diagnostic using exact reserved production candidate; reversible project-JIT directory isolation."""
from p3_density_runtime_20261008 import *
from p3_density_runtime_20261008 import project_check as baseline_check
import stat
WORK=LOG/'P3'/'burst-prewarm-20261008-01'
PROD=LOG/'P3'/'burst-production-20261008-01'
ENGINE_ASM='Assets/MassEngine/MassEngine.asmdef'
TEST_ASM='Assets/MassEngine/Tests/EditMode/MassEngine.Tests.asmdef'
PATHS=[NAV_PATH,LANE_PATH,RUNTIME_PATH,ENGINE_ASM,TEST_PATH,TEST_ASM]
CACHE=ROOT/'Library/BurstCache/JIT'

def project_check(full=False):
 r=raw_check(full);reg=json.loads((WORK/'registry.json').read_text(encoding='utf-8'));card=json.loads((DEST/'registry.json').read_text(encoding='utf-8'))
 variant=next((name for name,files in reg['variants'].items() if all(sha(ROOT/p)==h for p,h in files.items())),None)
 valid=variant is not None and all(sha(ROOT/p)==h for p,h in card['tests'].items())
 if valid:
  r['changed']=[x for x in r['changed'] if x['path'] not in PATHS]
  r['unexpectedNew']=[p for p in r['unexpectedNew'] if p not in card['tests']]
 r['startupVariant']=variant;r['passed']=valid and not r['changed'] and not r['unexpectedNew'];return r

def tree(path):
 if not path.exists():return {'exists':False,'dirs':[],'files':{}}
 dirs=[];files={};total=0
 def walk(folder):
  nonlocal total
  for p in sorted(folder.iterdir()):
   st=p.lstat();assert not p.is_symlink() and not(getattr(st,'st_file_attributes',0)&0x400),'Reparse point not allowed '+str(p)
   rel=p.relative_to(path).as_posix()
   if p.is_dir():dirs.append(rel);walk(p)
   else:
    total+=st.st_size;assert total<2*1024**3 and len(files)<20000,'Cache inventory exceeds bounded scope'
    files[rel]={'bytes':st.st_size,'sha256':sha(p)}
 walk(path);return {'exists':True,'dirs':dirs,'files':files}

def main():
 sys.stdout.reconfigure(encoding='utf-8');assert not WORK.exists()
 prior=json.loads((PROD/'decision.json').read_text(encoding='utf-8'));assert prior['protectionPassed'] and not prior['adopted']
 assert baseline_check(full=True)['passed'] and user_check()['passed'] and not processes() and not(ROOT/'Temp/UnityLockfile').exists()
 original={p:(ROOT/p).read_bytes() for p in PATHS}
 for p in [NAV_PATH,LANE_PATH,RUNTIME_PATH,ENGINE_ASM]:assert original[p]==(PROD/'baseline'/p).read_bytes()
 for p in [TEST_PATH,TEST_ASM]:assert original[p]==(PROD/'support-original'/p).read_bytes()
 candidate={p:(PROD/'candidate'/p).read_bytes() for p in [NAV_PATH,LANE_PATH,RUNTIME_PATH,ENGINE_ASM]}
 candidate[TEST_ASM]=(PROD/'support-candidate'/TEST_ASM).read_bytes()
 candidate[TEST_PATH]=original[TEST_PATH]+b'\n'+(ROOT/'Tools/InteractionRefinement/P3BurstPrewarmTests-20261008.cs.txt').read_bytes()
 head=subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip();index=sha(ROOT/'.git/index') if(ROOT/'.git/index').is_file() else None
 packages={p:sha(ROOT/p) for p in ['Packages/manifest.json','Packages/packages-lock.json']}
 cache_before=tree(CACHE);assert cache_before['exists']
 other_cache={p.name:tree(p) if p.is_dir() else {'sha256':sha(p)} for p in CACHE.parent.iterdir() if p!=CACHE}
 WORK.mkdir();save(WORK/'cache-before.json',cache_before);save(WORK/'other-cache-before.json',other_cache)
 for name,files in [('original',original),('candidate',candidate)]:
  for p,data in files.items():q=WORK/name/p;q.parent.mkdir(parents=True,exist_ok=True);q.write_bytes(data)
 save(WORK/'registry.json',{'variants':{name:{p:hashlib.sha256(data).hexdigest() for p,data in files.items()} for name,files in [('original',original),('candidate',candidate)]}})
 save(WORK/'scope.json',{'scope':'Reserved exact production Burst candidate temporarily installed only for explicit null-goal warmup / first real solve / recreated-workspace diagnostic. One filtered NUnit test in one fresh process; no performance adoption/requalification, no rendered queue, no packaging.', 'sequence':['empty project JIT at launch; explicit null-goal warmup before first real solve'], 'cacheProtection':'Hash original JIT tree then rename whole directory into evidence escrow. Create empty JIT. Do NOT delete original or generated cache. After the owned process exits, archive generated JIT and rename original back, verify exact directory/file hash tree. Other BurstCache siblings must remain identical. Refuse reparse points, unknown source edits, running project Editor or lock. On interruption preserve directories for recovery; never merge/delete unknown cache.', 'timing':'Test-entry and before/after first Run cache file metadata snapshots. Managed first, reflection factory invocation, explicit null-goal compiled warmup (must return all zero), first real solve,12reuse,Dispose,recreated factory/Run. Warmup moves cost to setup; it cannot remove it. No loading-screen integration performed. Timers include wrapper allocation/copy. Prior test/app initialization may compile before measured Run; empty project JIT at launch does not prove whole-machine cold state or first-Run compile miss.', 'limits':'No thresholds changed. Cold compilation attribution requires evidence, not a fresh process alone. OS disk cache/service/other caches not flushed. Factory uses reflection/hydrated captured grid, not a live scene or player. Exact first/result bits and2created2disposed0active15Burst0managed assertions required. Disabled branch already172passed historically, not retested here.', 'preserved':'Restore all6 source/asmdef paths and original150tests, package files,user data,37,HEAD/index; Burst stays reserved, not adopted.'})
 runner=(LANE_OUT/'test_runner.py').read_text(encoding='utf-8').replace('from p3_density_runtime_20261008 import *','from p3_burst_prewarm_20261008 import *')
 needle="filters={'density-runtime'";assert runner.count(needle)==1
 runner=runner.replace(needle,"filters={'burst-prewarm':'MassEngine.Tests.P3BurstPrewarmTests','density-runtime'")
 ast.parse(runner);(WORK/'test_runner.py').write_bytes(runner.encode('utf-8'))
 state={'status':'running','adopted':False,'P3Closed':False,'phases':[]};save(WORK/'summary.json',state)
 moved=False;archive=WORK/'original-JIT-escrow';generated=WORK/'generated-JIT'
 try:
  assert not processes() and not(ROOT/'Temp/UnityLockfile').exists() and baseline_check()['passed']
  assert tree(CACHE)==cache_before
  CACHE.rename(archive);moved=True;CACHE.mkdir()
  save(WORK/'recovery.json',{'originalEscrow':str(archive),'activeJIT':str(CACHE),'generatedArchive':str(generated),'sourceBackup':str(WORK/'original'),'rule':'Only restore when project has no Unity process/lock. Preserve generated JIT by rename, then rename original escrow back. Never delete or overwrite unknown files.'})
  for p,data in candidate.items():(ROOT/p).write_bytes(data)
  env=os.environ.copy();env['PYTHONPATH']=str(ROOT/'Tools/InteractionRefinement')+os.pathsep+env.get('PYTHONPATH','')
  for label,tag in [('empty-project-JIT-prewarm-null','p3-burst-prewarm-cold-01')]:
   assert not processes() and not(ROOT/'Temp/UnityLockfile').exists() and project_check()['passed']
   before=tree(CACHE);save(WORK/(tag+'-cache-before.json'),before)
   if label=='empty-project-JIT-prewarm-null':assert not before['files'] and not before['dirs']
   folder=P8/tag;assert not folder.exists();print('RUN',label,tag,flush=True)
   with (WORK/(tag+'.log')).open('w',encoding='utf-8') as f:r=subprocess.run([sys.executable,'-X','utf8',str(WORK/'test_runner.py'),'editmode',tag,'burst-prewarm'],cwd=ROOT,env=env,stdout=f,stderr=subprocess.STDOUT)
   assert r.returncode==0,'Startup test runner failed '+tag
   receipt=json.loads((folder/'process.json').read_text(encoding='utf-8'));assert receipt['passed']
   tests=ET.parse(folder/'results.xml').getroot();assert int(tests.get('passed','0'))==1 and int(tests.get('failed','0'))==0 and int(tests.get('skipped','0'))==0
   q=json.loads((folder/'burst-startup.json').read_text(encoding='utf-8'));assert q['exactBits'] and q['firstUsedBurst'] and q['secondUsedBurst'] and q['active']==0 and q['created']==q['disposedCount']==2 and q['burstCalls']==15 and q['prewarmed'] and q['managedCalls']==0
   log=(folder/'Editor.log').read_text(encoding='utf-8',errors='replace');assert 'A Native Collection has not been disposed' not in log
   after=tree(CACHE);save(WORK/(tag+'-cache-after.json'),after)
   started=datetime.datetime.fromisoformat(receipt['startedAt']).astimezone(datetime.timezone.utc);entered=datetime.datetime.fromisoformat(q['utcStart'].replace('Z','+00:00'))
   q['launchToTestEntrySeconds']=(entered-started).total_seconds();q['reuseMedianMs']=statistics.median(q['reuseMs'])
   state['phases'].append({'label':label,'tag':tag,'cacheFilesBeforeLaunch':len(before['files']),'cacheFilesAfterExit':len(after['files']),'result':q});save(WORK/'summary.json',state)
  state['status']='prewarm_diagnostic_complete_not_adopted'
 except Exception as e:state.update(status='startup_diagnostic_failed',error=str(e));print('ERROR',str(e),flush=True)
 finally:
  safe=not processes() and not(ROOT/'Temp/UnityLockfile').exists() and all((ROOT/p).read_bytes() in [original[p],candidate[p]] for p in PATHS)
  state['safeRestoration']=safe
  if safe:
   for p,data in original.items():(ROOT/p).write_bytes(data)
   if moved:
    try:
     assert tree(archive)==cache_before,'Escrow cache changed; do not overwrite'
     assert not generated.exists()
     if CACHE.exists():CACHE.rename(generated)
     archive.rename(CACHE)
    except Exception as e:state['cacheRestoreError']=str(e)
  state['sourceRestored']=all((ROOT/p).read_bytes()==data for p,data in original.items())
  state['cacheRestored']=tree(CACHE)==cache_before
  state['otherCacheUnchanged']={p.name:tree(p) if p.is_dir() else {'sha256':sha(p)} for p in CACHE.parent.iterdir() if p!=CACHE}==other_cache
  state['finalProject']=baseline_check(full=True);state['userData']=user_check();state['packagesUnchanged']=all(sha(ROOT/p)==h for p,h in packages.items())
  state['headUnchanged']=subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip()==head
  state['indexUnchanged']=(sha(ROOT/'.git/index') if(ROOT/'.git/index').is_file() else None)==index
  build=ROOT/'Builds/CaptureDefault-20261006-37';manifest=json.loads((BASE/'current37.sha256.json').read_text(encoding='utf-8'))
  changed=[x['path'] for x in manifest if not(build/x['path']).is_file() or sha(build/x['path'])!=x['sha256']]
  extra=sorted({p.relative_to(build).as_posix() for p in build.rglob('*') if p.is_file()}-{x['path'] for x in manifest})
  state['build37']={'files':len(manifest),'changed':changed,'unexpectedNew':extra,'passed':not changed and not extra}
  state['activeProcesses']=processes();state['projectLock']=(ROOT/'Temp/UnityLockfile').exists()
  state['protectionPassed']=safe and state['sourceRestored'] and state['cacheRestored'] and state['otherCacheUnchanged'] and state['finalProject']['passed'] and state['userData']['passed'] and state['packagesUnchanged'] and state['headUnchanged'] and state['indexUnchanged'] and state['build37']['passed'] and not state['activeProcesses'] and not state['projectLock']
  save(WORK/'summary.json',state);print('FINAL',json.dumps(state,ensure_ascii=False,indent=2),flush=True)
 if 'error' in state or not state['protectionPassed']:sys.exit(1)
if __name__=='__main__':main()
