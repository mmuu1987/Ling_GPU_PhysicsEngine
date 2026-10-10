from common_p3 import *
import base64,sys
sys.stdout.reconfigure(encoding='utf-8');assert not processes() and not(ROOT/'Temp/UnityLockfile').exists();assert project_check()['passed'] and user_check()['passed']
doc=ROOT/'Docs/InteractionRefinement-20261007';payload=json.loads((ROOT/'Tools/InteractionRefinement/p3-report-revision-02.json').read_text(encoding='utf-8'));backup=P3/'docs-revision-02-before';assert not backup.exists()
for name,r in payload.items():
 assert name in ['P3-REPORT.md','P3-诊断预览.html','P3-IMAGE-PROVENANCE.json','P3-初始站位.svg'];p=doc/name
 assert (sha(p)==r['expectedSha256']) if r['expectedSha256'] is not None else not p.exists()
backup.mkdir()
for name,r in payload.items():
 p=doc/name
 if p.exists():(backup/name).write_bytes(p.read_bytes())
 p.write_bytes(base64.b64decode(r['content']))
manifest=json.loads((P3/'published-docs.json').read_text(encoding='utf-8'));files={r['path']:r for r in manifest['files']}
for n in payload:
 p=doc/n;rel=p.relative_to(ROOT).as_posix();files[rel]={'path':rel,'bytes':p.stat().st_size,'sha256':sha(p)}
manifest.update({'publishedAt':datetime.datetime.now().isoformat(),'revision':2,'reason':'Reject initial PNG as subject-visibility evidence; display accurately labeled GPU-position circle plot instead. Numeric evidence unchanged.','files':list(files.values()),'projectCheck':project_check(),'userCheck':user_check(),'activeProcesses':processes(),'lock':(ROOT/'Temp/UnityLockfile').exists()});save(P3/'published-docs-02.json',manifest);print('P3 report revision 2 published; invalid initial rendering explicitly rejected')
