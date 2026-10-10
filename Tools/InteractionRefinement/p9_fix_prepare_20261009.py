from pathlib import Path
import os,sys,json,hashlib,shutil,subprocess,time
R=Path(__file__).resolve().parents[2];S=R/'outputs/P11';P=R/'outputs/P12';D=R/'outputs/P9ColdFix-20261009-01'
assert not P.exists();assert subprocess.check_output(['powershell','-NoProfile','-Command',"@(Get-Process Unity,WarSandbox -ErrorAction SilentlyContinue).Count"],text=True).strip()=='0'
src=json.loads((R/'outputs/P9Combined-20261009-01/source-manifest.json').read_text(encoding='utf-8'))['files'];h=json.loads((R/'outputs/P9Combined-20261009-01/harness-manifest.json').read_text(encoding='utf-8'));expected={f:x['sha256'] for f,x in src.items()};expected.update(h)
def lp(p):return Path('\\\\?\\'+str(p.resolve()))
def sha(p):
 v=hashlib.sha256()
 with lp(p).open('rb') as f:
  for b in iter(lambda:f.read(1048576),b''):v.update(b)
 return v.hexdigest()
cache=[]
for root,dirs,files in os.walk(lp(S/'Library')):
 rel=Path(root).relative_to(lp(S/'Library'))
 if rel==Path('.'):
  dirs[:]=[d for d in dirs if d not in ['Bee','BurstCache','Search','Collab','Il2cppBuildCache']]
 for f in files:cache.append((Path(root)/f,Path('Library')/rel/f))
total=sum((S/f).stat().st_size for f in expected)+sum(f.stat().st_size for f,_ in cache)
assert shutil.disk_usage(R).free>total+4*1024**3,'Not enough free disk for independent copy'
P.mkdir();print('COPY independent P12',len(expected),'sources',len(cache),'cache files',total,'bytes',flush=True)
for f,want in expected.items():
 assert sha(S/f)==want,f;t=P/f;t.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(lp(S/f),lp(t));assert sha(t)==want
print('ALL SOURCES VERIFIED; COPY READ-ONLY CACHE SEED',flush=True)
for f,rel in cache:
 t=lp(P/rel);t.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(f,t)
(D/'prepare.json').write_text(json.dumps({'project':str(P),'source':str(S),'sourceFiles':expected,'cacheSeedFiles':len(cache),'totalCopiedBytes':total,'sourceExactBeforeFix':True,'noLinksOrSharedWritableFiles':True},indent=2),encoding='utf-8')
print('P12 independent copy ready. P11 and mother unchanged.',flush=True)
