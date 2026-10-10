from common_p0 import *
import sys,time,re,xml.etree.ElementTree as ET
sys.stdout.reconfigure(encoding='utf-8')
mode=sys.argv[1];tag=sys.argv[2];assert mode in ['editmode','capture','move','hold','retreat','performance'];assert re.fullmatch(r'[a-z0-9-]+',tag)
gui=mode=='performance' and len(sys.argv)>3 and sys.argv[3]=='gui'
output=P0/tag;assert not output.exists(),'Never overwrite test evidence'
prepared=json.loads((P0/'preparation.json').read_text(encoding='utf-8'));assert prepared['prepared']
assert not processes(),'User/other Unity or player process active; do not close it'
assert not (ROOT/'Temp/UnityLockfile').exists(),'Project lock still held; never delete it'
check=project_check();assert check['passed'],json.dumps(check,ensure_ascii=False)
assert user_check()['passed'],'User files changed; inspect before testing'
output.mkdir();save(output/'precheck.json',check)
guide=output/'guide.json';guide.write_text('{"version":1,"dismissed":true}',encoding='utf-8')
unity=Path(r'D:\soft\Unity6\6000.3.14f1\Editor\Unity.exe');assert unity.is_file()
kind='editmode' if mode=='editmode' else 'performance' if mode=='performance' else 'functional'
args=[str(unity),'-batchmode','-force-d3d11','-projectPath',str(ROOT),'-logFile',str(output/'Editor.log'),
 '--interaction-p0-mode='+kind,'--interaction-p0-output='+str(output),
 '--war-sandbox-guide-file='+str(guide),'--war-sandbox-settings-file='+str(output/'audio.json'),
 '--war-sandbox-review-global-file='+str(output/'globals.json'),'-screen-width','1280','-screen-height','720']
if mode=='editmode':
 args+=['-runTests','-testPlatform','EditMode','-assemblyNames','Game.Tests;MassEngine.Tests','-testResults',str(output/'results.xml')]
 if len(sys.argv)>3:
  assert sys.argv[3]=='critical'
  args+=['-testFilter','MassEngine.Game.Tests.ControlPointDefaultOrderTests;MassEngine.Game.Tests.CommandInputSafetyTests;MassEngine.Game.Tests.BattleCycleStabilityTests']
else:
 args+=['-executeMethod','MassEngine.Game.Editor.InteractionRefinementP0.Begin']
 if mode=='capture':args+=['--capture-default-probe','--capture-default-mode=fixed','--terrain-output='+str(output/'observations')]
 elif mode in ['move','hold','retreat']:args+=['--contact34-probe','--contact34-assert','--contact34-case='+mode,'--terrain-output='+str(output/'observations')]
if gui:
 args.remove('-batchmode');args+=['--interaction-p0-owned-editor'] # FrameTimingManager works in rendered GUI; optional unsupported Profiler reflection is disabled.
# Known Editor-owned settings changes are journaled and restored only after this process exits.
auto=['ProjectSettings/TimeManager.asset','ProjectSettings/EditorBuildSettings.asset','Library/LastSceneManagerSetup.txt']
if gui:
 auto+=['Library/CurrentLayout-default.dwlt','UserSettings/EditorUserSettings.asset']
 if(ROOT/'UserSettings/Layouts').exists():auto+=[p.relative_to(ROOT).as_posix() for p in(ROOT/'UserSettings/Layouts').rglob('*') if p.is_file()]
saved={p:(ROOT/p).read_bytes() for p in auto if(ROOT/p).is_file()}
for p,b in saved.items():q=output/'auto-settings-before'/p;q.parent.mkdir(parents=True,exist_ok=True);q.write_bytes(b)
external_saved={}
if gui:
 preferences=Path(os.environ['APPDATA'])/'Unity/Editor-5.x/Preferences'
 for rel in ['Layouts/current/default-6000.dwlt','GameViewSizes.asset']:
  path=preferences/rel
  if path.is_file():
   external_saved[rel]=(path,path.read_bytes());q=output/'external-editor-settings-before'/rel;q.parent.mkdir(parents=True,exist_ok=True);q.write_bytes(external_saved[rel][1])
receipt={'mode':mode,'startedAt':datetime.datetime.now().isoformat(),'args':args,'ownedPid':None,'exitCode':None,'restoredAutoFiles':[],'timedOut':False,'passed':False}
child=subprocess.Popen(args,cwd=ROOT);receipt['ownedPid']=child.pid;save(output/'process.json',receipt);print('OWNED_UNITY',child.pid,'MODE',mode,'OUTPUT',output.relative_to(ROOT),flush=True)
try:receipt['exitCode']=child.wait(timeout=720)
except subprocess.TimeoutExpired:
 receipt['timedOut']=True
 if child.poll() is None:subprocess.run(['taskkill','/PID',str(child.pid),'/T','/F'],capture_output=True,timeout=45);child.wait(timeout=30)
 receipt['exitCode']=child.returncode
finally:
 # Never restore into a concurrently opened user Editor.
 other=processes();receipt['otherProcessesAfter']=other
 if not other:
  for p,b in saved.items():
   current=ROOT/p
   if current.is_file() and current.read_bytes()!=b:
    dest=output/'auto-settings-generated'/p;dest.parent.mkdir(parents=True,exist_ok=True);dest.write_bytes(current.read_bytes());current.write_bytes(b);receipt['restoredAutoFiles'].append(p)
  receipt['restoredExternalEditorSettings']=[]
  for rel,(path,data) in external_saved.items():
   if path.is_file() and path.read_bytes()!=data:
    q=output/'external-editor-settings-generated'/rel;q.parent.mkdir(parents=True,exist_ok=True);q.write_bytes(path.read_bytes());path.write_bytes(data);receipt['restoredExternalEditorSettings'].append(rel)
 receipt['finishedAt']=datetime.datetime.now().isoformat()
 log=(output/'Editor.log').read_text(encoding='utf-8',errors='replace') if(output/'Editor.log').exists() else ''
 receipt['compileErrors']=sorted(set(re.findall(r'^.*error CS\d+.*$',log,re.M)))
 receipt['isolationMarkerSeen']='INTERACTION_P0_ISOLATED' in log
 receipt['postCheck']=project_check();receipt['userDataCheck']=user_check()
 if mode=='editmode' and(output/'results.xml').exists():
  root=ET.parse(output/'results.xml').getroot();receipt['tests']=dict(root.attrib)
  receipt['failedTests']=[{'name':n.get('fullname') or n.get('name'),'message':n.findtext('failure/message'),'stack':n.findtext('failure/stack-trace')} for n in root.iter('test-case') if n.get('result')=='Failed']
  successful=int(root.get('failed','0'))==0 and int(root.get('passed','0'))>0
 elif mode=='performance' and(output/'performance.json').exists():receipt['performance']=json.loads((output/'performance.json').read_text(encoding='utf-8'));successful=receipt['performance']['passed']
 elif(output/'observations/receipt.json').exists():
  obs=json.loads((output/'observations/receipt.json').read_text(encoding='utf-8'));receipt['observationSummary']={k:v for k,v in obs.items() if k not in ['samples','phases']};successful=obs['passed']
 else:successful=False
 receipt['passed']=receipt['exitCode']==0 and not receipt['timedOut'] and not receipt['compileErrors'] and receipt['isolationMarkerSeen'] and receipt['postCheck']['passed'] and receipt['userDataCheck']['passed'] and successful
 save(output/'process.json',receipt);print(json.dumps(receipt,ensure_ascii=False,indent=2),flush=True)
sys.exit(0 if receipt['passed'] else 1)


