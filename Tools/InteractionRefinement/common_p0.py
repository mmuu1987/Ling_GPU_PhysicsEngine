from pathlib import Path
import hashlib,json,os,subprocess,datetime
ROOT=Path(__file__).resolve().parents[2];LOG=ROOT/'Logs/InteractionRefinement-20261007';BASE=LOG/'baseline';P0=LOG/'P0'
ALLOWED_NEW={'Assets/Game/Editor/InteractionRefinementP0.cs','Assets/Game/Editor/InteractionRefinementP0.cs.meta'}
def sha(p):
 h=hashlib.sha256()
 with p.open('rb') as f:
  for c in iter(lambda:f.read(1048576),b''):h.update(c)
 return h.hexdigest()
def save(path,data):
 path.parent.mkdir(parents=True,exist_ok=True);temp=path.with_suffix(path.suffix+'.tmp');temp.write_text(json.dumps(data,ensure_ascii=False,indent=2),encoding='utf-8');os.replace(temp,path)
def processes():
 code="[Console]::OutputEncoding=[System.Text.UTF8Encoding]::new(); $ErrorActionPreference='Stop'; Get-CimInstance Win32_Process | Where-Object { $_.Name -eq 'Unity.exe' -or $_.Name -eq 'WarSandbox.exe' } | Select-Object ProcessId,Name,ExecutablePath | ConvertTo-Json -Compress"
 r=subprocess.run(['powershell','-NoProfile','-Command',code],capture_output=True,encoding='utf-8',check=True,timeout=45)
 data=json.loads(r.stdout) if r.stdout.strip() else [];return [data] if isinstance(data,dict) else data
def project_check(full=False):
 original=json.loads((BASE/'project-files.sha256.json').read_text(encoding='utf-8'));expected={r['path']:r for r in original};current=set();changed=[];statChanged=[];approvedChanges=[]
 registry=P0/'approved-source-changes.json';approved=json.loads(registry.read_text(encoding='utf-8')) if registry.exists() else {}
 for p,r in approved.items():assert p in expected and r['originalSha256']==expected[p]['sha256'],'Invalid repair registry'
 for root in ['Assets','ProjectSettings','Packages']:
  for parent,dirs,names in os.walk(ROOT/root,followlinks=False):
   for n in dirs+names:
    p=Path(parent)/n;assert not p.is_symlink() and not(p.lstat().st_file_attributes&0x400),'Reparse point appeared: '+str(p)
   for n in names:current.add((Path(parent)/n).relative_to(ROOT).as_posix())
 for path,r in expected.items():
  p=ROOT/path
  if path not in current:changed.append({'path':path,'kind':'missing'});continue
  st=p.stat();different=(st.st_size,st.st_mtime_ns)!=(r['bytes'],r['mtime_ns'])
  if different:statChanged.append(path)
  if path in approved:
   actual=sha(p)
   if actual!=approved[path]['expectedSha256']:changed.append({'path':path,'kind':'unexpected-modification-to-repaired-file'})
   else:approvedChanges.append({'path':path,**approved[path]})
  elif (full or different) and sha(p)!=r['sha256']:changed.append({'path':path,'kind':'modified'})
 newRegistry=P0/'approved-new-files.json';allowedNew=ALLOWED_NEW | (set(json.loads(newRegistry.read_text(encoding='utf-8'))) if newRegistry.exists() else set())
 unexpected=sorted(current-set(expected)-allowedNew)
 return {'checkedAt':datetime.datetime.now().isoformat(),'mode':'full-sha256' if full else 'stat-assisted-sha256','originalFiles':len(expected),'changed':changed,'approvedChanges':approvedChanges,'unexpectedNew':unexpected,'ownedNew':sorted(current&allowedNew),'statChangedButPossiblySameBytes':statChanged,'passed':not changed and not unexpected}
def user_check():
 b=json.loads((BASE/'user-data.sha256.json').read_text(encoding='utf-8'));root=Path(b['root']);changed=[]
 for r in b['files']:
  p=root/r['path']
  if not p.is_file() or sha(p)!=r['sha256']:changed.append(r['path'])
 return {'passed':not changed,'filesChecked':len(b['files']),'changed':changed}
if __name__=='__main__':
 import sys
 sys.stdout.reconfigure(encoding='utf-8');name=sys.argv[1]
 assert name=='after-user-close'
 d=project_check();d['userData']=user_check();d['activeProcesses']=processes();d['unityLockExists']=(ROOT/'Temp/UnityLockfile').exists()
 save(P0/'after-user-close.json',d);print(json.dumps(d,ensure_ascii=False,indent=2),flush=True)


