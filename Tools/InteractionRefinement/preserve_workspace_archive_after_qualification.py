from common_p3 import *
import sys,zipfile,hashlib
sys.stdout.reconfigure(encoding='utf-8');arc=ROOT/'Logs/WorkspaceArchive-20261007-01';p=arc/'manifest.json';old=p.read_bytes();m=json.loads(old);relocations=[]
assert hashlib.sha256(old).hexdigest()=='7fd20a38dc486d44ce1119cbac5f3b439af1634d316ad7d77165c7bad83885a4' and len(m['files'])==462
assert not(arc/'manifest-before-qualification.json').exists();assert not(arc/'verification-qualification-01.json').exists()
for r in m['files']:
 s=r['storage']
 if s['kind']!='existing-file':continue
 actual=ROOT/s['path']
 if actual.exists() and actual.stat().st_size==r['bytes'] and sha(actual)==r['sha256']:continue
 candidates=[P3/'changes/07-qualification-tools/before'/s['path']]
 if s['path']=='Docs/InteractionRefinement-20261007/IMPLEMENTATION.md':candidates.append(P3/'qualification-docs-before/IMPLEMENTATION.md')
 matches=[x for x in candidates if x.exists() and x.stat().st_size==r['bytes'] and sha(x)==r['sha256']]
 assert matches,('Archived source changed without matching controlled backup; stop',r['path'],s['path'])
 dest=matches[0].relative_to(ROOT).as_posix();relocations.append({'originalWorkspacePath':r['path'],'oldSource':s['path'],'newSource':dest,'sha256':r['sha256']});s['path']=dest
# Reverify the complete preserved set, including the uploaded-only assets.
with zipfile.ZipFile(arc/'extra-files.zip') as z:
 assert z.testzip() is None
 for r in m['files']:
  s=r['storage'];data=(ROOT/s['path']).read_bytes() if s['kind']=='existing-file' else z.read(s['entry'])
  assert len(data)==r['bytes'] and hashlib.sha256(data).hexdigest()==r['sha256'],r['path']
(arc/'manifest-before-qualification.json').write_bytes(old);temp=arc/'manifest-qualification.tmp';temp.write_text(json.dumps(m,ensure_ascii=False,indent=2),encoding='utf-8');os.replace(temp,p)
r={'status':'ALL_462_PRESERVED_FILES_REVERIFIED','checkedAt':datetime.datetime.now().isoformat(),'reason':'Normal continued development changed a self-owned diagnostic fixture and implementation plan. Point archive records at byte-identical controlled backups; original file bytes remain recoverable.','oldManifestSha256':hashlib.sha256(old).hexdigest(),'newManifestSha256':sha(p),'preservedFiles':len(m['files']),'relocations':relocations};save(arc/'verification-qualification-01.json',r)
with (arc/'README.md').open('a',encoding='utf-8') as f:f.write('\n## 后续开发后的归档引用更新\n\nP3补验后已按受控原字节备份迁移发生变化的引用，全部462文件重新核验。当前清单仍为manifest.json；首次清单保存在manifest-before-qualification.json。最新收据verification-qualification-01.json包含清单哈希变更和迁移明细。原verification.json是清理当时的历史收据。\n')
print(json.dumps(r,ensure_ascii=False,indent=2))
