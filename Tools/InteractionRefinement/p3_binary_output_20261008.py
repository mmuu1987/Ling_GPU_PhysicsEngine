"""P3 direction-output candidate, gated fixed-input equivalence then rendered confirmation."""
from p3_cardinal_followup import *
from p3_cardinal_followup import project_check as handoff_check
from common_p8 import project_check as p8_check
import ast, xml.etree.ElementTree as ET

OUT=LOG/'P3'/'binary-output-20261008-01'
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
    from p3_output_20261008 import project_check as previous_check
    sys.stdout.reconfigure(encoding='utf-8')
    assert not OUT.exists()
    print('PREFLIGHT cached-priority binary heap/output candidate',flush=True)
    assert not processes() and not(ROOT/'Temp/UnityLockfile').exists()
    check=previous_check(full=True);assert check['passed'] and check['outputVariant']=='reference',check
    assert user_check()['passed']
    previous=LOG/'P3'/'output-20261008-01'
    previousDecision=json.loads((previous/'decision.json').read_text(encoding='utf-8'))
    assert previousDecision['protectionPassed'] and previousDecision['referenceRestored']
    original=(ROOT/NAV_PATH).read_bytes()
    assert original==(previous/'reference.cs.txt').read_bytes()
    text=(previous/'candidate.cs.txt').read_text(encoding='utf-8')
    patch=json.loads((ROOT/'Tools/InteractionRefinement/p3-binary-output-20261008-payload.json').read_text(encoding='utf-8'))
    for a,b in patch['patches']:
        assert text.count(a)==1,a
        text=text.replace(a,b,1)
    candidate=text.encode('utf-8')
    OUT.mkdir()
    (OUT/'reference.cs.txt').write_bytes(original);(OUT/'candidate.cs.txt').write_bytes(candidate)
    registry={'variants':{'reference':hashlib.sha256(original).hexdigest(),'candidate':hashlib.sha256(candidate).hexdigest()},'testSha256':sha(ROOT/TEST_PATH)}
    save(OUT/'registry.json',registry)
    save(OUT/'initial.json',{'project':check,'gitHead':subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip(),'index':sha(ROOT/'.git/index') if (ROOT/'.git/index').is_file() else None})
    save(OUT/'scope.json',{'candidate':'Binary indexed heap with cached priorities during sifts and explicit unchanged distance/index total order, plus exact uniform output table. Based on completed operation-count and trace analysis; no costs, target order, relaxation, refresh-rate or cardinal-route changes.', 'localGate':'All 91 targeted tests and six exact recorded outputs pass. Each current/reference ratio at least 5% better than corresponding previous output-only ratio; clock blocks not pooled.', 'renderGate':'Both bracketed ABBA pairs P99 at least 10% lower; >50ms at least 50% fewer; ordinary median no more than 10% slower. P8 six GPU checks pass. P3 still not closed by gate.', 'unchangedTestOracle':'Existing frozen original, saved graph and inputs, and 14 output exactness cases; original before-test calibration reused, not reported as new.'})
    for name in ['test_runner.py','rendered_runner.py']:
        text=(previous/name).read_text(encoding='utf-8').replace('from p3_output_20261008 import *','from p3_binary_output_20261008 import *')
        ast.parse(text);(OUT/name).write_text(text,encoding='utf-8')
    state={'status':'running','adopted':False,'P3Closed':False,'steps':[]};save(OUT/'decision.json',state)
    try:
        state['baselineEvidence']='output-20261008-01; reused, not rerun'
        select('candidate')
        after=run('editmode','p3-binary-output-after-01',['output-fixed']);state['after']=analyze_fixed(after)
        assert all(r['uniformTableActive'] and r['ratio']<=previousDecision['after'][i]['ratio']*.95 for i,r in enumerate(state['after'])),'No sufficient fixed-input benefit; no rendered queue'
        state['steps'].append('candidate_exact_and_local_gain');save(OUT/'decision.json',state)
        run('playmode','p3-binary-output-scoped-01',['scoped-scene','gui']);state['steps'].append('scoped_gpu_passed');save(OUT/'decision.json',state)
        state['rendered']=[]
        for i,variant in enumerate(['reference','candidate','candidate','reference']):
            select(variant)
            folder=run('performance','p3-binary-output-abba-'+str(i+1),['gui','48'])
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
