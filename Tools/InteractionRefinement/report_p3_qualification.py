from common_p3 import *
import sys,statistics as st,html
sys.stdout.reconfigure(encoding='utf-8')
assert not processes() and not(ROOT/'Temp/UnityLockfile').exists()
a=json.loads((P3/'qualification-audit-01.json').read_text(encoding='utf-8'));m=json.loads((P3/'qualification-analysis-01.json').read_text(encoding='utf-8'))
assert a['protectionPassed'] and a['captureAndRegressionPassed'];doc=ROOT/'Docs/InteractionRefinement-20261007';assert not(doc/'P3-QUALIFICATION-REPORT.md').exists()
w=json.loads((P3/'qualification-wall-pacing-01.json').read_text(encoding='utf-8'));assert w['status']=='WALL_CLOCK_INTERVALS_RECOMPUTED'
flags=m['performanceReviewFlags']+m['motionReviewFlags']+m['runNoiseFlags']+w['reviewFlags']
assert m['needsReview'] or not w['noiseFlags'], 'Update review classification before publishing'
if not m['gpuTimingComparisonAvailable']:flags.append('GPU FrameTiming细分不可用，不能当成零耗时')
displayFlags=[x.replace('48 frameP95Ms run-to-run spread >20%','48两轮P95帧时为9.427与7.182ms，相对较小值波动31.3%，超过20%重复噪声复核线') for x in flags]
review='需继续复核：'+'；'.join(displayFlags) if flags else '本轮量化检查未触发预设复核线；仍待真人观感和阶段签收'
def fmt(x):return f'{x:.3f}' if isinstance(x,(float,int)) else str(x)
krows=[]
for r in m['kernels']:
 if r['kind'].startswith('corridor'):
  krows.append('| '+' | '.join([('男战士' if r['template']==0 else '剑盾骑士'),str(r['strength']),fmt(r['allPassedAtSeconds']),fmt(r['maxSide']),fmt(r['reverseProgress'])])+' |')
cmdrows=[]
for kind,label in [('move','移动'),('hold','待命'),('retreat','撤退')]:
 r=m['commands'][kind];x=next(x for x in r['phases'] if x['phase']==kind);end=next(x for x in r['phases'] if x['phase']=='attack_again')
 cmdrows.append('| '+' | '.join([label,str(x['gpuStance']),fmt(x['meanDisplacement']),fmt(x['meanGoalProgress']),fmt(x['contactGoalProgress']),str(x['attacking']),str(end['attacking'])])+' |')
mo=m['motionComparison'];motionrows=[]
for v in ['reference-24','official-48']:
 x=mo[v];motionrows.append('| '+' | '.join([v,f'{x["meanSpeed"]:.6f}',f'{x["maxStep"]*1000:.3f}',f'{x["reverseEventsPerAgentSecond"]:.6f}',f'{x["movingPresentationPct"]:.4f}'])+' |')
perfrows=[]
for v in ['24','48']:
 x=m['performanceComparison'][v];perfrows.append('| '+' | '.join([v,fmt(x['frameMedianMs']),fmt(x['frameP95Ms']),fmt(x.get('gpuMedianMs','不可用')),fmt(x.get('gpuP95Ms','不可用'))])+' |')
