from common_p6 import *
import sys,html,re
sys.stdout.reconfigure(encoding='utf-8')
a=json.loads((P6/'audit-01.json').read_text(encoding='utf-8'));assert a['passed'];assert not processes() and not(ROOT/'Temp/UnityLockfile').exists()
doc=ROOT/'Docs/InteractionRefinement-20261007';assert not(doc/'P6-REPORT.md').exists()
for r in json.loads((P6/'handoff.json').read_text(encoding='utf-8'))['p5Publication']['files']:assert sha(ROOT/r['path'])==r['sha256'],r['path']
resources=(P6/'scene-03/p6-green-resources.txt').read_text(encoding='utf-8');timing=json.loads((P6/'scene-03/p6-short-intervals.json').read_text(encoding='utf-8'))
rows='\n'.join('| '+tag+' | '+r['tests']['passed']+'/'+r['tests']['total']+' | '+r['tests']['skipped']+' |' for tag,r in a['runs'].items())
times='\n'.join('| '+str(i+1)+' / '+r['mode']+' | '+str(r['frames'])+' | '+f"{r['medianMs']:.4f} | {r['p95Ms']:.4f} | {r['meanMs']:.4f}"+' |' for i,r in enumerate(timing['rows']))
off=[r for r in timing['rows'] if r['mode']=='disabled'];empty=[r for r in timing['rows'] if r['mode']=='enabled-empty']
avg=lambda rows,key:sum(r[key] for r in rows)/len(rows)
medianDelta=(avg(empty,'medianMs')/avg(off,'medianMs')-1)*100
text=f'''# P6：独立成员命令／导航原型检查点

2026-10-07。**用户已授权P6。本轮交付受控、显式启用的底层原型及自动测试；不是P6全面验收完成。代表性生产混合兵种、四组负载与正式性能资格仍待补验。未进入P7/P8，未制作38。**

P4/P5已实现且自动检查通过，真人体验仍待验；用户允许其不阻塞本P6原型。P3余项继续暂缓，male/knight正式separationStrength=48保持。

## 1. 当前能证明什么

- 同一team固定A/B两组独立向相反方向移动；只A待命，B仍执行原目标。未分配成员保留原全军团命令。
- 一个组只有一个共享地形流场，不是每兵一张，也不是纯直线追目标。墙障测试验证绕行到达，并逐步检查位置可行走。
- 移动释放敌方目标；待命仍近身自卫，不等于停火。
- 两个不同半径的fixture兵种实际GPU移动；暂停期间不冒充执行，恢复后继续。
- 重叠改派只移走交集；旧组剩余成员保持原序号与到达点。重复换向、整组替换、第五组预算拒绝、死亡回收、整军团覆盖和reset代次隔离均有断言。
- 2048人Green场景验证Manager接入、32成员命令、GPU观察、整军团清覆盖、reset与实际65536格导航资源；测量首个导航上下文和复用上下文的Move解算／上传成本。

**尚无普通玩家入口。** 仅开发API `TryEnableLocalOrderPrototype` / `LocalOrders.Submit` 和owned测试启用；鼠标框选、选中环、四命令所有入口统一分别属于P7/P8。不能把当前工程或37包描述成已经支持正式局部框选。

## 2. 原链路与本轮接入

原链路：玩家入口 → ArmyOrder / WarSandboxBattleController → team的导航覆盖与姿态 → team流场 → GPU移动、索敌、姿态。过去同team只有共同的目标上下文，因此不能把两组不同目标继续写回同一个team槽。

本轮保留teamId及敌我归属，AgentData仍64 bytes、VAT消费不变。新增独立32-byte LocalAgentOrder侧缓冲：slot、sequence、stance、epoch、分布到达点及停止参数。有局部覆盖时成员的流场、姿态、目标获取、停止／等待优先读取有效覆盖；无覆盖仍走原team命令。

局部逻辑组不是新阵营或永久编队，当前选区也不是历史命令成员。请求复制成员快照，另记录成员版本、类型、目标、本局代次和序号。局部Hold使用当前位置，不做完整战中阵型保持。

### 异步与状态

CPU提交请求 → GPU位置与hp异步快照 → 完成当帧复制到普通数组 → 单CPU worker做导航规划 → 主线程预算校验与事务上传 → GPU观察回执。

- AwaitingSnapshot包含快照／worker等待，不是GPU执行。
- Rejected保留原命令；Committed表示CPU已提交；GpuExecuting表示观察到成员消费该序号，不表示所有成员已到达。
- ObservedAlive、GpuExecutingMembers、GpuBlockedMembers需一起读取；LastObservationTime / LastObservationError表明最新观察时刻与失败。
- 暂停或尚未消费的序号不能计入执行人数；观察读取的是主战斗核写入的命令revision，而不是仅把CPU请求回显。
- 请求10秒超时显式拒绝。冷Editor着色器编译可能触发此超时，不能将冷编译延迟算作导航解算；本轮保留了对应失败记录。
- 过期readback由版本挡住；未完成worker被撤销后禁止同通道再并发启动第二worker。reset或几何有效性回调失效使通道失效，旧代次不能继续提交。

## 3. 导航、预算与事务边界

只对所选存活成员的当前GPU坐标检查可达性，使用所选有效半径的最大值；不以出生位置或未选大兵种代替局部检查。Manager的保守半径不小于基础物理半径，并覆盖XZ放大。静态地形／障碍输入共享，不可变；局部半径导航网格缓存、目标流场独立。

Move使用障碍感知Dijkstra流场；接近目标后仅在可直达检查通过时转向分布到达点。到达矩形容不下、路径不通、索引／team不合法、全死或超预算均拒绝，不暗合并。

原型上限：每次4096成员、65536格、4个逻辑组＋1个事务备用槽。先上传未被旧命令引用的flow/mask，再把完整新订单上传备用buffer并交换；部分替换不重写旧组剩余订单。死亡清理同样先构造副本再上传。

空组是把槽归还固定池，不是每次释放GPU后重新分配。开启但空覆盖时仍保留预分配池；shader局部keyword关闭，走原GPU路径。关闭/reset释放全部局部GPU缓冲。默认有序图形队列下测试通过；不声称支持第三方async-compute调度。

### Green实测记录与分配量

`{resources}`

- 实际FlowCellCount=65536；每物理槽flow 524288 bytes＋mask 262144 bytes，共786432 bytes（0.75 MiB）。
- 5槽池3932160 bytes；2048成员的双32-byte订单＋4-byte回执共139264 bytes；合计4071424 bytes，约3.883 MiB。
- 上述GPU值是实际已创建ComputeBuffer的逻辑count×stride分配量，不是驱动显存峰值读数。
- CPU缓存每网格按96 bytes/cell估算约6 MiB，最多5缓存；worker及解算临时数组会另占内存。这个估算不是实测内存上界或峰值。
- 首个网格创建在worker，不把数百毫秒后台工作伪报成帧内阻塞；snapshot、worker、upload分别记录。
- 四组是保守原型预算，尚未以真实生产混编四组并行负载做最终性能签收，不提高上限。

## 4. 最终候选自动检查

| 运行 | 通过/总数 | 跳过 |
|---|---:|---:|
{rows}

EditMode包含原703项与12项新布局、快照、当前坐标、存活、半径、非法请求、分布到达与Hold规划测试。局部GPU五项覆盖本报告第1节；旧内核67项在非局部路径回归。P4/P5只补测试runner的P6隔离参数兼容，不改变交互合同或宣称真人验收。

### 历史失败如实保留

1. local-01：4项失败。新增回执使D3D11主战斗核UAV从8增至9，核未正常执行；不能把这一轮移动断言算有效通过。
2. 修正为独立ObserveLocalOrders核，受限频率观察执行revision并异步回读，不再增加主战斗核UAV。local-02：3通过、1请求在冷编译期间超时。
3. scene-02：新增Move观察断言按35模拟帧等待，但回执按0.2秒墙钟采样，快速batch迭代未等到下一回执；改为5秒有界实际观察等待，仍要求执行人数大于0。
4. critical-01误用PlayMode筛选，匹配0项，runner明确判未通过，不计入通过数；占点默认命令、输入安全、战斗周期三类实际是EditMode，由最终715项全套覆盖。
5. local-03及后续最终候选完整复验；没有提高10秒超时、删除失败日志或把CPU已提交冒充GPU执行。

## 5. 关闭／空覆盖的批处理诊断，不是正式FPS签收

单场景ABBA：关闭 → 开启空覆盖 → 开启空覆盖 → 关闭，每窗20秒、暖机3秒、固定模拟delta。下面是batchmode场景协程相邻迭代的墙钟毫秒，没有present或实际绘制保证，不是GPU timestamp，也不能取倒数宣传为玩家FPS。

| 顺序／状态 | 样本帧 | 中位ms | p95 ms | 平均ms |
|---|---:|---:|---:|---:|
{times}

**本轮未达到“可声明无明显回退”的证据标准。** 两窗平均中位间隔：关闭{avg(off,"medianMs"):.4f}ms、开启空覆盖{avg(empty,"medianMs"):.4f}ms，约增加{medianDelta:.2f}%。关闭末窗相对首窗也有漂移，不能把差异直接归因于某一函数；但不得忽略或写成性能通过。后续应检查空路径的重复keyword调用／上下文检查开销，用重复对照与Profiler建立因果，而不是直接猜改。

这些数据只用于检查批处理调度退化；单次短ABBA不足以签收P6性能。scene-01的5秒预检也保留，不用它挑选更好数字。

## 6. 尚未完成／下一步仅限P6

1. 实际生产male、knight、远程组合的局部Move/Hold与目标策略，以及四组同时活跃、反复换目标的代表性场景；当前混合GPU测试是两个fixture类型、不同半径，不能等同全部正式混编覆盖。
2. 真实呈现环境下关闭／空覆盖／四组活跃的重复性能对照，CPU/GPU timing与资源峰值；根据结果最终确认4组预算。不能用上述batch协程间隔替代。
3. 扩展真实运行时地形／障碍变化、在途句柄压力与故障注入。已有回调失效与reset复用测试，不等于所有设备故障和动态几何组合验收。
4. 以上未过不接P7。P4/P5真人待验与P3暂缓项保持各自原状态，不混写为P6通过。

## 7. 保护与位置

全量9659文件基线＋24个批准source/meta路径核对通过；既有生产源6处、旧fixture兼容2处，其余为新增原型／测试及meta。正式48、用户4份数据、37包387文件、HEAD／暂存区保持；无重复GUID，462项归档逐项核验，清单SHA `{a['archive']['manifestSha256']}`。没有关闭用户程序、移除未知锁、提交git或产出38。

源码在当前电脑Unity工程；证据和失败历史在 `Logs/InteractionRefinement-20261007/P6/`，受控载荷与runner在 `Tools/InteractionRefinement/`。报告在 `Docs/InteractionRefinement-20261007/P6-REPORT.md/html`。

状态：**P6_FUNCTIONAL_PROTOTYPE_PENDING_QUALIFICATION**。这是一份可追踪的原型交接，不是P6完工或P7放行单。
'''
(doc/'P6-REPORT.md').write_text(text,encoding='utf-8')
parts=[];table=False
for line in text.splitlines():
 if line.startswith('|'):
  if not table:parts.append('<table>');table=True
  if not re.fullmatch(r'[|:\-\s]+',line):parts.append('<tr>'+''.join('<td>'+html.escape(c.strip())+'</td>' for c in line.strip('|').split('|'))+'</tr>')
  continue
 if table:parts.append('</table>');table=False
 value=re.sub(r'\*\*(.+?)\*\*',r'<strong>\1</strong>',re.sub(r'`([^`]+)`',r'<code>\1</code>',html.escape(line)))
 if line.startswith('### '):parts.append('<h3>'+value[4:]+'</h3>')
 elif line.startswith('## '):parts.append('<h2>'+value[3:]+'</h2>')
 elif line.startswith('# '):parts.append('<h1>'+value[2:]+'</h1>')
 elif line.strip():parts.append('<p>'+value+'</p>')
