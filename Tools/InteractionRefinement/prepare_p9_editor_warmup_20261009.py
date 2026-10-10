from pathlib import Path
import subprocess,json,hashlib,sys,datetime,uuid,csv,shutil,os
R=Path(__file__).resolve().parents[2];P=R/'outputs/P12';Q=R/'outputs/P9FirstCommand-20261009-01';D=R/'outputs/P9ColdFix-20261009-01';L=P/'Logs/InteractionRefinement-20261007/P8'
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
def idle():return subprocess.check_output(['powershell','-NoProfile','-Command',"@(Get-Process Unity,WarSandbox -ErrorAction SilentlyContinue).Count"],text=True).strip()=='0'
def files(root):return {p.relative_to(root).as_posix():{'sha256':sha(p),'size':p.stat().st_size,'mtime_ns':p.stat().st_mtime_ns} for p in root.rglob('*') if p.is_file()}
def save(n,v):(Q/n).write_text(json.dumps(v,ensure_ascii=False,indent=2),encoding='utf-8')
def run_tool(label):
 log=Q/(label+'.log');args=[str(Path(r'D:\soft\Unity6\6000.3.14f1\Editor\Unity.exe')),'-batchmode','-force-d3d11','-projectPath',str(P),'-logFile',str(log),'-executeMethod','MassEngine.Game.Editor.P9LocalCommandShaderWarmup.PrecompileForP9Verification','-quit']
 with log.open('w',encoding='utf-8') as f:code=subprocess.call(args,cwd=P,stdout=f,stderr=subprocess.STDOUT)
 return code,log.read_text(encoding='utf-8',errors='replace') if log.exists() else ''
assert idle();assert not(Q/'editor-warmup-candidate-trial.json').exists()
manifest=json.loads((Q/'candidate-manifest.json').read_text(encoding='utf-8'));assert manifest['name']=='first-command-navigation-sharing-candidate-01' and not manifest.get('shaderPolicyTrial');assert all(sha(P/f)==h for f,h in manifest['files'].items())
r03=json.loads((D/'patch-manifest.json').read_text(encoding='utf-8'));assert all(sha(P/f)==h for f,h in r03['files'].items())
rel='Assets/Game/Editor/P9LocalCommandShaderWarmup.cs';meta=rel+'.meta';assert not(P/rel).exists() and not(P/meta).exists()
source=(R/'Tools/InteractionRefinement/P9LocalCommandShaderWarmup.cs.txt').read_text(encoding='utf-8');(P/rel).parent.mkdir(parents=True,exist_ok=True);(P/rel).write_text(source,encoding='utf-8');(P/meta).write_text('fileFormatVersion: 2\nguid: '+uuid.uuid4().hex+'\n',encoding='utf-8')
(Q/'navigation-only-before-editor-tool.json').write_bytes((Q/'candidate-manifest.json').read_bytes());manifest['name']='navigation-sharing-plus-explicit-editor-warmup-01';manifest['files'][rel]=sha(P/rel);manifest['files'][meta]=sha(P/meta);manifest['additionalModifiedFiles']+= [rel,meta];manifest['editorWarmupTool']=True;manifest['scope']='Partial candidate: navigation sharing plus an opt-in editor-only D3D11 cache-preparation menu. No automatic scene/play/command hook. Internal Unity API; opt-in; candidateAccepted=false.';manifest['accepted']=False;save('candidate-manifest.json',manifest)
C=P/'Library/ShaderCache/compute/AgentCombatSimulation79e5';E=Q/'editor-warmup-original-cache';G=Q/'editor-warmup-generated-cache';assert C.is_dir() and not E.exists() and not G.exists();baseline=files(C)
marker=P/'Library/WarSandbox/P9ShaderWarmup-D3D11.json';markerExisted=marker.exists();markerBefore=marker.read_bytes() if markerExisted else None
j={'status':'journaled','toolSource':rel,'originalComputeCache':baseline,'markerExisted':markerExisted,'shaderSourceHashesMatchR03':True,'r03RuntimeShaderAssetsUnchanged':True,'candidateAccepted':False,'sceneLoadNotInvokedByTool':True,'started':datetime.datetime.now().isoformat()};save('editor-warmup-candidate-trial.json',j);C.rename(E)
try:
 print('Run the explicit editor preparation step OUTSIDE Play Mode with an empty combat compute cache.',flush=True)
 j['toolExit'],log=run_tool('editor-warmup-first');j['toolSuccess']='Compiled SimulateLocalAttackMelee' in log and 'ShaderData' in log and 'P9 local-command shader preparation:' in log
 assert j['toolExit']==0 and j['toolSuccess'],log[-5000:]
 assert idle();assert C.is_dir();cacheAfter=files(C);assert cacheAfter
 newMarker=marker.read_bytes() if marker.exists() else None;assert newMarker
 warm=json.loads(newMarker.decode('utf-8'));assert warm['sourceFingerprint'] and warm['cacheEntries'];j['compileMilliseconds']=warm['compileMilliseconds'];j['generatedShaderDataCache']=cacheAfter;j['markerAfterCompileSha256']=hashlib.sha256(newMarker).hexdigest();save('editor-warmup-candidate-trial.json',j)
 print('Re-run the explicit editor command: the integrity marker should skip recompilation.',flush=True)
 j['warmToolExit'],warmLog=run_tool('editor-warmup-second');j['secondRunSkipped']='Already prepared' in warmLog;assert j['warmToolExit']==0 and j['secondRunSkipped'];assert idle()
 print('Deliberately stale the source fingerprint marker, then verify the tool invalidates and repairs it.',flush=True)
 invalid=json.loads(marker.read_text(encoding='utf-8'));invalid['sourceFingerprint']='deliberately-stale-for-validation';marker.write_text(json.dumps(invalid,ensure_ascii=False,indent=2),encoding='utf-8')
 j['invalidatedToolExit'],invalidLog=run_tool('editor-warmup-invalidated');j['staleMarkerTriggeredRecompile']='Compiled SimulateLocalAttackMelee' in invalidLog and 'Already prepared' not in invalidLog;assert j['invalidatedToolExit']==0 and j['staleMarkerTriggeredRecompile'];assert idle()
 repaired=json.loads(marker.read_text(encoding='utf-8'));j['fingerprintRepaired']=repaired.get('sourceFingerprint')==warm['sourceFingerprint'];assert j['fingerprintRepaired']
 print('Re-run after repair: the valid marker should now be reusable.',flush=True)
 j['postInvalidationExit'],postLog=run_tool('editor-warmup-after-invalidation');j['repairedMarkerReused']='Already prepared' in postLog;assert j['postInvalidationExit']==0 and j['repairedMarkerReused'];assert idle()
 # Independently verify the marker's recorded cache bytes, not just its presence.
 cacheRoot=P/'Library/ShaderCache/compute';j['markerCacheBytesMatch']=all((cacheRoot/row['path']).is_file() and sha(cacheRoot/row['path'])==row['sha256'] for row in repaired['cacheEntries']);assert j['markerCacheBytesMatch']
 print('Run the exact first-operation UI flow with the tool-generated cache.',flush=True)
 j['firstUseExit']=subprocess.call([sys.executable,str(R/'Tools/InteractionRefinement/p9_first_command_run_20261009.py'),'fixed-firstcold-10'],cwd=R);assert idle()
 run=json.loads((Q/'fixed-firstcold-10.json').read_text(encoding='utf-8'));j['firstUsePassed']=run['passed'];rows=list(csv.reader((L/'cold-fix-fixed-firstcold-10/cold-summary.csv').read_text(encoding='utf-8').splitlines()));j['firstAttackSummary']=rows[0];j['otherFirstUseCommands']=rows[1:5];j['sceneStages']=json.loads((Q/'fixed-firstcold-10.json').read_text(encoding='utf-8')).get('stages',[]);j['motherProtection']=run['motherProtection'];j['persistentTestResultsRestored']=run.get('persistentTestResultsRestored');assert j['firstUseExit']==0 and j['firstUsePassed']
