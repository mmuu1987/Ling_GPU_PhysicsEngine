from common_p1 import *
import shutil,sys
sys.stdout.reconfigure(encoding='utf-8');P2=LOG/'P2';assert not(P2/'baseline.json').exists()
assert not processes() and not(ROOT/'Temp/UnityLockfile').exists()
check=project_check(full=True);assert check['passed'];assert user_check()['passed']
b=json.loads((P1/'baseline.json').read_text(encoding='utf-8'))['files'];a=json.loads((P1/'approved-changes.json').read_text(encoding='utf-8'));b.update({p:r['expectedSha256'] for p,r in a.items()})
save(P2/'baseline.json',{'createdAt':datetime.datetime.now().isoformat(),'files':b,'p1Protection':check,'humanAcceptance':False})
names=['WarSandboxUnitStats.cs','WarSandboxDeploymentDraft.cs','WarSandboxRuntimeDeployment.cs','WarSandboxTerrainValidation.cs','WarSandboxFrontEnd.UnitLibrary.cs','WarSandboxFrontEnd.UnitPreview.cs','WarSandboxDeploymentHUD.UnitStats.cs','WarSandboxUGUI.cs','UnitPreviewRadius.cs','UnitModelPreviewWidget.cs','UnitModelPreviewRenderer.cs']
paths=['Assets/Game/Scripts/'+n for n in names]+['Assets/MassEngine/Core/MassEngineManager.Terrain.cs','Assets/Game/Tests/PlayMode/UnitPreviewRadiusPlayModeTests.cs']
for rel in paths:
 p=P2/'backups'/rel;p.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(ROOT/rel,p);assert sha(p)==b[rel]
save(P2/'intent.json',{'modified':paths,'new':['Assets/Game/Scripts/WarSandboxRadiusPolicy.cs','Assets/Game/Tests/EditMode/UnitRadiusEditingTests.cs','Assets/Game/Tests/PlayMode/UnitRadiusEditingPlayModeTests.cs','Assets/Game/Editor/InteractionRefinementP2.cs'],'newMetas':True,'scope':'Append stable agentRadius field; strict 0.05..4.8 storage; current default spawn/module only. Scene gate max 2r <= cellSize, row/column jitter separation and footprint/terrain. Preview pending values; local>global>official; clone flocking only when changed. Explicit deployment-only clearance override, never live updates; legacy clearance unchanged unless radius workflow used. No P3/drag/selection/build.'})
save(P2/'approved-changes.json',{});print('P2 baseline protected',len(b),'files; backups',len(paths),flush=True)
