from common_p2 import *
import io,zipfile,base64,sys
sys.stdout.reconfigure(encoding='utf-8')
assert not processes() and not(ROOT/'Temp/UnityLockfile').exists()
assert project_check()['passed'] and user_check()['passed']
a=json.loads((P2/'final-audit-02.json').read_text(encoding='utf-8'));assert a['status']=='P2_AUTOMATED_PASS_PENDING_MANUAL_ACCEPTANCE'
payload=json.loads((ROOT/'Tools/InteractionRefinement/p2-report-payload.json').read_text(encoding='utf-8'))
doc=ROOT/'Docs/InteractionRefinement-20261007';current=doc/'IMPLEMENTATION.md'
assert sha(current)==payload['oldImplementationSha256'],'Plan bytes changed after verified read; do not overwrite user changes'
backup=P2/'docs-before/IMPLEMENTATION.md';assert not backup.exists();backup.parent.mkdir(parents=True,exist_ok=True);backup.write_bytes(current.read_bytes())
z=zipfile.ZipFile(io.BytesIO(base64.b64decode(''.join((ROOT/'Tools/InteractionRefinement'/name).read_text(encoding='ascii') for name in payload['zipParts']))));names=z.namelist()
for name in names:
 assert '..' not in name and not name.startswith(('/','\\')) and '\\' not in name
 assert name=='IMPLEMENTATION.md' or name.startswith(('P2-','P2证据/'))
 if name!='IMPLEMENTATION.md':assert not(doc/name).exists(),name
manifest=[]
for name in names:
 p=doc/name;p.parent.mkdir(parents=True,exist_ok=True);data=payload['newImplementation'].encode('utf-8') if name=='IMPLEMENTATION.md' else z.read(name);p.write_bytes(data);assert sha(p)==__import__('hashlib').sha256(data).hexdigest();manifest.append({'path':p.relative_to(ROOT).as_posix(),'sha256':sha(p),'bytes':len(data)})
save(P2/'published-docs.json',{'publishedAt':datetime.datetime.now().isoformat(),'files':manifest,'projectCheck':project_check(),'userCheck':user_check(),'otherProcesses':processes(),'lock':(ROOT/'Temp/UnityLockfile').exists()})
print('PUBLISHED',len(manifest),'files; source and real user data remain protected')
