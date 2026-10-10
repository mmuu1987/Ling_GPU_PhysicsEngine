from pathlib import Path
import subprocess,json,hashlib,sys,datetime,csv,shutil,re
R=Path(__file__).resolve().parents[2];P=R/'outputs/P12';Q=R/'outputs/P9FirstCommand-20261009-01';D=R/'outputs/P9ColdFix-20261009-01';L=P/'Logs/InteractionRefinement-20261007/P8'
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
def idle():return subprocess.check_output(['powershell','-NoProfile','-Command',"@(Get-Process Unity,WarSandbox -ErrorAction SilentlyContinue).Count"],text=True).strip()=='0'
def files(root):return {p.relative_to(root).as_posix():{'sha256':sha(p),'size':p.stat().st_size,'mtime_ns':p.stat().st_mtime_ns} for p in root.rglob('*') if p.is_file()}
def save(v):(Q/'editor-warmup-baseline-cache-trial.json').write_text(json.dumps(v,ensure_ascii=False,indent=2),encoding='utf-8')
def run_tool(label):
 log=Q/(label+'.log');args=[str(Path(r'D:\soft\Unity6\6000.3.14f1\Editor\Unity.exe')),'-batchmode','-force-d3d11','-projectPath',str(P),'-logFile',str(log),'-executeMethod','MassEngine.Game.Editor.P9LocalCommandShaderWarmup.PrecompileForP9Verification','-quit']
 with log.open('w',encoding='utf-8') as f:code=subprocess.call(args,cwd=P,stdout=f,stderr=subprocess.STDOUT)
 return code,log.read_text(encoding='utf-8',errors='replace') if log.exists() else ''
assert idle();trialPath=Q/'editor-warmup-baseline-cache-trial.json';assert not trialPath.exists()
manifest=json.loads((Q/'candidate-manifest.json').read_text(encoding='utf-8'));assert manifest['name']=='navigation-sharing-plus-explicit-editor-warmup-01' and manifest['editorWarmupTool'] and not manifest.get('accepted');assert all(sha(P/f)==h for f,h in manifest['files'].items())
r03=json.loads((D/'patch-manifest.json').read_text(encoding='utf-8'));assert all(sha(P/f)==h for f,h in r03['files'].items())
rel='Assets/Game/Editor/P9LocalCommandShaderWarmup.cs';assert sha(P/rel)==manifest['files'][rel]
C=P/'Library/ShaderCache/compute/AgentCombatSimulation79e5';marker=P/'Library/WarSandbox/P9ShaderWarmup-D3D11.json';B=Q/'editor-warmup-baseline-original-cache';G=Q/'editor-warmup-on-baseline-generated-cache';assert C.is_dir() and not B.exists() and not G.exists();baseline=files(C);prior=json.loads((Q/'editor-warmup-candidate-trial-01-compile-failure.json').read_text(encoding='utf-8'))['originalComputeCache'];assert baseline==prior
markerExisted=marker.exists();markerBefore=marker.read_bytes() if markerExisted else None;shutil.copytree(C,B)
j={'status':'journaled','run':'fixed-firstcold-11','testSetup':'Keep all original P12 compute-cache files in place during explicit editor preparation. Preserve an exact copy solely for cleanup.','baselineCache':baseline,'markerExisted':markerExisted,'r03RuntimeShaderAssetsUnchanged':True,'candidateAccepted':False,'sceneLoadNotInvokedByTool':True,'started':datetime.datetime.now().isoformat()};save(j)
try:
 print('Run explicit editor preparation with the ORIGINAL compute cache preserved (normal workflow).',flush=True)
 j['initialToolExit'],firstLog=run_tool('editor-warmup-baseline-first');j['initialToolCompiled']='P9 local-command shader preparation: Compiled SimulateLocalAttackMelee' in firstLog;j['initialToolError']=sorted(set(re.findall(r'^.*error CS\d+.*$',firstLog,re.M)));assert j['initialToolExit']==0 and j['initialToolCompiled'] and not j['initialToolError'];assert idle()
 after=files(C);assert all(f in after and after[f]['sha256']==v['sha256'] for f,v in baseline.items());j['cacheAfterPreparation']=after
 m=json.loads(marker.read_text(encoding='utf-8'));j['compileMilliseconds']=m['compileMilliseconds'];j['generatedCacheEntryCount']=len(after)-len(baseline);j['generatedVariantRows']=[x for x in m['cacheEntries'] if x['sha256']=='e296446c6f2c66b2718d30bc7aa0b853386fb3b82457864b8e18771300e28ac7' and x['bytes']==111032];cacheRoot=P/'Library/ShaderCache/compute';j['markerCacheBytesMatch']=all((cacheRoot/row['path']).is_file() and sha(cacheRoot/row['path'])==row['sha256'] for row in m['cacheEntries']);assert j['markerCacheBytesMatch'] and j['generatedVariantRows'];save(j)
 print('Re-run valid marker to verify cache reuse (no recompilation).',flush=True)
 j['reuseExit'],reuseLog=run_tool('editor-warmup-baseline-reuse');j['validMarkerReused']='Already prepared' in reuseLog;assert j['reuseExit']==0 and j['validMarkerReused'];assert idle()
 print('Invalidate the recorded source fingerprint deliberately; confirm explicit tool regenerates marker.',flush=True)
 stale=json.loads(marker.read_text(encoding='utf-8'));validFingerprint=stale['sourceFingerprint'];stale['sourceFingerprint']='deliberately-stale-for-validation';marker.write_text(json.dumps(stale,ensure_ascii=False,indent=2),encoding='utf-8')
 j['invalidatedExit'],invalidLog=run_tool('editor-warmup-baseline-invalidation');j['staleFingerprintTriggeredRecompile']='P9 local-command shader preparation: Compiled SimulateLocalAttackMelee' in invalidLog and 'Already prepared' not in invalidLog;assert j['invalidatedExit']==0 and j['staleFingerprintTriggeredRecompile'];assert idle()
 repaired=json.loads(marker.read_text(encoding='utf-8'));j['fingerprintRepaired']=repaired['sourceFingerprint']==validFingerprint;cacheRoot=P/'Library/ShaderCache/compute';j['repairedCacheBytesMatch']=all((cacheRoot/row['path']).is_file() and sha(cacheRoot/row['path'])==row['sha256'] for row in repaired['cacheEntries']);assert j['fingerprintRepaired'] and j['repairedCacheBytesMatch'];save(j)
 print('Run exact cold first-operation UI test after explicit preparation.',flush=True)
 j['firstUseExit']=subprocess.call([sys.executable,str(R/'Tools/InteractionRefinement/p9_first_command_run_20261009.py'),'fixed-firstcold-11'],cwd=R);assert idle()
 run=json.loads((Q/'fixed-firstcold-11.json').read_text(encoding='utf-8'));j['firstUsePassed']=run['passed'];j['firstUseResult']=run;rows=list(csv.reader((L/'cold-fix-fixed-firstcold-11/cold-summary.csv').read_text(encoding='utf-8').splitlines()));j['firstUseSummary']=rows[:5];latencies={row[1]:float(row[5]) for row in rows[:4] if len(row)>5 and row[0]=='joint-session'};j['firstOperationReceiptMilliseconds']=latencies;j['meetsP3ColdUseThreshold']=set(latencies)=={'Attack','Hold','Retreat','Move'} and all(v<250.0 for v in latencies.values());save(j)
 assert j['firstUseExit']==0 and j['firstUsePassed'] and j['meetsP3ColdUseThreshold']
