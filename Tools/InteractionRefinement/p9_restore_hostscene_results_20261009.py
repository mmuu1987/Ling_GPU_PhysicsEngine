from pathlib import Path
import json,hashlib,subprocess
R=Path(__file__).resolve().parents[2];D=R/'outputs/P9ColdFix-20261009-01';O=R/'outputs/P9Combined-20261009-01';V=R/'outputs/P12/Logs/InteractionRefinement-20261007/P8/cold-fix-fixed-hostscene-01'
assert subprocess.check_output(['powershell','-NoProfile','-Command',"@(Get-Process Unity,WarSandbox -ErrorAction SilentlyContinue).Count"],text=True).strip()=='0'
b=json.loads((O/'mother-before.json').read_text(encoding='utf-8'));s=json.loads((D/'fixed-hostscene-01.json').read_text(encoding='utf-8'));user=Path(b['userRoot']);sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
actual={p.relative_to(user).as_posix():sha(p) for p in user.rglob('*') if p.is_file()};diff=sorted(k for k in actual.keys()|b['userFiles'].keys() if actual.get(k)!=b['userFiles'].get(k));assert diff==['TestResults.xml'],diff
assert s['exitCode']==0 and s['tests']['passed']=='4' and len(s['cases'])==4 and all(c['name'].startswith('MassEngine.Game.Tests.LocalOrdersSceneTests.') and c['result']=='Passed' for c in s['cases'])
p=user/'TestResults.xml';assert p.read_bytes()==(V/'results.xml').read_bytes();before=V/'persistent-TestResults-before.xml';assert sha(before)==b['userFiles']['TestResults.xml']
archive=V/'persistent-TestResults-generated-after-owned-verification.xml';assert not archive.exists();archive.write_bytes(p.read_bytes());p.write_bytes(before.read_bytes())
assert {f.relative_to(user).as_posix():sha(f) for f in user.rglob('*') if f.is_file()}==b['userFiles']
report={'restoredOnly':'TestResults.xml','generatedContentExactlyMatchedOwnedResult':True,'classAndFourCasesVerified':True,'restoredSha256':sha(p),'allUserFilesMatchOriginal':True,'originalFailedRunRecordPreserved':True}
(D/'hostscene-01-owned-cleanup.json').write_text(json.dumps(report,indent=2),encoding='utf-8');print(json.dumps(report,indent=2))
