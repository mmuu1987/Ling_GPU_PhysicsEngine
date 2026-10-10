from pathlib import Path
import re,json,hashlib,subprocess,sys,uuid
R=Path(__file__).resolve().parents[2];P=R/'outputs/P12';D=R/'outputs/P9ColdFix-20261009-01'
assert json.loads((D/'baseline-parity-02.json').read_text(encoding='utf-8'))['passed']
assert subprocess.check_output(['powershell','-NoProfile','-Command',"@(Get-Process Unity,WarSandbox -ErrorAction SilentlyContinue).Count"],text=True).strip()=='0'
m=json.loads((D/'prototype-harness.json').read_text(encoding='utf-8'))
for f,h in m.items():assert hashlib.sha256((P/f).read_bytes()).hexdigest()==h,f
original=(D/'compiler-probes-v2/baseline/expanded.compute').read_text(encoding='utf-8')
selected=(D/'compiler-probes-v10-REQUIRES-HOST-SPLIT/selected-attack-melee/expanded.compute').read_text(encoding='utf-8')
pragmas='\n'.join(x for x in original.splitlines() if x.startswith('#pragma') and 'MASS_LOCAL_ORDERS' not in x)
strip=lambda s:re.sub(r'^#pragma[^\n]*','',s,flags=re.M)
original=strip(original);selected=strip(selected)
assert selected.count('void SimulateCombatAndAccumulateDamage(')==1
selected=selected.replace('void SimulateCombatAndAccumulateDamage(','void P9SimulateSelected(')
needle='[numthreads(64, 1, 1)]\nvoid SimulateCombatAndAccumulateDamage('
assert original.count(needle)==1
original=original.replace(needle,'uint _P9ExcludedMask; // TEST-ONLY fixed fixture mask, not production membership.\n'+needle)
start=original.index('void SimulateCombatAndAccumulateDamage(');pos=original.index('    AgentData agent = agentBuffer[id.x];',start)
original=original[:pos]+'    if(id.x < 32 && (_P9ExcludedMask & (1u << id.x)) != 0) return;\n'+original[pos:]
shader='// TEST-ONLY partition scheduling proof; production sources unchanged.\n'+pragmas+'\n#pragma kernel P9SimulateSelected P9_SELECTED\n#ifdef P9_SELECTED\n#ifndef MASS_TERRAIN_ENABLED\n#define MASS_TERRAIN_ENABLED 1\n#endif\n#define MASS_LOCAL_ORDERS 1\n'+selected+'\n#else\n'+original+'\n#endif\n'
cs=(R/'Tools/InteractionRefinement/p9_partition_fixture_20261009.cs.txt').read_text(encoding='utf-8')
files={'Assets/MassEngine/Tests/PlayMode/P9PartitionedAttackMelee.compute':shader,'Assets/MassEngine/Tests/PlayMode/MassEngineGpuKernelTests.P9Partition.cs':cs}
for f in list(files):files[f+'.meta']='fileFormatVersion: 2\nguid: '+uuid.uuid4().hex+'\n'
backup=D/'prototype-harness-before-partition.json';assert not backup.exists()
for f in files:assert not(P/f).exists(),f
backup.write_bytes((D/'prototype-harness.json').read_bytes())
archive=D/'prototype-partition-original';archive.mkdir()
for f,text in files.items():
 (P/f).write_text(text,encoding='utf-8');m[f]=hashlib.sha256((P/f).read_bytes()).hexdigest();(archive/Path(f).name).write_bytes((P/f).read_bytes())
(D/'prototype-harness.json').write_text(json.dumps(m,indent=2),encoding='utf-8')
print('Four additional test-only assets installed; no production changes. 26 new partition cases.',flush=True)
sys.exit(subprocess.call([sys.executable,str(R/'Tools/InteractionRefinement/p9_fix_run_v4_20261009.py'),'baseline-partition-01'],cwd=R))
