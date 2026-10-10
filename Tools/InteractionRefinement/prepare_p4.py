from common_p3 import *
import sys,hashlib
sys.stdout.reconfigure(encoding='utf-8');P4=LOG/'P4';assert not(P4/'baseline.json').exists()
active=processes();assert not active,('Do not close user processes',active);assert not(ROOT/'Temp/UnityLockfile').exists(),'Stop on lock; old recovery permission is not reusable'
check=project_check(full=True);assert check['passed'],check;assert user_check()['passed']
doc=ROOT/'Docs/InteractionRefinement-20261007';pub=json.loads((P3/'perf-review-01/publication.json').read_text(encoding='utf-8'))
for name in ['IMPLEMENTATION.md','P3-QUALIFICATION-STATE.md']:
 p=doc/name;expected=next(r['sha256'] for r in pub['files'] if r['path']==p.relative_to(ROOT).as_posix());assert sha(p)==expected,('Document changed',name)
b=json.loads((P3/'baseline.json').read_text(encoding='utf-8'))['files'];a=json.loads((P3/'approved-changes.json').read_text(encoding='utf-8'));b.update({p:r['expectedSha256'] for p,r in a.items()});save(P4/'baseline.json',{'createdAt':datetime.datetime.now().isoformat(),'files':b,'p3Protection':check,'progressionAuthorizedByUser':True});save(P4/'approved-changes.json',{})
save(P4/'scope.json',{'phase':'P4','scope':'Deployment formation translation only. No scaling, P5 handles, P6 navigation, P7 subset selection, P8 commands, onboarding or build38.','userDecision':'p3的问题先用文档记下来，然后进入p4','knownIssuesDeferredNotFixed':True,'p3Strength':48,'identity':'Draft object + monotonic revision + selected index + exact entry snapshot; cancel on any draft mutation, never use index alone.','inputPolicy':'Pending numeric input blocks the gesture and remains unchanged. User explicitly updates fields first.','previewPolicy':'Lightweight ghost; no draft mutation, no GPU rebuild, no undo until valid release. Full existing footprint/terrain/radius validation on release. Leaving map cancels; layout change/modal/focus/Esc cancels.','checkpoint':'Automated checks and human drag acceptance reported separately; no automatic P5 or build.'})
backup=P4/'handoff-docs-before';backup.mkdir()
for name in ['IMPLEMENTATION.md','P3-QUALIFICATION-STATE.md']:(backup/name).write_bytes((doc/name).read_bytes())
known='''# P3 已知事项与暂缓决定 · 2026-10-07

## 用户决策

用户明确要求：P3的问题先用文档记下来，进入P4。按此允许带已知事项进入B阶段；不是宣称问题已修复、性能统计等效或真人验收已完成。正式男战士/剑盾骑士分离强度48保持。

## 保留事项

- **P3-PERF-01 周期性长帧 / 暂缓，不再阻挡P4。** 旧4轮+新8轮，每个15秒窗口均85个>50ms间隔，间距中位约0.172–0.179秒；24/48均出现。新批墙钟中位5.2475→5.2150ms，P95均值8.4675→7.8600ms，但同条件波动仍>20%。不是已证实48的稳定回退，也不能宣称无卡顿。CPU流场重建及HUD刷新仅为候选；主线程计数器约晚一行。后续按需逐帧对齐现有CPU计数器，不能凭猜测调整流场频率；P6导航独立。
- **P3-VISUAL-01 近景动画及抖动 / 待人工观察，不阻挡本次进入P4。** 原有残余移动代理改善不替代VAT/武器观感；未冒充人工验收。
- **P3-COVERAGE-01 覆盖边界。** 微型窄路不是2048人全地形吞吐；小体型及非1缩放角色未在48采纳范围中。
- **P1/P2-MANUAL、AUDIO 继续保留。** 半径圈/编辑使用体验及既有音频事项未自动关闭；测试Listener不等于生产修复。

## 已有通过证据

652/652 EditMode、41/41 GPU补验，实际2048人移动/待命/撤退再攻击通过；持续动作原始轨迹独立核对；12轮独立帧时数据和保护审计保留。未发现本批检查覆盖范围内需要阻止继续开发的指令失效/通行卡死。用户已接受此前战斗时长和存活取舍。

## 重开处理触发

若出现可复现的新指令失效、崩溃、明显新增长帧/卡死、或用户认为观感不可接受，应重新定位，不能因本次暂缓而忽略。不得删除差轮或把P3写成全项技术签收。

权威证据：P3-PERFORMANCE-REVIEW.md、P3-QUALIFICATION-REPORT.md及Logs/InteractionRefinement-20261007/P3。37包保留未改。

## 本次进入P4

严格依原计划：部署色块直接平移，当前编成而非整军团；抓取偏移、轻量预览、完整占地校验、一次提交/撤销、取消与输入互斥。P5尺寸手柄、P6导航、P7框选、P8局部命令及38包不自动进入。
'''
(doc/'P3-DEFERRED-ISSUES.md').write_text(known,encoding='utf-8')
p=doc/'IMPLEMENTATION.md';s=p.read_text(encoding='utf-8');import re
s=re.sub(r'^状态：.*$', '状态：**P3按用户决定带已知事项进入P4；48保持，长帧/真人/音频事项见P3-DEFERRED-ISSUES.md，未宣称修复。P4部署平移进行中；P5及后续未进入，未出38。**',s,count=1,flags=re.M)
s=s.replace('| P4 | 部署色块直接平移 | A 阶段验收通过 | 位置拖动、取消、撤销和校验正确 | 未开始 |','| P4 | 部署色块直接平移 | 用户同意P3已知问题暂缓后进入 | 位置拖动、取消、撤销和校验正确 | 进行中 |')
s+='\n\n## 最新阶段决定：进入P4（2026-10-07）\n\n用户授权将P3余项记入[P3已知事项](P3-DEFERRED-ISSUES.md)后推进部署色块平移。此前“不P4”的状态为历史检查点，已被本次用户推进决定取代；原始测试结果及限制不改写。P4不包含尺寸手柄、导航或局部框选。\n';p.write_text(s,encoding='utf-8')
(doc/'P3-QUALIFICATION-STATE.md').write_text('# 最新阶段决定\n\n用户授权P3带已知事项进入P4；不是宣称问题已修复。48保持；详见P3-DEFERRED-ISSUES.md与IMPLEMENTATION.md。P4部署平移进行中；未P5及后续，未38。旧状态已备份到P4/handoff-docs-before。\n',encoding='utf-8')
save(P4/'handoff.json',{'status':'P3_KNOWN_ISSUES_DEFERRED_P4_AUTHORIZED','p3Fixed':False,'p4BaselineFiles':len(b),'userDecisionRecorded':True,'documents':[{'path':(doc/n).relative_to(ROOT).as_posix(),'sha256':sha(doc/n)} for n in ['P3-DEFERRED-ISSUES.md','IMPLEMENTATION.md','P3-QUALIFICATION-STATE.md']]});print('P4 PREPARED',len(b),'baseline files; P3 deferred by user, not marked fixed')
