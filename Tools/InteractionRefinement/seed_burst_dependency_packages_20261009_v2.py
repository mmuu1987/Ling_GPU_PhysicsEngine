from pathlib import Path
import json,shutil,hashlib,subprocess,sys
sys.stdout.reconfigure(encoding='utf-8')
R=Path(__file__).resolve().parents[2];W=R/'outputs/BurstDependencyRepair-20261009-01';P=R/'outputs/B9'
assert (W/'fixture-repair.json').is_file() and not(P/'Temp/UnityLockfile').exists()
lock=json.loads((P/'Packages/packages-lock.json').read_text())['dependencies']
records=[]
for name in ['com.unity.burst','com.unity.render-pipelines.core','com.unity.render-pipelines.universal','com.unity.shadergraph']:
 candidates=[]
 for path in (R/'Library/PackageCache').glob(name+'@*'):
  f=path/'package.json'
  if f.is_file():
   obj=json.loads(f.read_text(encoding='utf-8-sig'))
   if obj['name']==name and obj['version']==lock[name]['version']:candidates.append(path)
 assert len(candidates)==1,(name,candidates)
 src=candidates[0];dst=P/'Library/PackageCache'/src.name
 print('VERIFY EXISTING' if dst.exists() else 'COPY PINNED PACKAGE',name,lock[name]['version'],flush=True)
 if not dst.exists():shutil.copytree(src,dst)
 files={f.relative_to(src).as_posix():hashlib.sha256(f.read_bytes()).hexdigest() for f in src.rglob('*') if f.is_file()}
 assert all(hashlib.sha256((dst/f).read_bytes()).hexdigest()==h for f,h in files.items())
 records.append({'name':name,'version':lock[name]['version'],'source':str(src),'destination':str(dst),'files':files})
(W/'package-source-cache-receipt.json').write_text(json.dumps({'scope':'Pinned package source copy only; no script assemblies, AssetDatabase, project JIT or mother cached compilation reused','packages':records},indent=2))
print('PINNED PACKAGE SOURCES VERIFIED; launching fresh compilation',flush=True)
r=subprocess.run([sys.executable,str(R/'Tools/InteractionRefinement/validate_burst_dependency_repair_20261009_v2.py')],cwd=R)
sys.exit(r.returncode)
