from p3_lane_followup_20261008 import *
sys.stdout.reconfigure(encoding='utf-8')
d=json.loads((LANE_OUT/'decision.json').read_text(encoding='utf-8'))
assert d['protectionPassed'] and not d['adopted'] and d['adoptedBinaryBaselinePreserved']
assert not processes() and not(ROOT/'Temp/UnityLockfile').exists()
c=project_check(full=False);assert c['passed'] and c['laneVariant']=='baseline'
report=ROOT/'Docs/InteractionRefinement-20261007/P3-LANE-REJECTED-20261008.md';assert not report.exists()
lines=['# P3 Lane优化验证：候选撤回，保留此前已采纳优化','', '## 结论','Lane候选未达到事前整场门槛，已恢复本轮baseline：此前已采纳的二叉堆优先级复用＋均匀格距方向输出表，以及原Lane。不是恢复古老导航reference。P3-PERF-01仍开启。','', '## 改动与局部证据','候选仅预计算原表达式得到的X/Z中心坐标轴，并标量化Lane逐格扫描，延后构造Vector2。保留零乘积、NaN/Inf、严格阈值、重复目标权重、排序过滤、Distance和路径检查。两轴在256²网格有效载荷2048字节加两个数组头；不是路由缓存。生产两文件均已撤回本轮改动。','基线扫描占约97%–99%；优化前后各105项定向EditMode通过，含新增14项Lane/中心测试；6项真实GPU局部指令回归通过，0失败0跳过。六份固定输入全量方向float位、中心位及目标保持一致，前后最终Lane输出哈希一致。','测试四模式轮换，每模式2次预热、12次计时；复制与比较位于计时外。下列为同轮参考/候选中位数，不是整场耗时。','', '|输入|参考ms|候选ms|降低|','|---|---:|---:|---:|']
for r in d['after']:
 m=r['modeMedianMs'];lines.append(f"|{r['sample']}|{m['reference']:.4f}|{m['current']:.4f}|{100*(1-r['currentRatio']):.2f}%|")
