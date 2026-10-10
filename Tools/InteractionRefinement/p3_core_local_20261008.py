"""Temporary test-only work census. No production variants or rendered queue."""
from p3_density_runtime_20261008 import *
from p3_density_runtime_20261008 import project_check as lane_check
WORK=LOG/'P3'/'core-local-20261008-01'

def project_check(full=False):
    r=raw_check(full)
    reg=json.loads((LANE_OUT/'registry.json').read_text(encoding='utf-8'))
    diag=json.loads((WORK/'registry.json').read_text(encoding='utf-8'))
    cardinal=json.loads((DEST/'registry.json').read_text(encoding='utf-8'))
    valid=all(sha(ROOT/p)==h for p,h in reg['variants']['baseline'].items()) and sha(ROOT/TEST_PATH) in [reg['testSha256'],diag['diagnosticTestSha256']] and all(sha(ROOT/p)==h for p,h in cardinal['tests'].items())
    if valid:
        r['changed']=[x for x in r['changed'] if x['path'] not in [NAV_PATH,LANE_PATH,RUNTIME_PATH,TEST_PATH]]
        r['unexpectedNew']=[p for p in r['unexpectedNew'] if p not in cardinal['tests']]
    r['passed']=valid and not r['changed'] and not r['unexpectedNew'];return r

