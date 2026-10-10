"""Lane fixed-input diagnosis and bounded same-output candidate, preserving the adopted binary/output baseline."""
from p3_lane_early_20261008 import *
from p3_lane_early_20261008 import project_check as adopted_check
PRIOR_LANE_OUT=LANE_OUT
from common_p8 import project_check as raw_check
import ast

LANE_OUT=LOG/'P3'/'density-runtime-20261008-01'
LANE_PATH='Assets/MassEngine/Terrain/TerrainLaneApproach33.cs'
RUNTIME_PATH='Assets/MassEngine/Terrain/TerrainNavigationRuntime.cs'


def project_check(full=False):
    r=raw_check(full)
    reg=json.loads((LANE_OUT/'registry.json').read_text(encoding='utf-8'))
    cardinal=json.loads((DEST/'registry.json').read_text(encoding='utf-8'))
    variant=next((name for name,files in reg['variants'].items() if all(sha(ROOT/p)==h for p,h in files.items())),None)
    valid=variant is not None and sha(ROOT/TEST_PATH)==reg['testSha256'] and all(sha(ROOT/p)==h for p,h in cardinal['tests'].items())
    if valid:
        r['changed']=[x for x in r['changed'] if x['path'] not in [NAV_PATH,LANE_PATH,RUNTIME_PATH,TEST_PATH]]
        r['unexpectedNew']=[p for p in r['unexpectedNew'] if p not in cardinal['tests']]
    r['laneVariant']=variant;r['passed']=valid and not r['changed'] and not r['unexpectedNew']
    return r


def select_lane(name):
    assert name in ['baseline','candidate']
    assert not processes() and not(ROOT/'Temp/UnityLockfile').exists()
    check=project_check();assert check['passed'],check
    reg=json.loads((LANE_OUT/'registry.json').read_text(encoding='utf-8'))
    for p in [NAV_PATH,LANE_PATH,RUNTIME_PATH]:
        data=(LANE_OUT/name/p).read_bytes();assert hashlib.sha256(data).hexdigest()==reg['variants'][name][p]
        (ROOT/p).write_bytes(data)
    print('VARIANT',name,flush=True)


def run_lane(kind,tag,extra):
    folder=(LOG/'P3' if kind=='performance' else P8)/tag;assert not folder.exists()
    runner=LANE_OUT/('rendered_runner.py' if kind=='performance' else 'test_runner.py')
    env=os.environ.copy();env['PYTHONPATH']=str(ROOT/'Tools/InteractionRefinement')+os.pathsep+env.get('PYTHONPATH','')
    print('RUN',tag,flush=True)
    with (LANE_OUT/(tag+'.log')).open('w',encoding='utf-8') as f:
        r=subprocess.run([sys.executable,'-X','utf8',str(runner),kind,tag]+extra,cwd=ROOT,env=env,stdout=f,stderr=subprocess.STDOUT)
    assert r.returncode==0,'Runner failed '+tag
    receipt=json.loads((folder/'process.json').read_text(encoding='utf-8'));assert receipt['passed'],tag
    if kind!='performance':
        root=ET.parse(folder/'results.xml').getroot();assert int(root.get('failed','0'))==0 and int(root.get('skipped','0'))==0
        print('TESTS',tag,root.get('passed'),flush=True)
    return folder


def lane_analysis(folder):
    results=[]
    for i in range(6):
        report=json.loads((folder/('lane-fixed-'+str(i)+'.json')).read_text(encoding='utf-8'))
        assert report['exactBits'] and report['centerBitsExact'] and report['goalsUnchanged'] and report['changedCells']>0
        rows=report['rows'];assert len(rows)==48
        med={mode:statistics.median(x['milliseconds'] for x in rows if x['mode']==mode) for mode in ['reference','current-grid-reference-lane','current','profiled-reference']}
        profile=[x for x in rows if x['mode']=='profiled-reference']
        phases={k:statistics.median(x[k] for x in profile) for k in ['buildMs','sortMs','scanMs']}
        results.append({'sample':i,'changedCells':report['changedCells'],'modeMedianMs':med,'originalPhasesMs':phases,'currentRatio':med['current']/med['reference'],'sameGridReferenceRatio':med['current-grid-reference-lane']/med['reference'],'profileRatio':med['profiled-reference']/med['reference'],'finalLaneOutputSha256':sha(folder/('lane-final-'+str(i)+'.bin'))})
    return results


