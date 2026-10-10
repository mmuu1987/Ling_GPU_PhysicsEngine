from common_p8 import *
import sys,re,concurrent.futures,zipfile,hashlib
sys.stdout.reconfigure(encoding='utf-8');tags=sys.argv[1:];assert len(tags)>=2;dest=P8/'audit-01.json';assert not dest.exists();assert not processes() and not(ROOT/'Temp/UnityLockfile').exists()
a={'phase':'P8','checkedAt':datetime.datetime.now().isoformat(),'project':project_check(full=True),'p3DeferredByUser':True,'p4HumanAcceptance':False,'newBuildProduced':False}
b=json.loads((BASE/'user-data.sha256.json').read_text(encoding='utf-8'));root=Path(b['root']);old={x['path']:x['sha256'] for x in b['files']};now={}
if root.exists():
 for p in root.rglob('*'):
  if p.is_file():
   rel=p.relative_to(root).as_posix()
   if ('warsandbox' in rel.casefold() or 'war-sandbox' in rel.casefold()) and p.suffix.lower() not in {'.log','.dmp'}:now[rel]=sha(p)
d=[p for p in sorted(set(old)|set(now)) if old.get(p)!=now.get(p)];a['userData']={'before':len(old),'after':len(now),'deltas':d,'passed':not d}
keep=ROOT/'Builds/CaptureDefault-20261006-37';old={x['path']:x['sha256'] for x in json.loads((BASE/'current37.sha256.json').read_text(encoding='utf-8'))};paths=[p for p in keep.rglob('*') if p.is_file()]
with concurrent.futures.ThreadPoolExecutor(max_workers=3) as pool:now=dict(pool.map(lambda p:(p.relative_to(keep).as_posix(),sha(p)),paths))
d=[p for p in sorted(set(old)|set(now)) if old.get(p)!=now.get(p)];a['current37']={'files':len(now),'deltas':d,'passed':not d,'mode':'fresh-full-sha256'}
a['git']={}
for label,args in [('git-head',['rev-parse','HEAD']),('git-staged-names',['diff','--cached','--name-status'])]:
 value=subprocess.check_output(['git','-c','core.quotepath=false']+args,cwd=ROOT,encoding='utf-8',timeout=60);a['git'][label+'-unchanged']=value==(BASE/(label+'.txt')).read_text(encoding='utf-8')
changes=json.loads((P8/'approved-changes.json').read_text(encoding='utf-8'));baseline=json.loads((P8/'baseline.json').read_text(encoding='utf-8'))['files'];expected=(set(json.loads((ROOT/'Tools/InteractionRefinement/p8-01-scoped-commands.json').read_text(encoding='utf-8')))|set(json.loads((ROOT/'Tools/InteractionRefinement/p8-02-player-evidence.json').read_text(encoding='utf-8'))))&set(baseline);a['existingChangesExact']=set(changes)&set(baseline)==expected;a['approvedChanges']=changes
assets=['Assets/Game/Content/Characters/UnifiedRoster/Version03/Library/'+role+'_Flocking.asset' for role in ['male','knight']];a['official48Unchanged']=all(sha(ROOT/p)==baseline[p] and b'separationStrength: 48' in(ROOT/p).read_bytes() for p in assets)
guids={};duplicates=[]
for p in(ROOT/'Assets').rglob('*.meta'):
 m=re.search(r'^guid:\s*([a-f0-9]{32})',p.read_text(encoding='utf-8',errors='replace'),re.M)
 if m:
  if m[1] in guids:duplicates.append([guids[m[1]],p.relative_to(ROOT).as_posix()])
  guids[m[1]]=p.relative_to(ROOT).as_posix()
a['duplicateGuids']=duplicates;a['runs']={}
for tag in tags:
 r=json.loads((P8/tag/'process.json').read_text(encoding='utf-8'));a['runs'][tag]={k:r[k] for k in ['passed','exitCode','compileErrors','tests','failedTests','restoredAutoFiles','isolationMarkerSeen'] if k in r}
arc=ROOT/'Logs/WorkspaceArchive-20261007-01';manifest=json.loads((arc/'manifest.json').read_text(encoding='utf-8'));assert len(manifest['files'])==462
with zipfile.ZipFile(arc/'extra-files.zip') as z:
 assert z.testzip() is None
 for r in manifest['files']:
  s=r['storage'];data=(ROOT/s['path']).read_bytes() if s['kind']=='existing-file' else z.read(s['entry']);assert len(data)==r['bytes'] and hashlib.sha256(data).hexdigest()==r['sha256'],r['path']
a['archive']={'filesVerified':462,'manifestSha256':sha(arc/'manifest.json'),'migrationReceipts':[p.relative_to(ROOT).as_posix() for p in sorted((P8/'changes').glob('*/archive-migrations.json'))]}
evidencePaths=set()
for tag in tags:
 for p in(P8/tag).rglob('*'):
  if p.is_file() and not any(x in p.parts for x in ['auto-settings-before','auto-settings-generated','external-editor-settings-before','external-editor-settings-generated']):evidencePaths.add(p)
