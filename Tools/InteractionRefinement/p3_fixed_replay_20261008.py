"""P3 fixed-input CPU diagnosis only. No optimization or build is applied."""
from p3_cardinal_followup import *
from p3_cardinal_followup import project_check as handoff_check
from common_p8 import project_check as base_p8_check
import ast, statistics

FIXED = LOG / 'P3' / 'fixed-input-20261008-01'
CAPTURE = LOG / 'P3' / 'fixed-input-20261008-01-capture'
PAYLOAD = ROOT / 'Tools/InteractionRefinement/p3-fixed-input-20261008-payload.json'
SOURCE_PATHS = ['Assets/MassEngine/Terrain/TerrainNavigationGrid.cs', 'Assets/MassEngine/Terrain/TerrainNavigationRuntime.cs', 'Assets/Game/Editor/InteractionRefinementP3Qualification.cs']

def project_check(full=False):
    r = base_p8_check(full)
    registry = json.loads((FIXED / 'registry.json').read_text(encoding='utf-8'))
    cardinal = json.loads((DEST / 'registry.json').read_text(encoding='utf-8'))
    expected = {**cardinal['tests'], **registry['currentExpected']}
    valid = all((ROOT/p).is_file() and sha(ROOT/p) == h for p,h in expected.items())
    if valid:
        r['changed'] = [x for x in r['changed'] if x['path'] not in registry['currentExpected']]
        r['unexpectedNew'] = [p for p in r['unexpectedNew'] if p not in cardinal['tests']]
    r['fixedInputDiagnosticHashesMatch'] = valid
    r['passed'] = valid and not r['changed'] and not r['unexpectedNew']
    return r

def quantile(a, q):
    a = sorted(a)
    return a[min(len(a)-1, math.ceil(len(a)*q)-1)]

