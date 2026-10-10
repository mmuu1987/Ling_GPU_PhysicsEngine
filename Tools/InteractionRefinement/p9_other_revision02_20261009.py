from pathlib import Path
import json,hashlib,subprocess,sys
R=Path(__file__).resolve().parents[2];D=R/'outputs/P9ColdFix-20261009-01';P=R/'outputs/P12'
assert json.loads((D/'baseline-other-01.json').read_text(encoding='utf-8'))['passed']
assert subprocess.check_output(['powershell','-NoProfile','-Command',"@(Get-Process Unity,WarSandbox -ErrorAction SilentlyContinue).Count"],text=True).strip()=='0'
m=json.loads((D/'prototype-harness.json').read_text(encoding='utf-8'));f='Assets/MassEngine/Tests/PlayMode/MassEngineGpuKernelTests.P9Other.cs';p=P/f
assert hashlib.sha256(p.read_bytes()).hexdigest()==m[f]
backup=D/'prototype-harness-before-other-revision02.json';assert not backup.exists();backup.write_bytes((D/'prototype-harness.json').read_bytes())
p.write_bytes((R/'Tools/InteractionRefinement/p9_other_fixture_revision02_20261009.cs.txt').read_bytes());m[f]=hashlib.sha256(p.read_bytes()).hexdigest();(D/'prototype-harness.json').write_text(json.dumps(m,indent=2),encoding='utf-8');(D/'prototype-other-fixture-revision02.cs.txt').write_bytes(p.read_bytes())
code=subprocess.call([sys.executable,str(R/'Tools/InteractionRefinement/p9_fix_run_v5_20261009.py'),'baseline-other-02'],cwd=R)
if code:sys.exit(code)
sys.exit(subprocess.call([sys.executable,str(R/'Tools/InteractionRefinement/p9_shader_specialization_v13_20261009.py')],cwd=R))
