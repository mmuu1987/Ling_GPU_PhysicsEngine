from p3_density_runtime_20261008 import *
sys.stdout.reconfigure(encoding='utf-8')
O=LOG/'P3'/'thread-scope-clock-20261008-01';P=LOG/'P3'/'thread-scope-20261008-01'
d=json.loads((O/'summary.json').read_text(encoding='utf-8'));old=json.loads((P/'summary.json').read_text(encoding='utf-8'));a=json.loads((O/'analysis.json').read_text(encoding='utf-8'))
assert all(x['diagnosticRestored'] and x['protectionPassed'] and not x['performanceQualificationAttempted'] for x in [d,old])
assert project_check(full=True)['passed'] and user_check()['passed'] and not processes() and not(ROOT/'Temp/UnityLockfile').exists()
report=ROOT/'Docs/InteractionRefinement-20261007/P3-THREAD-SCOPE-20261008.md';assert not report.exists()
lines=['# P3：排除错误的主线程等待读数，修正跨进程时钟对齐','', '## 结论','完成线程范围诊断和一次明确的时钟fixture修正验证，两次临时源码均完整恢复。此前Semaphore约12ms不是主线程等待；不再沿这条错误线索改引擎。低频系统/本Unity进程CPU现已能用同一原生QPC时钟与测量窗口对齐。本轮只验证观测，不是性能校准/正式验收重试，也没有启用Burst。','', '## 线程范围：参考API与本机实测','参考源码： https://raw.githubusercontent.com/Unity-Technologies/UnityCsReference/master/Runtime/Profiler/ScriptBindings/ProfilerRecorder.bindings.cs 。该参考分支不等同于本机版本；实际用Unity6000.3.14f1编译运行验证：DefaultValue=24，CollectOnlyOnCurrentThread=4，主线程回调启动两组独立recorder，一组默认，另一组显式Default|CollectOnlyOnCurrentThread。','参考Default=WrapAroundWhenCapacityReached|SumAllSamplesInFrame，并未启用当前线程过滤。实际同一窗口同时记录的结果如下，不是跨轮减去两个中位数。','', '|诊断|默认Semaphore中位ms|限定主线程中位ms|主线程最大ms|默认sample Count中位|主线程Count中位|','|---|---:|---:|---:|---:|---:|']
for label,x in [('首次线程范围',old),('原生时钟修正版',d)]:
 w=x['waitProbes'][0];c=w['counters'];n=w['semaphoreSampleCounts'];lines.append(f"|{label}|{c['semaphoreNs']['medianMs']:.4f}|{c['mainSemaphoreNs']['medianMs']:.4f}|{c['mainSemaphoreNs']['maxMs']:.4f}|{n['semaphoreSampleCount']}|{n['mainSemaphoreSampleCount']}|")
lines+=['','足以否定“主线程每帧在Semaphore等待约12ms”的解释。只能排除这个具体marker解释，不能据此断言主线程没有其他等待。Present/TargetFPS/EditorLoop仍全零，即便Valid也不表示真实环节零成本；最新样本仍可能滞后。','', '## 发现并修复了诊断工具错误','首次原生OS QPC与Mono Stopwatch频率均10000000，却分别是系统计时起点和另一相对起点。对齐分析断言没有足够重叠区间并exit1，阻止了错误CPU窗口结论。这个失败是fixture时间域假设错误，不是产品缺陷；首次线程范围结果仍有效，首次CPU/帧关联无效且不使用。','未靠调整偏移猜测起点，也未仅凭频率相等继续。修正版observer和旁路采样都显式使用Windows QueryPerformanceCounter/QueryPerformanceFrequency，临时stage记录仍使用Stopwatch，只按frameID关联，禁止两种时钟直接相减。修正后同一15秒窗口包含14个完整1秒OS区间，验证时间域实际重叠。','', '## 低频CPU结果（仅本次窗口）','GetSystemTimes与GetProcessTimes，每秒一次，只查询本次拥有的Unity PID及系统汇总，不枚举其他进程、不采集进程名称，不调优先级/电源/驱动或结束用户进程。机器8逻辑CPU。','', '|指标|中位|最小|最大|','|---|---:|---:|---:|']
for label,key in [('系统总CPU忙碌率%','systemBusyPercent'),('Unity占系统CPU容量%','unityPercentOfSystemCpuCapacity'),('Unity等效忙碌逻辑核数','unityCpuCoreEquivalents'),('单次计数器读取ms','counterReadCostMs')]:
 r=a['cpu'][key];lines.append(f"|{label}|{r['median']:.4f}|{r['min']:.4f}|{r['max']:.4f}|")
