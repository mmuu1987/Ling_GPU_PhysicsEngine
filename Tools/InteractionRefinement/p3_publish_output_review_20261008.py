from p3_heap_output_20261008 import *
sys.stdout.reconfigure(encoding='utf-8')
a=LOG/'P3'/'output-20261008-01';b=LOG/'P3'/'heap-output-20261008-01'
d=json.loads((a/'decision.json').read_text(encoding='utf-8'));h=json.loads((b/'decision.json').read_text(encoding='utf-8'))
assert d['protectionPassed'] and h['protectionPassed'] and d['referenceRestored'] and h['referenceRestored']
assert not processes() and not(ROOT/'Temp/UnityLockfile').exists()
assert project_check()['passed']
report=ROOT/'Docs/InteractionRefinement-20261007/P3-OUTPUT-HEAP-FOLLOWUP-20261008.md'
assert not report.exists()
lines=['# P3方向输出与四叉堆候选：本轮已收尾，问题仍开启','',
'本轮已跟进至两个候选的结果收取、复核与回退，没有后台任务遗留。两项生产候选均未采纳，TerrainNavigationGrid恢复原字节；新测试和原始证据保留。没有修改刷新频率、分离48、人数、场景、GPU数据布局或37包。','',
'## 方向输出候选','',
'固定输入已定位：遍历约22.2ms、方向输出约8.3ms。方向候选在构造时确认每轴所有相邻float格中心的间距一致，仅在满足该条件时缓存9个归一化方向；否则保留旧输出计算。缓存72字节有效载荷加数组头，初始化O(宽+高)，不是每格缓存，也不是此前撤回的直线路径缓存。Dijkstra、边代价、目标过滤与停止半径不变。','',
'使用固定输入阶段保存的同一二进制图、原顺序目标、停止半径和现场预期方向；测试内恢复确切数组，不重建近似地表。原实现类冻结到测试程序集，只将不可见的internal Finite辅助函数复制成完全相同的私有谓词；寻路及数值计算原样保留。','',
'|输入|同轮原实现中位ms|方向候选中位ms|降低|','|---|---:|---:|---:|']
for r in d['after']:lines.append(f"|{r['sample']}|{r['referenceMedianMs']:.4f}|{r['currentMedianMs']:.4f}|{(1-r['ratio'])*100:.2f}%|")
lines += ['', '优化前与优化后各91/91定向EditMode（含14个新增展开用例）；每份输入3对预热与12对交错测量，完整float位对拍；另外覆盖真实构造的均匀/不均匀舍入、小数格长、大原点、单行/单列、重复/非法/空目标与停止半径。P8真实GPU局部四命令/拒绝/暂停/清理6/6。均0失败0跳过；不是全量或真人签收。', '', '## 整场ABBA：确有改善，但不满足本轮全部预设条件','', '|轮次|版本|普通中位ms|P99 ms|>50ms/15秒|>33.33ms/15秒|','|---|---|---:|---:|---:|---:|']
for i,r in enumerate(d['rendered']):lines.append(f"|{i+1}|{['原','方向候选','方向候选','原'][i]}|{r['medianMs']:.2f}|{r['p99Ms']:.2f}|{r['over50ms']}|{r['over33ms']}|")
pairs=[(d['rendered'][0],d['rendered'][1]),(d['rendered'][3],d['rendered'][2])]
gains=[100*(1-y['p99Ms']/x['p99Ms']) for x,y in pairs]
lines += ['',f'两组P99降低{gains[0]:.2f}% / {gains[1]:.2f}%。预设每组至少10%，第一组未达标；>50ms分别85→30、85→5，但>33.33ms仍每窗85次。不能把跨过50ms阈值的次数降低当作周期性卡顿消失。遵循预设规则恢复原实现，不改调查线、删差轮或否认已测得的局部收益。', '', '固定Green、2048、48、1920×1080、GTX1060 3GB/D3D11，相机/限帧设置一致；35秒预热、15秒采样。采样passed只说明采集条件，CPU重放收益不是EXE帧率；本轮没有真人输入。', '', '## 四叉堆追加候选：不进入整场','', '在方向候选基础上改成四叉索引堆，保持Before的距离/格索引全序、松弛和目标顺序。91/91测试与完整方向对拍通过，但六份固定输入中位26.61–26.89ms，慢于方向单独候选24.57–25.13ms。未满足相对方向候选再改善5%的预设局部门槛，立即回退，没有启动无收益候选的GPU或整场队列。', '', '决策JSON将调查门槛拒绝统一标为blocked_or_failed_reference_required；这里不是测试失败或代码异常，实际91项全过，是性能候选不采纳。','',
'## 测量与过程问题','',
'- 已知1MiB分配正对照仍报告0，确认此运行时的GC.GetAllocatedBytesForCurrentThread差量不可用。所有原始0值保留，但不能据此声称零分配；结果数组仍每次分配约512KiB有效载荷。',
'- 首次测试文件写入发生Windows换行转换，精确哈希检查在Unity启动前阻断。保留错误字节与失败日志，只在逐字确认完全由本轮换行扩展造成后改为write_bytes。',
'- 第二次冻结原实现跨测试程序集引用internal TerrainSurface.Finite，编译失败；只修测试中的同义私有谓词，没有为夹具更改生产可见性。编译失败遗留的0字节锁经本轮owned PID/时窗、无在途进程、Win32独占打开确认后移入证据，不删除未知用户锁。',
'- 所有失败、修复前后测试字节及哈希登记保留；未把失败算通过。','',
'## 当前工程与接续','',
'生产源码恢复reference，SHA256 '+sha(ROOT/NAV_PATH)+'。新增14项输出护栏及冻结原实现保存在原TerrainNavigationGridTests.cs，旧42项Cardinal测试及meta不动。后续保护基线使用Tools/InteractionRefinement/p3_output_20261008.py的project_check及output-20261008-01/registry.json，不能再直接用不认识新增测试内容的旧Cardinal保护函数。','',
'最终源码/测试登记检查、4份用户文件、37包387文件、HEAD/暂存区及无项目锁检查通过。没有新EXE、stage、commit或push。','',
'P3-PERF-01仍开启。下一次应进一步区分二叉堆操作与邻居松弛的成本或研究同义计算优化；不把整个22ms归给堆，四叉堆已实测不优，不重复该候选。方向输出的小表有真实局部收益，可作为有依据的后续组合候选，但当前不留在生产，不自动重用已撤回的Cardinal缓存。','',
'证据：Logs/InteractionRefinement-20261007/P3/output-20261008-01/decision.json、scope.json、registry.json；heap-output-20261008-01/decision.json、scope.json。原始固定重放报告和XML在P8/p3-output-before-03、p3-output-after-01、p3-output-scoped-01、p3-heap-output-after-01；新整场CSV在P3/p3-output-abba-1至4。']
report.write_bytes(('\n'.join(lines)+'\n').encode('utf-8'))
nextp=ROOT/'NEXT_TASK.md';backup=a/'NEXT_TASK.before-output-review.md';assert not backup.exists();backup.write_bytes(nextp.read_bytes())
nexttext='''# 接力：P3两项新候选已验证并撤回，生产原实现保持（2026-10-08）

权威记录：Docs/InteractionRefinement-20261007/P3-OUTPUT-HEAP-FOLLOWUP-20261008.md。

- 固定输入阶段已完成：六份图/目标/停止半径及逐位预期方向在P3/fixed-input-20261008-01-capture。遍历约22.2ms，方向输出约8.3ms。不要重抓同样战局或重跑已完成队列。
- 方向均匀间距九向量表：六份输入CreateFlowField约31.4–31.9→24.6–25.1ms，完整输出精确相同。前后各91/91定向EditMode（含14新用例）和P8 GPU局部指令6/6通过。
- ABBA：原/候选/候选/原 P99为56.44/51.34/48.71/55.99ms，>50ms为85/30/5/85，>33.33ms均85。第一组P99改善约9.04%，未达到预设每组10%，候选按规则撤回，不等于无局部收益。
- 追加四叉堆+方向表：91/91通过，但固定输入26.6–26.9ms反而慢于方向单独候选。局部门槛拒绝后未进行GPU/整场，已撤回。
- 生产TerrainNavigationGrid.cs恢复本轮原字节；不留任何新生产优化。14项新护栏及冻结原实现保存在TerrainNavigationGridTests.cs；旧42项Cardinal测试/meta保留。
- 当前保护入口：Tools/InteractionRefinement/p3_output_20261008.py的project_check，登记Logs/InteractionRefinement-20261007/P3/output-20261008-01/registry.json。原Cardinal检查器不认识本轮修改后的测试文件，勿误判未知改动再覆盖。
- 1MiB分配正对照仍返回0：分配计数API本轮不可用，不能声称零分配。结果数组仍有约512KiB有效载荷。GC分配不等于暂停。
- 初始换行/测试internal引用两次失败均已记录并修正，编译失败自有锁已按PID/时窗/独占打开确认后保留归档。不要重跑恢复脚本。
- 所有本轮任务已结束并收取结果，最终无本项目进程或锁；4份用户文件、37包387文件、HEAD/暂存区及工程保护通过。没有出包/提交/推送。

下一步：继续区分二叉堆操作与邻居松弛成本，或验证其它保持完整输出的局部优化。四叉堆已实测不优，不重复；方向输出有真实收益但尚未采纳，不为过线调整人数/48/刷新率，也不直接重用早前撤回的Cardinal候选。P3仍未解决，真人/EXE事项独立，不因旧索引重做P9。

详细交接备份保留在output-20261008-01/NEXT_TASK.before-output-review.md。保留pelican SVG/meta、全部既有脏工作区；不使用git add .，不强关用户Editor、不使用-nographics。
'''
nextp.write_bytes(nexttext.encode('utf-8'))
print('Published review and handoff; all owned queues ended; production reference retained',flush=True)
