from pathlib import Path
import subprocess,json,hashlib,os,sys,time,datetime,re,xml.etree.ElementTree as ET
sys.stdout.reconfigure(encoding='utf-8')
R=Path(__file__).resolve().parents[2];O=R/'outputs/P9Combined-20261009-01';P=R/'outputs/P12';U=Path(r'D:\soft\Unity6\6000.3.14f1\Editor\Unity.exe')
D=R/'outputs/P9ColdFix-20261009-01'
env=os.environ.copy();env['GIT_OPTIONAL_LOCKS']='0'
def sha(p):
 h=hashlib.sha256()
 with p.open('rb') as f:
  for v in iter(lambda:f.read(1048576),b''):h.update(v)
 return h.hexdigest()
def save(p,d):p.parent.mkdir(parents=True,exist_ok=True);p.write_text(json.dumps(d,ensure_ascii=False,indent=2),encoding='utf-8')
def git(*a):return subprocess.check_output(['git',*a],cwd=R,env=env).decode().strip()
assert json.loads((O/'prepare.json').read_text(encoding='utf-8'))['motherProtectionPassed']
b=json.loads((O/'mother-before.json').read_text(encoding='utf-8'));src=json.loads((O/'source-manifest.json').read_text(encoding='utf-8'))['files'];harness=json.loads((O/'harness-manifest.json').read_text(encoding='utf-8'));user=Path(b['userRoot'])
def ps():
 q=subprocess.run(['powershell','-NoProfile','-Command',"[Console]::OutputEncoding=[System.Text.UTF8Encoding]::new();Get-CimInstance Win32_Process | Where-Object {$_.Name -eq 'Unity.exe' -or $_.Name -eq 'WarSandbox.exe'} | Select-Object ProcessId,Name,CommandLine | ConvertTo-Json -Compress"],capture_output=True,text=True,encoding='utf-8',timeout=40,check=True)
 d=json.loads(q.stdout) if q.stdout.strip() else [];return [d] if isinstance(d,dict) else d
# Full initial hash verification, then stat-assisted checks of unchanged protected files.
print('VERIFY PROTECTED MOTHER',flush=True)
assert git('rev-parse','HEAD')==b['head'] and sha(R/'.git/index')==b['index']
C=R/'outputs/P9Capacity-20261009-01';cache=C/'verified-mother-stat-cache.json';baselineSha=sha(O/'mother-before.json');oldStats={}
if cache.exists():
 ck=json.loads(cache.read_text(encoding='utf-8'))
 if ck.get('baselineSha')==baselineSha:oldStats=ck['stats']
stats={};hashed=0
for f,h in b['files'].items():
 p=R/f;assert p.is_file(),f;st=(p.stat().st_size,p.stat().st_mtime_ns)
 if list(st)!=oldStats.get(f):assert sha(p)==h,'Mother baseline drift: '+f;hashed+=1
 stats[f]=st
save(cache,{'baselineSha':baselineSha,'stats':stats,'verifiedAgainstBaseline':True})
print('MOTHER VERIFIED',len(stats),'files; content hashes recomputed',hashed,flush=True)
def protect():
 changed=[f for f,h in b['files'].items() if not(R/f).is_file() or (((R/f).stat().st_size,(R/f).stat().st_mtime_ns)!=stats[f] and sha(R/f)!=h)]
 current={f.relative_to(R).as_posix() for root in ['Assets','Packages','ProjectSettings'] for f in (R/root).rglob('*') if f.is_file()}
 expected={f for f in b['files'] if f.split('/')[0] in ['Assets','Packages','ProjectSettings']}
 uf={f.relative_to(user).as_posix():sha(f) for f in user.rglob('*') if f.is_file()}
 return {'passed':not changed and current==expected and uf==b['userFiles'] and git('rev-parse','HEAD')==b['head'] and sha(R/'.git/index')==b['index'],'changed':changed,'addedOrRemovedProjectFiles':sorted(current^expected),'userDataUnchanged':uf==b['userFiles'],'headUnchanged':git('rev-parse','HEAD')==b['head'],'indexUnchanged':sha(R/'.git/index')==b['index']}
