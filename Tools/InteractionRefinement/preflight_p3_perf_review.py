from common_p3 import *
import sys,csv,math,importlib.util,shutil
sys.stdout.reconfigure(encoding='utf-8');out=P3/'perf-review-preflight-01.json';assert not out.exists()
r={'checkedAt':datetime.datetime.now().isoformat(),'activeProcesses':processes(),'lock':(ROOT/'Temp/UnityLockfile').exists(),'project':project_check(),'userData':user_check(),'psutilAvailable':importlib.util.find_spec('psutil') is not None,'freeDiskGB':shutil.disk_usage(ROOT).free/1024**3,'oldRuns':[]}
for tag in ['qualification-perf24-a','qualification-perf48-a','qualification-perf48-b','qualification-perf24-b']:
 with (P3/tag/'frames.csv').open() as f:rows=list(csv.DictReader(f))
 intervals=[]
 for a,b in zip(rows,rows[1:]):
  ms=1000*(float(b['seconds'])-float(a['seconds']));intervals.append({'seconds':float(b['seconds']),'ms':ms,'mainThreadMs':float(b['mainThreadMs']),'gpuTimingMs':float(b['gpuTimingMs']),'gcAllocatedBytes':int(b['gcBytes'])})
 r['oldRuns'].append({'tag':tag,'samples':len(rows),'over33ms':sum(x['ms']>33.333 for x in intervals),'over50ms':sum(x['ms']>50 for x in intervals),'worst':sorted(intervals,key=lambda x:x['ms'],reverse=True)[:8]})
r['passed']=not r['activeProcesses'] and not r['lock'] and r['project']['passed'] and r['userData']['passed'] and r['freeDiskGB']>5
save(out,r);print(json.dumps(r,ensure_ascii=False,indent=2));sys.exit(0 if r['passed'] else 1)
