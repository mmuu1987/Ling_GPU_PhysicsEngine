from common_p3 import *
import io,zipfile,base64,sys
sys.stdout.reconfigure(encoding='utf-8');assert not processes() and not(ROOT/'Temp/UnityLockfile').exists()
assert project_check()['passed'] and user_check()['passed']
a=json.loads((P3/'final-audit-01.json').read_text(encoding='utf-8'));assert a['status']=='P3_DIAGNOSTIC_CHECKPOINT_NO_PRODUCTION_FIX'
payload=json.loads((ROOT/'Tools/InteractionRefinement/p3-report-payload.json').read_text(encoding='utf-8'));doc=ROOT/'Docs/InteractionRefinement-20261007';current=doc/'IMPLEMENTATION.md'
assert sha(current)==payload['oldImplementationSha256'],'Plan changed after verified read; do not overwrite user changes'
b=P3/'docs-before/IMPLEMENTATION.md';assert not b.exists();b.parent.mkdir(parents=True,exist_ok=True);b.write_bytes(current.read_bytes())
z=zipfile.ZipFile(io.BytesIO(base64.b64decode(''.join((ROOT/'Tools/InteractionRefinement'/n).read_text(encoding='ascii') for n in payload['zipParts']))));assert sorted(z.namelist())==sorted(payload['files'])
for n in z.namelist():
 assert '..' not in n and not n.startswith(('/','\\')) and '\\' not in n
 assert n=='IMPLEMENTATION.md' or n.startswith(('P3-','P3预览图/'))
 if n!='IMPLEMENTATION.md':assert not(doc/n).exists(),n
manifest=[]
for n in z.namelist():
 p=doc/n;p.parent.mkdir(parents=True,exist_ok=True);data=z.read(n);p.write_bytes(data);assert sha(p)==__import__('hashlib').sha256(data).hexdigest();manifest.append({'path':p.relative_to(ROOT).as_posix(),'bytes':len(data),'sha256':sha(p)})
save(P3/'published-docs.json',{'publishedAt':datetime.datetime.now().isoformat(),'files':manifest,'projectCheck':project_check(),'userCheck':user_check(),'activeProcesses':processes(),'lock':(ROOT/'Temp/UnityLockfile').exists()});print('PUBLISHED',len(manifest),'documents/previews; full raw evidence remains in P3 logs')