lines+=['','系统总量没有接近饱和，但不能排除单核忙、频率/调度变化或GPU争用。Unity进程CPU包括多线程，不能当成主线程CPU；其余系统忙碌也不能直接归为某个外部程序。计数器API读取成本不是整个采样器的开销证明，未作优化收益或无扰动宣称。未采GPU时钟/利用率、逐核CPU或主线程CPU时间，不能用本次记录反推之前Burst B1的历史波动原因。','', '## 下一步取舍','已解决的是观测假设，不是卡顿本身。停止围绕全线程Semaphore数字尝试优化，也不以“总CPU不满”断言CPU求解无瓶颈。下一项更有针对性的证据是：仅在导航求解边界记录主线程CPU时间与墙钟，跨85次求解累计比较，并报告GetThreadTimes等API的计时粒度/调用成本；避免把采样器本身铺到每格/每单位或采整个系统进程列表。若实施，仍为临时诊断且先明确校准边界，不得把未校准中位数当正式收益。此项尚未执行。','Burst继续作为用户明确指定的可行后备、未启用；不认为其他方向已全部耗尽，不改写原正式门槛失败。若最终采用，需明确代价/剩余验证，不意味着自动打包或完成AOT/冷启动/人工验收。','', '## 保护与证据','两次诊断exit0且Runtime/observer原字节恢复，第一次纯分析exit1的原因和修正已披露。当前仍density-runtime baseline、原150项测试及早前采纳的二叉堆/输出/Lane优化。两次全检9693+30、4用户文件、37的387文件、HEAD/index均通过，无项目Unity/锁；无打包、stage、commit、push，P3/P9开放。','Logs/InteractionRefinement-20261007/P3/thread-scope-20261008-01：第一次summary，线程范围有效、CPU窗口关联弃用；thread-scope-clock-20261008-01：summary、analysis、aligned-os-intervals。原始p3-thread-scope-clock-1含wait-probe.csv、recorder-options.txt、os-counters.csv/status、marker inventory和stages。']
report.write_bytes(('\n'.join(lines)+'\n').encode('utf-8'))
p=ROOT/'NEXT_TASK.md';backup=O/'NEXT_TASK.before-thread-scope.md';assert not backup.exists();backup.write_bytes(p.read_bytes())
p.write_bytes(('''# 最新接力：主线程等待误读已排除，原生时钟对齐完成（2026-10-08）

## 用户约定
自主继续并监督到结果，不每一步问。Burst是可行后备，其他方向实在无解时采用；当前未启用，不抹掉正式门槛失败，不默认获准打包。P3/P9/人工仍开放。

## 本轮实际完成
1. API参考+本机Unity6000.3.14f1双recorder验证：默认无当前线程过滤；默认Semaphore中位12.59ms，主线程过滤仅0.0002ms，Count中位86对1。不是主线程12ms等待，停止这条错误归因。
2. 低频OS/拥有的Unity PID CPU采样没有报错，但首次CPU对齐分析exit1：Mono Stopwatch和原生QPC同频率却不同起点，未使用错误关联。这是诊断fixture缺陷，不是产品问题。
3. 修正版显式原生QPC时钟，两端频率/起点匹配，15秒内14个完整1秒区间。系统忙碌率中位29.94%（27.34–33.27），Unity容量占比23.65%，约1.89逻辑核。读取API单次中位0.131ms、最大0.462ms，不等于总采样开销；未做性能校准或门槛重试。
4. 修正版Semaphore默认12.40ms、主线程0.0002ms、主线程最大0.0005ms，再次印证范围误读。零marker仍不解释为零耗时，GPU latest不逐帧相减。

## 结论边界与下一步
排除的是特定marker的误读，尚未解释历史Burst B1波动或修复卡顿。总CPU未饱和不排除单核/频率/调度/GPU问题；没有GPU时钟、逐核或主线程CPU时间。下一项考虑仅在导航求解边界测主线程CPU时间对墙钟，累计多次求解并标明GetThreadTimes粒度/调用成本；不要继续全线程等待数字驱动的优化或盲目重复ABBA。该下一项尚未实现/执行。

## 保护与证据
两次临时Runtime/observer均原字节恢复。当前仍150项、density-runtime baseline，保护入口p3_density_runtime_20261008.py::project_check；之前二叉堆/输出/Lane保持。全检9693+30、4用户文件、37的387文件、HEAD/index通过，无Unity/项目锁，无打包/暂存/提交/推送；pelican SVG/meta不动，不-nographics、不强关Editor，人数/48不变。
报告：Docs/InteractionRefinement-20261007/P3-THREAD-SCOPE-20261008.md。
证据：Logs/InteractionRefinement-20261007/P3/thread-scope-20261008-01（首次范围有效，CPU关联无效）；thread-scope-clock-20261008-01/{summary,analysis,aligned-os-intervals}.json（修正后）。任务3d617e58、14ee01fe已exit0；首次分析399cc234 exit1，修正分析0398490b exit0。所有工作已收取，无任务在途，勿重跑一次性脚本。
旧NEXT_TASK原字节见thread-scope-clock-20261008-01/NEXT_TASK.before-thread-scope.md。
''').encode('utf-8'))
with (ROOT/'Docs/InteractionRefinement-20261007/P3-DEFERRED-ISSUES.md').open('ab') as f:f.write(('\n\n## 2026-10-08 线程范围与CPU时钟诊断\n确认默认Semaphore约12ms非主线程等待；主线程过滤约0.0002ms。发现并修正Mono/原生时钟不同起点的诊断fixture缺陷，已获得14个同窗口CPU区间；不反推历史波动、不作性能通过宣称。见[P3线程范围报告](P3-THREAD-SCOPE-20261008.md)。临时源码恢复，Burst仍可行后备未启用，37未动。\n').encode('utf-8'))
assert project_check(full=True)['passed'] and user_check()['passed'] and not processes() and not(ROOT/'Temp/UnityLockfile').exists()
save(O/'publication.json',{'report':str(report.relative_to(ROOT)),'reportSha256':sha(report),'nextTaskSha256':sha(p),'sourceRestored':True,'firstCpuAlignmentRejected':True,'correctedNativeClockAlignmentPassed':True,'performanceQualificationAttempted':False,'burstStillReserved':True,'P3Closed':False,'noTasksInFlight':True})
print('PUBLISHED verified thread-scope correction and native-clock CPU analysis; source restored.',flush=True)
