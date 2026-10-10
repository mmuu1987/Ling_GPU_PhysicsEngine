from pathlib import Path
import subprocess,json,hashlib,sys,datetime
R=Path(__file__).resolve().parents[2];P=R/'outputs/P11';D=R/'outputs/P9ColdAttack-20261009-01';C=P/'Library/ShaderCache/compute/AgentCombatSimulation79e5';E=D/'combat-cache-escrow';G=D/'combat-cache-generated-cold';runner=R/'Tools/InteractionRefinement/p9_cold_run_v2_20261009.py'
def no_editor():
 q=subprocess.check_output(['powershell','-NoProfile','-Command',"@(Get-Process Unity,WarSandbox -ErrorAction SilentlyContinue).Count"],text=True);return q.strip()=='0'
def manifest(p):return {f.relative_to(p).as_posix():{'sha256':hashlib.sha256(f.read_bytes()).hexdigest(),'size':f.stat().st_size,'mtime_ns':f.stat().st_mtime_ns} for f in p.rglob('*') if f.is_file()}
def save(n,x):(D/n).write_text(json.dumps(x,indent=2),encoding='utf-8')
assert json.loads((D/'warm-02.json').read_text(encoding='utf-8'))['passed']
assert no_editor() and C.is_dir() and not E.exists() and not G.exists()
before=manifest(C);assert before
state={'scope':str(C),'original':before,'originalFiles':len(before),'originalBytes':sum(x['size'] for x in before.values()),'status':'journaled','started':datetime.datetime.now().isoformat(),'globalOrDriverCacheTouched':False}
save('cache-aba.json',state)
C.rename(E);state['status']='cold-cache-escrowed';save('cache-aba.json',state)
try:
 print('Only P11 AgentCombatSimulation compute cache escrowed; cold-01 now.',flush=True)
 state['coldExit']=subprocess.call([sys.executable,str(runner),'cold-01'],cwd=R)
finally:
 assert no_editor(),'Restore blocked: active Editor; cache remains escrowed, no overwrite'
 if C.exists():state['generated']=manifest(C);C.rename(G)
 assert manifest(E)==before and not C.exists()
 E.rename(C);state['cacheRestoredExactly']=manifest(C)==before;assert state['cacheRestoredExactly']
 state['status']='original-cache-restored';save('cache-aba.json',state)
 print('Original combat cache restored exactly.',flush=True)
state['restoredExit']=subprocess.call([sys.executable,str(runner),'restored-01'],cwd=R)
state['afterRestoredRun']=manifest(C);state['status']='aba-complete';state['finished']=datetime.datetime.now().isoformat();save('cache-aba.json',state)
sys.exit(0 if state.get('coldExit')==0 and state['restoredExit']==0 else 1)
