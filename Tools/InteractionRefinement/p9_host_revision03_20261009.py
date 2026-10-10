from pathlib import Path
import json,hashlib,subprocess,sys
R=Path(__file__).resolve().parents[2];P=R/'outputs/P12';D=R/'outputs/P9ColdFix-20261009-01';sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
assert subprocess.check_output(['powershell','-NoProfile','-Command',"@(Get-Process Unity,WarSandbox -ErrorAction SilentlyContinue).Count"],text=True).strip()=='0'
t=json.loads((D/'fixed-unified-01.json').read_text(encoding='utf-8'));assert t['passed'] and int(t['tests']['passed'])==144 and t['motherProtection']['passed']
m=json.loads((D/'patch-manifest.json').read_text(encoding='utf-8'));assert m['revision']==2 and all(sha(P/f)==v for f,v in m['files'].items())
archive=D/'host-candidate-revision02';archive.mkdir()
for f in m['files']:
 dest=archive/f;dest.parent.mkdir(parents=True,exist_ok=True);dest.write_bytes((P/f).read_bytes())
(archive/'patch-manifest.json').write_bytes((D/'patch-manifest.json').read_bytes())
f='Assets/MassEngine/Simulation/Shaders/AgentCombatSimulation.compute'
(P/f).write_bytes((R/'Tools/InteractionRefinement/p9_host_revision03_20261009.compute.txt').read_bytes());m['files'][f]=sha(P/f);m['revision']=3;m['revisionNote']='Typed local kernels only: resolve-before-integration helper plus one integration/store call. Legacy global entry remains unchanged. No additional buffers or GPU dispatches. 144 GPU prototype comparisons passed; real cold acceptance pending.'
(D/'patch-manifest.json').write_text(json.dumps(m,indent=2),encoding='utf-8')
runner=R/'Tools/InteractionRefinement/p9_fix_run_v13_20261009.py'
for name in ['fixed-membership-03','fixed-golden-02','fixed-regression-03','fixed-hosttrace-04']:
 code=subprocess.call([sys.executable,str(runner),name],cwd=R)
 if code:sys.exit(code)
code=subprocess.call([sys.executable,str(R/'Tools/InteractionRefinement/p9_host_r03_cold_aba_20261009.py')],cwd=R)
if code:sys.exit(code)
for name in ['fixed-joint-02','fixed-repeat-02']:
 code=subprocess.call([sys.executable,str(runner),name],cwd=R)
 if code:sys.exit(code)
