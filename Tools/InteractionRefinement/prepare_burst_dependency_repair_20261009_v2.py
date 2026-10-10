"""Fresh Git-object export and minimal dependency repair; never modifies mother Assets/index/HEAD."""
from pathlib import Path
import subprocess,json,hashlib,shutil,zipfile,datetime,sys,uuid,difflib
sys.stdout.reconfigure(encoding='utf-8')
R=Path(__file__).resolve().parents[2]
C='085957ba7e838d23379cbc8fe6249540c86f31d9'
W=R/'outputs/BurstDependencyRepair-20261009-01'
P=R/'outputs/B9'
def git(*args):return subprocess.check_output(['git',*args],cwd=R)
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def save(p,d):p.parent.mkdir(parents=True,exist_ok=True);p.write_text(json.dumps(d,ensure_ascii=False,indent=2),encoding='utf-8')
def snapshot():
 paths=list((R/'Assets').rglob('*.cs'))+list((R/'Assets').rglob('*.asmdef'))+list((R/'Assets').rglob('*.hlsl'))+list((R/'Assets').rglob('*.compute'))+list((R/'Assets').rglob('*.shader'))+list((R/'ProjectSettings').glob('*'))+list((R/'Packages').glob('*.json'))+list((R/'Builds/CaptureDefault-20261006-37').rglob('*'))
 return {'head':git('rev-parse','HEAD').decode().strip(),'index':sha(R/'.git/index'),'files':{p.relative_to(R).as_posix():sha(p) for p in paths if p.is_file()}}
assert W.exists() and (W/'committed-source.zip').is_file(),'Original immutable archive missing'
assert not P.exists(),'Fresh short path must not exist'
before=json.loads((W/'mother-before.json').read_text(encoding='utf-8'))
assert snapshot()==before,'Mother drift since initial prepare'
archive=W/'committed-source.zip'
P.mkdir(parents=True)
print('RESUMING committed Unity project export into short path',str(P),flush=True)
with zipfile.ZipFile(archive) as z:
 members=[i for i in z.infolist() if i.filename.split('/')[0] in ['Assets','Packages','ProjectSettings']]
 longest=max(len(str(P/i.filename)) for i in members)
 print('Unity project entries',len(members),'longest absolute path',longest,flush=True)
 for info in members:
  q=(P/info.filename).resolve();assert q.is_relative_to(P.resolve()),info.filename
  z.extract(info,str(P))
print('EXPORTED clean committed project',flush=True)
changes={};sources={}
def put(path,data,source):
 dst=P/path;old=dst.read_bytes() if dst.exists() else None
 dst.parent.mkdir(parents=True,exist_ok=True);dst.write_bytes(data)
 changes[path]={'oldSha':hashlib.sha256(old).hexdigest() if old is not None else None,'newSha':hashlib.sha256(data).hexdigest(),'source':source}
 sources[path]=(old,data)
def copy(path):put(path,(R/path).read_bytes(),'existing adopted mother file; exact bytes')
for f in ['Assets/MassEngine/Terrain/TerrainLaneApproach33.cs','Assets/MassEngine/Terrain/TerrainLaneApproach33.cs.meta','Assets/MassEngine/Tests/EditMode/TerrainCardinalRouteCacheTests.cs','Assets/MassEngine/Tests/EditMode/TerrainCardinalRouteCacheTests.cs.meta']:copy(f)
for f in ['Assets/MassEngine/MassEngine.asmdef','Assets/MassEngine/Tests/EditMode/MassEngine.Tests.asmdef']:
 obj=json.loads((P/f).read_text(encoding='utf-8-sig'))
 for ref in ['Unity.Burst','Unity.Collections']:
  if ref not in obj['references']:obj['references'].append(ref)
 put(f,(json.dumps(obj,indent=4)+'\n').encode(),'explicit dependencies, existing package versions unchanged')
