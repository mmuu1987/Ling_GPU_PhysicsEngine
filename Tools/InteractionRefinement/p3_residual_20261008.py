"""Coarse runtime decomposition on adopted Lane baseline. Temporary probes; restores exact source."""
from p3_lane_early_20261008 import *
from p3_lane_early_20261008 import project_check as adopted_check
DETAIL=LOG/'P3'/'residual-20261008-01'
PATHS=['Assets/MassEngine/Terrain/TerrainNavigationRuntime.cs','Assets/Game/Editor/InteractionRefinementP3Qualification.cs']

def project_check(full=False):
    r=raw_check(full)
    reg=json.loads((LANE_OUT/'registry.json').read_text(encoding='utf-8'))
    diag=json.loads((DETAIL/'registry.json').read_text(encoding='utf-8'))
    cardinal=json.loads((DEST/'registry.json').read_text(encoding='utf-8'))
    variant=next((name for name,files in diag['variants'].items() if all(sha(ROOT/p)==h for p,h in files.items())),None)
    valid=variant is not None and all(sha(ROOT/p)==h for p,h in reg['variants']['candidate'].items()) and sha(ROOT/TEST_PATH)==reg['testSha256'] and all(sha(ROOT/p)==h for p,h in cardinal['tests'].items())
    if valid:
        r['changed']=[x for x in r['changed'] if x['path'] not in PATHS+[NAV_PATH,LANE_PATH,TEST_PATH]]
        r['unexpectedNew']=[p for p in r['unexpectedNew'] if p not in cardinal['tests']]
    r['diagnosticVariant']=variant;r['passed']=valid and not r['changed'] and not r['unexpectedNew'];return r

def stage_summary(folder):
    frames=list(csv.DictReader((folder/'frames.csv').open(encoding='utf-8')))
    intervals={int(b['frame']):1000*(float(b['seconds'])-float(a['seconds'])) for a,b in zip(frames,frames[1:])}
    raw=list(csv.DictReader((folder/'stages.csv').open(encoding='utf-8')))
    assert len(raw)<=32768,'Probe list capacity exceeded'
    events=[{k:int(v) for k,v in x.items()} for x in raw]
    assert all(x['t0']<=x['t1']<=x['t2']<=x['t3']<=x['t4'] for x in events)
    group={}
    for x in events:
        if x['frame'] in intervals:group.setdefault(x['frame'],[]).append(x)
    per=[]
    for frame,dt in intervals.items():
        xs=group.get(frame,[]);ticks=[x for x in xs if x['kind']==0];uploads=[x for x in xs if x['kind']!=0]
        ms=lambda x,a,b:1000*(x[b]-x[a])/x['frequency']
        row={'frame':frame,'intervalMs':dt,'tickCount':len(ticks),'uploads':len(uploads),'goals':sum(x['goals'] for x in uploads),'flowMs':sum(ms(x,'t0','t1') for x in uploads),'laneMs':sum(ms(x,'t1','t2') for x in uploads),'metricBookkeepingMs':sum(ms(x,'t2','t3') for x in uploads),'setDataCpuMs':sum(ms(x,'t3','t4') for x in uploads),'uploadTotalMs':sum(ms(x,'t0','t4') for x in uploads),'tickTotalMs':sum(ms(x,'t0','t2') for x in ticks),'readbackPrepMs':sum(ms(x,'t0','t1') for x in ticks)}
        row['outsideTickMs']=dt-row['tickTotalMs'];row['outsideUploadMs']=dt-row['uploadTotalMs'];per.append(row)
    save(folder/'aligned-stages.json',per)
    with (folder/'aligned-stages.csv').open('w',encoding='utf-8',newline='') as f:
        w=csv.DictWriter(f,fieldnames=list(per[0]));w.writeheader();w.writerows(per)
    solve=[x for x in per if x['uploads']];ordinary=[x for x in per if not x['uploads']];long=[x for x in per if x['intervalMs']>1000/30]
    assert solve and ordinary and all(x['tickCount']>0 for x in solve)
    assert all(x['outsideTickMs']>=-.2 for x in solve),'Frame attribution inconsistent'
    stats=lambda xs:{k:statistics.median(x[k] for x in xs) for k in ['intervalMs','flowMs','laneMs','setDataCpuMs','uploadTotalMs','tickTotalMs','readbackPrepMs','outsideTickMs','outsideUploadMs']}
    return {'tag':folder.name,'rawEvents':len(events),'intervals':len(per),'solveFrames':len(solve),'ordinaryFrames':len(ordinary),'longFrames':len(long),'longWithUpload':sum(x['uploads']>0 for x in long),'uploadCounts':sorted(set(x['uploads'] for x in solve)),'solveFrameMedians':stats(solve),'ordinaryFrameMedians':stats(ordinary),'longFrameMedians':stats(long) if long else {},'lagLongMatches':{str(lag):sum(any(x['kind']!=0 for x in group.get(y['frame']+lag,[])) for y in long) for lag in [-2,-1,0,1,2]},'maxSetDataCpuMs':max(x['setDataCpuMs'] for x in per),'boundaryNote':'Direct Time.frameCount join to callback interval ending at this frame. OutsideTick is inclusive wall-clock residual, NOT pure CPU component or GPU attribution. SetData CPU time is not GPU execution/wait completion.'}

