from common_p8 import *
import sys,hashlib
sys.stdout.reconfigure(encoding='utf-8');assert not(P8/'local-cleanup.json').exists();assert json.loads((P8/'publication.json').read_text(encoding='utf-8'))['passed'];manifest=json.loads((P8/'assistant-local-manifest.json').read_text(encoding='utf-8'))
assert sha(LOG/'P7/WORKSPACE-INDEX.md')==manifest['previousIndexSha256'];assert sha(P8/'WORKSPACE-INDEX.md')==manifest['indexSha256']
byname={}
for folder in ['Assets','Tools/InteractionRefinement','Docs/InteractionRefinement-20261007','Logs/InteractionRefinement-20261007/P8','Logs/InteractionRefinement-20261007/P7']:
 for p in(ROOT/folder).rglob('*'):
  if p.is_file():byname.setdefault(p.name,[]).append(p)
verified=[];unmatched=[]
for r in manifest['files']:
 if 'disposableReason' in r:verified.append({**r,'preservation':'disposable-nonessential-generated-helper'});continue
 candidates=[ROOT/x for x in r.get('explicitCandidates',[])]+byname.get(r['name'],[]);hit=None
 for p in candidates:
  if not p.is_file():continue
  if sha(p)==r['sha256']:hit={'computerPath':p.relative_to(ROOT).as_posix(),'proof':'exact-sha256'};break
  if 'normalizedTextSha256' in r:
   try:t=p.read_text(encoding='utf-8-sig').replace('\r\n','\n')
   except UnicodeError:continue
   if hashlib.sha256(t[:r['normalizedTextLength']].encode()).hexdigest()==r['normalizedTextSha256']:hit={'computerPath':p.relative_to(ROOT).as_posix(),'proof':'normalized-text-full-or-read-prefix','computerSha256':sha(p)};break
 if hit:verified.append({**r,**hit})
 else:unmatched.append(r)
r={'safeToDeleteLocalCopies':not unmatched,'indexSha256':manifest['indexSha256'],'previousIndexPreservedAt':'Logs/InteractionRefinement-20261007/P7/WORKSPACE-INDEX.md','verified':verified,'unmatched':unmatched,'disposableDirectories':manifest['disposableDirectories'],'note':manifest['note'],'checkedAt':datetime.datetime.now().isoformat()}
save(P8/'local-cleanup.json',r);print(json.dumps({'safeToDeleteLocalCopies':r['safeToDeleteLocalCopies'],'verified':len(verified),'unmatched':unmatched},ensure_ascii=False,indent=2));assert not unmatched