def main():
    sys.stdout.reconfigure(encoding='utf-8')
    assert not LANE_OUT.exists()
    print('PREFLIGHT preserve adopted binary/output source',flush=True)
    initialCheck=adopted_check(full=True);assert initialCheck['passed'] and initialCheck['laneVariant']=='candidate',initialCheck
    assert not processes() and not(ROOT/'Temp/UnityLockfile').exists() and user_check()['passed']
    fixed=json.loads((LOG/'P3'/'fixed-input-20261008-01/summary.json').read_text(encoding='utf-8'))
    capture=LOG/'P3'/'fixed-input-20261008-01-capture'
    assert all(sha(capture/p)==h for p,h in fixed['artifactHashes'].items())
    original={p:(ROOT/p).read_bytes() for p in [NAV_PATH,LANE_PATH,RUNTIME_PATH]};testOriginal=(ROOT/TEST_PATH).read_bytes()
    payload=json.loads((ROOT/'Tools/InteractionRefinement/p3-density-runtime-20261008-payload.json').read_text(encoding='utf-8'))
    text=original[RUNTIME_PATH].decode('utf-8').replace('\r\n','\n')
    for a,b in payload['runtimePatches']:
        assert text.count(a)==1,a;text=text.replace(a,b,1)
    candidate={**original,RUNTIME_PATH:text.encode('utf-8')}
    testBytes=testOriginal+b'\n'+payload['testAppend'].encode('utf-8')
    LANE_OUT.mkdir();(LANE_OUT/'test-before.cs.txt').write_bytes(testOriginal);(LANE_OUT/'test-reviewed.cs.txt').write_bytes(testBytes)
    for name,files in [('baseline',original),('candidate',candidate)]:
        for p,b in files.items():q=LANE_OUT/name/p;q.parent.mkdir(parents=True,exist_ok=True);q.write_bytes(b)
    reg={'variants':{name:{p:hashlib.sha256(b).hexdigest() for p,b in files.items()} for name,files in [('baseline',original),('candidate',candidate)]},'testSha256':hashlib.sha256(testBytes).hexdigest(),'testBeforeSha256':hashlib.sha256(testOriginal).hexdigest()};save(LANE_OUT/'registry.json',reg)
    initial={'project':initialCheck,'gitHead':subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip(),'index':sha(ROOT/'.git/index') if (ROOT/'.git/index').is_file() else None};save(LANE_OUT/'initial.json',initial)
    scope={'baseline':'Adopted binary/output navigation AND Lane exact early exits. Only Runtime density reading changes.',
      'changes':'Lazily allocate one uint[CellCount] per runtime after a completed valid request; CopyTo same frame; same index-ordered filter; clear reference on Dispose. No scheduling/wait changes.',
      'tests':'Before/after150 targeted EditMode incl15 density local cases and10 runtime lifecycle cases against frozen runtime; real request completion waits ONLY IN TESTS. Both-team deferral, disabled/battle-stopped/dynamic-off, explicit command invalidation, cancellation, Dispose/no-op, repeat cycles, next Editor update deferred targets. Existing125 retained. Six GPU command regressions before rendered.',
      'localGate':'All6 density copy comparisons >=20% faster; full ordered target bits and runtime direction/state comparisons exact.',
      'renderGate':'Predeclared for ~2ms local opportunity: BOTH ABBA pairs P99 improve >=1ms AND >=3%; ordinary median<=1.05x; >33.33ms counts no higher; >50ms<=max(baseline,3). This is a new small candidate, not post-hoc relaxation of a completed run.',
      'memory':'One lazily reused256KiB payload+header at256x256 per runtime, not per team/frame. No zero allocation claim.',
      'preserve':'125 prior tests plus25 retained cases, source baseline if rejected,37,user data,HEAD,index; no package or P3 closure.'};save(LANE_OUT/'scope.json',scope)

    for name in ['test_runner.py','rendered_runner.py']:
        text=(PRIOR_LANE_OUT/name).read_text(encoding='utf-8').replace('from p3_lane_early_20261008 import *','from p3_density_runtime_20261008 import *')
        if name=='test_runner.py':text=text.replace("filters={'lane-early'", "filters={'density-runtime':'MassEngine.Tests.P3DensityRuntimeTests;MassEngine.Tests.P3DensityCopyTests;MassEngine.Tests.P3LaneEarlyExitTests;MassEngine.Tests.P3LaneFixedTests;MassEngine.Tests.P3UniformOutputTests;MassEngine.Tests.TerrainNavigationGridTests;MassEngine.Tests.TerrainCardinalRouteCacheTests;MassEngine.Tests.LocalOrderPlanTests','lane-early'")
        ast.parse(text);(LANE_OUT/name).write_bytes(text.encode('utf-8'))
    state={'status':'running','adopted':False,'P3Closed':False,'steps':[]};save(LANE_OUT/'decision.json',state)
    try:
        assert not processes() and not(ROOT/'Temp/UnityLockfile').exists()
        assert (ROOT/TEST_PATH).read_bytes()==testOriginal
        (ROOT/TEST_PATH).write_bytes(testBytes)
        before=run_lane('editmode','p3-density-runtime-before-01',['density-runtime']);state['before']=lane_analysis(before)
        root=ET.parse(before/'results.xml').getroot();cases=[x for x in root.iter('test-case') if 'P3LaneFixedTests.' in x.get('fullname','')]
        assert len(cases)==14 and all(x.get('result')=='Passed' for x in cases)
        assert int(root.get('passed','0'))==150
        state['steps'].append('baseline_lane_diagnosed');save(LANE_OUT/'decision.json',state)
        select_lane('candidate')
        after=run_lane('editmode','p3-density-runtime-after-01',['density-runtime']);state['after']=lane_analysis(after)
        state['densityLocal']=[]
        for i in range(6):
            q=json.loads((after/('density-copy-'+str(i)+'.json')).read_text(encoding='utf-8'))
            m={k:statistics.median(x['milliseconds'] for x in q['rows'] if x['mode']==k) for k in ['native-index','bulk-copy-scan']}
            assert q['exactBits'] and q['sourceUnchanged'] and m['bulk-copy-scan']<=.8*m['native-index']
            state['densityLocal'].append({'sample':i,'medians':m})
        assert all(a['finalLaneOutputSha256']==b['finalLaneOutputSha256'] for a,b in zip(state['before'],state['after']))
        assert int(ET.parse(after/'results.xml').getroot().get('passed','0'))==150
        state['steps'].append('candidate_exact_and_local_gain');save(LANE_OUT/'decision.json',state)
        run_lane('playmode','p3-density-runtime-scoped-01',['scoped-scene','gui']);state['steps'].append('scoped_gpu_passed');save(LANE_OUT/'decision.json',state)
        state['rendered']=[]
        for i,name in enumerate(['baseline','candidate','candidate','baseline']):
            select_lane(name);folder=run_lane('performance','p3-density-runtime-abba-'+str(i+1),['gui','48'])
            state['rendered'].append(analyze_render(folder));save(LANE_OUT/'decision.json',state)
        a,b,c,d=state['rendered'];assert all(x['conditions']==a['conditions'] for x in state['rendered'])
        state['renderGatePassed']=all(y['p99Ms']<=x['p99Ms']*.97 and x['p99Ms']-y['p99Ms']>=1.0 and y['medianMs']<=x['medianMs']*1.05 and y['over33ms']<=x['over33ms'] and y['over50ms']<=max(x['over50ms'],3) for x,y in [(a,b),(d,c)])
        state['adopted']=state['renderGatePassed'];state['status']='density_runtime_adopted_P3_still_open' if state['adopted'] else 'density_runtime_reverted_insufficient_rendered_gain'
        select_lane('candidate' if state['adopted'] else 'baseline')
    except Exception as e:
        state['status']='blocked_or_failed_baseline_required';state['error']=str(e);print('ERROR',str(e),flush=True)
    finally:
        if not state['adopted']:
            if not processes() and not(ROOT/'Temp/UnityLockfile').exists() and all((ROOT/p).read_bytes() in [original[p],candidate[p]] for p in original):
                for p,b in original.items():(ROOT/p).write_bytes(b)
                state['adoptedBinaryBaselinePreserved']=True
            else:state['adoptedBinaryBaselinePreserved']=False
        state['finalProject']=project_check(full=True);state['userData']=user_check()
        state['headUnchanged']=subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip()==initial['gitHead']
        state['indexUnchanged']=(sha(ROOT/'.git/index') if (ROOT/'.git/index').is_file() else None)==initial['index']
        build=ROOT/'Builds/CaptureDefault-20261006-37';manifest=json.loads((BASE/'current37.sha256.json').read_text(encoding='utf-8'))
        changed=[x['path'] for x in manifest if not(build/x['path']).is_file() or sha(build/x['path'])!=x['sha256']]
        unexpected=sorted({p.relative_to(build).as_posix() for p in build.rglob('*') if p.is_file()}-{x['path'] for x in manifest})
        state['build37']={'files':len(manifest),'changed':changed,'unexpectedNew':unexpected,'passed':not changed and not unexpected}
        state['activeProcesses']=processes();state['projectLock']=(ROOT/'Temp/UnityLockfile').exists()
        state['protectionPassed']=state['finalProject']['passed'] and state['userData']['passed'] and state['headUnchanged'] and state['indexUnchanged'] and state['build37']['passed'] and not state['activeProcesses'] and not state['projectLock']
        save(LANE_OUT/'decision.json',state);print('FINAL',json.dumps(state,ensure_ascii=False,indent=2),flush=True)
    if 'error' in state or not state['protectionPassed']:sys.exit(1)

if __name__=='__main__':main()
