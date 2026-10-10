from p3_density_runtime_20261008 import *
sys.stdout.reconfigure(encoding='utf-8')
S=LOG/'P3'/'burst-startup-20261008-01';W=LOG/'P3'/'burst-prewarm-20261008-01';P=LOG/'P3'/'burst-production-20261008-01'
a=json.loads((W/'combined-analysis.json').read_text(encoding='utf-8'));assert a['allProtectionPassed'] and a['sourceAndOriginalCacheRestored'] and not a['adopted']
assert project_check(full=True)['passed'] and user_check()['passed'] and not processes() and not(ROOT/'Temp/UnityLockfile').exists()
report=ROOT/'Docs/InteractionRefinement-20261007/P3-BURST-STARTUP-20261008.md';assert not report.exists()
cold,warm,pre=a['phases']
lines=['# P3 Burst后备：发现秒级无缓存首调用；显式预热能前移但不能消除成本','', '## 结论','保留Burst为可行后备，但不能把无缓存Editor的第一次同步编译留给战斗中的求解。空项目JIT缓存首次实际Run约9.18秒；缓存复用的新进程首次Run约24.6ms。另一次空缓存试验先执行空目标预热，预热约12.09秒，随后首次真实求解约14.0ms。说明可以把成本前移至显式准备阶段，不是把成本消除。没有接入游戏加载流程，也未证明加载UI仍响应。','这修正了此前“首观察到的Run20–36ms”只能代表既有缓存条件的局限。不把Editor JIT结果外推成播放器AOT启动时间；没有出包或AOT验收。原正式ABBA首组失败仍有效，预热试验也没有修复或改判稳态门槛。','', '## 三种实测条件','每种只执行一个定向NUnit测试，真实保留候选的Nav/Runtime/引擎asmdef原字节临时接入，未修改求解算法。测试使用同一历史捕获输入0，目标有序、半径及完整输出位对拍；生产候选是非测试程序集中的同一个TerrainNavigationSolveJob，不是重命名的简化作业。','', '|条件|启动前JIT文件|工厂调用ms|显式预热ms|首次真实Run ms|12次复用中位ms|重建工作区首Run ms|','|---|---:|---:|---:|---:|---:|---:|']
for label,q in zip(['空项目JIT缓存','复用上一进程生成缓存','空缓存+显式空目标预热'],a['phases']):lines.append(f"|{label}|{q['cacheFilesBeforeLaunch']}|{q['factoryMs']:.4f}|{q.get('prewarmMs',0):.4f}|{q['firstRunMs']:.4f}|{q['reuseMedianMs']:.4f}|{q['secondFirstRunMs']:.4f}|")
lines+=['','未预热行的0表示没有执行预热步骤，而不是预热免费。工厂计时含反射调用、native分配/复制、相应托管初始化，不能直接当正式场景构造成本。每次Run计时含目标准备、结果分配、同步Run及CopyTo；输出比较和缓存清单读取在计时外。三个阶段均2创建/2释放/active0，真实Burst次数14/14/15、managed-native0，无native泄漏日志；空目标预热还逐位验证结果全零。','三个进程启动到测试入口分别约81.50/16.82/46.10秒，包含Editor启动、资产/C#导入和测试框架，不是游戏加载时间，不能把差额全算作Burst编译。复用中位不同也不是新的稳定性能门槛结果；没有在本轮做成对性能校准或声明收益。','', '## 缓存隔离与编译证据','未找到公开可直接传给Unity的独立JIT缓存目录开关；没有修改包内部API/用户偏好，也没有删除缓存。每次空缓存试验前确认本项目无Unity/锁，记录原Library/BurstCache/JIT目录树及所有文件哈希，再同盘重命名到证据目录封存，创建空JIT目录。缓存复用阶段不改候选源码或JIT；结束后把新生成缓存整体归档，再把原目录移回。','两次空缓存运行的启动前清单均0文件；首次实际Run之前/显式预热之前均0 DLL，之后各出现3个DLL，且新增同一hash的PDB含TerrainNavigationSolveJob名称。缓存复用进程在测试入口就已有相同job名称对应文件。原始逐阶段缓存清单及新缓存保存，构成编译发生于相应阶段的佐证。','但PDB字符串/文件时刻不是编译器cache-hit/miss遥测，也不能把约9–12秒全部拆为该单一作业的纯编译CPU时间；还可能包含编译服务启动、其他编译工作或等待。只证明“项目JIT目录为空”的受控启动，不声称整台机器OS磁盘缓存、编译服务及所有其他缓存均冷。','原JIT目录最终逐文件哈希及目录树一致，其他BurstCache兄弟项未变。所有新缓存保留在各证据目录generated-JIT，没有覆盖/删除原缓存。恢复记录见各summary的cacheRestored/otherCacheUnchanged/protectionPassed=true。','', '## 预热方案边界与后续决策','空目标仍执行相同泛型Job.Run、相同完整作业入口和Burst执行证明；hasTargets=false只在作业内部初始化后返回。不会派发后台导航任务或改变目标刷新节奏。这次验证了预热后的真实求解精确输出以及工作区重新创建后的复用。','预热本身是同步12秒调用，把它放在战斗Tick或一个没有设计好的加载回调里仍会卡住主线程；不能简单增加一次调用就称体验问题解决。尚未做准备阶段接入、进度显示/取消/重开、真实场景首次命令和加载响应验证。','下一项应先只读定位现有世界创建/导航初始化/战斗启用之间是否存在合适且不重复的准备边界，并区分Editor JIT与Player AOT。若接入，仍需生命周期、无Burst回退与独立场景验证；不得用预热掩盖稳态ABBA失败或悄悄降低门槛。打包/AOT/EXE验证仍走独立授权边界，不自动出包。','用户的Burst后备指示继续有效。当前状态是“算法/回退已验证、Editor无缓存首次同步调用秒级、显式预热可前移成本但尚未接入、正式性能采纳未通过”，不是已正式启用。','', '## 恢复与保护','两轮执行器及分析均已收取；6个源码/asmdef文件按原字节恢复，测试仍原150项。当前仍density-runtime登记baseline；二叉堆/方向输出及Lane已采纳优化保持。','9693+30、4用户文件、37全部387文件、packages manifest/lock、HEAD/index保护通过；原JIT及其他缓存保护通过，无本项目Unity或锁，无打包/stage/commit/push。P3/P9与人工门槛不关闭。','证据：Logs/InteractionRefinement-20261007/P3/burst-startup-20261008-01与burst-prewarm-20261008-01的scope/registry/summary、缓存清单/generated-JIT/recovery；后者combined-analysis.json。测试P8/p3-burst-startup-cold-01、p3-burst-startup-warm-01、p3-burst-prewarm-cold-01。']
report.write_bytes(('\n'.join(lines)+'\n').encode('utf-8'))
reserve_path=P/'fallback-reserve.json';reserve_backup=W/'fallback-reserve.before-startup.json';assert not reserve_backup.exists();reserve_backup.write_bytes(reserve_path.read_bytes())
reserve=json.loads(reserve_path.read_text(encoding='utf-8'));reserve.update(status='feasible_reserved_editor_startup_risk_verified',currentlyEnabled=False,formalGatePassed=False,editorEmptyProjectJitFirstRunMs=cold['firstRunMs'],editorCachedFirstRunMs=warm['firstRunMs'],explicitNullPrewarmMs=pre['prewarmMs'],firstRealRunAfterPrewarmMs=pre['firstRunMs'],prewarmIntegrated=False,playerAotStartupVerified=False,startupReport=str(report.relative_to(ROOT)),startupEvidence=str((W/'combined-analysis.json').relative_to(ROOT)),activationRequirement='Do not trigger first uncached synchronous Editor compilation in interactive battle. Explicit preparation can move cost but currently blocks for seconds and is not integrated. Preserve failed formal performance verdict; validate intended lifecycle and Player/AOT separately before claims.')
save(reserve_path,reserve)
p=ROOT/'NEXT_TASK.md';backup=W/'NEXT_TASK.before-startup.md';assert not backup.exists();backup.write_bytes(p.read_bytes())
p.write_bytes(('''# 最新接力：Burst后备无缓存启动风险已实测，预热可前移但未接入（2026-10-08）

## 用户约定
自己继续并监督到结果，不每一步问；Burst为可行后备，其他路线实在无解时用。当前未启用，不修改原正式门槛失败，打包/AOT/EXE仍有独立授权门槛，P3/P9/人工未关闭。

## 本轮实测
临时使用原保留生产候选的Nav/Runtime/engine asmdef，不改算法：
- 空项目JIT缓存：首个实际同步Run 9177.94ms，工厂36.10ms，12次复用中位23.23ms。
- 复用生成缓存的新进程：首Run24.60ms，工厂8.83ms，复用中位15.26ms。
- 再次空项目JIT：显式空目标预热12094.51ms，随后首次真实Run14.01ms，复用中位18.19ms。
每阶段1/1定向测试；真实输出位一致，预热输出全零，2创建/2释放/active0，真实Burst14/14/15、managed-native0。首/预热之前0DLL，之后3DLL且出现含同一TerrainNavigationSolveJob名称的PDB。这里只验证项目JIT为空，不声称整机/OS缓存全冷，也不把9–12秒全部归该job纯编译CPU。Editor启动到测试入口81.5/16.8/46.1秒含导入/测试框架，不当游戏加载时间。

## 结论与下一项
无缓存首调用若留在战斗会有秒级停顿风险；预热能前移，不能消除成本，同步预热放在加载回调也会阻塞主线程。没有接入加载流程/进度/取消/重开，不能说加载UI响应已解决；更不能由Editor JIT推断Player AOT启动。
下一项先只读定位现有世界创建/导航初始化/战斗启用之间的准备边界，区分Editor JIT与Player AOT；如果接入需生命周期、回退及真实首次命令验证，不改刷新/调度/人数/48，不以预热掩盖稳态ABBA失败或降低门槛。新报告和fallback-reserve.json已明确这些条件。尚未进行准备阶段接入。

## 保护
无公开可直接用的缓存隔离CLI，实际采用可逆目录封存：先记录原JIT所有文件哈希/目录树，原目录整体重命名保管，空目录启动；结束后新缓存归档、原目录移回并核验。原缓存未删除，最终JIT哈希/目录树相同，其他BurstCache兄弟项未变。
所有6个源码/asmdef恢复原字节，仍150项、density-runtime baseline，检查入口p3_density_runtime_20261008.py::project_check。包、4用户文件、37的387文件、HEAD/index未变，无本项目Unity或锁，无打包/暂存/提交/推送。pelican SVG/meta不动，人数/48不变，不-nographics、不强关用户Editor。

## 证据/任务
报告：Docs/InteractionRefinement-20261007/P3-BURST-STARTUP-20261008.md。
Logs/InteractionRefinement-20261007/P3/burst-startup-20261008-01：双进程诊断abe6b49c exit0；burst-prewarm-20261008-01：单窗预热887b46e8 exit0、combined-analysis.json，分析d495b415 exit0。所有summary protectionPassed/sourceRestored/cacheRestored/otherCacheUnchanged=true。新缓存保留generated-JIT，原JIT已回原位置，勿重跑一次性脚本。
P8/p3-burst-startup-cold-01、warm-01、p3-burst-prewarm-cold-01。后备标记burst-production-20261008-01/fallback-reserve.json已追加启动风险及未接入状态；旧标记和旧NEXT_TASK原字节在burst-prewarm-20261008-01备份。无任务在途。
''').encode('utf-8'))
for path in ['Docs/InteractionRefinement-20261007/P3-DEFERRED-ISSUES.md','Docs/InteractionRefinement-20261007/P3-BURST-RESERVE-AND-VARIANCE-20261008.md']:
 with (ROOT/path).open('ab') as f:f.write(('\n\n## 2026-10-08 后备启动风险实测\n空项目JIT首Run约9.18秒，缓存复用24.6ms；另次空缓存显式空目标预热12.09秒，之后真实首Run14.0ms。预热只前移成本，未接入加载，也未验证Player AOT。原源码和缓存均恢复，仍未启用/打包、不改原稳态验收失败。见[P3 Burst启动报告](P3-BURST-STARTUP-20261008.md)。\n').encode('utf-8'))
assert project_check(full=True)['passed'] and user_check()['passed'] and not processes() and not(ROOT/'Temp/UnityLockfile').exists()
save(W/'publication.json',{'report':str(report.relative_to(ROOT)),'reportSha256':sha(report),'nextTaskSha256':sha(p),'reserveSha256':sha(reserve_path),'sourceAndCacheRestored':True,'startupRiskRecorded':True,'prewarmIntegrated':False,'burstEnabled':False,'P3Closed':False,'noTasksInFlight':True})
print('PUBLISHED uncached Editor startup risk and bounded prewarm result; sources/original caches restored; reserve updated.',flush=True)
