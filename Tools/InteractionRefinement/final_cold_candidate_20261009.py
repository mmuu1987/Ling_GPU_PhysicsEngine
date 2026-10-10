from pathlib import Path
import subprocess,json,hashlib,sys,datetime
R=Path(__file__).resolve().parents[2];P=R/'outputs/P12';Q=R/'outputs/P9FirstCommand-20261009-01';D=R/'outputs/P9ColdFix-20261009-01'
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
def idle():return subprocess.check_output(['powershell','-NoProfile','-Command',"@(Get-Process Unity,WarSandbox -ErrorAction SilentlyContinue).Count"],text=True).strip()=='0'
def files(root):return {p.relative_to(root).as_posix():{'sha256':sha(p),'size':p.stat().st_size,'mtime_ns':p.stat().st_mtime_ns} for p in root.rglob('*') if p.is_file()}
def save(v):(Q/'final-cold-navigation-only.json').write_text(json.dumps(v,ensure_ascii=False,indent=2),encoding='utf-8')
assert idle()
m=json.loads((Q/'candidate-manifest.json').read_text(encoding='utf-8'));assert m['name']=='first-command-navigation-sharing-candidate-01' and not m.get('shaderPolicyTrial')
assert all(sha(P/f)==h for f,h in m['files'].items())
base=json.loads((D/'patch-manifest.json').read_text(encoding='utf-8'));assert all(sha(P/f)==h for f,h in base['files'].items())
C=P/'Library/ShaderCache/compute/AgentCombatSimulation79e5';E=Q/'final-navigation-cold-cache-original';G=Q/'final-navigation-cold-cache-generated';assert C.is_dir() and not E.exists() and not G.exists()
before=files(C);assert before
j={'status':'journaled','run':'fixed-firstcold-08','navigationCandidateOnly':True,'candidateAccepted':False,'shaderFilesMatchR03':True,'originalCache':before,'globalOrDriverCacheTouched':False,'started':datetime.datetime.now().isoformat()};save(j);C.rename(E);j['status']='escrowed';save(j)
try:
 j['exitCode']=subprocess.call([sys.executable,str(R/'Tools/InteractionRefinement/p9_first_command_run_20261009.py'),'fixed-firstcold-08'],cwd=R)
finally:
 assert idle(),'Do not restore cache while Unity process is active'
 if C.exists():j['generatedCache']=files(C);C.rename(G)
 assert files(E)==before and not C.exists();E.rename(C);j['cacheRestoredExactly']=files(C)==before;assert j['cacheRestoredExactly'];j['status']='complete';j['finished']=datetime.datetime.now().isoformat();save(j)
print(json.dumps({k:v for k,v in j.items() if k not in ['originalCache','generatedCache']},ensure_ascii=False,indent=2),flush=True)
sys.exit(j['exitCode'])
