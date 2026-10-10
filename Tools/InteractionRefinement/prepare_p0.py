"""P0 evidence preparation only. Writes Tools/Logs artifacts, never modifies Assets or Builds."""
from pathlib import Path
import os,sys,json,re,hashlib,subprocess,datetime,shutil,concurrent.futures,platform
sys.stdout.reconfigure(encoding='utf-8')
R=Path(__file__).resolve().parents[2]; L=R/'Logs/InteractionRefinement-20261007'; B=L/'baseline'; P=L/'P0'
assert (R/'Assets/Game/Scenes/MainMenu.unity').is_file()
assert not L.exists(),'Baseline exists; inspect instead of replacing it'
L.mkdir();B.mkdir();P.mkdir();(L/'backups').mkdir()
def write(path,data):
 path.parent.mkdir(parents=True,exist_ok=True)
 with path.open('x',encoding='utf-8',newline='\n') as f:f.write(json.dumps(data,ensure_ascii=False,indent=2))
def sha(p):
 h=hashlib.sha256()
 with p.open('rb') as f:
  for c in iter(lambda:f.read(2**20),b''):h.update(c)
 return h.hexdigest()
def record(p,root=R):
 before=p.stat();s=sha(p);after=p.stat()
 assert (before.st_size,before.st_mtime_ns)==(after.st_size,after.st_mtime_ns),'File changed during baseline: '+str(p)
 return {'path':p.relative_to(root).as_posix(),'bytes':after.st_size,'mtime_ns':after.st_mtime_ns,'sha256':s}
def walk(root):
 result=[]
 if not root.exists():return result
 for base,dirs,files in os.walk(root,followlinks=False):
  for name in dirs+files:
   p=Path(base)/name;assert not p.is_symlink() and not(p.lstat().st_file_attributes & 0x400),'Reparse entry: '+str(p)
  result.extend(Path(base)/n for n in files)
 return sorted(result)
def ps(code):
 prefix="[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new(); $ErrorActionPreference='Stop'; "
 r=subprocess.run(['powershell','-NoProfile','-Command',prefix+code],capture_output=True,encoding='utf-8',timeout=45,check=True)
 return json.loads(r.stdout) if r.stdout.strip() else []
procerror=None
try:
 procs=ps("Get-CimInstance Win32_Process | Where-Object { $_.Name -eq 'Unity.exe' -or $_.Name -eq 'WarSandbox.exe' -or ($_.ExecutablePath -and $_.ExecutablePath.StartsWith('"+str(R/'Builds').replace("'","''")+"\\',[System.StringComparison]::OrdinalIgnoreCase)) } | Select-Object ProcessId,Name,ExecutablePath,CommandLine | ConvertTo-Json -Depth 4 -Compress")
 if isinstance(procs,dict):procs=[procs]
 for proc in procs:
  cmd=proc.pop('CommandLine','')
  proc['role']='AssetImportWorker' if 'AssetImportWorker' in cmd else 'EditorOrPlayer'
  proc['matchesProject']=str(R).replace('\\','/').lower() in cmd.replace('\\','/').lower()
  proc['commandLineOmitted']=True
except Exception as e:procs=[];procerror=repr(e)
pre={'createdAt':datetime.datetime.now().isoformat(),'workspace':str(R),'activeProcesses':procs,'processQueryError':procerror,'unityLockExists':(R/'Temp/UnityLockfile').exists(),'unityPath':r'D:\soft\Unity6\6000.3.14f1\Editor\Unity.exe','diskFreeBytes':shutil.disk_usage(R).free,'python':platform.python_version(),'unityVersion':(R/'ProjectSettings/ProjectVersion.txt').read_text(encoding='utf-8')}
write(P/'preflight.json',pre);print('PREFLIGHT',json.dumps(pre,ensure_ascii=False),flush=True)
try:
 hardware=ps("[pscustomobject]@{GPU=@(Get-CimInstance Win32_VideoController | Select-Object Name,DriverVersion,AdapterRAM,CurrentHorizontalResolution,CurrentVerticalResolution); CPU=@(Get-CimInstance Win32_Processor | Select-Object Name,NumberOfCores,NumberOfLogicalProcessors); Memory=@(Get-CimInstance Win32_ComputerSystem | Select-Object TotalPhysicalMemory)} | ConvertTo-Json -Depth 5 -Compress")
