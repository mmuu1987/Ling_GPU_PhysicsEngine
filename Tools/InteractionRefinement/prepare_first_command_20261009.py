from pathlib import Path
import json,hashlib,subprocess,datetime
R=Path(__file__).resolve().parents[2];P=R/'outputs/P12';Q=R/'outputs/P9FirstCommand-20261009-01';D=R/'outputs/P9ColdFix-20261009-01';O=R/'outputs/P9Combined-20261009-01'
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
assert subprocess.check_output(['powershell','-NoProfile','-Command',"@(Get-Process Unity,WarSandbox -ErrorAction SilentlyContinue).Count"],text=True).strip()=='0'
assert not Q.exists()
src=json.loads((O/'source-manifest.json').read_text(encoding='utf-8'))['files'];r03=json.loads((D/'patch-manifest.json').read_text(encoding='utf-8'));assert r03['revision']==3
assert all(sha(P/f)==v for f,v in r03['files'].items())
payload=json.loads((R/'Tools/InteractionRefinement/first-command-payload.json').read_text(encoding='utf-8'));production=[f for f in payload if not f.startswith('Assets/Game/Tests/')]
assert len(production)==2
for f in production:assert sha(P/f)==src[f]['sha256'],f
for f in payload:
 if f not in production:assert not(P/f).exists(),f
Q.mkdir();(Q/'r03-manifest.json').write_bytes((D/'patch-manifest.json').read_bytes());(Q/'NEXT_TASK-before.md').write_bytes((R/'NEXT_TASK.md').read_bytes())
before={}
for f in production:
 p=Q/'baseline'/f;p.parent.mkdir(parents=True,exist_ok=True);p.write_bytes((P/f).read_bytes());before[f]=sha(P/f)
(Q/'baseline-files.json').write_text(json.dumps(before,indent=2),encoding='utf-8')
for f,text in payload.items():
 p=P/f;p.parent.mkdir(parents=True,exist_ok=True);p.write_bytes(text.encode('utf-8'))
files=dict(r03['files']);files.update({f:sha(P/f) for f in production})
manifest={'name':'first-command-navigation-sharing-candidate-01','baseline':'r03','files':files,'additionalModifiedFiles':production,'scope':'Exact matching immutable topology reuse with private solve workspaces. No shader edits or prewarming. Residual cold shader latency NOT fixed by this patch.','accepted':False}
(Q/'candidate-manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf-8')
(Q/'harness-manifest.json').write_text(json.dumps({f:sha(P/f) for f in payload if f not in production},indent=2),encoding='utf-8')
(Q/'prepared.json').write_text(json.dumps({'utc':datetime.datetime.now().isoformat(),'productionFilesChanged':2,'r03ShaderHashesUnchanged':all(sha(P/f)==v for f,v in r03['files'].items()),'runtimeTestsExecuted':False,'motherCodeWritten':False},indent=2),encoding='utf-8')
print('Candidate installed in P12 only; two production C# files, baseline bytes retained; r03 shader files unchanged.',flush=True)
