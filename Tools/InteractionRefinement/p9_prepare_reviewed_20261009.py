from pathlib import Path
import json,subprocess,hashlib,shutil,os,sys,time,difflib
sys.stdout.reconfigure(encoding='utf-8')
R=Path(__file__).resolve().parents[2];O=R/'outputs/P9Combined-20261009-01';P=R/'outputs/P11';M='080c5e498f0004c8d54fbe745970fb11e36d3597'
env=os.environ.copy();env['GIT_OPTIONAL_LOCKS']='0'
def git(*a):return subprocess.check_output(['git',*a],cwd=R,env=env)
def sha(p):
 h=hashlib.sha256()
 with p.open('rb') as f:
  for b in iter(lambda:f.read(1048576),b''):h.update(b)
 return h.hexdigest()
def save(n,x):(O/n).write_text(json.dumps(x,ensure_ascii=False,indent=2),encoding='utf-8')
d=json.loads((O/'inventory.json').read_text(encoding='utf-8'));assert not P.exists()
# Resolve actual overlapping code via normalized three-way merge, never selecting one side blindly.
checks=json.loads((O/'three-way.json').read_text(encoding='utf-8'))
for p,x in checks.items():
 local=(R/p).read_bytes().replace(b'\r\n',b'\n');merged=git('show',M+':'+p).replace(b'\r\n',b'\n')
 assert hashlib.sha256((R/p).read_bytes()).hexdigest()==d['mergeLocalOverlap'][p]['localSha']
 ops=difflib.SequenceMatcher(None,local.splitlines(True),merged.splitlines(True),autojunk=False).get_opcodes()
 assert all(tag in ['equal','insert'] for tag,*_ in ops), 'Merged file does not preserve every mother line: '+p
 x['reviewedAdditionsOnly']=True;x['removedOrReplacedMotherLines']=0
save('overlap-review.json',{'decision':'Use exact previously validated merge blobs; every mother line is preserved in order. Grid three-way clean; Runtime adjacent independent additions conflict in Git three-way but reviewed diff consists solely of Burst opt-in additions and guards, no removals/replacements.','checks':checks})
print('OVERLAP REVIEWED: all mother lines preserved; no production edits',flush=True)
paths=[]
for root in ['Assets','Packages','ProjectSettings']:
 for parent,dirs,files in os.walk(R/root,followlinks=False):
  for n in dirs+files:
   f=Path(parent)/n;assert not f.is_symlink() and not(f.lstat().st_file_attributes&0x400),str(f)
  paths.extend(Path(parent)/f for f in files)
size=sum(f.stat().st_size for f in paths);assert shutil.disk_usage(R).free>size*2+8*1024**3
before={'head':git('rev-parse','HEAD').decode().strip(),'index':sha(R/'.git/index'),'files':{f.relative_to(R).as_posix():sha(f) for f in paths}}
for f in (R/'Builds/CaptureDefault-20261006-37').rglob('*'):
 if f.is_file():before['files'][f.relative_to(R).as_posix()]=sha(f)
# Snapshot all actual user files, not just old selective hashes.
u=json.loads((R/'Logs/InteractionRefinement-20261007/baseline/user-data.sha256.json').read_text(encoding='utf-8'));user=Path(u['root'])
before['userRoot']=str(user);before['userFiles']={f.relative_to(user).as_posix():sha(f) for f in user.rglob('*') if f.is_file()}
save('mother-before.json',before)
print('COPY INDEPENDENT PROJECT',len(paths),'files',size,'bytes',flush=True)
P.mkdir();source={};excluded=[]
for f in paths:
 rel=f.relative_to(R).as_posix()
 if rel in ['Assets/pelican-cycling.svg','Assets/pelican-cycling.svg.meta']:excluded.append(rel);continue
 t=P/rel;t.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(f,t);assert sha(t)==before['files'][rel]
 source[rel]={'origin':'mother-current-P9-overlay','sha256':sha(t)}
for rel in d['mergedProjectChanges']:
 t=P/rel;t.parent.mkdir(parents=True,exist_ok=True);b=git('show',M+':'+rel);t.write_bytes(b)
 source[rel]={'origin':'merged-git-blob','commit':M,'sha256':sha(t)}
# Source-only package cache; never copy AssetDatabase, compiled assemblies or project JIT.
lock=json.loads((P/'Packages/packages-lock.json').read_text(encoding='utf-8'))['dependencies'];pkgs=[]
for name,rec in lock.items():
 if rec.get('source')!='registry':continue
 options=[]
 for f in (R/'Library/PackageCache').glob(name+'@*/package.json'):
  obj=json.loads(f.read_text(encoding='utf-8-sig'))
  if obj.get('name')==name and obj.get('version')==rec['version']:options.append(f.parent)
 if len(options)!=1:continue
 src=options[0];dst=P/'Library/PackageCache'/src.name;shutil.copytree(src,dst)
 hashes={f.relative_to(src).as_posix():sha(f) for f in src.rglob('*') if f.is_file()}
 assert all(sha(dst/f)==h for f,h in hashes.items());pkgs.append({'name':name,'version':rec['version'],'files':len(hashes)})
save('source-manifest.json',{'mergedCommit':M,'description':'Explicit merged baseline + complete current mother project overlay, including existing layout moves/deletions; not a pure Git commit. All twelve merge project blobs win only after overlap three-way proof. No new production changes.','files':source,'excludedForbiddenArtwork':excluded,'pinnedSourcePackages':pkgs,'sourceBytes':size})
assert git('rev-parse','HEAD').decode().strip()==before['head'] and sha(R/'.git/index')==before['index']
assert all(sha(R/f)==h for f,h in before['files'].items())
assert {f.relative_to(user).as_posix():sha(f) for f in user.rglob('*') if f.is_file()}==before['userFiles']
save('prepare.json',{'status':'ready_for_test_harness','project':str(P),'mergedCommit':M,'projectFiles':len(source),'motherProtectedFiles':len(before['files']),'userFiles':len(before['userFiles']),'motherProtectionPassed':True,'threeWayChecks':checks,'freshCompilationRequired':True,'sourceOnlyPackages':len(pkgs),'testHarnessAdded':False})
print('PREPARED',len(source),'files; mother and user data unchanged',flush=True)
