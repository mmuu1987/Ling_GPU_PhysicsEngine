"""P3 direction-output candidate, gated fixed-input equivalence then rendered confirmation."""
from p3_cardinal_followup import *
from p3_cardinal_followup import project_check as handoff_check
from common_p8 import project_check as p8_check
import ast, xml.etree.ElementTree as ET

OUT=LOG/'P3'/'output-20261008-01'
NAV_PATH='Assets/MassEngine/Terrain/TerrainNavigationGrid.cs'
TEST_PATH='Assets/MassEngine/Tests/EditMode/TerrainNavigationGridTests.cs'


def project_check(full=False):
    r=p8_check(full)
    registry=json.loads((OUT/'registry.json').read_text(encoding='utf-8'))
    old=json.loads((DEST/'registry.json').read_text(encoding='utf-8'))
    tests=old['tests']
    variant=next((k for k,v in registry['variants'].items() if sha(ROOT/NAV_PATH)==v),None)
    valid=variant is not None and all(sha(ROOT/p)==h for p,h in tests.items()) and sha(ROOT/TEST_PATH)==registry['testSha256']
    if valid:
        r['changed']=[x for x in r['changed'] if x['path'] not in [NAV_PATH,TEST_PATH]]
        r['unexpectedNew']=[p for p in r['unexpectedNew'] if p not in tests]
    r['outputVariant']=variant
    r['passed']=valid and not r['changed'] and not r['unexpectedNew']
    return r


def select(name):
    assert not processes() and not (ROOT/'Temp/UnityLockfile').exists()
    check=project_check();assert check['passed'],check
    data=(OUT/(name+'.cs.txt')).read_bytes()
    registry=json.loads((OUT/'registry.json').read_text(encoding='utf-8'))
    assert hashlib.sha256(data).hexdigest()==registry['variants'][name]
    (ROOT/NAV_PATH).write_bytes(data)
    print('VARIANT',name,flush=True)


def run(kind,tag,extra):
    folder=(LOG/'P3' if kind=='performance' else P8)/tag
    assert not folder.exists()
    script=OUT/('rendered_runner.py' if kind=='performance' else 'test_runner.py')
    env=os.environ.copy();env['PYTHONPATH']=str(ROOT/'Tools/InteractionRefinement')+os.pathsep+env.get('PYTHONPATH','')
    print('RUN',kind,tag,flush=True)
    with (OUT/(tag+'.log')).open('w',encoding='utf-8') as log:
        proc=subprocess.run([sys.executable,'-X','utf8',str(script),kind,tag]+extra,cwd=ROOT,env=env,stdout=log,stderr=subprocess.STDOUT)
    assert proc.returncode==0,'Runner failed: '+tag
    receipt=json.loads((folder/'process.json').read_text(encoding='utf-8'))
    assert receipt['passed'],tag
    if kind!='performance':
        tree=ET.parse(folder/'results.xml').getroot()
        assert int(tree.get('failed','0'))==0 and int(tree.get('skipped','0'))==0
        print('TESTS',tag,tree.get('passed'),flush=True)
    return folder


def analyze_fixed(folder):
    results=[]
    for i in range(6):
        r=json.loads((folder/('output-fixed-'+str(i)+'.json')).read_text(encoding='utf-8'))
        assert r['exactBits'] and len(r['rows'])==12
        a=statistics.median(x['referenceMs'] for x in r['rows']);b=statistics.median(x['candidateMs'] for x in r['rows'])
        results.append({'sample':i,'referenceMedianMs':a,'currentMedianMs':b,'ratio':b/a,'uniformTableActive':r['uniformTableActive'],'allocationCounterAvailable':r['allocationCounterAvailable'],'canaryReportedBytes':r['canaryReportedBytes']})
    return results


def analyze_render(folder):
    receipt=json.loads((folder/'process.json').read_text(encoding='utf-8'));p=receipt['performance']
    assert p['units']==2048 and p['separationStrength']==48 and p['width']==1920 and p['height']==1080 and p['cameraFixed']
    rows=list(csv.DictReader((folder/'frames.csv').open(encoding='utf-8')))
    assert all(int(b['frame'])==int(a['frame'])+1 for a,b in zip(rows,rows[1:]))
    dt=[1000*(float(b['seconds'])-float(a['seconds'])) for a,b in zip(rows,rows[1:])]
    assert all(x>0 for x in dt)
    ordered=sorted(dt)
    q=lambda f:ordered[min(len(ordered)-1,math.ceil(f*len(ordered))-1)]
    return {'tag':folder.name,'samples':len(dt),'medianMs':statistics.median(dt),'p95Ms':q(.95),'p99Ms':q(.99),'over50ms':sum(x>50 for x in dt),'over33ms':sum(x>1000/30 for x in dt),'conditions':{k:p[k] for k in ['scene','cameraStart','cameraEnd','gpu','graphicsApi','vSyncCount','targetFrameRate']}}


