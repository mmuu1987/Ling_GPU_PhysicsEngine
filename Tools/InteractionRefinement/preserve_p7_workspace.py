from common_p7 import *
import base64,hashlib
inp=P7/'local-cleanup-input.json';v=json.loads(inp.read_text(encoding='utf-8'));assert sha(P7/'WORKSPACE-INDEX.md')==v['indexSha256'];rows=[]
def normalized(b):return b.decode('utf-8-sig').replace('\r\n','\n').rstrip()
for r in v['files']:
 data=base64.b64decode(r['data']);assert hashlib.sha256(data).hexdigest()==r['sha256'];matches=[]
 if r.get('computerHint'):
  q=ROOT/r['computerHint'];assert sha(q)==r['sha256'];matches=[q]
 else:
  for root in [ROOT/'Tools/InteractionRefinement',ROOT/'Assets',ROOT/'Logs/InteractionRefinement-20261007',ROOT/'Docs/InteractionRefinement-20261007']:
   for q in root.rglob(r['name']):
    if q.is_file() and normalized(q.read_bytes())==normalized(data):matches.append(q);break
   if matches:break
 if matches:q=matches[0];disposition='existing-equivalent'
 else:
  q=P7/'local-preserved'/r['name'];q.parent.mkdir(exist_ok=True);assert not q.exists();q.write_bytes(data);assert sha(q)==r['sha256'];disposition='unique-copy-preserved'
 rows.append({'name':r['name'],'localSha256':r['sha256'],'computerPath':q.relative_to(ROOT).as_posix(),'computerSha256':sha(q),'disposition':disposition})
save(P7/'local-cleanup.json',{'indexSha256':v['indexSha256'],'files':rows,'disposable':v['disposable'],'safeToDeleteLocalCopies':True,'checkedAt':datetime.datetime.now().isoformat()});inp.unlink();print(json.dumps({'files':len(rows),'preserved':sum(r['disposition']=='unique-copy-preserved' for r in rows),'safeToDeleteLocalCopies':True},ensure_ascii=False))
