from p3_density_copy_20261008 import *
sys.stdout.reconfigure(encoding='utf-8')
d=json.loads((WORK/'summary.json').read_text(encoding='utf-8'))
assert d['protectionPassed'] and d['diagnosticTestRestored'] and d['localGatePassed'] and d['testsPassed']==15
c=lane_check();assert c['passed'] and c['laneVariant']=='candidate'
assert not processes() and not(ROOT/'Temp/UnityLockfile').exists()
report=ROOT/'Docs/InteractionRefinement-20261007/P3-DENSITY-COPY-LOCAL-20261008.md';assert not report.exists()
lines=['# P3 回读目标提取：局部等价试验通过，尚未接入生产','', '## 结论','批量CopyTo复用uint缓冲后顺序扫描，在六份重建密度支持集上，含复制的提取耗时由2.73–2.82ms降到0.70–0.79ms，约减少72%–74%。每份20/20配对更快，满足预声明至少20%改善且至少16/20配对更快的局部门槛。15项测试通过，临时测试代码按原字节恢复，生产未改。此结论只支持继续最小runtime集成试验，不等于已采纳或P3关闭。','', '## 输入与计时边界','六份图与有序目标来自此前不可变固定输入。由目标格重建密度正值支持集，并交替填1与uint.MaxValue；这些不是新捕获的原始GPU密度值，也不是新实战回放。先确认重建支持集经原扫描得到与记录完全相同的有序目标。','两种测试内实现严格保留原Clear、i递增、density>0、IsWalkable(CellCenter(i))过滤及目标CellCenter写入。原实现逐元素读NativeArray；候选先CopyTo同长度复用uint数组，再遍历。每模式3次预热＋20次测量，两模式轮换；List.Clear与整块复制都在计时内，对拍在计时外。NativeArray、目标容量和scratch分配在时钟外，是稳定态提取实验，不能冒称覆盖首次分配或零分配。','', '|输入|目标数|原逐元素读取ms|复制＋扫描ms|减少|更快配对|','|---|---:|---:|---:|---:|---:|']
for q in d['inputs']:lines.append(f"|{q['sample']}|{q['targets']}|{q['medians']['native-index']:.5f}|{q['medians']['bulk-copy-scan']:.5f}|{100*(1-q['ratio']):.2f}%|{q['pairedFaster']}/20|")
lines+=['', '## 正确性与GPU检查','- 六份输入每次计时后按float原始位核对有序目标，源NativeArray不变，scratch最终与源一致。','- 八项边界/复用：全零、首/末可走格、全正密度、混合稀疏值、只含阻塞格、跨队复用后空输入清理、分数坐标中心。先污染scratch再覆盖，另一个空团队复用同一缓冲后，前一团队已生成的目标列表保持不变。','- 一项真实GPU检查：ComputeBuffer.SetData后AsyncGPUReadback.Request，测试中WaitForCompletion取得已完成视图；原扫描与CopyTo扫描逐位一致。释放GPU buffer后只使用拥有的托管目标/缓冲，不访问请求视图。没有跳过或模拟GPU。','**WaitForCompletion只用于测试获取已完成请求，绝不能加入生产Tick。**该smoke不是完整跨帧请求取消、双队轮转、异步错误或场景释放回归；不能将它冒称上一轮6项GPU局部指令回归。本轮也没有重跑既有125项。','', '## 内存及接入条件','若生产接入，256×256网格一个复用uint数组的有效载荷为262144字节（256KiB）＋数组头，按runtime一次，不是每队/每帧新建。应在当前请求仍有效的同帧内完成CopyTo，然后只保留拥有的托管数据/目标。不能持有失效NativeArray，也不能延后GetData。','下一步值得做最小runtime候选：只替换回读目标提取的读取方式，保持pending/ready、原有done/error判断、取消/显式命令、轮转/readySince和Dispose逻辑。先补这些生命周期与双队回归，验证scratch初始化/复用/释放边界，随后才考虑有事前门槛的整场比较。','局部绝对收益约2ms，并非整场72%改善；与上轮未全通过校准的2.73ms观察不可直接拼接作精确预算，更不能保证长帧低于33.33ms。该优化机会较小，应避免不必要的多轮队列；整场若无可靠收益不采纳。当前尚无生产候选、无新整场结果。','', '## 最终保护','远程试验exit0、15/15通过、0失败0跳过；临时追加的测试按原字节恢复，之前125项测试及所有生产文件不变。既有二叉堆/方向输出与Lane提前退出优化保留。','最终保护通过：9693基线＋30批准变更及登记文件、4份用户文件、37全部387文件、HEAD与暂存区均符合。无本项目Unity进程/锁；无打包、stage、commit、push。37未更新，不含源码优化。P3-PERF-01/P9/其它人工或EXE验收仍独立待办。','长期保护入口仍为p3_lane_early_20261008.py::project_check，当前生产仍为lane-early-20261008-01登记candidate；本轮诊断入口仅识别临时测试。','', '证据：Logs/InteractionRefinement-20261007/P3/density-copy-20261008-01/{scope,summary,registry}.json、test-before.cs.txt、test-diagnostic.cs.txt；P8/p3-density-copy-01/{process.json,results.xml,density-copy-0.json至5.json}。']
report.write_bytes(('\n'.join(lines)+'\n').encode('utf-8'))
p=ROOT/'NEXT_TASK.md';backup=WORK/'NEXT_TASK.before-density-copy-report.md';assert not backup.exists();backup.write_bytes(p.read_bytes())
text='''# 最新接力：回读目标提取局部试验通过，生产未改（2026-10-08）

最新报告：Docs/InteractionRefinement-20261007/P3-DENSITY-COPY-LOCAL-20261008.md。

## 当前生产/保护
保留二叉堆/方向输出与Lane两条提前退出。生产及125项测试源码未改，临时追加测试已恢复。长期保护入口仍p3_lane_early_20261008.py::project_check；源码为lane-early-20261008-01登记candidate。不要按旧拒绝报告回退。

## 本轮完成
- 15/15临时测试通过：六份记录目标支持集重建密度（非原始GPU密度值）、八项空/稠密/稀疏/阻塞/分数中心/复用边界，以及一项真实GPU完成回读smoke。
- 3次预热＋20配对，复制及List.Clear计入，分配/对拍在时钟外。原2.7301–2.8184ms，CopyTo复用缓冲＋扫描0.69865–0.79070ms；逐输入减少71.82%–74.42%，每份20/20配对更快，超过事前≥20%且≥16/20条件。
- 有序目标及中心float位一致，源数据不变，scratch完全覆盖。GPU测试中WaitForCompletion仅为测试，不准加入生产。
- 这只支持局部方案，不是新生产采纳或整场收益。请求取消/双队轮转/场景释放完整集成尚未验证；未重跑既有125＋6或ABBA。

## 下一步
做最小runtime候选，只把当帧已完成回读的目标提取改为CopyTo复用uint缓冲再顺序扫描；保持done/error/pending/ready、取消/显式命令、readySince/轮转及Dispose语义。先做生命周期/双队回归；不得持有失效NativeArray、延后GetData或引入同步等待。预计每runtime增256KiB有效载荷＋数组头（256²），不是每队/每帧分配。验证集成后再按事前门槛决定是否值得整场；局部约2ms不是整场72%改善，也不能保证P3关闭。

## 已知限制与保护
上一轮分段计时首组P99-5.33%超±5%校准，不能直接拼接精确预算。最近四窗仍85次>33.33ms，P3/P9/人工/EXE待办不变。此次任务exit0，最终保护通过，无本项目Unity进程/锁；4份用户文件、37全部387文件、HEAD/暂存区未变。无出包/提交/推送；37不含源码优化。保护pelican SVG/meta和既有脏工作区，不git add .、不-nographics、不强关用户Editor、不改刷新率/人数/48。
上份NEXT_TASK原字节在density-copy-20261008-01/NEXT_TASK.before-density-copy-report.md。
'''
p.write_bytes(text.encode('utf-8'))
with (ROOT/'Docs/InteractionRefinement-20261007/P3-DEFERRED-ISSUES.md').open('ab') as f:f.write(('\n\n## 2026-10-08 回读提取局部方案通过\n15项测试通过，CopyTo复用缓冲后扫描约2.73–2.82→0.70–0.79ms，目标位/顺序一致。尚未接入生产或验证整场/完整请求生命周期。临时测试恢复，已有优化保持。见[局部试验报告](P3-DENSITY-COPY-LOCAL-20261008.md)及NEXT_TASK；P3仍开启、37未变。\n').encode('utf-8'))
assert not processes() and not(ROOT/'Temp/UnityLockfile').exists()
save(WORK/'publication.json',{'report':str(report.relative_to(ROOT)),'reportSha256':sha(report),'nextTaskSha256':sha(p),'productionUnchanged':True,'localGatePassed':True,'P3Closed':False,'noTasksInFlight':True})
print('PUBLISHED density-copy local evidence; production unchanged; lifecycle integration remains next.',flush=True)
