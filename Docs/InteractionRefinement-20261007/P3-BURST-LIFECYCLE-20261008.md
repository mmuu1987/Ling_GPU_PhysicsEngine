# P3 Burst后备：准备边界已定位，不能直接接入同步预热

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
