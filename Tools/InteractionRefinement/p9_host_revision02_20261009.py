from pathlib import Path
import json,hashlib,subprocess,sys
R=Path(__file__).resolve().parents[2];P=R/'outputs/P12';D=R/'outputs/P9ColdFix-20261009-01';sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
assert subprocess.check_output(['powershell','-NoProfile','-Command',"@(Get-Process Unity,WarSandbox -ErrorAction SilentlyContinue).Count"],text=True).strip()=='0'
assert json.loads((D/'fixed-repeat-01.json').read_text(encoding='utf-8'))['passed']
m=json.loads((D/'patch-manifest.json').read_text(encoding='utf-8'));h=json.loads((D/'prototype-harness.json').read_text(encoding='utf-8'));assert all(sha(P/f)==x for f,x in m['files'].items()) and all(sha(P/f)==x for f,x in h.items())
archive=D/'host-candidate-revision01';archive.mkdir()
for f in m['files']:
 dest=archive/f;dest.parent.mkdir(parents=True,exist_ok=True);dest.write_bytes((P/f).read_bytes())
(archive/'patch-manifest.json').write_bytes((D/'patch-manifest.json').read_bytes());(D/'prototype-harness-before-null-guard.json').write_bytes((D/'prototype-harness.json').read_bytes())
f='Assets/MassEngine/Core/MassGpuShaderSet.cs';s=(P/f).read_text(encoding='utf-8');old='get { for(int i=0;i<6;i++)if(LocalKernel(i)<0)return false;return true; }';assert s.count(old)==1
(P/f).write_text(s.replace(old,'get { if(CombatSimulationShader==null)return false;for(int i=0;i<6;i++)if(LocalKernel(i)<0)return false;return true; }'),encoding='utf-8');m['files'][f]=sha(P/f);m['revision']=2;m['revisionNote']='Null/default shader-set guard only; all compute/HLSL bytes unchanged from revision01.'
(D/'patch-manifest.json').write_text(json.dumps(m,indent=2),encoding='utf-8')
f='Assets/MassEngine/Tests/PlayMode/MassEngineGpuKernelTests.P9Membership.cs';(D/'membership-fixture-before-null-guard.cs.txt').write_bytes((P/f).read_bytes());(P/f).write_bytes((R/'Tools/InteractionRefinement/p9_membership_fixture_revision02_20261009.cs.txt').read_bytes());h[f]=sha(P/f);(D/'prototype-harness.json').write_text(json.dumps(h,indent=2),encoding='utf-8')
runner=R/'Tools/InteractionRefinement/p9_fix_run_v12_20261009.py'
for name in ['fixed-membership-02','fixed-regression-02','fixed-hosttrace-03']:
 code=subprocess.call([sys.executable,str(runner),name],cwd=R)
 if code:sys.exit(code)
