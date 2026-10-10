from common_p8 import *
import sys,runpy,csv,math,statistics
sys.stdout.reconfigure(encoding='utf-8')
P3=LOG/'P3'; dest=P3/'reopen-20261008-01'
assert not dest.exists(),'Never overwrite evidence'
assert not processes() and not (ROOT/'Temp/UnityLockfile').exists(),'Editor/player active; do not close'
assert project_check(full=True)['passed'] and user_check()['passed'],'Current P8 protection baseline mismatch'
dest.mkdir()
source=ROOT/'Assets/Game/Editor/InteractionRefinementP3Qualification.cs'
original=source.read_bytes(); (dest/'observer-original.bin').write_bytes(original)
sourceRel=source.relative_to(ROOT).as_posix()
originalHash=sha(source)
oldRunner=(ROOT/'Tools/InteractionRefinement/run_p3_rendered.py').read_text(encoding='utf-8')
# P3's old hash registry predates P4-P8. Use current P8 protection registry; narrowly permit only our exact observer bytes.
oldRunner=oldRunner.replace('from common_p3 import *','from common_p8 import *\nP3=LOG/"P3"\n_base_check=project_check\ndef project_check(full=False):\n r=_base_check(full)\n p=ROOT/"Assets/Game/Editor/InteractionRefinementP3Qualification.cs"\n expected=json.loads((P3/"reopen-20261008-01/allowed-observer.json").read_text(encoding="utf-8"))["sha256"]\n if sha(p)==expected:\n  r["changed"]=[x for x in r["changed"] if x["path"]!=p.relative_to(ROOT).as_posix()]\n r["passed"]=not r["changed"] and not r["unexpectedNew"] and sha(p)==expected\n return r')
runner=dest/'runner.py';runner.write_text(oldRunner,encoding='utf-8')
text=original.decode('utf-8')
def replace_once(old,new):
 global text
 assert text.count(old)==1,old
 text=text.replace(old,new,1)
replace_once('public long gcBytes;', 'public long gcBytes;\n            public double flowTotalMs, flowCompleted, observerReadUs; public bool focused;')
replace_once('FrameTimingManager.CaptureFrameTimings();', 'long observerStart = System.Diagnostics.Stopwatch.GetTimestamp();\n            var observedController = WarSandboxSceneSession.Instance != null ? WarSandboxSceneSession.Instance.Controller : null;\n            double flowMs = observedController != null ? observedController.manager.TerrainTotalSolveMilliseconds : -1;\n            double flowCount = observedController != null ? observedController.manager.TerrainCompletedFields : -1;\n            double readUs = (System.Diagnostics.Stopwatch.GetTimestamp() - observerStart) * 1000000.0 / System.Diagnostics.Stopwatch.Frequency;\n            FrameTimingManager.CaptureFrameTimings();')
replace_once('gpuTimingMs = gpu, gcBytes = gc.Valid ? gc.LastValue : -1 });', 'gpuTimingMs = gpu, gcBytes = gc.Valid ? gc.LastValue : -1, flowTotalMs = flowMs, flowCompleted = flowCount, observerReadUs = readUs, focused = Application.isFocused });')
replace_once('gpuTimingMs,gcBytes");','gpuTimingMs,gcBytes,flowTotalMs,flowCompleted,observerReadUs,focused");')
replace_once('r.gcBytes.ToString(Inv)));','r.gcBytes.ToString(Inv), r.flowTotalMs.ToString(Inv), r.flowCompleted.ToString(Inv), r.observerReadUs.ToString(Inv), r.focused ? "1" : "0"));')
instrumented=text.encode('utf-8');(dest/'observer-instrumented.bin').write_bytes(instrumented)
state={'status':'running','scope':'Current P8 source; 2048 agents; separation 48; original-observer/instrumented/instrumented/original-observer. No flow or HUD tuning. 35s warmup and 15s rendered samples per run. Diagnostic correlation is not causal proof.','runs':[],'humanAcceptance':False,'productFix':False}
save(dest/'sequence.json',state)
def quantile(values,q):
 a=sorted(values);return a[min(len(a)-1,math.ceil(len(a)*q)-1)] if a else None
