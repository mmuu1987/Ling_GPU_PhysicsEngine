# P3 Burst后备：后台编译机制独立试验通过，未接入游戏准备阶段

## 结论
已在空项目JIT缓存下观察到同一导航Job由托管执行切换为真实Burst执行，等待期间Editor更新持续推进。只把Job属性`CompileSynchronously=true`临时改为`false`，导航仍使用同步`IJob.Run`，没有Schedule或导航并行化。准备总墙钟8.83秒，但这次不再表现为单次9–12秒的同步Run。
**这是机制与定向正确性验证，不是加载UI流畅性验收。** 首次准备调用52.5ms、最大Editor更新间隔74.2ms，仍有短停顿；正式稳态ABBA失败未改判。所有临时源码和原缓存已经恢复，Burst仍为未启用后备。

## 公开机制及限制
实际安装Burst1.8.29：`Runtime/BurstCompileAttribute.cs:129–139`公开说明该属性默认false，决定首次调用立即编译或后台编译。`BurstCompilerOptions.cs:310–324,428,585`说明全局同步选项及强制同步参数仍可覆盖属性；本轮读取公共Options及命令行，强制同步时测试会拒绝，而非修改用户偏好。
只读取包实现以核实行为，没有调用内部编译器API、反射内部编译器、修改包或编辑EditorPrefs。反射仅用于现有项目测试Hydrate及内部工作区入口。测试中全局同步选项和强制同步参数均false，Burst开启。

## 独立实测
一次EditMode UnityTest（1/1通过），同一历史捕获sample0，严格浮点/算法/目标顺序保持，生产候选仅一个编译属性改变。使用IEnumerator让出Editor更新，约每100ms重试空目标Run；这只是测试驱动，不是新生产导航刷新周期。

|观测项|结果|
|准备墙钟（从首个空目标Run前起）|8830.611ms|
|空目标探测|76次；前75次managed-native，第76次真实Burst|
|首个/最慢空目标Run|52.5034ms|
|空目标Run中位 / 累计|12.8166 / 1021.7295ms|
|Editor update回调次数|26175|
|最长update间隔|74.201ms|
|update间隔超过33ms / 100ms|5 / 0|
|准备后的首个真实求解|22.2831ms|
|后续12次真实求解中位|21.8956ms|
|重建工作区后的真实求解|18.2139ms|
|工作区创建 / 释放 / 活跃|3 / 3 / 0|
|真实Burst调用 / managed-native调用|15 / 75|

空目标每次逐位验证全零；真实首次、12次复用及重建后输出均与捕获期望逐位一致。75次managed-native只发生在准备探测，不是75次完整真实目标求解。第一次尚未观察到Burst时，先完成同步Run、释放其工作区、再新建继续探测；最终计数平衡且无Native泄漏日志。这证明的是本试验路径的释放/重建，不是所有场景退出或编译错误恢复。

## 严格解释观测窗口
- Heartbeat是EditMode的EditorApplication.update回调，不是渲染帧，不可把26175当FPS。记录窗口从首个探测前到就绪后的下一次yield，包括探测/空输出对拍/一次中途释放重建，但不覆盖整个Editor启动或整个测试。
- 之前的磁盘读取/测试Hydrate约413.3ms、托管参考求解33.2ms、首次反射工厂29.9ms均在心跳窗口外；之后的真实求解/报告写盘也不在窗口内。因此74.2ms不是整个进入战场过程的最长帧，更不能声称全流程无卡顿。
- 总准备8.83秒仍然存在；累计探测约1.02秒主线程工作也不免费。首调用52.5ms和5个>33ms更新间隔意味着还需处理准备成本，不能直接宣传平滑加载。与旧同步结果不是成对稳态性能ABBA，不以此计算正式速度收益。
- 启动前项目JIT为0文件；测试入口/准备前0 DLL，准备后2 DLL、测试结束3 DLL；退出后402文件。结合执行证明的75次托管→Burst，排除“测试入口已经全就绪”的解释，但不把8.83秒全算作某个内核的纯编译CPU，不声称整机缓存全冷。
- 启动到测试入口约83.40秒包含C#/资产导入和测试框架，绝不是游戏加载耗时。Player/AOT及实际加载画面、键鼠响应没有测试。

## 为什么仍不能只改一个属性上线
保留生产候选Upload在首次LastRunUsedBurst=false时会Dispose并设置burstUnavailable，后续永久回到托管；它把“未编译完成”与“不可用”合并了。本试验直接操作准备工作区，没有走该Upload降级路径。因此只改属性并不能得到生产可用的后台准备机制。
后续最小方向是先独立验证准备状态/后备策略，区分等待、就绪、不可用和已释放；未就绪时正常求解保持原托管路径，不偷偷触发冷同步编译。强制同步/禁用/超时应停留托管，不覆盖偏好；必须防止旧工作区被世界重建后继续使用。编译完成与实例快照有效性仍需分开。
先处理本试验暴露的全尺寸空跑成本，并验证禁用/强制同步/超时/重建策略，再考虑接入已定位的加载边界；不改变导航调度/刷新/并行或新增取消UI，不扩张加载系统。真实场景首次命令、固定目标、直接加载、重开/布阵/失败返回仍是后续集成门槛，而不是本次已经通过。

## 恢复、后备及证据
原6个源码/asmdef按字节恢复，原150项测试保留；原JIT目录采用整目录封存后移回，文件哈希/目录树一致，生成缓存另存generated-JIT，其他BurstCache项不变。保护9693+30、4用户文件、37全部387文件、包、HEAD/index通过，无本项目Unity或锁。无出包/stage/commit/push，无用户偏好/包改动，separation48和已采纳优化保持。
后备记录新增独立异步编译机制已观测，但currentlyEnabled=false、formalGatePassed=false、prewarmIntegrated=false、playerAotStartupVerified=false。不关闭P3/P9或人工门槛。
证据：`Logs/InteractionRefinement-20261007/P3/burst-async-prepare-20261008-01/`的scope/registry/summary、compact-analysis、original/candidate、cache清单/generated-JIT、public-api-source及publication；原始测试在P8/p3-burst-async-prepare-cold-01。执行38d58415 exit0，分析147e935e exit0。一次性执行器勿重跑。
