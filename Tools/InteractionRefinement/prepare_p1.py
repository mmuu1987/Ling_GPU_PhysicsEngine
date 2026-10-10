from common_p0 import *
import sys,shutil
sys.stdout.reconfigure(encoding='utf-8')
P1=LOG/'P1';assert not(P1/'baseline.json').exists(),'Never replace P1 baseline'
assert not processes() and not(ROOT/'Temp/UnityLockfile').exists()
check=project_check(full=True);assert check['passed'],check
assert user_check()['passed']
last=json.loads((P0/'post-repair-audit-01.json').read_text(encoding='utf-8'))
assert all(sha(ROOT/r['path'])==r['sha256'] for r in last['ownedFiles']),'P0-owned files changed since final audit'
original=json.loads((BASE/'project-files.sha256.json').read_text(encoding='utf-8'));approved=json.loads((P0/'approved-source-changes.json').read_text(encoding='utf-8'))
expected={r['path']:approved.get(r['path'],{}).get('expectedSha256',r['sha256']) for r in original}
expected.update({r['path']:r['sha256'] for r in last['ownedFiles']})
save(P1/'baseline.json',{'createdAt':datetime.datetime.now().isoformat(),'files':expected,'p0Protection':check,'p0Report':'Docs/InteractionRefinement-20261007/P0-REPORT.md','p0HumanAcceptance':False})
planned=['Assets/Game/Scripts/'+n for n in ['UnitModelPreviewRenderer.cs','UnitModelPreviewWidget.cs','WarSandboxFrontEnd.UnitLibrary.cs','WarSandboxFrontEnd.UnitPreview.cs']]+['Assets/Game/Tests/PlayMode/'+n for n in ['UnitModelPreviewPlayModeTests.cs','UnitPreviewCameraParityTests.cs']]
for rel in planned:
 p=P1/'backups'/rel;p.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(ROOT/rel,p);assert sha(p)==expected[rel]
save(P1/'intent.json',{'modified':planned,'scope':'Read-only template radius, actual-anchor world-space preview contact ring, camera fit, diagnostics and tests only. No battle/config/persistence modifications.','knownMismatch':'ordinary separation and static obstacle radii do not share contact/projectile scale/clamp rules; no universal effective radius claim','p2Allowed':False})
save(P1/'approved-changes.json',{});print('P1 BASELINE SAVED',len(expected),'files;',len(planned),'backups',flush=True)
