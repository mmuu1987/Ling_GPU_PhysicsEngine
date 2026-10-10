from p3_density_runtime_20261008 import *
sys.stdout.reconfigure(encoding='utf-8')
O=LOG/'P3'/'burst-variance-20261008-01';D=LOG/'P3'/'burst-production-20261008-01'
a=json.loads((O/'analysis.json').read_text(encoding='utf-8'));done=json.loads((O/'completion.json').read_text(encoding='utf-8'))
assert done['completed'] and done['project']['passed'] and project_check(full=True)['passed']
assert not processes() and not(ROOT/'Temp/UnityLockfile').exists()
p=ROOT/'NEXT_TASK.md';backup=O/'NEXT_TASK.before-variance.md';assert not backup.exists();backup.write_bytes(p.read_bytes())
report=ROOT/'Docs/InteractionRefinement-20261007/P3-BURST-RESERVE-AND-VARIANCE-20261008.md';assert not report.exists()
text='''# P3：Burst保留为后备；时段差异已定位，原因尚未归属

## 决定
用户明确要求：Burst既然可行就标记保留，其他方向实在无解时采用。现标记为 **可行后备 / 未启用**，不是技术路线废弃；历史正式门槛失败仍保留，不改写成通过。候选及测试原字节快照、registry和完整验收证据均在burst-production-20261008-01，机器可读标记为fallback-reserve.json。未来条件满足时可按此后备推进，需明确接受哪些权衡并针对最终状态验证；不代表已经授权打包或完成冷启动/AOT/EXE验收。

## 新方向本轮实际完成
复用临时桥接与正式验收共8个窗口的逐帧记录、原有Main Thread/GPU/GC计数器及Editor日志，做3秒固定桶交叉核查。未启动Unity、未改生产、未重新跑验收，也未删掉较差时段改算成绩。

正式B1不是全窗持续同幅变慢：前6秒中位数约5.35–5.46ms，第6–9秒升至6.91ms，随后回落。该3秒桶包含9/16个>33.33ms帧；6–12秒合计13/16。但这只定位集中时段，不能把它们排除。

|窗口及相对采样时段|回调间隔中位ms|Main Thread中位ms|GPU记录中位ms|GC分配中位bytes/样本|
|---|---:|---:|---:|---:|
'''
for tag,indices in [('p3-burst-production-abba-2',[0,1,2,3,4]),('p3-burst-production-abba-4',[0,2])]:
 r=next(x for x in a if x['tag']==tag)
 for i in indices:
  b=r['buckets'][i];label=('B1' if tag.endswith('-2') else 'A2')+f" {i*3}–{(i+1)*3}s"
  text+=f"|{label}|{b['intervalMs']['median']:.3f}|{b['mainThreadMs']['median']:.3f}|{b['gpuTimingMs']['median']:.3f}|{b['gcBytes']['median']:.0f}|\n"
