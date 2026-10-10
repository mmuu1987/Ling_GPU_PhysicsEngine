from pathlib import Path
import json,hashlib,subprocess,concurrent.futures,os,datetime,csv,statistics
R=Path(__file__).resolve().parents[2];O=R/'outputs/P9Combined-20261009-01';P=R/'outputs/P12';D=R/'outputs/P9ColdFix-20261009-01'
assert subprocess.check_output(['powershell','-NoProfile','-Command',"@(Get-Process Unity,WarSandbox -ErrorAction SilentlyContinue).Count"],text=True).strip()=='0'
assert not(D/'host-final-audit.json').exists()
b=json.loads((O/'mother-before.json').read_text(encoding='utf-8'));src=json.loads((O/'source-manifest.json').read_text(encoding='utf-8'))['files'];baseh=json.loads((O/'harness-manifest.json').read_text(encoding='utf-8'));patch=json.loads((D/'patch-manifest.json').read_text(encoding='utf-8'))
h=dict(baseh)
for f in ['prototype-harness.json','diagnostic-harness.json']:h.update(json.loads((D/f).read_text(encoding='utf-8')))
def sha(p):
 if not p.is_file():return None
 before=p.stat();v=hashlib.sha256()
 with p.open('rb') as f:
  for data in iter(lambda:f.read(1048576),b''):v.update(data)
 after=p.stat();assert (before.st_size,before.st_mtime_ns)==(after.st_size,after.st_mtime_ns),str(p)
 return v.hexdigest()
def check(row):
 root,f,want=row;got=sha(root/f)
 return {'path':f,'expected':want,'actual':got} if got!=want else None
expected={f:v['sha256'] for f,v in src.items()};expected.update(h);expected.update(patch['files'])
print('Full byte-hash audit: mother, P12 declared candidate, P11 unchanged sources.',flush=True)
with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool:
 mother=[v for v in pool.map(check,[(R,f,x) for f,x in b['files'].items()]) if v]
 candidate=[v for v in pool.map(check,[(P,f,x) for f,x in expected.items()]) if v]
 p11=[v for v in pool.map(check,[(R/'outputs/P11',f,x['sha256']) for f,x in src.items()]) if v]
files=lambda root:{p.relative_to(root).as_posix() for folder in ['Assets','Packages','ProjectSettings'] for p in (root/folder).rglob('*') if p.is_file()}
user=Path(b['userRoot']);uf={p.relative_to(user).as_posix():sha(p) for p in user.rglob('*') if p.is_file()};env=os.environ.copy();env['GIT_OPTIONAL_LOCKS']='0'
runs={}
for name in ['fixed-membership-01','fixed-golden-01','fixed-regression-01','fixed-hosttrace-01','fixed-hosttrace-02','fixed-cold-01','fixed-cold-02','fixed-cold-03','fixed-cold-04','fixed-joint-01','fixed-repeat-01','fixed-membership-02','fixed-regression-02','fixed-hosttrace-03']:
 s=json.loads((D/(name+'.json')).read_text(encoding='utf-8'));assert s['passed'] and s['motherProtection']['passed'],name
 runs[name]={'cases':int(s['tests']['passed']),'seconds':float(s['tests']['duration']),'passed':True,'renderedGui':s['renderedGui'],'burstOptIn':s['burstOptIn']}
rows=[]
V=P/'Logs/InteractionRefinement-20261007/P8/cold-fix-fixed-repeat-01'
for run in range(1,4):
 samples=list(csv.DictReader((V/('repeat-'+str(run)+'-frames.csv')).read_text(encoding='utf-8-sig').splitlines()));assert len(samples)==300
 dt=sorted(float(x['wall_ms']) for x in samples)
 rows.append({'repeat':run,'frames':300,'medianWallMs':statistics.median(dt),'p95WallMs':dt[int((len(dt)-1)*.95)],'maxWallMs':max(dt),'lastSelectionBytes':samples[-1]['selection_bytes'],'lastLocalBufferBytes':samples[-1]['local_bytes']})
resources=list(csv.DictReader((V/'repeat-resources.csv').read_text(encoding='utf-8-sig').splitlines()));assert len(resources)==3
cache=json.loads((D/'host-cache-aba.json').read_text(encoding='utf-8'));assert cache['cacheRestoredExactly'] and cache['originalStillExactAfterRestoredRun'] and not(D/'host-combat-cache-escrow').exists()
s={'finished':datetime.datetime.now().isoformat(),'mode':'Full SHA256, including non-code assets','patchRevision':patch.get('revision',1),'declaredPatchFiles':len(patch['files']),'expectedP12Files':len(expected),'motherChanges':mother,'motherFileSetDelta':sorted(files(R)^{f for f in b['files'] if f.split('/')[0] in ['Assets','Packages','ProjectSettings']}),'candidateChanges':candidate,'candidateFileSetDelta':sorted(files(P)^set(expected)),'p11SourceChanges':p11,'userFilesUnchanged':uf==b['userFiles'],'headUnchanged':subprocess.check_output(['git','rev-parse','HEAD'],cwd=R,env=env,text=True).strip()==b['head'],'indexUnchanged':sha(R/'.git/index')==b['index'],'cacheEscrowAbsentAndABAExactlyRestored':True,'tests':runs,'renderedRepeatedSamples':rows,'resourcesAfterUnload':resources,'noOwnedUnityRemaining':True,'coldAcceptancePassed':False,'scope':'Editor D3D11 managed candidate only; residual cold stalls remain. No release/merge, no Player/AOT or human sign-off.'}
s['protectionPassed']=not mother and not s['motherFileSetDelta'] and not candidate and not s['candidateFileSetDelta'] and not p11 and s['userFilesUnchanged'] and s['headUnchanged'] and s['indexUnchanged']
(D/'host-final-audit.json').write_text(json.dumps(s,ensure_ascii=False,indent=2),encoding='utf-8');print(json.dumps(s,ensure_ascii=False,indent=2),flush=True)
assert s['protectionPassed']
