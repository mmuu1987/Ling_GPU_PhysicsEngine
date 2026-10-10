from pathlib import Path
import json,hashlib,subprocess,sys,uuid
R=Path(__file__).resolve().parents[2];P=R/'outputs/P12';D=R/'outputs/P9ColdFix-20261009-01';O=R/'outputs/P9Combined-20261009-01'
assert subprocess.check_output(['powershell','-NoProfile','-Command',"@(Get-Process Unity,WarSandbox -ErrorAction SilentlyContinue).Count"],text=True).strip()=='0'
assert json.loads((D/'baseline-optimized-03.json').read_text(encoding='utf-8'))['passed']
source=json.loads((O/'source-manifest.json').read_text(encoding='utf-8'))['files'];sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
assert not(D/'patch-manifest.json').exists()
stage=R/'Tools/InteractionRefinement/host-patch-01'
package={f.relative_to(stage).as_posix():f.read_text(encoding='utf-8') for f in stage.rglob('*') if f.is_file()}
for f in package:
 if f in source:assert sha(P/f)==source[f]['sha256'],f
 else:assert not(P/f).exists(),f
h=json.loads((D/'prototype-harness.json').read_text(encoding='utf-8'));assert all(sha(P/f)==v for f,v in h.items())
fixtures=json.loads((R/'Tools/InteractionRefinement/p9_host_test_payload_20261009.json').read_text(encoding='utf-8'))
for f in list(fixtures):fixtures[f+'.meta']='fileFormatVersion: 2\nguid: '+uuid.uuid4().hex+'\n'
for f in fixtures:assert not(P/f).exists(),f
archive=D/'host-original-01';archive.mkdir()
for f in package:
 if (P/f).exists():p=archive/f;p.parent.mkdir(parents=True,exist_ok=True);p.write_bytes((P/f).read_bytes())
(D/'prototype-harness-before-host.json').write_bytes((D/'prototype-harness.json').read_bytes())
for f,t in package.items():(P/f).write_text(t,encoding='utf-8')
manifest={'scope':'P12 only; first production host candidate; NOT accepted','files':{f:sha(P/f) for f in package},'originalFiles':{f:source[f]['sha256'] for f in package if f in source},'newFiles':[f for f in package if f not in source]}
(D/'patch-manifest.json').write_text(json.dumps(manifest,indent=2),encoding='utf-8')
for f,t in fixtures.items():(P/f).write_text(t,encoding='utf-8');h[f]=sha(P/f)
(D/'prototype-harness.json').write_text(json.dumps(h,indent=2),encoding='utf-8')
print('P12 host candidate installed: '+str(len(package))+' declared production files; mother untouched.',flush=True)
runner=R/'Tools/InteractionRefinement/p9_fix_run_v9_20261009.py'
for name in ['fixed-membership-01','fixed-golden-01','fixed-regression-01']:
 code=subprocess.call([sys.executable,str(runner),name],cwd=R)
 if code:sys.exit(code)
