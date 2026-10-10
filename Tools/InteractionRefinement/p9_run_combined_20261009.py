from pathlib import Path
import subprocess,json,hashlib,os,sys,time,datetime,re,xml.etree.ElementTree as ET
sys.stdout.reconfigure(encoding='utf-8')
R=Path(__file__).resolve().parents[2];O=R/'outputs/P9Combined-20261009-01';P=R/'outputs/P11';U=Path(r'D:\soft\Unity6\6000.3.14f1\Editor\Unity.exe')
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
assert all((R/f).is_file() and sha(R/f)==h for f,h in b['files'].items()),'Mother baseline drift'
stats={f:((R/f).stat().st_size,(R/f).stat().st_mtime_ns) for f in b['files']}
def protect():
 changed=[f for f,h in b['files'].items() if not(R/f).is_file() or (((R/f).stat().st_size,(R/f).stat().st_mtime_ns)!=stats[f] and sha(R/f)!=h)]
 current={f.relative_to(R).as_posix() for root in ['Assets','Packages','ProjectSettings'] for f in (R/root).rglob('*') if f.is_file()}
 expected={f for f in b['files'] if f.split('/')[0] in ['Assets','Packages','ProjectSettings']}
 uf={f.relative_to(user).as_posix():sha(f) for f in user.rglob('*') if f.is_file()}
 return {'passed':not changed and current==expected and uf==b['userFiles'] and git('rev-parse','HEAD')==b['head'] and sha(R/'.git/index')==b['index'],'changed':changed,'addedOrRemovedProjectFiles':sorted(current^expected),'userDataUnchanged':uf==b['userFiles'],'headUnchanged':git('rev-parse','HEAD')==b['head'],'indexUnchanged':sha(R/'.git/index')==b['index']}
configs={'managed-joint-01':(False,False,'P9SingleSessionRadiusDeploymentPlansOrdersNaturalResultAndSceneSwitch'),'managed-repeat-01':(False,True,'P9Repeated2048ScopedFeedbackAndResourceRelease'),'optin-joint-01':(True,False,'P9SingleSessionRadiusDeploymentPlansOrdersNaturalResultAndSceneSwitch'),'optin-repeat-01':(True,True,'P9Repeated2048ScopedFeedbackAndResourceRelease')}
name=sys.argv[1];assert name in configs;burst,gui,method=configs[name];V=P/'Logs/InteractionRefinement-20261007/P8'/name
assert not V.exists() and not(P/'Temp/UnityLockfile').exists()
assert not ps(),'Other Unity/WarSandbox process active; do not close user process or contaminate GPU measurement'
assert all(sha(P/f)==h for f,h in harness.items())
production={f:x['sha256'] for f,x in src.items() if Path(f).suffix in ['.cs','.asmdef','.compute','.hlsl','.shader'] or f.startswith('Packages/')}
assert all(sha(P/f)==h for f,h in production.items()),'Candidate code/package drift'
V.mkdir(parents=True);(V/'guide.json').write_text('{"version":1,"dismissed":true}',encoding='utf-8')
auto={str(f):f.read_bytes() for f in (P/'ProjectSettings').glob('*') if f.is_file()}
prefs=Path(os.environ['APPDATA'])/'Unity/Editor-5.x/Preferences';external={}
if gui:
 for rel in ['GameViewSizes.asset','Layouts/current/default-6000.dwlt']:
  f=prefs/rel;external[str(f)]=f.read_bytes() if f.exists() else None
args=[str(U),'-force-d3d11','-projectPath',str(P),'-logFile',str(V/'Editor.log'),'-runTests','-testPlatform','PlayMode','-assemblyNames','Game.PlayModeTests','-testResults',str(V/'results.xml'),'-testFilter','MassEngine.Game.Tests.P9CombinedAcceptanceTests.'+method,'--interaction-p8-output='+str(V),'--war-sandbox-guide-file='+str(V/'guide.json'),'--war-sandbox-settings-file='+str(V/'audio.json'),'--war-sandbox-review-global-file='+str(V/'globals.json'),'--war-sandbox-menu','-screen-width','1280','-screen-height','720']
if not gui:args.insert(1,'-batchmode')
else:args+=['--interaction-p8-owned-editor']
if burst:args+=['--war-sandbox-nav-burst']
s={'name':name,'args':args,'started':datetime.datetime.now().isoformat(),'passed':False,'status':'running','renderedGui':gui,'burstOptIn':burst}
save(V/'process.json',s);save(O/'current-run.json',s)
# Journal preferences before launch; only these owned changes may be restored.
for i,(f,data) in enumerate(external.items()):
 if data is not None:(V/('external-before-'+str(i)+'.bin')).write_bytes(data)
