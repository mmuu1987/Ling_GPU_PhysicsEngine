from p3_density_runtime_20261008 import *
from p3_burst_startup_20261008 import tree
sys.stdout.reconfigure(encoding='utf-8')
W=LOG/'P3'/'burst-runtime-qualification-20261008-05'
R=LOG/'P3'/'burst-runtime-gate-20261008-01'
S=LOG/'P3'/'burst-runtime-scene-20261008-02'
P=LOG/'P3'/'burst-production-20261008-01'
def load(p):return json.loads(p.read_text(encoding='utf-8'))
r=load(R/'summary.json');s=load(S/'summary.json');q=load(W/'summary.json')
assert all(x['protectionPassed'] and x['sourceRestored'] and x['cacheRestored'] and x['otherCacheUnchanged'] for x in [r,s,q])
assert len(r['phases'])==4 and sum(x['testsPassed'] for x in r['phases'])==153 and len(s['phases'])==1 and s['phases'][0]['testsPassed']==1
for i in range(1,6):assert load(LOG/'P3'/('burst-runtime-qualification-20261008-%02d'%i)/'summary.json')['protectionPassed']
gpu=load(P8/'p3-runtime-qualified-gpu-02'/'process.json');assert gpu['passed'] and int(gpu['tests']['passed'])==6
for p in [NAV_PATH,LANE_PATH,RUNTIME_PATH,'Assets/MassEngine/MassEngine.asmdef']:
 assert (W/'candidate'/p).read_bytes()==(R/'candidate'/p).read_bytes()==(S/'candidate'/p).read_bytes()==(LOG/'P3'/'burst-runtime-qualification-20261008-02'/'candidate'/p).read_bytes()
assert project_check(full=True)['passed'] and user_check()['passed'] and not processes() and not(ROOT/'Temp/UnityLockfile').exists()
complete=len(q['rendered'])==4 and len(q.get('pairs',[]))==2
passed=complete and q.get('renderGatePassed',False)
if complete:
 assert all(x['conditions']==q['rendered'][0]['conditions'] for x in q['rendered'])
 assert [x['candidateAvailable'] for x in q['audits']]==[False,True,True,False]
 for x in q['audits'][1:3]:
  assert x['sameRuntime'] and x['startState']==x['endState']=='Ready' and x['endNative']>x['startNative'] and x['startManaged']==x['endManaged'] and x['endManagedNative']==0 and x['startProbes']==x['endProbes'] and x['created']==x['disposed'] and x['active']==0
