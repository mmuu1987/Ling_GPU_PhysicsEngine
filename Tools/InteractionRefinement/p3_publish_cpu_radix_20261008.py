from p3_density_runtime_20261008 import *
sys.stdout.reconfigure(encoding='utf-8')
C=LOG/'P3'/'thread-cpu-20261008-01';R=LOG/'P3'/'radix-local-20261008-01'
a=json.loads((C/'summary.json').read_text(encoding='utf-8'));b=json.loads((R/'summary.json').read_text(encoding='utf-8'))
assert a['supportsCpuDominatedFlow'] and a['diagnosticRestored'] and a['protectionPassed']
assert b['testsPassed']==18 and b['diagnosticTestRestored'] and b['protectionPassed'] and not b['eligible']
assert project_check(full=True)['passed'] and user_check()['passed'] and not processes() and not(ROOT/'Temp/UnityLockfile').exists()
report=ROOT/'Docs/InteractionRefinement-20261007/P3-CPU-ACCOUNTING-AND-RADIX-20261008.md';assert not report.exists()
lines=['# P3：求解以实际CPU计算为主；单调队列候选淘汰','', '## 结论','本轮没有停在观测：先完成两窗主线程CPU/墙钟核查，结果支持求解与Lane主要在实际计算；随后自主筛选一个测试内单调radix优先队列，18项正确性通过但局部速度明显更慢，且末两份基准校准不通过，拒绝进入生产/整场。临时生产探针和测试均已恢复。Burst仍是可行后备，未启用。','', '## 一、主线程CPU记账，不再追错误等待指标','只在Upload的求解前、求解后、Lane后三个边界调用GetThreadTimes、QueryThreadCycleTime和原生QPC；同一原生线程ID验证。不逐帧/逐格/逐单位调用，预分配4096行，257次空采样在首个求解前预热时记录，完成后落盘。两个固定诊断窗口，每窗35秒预热+15秒采样，均取85次动态调用。','', '|窗口|阶段|累计墙钟ms|累计主线程CPU ms|CPU/墙钟|','|---|---|---:|---:|---:|']
for i,q in enumerate(a['cpuProbes'],1):
 for phase in ['flow','lane','flowAndLane']:
  x=q['phases'][phase];lines.append(f"|{i}|{phase}|{x['wallTotalMs']:.4f}|{x['cpuTotalMs']:.4f}|{100*x['cpuToWallRatio']:.2f}%|")
lines+=['','事前只用于判断方向：两窗flow累计CPU/墙钟均0.8–1.2，API读取中位<0.1ms且最大<1ms。两窗满足；这不是原整场性能门槛，也不是优化采纳。','实际线程CPU增量以15.625ms为观察到的步长，单次可能0/15.625/31.25/46.875ms。因此不拿单次CPU中位数当耗时，也不把100.13%读成负等待。累计值支持“主要是CPU计算”，但余量不能精确分成等待/抢占/GPU耗时；没有单核/频率/温度归因。线程cycle仅作为工作量记录，不换算频率或秒数。','两窗求解墙钟中位约24.19/23.80ms，Lane约5.12/5.13ms；两阶段内部GC.CollectionCount(0)增量均为0，不能据此排除其他时段的GC。边界API读取中位0.0026/0.0027ms，最大0.0067/0.0284ms，255次读取总计约0.70/0.73ms；这仅是API区间成本，不等于完整探针开销已校准。未用本轮单窗帧率改判之前校准/Burst验收。','', '## 二、据此转向有界计算优化筛选','不同于已拒绝的四叉堆或并排键缓存，这次原型利用非负Dijkstra距离单调性，将正double的精确IEEE位序用于65桶radix队列。桶内链表支持减键，最低距离桶继续以cell ID小堆保留原同距全序；距离、松弛表达式、邻居顺序和结果分配不变。零键归一处理；每次插入检查key>=last。仅测试内克隆，不改生产、包、程序集或刷新节奏。','额外两个int[CellCount]链表和65个head，256²约512KiB+260字节有效载荷，复用原heap/heapPositions。这是额外内存，不是节省。','18/18：六份历史图/有序目标/半径完整输出位与完整出堆序列一致；八份细长/大原点/舍入/空目标/重复调用；四类直接队列oracle每类40次重置，覆盖零/次正规/指数边界/极大有限键/减键/同距顺序。定时类不含trace分支，独立trace类检查完整pop序列。','', '|输入|真实生产ms|冻结基准ms|radix候选ms|基准/生产|候选/基准|','|---|---:|---:|---:|---:|---:|']
for q in b['inputs']:
 m=q['medians'];lines.append(f"|{q['sample']}|{m['TerrainNavigationGrid']:.4f}|{m['P3RadixBaseline']:.4f}|{m['P3RadixCandidate']:.4f}|{q['calibrationRatio']:.4f}|{q['ratios']['P3RadixCandidate']:.4f}|")
