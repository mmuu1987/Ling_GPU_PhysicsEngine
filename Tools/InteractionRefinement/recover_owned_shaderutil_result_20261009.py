from pathlib import Path
import subprocess,json,hashlib,datetime,xml.etree.ElementTree as ET
R=Path(__file__).resolve().parents[2];P=R/'outputs/P12';Q=R/'outputs/P9FirstCommand-20261009-01';O=R/'outputs/P9Combined-20261009-01';V=P/'Logs/InteractionRefinement-20261007/P8/cold-fix-fixed-firstshaderutil-01'
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
def idle():return subprocess.check_output(['powershell','-NoProfile','-Command',"@(Get-Process Unity,WarSandbox -ErrorAction SilentlyContinue).Count"],text=True).strip()=='0'
assert idle()
run=json.loads((V/'process.json').read_text(encoding='utf-8'));assert run.get('ownedPid') and run.get('tests',{}).get('result')=='Passed'
xml=ET.parse(V/'results.xml').getroot();cases=[x for x in xml.iter('test-case')];assert len(cases)==1 and cases[0].get('fullname')=='MassEngine.Game.Tests.P9ShaderUtilCompileProbe.CompileExactLocalAttackVariant' and cases[0].get('result')=='Passed'
b=json.loads((O/'mother-before.json').read_text(encoding='utf-8'));user=Path(b['userRoot']);before=b['userFiles'];now={f.relative_to(user).as_posix():sha(f) for f in user.rglob('*') if f.is_file()};added=sorted(now.keys()-before.keys());removed=sorted(before.keys()-now.keys());changed=sorted(k for k in before.keys()&now.keys() if before[k]!=now[k])
persistent=user/'TestResults.xml';own=V/'results.xml';backup=V/'persistent-TestResults-before.xml'
info={'status':'inspect','userRoot':str(user),'diffBeforeRestore':{'added':added,'removed':removed,'changed':changed},'persistentExists':persistent.exists(),'ownedResultExists':own.exists(),'backupExists':backup.exists(),'restored':False,'otherFilesRestored':False}
assert persistent.exists() and own.exists() and persistent.read_bytes()==own.read_bytes(),'Current persistent result is not byte-identical to this isolated run; leave untouched'
if backup.exists():persistent.write_bytes(backup.read_bytes())
else:persistent.unlink()
now2={f.relative_to(user).as_posix():sha(f) for f in user.rglob('*') if f.is_file()};info['restored']=True;info['userFilesNowMatchBaseline']=now2==before;info['remainingDiff']={'added':sorted(now2.keys()-before.keys()),'removed':sorted(before.keys()-now2.keys()),'changed':sorted(k for k in before.keys()&now2.keys() if before[k]!=now2[k])};info['otherFilesRestored']=not info['remainingDiff']['added'] and not info['remainingDiff']['removed'] and not info['remainingDiff']['changed'];info['finished']=datetime.datetime.now().isoformat();(Q/'shaderutil-user-result-recovery.json').write_text(json.dumps(info,ensure_ascii=False,indent=2),encoding='utf-8');print(json.dumps(info,ensure_ascii=False,indent=2))