def main():
    sys.stdout.reconfigure(encoding='utf-8')
    assert not WORK.exists()
    check=lane_check(full=True);assert check['passed'] and check['laneVariant']=='baseline',check
    assert not processes() and not(ROOT/'Temp/UnityLockfile').exists() and user_check()['passed']
    fixed=json.loads((LOG/'P3'/'fixed-input-20261008-01/summary.json').read_text(encoding='utf-8'))
    capture=LOG/'P3'/'fixed-input-20261008-01-capture'
    assert all(sha(capture/p)==h for p,h in fixed['artifactHashes'].items())
    original=(ROOT/TEST_PATH).read_bytes();append=(ROOT/'Tools/InteractionRefinement/P3CorePayload-20261008.cs.txt').read_bytes();diagnostic=original+b'\n'+append
    head=subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip();index=sha(ROOT/'.git/index') if (ROOT/'.git/index').is_file() else None
    WORK.mkdir();(WORK/'test-before.cs.txt').write_bytes(original);(WORK/'test-diagnostic.cs.txt').write_bytes(diagnostic)
    save(WORK/'registry.json',{'originalTestSha256':hashlib.sha256(original).hexdigest(),'diagnosticTestSha256':hashlib.sha256(diagnostic).hexdigest()})
    save(WORK/'scope.json',{'scope':'Test-only same-current-source baseline, hoisted settled cell invariants, uniform-output next-index delta decoding at widths>=3, and their combination. No production writes. Width1/2 and nonuniform spacing retain prior formulas.', 'validation':'6 recorded inputs complete bits;12 rotated timings/mode+3warmup;8 skinny/width3/fractional/large-origin constructed cases; untimed separate baseline/combined pop-trace clones exact. No trace branch in timed clones.', 'gate':'Baseline clone median within3% current production each input; candidate >=10% gain vs same-round baseline clone on ALL6 inputs before any production integration/rendering. No retry-until-pass.', 'preserved':'Binary heap/tie order/relaxation order/costs/refresh/count/48;150 existing tests restored;all adopted production kept. No new arrays/cache in candidates. GC allocation counter unavailable.'})
    script=(LANE_OUT/'test_runner.py').read_text(encoding='utf-8').replace('from p3_density_runtime_20261008 import *','from p3_core_local_20261008 import *')
    script=script.replace("filters={'density-runtime'","filters={'core-local':'MassEngine.Tests.P3CoreTests','density-runtime'")
    ast.parse(script);(WORK/'test_runner.py').write_bytes(script.encode('utf-8'))
    state={'status':'running','productionChanged':False,'P3Closed':False};save(WORK/'summary.json',state)
    folder=P8/'p3-core-local-01';assert not folder.exists()
    try:
        (ROOT/TEST_PATH).write_bytes(diagnostic)
        env=os.environ.copy();env['PYTHONPATH']=str(ROOT/'Tools/InteractionRefinement')+os.pathsep+env.get('PYTHONPATH','')
        print('RUN14 core fixed-input candidates and exact heap traces; production unchanged',flush=True)
        with (WORK/'runner.log').open('w',encoding='utf-8') as f:
            r=subprocess.run([sys.executable,'-X','utf8',str(WORK/'test_runner.py'),'editmode',folder.name,'core-local'],cwd=ROOT,env=env,stdout=f,stderr=subprocess.STDOUT)
        assert r.returncode==0,'Test runner failed; inspect runner.log'
        receipt=json.loads((folder/'process.json').read_text(encoding='utf-8'));assert receipt['passed']
        root=ET.parse(folder/'results.xml').getroot();assert int(root.get('passed','0'))==14 and int(root.get('failed','0'))==0 and int(root.get('skipped','0'))==0
        inputs=[]
        modes=['TerrainNavigationGrid','P3CoreBaseline','P3CoreHoisted','P3CoreDelta','P3CoreCombined']
        for i in range(6):
            q=json.loads((folder/('core-'+str(i)+'.json')).read_text(encoding='utf-8'))
            assert q['exactBits'] and q['heapTraceExact'] and len(q['rows'])==60
            med={m:statistics.median(x['milliseconds'] for x in q['rows'] if x['mode']==m) for m in modes}
            q['medians']=med;q['calibrationRatio']=med['P3CoreBaseline']/med['TerrainNavigationGrid'];q['ratios']={m:med[m]/med['P3CoreBaseline'] for m in modes[2:]};inputs.append(q)
        calibrated=all(.97<=q['calibrationRatio']<=1.03 for q in inputs)
        eligible=[m for m in modes[2:] if calibrated and all(q['ratios'][m]<=.9 for q in inputs)]
        state.update(status='local_candidates_eligible' if eligible else 'no_candidate_meets_local_gate',testsPassed=14,calibrated=calibrated,eligible=eligible,inputs=inputs)
    except Exception as e:
        state.update(status='diagnosis_failed',error=str(e));print('ERROR',str(e),flush=True)
    finally:
        safe=not processes() and not(ROOT/'Temp/UnityLockfile').exists() and (ROOT/TEST_PATH).read_bytes() in [original,diagnostic]
        if safe:(ROOT/TEST_PATH).write_bytes(original)
        state['diagnosticTestRestored']=safe and (ROOT/TEST_PATH).read_bytes()==original
        state['finalProject']=lane_check(full=True);state['userData']=user_check()
        state['headUnchanged']=subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip()==head
        state['indexUnchanged']=(sha(ROOT/'.git/index') if (ROOT/'.git/index').is_file() else None)==index
        build=ROOT/'Builds/CaptureDefault-20261006-37';manifest=json.loads((BASE/'current37.sha256.json').read_text(encoding='utf-8'))
        changed=[x['path'] for x in manifest if not(build/x['path']).is_file() or sha(build/x['path'])!=x['sha256']]
        extra=sorted({p.relative_to(build).as_posix() for p in build.rglob('*') if p.is_file()}-{x['path'] for x in manifest})
        state['build37']={'files':len(manifest),'changed':changed,'unexpectedNew':extra,'passed':not changed and not extra}
        state['activeProcesses']=processes();state['projectLock']=(ROOT/'Temp/UnityLockfile').exists()
        state['protectionPassed']=state['diagnosticTestRestored'] and state['finalProject']['passed'] and state['finalProject']['laneVariant']=='baseline' and state['userData']['passed'] and state['headUnchanged'] and state['indexUnchanged'] and state['build37']['passed'] and not state['activeProcesses'] and not state['projectLock']
        save(WORK/'summary.json',state);print('FINAL',json.dumps(state,ensure_ascii=False,indent=2),flush=True)
    if 'error' in state or not state['protectionPassed']:sys.exit(1)

if __name__=='__main__':main()
