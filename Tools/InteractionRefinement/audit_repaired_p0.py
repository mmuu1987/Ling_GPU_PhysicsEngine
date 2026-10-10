from common_p0 import *
import sys,re,concurrent.futures,xml.etree.ElementTree as ET
sys.stdout.reconfigure(encoding='utf-8')
tag=sys.argv[1];assert re.fullmatch('[a-z0-9-]+',tag);destination=P0/(tag+'.json');assert not destination.exists()
assert not processes() and not(ROOT/'Temp/UnityLockfile').exists(),'Editor/player active; audit deferred, not forcibly closed'
r={'phase':'P0','checkedAt':datetime.datetime.now().isoformat(),'project':project_check(full=True),'newBuildProduced':False,'p1Started':False,'humanMouseTest':False}
b=json.loads((BASE/'user-data.sha256.json').read_text(encoding='utf-8'));root=Path(b['root']);old={x['path']:x['sha256'] for x in b['files']};now={}
if root.exists():
 for p in root.rglob('*'):
  if p.is_file():
   rel=p.relative_to(root).as_posix()
   if ('warsandbox' in rel.casefold() or 'war-sandbox' in rel.casefold()) and p.suffix.lower() not in {'.log','.dmp'}:now[rel]=sha(p)
diff=[p for p in sorted(set(old)|set(now)) if old.get(p)!=now.get(p)];r['userData']={'before':len(old),'after':len(now),'deltas':diff,'passed':not diff}
keep=ROOT/'Builds/CaptureDefault-20261006-37';old={x['path']:x['sha256'] for x in json.loads((BASE/'current37.sha256.json').read_text(encoding='utf-8'))};files=[p for p in keep.rglob('*') if p.is_file()]
with concurrent.futures.ThreadPoolExecutor(max_workers=3) as pool:now=dict(pool.map(lambda p:(p.relative_to(keep).as_posix(),sha(p)),files))
diff=[p for p in sorted(set(old)|set(now)) if old.get(p)!=now.get(p)];r['current37']={'files':len(now),'deltas':diff,'passed':not diff,'mode':'fresh-full-sha256'}
r['git']={}
for label,args in [('git-head',['rev-parse','HEAD']),('git-staged-names',['diff','--cached','--name-status'])]:
 data=subprocess.check_output(['git','-c','core.quotepath=false']+args,cwd=ROOT,encoding='utf-8',timeout=60)
 r['git'][label+'-unchanged']=data==(BASE/(label+'.txt')).read_text(encoding='utf-8')
new=json.loads((P0/'approved-new-files.json').read_text(encoding='utf-8'));r['fixturePostImportChanges']=[];r['unexpectedFixturePayloadChanges']=[]
for name,info in new.items():
 actual=sha(ROOT/name) if(ROOT/name).is_file() else None
 if actual!=info['initialSha256']:
  row={'path':name,'initialSha256':info['initialSha256'],'currentSha256':actual};r['fixturePostImportChanges'].append(row)
  if not name.endswith(('.meta','.mat')) or actual is None:r['unexpectedFixturePayloadChanges'].append(row)
r['ownedFiles']=[{'path':p,'sha256':sha(ROOT/p)} for p in sorted(set(new)|ALLOWED_NEW)]
guids={};duplicates=[]
for p in (ROOT/'Assets').rglob('*.meta'):
 m=re.search(r'^guid:\s*([a-f0-9]{32})',p.read_text(encoding='utf-8',errors='replace'),re.M)
 if m:
  if m[1] in guids:duplicates.append([guids[m[1]],p.relative_to(ROOT).as_posix()])
  guids[m[1]]=p.relative_to(ROOT).as_posix()
r['duplicateGuids']=duplicates
r['runs']={}
for name in ['editmode-critical-02','editmode-03','capture-01','move-01','hold-01','retreat-01','rendered-performance-01','rendered-performance-02','rendered-performance-03']:
 p=P0/name/'process.json'
 if not p.exists():r['runs'][name]={'notRun':True};continue
 d=json.loads(p.read_text(encoding='utf-8'));entry={'passed':d['passed'],'exitCode':d['exitCode'],'compileErrors':d.get('compileErrors',[]),'restoredAutoFiles':d.get('restoredAutoFiles',[])}
 if 'tests'in d:entry['tests']=d['tests'];entry['failedTests']=d['failedTests']
 if 'performance'in d:entry['performance']=d['performance']
 if 'observationSummary'in d:entry['observations']=d['observationSummary']
 log=(P0/name/'Editor.log').read_text(encoding='utf-8',errors='replace')
 entry['noAudioListenerWarnings']=sum('There are no audio listeners in the scene' in l for l in log.splitlines())
 entry['compilerWarnings']=sorted(set(re.findall(r'^.*warning CS\d+.*$',log,re.M)))
 entry['graphicsDeviceLines']=[l.strip() for l in log.splitlines() if re.match(r'^\s*(Renderer:|Vendor:|VRAM:|Driver:)',l)]
 r['runs'][name]=entry
perf=[v['performance'] for v in r['runs'].values() if 'performance'in v]
r['timingCoverage']={'repeats':len(perf),'sameResolution':len({(p['width'],p['height']) for p in perf})==1,'sameGpu':len({p['gpu'] for p in perf})==1,'allRendered':len(perf)==3 and all(p.get('renderedEditor') and p.get('renderedGameSamples',0)>=100 for p in perf),'allCameraFixed':len(perf)==3 and all(p.get('cameraFixed') for p in perf),'sameCameraPosition':len({tuple(p.get('cameraStart',{}).get(k) for k in ['x','y','z']) for p in perf})==1,'gpuTimingAllAvailable':len(perf)==3 and all(p['gpuTimingAvailable'] for p in perf),'editorOnly':True,'window':'10s warmup then battle seconds 10-30; not long-battle/peak-combat performance','collectorOverhead':'Includes Editor and lightweight collector allocations; no synchronous agent readback in performance runs'}
r['batchPerformanceNotQualified']=['performance-01','performance-02','performance-03']
r['profilerFallbackNote']='Optional Profiler reflection setup failed in GUI trials. Valid GPU timings came from FrameTimingManager; unsupported optional flag is now disabled for future runs.'
r['globalEditorLayoutCaveat']='First GUI run restored selected preset/maximization and project layout bytes, but had no pre-run byte snapshot of the global current layout. Later GUI runs additionally restored the specific global files from exact pre-run backups.'
r['activeProcessesAfter']=processes();r['unityLockExists']=(ROOT/'Temp/UnityLockfile').exists();r['finishedAt']=datetime.datetime.now().isoformat()
r['protectionPassed']=r['project']['passed'] and r['userData']['passed'] and r['current37']['passed'] and all(r['git'].values()) and not duplicates and not r['unexpectedFixturePayloadChanges']
r['automatedRunsPassed']=all(v.get('passed',False) for v in r['runs'].values())
r['status']='AUTOMATED_BASELINE_RESTORED_PENDING_MANUAL_ACCEPTANCE' if r['automatedRunsPassed'] and r['protectionPassed'] else 'INCOMPLETE_OR_BLOCKED'
if not r['timingCoverage']['gpuTimingAllAvailable']:r['status']='PENDING_GPU_TIMING_AND_MANUAL_ACCEPTANCE';r['timingCoverage']['limitation']='GPU frame timing unavailable/incomplete; never interpret unavailable zero samples as zero GPU cost.'
save(destination,r);print(json.dumps({k:v for k,v in r.items() if k not in ['ownedFiles','runs','fixturePostImportChanges']},ensure_ascii=False,indent=2),flush=True)

