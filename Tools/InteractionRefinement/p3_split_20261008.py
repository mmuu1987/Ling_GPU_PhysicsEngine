from common_p8 import *
import sys,csv,statistics,math
sys.stdout.reconfigure(encoding='utf-8')
P3=LOG/'P3';dest=P3/'split-20261008-01';assert not dest.exists()
assert not processes() and not(ROOT/'Temp/UnityLockfile').exists()
assert project_check(full=True)['passed'] and user_check()['passed']
dest.mkdir()
paths=['Assets/Game/Editor/InteractionRefinementP3Qualification.cs','Assets/MassEngine/Terrain/TerrainNavigationRuntime.cs']
original={p:(ROOT/p).read_bytes() for p in paths}
for p,b in original.items():q=dest/'backup'/p;q.parent.mkdir(parents=True,exist_ok=True);q.write_bytes(b)
texts={p:b.decode('utf-8') for p,b in original.items()}
def sub(path,a,b):
 assert texts[path].count(a)==1,a
 texts[path]=texts[path].replace(a,b)
o,r=paths
sub(r,'public const string Keyword', 'public static double DiagnosticFlowMs, DiagnosticLaneMs, DiagnosticUploadMs;\n        public const string Keyword')
sub(r,'if(LaneApproach33 && dynamicEnemy) TerrainLaneApproach33.Apply(Navigation, goals, directions, stopRadius);','DiagnosticFlowMs += watch.Elapsed.TotalMilliseconds;\n            var laneWatch = System.Diagnostics.Stopwatch.StartNew();\n            if(LaneApproach33 && dynamicEnemy) TerrainLaneApproach33.Apply(Navigation, goals, directions, stopRadius);\n            DiagnosticLaneMs += laneWatch.Elapsed.TotalMilliseconds;')
sub(r,'buffers.flowFieldDirectionsBuffer.SetData(directions, 0, team * Navigation.CellCount, directions.Length);','var uploadWatch = System.Diagnostics.Stopwatch.StartNew();\n            buffers.flowFieldDirectionsBuffer.SetData(directions, 0, team * Navigation.CellCount, directions.Length);\n            DiagnosticUploadMs += uploadWatch.Elapsed.TotalMilliseconds;')
sub(o,'public long gcBytes;','public long gcBytes; public double flowMs, laneMs, uploadMs;')
sub(o,'gpuTimingMs = gpu, gcBytes = gc.Valid ? gc.LastValue : -1 });','gpuTimingMs = gpu, gcBytes = gc.Valid ? gc.LastValue : -1, flowMs = TerrainNavigationRuntime.DiagnosticFlowMs, laneMs = TerrainNavigationRuntime.DiagnosticLaneMs, uploadMs = TerrainNavigationRuntime.DiagnosticUploadMs });')
sub(o,'gpuTimingMs,gcBytes");','gpuTimingMs,gcBytes,flowMs,laneMs,uploadMs");')
sub(o,'r.gcBytes.ToString(Inv)));','r.gcBytes.ToString(Inv),r.flowMs.ToString(Inv),r.laneMs.ToString(Inv),r.uploadMs.ToString(Inv)));')
modified={p:t.encode('utf-8') for p,t in texts.items()}
runner=(ROOT/'Tools/InteractionRefinement/run_p3_rendered.py').read_text(encoding='utf-8')
header='''from common_p8 import *
P3=LOG/'P3'
base_check=project_check
def project_check(full=False):
 r=base_check(full)
 expected=json.loads((P3/'split-20261008-01/allowed.json').read_text(encoding='utf-8'))
 valid=all(sha(ROOT/p)==h for p,h in expected.items())
 if valid:r['changed']=[x for x in r['changed'] if x['path'] not in expected]
 r['passed']=valid and not r['changed'] and not r['unexpectedNew']
 return r
'''
(dest/'runner.py').write_text(runner.replace('from common_p3 import *',header),encoding='utf-8')
state={'status':'running','productionFix':False,'scope':'Temporary CPU flow / lane approach / upload timing, same 2048 / 48 settings. Extra Stopwatch allocations are diagnostic overhead.'}
save(dest/'summary.json',state)
try:
 for p,b in modified.items():assert (ROOT/p).read_bytes()==original[p];(ROOT/p).write_bytes(b)
 save(dest/'allowed.json',{p:sha(ROOT/p) for p in paths})
 env=os.environ.copy();env['PYTHONPATH']=str(ROOT/'Tools/InteractionRefinement')+os.pathsep+env.get('PYTHONPATH','')
 with (dest/'runner.log').open('w',encoding='utf-8') as f:
  result=subprocess.run([sys.executable,'-X','utf8',str(dest/'runner.py'),'performance','split-20261008-01-measure','gui','48'],cwd=ROOT,env=env,stdout=f,stderr=subprocess.STDOUT,timeout=900)
 state['exitCode']=result.returncode
 if result.returncode:raise RuntimeError('split runner failed; inspect log')
 rows=list(csv.DictReader((P3/'split-20261008-01-measure/frames.csv').open()))
 samples=[]
 for a,b in zip(rows,rows[1:]):
  d={k:float(b[k])-float(a[k]) for k in ['flowMs','laneMs','uploadMs']};d['wallMs']=1000*(float(b['seconds'])-float(a['seconds']))
  if d['flowMs']>0:samples.append(d)
 state['solveSamples']=len(samples)
 state['medianMs']={k:statistics.median(d[k] for d in samples) for k in ['flowMs','laneMs','uploadMs','wallMs']}
 state['status']='split_capture_complete';save(dest/'samples.json',samples)
except Exception as e:state['status']='failed';state['error']=str(e);raise
finally:
 state['restored']=[]
 if not processes() and not(ROOT/'Temp/UnityLockfile').exists():
  for p,b in modified.items():
   if (ROOT/p).read_bytes()==b:(ROOT/p).write_bytes(original[p]);state['restored'].append(p)
 state['project']=project_check(full=True);state['userData']=user_check();save(dest/'summary.json',state)
 print(json.dumps(state,ensure_ascii=False,indent=2),flush=True)
