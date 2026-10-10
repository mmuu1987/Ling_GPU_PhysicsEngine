from p3_lane_work_20261008 import *
import struct
sys.stdout.reconfigure(encoding='utf-8')
d=json.loads((WORK/'summary.json').read_text(encoding='utf-8'))
assert d['status']=='diagnosis_complete_no_production_change' and d['protectionPassed']
c=lane_check();assert c['passed'] and c['laneVariant']=='baseline'
assert not processes() and not(ROOT/'Temp/UnityLockfile').exists()
report=ROOT/'Docs/InteractionRefinement-20261007/P3-LANE-WORK-20261008.md';assert not report.exists()
rows=[]
for r in d['inputs']:
 i=r['sample'];a=(LOG/'P3'/'fixed-input-20261008-01-capture'/('fixed-sample-'+str(i)+'-expected.bin')).read_bytes();b=(P8/'p3-lane-work-01'/('lane-work-final-'+str(i)+'.bin')).read_bytes()
 assert hashlib.sha256(b).hexdigest()==r['finalOutputSha256']
 n=struct.unpack_from('<i',a)[0];assert n==r['counts']['cells'] and a[:4]==b[:4] and len(a)==len(b)==4+n*8
 bits=sum(a[4+j*8:12+j*8]!=b[4+j*8:12+j*8] for j in range(n))
 numeric=sum(struct.unpack_from('<ff',a,4+j*8)!=struct.unpack_from('<ff',b,4+j*8) for j in range(n))
 assert numeric==r['counts']['changed'] and bits>=numeric
 k=r['counts'];q={'sample':i,'bitChangedCells':bits,'numericChangedCells':numeric,'extraBitOnlyChanges':bits-numeric,'exactNoopReplacements':k['replacements']-bits,'exactNoopFractionOfRoutes':(k['replacements']-bits)/k['routeCalls'],'bothLanesEmptyFractionOfActive':k['bothLanesEmpty']/k['active']};rows.append(q)
save(WORK/'bit-noop-analysis.json',{'inputs':rows,'scope':'Postprocess recorded pre/post complete raw float bytes, no Unity execution. Actual replacement count from exact counting clone; subtract bit-changed cells. Not a proposed optimization performance test.'})
lines=['# P3 Lane工作量诊断：优先验证提前排除无用工作','', '## 结论','生产源码未改。本轮六份固定输入表明，约70%–74%有效格的行列均无目标，且约71%–80%的路线检查最终只重复写入完全相同的float位。下一候选应优先验证两个提前退出，而不是恢复旧Cardinal连续段缓存、标量/中心轴候选或直接改异步调度。这里只证明工作冗余，未证明整场收益；P3仍开启。','', '## 方法与可信边界','使用此前六份不可变图/目标/停止半径/求解输出，不重新采集。仅临时追加测试克隆，计数运行不计时、不在单格或单边调用时钟。每份运行两次，所有计数完全重复。二分查找结果（含命中下标）与List.BinarySearch核对，每次路线判定与生产HasClearCardinalRoute33核对；完整最终输出float位与生产实现和上轮保存的哈希一致，目标不变。','未插桩生产Apply仅在计数前后各2次预热＋8次测量，用于报告对照，不把计数克隆的开销或次数换算成生产阶段耗时。本轮只跑6项诊断测试，不冒充重跑既有105项或GPU/整场回归。','', '|输入|有效格|行列均无目标|二分查找次数|路线检查|边读取|实际方向位改变|重复写入相同位|','|---|---:|---:|---:|---:|---:|---:|---:|']
for r,q in zip(d['inputs'],rows):
 k=r['counts'];lines.append(f"|{r['sample']}|{k['active']}|{k['bothLanesEmpty']}|{k['searches']}|{k['routeCalls']}|{k['routeEdges']}|{q['bitChangedCells']}|{q['exactNoopReplacements']}|")