lines+=['','事前要求每份冻结基准与真实生产在±3%，候选六份均至少快10%，每类3预热+12轮换测量。实际候选约45.7–51.9ms，基准约24.0–25.0ms，没有收益；输入4/5基准校准还失败。因此不将这些比值宣传为精确稳定的回归幅度，但足以判定本轮不满足进入生产的前提。18项正确不等于值得采用。未重跑到通过，不继续调桶参数或叠加已拒绝微优化。','', '## 三、收敛方向及Burst后备','结束“可能是主线程Semaphore等待”的偏离路线；当前证据支持CPU求解实际工作量是关键。新数据结构没有给出合格替代方案。此前小算式/堆键/回读等筛选已未达门槛，不再为了不使用Burst而无限枚举微优化。下一优先工作转为后备Burst的剩余风险核查，尤其初始化/真正未缓存编译与现有managed回退，不立即采纳、不篡改原正式验收失败。','本轮已只读查看现用Burst1.8.29缓存选项：发现内部cache-directory常量；所查看的BurstCompilerOptions命令行初始化只处理禁用编译/强制同步两项，不能由内部常量推断支持一个可直接传给Unity的清缓存开关。没有删Library/BurstCache、没改用户偏好/包版本，也没有宣称新鲜进程等于冷编译。需要先找到可验证且可恢复的隔离方法，再开始冷启动诊断；此诊断尚未执行。','用户明确指定Burst为可行后备；现在保留候选及原失败门槛、内存/冷启动/AOT/EXE边界，不将“正确性与局部收益通过”自动升格为整场采纳。如果最终选择带已知权衡的后备，应明确记录接受的范围，而不是修改历史分数。','', '## 四、恢复与保护','CPU诊断55277ed6、队列筛选0901218d均exit0并收取；临时Runtime/observer原字节恢复、临时测试恢复为原150项，生产Nav/Lane/asmdef未变。权威仍density-runtime baseline，保护入口p3_density_runtime_20261008.py::project_check。','最终保护9693+30、4用户文件、37的387文件、HEAD/index通过，无本项目Unity或锁。此前二叉堆/方向输出与Lane优化保留，未启用Burst、未打包/暂存/提交/推送。P3/P9与人工门槛开放。','证据：Logs/InteractionRefinement-20261007/P3/thread-cpu-20261008-01/{scope,summary,registry}.json；p3-thread-cpu-1/2的thread-cpu.csv/empty/analysis、OS计数器；radix-local-20261008-01/{scope,summary,registry}.json及原测试备份，P8/p3-radix-local-01。']
report.write_bytes(('\n'.join(lines)+'\n').encode('utf-8'))
p=ROOT/'NEXT_TASK.md';backup=R/'NEXT_TASK.before-cpu-radix.md';assert not backup.exists();backup.write_bytes(p.read_bytes())
p.write_bytes(('''# 最新接力：CPU实算确认，radix替代淘汰；回到Burst后备风险核查（2026-10-08）

## 用户约定
自主继续、监督到真实结果，不每一步问。Burst明确为可行后备：其他方向实在无解时使用。当前未启用；不改历史正式门槛失败，不默认获准打包。P3/P9/人工仍开放。

## 本轮完成
- 两个固定窗口，各85次动态求解。求解累计墙钟2065.01/2059.74ms，主线程CPU2046.875/2062.5ms；Lane墙钟437.99/445.73ms、CPU各437.5ms。支持CPU实际计算占主导，不继续错误Semaphore等待路线。
- GetThreadTimes观察到15.625ms粒度，单次和CPU/墙钟差额不作精确解释；边界API中位0.0026/0.0027ms、最大0.0067/0.0284ms。不是整场性能校准，未用这轮帧率改判旧验收。
- 随后自主筛选单调radix优先队列（精确double位序、65桶、同距cell小堆），只在测试内。18/18，六份完整输出/pop顺序一致；但候选45.7–51.9ms对基准24–25ms更慢，且输入4/5基准与生产±3%校准失败。eligible=[]，不进生产/整场，不重跑争取通过。新增内存512KiB+260B/grid，未保留在生产。

## 下一方向与边界
停止无限枚举小优化。现有CPU归因及多项失败筛选使后备Burst成为下一优先风险核查对象；先核查初始化、真正未缓存编译、已验证managed回退的剩余边界，再决定启用权衡，非立即采纳，也不是宣称所有数学方案都已穷尽。
已只读确认Burst1.8.29有内部cache-directory常量，但所查看的Unity命令行初始化仅处理禁用/强制同步选项，不能凭内部常量假设有公开清缓存CLI。未删除Library缓存、未改偏好/包、未运行冷启动试验；要找到可验证/可恢复隔离方法，不把新进程/首调用当编译器真冷启动。Burst正式172/禁用172/GPU6通过但ABBA首组失败的历史仍有效，不能直接改判。候选原字节和fallback-reserve.json在burst-production-20261008-01。

## 当前源码与保护
临时Runtime/observer及测试均恢复，仍150项、density-runtime baseline，检查入口p3_density_runtime_20261008.py::project_check。此前二叉堆/输出/Lane采纳保持；Nav/asmdefs/包未改，Burst未启用。9693+30、4用户文件、37全部387文件、HEAD/index保护通过，无本项目Unity/锁；无打包/stage/commit/push。pelican SVG/meta不动，人数/48不变，不-nographics、不强关用户Editor。

## 证据
报告：Docs/InteractionRefinement-20261007/P3-CPU-ACCOUNTING-AND-RADIX-20261008.md。
Logs/InteractionRefinement-20261007/P3/thread-cpu-20261008-01/summary.json：supportsCpuDominatedFlow=true，diagnosticRestored/protectionPassed=true。CPU诊断55277ed6 exit0。
radix-local-20261008-01/summary.json：testsPassed18，calibrated=false，eligible=[]，diagnosticTestRestored/protectionPassed=true。筛选0901218d exit0。无任务在途，勿重跑一次性执行器。
旧NEXT_TASK原字节：radix-local-20261008-01/NEXT_TASK.before-cpu-radix.md。
''').encode('utf-8'))
with (ROOT/'Docs/InteractionRefinement-20261007/P3-DEFERRED-ISSUES.md').open('ab') as f:f.write(('\n\n## 2026-10-08 主线程CPU与单调队列筛选\n两窗累计CPU/墙钟支持求解以实际CPU计算为主。自主追加radix18项正确通过，但局部明显更慢且末两份校准失败，淘汰并恢复测试。回到Burst后备的剩余风险核查，未启用/打包，不改历史门槛。详见[P3 CPU与radix报告](P3-CPU-ACCOUNTING-AND-RADIX-20261008.md)。\n').encode('utf-8'))
assert project_check(full=True)['passed'] and user_check()['passed'] and not processes() and not(ROOT/'Temp/UnityLockfile').exists()
save(R/'publication.json',{'report':str(report.relative_to(ROOT)),'reportSha256':sha(report),'nextTaskSha256':sha(p),'cpuDominatedSupported':True,'radixEligible':False,'sourceRestored':True,'burstStillReserved':True,'burstEnabled':False,'P3Closed':False,'noTasksInFlight':True})
print('PUBLISHED CPU accounting and rejected radix18 screen; all restored; Burst reserve risk review next.',flush=True)