def main():
    sys.stdout.reconfigure(encoding='utf-8')
    assert not FIXED.exists() and not CAPTURE.exists(), 'Never overwrite old evidence'
    print('PREFLIGHT: current handoff protection, per-project lock and user files', flush=True)
    assert not processes() and not (ROOT/'Temp/UnityLockfile').exists()
    initial = handoff_check(full=True)
    assert initial['passed'] and initial['cardinalVariant'] == 'reference', initial
    assert user_check()['passed']
    FIXED.mkdir()
    original = {p: (ROOT/p).read_bytes() for p in SOURCE_PATHS}
    payload = json.loads(PAYLOAD.read_text(encoding='utf-8'))
    assert set(payload) == set(SOURCE_PATHS)
    modified = {}
    for p,b in original.items():
        text = b.decode('utf-8').replace('\r\n','\n')
        for old,new in payload[p]:
            assert text.count(old) == 1, (p, old[:100])
            text = text.replace(old,new,1)
        modified[p] = text.encode('utf-8')
        for folder,data in [('original',b),('diagnostic',modified[p])]:
            q = FIXED/folder/p; q.parent.mkdir(parents=True,exist_ok=True); q.write_bytes(data)
    registry = {'original':{p:hashlib.sha256(b).hexdigest() for p,b in original.items()}, 'diagnostic':{p:hashlib.sha256(b).hexdigest() for p,b in modified.items()}}
    registry['currentExpected'] = registry['original']
    save(FIXED/'registry.json',registry)
    save(FIXED/'initial-protection.json',{'project':initial,'userData':user_check(),'gitHead':subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip(),'indexSha256':sha(ROOT/'.git/index') if (ROOT/'.git/index').is_file() else None})
    runner = (ROOT/'Tools/InteractionRefinement/run_p3_rendered.py').read_text(encoding='utf-8')
    assert runner.count('from common_p3 import *') == 1
    runner = runner.replace('from common_p3 import *','from p3_fixed_replay_20261008 import *')
    runner = protect_external_restore(runner)
    # Restoration of settings which did not exist before is explicit and restricted to this process's known settings.
    runner = runner.replace("saved={p:(ROOT/p).read_bytes() for p in auto if(ROOT/p).is_file()}", "missing_auto=[p for p in auto if not(ROOT/p).is_file()]\nsaved={p:(ROOT/p).read_bytes() for p in auto if(ROOT/p).is_file()}")
    marker="  receipt['restoredExternalEditorSettings']=[]"
    assert runner.count(marker)==1
    runner = runner.replace(marker,"  receipt['removedOriginallyAbsentOwnedSettings']=[]\n  for rel in missing_auto:\n   path=ROOT/rel\n   if path.is_file():\n    q=output/'auto-settings-generated'/rel;q.parent.mkdir(parents=True,exist_ok=True);q.write_bytes(path.read_bytes());path.unlink();receipt['removedOriginallyAbsentOwnedSettings'].append(rel)\n"+marker)
    ast.parse(runner)
    (FIXED/'capture_runner.py').write_text(runner,encoding='utf-8')
    state={'status':'prepared','scope':'Six live snapshots across both teams and three contact-time windows. 3 warmup pairs and 12 alternating original/instrumented pairs per fixed input. Not a candidate optimization, not a new rendered FPS qualification. Capture copy/I/O excluded from CPU replay timings.','runs':[]}
    save(FIXED/'summary.json',state)
    try:
        assert not processes() and not(ROOT/'Temp/UnityLockfile').exists()
        for p,b in original.items(): assert (ROOT/p).read_bytes()==b, 'Concurrent source changes'
        for p,b in modified.items(): (ROOT/p).write_bytes(b)
        registry['currentExpected']=registry['diagnostic']; save(FIXED/'registry.json',registry)
        state['status']='capturing_then_replaying';save(FIXED/'summary.json',state)
        env=os.environ.copy();env['PYTHONPATH']=str(ROOT/'Tools/InteractionRefinement')+os.pathsep+env.get('PYTHONPATH','')
        print('CAPTURE: owned Unity; 2048, 48, live target snapshots; fixed-input replay at end',flush=True)
        with (FIXED/'capture-runner.log').open('w',encoding='utf-8') as f:
            result=subprocess.run([sys.executable,'-X','utf8',str(FIXED/'capture_runner.py'),'performance',CAPTURE.name,'gui','48'],cwd=ROOT,env=env,stdout=f,stderr=subprocess.STDOUT)
        state['captureExitCode']=result.returncode
        assert result.returncode==0,'Capture runner failed; preserve logs and stop'
        assert (CAPTURE/'fixed-replay-complete.txt').is_file(),'Replay completion marker missing'
        index=json.loads((CAPTURE/'fixed-input-index.json').read_text(encoding='utf-8'))
        assert len(index['samples'])==6
        assert len({r['team'] for r in index['samples']})==2
        assert {r['window'] for r in index['samples']}=={0,1,2}
        for item in index['samples']:
            report=json.loads((CAPTURE/(item['prefix']+'-replay.json')).read_text(encoding='utf-8'))
            assert report['capturedOutputMatches'] and report['allMeasuredOutputsMatch']
            rows=report['rows'];assert len(rows)==12
            metrics=['baselineMs','timedMs','resetMs','seedMs','traversalMs','outputMs','baselineAllocatedBytes','timedAllocatedBytes']
            medians={k:statistics.median(r[k] for r in rows) for k in metrics}
            state['runs'].append({'input':item,'exactOutput':True,'pairs':12,'medians':medians,'p95Ms':{k:quantile([r[k] for r in rows],.95) for k in metrics[:6]},'timedToBaselineMedianRatio':medians['timedMs']/medians['baselineMs'],'collectionCounts':{k:sum(r[k] for r in rows) for k in ['gen0Collections','gen1Collections','gen2Collections']}})
        state['artifactHashes']={p.name:sha(p) for p in CAPTURE.iterdir() if p.is_file() and p.name.startswith('fixed-')}
        state['status']='fixed_input_analysis_complete_no_optimization_applied'
        save(FIXED/'summary.json',state)
    except Exception as e:
        state['status']='blocked_or_failed';state['error']=str(e)
        raise
    finally:
        state['restoredSources']=[]
        if not processes() and not(ROOT/'Temp/UnityLockfile').exists():
            for p,b in modified.items():
                if (ROOT/p).read_bytes()==b:
                    (ROOT/p).write_bytes(original[p]);state['restoredSources'].append(p)
        if len(state['restoredSources'])==len(SOURCE_PATHS):
            registry['currentExpected']=registry['original'];save(FIXED/'registry.json',registry)
        state['finalProject']=handoff_check(full=True)
        state['finalUserData']=user_check()
        initialProtection=json.loads((FIXED/'initial-protection.json').read_text(encoding='utf-8'))
        state['headUnchanged']=subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip()==initialProtection['gitHead']
        state['indexUnchanged']=(sha(ROOT/'.git/index') if (ROOT/'.git/index').is_file() else None)==initialProtection['indexSha256']
        state['protectionPassed']=len(state['restoredSources'])==3 and state['finalProject']['passed'] and state['finalUserData']['passed'] and state['headUnchanged'] and state['indexUnchanged']
        save(FIXED/'summary.json',state)
        lines=['# P3 CreateFlowField 固定输入分析','', '状态：'+state['status'], '', '采集当前正式Green场景2048人、分离48；六份真实CPU目标快照，原始图边/代价与输入均保存为二进制。重放不包括Lane或GPU上传，不将结果当作实战帧率。', '', '## 每份输入的12对测量中位数（毫秒）', '', '|样本|目标数|原实现|计时版|分配/重置|目标入堆|遍历|方向输出|逐位对拍|','|---|---:|---:|---:|---:|---:|---:|---:|---|']
        for r in state['runs']:
            m=r['medians'];lines.append('|'+r['input']['prefix']+'|'+str(r['input']['targetCount'])+'|'+'|'.join(format(m[k],'.4f') for k in ['baselineMs','timedMs','resetMs','seedMs','traversalMs','outputMs'])+'|通过|')
        lines += ['', '保护检查：'+str(state['protectionPassed']), '', '本轮没有优化候选或生产净改动。分配字节与GC代数次数分别报告；没有测得GC暂停时长。分段时钟有观测开销，12对交错原实现/计时版用于展示扰动，不作显著性承诺。', '', '完整证据：Logs/InteractionRefinement-20261007/P3/fixed-input-20261008-01/summary.json；原始输入与逐对计时在同级fixed-input-20261008-01-capture。']
        if 'error' in state:lines += ['', '失败：'+state['error']]
        doc=ROOT/'Docs/InteractionRefinement-20261007/P3-FIXED-INPUT-20261008.md'
        assert not doc.exists(),'Do not overwrite report'
        doc.write_text('\n'.join(lines)+'\n',encoding='utf-8')
        print(json.dumps(state,ensure_ascii=False,indent=2),flush=True)
    assert state['protectionPassed']

if __name__=='__main__':main()
