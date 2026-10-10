"""Temporary test-only work census. No production variants or rendered queue."""
from p3_lane_early_20261008 import *
from p3_lane_early_20261008 import project_check as lane_check
WORK=LOG/'P3'/'density-copy-20261008-01'

def project_check(full=False):
    r=raw_check(full)
    reg=json.loads((LANE_OUT/'registry.json').read_text(encoding='utf-8'))
    diag=json.loads((WORK/'registry.json').read_text(encoding='utf-8'))
    cardinal=json.loads((DEST/'registry.json').read_text(encoding='utf-8'))
    valid=all(sha(ROOT/p)==h for p,h in reg['variants']['candidate'].items()) and sha(ROOT/TEST_PATH) in [reg['testSha256'],diag['diagnosticTestSha256']] and all(sha(ROOT/p)==h for p,h in cardinal['tests'].items())
    if valid:
        r['changed']=[x for x in r['changed'] if x['path'] not in [NAV_PATH,LANE_PATH,TEST_PATH]]
        r['unexpectedNew']=[p for p in r['unexpectedNew'] if p not in cardinal['tests']]
    r['passed']=valid and not r['changed'] and not r['unexpectedNew'];return r

def main():
    sys.stdout.reconfigure(encoding='utf-8')
    assert not WORK.exists()
    check=lane_check(full=True);assert check['passed'] and check['laneVariant']=='candidate',check
    assert not processes() and not(ROOT/'Temp/UnityLockfile').exists() and user_check()['passed']
    fixed=json.loads((LOG/'P3'/'fixed-input-20261008-01/summary.json').read_text(encoding='utf-8'))
    capture=LOG/'P3'/'fixed-input-20261008-01-capture'
    assert all(sha(capture/p)==h for p,h in fixed['artifactHashes'].items())
    original=(ROOT/TEST_PATH).read_bytes();append=(ROOT/'Tools/InteractionRefinement/P3DensityCopyTests-20261008.cs.txt').read_bytes();diagnostic=original+b'\n'+append
    head=subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip();index=sha(ROOT/'.git/index') if (ROOT/'.git/index').is_file() else None
    WORK.mkdir();(WORK/'test-before.cs.txt').write_bytes(original);(WORK/'test-diagnostic.cs.txt').write_bytes(diagnostic)
    save(WORK/'registry.json',{'originalTestSha256':hashlib.sha256(original).hexdigest(),'diagnosticTestSha256':hashlib.sha256(diagnostic).hexdigest()})
    save(WORK/'scope.json',{'purpose':'Test-only native density indexing vs CopyTo(reused uint[]) + identical ordered scan. Six support patterns reconstructed from captured ordered goals; magnitudes synthetic, not original GPU snapshots. 8 edge/reuse fixtures and1 real completed GPU readback smoke. No production change.', 'timing':'3 warmups +20 alternating pairs, copy and List.Clear included, comparison outside clock; persistent scratch allocation excluded and disclosed. Paired output lists preallocated to target count.', 'localGate':'Every six inputs bulk/native median <=0.80 AND >=16/20 paired repetitions faster. Gate only merits further runtime integration investigation, NOT adoption/P3 closure.', 'memory':'4*CellCount bytes plus array header per runtime if later integrated:256KiB at256x256; no zero-allocation claim.', 'limits':'Owned NativeArray local test is not live GetData timing. GPU WaitForCompletion only in test to obtain completed view; do NOT introduce waits in production. Request cancellation/roundrobin/release integration remains untested.', 'preserve':'All production bytes,125 retained tests restored, input hashes,user files,37,HEAD,index. No rendered queue or packaging.'})
    script=(LANE_OUT/'test_runner.py').read_text(encoding='utf-8').replace('from p3_lane_early_20261008 import *','from p3_density_copy_20261008 import *')
    script=script.replace("filters={'lane-early'","filters={'density-copy':'MassEngine.Tests.P3DensityCopyTests','lane-early'")
    ast.parse(script);(WORK/'test_runner.py').write_bytes(script.encode('utf-8'))
    state={'status':'running','productionChanged':False,'P3Closed':False};save(WORK/'summary.json',state)
    folder=P8/'p3-density-copy-01';assert not folder.exists()
    try:
        (ROOT/TEST_PATH).write_bytes(diagnostic)
        env=os.environ.copy();env['PYTHONPATH']=str(ROOT/'Tools/InteractionRefinement')+os.pathsep+env.get('PYTHONPATH','')
        print('RUN 15 density copy equivalence/timing/GPU-smoke tests; production unchanged',flush=True)
        with (WORK/'runner.log').open('w',encoding='utf-8') as f:
            r=subprocess.run([sys.executable,'-X','utf8',str(WORK/'test_runner.py'),'editmode',folder.name,'density-copy'],cwd=ROOT,env=env,stdout=f,stderr=subprocess.STDOUT)
        assert r.returncode==0,'Test runner failed; inspect runner.log'
        receipt=json.loads((folder/'process.json').read_text(encoding='utf-8'));assert receipt['passed']
        root=ET.parse(folder/'results.xml').getroot();assert int(root.get('passed','0'))==15 and int(root.get('failed','0'))==0 and int(root.get('skipped','0'))==0
        inputs=[]
        for i in range(6):
            q=json.loads((folder/('density-copy-'+str(i)+'.json')).read_text(encoding='utf-8'))
            assert q['exactBits'] and q['sourceUnchanged'] and len(q['rows'])==40
            med={m:statistics.median(x['milliseconds'] for x in q['rows'] if x['mode']==m) for m in ['native-index','bulk-copy-scan']}
            wins=sum(next(x['milliseconds'] for x in q['rows'] if x['repetition']==rep and x['mode']=='bulk-copy-scan')<next(x['milliseconds'] for x in q['rows'] if x['repetition']==rep and x['mode']=='native-index') for rep in range(20))
            q.update(medians=med,ratio=med['bulk-copy-scan']/med['native-index'],pairedFaster=wins);inputs.append(q)
        passed=all(q['ratio']<=.8 and q['pairedFaster']>=16 for q in inputs)
        state.update(status='local_experiment_passed_no_production_change' if passed else 'local_gain_insufficient_no_production_change',testsPassed=15,localGatePassed=passed,inputs=inputs)
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
        state['protectionPassed']=state['diagnosticTestRestored'] and state['finalProject']['passed'] and state['finalProject']['laneVariant']=='candidate' and state['userData']['passed'] and state['headUnchanged'] and state['indexUnchanged'] and state['build37']['passed'] and not state['activeProcesses'] and not state['projectLock']
        save(WORK/'summary.json',state);print('FINAL',json.dumps(state,ensure_ascii=False,indent=2),flush=True)
    if 'error' in state or not state['protectionPassed']:sys.exit(1)

if __name__=='__main__':main()
