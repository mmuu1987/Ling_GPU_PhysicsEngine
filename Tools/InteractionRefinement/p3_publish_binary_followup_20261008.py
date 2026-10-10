from p3_binary_output_20261008 import *
sys.stdout.reconfigure(encoding='utf-8')
assert not processes() and not(ROOT/'Temp/UnityLockfile').exists()
check=project_check(full=False);assert check['passed'] and check['outputVariant']=='candidate'
d=json.loads((OUT/'decision.json').read_text(encoding='utf-8'))
tdir=LOG/'P3'/'traversal-details-20261008-01'
t=json.loads((tdir/'summary.json').read_text(encoding='utf-8'))
assert d['adopted'] and d['protectionPassed'] and t['protectionPassed']
registry=json.loads((OUT/'registry.json').read_text(encoding='utf-8'))
assert sha(ROOT/NAV_PATH)==registry['variants']['candidate']
report=ROOT/'Docs/InteractionRefinement-20261007/P3-BINARY-OUTPUT-ADOPTED-20261008.md'
assert not report.exists()
lines=['# P3：遍历归因完成，二叉堆热循环＋方向输出优化已采纳','',
'## 结论','',
'本轮跟进到真实结果收取、条件核对和源码采纳：保留二叉堆结构与距离/格索引全序，只减少sift中的重复优先级读取/比较，并组合已精确验证的均匀格距九方向表。固定输入约31.4ms→23.2ms；本轮两组整场P99降低约15%，>50ms间隔85→1/3，普通帧中位无退化，达到事前条件。P3-PERF-01仍开启：>33.33ms每窗仍85次，不把阈值跨越当作周期停顿消失。','',
'## 一、遍历内部归因及测量边界','',
'只在测试程序集添加临时诊断克隆，生产源码当时保持原字节；六份已保存图/目标/停止半径，四种完整求解模式及两种堆轨迹模式轮换。各模式两次预热、八次测量。6/6用例通过，所有完整方向float位与记录一致，真实堆轨迹每次pop顺序完全一致；原始轨迹已保存并哈希。临时测试代码已恢复到上一轮保留的14项护栏和冻结原实现，不留诊断分支。','',
'|输入|目标数|原实现ms|关闭探针的诊断克隆ms|细计时ms|独立堆轨迹ms|轨迹框架ms|','|---|---:|---:|---:|---:|---:|---:|']
for r in t['inputs']:
 m=r['modeMedianMs'];lines.append('|'+str(r['sample'])+'|'+str(r['goals'])+'|'+'|'.join(format(m[k],'.3f') for k in ['original','disabled','timed','heap-trace','trace-scaffold'])+'|')
