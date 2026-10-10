"""One bounded mitigation diagnostic using exact reserved production candidate; reversible project-JIT directory isolation."""
from p3_density_runtime_20261008 import *
from p3_density_runtime_20261008 import project_check as baseline_check
import stat
WORK=LOG/'P3'/'burst-cold-tail-20261008-01'
PROD=LOG/'P3'/'burst-production-20261008-01'
ENGINE_ASM='Assets/MassEngine/MassEngine.asmdef'
TEST_ASM='Assets/MassEngine/Tests/EditMode/MassEngine.Tests.asmdef'
OBSERVER='Assets/Game/Editor/InteractionRefinementP3Qualification.cs'
PATHS=[NAV_PATH,LANE_PATH,RUNTIME_PATH,ENGINE_ASM,OBSERVER,TEST_PATH,TEST_ASM]
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
 prior=LOG/'P3'/'burst-runtime-gate-20261008-01';scene=LOG/'P3'/'burst-runtime-scene-20261008-02'
 a=json.loads((prior/'summary.json').read_text(encoding='utf-8'));b=json.loads((scene/'summary.json').read_text(encoding='utf-8'));assert a['protectionPassed'] and len(a['phases'])==4 and b['protectionPassed'] and len(b['phases'])==1
 assert baseline_check(full=True)['passed'] and user_check()['passed'] and not processes() and not(ROOT/'Temp/UnityLockfile').exists()
 original={p:(ROOT/p).read_bytes() for p in PATHS}
 candidate={p:(prior/'candidate'/p).read_bytes() for p in [NAV_PATH,LANE_PATH,RUNTIME_PATH,ENGINE_ASM]}
 candidate[TEST_PATH]=original[TEST_PATH];candidate[TEST_ASM]=original[TEST_ASM]
 assert json.loads((LOG/'P3'/'burst-optin-policy-20261008-01'/'summary.json').read_text(encoding='utf-8'))['protectionPassed']
 assert len(json.loads((LOG/'P3'/'burst-optin-policy-20261008-01'/'summary.json').read_text(encoding='utf-8'))['phases'])==6
 candidate[RUNTIME_PATH]=(ROOT/'Tools/InteractionRefinement/P3ColdTailRuntime-20261008.cs.txt').read_bytes()
 observer=(LOG/'P3'/'burst-runtime-qualification-20261008-05'/'candidate'/OBSERVER).read_text(encoding='utf-8')
 observer=observer.replace('            if (rows.Count == 0) StartPreparationAudit();','').replace('                EndPreparationAudit();','').replace('                WritePreparationAudit();','')
 needle='        private static bool finishing;';assert observer.count(needle)==1
 observer=observer.replace(needle,needle+(ROOT/'Tools/InteractionRefinement/P3TailObserver-20261008.cs.txt').read_text(encoding='utf-8'))
 needle='private static void Rendered(ScriptableRenderContext context, List<Camera> cameras)';assert observer.count(needle)==1
 a=observer.index('{',observer.index(needle));observer=observer[:a+1]+'\n            RecordTail(cameras);'+observer[a+1:]
 needle='                var controller = WarSandboxSceneSession.Instance != null ? WarSandboxSceneSession.Instance.Controller : null;';assert observer.count(needle)==1
 observer=observer.replace(needle,'                WriteTail();\n'+needle)
 candidate[OBSERVER]=observer.encode('utf-8');baseline=dict(original);baseline[OBSERVER]=candidate[OBSERVER]
 head=subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip();index=sha(ROOT/'.git/index') if(ROOT/'.git/index').is_file() else None
 packages={p:sha(ROOT/p) for p in ['Packages/manifest.json','Packages/packages-lock.json']}
 cache_before=tree(CACHE);assert cache_before['exists'];other_cache={p.name:tree(p) if p.is_dir() else {'sha256':sha(p)} for p in CACHE.parent.iterdir() if p!=CACHE}
 WORK.mkdir();save(WORK/'cache-before.json',cache_before);save(WORK/'other-cache-before.json',other_cache)
 for name,files in [('original',original),('baseline',baseline),('candidate',candidate)]:
  for p,data in files.items():q=WORK/name/p;q.parent.mkdir(parents=True,exist_ok=True);q.write_bytes(data)
 save(WORK/'registry.json',{'variants':{name:{p:hashlib.sha256(data).hexdigest() for p,data in files.items()} for name,files in [('original',original),('baseline',baseline),('candidate',candidate)]}})
 save(WORK/'scope.json',{'scope':'One new project-JIT-empty rendered cold-start/first-command and frame-tail attribution. Uses default-off reserve with explicit process optin. Same algorithm/targets/sync/round-robin. Instrumentation is temporary and not adopted.', 'limits':'Not formal ABBA, no causal claims about the historical three frames. Frame gap is rendered callback wall time, GC counts are correlation only. Tick includes preparation and dynamic solve/lane/upload; do not add Tick to its children. Unmeasured gaps remain unattributed. Cold project JIT does not imply waiting at scene entry. No OS input/manual/AOT claims.', 'restore':'Always restore source and original JIT; retain all diagnostic files. No packaging or preferences.'})
 for name in ['test_runner.py','rendered_runner.py']:
  runner=(LANE_OUT/name).read_text(encoding='utf-8').replace('from p3_density_runtime_20261008 import *',f'from {Path(__file__).stem} import *')
  assert f'from {Path(__file__).stem} import *' in runner
  if name=='rendered_runner.py':
   marker="saved={p:(ROOT/p).read_bytes() for p in auto if(ROOT/p).is_file()}"
   if 'missing_auto=' not in runner:
    assert runner.count(marker)==1;runner=runner.replace(marker,"missing_auto=[p for p in auto if not(ROOT/p).is_file()]\n"+marker)
    marker="  receipt['restoredExternalEditorSettings']=[]";assert runner.count(marker)==1
    runner=runner.replace(marker,"  receipt['restoredOriginallyAbsent']=[]\n  for rel in missing_auto:\n   path=ROOT/rel\n   if path.is_file():\n    q=output/'auto-settings-generated'/rel;q.parent.mkdir(parents=True,exist_ok=True);q.write_bytes(path.read_bytes());path.unlink();receipt['restoredOriginallyAbsent'].append(rel)\n"+marker)
  if name=='rendered_runner.py':
   marker='# Known Editor-owned settings';assert runner.count(marker)==1;runner=runner.replace(marker,"args+=['--war-sandbox-nav-burst']\n"+marker)
  ast.parse(runner);(WORK/name).write_bytes(runner.encode('utf-8'))
 state={'status':'running','adopted':False,'P3Closed':False,'rendered':[],'audits':[],'steps':[]};save(WORK/'summary.json',state)
 moved=False;archive=WORK/'original-JIT-escrow';generated=WORK/'generated-JIT-final'
 try:
  assert not processes() and not(ROOT/'Temp/UnityLockfile').exists() and baseline_check()['passed'] and tree(CACHE)==cache_before
  CACHE.rename(archive);moved=True;CACHE.mkdir()
  save(WORK/'recovery.json',{'originalEscrow':str(archive),'activeJIT':str(CACHE),'generatedArchive':str(generated),'sourceBackup':str(WORK/'original'),'rule':'No project process/lock, only known source bytes; preserve generated cache then restore original. Never overwrite unknown user changes.'})
  for p,data in candidate.items():(ROOT/p).write_bytes(data)
  env=os.environ.copy();env['PYTHONPATH']=str(ROOT/'Tools/InteractionRefinement')+os.pathsep+env.get('PYTHONPATH','')
  for p,data in candidate.items():(ROOT/p).write_bytes(data)
  tag='p3-optin-cold-tail-01';folder=LOG/'P3'/tag;assert not folder.exists();assert not tree(CACHE)['files'];print('RUN project-cold rendered diagnostic',flush=True)
  with (WORK/(tag+'.log')).open('w',encoding='utf-8') as f:r=subprocess.run([sys.executable,'-X','utf8',str(WORK/'rendered_runner.py'),'performance',tag,'gui','48'],cwd=ROOT,env=env,stdout=f,stderr=subprocess.STDOUT)
  assert r.returncode==0,'Diagnostic rendered runner failed'
  assert (folder/'cold-tail.csv').exists();state['diagnosticFolder']=str(folder.relative_to(ROOT));state['status']='cold_tail_diagnostic_complete_not_formal_gate';print('DIAGNOSTIC COMPLETE',flush=True)

 except Exception as e:state.update(status='qualification_failed',error=str(e));print('ERROR',str(e),flush=True)
 finally:
  safe=not processes() and not(ROOT/'Temp/UnityLockfile').exists() and all((ROOT/p).read_bytes() in [original[p],baseline[p],candidate[p]] for p in PATHS)
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
