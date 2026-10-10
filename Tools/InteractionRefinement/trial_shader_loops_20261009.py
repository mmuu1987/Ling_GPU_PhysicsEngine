from pathlib import Path
import subprocess,json,hashlib,sys,datetime
R=Path(__file__).resolve().parents[2];P=R/'outputs/P12';Q=R/'outputs/P9FirstCommand-20261009-01';D=R/'outputs/P9ColdFix-20261009-01';EVID=P/'Logs/InteractionRefinement-20261007/P8'
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
def idle():return subprocess.check_output(['powershell','-NoProfile','-Command',"@(Get-Process Unity,WarSandbox -ErrorAction SilentlyContinue).Count"],text=True).strip()=='0'
def save(n,v):(Q/n).write_text(json.dumps(v,ensure_ascii=False,indent=2),encoding='utf-8')
def files(root):return {p.relative_to(root).as_posix():{'sha256':sha(p),'size':p.stat().st_size,'mtime_ns':p.stat().st_mtime_ns} for p in root.rglob('*') if p.is_file()}
assert idle();assert not(Q/'shader-loop-trial.json').exists()
manifest=json.loads((Q/'candidate-manifest.json').read_text(encoding='utf-8'));assert manifest['name']=='first-command-navigation-sharing-candidate-01' and not manifest.get('shaderPolicyTrial')
assert all(sha(P/f)==v for f,v in manifest['files'].items())
changed=['Assets/MassEngine/Simulation/Shaders/AgentCombatSimulation.compute','Assets/MassEngine/Core/Shaders/AgentDataCommon.hlsl','Assets/MassEngine/Terrain/Shaders/TerrainNavigation.hlsl']
variant=Q/'compiler-loop-screen/local-rolled-loops';assert variant.is_dir()
base=Q/'loop-trial-baseline';
for rel in changed:
 p=base/rel;p.parent.mkdir(parents=True,exist_ok=True);p.write_bytes((P/rel).read_bytes())
save('navigation-only-before-loop-trial.json',manifest)
for rel in changed:
 src=variant/rel;assert src.is_file(),str(src);(P/rel).write_bytes(src.read_bytes())
trial=dict(manifest);trial['name']='navigation-plus-roll-loops-trial';trial['shaderLoopTrial']=True;trial['accepted']=False;trial['files']=dict(manifest['files']);trial['files'].update({f:sha(P/f) for f in changed});trial['additionalModifiedFiles']=manifest['additionalModifiedFiles']+changed;trial['scope']='Temporary loop-structure trial only; reverts automatically after parity/cold/warm tests.';save('candidate-manifest.json',trial)
journal={'baselineFiles':{f:sha(base/f) for f in changed},'trialFiles':{f:sha(P/f) for f in changed},'status':'installed','accepted':False,'coldRun':'fixed-firstcold-04','warmRun':'fixed-firstcold-05','parityRun':'fixed-unified-01'};save('shader-loop-trial.json',journal)
try:
 print('Run exact GPU parity against global reference, all 144 cases.',flush=True)
 journal['parityExit']=subprocess.call([sys.executable,str(R/'Tools/InteractionRefinement/p9_first_command_run_20261009.py'),'fixed-unified-01'],cwd=R);save('shader-loop-trial.json',journal)
 assert journal['parityExit']==0,'Parity failure; trial will revert'
 assert idle()
 cache=P/'Library/ShaderCache/compute/AgentCombatSimulation79e5';old=Q/'loop-trial-cache-baseline';generated=Q/'loop-trial-cache-generated';assert cache.is_dir() and not old.exists() and not generated.exists()
 originalCache=files(cache);save('shader-loop-cache-before.json',originalCache);cache.rename(old);journal['cacheEscrowed']=True;save('shader-loop-trial.json',journal)
 try:
  print('Run cold first-use operation with the three loop-only shader variants.',flush=True)
  journal['coldExit']=subprocess.call([sys.executable,str(R/'Tools/InteractionRefinement/p9_first_command_run_20261009.py'),'fixed-firstcold-04'],cwd=R);save('shader-loop-trial.json',journal)
  assert idle()
  print('Run the same first-operation sequence warm, to catch runtime cost changes.',flush=True)
  journal['warmExit']=subprocess.call([sys.executable,str(R/'Tools/InteractionRefinement/p9_first_command_run_20261009.py'),'fixed-firstcold-05'],cwd=R);save('shader-loop-trial.json',journal)
 finally:
  assert idle(),'Do not touch cache while Unity is active'
  if cache.exists():journal['generatedCache']=files(cache);cache.rename(generated)
  assert files(old)==originalCache and not cache.exists();old.rename(cache);journal['cacheRestored']=files(cache)==originalCache;assert journal['cacheRestored'];save('shader-loop-trial.json',journal)
 assert journal['coldExit']==0 and journal['warmExit']==0
except Exception as ex:
 journal['error']=repr(ex);save('shader-loop-trial.json',journal);raise
finally:
 assert idle(),'Do not overwrite candidate while Unity is active'
 for rel in changed:(P/rel).write_bytes((base/rel).read_bytes())
 (Q/'candidate-manifest.json').write_bytes((Q/'navigation-only-before-loop-trial.json').read_bytes())
 journal['shaderSourcesRestored']=all(sha(P/f)==h for f,h in journal['baselineFiles'].items());journal['navCandidateManifestRestored']=json.loads((Q/'candidate-manifest.json').read_text(encoding='utf-8'))['name']=='first-command-navigation-sharing-candidate-01';journal['status']='reverted';journal['finished']=datetime.datetime.now().isoformat();save('shader-loop-trial.json',journal)
print(json.dumps(journal,ensure_ascii=False,indent=2),flush=True)
