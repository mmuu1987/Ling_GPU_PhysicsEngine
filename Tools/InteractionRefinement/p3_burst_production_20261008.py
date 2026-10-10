"""Source-only synchronous Burst integration after isolated and rendered feasibility. No packaging."""
from p3_density_runtime_20261008 import *
from p3_density_runtime_20261008 import project_check as previous_check
PRIOR=LANE_OUT
LANE_OUT=LOG/'P3'/'burst-production-20261008-01'
ENGINE_ASM='Assets/MassEngine/MassEngine.asmdef'
TEST_ASM='Assets/MassEngine/Tests/EditMode/MassEngine.Tests.asmdef'
OBSERVER='Assets/Game/Editor/InteractionRefinementP3Qualification.cs'
PATHS=[NAV_PATH,LANE_PATH,RUNTIME_PATH,ENGINE_ASM]

def project_check(full=False):
    r=raw_check(full);reg=json.loads((LANE_OUT/'registry.json').read_text(encoding='utf-8'));cardinal=json.loads((DEST/'registry.json').read_text(encoding='utf-8'))
    variant=next((name for name,files in reg['variants'].items() if all(sha(ROOT/p)==h for p,h in files.items())),None)
    valid=variant is not None and all(sha(ROOT/p) in hashes for p,hashes in reg['supportHashes'].items()) and all(sha(ROOT/p)==h for p,h in cardinal['tests'].items())
    if valid:
        r['changed']=[x for x in r['changed'] if x['path'] not in PATHS+[TEST_PATH,TEST_ASM,OBSERVER]]
        r['unexpectedNew']=[p for p in r['unexpectedNew'] if p not in cardinal['tests']]
    r['burstVariant']=variant;r['passed']=valid and not r['changed'] and not r['unexpectedNew'];return r

def write_variant(name):
    assert not processes() and not(ROOT/'Temp/UnityLockfile').exists()
    assert project_check()['passed']
    for p in PATHS:(ROOT/p).write_bytes((LANE_OUT/name/p).read_bytes())
    print('VARIANT',name,flush=True)

def run(kind,tag,extras,expected=None):
    folder=(LOG/'P3' if kind=='performance' else P8)/tag;assert not folder.exists()
    runner=LANE_OUT/('rendered_runner.py' if kind=='performance' else 'test_runner.py')
    env=os.environ.copy();env['PYTHONPATH']=str(ROOT/'Tools/InteractionRefinement')+os.pathsep+env.get('PYTHONPATH','')
    print('RUN',tag,flush=True)
    with (LANE_OUT/(tag+'.log')).open('w',encoding='utf-8') as f:r=subprocess.run([sys.executable,'-X','utf8',str(runner),kind,tag]+extras,cwd=ROOT,env=env,stdout=f,stderr=subprocess.STDOUT)
    assert r.returncode==0,'Runner failed '+tag
    receipt=json.loads((folder/'process.json').read_text(encoding='utf-8'));assert receipt['passed'],tag
    if expected is not None:
        tree=ET.parse(folder/'results.xml').getroot();assert int(tree.get('passed','0'))==expected and int(tree.get('failed','0'))==0 and int(tree.get('skipped','0'))==0
        print('TESTS',tag,expected,flush=True)
    log=(folder/'Editor.log').read_text(encoding='utf-8',errors='replace')
    assert 'A Native Collection has not been disposed' not in log,'Native ownership leak warning'
    return folder

