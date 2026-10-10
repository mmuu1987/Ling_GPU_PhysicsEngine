"""Validate the complete isolated revision; never deploy candidates into mother workspace."""
from pathlib import Path
import subprocess,json,hashlib,datetime,sys,time,xml.etree.ElementTree as ET
sys.stdout.reconfigure(encoding='utf-8')
R=Path(__file__).resolve().parents[2];W=R/'outputs/BurstDependencyRepair-20261009-01';P=R/'outputs/B9'
U=Path('D:/soft/Unity6/6000.3.14f1/Editor/Unity.exe')
V=W/'validation-03'
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def save(p,d):p.parent.mkdir(parents=True,exist_ok=True);p.write_text(json.dumps(d,ensure_ascii=False,indent=2),encoding='utf-8')
def protect():
 b=json.loads((W/'mother-before.json').read_text(encoding='utf-8'))
 changed=[p for p,h in b['files'].items() if not(R/p).is_file() or sha(R/p)!=h]
 head=subprocess.check_output(['git','rev-parse','HEAD'],cwd=R,text=True).strip()
 return {'passed':not changed and head==b['head'] and sha(R/'.git/index')==b['index'],'changed':changed,'headUnchanged':head==b['head'],'indexUnchanged':sha(R/'.git/index')==b['index'],'checkedFiles':len(b['files'])}
