from pathlib import Path
import json,datetime,hashlib,subprocess,os
R=Path(__file__).resolve().parents[2];D=R/'outputs/P9ColdFix-20261009-01';P=R/'outputs/P12';sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
a=json.loads((D/'host-r03-final-audit.json').read_text(encoding='utf-8'));m=json.loads((D/'patch-manifest.json').read_text(encoding='utf-8'))
assert a['protectionPassed'] and a['patchRevision']==m['revision']==3
assert all(sha(P/f)==v for f,v in m['files'].items())
assert json.loads((D/'compiler-probes-v16-global-unified/protection.json').read_text(encoding='utf-8'))['productionShadersUnchanged']
assert subprocess.check_output(['powershell','-NoProfile','-Command',"@(Get-Process Unity,WarSandbox -ErrorAction SilentlyContinue).Count"],text=True).strip()=='0'
b=json.loads((R/'outputs/P9Combined-20261009-01/mother-before.json').read_text(encoding='utf-8'));stats=json.loads((D/'verified-mother-stat-cache.json').read_text(encoding='utf-8'))['stats']
for f,want in b['files'].items():
 p=R/f;assert p.is_file();st=p.stat()
 if [st.st_size,st.st_mtime_ns]!=stats.get(f):assert sha(p)==want,f
user=Path(b['userRoot']);assert {f.relative_to(user).as_posix():sha(f) for f in user.rglob('*') if f.is_file()}==b['userFiles']
assert sha(R/'.git/index')==b['index']
env=os.environ.copy();env['GIT_OPTIONAL_LOCKS']='0';assert subprocess.check_output(['git','rev-parse','HEAD'],cwd=R,env=env,text=True).strip()==b['head']
archive=D/'records-before-r03-status';archive.mkdir()
for src in [D/'progress.json',D/'PROGRESS.md',R/'NEXT_TASK.md']:
 if src.exists():(archive/src.name).write_bytes(src.read_bytes())
p=json.loads((D/'progress.json').read_text(encoding='utf-8'))
p.update(updated=datetime.datetime.now().isoformat(),productionFixApplied=True,candidateAccepted=False,coldPerformanceAccepted=False,productionPatchRevision=3,productionPatchFiles=12,testAssetCount=44,diagnosticAssetCount=2,allStartedRunsCompleted=True,productionValidation=a,unifiedIntegrationGpuCasesPassed=144)
p['singleWritebackScreening']=json.loads((D/'compiler-specialization-v14b.json').read_text(encoding='utf-8'))
p['unifiedIntegrationScreening']=json.loads((D/'compiler-specialization-v15.json').read_text(encoding='utf-8'))
p['globalUnifiedScreeningOnly']=json.loads((D/'compiler-specialization-v16.json').read_text(encoding='utf-8'))
p['remaining']=['Cold Attack gap 4946.8357ms, Hold 3928.5203ms, Retreat 3049.9341ms: NOT accepted.','Cold entry interval 44.4088s remains; v16 global CPU-only candidate not GPU validated or deployed.','Warm planning approximately 330ms remains. First-radius navigation construction identified statically; no phase timings or optimization yet.','Real ranged/mixed and independent Move cold command coverage remains incomplete.','Type-mask direct SetData bypass and cancel/death/reset/multi-manager coverage review remain open.','No release/merge/Burst default/Player/AOT/human signoff.']
p['warning']='Revision03 typed local integration/writeback candidate deployed and regressed; real cold ABA still fails performance acceptance. v16 global candidate is CPU screening only, not deployed. Historical CPU ablations below must not be deployed.'
(D/'progress.json').write_text(json.dumps(p,ensure_ascii=False,indent=2),encoding='utf-8')
report=(R/'Tools/InteractionRefinement/p9_host_r03_report_20261009.md').read_text(encoding='utf-8')
(D/'PROGRESS.md').write_text(report,encoding='utf-8');(R/'NEXT_TASK.md').write_text('# 当前接续：revision 3 已回归；性能仍未验收\n\n'+report+'\n所有本轮进程已结束。一次性准备/部署/归档/A-B-A 脚本不可原样重跑；NEXT_TASK 不入 git。\n',encoding='utf-8')
print('R03 records updated; mother/user/index rechecked; all started runs completed; NOT accepted.',flush=True)