wallRows=['| '+x['tag'].replace('qualification-perf','')+' | '+fmt(x['wallMedianMs'])+' | '+fmt(x['wallP95Ms'])+' | '+fmt(x['wallP99Ms'])+' | '+fmt(x['renderedSamplesPerSecond'])+' |' for x in w['runs']]
deltas='；'.join(k+f' {v:+.1f}%' for k,v in m['performanceDeltasPct'].items())
report=f'''# P3 补验检查点：窄路、指令、持续动作与独立帧时

2026-10-07。**{review}。P3/A阶段未自动签收，不进入P4，不出38。**

## 本轮实际范围

你已采纳的男战士／剑盾骑士分离强度48保持不变。已接受的战斗时长与存活取舍不再单独作为否决项；但指令失效、通行卡死、明显抖动或帧时回退仍需分别检查。本轮只扩展自有测试和Editor观察器，没有再次改变生产参数、生产Shader或交战逻辑。

原始数据均直接留在你电脑的 `Logs/InteractionRefinement-20261007/P3/qualification-*`，助手工作空间仍只保留小索引和临时工具，不把整套CSV／图片再下载一遍。

## 1. 真实GPU窄路／接触／弹道：41/41通过

新用例从两份真实官方配置克隆移动、战斗、分离参数，只将比较值设为24或48。半径0.55m、scale1不变。

这是**8个GPU代理中的4人微型队列**，固定1.6m走廊、真实静态障碍。关闭门时自然进入等待；只移除门，不改墙宽、半径、人数；4人须在12模拟秒内全部清出出口，不能穿墙。另检查暂停位置不变、反向新命令的实际推进。它不等同于2048人在所有地形瓶颈中的吞吐测试。

| 角色 | 分离强度 | 开门后4人通过/模拟秒 | 通道内最大中心横偏/m | 反向平均推进/m |
|---|---:|---:|---:|---:|
{chr(10).join(krows)}

同轮包含近战距离／待命近身自卫、既有停等／命令唤醒以及地形弹道、射击轨迹等真实GPU用例，共41项，无失败／跳过。新的近战用例确认待命不是停火；测得的活目标接触距离不越过攻击距离加离散采样容差。未修改任何攻击距离或投射物半径。

## 2. 实际2048人场景：移动、待命、撤退和重新攻击

通过正式入口进入Green默认部署；先达到真实交战，再下达相应整军指令并观察8秒，然后重新攻击。使用API／UI事件，不冒充OS鼠标键盘人工验收。三条完整流程均通过。

| 指令 | GPU姿态 | 平均位移/m | 平均目标推进/m | 原交战队列推进/m | 指令末攻击人数 | 再攻击后攻击人数 |
|---|---:|---:|---:|---:|---:|---:|
{chr(10).join(cmdrows)}

移动／撤退要求结束时攻击和接敌追击计数为0、整军目标推进>6m、存活交战队列推进>4m；待命允许近身自卫和避让，平均位移须<2m；再攻击必须出现真实攻击状态。每次采样还检查活兵所在位置可通行。上述是本轮覆盖范围，不是所有地图／指令组合都已穷尽。

## 3. 20秒持续动作窗口：与性能测量严格分开

默认2048人，行军30模拟秒造成汇聚后下达待命，缓冲2秒，再连续记录20秒。24／48各2轮，每轮1200个60Hz步骤。每帧统计军团0的1024人；另对固定64个前排代理保存原始位置、速度、表现状态二进制轨迹。共4800帧全体统计、307200个独立小轨迹样本重算通过。

| 条件 | 平均速度/m·s⁻¹ | 每轮最大单步位移的均值/mm | 反向事件/人·秒 | Move表现状态占比/% |
|---|---:|---:|---:|---:|
{chr(10).join(motionrows)}

反向事件定义为相邻速度均>0.05m/s且夹角>120°；原始轨迹另行独立复算。0.25m/步为异常跳位防线，不是“看起来不抖”的标准。复核线另看48相对24的位移／反向代理指标增加。本窗口有同步回读，**不能用于帧率比较**；它也不测VAT网格、骨骼或武器视觉摆动，仍不能替代真人近景判断。

## 4. 独立渲染帧时：24／48／48／24顺序

自有可视Editor，1920×1080，真实时钟，原相机固定、原模拟LOD与渲染设置保持；默认2048人。每轮重新开战，热身35秒，再测35–50秒接敌期。GPU单位参数仅在计时前读取核对，观察器在采样窗口不额外做Agent同步回读、不截图、不导出大文件；生产循环自身的行为保持原样。测试自有Listener用于减少夹具缺失Listener日志噪声，不是生产音频修复。

下表是Unity unscaledDeltaTime帧间隔及GPU计时，每个条件取两轮分位数的均值；不是由模拟战斗时长推算，也不与旧P0历史墙钟数据直接比较。

| 强度 | 帧时中位/ms | 帧时P95/ms | GPU中位/ms | GPUP95/ms |
|---|---:|---:|---:|---:|
{chr(10).join(perfrows)}

48相对24：{deltas}。

逐轮采样覆盖与限帧状态：{'; '.join(x['tag']+': '+str(x['frames'])+'帧，正GPU样本'+str(x['gpuSamples'])+'，VSync='+str(x['vSyncCount'])+'，targetFrameRate='+str(x['targetFrameRate']) for x in m['performanceRuns'])}。GPU分位数仅基于可用正样本，未将缺失项当0；它不是每帧完整覆盖。

另直接用相邻Game相机渲染完成回调的realtime时间戳独立复算；四轮帧号均连续，没有漏帧号。它包含Editor调度开销，不是显示器真正呈现时刻。

| 轮次 | 墙钟间隔中位/ms | 墙钟间隔P95/ms | 墙钟间隔P99/ms | 渲染样本/秒 |
|---|---:|---:|---:|---:|
{chr(10).join(wallRows)}

墙钟间隔中位均值{w['comparison']['24']['wallMedianMs']:.3f}→{w['comparison']['48']['wallMedianMs']:.3f}ms（{w['deltasPct']['wallMedianMs']:+.1f}%）；P95均值{w['comparison']['24']['wallP95Ms']:.3f}→{w['comparison']['48']['wallP95Ms']:.3f}ms（{w['deltasPct']['wallP95Ms']:+.1f}%）；渲染采样吞吐均值{w['comparison']['24']['renderedSamplesPerSecond']:.1f}→{w['comparison']['48']['renderedSamplesPerSecond']:.1f}/秒。墙钟P95也出现48重复波动>20%，并非换一种计时方式就能消除该复核项。追加收据：`qualification-wall-pacing-01.json`。

预先声明：帧中位／P95或GPU指标增加>15%应复核；同条件重复差异>20%时报告噪声，不作确定结论。GPU不可用时不能记成0。结果：**{review}**。两轮重复不足以宣称统计显著性；这是Editor固定场景测量，不是所有设备、地图或发行包性能保证。VSync／帧率上限等原值逐轮记录于performance.json，未通过改变这些设置粉饰结果。

## 5. 保护与回归

- 新鲜EditMode {a['runs']['qualification-editmode-01']['tests']['passed']}/{a['runs']['qualification-editmode-01']['tests']['total']}通过；41项GPU核验及持续动作采集通过；三项实战指令流程通过；四轮渲染性能采集完成。
- P3基线9627文件：既有生产资产仍只保留原先两份Flocking的24→48，逐字节差异核对；本轮无额外生产变化。另有10个自有诊断源/meta路径登记。
- 用户{a['userData']['before']}份数据、37包{a['current37']['files']}份文件SHA不变；HEAD／暂存区不变；无重复GUID；最终无Unity／工程锁。GUI观测器使用的编辑器布局等按日志备份恢复。
- P1/P2真人及音频事项未自动关闭；本轮缺少Listener日志按各run收据保留，测试加Listener不能当成音频修复。

## 6. 下一步门槛

本轮没有启动部署拖拽、局部框选或新手引导，也没有制作38。

真人需确认当前两角色的交战疏密、近景动画／抖动观感，以及P1/P2半径圈和编辑保存使用体验。微型走廊不能替代所有大部队瓶颈，小体型／非1缩放角色未在本次48调整范围中，也不能借本报告宣称全覆盖。本轮确有48的P95重复波动标记。下一步优先核对桌面负载与采样噪声，再受控复测；保留全部原始轮次，不静默剔除较差轮。该项与真人观感门槛通过后，再单独进入P4。

证据：`qualification-analysis-01.json`列出原始文件SHA、逐轮数值及限制；`qualification-audit-01.json`为全量保护审计。已有48效果截图继续使用此前的真实预览，不以静态截图替代本次连续动作数据。
'''
old=(doc/'IMPLEMENTATION.md').read_bytes();pub=json.loads((P3/'adopted-published-docs.json').read_text(encoding='utf-8'));expected=next(r['sha256'] for r in pub['files'] if r['path'].endswith('/IMPLEMENTATION.md'));assert __import__('hashlib').sha256(old).hexdigest()==expected,'Plan changed; refuse overwrite'
backup=P3/'qualification-docs-before';assert not backup.exists();backup.mkdir();(backup/'IMPLEMENTATION.md').write_bytes(old)
plan=old.decode('utf-8');plan=plan.replace('最新执行证据：','最新执行证据：[P3 补验检查点](P3-QUALIFICATION-REPORT.md) · ',1)
plan+='\n\n## P3 补验检查点（2026-10-07）\n\n41/41窄路、接触及相关GPU测试；实际2048人的移动／待命／撤退再攻击流程；24/48各两轮20秒持续动作窗口；四轮1920×1080独立渲染帧时及652项新鲜EditMode回归已完成。'+review+'。不将接受模拟战斗时长变化等同接受帧时回退；P3/A未自动签收，不P4、不38。详见P3-QUALIFICATION-REPORT.md。\n'
(doc/'IMPLEMENTATION.md').write_text(plan,encoding='utf-8');(doc/'P3-QUALIFICATION-REPORT.md').write_text(report,encoding='utf-8')
# Self-contained lightweight report; no bulk image re-download into the assistant workspace.
def inline(s):
 import re
 s=html.escape(s)
 s=re.sub(r'\*\*(.+?)\*\*',r'<strong>\1</strong>',s)
 return re.sub(r'`([^`]+)`',r'<code>\1</code>',s)
