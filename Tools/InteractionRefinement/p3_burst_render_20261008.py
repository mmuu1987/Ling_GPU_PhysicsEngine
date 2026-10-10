"""Coarse runtime decomposition on adopted Lane baseline. Temporary probes; restores exact source."""
from p3_density_runtime_20261008 import *
from p3_density_runtime_20261008 import project_check as adopted_check
DETAIL=LOG/'P3'/'burst-render-20261008-01'
ASM_PATH='Assets/MassEngine/Tests/EditMode/MassEngine.Tests.asmdef'
PATHS=[RUNTIME_PATH,'Assets/Game/Editor/InteractionRefinementP3Qualification.cs',TEST_PATH,ASM_PATH]

def project_check(full=False):
    r=raw_check(full)
    reg=json.loads((LANE_OUT/'registry.json').read_text(encoding='utf-8'))
    diag=json.loads((DETAIL/'registry.json').read_text(encoding='utf-8'))
    cardinal=json.loads((DEST/'registry.json').read_text(encoding='utf-8'))
    variant=next((name for name,files in diag['variants'].items() if all(sha(ROOT/p)==h for p,h in files.items())),None)
    valid=variant is not None and all(sha(ROOT/p)==h for p,h in reg['variants']['baseline'].items() if p not in PATHS) and all(sha(ROOT/p)==h for p,h in cardinal['tests'].items())
    if valid:
        r['changed']=[x for x in r['changed'] if x['path'] not in PATHS+[NAV_PATH,LANE_PATH,TEST_PATH]]
        r['unexpectedNew']=[p for p in r['unexpectedNew'] if p not in cardinal['tests']]
    r['diagnosticVariant']=variant;r['passed']=valid and not r['changed'] and not r['unexpectedNew'];return r