lines+=['','六份中每次路线边读取约75万–84万次，独特有向边约1.37万–1.58万，单边最高重复117–120次。但旧路由缓存已失败，重复次数本身不构成重启它的理由。此批路线全部通畅，只能代表这些记录输入；不能据此删掉通路检查，障碍/不通仍必须保持。','目标数249–606且此批无重复目标；测试与未来候选仍须保留重复权重与顺序语义，不据输入碰巧无重复改契约。二分比较约3.38万–5.59万次，不能仅按次数声称二分或路径占多少毫秒。','', '## 浮点位补核与候选边界','对原始输入与完整最终输出的每个Vector2的8字节直接比较，再从实际替换次数减去位变化数。此批数值变化数与位变化数一致，没有额外仅符号零变化；这并不证明其它输入没有。详情见bit-noop-analysis.json。','建议最小候选只做：','1. 对合法格，在确认其行列都无目标时跳过后续中心/朝向计算。注意保持非法索引/超长方向数组、停止/不可达、null等原有行为，不靠新fixture掩盖语义变化。','2. 计算proposed之后，若输入与proposed的两个float位确实相同，可保留原值而跳过dot、查找、Distance和路线检查。理由：原流程只可能保留original或写入proposed；两者位相同则结果完全一致，相关导航查询为只读。必须保留signed-zero区别，不能用Unity近似Vector2==或普通数值相等冒充位相等。具体位判定实现成本还需测量。','3. 本轮不改生产、不组合上轮已拒绝的中心轴/标量候选、不引入持久路线缓存、不改刷新率/人数/48/调度。未来先加入signed-zero、NaN/Inf、阈值、目标重复、阻塞/停止和长度边界精确对拍，再按事前局部与整场门槛决定；不能从当前计数推导毫秒收益或承诺过线。','', '## 未插桩控制时间','|输入|计数前中位ms|计数后中位ms|','|---|---:|---:|']
for r in d['inputs']:lines.append(f"|{r['sample']}|{r['beforeMedianMs']:.4f}|{r['afterMedianMs']:.4f}|")
lines+=['', '## 最终状态','临时诊断克隆已按原字节恢复，上一轮保留的105项定向测试源码不变。生产仍为已采纳二叉堆/输出版本＋原Lane。当前保护入口仍是Tools/InteractionRefinement/p3_lane_followup_20261008.py::project_check，诊断期间专用入口不替代它。','远程任务exit0，最终保护通过：9693基线＋30批准变更及登记文件、4份用户文件、37包全部387文件、HEAD及暂存区均符合；无本项目Unity进程或锁。无GPU/ABBA重跑、打包、提交或推送。37未更新，不含源码优化。P3/P9及其它人工验收状态不变。','', '证据：Logs/InteractionRefinement-20261007/P3/lane-work-20261008-01/{summary,scope,registry,bit-noop-analysis}.json；临时诊断字节test-diagnostic.cs.txt及原字节test-before.cs.txt；P8/p3-lane-work-01/results.xml、lane-work-*.json、lane-work-final-*.bin。']
report.write_bytes(('\n'.join(lines)+'\n').encode('utf-8'))
p=ROOT/'NEXT_TASK.md';backup=WORK/'NEXT_TASK.before-work-report.md';assert not backup.exists();backup.write_bytes(p.read_bytes())
text='''# 当前接力：Lane工作量诊断完成，生产未改（2026-10-08）

最新报告：Docs/InteractionRefinement-20261007/P3-LANE-WORK-20261008.md。

- 6/6固定输入诊断通过：无每格计时；两次计数一致，二分/每次路线结果及完整最终float位精确核对。临时测试代码已恢复，既有105项测试源码不变。
- 约70%–74%有效格所在行列均无目标；约71%–80%路线检查只重复写入完全相同的float位。75万–84万次边读取不直接等于某毫秒成本，不能据此恢复旧路由缓存。
- **下一步候选方向**：行列双空提前退出＋原方向/proposed逐位相同时提前退出。先审查非法索引和方向长度行为，补signed-zero/NaN/Inf/阈值/重复目标/阻塞/停止对拍；再事前登记收益门槛。普通Vector2==或数值相等不能代替位相等。候选尚未实现，性能尚未验证。
- 当前生产仍是lane-20261008-01登记的baseline：已采纳二叉堆/方向输出优化＋原Lane。上一轮中心轴/标量候选已撤回，不能悄悄组合回来。当前保护仍用p3_lane_followup_20261008.py::project_check。
- 最终保护通过，任务exit0，无本项目Unity进程或锁；37全部387文件、用户文件、HEAD/暂存区未变。无整场/GPU重跑或打包/提交/推送。
- P3-PERF-01仍开启；最近完整ABBA每窗>33.33ms仍85次，本轮无新整场数据。P9与人工/EXE验收不变。
- 继续保护pelican SVG/meta、既有脏工作区；不git add .，不强关用户Editor，不-nographics，不改变人数/刷新率/48。37不包含源码修复。

历史：Lane候选失败见P3-LANE-REJECTED-20261008.md；此前已采纳优化见P3-BINARY-OUTPUT-ADOPTED-20261008.md。上一份NEXT_TASK完整原字节在lane-work-20261008-01/NEXT_TASK.before-work-report.md。
'''
p.write_bytes(text.encode('utf-8'))
with (ROOT/'Docs/InteractionRefinement-20261007/P3-DEFERRED-ISSUES.md').open('ab') as f:f.write(('\n\n## 2026-10-08 Lane工作量诊断完成\n6/6固定输入精确诊断通过；生产未改、临时测试恢复。下一候选方向为空行列及逐位相同方向提前退出，尚未实现或证明性能收益。P3-PERF-01仍开启。见[P3 Lane工作量报告](P3-LANE-WORK-20261008.md)及NEXT_TASK。\n').encode('utf-8'))
assert not processes() and not(ROOT/'Temp/UnityLockfile').exists()
save(WORK/'publication.json',{'report':str(report.relative_to(ROOT)),'reportSha256':sha(report),'nextTaskSha256':sha(p),'productionUnchanged':True,'P3Closed':False,'noTasksInFlight':True})
print('PUBLISHED',json.dumps(rows,ensure_ascii=False,indent=2),flush=True)
