from common_p6 import *
import base64,hashlib
folder=P6/'scenario-followup-01';inp=folder/'local-cleanup-input.json';v=json.loads(inp.read_text(encoding='utf-8'));assert sha(folder/'WORKSPACE-INDEX.md')==v['indexSha256'];rows=[]
def normalized(b):return b.decode('utf-8-sig').replace('\r\n','\n').rstrip()
for r in v['files']:
 data=base64.b64decode(r['data']);assert hashlib.sha256(data).hexdigest()==r['sha256'];matches=[]
 for root in [ROOT/'Tools/InteractionRefinement',ROOT/'Assets']:
  for q in root.rglob(r['name']):
   if q.is_file() and normalized(q.read_bytes())==normalized(data):matches.append(q.relative_to(ROOT).as_posix())
 if matches:row={'name':r['name'],'localSha256':r['sha256'],'disposition':'existing-equivalent-text','computerPath':matches[0],'computerSha256':sha(ROOT/matches[0])}
 else:
  q=folder/'local-preserved'/r['name'];q.parent.mkdir(exist_ok=True);assert not q.exists();q.write_bytes(data);assert sha(q)==r['sha256'];row={'name':r['name'],'localSha256':r['sha256'],'disposition':'unique-copy-preserved','computerPath':q.relative_to(ROOT).as_posix(),'computerSha256':sha(q)}
 rows.append(row)
save(folder/'local-cleanup.json',{'indexSha256':v['indexSha256'],'files':rows,'disposable':v['disposable'],'reason':v['reason'],'safeToDeleteLocalCopies':True,'checkedAt':datetime.datetime.now().isoformat()});inp.unlink();print(json.dumps({'safeToDeleteLocalCopies':True,'files':len(rows),'preserved':sum(r['disposition']=='unique-copy-preserved' for r in rows)},ensure_ascii=False))
