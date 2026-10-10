from pathlib import Path
import json,hashlib,subprocess,sys
R=Path(__file__).resolve().parents[2];D=R/'outputs/P9ColdFix-20261009-01';P=R/'outputs/P12'
assert subprocess.check_output(['powershell','-NoProfile','-Command',"@(Get-Process Unity,WarSandbox -ErrorAction SilentlyContinue).Count"],text=True).strip()=='0'
f='Assets/MassEngine/Tests/PlayMode/MassEngineGpuKernelTests.P9Specialization.cs';p=P/f;m=json.loads((D/'prototype-harness.json').read_text(encoding='utf-8'));assert hashlib.sha256(p.read_bytes()).hexdigest()==m[f]
s=p.read_text(encoding='utf-8');assert s.count('buffers.combatBuffers.hpBuffer')==1
backup=D/'prototype-harness-before-fixture-fix.json';assert not backup.exists();backup.write_bytes((D/'prototype-harness.json').read_bytes())
p.write_text(s.replace('buffers.combatBuffers.hpBuffer','buffers.combatBuffers.hpWriteBuffer'),encoding='utf-8');m[f]=hashlib.sha256(p.read_bytes()).hexdigest();(D/'prototype-harness.json').write_text(json.dumps(m,indent=2),encoding='utf-8');(D/'prototype-fixture-revision-02.cs.txt').write_bytes(p.read_bytes())
print('Corrected test-side write-buffer field only. No production changes.',flush=True)
sys.exit(subprocess.call([sys.executable,str(R/'Tools/InteractionRefinement/p9_fix_run_v3_20261009.py'),'baseline-parity-02'],cwd=R))
