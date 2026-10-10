from p3_density_runtime_20261008 import *
sys.stdout.reconfigure(encoding='utf-8')
O=LOG/'P3'/'wait-probe-20261008-01'
d=json.loads((O/'summary.json').read_text(encoding='utf-8'))
assert d['diagnosticRestored'] and d['protectionPassed'] and not d['controlGatePassed']
assert project_check(full=True)['passed'] and not processes() and not(ROOT/'Temp/UnityLockfile').exists()
report=ROOT/'Docs/InteractionRefinement-20261007/P3-WAIT-PROBE-20261008.md';assert not report.exists()
lines=['# P3 计算/等待粗粒度探针：校准未通过，已恢复','', '## 结论','完成普通/探针/探针/普通四轮，不含Burst，不含优化候选。第二配对P99差异超过事前±5%，校准失败；数据仅保留为描述性观测，不作为已校准的阶段成本或优化归因。不删较差窗口、不原样重跑直到通过。已恢复Runtime与observer原字节。','', '|轮次|中位数ms|P99ms|>33.33ms|>50ms|','|---|---:|---:|---:|---:|']
for label,r in zip(['A1普通','B1探针','B2探针','A2普通'],d['rendered']):lines.append(f"|{label}|{r['medianMs']:.3f}|{r['p99Ms']:.3f}|{r['over33ms']}|{r['over50ms']}|")
for label,x,y in [('第一配对',d['rendered'][0],d['rendered'][1]),('第二配对',d['rendered'][3],d['rendered'][2])]:lines.append(f"\n{label}：P99偏差{100*(y['p99Ms']/x['p99Ms']-1):.2f}%，中位数偏差{100*(y['medianMs']/x['medianMs']-1):.2f}%。规则为P99±5%、中位±10%，两个配对均需满足。")
lines+=['','## 实际执行与观测边界','同一Green/2048/48/1920×1080/GTX1060/D3D11，35秒预热+15秒采样。临时粗粒度Stopwatch记录Tick、回读准备、求解、Lane、上传；独立frameID/clock记录GC.CollectionCount增量和五个实际枚举到的Profiler marker。预分配32768项缓冲，窗口结束后落盘，没有逐格计时/deep profile。','两轮探针均85个>33.33ms间隔，均与导航上传处于同一frameID；每帧一次上传。这是本次直接观测到的共现，不证明排他根因。观测到的求解中位约24.0/24.3ms、Lane约5.17/5.10ms、回读准备约2.78/2.76ms、上传API约0.057ms；由于校准失败，不将其当作可信的跨变体收益分解，且这些中位数不可相加。','两轮GC.CollectionCount(0/1/2)分别各增加1，不能加总为3次独立回收；GC.Collect最新样本每窗2条为正，可能包含滞后/重复观察，最大约4.25/4.45ms。计数不支持直接把85次重复长帧都说成GC，但不排除GC对个别帧有影响。','五个marker均枚举成功、单位为TimeNanoseconds，recorder.Valid为true。然而Present/TargetFPS/EditorLoop所有读取为零，不能解释成这些环节确定零耗时。Semaphore最新样本中位约12.46/12.34ms，高于普通帧间隔约5.3ms；其统计范围/线程聚合及样本滞后未经验证，不可直接作为主线程等待，更不能与GPU或Tick相减。','原GPU仍是latest timing，不新增完成帧ID关联；不作逐帧GPU因果归属。本轮不能解释之前Burst B1时段抬升的原因，因为没有启用Burst且测量时间不同。','', '## 下一项：先解决观测与对照可靠性','停止根据不可靠阶段归因改引擎，也不直接重跑此校准。首先核对ProfilerRecorder默认线程/聚合选项与零样本含义，若明确缺陷则修正为限定线程或分别标注线程；单独的OS/进程CPU及可用GPU时钟旁路记录必须低频、统一单调时间、无用户进程终止/设置修改。需要实际API确认和预注册的新观测设计，才开新队列。当前这些后续工作尚未执行。','主要问题已从“缺少阶段数字”收窄为“对照窗口不稳定且等待计数器范围不清”。不能把后来普通对照更慢自动归咎环境，更不能降低门槛或把先前正式Burst失败改判。','Burst仍为用户指定的可行后备、未启用；候选快照和fallback-reserve.json保留。其他路线无解时可按用户指示推进后备，但冷启动/AOT/EXE和权衡验证仍需处理；本轮未判断所有其他路线已耗尽。','', '## 恢复与保护','执行器exit0表示诊断和清理完成，不表示校准通过。源码/observer原字节恢复、150项原套件保持；全检9693+30、4份用户文件、37的387文件、HEAD/index均通过。无本项目Unity/锁，无打包/提交/推送；P3/P9与人工验收仍开放。当前保护入口仍p3_density_runtime_20261008.py::project_check，density-runtime baseline。','证据：Logs/InteractionRefinement-20261007/P3/wait-probe-20261008-01/{scope,registry,summary}.json；p3-wait-probe-1..4；两探针目录另含stages.csv、aligned-stages.json、wait-probe.csv、wait-probe-status.tsv、marker-inventory.tsv。']
report.write_bytes(('\n'.join(lines)+'\n').encode('utf-8'))
p=ROOT/'NEXT_TASK.md';backup=O/'NEXT_TASK.before-wait-probe.md';assert not backup.exists();backup.write_bytes(p.read_bytes())
text='''# 最新接力：粗粒度等待/计算观测已完成，校准失败并恢复（2026-10-08）

## 用户方向与当前状态
自主推进并监督到真实结果，不每一步问。Burst是用户明确指定的可行后备，不是废弃；其他方向实在无解时使用。当前未启用，候选快照/测试和fallback-reserve.json保持；正式历史失败不改判，未认定所有其他路线已耗尽。包装仍有独立门槛。

## 本轮真实结果
四轮ordinary/probed/probed/ordinary：P99=38.94/38.90/38.76/41.67ms，中位=5.51/5.41/5.35/5.63ms，>33.33ms各85，>50ms=0/0/1/9。第二配对P99约-6.98%，越过事前±5%；校准失败，不原样重跑，不删窗。两轮85长帧均同frameID导航上传；粗略阶段24ms求解/5.1msLane/2.8ms回读是未校准描述值，不能作可靠收益分解。

GC各代count每窗各+1，不能加总为3次；不能把85次长帧直接归因GC。5个Profiler marker均valid，但Present/TargetFPS/EditorLoop读数全零，Semaphore中位12.3–12.5ms超过普通帧5.3ms，线程聚合/样本范围尚未验证，不能当作主线程等待时间。GPU仍latest样本不可逐帧相减。没有运行Burst，不能宣称解释了旧B1波动。

## 下一项收窄
先核对ProfilerRecorder默认线程/聚合选项及零样本含义；必要时明确限定线程或分别报告。之后考虑统一时钟的低频OS/本进程CPU和可用GPU时钟旁路观测，先确认API、范围和开销；不能修改用户设置/终止用户进程。不是直接重跑验收或进一步凭猜测改引擎。这些后续工作尚未执行。

## 保护/证据
Runtime与observer原字节恢复；仍150项、density-runtime baseline，保护入口p3_density_runtime_20261008.py::project_check。全检9693+30、4用户文件、37的387文件、HEAD/index均通过，无本项目Unity或锁；无打包/stage/commit/push。pelican SVG/meta不动，人数/48不变，不-nographics，不强关Editor，P3/P9/人工保持开放。
报告：Docs/InteractionRefinement-20261007/P3-WAIT-PROBE-20261008.md。
证据：Logs/InteractionRefinement-20261007/P3/wait-probe-20261008-01/summary.json，status=diagnosis_complete_control_disturbance_disclosed，controlGatePassed=false，diagnosticRestored/protectionPassed=true。执行器bf4eeda1已exit0，无任务在途；不要重跑一次性脚本。
上份交接原字节备份位于wait-probe-20261008-01/NEXT_TASK.before-wait-probe.md。Burst报告/后备标记见该旧接力与P3-BURST-RESERVE-AND-VARIANCE-20261008.md。
'''
p.write_bytes(text.encode('utf-8'))
with (ROOT/'Docs/InteractionRefinement-20261007/P3-DEFERRED-ISSUES.md').open('ab') as f:f.write(('\n\n## 2026-10-08 等待/计算探针校准\n完成四轮并恢复；第二配对P99约-6.98%超事前±5%，不能作已校准成本分解。等待计数器存在零样本/范围未明限制，GC计数不足以直接解释每窗85次重复长帧。详见[P3等待探针报告](P3-WAIT-PROBE-20261008.md)。Burst仍是可行后备未启用，未打包，P3仍开放。\n').encode('utf-8'))
assert project_check(full=True)['passed'] and user_check()['passed'] and not processes() and not(ROOT/'Temp/UnityLockfile').exists()
save(O/'publication.json',{'report':str(report.relative_to(ROOT)),'reportSha256':sha(report),'nextTaskSha256':sha(p),'calibrated':False,'sourceRestored':True,'burstStillReserved':True,'noTasksInFlight':True,'P3Closed':False})
print('PUBLISHED uncalibrated diagnostic with counter limitations; exact source restored; Burst reserve retained.',flush=True)
