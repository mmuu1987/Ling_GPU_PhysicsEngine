from p3_density_runtime_20261008 import *
from p3_burst_startup_20261008 import tree
import ast
sys.stdout.reconfigure(encoding='utf-8')
W=LOG/'P3'/'burst-preparation-gate-20261008-01';P=LOG/'P3'/'burst-production-20261008-01';A=LOG/'P3'/'burst-async-prepare-20261008-01'
s=json.loads((W/'summary.json').read_text(encoding='utf-8'));q=json.loads((W/'compact-analysis.json').read_text(encoding='utf-8'))
assert s['protectionPassed'] and s['sourceRestored'] and s['cacheRestored'] and s['otherCacheUnchanged'] and q['testsPassed']==14 and q['observedManagedToBurst'] and q['probePreservedOutput'] and q['fallbackChecks']==1 and q['recreatedGateStartedWaiting']
assert project_check(full=True)['passed'] and user_check()['passed'] and not processes() and not(ROOT/'Temp/UnityLockfile').exists()
# Prove that stripping exactly the two bounded additions recovers the previous async candidate byte-for-byte.
source=ast.parse((ROOT/'Tools/InteractionRefinement/p3_burst_preparation_gate_20261008.py').read_text(encoding='utf-8'))
probe=next(ast.literal_eval(n.value) for n in ast.walk(source) if isinstance(n,ast.Assign) and any(isinstance(t,ast.Name) and t.id=='probe' for t in n.targets))
gate=(ROOT/'Tools/InteractionRefinement/P3PreparationGate-20261008.cs.txt').read_bytes()
now=(W/'candidate'/RUNTIME_PATH).read_bytes();old=(A/'candidate'/RUNTIME_PATH).read_bytes()
assert now.endswith(gate) and now.count(probe)==1 and now[:-len(gate)].replace(probe,b'')==old
assert (W/'candidate'/NAV_PATH).read_bytes()==(A/'candidate'/NAV_PATH).read_bytes()
cache=ROOT/'Library/BurstCache/JIT';cacheBefore=tree(cache);assert cacheBefore==json.loads((W/'cache-before.json').read_text(encoding='utf-8'))
other={p.name:tree(p) if p.is_dir() else {'sha256':sha(p)} for p in cache.parent.iterdir() if p!=cache}
head=subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip();index=sha(ROOT/'.git/index')
report='''# P3 Burst后备：最小准备策略与轻量同内核探测通过，未接真实流程

## 结论
14/14定向测试通过：13个注入式准备策略用例，以及1个真实空项目JIT的UnityTest。准备探测不再反复初始化整张网格/分配并复制方向输出；同一内核从66次托管探测转为第67次真实Burst，首次真实求解及重建后结果逐位一致。
本次探测中位0.0277ms、累计14.9173ms、最长12.83ms。仍需约6.70秒等到就绪，Editor更新最大间隔75.04ms；**不声称实际加载已经流畅，不把两次诊断差异当正式性能采纳证据。** 所有临时源码/原缓存恢复，Burst仍未启用。

## 改了什么、没改什么
- 相对上轮异步编译候选，仅临时增加Editor限定的ProbeCompilation及独立P3PreparationGate；主流程Upload/场景/Manager没有接线。最终源码没有保留这些改动。
- Probe复制同一个TerrainNavigationSolveJob结构体，副本设CellCount=0、targetCount=0、hasTargets=false后仍同步IJob.Run。进入相同完整内核，写执行证明后返回，不运行网格初始化；不是另写一个空Job来冒充目标内核就绪，也没有Schedule/导航并行。
- 工作区仍按完整网格分配。本轮减少的是重复准备调用的工作量，**没有减少工作区常驻内存或消除首次构造/编译服务成本**。同一工作区的真实网格尺寸未变；准备前填充输出哨兵，托管/原生探测后以及真实求解后的再次探测均验证输出未被改写。
- 发布器逐字节核对：移除这两个新增片段即恢复上一轮异步候选Runtime，Nav也完全相同。求解算法、严格浮点、目标顺序、刷新/调度、人数/separation48均未改变。

## 准备策略验证范围
独立原型区分Waiting、Ready、Unavailable、Faulted、Disposed，绑定实例所有者引用，没有全局ready捷径。
1. 正常求解只查询CanUseBurst，不隐式启动探测；Waiting选择原托管路径。Advance才允许按测试原型的100ms间隔探测，Ready后不重复探测。
2. 禁用或强制同步在探测前拒绝；已Ready后失去许可也释放并回退。本实例Unavailable后不自动反复尝试，重新准备需要新实例。
3. 达到等待deadline先拒绝，不再多探测一次；已Ready不因等待期限过去而过期。
4. 所有者替换使旧ready失效；新实例从Waiting开始。释放幂等，释放后不再调用后端。
5. 注入的明确“后端不可用”标记可降级；意外异常先释放再原样抛出，不在首次错误处一概吞成成功回退；NaN/倒退时钟不启动探测。
这些禁用/强制同步/超时/异常用例使用注入布尔值、时钟和假后端，**没有实际切换全局Burst选项，没有制造真实编译器故障**。自定义不可用异常只是策略边界，不是已确定的Burst异常映射。真实生产调用的选项读取、错误上报和生命周期连接仍未验证。

## 真实空缓存试验
|观测|本次结果|
|等待就绪墙钟|6699.9124ms|
|探测次数 / 托管→Burst|67 / 前66次托管，第67次原生|
|首次及最长探测 / 中位 / 累计|12.8300 / 0.0277 / 14.9173ms|
|Editor update次数 / 最大间隔|21729 / 75.0358ms|
|update间隔>33ms / >100ms|1 / 0|
|真实首次求解 / 12次复用中位|21.1250 / 19.96735ms|
|工作区重建后真实求解|21.9605ms|
|工作区创建 / 释放 / 活跃|3 / 3 / 0|
|真实Burst / managed-native调用|17 / 66|

实际Waiting时先确认不允许Burst，再调用原托管求解对拍一次；随后释放尚在准备的工作区，重新创建并继续等待。就绪后真实输出、12次复用、准备哨兵及重建后输出均通过。新实例即使编译缓存已经就绪，也先确认门禁为Waiting，再用真实探测确认后求解。
启动前项目JIT为0文件；测试入口/探测前0 DLL，探测后2 DLL；最终生成缓存402文件另行归档。执行证明支持确实经历未就绪状态，不是把已有就绪缓存当冷准备。

## 不能扩大解释的地方
上轮完整网格空目标探测中位12.8166ms、累计1021.7295ms；本轮结构上去掉了那部分重复工作，观测负担明显更小，但两个诊断不是校准后的ABBA，探测次数和测试工作也不同。不能把8.83→6.70秒说成稳定编译加速，不能把探测差值当战斗P99收益。
Heartbeat只是EditMode的EditorApplication.update，不是渲染帧/FPS。窗口包含哨兵对拍、一次真实托管回退及一次释放重建；不单独归因75ms最长间隔。Hydrate327.35ms、参考求解36.25ms、首次工厂10.20ms在窗口外，后续真实求解/文件写盘也在窗口外。启动到测试入口89.31秒含导入/测试框架，不是玩家加载耗时。未测试完整场景、输入响应或Player/AOT。

## 下一步的最小范围
在临时后备候选中把同一策略连接到真实Runtime.Upload的托管/Burst选择，定向验证真实调用路径，而非仅继续测试假的后端。未就绪必须走原托管，不再把“尚未编译”当永久不可用；编译许可/实例有效性必须每次使用前核验，准备仍与真实目标求解分离。
先验证该临时连接的固定目标、动态目标、禁用及Runtime重建/Dispose，不启用为默认，也不同时改加载UI/新加取消功能。过了真实Runtime路径再验证已定位的场景边界。意外错误的游戏层处置不能靠原型自动推断；稳态ABBA原失败仍有效。

## 恢复与证据
原6个源码/asmdef、原150项测试、原JIT哈希/目录树均恢复，其他缓存未变。9693+30、用户4文件、37全部387、包、HEAD/index保护通过；无本项目Unity或锁，无打包/stage/commit/push，不改用户偏好或包，不动pelican/svg/meta，已采纳优化保持。P3/P9、人工及Player门槛不关闭。
报告状态是preparationGatePrototypeVerified=true、preparationGateProductionIntegrated=false；Burst目前未启用、预热未接游戏、正式性能门槛未通过。
证据：Logs/InteractionRefinement-20261007/P3/burst-preparation-gate-20261008-01（scope/registry/original/candidate/summary/compact-analysis/cache/protection/publication）；原始14项在P8/p3-burst-preparation-gate-cold-01。执行f17ee732 exit0，分析e3104ad9 exit0。一次性执行器勿重跑。
'''
p=ROOT/'Docs/InteractionRefinement-20261007/P3-BURST-PREPARATION-GATE-20261008.md';assert not p.exists();p.write_bytes(report.encode('utf-8'))
rp=P/'fallback-reserve.json';backup=W/'fallback-reserve.before-gate.json';assert not backup.exists();backup.write_bytes(rp.read_bytes());r=json.loads(rp.read_text(encoding='utf-8'))
r.update(preparationGatePrototypeVerified=True,preparationGateTestsPassed=14,preparationGatePolicyTestsInjected=13,lightweightSameJobProbeVerified=True,preparationGateReport=str(p.relative_to(ROOT)),preparationGateProductionIntegrated=False,lightweightProbeMedianMs=q['pollMedianMs'],lightweightProbeMaxMs=q['maxPollMs'],lightweightProbeTotalMs=q['pollTotalMs'],currentlyEnabled=False,formalGatePassed=False,prewarmIntegrated=False,playerAotStartupVerified=False)
save(rp,r)
nextp=ROOT/'NEXT_TASK.md';backup=W/'NEXT_TASK.before-gate.md';assert not backup.exists();backup.write_bytes(nextp.read_bytes())
nextp.write_bytes(('''# 最新接力：最小准备策略/轻量同内核探测14项通过，未接真实Runtime（2026-10-08）

## 最新结果
执行f17ee732 exit0，分析e3104ad9 exit0；13个注入策略用例+1个真实空项目JIT UnityTest，共14/14。临时异步候选新增Editor-only ProbeCompilation和P3PreparationGate，最终已恢复源码。Probe使用原TerrainNavigationSolveJob副本，CellCount/targetCount=0、hasTargets=false，仍同步IJob.Run；不是另写空Job。仅写执行证明，跳过整网格初始化与结果分配/复制，真实job尺寸与输出哨兵未变；完整工作区仍分配，没减常驻内存。
67探测中66托管→第67Burst，等待6699.9124ms；最大12.83ms、中位0.0277ms、累计14.9173ms。Editor update21729次，最长75.0358ms，>33ms一次、>100ms零次；不是渲染FPS/全加载时间。窗口含哨兵对拍、一次原托管回退及释放重建，Hydrate327.35ms/工厂10.20ms等在窗口外，不能宣布流畅。
真实首次21.125ms，12次复用中位19.96735ms，重建后21.9605ms；位一致、3created/3disposed/0active、17Burst/66managed-native。实际Waiting时原托管对拍一次，pending释放重建通过；新门禁从Waiting重新确认ready，不复用旧实例状态。

## 原型策略与证据边界
Waiting/Ready/Unavailable/Faulted/Disposed绑定owner；CanUseBurst不启动探测；Advance测试原型100ms节流。禁用/强制同步/timeout先拒绝，Unavailable本实例不重试；替换owner使旧ready失效，Dispose幂等；明确不可用标记降级，意外异常释放后原样抛出。13项是注入选项/时钟/后端测试，不是真改Burst全局开关或真实编译器故障，异常映射及游戏层错误处置未完成。
发布器剥离两个新增片段后与前一异步候选Runtime逐字节一致，Nav一致。上轮空探测累计1021.7295ms、本轮14.9173ms不是成对ABBA；6.70s与8.83s差别不证明编译稳定加速，原正式稳态验收失败未改判。

## 下一项
在临时后备候选中连接同一准备策略到真实TerrainNavigationRuntime.Upload的托管/Burst选择，定向验证真实路径，避免无限只测假后端。未就绪仍用原托管，不把尚未编译当永久不可用；使用前检查编译许可与实例有效性，准备不改目标刷新/导航同步Run/调度/并行。
先固定/动态目标、禁用和Runtime重建/Dispose路径，再考虑场景边界；不要同时改加载UI/增取消功能，也不启用默认。真实异常的主流程处置必须明确，不能把原型case视作已完成。
生命周期结论保留：最终规则/全局数值/验证后、State=Battle前是逻辑边界但同步；暂停不挡固定目标求解；直接场景/重开/布阵/恢复需覆盖。Fail禁用Manager经OnDisable释放；目前无加载取消。

## 保护与证据
原6源码/asmdef、150项测试、原JIT目录树/文件哈希已恢复，生成JIT归档，其他缓存不变。9693+30、4用户文件、37全部387、包/HEAD/index保护通过，无Unity/锁；无包/stage/commit/push，不改偏好/包、不动pelican/svg/meta，不-nographics，人数/48/已采纳优化保持。一次性脚本勿重跑。
报告Docs/InteractionRefinement-20261007/P3-BURST-PREPARATION-GATE-20261008.md；Logs/InteractionRefinement-20261007/P3/burst-preparation-gate-20261008-01：compact-analysis.json可完整读，summary有27000+心跳/缓存行不要截断当完整JSON；P8/p3-burst-preparation-gate-cold-01。旧NEXT_TASK/reserve原字节备份在该证据目录。
Burst仍可行后备而非已采纳：currentlyEnabled=false/formalGatePassed=false/prewarmIntegrated=false/preparationGateProductionIntegrated=false，Player/AOT未验证；P3/P9/人工未关闭。自主继续监督到结论，不每步问；出包仍需另授权。
''').encode('utf-8'))
check=project_check(full=True);users=user_check();assert check['passed'] and users['passed'] and not processes() and not(ROOT/'Temp/UnityLockfile').exists()
assert tree(cache)==cacheBefore and {x.name:tree(x) if x.is_dir() else {'sha256':sha(x)} for x in cache.parent.iterdir() if x!=cache}==other
assert subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip()==head and sha(ROOT/'.git/index')==index
save(W/'publication.json',{'passed':True,'sourceAndCachesRestored':True,'projectCheck':check,'userCheck':users,'headIndexUnchanged':True,'jobAlgorithmUnchangedByExactStrip':True,'testsPassed':14,'policyTestsInjected':13,'productionIntegrated':False,'currentlyEnabled':False,'formalGatePassed':False,'prewarmIntegrated':False,'P3Closed':False,'report':str(p.relative_to(ROOT)),'reportSha256':sha(p),'nextTaskSha256':sha(nextp),'reserveSha256':sha(rp),'noProjectUnityOrLock':True})
print('PUBLISHED preparation gate diagnostic: 14/14, exact algorithm preservation, source/cache protection passed; no production integration.',flush=True)
