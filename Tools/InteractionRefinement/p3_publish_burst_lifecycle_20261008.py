"""Read-only source/lifecycle audit; publish bounded findings, no Unity launch or runtime edits."""
from p3_density_runtime_20261008 import *
from p3_burst_startup_20261008 import tree
sys.stdout.reconfigure(encoding='utf-8')
WORK=LOG/'P3'/'burst-lifecycle-20261008-01'
REPORT=ROOT/'Docs/InteractionRefinement-20261007/P3-BURST-LIFECYCLE-20261008.md'
RESERVE=LOG/'P3'/'burst-production-20261008-01'/'fallback-reserve.json'
assert not WORK.exists() and not REPORT.exists()
assert project_check(full=True)['passed'] and user_check()['passed'] and not processes() and not(ROOT/'Temp/UnityLockfile').exists()
paths=[
'Assets/Game/Scripts/WarSandboxSceneSession.cs',
'Assets/Game/Scripts/WarSandboxBattleController.cs',
'Assets/Game/Scripts/WarSandboxRuntimeBootstrap.cs',
'Assets/Game/Scripts/WarSandboxRuntimeDeployment.cs',
'Assets/Game/Scripts/WarSandboxFrontEnd.cs',
'Assets/MassEngine/Core/MassEngineManager.cs',
'Assets/MassEngine/Core/MassEngineManager.Terrain.cs',
'Assets/MassEngine/Core/MassEngineManager.LocalOrders.cs',
'Assets/MassEngine/Core/ComputePipelineOrchestrator.cs',
'Assets/MassEngine/Terrain/TerrainNavigationRuntime.cs',
'Assets/MassEngine/Terrain/TerrainNavigationGrid.cs',
'Assets/Game/Tests/PlayMode/WarSandboxSceneEntryTests.cs']
source={p:(ROOT/p).read_bytes() for p in paths}
text={p:b.decode('utf-8-sig') for p,b in source.items()}
S=text[paths[0]];C=text[paths[1]];D=text[paths[3]];M=text[paths[5]];T=text[paths[6]];R=text[paths[9]]
def section(s,a,b):return s[s.index(a):s.index(b,s.index(a)+len(a))]
apply=section(S,'private void ApplyLoadedScene','private void Fail')
fail=section(S,'private void Fail','private void ClearPending')
rebuild=section(R,'public void Rebuild','private void Upload')
checks={
'sceneLoadedSetupSynchronous':'try { ApplyLoadedScene(scene); }' in S,
'loadingRejectsNavigation':'if (IsLoading) { error = "A scene is already loading."; return false; }' in S,
'finalRulesThenStatsThenBattle':apply.index('TryApplyBattlefieldConfig')<apply.index('TryApplyGlobalStatsOnLoad')<apply.index('if (!TryValidateScene(manager, out error))')<apply.index('State = WarSandboxEntryState.Battle'),
'failedManagerDisabled':'manager.PauseBattle();' in fail and 'manager.enabled = false;' in fail,
'onDisableReleases':'Release();' in section(M,'private void OnDisable()','[ContextMenu'),
'initializeReleasesBeforeAllocation':M.index('Release();',M.index('public void Initialize()'))<M.index('bufferManager.Allocate(',M.index('public void Initialize()')),
'resetReinitializes':'Initialize();' in section(M,'public void ResetScenario()','/// <summary>'),
'releaseOwnsTerrain':'ReleaseTerrain();' in section(M,'public void Release()','private void ResolveRenderRuntimes'),
'releaseTerrainDisposesRuntime':'terrainRuntime?.Dispose(); terrainRuntime = null;' in T,
'fixedTargetSolveNotPausedGuard':'(flow.targetMode == 0 && (!context.battleStarted || !flow.dynamicFlowEnabled))' in rebuild and 'if (flow.targetMode != 0)' in rebuild and 'Upload(buffers, team, flow.targetStopRadius, targets);' in rebuild,
'deploymentCommitAndRollbackReinitialize':D.count('manager.ResetScenario(); manager.PauseBattle();')>=2 and 'controller.ResetForDeployment(Draft.Rules);' in D and 'controller.ResetForDeployment(previousRules);' in D,
'directSceneCanHaveNoSession':'return Instance == null || (!Instance.InputBlocked && Instance.Controller == controller);' in S,
'currentRuntimeNotBurstEnabled':'TerrainNavigationBurstWorkspace' not in R,
}
assert all(checks.values()),checks
candidate=(LOG/'P3'/'burst-production-20261008-01'/'candidate'/paths[9]).read_text(encoding='utf-8-sig')
checks['candidateLazyWorkspaceBeforeSolve']='if (burstWorkspace == null) burstWorkspace = Navigation.CreateBurstWorkspace();' in candidate and 'directions = burstWorkspace.Solve(goals, stopRadius);' in candidate
assert checks['candidateLazyWorkspaceBeforeSolve']
cache=ROOT/'Library/BurstCache/JIT';cacheBefore=tree(cache)
assert cacheBefore==json.loads((LOG/'P3'/'burst-startup-20261008-01'/'cache-before.json').read_text(encoding='utf-8'))
otherBefore={p.name:tree(p) if p.is_dir() else {'sha256':sha(p)} for p in cache.parent.iterdir() if p!=cache}
head=subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip();index=sha(ROOT/'.git/index')
packages={p:sha(ROOT/p) for p in ['Packages/manifest.json','Packages/packages-lock.json']}
WORK.mkdir()
for p,b in source.items():
 q=WORK/'source'/p;q.parent.mkdir(parents=True,exist_ok=True);q.write_bytes(b)