(doc/'P6-REPORT.html').write_text('<!doctype html><html lang="zh-CN"><meta charset="utf-8"><title>P6原型检查点</title><style>body{max-width:1050px;margin:45px auto;padding:0 24px;color:#203047;background:#f5f7fa;font:16px/1.85 system-ui,sans-serif}h1,h2{color:#174e78}h2{margin-top:42px;border-bottom:1px solid #ccd7df}table{border-collapse:collapse;width:100%;background:white}td{padding:9px;border:1px solid #d8e0e8}code{overflow-wrap:anywhere;font-size:13px}strong{color:#944011}</style>'+''.join(parts)+'</html>',encoding='utf-8')
p=doc/'IMPLEMENTATION.md';s=p.read_text(encoding='utf-8');s=s.replace('状态：**P5尺寸手柄与参数联动已实现并通过自动检查；B阶段真人体验待验。P3余项继续暂缓，正式48保持；未P6及后续，未38。**','状态：**P6独立成员命令原型已实现并通过本轮功能自动检查；生产混编／四组负载与正式性能资格待补验，不放行P7。P4/P5真人待验，P3余项暂缓、正式48保持；未P7/P8或38。**');s=s.replace('| P6 | 局部命令与导航最小原型 | B 阶段验收通过 | 同军团两组可独立行动，不改敌我关系 | 未开始 |','| P6 | 局部命令与导航最小原型 | 用户已明确授权，不以B真人待验阻塞原型 | 同军团两组可独立行动，不改敌我关系 | 功能原型检查通过；性能／生产混编待验 |');s+='\n\n## P6原型检查点（2026-10-07）\n\n用户明确授权P6，取代旧文“未P6”边界，不等于P4/P5真人签收。见[P6报告](P6-REPORT.md)。当前状态P6_FUNCTIONAL_PROTOTYPE_PENDING_QUALIFICATION；独立buffer、事务槽与执行观察原型已有自动证据，正式生产混编与性能仍待补验，未P7/P8、未38。P3暂缓与48保持。\n';p.write_text(s,encoding='utf-8')
(doc/'P3-QUALIFICATION-STATE.md').write_text('# 当前交互整改状态\n\nP6已授权：独立命令原型功能自动检查通过，生产混编／四组负载与正式性能待补验；未P7/P8或38。详见[P6报告](P6-REPORT.md)。P4/P5真人待验，P3余项暂缓而非修复，正式48保持。\n',encoding='utf-8')
files=[doc/n for n in ['P6-REPORT.md','P6-REPORT.html','IMPLEMENTATION.md','P3-QUALIFICATION-STATE.md']]
r={'status':'P6_FUNCTIONAL_PROTOTYPE_PENDING_QUALIFICATION','publishedAt':datetime.datetime.now().isoformat(),'files':[{'path':p.relative_to(ROOT).as_posix(),'bytes':p.stat().st_size,'sha256':sha(p)} for p in files],'project':project_check(),'userData':user_check(),'activeProcesses':processes(),'lock':(ROOT/'Temp/UnityLockfile').exists(),'p6FullyAccepted':False,'p7Started':False,'newBuildProduced':False};assert r['project']['passed'] and r['userData']['passed'] and not r['activeProcesses'] and not r['lock'];save(P6/'publication.json',r);print(json.dumps(r,ensure_ascii=False,indent=2))
