from common_p3 import *
import sys,base64,zipfile,io,hashlib
sys.stdout.reconfigure(encoding='utf-8');assert not processes() and not(ROOT/'Temp/UnityLockfile').exists();assert project_check()['passed'] and user_check()['passed']
a=json.loads((P3/'adoption-audit-01.json').read_text(encoding='utf-8'));assert a['status']=='P3_ADOPTED_CONFIG_AUTOMATED_CHECKPOINT_PENDING_MANUAL'
payload=json.loads((ROOT/'Tools/InteractionRefinement/adopted-report-payload.json').read_text(encoding='utf-8'));doc=ROOT/'Docs/InteractionRefinement-20261007';p=doc/'IMPLEMENTATION.md';assert sha(p)==payload['oldImplementationSha256'],'Existing plan changed; refuse overwrite'
backup=P3/'adopted-docs-before/IMPLEMENTATION.md';assert not backup.exists();backup.parent.mkdir(parents=True);backup.write_bytes(p.read_bytes())
z=zipfile.ZipFile(io.BytesIO(base64.b64decode(''.join((ROOT/'Tools/InteractionRefinement'/n).read_text(encoding='ascii') for n in payload['parts']))));assert sorted(z.namelist())==sorted(payload['files'])
for n in z.namelist():
 assert '..' not in n and not n.startswith(('/','\\')) and '\\' not in n
 assert n=='IMPLEMENTATION.md' or n.startswith('P3')
 if n!='IMPLEMENTATION.md':assert not(doc/n).exists(),n
records=[]
for n in z.namelist():
 data=z.read(n);p=doc/n;p.parent.mkdir(parents=True,exist_ok=True);p.write_bytes(data);assert sha(p)==hashlib.sha256(data).hexdigest();records.append({'path':p.relative_to(ROOT).as_posix(),'bytes':len(data),'sha256':sha(p)})
r={'publishedAt':datetime.datetime.now().isoformat(),'files':records,'projectCheck':project_check(),'userCheck':user_check(),'activeProcesses':processes(),'lock':(ROOT/'Temp/UnityLockfile').exists()};save(P3/'adopted-published-docs.json',r);assert r['projectCheck']['passed'] and r['userCheck']['passed'] and not r['activeProcesses'] and not r['lock'];print('PUBLISHED',len(records),'adoption documents and photos; historical diagnostic reports kept intact')