name=sys.argv[1];assert re.fullmatch(r'(baseline|fixed)-(golden|cold|regression|repeat|joint|parity|partition|other|optimized|membership|hostscene|hosttrace|unified|capacityprep|capacity2048|capacity50000|capacitybreakdown|capacitywarm|capacityviewport)-[0-9]{2}',name);burst=False;gui='-repeat-' in name or '-capacity' in name
method='';assembly='MassEngine.PlayModeTests';expectedCases=3
if '-capacityviewport-' in name:assembly='Game.PlayModeTests';expectedCases=2;testFilter='MassEngine.Game.Tests.P9CapacityViewportTests'
elif '-capacitybreakdown-' in name or '-capacitywarm-' in name:assembly='Game.PlayModeTests';expectedCases=1;testFilter='MassEngine.Game.Tests.P9CapacityBreakdownTests.'+('Capacity50000Breakdown' if '-capacitybreakdown-' in name else 'CapacityWarmCommands')
elif '-capacity' in name:assembly='Game.PlayModeTests';expectedCases=1;testFilter='MassEngine.Game.Tests.P9CapacityTests.'+('CapacityGeometryPreflight' if '-capacityprep-' in name else ('Capacity2048Standing' if '-capacity2048-' in name else 'Capacity50000Standing'))
elif '-unified-' in name:expectedCases=144;testFilter='MassEngine.Tests.MassEngineGpuKernelTests.P9UnifiedIntegrationMatchesReference'
elif '-hosttrace-' in name:assembly='Game.PlayModeTests';expectedCases=4;testFilter='MassEngine.Game.Tests.P9HostRoutingTests'
elif '-hostscene-' in name:
 assembly='Game.PlayModeTests';expectedCases=4
 methods=['P6GreenBridgeWholeArmyResetAndResourceBudget','P6FourGroupsGreen2048AndRealObstacleChange','P6FourGroupsAuthoredMaleKnightRangerMix','P6AuthoredRangerMoveThenHoldFiresOwnedProjectile']
 testFilter='^MassEngine.Game.Tests.LocalOrdersSceneTests.('+'|'.join(methods)+')$'
elif '-membership-' in name:expectedCases=5;testFilter='MassEngine.Tests.MassEngineGpuKernelTests.P9HostMembership'
elif '-optimized-' in name:expectedCases=32;testFilter='MassEngine.Tests.MassEngineGpuKernelTests.P9OptimizedRangedMatchesReference'
elif '-other-' in name:expectedCases=120;testFilter='MassEngine.Tests.MassEngineGpuKernelTests.P9OtherCommandsMatchReference'
elif '-partition-' in name:expectedCases=26;testFilter='MassEngine.Tests.MassEngineGpuKernelTests.P9PartitionedAttackMelee'
elif '-parity-' in name:expectedCases=16;testFilter='MassEngine.Tests.MassEngineGpuKernelTests.P9SpecializedAttackMelee'
elif '-golden-' in name:
 methods=['DamageAccruesAtAttackIntervalAndKillsAtZeroHp','ObservedStateTransitionsAreLegalAndBattleProducesCombatStates','BattleNotStartedFreezesAgentsInIdleWithNoDisplacement']
 testFilter='^MassEngine.Tests.MassEngineGpuKernelTests.('+'|'.join(methods)+')$'
elif '-cold-' in name:assembly='Game.PlayModeTests';expectedCases=1;testFilter='MassEngine.Game.Tests.P9ColdAttackDiagnosticTests.P9ColdAttackExactJointPrefix'
elif '-regression-' in name:
 spec=json.loads((D/'regression-methods-v2.json').read_text(encoding='utf-8'));methods=spec['methods'];expectedCases=spec['expectedCases'];testFilter='^MassEngine.Tests.MassEngineGpuKernelTests.('+'|'.join(methods)+')(\\(.*\\))?$'
else:
 assembly='Game.PlayModeTests';expectedCases=1;testFilter='MassEngine.Game.Tests.P9CombinedAcceptanceTests.'+('P9Repeated2048ScopedFeedbackAndResourceRelease' if gui else 'P9SingleSessionRadiusDeploymentPlansOrdersNaturalResultAndSceneSwitch')