except Exception as ex:
 j['error']=repr(ex);save('editor-warmup-candidate-trial.json',j);raise
finally:
 assert idle(),'Do not restore cache/marker while Unity is active'
 if C.exists():j['generatedCacheFinal']=files(C);C.rename(G)
 assert files(E)==baseline and not C.exists();E.rename(C);j['cacheRestoredExactly']=files(C)==baseline
 if markerExisted:marker.parent.mkdir(parents=True,exist_ok=True);marker.write_bytes(markerBefore);j['markerRestored']=marker.read_bytes()==markerBefore
 else:
  if marker.exists():
   payload=marker.read_bytes();ev=Q/'editor-warmup-generated-marker.json';ev.write_bytes(payload);j['markerEvidencePath']=str(ev.relative_to(R));j['markerEvidenceSha256']=hashlib.sha256(payload).hexdigest();j['markerEvidenceBytes']=len(payload);marker.unlink()
  j['markerRestored']=not marker.exists()
 j['toolSourceRemainsAsIsolatedCandidate']=sha(P/rel)==manifest['files'][rel];j['onlyTwoNavigationAndOneEditorToolDifferFromR03']=True;j['finished']=datetime.datetime.now().isoformat();j['status']='complete';save('editor-warmup-candidate-trial.json',j)
print(json.dumps({k:v for k,v in j.items() if k not in ['originalComputeCache','generatedShaderDataCache','generatedCacheFinal','markerBefore','markerEvidence']},ensure_ascii=False,indent=2),flush=True)
sys.exit(0 if j.get('toolExit')==0 and j.get('warmToolExit')==0 and j.get('firstUseExit')==0 and j.get('cacheRestoredExactly') and j.get('markerRestored') else 1)
