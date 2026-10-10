"""Coarse runtime decomposition on adopted Lane baseline. Temporary probes; restores exact source."""
from p3_density_runtime_20261008 import *
from p3_density_runtime_20261008 import project_check as adopted_check
DETAIL=LOG/'P3'/'thread-cpu-20261008-01'
PATHS=['Assets/MassEngine/Terrain/TerrainNavigationRuntime.cs','Assets/Game/Editor/InteractionRefinementP3Qualification.cs']

def project_check(full=False):
    r=raw_check(full)
    reg=json.loads((LANE_OUT/'registry.json').read_text(encoding='utf-8'))
    diag=json.loads((DETAIL/'registry.json').read_text(encoding='utf-8'))
    cardinal=json.loads((DEST/'registry.json').read_text(encoding='utf-8'))
    variant=next((name for name,files in diag['variants'].items() if all(sha(ROOT/p)==h for p,h in files.items())),None)
    valid=variant is not None and all(sha(ROOT/p)==h for p,h in reg['variants']['baseline'].items() if p not in PATHS) and sha(ROOT/TEST_PATH)==reg['testSha256'] and all(sha(ROOT/p)==h for p,h in cardinal['tests'].items())
    if valid:
        r['changed']=[x for x in r['changed'] if x['path'] not in PATHS+[NAV_PATH,LANE_PATH,TEST_PATH]]
        r['unexpectedNew']=[p for p in r['unexpectedNew'] if p not in cardinal['tests']]
    r['diagnosticVariant']=variant;r['passed']=valid and not r['changed'] and not r['unexpectedNew'];return r

def cpu_summary(folder):
    from functools import reduce
    raw=[{k:int(v) for k,v in x.items()} for x in csv.DictReader((folder/'thread-cpu.csv').open(encoding='utf-8'))]
    frames=list(csv.DictReader((folder/'frames.csv').open(encoding='utf-8')));wanted={int(x['frame']) for x in frames[1:]}
    rows=[r for r in raw if r['frame'] in wanted and r['kind']==1]
    assert 60<=len(rows)<=120 and len({r['threadId'] for r in raw})==1
    assert all(r['a0']<=r['a1']<=r['b0']<=r['b1']<=r['c0']<=r['c1'] for r in raw)
    assert all(r['ak']<=r['bk']<=r['ck'] and r['au']<=r['bu']<=r['cu'] and r['acycles']<=r['bcycles']<=r['ccycles'] for r in raw)
    freq=rows[0]['frequency'];assert all(r['frequency']==freq for r in raw)
    phases={}
    for name,x,y in [('flow','a','b'),('lane','b','c'),('flowAndLane','a','c')]:
        wall=[1000*(r[y+'0']-r[x+'1'])/freq for r in rows]
        cpu=[(r[y+'k']+r[y+'u']-r[x+'k']-r[x+'u'])/10000 for r in rows]
        cycles=[r[y+'cycles']-r[x+'cycles'] for r in rows]
        ticks=[int(round(t*10000)) for t in cpu if t>0]
        phases[name]={'calls':len(rows),'wallTotalMs':sum(wall),'cpuTotalMs':sum(cpu),'cpuToWallRatio':sum(cpu)/sum(wall),'wallMedianMs':statistics.median(wall),'cyclesTotal':sum(cycles),'cyclesMedian':statistics.median(cycles),'cpuDeltasMs':sorted(set(cpu)),'observedCpuDeltaGcdMs':reduce(math.gcd,ticks)/10000 if ticks else None,'gc0Deltas':sum(r[y+'gc']-r[x+'gc'] for r in rows),'note':'Aggregate accounting comparison, not exact wait percentage. Thread time may be quantized; cycle counts are not converted to seconds or frequency.'}
    empty=[{k:int(v) for k,v in x.items()} for x in csv.DictReader((folder/'thread-cpu-empty.csv').open(encoding='utf-8'))]
    emptycost=[1000*(r['end']-r['begin'])/r['frequency'] for r in empty]
    costs=[1000*(r[x+'1']-r[x+'0'])/freq for r in rows for x in ['a','b','c']]
    result={'tag':folder.name,'windowDynamicCalls':len(rows),'allCallsIncludingWarmup':len(raw),'threadId':rows[0]['threadId'],'phases':phases,'emptyReadCostMs':{'median':statistics.median(emptycost),'max':max(emptycost)},'windowReadCostMs':{'median':statistics.median(costs),'max':max(costs),'sum':sum(costs)},'osStatus':json.loads((folder/'os-counter-status.json').read_text(encoding='utf-8')),'notPerformanceQualification':True}
    result['supportsCpuDominatedFlow']=.8<=phases['flow']['cpuToWallRatio']<=1.2 and result['windowReadCostMs']['median']<.1 and result['windowReadCostMs']['max']<1
    save(folder/'thread-cpu-analysis.json',result);return result

