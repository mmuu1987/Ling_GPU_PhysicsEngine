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
dda=base
p=dda.index('    [loop] for (int i = 0;');q=dda.index('\nfloat TerrainDistanceSquared',p)
segment=dda[p:q]
assert segment.count('return false;')==3
segment=segment.replace('    [loop] for','    bool clear = false;\n    [loop] for',1).replace('if (all(cell == goal)) return true;','if (all(cell == goal)) { clear = true; break; }')
segment=segment.replace('return false;','break;',2).replace('    return false;','    return clear;')
dda=dda[:p]+segment+dda[q:]
direct='    // The mixed corners are in bounds iff both lo and hi are in bounds.\n    // Hoist shared bounds/mask selection; keep all four conservative samples.\n    if (any(lo < 0) || any(lo >= flowFieldResolution) ||\n        any(hi < 0) || any(hi >= flowFieldResolution)) return false;\n    int4 cell = int4(lo.y * flowFieldResolution.x + lo.x,\n        hi.y * flowFieldResolution.x + hi.x,\n        hi.y * flowFieldResolution.x + lo.x,\n        lo.y * flowFieldResolution.x + hi.x);\n#if defined(MASS_LOCAL_ORDERS) && defined(MASS_TERRAIN_ENABLED)\n    if (localSelfSlot >= 0)\n    {\n        cell += localSelfSlot * _LocalOrderCellCount;\n        return _LocalOrderMask[cell.x] != 0 && _LocalOrderMask[cell.y] != 0\n            && _LocalOrderMask[cell.z] != 0 && _LocalOrderMask[cell.w] != 0;\n    }\n#endif\n    return _NavigationWalkable[cell.x] != 0 && _NavigationWalkable[cell.y] != 0\n        && _NavigationWalkable[cell.z] != 0 && _NavigationWalkable[cell.w] != 0;'
variants={'dda-single-exit':dda,'point-direct':base.replace(point,direct)}

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
 dst=D/'compiler-probes-v5'/name
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
 (D/'compiler-screen-v5.json').write_text(json.dumps(results,indent=2),encoding='utf-8');print(json.dumps(row),flush=True)
 if hr<0:sys.exit(1)