except Exception as ex:
 j['error']=repr(ex);save(j);raise
finally:
 assert idle(),'Never restore cache while Unity is active'
 if C.exists():j['generatedCacheFinal']=files(C);C.rename(G)
 assert files(B)==baseline and not C.exists();B.rename(C);j['cacheRestoredExactly']=files(C)==baseline
 if markerExisted:
  marker.parent.mkdir(parents=True,exist_ok=True);marker.write_bytes(markerBefore);j['markerRestored']=marker.read_bytes()==markerBefore
 else:
  if marker.exists():
   payload=marker.read_bytes();ev=Q/'editor-warmup-baseline-generated-marker.json';ev.write_bytes(payload);j['generatedMarkerEvidence']=str(ev.relative_to(R));j['generatedMarkerSha256']=hashlib.sha256(payload).hexdigest();marker.unlink()
  j['markerRestored']=not marker.exists()
 j['toolSourceMatchesManifest']=sha(P/rel)==manifest['files'][rel];j['r03RuntimeShaderAssetsUnchanged']=all(sha(P/f)==h for f,h in r03['files'].items());j['motherCodeTouched']=False;j['finished']=datetime.datetime.now().isoformat();j['status']='complete' if j.get('firstUsePassed') and j.get('cacheRestoredExactly') and j.get('markerRestored') else 'failed_or_blocked';save(j)
print(json.dumps({k:v for k,v in j.items() if k not in ['baselineCache','cacheAfterPreparation','generatedCacheFinal','firstUseResult','markerBefore']},ensure_ascii=False,indent=2),flush=True)
sys.exit(0 if j.get('initialToolExit')==0 and j.get('reuseExit')==0 and j.get('invalidatedExit')==0 and j.get('firstUseExit')==0 and j.get('firstUsePassed') and j.get('cacheRestoredExactly') and j.get('markerRestored') else 1)
