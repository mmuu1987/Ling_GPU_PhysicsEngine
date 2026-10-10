"""Lane fixed-input diagnosis and bounded same-output candidate, preserving the adopted binary/output baseline."""
from p3_binary_output_20261008 import *
from p3_binary_output_20261008 import project_check as adopted_check
from common_p8 import project_check as raw_check
import ast

LANE_OUT=LOG/'P3'/'lane-20261008-01'
LANE_PATH='Assets/MassEngine/Terrain/TerrainLaneApproach33.cs'


def project_check(full=False):
    r=raw_check(full)
    reg=json.loads((LANE_OUT/'registry.json').read_text(encoding='utf-8'))
    cardinal=json.loads((DEST/'registry.json').read_text(encoding='utf-8'))
    variant=next((name for name,files in reg['variants'].items() if all(sha(ROOT/p)==h for p,h in files.items())),None)
    valid=variant is not None and sha(ROOT/TEST_PATH)==reg['testSha256'] and all(sha(ROOT/p)==h for p,h in cardinal['tests'].items())
    if valid:
        r['changed']=[x for x in r['changed'] if x['path'] not in [NAV_PATH,LANE_PATH,TEST_PATH]]
        r['unexpectedNew']=[p for p in r['unexpectedNew'] if p not in cardinal['tests']]
    r['laneVariant']=variant;r['passed']=valid and not r['changed'] and not r['unexpectedNew']
    return r


