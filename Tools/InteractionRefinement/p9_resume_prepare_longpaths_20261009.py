from pathlib import Path
import subprocess,hashlib,json,shutil,os,sys
sys.stdout.reconfigure(encoding='utf-8')
R=Path(__file__).resolve().parents[2];O=R/'outputs/P9Combined-20261009-01';P=R/'outputs/P11';M='080c5e498f0004c8d54fbe745970fb11e36d3597'
env=os.environ.copy();env['GIT_OPTIONAL_LOCKS']='0'
def lp(p):
 s=str(p.resolve());prefix=chr(92)*2+'?'+chr(92);return Path(s if s.startswith(prefix) else prefix+s)
def sha(p):
 h=hashlib.sha256()
 with lp(p).open('rb') as f:
  for b in iter(lambda:f.read(1048576),b''):h.update(b)
 return h.hexdigest()
def save(n,x):(O/n).write_text(json.dumps(x,ensure_ascii=False,indent=2),encoding='utf-8')
def git(*a):return subprocess.check_output(['git',*a],cwd=R,env=env)
assert P.is_dir() and not (P/'Temp/UnityLockfile').exists() and not(O/'prepare.json').exists()
b=json.loads((O/'mother-before.json').read_text(encoding='utf-8'));d=json.loads((O/'inventory.json').read_text(encoding='utf-8'))
assert git('rev-parse','HEAD').decode().strip()==b['head'] and sha(R/'.git/index')==b['index']
print('VERIFY MOTHER AND EXISTING ISOLATED COPY',flush=True)
changes=[f for f,h in b['files'].items() if not (R/f).is_file() or sha(R/f)!=h]
user=Path(b['userRoot']);uf={f.relative_to(user).as_posix():sha(f) for f in user.rglob('*') if f.is_file()}
save('resume-precheck.json',{'motherChanges':changes,'userDataUnchanged':uf==b['userFiles']});assert not changes and uf==b['userFiles']
source={};excluded=[]
for f,h in b['files'].items():
 if f.split('/')[0] not in ['Assets','Packages','ProjectSettings']:continue
 if f in ['Assets/pelican-cycling.svg','Assets/pelican-cycling.svg.meta']:excluded.append(f);continue
 if f in d['mergedProjectChanges']:continue
 assert sha(P/f)==h,'Existing copy differs: '+f
 source[f]={'origin':'mother-current-P9-overlay','sha256':h}
for f in d['mergedProjectChanges']:
 want=hashlib.sha256(git('show',M+':'+f)).hexdigest();assert sha(P/f)==want,f
 source[f]={'origin':'merged-git-blob','commit':M,'sha256':want}
print('PROJECT SOURCE VERIFIED',len(source),flush=True)
lock=json.loads((P/'Packages/packages-lock.json').read_text(encoding='utf-8'))['dependencies'];pkgs=[]
for name,rec in lock.items():
 if rec.get('source')!='registry':continue
 matches=[]
 for q in (R/'Library/PackageCache').glob(name+'@*/package.json'):
  meta=json.loads(q.read_text(encoding='utf-8-sig'))
  if meta.get('name')==name and meta.get('version')==rec['version']:matches.append(q.parent)
 if len(matches)!=1:continue
 src=matches[0];dst=P/'Library/PackageCache'/src.name;count=0;copied=0
 # Extended-length paths only for this owned source-copy operation, no global Windows/Git setting changes.
 for parent,dirs,files in os.walk(lp(src)):
  for filename in files:
   f=Path(parent)/filename;rel=f.relative_to(lp(src));t=lp(dst)/rel;h=sha(f)
   if not t.is_file() or sha(t)!=h:t.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(f,t);copied+=1
   assert sha(t)==h;count+=1
 pkgs.append({'name':name,'version':rec['version'],'files':count,'copiedOrRepairedFiles':copied})
 print('PACKAGE',name,'verified',count,'copied',copied,flush=True)
save('source-manifest.json',{'mergedCommit':M,'description':'Merged baseline combined with current mother P9 project overlay and prior asset relocations; exact twelve merge blobs retained after additions-only overlap review. Not a pure Git commit.','files':source,'excludedForbiddenArtwork':excluded,'pinnedSourcePackages':pkgs})
assert git('rev-parse','HEAD').decode().strip()==b['head'] and sha(R/'.git/index')==b['index']
save('prepare.json',{'status':'ready_for_test_harness','project':str(P),'mergedCommit':M,'projectFiles':len(source),'motherProtectedFiles':len(b['files']),'userFiles':len(b['userFiles']),'motherProtectionPassed':True,'freshCompilationRequired':True,'sourceOnlyPackages':len(pkgs),'testHarnessAdded':False,'resumedAfter':'Windows MAX_PATH package copy failure; preexisting project verified, only missing/mismatched package-source files copied'})
print('PREPARED; no mother checkout/pull/stage or production changes',flush=True)