for p in(ROOT/'Docs/InteractionRefinement-20261007/P8-media').rglob('*'):
 if p.is_file():evidencePaths.add(p)
evidencePaths.update([P8/'media-and-cost.json',P8/'world-ring-inspection.json',P8/'media-encoding.json'])
a['evidence']=[{'path':p.relative_to(ROOT).as_posix(),'bytes':p.stat().st_size,'sha256':sha(p)} for p in sorted(evidencePaths)]
core={p for p in changes if p.startswith('Assets/MassEngine/') and '/Tests/' not in p};assert core=={'Assets/MassEngine/Core/LocalOrderChannel.cs','Assets/MassEngine/Core/LocalOrderPlan.cs','Assets/MassEngine/Core/MassEngineManager.LocalOrders.cs','Assets/MassEngine/Core/Shaders/AgentDataCommon.hlsl'};a['coreChangesExact']=sorted(core)
code=(ROOT/'Assets/Game/Scripts/GpuMemberSelection.cs').read_text(encoding='utf-8')
assert 'WaitForCompletion' not in code and 'WaitAllRequests' not in code
assert 'identity.GetData' not in code and 'agentBuffer.GetData' not in code
assert 'AsyncGPUReadback.Request(mask)' in code and 'AsyncGPUReadback.Request(counts)' in code
assert 'Graphics.DrawMeshInstancedIndirect' in code and 'new GameObject' not in code
hlsl=(ROOT/'Assets/Game/Resources/MemberSelectionBuffers.hlsl').read_text(encoding='utf-8');original=(ROOT/'Assets/MassEngine/Core/Shaders/AgentDataCommon.hlsl').read_text(encoding='utf-8')
for name in ['AgentData','UnitTypeSettings']:
 pattern=r'struct '+name+r'\s*\{.*?\};';assert re.search(pattern,hlsl,re.S)[0]==re.search(pattern,original,re.S)[0]
a['layoutAndReadbackInspectionPassed']=True
assert sha(arc/'extra-files.zip')=='49d58bf32bd6b89cd044481c6786118cc1b02f8fcf7a471dfe269a6df15605d0'
for tag in tags:
 process=json.loads((P8/tag/'process.json').read_text(encoding='utf-8'));assert not process.get('shaderErrors') and not process.get('compileErrors') and process['userDataCheck']['passed'] and not process.get('otherProcessesAfter')

assert len(changes)==30
assert int(json.loads((P8/'scoped-scene-05/process.json').read_text(encoding='utf-8'))['tests']['passed'])==6
assert all(tag in tags for tag in ['scoped-scene-05','scoped-kernels-03','selection-01','local-01','legacy-kernels-01','deployment-01','scenarios-01','editmode-02'])
prior=json.loads((LOG/'P7/publication.json').read_text(encoding='utf-8'))
for r in prior['files']:assert sha(ROOT/r['path'])==r['sha256'],r['path']
router=(ROOT/'Assets/Game/Scripts/WarSandboxCommandHUD.ScopedOrders.cs').read_text(encoding='utf-8');hud=(ROOT/'Assets/Game/Scripts/WarSandboxCommandHUD.cs').read_text(encoding='utf-8')
assert hud.count('RoutePlayerOrderP8(')==4 and 'CaptureMoveIntentP8()' in hud
assert 'scope.Scope==MemberSelectionScope.WholeArmy)return false' in router and 'moveIntent.SameIntent(scope)' in router and 'receiptPlaybackRevision' in router
assert 'MASS_LOCAL_ORDERS' in original
assert (ROOT/'Docs/InteractionRefinement-20261007/P8-media/P8-command-capture.mp4').is_file()
a['publicSelectionEntryEnabled']=True;a['humanInputAccepted']=False;a['universalPerformanceAccepted']=False;a['p9Started']=False;a['p5HumanAcceptance']=False
a['historicalFailures']={}
for tag in ['scoped-scene-01','scoped-scene-02']:
 r=json.loads((P8/tag/'process.json').read_text(encoding='utf-8'));a['historicalFailures'][tag]={k:r[k] for k in ['passed','exitCode','compileErrors','tests','failedTests'] if k in r}

a['activeProcesses']=processes();a['lock']=(ROOT/'Temp/UnityLockfile').exists();a['finishedAt']=datetime.datetime.now().isoformat();a['passed']=a['project']['passed'] and a['userData']['passed'] and a['current37']['passed'] and all(a['git'].values()) and a['existingChangesExact'] and a['official48Unchanged'] and not duplicates and all(r['passed'] and int(r.get('tests',{}).get('skipped','0'))==0 for r in a['runs'].values()) and not a['activeProcesses'] and not a['lock'];save(dest,a);print(json.dumps({k:v for k,v in a.items() if k not in ['approvedChanges','evidence']},ensure_ascii=False,indent=2));sys.exit(0 if a['passed'] else 1)