verdict='通过（仍仅后备，未启用）' if passed else ('未通过（两对分别判定，不互相抵消）' if complete else '未完成/阻断，不能宣称通过')
cache=ROOT/'Library/BurstCache/JIT';cacheBefore=tree(cache);assert cacheBefore==load(W/'cache-before.json')
other={p.name:tree(p) if p.is_dir() else {'sha256':sha(p)} for p in cache.parent.iterdir() if p!=cache}
head=subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip();index=sha(ROOT/'.git/index')
analysis={'runtimePhases':r['phases'],'scenePhases':s['phases'],'successfulCaseExecutions':160,'renderedGpuTestsPassed':6,'engineBytesEqualAcrossRuntimeSceneGpuAndAbba':True,'formalAbbaComplete':complete,'latestRuntimeCandidateFormalGatePassed':passed,'historicalFormalGatePassed':False,'rendered':q['rendered'],'pairs':q.get('pairs',[]),'audits':q['audits'],'error':q.get('error'),'currentlyEnabled':False,'allAttemptsRestored':True}
assert not(W/'compact-analysis.json').exists();save(W/'compact-analysis.json',analysis)
report=f'''# P3 Burst 后备：真实 Runtime / 场景 / 渲染资格验证

## 结论
真实 Runtime 连接及当前 Green 场景生命周期通过；原150项回归、3个实际启动配置 Runtime 用例、1个场景用例、6个有渲染 GPU 用例，共160次成功用例执行（不是160个新用例）。新候选正式 ABBA：**{verdict}**。
候选始终是临时后备。所有轮次的源码、原项目 JIT、保护文件均已恢复；没有自动采纳、打包、提交或关闭 P3/P9。旧候选正式 ABBA 失败仍有效，不能被新试验覆盖。

## 真实连接与边界
- TerrainNavigationRuntime 在正常 Tick 中推进 Editor 准备策略；尚未 Ready 时 Upload 使用原托管求解。就绪且许可/实例仍有效时使用相同同步 IJob.Run 原生内核。
- 实际启动禁用和强制同步标志在分配与探测之前拒绝该准备路径。没有操作持久化 Burst 偏好开关。
- 点/区域/动态密度目标、两个排队队伍每 Tick 一个、未完成 GPU 回读时 Dispose、释放重建/旧实例 Rebuild 拒绝均有实际验证。求解算法、严格浮点、目标顺序、刷新、round-robin、同步 Run 未改；没有并行导航。
- 冷 Runtime：确实见到 Waiting；2次原托管、4次原生真实求解、0次完整 managed-native 求解，127次探测，逐位一致；2创建/2释放/0活跃。等待12680.2922ms，最大 Tick38.0708ms、探测15.6904ms，最终 Ready。
- 实际禁用/强制同步：各6次原托管、0原生/0探测/0工作区，结果一致、Unavailable。它们不证明运行中切换偏好、真实编译故障或超时恢复；这些只有此前注入策略证据。

## 当前场景生命周期
启动时项目 JIT 为空不代表进入 Green 时仍未编译完成。首次场景夹具错误要求入口必须 Waiting，失败保留；仅修正此前提后，生产候选逐字节不变。
有效场景用例入口已 Ready，命令后确认新的原生导航工作；重开释放、取消布阵、应用布阵并重建、返回释放、无效已加载世界失败后重试、直接 Green→Menu 均通过。4创建/4释放/0活跃。
load4193.0754ms；命令到新原生场152.1807ms不是编译耗时；最大准备帧间隔72.0609ms、探测8.8905ms。场景本身没有见到 pending 托管回退，冷 Runtime 用例独立证明它。该批1280×720生命周期测试不是正式渲染 FPS 或人工加载体验验收。

## 渲染试验及所有失败留档
1. qualification-01：登记表漏了未改动的已采纳测试文件，保护检查在启动 Unity 前拒绝。只补齐精确原字节登记，不扩大允许修改范围。
2. qualification-02：有渲染 GPU6通过。A1采样中相机从(250,280,-384)缩近至约(21.71,31.62,-33.35)，cameraFixed=false，窗口无效；没有运行候选窗口，不用其性能数字判门槛。原因不能直接归咎用户操作。
3. qualification-03：图形 Editor 在观测器标记/性能数据出现之前正常退出码0；无编译错误记录，退出来源未确定。不能据此宣称夹具已运行或产品失败。
4. qualification-04：执行器子 runner 误导入上一版登记表，被精确哈希保护在 Unity 启动前拒绝。属于本次自动化夹具错误，不是产品性能结果。
5. qualification-05：runner 显式绑定本脚本模块并断言；新的完整 ABBA 目录，不复用以上任何无效窗口。复用 GPU6之前逐字节核对同一生产候选，未声称重新执行了GPU6。
后续夹具仅通过现有 LockInput API 隔离测试自有相机输入、取消待处理运动、要求原宏观位置（不强行改写视角），只测 manager.cullingCamera，并检查 FOV。所有 A/B 用相同观测器，结束恢复锁状态；没有修改游戏相机源码、加载/UI或OS输入。

## 新正式 ABBA
原门槛保持：B1/A1和B2/A2各自 P99≤0.80倍、median≤1.05倍、>33.33ms帧数≤0.50倍、>50ms帧数≤max(A,3)。固定35秒预热+15秒采样；不为 Ready 额外等待，不挑窗口。
条件为2048单位、separation48、1920×1080、GTX1060 3GB/D3D11、vsync0/target−1、宏观相机(250,280,-384)。候选首采样必须 Ready，到末尾保持同一 Runtime，真实原生次数增长、原托管不增长、完整 managed-native为0、探测次数不增长，释放后工作区平衡。准备阶段的零单元托管探测明确计数，不能误写成全程没有托管调用。
'''
if complete:
 report+='\n|窗口|median ms|P99 ms|>33.33ms|>50ms|\n|---|---:|---:|---:|---:|\n'
 for label,x in zip(['A1','B1','B2','A2'],q['rendered']):report+=f"|{label}|{x['medianMs']:.4f}|{x['p99Ms']:.4f}|{x['over33ms']}|{x['over50ms']}|\n"
 for x in q['pairs']:report+=f"\n{x['pair']}：P99比例{x['p99Ratio']:.6f}，median比例{x['medianRatio']:.6f}，门槛{json.dumps(x['gates'])}；{'通过' if x['passed'] else '失败'}。\n"
 report+='\n原生路径/释放审计：\n```json\n'+json.dumps(q['audits'][1:3],ensure_ascii=False,indent=2)+'\n```\n'
