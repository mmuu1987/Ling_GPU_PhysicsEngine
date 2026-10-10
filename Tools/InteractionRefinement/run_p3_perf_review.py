from common_p3 import *
import sys,time,threading,ctypes,re
from ctypes import wintypes as wt
sys.stdout.reconfigure(encoding='utf-8');dest=P3/'perf-review-01';assert not dest.exists();assert not processes() and not(ROOT/'Temp/UnityLockfile').exists();assert project_check()['passed'] and user_check()['passed'];dest.mkdir()
order=[48,24,24,48,24,48,48,24]
scope={'createdAt':datetime.datetime.now().isoformat(),'order':order,'runs':8,'sameUnchangedObserverAndRunner':True,'warmupSeconds':35,'measurementSeconds':15,'resolution':[1920,1080],'productionChanges':False,'oldRunsRetained':True,'thresholds':{'betweenStrengthIncreaseReviewPct':15,'withinStrengthRangeReviewPct':20},'decision':'Compare new block separately; retain old block and all outliers. No claim of statistical significance or root cause from timing counters. P95/P99/long-frame rates and wall throughput all reported. Persistent variance stays open, no automatic P4/build.','monitor':'Windows GetSystemTimes at 1Hz, buffered until run ends; no process termination or configuration tuning. Entire run telemetry, not tightly aligned with 35-50s window.'}
save(dest/'scope.json',scope)
ps="[Console]::OutputEncoding=[System.Text.UTF8Encoding]::new(); Get-Process | Select-Object ProcessName,Id,CPU | ConvertTo-Json -Compress"
def context():
 r=subprocess.run(['powershell','-NoProfile','-Command',ps],capture_output=True,encoding='utf-8',timeout=45);power=subprocess.run(['powercfg','/getactivescheme'],capture_output=True,timeout=15)
 return {'at':datetime.datetime.now().isoformat(),'processCpuCumulative':json.loads(r.stdout) if r.returncode==0 and r.stdout.strip() else [],'powerPlan':power.stdout.decode('utf-8',errors='replace')}
k=ctypes.WinDLL('kernel32',use_last_error=True);k.GetSystemTimes.argtypes=[ctypes.POINTER(wt.FILETIME)]*3;k.GetSystemTimes.restype=wt.BOOL
class Monitor:
 def __init__(self):self.stop=threading.Event();self.rows=[];self.thread=threading.Thread(target=self.run,daemon=True)
 def run(self):
  prev=None
  while not self.stop.is_set():
   vals=[wt.FILETIME() for _ in range(3)];ok=k.GetSystemTimes(*[ctypes.byref(v) for v in vals]);v=[(x.dwHighDateTime<<32)|x.dwLowDateTime for x in vals]
   if ok and prev:
    idle=v[0]-prev[0];total=v[1]+v[2]-prev[1]-prev[2]
    if total>0:self.rows.append({'utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'cpuBusyPct':100*(total-idle)/total})
   prev=v;self.stop.wait(1)
 def start(self):self.thread.start()
 def finish(self):self.stop.set();self.thread.join();return self.rows
runs=[]
for i,strength in enumerate(order):
 tag=f'perf-review-{i+1:02d}-{strength}';assert not(P3/tag).exists();assert not processes() and not(ROOT/'Temp/UnityLockfile').exists()
 before=context();mon=Monitor();mon.start();args=[sys.executable,'-X','utf8',str(ROOT/'Tools/InteractionRefinement/run_p3_rendered.py'),'performance',tag,'gui',str(strength)]
 print('STEP',i+1,tag,flush=True);start=datetime.datetime.now().isoformat()
 try:r=subprocess.run(args,cwd=ROOT,stdout=(dest/(tag+'-runner.log')).open('w',encoding='utf-8'),stderr=subprocess.STDOUT)
 finally:telemetry=mon.finish()
 after=context();receipt={'tag':tag,'strength':strength,'startedAt':start,'finishedAt':datetime.datetime.now().isoformat(),'exitCode':r.returncode,'before':before,'after':after,'systemCpuSamples':telemetry};save(dest/(tag+'-environment.json'),receipt);runs.append({'tag':tag,'exitCode':r.returncode});save(dest/'sequence.json',{'scope':scope,'runs':runs,'complete':len(runs)==8 and all(x['exitCode']==0 for x in runs)})
 print('FINISHED',tag,'EXIT',r.returncode,flush=True)
 if r.returncode:sys.exit(r.returncode)
print('ALL_EIGHT_COMPLETE',flush=True)