def main():
    sys.stdout.reconfigure(encoding='utf-8')
    assert not DETAIL.exists()
    initial=adopted_check(full=True);assert initial['passed'] and initial['laneVariant']=='baseline',initial
    assert not processes() and not(ROOT/'Temp/UnityLockfile').exists() and user_check()['passed']
    original={p:(ROOT/p).read_bytes() for p in PATHS}
    head=subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip();index=sha(ROOT/'.git/index') if (ROOT/'.git/index').is_file() else None
    payload=json.loads((ROOT/'Tools/InteractionRefinement/p3-thread-cpu-20261008-payload.json').read_text(encoding='utf-8'))
    modified={}
    for p,data in original.items():
        text=data.decode('utf-8').replace('\r\n','\n')
        for a,b in payload[p]:assert text.count(a)==1,(p,a);text=text.replace(a,b,1)
        modified[p]=text.encode('utf-8')
    DETAIL.mkdir()
    for name,files in [('original',original),('probed',modified)]:
        for p,data in files.items():q=DETAIL/name/p;q.parent.mkdir(parents=True,exist_ok=True);q.write_bytes(data)
    save(DETAIL/'registry.json',{'variants':{name:{p:hashlib.sha256(data).hexdigest() for p,data in files.items()} for name,files in [('original',original),('probed',modified)]}})
    save(DETAIL/'scope.json',{'sequence':['probed','probed'],'purpose':'Boundary-only main-thread CPU vs wall-clock accounting in current managed solver and Lane; two fixed diagnostic replications, no new optimization or performance qualification.','probes':'Native GetThreadTimes + QueryThreadCycleTime + QPC at three Upload phase boundaries. Preallocated4096 rows;257 empty snapshots before first solve in warmup. Only window dynamic calls analyzed, rest preserved. No per-cell/per-agent/per-frame native calls. OS sampler1Hz for owned process only.','interpretation':'GetThreadTimes100ns units do NOT imply100ns accuracy. Report observed deltas and gcd; sum85-ish calls instead of interpreting individual rounded CPU deltas. Cycles not converted to frequency. CPU/wall residual NOT exact wait time.','directionalCheck':'Two windows fixed before execution. Support CPU-dominated flow only if BOTH aggregate CPU/wall ratios0.8..1.2 and boundary API read median<0.1ms/max<1ms. Otherwise unresolved, no retry fishing. This is diagnostic interpretation only, not original performance gates.','limits':'No Burst enabled, no scalar sum claims for overlapping times, no source adoption. Whole-frame overhead uncalibrated; prior calibration failure unchanged. Final source/observer restored exactly. Preserve150tests,37,48,userfiles,HEAD/index; no packaging.'})
    runner=(LANE_OUT/'rendered_runner.py').read_text(encoding='utf-8').replace('from p3_density_runtime_20261008 import *','from p3_thread_cpu_20261008 import *')
    runner=runner.replace('child=subprocess.Popen(args,cwd=ROOT);', 'from p3_os_sampler_20261008 import OsSampler\nchild=subprocess.Popen(args,cwd=ROOT);os_sampler=OsSampler(child.pid);os_sampler.start();')
    marker='finally:\n # Never restore into a concurrently opened user Editor.'
    assert runner.count(marker)==1
    runner=runner.replace(marker,'finally:\n os_sampler.stop(output)\n # Never restore into a concurrently opened user Editor.')
    ast.parse(runner);(DETAIL/'rendered_runner.py').write_bytes(runner.encode('utf-8'))
    state={'status':'running','P3Closed':False,'rendered':[],'stages':[]};save(DETAIL/'summary.json',state)
    try:
        env=os.environ.copy();env['PYTHONPATH']=str(ROOT/'Tools/InteractionRefinement')+os.pathsep+env.get('PYTHONPATH','')
        for i,name in enumerate(['probed','probed']):
            assert not processes() and not(ROOT/'Temp/UnityLockfile').exists() and project_check()['passed']
            for p,data in (original if name=='original' else modified).items():(ROOT/p).write_bytes(data)
            tag='p3-thread-cpu-'+str(i+1);folder=LOG/'P3'/tag;assert not folder.exists()
            print('RUN',tag,name,flush=True)
            with (DETAIL/(tag+'.log')).open('w',encoding='utf-8') as f:r=subprocess.run([sys.executable,'-X','utf8',str(DETAIL/'rendered_runner.py'),'performance',tag,'gui','48'],cwd=ROOT,env=env,stdout=f,stderr=subprocess.STDOUT)
            assert r.returncode==0,'Runner failed '+tag
            state['rendered'].append(analyze_render(folder))
            if name=='probed':
                state.setdefault('cpuProbes',[]).append(cpu_summary(folder))
            save(DETAIL/'summary.json',state)
        state['controlGatePassed']=False;state['performanceQualificationAttempted']=False;state['supportsCpuDominatedFlow']=all(r['supportsCpuDominatedFlow'] for r in state['cpuProbes']);state['status']='thread_cpu_diagnosis_complete_not_performance_qualification'
    except Exception as e:state.update(status='diagnosis_failed',error=str(e));print('ERROR',str(e),flush=True)
    finally:
        safe=not processes() and not(ROOT/'Temp/UnityLockfile').exists() and all((ROOT/p).read_bytes() in [original[p],modified[p]] for p in PATHS)
        if safe:
            for p,data in original.items():(ROOT/p).write_bytes(data)
        state['diagnosticRestored']=safe and all((ROOT/p).read_bytes()==data for p,data in original.items())
        state['finalProject']=adopted_check(full=True);state['userData']=user_check()
        state['headUnchanged']=subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip()==head
        state['indexUnchanged']=(sha(ROOT/'.git/index') if (ROOT/'.git/index').is_file() else None)==index
        build=ROOT/'Builds/CaptureDefault-20261006-37';manifest=json.loads((BASE/'current37.sha256.json').read_text(encoding='utf-8'))
        changed=[x['path'] for x in manifest if not(build/x['path']).is_file() or sha(build/x['path'])!=x['sha256']]
        extra=sorted({p.relative_to(build).as_posix() for p in build.rglob('*') if p.is_file()}-{x['path'] for x in manifest})
        state['build37']={'files':len(manifest),'changed':changed,'unexpectedNew':extra,'passed':not changed and not extra}
        state['activeProcesses']=processes();state['projectLock']=(ROOT/'Temp/UnityLockfile').exists()
        state['protectionPassed']=state['diagnosticRestored'] and state['finalProject']['passed'] and state['finalProject']['laneVariant']=='baseline' and state['userData']['passed'] and state['headUnchanged'] and state['indexUnchanged'] and state['build37']['passed'] and not state['activeProcesses'] and not state['projectLock']
        save(DETAIL/'summary.json',state);print('FINAL',json.dumps(state,ensure_ascii=False,indent=2),flush=True)
    if 'error' in state or not state['protectionPassed']:sys.exit(1)
if __name__=='__main__':main()
