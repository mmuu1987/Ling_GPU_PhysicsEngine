"""Finer decomposition of CreateFlowField and Lane Apply on the adopted baseline. Temporary probes; restores exact source bytes."""
from p3_density_runtime_20261008 import *
from p3_density_runtime_20261008 import project_check as adopted_check
import ast, csv, statistics
DETAIL=LOG/'P3'/'solve-detail-20261008-02'
ADOPTED_TEST_SHA='8bddd61426ec0eeb5de83bfa2594963ec5c8482c4f0228f1ce1d58a1d5a24ba8'
OBSERVER='Assets/Game/Editor/InteractionRefinementP3Qualification.cs'
PATHS=[NAV_PATH,LANE_PATH,OBSERVER]

def project_check(full=False):
    r=raw_check(full)
    diag=json.loads((DETAIL/'registry.json').read_text(encoding='utf-8'))
    cardinal=json.loads((DEST/'registry.json').read_text(encoding='utf-8'))
    variant=next((name for name,files in diag['variants'].items() if all(sha(ROOT/p)==h for p,h in files.items())),None)
    valid=variant is not None and sha(ROOT/TEST_PATH)==ADOPTED_TEST_SHA and all(sha(ROOT/p)==h for p,h in cardinal['tests'].items())
    if valid:
        r['changed']=[x for x in r['changed'] if x['path'] not in PATHS+[TEST_PATH]]
        r['unexpectedNew']=[p for p in r['unexpectedNew'] if p not in cardinal['tests']]
    r['diagnosticVariant']=variant;r['passed']=valid and not r['changed'] and not r['unexpectedNew'];return r

KEYS=['intervalMs','solveCalls','resetMs','seedMs','dijkstraMs','outputMs','solveMs','targets','seeds','pops','relaxes','laneCalls','laneBuildMs','laneLoopMs','laneRouteMs','laneMs','routeCalls','laneNonZero','laneRowCol','laneOverrides']

def detail_summary(folder):
    frames=list(csv.DictReader((folder/'frames.csv').open(encoding='utf-8')))
    intervals={int(b['frame']):1000*(float(b['seconds'])-float(a['seconds'])) for a,b in zip(frames,frames[1:])}
    solve=[{k:int(v) for k,v in x.items()} for x in csv.DictReader((folder/'solve-detail.csv').open(encoding='utf-8'))]
    lane=[{k:int(v) for k,v in x.items()} for x in csv.DictReader((folder/'lane-detail.csv').open(encoding='utf-8'))]
    assert len(solve)<32768 and len(lane)<32768,'Probe capacity exceeded'
    assert all(x['s0']<=x['s1']<=x['s2']<=x['s3']<=x['s4'] for x in solve)
    assert all(x['l0']<=x['l1']<=x['l2'] for x in lane)
    per={}
    for frame,dt in intervals.items():
        row={k:0 for k in KEYS};row['frame']=frame;row['intervalMs']=dt;per[frame]=row
    dropSolve=dropLane=0
    for x in solve:
        row=per.get(x['frame']);f=x['frequency']
        if row is None:dropSolve+=1;continue
        row['solveCalls']+=1
        row['resetMs']+=1000.0*(x['s1']-x['s0'])/f;row['seedMs']+=1000.0*(x['s2']-x['s1'])/f
        row['dijkstraMs']+=1000.0*(x['s3']-x['s2'])/f;row['outputMs']+=1000.0*(x['s4']-x['s3'])/f
        row['solveMs']+=1000.0*(x['s4']-x['s0'])/f
        row['targets']+=x['targets'];row['seeds']+=x['seeds'];row['pops']+=x['pops'];row['relaxes']+=x['relaxes']
    for x in lane:
        row=per.get(x['frame']);f=x['frequency']
        if row is None:dropLane+=1;continue
        row['laneCalls']+=1
        row['laneBuildMs']+=1000.0*(x['l1']-x['l0'])/f;row['laneLoopMs']+=1000.0*(x['l2']-x['l1'])/f
        row['laneRouteMs']+=1000.0*x['routeTicks']/f;row['laneMs']+=1000.0*(x['l2']-x['l0'])/f
        row['routeCalls']+=x['routeCalls'];row['laneNonZero']+=x['nonZero'];row['laneRowCol']+=x['rowCol'];row['laneOverrides']+=x['overrides']
    rows=list(per.values());assert rows
    save(folder/'aligned-detail.json',rows)
    with (folder/'aligned-detail.csv').open('w',encoding='utf-8',newline='') as fh:
        w=csv.DictWriter(fh,fieldnames=list(rows[0]));w.writeheader();w.writerows(rows)
    solveFrames=[x for x in rows if x['solveCalls']];longFrames=[x for x in rows if x['intervalMs']>1000/30]
    assert solveFrames
    med=lambda xs:{k:statistics.median(x[k] for x in xs) for k in KEYS}
    return {'tag':folder.name,'solveRows':len(solve),'laneRows':len(lane),'droppedSolveRows':dropSolve,'droppedLaneRows':dropLane,
            'intervals':len(rows),'solveFrames':len(solveFrames),'ordinaryFrames':len(rows)-len(solveFrames),
            'longFrames':len(longFrames),'longWithSolve':sum(1 for x in longFrames if x['solveCalls']>0),
            'solveFrameMedians':med(solveFrames),'longFrameMedians':med(longFrames) if longFrames else {},
            'note':'Coarse wall-clock probes only. laneRouteMs includes two extra clock reads per route call. Medians are not additive. Rows outside the measured window are dropped and counted. No GPU or GC attribution.'}

