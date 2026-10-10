from pathlib import Path
import subprocess,json,hashlib,sys,datetime
R=Path(__file__).resolve().parents[2];P=R/'outputs/P12';D=R/'outputs/P9ColdFix-20261009-01';old=R/'outputs/P9ColdAttack-20261009-01';runner=R/'Tools/InteractionRefinement/p9_fix_run_v13_20261009.py'
def idle():return subprocess.check_output(['powershell','-NoProfile','-Command',"@(Get-Process Unity,WarSandbox -ErrorAction SilentlyContinue).Count"],text=True).strip()=='0'
def manifest(p):return {f.relative_to(p).as_posix():{'sha256':hashlib.sha256(f.read_bytes()).hexdigest(),'size':f.stat().st_size,'mtime_ns':f.stat().st_mtime_ns} for f in p.rglob('*') if f.is_file()}
def save(n,v):(D/n).write_text(json.dumps(v,indent=2),encoding='utf-8')
assert idle();assert json.loads((D/'fixed-hosttrace-04.json').read_text(encoding='utf-8'))['passed']
h=json.loads((D/'diagnostic-harness.json').read_text(encoding='utf-8'));assert all(hashlib.sha256((P/f).read_bytes()).hexdigest()==v for f,v in h.items())
for name in ['fixed-cold-05','fixed-cold-06']:
 code=subprocess.call([sys.executable,str(runner),name],cwd=R)
 if code:sys.exit(code)
C=P/'Library/ShaderCache/compute/AgentCombatSimulation79e5';E=D/'host-r03-combat-cache-escrow';G=D/'host-r03-combat-cache-generated-cold'
assert idle() and C.is_dir() and not E.exists() and not G.exists() and not(D/'host-r03-cache-aba.json').exists()
before=manifest(C);assert before
s={'started':datetime.datetime.now().isoformat(),'scope':str(C),'initialSeededRun':'fixed-cold-05','warmA':'fixed-cold-06','coldB':'fixed-cold-07','restoredA':'fixed-cold-08','original':before,'originalFiles':len(before),'originalBytes':sum(v['size'] for v in before.values()),'globalOrDriverCacheTouched':False,'status':'journaled'};save('host-r03-cache-aba.json',s)
C.rename(E);s['status']='escrowed';save('host-r03-cache-aba.json',s)
try:
 print('Only P12 combat cache escrowed. Running true cold B.',flush=True)
 s['coldExit']=subprocess.call([sys.executable,str(runner),'fixed-cold-07'],cwd=R)
finally:
 assert idle(),'Owned restoration deferred: other Editor active, do not overwrite'
 if C.exists():s['generated']=manifest(C);C.rename(G)
 assert manifest(E)==before and not C.exists();E.rename(C)
 s['cacheRestoredExactly']=manifest(C)==before;assert s['cacheRestoredExactly'];s['status']='restored';save('host-r03-cache-aba.json',s)
s['restoredExit']=subprocess.call([sys.executable,str(runner),'fixed-cold-08'],cwd=R)
s['afterRestoredRun']=manifest(C);s['originalStillExactAfterRestoredRun']=s['afterRestoredRun']==before;s['status']='complete';s['finished']=datetime.datetime.now().isoformat();save('host-r03-cache-aba.json',s)
print(json.dumps({k:v for k,v in s.items() if k not in ['original','generated','afterRestoredRun']},indent=2),flush=True)
sys.exit(0 if s['coldExit']==0 and s['restoredExit']==0 and s['originalStillExactAfterRestoredRun'] else 1)
