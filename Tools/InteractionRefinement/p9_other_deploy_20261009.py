from pathlib import Path
import json,hashlib,subprocess,sys,uuid
R=Path(__file__).resolve().parents[2];P=R/'outputs/P12';D=R/'outputs/P9ColdFix-20261009-01'
assert json.loads((D/'baseline-partition-01.json').read_text(encoding='utf-8'))['passed']
assert subprocess.check_output(['powershell','-NoProfile','-Command',"@(Get-Process Unity,WarSandbox -ErrorAction SilentlyContinue).Count"],text=True).strip()=='0'
m=json.loads((D/'prototype-harness.json').read_text(encoding='utf-8'))
for f,h in m.items():assert hashlib.sha256((P/f).read_bytes()).hexdigest()==h,f
files={}
for label in ['move-melee','hold-melee','attack-ranged','move-ranged','hold-ranged']:
 s=(D/'compiler-probes-v12-REQUIRES-HOST-SPLIT'/label/'expanded.compute').read_text(encoding='utf-8')
 s=s.replace('#pragma multi_compile __ MASS_TERRAIN_ENABLED','').replace('#pragma multi_compile_local __ MASS_LOCAL_ORDERS','')
 files['Assets/MassEngine/Tests/PlayMode/P9Other-'+label+'.compute']='// TEST-ONLY v12 specialization; no production integration.\n#define MASS_TERRAIN_ENABLED 1\n#define MASS_LOCAL_ORDERS 1\n'+s
files['Assets/MassEngine/Tests/PlayMode/MassEngineGpuKernelTests.P9Other.cs']=(R/'Tools/InteractionRefinement/p9_other_fixture_20261009.cs.txt').read_text(encoding='utf-8')
for f in list(files):files[f+'.meta']='fileFormatVersion: 2\nguid: '+uuid.uuid4().hex+'\n'
backup=D/'prototype-harness-before-other.json';assert not backup.exists()
for f in files:assert not(P/f).exists(),f
backup.write_bytes((D/'prototype-harness.json').read_bytes());archive=D/'prototype-other-original';archive.mkdir()
for f,text in files.items():
 (P/f).write_text(text,encoding='utf-8');m[f]=hashlib.sha256((P/f).read_bytes()).hexdigest();(archive/Path(f).name).write_bytes((P/f).read_bytes())
(D/'prototype-harness.json').write_text(json.dumps(m,indent=2),encoding='utf-8')
print('12 test-only assets added. 120 cases. No production changes.',flush=True)
sys.exit(subprocess.call([sys.executable,str(R/'Tools/InteractionRefinement/p9_fix_run_v5_20261009.py'),'baseline-other-01'],cwd=R))