def main():
    sys.stdout.reconfigure(encoding='utf-8')
    assert not DETAIL.exists()
    initial=adopted_check(full=True);assert initial['passed'],initial
    assert not processes() and not(ROOT/'Temp/UnityLockfile').exists() and user_check()['passed']
    original={p:(ROOT/p).read_bytes() for p in PATHS}
    head=subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip();index=sha(ROOT/'.git/index') if (ROOT/'.git/index').is_file() else None
    payload=json.loads((ROOT/'Tools/InteractionRefinement/p3-solve-detail-20261008-payload.json').read_text(encoding='utf-8'))
    modified={}
    for p,data in original.items():
        text=data.decode('utf-8').replace('\r\n','\n')
        for a,b in payload[p]:assert text.count(a)==1,(p,a[:70]);text=text.replace(a,b,1)
        modified[p]=text.encode('utf-8')
    DETAIL.mkdir()
    for name,files in [('original',original),('probed',modified)]:
        for p,data in files.items():q=DETAIL/name/p;q.parent.mkdir(parents=True,exist_ok=True);q.write_bytes(data)
    save(DETAIL/'registry.json',{'variants':{name:{p:hashlib.sha256(data).hexdigest() for p,data in files.items()} for name,files in [('original',original),('probed',modified)]}})
    save(DETAIL/'scope.json',{'sequence':['original','probed','probed','original'],
        'purpose':'Decompose adopted-baseline CreateFlowField (reset/seed/dijkstra/output) and Lane Apply (build+sort/loop/route) inside long frames. Diagnosis only: no optimization candidate, no requalification, no adoption.',
        'probes':'UNITY_EDITOR temporary stamps and counters; per-call rows appended to preallocated 32768 lists, written once after measurement. Route check timing adds two clock reads per call and is disclosed as overhead. No per-cell clocks inside the Dijkstra loop; only integer counters there.',
        'controlGate':'Both probed/original pairs P99 within +/-5 percent and median within +/-10 percent, else disclosed as disturbance; no repeat-until-pass.',
        'attribution':'Frame-ID joined callback intervals, same as residual diagnostic. Unassigned wall-clock residual is NOT GPU wait, GC, or a named subsystem. No zero-allocation claim.',
        'protected':'No refresh/count/48/build/stage/commit/push changes; adopted navigation/Lane and all tests byte-identical outside this run; restore exact bytes afterwards.'})
    runner=(LANE_OUT/'rendered_runner.py').read_text(encoding='utf-8').replace('from p3_density_runtime_20261008 import *','from p3_solve_detail_20261008 import *')
    assert 'from p3_solve_detail_20261008 import *' in runner
    if 'missing_auto=' not in runner:
        marker="saved={p:(ROOT/p).read_bytes() for p in auto if(ROOT/p).is_file()}"
        assert runner.count(marker)==1
        runner=runner.replace(marker,"missing_auto=[p for p in auto if not(ROOT/p).is_file()]\n"+marker)
        marker="  receipt['restoredExternalEditorSettings']=[]"
        assert runner.count(marker)==1
        runner=runner.replace(marker,"  receipt['restoredOriginallyAbsent']=[]\n  for rel in missing_auto:\n   path=ROOT/rel\n   if path.is_file():\n    q=output/'auto-settings-generated'/rel;q.parent.mkdir(parents=True,exist_ok=True);q.write_bytes(path.read_bytes());path.unlink();receipt['restoredOriginallyAbsent'].append(rel)\n"+marker)
    ast.parse(runner);(DETAIL/'rendered_runner.py').write_bytes(runner.encode('utf-8'))
    state={'status':'running','P3Closed':False,'rendered':[],'details':[]};save(DETAIL/'summary.json',state)
    try:
        env=os.environ.copy();env['PYTHONPATH']=str(ROOT/'Tools/InteractionRefinement')+os.pathsep+env.get('PYTHONPATH','')
        for i,name in enumerate(['original','probed','probed','original']):
            assert not processes() and not(ROOT/'Temp/UnityLockfile').exists() and project_check()['passed']
            for p,data in (original if name=='original' else modified).items():(ROOT/p).write_bytes(data)
            tag='p3-solve-detail2-'+str(i+1);folder=LOG/'P3'/tag;assert not folder.exists()
            print('RUN',tag,name,flush=True)
            with (DETAIL/(tag+'.log')).open('w',encoding='utf-8') as f:r=subprocess.run([sys.executable,'-X','utf8',str(DETAIL/'rendered_runner.py'),'performance',tag,'gui','48'],cwd=ROOT,env=env,stdout=f,stderr=subprocess.STDOUT)
            assert r.returncode==0,'Runner failed '+tag
            state['rendered'].append(analyze_render(folder))
            if name=='probed':state['details'].append(detail_summary(folder))
            save(DETAIL/'summary.json',state)
        a,b,c,e=state['rendered'];assert all(x['conditions']==a['conditions'] for x in state['rendered'])
        state['controlGatePassed']=all(.95<=y['p99Ms']/x['p99Ms']<=1.05 and .9<=y['medianMs']/x['medianMs']<=1.1 for x,y in [(a,b),(e,c)])
        state['status']='solve_detail_complete_calibrated' if state['controlGatePassed'] else 'solve_detail_complete_control_disturbance_disclosed'
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
        state['protectionPassed']=state['diagnosticRestored'] and state['finalProject']['passed'] and state['userData']['passed'] and state['headUnchanged'] and state['indexUnchanged'] and state['build37']['passed'] and not state['activeProcesses'] and not state['projectLock']
        save(DETAIL/'summary.json',state);print('FINAL',json.dumps(state,ensure_ascii=False,indent=2),flush=True)
    if 'error' in state or not state['protectionPassed']:sys.exit(1)
if __name__=='__main__':main()
