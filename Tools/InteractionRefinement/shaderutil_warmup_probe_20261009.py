from pathlib import Path
import subprocess,json,hashlib,sys,datetime,uuid,shutil
R=Path(__file__).resolve().parents[2];P=R/'outputs/P12';Q=R/'outputs/P9FirstCommand-20261009-01';D=R/'outputs/P9ColdFix-20261009-01';
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
def idle():return subprocess.check_output(['powershell','-NoProfile','-Command',"@(Get-Process Unity,WarSandbox -ErrorAction SilentlyContinue).Count"],text=True).strip()=='0'
def files(root):return {p.relative_to(root).as_posix():{'sha256':sha(p),'size':p.stat().st_size,'mtime_ns':p.stat().st_mtime_ns} for p in root.rglob('*') if p.is_file()}
def save(n,v):(Q/n).write_text(json.dumps(v,ensure_ascii=False,indent=2),encoding='utf-8')
assert idle();assert not(Q/'shaderutil-probe-transaction.json').exists()
manifest=json.loads((Q/'candidate-manifest.json').read_text(encoding='utf-8'));assert manifest['name']=='first-command-navigation-sharing-candidate-01' and not manifest.get('shaderPolicyTrial');assert all(sha(P/f)==h for f,h in manifest['files'].items())
origHarness=Q/'harness-manifest.json';harnessBytes=origHarness.read_bytes();harness=json.loads(harnessBytes)
test='Assets/Game/Tests/PlayMode/P9ShaderUtilCompileProbe.cs';meta=test+'.meta';assert not(P/test).exists() and not(P/meta).exists();source=(R/'Tools/InteractionRefinement/P9ShaderUtilCompileProbe.cs.txt').read_text(encoding='utf-8');(P/test).write_text(source,encoding='utf-8');(P/meta).write_text('fileFormatVersion: 2\nguid: '+uuid.uuid4().hex+'\n',encoding='utf-8');harness[test]=sha(P/test);harness[meta]=sha(P/meta);origHarness.write_text(json.dumps(harness,indent=2),encoding='utf-8')
(Q/'P9ShaderUtilCompileProbe.cs.frozen').write_bytes((P/test).read_bytes());(Q/'p9-shaderutil-runner.py.frozen').write_bytes((R/'Tools/InteractionRefinement/p9_shaderutil_run_20261009.py').read_bytes())
C=P/'Library/ShaderCache/compute/AgentCombatSimulation79e5';E=Q/'shaderutil-original-cache';G=Q/'shaderutil-generated-cache';assert C.is_dir() and not E.exists() and not G.exists();before=files(C);assert before
journal={'status':'journaled','test':'fixed-firstshaderutil-01','followup':'fixed-firstcold-09','originalCache':before,'candidateShaderSourcesMatchR03':True,'candidateManifestUnchanged':True,'globalOrDriverCacheTouched':False,'started':datetime.datetime.now().isoformat()};save('shaderutil-probe-transaction.json',journal);C.rename(E);journal['status']='cache-escrowed';save('shaderutil-probe-transaction.json',journal)
try:
 print('Isolated ShaderUtil internal compute variant compile probe; original Unity shader cache escrowed.',flush=True)
 journal['probeExit']=subprocess.call([sys.executable,str(R/'Tools/InteractionRefinement/p9_shaderutil_run_20261009.py'),'fixed-firstshaderutil-01'],cwd=R);assert idle()
 V=P/'Logs/InteractionRefinement-20261007/P8/cold-fix-fixed-firstshaderutil-01';probe=json.loads((V/'shaderutil-probe.json').read_text(encoding='utf-8'));journal['probe']={k:probe.get(k) for k in ['methodFound','error','kernel','keywords','elapsedMs','returnType','returnMembers','added','changed','removed']};journal['testPassed']=json.loads((Q/'fixed-firstshaderutil-01.json').read_text(encoding='utf-8'))['passed'];journal['cacheAfterProbe']=files(C) if C.exists() else {};save('shaderutil-probe-transaction.json',journal)
 assert journal['probeExit']==0 and journal['testPassed']
 print('Run exact first-command workflow after the internal compile API, without clearing its generated cache.',flush=True)
 journal['firstUseExit']=subprocess.call([sys.executable,str(R/'Tools/InteractionRefinement/p9_first_command_run_20261009.py'),'fixed-firstcold-09'],cwd=R);assert idle()
 journal['cacheAfterFirstUse']=files(C) if C.exists() else {};journal['status']='tested';save('shaderutil-probe-transaction.json',journal)
finally:
 assert idle(),'Never restore cache/harness while Unity process is active'
 if C.exists():journal['generatedCacheFinal']=files(C);C.rename(G)
 assert files(E)==before and not C.exists();E.rename(C);journal['cacheRestoredExactly']=files(C)==before;assert journal['cacheRestoredExactly']
 (P/test).unlink(missing_ok=True);(P/meta).unlink(missing_ok=True);origHarness.write_bytes(harnessBytes);journal['harnessRestoredExactly']=origHarness.read_bytes()==harnessBytes;journal['temporaryTestRemoved']=not(P/test).exists() and not(P/meta).exists();journal['candidateManifestUnchanged']=json.loads((Q/'candidate-manifest.json').read_text(encoding='utf-8'))==manifest;journal['status']='complete';journal['finished']=datetime.datetime.now().isoformat();save('shaderutil-probe-transaction.json',journal)
print(json.dumps({k:v for k,v in journal.items() if k not in ['originalCache','cacheAfterUse','cacheAfterProbe','cacheAfterFirstUse','generatedCacheFinal']},ensure_ascii=False,indent=2),flush=True)
sys.exit(0 if journal.get('probeExit')==0 and journal.get('firstUseExit')==0 and journal.get('cacheRestoredExactly') and journal.get('harnessRestoredExactly') else 1)