except Exception as e:hardware={'error':repr(e)}
write(B/'hardware.json',hardware)
for label,args in [('git-status',['status','--porcelain=v1','--untracked-files=all']),('git-head',['rev-parse','HEAD']),('git-diff-names',['diff','--name-status']),('git-staged-names',['diff','--cached','--name-status'])]:
 result=subprocess.run(['git','-c','core.quotepath=false']+args,cwd=R,capture_output=True,encoding='utf-8',errors='replace',timeout=90)
 (B/(label+'.txt')).write_text(result.stdout,encoding='utf-8');assert result.returncode==0,(label,result.stderr)
files=[]
for root in ['Assets','ProjectSettings','Packages']:files.extend(walk(R/root))
print('HASHING_PROJECT_FILES',len(files),flush=True)
with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool:manifest=list(pool.map(record,files))
write(B/'project-files.sha256.json',manifest)
print('PROJECT_HASH_COMPLETE',len(manifest),sum(x['bytes'] for x in manifest),flush=True)
# Exact current37 package identity, compared to pre-cleanup hashes.
keep=R/'Builds/CaptureDefault-20261006-37';old=json.loads((R/'Logs/BuildCleanup-20261007/kept-release.sha256.json').read_text(encoding='utf-8'))
with concurrent.futures.ThreadPoolExecutor(max_workers=3) as pool:kept=list(pool.map(lambda p:record(p,keep),walk(keep)))
write(B/'current37.sha256.json',kept)
oldmap={x['path']:x for x in old};nowmap={x['path']:x for x in kept}
keptdiff=[n for n in sorted(set(oldmap)|set(nowmap)) if n not in oldmap or n not in nowmap or (oldmap[n]['bytes'],oldmap[n]['sha256'])!=(nowmap[n]['bytes'],nowmap[n]['sha256'])]
print('CURRENT37',len(kept),'files; mismatches',len(keptdiff),flush=True)
# Preserve source/configuration bytes broadly enough to back the proposed phases, without binary asset archives.
meta={};texts={}
for p in files:
 if p.suffix=='.meta':
  t=p.read_text(encoding='utf-8-sig',errors='replace');m=re.search(r'^guid:\s*([a-f0-9]{32})',t,re.M)
  if m:meta[m.group(1)]=p.with_suffix('').relative_to(R).as_posix()
write(B/'guid-paths.json',meta)
def text(p):
 if p not in texts:texts[p]=(R/p).read_text(encoding='utf-8-sig',errors='replace')
 return texts[p]
def ref(t,k):
 m=re.search(r'^  '+re.escape(k)+r':.*?guid:\s*([a-f0-9]{32})',t,re.M)
 return meta.get(m.group(1)) if m else None
def val(t,k):
 m=re.search(r'^  '+re.escape(k)+r':\s*(.*)$',t,re.M);return m.group(1).strip() if m else None
unitScript=next(g for g,p in meta.items() if p=='Assets/MassEngine/UnitTypes/UnitTypeConfig.cs')
units=[];configPaths=set()
for p in files:
 if p.suffix!='.asset' or p.stat().st_size>262144:continue
 rel=p.relative_to(R).as_posix();t=text(rel)
 if not re.search(r'm_Script:.*guid:\s*'+unitScript,t):continue
 row={'asset':rel,'name':val(t,'unitTypeName'),'teamId':val(t,'teamId'),'spawn':ref(t,'spawnConfig'),'flocking':ref(t,'flockingConfig'),'movement':ref(t,'movementConfig'),'combat':ref(t,'combatConfig'),'render':ref(t,'renderConfig')}
 for field in ['spawn','flocking','movement','combat','render']:
  if row[field]:configPaths.add(row[field])
 configPaths.add(rel)
 if row['spawn']:
  st=text(row['spawn']);row['serializedSpawn']={k:val(st,k) for k in ['unitCount','spawnCenter','formationDensity','formationAspect','formationJitterFraction','spawnSize']}
 if row['flocking']:
  ft=text(row['flocking']);row['serializedFlocking']={k:val(ft,k) for k in ['agentRadius','separationStrength','densityAvoidanceStrength','densityComfortPerSqm']}
 units.append(row)
