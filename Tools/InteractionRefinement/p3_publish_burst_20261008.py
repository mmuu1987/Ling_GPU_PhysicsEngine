from p3_burst_production_20261008 import *
sys.stdout.reconfigure(encoding='utf-8')
d=json.loads((LANE_OUT/'decision.json').read_text(encoding='utf-8'))
assert d['status']=='burst_source_rejected_render_gate' and not d['adopted'] and d['protectionPassed']
assert previous_check(full=True)['passed'] and project_check(full=True)['passed']
assert not processes() and not(ROOT/'Temp/UnityLockfile').exists()
assert not (LANE_OUT/'publication.json').exists()
# Read-only follow-up: describe variation, never change the preregistered verdict.
variation=[]
for r in d['rendered']:
 rows=list(csv.DictReader((LOG/'P3'/r['tag']/'frames.csv').open(encoding='utf-8')))
 dt=[(float(b['seconds']),1000*(float(b['seconds'])-float(a['seconds']))) for a,b in zip(rows,rows[1:])]
 ordinary=[v for t,v in dt if v<=1000/60]
 buckets=[];start=float(rows[0]['seconds'])
 for i in range(5):
  values=[v for t,v in dt if i*3<=t-start<(i+1)*3]
  if values:buckets.append({'seconds':[i*3,(i+1)*3],'samples':len(values),'medianMs':statistics.median(values),'over33ms':sum(v>1000/30 for v in values)})
 variation.append({'tag':r['tag'],'over16_67ms':sum(v>1000/60 for t,v in dt),'ordinarySubsetMedianMs':statistics.median(ordinary),'threeSecondBuckets':buckets})