lines+=['','输入2参考19.8832ms而粗计时参考17.1277ms，有明显测量波动；不据此宣称稳定绝对节省或精确加法归因。局部22%–24%收益仍需由整场门槛判定。分配计数API的既有正对照失败仍有效，不能声称零分配。','', '## 整场ABBA','基线是此前已采纳版本；GTX1060 3GB/D3D11、Green、2048人、分离48、1920×1080、固定相机、vsync0、不限帧；35秒预热＋15秒窗口。保留全部轮次。','', '|轮次|版本|中位ms|P95 ms|P99 ms|>50ms|>33.33ms|','|---|---|---:|---:|---:|---:|---:|']
for i,r in enumerate(d['rendered']):lines.append(f"|{i+1}|{['基线','候选','候选','基线'][i]}|{r['medianMs']:.2f}|{r['p95Ms']:.2f}|{r['p99Ms']:.2f}|{r['over50ms']}|{r['over33ms']}|")
a,b,c,e=d['rendered'];lines+=['',f"两组P99改善{100*(1-b['p99Ms']/a['p99Ms']):.2f}%/{100*(1-c['p99Ms']/e['p99Ms']):.2f}%，均未达到事前10%。第一组>50ms从6增至13，也不满足不超过max(基线,3)的门槛。普通中位满足≤1.10倍，>33.33ms仍每窗85次。未事后改门槛或挑选轮次。",'这是短Editor渲染回调墙钟间隔对照，不是OS呈现、独立EXE、逐帧同战局或真人签收；不把首组异常归因于未测量的后台活动。','', '## 工程状态与后续','生产源码已恢复Lane阶段baseline；新增14项测试保留，总105项定向测试。当前保护入口为Tools/InteractionRefinement/p3_lane_followup_20261008.py::project_check，旧binary检查器不认识扩展测试，不能按旧保护失败覆盖它们。注册哈希、两个生产文件baseline/candidate字节和测试备份均在lane-20261008-01/。','最终保护通过：9693基线文件＋30已批准变更及登记源码/测试、4份用户文件、37包全部387文件、HEAD/暂存区未变；本轮远程进程exit0，无本项目Unity进程或锁。没有stage/commit/push/新包；37不含源码优化。','下一步应保留已采纳二叉堆/输出收益，先针对Lane扫描剩余工作做低扰动分项计数及同输入复杂度分析，再决定是否有更大且行为精确一致的削减。不能反复重跑本候选挑过线结果，不能降低刷新率/人数/48，不能恢复旧Cardinal缓存。更大异步/调度方案须单独审查一致性与范围，不在本轮偷改。','其它P3视觉/真人、P1/P4/P5/P8及P9状态不因本轮变化而签收。','', '证据：Logs/InteractionRefinement-20261007/P3/lane-20261008-01/decision.json、scope.json、registry.json；P8/p3-lane-before-01、p3-lane-after-01、p3-lane-scoped-01；P3/p3-lane-abba-1至4。']
report.write_bytes(('\n'.join(lines)+'\n').encode('utf-8'))
p=ROOT/'NEXT_TASK.md';backup=LANE_OUT/'NEXT_TASK.before-lane-result.md';assert not backup.exists();backup.write_bytes(p.read_bytes())
intro='''# 最新接力：Lane候选撤回，之前已采纳优化保留（2026-10-08）

权威最新报告：Docs/InteractionRefinement-20261007/P3-LANE-REJECTED-20261008.md。

- 本轮已完成并收取：前后105项EditMode、6项GPU、完整ABBA和最终保护。没有在途队列或本项目Unity锁。
- Lane局部22%–24%改善，但整场P99仅4.16%/6.43%，未达预设10%；首组>50ms 6→13也失败。因此Lane两文件改动撤回，之前采纳的二叉堆/方向输出优化未撤回。
- 当前生产版本是lane-20261008-01/registry.json中的baseline，完整备份在同目录baseline/；不是旧导航原始reference。新增14项Lane/中心护栏保留。
- **当前保护入口只用p3_lane_followup_20261008.py::project_check**；旧binary保护器不认识新增测试。
- 全四窗>33.33ms仍85次，P3-PERF-01仍开启，37全部387文件未变，不含源码修复；无打包/提交/推送或真人签收。
- 下一步：保留现有收益，低扰动分析Lane逐格扫描剩余工作及精确等价减少工作量的空间。不要重跑已拒绝候选挑结果、重试旧Cardinal缓存、降低刷新率/人数/48或无审查引入异步调度。
- 保护pelican SVG/meta、既有脏工作区及用户文件；不git add .，不强关用户Editor，不-nographics。P9与其它人工验收独立保留。

## 上一阶段历史交接（下文保护入口与下一步已被上文取代）

'''
p.write_bytes(intro.encode('utf-8')+backup.read_bytes())
index=ROOT/'Docs/InteractionRefinement-20261007/P3-DEFERRED-ISSUES.md'
with index.open('ab') as f:f.write(('\n\n## 2026-10-08 Lane后续结果\nLane候选局部改善但整场门槛失败，已撤回；此前二叉堆/输出优化仍保留。P3-PERF-01仍开启，37未变。最新状态及保护入口见[P3 Lane报告](P3-LANE-REJECTED-20261008.md)和NEXT_TASK。\n').encode('utf-8'))
assert not processes() and not(ROOT/'Temp/UnityLockfile').exists()
save(LANE_OUT/'publication.json',{'report':str(report.relative_to(ROOT)),'reportSha256':sha(report),'nextTaskSha256':sha(p),'laneCandidateAdopted':False,'priorBinaryAdoptionPreserved':True,'P3Closed':False,'noTasksInFlight':True})
print('PUBLISHED Lane rejection; prior adopted binary baseline preserved; handoff updated.',flush=True)