save(V/'external-paths.json',{f:{'existed':data is not None,'sha256':hashlib.sha256(data).hexdigest() if data is not None else None} for f,data in external.items()})
child=None
try:
 with (V/'launcher.log').open('w',encoding='utf-8') as log:
  child=subprocess.Popen(args,cwd=P,stdout=log,stderr=subprocess.STDOUT);s['ownedPid']=child.pid;save(V/'process.json',s);save(O/'current-run.json',s);print('OWNED UNITY',child.pid,name,flush=True)
  try:s['exitCode']=child.wait(timeout=2100)
  except subprocess.TimeoutExpired:
   subprocess.run(['taskkill','/PID',str(child.pid),'/T','/F'],capture_output=True,timeout=30);child.wait(timeout=30);s['exitCode']=child.returncode;s['timedOut']=True
 xml=V/'results.xml'
 if xml.exists():
  tree=ET.parse(xml).getroot();s['tests']=dict(tree.attrib);s['cases']=[{'name':t.get('fullname'),'result':t.get('result'),'message':t.findtext('failure/message'),'stack':t.findtext('failure/stack-trace')} for t in tree.iter('test-case')]
 text=(V/'Editor.log').read_text(encoding='utf-8',errors='replace') if (V/'Editor.log').exists() else ''
 s['compileErrors']=sorted(set(re.findall(r'^.*error CS\d+.*$',text,re.M)));s['shaderErrors']=sorted(set(re.findall(r'^.*Shader error.*$',text,re.M)));s['isolationMarkerSeen']='INTERACTION_P8_ISOLATED' in text;s['stages']=re.findall(r'^P9_STAGE.*$',text,re.M)
 s['productionCodeAndPackagesUnchanged']=all(sha(P/f)==h for f,h in production.items());s['harnessUnchanged']=all(sha(P/f)==h for f,h in harness.items())
 s['passed']=s.get('exitCode')==0 and not s.get('timedOut') and not s['compileErrors'] and not s['shaderErrors'] and s['isolationMarkerSeen'] and s['productionCodeAndPackagesUnchanged'] and s['harnessUnchanged'] and s.get('tests',{}).get('result')=='Passed' and len(s.get('cases',[]))==1 and all(c['result']=='Passed' for c in s['cases'])
except Exception as e:s['error']=repr(e)
finally:
 s['restoredIsolatedSettings']=[]
 if child is None or child.poll() is not None:
  for f,data in auto.items():
   p=Path(f)
   if p.is_file() and p.read_bytes()!=data:
    dest=V/'auto-settings-generated'/p.name;dest.write_bytes(p.read_bytes());p.write_bytes(data);s['restoredIsolatedSettings'].append(f)
  if gui:
   if not ps():
    for f,data in external.items():
     p=Path(f)
     if data is None:
      if p.exists():(V/('external-generated-'+p.name)).write_bytes(p.read_bytes());p.unlink()
     elif not p.exists() or p.read_bytes()!=data:p.parent.mkdir(parents=True,exist_ok=True);p.write_bytes(data)
    s['externalPreferencesRestored']=True
   else:s['externalPreferencesRestored']=False;s['passed']=False;s['restoreBlockedByOtherEditor']=True
 s['motherProtection']=protect();s['passed']=bool(s['passed'] and s['motherProtection']['passed']);s['finished']=datetime.datetime.now().isoformat();s['status']='passed' if s['passed'] else 'failed_or_blocked';save(V/'process.json',s);save(O/(name+'.json'),s);save(O/'current-run.json',s);print(json.dumps(s,ensure_ascii=False,indent=2),flush=True)
sys.exit(0 if s['passed'] else 1)