V=P/'Logs/InteractionRefinement-20261007/P8'/('cold-fix-'+name)
if (D/'prototype-harness.json').exists():harness.update(json.loads((D/'prototype-harness.json').read_text(encoding='utf-8')))
if (D/'diagnostic-harness.json').exists():harness.update(json.loads((D/'diagnostic-harness.json').read_text(encoding='utf-8')))
harness.update(json.loads((R/'outputs/P9Capacity-20261009-01/capacity-harness-manifest.json').read_text(encoding='utf-8')))
harness.update(json.loads((C/'breakdown-harness-manifest.json').read_text(encoding='utf-8')))
harness.update(json.loads((C/'viewport-harness-manifest.json').read_text(encoding='utf-8')))
assert not V.exists() # Lock-file existence is not ownership; ps() below excludes active editors.
assert not ps(),'Other Unity/WarSandbox process active; do not close user process or contaminate GPU measurement'
assert all(sha(P/f)==h for f,h in harness.items())
production={f:x['sha256'] for f,x in src.items() if Path(f).suffix in ['.cs','.asmdef','.compute','.hlsl','.shader'] or f.startswith('Packages/')}
if name.startswith('fixed-'):production.update(json.loads((D/'patch-manifest.json').read_text(encoding='utf-8'))['files'])
assert all(sha(P/f)==h for f,h in production.items()),'Candidate code/package drift'
V.mkdir(parents=True)
save(V/'complete-harness-snapshot.json',harness)
save(V/'complete-production-snapshot.json',production)
if name.startswith('fixed-'):(V/'patch-manifest-snapshot.json').write_bytes((D/'patch-manifest.json').read_bytes())
persistentXml=user/'TestResults.xml';persistentBefore=persistentXml.read_bytes() if persistentXml.exists() else None
if persistentBefore is not None:(V/'persistent-TestResults-before.xml').write_bytes(persistentBefore)
(V/'guide.json').write_text('{"version":1,"dismissed":true}',encoding='utf-8')
auto={str(f):f.read_bytes() for f in (P/'ProjectSettings').glob('*') if f.is_file()}
prefs=Path(os.environ['APPDATA'])/'Unity/Editor-5.x/Preferences';external={}
if gui:
 for rel in ['GameViewSizes.asset','Layouts/current/default-6000.dwlt']:
  f=prefs/rel;external[str(f)]=f.read_bytes() if f.exists() else None
args=[str(U),'-force-d3d11','-projectPath',str(P),'-logFile',str(V/'Editor.log'),'-runTests','-testPlatform','PlayMode','-assemblyNames',assembly,'-testResults',str(V/'results.xml'),'-testFilter',testFilter,'--interaction-p8-output='+str(V),'--war-sandbox-guide-file='+str(V/'guide.json'),'--war-sandbox-settings-file='+str(V/'audio.json'),'--war-sandbox-review-global-file='+str(V/'globals.json'),'--war-sandbox-menu','-screen-width','1280','-screen-height','720']
if not gui:args.insert(1,'-batchmode')
else:args+=['--interaction-p8-owned-editor']
if burst:args+=['--war-sandbox-nav-burst']
s={'name':name,'args':args,'started':datetime.datetime.now().isoformat(),'passed':False,'status':'running','renderedGui':gui,'burstOptIn':burst}
save(V/'process.json',s);save(C/'current-run.json',s)
# Journal preferences before launch; only these owned changes may be restored.
for i,(f,data) in enumerate(external.items()):
 if data is not None:(V/('external-before-'+str(i)+'.bin')).write_bytes(data)
save(V/'external-paths.json',{f:{'existed':data is not None,'sha256':hashlib.sha256(data).hexdigest() if data is not None else None} for f,data in external.items()})
child=None;monitor=None
stop=V/'monitor-stop';monitorFile=V/'process-cpu.jsonl'
script="while(-not(Test-Path -LiteralPath '"+str(stop)+"')){ $p=@(Get-Process Unity,UnityShaderCompiler -ErrorAction SilentlyContinue | ForEach-Object { @{pid=$_.Id;name=$_.ProcessName;cpu_ms=$_.TotalProcessorTime.TotalMilliseconds;started=$_.StartTime.ToUniversalTime().ToString('o')} });$r=@{utc=[DateTime]::UtcNow.ToString('o');processes=$p}|ConvertTo-Json -Compress -Depth 4;[IO.File]::AppendAllText('"+str(monitorFile)+"',$r+[Environment]::NewLine);Start-Sleep -Milliseconds 500 }"
try:
 monitor=subprocess.Popen(['powershell','-NoProfile','-Command',script],stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)

 with (V/'launcher.log').open('w',encoding='utf-8') as log:
  child=subprocess.Popen(args,cwd=P,stdout=log,stderr=subprocess.STDOUT);s['ownedPid']=child.pid;save(V/'process.json',s);save(C/'current-run.json',s);print('OWNED UNITY',child.pid,name,flush=True)
  try:s['exitCode']=child.wait(timeout=1200)
  except subprocess.TimeoutExpired:
   subprocess.run(['taskkill','/PID',str(child.pid),'/T','/F'],capture_output=True,timeout=30);child.wait(timeout=30);s['exitCode']=child.returncode;s['timedOut']=True
 xml=V/'results.xml'
 if xml.exists():
  tree=ET.parse(xml).getroot();s['tests']=dict(tree.attrib);s['cases']=[{'name':t.get('fullname'),'result':t.get('result'),'message':t.findtext('failure/message'),'stack':t.findtext('failure/stack-trace')} for t in tree.iter('test-case')]
 text=(V/'Editor.log').read_text(encoding='utf-8',errors='replace') if (V/'Editor.log').exists() else ''
 s['compileErrors']=sorted(set(re.findall(r'^.*error CS\d+.*$',text,re.M)));s['shaderErrors']=sorted(set(re.findall(r'^.*Shader error.*$',text,re.M)));s['isolationMarkerSeen']='INTERACTION_P8_ISOLATED' in text;s['stages']=re.findall(r'^P9_STAGE.*$',text,re.M)
 s['candidateCodeMatchesDeclaredManifest']=all(sha(P/f)==h for f,h in production.items());s['harnessUnchanged']=all(sha(P/f)==h for f,h in harness.items())
 s['passed']=s.get('exitCode')==0 and not s.get('timedOut') and not s['compileErrors'] and not s['shaderErrors'] and s['isolationMarkerSeen'] and s['candidateCodeMatchesDeclaredManifest'] and s['harnessUnchanged'] and s.get('tests',{}).get('result')=='Passed' and len(s.get('cases',[]))==expectedCases and all(c['result']=='Passed' for c in s['cases'])
