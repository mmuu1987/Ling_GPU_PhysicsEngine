from common_p3 import *
import sys,hashlib,difflib
sys.stdout.reconfigure(encoding='utf-8');assert not processes() and not(ROOT/'Temp/UnityLockfile').exists();assert project_check()['passed'] and user_check()['passed']
refs=json.loads((P3/'adoption-reference-inspection.json').read_text(encoding='utf-8'));assert len(refs)==2
out=P3/'changes/06-user-adopted-separation';assert not out.exists();base=json.loads((P3/'baseline.json').read_text(encoding='utf-8'))['files'];approved=json.loads((P3/'approved-changes.json').read_text(encoding='utf-8'));planned={}
for r in refs.values():
 rel=r['asset'];assert rel in ['Assets/Game/Content/Characters/UnifiedRoster/Version03/Library/male_Flocking.asset','Assets/Game/Content/Characters/UnifiedRoster/Version03/Library/knight_Flocking.asset'];p=ROOT/rel
 assert sha(p)==r['assetSha256']==base[rel];b=p.read_bytes();assert b.count(b'separationStrength: 24')==1
 new=b.replace(b'separationStrength: 24',b'separationStrength: 48');assert len(new)==len(b);planned[rel]=(b,new)
save(P3/'adoption-scope-06.json',{'userDecision':'Accept separation x2; prioritize less crowding and better appearance; accept measured battle duration and survivor tradeoff','files':list(planned),'references':refs,'operation':'Only separationStrength 24 to 48 in canonical male/knight configs; every reference to these same two characters inherits new value. No other character config, radius, count, scale, damage, capacity or shader changes. No wait-cap adoption.','newBuildAuthorized':False,'manualAcceptanceComplete':False})
out.mkdir(parents=True)
for rel,(old,new) in planned.items():
 backup=out/'before'/rel;backup.parent.mkdir(parents=True,exist_ok=True);backup.write_bytes(old)
 patch=out/'diffs'/(rel+'.diff');patch.parent.mkdir(parents=True,exist_ok=True);patch.write_text(''.join(difflib.unified_diff(old.decode('utf-8-sig').splitlines(True),new.decode('utf-8-sig').splitlines(True),fromfile=rel,tofile=rel)),encoding='utf-8')
for rel,(old,new) in planned.items():
 p=ROOT/rel;assert p.read_bytes()==old;p.write_bytes(new);approved[rel]={'originalSha256':hashlib.sha256(old).hexdigest(),'expectedSha256':sha(p),'change':'06-user-adopted-separation'}
save(P3/'approved-changes.json',approved);check=project_check();save(out/'receipt.json',check);assert check['passed'];print('ADOPTED two canonical character configs: 24 -> 48; byte-preserved except those two scalars')