def render_md(src):
 import re
 lines=src.splitlines();parts=[];i=0
 while i<len(lines):
  line=lines[i].strip()
  if not line:i+=1;continue
  if line.startswith('|'):
   rows=[]
   while i<len(lines) and lines[i].strip().startswith('|'):
    raw=lines[i].strip();i+=1
    if re.fullmatch(r'[|:\-\s]+',raw):continue
    cells=raw.strip('|').split('|');tag='th' if not rows else 'td';rows.append('<tr>'+''.join('<'+tag+'>'+inline(c.strip())+'</'+tag+'>' for c in cells)+'</tr>')
   parts.append('<div class="table"><table>'+''.join(rows)+'</table></div>');continue
  if line.startswith('#'):
   level=min(4,len(line)-len(line.lstrip('#')));parts.append(f'<h{level}>'+inline(line[level:].strip())+f'</h{level}>')
  elif line.startswith('- '):parts.append('<p class="item">• '+inline(line[2:])+'</p>')
  else:parts.append('<p>'+inline(line)+'</p>')
  i+=1
 return ''.join(parts)
page='<!doctype html><html lang="zh-CN"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>P3补验检查点</title><style>body{margin:0;background:#edf2ef;color:#203b34;font:16px/1.8 system-ui}main{max-width:1050px;margin:auto;padding:30px}header{padding:30px;background:#17443a;color:white;border-radius:18px}section{padding:28px;margin:20px 0;background:white;border-radius:16px;overflow-wrap:anywhere}h1{font-size:30px;line-height:1.4}h2{margin-top:38px;padding-top:12px;border-top:1px solid #dce4df}table{width:100%;border-collapse:collapse;font-size:14px;text-align:left}td,th{padding:10px;border-bottom:1px solid #dce4df;min-width:80px}th{background:#edf2ef}.table{overflow-x:auto}code{background:#edf2ef;padding:2px 5px;font-size:13px}.warn{border-left:4px solid #d6a456;padding-left:16px}.item{margin:7px 0}@media(max-width:600px){main{padding:12px}section,header{padding:18px}}</style><main><header><p>P3 · SUPPLEMENTAL QUALIFICATION</p><h1>参数48保持。<br>补齐功能与帧时证据。</h1><p>41项GPU核验 · 2048人三项指令流程 · 4×20秒动作窗口 · 4轮独立帧时</p></header><section><p class="warn">'+html.escape(review)+'。未自动进入P4，未出38。</p>'+render_md(report)+'</section></main></html>'
(doc/'P3-QUALIFICATION.html').write_text(page,encoding='utf-8')
state=doc/'P3-QUALIFICATION-STATE.md';(backup/state.name).write_bytes(state.read_bytes());state.write_text('# P3补验最新状态\n\n'+review+'。正式48保持；P3整阶段仍未签收，未P4／38。\n\n当前权威：P3-QUALIFICATION-REPORT.md、P3-QUALIFICATION.html、Logs/InteractionRefinement-20261007/P3/qualification-analysis-01.json及qualification-audit-01.json。旧进行中状态已备份到qualification-docs-before。原始轨迹/CSV留电脑，工作空间只保留小索引。\n',encoding='utf-8')
paths=[doc/'IMPLEMENTATION.md',doc/'P3-QUALIFICATION-REPORT.md',doc/'P3-QUALIFICATION.html',state]
r={'publishedAt':datetime.datetime.now().isoformat(),'files':[{'path':p.relative_to(ROOT).as_posix(),'bytes':p.stat().st_size,'sha256':sha(p)} for p in paths],'reviewFlags':flags,'needsReview':m['needsReview'],'project':project_check(),'userData':user_check(),'activeProcesses':processes(),'lock':(ROOT/'Temp/UnityLockfile').exists()};save(P3/'qualification-published.json',r);assert r['project']['passed'] and r['userData']['passed'] and not r['activeProcesses'] and not r['lock'];print('PUBLISHED supplemental report; review=',m['needsReview'],flush=True)
