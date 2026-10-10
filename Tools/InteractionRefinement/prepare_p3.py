from common_p2 import *
import sys
sys.stdout.reconfigure(encoding='utf-8');P3=LOG/'P3';assert not(P3/'baseline.json').exists()
assert not processes() and not(ROOT/'Temp/UnityLockfile').exists()
check=project_check(full=True);assert check['passed'],check;assert user_check()['passed']
b=json.loads((P2/'baseline.json').read_text(encoding='utf-8'))['files'];a=json.loads((P2/'approved-changes.json').read_text(encoding='utf-8'));b.update({p:r['expectedSha256'] for p,r in a.items()})
save(P3/'baseline.json',{'createdAt':datetime.datetime.now().isoformat(),'files':b,'p2Protection':check,'humanAcceptance':False})
save(P3/'intent.json',{'modified':[],'new':['Assets/Game/Editor/InteractionRefinementP3.cs','Assets/Game/Tests/PlayMode/CrowdingDiagnosticsPlayModeTests.cs'],'newMetas':True,'scope':'Diagnosis only first: fixed default Green scene, 2048 agents, official parameters. Initial/hold/move/contact GPU readbacks and fixed-camera screenshots. No production shader/config edits until evidence establishes a causal intervention. No density/count/scale/radius normalization, no P4/P6/build. CPU sample capture is instrumented, not a performance claim.'})
save(P3/'approved-changes.json',{});print('P3 BASELINE',len(b),'files; no production source edits authorized in this diagnostic patch',flush=True)
