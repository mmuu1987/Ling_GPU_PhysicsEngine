from p3_lane_early_20261008 import *
sys.stdout.reconfigure(encoding='utf-8')
d=json.loads((LANE_OUT/'decision.json').read_text(encoding='utf-8'))
assert d['adopted'] and d['renderGatePassed'] and d['protectionPassed'] and not d['P3Closed']
assert not processes() and not(ROOT/'Temp/UnityLockfile').exists()
check=project_check();assert check['passed'] and check['laneVariant']=='candidate'
reg=json.loads((LANE_OUT/'registry.json').read_text(encoding='utf-8'))
assert reg['variants']['candidate'][NAV_PATH]==reg['variants']['baseline'][NAV_PATH]
report=ROOT/'Docs/InteractionRefinement-20261007/P3-LANE-EARLY-EXIT-ADOPTED-20261008.md';assert not report.exists()
lines=['# P3 Lane提前退出优化已采纳，周期性停顿仍未关闭','', '## 结论','本轮只在原Lane中增加两个提前退出，不复用上轮被拒绝的轴中心缓存/标量重写或历史Cardinal路线缓存。六份同输入Lane耗时减少约61%–66%，完整输出float位不变；两组整场P99约47.1→36.7ms，改善约22%，达到全部事前门槛，保留源码。每15秒>33.33ms仍85次，P3-PERF-01继续开启。','', '## 改动与等价条件','- 保留原有停止/不可达幅值判断，随后仅对cell<CellCount的有效格检查所在行列是否都无目标；双空则跳过中心/朝向。越界非零方向仍走原CellCenter并抛出原异常，越界零方向仍按原先逻辑跳过。','- 保留原来的CellCenter、toward、主轴、sign和proposed表达式；若original与proposed两个分量通过BitConverter.SingleToInt32Bits逐位相同，则跳过dot/二分/Distance/只读路线检查。原流程最终只可能保留original或写入proposed，两者位相同，输出不变。绝不用Unity近似Vector2==或普通float相等替代，保留负零改写、NaN/Inf行为。','- 生产净改动仅TerrainLaneApproach33.cs。TerrainNavigationGrid.cs原字节不变，保留此前采纳的二叉堆/均匀方向输出收益。无新增持久缓存或数组，不据已知失效分配计数API声称零分配。原有列表/结果数组分配仍存在。','- 目标过滤/重复权重/求和顺序/排序、阈值、路线合法性、停止半径、刷新/同步调度、人数2048、分离48和GPU布局均未改。','', '## 正确性与局部计时','优化前后分别125/125定向EditMode通过（既有105＋新增20），6/6真实GPU局部指令回归通过，0失败0跳过。新增20覆盖7种平地/分数格/洞/墙/山坡/净空布局的特殊值及重复目标、4方向负零必须实际改写、6种null/空/短/正常/超长方向数组与异常合同、3种空/无效目标早返。保留已有阈值/路线/输出护栏。','六份冻结真实输入完整输出与同轮冻结原Lane逐位一致、目标与中心位一致，优化前后输出哈希一致。四模式轮换、每模式2次预热＋12次测量；拷贝与断言在计时外。下表基准为同一个当前导航网格上的冻结原Lane，不混入上轮轴缓存。','', '|输入|同网格原Lane ms|候选ms|减少|','|---|---:|---:|---:|']
for r in d['after']:
 m=r['modeMedianMs'];lines.append(f"|{r['sample']}|{m['current-grid-reference-lane']:.4f}|{m['current']:.4f}|{100*(1-m['current']/m['current-grid-reference-lane']):.2f}%|")
