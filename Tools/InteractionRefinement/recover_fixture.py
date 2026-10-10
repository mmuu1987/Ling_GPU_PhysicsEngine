from common_p0 import *
import re,uuid,sys
sys.stdout.reconfigure(encoding='utf-8')
plan=json.loads((P0/'fixture-recovery-plan.json').read_text(encoding='utf-8'));target=ROOT/plan['editorOnlyTarget'];assert not target.exists()
assert not processes() and not(ROOT/'Temp/UnityLockfile').exists();assert project_check()['passed'];assert user_check()['passed']
packageRefs={};missing={x['guid'] for x in plan['unresolved']}
for folder in (ROOT/'Library/PackageCache').glob('com.unity.render-pipelines.*'):
 for p in folder.rglob('*.meta'):
  m=re.search(r'^guid:\s*([a-f0-9]{32})',p.read_text(encoding='utf-8',errors='replace'),re.M)
  if m and m[1] in missing:packageRefs[m[1]]=str(p.with_suffix('').relative_to(ROOT))
assert missing<=packageRefs.keys(),('Unresolved original dependencies',missing-packageRefs.keys())
# Clone fixture identities: restoring an old GUID must not silently reactivate retired scene references.
remap={x['guid']:uuid.uuid4().hex for x in plan['files']};prepared={};record=[]
for row in plan['files']:
 for suffix,hashkey in [('', 'sha256'),('.meta','metaSha256')]:
  data=subprocess.check_output(['git','show',plan['sourceCommit']+':'+row['source']+suffix],cwd=ROOT,timeout=60)
  assert hashlib.sha256(data).hexdigest()==row[hashkey]
  if suffix or data.startswith(b'%YAML'):
   data=re.sub(rb'(guid:\s*)([a-f0-9]{32})',lambda m:m[1]+remap.get(m[2].decode(),m[2].decode()).encode(),data)
  dest=row['destination']+suffix;assert not(ROOT/dest).exists();prepared[dest]=data
  record.append({'path':dest,'source':row['source']+suffix,'sourceCommit':plan['sourceCommit'],'sourceSha256':row[hashkey],'sha256':hashlib.sha256(data).hexdigest(),'transform':'Unity GUID identity remap only; FBX/texture payloads byte-identical'})
folders=set()
for name in list(prepared):
 parent=(ROOT/name).parent
 while not parent.exists():folders.add(parent);parent=parent.parent
for folder in sorted(folders,key=lambda p:len(p.parts)):
 name=folder.relative_to(ROOT).as_posix()+'.meta';assert not(ROOT/name).exists()
 data=('fileFormatVersion: 2\nguid: '+uuid.uuid4().hex+'\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n').encode();prepared[name]=data
assert not processes() and not(ROOT/'Temp/UnityLockfile').exists()
for name,data in prepared.items():p=ROOT/name;p.parent.mkdir(parents=True,exist_ok=True);p.write_bytes(data)
registry=P0/'approved-new-files.json';existing=json.loads(registry.read_text(encoding='utf-8')) if registry.exists() else {}
existing.update({n:{'purpose':'Editor-only historical VAT regression fixture','initialSha256':hashlib.sha256(b).hexdigest()} for n,b in prepared.items()});save(registry,existing)
result={'sourceCommit':plan['sourceCommit'],'fixtureAssets':len(plan['files']),'filesIncludingMetas':len(prepared),'bytes':sum(len(x) for x in prepared.values()),'newGuids':remap,'packageReferences':packageRefs,'files':record,'postCheck':project_check()}
save(P0/'fixture-recovery.json',result);print(json.dumps({k:v for k,v in result.items() if k not in ['files','newGuids']},ensure_ascii=False,indent=2),flush=True);assert result['postCheck']['passed']