lines += ['', '每份输入出堆52,320次，邻居槽扫描418,560次，合法边415,816次，未结算邻居207,908次；成功松弛71,268–72,612次，降键19,541–20,549次，比较921,437–923,330次，堆峰值807–840。工作量在重复运行中一致，不因目标数变少而省掉全图遍历。','',
'计时探针不是免费：即使关闭采样，诊断克隆的包装/分支已使原31.5–31.7ms变成38.6–38.8ms；细计时进一步到44.4–45.3ms。空时钟对校准约56ns/对，每次求解约12.4万对，其量级约7ms。该量级只解释扰动，不盲目扣除。计时版pop约16.1ms、更新约3.6ms及剩余约15.7–16.3ms不能直接当作未插桩生产组件耗时；剩余尤其包含探针和分支。','',
'独立重放同一Push/Decrease/Pop序列约17.5ms；纯记录遍历/写入框架约1.39ms。这是测试重放环境，不与整场或原22ms遍历直接相减。但该独立证据与约92万次比较共同支持优先减少二叉堆热循环的重复工作。没有重新试已证实更慢的四叉堆，也未声称邻居松弛已经精确测为某个毫秒数。','',
'证据：Logs/InteractionRefinement-20261007/P3/traversal-details-20261008-01/summary.json、scope.json；P8/p3-traversal-details-01/traversal-*.json与*-heap-trace.bin。','',
'## 二、采纳的最小生产改动','',
'- TerrainNavigationGrid.PushOrDecrease：同一次上滤的cell距离不变，只读一次；每层父节点的距离只读一次，严格保留distance优先、cell id破同分的顺序。',
'- PopMinimum：缓存被下滤last的距离；选左右子节点时复用已读的child距离，保持二叉结构、原数组、位置映射、严格比较和回写顺序。',
'- 组合均匀格距方向表：在构造时检查所有相邻float中心间距；只有两轴均匀才预计算9个方向，否则原算式回退。原始double归一化和正零语义保持，额外只有9个Vector2（72字节有效载荷＋数组头）。',
'- 不变：目标顺序/过滤、Dijkstra松弛及边代价、禁止切角、停止半径、Lane规则、队列/刷新率/同步提交、局部指令生命周期、正式48、2048人、场景/角色/Shader。没有重新采纳静态直线路径段缓存。','',
'## 三、固定输入与GPU回归','',
'|输入|同轮原实现ms|当前采纳实现ms|降低|','|---|---:|---:|---:|']
for r in d['after']:lines.append(f"|{r['sample']}|{r['referenceMedianMs']:.4f}|{r['currentMedianMs']:.4f}|{(1-r['ratio'])*100:.2f}%|")
lines += ['', '六份输入均逐位输出一致，当前/原实现比值还均比上一轮方向单独候选再低至少5%，达到预先固定的局部门槛。91/91定向EditMode通过（含14个固定输入/构造/舍入回退用例，以及既有地形、Cardinal、局部计划护栏）；P8真实GPU局部四命令/拒绝/暂停/清理6/6，0失败0跳过。原实现前置校准复用上一轮证据，没有冒充本轮重跑；不是全项目或EXE/真人验收。', '',
'## 四、整场ABBA与预设条件','',
'|轮次|版本|普通中位ms|P95 ms|P99 ms|>50ms/15秒|>33.33ms/15秒|','|---|---|---:|---:|---:|---:|---:|']
for i,r in enumerate(d['rendered']):lines.append(f"|{i+1}|{['原','本轮候选','本轮候选','原'][i]}|{r['medianMs']:.2f}|{r['p95Ms']:.2f}|{r['p99Ms']:.2f}|{r['over50ms']}|{r['over33ms']}|")
a,b,c,e=d['rendered'];g1=100*(1-b['p99Ms']/a['p99Ms']);g2=100*(1-c['p99Ms']/e['p99Ms'])
lines += ['',f'两组P99改善{g1:.2f}%/{g2:.2f}%，均超过10%；>50ms次数均至少减少50%；普通中位未变慢超过10%。三项条件均在本轮scope.json先声明，结果没有改变门槛。候选因此被采纳为有边界的性能改善，不是P3全部通过。', '',
'GTX1060 3GB / D3D11 / Green / 2048 / 分离48 / 1920×1080，35秒预热＋15秒窗口，相机、场景与限帧设置相同。统计相邻渲染回调墙钟间隔，保留全部轮次。帧计数连续不能证明逐帧战局完全相同；短Editor采样不等同OS呈现、独立EXE、任意场景/硬件或长期不卡顿。','',
'## 五、当前工程与接力','',
'当前生产导航源码为采纳候选，SHA256：`'+registry['variants']['candidate']+'`。原始回退字节及哈希在binary-output-20261008-01/reference.cs.txt及registry.json，禁止按前轮“所有候选撤回”的旧文自动恢复。当前保护入口是Tools/InteractionRefinement/p3_binary_output_20261008.py的project_check；旧output/Cardinal保护器不认识新生产哈希。','',
'本轮唯一生产源码净改动为TerrainNavigationGrid.cs。原有测试护栏保持，临时遍历诊断已恢复。最终保护通过：当前批准源码/测试、4份既有用户文件、37包387文件、HEAD与暂存区均符合登记；无在途任务和本项目锁。没有出包、stage、commit或push。37包没有这次优化。','',
'分配计数API仍按上一轮1MiB正对照失败处理，不据原始0声称零分配；结果数组仍分配约512KiB有效载荷。不将分配或GC代数次数冒称GC暂停时长。','',
'下一步：保留本次收益，针对仍约每0.17秒一次的33–50ms停顿继续定位剩余CPU求解/Lane工作；如对Lane作优化，先复用同图同目标和完整方向对拍，再验证实际收益。不要再反复做已结束的ABBA队列，不降低刷新率、人数或48，不把真人动画/小体型/音频/P9其它事项混为完成。','',
'权威证据：Logs/InteractionRefinement-20261007/P3/binary-output-20261008-01/decision.json、scope.json、registry.json。测试：P8/p3-binary-output-after-01、p3-binary-output-scoped-01；四轮原始CSV：P3/p3-binary-output-abba-1至4。']
report.write_bytes(('\n'.join(lines)+'\n').encode('utf-8'))
old=ROOT/'NEXT_TASK.md';backup=OUT/'NEXT_TASK.before-binary-adoption.md';assert not backup.exists();backup.write_bytes(old.read_bytes())
nexttext='''# 接力：P3已有采纳的局部性能修复，周期性停顿仍未全消除（2026-10-08）

权威报告：Docs/InteractionRefinement-20261007/P3-BINARY-OUTPUT-ADOPTED-20261008.md。

## 当前状态——不要按旧交接回退
- 已采纳：原二叉堆sift中缓存不变优先级、减少重复读取/比较，组合均匀float格距九方向表。没有使用四叉堆或旧Cardinal连续段缓存。
- 当前TerrainNavigationGrid.cs是本轮candidate，不是原reference。哈希与备份见Logs/InteractionRefinement-20261007/P3/binary-output-20261008-01/registry.json、candidate.cs.txt、reference.cs.txt。
- 当前保护入口：Tools/InteractionRefinement/p3_binary_output_20261008.py的project_check。旧output/Cardinal/P8原始检查器不认识这份批准生产改动，不要因此覆盖源码。
- 本轮唯一生产源码净改动为TerrainNavigationGrid.cs；已有14项输出/固定输入护栏及冻结原实现保留于TerrainNavigationGridTests.cs，旧42项Cardinal测试/meta保留。临时遍历诊断克隆已恢复。

## 最新证据
- 遍历诊断6/6：每次出堆52,320次、比较约92万次；诊断克隆即使关闭计数仍有包装开销，计时版31.6→44–45ms。独立真实堆操作轨迹约17.5ms、纯遍历框架约1.39ms，不将差值硬当原生产堆耗时；原始轨迹与校准均保存。
- 采纳候选六份固定输入约31.35–31.50→23.15–23.31ms（约26%），全部float位一致；91/91定向EditMode与6/6真实GPU局部指令回归，0失败0跳过。
- ABBA P99：55.88/47.50/47.43/56.14ms；>50ms每15秒：85/1/3/85；普通帧中位4.94–5.00ms。两组P99约改善15%，达到事前门槛，保留候选。
- **P3仍开启**：>33.33ms每窗仍85次，周期性同步CPU停顿没有消失。不能声称全工况流畅、EXE/真人签收或P9完成。
- GC分配API已知1MiB正对照仍报告0，此计数不可用，不能声称零分配；结果数组仍有约512KiB有效载荷。

## 下一步
保留本次收益，继续针对剩余CPU求解/Lane工作收窄33–50ms停顿；如优化Lane，先做同图同目标及完整方向精确对拍，再核验收益。不重复已结束的队列，不重试更慢四叉堆，不自动恢复撤回Cardinal缓存，不降低刷新率/人数/48来过线。

## 保护与交接
- 本轮所有任务已结束并收取结果，无在途Unity/本项目锁。最终源码、测试、4份用户文件、37包387文件、HEAD/暂存区保护通过。
- 37包未更新，不包含本次源码修复；没有38、提交或推送。保护pelican SVG/meta及既有脏工作区，不git add .、不强关用户Editor、不-nographics。
- 旧NEXT_TASK原字节保留在binary-output-20261008-01/NEXT_TASK.before-binary-adoption.md；更早不采纳报告都是历史，不与本轮采纳混用。
- P3真人动画/小体型与P1/P2音频/真人事项独立保留；不因旧索引重做P9。
'''
old.write_bytes(nexttext.encode('utf-8'))
index=ROOT/'Docs/InteractionRefinement-20261007/P3-DEFERRED-ISSUES.md'
with index.open('ab') as f:
    f.write(('\n\n## 2026-10-08 后续更新\n\nP3-PERF-01仍开启，但已采纳一项有边界的源码性能改善：二叉堆优先级复用＋均匀格距方向表。两组P99约56→47.5ms，>50ms每15秒85→1/3；>33.33ms仍85。当前源码不再是此前全撤回状态；以[P3最新采纳报告](P3-BINARY-OUTPUT-ADOPTED-20261008.md)及NEXT_TASK为准，不自动恢复旧reference。37未更新。\n').encode('utf-8'))
save(OUT/'publication.json',{'report':str(report.relative_to(ROOT)),'reportSha256':sha(report),'nextTaskSha256':sha(old),'navigationSha256':sha(ROOT/NAV_PATH),'productionCandidateRetained':True,'P3Closed':False,'noTasksInFlight':not processes() and not(ROOT/'Temp/UnityLockfile').exists()})
print('Published latest adoption, remaining boundary and current protection baseline. Candidate retained; P3 not closed.',flush=True)
