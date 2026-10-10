from pathlib import Path
import json,hashlib,subprocess,datetime
R=Path(__file__).resolve().parents[2];D=R/'outputs/P9ColdFix-20261009-01';P=R/'outputs/P12';O=R/'outputs/P9Combined-20261009-01'
assert subprocess.check_output(['powershell','-NoProfile','-Command',"@(Get-Process Unity,WarSandbox -ErrorAction SilentlyContinue).Count"],text=True).strip()=='0'
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
b=json.loads((O/'mother-before.json').read_text(encoding='utf-8'))
assert sha(R/'.git/index')==b['index'];assert subprocess.check_output(['git','rev-parse','HEAD'],cwd=R,text=True).strip()==b['head']
h=json.loads((D/'prototype-harness.json').read_text(encoding='utf-8'));assert len(h)==8
assert all(sha(P/f)==x for f,x in h.items())
source=json.loads((O/'source-manifest.json').read_text(encoding='utf-8'))['files']
prod={f:x['sha256'] for f,x in source.items() if Path(f).suffix in ['.cs','.asmdef','.compute','.hlsl','.shader'] or f.startswith('Packages/')}
assert all(sha(P/f)==x for f,x in prod.items())
state=json.loads((D/'progress.json').read_text(encoding='utf-8'))
state.update({'updated':datetime.datetime.now().isoformat(),'productionFixApplied':False,'candidateAccepted':False,'prototypeOnly':True,'testAssetCount':len(h),'allStartedRunsCompleted':True,'fullProductionHostPartitionImplemented':False,'fixedColdAcceptanceRun':False})
state['gpuTests']={}
for name,expected in [('baseline-regression-01',60),('baseline-parity-02',16),('baseline-partition-01',26)]:
 j=json.loads((D/(name+'.json')).read_text(encoding='utf-8'));assert j['passed'] and j['motherProtection']['passed'] and len(j['cases'])==expected
 state['gpuTests'][name]={'passed':expected,'failed':0,'durationSeconds':float(j['tests']['duration']),'motherProtectionPassed':True,'scope':'original baseline' if 'regression' in name else 'test-only prototype; not production cold acceptance'}
for name in ['compiler-screen-v9.json','compiler-specialization-v10.json','compiler-specialization-v11.json','compiler-specialization-v12.json']:
 for x in json.loads((D/name).read_text(encoding='utf-8')):
  assert x['hresult']==0
  state['screening'].append({'file':name,'name':x['name'],'seconds':x['seconds'],'bytes':x['bytes'],'sha256':x['sha256'],'requiresHostPartitionAndParityTests':True,'scope':'CPU-only; not Unity command latency'})
state['remaining']=['GPU parity for v12 Move/Hold/ranged','Further compile-cost reduction (ranged Attack CPU screening remains 17.91s)','Production-scale routing and lifetime integration','Real 2048 select4 mixed-command/mixed-type and charge coverage','Unity cold-cache command acceptance plus full regression','Warm planning ~333ms separate; Player/AOT and human acceptance remain open']
text=(R/'Tools/InteractionRefinement/p9_gpu_progress_20261009.md.txt').read_text(encoding='utf-8')
for filename in ['progress.json','PROGRESS.md']:
 p=D/filename;backup=D/('before-gpu-partition-'+filename);assert not backup.exists();backup.write_bytes(p.read_bytes())
n=R/'NEXT_TASK.md';backup=D/'NEXT_TASK-before-gpu-partition-progress.md';assert not backup.exists();backup.write_bytes(n.read_bytes());old=n.read_text(encoding='utf-8')
(D/'progress.json').write_text(json.dumps(state,ensure_ascii=False,indent=2),encoding='utf-8');(D/'PROGRESS.md').write_text(text,encoding='utf-8')
n.write_text('# 最新接力：42 项 GPU 原型验证通过；冷态 Attack 完整修复仍未验收\n\n'+text+'\n\n---\n## 历史接力（以下状态可能已过时，以本文最上方为准）\n'+old,encoding='utf-8')
assert sha(R/'.git/index')==b['index']
print(json.dumps({'recorded':True,'prototypeCasesPassed':42,'productionCodeFilesVerified':len(prod),'testAssetsVerified':len(h),'productionFixApplied':False,'candidateAccepted':False,'cpuMatrixV12':[{k:x[k] for k in ['name','seconds','bytes']} for x in json.loads((D/'compiler-specialization-v12.json').read_text(encoding='utf-8'))]},ensure_ascii=False,indent=2))