def main():
    sys.stdout.reconfigure(encoding='utf-8')
    assert not DETAIL.exists()
    initial=adopted_check(full=True);assert initial['passed'] and initial['laneVariant']=='baseline',initial
    assert not processes() and not(ROOT/'Temp/UnityLockfile').exists() and user_check()['passed']
    original={p:(ROOT/p).read_bytes() for p in PATHS}
    head=subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip();index=sha(ROOT/'.git/index') if (ROOT/'.git/index').is_file() else None
    payload=json.loads((ROOT/'Tools/InteractionRefinement/p3-burst-render-20261008-payload.json').read_text(encoding='utf-8'))
    modified={}
    for p,key in [(RUNTIME_PATH,'runtime'),(PATHS[1],'observer')]:
        text=original[p].decode('utf-8').replace('\r\n','\n')
        for a,b in payload[key]:assert text.count(a)==1,(p,a);text=text.replace(a,b,1)
        modified[p]=text.encode('utf-8')
    modified[TEST_PATH]=original[TEST_PATH]+b'\n'+payload['testAppend'].encode('utf-8')
    asm=json.loads(original[ASM_PATH].decode('utf-8'))
    for ref in ['Unity.Burst','Unity.Collections']:
        if ref not in asm['references']:asm['references'].append(ref)
    modified[ASM_PATH]=(json.dumps(asm,indent=4)+'\n').encode('utf-8')
    DETAIL.mkdir()
    for name,files in [('original',original),('probed',modified)]:
        for p,data in files.items():q=DETAIL/name/p;q.parent.mkdir(parents=True,exist_ok=True);q.write_bytes(data)
    save(DETAIL/'registry.json',{'variants':{name:{p:hashlib.sha256(data).hexdigest() for p,data in files.items()} for name,files in [('original',original),('probed',modified)]}})
    save(DETAIL/'scope.json',{'purpose':'Controlled transient Editor integration after21 Burst feasibility cases passed. No production adoption; restore all4 source/asmdef files after ABBA.', 'sequence':['original','probed','probed','original'], 'bridge':'Test assembly same strict synchronous job via opt-in Editor-only Runtime delegates; lazy owned workspace per runtime; Dispose hook and pre-domain-reload/quit cleanup; audit after scene runtime release. No engine asmdef/package changes, no scheduling.', 'gate':'Both P99 <=0.80x paired baseline, ordinary median<=1.05x, >33.33ms count<=0.5x, >50ms<=max(baseline,3). All probed solves prove Burst. Every created native workspace disposed, active0 at audit. Gate is eligibility for production design, NOT automatic adoption.', 'limits':'Native+managed graph coexist,~4.31MiB extra at256square, startup compile/copy disclosed; proof check uses native scalar. Existing Lane output, density readback, counts2048/separation48 unchanged. Editor evidence only, no EXE/AOT/player guarantees.'})
    runner=(LANE_OUT/'rendered_runner.py').read_text(encoding='utf-8').replace('from p3_density_runtime_20261008 import *','from p3_burst_render_20261008 import *')
    runner=runner.replace("# Known Editor-owned settings", "if 'burst-probe' in sys.argv:args+=['--p3-burst-render-probe']\n# Known Editor-owned settings",1)
    # Preserve absent automatic settings as well as existing ones, without touching user edits.
    marker="saved={p:(ROOT/p).read_bytes() for p in auto if(ROOT/p).is_file()}"
    assert runner.count(marker)==1
    runner=runner.replace(marker,"missing_auto=[p for p in auto if not(ROOT/p).is_file()]\n"+marker)
    marker="  receipt['restoredExternalEditorSettings']=[]"
    assert runner.count(marker)==1
    runner=runner.replace(marker,"  receipt['restoredOriginallyAbsent']=[]\n  for rel in missing_auto:\n   path=ROOT/rel\n   if path.is_file():\n    q=output/'auto-settings-generated'/rel;q.parent.mkdir(parents=True,exist_ok=True);q.write_bytes(path.read_bytes());path.unlink();receipt['restoredOriginallyAbsent'].append(rel)\n"+marker)
    ast.parse(runner);(DETAIL/'rendered_runner.py').write_bytes(runner.encode('utf-8'))
    state={'status':'running','P3Closed':False,'rendered':[],'audits':[]};save(DETAIL/'summary.json',state)
    try:
        env=os.environ.copy();env['PYTHONPATH']=str(ROOT/'Tools/InteractionRefinement')+os.pathsep+env.get('PYTHONPATH','')
        for i,name in enumerate(['original','probed','probed','original']):
            assert not processes() and not(ROOT/'Temp/UnityLockfile').exists() and project_check()['passed']
            for p,data in (original if name=='original' else modified).items():(ROOT/p).write_bytes(data)
            tag='p3-burst-render-'+str(i+1);folder=LOG/'P3'/tag;assert not folder.exists()
            print('RUN',tag,name,flush=True)
            with (DETAIL/(tag+'.log')).open('w',encoding='utf-8') as f:r=subprocess.run([sys.executable,'-X','utf8',str(DETAIL/'rendered_runner.py'),'performance',tag,'gui','48']+(['burst-probe'] if name=='probed' else []),cwd=ROOT,env=env,stdout=f,stderr=subprocess.STDOUT)
            assert r.returncode==0,'Runner failed '+tag
            state['rendered'].append(analyze_render(folder))
            if name=='probed':
                audit=json.loads((folder/'burst-audit.json').read_text(encoding='utf-8'))
                assert audit['solves']>0 and audit['solves']==audit['burstProofs'] and audit['created']==audit['disposed'] and audit['active']==0
                state['audits'].append(audit)
            save(DETAIL/'summary.json',state)
        a,b,c,e=state['rendered'];assert all(x['conditions']==a['conditions'] for x in state['rendered'])
        state['renderGatePassed']=all(y['p99Ms']<=.80*x['p99Ms'] and y['medianMs']<=1.05*x['medianMs'] and y['over33ms']<=.5*x['over33ms'] and y['over50ms']<=max(x['over50ms'],3) for x,y in [(a,b),(e,c)])
        state['status']='burst_render_feasibility_passed_source_restored' if state['renderGatePassed'] else 'burst_render_gate_failed_source_restored'
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
