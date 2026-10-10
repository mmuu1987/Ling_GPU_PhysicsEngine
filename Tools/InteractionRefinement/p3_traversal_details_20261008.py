"""Fixed-input traversal diagnosis; changes test assembly only, restores exact bytes."""
from p3_output_20261008 import *
from p3_output_20261008 import project_check as baseline_check
from common_p8 import project_check as raw_p8_check
import ast

DETAIL=LOG/'P3'/'traversal-details-20261008-01'

def project_check(full=False):
    r=raw_p8_check(full)
    registry=json.loads((DETAIL/'registry.json').read_text(encoding='utf-8'))
    cardinal=json.loads((DEST/'registry.json').read_text(encoding='utf-8'))
    valid=sha(ROOT/NAV_PATH)==registry['navigationSha256'] and sha(ROOT/TEST_PATH)==registry['activeTestSha256'] and all(sha(ROOT/p)==h for p,h in cardinal['tests'].items())
    if valid:
        r['changed']=[x for x in r['changed'] if x['path'] not in [TEST_PATH,NAV_PATH]]
        r['unexpectedNew']=[p for p in r['unexpectedNew'] if p not in cardinal['tests']]
    r['diagnosticTestHashesMatch']=valid
    r['passed']=valid and not r['changed'] and not r['unexpectedNew']
    return r

