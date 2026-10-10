from pathlib import Path
import json,hashlib,uuid,subprocess,re,sys
R=Path(__file__).resolve().parents[2];P=R/'outputs/P12';D=R/'outputs/P9ColdFix-20261009-01';sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
assert subprocess.check_output(['powershell','-NoProfile','-Command',"@(Get-Process Unity,WarSandbox -ErrorAction SilentlyContinue).Count"],text=True).strip()=='0'
m=json.loads((D/'patch-manifest.json').read_text(encoding='utf-8'));h=json.loads((D/'prototype-harness.json').read_text(encoding='utf-8'))
assert m['revision']==2 and all(sha(P/f)==v for f,v in m['files'].items()) and all(sha(P/f)==v for f,v in h.items())
backup=D/'prototype-harness-before-v15.json';assert not backup.exists();backup.write_bytes((D/'prototype-harness.json').read_bytes())
s=(D/'compiler-probes-v15-single-integrate/single-integrate-and-store/expanded.compute').read_text(encoding='utf-8')
s=re.sub(r'^#pragma kernel SimulateLocal.*\n','',s,flags=re.M)
s=re.sub(r'^#define SimulateCombatAndAccumulateDamage SimulateLocal.*\n','',s,flags=re.M)
base='Assets/MassEngine/Tests/PlayMode/'
new={}
for role,label in [('MoveMelee','move-melee'),('HoldMelee','hold-melee'),('AttackRanged','attack-ranged'),('MoveRanged','move-ranged'),('HoldRanged','hold-ranged'),('AttackMelee','attack-melee')]:
 new[base+'P9Unified-'+label+'.compute']='#define LP_'+role.upper()+' 1\n'+s
new[base+'MassEngineGpuKernelTests.P9Unified.cs']=(R/'Tools/InteractionRefinement/p9_unified_fixture_20261009.cs.txt').read_text(encoding='utf-8')
for f,text in new.items():
 assert not(P/f).exists() and not(P/(f+'.meta')).exists()
 (P/f).write_text(text,encoding='utf-8');(P/(f+'.meta')).write_text('fileFormatVersion: 2\nguid: '+uuid.uuid4().hex+'\n',encoding='utf-8')
 h[f]=sha(P/f);h[f+'.meta']=sha(P/(f+'.meta'))
(D/'prototype-harness.json').write_text(json.dumps(h,indent=2),encoding='utf-8')
assert all(sha(P/f)==v for f,v in m['files'].items())
sys.exit(subprocess.call([sys.executable,str(R/'Tools/InteractionRefinement/p9_fix_run_v13_20261009.py'),'fixed-unified-01'],cwd=R))
