from common_p7 import *
import sys,hashlib,difflib,zipfile
sys.stdout.reconfigure(encoding='utf-8');tag=sys.argv[1];assert tag.replace('-','').isalnum();out=P7/'changes'/tag;assert not out.exists()
assert not processes() and not(ROOT/'Temp/UnityLockfile').exists();assert project_check()['passed'] and user_check()['passed']
payload=json.loads((ROOT/('Tools/InteractionRefinement/p7-'+tag+'.json')).read_text(encoding='utf-8'));allowed={'Assets/Game/Scripts/MemberSelectionState.cs.meta', 'Assets/Game/Scripts/WarSandboxCommandHUD.Selection.cs', 'Assets/Game/Scripts/WarSandboxCommandHUD.Details26.cs', 'Assets/Game/Tests/PlayMode/DeploymentTranslationPlayModeTests.cs', 'Assets/Game/Resources/MemberSelectionBuffers.hlsl.meta', 'Assets/Game/Editor/InteractionRefinementP7.cs', 'Assets/Game/Tests/EditMode/MemberSelectionTests.cs', 'Assets/Game/Editor/InteractionRefinementP7.cs.meta', 'Assets/Game/Scripts/WarSandboxCommandHUD.Selection.cs.meta', 'Assets/Game/Scripts/WarSandboxCommandHUD.cs', 'Assets/Game/Resources/MemberSelection.compute', 'Assets/Game/Scripts/GpuMemberSelection.cs.meta', 'Assets/Game/Scripts/MemberSelectionState.cs', 'Assets/Game/Resources/MemberSelectionRings.shader.meta', 'Assets/Game/Scripts/WarSandboxCommandHUD.Commands25.cs', 'Assets/Game/Resources/MemberSelection.compute.meta', 'Assets/Game/Resources/MemberSelectionRings.shader', 'Assets/Game/Resources/MemberSelectionBuffers.hlsl', 'Assets/MassEngine/Tests/PlayMode/MassEngineGpuKernelTests.P6.cs', 'Assets/Game/Tests/PlayMode/LocalOrdersSceneTests.cs', 'Assets/Game/Scripts/GpuMemberSelection.cs', 'Assets/Game/Tests/PlayMode/MemberSelectionPlayModeTests.cs', 'Assets/Game/Scripts/WarSandboxCommandHUD.Toy.cs', 'Assets/Game/Tests/EditMode/MemberSelectionTests.cs.meta', 'Assets/Game/Tests/PlayMode/MemberSelectionPlayModeTests.cs.meta', 'Assets/Game/Tests/PlayMode/DeploymentResizePlayModeTests.cs', 'Assets/Game/Scripts/CameraControls/MyCameraManager.cs'}
assert set(payload)<=allowed
out.mkdir(parents=True);approved=json.loads((P7/'approved-changes.json').read_text(encoding='utf-8'));arc=ROOT/'Logs/WorkspaceArchive-20261007-01';mp=arc/'manifest.json';oldmanifest=mp.read_bytes();manifest=json.loads(oldmanifest);migrations=[]
# Preflight every input and archive record before touching any live source.
for rel in payload:
 p=ROOT/rel
 if p.exists():
  before=p.read_bytes();q=out/'before'/rel;q.parent.mkdir(parents=True,exist_ok=True);q.write_bytes(before)
  for item in manifest['files']:
   storage=item['storage']
   if storage['kind']=='existing-file' and storage['path']==rel:
    assert hashlib.sha256(before).hexdigest()==item['sha256'] and len(before)==item['bytes'],('Archive mismatch',rel)
    new=q.relative_to(ROOT).as_posix();migrations.append({'workspacePath':item['path'],'old':rel,'new':new,'sha256':item['sha256']});storage['path']=new
for rel,content in payload.items():
 p=ROOT/rel;before=p.read_bytes() if p.exists() else None
 if before is not None:
  diff=''.join(difflib.unified_diff(before.decode('utf-8-sig').splitlines(True),content.splitlines(True),fromfile=rel,tofile=rel));q=out/'diffs'/(rel+'.diff');q.parent.mkdir(parents=True,exist_ok=True);q.write_text(diff,encoding='utf-8')
 p.parent.mkdir(parents=True,exist_ok=True);p.write_bytes(content.encode('utf-8'));approved[rel]={'originalSha256':approved.get(rel,{}).get('originalSha256',hashlib.sha256(before).hexdigest() if before is not None else None),'expectedSha256':sha(p),'change':tag}
save(P7/'approved-changes.json',approved)
if migrations:
 (out/'archive-manifest-before.json').write_bytes(oldmanifest);save(mp,manifest)
save(out/'archive-migrations.json',{'oldManifestSha256':hashlib.sha256(oldmanifest).hexdigest(),'newManifestSha256':sha(mp),'migrations':migrations})
check=project_check();save(out/'receipt.json',check);assert check['passed'];print('APPLIED',tag,len(payload),'files; archive references migrated:',len(migrations))



