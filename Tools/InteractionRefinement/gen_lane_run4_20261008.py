# -*- coding: utf-8 -*-
"""Generate corrected lane candidate + equivalence runner + ABBA run-4 runner."""
import ast, hashlib, os, sys

ROOT = r'E:\GitHub\Ling_GPU_PhysicsEngine\Ling_GPU_PhysicsEngine'
TOOLS = os.path.join(ROOT, 'Tools', 'InteractionRefinement')
P3 = os.path.join(ROOT, 'Logs', 'InteractionRefinement-20261007', 'P3')
ORIG_GRID_SHA = '1c9143a70ab085cf28ef51aed2ca9b477561551e2651d2bcbdf38cf0a45904f3'

def sha(path):
    h = hashlib.sha256()
    with open(path, 'rb') as f:
        for chunk in iter(lambda: f.read(1 << 20), b''):
            h.update(chunk)
    return h.hexdigest()

def must_replace(text, old, new, n=1):
    assert text.count(old) == n, 'anchor count %d != %d: %r' % (text.count(old), n, old[:80])
    return text.replace(old, new)

# ---------- 1. corrected candidate grid ----------
grid_path = os.path.join(ROOT, 'Assets', 'MassEngine', 'Terrain', 'TerrainNavigationGrid.cs')
assert sha(grid_path) == ORIG_GRID_SHA, 'workspace grid is not the verified original'
g = open(grid_path, encoding='utf-8', newline='').read()
a = g.index('        public bool HasClearCardinalRoute33(int from,int to)')
b = g.index('        public uint[] CopyWalkable()')
v1 = open(os.path.join(TOOLS, 'make_lane_files_20261008.py'), encoding='utf-8').read()
# reuse the exact splice block from v1 (between new=''' and ''')
marker = "new='''"
s = v1.index(marker) + len(marker)
e = v1.index("'''", s)
new_block = v1[s:e]
assert 'EnsureStraightRuns' in new_block and 'HasClearCardinalRoute33' in new_block
cand = g[:a] + new_block + g[b:]
assert 'Burst' not in cand, 'corrected candidate must not reference Burst'
assert cand.count('HasClearCardinalRoute33') == 1 + g[:a].count('HasClearCardinalRoute33')
dst_dir = os.path.join(P3, 'burst-lane-20261008-02', 'files', 'Assets', 'MassEngine', 'Terrain')
os.makedirs(dst_dir, exist_ok=True)
dst = os.path.join(dst_dir, 'TerrainNavigationGrid.cs')
assert not os.path.exists(dst), 'candidate already exists'
open(dst, 'w', encoding='utf-8', newline='').write(cand)
print('CANDIDATE', sha(dst), len(cand))

# ---------- 2. equivalence-only runner on original base ----------
t = open(os.path.join(TOOLS, 'p3_lane_tests_20261008.py'), encoding='utf-8').read()
t = must_replace(t, "WORK=LOG/'P3'/'lane-tests-20261008-01'", "WORK=LOG/'P3'/'lane-equiv-20261008-02'")
t = must_replace(t,
    "candidate={p:(prior/'candidate'/p).read_bytes() for p in [NAV_PATH,LANE_PATH,RUNTIME_PATH,ENGINE_ASM,TEST_ASM]}",
    "candidate={p:original[p] for p in [NAV_PATH,LANE_PATH,RUNTIME_PATH,ENGINE_ASM,TEST_ASM]}")
t = must_replace(t, " candidate[RUNTIME_PATH]=(ROOT/'Tools/InteractionRefinement/P3OptInRuntime-20261008.cs.txt').read_bytes()\n", '')
t = must_replace(t, " candidate[RUNTIME_PATH]=(LOG/'P3'/'burst-landing-20261008-01'/'files'/RUNTIME_PATH).read_bytes()\n", '', 2)
t = must_replace(t,
    "candidate[NAV_PATH]=(LOG/'P3'/'burst-lane-20261008-01'/'files'/NAV_PATH).read_bytes()",
    "candidate[NAV_PATH]=(LOG/'P3'/'burst-lane-20261008-02'/'files'/NAV_PATH).read_bytes()")
t = must_replace(t,
    "phases=[('straightRunEquivalence','editmode','p3-lane-straightrun-01','straight-run',[],1),('regression150','editmode','p3-lane-regression-01','density-runtime',[],150)]",
    "phases=[('straightRunEquivalence','editmode','p3-lane4-straightrun-01','straight-run',[],1)]")
t = must_replace(t,
    "'scope':'Exact proposed final source shape: four production paths from verified default-off candidate, original tests and original test assembly references, no observers/fault injection/attribution instrumentation. Original150 regression with no opt-in argument. Always restore; separate controlled adoption afterwards.'",
    "'scope':'Corrected lane O(1) candidate spliced from the verified original Grid only (no Burst identifiers). Deployed combination identical to ABBA B windows: original LaneApproach/Runtime/asmdefs, equivalence test appended to original test file. Exhaustive straight-run equivalence on this exact combination before ABBA run 4. Always restore.'")
t = must_replace(t, "state['status']='landing_default_off_regression_restored'",
                    "state['status']='lane_candidate_equivalence_on_original_base_passed'")
ast.parse(t)
eq_path = os.path.join(TOOLS, 'p3_lane_equiv4_20261008.py')
assert not os.path.exists(eq_path)
open(eq_path, 'w', encoding='utf-8', newline='').write(t)
print('EQUIV RUNNER written', len(t))

# ---------- 3. ABBA run 4 ----------
r = open(os.path.join(TOOLS, 'p3_lane_abba_20261008.py'), encoding='utf-8').read()
r = must_replace(r, "WORK=LOG/'P3'/'lane-abba-20261008-03'", "WORK=LOG/'P3'/'lane-abba-20261008-04'")
r = must_replace(r,
    "candidate[NAV_PATH]=(LOG/'P3'/'burst-lane-20261008-01'/'files'/NAV_PATH).read_bytes()",
    "candidate[NAV_PATH]=(LOG/'P3'/'burst-lane-20261008-02'/'files'/NAV_PATH).read_bytes()")
r = must_replace(r, "tag='p3-lane-abba2-'+str(i+1)", "tag='p3-lane-abba4-'+str(i+1)")
r = must_replace(r,
    "'sequence':['reuse GPU6 only after exact engine byte check; previous A1 invalid camera movement preserved, not used','A1 baseline','B1 candidate','B2 candidate','A2 baseline']",
    "'sequence':['run 4 after corrected candidate (original-base splice, no Burst identifiers) passed exhaustive equivalence on the exact B combination','A1 baseline','B1 candidate','B2 candidate','A2 baseline']")
ast.parse(r)
abba_path = os.path.join(TOOLS, 'p3_lane_abba4_20261008.py')
assert not os.path.exists(abba_path)
open(abba_path, 'w', encoding='utf-8', newline='').write(r)
print('ABBA4 RUNNER written', len(r))
print('ALL OK')