write(B/'serialized-unit-radius-inventory.json',units)
scenes=[]
for name in ['MainMenu','Green','Autumn','Winter']:
 rel='Assets/Game/Scenes/'+name+'.unity';s=text(rel);scenario=ref(s,'scenarioConfig')
 refs=re.findall(r'^  - \{fileID:.*?guid:\s*([a-f0-9]{32})',text(scenario),re.M) if scenario else []
 scenunits=[meta[g] for g in refs if g in meta and any(u['asset']==meta[g] for u in units)]
 scenes.append({'scene':rel,'scenario':scenario,'serializedUnitAssets':scenunits,'note':'Serialized defaults only; not runtime override verification'})
write(B/'formal-scene-links.json',scenes)
backup=[];manifestmap={x['path']:x for x in manifest}
for row in manifest:
 rel=row['path'];p=R/rel
 isCode=(rel.startswith('Assets/Game/') or rel.startswith('Assets/MassEngine/')) and p.suffix.lower() in {'.cs','.hlsl','.compute','.shader','.asmdef','.asmref','.cginc','.uss'}
 smallConfig=rel.startswith(('ProjectSettings/','Packages/')) or rel in configPaths or rel.startswith('Assets/Game/Scenes/')
 if (isCode or smallConfig) and row['bytes']<16*1024*1024:
  backup.append(rel)
  if rel+'.meta' in manifestmap:backup.append(rel+'.meta')
backup=sorted(set(backup));copied=[]
for rel in backup:
 source=R/rel;dest=L/'backups'/rel;dest.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(source,dest)
 assert sha(dest)==manifestmap[rel]['sha256'],'Backup verification failed: '+rel
 copied.append(manifestmap[rel])
write(B/'backup-manifest.json',copied)
# Only this product's non-log user files; record content hashes, do not copy or rewrite them.
settings=text('ProjectSettings/ProjectSettings.asset');company=val(settings,'companyName');product=val(settings,'productName')
userroot=Path(os.environ['USERPROFILE'])/'AppData/LocalLow'/str(company)/str(product)
userdata=[]
if userroot.exists():
 for p in walk(userroot):
  relative=p.relative_to(userroot).as_posix()
  if ('warsandbox' in relative.casefold() or 'war-sandbox' in relative.casefold()) and p.suffix.lower() not in {'.log','.dmp'}:userdata.append(record(p,userroot))
write(B/'user-data.sha256.json',{'root':str(userroot),'files':userdata,'scope':'Only WarSandbox-named product files; unrelated files and volatile logs excluded; no content copied'})
# Static test discovery is not execution or a claimed NUnit test count.
tests=[]
for p in files:
 rel=p.relative_to(R).as_posix()
 if '/Tests/' in rel and p.suffix=='.cs':
  t=p.read_text(encoding='utf-8-sig');tests.append({'path':rel,'classes':re.findall(r'\bclass\s+(\w+)',t),'attributeOccurrences':len(re.findall(r'\[(?:Test|TestCase|UnityTest|TestCaseSource)\b',t))})
write(B/'test-source-inventory.json',tests)
summary={'prepared':not keptdiff,'createdAt':datetime.datetime.now().isoformat(),'projectFiles':len(manifest),'projectBytes':sum(x['bytes'] for x in manifest),'originalMetas':sum(x['path'].endswith('.meta') for x in manifest),'backupFiles':len(copied),'backupBytes':sum(x['bytes'] for x in copied),'current37Files':len(kept),'current37Mismatches':keptdiff,'unitTypeAssets':len(units),'formalScenes':scenes,'testSourceFiles':len(tests),'userDataFiles':len(userdata),'activeProcesses':procs,'processQueryError':procerror,'unityLockExists':pre['unityLockExists'],'testsRun':False,'gpuBaselineRun':False,'humanMouseTest':False,'performanceMeasured':False,'newBuildProduced':False,'phase':'P0 only','backupScope':'Protective coverage superset; does not authorize editing all backed-up files'}
write(P/'preparation.json',summary);print('PREPARATION',json.dumps(summary,ensure_ascii=False,indent=2),flush=True)
if keptdiff:sys.exit(1)