save(WORK/'audit.json',{'kind':'static source audit, NOT Unity execution or responsiveness proof','checks':checks,'sources':{p:hashlib.sha256(b).hexdigest() for p,b in source.items()},'candidateRuntimeSha256':sha(LOG/'P3'/'burst-production-20261008-01'/'candidate'/paths[9]),'runtimeTestsRun':0,'productionEdits':0})
report='''# P3 Burst后备：准备边界已定位，不能直接接入同步预热

## 结论
本轮只读核实生命周期，未启动Unity、未修改运行代码。现有流程有“最终规则/数值完成→开放操作”的逻辑边界，但没有现成的非阻塞Burst准备阶段。**不把实测9–12秒同步调用塞进加载回调，也不把暂停战斗视为导航隔离。** Burst继续是未启用后备，正式ABBA失败不改判。

## 调用链与实际边界
以下均为本轮快照的源码事实，不是新一轮运行时测试：

|入口|源码位置|结论|
|菜单进场|`WarSandboxSceneSession.cs:179–235`|BeginTransition暂停、封锁输入；LoadSceneAsync协程让出帧，但sceneLoaded同步调用ApplyLoadedScene，后者并不异步。|
|初始世界|`MassEngineManager.cs:138–142,267–385`；`.Terrain.cs:151–164`|OnEnable→Initialize；先Release，再分配/建立TerrainNavigationRuntime。该构造并不是保留候选的首次Burst Run。|
|最终配置|`WarSandboxSceneSession.cs:237–276`；`WarSandboxRuntimeDeployment.cs:115–160`|应用战场规则后可能ResetScenario；全局数值应用也可能释放重建。候选准备边界必须在这些操作及最终验证之后、State=Battle之前；不能把第一次OnEnable视为最终世界。|
|首次求解|`TerrainNavigationRuntime.cs:78–143`；保留候选同文件Upload|动态目标在Tick消费完成读回；固定目标在Rebuild直接Upload。保留候选是在Upload懒建BurstWorkspace并立即Solve，因此构造TerrainNavigationRuntime并不代表内核已预热。|
|暂停不是求解屏障|`MassEngineManager.cs:144–212,900–970`；`ComputePipelineOrchestrator.cs:60–74,126–143`；`TerrainNavigationRuntime.cs:114–135`|暂停仍进入渲染/流水线。Rebuild只对动态目标检查battleStarted，固定点/区域目标仍能求解；仅挡命令或PauseBattle不能保证冷Burst不运行。|
|开战不止按钮|`WarSandboxBattleController.cs:505–575,627–642`|IssueOrderInternal和StartOrResumeBattle均会StartBattle。只给一个“开始”按钮加准备检查会漏路径；底层Manager.StartBattle也没有准备契约。Session的Battle代表已进入战场，不等于Running。|
|重开/布阵|`WarSandboxBattleController.cs:677–718`；`WarSandboxRuntimeDeployment.cs:350–409`|ResetScenario→Initialize→Release；应用布阵和回滚均可再建。还存在配置签名改变、shader恢复、GPU哨兵丢失后的Initialize（Manager:179,189,255）。不能只覆盖菜单首次进入。|
|直接开场景|`WarSandboxSceneSession.cs:213–232,74–78`；`WarSandboxRuntimeBootstrap.cs:12–46`|直接场景可销毁旧Session；无Session时命令门禁允许操作。只在Session接入不能覆盖直接打开场景及开发入口。|

## 释放、失败和“取消”
- `SceneSession.Fail:279–292`不仅暂停，还设置manager.enabled=false；对应`Manager.OnDisable:261–264→Release:605–637→ReleaseTerrain:208–212→Runtime.Dispose`。所以不能因Fail没有直接写Release就断言泄漏。保留候选的Runtime.Dispose也释放BurstWorkspace；本轮仅核对链路，没有新测Native计数。
- 返回菜单/替换Single场景依赖组件停用释放；布阵异常先Release解除候选引用，再恢复场景/规则，最后Dispose候选（Deployment:397–408）；OnDestroy也处理活跃运行副本（429–440）。新准备状态必须与同一Runtime所有权绑定，不得回调到已释放或已替换的世界。
- **现在没有加载取消功能。** CanNavigate在IsLoading时拒绝返回/切换（Session:115–122）；DrawLoading只有状态/进度（FrontEnd:256–265）。CancelConfirmation是确认框取消，CancelEditing是布阵草稿取消，二者不是取消LoadScene或编译。不要凭空承诺按钮或取消能力，也不在本轮增加新UI。
- 保留候选已有“Burst关闭/执行证明未走Burst”降级分支；它不等于完整的“准备失败自动恢复纯托管”契约。Upload内直接调用factory/Solve，不能把任意异常都称为可恢复的编译失败；正式数据/地形错误仍须明确报错。

## 最小后续方向与验证门槛
1. 先核实已安装Burst的公开机制是否能在**不改变导航同步Run、刷新/调度/并行方式**的前提下完成可响应准备，再做独立小试；不修改包或用户偏好，不用内部编译器反射。如果找不到安全机制，保持原托管默认，不扩张为加载系统重构。当前没有确定或接入非阻塞方案。
2. 必须区分进程级“同一内核编译过”和实例级“当前导航快照/工作区可用”。重开、半径/地形/配置变化使旧工作区失效，即使编译缓存还在；一个全局ready布尔值不能替代实例有效性。
3. 独立试验先记录空项目JIT下主线程最长调用及逐帧/心跳间隔、首个真实求解、执行证明和精确输出。yield包裹同步调用、换个fresh process或已有缓存下变快都不算非阻塞证据。
4. 若以后接入，再覆盖菜单自动进入/手选、直接场景、首次命令与固定目标、重开/布阵重建及回滚、失败返回重试、禁用Burst、退出/销毁及资源计数。准备未完成或可恢复后备不可用时不得偷偷在交互帧触发秒级同步编译；不增加本轮未授权的新加载取消UI。
5. `WarSandboxSceneEntryTests`已有正常进入Setup、返回释放、失败重试及直接加载案例（51–153行），可定向扩展；它们**不证明**Burst准备、冷缓存响应或新增Native资源释放。本轮未重跑，遵循纯文档不启动Unity的约定。

## 状态与保护
源文件/asmdef/包/缓存均未写；保留已采纳二叉堆/方向输出及Lane优化、人数和separation48、用户文件与37。无打包、提交、推送、偏好修改或缓存清空。发布后完整保护检查见同目录publication.json。
后备状态继续feasible_reserved_editor_startup_risk_verified，currentlyEnabled=false、formalGatePassed=false、prewarmIntegrated=false、playerAotStartupVerified=false。未解决原稳态ABBA门槛，P3/P9及人工验证不关闭。
证据：`Logs/InteractionRefinement-20261007/P3/burst-lifecycle-20261008-01/audit.json`及12份只读源快照；历史计时见[P3 Burst启动报告](P3-BURST-STARTUP-20261008.md)。
'''
REPORT.write_bytes(report.encode('utf-8'))
(WORK/'fallback-reserve.before-lifecycle.json').write_bytes(RESERVE.read_bytes())
r=json.loads(RESERVE.read_text(encoding='utf-8'));assert not r['currentlyEnabled'] and not r['formalGatePassed'] and not r['prewarmIntegrated']
r.update(lifecycleAuditReport=str(REPORT.relative_to(ROOT)),lifecycleAuditKind='read-only source audit; no runtime/UI responsiveness proof',preparationBoundaryIdentified=True,nonblockingPreparationIntegrated=False,pausedBattleIsNavigationBarrier=False,existingLoadingCancellation=False)
save(RESERVE,r)
p=ROOT/'NEXT_TASK.md';(WORK/'NEXT_TASK.before-lifecycle.md').write_bytes(p.read_bytes())
p.write_bytes(('''# 最新接力：Burst准备边界只读审查完成，暂不接同步预热（2026-10-08）

## 结论/当前状态
逻辑边界在SceneSession.ApplyLoadedScene完成最终规则/全局数值/验证之后、State=Battle之前，但这里是同步回调，放入12秒预热仍阻塞。暂停并非导航屏障：固定目标仍可求解；Session输入锁也挡不住引擎Update。直接开场景、重开/布阵及配置恢复会绕过首次进场或重建资源。Fail会禁用Manager→OnDisable→Release，不能误判为未释放。现有加载没有取消功能，确认框/布阵取消不是加载取消。
本轮只读代码+文档发布，Unity运行0次、运行源码改动0。静态链路与源码快照不是动态验证。Burst继续可行后备、当前未启用；原正式ABBA失败不改判，prewarmIntegrated=false，Player/AOT未验证。

## 下一项
先核实已安装Burst是否有公开、保持导航同步Run/刷新/调度不变的可响应准备机制，做独立最小试验，未证明前不接游戏加载。需要空项目JIT下主线程最长调用/逐帧心跳、首次真实求解与精确输出/执行证明；不能用yield包同步调用伪装异步。无安全机制则保留托管默认，不扩大成加载系统重构，不修改包或用户偏好/内部编译器反射。
如果后续接入，分开进程编译就绪与实例工作区有效性，覆盖菜单/直接场景、首次命令/固定目标、重开/布阵/回滚、失败重试/退出及Native资源计数；不得冷编译落入交互帧，也不擅自新增加载取消UI。报告有准确路径/行号及既有测试入口。

## 历史实测保留
空项目JIT首次实际Run9177.9416ms；缓存新进程24.5957ms；另次空缓存空目标预热12094.5122ms→真实首次14.0145ms。只证明项目JIT为空，非整机冷启动；预热前移不消除成本。三阶段各1/1、精确输出、2created/2disposed/0active、真实Burst14/14/15，无managed-native。原6源码/asmdef和150项测试、原JIT/其他缓存均已恢复；一次性旧执行器勿重跑。

## 保护/证据
报告：Docs/InteractionRefinement-20261007/P3-BURST-LIFECYCLE-20261008.md；证据Logs/InteractionRefinement-20261007/P3/burst-lifecycle-20261008-01（audit.json/source/publication.json，旧NEXT_TASK和reserve原字节备份）。上轮启动报告P3-BURST-STARTUP-20261008.md；prewarm目录combined-analysis.json。
发布前后density-runtime::project_check(full=True)、用户文件/37/HEAD/index/包/原JIT及其他缓存核对；生产保持baseline和已采纳优化、separation48。pelican/svg/meta不动，不-nographics、不关闭用户Editor，不打包/stage/commit/push。用户要求自主继续并监督到结果，不每步问；打包/AOT另授权，P3/P9/人工仍未关闭。
''').encode('utf-8'))
protection=project_check(full=True);users=user_check()
assert protection['passed'] and users['passed'] and not processes() and not(ROOT/'Temp/UnityLockfile').exists()
assert all((ROOT/p).read_bytes()==b for p,b in source.items())
assert tree(cache)==cacheBefore
assert {p.name:tree(p) if p.is_dir() else {'sha256':sha(p)} for p in cache.parent.iterdir() if p!=cache}==otherBefore
assert subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip()==head and sha(ROOT/'.git/index')==index
assert all(sha(ROOT/p)==h for p,h in packages.items())
save(WORK/'publication.json',{'passed':True,'staticChecks':checks,'protectedCheck':protection,'userCheck':users,'sourceUnchanged':True,'originalJitAndOtherCachesUnchanged':True,'headIndexPackagesUnchanged':True,'unityRuns':0,'runtimeEdits':0,'prewarmIntegrated':False,'burstEnabled':False,'P3Closed':False,'report':str(REPORT.relative_to(ROOT)),'reportSha256':sha(REPORT),'reserveSha256':sha(RESERVE),'nextTaskSha256':sha(p),'noProjectUnityOrLock':True})
print('PUBLISHED lifecycle audit; static checks '+str(len(checks))+' passed; source/cache/user/37 protection passed; no Unity or runtime edits.',flush=True)
