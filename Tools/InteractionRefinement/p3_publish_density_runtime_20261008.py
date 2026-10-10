from p3_density_runtime_20261008 import *
sys.stdout.reconfigure(encoding='utf-8')
d=json.loads((LANE_OUT/'decision.json').read_text(encoding='utf-8'))
assert not d['adopted'] and d['protectionPassed'] and d['adoptedBinaryBaselinePreserved']
c=project_check();assert c['passed'] and c['laneVariant']=='baseline'
assert not processes() and not(ROOT/'Temp/UnityLockfile').exists()
report=ROOT/'Docs/InteractionRefinement-20261007/P3-DENSITY-RUNTIME-REJECTED-20261008.md';assert not report.exists()
a,b,c,e=d['rendered']
lines=['# P3 回读缓冲接入：正确性通过，整场门槛失败，已撤回','', '## 结论','回读缓冲候选不采纳。第一组P99改善2.36ms/6.12%，第二组仅0.20ms/0.52%；第二组普通帧中位5.33→5.65ms（+6.00%）也超门槛。不能只选第一组，不能未测量就把第二组归咎于后台/网络。生产Runtime已恢复原字节；之前已采纳导航二叉堆/方向输出与Lane提前退出全部保留。P3仍开启。','', '## 候选及事前条件','唯一生产候选改动是TerrainNavigationRuntime：在done/无错误/仍有效请求的GetData之后，懒分配一份uint[CellCount]，当帧CopyTo后按原索引顺序扫描；Dispose清除托管引用。256²多256KiB有效载荷＋数组头，跨队复用，非每帧分配。没有更改轮转、readySince、取消/显式命令或请求时序，没有把测试同步等待带入生产。此生产改动现已撤回。','本轮依据约2ms局部机会，在执行前声明小候选专用整场门槛，并向用户说明：两组P99均至少改善1ms且3%；普通中位≤1.05倍；>33.33ms不增；>50ms≤max(基线,3)。这是事前条件，不是结果出来后降低旧Lane门槛；本轮也没有事后放宽。','', '## 正确性和局部证据','优化前后各150/150定向EditMode通过：既有125＋15回读局部/边界/GPU完成回读测试＋10真实runtime生命周期对照；6/6GPU局部指令回归通过，0失败0跳过。六份完整Lane输出前后哈希一致。','生命周期测试用冻结原Runtime及相同图/真实ComputeBuffer请求比较：双队独立目标、一次Tick一个求解、禁用/停止战斗/动态关闭、显式命令使旧请求失效、取消已ready队、Dispose后Tick无操作、重复请求周期、下一次Editor更新后消费另一队拥有的目标。逐位比较GPU方向缓冲、有序目标，以及pending/ready/轮转/完成数。候选还检查懒分配和Dispose清引用。','WaitForCompletion仅在测试中使请求完成，不在生产。没有强制制造GPU设备错误；hasError分支原代码未改，但这不冒称覆盖所有硬件错误或完整游戏场景释放。延后消费测试是一次Editor更新，不冒称真人/长时间运行验收。','本轮保留新增25项及冻结参考，当前定向套件150项；旧125项未删。局部计时仍为测试内两个提取实现，不是完整Runtime计时。','', '|输入|NativeArray逐元素ms|CopyTo＋扫描ms|','|---|---:|---:|']
for q in d['densityLocal']:lines.append(f"|{q['sample']}|{q['medians']['native-index']:.5f}|{q['medians']['bulk-copy-scan']:.5f}|")
lines+=['','六份局部全部超过事前20%改善门槛；输入支持集由旧记录目标重建，正值幅度为合成，非原GPU密度快照。分配不含在稳定态局部时钟内，复制/List.Clear包含。不能把约72%局部收益声称整场收益。','', '## 完整整场ABBA','GTX1060 3GB/D3D11、Green、2048人/分离48、1920×1080、固定相机、vsync0/不限帧、35秒预热＋15秒采样。','', '|轮次|版本|中位ms|P95 ms|P99 ms|>50ms|>33.33ms|','|---|---|---:|---:|---:|---:|---:|']
for i,r in enumerate(d['rendered']):lines.append(f"|{i+1}|{['基线','候选','候选','基线'][i]}|{r['medianMs']:.2f}|{r['p95Ms']:.2f}|{r['p99Ms']:.2f}|{r['over50ms']}|{r['over33ms']}|")
for label,x,y in [('第一组',a,b),('第二组',e,c)]:lines.append(f"\n{label}P99减少{x['p99Ms']-y['p99Ms']:.2f}ms（{100*(1-y['p99Ms']/x['p99Ms']):.2f}%），普通中位变化{100*(y['medianMs']/x['medianMs']-1):+.2f}%。")
lines+=['','第二组P99绝对/相对改善和普通中位均失败；>50ms和>33.33ms条件通过不补偿其它门槛。未重跑挑结果。全部四窗>33.33ms仍85次。本轮是短Editor回调间隔，不等于OS呈现/独立EXE/任意硬件/真人验收。','', '## 当前版本与后续','生产文件为density-runtime-20261008-01/registry.json的baseline（三个文件：导航、Lane、Runtime）；该baseline已包含前两项独立采纳优化，不是历史原始导航。候选与基线完整字节在该目录candidate/baseline下。','**当前保护入口改为p3_density_runtime_20261008.py::project_check**。它识别当前Runtime基线、已有导航/Lane采纳和新增25项测试；旧p3_lane_early保护器不认识150项测试，不得因旧检查失败覆盖。','此方向局部有收益但整场未稳定确认，暂不继续重复小额回读候选ABBA。后续若继续P3，回到仍占约23.8ms的核心求解：先用既有六份固定输入、完整方向位和堆顺序护栏，测试小范围热循环重复工作削减；有明显局部收益才考虑新整场。不得直接恢复被拒绝的四叉堆/路线缓存，不将旧探针计时当精确归因，不以改刷新率/人数/48/异步调度换过线。','最终保护通过：9693基线＋30批准变更及登记源码/测试，4份用户文件、37全部387文件、HEAD/暂存区不变；任务exit0，无本项目Unity进程/锁。无打包、stage、commit或push。37不含源码优化；P3/P9和人工/EXE验收不关闭。','', '证据：Logs/InteractionRefinement-20261007/P3/density-runtime-20261008-01/{decision,scope,registry}.json；P8/p3-density-runtime-before-01、p3-density-runtime-after-01、p3-density-runtime-scoped-01；P3/p3-density-runtime-abba-1至4。']
report.write_bytes(('\n'.join(lines)+'\n').encode('utf-8'))
p=ROOT/'NEXT_TASK.md';backup=LANE_OUT/'NEXT_TASK.before-density-runtime-result.md';assert not backup.exists();backup.write_bytes(p.read_bytes())
text='''# 最新接力：回读缓冲整场未过，已撤回（2026-10-08）

最新报告：Docs/InteractionRefinement-20261007/P3-DENSITY-RUNTIME-REJECTED-20261008.md。

## 当前工程
- Runtime回读缓冲候选已撤回，之前采纳的二叉堆/方向输出与Lane提前退出保留。当前三个生产文件是density-runtime-20261008-01/registry.json的baseline，不是历史原始导航。
- 新增25项测试和冻结Runtime参考保留，当前150项定向套件。保护入口改为p3_density_runtime_20261008.py::project_check。旧Lane early入口不认识扩展测试，不能据旧检查失败覆盖。

## 本轮真实结果
- 前后各150/150定向EditMode，6/6GPU局部指令回归，0失败0跳过。生命周期比较覆盖双队、一次Tick一队、禁用/战斗停止/动态关闭、显式命令取消旧请求、ready队取消、Dispose、重复周期、下一Editor更新消费拥有的目标；完整方向/目标位与状态同冻结Runtime。没有强造GPU设备错误或真人验收。
- 本轮局部提取仍约2.68–2.77→0.675–0.767ms，但不能当整场收益。
- 事前小候选整场门槛：两组P99至少改善1ms且3%，普通中位≤1.05倍，>33.33ms不增，>50ms≤max(基线,3)。结果没有改门槛。
- ABBA P99=38.58/36.22/38.17/38.37ms。第一组改善2.36ms/6.12%；第二组仅0.20ms/0.52%，普通中位5.33→5.65（+6%）也失败。因此撤回，不选好的一轮，不归咎于未测量后台因素。
- >33.33ms四窗仍85次，P3-PERF-01仍开启。没有新包或新的EXE/真人签收。

## 下一步建议
暂停重复回读小额候选ABBA，转回仍占约23.8ms的核心求解。在六份固定输入和完整位/堆顺序护栏下，先做小范围热循环重复工作削减试验；明显局部收益才值得进入整场。不要重试旧四叉堆/路线缓存，不用受扰动旧细探针当精确归因，不改刷新率/人数/48或偷换异步调度。

## 保护
任务全部结束并收取，最终保护通过，无本项目Unity进程/锁；4份用户文件、37全部387文件、HEAD/暂存区未变；无打包/stage/commit/push。37不含源码优化。保护pelican SVG/meta与既有脏工作区，不git add .、不-nographics、不强关用户Editor。P9及其它人工事项独立待办。
上份NEXT_TASK原字节在density-runtime-20261008-01/NEXT_TASK.before-density-runtime-result.md。
'''
p.write_bytes(text.encode('utf-8'))
with (ROOT/'Docs/InteractionRefinement-20261007/P3-DEFERRED-ISSUES.md').open('ab') as f:f.write(('\n\n## 2026-10-08 回读缓冲整场未过，已撤回\n150＋6测试通过，但第二组P99仅改善0.20ms/0.52%、普通中位变慢6%，未达到本轮事前条件。Runtime已撤回；此前导航/Lane采纳保留，新25项测试保留。当前保护入口p3_density_runtime_20261008.py。见[最新结果](P3-DENSITY-RUNTIME-REJECTED-20261008.md)及NEXT_TASK。P3仍开启，37未更新。\n').encode('utf-8'))
assert not processes() and not(ROOT/'Temp/UnityLockfile').exists()
save(LANE_OUT/'publication.json',{'report':str(report.relative_to(ROOT)),'reportSha256':sha(report),'nextTaskSha256':sha(p),'densityCandidateAdopted':False,'priorAdoptionsPreserved':True,'retainedTargetedTests':150,'P3Closed':False,'noTasksInFlight':True})
print('PUBLISHED density runtime rejection; prior adoptions preserved,150 tests retained.',flush=True)
