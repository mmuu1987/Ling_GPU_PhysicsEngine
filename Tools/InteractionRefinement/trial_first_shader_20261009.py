from pathlib import Path
import subprocess,json,hashlib,sys,datetime
R=Path(__file__).resolve().parents[2];P=R/'outputs/P12';Q=R/'outputs/P9FirstCommand-20261009-01'
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
def idle():return subprocess.check_output(['powershell','-NoProfile','-Command',"@(Get-Process Unity,WarSandbox -ErrorAction SilentlyContinue).Count"],text=True).strip()=='0'
def save(n,v):(Q/n).write_text(json.dumps(v,indent=2),encoding='utf-8')
def manifest(p):return {f.relative_to(p).as_posix():{'sha256':sha(f),'size':f.stat().st_size,'mtime_ns':f.stat().st_mtime_ns} for f in p.rglob('*') if f.is_file()}
assert idle()
m=json.loads((Q/'candidate-manifest.json').read_text(encoding='utf-8'));assert all(sha(P/f)==h for f,h in m['files'].items())
f='Assets/MassEngine/Simulation/Shaders/AgentCombatSimulation.compute';backup=Q/'baseline'/f;assert not backup.exists();backup.parent.mkdir(parents=True,exist_ok=True);backup.write_bytes((P/f).read_bytes())
(Q/'navigation-only-manifest.json').write_bytes((Q/'candidate-manifest.json').read_bytes())
s=(P/f).read_text(encoding='utf-8');needle='#pragma multi_compile __ MASS_TERRAIN_ENABLED';assert s.count(needle)==1
s=s.replace(needle,'''// Candidate: reduce first-use compiler optimization work only for typed local kernels.
// Must pass Unity cold timing, unchanged global bytecode, GPU parity and warm cost gates.
#if defined(LP_ATTACKMELEE) || defined(LP_ATTACKRANGED) || defined(LP_MOVEMELEE) || defined(LP_MOVERANGED) || defined(LP_HOLDMELEE) || defined(LP_HOLDRANGED)
#pragma skip_optimizations d3d11
#endif
'''+needle)
(P/f).write_bytes(s.encode('utf-8'));m['files'][f]=sha(P/f);m['shaderPolicyTrial']=True;m['accepted']=False;m['scope']='Navigation sharing plus CONDITIONAL shader compiler-policy TRIAL; NOT accepted, requires cold/GPU/warm verification.';save('candidate-manifest.json',m)
C=P/'Library/ShaderCache/compute/AgentCombatSimulation79e5';E=Q/'trial-cache-escrow';G=Q/'trial-cache-generated';assert C.is_dir() and not E.exists() and not G.exists()
before=manifest(C);assert before
journal={'scope':str(C),'original':before,'status':'journaled','globalOrDriverCacheTouched':False};save('shader-trial-cache.json',journal)
C.rename(E);journal['status']='escrowed';save('shader-trial-cache.json',journal)
try:
 print('Conditional compiler policy TRIAL; exact original combat cache escrowed; original UI prefix cold test.',flush=True)
 journal['exitCode']=subprocess.call([sys.executable,str(R/'Tools/InteractionRefinement/p9_first_command_run_20261009.py'),'fixed-firstcold-01'],cwd=R)
finally:
 assert idle(),'Do not overwrite cache while another Editor is active'
 if C.exists():journal['generated']=manifest(C);C.rename(G)
 assert manifest(E)==before and not C.exists();E.rename(C);journal['cacheRestoredExactly']=manifest(C)==before;assert journal['cacheRestoredExactly'];journal['status']='restored';save('shader-trial-cache.json',journal)
print(json.dumps({k:v for k,v in journal.items() if k not in ['original','generated']},indent=2),flush=True)
sys.exit(journal['exitCode'])
