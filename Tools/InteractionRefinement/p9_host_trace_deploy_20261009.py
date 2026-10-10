from pathlib import Path
import json,hashlib,subprocess,sys,uuid
R=Path(__file__).resolve().parents[2];P=R/'outputs/P12';D=R/'outputs/P9ColdFix-20261009-01'
assert subprocess.check_output(['powershell','-NoProfile','-Command',"@(Get-Process Unity,WarSandbox -ErrorAction SilentlyContinue).Count"],text=True).strip()=='0'
assert json.loads((D/'hostscene-01-owned-cleanup.json').read_text(encoding='utf-8'))['allUserFilesMatchOriginal']
h=json.loads((D/'prototype-harness.json').read_text(encoding='utf-8'));sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest();assert all(sha(P/f)==v for f,v in h.items())
f='Assets/Game/Tests/PlayMode/P9HostRoutingTests.cs';assert not(P/f).exists() and not(P/(f+'.meta')).exists()
(D/'prototype-harness-before-hosttrace.json').write_bytes((D/'prototype-harness.json').read_bytes())
(P/f).write_bytes((R/'Tools/InteractionRefinement/p9_host_routing_fixture_20261009.cs.txt').read_bytes());(P/(f+'.meta')).write_text('fileFormatVersion: 2\nguid: '+uuid.uuid4().hex+'\n',encoding='utf-8')
for name in [f,f+'.meta']:h[name]=sha(P/name)
(D/'prototype-harness.json').write_text(json.dumps(h,indent=2),encoding='utf-8')
sys.exit(subprocess.call([sys.executable,str(R/'Tools/InteractionRefinement/p9_fix_run_v11_20261009.py'),'fixed-hosttrace-01'],cwd=R))