def main():
    sys.stdout.reconfigure(encoding='utf-8')
    assert not DETAIL.exists()
    print('PREFLIGHT current production and test hashes',flush=True)
    check=baseline_check(full=True);assert check['passed'] and check['outputVariant']=='reference',check
    assert not processes() and not(ROOT/'Temp/UnityLockfile').exists() and user_check()['passed']
    captures=LOG/'P3'/'fixed-input-20261008-01-capture'
    fixed=json.loads((LOG/'P3'/'fixed-input-20261008-01/summary.json').read_text(encoding='utf-8'))
    assert fixed['protectionPassed'] and all(sha(captures/name)==h for name,h in fixed['artifactHashes'].items())
    original=(ROOT/TEST_PATH).read_bytes();navOriginal=(ROOT/NAV_PATH).read_bytes()
    payload=json.loads((ROOT/'Tools/InteractionRefinement/p3-traversal-details-20261008-payload.json').read_text(encoding='utf-8'))
    diagnostic=original+b'\n'+payload['append'].encode('utf-8')
    DETAIL.mkdir();(DETAIL/'test-original.cs.txt').write_bytes(original);(DETAIL/'test-diagnostic.cs.txt').write_bytes(diagnostic)
    registry={'navigationSha256':sha(ROOT/NAV_PATH),'originalTestSha256':hashlib.sha256(original).hexdigest(),'diagnosticTestSha256':hashlib.sha256(diagnostic).hexdigest(),'activeTestSha256':hashlib.sha256(diagnostic).hexdigest()}
    save(DETAIL/'registry.json',registry)
    initial={'project':check,'gitHead':subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip(),'index':sha(ROOT/'.git/index') if (ROOT/'.git/index').is_file() else None};save(DETAIL/'initial.json',initial)
    scope={'productionChanged':False,'inputs':6,'modes':['original','disabled','counts','timed','heap-trace','trace-scaffold'],'repetitionsPerMode':8,'warmupPerMode':2,'rules':['All complete direction outputs bit-exact to recorded field','All heap pops exactly match trace','Count invariants must match across repeats','Use same saved graph/goals/radius','Do not subtract measured empty-clock means as if exact work cost','Traversal minus timed heap includes timer bookkeeping and guards, NOT pure neighbour-relaxation time','Heap trace excludes workspace reset and includes interpreter/record costs; scaffold only contextualizes, not exact subtraction','No production optimization or rendered performance queue; allocation counter previously failed its 1MiB positive control and is not used']}
    save(DETAIL/'scope.json',scope)
    runner=(OUT/'test_runner.py').read_text(encoding='utf-8')
    assert runner.count('from p3_output_20261008 import *')==1
    runner=runner.replace('from p3_output_20261008 import *','from p3_traversal_details_20261008 import *')
    runner=runner.replace("filters={'output-fixed'","filters={'traversal-detail':'MassEngine.Tests.P3TraversalAnalysisTests','output-fixed'")
    ast.parse(runner);(DETAIL/'test_runner.py').write_bytes(runner.encode('utf-8'))
    state={'status':'prepared','productionChanged':False,'P3Closed':False,'inputs':[]};save(DETAIL/'summary.json',state)
    try:
        assert (ROOT/TEST_PATH).read_bytes()==original and (ROOT/NAV_PATH).read_bytes()==navOriginal
        (ROOT/TEST_PATH).write_bytes(diagnostic)
        env=os.environ.copy();env['PYTHONPATH']=str(ROOT/'Tools/InteractionRefinement')+os.pathsep+env.get('PYTHONPATH','')
        tag='p3-traversal-details-01';folder=P8/tag;assert not folder.exists()
        print('RUN six-input traversal diagnostics and exact heap trace replay',flush=True)
        state['status']='running';save(DETAIL/'summary.json',state)
        with (DETAIL/'runner.log').open('w',encoding='utf-8') as f:
            r=subprocess.run([sys.executable,'-X','utf8',str(DETAIL/'test_runner.py'),'editmode',tag,'traversal-detail'],cwd=ROOT,env=env,stdout=f,stderr=subprocess.STDOUT)
        state['exitCode']=r.returncode
        assert r.returncode==0,'Diagnostic test runner failed; inspect runner.log and test XML'
        tree=ET.parse(folder/'results.xml').getroot();state['tests']=dict(tree.attrib)
        assert int(tree.get('passed','0'))==6 and int(tree.get('failed','0'))==0 and int(tree.get('skipped','0'))==0
        for i in range(6):
            report=json.loads((folder/('traversal-'+str(i)+'.json')).read_text(encoding='utf-8'))
            assert report['allDirectionsExact'] and report['allHeapPopsExact']
            rows=report['rows'];assert len(rows)==48
            groups={mode:[r for r in rows if r['mode']==mode] for mode in scope['modes']}
            assert all(len(v)==8 for v in groups.values())
            med={mode:statistics.median(r['elapsedMs'] for r in group) for mode,group in groups.items()}
            freq=report['clock']['frequency'];timed=groups['timed']
            times={name:statistics.median(r['counters'][name]*1000/freq for r in timed) for name in ['resetTicks','seedTicks','traversalTicks','outputTicks','popTicks','updateTicks','seedHeapTicks']}
            residual=statistics.median((r['counters']['traversalTicks']-r['counters']['popTicks']-r['counters']['updateTicks'])*1000/freq for r in timed)
            pairs=timed[0]['counters']['timerPairs']
            item={'sample':i,'goals':report['goals'],'cells':report['cells'],'allDirectionsExact':True,'allHeapPopsExact':True,'modeMedianMs':med,'instrumentedPhaseMedianMs':times,'traversalResidualIncludingProbeOverheadMs':residual,'work':report['work'],'clockCalibration':report['clock'],'clockPairsPerSolve':pairs,'emptyClockLoopScaleMsNotCorrection':pairs*report['clock']['averageWholePairNs']/1000000,'diagnosticDisabledOverOriginalRatio':med['disabled']/med['original'],'timedOverOriginalRatio':med['timed']/med['original'],'traceOperations':report['traceOperations'],'traceSha256':sha(folder/('traversal-'+str(i)+'-heap-trace.bin'))}
            state['inputs'].append(item)
        state['status']='analysis_complete_production_unchanged'
    except Exception as e:
        state['status']='failed_or_blocked';state['error']=str(e)
        print('ERROR',str(e),flush=True)
    finally:
        state['testRestored']=False
        if not processes() and not(ROOT/'Temp/UnityLockfile').exists() and (ROOT/TEST_PATH).read_bytes()==diagnostic:
            (ROOT/TEST_PATH).write_bytes(original);state['testRestored']=True
            registry['activeTestSha256']=registry['originalTestSha256'];save(DETAIL/'registry.json',registry)
        state['finalProject']=baseline_check(full=True);state['userData']=user_check();state['productionByteExact']=(ROOT/NAV_PATH).read_bytes()==navOriginal
        state['headUnchanged']=subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip()==initial['gitHead']
        state['indexUnchanged']=(sha(ROOT/'.git/index') if (ROOT/'.git/index').is_file() else None)==initial['index']
        build=ROOT/'Builds/CaptureDefault-20261006-37';manifest=json.loads((BASE/'current37.sha256.json').read_text(encoding='utf-8'))
        changes=[x['path'] for x in manifest if not(build/x['path']).is_file() or sha(build/x['path'])!=x['sha256']]
        unexpected=sorted({p.relative_to(build).as_posix() for p in build.rglob('*') if p.is_file()}-{x['path'] for x in manifest})
        state['build37']={'files':len(manifest),'changed':changes,'unexpectedNew':unexpected,'passed':not changes and not unexpected}
        state['activeProcesses']=processes();state['projectLock']=(ROOT/'Temp/UnityLockfile').exists()
        state['protectionPassed']=state['testRestored'] and state['productionByteExact'] and state['finalProject']['passed'] and state['userData']['passed'] and state['headUnchanged'] and state['indexUnchanged'] and state['build37']['passed'] and not state['activeProcesses'] and not state['projectLock']
        save(DETAIL/'summary.json',state)
        print('FINAL',json.dumps(state,ensure_ascii=False,indent=2),flush=True)
    if 'error' in state or not state['protectionPassed']:sys.exit(1)

if __name__=='__main__':main()
