from p3_density_runtime_20261008 import *
sys.stdout.reconfigure(encoding='utf-8')
C=LOG/'P3'/'core-local-20261008-01';H=LOG/'P3'/'heap-key-local-20261008-01'
a=json.loads((C/'summary.json').read_text(encoding='utf-8'));b=json.loads((H/'summary.json').read_text(encoding='utf-8'))
assert all(d['protectionPassed'] and d['diagnosticTestRestored'] and d['calibrated'] and not d['eligible'] and d['testsPassed']==14 for d in [a,b])
c=project_check();assert c['passed'] and c['laneVariant']=='baseline'
assert not processes() and not(ROOT/'Temp/UnityLockfile').exists()
bounds=[]
for tag in ['p3-residual-2','p3-residual-3']:
 folder=LOG/'P3'/tag;frames=list(csv.DictReader((folder/'frames.csv').open(encoding='utf-8')));wanted={int(x['frame']) for x in frames[1:]}
 events=sorted([{k:int(v) for k,v in x.items()} for x in csv.DictReader((folder/'stages.csv').open(encoding='utf-8')) if int(x['kind'])==1],key=lambda x:x['t0']);last={};n=same=0
 for x in events:
  if x['frame'] in wanted:n+=1;same+=int(last.get(x['team'])==x['goals'])
  last[x['team']]=x['goals']
 bounds.append({'tag':tag,'dynamicUpdates':n,'sameCount':same,'exactSequenceReuseUpperBound':same/n})
lock=json.loads((ROOT/'Packages/packages-lock.json').read_text(encoding='utf-8'))['dependencies'];manifest=json.loads((ROOT/'Packages/manifest.json').read_text(encoding='utf-8'))['dependencies'];asm=json.loads((ROOT/'Assets/MassEngine/MassEngine.asmdef').read_text(encoding='utf-8'))
review={'targetCountBounds':bounds,'burstResolvedVersion':lock.get('com.unity.burst',{}).get('version'),'collectionsResolvedVersion':lock.get('com.unity.collections',{}).get('version'),'burstDirectDependency':'com.unity.burst' in manifest,'engineReferences':asm['references'],'engineUnsafeAllowed':asm['allowUnsafeCode'],'packageManifestSha256':sha(ROOT/'Packages/manifest.json'),'packageLockSha256':sha(ROOT/'Packages/packages-lock.json')};save(H/'followup-readonly-review.json',review)
report=ROOT/'Docs/InteractionRefinement-20261007/P3-CORE-LOCAL-SCREEN-20261008.md';assert not report.exists()
lines=['# P3 核心求解：两轮候选筛选结束，均不进入整场','', '## 结论','按用户要求自主连续推进，没有在第一轮失败后等待确认。第一轮筛选已结算格不变量外提、均匀输出下标差解码及组合；未达局部收益门槛，直接淘汰。第二轮独立筛选二叉堆距离键就近存储，也未达门槛，直接淘汰。两轮均14/14测试通过、完整输出位及出堆顺序一致，但正确不等于值得采纳。未改生产、未启动不值得的GPU/ABBA队列。','', '## 第一轮：减少重复计算','同一已采纳源码冻结基准，三个测试内原型：','- Pop后缓存该格邻居掩码、edgeCosts基址和已结算距离，不改邻居顺序或松弛表达式。','- 已有均匀方向表激活且宽度≥3时，用next-cell的索引差还原相邻dx/dz，省去重复除法/取模；宽度1/2有歧义或非均匀中心时保持原算式。','- 以上组合；没有新缓存/数组，没有组合上轮回读候选。','每模式3次预热、12次轮换测量，含当前生产、冻结基准及三个原型。冻结基准/生产每输入差异均在预设±3%内；候选必须六份均比冻结基准快至少10%才继续，结果均不满足。','', '|输入|冻结基准ms|不变量外提ms|下标差输出ms|组合ms|组合改善|','|---|---:|---:|---:|---:|---:|']
for r in a['inputs']:
 m=r['medians'];lines.append(f"|{r['sample']}|{m['P3CoreBaseline']:.4f}|{m['P3CoreHoisted']:.4f}|{m['P3CoreDelta']:.4f}|{m['P3CoreCombined']:.4f}|{100*(1-r['ratios']['P3CoreCombined']):.2f}%|")