def analyze(folder):
 rows=list(csv.DictReader((folder/'frames.csv').open(encoding='utf-8')))
 intervals=[1000*(float(b['seconds'])-float(a['seconds'])) for a,b in zip(rows,rows[1:])]
 result={'samples':len(rows),'wallMedianMs':quantile(intervals,.5),'wallP95Ms':quantile(intervals,.95),'wallP99Ms':quantile(intervals,.99),'over50ms':sum(x>50 for x in intervals),'over50Seconds':[float(rows[i+1]['seconds']) for i,v in enumerate(intervals) if v>50]}
 if rows and 'flowTotalMs' in rows[0]:
  deltas=[float(b['flowTotalMs'])-float(a['flowTotalMs']) for a,b in zip(rows,rows[1:])]
  events=[float(b['flowCompleted'])-float(a['flowCompleted']) for a,b in zip(rows,rows[1:])]
  result['lagAlignment']={}
  for lag in [-2,-1,0,1,2]:
   pairs=[(v,deltas[i+lag],events[i+lag]) for i,v in enumerate(intervals) if 0<=i+lag<len(deltas)]
   result['lagAlignment'][str(lag)]={'longFrames':sum(v>50 for v,d,e in pairs),'longWithFlowEvent':sum(v>50 and e>0 for v,d,e in pairs),'flowEventFrames':sum(e>0 for v,d,e in pairs),'flowSolveMedianOnLongMs':quantile([d for v,d,e in pairs if v>50],.5),'flowSolveMedianOtherMs':quantile([d for v,d,e in pairs if v<=50],.5)}
  result['observerReadUsP95']=quantile([float(r['observerReadUs']) for r in rows],.95)
  result['focusSamples']=sum(r['focused']=='1' for r in rows)
  result['longFrameDetails']=[{'frame':rows[i+1]['frame'],'seconds':rows[i+1]['seconds'],'wallMs':v,'flowSolveDeltaMs':deltas[i],'flowCompletedDelta':events[i],'mainThreadMs':rows[i+1]['mainThreadMs'],'gcBytes':rows[i+1]['gcBytes']} for i,v in enumerate(intervals) if v>50]
 return result
try:
 for index,observed in enumerate([False,True,True,False]):
  assert not processes() and not (ROOT/'Temp/UnityLockfile').exists()
  expected=original if not state['runs'] or not state['runs'][-1]['instrumented'] else instrumented
  assert source.read_bytes()==expected,'Concurrent source change; do not overwrite'
  source.write_bytes(instrumented if observed else original)
  save(dest/'allowed-observer.json',{'sha256':sha(source),'originalSha256':originalHash,'instrumented':observed})
  tag='reopen-20261008-01-'+str(index+1)+('-observed' if observed else '-baseline')
  args=[sys.executable,'-X','utf8',str(runner),'performance',tag,'gui','48']
  print('STEP',tag,flush=True)
  env=os.environ.copy();env['PYTHONPATH']=str(ROOT/'Tools/InteractionRefinement')+os.pathsep+env.get('PYTHONPATH','')
  with (dest/(tag+'.log')).open('w',encoding='utf-8') as out:
   r=subprocess.run(args,cwd=ROOT,env=env,stdout=out,stderr=subprocess.STDOUT,timeout=900)
  row={'tag':tag,'instrumented':observed,'exitCode':r.returncode}
  state['runs'].append(row);save(dest/'sequence.json',state)
  if r.returncode:raise RuntimeError('Runner failed; inspect '+tag)
  row['analysis']=analyze(P3/tag);save(dest/'sequence.json',state)
  print(json.dumps(row,ensure_ascii=False),flush=True)
 state['status']='diagnostic_capture_complete_not_root_cause_acceptance'
except Exception as e:
 state['status']='blocked_or_failed';state['error']=str(e)
 raise
finally:
 # Restore only our exact observer bytes and only after owned Editor has exited.
 if not processes() and not (ROOT/'Temp/UnityLockfile').exists() and source.read_bytes() in [original,instrumented]:
  source.write_bytes(original);state['observerRestored']=sha(source)==originalHash
 else:state['observerRestored']=False
 state['finalProjectCheck']=project_check(full=True);state['finalUserCheck']=user_check()
 save(dest/'sequence.json',state)
 print('FINAL',json.dumps(state,ensure_ascii=False),flush=True)