def select_lane(name):
    assert name in ['baseline','candidate']
    assert not processes() and not(ROOT/'Temp/UnityLockfile').exists()
    check=project_check();assert check['passed'],check
    reg=json.loads((LANE_OUT/'registry.json').read_text(encoding='utf-8'))
    for p in [NAV_PATH,LANE_PATH]:
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
    initialCheck=adopted_check(full=True);assert initialCheck['passed'] and initialCheck['outputVariant']=='candidate',initialCheck
    assert not processes() and not(ROOT/'Temp/UnityLockfile').exists() and user_check()['passed']
    fixed=json.loads((LOG/'P3'/'fixed-input-20261008-01/summary.json').read_text(encoding='utf-8'))
    capture=LOG/'P3'/'fixed-input-20261008-01-capture'
    assert all(sha(capture/p)==h for p,h in fixed['artifactHashes'].items())
    original={p:(ROOT/p).read_bytes() for p in [NAV_PATH,LANE_PATH]};testOriginal=(ROOT/TEST_PATH).read_bytes()
    payload=json.loads((ROOT/'Tools/InteractionRefinement/p3-lane-20261008-payload.json').read_text(encoding='utf-8'))
    text=original[NAV_PATH].decode('utf-8').replace('\r\n','\n')
    for a,b in payload['gridPatches']:
        assert text.count(a)==1,a;text=text.replace(a,b,1)
    candidate={NAV_PATH:text.encode('utf-8'),LANE_PATH:payload['laneCandidate'].encode('utf-8')}
    tests=testOriginal.decode('utf-8')
    marker='            type.GetMethod("PrepareUniformOutputDirections", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.Invoke(grid, null);'
    assert tests.count(marker)==1
    tests=tests.replace(marker,'            type.GetMethod("PrepareCellCenterAxes", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.Invoke(grid, null);\n'+marker,1)
    tests+=payload['testAppend'];testBytes=tests.encode('utf-8')
    LANE_OUT.mkdir();(LANE_OUT/'test-before.cs.txt').write_bytes(testOriginal);(LANE_OUT/'test-reviewed.cs.txt').write_bytes(testBytes)
    for name,files in [('baseline',original),('candidate',candidate)]:
        for p,b in files.items():q=LANE_OUT/name/p;q.parent.mkdir(parents=True,exist_ok=True);q.write_bytes(b)
    reg={'variants':{name:{p:hashlib.sha256(b).hexdigest() for p,b in files.items()} for name,files in [('baseline',original),('candidate',candidate)]},'testSha256':hashlib.sha256(testBytes).hexdigest(),'testBeforeSha256':hashlib.sha256(testOriginal).hexdigest()};save(LANE_OUT/'registry.json',reg)
    initial={'project':initialCheck,'gitHead':subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip(),'index':sha(ROOT/'.git/index') if (ROOT/'.git/index').is_file() else None};save(LANE_OUT/'initial.json',initial)
    scope={'baseline':'Previously ADOPTED binary heap priority reuse + uniform direction output table; never restore pre-adoption original as Lane fallback.','fixedGate':'All 14 new Lane/center cases and existing suites pass; 6 full Lane outputs bit-exact; baseline current/reference within 15%; coarse reference scan >=70% of reference total and >1ms total; candidate current/reference <=0.85 in all 6 inputs.','renderGate':'Both ABBA pairs P99 at least 10% below adopted baseline; ordinary median <=1.10x baseline; >33.33ms count no higher; >50ms count <= max(baseline,3) (rare-event floor declared in advance).','memory':'New per-grid center axes: 4*(ResolutionX+ResolutionZ) payload bytes plus two array headers, 2048 bytes for 256x256. Constructor-only O(width+depth). No route/edge cache, no per-Apply center-array allocations.','preserved':'Mean duplicate weights, filter/order, scalar floating expression order, zero products for NaN/Inf, strict dot/near thresholds, Vector2.Distance, cardinal route checks, stop/unreachable guards; no frequency/count/48/GPU-layout changes.','bounds':'GC allocation counter unavailable; coarse clocks have paired uninstrumented control. Candidate acceptance is bounded improvement, not P3 closure.'};save(LANE_OUT/'scope.json',scope)
    for name in ['test_runner.py','rendered_runner.py']:
        text=(OUT/name).read_text(encoding='utf-8').replace('from p3_binary_output_20261008 import *','from p3_lane_followup_20261008 import *')
        if name=='test_runner.py':text=text.replace("filters={'output-fixed'","filters={'lane-fixed':'MassEngine.Tests.P3LaneFixedTests;MassEngine.Tests.P3UniformOutputTests;MassEngine.Tests.TerrainNavigationGridTests;MassEngine.Tests.TerrainCardinalRouteCacheTests;MassEngine.Tests.LocalOrderPlanTests','output-fixed'")
        ast.parse(text);(LANE_OUT/name).write_bytes(text.encode('utf-8'))
    state={'status':'running','adopted':False,'P3Closed':False,'steps':[]};save(LANE_OUT/'decision.json',state)
    try:
        assert not processes() and not(ROOT/'Temp/UnityLockfile').exists()
        assert (ROOT/TEST_PATH).read_bytes()==testOriginal
        (ROOT/TEST_PATH).write_bytes(testBytes)
        before=run_lane('editmode','p3-lane-before-01',['lane-fixed']);state['before']=lane_analysis(before)
        root=ET.parse(before/'results.xml').getroot();cases=[x for x in root.iter('test-case') if 'P3LaneFixedTests.' in x.get('fullname','')]
        assert len(cases)==14 and all(x.get('result')=='Passed' for x in cases)
        assert all(.85<=r['currentRatio']<=1.15 and r['modeMedianMs']['reference']>1 and r['originalPhasesMs']['scanMs']>=r['modeMedianMs']['profiled-reference']*.70 for r in state['before']),'Baseline Lane calibration/hotspot gate failed'
        state['steps'].append('baseline_lane_diagnosed');save(LANE_OUT/'decision.json',state)
        select_lane('candidate')
        after=run_lane('editmode','p3-lane-after-01',['lane-fixed']);state['after']=lane_analysis(after)
        assert all(r['currentRatio']<=.85 for r in state['after']),'Insufficient same-input Lane gain; do not render'
        assert all(a['finalLaneOutputSha256']==b['finalLaneOutputSha256'] for a,b in zip(state['before'],state['after']))
        state['steps'].append('candidate_exact_and_local_gain');save(LANE_OUT/'decision.json',state)
        run_lane('playmode','p3-lane-scoped-01',['scoped-scene','gui']);state['steps'].append('scoped_gpu_passed');save(LANE_OUT/'decision.json',state)
        state['rendered']=[]
        for i,name in enumerate(['baseline','candidate','candidate','baseline']):
            select_lane(name);folder=run_lane('performance','p3-lane-abba-'+str(i+1),['gui','48'])
            state['rendered'].append(analyze_render(folder));save(LANE_OUT/'decision.json',state)
        a,b,c,d=state['rendered'];assert all(x['conditions']==a['conditions'] for x in state['rendered'])
        state['renderGatePassed']=all(y['p99Ms']<=x['p99Ms']*.9 and y['medianMs']<=x['medianMs']*1.1 and y['over33ms']<=x['over33ms'] and y['over50ms']<=max(x['over50ms'],3) for x,y in [(a,b),(d,c)])
        state['adopted']=state['renderGatePassed'];state['status']='lane_improvement_adopted_P3_still_open' if state['adopted'] else 'lane_candidate_reverted_insufficient_rendered_gain'
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