lines+=['','各输入均超过事前15%局部门槛才进入GPU/整场。没有把工作计数直接换算成阶段毫秒，也未声称任意目标布局都有相同收益。','', '## 完整整场ABBA','基线为此前已采纳二叉堆/输出优化＋原Lane；候选只加两条提前退出。GTX1060 3GB/D3D11、Green、2048人、48、1920×1080、相机固定、vsync0/不限帧，35秒预热＋15秒窗口。全部轮次均保留。','', '|轮次|版本|样本间隔|中位ms|P95 ms|P99 ms|>50ms|>33.33ms|','|---|---|---:|---:|---:|---:|---:|---:|']
for i,r in enumerate(d['rendered']):lines.append(f"|{i+1}|{['基线','候选','候选','基线'][i]}|{r['samples']}|{r['medianMs']:.2f}|{r['p95Ms']:.2f}|{r['p99Ms']:.2f}|{r['over50ms']}|{r['over33ms']}|")
a,b,c,e=d['rendered'];g1=100*(1-b['p99Ms']/a['p99Ms']);g2=100*(1-c['p99Ms']/e['p99Ms'])
lines += ['',f'两组P99改善{g1:.2f}%/{g2:.2f}%，均超过事前10%；普通中位≤1.10倍；>33.33ms不增加；>50ms均≤max(基线,3)。全部通过，无事后改门槛。候选两窗>50ms均0，不等于长期永无长帧。','这些是短Editor渲染回调墙钟间隔，不是OS呈现、独立EXE或真人验收；帧号连续不是逐帧战局完全一致的证明。>33.33ms仍85/窗，周期性同步停顿未消失。不得称P3全部完成。','', '## 当前版本与保护','当前TerrainLaneApproach33.cs SHA256：`'+reg['variants']['candidate'][LANE_PATH]+'`。导航SHA256：`'+reg['variants']['candidate'][NAV_PATH]+'`。125项测试源码哈希与baseline/candidate字节保存在lane-early-20261008-01/registry.json及对应目录。','**当前保护入口改为Tools/InteractionRefinement/p3_lane_early_20261008.py::project_check。**旧Lane/binary检查器不认识本轮新源码与20项测试，禁止按旧报告“撤回Lane”自动回退当前候选。上轮拒绝的中心轴/标量候选仍拒绝，与本轮不同。','最终检查通过：9693基线＋30批准变更及登记文件、4份用户文件、37包全部387文件、HEAD/暂存区未变；任务exit0，无本项目Unity进程/锁。无打包、stage、commit或push。37仍不包含这些源码优化。','', '## 后续','保留当前收益。下一轮若继续P3，先在这一已采纳基线上用低扰动粗阶段测量重新分解剩余约36–37ms长间隔中的求解、Lane、提交及其它主线程工作；不要直接拼接跨轮旧计时作剩余成本定论。根据新证据再决定小范围改动，不反复重跑已完成ABBA，不改变刷新率/人数/48，不偷换异步调度或重新引入拒绝缓存。打包需单独确认；其它P3视觉/音频/真人和P9状态不因此签收。','', '证据：Logs/InteractionRefinement-20261007/P3/lane-early-20261008-01/{decision,scope,registry}.json；P8/p3-lane-early-before-01、p3-lane-early-after-01、p3-lane-early-scoped-01；P3/p3-lane-early-abba-1至4。']
report.write_bytes(('\n'.join(lines)+'\n').encode('utf-8'))
p=ROOT/'NEXT_TASK.md';backup=LANE_OUT/'NEXT_TASK.before-early-adoption.md';assert not backup.exists();backup.write_bytes(p.read_bytes())
text='''# 最新接力：Lane提前退出已采纳，P3长帧仍开启（2026-10-08）

权威报告：Docs/InteractionRefinement-20261007/P3-LANE-EARLY-EXIT-ADOPTED-20261008.md。

## 当前版本——勿按上轮撤回报告误回退
- 已采纳：TerrainLaneApproach33的有效格行列双空早返、original/proposed两分量float位完全相同早返。只有这两条，不含上轮拒绝的中心轴/标量或历史Cardinal缓存。
- 导航源码本轮原字节未改，保留之前已采纳二叉堆优先级复用＋均匀方向表。
- 当前源码是lane-early-20261008-01/registry.json的candidate。完整两个文件基线/候选及测试字节在该目录。
- 当前保护入口：p3_lane_early_20261008.py::project_check；旧Lane/binary入口不认识新增Lane源码/测试。禁止由旧检查失败自动覆盖。

## 已收取验证
- 前后各125/125定向EditMode（既有105＋新增20边界护栏），6/6真实GPU局部指令回归，0失败0跳过；六份完整输出float位、中心、目标及前后哈希一致。
- 同网格原Lane 14.6073–16.3051ms，候选4.9264–6.2948ms；逐输入改善60.62%–66.27%。局部≥15%门槛通过。
- 完整ABBA P99=47.08/36.63/36.86/47.28ms，两组改善约22%；普通中位4.94–4.98ms，>50ms=3/0/0/6。全部事前整场门槛通过，候选保留。
- P3-PERF-01仍开启：>33.33ms四窗仍85次。不能将严重程度下降说成周期性停顿消失，不能冒称EXE/真人或P9签收。

## 下一步
保留当前版本，针对剩余36–37ms间隔，在新基线上低扰动重测求解/Lane/提交及主线程残余。不要拼接跨轮旧计时直接归因，不重复已完成队列，不恢复拒绝缓存，不改变刷新率/人数/48或未经审查改异步调度。打包需另行确认。

## 保护与交接
- 本轮任务均结束并收取，最终保护通过，无本项目Unity进程/锁。4份用户文件、37全部387文件、HEAD与暂存区未变，无stage/commit/push/新包。
- 37不含源码优化；保护pelican SVG/meta与既有脏工作区，不git add .、不强关用户Editor、不-nographics。其它人工验收保持待办。
- 上份NEXT_TASK原字节在lane-early-20261008-01/NEXT_TASK.before-early-adoption.md。旧P3-LANE-REJECTED报告只针对另一候选，不是当前版本。
'''
p.write_bytes(text.encode('utf-8'))
with (ROOT/'Docs/InteractionRefinement-20261007/P3-DEFERRED-ISSUES.md').open('ab') as f:f.write(('\n\n## 2026-10-08 Lane提前退出已采纳\nP3-PERF-01仍开启，但两条精确提前退出已通过125＋6测试和完整ABBA：P99约47.1→36.7ms，改善约22%；>33.33ms仍85/窗。当前Lane不再是原版，不按上轮另一候选撤回结论自动恢复。当前保护入口p3_lane_early_20261008.py，详见[最新采纳报告](P3-LANE-EARLY-EXIT-ADOPTED-20261008.md)及NEXT_TASK。37未更新。\n').encode('utf-8'))
assert not processes() and not(ROOT/'Temp/UnityLockfile').exists()
save(LANE_OUT/'publication.json',{'report':str(report.relative_to(ROOT)),'reportSha256':sha(report),'nextTaskSha256':sha(p),'laneCandidateRetained':True,'navigationUnchangedThisPhase':True,'P3Closed':False,'noTasksInFlight':True})
print('PUBLISHED Lane early-exit adoption; prior navigation preserved; P3 still open.',flush=True)