else:report+='\n本轮阻断原因：'+q.get('error','完整四窗口/两对结果缺失')+'。已完成窗口数：'+str(len(q['rendered']))+'。不计算不完整ABBA的采纳结论。\n'
report+='''
## 判定与后续边界
旧正式第一对 P99仅改善16.19%且median增加6.08%，失败保留。新结果独立判定；本次准备/生命周期通过不等于所有长帧已解决。
Burst标为可行后备，不默认开启。不将Editor异步准备推断为Player/AOT行为；实际编译器故障的游戏层错误处置仍未证明优雅恢复。意外异常释放后原样抛出，没有增加加载取消，也没有保证无分配/无卡顿。
后续不得通过重复挑样或放宽指标翻转失败。若继续性能工作，先单独诊断真实求解及上传的尾部成本，不混入正式采样；任何启用须保留适用范围与取舍，出包另获授权。P3/P9及人工/Player门槛保持开放。

## 保护与索引
原7个登记源文件/asmdef/观测器均恢复，其中原测试内容不参与A/B变化；原JIT目录树/哈希恢复，生成缓存归档，其他BurstCache不变。9693+30、用户4文件、37全部387文件、包/HEAD/index检查通过，无本项目Unity进程或锁。pelican SVG/meta未动，既有二进制堆/输出复用/Lane优化和人数/48保留；37本身未重建、没有包含这些后续源码收益的新包。
Runtime：P3/burst-runtime-gate-20261008-01；场景修正：P3/burst-runtime-scene-20261008-02；渲染/恢复：P3/burst-runtime-qualification-20261008-01至05，原始窗口P3/p3-runtime-fixedcamera-r3-abba-{1,2,3,4}（仅实际存在的窗口算已执行）；GPU6：P8/p3-runtime-qualified-gpu-02。以上均在Logs/InteractionRefinement-20261007下。compact-analysis.json为最新精简汇总，summary/registry/scope/original/candidate及失败日志保留；一次性执行器不可重跑。
'''
p=ROOT/'Docs/InteractionRefinement-20261007/P3-BURST-RUNTIME-QUALIFICATION-20261008.md';assert not p.exists();p.write_bytes(report.encode('utf-8'))
rp=P/'fallback-reserve.json';backup=W/'fallback-reserve.before-runtime-publication.json';assert not backup.exists();backup.write_bytes(rp.read_bytes());reserve=load(rp)
reserve.update(status='feasible_reserved_runtime_scene_verified_render_'+('passed' if passed else 'failed' if complete else 'blocked'),currentlyEnabled=False,formalGatePassed=False,historicalFormalGatePassed=False,latestRuntimeCandidateFormalGatePassed=passed,latestRuntimeCandidateAbbaComplete=complete,runtimePreparationConnectedInTemporaryCandidate=True,runtimePreparationCurrentlyIntegrated=False,actualRuntimeModesVerified=True,currentSceneLifecycleVerified=True,currentScenePendingObserved=False,renderedGpuTestsPassed=6,runtimeCandidateSnapshot=str((W/'candidate').relative_to(ROOT)),runtimeCandidateRegistry=str((W/'registry.json').relative_to(ROOT)),runtimeQualificationReport=str(p.relative_to(ROOT)),runtimeQualificationEvidence=str((W/'compact-analysis.json').relative_to(ROOT)),preparationGateProductionIntegrated=False,playerAotStartupVerified=False,activationRequirement='Use the validated Runtime preparation candidate rather than old synchronous startup. Preserve failed historical verdict and any latest failed/blocked gate. Activation requires explicit scope/tradeoff; no default adoption, packaging or Player/AOT claim.',remaining='Editor qualification verdict recorded separately. Actual compiler-fault UI recovery, Player/AOT and manual gates remain open. No performance cherry-picking or relaxed thresholds.')
save(rp,reserve)
nextp=ROOT/'NEXT_TASK.md';backup=W/'NEXT_TASK.before-runtime-publication.md';assert not backup.exists();backup.write_bytes(nextp.read_bytes())
nextp.write_bytes((f'''# 最新接力：真实Runtime/场景/GPU验证完成；新ABBA{verdict}（2026-10-08）

## 最新结论
真实Runtime153次成功执行（原回归150+冷/禁用/强同步各1），当前场景1，渲染GPU6，共160次成功用例执行，非160个新用例。准备连接只在临时候选验证，所有源码/缓存恢复、Burst未启用。
新ABBA complete={complete} / passed={passed}；两对原门槛独立判定，旧第一对P99改善16.19%、median+6.08%的失败不改判。详情报告/精简JSON，不把夹具或启动失败计为性能结果。

## 已确认的真实路径
Waiting走原托管，Ready后同内核同步IJob.Run；禁用/强同步实际启动均0工作区/0探测。冷Runtime观察到pending，原托管2/原生4/完整managed-native0，逐位一致，2创建2释放；127探测，等待12.680s，maxTick38.071ms。当前Green入口Ready而非Waiting，命令后新原生工作与重开/布阵/返回/无效世界失败重试/直接场景边界通过，4创建4释放；72.061ms帧间隔不是流畅性通过。
场景第一次错误冷入口前提、qualification01登记遗漏、02相机漂移、03启动前退出0、04错导入登记模块均保留；05自绑定模块，完整四窗仅在实际成功时才计结果，不复用无效数据。不存在自动采纳/改门槛/选窗。

## 后续工作约束
不要重复已完成Runtime/场景用例或只测试假后端。若继续长帧方向，先独立诊断真实求解与上传尾部成本再决定小改动，不把带诊断开销数据混入正式ABBA，不用反复重跑挑通过。实际编译器故障主流程恢复、Player/AOT、人工及宽覆盖仍开放。Burst是可行后备，不默认启用；启用须说明最新门槛结果与取舍，出包仍需另授权。
保持同步求解/目标顺序/刷新/round-robin/严格浮点，不并行导航、不改加载UI/增取消、不迁移UI。自主推进并监督到结果，非仅挂任务；遇实际权限/人工门槛不可虚假关闭。

## 证据和保护
报告：{p.relative_to(ROOT).as_posix()}。
汇总：{(W/'compact-analysis.json').relative_to(ROOT).as_posix()}；同目录publication.json/registry/summary/original/candidate；Runtime和场景见报告索引。一次性脚本勿重跑。
源7/原JIT/其他缓存恢复，9693+30、用户4、37的387文件、包/HEAD/index通过，无项目Unity/锁；NEXT_TASK仅本地未提交。pelican/svg/meta、已采纳堆/输出/Lane、2048/48和旧包保护；无stage/commit/push/新包，37没有自动更新。P3/P9/人工/Player仍开放。
''').encode('utf-8'))
check=project_check(full=True);users=user_check();assert check['passed'] and users['passed'] and not processes() and not(ROOT/'Temp/UnityLockfile').exists()
assert tree(cache)==cacheBefore and {x.name:tree(x) if x.is_dir() else {'sha256':sha(x)} for x in cache.parent.iterdir() if x!=cache}==other
assert subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip()==head and sha(ROOT/'.git/index')==index
save(W/'publication.json',{'passed':True,'report':str(p.relative_to(ROOT)),'reportSha256':sha(p),'nextTaskSha256':sha(nextp),'reserveSha256':sha(rp),'projectCheck':check,'userCheck':users,'sourceAndCachesRestored':True,'headIndexUnchanged':True,'noProjectUnityOrLock':True,'successfulCaseExecutions':160,'formalAbbaComplete':complete,'latestRuntimeCandidateFormalGatePassed':passed,'historicalFormalGatePassed':False,'currentlyEnabled':False,'P3Closed':False})
print('PUBLISHED runtime/scene/render qualification:',verdict,flush=True)
