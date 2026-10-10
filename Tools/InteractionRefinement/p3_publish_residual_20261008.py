from p3_residual_20261008 import *
sys.stdout.reconfigure(encoding='utf-8')
d=json.loads((DETAIL/'summary.json').read_text(encoding='utf-8'))
assert d['protectionPassed'] and d['diagnosticRestored'] and len(d['rendered'])==4 and len(d['stages'])==2
c=adopted_check();assert c['passed'] and c['laneVariant']=='candidate'
assert not processes() and not(ROOT/'Temp/UnityLockfile').exists()
report=ROOT/'Docs/InteractionRefinement-20261007/P3-RESIDUAL-DIAGNOSIS-20261008.md';assert not report.exists()
a,b,c,e=d['rendered'];delta1=100*(b['p99Ms']/a['p99Ms']-1);delta2=100*(c['p99Ms']/e['p99Ms']-1)
lines=['# P3 新基线长帧分段诊断：同步导航主导，校准未全通过','', '## 结论','已完整收取原版/插桩/插桩/原版四轮，并恢复临时代码；保留此前已采纳的二叉堆/输出与Lane提前退出优化，没有新增性能候选。两份插桩窗口均有85个>33.33ms间隔，全部对应同帧的一次导航更新。观察到求解约23.8–23.9ms、Lane约5.05ms、回读整理约2.73ms、SetData CPU约0.055ms；Tick外墙钟残余约5.6ms，与普通间隔5.34–5.36ms接近。','**事前计时校准未全部通过，不能称为精确、已校准的成本归因。**首组插桩P99相对原版约低5.33%，超过±5%范围；第二组约高0.37%在范围内。保留全部数据，不重跑挑结果、不事后放宽，不因本轮校准失败撤回已独立验收的生产优化。','', '## 方法和边界','在当前已采纳源码上临时修改TerrainNavigationRuntime及Editor观察器。只用粗时钟：Tick入口/回读准备结束/Tick结束，Upload的求解前后/Lane后/SetData前后；无逐格或逐边探针。事件是预分配32768容量的结构体列表，本轮7537/7586条，未扩容；只在窗口结束后写CSV。仍不能声称全部零分配，原列表/结果数组/Stopwatch分配保持。','四轮分别是完全原字节、插桩、插桩、完全原字节。GTX1060 3GB/D3D11、Green、2048/48、1920×1080、固定相机、vsync0、不限帧、35秒预热＋15秒采样。不重跑旧候选验收，本轮是计时扰动控制，不是新优化ABBA。','直接以Time.frameCount连接该帧内事件和结束于该帧渲染回调的墙钟间隔；同时检查±1/±2帧匹配。两份插桩记录lag0均85/85，其它四种lag均0，且每个求解帧仅一次上传。该连接不依赖Profiler上一帧读数。','', '## 全部窗口','|轮次|版本|间隔样本|中位ms|P95 ms|P99 ms|>50ms|>33.33ms|','|---|---|---:|---:|---:|---:|---:|---:|']
for i,r in enumerate(d['rendered']):lines.append(f"|{i+1}|{['原字节','临时插桩','临时插桩','原字节'][i]}|{r['samples']}|{r['medianMs']:.2f}|{r['p95Ms']:.2f}|{r['p99Ms']:.2f}|{r['over50ms']}|{r['over33ms']}|")
lines += ['',f'预声明控制门槛：两组插桩/原版P99比值在0.95–1.05、普通中位比值在0.90–1.10。P99差异实际为{delta1:+.2f}%/{delta2:+.2f}%，因此controlGatePassed=false。原字节首尾P99本身也从40.89到38.35ms，存在跨轮变化，但未测量其来源，不能归咎于网络、后台程序或单一探针。与上轮36.6–36.9ms不可直接拼接成本或判生产回归。','', '## 插桩窗口内逐帧分段','下列均为85个求解帧的各项中位数，不保证中位数可逐项相加。','|量|窗口2 ms|窗口3 ms|','|---|---:|---:|']
keys=[('整个回调间隔','intervalMs'),('CreateFlowField（含紧邻的Stopwatch启动）','flowMs'),('Lane','laneMs'),('SetData CPU调用','setDataCpuMs'),('整个Upload','uploadTotalMs'),('回读就绪检查/取数/稀疏目标整理','readbackPrepMs'),('整个Terrain Tick','tickTotalMs'),('Tick外墙钟残余','outsideTickMs'),('Upload外墙钟残余','outsideUploadMs')]
for name,k in keys:lines.append(f"|{name}|{d['stages'][0]['solveFrameMedians'][k]:.4f}|{d['stages'][1]['solveFrameMedians'][k]:.4f}|")
lines+=['','SetData CPU最大0.1258/0.1247ms，不支持优先优化该调用的CPU耗时；这绝不证明GPU执行成本/后续同步等待为零。Tick外残余不是某个具体主线程函数，更不是全部GPU等待或GC暂停；当前没有证据可将它细分。','回读整理2.73ms包含pending/done检查、GetData、NativeArray密度遍历、walkability/中心与目标列表写入，尚未分离各项；不能将全部成本归给NativeArray索引或某一次拷贝。无上传普通帧的整个Tick中位仅0.0006ms。','本轮临时计时没有改计算表达式、目标顺序、上传内容或调用调度；但本轮不是一次新的逐位输出/GPU回归测试，不冒充重跑125＋6。现有125项源码原字节保持，之前的独立正确性与采纳证据仍有效。','', '## 下一步选择','优先做一个局部等价试验，而非立即再跑四轮整场：针对回读目标提取，比较现有NativeArray逐元素扫描与“当帧批量复制到复用uint缓冲，再按原索引顺序扫描”。这只是待验证方案，尚未实施：必须包含拷贝成本、额外每runtime约256KiB有效载荷（256²）及生命周期；不得缓存失效NativeArray/延后读取；保留所有目标顺序、过滤、CellCenter位、双队/请求完成/取消/场景释放语义。','先用固定/边界密度输入逐位对拍有序目标并测整段开销，验证有实质收益再决定是否值得进入生产及整场。若局部收益不足就停止，不为过线叠加未经验证改动。即使这一段完全消失，约2.7ms也不足保证所有长间隔降到33.33ms以下，求解仍是主要成本；不承诺这一小改就关闭P3。不降低刷新率/人数/48，不恢复拒绝缓存或擅改异步调度。','', '## 保护及交接','诊断远程进程exit0；两个临时改动文件均按原字节恢复。已采纳导航/Lane和125项测试保持；9693基线＋30批准变更及登记文件、4份用户文件、37全部387文件、HEAD/暂存区检查通过。无本项目Unity进程/锁，无打包、stage、commit、push。','当前长期保护入口仍为p3_lane_early_20261008.py::project_check；p3_residual_20261008.py只用于本轮诊断注册，不取代已采纳入口。37不含源码优化。P3-PERF-01、P9及其它人工/EXE验收状态不关闭。','', '证据：Logs/InteractionRefinement-20261007/P3/residual-20261008-01/{summary,scope,registry}.json及original/probed字节；P3/p3-residual-1至4的process.json、frames.csv、performance.json；p3-residual-2/3的stages.csv与aligned-stages.csv/json。']
report.write_bytes(('\n'.join(lines)+'\n').encode('utf-8'))
p=ROOT/'NEXT_TASK.md';backup=DETAIL/'NEXT_TASK.before-residual-report.md';assert not backup.exists();backup.write_bytes(p.read_bytes())
text='''# 最新接力：新基线分段诊断结束，原字节恢复（2026-10-08）

最新报告：Docs/InteractionRefinement-20261007/P3-RESIDUAL-DIAGNOSIS-20261008.md。
采纳依据仍为P3-LANE-EARLY-EXIT-ADOPTED-20261008.md及先前二叉堆/输出报告。

## 当前工程
- 生产仍保留已采纳二叉堆/方向输出和Lane两条提前退出；本轮Runtime/观察器临时计时已按原字节恢复，125项测试源码未改，无新增优化候选。
- 长期保护入口仍为p3_lane_early_20261008.py::project_check，源码哈希仍是lane-early-20261008-01/registry.json的candidate。不可按旧拒绝报告回退。

## 新结果及限制
- 原字节/插桩/插桩/原字节P99为40.89/38.71/38.49/38.35ms，普通中位5.67/5.41/5.39/5.38ms；>33.33ms仍85/窗。
- 计时控制预设P99±5%；首组-5.33%越界，第二+0.37%通过。因此校准未全通过，不能称已精确归因，不重复跑到过线，不据此撤回已独立验收的源码。
- 两份插桩85长间隔均对应同帧一次导航上传；lag±1/±2均0匹配。求解中位23.82–23.89ms、Lane5.05ms、回读整理2.73ms、SetDataCPU约0.055ms、整个Tick31.86–31.98ms，Tick外墙钟残余约5.6ms（非具体CPU/GPU/GC归因）。
- 回读整理包含GetData/密度遍历/过滤/中心/列表，不能单独归因于NativeArray索引。SetData CPU短不等于GPU执行/等待为零。各阶段中位数不可直接相加。

## 下一步
先做回读目标提取的局部等价试验：当帧批量复制到复用uint缓冲后按原索引扫描，对比现有NativeArray逐元素访问。尚未实现；必须计入拷贝/约256KiB额外有效载荷，精确保持目标顺序/中心位、请求完成/取消/释放及双队语义，不能延后GetData或持有失效NativeArray。先固定/边界输入对拍与整段计时，收益足够才进生产和整场，不先堆ABBA。约2.7ms上限也不能承诺P3闭环，求解仍占主要成本。

## 保护
全部任务已结束，最终保护通过，无本项目Unity进程/锁；4份用户文件、37全部387文件、HEAD/暂存区未变，无打包/提交/推送。37不含源码优化。保护pelican SVG/meta与既有脏工作区；不git add .、不-nographics、不强关用户Editor、不改刷新率/人数/48。P3/P9及人工/EXE验收独立待办。
上一份NEXT_TASK原字节：residual-20261008-01/NEXT_TASK.before-residual-report.md。
'''
p.write_bytes(text.encode('utf-8'))
with (ROOT/'Docs/InteractionRefinement-20261007/P3-DEFERRED-ISSUES.md').open('ab') as f:f.write(('\n\n## 2026-10-08 新基线分段诊断\n四轮已收取并恢复临时代码；保留此前采纳优化。两份插桩窗口长帧均与同帧导航更新对应，但首组P99计时校准超±5%，不宣称精确归因。回读整理约2.73ms成为下一局部等价试验方向，尚无新候选。见[分段诊断报告](P3-RESIDUAL-DIAGNOSIS-20261008.md)和NEXT_TASK。P3仍开启、37未变。\n').encode('utf-8'))
assert not processes() and not(ROOT/'Temp/UnityLockfile').exists()
save(DETAIL/'publication.json',{'report':str(report.relative_to(ROOT)),'reportSha256':sha(report),'nextTaskSha256':sha(p),'priorAdoptionsPreserved':True,'diagnosticSourceRestored':True,'controlGatePassed':d['controlGatePassed'],'P3Closed':False,'noTasksInFlight':True})
print('PUBLISHED residual diagnosis, calibration limitation and restored source handoff.',flush=True)
