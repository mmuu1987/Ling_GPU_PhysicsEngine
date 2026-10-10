from pathlib import Path
import json,datetime
R=Path(__file__).resolve().parents[2];D=R/'outputs/P9ColdFix-20261009-01'
a=json.loads((D/'host-final-audit.json').read_text(encoding='utf-8'));assert a['protectionPassed'] and a['patchRevision']==2
archive=D/'records-before-production-status';archive.mkdir()
for src in [D/'progress.json',D/'PROGRESS.md',R/'NEXT_TASK.md']:
 if src.exists():(archive/src.name).write_bytes(src.read_bytes())
p=json.loads((D/'progress.json').read_text(encoding='utf-8'));p.update(productionFixApplied=True,candidateAccepted=False,prototypeOnly=False,testAssetCount=30,diagnosticAssetCount=2,fullProductionHostPartitionImplemented=True,fixedColdAcceptanceRun=True,coldPerformanceAccepted=False,allStartedRunsCompleted=True,fullProtectionAuditPassed=True,updated=datetime.datetime.now().isoformat(),productionPatchRevision=2,productionPatchFiles=12,productionValidation=a,remaining=['Cold Attack max gap 7052.9454ms; Hold 6724.9801ms; Retreat 3726.9622ms. NOT accepted.','Warm CPU planning approximately 326-336ms remains open.','Loading versus command compilation attribution remains incomplete.','Type-mask SetData bypass contract, mixed/cancel/death/reset/multi-manager coverage review remains open.','No new phase split or single-writeback optimization implemented; no Player/AOT or human signoff.'])
p['warning']='Production candidate is deployed but NOT accepted. CPU-only screening below remains historical, not Unity cold timing. Cold/joint/repeat evidence is revision01; revision02 adds null ShaderSet guard only, unchanged HLSL, retested membership/regression/hosttrace.'
(D/'progress.json').write_text(json.dumps(p,ensure_ascii=False,indent=2),encoding='utf-8')
report=(R/'Tools/InteractionRefinement/p9_host_report_20261009.md').read_text(encoding='utf-8')
(D/'PROGRESS.md').write_text(report,encoding='utf-8');(R/'NEXT_TASK.md').write_text('# 当前接续：生产候选已接入；冷态性能仍未验收\n\n'+report+'\n\n证据根目录 outputs/P9ColdFix-20261009-01。一次性部署、A/B/A、revision02、记录归档脚本均已执行，不可原样重跑。母/P11/37 保持不动，不提交 NEXT_TASK。\n',encoding='utf-8')
print('Updated progress, report and NEXT_TASK; archived prior records. NOT accepted.',flush=True)