def main():
    sys.stdout.reconfigure(encoding='utf-8')
    assert not OUT.exists(),'Never overwrite evidence'
    print('PREFLIGHT current reference and preserved test baseline',flush=True)
    check=handoff_check(full=True);assert check['passed'] and check['cardinalVariant']=='reference',check
    assert not processes() and not(ROOT/'Temp/UnityLockfile').exists() and user_check()['passed']
    captures=LOG/'P3'/'fixed-input-20261008-01-capture'
    fixed=json.loads((LOG/'P3'/'fixed-input-20261008-01'/'summary.json').read_text(encoding='utf-8'))
    assert fixed['protectionPassed'] and all(sha(captures/name)==h for name,h in fixed['artifactHashes'].items())
    original=(ROOT/NAV_PATH).read_bytes();testOriginal=(ROOT/TEST_PATH).read_bytes()
    payload=json.loads((ROOT/'Tools/InteractionRefinement/p3-output-20261008-payload.json').read_text(encoding='utf-8'))
    text=original.decode('utf-8').replace('\r\n','\n')
    for old,new in payload['patches']:
        assert text.count(old)==1,old[:100]
        text=text.replace(old,new,1)
    candidate=text.encode('utf-8')
    frozen=original.decode('utf-8').replace('\r\n','\n')
    frozen=frozen[frozen.index('namespace MassEngine'):].replace('TerrainNavigationGrid','P3OriginalNavigationGrid')
    tests=testOriginal.decode('utf-8')+'\n'+frozen+'\n'+payload['tests']
    OUT.mkdir()
    (OUT/'reference.cs.txt').write_bytes(original);(OUT/'candidate.cs.txt').write_bytes(candidate);(OUT/'test-original.cs.txt').write_bytes(testOriginal)
    registry={'variants':{'reference':hashlib.sha256(original).hexdigest(),'candidate':hashlib.sha256(candidate).hexdigest()},'testSha256':hashlib.sha256(tests.encode('utf-8')).hexdigest(),'testOriginalSha256':hashlib.sha256(testOriginal).hexdigest()}
    save(OUT/'registry.json',registry)
    save(OUT/'initial.json',{'project':check,'gitHead':subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip(),'index':sha(ROOT/'.git/index') if (ROOT/'.git/index').is_file() else None})
    save(OUT/'scope.json',{'localGate':'All 6 saved inputs exact bits; 14 new test cases pass; baseline current/reference ratio in [0.85,1.15]; candidate ratio<=0.90 for every input.','renderGate':'All 4 ABBA rounds valid and same settings; both pairs P99 at least 10% lower and >50ms counts at least 50% lower; ordinary median at most 10% worse. GPU scoped regression passes. Failure restores reference.','caveat':'Candidate adoption means bounded improvement only, not P3 closure or EXE/human qualification. No lower refresh rate, no changed Dijkstra, no cardinal-route-cache reuse.','memory':'One optional 9-element Vector2 array per uniform grid (72 payload bytes plus array header). Nonuniform float spacing falls back to original output arithmetic.'})
    (ROOT/TEST_PATH).write_text(tests,encoding='utf-8')
    for name,path,oldImport in [('test_runner.py','run_p8.py','from common_p8 import *'),('rendered_runner.py','run_p3_rendered.py','from common_p3 import *')]:
        text=(ROOT/'Tools/InteractionRefinement'/path).read_text(encoding='utf-8')
        assert text.count(oldImport)==1
        text=text.replace(oldImport,'from p3_output_20261008 import *')
        text=protect_external_restore(text)
        if name=='test_runner.py':
            text=text.replace("filters={'scoped-kernels'","filters={'output-fixed':'MassEngine.Tests.P3UniformOutputTests;MassEngine.Tests.TerrainNavigationGridTests;MassEngine.Tests.TerrainCardinalRouteCacheTests;MassEngine.Tests.LocalOrderPlanTests','scoped-kernels'")
        ast.parse(text);(OUT/name).write_text(text,encoding='utf-8')
    state={'status':'running','adopted':False,'P3Closed':False,'steps':[]};save(OUT/'decision.json',state)
    try:
        before=run('editmode','p3-output-before-01',['output-fixed']);state['before']=analyze_fixed(before)
        assert all(.85<=r['ratio']<=1.15 for r in state['before']),'Original/reference calibration outside noise bound'
        tree=ET.parse(before/'results.xml').getroot();new=[n for n in tree.iter('test-case') if 'P3UniformOutputTests.' in n.get('fullname','')]
        assert len(new)==14 and all(n.get('result')=='Passed' for n in new)
        state['steps'].append('baseline_equivalence_14_cases');save(OUT/'decision.json',state)
        select('candidate')
        after=run('editmode','p3-output-after-01',['output-fixed']);state['after']=analyze_fixed(after)
        assert all(r['uniformTableActive'] and r['ratio']<=.90 for r in state['after']),'No sufficient fixed-input benefit; no rendered queue'
        state['steps'].append('candidate_exact_and_local_gain');save(OUT/'decision.json',state)
        run('playmode','p3-output-scoped-01',['scoped-scene','gui']);state['steps'].append('scoped_gpu_passed');save(OUT/'decision.json',state)
        state['rendered']=[]
        for i,variant in enumerate(['reference','candidate','candidate','reference']):
            select(variant)
            folder=run('performance','p3-output-abba-'+str(i+1),['gui','48'])
            state['rendered'].append(analyze_render(folder));save(OUT/'decision.json',state)
        a,b,c,d=state['rendered'];assert all(r['conditions']==a['conditions'] for r in state['rendered'])
        pairs=[(a,b),(d,c)]
        state['renderGatePassed']=all(y['p99Ms']<=x['p99Ms']*.90 and y['over50ms']<=x['over50ms']*.50 and y['medianMs']<=x['medianMs']*1.10 for x,y in pairs)
        state['adopted']=state['renderGatePassed']
        state['status']='bounded_improvement_adopted_P3_still_open' if state['adopted'] else 'candidate_reverted_insufficient_rendered_gain'
        select('candidate' if state['adopted'] else 'reference')
    except Exception as e:
        state['status']='blocked_or_failed_reference_required';state['error']=str(e)
        print('ERROR',str(e),flush=True)
    finally:
        if not state['adopted']:
            if not processes() and not(ROOT/'Temp/UnityLockfile').exists() and sha(ROOT/NAV_PATH) in registry['variants'].values():
                (ROOT/NAV_PATH).write_bytes(original)
                state['referenceRestored']=True
            else:state['referenceRestored']=False
        state['finalProject']=project_check(full=True);state['userData']=user_check()
        initial=json.loads((OUT/'initial.json').read_text(encoding='utf-8'))
        state['headUnchanged']=subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip()==initial['gitHead']
        state['indexUnchanged']=(sha(ROOT/'.git/index') if (ROOT/'.git/index').is_file() else None)==initial['index']
        build=ROOT/'Builds/CaptureDefault-20261006-37';manifest=json.loads((BASE/'current37.sha256.json').read_text(encoding='utf-8'))
        changed=[r['path'] for r in manifest if not (build/r['path']).is_file() or sha(build/r['path'])!=r['sha256']]
        unexpected=sorted({p.relative_to(build).as_posix() for p in build.rglob('*') if p.is_file()}-{r['path'] for r in manifest})
        state['build37']={'files':len(manifest),'changed':changed,'unexpectedNew':unexpected,'passed':not changed and not unexpected}
        state['activeProcesses']=processes();state['projectLock']=(ROOT/'Temp/UnityLockfile').exists()
        state['protectionPassed']=state['finalProject']['passed'] and state['userData']['passed'] and state['build37']['passed'] and state['headUnchanged'] and state['indexUnchanged'] and not state['activeProcesses'] and not state['projectLock']
        save(OUT/'decision.json',state)
        print('FINAL',json.dumps(state,ensure_ascii=False,indent=2),flush=True)
    if 'error' in state or not state['protectionPassed']:sys.exit(1)

if __name__=='__main__':main()