except Exception as e:s['error']=repr(e)
finally:
 stop.write_text('owned monitor stop',encoding='utf-8')
 if monitor is not None:
  try:monitor.wait(timeout=15)
  except subprocess.TimeoutExpired:monitor.terminate();monitor.wait(timeout=15)
 s['restoredIsolatedSettings']=[]
 if child is None or child.poll() is not None:
  for f,data in auto.items():
   p=Path(f)
   if p.is_file() and p.read_bytes()!=data:
    dest=V/'auto-settings-generated'/p.name;dest.parent.mkdir(parents=True,exist_ok=True);dest.write_bytes(p.read_bytes());p.write_bytes(data);s['restoredIsolatedSettings'].append(f)
  if gui:
   if not ps():
    for f,data in external.items():
     p=Path(f)
     if data is None:
      if p.exists():(V/('external-generated-'+p.name)).write_bytes(p.read_bytes());p.unlink()
     elif not p.exists() or p.read_bytes()!=data:p.parent.mkdir(parents=True,exist_ok=True);p.write_bytes(data)
    s['externalPreferencesRestored']=True
   else:s['externalPreferencesRestored']=False;s['passed']=False;s['restoreBlockedByOtherEditor']=True
 if child is not None and child.poll() is not None and persistentXml.exists():
  generated=persistentXml.read_bytes()
  if generated!=persistentBefore:
   own=V/'results.xml'
   if not ps() and own.exists() and generated==own.read_bytes() and s.get('cases') and all(c['name'].startswith(('MassEngine.Tests.MassEngineGpuKernelTests.','MassEngine.Game.Tests.P9ColdAttackDiagnosticTests.','MassEngine.Game.Tests.P9CombinedAcceptanceTests.','MassEngine.Game.Tests.LocalOrdersSceneTests.','MassEngine.Game.Tests.P9HostRoutingTests.','MassEngine.Game.Tests.P9CapacityTests.','MassEngine.Game.Tests.P9CapacityBreakdownTests.','MassEngine.Game.Tests.P9CapacityViewportTests.')) for c in s['cases']):
    (V/'persistent-TestResults-generated.xml').write_bytes(generated)
    if persistentBefore is None:persistentXml.unlink()
    else:persistentXml.write_bytes(persistentBefore)
    s['persistentTestResultsRestored']=True
   else:s['persistentTestResultsRestored']=False;s['passed']=False;s['persistentRestoreBlocked']='Unexpected content or active user process; not overwritten'
 s['motherProtection']=protect();s['passed']=bool(s['passed'] and s['motherProtection']['passed']);s['finished']=datetime.datetime.now().isoformat();s['status']='passed' if s['passed'] else 'failed_or_blocked';save(V/'process.json',s);save(C/(name+'.json'),s);save(C/'current-run.json',s);print(json.dumps(s,ensure_ascii=False,indent=2),flush=True)
sys.exit(0 if s['passed'] else 1)
