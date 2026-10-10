from common_p0 import *
import sys,time
sys.stdout.reconfigure(encoding='utf-8')
assert not(P0/'suite.json').exists()
summary={'phase':'P0','passed':False,'runs':[],'newBuildProduced':False}
deadline=time.monotonic()+1200
while time.monotonic()<deadline:
 p=P0/'editmode-01/process.json'
 if p.exists():
  d=json.loads(p.read_text(encoding='utf-8'))
  if d.get('finishedAt'):
   summary['runs'].append({'tag':'editmode-01','passed':d['passed']})
   if not d['passed']:
    summary['blockedBy']='EditMode baseline failure';save(P0/'suite.json',summary);print(json.dumps(summary),flush=True);sys.exit(1)
   break
 time.sleep(5)
else:raise TimeoutError('EditMode receipt still pending')
for mode,tag in [('capture','capture-01'),('move','move-01'),('hold','hold-01'),('retreat','retreat-01'),('performance','performance-01'),('performance','performance-02'),('performance','performance-03')]:
 print('START',tag,flush=True)
 result=subprocess.run([sys.executable,'-X','utf8',str(ROOT/'Tools/InteractionRefinement/run_p0.py'),mode,tag],cwd=ROOT,capture_output=True,encoding='utf-8',errors='replace',timeout=1050)
 (P0/(tag+'-runner.log')).write_text(result.stdout+'\n'+result.stderr,encoding='utf-8')
 summary['runs'].append({'tag':tag,'passed':result.returncode==0})
 save(P0/'suite-progress.json',summary)
 print('FINISH',tag,'exit',result.returncode,flush=True)
 if result.returncode!=0:
  summary['blockedBy']=tag;save(P0/'suite.json',summary);sys.exit(1)
summary['passed']=True;save(P0/'suite.json',summary);print(json.dumps(summary,ensure_ascii=False,indent=2),flush=True)