assert (W/'prepare-result.json').is_file() and json.loads((W/'prepare-result.json').read_text())['passed']
assert U.is_file() and P.is_dir() and not V.exists()
V.mkdir()
state={'status':'running','scope':'Isolated full committed project + explicit dependency repair; Editor validation, not performance/Player/P9/manual acceptance','runs':[],'baseCommit':'085957ba7e838d23379cbc8fe6249540c86f31d9'}
save(V/'summary.json',state)
manifest=json.loads((W/'repair-manifest.json').read_text(encoding='utf-8'))
expected={p:d['newSha'] for p,d in manifest['changes'].items()}
expected.update({p:sha(P/p) for p in ['Packages/manifest.json','Packages/packages-lock.json']})
try:
 pre=protect();save(V/'mother-preflight.json',pre);assert pre['passed']
 assert not(P/'Temp/UnityLockfile').exists(),'Isolated project already open'
 # Read-only process inventory; do not close any existing user Editor.
 ps='Get-CimInstance Win32_Process | Where-Object { $_.Name -eq "Unity.exe" } | Select-Object ProcessId,CommandLine | ConvertTo-Json -Compress'
 q=subprocess.run(['powershell','-NoProfile','-Command',ps],capture_output=True,text=True,encoding='utf-8',timeout=45,check=True)
 save(V/'unity-processes-before.json',{'output':q.stdout})
 processes=json.loads(q.stdout) if q.stdout.strip() else [];processes=[processes] if isinstance(processes,dict) else processes
 for proc in processes:
  command=(proc.get('CommandLine') or '').replace('\\','/').lower()
  assert str(P).replace('\\','/').lower() not in command,'Isolated project already owned'
 # No user scene automation, input injection, preference changes, or shared cache manipulation.
 configurations=[('default',[],True),('optin',['--war-sandbox-nav-burst'],True),('managed-override',['--war-sandbox-nav-burst','--war-sandbox-nav-managed'],False),('compiler-disabled',['--war-sandbox-nav-burst','--burst-disable-compilation'],False),('force-sync-refused',['--war-sandbox-nav-burst','--burst-force-sync-compilation'],False),('test-runner-compatible',[],False)]
 for name,extra,full in configurations:
  assert not(P/'Temp/UnityLockfile').exists(),'Isolation lock before '+name
  for path,h in expected.items():assert sha(P/path)==h,'Candidate changed before '+name+': '+path
  folder=V/name;folder.mkdir()
  xml=folder/'results.xml';log=folder/'Editor.log'
  filt='MassEngine.Tests.TerrainNavigationGridTests;MassEngine.Tests.TerrainCardinalRouteCacheTests;MassEngine.Tests.BurstDependencyRepairRuntimeTests;MassEngine.Tests.BurstDependencyRepairPolicyTests' if full else 'MassEngine.Tests.BurstDependencyRepairRuntimeTests'
  args=[str(U),'-batchmode','-force-d3d11','-projectPath',str(P),'-logFile',str(log),'-runTests','-testPlatform','EditMode','-testResults',str(xml),'-testFilter',filt,'--interaction-p8-output='+str(folder),'-screen-width','1280','-screen-height','720']+extra
  if name=='test-runner-compatible':args=[a for a in args if not a.startswith('--interaction-p8-output=')]
  save(folder/'invocation.json',{'args':args,'started':datetime.datetime.now().isoformat()})
  print('RUN',name,flush=True);start=time.monotonic()
  with (folder/'launcher.log').open('w',encoding='utf-8') as out:
   child=subprocess.Popen(args,cwd=P,stdout=out,stderr=subprocess.STDOUT)
   save(folder/'owner.json',{'pid':child.pid,'project':str(P)})
   try:code=child.wait(timeout=1500 if name=='default' else 420)
   except subprocess.TimeoutExpired:
    subprocess.run(['taskkill','/PID',str(child.pid),'/T','/F'],capture_output=True,timeout=30)
    raise RuntimeError('Owned isolated Unity timed out: '+name)
  receipt={'name':name,'exitCode':code,'seconds':time.monotonic()-start,'resultsExist':xml.is_file()}
  if xml.is_file():
   root=ET.parse(xml).getroot();receipt['tests']=dict(root.attrib)
   receipt['failures']=[{'name':e.attrib.get('fullname'), 'message':e.findtext('failure/message')} for e in root.iter('test-case') if e.attrib.get('result')=='Failed']
   receipt['runtimeCases']=[e.attrib for e in root.iter('test-case') if 'BurstDependencyRepairRuntimeTests' in e.attrib.get('fullname','')]
  if (folder/'runtime-gate.json').is_file():receipt['runtime']=json.loads((folder/'runtime-gate.json').read_text(encoding='utf-8-sig'))
  receipt['candidateAndPackageHashesUnchanged']=all(sha(P/path)==h for path,h in expected.items())
  save(folder/'receipt.json',receipt);state['runs'].append(receipt);save(V/'summary.json',state)
  print('RESULT',json.dumps(receipt,ensure_ascii=False),flush=True)
  assert code==0 and xml.is_file(),'No successful test run: '+name
  tests=receipt['tests'];assert tests.get('result')=='Passed' and int(tests.get('failed','0'))==0 and int(tests.get('skipped','0'))==0,'Test failure or skip: '+name
  assert int(tests.get('passed','0'))>0 and len(receipt['runtimeCases'])==1 and receipt['runtimeCases'][0]['result']=='Passed','Runtime fixture not actually run'
  assert receipt['candidateAndPackageHashesUnchanged'],'Source/package drift'
  if name=='test-runner-compatible':continue
  runtime=receipt['runtime'];assert runtime['exactBits'] and runtime['active']==0 and runtime['created']==runtime['disposed']
  if name=='optin':assert runtime['nativeSolves']>=4 and runtime['requested'] and runtime['managedNativeSolves']==0
  else:assert runtime['nativeSolves']==runtime['probes']==runtime['created']==0
 state['status']='all_six_editor_runs_passed'
except Exception as error:
 state['status']='blocked';state['error']=str(error);print('BLOCKED',str(error),flush=True)
finally:
 state['motherProtection']=protect();save(V/'summary.json',state)
 print('FINAL',json.dumps({'status':state['status'],'error':state.get('error'),'motherProtection':state['motherProtection']},ensure_ascii=False),flush=True)
if state['status']!='all_six_editor_runs_passed' or not state['motherProtection']['passed']:sys.exit(1)
