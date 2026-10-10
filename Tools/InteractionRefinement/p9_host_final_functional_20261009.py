from pathlib import Path
import json,hashlib,subprocess,sys
R=Path(__file__).resolve().parents[2];P=R/'outputs/P12';D=R/'outputs/P9ColdFix-20261009-01';sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
assert subprocess.check_output(['powershell','-NoProfile','-Command',"@(Get-Process Unity,WarSandbox -ErrorAction SilentlyContinue).Count"],text=True).strip()=='0'
assert json.loads((D/'host-cache-aba.json').read_text(encoding='utf-8'))['originalStillExactAfterRestoredRun']
h=json.loads((D/'prototype-harness.json').read_text(encoding='utf-8'));f='Assets/Game/Tests/PlayMode/P9HostRoutingTests.cs';assert sha(P/f)==h[f]
backup=D/'prototype-harness-before-hosttrace-revision02.json';assert not backup.exists();backup.write_bytes((D/'prototype-harness.json').read_bytes());(D/'hosttrace-fixture-revision01.cs.txt').write_bytes((P/f).read_bytes())
(P/f).write_bytes((R/'Tools/InteractionRefinement/p9_host_routing_fixture_revision02_20261009.cs.txt').read_bytes());h[f]=sha(P/f);(D/'prototype-harness.json').write_text(json.dumps(h,indent=2),encoding='utf-8')
runner=R/'Tools/InteractionRefinement/p9_fix_run_v11_20261009.py'
for name in ['fixed-hosttrace-02','fixed-joint-01','fixed-repeat-01']:
 code=subprocess.call([sys.executable,str(runner),name],cwd=R)
 if code:sys.exit(code)
