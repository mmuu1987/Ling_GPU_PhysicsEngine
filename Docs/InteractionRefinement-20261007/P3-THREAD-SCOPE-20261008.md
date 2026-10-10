# P3：排除错误的主线程等待读数，修正跨进程时钟对齐

## 结论
完成线程范围诊断和一次明确的时钟fixture修正验证，两次临时源码均完整恢复。此前Semaphore约12ms不是主线程等待；不再沿这条错误线索改引擎。低频系统/本Unity进程CPU现已能用同一原生QPC时钟与测量窗口对齐。本轮只验证观测，不是性能校准/正式验收重试，也没有启用Burst。

## 线程范围：参考API与本机实测
参考源码： https://raw.githubusercontent.com/Unity-Technologies/UnityCsReference/master/Runtime/Profiler/ScriptBindings/ProfilerRecorder.bindings.cs 。该参考分支不等同于本机版本；实际用Unity6000.3.14f1编译运行验证：DefaultValue=24，CollectOnlyOnCurrentThread=4，主线程回调启动两组独立recorder，一组默认，另一组显式Default|CollectOnlyOnCurrentThread。
参考Default=WrapAroundWhenCapacityReached|SumAllSamplesInFrame，并未启用当前线程过滤。实际同一窗口同时记录的结果如下，不是跨轮减去两个中位数。

|诊断|默认Semaphore中位ms|限定主线程中位ms|主线程最大ms|默认sample Count中位|主线程Count中位|
|---|---:|---:|---:|---:|---:|
|首次线程范围|12.5869|0.0002|0.0015|86|1|
|原生时钟修正版|12.4029|0.0002|0.0005|86.0|1.0|

足以否定“主线程每帧在Semaphore等待约12ms”的解释。只能排除这个具体marker解释，不能据此断言主线程没有其他等待。Present/TargetFPS/EditorLoop仍全零，即便Valid也不表示真实环节零成本；最新样本仍可能滞后。

## 发现并修复了诊断工具错误
首次原生OS QPC与Mono Stopwatch频率均10000000，却分别是系统计时起点和另一相对起点。对齐分析断言没有足够重叠区间并exit1，阻止了错误CPU窗口结论。这个失败是fixture时间域假设错误，不是产品缺陷；首次线程范围结果仍有效，首次CPU/帧关联无效且不使用。
未靠调整偏移猜测起点，也未仅凭频率相等继续。修正版observer和旁路采样都显式使用Windows QueryPerformanceCounter/QueryPerformanceFrequency，临时stage记录仍使用Stopwatch，只按frameID关联，禁止两种时钟直接相减。修正后同一15秒窗口包含14个完整1秒OS区间，验证时间域实际重叠。

## 低频CPU结果（仅本次窗口）
GetSystemTimes与GetProcessTimes，每秒一次，只查询本次拥有的Unity PID及系统汇总，不枚举其他进程、不采集进程名称，不调优先级/电源/驱动或结束用户进程。机器8逻辑CPU。

|指标|中位|最小|最大|
|---|---:|---:|---:|
|系统总CPU忙碌率%|29.9414|27.3438|33.2692|
|Unity占系统CPU容量%|23.6538|20.3125|25.0000|
|Unity等效忙碌逻辑核数|1.8947|1.6172|2.0215|
|单次计数器读取ms|0.1315|0.0744|0.4615|

系统总量没有接近饱和，但不能排除单核忙、频率/调度变化或GPU争用。Unity进程CPU包括多线程，不能当成主线程CPU；其余系统忙碌也不能直接归为某个外部程序。计数器API读取成本不是整个采样器的开销证明，未作优化收益或无扰动宣称。未采GPU时钟/利用率、逐核CPU或主线程CPU时间，不能用本次记录反推之前Burst B1的历史波动原因。

## 下一步取舍
已解决的是观测假设，不是卡顿本身。停止围绕全线程Semaphore数字尝试优化，也不以“总CPU不满”断言CPU求解无瓶颈。下一项更有针对性的证据是：仅在导航求解边界记录主线程CPU时间与墙钟，跨85次求解累计比较，并报告GetThreadTimes等API的计时粒度/调用成本；避免把采样器本身铺到每格/每单位或采整个系统进程列表。若实施，仍为临时诊断且先明确校准边界，不得把未校准中位数当正式收益。此项尚未执行。
Burst继续作为用户明确指定的可行后备、未启用；不认为其他方向已全部耗尽，不改写原正式门槛失败。若最终采用，需明确代价/剩余验证，不意味着自动打包或完成AOT/冷启动/人工验收。

## 保护与证据
两次诊断exit0且Runtime/observer原字节恢复，第一次纯分析exit1的原因和修正已披露。当前仍density-runtime baseline、原150项测试及早前采纳的二叉堆/输出/Lane优化。两次全检9693+30、4用户文件、37的387文件、HEAD/index均通过，无项目Unity/锁；无打包、stage、commit、push，P3/P9开放。
Logs/InteractionRefinement-20261007/P3/thread-scope-20261008-01：第一次summary，线程范围有效、CPU窗口关联弃用；thread-scope-clock-20261008-01：summary、analysis、aligned-os-intervals。原始p3-thread-scope-clock-1含wait-probe.csv、recorder-options.txt、os-counters.csv/status、marker inventory和stages。