text+='''
关键交叉证据：恢复managed基线后的A2前3秒也有间隔6.58ms、Main Thread6.59ms、GPU约5.90ms的抬升，后半窗回到约5.3–5.4ms。因此“只有Burst版本普通帧才慢”不受这组记录支持；但也不能据此证明环境是根因，更不能保证Burst无回归。

## 已排除的过度推断
- GPU字段显示同一时段抬升，只是桶级共现，不是逐帧因果：源代码使用GetLatestTimings；备用路径取latest completed profiler frame minus4，记录没有完整对应帧ID。报告中的source字符串也不是每行来源标记。不能拿GPU和主线程中位数相减当求解CPU耗时。
- Render Thread记录没有正的有效时长，不能把0/缺测解释为渲染线程不忙。
- GC分配中位仅从14837到14925（+88字节），这不是GC回收耗时。现有数据没有collection次数/暂停marker，既不能归咎GC，也不能排除它。
- B1/A2日志中error命中均是开头的授权token不可用消息；并非已发现产品异常。compile命中包含版本字符串及程序集导入/重载，focus命中是后台线程/分配器配置，不是失焦记录。未取得与测量第6–9秒可对齐的事件时间，不能将这些命中次数当成根因。
- 正式历史成绩保持原样：第一配对失败、完整回滚；不以后验挑窗重判。

## 下一项精确诊断规格
新证据优先级从“再改求解器”改为“分清计算与等待”：先在临时观察器枚举实际可用Profiler marker，记录缺失而不是假零；按帧保存导航Tick/求解/Lane/回读/上传的粗粒度耗时、Gfx等待/Editor循环可用marker、GC.CollectionCount增量。统一单调时钟和frameID，GPU若不能可靠映射就只作时间桶上下文，不参与逐帧相减。
采样写入预分配缓冲，窗口后落盘，不开deep profile、不逐格计时；先用当前managed源码做探针开销校准（未探针/探针/探针/未探针，配对P99±5%、中位±10%），不通过则只报告扰动，不能拿阶段中位数解释收益。校准通过才决定是否值得对后备Burst作同样诊断；那也不是正式采纳重试。
这是下一项执行规格，**本轮没有声称已经实现或跑过这些新探针**。不改调度/刷新/人数/48，不开无依据优化，不重复已拒绝的小改动。

## 保护与索引
只读分析结束，当前仍density-runtime登记baseline与150项套件，早前二叉堆/输出及Lane改进仍保留。全源码保护检查和用户文件检查通过，无本项目Unity或锁；未打包/提交/修改37。P3/P9及人工门槛仍开放。
- 详细分析：Logs/InteractionRefinement-20261007/P3/burst-variance-20261008-01/{scope,analysis,completion}.json，以及8份*-log-hits.json。
- 后备标记：Logs/InteractionRefinement-20261007/P3/burst-production-20261008-01/fallback-reserve.json。
- 完整历史验收：[P3-BURST-QUALIFICATION-20261008.md](P3-BURST-QUALIFICATION-20261008.md)。
'''
report.write_bytes(text.encode('utf-8'))
old=p.read_text(encoding='utf-8')
prefix='''# 最新接力：Burst已列后备，差异集中时段已查明（2026-10-08）

用户新指示：继续其他方向，Burst可行要明确标记；若实在无其他办法则用Burst。现为“可行后备/未启用”，不是废弃路线，也不是现在自动采纳。快照和历史失败验收保留，fallback-reserve.json已登记；打包仍需单独处理。

本轮完成8窗只读计数器/日志交叉分析，未启动Unity。B1第6–9秒间隔中位6.91ms、Main Thread6.89ms、GPU记录6.14ms，集中了9/16个>33ms帧；前6秒约5.35–5.46ms。managed A2开头也出现6.58ms抬升，不能把普通帧慢直接归因Burst，但根因尚未确定。Render Thread无有效正时长；GC字节不等于GC停顿，日志无测量时段对齐，禁止甩锅环境/GC/编译或删窗重判。

报告：Docs/InteractionRefinement-20261007/P3-BURST-RESERVE-AND-VARIANCE-20261008.md。
下一项：按报告规格做实际marker可用性枚举及计算/等待/GC粗粒度诊断，先managed探针校准再考虑Burst；这项尚未实现/运行。不要直接重跑正式验收，不叠加低收益微优化。继续自主监督到结果，不每一步问。
当前源码仍原150项及density-runtime baseline；全检通过，无项目Unity/锁，未改37/打包/提交。原交接原字节见burst-variance-20261008-01/NEXT_TASK.before-variance.md。

---
以下保留历史验收与恢复说明，若“拒绝/关闭”被理解为永不使用，以上用户新指示优先：拒绝的是当次自动采纳，不是抹掉可行后备。

'''
p.write_bytes((prefix+old).encode('utf-8'))
for path in ['Docs/InteractionRefinement-20261007/P3-BURST-QUALIFICATION-20261008.md','Docs/InteractionRefinement-20261007/P3-DEFERRED-ISSUES.md']:
 with (ROOT/path).open('ab') as f:f.write(('\n\n## 2026-10-08 用户指定后备路线\nBurst明确标记为可行后备、暂未启用；其他方向无解时考虑采用。保留候选快照/测试证据，不改历史失败结论；后备启用不等于既往门槛通过或打包许可。新只读差异分析见[P3后备与时段报告](P3-BURST-RESERVE-AND-VARIANCE-20261008.md)，下一项是校准后的计算/等待归属诊断。\n').encode('utf-8'))
assert project_check(full=True)['passed'] and user_check()['passed'] and not processes() and not(ROOT/'Temp/UnityLockfile').exists()
save(O/'publication.json',{'report':str(report.relative_to(ROOT)),'reportSha256':sha(report),'nextTaskSha256':sha(p),'fallbackReserved':True,'sourceChanged':False,'noUnityStarted':True,'P3Closed':False})
print('PUBLISHED fallback reserve and eight-window variance findings; source protection passed.',flush=True)
