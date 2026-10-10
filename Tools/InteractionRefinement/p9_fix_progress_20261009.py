from pathlib import Path
import json,hashlib
R=Path(__file__).resolve().parents[2];D=R/'outputs/P9ColdFix-20261009-01'
a=json.loads((D/'baseline-audit.json').read_text(encoding='utf-8'));assert a['motherProtectionPassed'] and a['candidateSourceExact']
g=json.loads((D/'baseline-golden-01.json').read_text(encoding='utf-8'));assert g['passed']
rows=[]
for name in ['compiler-screen-v2.json','compiler-screen-v3.json','compiler-screen-v4.json','compiler-screen-v5.json','compiler-screen-v6.json','compiler-ablation-v7.json','compiler-screen-v8.json']:
 for x in json.loads((D/name).read_text(encoding='utf-8')):rows.append({'file':name,'name':x['name'],'seconds':x['seconds'],'bytes':x['bytes'],'sha256':x['sha256'],'analysisOnlyAblation':x.get('NOT_A_FIX',False)})
state={'networkRestored':True,'project':'outputs/P12','productionFixApplied':False,'candidateAccepted':False,'goldenBaseline':{'passed':3,'failed':0,'seconds':8.7608777},'fullProtectionAuditPassed':True,'screening':rows,'warning':'Native O3 CPU-only screening is not Unity cold-command timing. Some runs overlapped copy/hash work. Ablations are intentionally nonfunctional and MUST NEVER be deployed.'}
(D/'progress.json').write_text(json.dumps(state,ensure_ascii=False,indent=2),encoding='utf-8')
text='''## 2026-10-09 修复推进：网络已恢复，尚无可验收修复
- 用户已明确授权“修复，开干”；后续“网络波动再试试”已重连成功，不再是连接阻断状态。
- 新独立工程 outputs/P12：9711来源+原2夹具，独立复制Library种子，无硬链接/共享可写文件；原P11未改。
- 修改前战斗黄金基线 baseline-golden-01 已3/3通过，0失败，8.7608777秒。运行器 p9_fix_run_v2_20261009.py；进一步回归清单58方法/60case已准备但未执行。
- CPU-only D3DCompile O3筛查：原版62.75秒；11种局部调整约55–80秒，均未解决分钟级编译，未采用或部署至P12。不能把这些数值当成Unity命令实测。
- 去除功能的编译热点消融：省略地形穿格检查约14.11秒、省略远程弹道检查32.60秒、省略邻域查询49.92秒。这些是故意不完整的分析样本，**绝不能作为修复部署**；影响并非可加性。
- fastopt三个试验的DXBC与原版完全相同；不要重复走这一无效路径。更改单一循环、写回合并、射界调用合并、自身命令缓存等均没有达到目标。
- 下一步应针对地形穿格/射界逻辑在大kernel中的重复内联及编译路径做有依据的结构性方案；保留原墙体/切角/边界安全和战斗节拍。不能删除判定、关闭全局优化或把65秒隐藏进加载来冒充修复。
- full SHA baseline-audit通过：母/37 10094保护文件、HEAD/index、7用户文件不变；P12 9711来源和原夹具精确，无额外未知文件，无Unity/Player。P12目前仍是未改生产代码的基线，patch-manifest.json尚不存在。
- 证据 outputs/P9ColdFix-20261009-01/{prepare,baseline-golden-01,baseline-audit,progress}.json 与 compiler-screen-v*.json；CPU筛查源码仅在compiler-probes*下。
- 所有本轮已启动Unity和CPU筛查进程均已结束。无38、无提交/合并、37不动、Burst仍默认关闭；P3/P9不关闭。

'''
(D/'PROGRESS.md').write_text(text,encoding='utf-8')
n=R/'NEXT_TASK.md';backup=D/'NEXT_TASK-before-repair-progress.md';assert not backup.exists();backup.write_bytes(n.read_bytes());old=n.read_text(encoding='utf-8');i=old.index('\n')+1;n.write_text('# 最新接力：冷态 Attack 修复已恢复推进；尚无有效候选\n\n'+text+old[i:],encoding='utf-8')
b=json.loads((R/'outputs/P9Combined-20261009-01/mother-before.json').read_text(encoding='utf-8'));assert hashlib.sha256((R/'.git/index').read_bytes()).hexdigest()==b['index']
print('Repair progress recorded. Network restored, baseline protected, no fix claimed.')