f='Assets/MassEngine/Terrain/TerrainNavigationRuntime.cs'
s=(P/f).read_text(encoding='utf-8-sig')
old='// Temporary independent diagnostic only. Not called by TerrainNavigationRuntime.Upload or scene code.'
assert s.count(old)==1
s=s.replace(old,'// Editor-only preparation policy owned by TerrainNavigationRuntime; normal solves do not probe compilation.')
put(f,s.encode(),'comment correction only; no algorithm or default change')
# Publish the previously isolated runtime test as a reproducible source test, with imports and bounded readiness wait.
s=(R/'Tools/InteractionRefinement/P3OptInRuntimeTests-20261008.cs.txt').read_text(encoding='utf-8-sig')
s='using System;\nusing System.Collections.Generic;\nusing NUnit.Framework;\nusing UnityEngine;\n'+s
s=s.replace('public sealed class P3RuntimeGateTests','public sealed class BurstDependencyRepairRuntimeTests')
old='w.runtime.Tick(w.buffers,w.context);Assert.AreEqual(q.blocked?"Unavailable":"Ready",w.runtime.PreparationState);'
new='w.runtime.Tick(w.buffers,w.context);double retryDeadline=Time.realtimeSinceStartupAsDouble+60;while(!q.blocked && w.runtime.PreparationState=="Waiting" && Time.realtimeSinceStartupAsDouble<retryDeadline){yield return null;w.runtime.Tick(w.buffers,w.context);}Assert.AreEqual(q.blocked?"Unavailable":"Ready",w.runtime.PreparationState);'
assert s.count(old)==1;s=s.replace(old,new)
f='Assets/MassEngine/Tests/EditMode/BurstDependencyRepairRuntimeTests.cs'
put(f,s.encode(),'historical actual Runtime/GPU fixture promoted; recreated readiness wait bounded rather than assumed')
put(f+'.meta',('fileFormatVersion: 2\nguid: '+uuid.uuid4().hex+'\n').encode(),'new unique Unity GUID')
# Bring only independent preparation state-machine tests, not timing/cached fixture assumptions.
s=(R/'Tools/InteractionRefinement/P3BurstPreparationTests-20261008.cs.txt').read_text(encoding='utf-8-sig')
start=s.index('namespace MassEngine.Tests {\n public sealed partial class P3BurstPreparationTests')
s='using System;\nusing NUnit.Framework;\n'+s[start:]
s=s.replace('public sealed partial class P3BurstPreparationTests','public sealed class BurstDependencyRepairPolicyTests')
f='Assets/MassEngine/Tests/EditMode/BurstDependencyRepairPolicyTests.cs';put(f,s.encode(),'existing independent gate tests promoted, no cache/timing assumptions')
put(f+'.meta',('fileFormatVersion: 2\nguid: '+uuid.uuid4().hex+'\n').encode(),'new unique Unity GUID')
# Export reviewable repair, relative to the actual commit, not dirty mother contents.
patch=''
for path,(old,new) in sources.items():
 a=old.decode('utf-8-sig').splitlines(True) if old else []
 b=new.decode('utf-8-sig').splitlines(True)
 patch+=''.join(difflib.unified_diff(a,b,fromfile='a/'+path if old else '/dev/null',tofile='b/'+path))
(W/'dependency-repair.patch').write_text(patch,encoding='utf-8')
for path,(_,data) in sources.items():
 dst=W/'repair-files'/path;dst.parent.mkdir(parents=True,exist_ok=True);dst.write_bytes(data)
save(W/'repair-manifest.json',{'baseCommit':C,'changes':changes,'scope':'Complete committed Assets/Packages/ProjectSettings tree plus explicitly listed repair files; repository documents/archives excluded. No mother Library, untracked source/tests or scene assets copied except listed files. No commit or branch change.'})
after=snapshot();save(W/'mother-after-prepare.json',after);assert after==before,'MOTHER PROTECTION CHANGED'
save(W/'prepare-result.json',{'passed':True,'project':str(P),'changedFiles':len(changes),'motherProtectionPassed':True})
print('PREPARED',str(P),'files',len(changes),'MOTHER PROTECTION OK',flush=True)