def main():
    sys.stdout.reconfigure(encoding='utf-8');assert not LANE_OUT.exists()
    probe=json.loads((LOG/'P3'/'burst-probe-20261008-01/summary.json').read_text(encoding='utf-8'));render=json.loads((LOG/'P3'/'burst-render-20261008-01/summary.json').read_text(encoding='utf-8'))
    assert probe['localGatePassed'] and probe['protectionPassed'] and render['renderGatePassed'] and render['protectionPassed']
    check=previous_check(full=True);assert check['passed'] and check['laneVariant']=='baseline'
    assert not processes() and not(ROOT/'Temp/UnityLockfile').exists() and user_check()['passed']
    original={p:(ROOT/p).read_bytes() for p in PATHS};support={p:(ROOT/p).read_bytes() for p in [TEST_PATH,TEST_ASM,OBSERVER]}
    payload=json.loads((ROOT/'Tools/InteractionRefinement/p3-burst-production-20261008-payload.json').read_text(encoding='utf-8'))
    candidate=dict(original)
    for p,patches in [(NAV_PATH,[payload['navPatch']]),(RUNTIME_PATH,payload['runtimePatches'])]:
        text=original[p].decode('utf-8').replace('\r\n','\n')
        for a,b in patches:assert text.count(a)==1,(p,a);text=text.replace(a,b,1)
        if p==RUNTIME_PATH:text+='\n'+payload['runtimeAppend']
        candidate[p]=text.encode('utf-8')
    def refs(data):
        d=json.loads(data.decode('utf-8'));assert not d['allowUnsafeCode']
        for ref in ['Unity.Burst','Unity.Collections']:
            if ref not in d['references']:d['references'].append(ref)
        return (json.dumps(d,indent=4)+'\n').encode('utf-8')
    candidate[ENGINE_ASM]=refs(original[ENGINE_ASM]);tests=support[TEST_PATH]+b'\n'+payload['testAppend'].encode('utf-8');testAsm=refs(support[TEST_ASM])
    observer=support[OBSERVER].decode('utf-8').replace('\r\n','\n')
    for a,b in payload['observerPatches']:assert observer.count(a)==1;observer=observer.replace(a,b,1)
    observer=observer.encode('utf-8')
    head=subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip();index=sha(ROOT/'.git/index') if (ROOT/'.git/index').is_file() else None
    packages={p:sha(ROOT/p) for p in ['Packages/manifest.json','Packages/packages-lock.json']}
    LANE_OUT.mkdir()
    for name,files in [('baseline',original),('candidate',candidate),('support-original',support),('support-candidate',{TEST_PATH:tests,TEST_ASM:testAsm,OBSERVER:observer})]:
        for p,data in files.items():q=LANE_OUT/name/p;q.parent.mkdir(parents=True,exist_ok=True);q.write_bytes(data)
    reg={'variants':{name:{p:hashlib.sha256(data).hexdigest() for p,data in files.items()} for name,files in [('baseline',original),('candidate',candidate)]},'supportHashes':{p:[hashlib.sha256(support[p]).hexdigest(),hashlib.sha256(data).hexdigest()] for p,data in [(TEST_PATH,tests),(TEST_ASM,testAsm),(OBSERVER,observer)]},'packageHashes':packages};save(LANE_OUT/'registry.json',reg)
    save(LANE_OUT/'scope.json',{'scope':'Minimal non-test-assembly synchronous Burst source candidate. Runtime lazy ownership, direct immutable graph copy factory, same strict binary algorithm, no background jobs. Existing resolved packages only; engine/test asmdefs add references, unsafe remains false. No package version/build change.', 'fallback':'BurstCompiler.IsEnabled=false uses original managed CreateFlowField without native workspace. If Run reports managed fallback despite enabled flag, return exact result then dispose native workspace and stay managed for that runtime. CLI --burst-disable-compilation validates disabled branch without changing user preferences.', 'tests':'Baseline150; candidate172 including22 direct production workspace/full state/ownership tests. Repeat172 with compilation disabled, then6GPU scoped commands. Same6 fixed inputs complete output+distance+nextCell+heapPosition bits. Probe previously compared pop traces.', 'localGate':'All6 production wrapper medians <=0.70x same-round managed baseline with Burst proof; disabled outputs exact.', 'renderGate':'Own fresh ABBA: BOTH P99<=0.80x; ordinary median<=1.05x; >33.33ms count<=0.5x; >50ms<=max(baseline,3). Candidate Editor audit provesBurst calls>0, managed-native fallback0, created=disposed,active0. No post-hoc threshold changes.', 'memory':'Native payload~4.31MiB per256square runtime plus existing managed grid. Lazy allocation/cold JIT not hidden by claims:35s warmup isolates steady state; cold-start/AOT/player/manual remain pending.', 'boundary':'Only source adoption if all gates pass; no package/build/commit/push/P3 closure. Observer ownership probe always restored. If rejected restore production + test source/asmdef; if adopted retain172 tests and asmdef references.'})
    for name in ['test_runner.py','rendered_runner.py']:
        text=(PRIOR/name).read_text(encoding='utf-8').replace('from p3_density_runtime_20261008 import *','from p3_burst_production_20261008 import *')
        if name=='test_runner.py':
            text=text.replace("filters={'density-runtime'","filters={'burst-production':'MassEngine.Tests.P3BurstIntegrationTests;MassEngine.Tests.P3DensityRuntimeTests;MassEngine.Tests.P3DensityCopyTests;MassEngine.Tests.P3LaneEarlyExitTests;MassEngine.Tests.P3LaneFixedTests;MassEngine.Tests.P3UniformOutputTests;MassEngine.Tests.TerrainNavigationGridTests;MassEngine.Tests.TerrainCardinalRouteCacheTests;MassEngine.Tests.LocalOrderPlanTests','density-runtime'")
            marker="# Known Editor-owned settings";assert text.count(marker)==1;text=text.replace(marker,"if 'burst-disabled' in sys.argv:args+=['--burst-disable-compilation']\n"+marker,1)
        ast.parse(text);(LANE_OUT/name).write_bytes(text.encode('utf-8'))
    state={'status':'running','adopted':False,'P3Closed':False,'steps':[]};save(LANE_OUT/'decision.json',state)
    try:
        (ROOT/TEST_PATH).write_bytes(tests);(ROOT/TEST_ASM).write_bytes(testAsm)
        run('editmode','p3-burst-production-before-01',['density-runtime'],150);state['steps'].append('baseline150_passed')
        write_variant('candidate')
        folder=run('editmode','p3-burst-production-after-01',['burst-production'],172)
        state['local']=[]
        for i in range(6):
            q=json.loads((folder/('burst-production-'+str(i)+'.json')).read_text(encoding='utf-8'));assert q['burstEnabled'] and q['exactBits'] and q['internalStateBits']
            a=statistics.median(r['managedMs'] for r in q['rows']);b=statistics.median(r['burstMs'] for r in q['rows']);assert b<=.7*a
            state['local'].append({'sample':i,'managedMs':a,'burstMs':b,'ratio':b/a})
        state['steps'].append('candidate172_exact_local_gate');save(LANE_OUT/'decision.json',state)
        folder=run('editmode','p3-burst-production-disabled-01',['burst-production','burst-disabled'],172)
        for i in range(6):
            q=json.loads((folder/('burst-production-'+str(i)+'.json')).read_text(encoding='utf-8'));assert not q['burstEnabled'] and q['exactBits'] and q['internalStateBits']
        state['steps'].append('disabled172_exact_managed_runtime_fallback')
        run('playmode','p3-burst-production-scoped-01',['scoped-scene','gui'],6);state['steps'].append('gpu6_passed');save(LANE_OUT/'decision.json',state)
        (ROOT/OBSERVER).write_bytes(observer)
        state['rendered']=[];state['audits']=[]
        for i,name in enumerate(['baseline','candidate','candidate','baseline']):
            write_variant(name);folder=run('performance','p3-burst-production-abba-'+str(i+1),['gui','48'])
            state['rendered'].append(analyze_render(folder));audit=json.loads((folder/'burst-ownership.json').read_text(encoding='utf-8'))
            assert audit['candidateAvailable']==(name=='candidate')
            if name=='candidate':assert audit['created']>0 and audit['created']==audit['disposed'] and audit['active']==0 and audit['burstCalls']>0 and audit['managedNativeCalls']==0
            state['audits'].append(audit);save(LANE_OUT/'decision.json',state)
        a,b,c,e=state['rendered'];assert all(x['conditions']==a['conditions'] for x in state['rendered'])
        state['renderGatePassed']=all(y['p99Ms']<=.8*x['p99Ms'] and y['medianMs']<=1.05*x['medianMs'] and y['over33ms']<=.5*x['over33ms'] and y['over50ms']<=max(x['over50ms'],3) for x,y in [(a,b),(e,c)])
        state['adopted']=state['renderGatePassed'];state['status']='burst_source_adopted_P3_still_open' if state['adopted'] else 'burst_source_rejected_render_gate'
        write_variant('candidate' if state['adopted'] else 'baseline')
    except Exception as e:state.update(status='burst_integration_blocked_or_failed',error=str(e));print('ERROR',str(e),flush=True)
    finally:
        safe=not processes() and not(ROOT/'Temp/UnityLockfile').exists() and all((ROOT/p).read_bytes() in [original[p],candidate[p]] for p in PATHS) and all((ROOT/p).read_bytes() in [support[p],x] for p,x in [(TEST_PATH,tests),(TEST_ASM,testAsm),(OBSERVER,observer)])
        if safe:
            (ROOT/OBSERVER).write_bytes(support[OBSERVER])
            if not state['adopted']:
                for p,data in original.items():(ROOT/p).write_bytes(data)
                for p in [TEST_PATH,TEST_ASM]:(ROOT/p).write_bytes(support[p])
        state['safeRestoration']=safe;state['observerRestored']=(ROOT/OBSERVER).read_bytes()==support[OBSERVER]
        state['finalProject']=project_check(full=True);state['userData']=user_check();state['packagesUnchanged']=all(sha(ROOT/p)==h for p,h in packages.items())
        state['headUnchanged']=subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip()==head
        state['indexUnchanged']=(sha(ROOT/'.git/index') if (ROOT/'.git/index').is_file() else None)==index
        build=ROOT/'Builds/CaptureDefault-20261006-37';manifest=json.loads((BASE/'current37.sha256.json').read_text(encoding='utf-8'))
        changed=[x['path'] for x in manifest if not(build/x['path']).is_file() or sha(build/x['path'])!=x['sha256']]
        extra=sorted({p.relative_to(build).as_posix() for p in build.rglob('*') if p.is_file()}-{x['path'] for x in manifest})
        state['build37']={'files':len(manifest),'changed':changed,'unexpectedNew':extra,'passed':not changed and not extra}
        state['activeProcesses']=processes();state['projectLock']=(ROOT/'Temp/UnityLockfile').exists()
        state['protectionPassed']=safe and state['observerRestored'] and state['finalProject']['passed'] and state['finalProject']['burstVariant']==('candidate' if state['adopted'] else 'baseline') and state['userData']['passed'] and state['packagesUnchanged'] and state['headUnchanged'] and state['indexUnchanged'] and state['build37']['passed'] and not state['activeProcesses'] and not state['projectLock']
        save(LANE_OUT/'decision.json',state);print('FINAL',json.dumps(state,ensure_ascii=False,indent=2),flush=True)
    if 'error' in state or not state['protectionPassed']:sys.exit(1)
if __name__=='__main__':main()
