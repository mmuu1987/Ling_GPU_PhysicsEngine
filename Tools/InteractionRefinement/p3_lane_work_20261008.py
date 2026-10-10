"""Temporary test-only work census. No production variants or rendered queue."""
from p3_lane_followup_20261008 import *
from p3_lane_followup_20261008 import project_check as lane_check
WORK=LOG/'P3'/'lane-work-20261008-01'

def project_check(full=False):
    r=raw_check(full)
    reg=json.loads((LANE_OUT/'registry.json').read_text(encoding='utf-8'))
    diag=json.loads((WORK/'registry.json').read_text(encoding='utf-8'))
    cardinal=json.loads((DEST/'registry.json').read_text(encoding='utf-8'))
    valid=all(sha(ROOT/p)==h for p,h in reg['variants']['baseline'].items()) and sha(ROOT/TEST_PATH) in [reg['testSha256'],diag['diagnosticTestSha256']] and all(sha(ROOT/p)==h for p,h in cardinal['tests'].items())
    if valid:
        r['changed']=[x for x in r['changed'] if x['path'] not in [NAV_PATH,LANE_PATH,TEST_PATH]]
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
    original=(ROOT/TEST_PATH).read_bytes();append=(ROOT/'Tools/InteractionRefinement/P3LaneWorkTests-20261008.cs.txt').read_bytes();diagnostic=original+b'\n'+append
    head=subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip();index=sha(ROOT/'.git/index') if (ROOT/'.git/index').is_file() else None
    WORK.mkdir();(WORK/'test-before.cs.txt').write_bytes(original);(WORK/'test-diagnostic.cs.txt').write_bytes(diagnostic)
    save(WORK/'registry.json',{'originalTestSha256':hashlib.sha256(original).hexdigest(),'diagnosticTestSha256':hashlib.sha256(diagnostic).hexdigest()})
    save(WORK/'scope.json',{'purpose':'Count work, not a new production candidate. No per-cell timing. Untimed clone counted twice, output and every route decision/search result checked against actual baseline. All six output hashes must match prior baseline evidence. Timing only untouched production Apply before/after count runs.','preserve':'All production bytes, retained105 tests restored, inputs, user files,37,HEAD,index. No rendered reruns/GPU/package.','interpretation':'Operation counts are not time attribution; repetitions of counters do not prove performance benefits of hypothetical rewrites.'})
    script=(LANE_OUT/'test_runner.py').read_text(encoding='utf-8').replace('from p3_lane_followup_20261008 import *','from p3_lane_work_20261008 import *')
    script=script.replace("filters={'lane-fixed'","filters={'lane-work':'MassEngine.Tests.P3LaneWorkTests','lane-fixed'")
    ast.parse(script);(WORK/'test_runner.py').write_bytes(script.encode('utf-8'))
    state={'status':'running','productionChanged':False,'P3Closed':False};save(WORK/'summary.json',state)
    folder=P8/'p3-lane-work-01';assert not folder.exists()
    try:
        (ROOT/TEST_PATH).write_bytes(diagnostic)
        env=os.environ.copy();env['PYTHONPATH']=str(ROOT/'Tools/InteractionRefinement')+os.pathsep+env.get('PYTHONPATH','')
        print('RUN six fixed-input Lane work census; production unchanged',flush=True)
        with (WORK/'runner.log').open('w',encoding='utf-8') as f:
            r=subprocess.run([sys.executable,'-X','utf8',str(WORK/'test_runner.py'),'editmode',folder.name,'lane-work'],cwd=ROOT,env=env,stdout=f,stderr=subprocess.STDOUT)
        assert r.returncode==0,'Test runner failed; inspect runner.log'
        receipt=json.loads((folder/'process.json').read_text(encoding='utf-8'));assert receipt['passed']
        root=ET.parse(folder/'results.xml').getroot();assert int(root.get('passed','0'))==6 and int(root.get('failed','0'))==0 and int(root.get('skipped','0'))==0
        inputs=[]
        before=json.loads((LANE_OUT/'decision.json').read_text(encoding='utf-8'))['before']
        for i in range(6):
            q=json.loads((folder/('lane-work-'+str(i)+'.json')).read_text(encoding='utf-8'))
            assert all(q[k] for k in ['exactBits','goalsUnchanged','countsRepeatedExactly','routeDecisionsExact'])
            h=sha(folder/('lane-work-final-'+str(i)+'.bin'));assert h==before[i]['finalLaneOutputSha256']
            q['finalOutputSha256']=h;q['beforeMedianMs']=statistics.median(q['uninstrumentedBeforeMs']);q['afterMedianMs']=statistics.median(q['uninstrumentedAfterMs']);inputs.append(q)
        state.update(status='diagnosis_complete_no_production_change',testsPassed=6,inputs=inputs)
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
