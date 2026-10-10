"""Temporary test-only work census. No production variants or rendered queue."""
from p3_density_runtime_20261008 import *
from p3_density_runtime_20261008 import project_check as lane_check
WORK=LOG/'P3'/'burst-probe-20261008-01'
ASM_PATH='Assets/MassEngine/Tests/EditMode/MassEngine.Tests.asmdef'

def project_check(full=False):
    r=raw_check(full)
    reg=json.loads((LANE_OUT/'registry.json').read_text(encoding='utf-8'))
    diag=json.loads((WORK/'registry.json').read_text(encoding='utf-8'))
    cardinal=json.loads((DEST/'registry.json').read_text(encoding='utf-8'))
    valid=sha(ROOT/ASM_PATH) in [diag['asmOriginalSha256'],diag['asmDiagnosticSha256']] and all(sha(ROOT/p)==h for p,h in reg['variants']['baseline'].items()) and sha(ROOT/TEST_PATH) in [reg['testSha256'],diag['diagnosticTestSha256']] and all(sha(ROOT/p)==h for p,h in cardinal['tests'].items())
    if valid:
        r['changed']=[x for x in r['changed'] if x['path'] not in [NAV_PATH,LANE_PATH,RUNTIME_PATH,TEST_PATH,ASM_PATH]]
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
    original=(ROOT/TEST_PATH).read_bytes();append=(ROOT/'Tools/InteractionRefinement/P3BurstPayload-20261008.cs.txt').read_bytes();diagnostic=original+b'\n'+append
    asmOriginal=(ROOT/ASM_PATH).read_bytes();asm=json.loads(asmOriginal.decode('utf-8'))
    assert not asm['allowUnsafeCode']
    for ref in ['Unity.Burst','Unity.Collections']:
        if ref not in asm['references']:asm['references'].append(ref)
    asmDiagnostic=(json.dumps(asm,indent=4)+'\n').encode('utf-8')
    head=subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip();index=sha(ROOT/'.git/index') if (ROOT/'.git/index').is_file() else None
    WORK.mkdir();(WORK/'test-before.cs.txt').write_bytes(original);(WORK/'test-diagnostic.cs.txt').write_bytes(diagnostic)
    (WORK/'asm-before.json').write_bytes(asmOriginal);(WORK/'asm-diagnostic.json').write_bytes(asmDiagnostic)
    save(WORK/'registry.json',{'asmOriginalSha256':hashlib.sha256(asmOriginal).hexdigest(),'asmDiagnosticSha256':hashlib.sha256(asmDiagnostic).hexdigest(),'originalTestSha256':hashlib.sha256(original).hexdigest(),'diagnosticTestSha256':hashlib.sha256(diagnostic).hexdigest()})
    save(WORK/'scope.json',{'scope':'Temporary test assembly Burst1.8.29/Collections2.6.5 references to already resolved packages; no package or engine asmdef changes. Same binary Dijkstra, synchronous IJob.Run only; FloatMode.Strict/FloatPrecision.Standard. No threading/scheduling change.', 'proof':'BurstDiscard managed canary must0 and Run must1. Full bits+complete pop trace vs current production on6 captured inputs and15 constructed skinny/fractional/blocked/height fixtures. Separate trace job is not timed.', 'timing':'3warmups+12rotated pairs each input; Burst wrapper includes ordered target conversion, independent managed result allocation, native reset, synchronous Run, result CopyTo. Initialization/native snapshot allocation and first compilation separately recorded. All native arrays disposed. No claimed steady-state zero allocations.', 'gate':'All21 cases pass, actual Burst proof, all6 exact bits/traces. Every fixed input median wrapper <=0.70x current production AND >=10/12 paired wins. This is feasibility only, not adoption or rendered/P3 closure.', 'memory':'4,522,060 native payload bytes at256square uniform grid, on top of managed grid; trace arrays separate untimed. Ownership/startup costs must be addressed before production.', 'restore':'Restore exact150-test source and test asmdef bytes; preserve all production, package files,user data,37,HEAD,index.'})
    script=(LANE_OUT/'test_runner.py').read_text(encoding='utf-8').replace('from p3_density_runtime_20261008 import *','from p3_burst_probe_20261008 import *')
    script=script.replace("filters={'density-runtime'","filters={'burst-probe':'MassEngine.Tests.P3BurstProbeTests','density-runtime'")
    ast.parse(script);(WORK/'test_runner.py').write_bytes(script.encode('utf-8'))
    state={'status':'running','productionChanged':False,'P3Closed':False};save(WORK/'summary.json',state)
    folder=P8/'p3-burst-probe-01';assert not folder.exists()
    try:
        (ROOT/ASM_PATH).write_bytes(asmDiagnostic)
        (ROOT/TEST_PATH).write_bytes(diagnostic)
        env=os.environ.copy();env['PYTHONPATH']=str(ROOT/'Tools/InteractionRefinement')+os.pathsep+env.get('PYTHONPATH','')
        print('RUN21 synchronous Burst feasibility cases, actual compiled proof and exact traces; production unchanged',flush=True)
        with (WORK/'runner.log').open('w',encoding='utf-8') as f:
            r=subprocess.run([sys.executable,'-X','utf8',str(WORK/'test_runner.py'),'editmode',folder.name,'burst-probe'],cwd=ROOT,env=env,stdout=f,stderr=subprocess.STDOUT)
        assert r.returncode==0,'Test runner failed; inspect runner.log'
        receipt=json.loads((folder/'process.json').read_text(encoding='utf-8'));assert receipt['passed']
        root=ET.parse(folder/'results.xml').getroot();assert int(root.get('passed','0'))==21 and int(root.get('failed','0'))==0 and int(root.get('skipped','0'))==0
        inputs=[]
        for i in range(6):
            q=json.loads((folder/('burst-probe-'+str(i)+'.json')).read_text(encoding='utf-8'))
            assert all(q[k] for k in ['exactBits','heapTraceExact','burstExecuted','managedCanaryWorks']) and len(q['rows'])==24
            med={m:statistics.median(x['milliseconds'] for x in q['rows'] if x['mode']==m) for m in ['current-managed','burst-wrapper']}
            wins=sum(next(x['milliseconds'] for x in q['rows'] if x['repetition']==rep and x['mode']=='burst-wrapper') < next(x['milliseconds'] for x in q['rows'] if x['repetition']==rep and x['mode']=='current-managed') for rep in range(12))
            q.update(medians=med,ratio=med['burst-wrapper']/med['current-managed'],pairedFaster=wins);inputs.append(q)
        eligible=all(q['ratio']<=.70 and q['pairedFaster']>=10 for q in inputs)
        state.update(status='burst_feasibility_passed' if eligible else 'burst_local_gain_insufficient',testsPassed=21,localGatePassed=eligible,inputs=inputs)
    except Exception as e:
        state.update(status='diagnosis_failed',error=str(e));print('ERROR',str(e),flush=True)
    finally:
        safe=not processes() and not(ROOT/'Temp/UnityLockfile').exists() and (ROOT/TEST_PATH).read_bytes() in [original,diagnostic] and (ROOT/ASM_PATH).read_bytes() in [asmOriginal,asmDiagnostic]
        if safe:
            (ROOT/TEST_PATH).write_bytes(original)
            (ROOT/ASM_PATH).write_bytes(asmOriginal)
        state['diagnosticTestRestored']=safe and (ROOT/TEST_PATH).read_bytes()==original and (ROOT/ASM_PATH).read_bytes()==asmOriginal
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