def main():
    sys.stdout.reconfigure(encoding='utf-8')
    assert not DETAIL.exists()
    initial=adopted_check(full=True);assert initial['passed'] and initial['laneVariant']=='candidate',initial
    assert not processes() and not(ROOT/'Temp/UnityLockfile').exists() and user_check()['passed']
    original={p:(ROOT/p).read_bytes() for p in PATHS}
    head=subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip();index=sha(ROOT/'.git/index') if (ROOT/'.git/index').is_file() else None
    payload=json.loads((ROOT/'Tools/InteractionRefinement/p3-residual-20261008-payload.json').read_text(encoding='utf-8'))
    modified={}
    for p,data in original.items():
        text=data.decode('utf-8').replace('\r\n','\n')
        for a,b in payload[p]:assert text.count(a)==1,(p,a);text=text.replace(a,b,1)
        modified[p]=text.encode('utf-8')
    DETAIL.mkdir()
    for name,files in [('original',original),('probed',modified)]:
        for p,data in files.items():q=DETAIL/name/p;q.parent.mkdir(parents=True,exist_ok=True);q.write_bytes(data)
    save(DETAIL/'registry.json',{'variants':{name:{p:hashlib.sha256(data).hexdigest() for p,data in files.items()} for name,files in [('original',original),('probed',modified)]}})
    save(DETAIL/'scope.json',{'sequence':['original','probed','probed','original'],'purpose':'New adopted source baseline decomposition only, no new optimization candidate or requalification claim. Unmodified navigation/Lane and tests throughout.','probes':'UNITY_EDITOR temporary Tick three timestamps, Upload five timestamps. Struct buffer32768; no per-cell clocks, no per-frame IO. Write only after measurement; restore exact runtime and observer bytes.','controlGate':'Both probed/original pairs P99 within +/-5%, median within +/-10%. If outside, disclose disturbance and do not interpret as calibrated decomposition; no repeat-until-pass.','attribution':'Frame-ID joined callback intervals. Tick includes async readback sparse-copy preparation + upload; upload covers flow, Lane, bookkeeping, SetData CPU. Residual is unassigned wall-clock, NOT GPU or specific main-thread subsystem. No zero-allocation claim.','protected':'No refresh/count/48/build/stage/commit/push or permanent gameplay changes; preserve125 tests and both adopted optimizations.'})
    runner=(LANE_OUT/'rendered_runner.py').read_text(encoding='utf-8').replace('from p3_lane_early_20261008 import *','from p3_residual_20261008 import *')
    # Preserve absent automatic settings as well as existing ones, without touching user edits.
    marker="saved={p:(ROOT/p).read_bytes() for p in auto if(ROOT/p).is_file()}"
    assert runner.count(marker)==1
    runner=runner.replace(marker,"missing_auto=[p for p in auto if not(ROOT/p).is_file()]\n"+marker)
    marker="  receipt['restoredExternalEditorSettings']=[]"
    assert runner.count(marker)==1
    runner=runner.replace(marker,"  receipt['restoredOriginallyAbsent']=[]\n  for rel in missing_auto:\n   path=ROOT/rel\n   if path.is_file():\n    q=output/'auto-settings-generated'/rel;q.parent.mkdir(parents=True,exist_ok=True);q.write_bytes(path.read_bytes());path.unlink();receipt['restoredOriginallyAbsent'].append(rel)\n"+marker)
    ast.parse(runner);(DETAIL/'rendered_runner.py').write_bytes(runner.encode('utf-8'))
    state={'status':'running','P3Closed':False,'rendered':[],'stages':[]};save(DETAIL/'summary.json',state)
    try:
        env=os.environ.copy();env['PYTHONPATH']=str(ROOT/'Tools/InteractionRefinement')+os.pathsep+env.get('PYTHONPATH','')
        for i,name in enumerate(['original','probed','probed','original']):
            assert not processes() and not(ROOT/'Temp/UnityLockfile').exists() and project_check()['passed']
            for p,data in (original if name=='original' else modified).items():(ROOT/p).write_bytes(data)
            tag='p3-residual-'+str(i+1);folder=LOG/'P3'/tag;assert not folder.exists()
            print('RUN',tag,name,flush=True)
            with (DETAIL/(tag+'.log')).open('w',encoding='utf-8') as f:r=subprocess.run([sys.executable,'-X','utf8',str(DETAIL/'rendered_runner.py'),'performance',tag,'gui','48'],cwd=ROOT,env=env,stdout=f,stderr=subprocess.STDOUT)
            assert r.returncode==0,'Runner failed '+tag
            state['rendered'].append(analyze_render(folder))
            if name=='probed':state['stages'].append(stage_summary(folder))
            save(DETAIL/'summary.json',state)
        a,b,c,e=state['rendered'];assert all(x['conditions']==a['conditions'] for x in state['rendered'])
        state['controlGatePassed']=all(.95<=y['p99Ms']/x['p99Ms']<=1.05 and .9<=y['medianMs']/x['medianMs']<=1.1 for x,y in [(a,b),(e,c)])
        state['status']='diagnosis_complete_calibrated' if state['controlGatePassed'] else 'diagnosis_complete_control_disturbance_disclosed'
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
        state['protectionPassed']=state['diagnosticRestored'] and state['finalProject']['passed'] and state['finalProject']['laneVariant']=='candidate' and state['userData']['passed'] and state['headUnchanged'] and state['indexUnchanged'] and state['build37']['passed'] and not state['activeProcesses'] and not state['projectLock']
        save(DETAIL/'summary.json',state);print('FINAL',json.dumps(state,ensure_ascii=False,indent=2),flush=True)
    if 'error' in state or not state['protectionPassed']:sys.exit(1)
if __name__=='__main__':main()
