from common_p0 import *
import json,os,datetime
P1=LOG/'P1'
def project_check(full=False):
 baseline=json.loads((P1/'baseline.json').read_text(encoding='utf-8'))['files'];changes=json.loads((P1/'approved-changes.json').read_text(encoding='utf-8'));expected={**baseline,**{k:v['expectedSha256'] for k,v in changes.items()}}
 oldstat={r['path']:(r['bytes'],r['mtime_ns']) for r in json.loads((BASE/'project-files.sha256.json').read_text(encoding='utf-8'))};current={}
 for folder in ['Assets','ProjectSettings','Packages']:
  for parent,dirs,names in os.walk(ROOT/folder,followlinks=False):
   for n in dirs+names:
    p=__import__('pathlib').Path(parent)/n;assert not p.is_symlink() and not(p.lstat().st_file_attributes&0x400)
   for n in names:
    p=__import__('pathlib').Path(parent)/n;current[p.relative_to(ROOT).as_posix()]=p
 errors=[]
 for rel,want in expected.items():
  p=current.get(rel)
  if p is None:errors.append({'path':rel,'kind':'missing'});continue
  st=p.stat()
  if full or rel in changes or oldstat.get(rel)!=(st.st_size,st.st_mtime_ns):
   if sha(p)!=want:errors.append({'path':rel,'kind':'unexpected-modification'})
 unexpected=sorted(set(current)-set(expected));return {'phase':'P1','full':full,'baselineFiles':len(baseline),'approvedChanges':len(changes),'changed':errors,'unexpectedNew':unexpected,'passed':not errors and not unexpected}
