from pathlib import Path
import json,hashlib,zipfile,difflib,sys,subprocess
sys.stdout.reconfigure(encoding='utf-8')
R=Path(__file__).resolve().parents[2];W=R/'outputs/BurstDependencyRepair-20261009-01';P=R/'outputs/B9'
summary=json.loads((W/'validation-01/summary.json').read_text(encoding='utf-8'))
assert summary['status']=='blocked' and summary['motherProtection']['passed'],'Wait for first isolated attempt to exit and protection check'
q=subprocess.run(['powershell','-NoProfile','-Command',"Get-CimInstance Win32_Process -Filter \"Name = 'Unity.exe'\" | Select-Object ProcessId,CommandLine | ConvertTo-Json -Compress"],capture_output=True,text=True,check=True)
rows=json.loads(q.stdout) if q.stdout.strip() else [];rows=[rows] if isinstance(rows,dict) else rows
assert all(str(P).replace('\\','/').lower() not in (x.get('CommandLine') or '').replace('\\','/').lower() for x in rows),'Do not edit an active project'
if (P/'Temp/UnityLockfile').exists():
 assert (W/'validation-01/owned-cancellation.json').exists()
 (P/'Temp/UnityLockfile').rename(W/'validation-01/owned-stale-UnityLockfile')
source=(R/'Tools/InteractionRefinement/P3BurstPreparationTests-20261008.cs.txt').read_text(encoding='utf-8-sig')
marker='namespace MassEngine.Tests {\n public sealed partial class P3BurstPreparationTests'
assert source.count(marker)==2
text='using System;\nusing NUnit.Framework;\n'+source[source.rindex(marker):]
text=text.replace('public sealed partial class P3BurstPreparationTests','public sealed class BurstDependencyRepairPolicyTests')
assert 'fixed-sample' not in text and text.count('class BurstDependencyRepairPolicyTests')==1
path='Assets/MassEngine/Tests/EditMode/BurstDependencyRepairPolicyTests.cs';old=(P/path).read_bytes();new=text.encode()
(P/path).write_bytes(new);(W/'repair-files'/path).write_bytes(new)
m=json.loads((W/'repair-manifest.json').read_text(encoding='utf-8'))
m['changes'][path]['newSha']=hashlib.sha256(new).hexdigest()
m['changes'][path]['source']='Only the second independent state-machine block; first block historical capture/benchmark fixture excluded'
m['fixtureRevision']='Initial extraction matched first of two partial class blocks; fixed extraction to final block. No product changes.'
(W/'repair-manifest.json').write_text(json.dumps(m,ensure_ascii=False,indent=2),encoding='utf-8')
patch=''
with zipfile.ZipFile(W/'committed-source.zip') as z:
 names=set(z.namelist())
 for path in m['changes']:
  a=z.read(path).decode('utf-8-sig').splitlines(True) if path in names else []
  b=(W/'repair-files'/path).read_text(encoding='utf-8-sig').splitlines(True)
  patch+=''.join(difflib.unified_diff(a,b,fromfile='a/'+path if path in names else '/dev/null',tofile='b/'+path))
(W/'dependency-repair.patch').write_text(patch,encoding='utf-8')
(W/'fixture-repair.json').write_text(json.dumps({'oldSha':hashlib.sha256(old).hexdigest(),'newSha':hashlib.sha256(new).hexdigest(),'reason':'Fixture extraction, not runtime defect; original failure kept in validation-01'},indent=2))
print('ISOLATED FIXTURE CORRECTED; product code unchanged')
