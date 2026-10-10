from pathlib import Path
import subprocess,json,hashlib,sys,shutil,datetime,csv
R=Path(__file__).resolve().parents[2];P=R/'outputs/P12';Q=R/'outputs/P9FirstCommand-20261009-01';L=P/'Logs/InteractionRefinement-20261007/P8'
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
def idle():return subprocess.check_output(['powershell','-NoProfile','-Command',"@(Get-Process Unity,WarSandbox -ErrorAction SilentlyContinue).Count"],text=True).strip()=='0'
def files(root):return {p.relative_to(root).as_posix():{'sha256':sha(p),'size':p.stat().st_size,'mtime_ns':p.stat().st_mtime_ns} for p in root.rglob('*') if p.is_file()}
def save(v):(Q/'shaderutil-cache-hit-test.json').write_text(json.dumps(v,ensure_ascii=False,indent=2),encoding='utf-8')
assert idle();assert not(Q/'shaderutil-cache-hit-test.json').exists()
man=json.loads((Q/'candidate-manifest.json').read_text(encoding='utf-8'));assert man['name']=='first-command-navigation-sharing-candidate-01' and all(sha(P/f)==h for f,h in man['files'].items())
r03=json.loads((R/'outputs/P9ColdFix-20261009-01/patch-manifest.json').read_text(encoding='utf-8'));assert all(sha(P/f)==h for f,h in r03['files'].items())
C=P/'Library/ShaderCache/compute/AgentCombatSimulation79e5';E=Q/'shaderutil-cachehit-original';assert C.is_dir() and not E.exists();baseline=files(C)
gen=Q/'shaderutil-generated-cache';gmanifest=json.loads((Q/'shaderutil-probe-transaction.json').read_text(encoding='utf-8'))['generatedCacheFinal'];assert gen.is_dir() and gmanifest
j={'status':'prepared','variant':'SimulateLocalAttackMelee / D3D11 / MASS_TERRAIN_ENABLED + MASS_LOCAL_ORDERS','firstUseRun':'fixed-firstcold-09','baselineCache':baseline,'generatedVariantCache':gmanifest,'cacheOnlyExperiment':True,'productionCodeUnchanged':True,'started':datetime.datetime.now().isoformat()};C.rename(E);shutil.copytree(E,C)
try:
 for rel,row in gmanifest.items():
  source=gen/rel;dest=C/rel;assert source.is_file() and sha(source)==row['sha256'];dest.parent.mkdir(parents=True,exist_ok=True)
  if dest.exists():assert sha(dest)==row['sha256'],'Pre-existing variant cache key has different bytes'
  else:shutil.copy2(source,dest)
 j['cacheWithInjectedCompileEntry']=files(C);j['injectedPaths']=list(gmanifest);j['status']='cache-entry-installed';save(j)
 print('Run final cold UI workflow with only the ShaderUtil-produced Attack variant added to the project cache.',flush=True)
 j['runnerExit']=subprocess.call([sys.executable,str(R/'Tools/InteractionRefinement/p9_first_command_run_20261009.py'),'fixed-firstcold-09'],cwd=R);assert idle()
 run=json.loads((Q/'fixed-firstcold-09.json').read_text(encoding='utf-8'));j['runnerPassed']=run['passed'];j['motherProtection']=run['motherProtection'];rows=list(csv.reader((L/'cold-fix-fixed-firstcold-09/cold-summary.csv').read_text(encoding='utf-8').splitlines()));j['firstAttackSummary']=rows[0];j['otherFirstUseCommands']=rows[1:5];j['status']='measured';save(j)
finally:
 assert idle(),'Do not touch cache while Unity is active'
 if C.exists():j['generatedAfterTest']=files(C);shutil.rmtree(C)
 E.rename(C);j['cacheRestoredExactly']=files(C)==baseline;assert j['cacheRestoredExactly'];j['finished']=datetime.datetime.now().isoformat();j['status']='restored';save(j)
print(json.dumps({k:v for k,v in j.items() if k not in ['baselineCache','generatedVariantCache','cacheWithInjectedCompileEntry','generatedAfterTest']},ensure_ascii=False,indent=2),flush=True)
sys.exit(0 if j.get('runnerExit')==0 and j.get('runnerPassed') and j.get('cacheRestoredExactly') else 1)
