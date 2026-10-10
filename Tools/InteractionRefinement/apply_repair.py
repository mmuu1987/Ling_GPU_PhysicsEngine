from common_p0 import *
import sys,shutil
sys.stdout.reconfigure(encoding='utf-8')
tag=sys.argv[1];patchpath=P0/'repairs'/tag/'patches.json';patches=json.loads(patchpath.read_text(encoding='utf-8'))
assert not processes() and not(ROOT/'Temp/UnityLockfile').exists(),'Editor/player open; do not modify'
assert project_check()['passed'],'Unexplained project changes; inspect first'
registry=P0/'approved-source-changes.json';records=json.loads(registry.read_text(encoding='utf-8')) if registry.exists() else {}
baseline={r['path']:r for r in json.loads((BASE/'project-files.sha256.json').read_text(encoding='utf-8'))}
ready=[]
for item in patches:
 path=item['path'];assert path in baseline
 p=ROOT/path;before=p.read_bytes();assert sha(p)==records.get(path,{}).get('expectedSha256',baseline[path]['sha256'])
 newline='\r\n' if b'\r\n' in before else '\n';old=item['old'].replace('\r\n','\n').replace('\n',newline).encode();new=item['new'].replace('\r\n','\n').replace('\n',newline).encode()
 assert before.count(old)==1,(path,'Patch must match exactly once')
 after=before.replace(old,new,1);backup=patchpath.parent/'before'/path;assert not backup.exists();ready.append((path,before,after,backup,item['reason']))
for path,before,after,backup,reason in ready:
 backup.parent.mkdir(parents=True,exist_ok=True);backup.write_bytes(before);(ROOT/path).write_bytes(after)
 records[path]={'originalSha256':baseline[path]['sha256'],'expectedSha256':sha(ROOT/path),'repair':tag,'reason':reason}
save(registry,records);check=project_check();save(patchpath.parent/'after-check.json',check);print(json.dumps(check,ensure_ascii=False,indent=2),flush=True);assert check['passed']
