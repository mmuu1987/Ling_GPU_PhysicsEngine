from common_p8 import *
from PIL import Image
import hashlib
assert not(P8/'local-cleanup-02.json').exists();old=json.loads((P8/'local-cleanup.json').read_text(encoding='utf-8'));extra=json.loads((P8/'assistant-local-normalized.json').read_text(encoding='utf-8'));byname={}
for folder in ['Assets','Tools/InteractionRefinement','Docs/InteractionRefinement-20261007','Logs/InteractionRefinement-20261007/P8','Logs/InteractionRefinement-20261007/P7']:
 for p in(ROOT/folder).rglob('*'):
  if p.is_file():byname.setdefault(p.name,[]).append(p)
matched=[];missing=[]
for r in old['unmatched']:
 e=extra[r['localPath']];candidates=[ROOT/p for p in r.get('explicitCandidates',[])]+byname.get(r['name'],[]);hit=None
 for p in candidates:
  if 'eofNormalizedSha256' in e:
   try:t=p.read_text(encoding='utf-8-sig').replace('\r\n','\n').rstrip()
   except UnicodeError:continue
   if hashlib.sha256(t[:e['eofNormalizedLength']].encode()).hexdigest()==e['eofNormalizedSha256']:hit={'computerPath':p.relative_to(ROOT).as_posix(),'proof':'UTF8 LF and trailing whitespace normalized, complete text or read-prefix','computerSha256':sha(p)};break
  else:
   im=Image.open(p).convert('RGBA')
   if list(im.size)==e['pixelSize'] and hashlib.sha256(im.tobytes()).hexdigest()==e['pixelSha256']:hit={'computerPath':p.relative_to(ROOT).as_posix(),'proof':'exact decoded RGBA pixels; PNG compression bytes differ','computerSha256':sha(p)};break
 if hit:matched.append({**r,**e,**hit})
 else:missing.append(r)
r={**old,'safeToDeleteLocalCopies':not missing,'verified':old['verified']+matched,'unmatched':missing,'previousCheck':'local-cleanup.json','explanation':'First strict byte/line check found normalized readback EOF differences and PNG encoding differences; no local deletion occurred. Verified semantic text/prefix and exact decoded image pixels in second check.','extraLocalBookkeepingCopies':{'local-p8-manifest.json':'Logs/InteractionRefinement-20261007/P8/assistant-local-manifest.json','preserve_p8_workspace.py':'Tools/InteractionRefinement/preserve_p8_workspace.py'},'checkedAt':datetime.datetime.now().isoformat()};save(P8/'local-cleanup-02.json',r);print({'safeToDeleteLocalCopies':r['safeToDeleteLocalCopies'],'verified':len(r['verified']),'missing':missing});assert not missing
