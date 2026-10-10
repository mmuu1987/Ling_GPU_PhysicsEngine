from pathlib import Path
import json,hashlib,zipfile,difflib,sys
sys.stdout.reconfigure(encoding='utf-8')
R=Path(__file__).resolve().parents[2];W=R/'outputs/BurstDependencyRepair-20261009-01';P=R/'outputs/B9'
s=json.loads((W/'validation-02/summary.json').read_text(encoding='utf-8'))
assert s['status']!='running' and s['motherProtection']['passed']
assert not(P/'Temp/UnityLockfile').exists()
f='Assets/MassEngine/Tests/EditMode/BurstDependencyRepairRuntimeTests.cs'
text=(P/f).read_text(encoding='utf-8-sig');old=text
needle='throw new Exception("Missing output");'
assert text.count(needle)==1;text=text.replace(needle,'return null;')
needle='System.IO.File.WriteAllText(System.IO.Path.Combine(Output(),"runtime-gate.json"),JsonUtility.ToJson(q,true));'
assert text.count(needle)==1
text=text.replace(needle,'string output=Output();if(output!=null){System.IO.Directory.CreateDirectory(output);System.IO.File.WriteAllText(System.IO.Path.Combine(output,"runtime-gate.json"),JsonUtility.ToJson(q,true));}')
(P/f).write_bytes(text.encode());(W/'repair-files'/f).write_bytes(text.encode())
m=json.loads((W/'repair-manifest.json').read_text(encoding='utf-8'))
m['changes'][f]['newSha']=hashlib.sha256((P/f).read_bytes()).hexdigest()
m['changes'][f]['source']='Historical actual Runtime/GPU fixture promoted; bounded recreated readiness wait; optional diagnostic output so standard Test Runner requires no custom CLI arguments'
for path,item in m['changes'].items():
 data=(P/path).read_bytes().decode('utf-8-sig').replace('\r\n','\n').encode('utf-8')
 (P/path).write_bytes(data);(W/'repair-files'/path).write_bytes(data)
 item['newSha']=hashlib.sha256(data).hexdigest()
 item['normalization']='UTF-8 without BOM, LF; code unchanged except described edits'
m['scope']='Complete committed Assets/Packages/ProjectSettings plus explicit repair files. No mother source outside manifest, compiled assemblies, AssetDatabase or project JIT reused. Pinned package source cache reuse separately verified in package-source-cache-receipt.json.'
(W/'repair-manifest.json').write_text(json.dumps(m,ensure_ascii=False,indent=2),encoding='utf-8')
patch=''
with zipfile.ZipFile(W/'committed-source.zip') as z:
 names=set(z.namelist())
 for path in m['changes']:
  a=z.read(path).decode('utf-8-sig').splitlines(True) if path in names else []
  b=(W/'repair-files'/path).read_text(encoding='utf-8-sig').splitlines(True)
  patch+=''.join(difflib.unified_diff(a,b,fromfile='a/'+path if path in names else '/dev/null',tofile='b/'+path))
(W/'dependency-repair.patch').write_text(patch,encoding='utf-8')
print('Runtime test now supports normal Test Runner without output CLI argument; product unchanged')
