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
variants={}

class Macro(C.Structure):_fields_=[('Name',C.c_char_p),('Definition',C.c_char_p)]
lib=C.WinDLL('d3dcompiler_47.dll');fn=lib.D3DCompileFromFile;fn.restype=C.c_long
fn.argtypes=[C.c_wchar_p,C.POINTER(Macro),C.c_void_p,C.c_char_p,C.c_char_p,C.c_uint,C.c_uint,C.POINTER(C.c_void_p),C.POINTER(C.c_void_p)]
def blob(b):
 if not b:return b''
 v=C.cast(b,C.POINTER(C.POINTER(C.c_void_p))).contents
 p=C.WINFUNCTYPE(C.c_void_p,C.c_void_p)(v[3])(b);n=C.WINFUNCTYPE(C.c_size_t,C.c_void_p)(v[4])(b)
 data=C.string_at(p,n);C.WINFUNCTYPE(C.c_ulong,C.c_void_p)(v[2])(b);return data
replacement='        // One rolled call site keeps the expensive trajectory/terrain sweep from\n        // being inlined three times. Preserve centre-first, then left/right order,\n        // all original admission conditions, and the maximum of three probes.\n        bool clear = false, leftClear = false, rightClear = false;\n        float2 side = 0.0;\n        [loop] for (int probe = 0; probe < 3; ++probe)\n        {\n            AgentData candidate = agent;\n            bool navigable = true;\n            if (probe > 0)\n            {\n                candidate.position.xz += probe == 1 ? side : -side;\n#ifdef MASS_TERRAIN_ENABLED\n                navigable = TerrainSegmentClear(agent.position.xz, candidate.position.xz);\n#endif\n            }\n            bool candidateClear = false;\n            if (navigable) candidateClear = RangedPathClear(index, candidate, target, settings);\n            if (probe == 0)\n            {\n                clear = candidateClear;\n                if (clear || !(state.rangedStatus >= 0.0) || !allowReposition) break;\n                float2 toTarget = agentPositionReadBuffer[target] - agent.position.xz;\n                if (!(dot(toTarget, toTarget) > 0.0001)) break;\n                side = normalize(float2(-toTarget.y, toTarget.x)) * (RangedSideBudget(agent, settings) * 0.6);\n            }\n            else if (probe == 1) leftClear = candidateClear;\n            else rightClear = candidateClear;\n        }\n        if (clear) state.rangedStatus = 1.0;\n        else if (state.rangedStatus >= 0.0)\n        {\n            state.rangedStatus = (Hash01(index ^ 0xB5297A4Du) < 0.5) ? -1.0 : -2.0;\n            if (leftClear != rightClear) state.rangedStatus = leftClear ? -1.0 : -2.0;\n'
source=(D/'compiler-probes-v12-REQUIRES-HOST-SPLIT/attack-ranged/expanded.compute').read_text(encoding='utf-8')
a=source.index('        bool clear = RangedPathClear(index, agent, target, settings);')
b=source.index('            state.rangedAnchor = agent.position.xz; state.rangedSeconds = 0.0;',a)
merged=source[:a]+replacement+source[b:]
def loops(s):
 assert s.count(needle)==1 and s.count(point)==1
 return s.replace(needle,needle.replace('[unroll]','[loop]')).replace(point,rolled)
variants={'ranged-nav-loops':loops(source),'ranged-one-probe-site':merged,'ranged-probe-and-nav-loops':loops(merged)}
results=[]
for label,text in variants.items():
 dst=D/'compiler-probes-v13-REQUIRES-HOST-SPLIT'/label
 assert not dst.exists();dst.mkdir(parents=True)
 (dst/'expanded.compute').write_text(text,encoding='utf-8')
 macros=(Macro*3)(Macro(b'MASS_TERRAIN_ENABLED',b'1'),Macro(b'MASS_LOCAL_ORDERS',b'1'),Macro(None,None))
 code=C.c_void_p();error=C.c_void_p();start=time.perf_counter();print('COMPILE',label,flush=True)
 hr=fn(str(dst/'expanded.compute'),macros,C.c_void_p(1),b'SimulateCombatAndAccumulateDamage',b'cs_5_0',1<<15,0,C.byref(code),C.byref(error))
 elapsed=time.perf_counter()-start;data=blob(code);warnings=blob(error).decode('utf-8',errors='replace');(dst/'compiler.txt').write_text(warnings,encoding='utf-8');(dst/'kernel.dxbc').write_bytes(data)
 row={'name':label,'seconds':elapsed,'hresult':hr,'bytes':len(data),'sha256':hashlib.sha256(data).hexdigest(),'warnings':warnings,'requiresHostPartitionAndParityTests':True,'compiler':'CPU-only D3DCompile O3; not Unity command timing'};results.append(row)
 (D/'compiler-specialization-v13.json').write_text(json.dumps(results,indent=2),encoding='utf-8');print(json.dumps({k:v for k,v in row.items() if k!='warnings'}),flush=True)
 if hr<0:sys.exit(1)
