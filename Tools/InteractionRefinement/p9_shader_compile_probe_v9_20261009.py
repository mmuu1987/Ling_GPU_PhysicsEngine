from pathlib import Path
import ctypes as C,json,time,hashlib,subprocess,shutil,sys,re
R=Path(__file__).resolve().parents[2];P=R/'outputs/P11';D=R/'outputs/P9ColdFix-20261009-01';assert D.is_dir()
assert subprocess.check_output(['powershell','-NoProfile','-Command',"@(Get-Process Unity,WarSandbox -ErrorAction SilentlyContinue).Count"],text=True).strip()=='0'
original=json.loads((R/'outputs/P9Combined-20261009-01/source-manifest.json').read_text(encoding='utf-8'))['files']
files=[p for p in (P/'Assets/MassEngine').rglob('*') if p.suffix in ['.hlsl','.compute']]
for p in files:assert hashlib.sha256(p.read_bytes()).hexdigest()==original[p.relative_to(P).as_posix()]['sha256']
nav='Assets/MassEngine/Terrain/Shaders/TerrainNavigation.hlsl';shader='Assets/MassEngine/Simulation/Shaders/AgentCombatSimulation.compute'
base=(P/nav).read_text(encoding='utf-8');needle='[unroll] for (int attempt = 0; attempt < 4; ++attempt)';assert base.count(needle)==1
point='''    return TerrainNavCellOpen(lo) && TerrainNavCellOpen(hi)
        && TerrainNavCellOpen(int2(lo.x, hi.y))
        && TerrainNavCellOpen(int2(hi.x, lo.y));'''
assert base.count(point)==1
rolled='''    // Keep the four conservative cell checks in their original order.
    // A real loop avoids cloning the local/global-mask branch into every inlined caller.
    [loop] for (int corner = 0; corner < 4; ++corner)
    {
        int2 cell = corner == 0 ? lo : (corner == 1 ? hi :
            (corner == 2 ? int2(lo.x, hi.y) : int2(hi.x, lo.y)));
        if (!TerrainNavCellOpen(cell)) return false;
    }
    return true;'''
a=base.index('    [loop] for (int i = 0;');b=base.index('\nfloat TerrainDistanceSquared',a)
replacement='    [loop] for (int i = 0; i < flowFieldResolution.x + flowFieldResolution.y + 2; ++i)\n    {\n        if (all(cell == goal)) return true;\n        bool tied = abs(tMax.x - tMax.y) <= 1e-6;\n        bool crossX = tied || tMax.x < tMax.y;\n        bool crossY = tied || !crossX;\n        int2 advance = int2(crossX ? step.x : 0, crossY ? step.y : 0);\n        // Preserve X-cardinal, Y-cardinal, diagonal order on tied crossings.\n        // Non-tied crossings visit only the chosen neighbour. One mask-call site.\n        [loop] for (int check = 0; check < (tied ? 3 : 1); ++check)\n        {\n            int2 offset = advance;\n            if (tied && check == 0) offset = int2(step.x, 0);\n            if (tied && check == 1) offset = int2(0, step.y);\n            if (!TerrainNavCellOpen(cell + offset)) return false;\n        }\n        cell += advance;\n        // Conditional additions preserve the original tMax update sequence.\n        if (crossX) tMax.x += tDelta.x;\n        if (crossY) tMax.y += tDelta.y;\n    }\n    return false;\n}\n'
variants={'dda-shared-cell-check':base[:a]+replacement+base[b:]}

class Macro(C.Structure):_fields_=[('Name',C.c_char_p),('Definition',C.c_char_p)]
lib=C.WinDLL('d3dcompiler_47.dll');fn=lib.D3DCompileFromFile;fn.restype=C.c_long
fn.argtypes=[C.c_wchar_p,C.POINTER(Macro),C.c_void_p,C.c_char_p,C.c_char_p,C.c_uint,C.c_uint,C.POINTER(C.c_void_p),C.POINTER(C.c_void_p)]
def blob(b):
 if not b:return b''
 v=C.cast(b,C.POINTER(C.POINTER(C.c_void_p))).contents
 p=C.WINFUNCTYPE(C.c_void_p,C.c_void_p)(v[3])(b);n=C.WINFUNCTYPE(C.c_size_t,C.c_void_p)(v[4])(b)
 data=C.string_at(p,n);C.WINFUNCTYPE(C.c_ulong,C.c_void_p)(v[2])(b);return data
results=[]
for name,text in variants.items():
 dst=D/'compiler-probes-v9'/name
 for f in files:
  t=dst/f.relative_to(P);t.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(f,t)
 (dst/nav).write_text(text,encoding='utf-8')
 def expand(p):
  data=p.read_text(encoding='utf-8')
  return re.sub(r'^\s*#include\s+"([^"]+)".*$',lambda m:expand((p.parent/m.group(1)).resolve()),data,flags=re.M)
 (dst/'expanded.compute').write_text(expand(dst/shader),encoding='utf-8')
 macros=(Macro*3)(Macro(b'MASS_TERRAIN_ENABLED',b'1'),Macro(b'MASS_LOCAL_ORDERS',b'1'),Macro(None,None))
 code=C.c_void_p();error=C.c_void_p();start=time.perf_counter();print('COMPILE',name,flush=True)
 hr=fn(str(dst/'expanded.compute'),macros,C.c_void_p(1),b'SimulateCombatAndAccumulateDamage',b'cs_5_0',1<<15,0,C.byref(code),C.byref(error))
 elapsed=time.perf_counter()-start;data=blob(code);warnings=blob(error).decode('utf-8',errors='replace');(dst/'compiler.txt').write_text(warnings,encoding='utf-8');(dst/'kernel.dxbc').write_bytes(data)
 row={'name':name,'seconds':elapsed,'hresult':hr,'bytes':len(data),'sha256':hashlib.sha256(data).hexdigest(),'warnings':warnings,'compiler':'system d3dcompiler_47.dll; O3; cs_5_0; CPU-only screening, not Unity timing'};results.append(row)
 (D/'compiler-screen-v9.json').write_text(json.dumps(results,indent=2),encoding='utf-8');print(json.dumps(row),flush=True)
 if hr<0:sys.exit(1)
