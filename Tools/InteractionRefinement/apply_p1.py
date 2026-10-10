from common_p1 import *
import sys,hashlib,difflib
sys.stdout.reconfigure(encoding='utf-8');tag=sys.argv[1]
assert tag.replace('-','').isalnum();out=P1/'changes'/tag;assert not out.exists()
assert not processes() and not(ROOT/'Temp/UnityLockfile').exists()
assert project_check()['passed'];assert user_check()['passed']
payload=json.loads((ROOT/('Tools/InteractionRefinement/p1-'+tag+'.json')).read_text(encoding='utf-8'));approved=json.loads((P1/'approved-changes.json').read_text(encoding='utf-8'));out.mkdir(parents=True)
for rel,content in payload.items():
 assert rel.startswith('Assets/Game/') and '..' not in rel
 p=ROOT/rel;before=p.read_bytes() if p.exists() else None
 if before is not None:
  q=out/'before'/rel;q.parent.mkdir(parents=True,exist_ok=True);q.write_bytes(before)
  patch=''.join(difflib.unified_diff(before.decode('utf-8-sig').splitlines(True),content.splitlines(True),fromfile=rel,tofile=rel))
  q=out/'diffs'/(rel+'.diff');q.parent.mkdir(parents=True,exist_ok=True);q.write_text(patch,encoding='utf-8')
 p.parent.mkdir(parents=True,exist_ok=True);p.write_bytes(content.encode('utf-8'));approved[rel]={'originalSha256':approved.get(rel,{}).get('originalSha256',hashlib.sha256(before).hexdigest() if before is not None else None),'expectedSha256':sha(p),'change':tag}
save(P1/'approved-changes.json',approved);check=project_check();save(out/'receipt.json',check);assert check['passed'];print('APPLIED',tag,len(payload),'files',flush=True)
