from pathlib import Path
import ctypes as C,json,time,hashlib,subprocess,shutil,sys,re
R=Path(__file__).resolve().parents[2];P=R/'outputs/P12';D=R/'outputs/P9ColdFix-20261009-01'
assert subprocess.check_output(['powershell','-NoProfile','-Command',"@(Get-Process Unity,WarSandbox -ErrorAction SilentlyContinue).Count"],text=True).strip()=='0'
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
m=json.loads((D/'patch-manifest.json').read_text(encoding='utf-8'));assert m['revision']==2
assert all(sha(P/f)==v for f,v in m['files'].items())
Q=D/'compiler-probes-v15-single-integrate';Q.mkdir()
(Q/'production-manifest-before.json').write_bytes((D/'patch-manifest.json').read_bytes())
files=[p for p in (P/'Assets/MassEngine').rglob('*') if p.suffix in ['.hlsl','.compute']]
before={p.relative_to(P).as_posix():sha(p) for p in files}
shader='Assets/MassEngine/Simulation/Shaders/AgentCombatSimulation.compute'
s=(P/shader).read_text(encoding='utf-8')
candidate=(R/'Tools/InteractionRefinement/p9_v15_single_integrate_20261009.compute.txt').read_text(encoding='utf-8')
variants={'single-integrate-and-store':candidate}
roles=['AttackMelee','AttackRanged','MoveMelee','MoveRanged','HoldMelee','HoldRanged']
class Macro(C.Structure):_fields_=[('Name',C.c_char_p),('Definition',C.c_char_p)]
lib=C.WinDLL('d3dcompiler_47.dll');fn=lib.D3DCompileFromFile;fn.restype=C.c_long
fn.argtypes=[C.c_wchar_p,C.POINTER(Macro),C.c_void_p,C.c_char_p,C.c_char_p,C.c_uint,C.c_uint,C.POINTER(C.c_void_p),C.POINTER(C.c_void_p)]
def blob(b):
 if not b:return b''
 v=C.cast(b,C.POINTER(C.POINTER(C.c_void_p))).contents
 p=C.WINFUNCTYPE(C.c_void_p,C.c_void_p)(v[3])(b);n=C.WINFUNCTYPE(C.c_size_t,C.c_void_p)(v[4])(b)
 data=C.string_at(p,n);C.WINFUNCTYPE(C.c_ulong,C.c_void_p)(v[2])(b);return data

results=[]
for label,text in variants.items():
 dst=Q/label
 for f in files:
  t=dst/f.relative_to(P);t.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(f,t)
 (dst/shader).write_text(text,encoding='utf-8')
 def expand(p):
  return re.sub(r'^\s*#include\s+"([^"]+)".*$',lambda m:expand((p.parent/m.group(1)).resolve()),p.read_text(encoding='utf-8'),flags=re.M)
 (dst/'expanded.compute').write_text(expand(dst/shader),encoding='utf-8')
 for role in roles:
  defs=[(b'MASS_TERRAIN_ENABLED',b'1'),(b'MASS_LOCAL_ORDERS',b'1'),(('LP_'+role.upper()).encode(),b'1')]
  macros=(Macro*4)(*[Macro(*x) for x in defs],Macro(None,None))
  code=C.c_void_p();error=C.c_void_p();start=time.perf_counter();print('COMPILE',label,role,flush=True)
  hr=fn(str(dst/'expanded.compute'),macros,C.c_void_p(1),('SimulateLocal'+role).encode(),b'cs_5_0',1<<15,0,C.byref(code),C.byref(error))
  elapsed=time.perf_counter()-start;data=blob(code);warnings=blob(error).decode('utf-8',errors='replace')
  (dst/(role+'.txt')).write_text(warnings,encoding='utf-8');(dst/(role+'.dxbc')).write_bytes(data)
  row={'variant':label,'role':role,'seconds':elapsed,'hresult':hr,'bytes':len(data),'sha256':hashlib.sha256(data).hexdigest(),'sourceSha256':sha(dst/shader),'compiler':'CPU-only D3DCompile O3, cs_5_0; not Unity timing; GPU parity pending'}
  results.append(row);(D/'compiler-specialization-v15.json').write_text(json.dumps(results,indent=2),encoding='utf-8');print(json.dumps(row),flush=True)
  if hr<0:print(warnings,flush=True);sys.exit(1)
assert all(sha(P/f)==v for f,v in before.items())
(Q/'protection.json').write_text(json.dumps({'productionShadersUnchanged':True,'patchManifestUnchanged':(Q/'production-manifest-before.json').read_bytes()==(D/'patch-manifest.json').read_bytes(),'productionDeployed':False,'gpuValidated':False}),encoding='utf-8')