lines+=['','组合仅改善约1.3%–3.4%，最后一份组合还弱于输出单项，不能假定小收益可相加。这个量级也接近编译布局/计时差异，不能宣称稳定整场效果。','', '## 第二轮：堆键就近存储','二叉堆结构、distance/cell全序和降键次序完全不变；测试内新增与heap位置并排的double键数组，在上/下滤时与cell一起搬移，避免比较时再次间接访问整张网格distances。需要每网格512KiB有效载荷＋数组头（256²），不新增每次求解分配。没有使用旧四叉堆或路线缓存，也没有混入第一轮已拒绝的小改动。','', '|输入|冻结基准ms|堆键原型ms|改善|','|---|---:|---:|---:|']
for r in b['inputs']:
 m=r['medians'];lines.append(f"|{r['sample']}|{m['P3HeapBaseline']:.4f}|{m['P3HeapKeys']:.4f}|{100*(1-r['ratios']['P3HeapKeys']):.2f}%|")
lines+=['','仅约2.6%–3.9%改善，不满足事前六份均≥10%。虽正确，也不足以承担额外键同步和内存，已淘汰，不反复跑到门槛通过。','', '## 正确性与测量隔离','每轮14项：六份原图/原有序目标/半径完整方向float位与旧记录一致；八项构造护栏覆盖宽1/2/3、单行、分数格、大正负原点、重复/空目标。计时类不含任何出堆记录分支。','另用独立、未计时的trace类比较完整出堆序列，每份52,320次；第二轮还在每次非空堆操作后检查heapKeys[i]==distances[heap[i]]以及反向位置映射。该重型检查绝未纳入性能计时。原型/基准都保留原结果数组分配；不使用已失效分配计数API宣称零分配。','测试内克隆把不可访问的TerrainSurface.Finite替为同表达式私有helper，冻结基准同样处理，并单独与真实生产计时/输出校准。没有把临时克隆或探针留在生产。','', '## 自主追加的只读排查','没有再试缓存前，先用已有两份分段事件核查同队相邻动态更新目标数量：分别仅4/85及2/85相同。因此“有序目标完全相同”最多4.71%/2.35%，实际可能更低；数量相同不证明目标相同。不值得为此直接新增结果缓存，也未新增目标采样队列。','编译条件只读核查：Packages锁文件已有间接Burst '+str(review['burstResolvedVersion'])+'、Collections '+str(review['collectionsResolvedVersion'])+'，manifest没有直接Burst依赖。MassEngine程序集references为空、allowUnsafeCode=false；Assets/MassEngine当前未检出BurstCompile/Unity.Jobs/IJob用法。这表明包已解析，不等于现有求解已使用Burst，也不等于切换即可免费加速。包和程序集配置未改。','', '## 后续执行边界','停止继续堆叠这些低收益微优化。下一项有区分度的研究是使用现有已解析Burst，在隔离测试中验证同一算法同步编译执行的可行性：不并行化、不改变刷新与队列、不移交生产内存所有权、不改引擎asmdef/包版本。须先证明实际编译执行、严格浮点与完整出堆/输出位一致，并把managed/native拷贝与生命周期成本纳入端到端对照，才能讨论生产接入。当前只完成只读条件核查，尚未实现/测量Burst原型，不宣称收益；若需要扩大范围或新依赖，须单独处理边界。','用户授权范围内的分析、候选筛选、回退、定向验收自行继续，不每一步问确认；出包、范围扩张或人工操作仍有独立门槛。','', '## 工程保护','两轮临时测试均按原字节恢复到已保留150项套件，生产完全未改。当前仍为density-runtime-20261008-01登记baseline（包含此前独立采纳的二叉堆/方向输出和Lane提前退出，不含已拒绝的回读缓冲）。长期保护入口仍p3_density_runtime_20261008.py::project_check。','最终保护通过：4份用户文件、37全部387文件、HEAD/暂存区、基线及登记源码/测试符合；两个诊断任务均exit0，无本项目Unity进程或锁，无打包/stage/commit/push。P3-PERF-01/P9和人工/EXE验收不关闭。','最初上传曾在本地触发命令参数长度限制，一次远程启动因脚本尚不存在而exit2，未启动Unity或修改项目源码；改为进程内传输后上述两轮正常完成。此错误不是产品或测试失败。','', '证据：P3/core-local-20261008-01与heap-key-local-20261008-01的summary/scope/registry、test-before/test-diagnostic；P8/p3-core-local-01、p3-heap-key-local-01；后续只读核查见heap-key-local-20261008-01/followup-readonly-review.json。']
report.write_bytes(('\n'.join(lines)+'\n').encode('utf-8'))
p=ROOT/'NEXT_TASK.md';backup=H/'NEXT_TASK.before-core-screen.md';assert not backup.exists();backup.write_bytes(p.read_bytes())
text='''# 最新接力：自主完成两轮核心局部筛选，均淘汰（2026-10-08）

报告：Docs/InteractionRefinement-20261007/P3-CORE-LOCAL-SCREEN-20261008.md。

## 工作方式
用户再次明确：自己能推进的下一步就自主执行，不每一步询问。授权范围内继续分析/小候选/定向验证/回退，跟到真实结果、保护及交接完成，不在队列启动后停下。打包/扩范围/人工操作仍有门槛。

## 当前源码及保护
生产未改，临时测试已恢复到150项。之前二叉堆/方向输出、Lane提前退出仍采纳；回读缓冲仍撤回。当前是density-runtime-20261008-01/registry.json的baseline；保护入口仍p3_density_runtime_20261008.py::project_check。

## 本轮实测与决定
- 第一轮14/14：不变量外提、宽≥3的均匀输出索引差解码、二者组合；组合仅改善1.3%–3.4%，未达事前六份均≥10%，全部淘汰，不跑整场。
- 第二轮14/14：二叉堆位置并排double距离键，拓扑/比较次序不变。仅改善2.6%–3.9%，还需512KiB/grid，未达同样事前门槛，淘汰。
- 六份完整方向位一致；独立未计时trace每份52,320 pop次序一致；键方案每个非空堆操作后检查键/距离/位置映射。计时类无trace分支；冻结基准与真实生产校准均在±3%内。
- 旧分段记录同队相邻更新仅4/85、2/85连目标数都相同：完全相同目标序列复用上限4.71%/2.35%，不能把数量相同当目标相同，不直接加结果缓存。

## 收窄后的下一项
停止低收益微优化堆叠。只读核查确认packages-lock已有间接Burst1.8.29/Collections2.6.5，MassEngine references=[]且unsafe=false，目前未检出Burst/Jobs使用。下一项可做隔离测试中的同算法同步编译执行可行性原型；包已解析不等于已加速。先证明实际Burst执行、严格浮点和完整出堆/输出位一致，计入拷贝/内存生命周期，再考虑生产；不直接改引擎asmdef/包版本、并行化、调度或刷新率。该原型尚未实现/测量，不宣称收益。

## 保护
任务均结束并收取，最终保护通过，无本项目Unity进程/锁。4份用户文件、37全部387文件、HEAD/暂存区未变；无打包/提交/推送，37不含源码优化。保护pelican SVG/meta与既有脏工作区，不git add .、不-nographics、不强关用户Editor、不改人数/48。P3/P9及人工/EXE待办不关闭。
上份NEXT_TASK原字节见heap-key-local-20261008-01/NEXT_TASK.before-core-screen.md。
'''
p.write_bytes(text.encode('utf-8'))
with (ROOT/'Docs/InteractionRefinement-20261007/P3-DEFERRED-ISSUES.md').open('ab') as f:f.write(('\n\n## 2026-10-08 核心局部候选筛选\n自主完成两轮14/14精确对拍/堆序验证；小算式组合仅1.3%–3.4%，堆键布局仅2.6%–3.9%，均未达事前局部门槛，未进生产/整场。已恢复测试，既有采纳保持。见[核心筛选报告](P3-CORE-LOCAL-SCREEN-20261008.md)及NEXT_TASK。P3仍开启、37未变。\n').encode('utf-8'))
assert not processes() and not(ROOT/'Temp/UnityLockfile').exists()
save(H/'publication.json',{'report':str(report.relative_to(ROOT)),'reportSha256':sha(report),'nextTaskSha256':sha(p),'productionUnchanged':True,'bothPhasesRestored':True,'noEligibleCandidate':True,'P3Closed':False,'noTasksInFlight':True})
print('PUBLISHED both screened/rejected phases; restored source; readonly compilation feasibility recorded.',flush=True)