save(LANE_OUT/'readonly-variation.json',variation)
lines=['# P3 同步 Burst：可行性通过，正式集成未通过独立整场门槛，已回滚','', '## 结论','三阶段均已执行并收取结果：隔离原型通过；临时整场诊断通过并恢复；正式非测试程序集候选虽通过正确性、禁用回退和GPU定向回归，但新一轮ABBA第一配对未达预注册门槛，拒绝采纳并恢复。不得把诊断成功称为正式源码采纳。没有重跑直到通过，也没有放宽门槛。','', '## 第一阶段：隔离可行性','21/21；六份原图/有序目标/半径完整输出与独立出堆序列精确一致，另15项边界/复用/释放用例。Strict浮点、同步IJob.Run，无Schedule/并行化/刷新率变化。BurstDiscard证明实际编译执行。包含目标准备、清零、结果分配和拷贝的包装路径比同轮managed快38.9%–46.9%；六份均过<=0.70门槛。临时测试和asmdef恢复。证据burst-probe-20261008-01/summary.json。','', '## 第二阶段：临时整场桥接','ABBA基线P99 39.77/39.68ms，临时Burst 28.14/28.08ms；>33.33ms从每窗85降至3/4，普通中位数未退化。两轮均实际Burst284次、native创建1释放1、active0；全部临时源码和asmdef恢复。此阶段只证明场景可行性。证据burst-render-20261008-01/summary.json。','', '## 第三阶段：正式源码候选','最小生产候选：导航内部工厂直接复制不可变图，Runtime惰性拥有native工作区；Burst不可用时走原managed；意外managed执行则返回精确结果、释放并永久回退该Runtime。引擎/测试asmdef只引用已解析Unity.Burst和Unity.Collections，unsafe=false，包版本不改。','原基线150/150；候选172/172；CLI --burst-disable-compilation下172/172；GPU定向6/6。新22项含六份完整输出及distance/nextCell/heapPosition位比较、细长/舍入/阻塞/高度护栏及真实Runtime后端选择/释放。禁用测试未改用户偏好。','', '|输入|managed ms|生产包装 Burst ms|改善|','|---|---:|---:|---:|']
for r in d['local']:lines.append(f"|{r['sample']}|{r['managedMs']:.4f}|{r['burstMs']:.4f}|{100*(1-r['ratio']):.2f}%|")
lines+=['','独立ABBA：Green固定相机(250,280,-384)、2048单位、separation48、1920×1080、GTX1060 3GB、D3D11、vsync0、targetFPS=-1；每窗预热35秒、记录15秒。','', '|轮次|样本|中位数ms|P95ms|P99ms|>33.33ms|>50ms|','|---|---:|---:|---:|---:|---:|---:|']
for label,r in zip(['A1 基线','B1 候选','B2 候选','A2 基线'],d['rendered']):lines.append(f"|{label}|{r['samples']}|{r['medianMs']:.3f}|{r['p95Ms']:.3f}|{r['p99Ms']:.3f}|{r['over33ms']}|{r['over50ms']}|")
lines+=['','事前要求两组分别满足P99<=0.80、普通中位数<=1.05、>33.33ms次数<=一半、>50ms<=max(基线,3)。第一组P99仅改善16.19%，中位数退化6.08%，两项失败；第二组P99改善30.51%，通过。即使两次长帧均明显减少，也不能改判。正式候选两轮各Burst284次、managed-native0、创建1/释放1/active0；不是未启用Burst或已观察到native泄漏导致的失败。','', '## 自主追加：只读差异分析','不再启动Unity或改候选，按已有连续frames.csv重算60Hz预算及3秒分桶，见readonly-variation.json。分桶是描述性追加，不能反过来作为放宽门槛/排除较差窗口的依据。','', '|轮次|>16.67ms帧数|仅<=16.67ms子集中位数ms|','|---|---:|---:|']
for label,r in zip(['A1','B1','B2','A2'],variation):lines.append(f"|{label}|{r['over16_67ms']}|{r['ordinarySubsetMedianMs']:.3f}|")
lines+=['','现有证据不能区分B1变慢究竟来自编译/程序集布局、系统负载、GPU或其他帧内阶段；没有进程/时钟/GPU完成时间遥测，不能直接归咎环境噪声。B2表现与诊断接近也不抵消B1失败。同步求解仍占主线程，不能宣称60FPS长帧消失。','', '## 代价与未覆盖','native约4.31MiB/grid是原managed图之外的额外内存，不是节省。仍有独立managed结果分配。诊断中的约6ms快照准备、20.7/36.0ms首个观察到的Run不等于真正编译器冷启动；之前测试/磁盘缓存可能预热。当前35秒预热的门槛只覆盖稳态，冷启动、AOT、EXE、跨硬件及人工验收未完成。','', '## 恢复与工程保护','生产Nav/Runtime、引擎asmdef、测试及test asmdef全部按原字节恢复，保留原150项套件；临时observer也已恢复。此前二叉堆/方向输出和Lane提前退出仍保留，回读缓冲仍拒绝。当前权威源码仍density-runtime登记baseline，使用p3_density_runtime_20261008.py::project_check；新burst-production检查器可读历史登记，但候选未采纳。','最终全检9693基线+30已登记变更无异常；4份用户文件、37的387文件、packages manifest/lock、HEAD/index均未变；无本项目Unity进程/锁。保护pelican SVG/meta；没有打包、stage、commit、push。P3/P9及人工门槛不关闭，37不含源码优化。','', '## 后续方向与停止条件','Burst算法可行，但本次正式候选已关闭为拒绝，不再直接重跑争取通过。若继续此方向，先设计能区分差异原因且开销校准过的只读/临时分段诊断，区分普通帧、同步求解、回读与渲染等待；要有新证据或明确修复才开新候选，并重新独立验收。不得把未知噪声作为默认解释，不叠加已拒绝微优化，不改调度、刷新、人数、48或37。用户已授权范围内自主推进，包装/扩范围/人工操作仍独立处理。','', '证据根：Logs/InteractionRefinement-20261007/P3/{burst-probe-20261008-01,burst-render-20261008-01,burst-production-20261008-01}；正式测试P8/p3-burst-production-{before,after,disabled,scoped}-01；正式整场P3/p3-burst-production-abba-1..4。']
report=ROOT/'Docs/InteractionRefinement-20261007/P3-BURST-QUALIFICATION-20261008.md';assert not report.exists();report.write_bytes(('\n'.join(lines)+'\n').encode('utf-8'))
p=ROOT/'NEXT_TASK.md';backup=LANE_OUT/'NEXT_TASK.before-burst.md';assert not backup.exists();backup.write_bytes(p.read_bytes())
p.write_bytes(('''# 最新接力：Burst正式候选独立验收失败，已完整回滚（2026-10-08）

报告：Docs/InteractionRefinement-20261007/P3-BURST-QUALIFICATION-20261008.md。

## 工作方式
用户要求自己继续、不每一步问、全程监督直到结果/保护/交接；此次三个阶段均已结束，未停在启动队列。打包/范围扩张/人工验收仍有独立门槛。

## 结论
- 隔离21/21，严格浮点/完整输出/出堆序一致，实际Burst证明，局部快38.9%–46.9%。
- 临时诊断ABBA通过：P99约39.7→28.1ms，>33.33ms每窗85→3/4；临时桥接恢复。
- 正式生产候选150基线、172候选、172禁用回退、GPU6全部通过；局部快43.8%–44.8%。但自己的ABBA：A1/B1/B2/A2 P99=38.35/32.14/27.90/40.15ms；>33.33ms=85/16/1/85；中位数5.43/5.76/5.35/5.60ms。第一配对P99只改善16.19%且中位数退化6.08%，未达事前20%/5%要求。第二配对通过也不能抵消，已拒绝并恢复，不重跑到通过。
- 两轮正式场景均实际Burst284、managed-native0、created1/disposed1/active0。没有证据支持未开启或native泄漏解释。冷编译、AOT、EXE、人工尚未覆盖；额外native约4.31MiB/grid。

## 当前源码/保护
恢复原Nav/Runtime、引擎asmdef、150项测试/test asmdef与observer；此前二叉堆/方向输出、Lane提前退出仍采纳，回读缓冲仍拒绝。权威仍p3_density_runtime_20261008.py::project_check，density-runtime登记baseline。禁止直接重跑历史一次性执行器。
最终保护全过：9693+30、4用户文件、37的387文件、packages、HEAD/index未变，无本项目Unity/锁。无打包/提交/推送，37不含源码优化；pelican SVG/meta不能动，不git add .、不-nographics、不强关用户Editor，人数/48不变。P3/P9及人工待办未关闭。

## 自主追加及下一方向
已对现有frames.csv只读重算60Hz预算/普通帧子集/3秒桶，证据burst-production-20261008-01/readonly-variation.json。未据此改变门槛。现有数据无法区分程序集/系统负载/GPU/帧内阶段，不能直接甩锅噪声，也不能认为同步主线程停顿已消失。
若继续Burst方向，先做能区分上述原因、且开销经过校准的临时分段诊断；需要新证据或明确修复才能建立新候选，不同候选仍独立验收。不要再叠加低收益微优化或用诊断成功替代正式门槛。冷启动与打包边界单列。

## 证据与恢复
Logs/InteractionRefinement-20261007/P3/burst-production-20261008-01/decision.json：rejected，adopted=false，protectionPassed=true。同目录有scope/registry、全套原字节备份、局部及整场日志；P8/p3-burst-production-{before,after,disabled,scoped}-01，P3/p3-burst-production-abba-1..4。
上份NEXT_TASK原字节见burst-production-20261008-01/NEXT_TASK.before-burst.md。隔离及诊断summary分别在burst-probe-20261008-01、burst-render-20261008-01。
''').encode('utf-8'))
with (ROOT/'Docs/InteractionRefinement-20261007/P3-DEFERRED-ISSUES.md').open('ab') as f:f.write(('\n\n## 2026-10-08 Burst可行性与正式源码验收\n隔离21项与临时整场通过；正式150/172/禁用172/GPU6通过但独立ABBA首配对未达P99/普通中位数门槛，已完整回滚，保留150项原套件。不是源码采纳，不重跑取巧。详见[P3 Burst报告](P3-BURST-QUALIFICATION-20261008.md)。P3/P9/人工未关闭，37未改。\n').encode('utf-8'))
assert previous_check(full=True)['passed'] and user_check()['passed'] and not processes() and not(ROOT/'Temp/UnityLockfile').exists()
save(LANE_OUT/'publication.json',{'report':str(report.relative_to(ROOT)),'reportSha256':sha(report),'nextTaskSha256':sha(p),'adopted':False,'restored':True,'P3Closed':False,'noTasksInFlight':True,'readonlyVariation':variation})
print('PUBLISHED Burst isolation/bridge success, formal rejection and full restoration; handoff updated.',flush=True)
print(json.dumps(variation,ensure_ascii=False,indent=2),flush=True)
