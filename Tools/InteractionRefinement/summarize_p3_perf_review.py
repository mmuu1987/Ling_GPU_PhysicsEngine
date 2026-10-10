from common_p3 import *
import sys
sys.stdout.reconfigure(encoding='utf-8');m=json.loads((P3/'perf-review-01/analysis.json').read_text(encoding='utf-8'))
for r in m['runs']:
 e=r.get('environment',{});print(json.dumps({'tag':r['tag'],'over50msCount':r['over50msCount'],'longFrameGapMedianSeconds':r['longFrameGapMedianSeconds'],'lagCorrelation':r['lagCorrelation'],'cpuMedianPct':e.get('wholeRunSystemCpuMedianPct'),'cpuP95Pct':e.get('wholeRunSystemCpuP95Pct'),'cpuMaxPct':e.get('wholeRunSystemCpuMaxPct'),'topSurvivingProcesses':e.get('survivingProcessesByCpuDelta',[])[:4]},ensure_ascii=False))
