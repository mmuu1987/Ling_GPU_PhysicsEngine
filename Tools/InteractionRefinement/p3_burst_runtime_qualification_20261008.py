"""One bounded mitigation diagnostic using exact reserved production candidate; reversible project-JIT directory isolation."""
from p3_density_runtime_20261008 import *
from p3_density_runtime_20261008 import project_check as baseline_check
import stat
WORK=LOG/'P3'/'burst-runtime-qualification-20261008-01'
PROD=LOG/'P3'/'burst-production-20261008-01'
ENGINE_ASM='Assets/MassEngine/MassEngine.asmdef'
TEST_ASM='Assets/MassEngine/Tests/EditMode/MassEngine.Tests.asmdef'
OBSERVER='Assets/Game/Editor/InteractionRefinementP3Qualification.cs'
PATHS=[NAV_PATH,LANE_PATH,RUNTIME_PATH,ENGINE_ASM,OBSERVER]
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
 candidate={p:(prior/'candidate'/p).read_bytes() for p in PATHS if p!=OBSERVER}
 observer=original[OBSERVER].decode('utf-8').replace('\r\n','\n')
 needle='        private static bool finishing;';assert observer.count(needle)==1
 observer=observer.replace(needle,needle+(ROOT/'Tools/InteractionRefinement/P3RuntimeGateAudit-20261008.cs.txt').read_text(encoding='utf-8'))
 needle='            FrameTimingManager.CaptureFrameTimings();';assert observer.count(needle)==1
 observer=observer.replace(needle,'            if (rows.Count == 0) StartPreparationAudit();\n'+needle)
 needle='                RestoreOwnedEditorState();\n                ReleaseVariant();';assert observer.count(needle)==1
 observer=observer.replace(needle,'                EndPreparationAudit();\n                RestoreOwnedEditorState();\n                ReleaseVariant();\n                WritePreparationAudit();')
 candidate[OBSERVER]=observer.encode('utf-8');baseline=dict(original);baseline[OBSERVER]=candidate[OBSERVER]
 head=subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip();index=sha(ROOT/'.git/index') if(ROOT/'.git/index').is_file() else None
 packages={p:sha(ROOT/p) for p in ['Packages/manifest.json','Packages/packages-lock.json']}
 cache_before=tree(CACHE);assert cache_before['exists'];other_cache={p.name:tree(p) if p.is_dir() else {'sha256':sha(p)} for p in CACHE.parent.iterdir() if p!=CACHE}
 WORK.mkdir();save(WORK/'cache-before.json',cache_before);save(WORK/'other-cache-before.json',other_cache)
 for name,files in [('original',original),('baseline',baseline),('candidate',candidate)]:
  for p,data in files.items():q=WORK/name/p;q.parent.mkdir(parents=True,exist_ok=True);q.write_bytes(data)
 save(WORK/'registry.json',{'variants':{name:{p:hashlib.sha256(data).hexdigest() for p,data in files.items()} for name,files in [('original',original),('baseline',baseline),('candidate',candidate)]}})
 save(WORK/'scope.json',{'scope':'Rendered GPU6 followed by one predeclared ABBA comparing adopted managed baseline vs real Runtime async-preparation candidate. No automatic adoption, always restore. Original formal failure retained separately. Engine candidate matches validated runtime/scene snapshots.', 'sequence':['candidate scoped-scene GUI6','A1 baseline','B1 candidate','B2 candidate','A2 baseline'], 'gate':'Both pairs B1/A1 and B2/A2 independently: P99<=0.80x, median<=1.05x, over33.33 count<=0.50x, over50 count<=max(A,3). Fixed35s warmup15s window; never wait extra for Ready or select windows. Same conditions required.', 'proof':'Boundary-only observer: same runtime Ready at measurement start/end, native REAL solves increased, original managed solves unchanged, real managed-native0, probe count unchanged. Null-probe managed calls before measurement explicitly allowed, not mistaken for full solver fallback. Native lifetimes balanced after release.', 'protection':'Only5 source/asmdef/observer paths temporarily varied; original test sources unchanged. Original JIT escrow and generated JIT archived, original hashes/tree restored; other cache/user/package/37/HEAD/index protected. GUI layouts/TimeManager and owned profiler flags restored by existing runner. No user preferences/packages/build/commit changes.', 'limits':'Editor-only formal comparison; no Player/AOT/manual closure. Passing new candidate would not erase historical failed candidate evidence or auto-enable fallback.'})
 for name in ['test_runner.py','rendered_runner.py']:
  runner=(LANE_OUT/name).read_text(encoding='utf-8').replace('from p3_density_runtime_20261008 import *','from p3_burst_runtime_qualification_20261008 import *')
  if name=='rendered_runner.py':
   marker="saved={p:(ROOT/p).read_bytes() for p in auto if(ROOT/p).is_file()}"
   if 'missing_auto=' not in runner:
    assert runner.count(marker)==1;runner=runner.replace(marker,"missing_auto=[p for p in auto if not(ROOT/p).is_file()]\n"+marker)
    marker="  receipt['restoredExternalEditorSettings']=[]";assert runner.count(marker)==1
    runner=runner.replace(marker,"  receipt['restoredOriginallyAbsent']=[]\n  for rel in missing_auto:\n   path=ROOT/rel\n   if path.is_file():\n    q=output/'auto-settings-generated'/rel;q.parent.mkdir(parents=True,exist_ok=True);q.write_bytes(path.read_bytes());path.unlink();receipt['restoredOriginallyAbsent'].append(rel)\n"+marker)
  ast.parse(runner);(WORK/name).write_bytes(runner.encode('utf-8'))
 state={'status':'running','adopted':False,'P3Closed':False,'rendered':[],'audits':[],'steps':[]};save(WORK/'summary.json',state)
 moved=False;archive=WORK/'original-JIT-escrow';generated=WORK/'generated-JIT-final'
 try:
  assert not processes() and not(ROOT/'Temp/UnityLockfile').exists() and baseline_check()['passed'] and tree(CACHE)==cache_before
  CACHE.rename(archive);moved=True;CACHE.mkdir()
  save(WORK/'recovery.json',{'originalEscrow':str(archive),'activeJIT':str(CACHE),'generatedArchive':str(generated),'sourceBackup':str(WORK/'original'),'rule':'No project process/lock, only known source bytes; preserve generated cache then restore original. Never overwrite unknown user changes.'})
  for p,data in candidate.items():(ROOT/p).write_bytes(data)
  env=os.environ.copy();env['PYTHONPATH']=str(ROOT/'Tools/InteractionRefinement')+os.pathsep+env.get('PYTHONPATH','')
  tag='p3-runtime-qualified-gpu-01';folder=P8/tag;assert not folder.exists();print('RUN GPU6',flush=True)
  with (WORK/(tag+'.log')).open('w',encoding='utf-8') as f:r=subprocess.run([sys.executable,'-X','utf8',str(WORK/'test_runner.py'),'playmode',tag,'scoped-scene','gui'],cwd=ROOT,env=env,stdout=f,stderr=subprocess.STDOUT)
  assert r.returncode==0,'GPU6 runner failed';receipt=json.loads((folder/'process.json').read_text(encoding='utf-8'));assert receipt['passed'];tests=ET.parse(folder/'results.xml').getroot();assert int(tests.get('passed','0'))==6 and int(tests.get('failed','0'))==0 and int(tests.get('skipped','0'))==0
  state['steps'].append('rendered_gpu6_passed');save(WORK/'summary.json',state);print('PASSED GPU6',flush=True)
  for i,(label,files) in enumerate([('A1',baseline),('B1',candidate),('B2',candidate),('A2',baseline)]):
   assert not processes() and not(ROOT/'Temp/UnityLockfile').exists() and project_check()['passed']
   for p,data in files.items():(ROOT/p).write_bytes(data)
   tag='p3-runtime-qualified-abba-'+str(i+1);folder=LOG/'P3'/tag;assert not folder.exists();print('RUN',label,tag,flush=True)
   with (WORK/(tag+'.log')).open('w',encoding='utf-8') as f:r=subprocess.run([sys.executable,'-X','utf8',str(WORK/'rendered_runner.py'),'performance',tag,'gui','48'],cwd=ROOT,env=env,stdout=f,stderr=subprocess.STDOUT)
   assert r.returncode==0,'Rendered runner failed '+tag
   q=analyze_render(folder);audit=json.loads((folder/'preparation-audit.json').read_text(encoding='utf-8'));assert audit['candidateAvailable']==label.startswith('B')
   if label.startswith('B'):assert audit['sameRuntime'] and audit['startState']==audit['endState']=='Ready' and audit['endNative']>audit['startNative'] and audit['startManaged']==audit['endManaged'] and audit['endManagedNative']==0 and audit['startProbes']==audit['endProbes'] and audit['created']==audit['disposed'] and audit['active']==0
   log=(folder/'Editor.log').read_text(encoding='utf-8',errors='replace');assert 'A Native Collection has not been disposed' not in log
   state['rendered'].append(q);state['audits'].append(audit);save(WORK/'summary.json',state);print('WINDOW',label,json.dumps(q),flush=True)
  a,b,c,d=state['rendered'];assert all(x['conditions']==a['conditions'] for x in state['rendered'])
  state['pairs']=[]
  for label,x,y in [('B1/A1',a,b),('B2/A2',d,c)]:
   gates={'p99':y['p99Ms']<=.8*x['p99Ms'],'median':y['medianMs']<=1.05*x['medianMs'],'over33':y['over33ms']<=.5*x['over33ms'],'over50':y['over50ms']<=max(x['over50ms'],3)}
   state['pairs'].append({'pair':label,'gates':gates,'passed':all(gates.values()),'p99Ratio':y['p99Ms']/x['p99Ms'],'medianRatio':y['medianMs']/x['medianMs']})
  state['renderGatePassed']=all(x['passed'] for x in state['pairs']);state['status']='new_candidate_gate_passed_reserved_not_enabled' if state['renderGatePassed'] else 'new_candidate_gate_failed_reserved_not_enabled'
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
