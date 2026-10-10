from common_p7 import *
import sys,re,concurrent.futures,zipfile,hashlib
sys.stdout.reconfigure(encoding='utf-8');tags=sys.argv[1:];assert len(tags)>=2;dest=P7/'audit-02.json';assert not dest.exists();assert not processes() and not(ROOT/'Temp/UnityLockfile').exists()
a={'phase':'P7','checkedAt':datetime.datetime.now().isoformat(),'project':project_check(full=True),'p3DeferredByUser':True,'p4HumanAcceptance':False,'newBuildProduced':False}
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
changes=json.loads((P7/'approved-changes.json').read_text(encoding='utf-8'));baseline=json.loads((P7/'baseline.json').read_text(encoding='utf-8'))['files'];expected={'Assets/Game/Scripts/WarSandboxCommandHUD.Details26.cs', 'Assets/Game/Scripts/WarSandboxCommandHUD.Toy.cs', 'Assets/Game/Tests/PlayMode/DeploymentResizePlayModeTests.cs', 'Assets/Game/Tests/PlayMode/LocalOrdersSceneTests.cs', 'Assets/Game/Scripts/WarSandboxCommandHUD.Commands25.cs', 'Assets/Game/Scripts/CameraControls/MyCameraManager.cs', 'Assets/Game/Scripts/WarSandboxCommandHUD.cs', 'Assets/MassEngine/Tests/PlayMode/MassEngineGpuKernelTests.P6.cs', 'Assets/Game/Tests/PlayMode/DeploymentTranslationPlayModeTests.cs'};a['existingChangesExact']=set(changes)&set(baseline)==expected;a['approvedChanges']=changes
assets=['Assets/Game/Content/Characters/UnifiedRoster/Version03/Library/'+role+'_Flocking.asset' for role in ['male','knight']];a['official48Unchanged']=all(sha(ROOT/p)==baseline[p] and b'separationStrength: 48' in(ROOT/p).read_bytes() for p in assets)
guids={};duplicates=[]
for p in(ROOT/'Assets').rglob('*.meta'):
 m=re.search(r'^guid:\s*([a-f0-9]{32})',p.read_text(encoding='utf-8',errors='replace'),re.M)
 if m:
  if m[1] in guids:duplicates.append([guids[m[1]],p.relative_to(ROOT).as_posix()])
  guids[m[1]]=p.relative_to(ROOT).as_posix()
a['duplicateGuids']=duplicates;a['runs']={}
for tag in tags:
 r=json.loads((P7/tag/'process.json').read_text(encoding='utf-8'));a['runs'][tag]={k:r[k] for k in ['passed','exitCode','compileErrors','tests','failedTests','restoredAutoFiles','isolationMarkerSeen'] if k in r}
arc=ROOT/'Logs/WorkspaceArchive-20261007-01';manifest=json.loads((arc/'manifest.json').read_text(encoding='utf-8'));assert len(manifest['files'])==462
with zipfile.ZipFile(arc/'extra-files.zip') as z:
 assert z.testzip() is None
 for r in manifest['files']:
  s=r['storage'];data=(ROOT/s['path']).read_bytes() if s['kind']=='existing-file' else z.read(s['entry']);assert len(data)==r['bytes'] and hashlib.sha256(data).hexdigest()==r['sha256'],r['path']
a['archive']={'filesVerified':462,'manifestSha256':sha(arc/'manifest.json'),'migrationReceipts':[p.relative_to(ROOT).as_posix() for p in sorted((P7/'changes').glob('*/archive-migrations.json'))]}
a['evidence']=[{'path':p.relative_to(ROOT).as_posix(),'bytes':p.stat().st_size,'sha256':sha(p)} for tag in tags for folder in ['p4-evidence','p5-evidence','.'] for p in sorted((P7/tag/folder).glob('*')) if p.is_file()]
a['runtimeEngineAndCombatShadersUnchanged']=not any(p.startswith('Assets/MassEngine/') and '/Tests/' not in p for p in changes)
assert a['runtimeEngineAndCombatShadersUnchanged']
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
 process=json.loads((P7/tag/'process.json').read_text(encoding='utf-8'));assert not process.get('shaderErrors') and not process.get('compileErrors') and process['userDataCheck']['passed'] and not process.get('otherProcessesAfter')
prior=json.loads((P7/'audit-01.json').read_text(encoding='utf-8'));assert prior['passed'];delta=[p for p,r in prior['approvedChanges'].items() if sha(ROOT/p)!=r['expectedSha256']];assert delta==['Assets/Game/Tests/PlayMode/MemberSelectionPlayModeTests.cs'];a['sinceRegressionAuditChangedOnlyTest']=delta
a['activeProcesses']=processes();a['lock']=(ROOT/'Temp/UnityLockfile').exists();a['finishedAt']=datetime.datetime.now().isoformat();a['passed']=a['project']['passed'] and a['userData']['passed'] and a['current37']['passed'] and all(a['git'].values()) and a['existingChangesExact'] and a['official48Unchanged'] and not duplicates and all(r['passed'] and int(r.get('tests',{}).get('skipped','0'))==0 for r in a['runs'].values()) and not a['activeProcesses'] and not a['lock'];save(dest,a);print(json.dumps({k:v for k,v in a.items() if k not in ['approvedChanges','evidence']},ensure_ascii=False,indent=2));sys.exit(0 if a['passed'] else 1)





