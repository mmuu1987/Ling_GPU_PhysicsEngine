from pathlib import Path
import json,hashlib,subprocess,concurrent.futures,os,sys
sys.stdout.reconfigure(encoding='utf-8')
R=Path(__file__).resolve().parents[2];O=R/'outputs/P9Combined-20261009-01';P=R/'outputs/P11';D=R/'outputs/P9ColdAttack-20261009-01'
b=json.loads((O/'mother-before.json').read_text(encoding='utf-8'));src=json.loads((O/'source-manifest.json').read_text(encoding='utf-8'))['files'];h=json.loads((O/'harness-manifest.json').read_text(encoding='utf-8'));assert not(D/'final-audit.json').exists()
q=subprocess.run(['powershell','-NoProfile','-Command','Get-CimInstance Win32_Process | Where-Object {$_.Name -eq "Unity.exe" -or $_.Name -eq "WarSandbox.exe"} | Select-Object ProcessId,CommandLine | ConvertTo-Json -Compress'],capture_output=True,text=True,encoding='utf-8',timeout=40,check=True);assert not q.stdout.strip(),'Final full audit requires no active game/editor'
def sha(p):
 if not p.is_file():return None
 before=p.stat();v=hashlib.sha256()
 with p.open('rb') as f:
  for data in iter(lambda:f.read(1048576),b''):v.update(data)
 after=p.stat();assert (before.st_size,before.st_mtime_ns)==(after.st_size,after.st_mtime_ns),str(p)
 return v.hexdigest()
def check(item):
 root,f,want=item;got=sha(root/f)
 return {'path':f,'expectedSha':want,'actualSha':got} if got!=want else None

# Remove only the two diagnostic files created in this investigation, preserving bytes outside Assets.
diagnostic=json.loads((D/'diagnostic-harness.json').read_text(encoding='utf-8'))
archive=D/'removed-diagnostic-harness';assert not archive.exists();archive.mkdir()
assert len(diagnostic)==2
for f,want in diagnostic.items():
 p=P/f;assert sha(p)==want and p.name in ['P9ColdAttackDiagnosticTests.cs','P9ColdAttackDiagnosticTests.cs.meta']
for f,want in diagnostic.items():(P/f).rename(archive/Path(f).name)
cache=json.loads((D/'cache-aba.json').read_text(encoding='utf-8'));C=P/'Library/ShaderCache/compute/AgentCombatSimulation79e5'
actualCache={p.relative_to(C).as_posix():{'sha256':sha(p),'size':p.stat().st_size,'mtime_ns':p.stat().st_mtime_ns} for p in C.rglob('*') if p.is_file()}
assert actualCache==cache['original'] and not(D/'combat-cache-escrow').exists()
(D/'cleanup.json').write_text(json.dumps({'diagnosticFilesArchivedAndRemoved':diagnostic,'originalCacheExactlyRestored':True,'originalFixtureUntouched':True},indent=2),encoding='utf-8')

print('FULL SHA256 FINAL AUDIT',len(b['files']),'mother files and',len(src),'isolated sources',flush=True)
with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool:
 mother=[v for v in pool.map(check,[(R,f,x) for f,x in b['files'].items()]) if v]
 source=[v for v in pool.map(check,[(P,f,x['sha256']) for f,x in src.items()]) if v]
user=Path(b['userRoot']);uf={p.relative_to(user).as_posix():sha(p) for p in user.rglob('*') if p.is_file()}
env=os.environ.copy();env['GIT_OPTIONAL_LOCKS']='0';head=subprocess.check_output(['git','rev-parse','HEAD'],cwd=R,env=env).decode().strip()
actual={p.relative_to(P).as_posix() for folder in ['Assets','Packages','ProjectSettings'] for p in (P/folder).rglob('*') if p.is_file()};extra=sorted(actual-set(src)-set(h))
currentMother={p.relative_to(R).as_posix() for folder in ['Assets','Packages','ProjectSettings'] for p in (R/folder).rglob('*') if p.is_file()};originalMother={f for f in b['files'] if f.split('/')[0] in ['Assets','Packages','ProjectSettings']}
s={'status':'audited','mode':'full SHA256, not stat-only','protectedMotherFiles':len(b['files']),'motherChanges':mother,'motherAddedOrRemoved':sorted(currentMother^originalMother),'motherHeadUnchanged':head==b['head'],'motherIndexUnchanged':sha(R/'.git/index')==b['index'],'userFilesChecked':len(uf),'userDataExactlyRestored':uf==b['userFiles'],'candidateExpectedSources':len(src),'candidateSourceChanges':source,'candidateAdditionalFiles':extra,'harnessMatchesFinalManifest':all(sha(P/f)==x for f,x in h.items()),'productionCodeChanged':any(Path(x['path']).suffix in ['.cs','.asmdef','.hlsl','.compute','.shader'] for x in source),'noActiveEditorOrPlayer':True}
s['motherProtectionPassed']=not mother and not s['motherAddedOrRemoved'] and s['motherHeadUnchanged'] and s['motherIndexUnchanged'] and s['userDataExactlyRestored'];s['candidateSourceExact']=not source and not extra
(D/'final-audit.json').write_text(json.dumps(s,ensure_ascii=False,indent=2),encoding='utf-8');print(json.dumps(s,ensure_ascii=False,indent=2),flush=True)
assert s['candidateSourceExact'] and s['motherProtectionPassed'] and s['harnessMatchesFinalManifest'] and not s['productionCodeChanged']
