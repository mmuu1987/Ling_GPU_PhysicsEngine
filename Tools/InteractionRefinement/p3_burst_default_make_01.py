import hashlib, json, re, shutil, sys, datetime as dt
from pathlib import Path
ROOT = Path.cwd(); P12 = ROOT / "outputs" / "P12"
OUT = ROOT / "outputs" / "P3BurstDefault-20261010-01"
FILES = ["Assets/MassEngine/Terrain/TerrainNavigationRuntime.cs", "Assets/MassEngine/Terrain/TerrainNavigationGrid.cs"]
sha = lambda b: hashlib.sha256(b).hexdigest()
if OUT.exists(): sys.exit("exists")
orig = {r: (P12 / r).read_bytes() for r in FILES}
for r, b in orig.items():
    if b"UnityEditor" in b.replace(b"UNITY_EDITOR", b""): sys.exit("UnityEditor API present " + r)
def strip(text):
    lines = text.split("\n"); out = []; stack = []; removed = 0
    for ln in lines:
        s = ln.strip()
        if s.startswith("#if"):
            stack.append([s == "#if UNITY_EDITOR", "if"])
            if stack[-1][0]: removed += 1; continue
        elif s.startswith("#else") and stack and stack[-1][0]:
            stack[-1][1] = "else"; continue
        elif s.startswith("#endif"):
            top = stack.pop()
            if top[0]: continue
        if any(x[0] and x[1] == "else" for x in stack): continue
        out.append(ln)
    assert not stack
    return "\n".join(out), removed
new = {}; counts = {}
for r, b in orig.items():
    t, n = strip(b.decode("utf-8")); counts[r] = n; new[r] = t
rt = new[FILES[0]]
def rep(a, b):
    global rt
    assert rt.count(a) == 1, a; rt = rt.replace(a, b)
rep("// Process-local Editor opt-in only; no preference or scene asset is written.",
    "// P3-PERF-01 candidate (2026-10-10): Burst is the default in Editor and Player; --war-sandbox-nav-managed forces the managed solver.\n        // --war-sandbox-nav-burst is still accepted (no-op). No preference or scene asset is written.")
rep("public static bool BurstReserveRequested => reserveOptIn && !reserveManagedOnly;",
    "public static bool BurstReserveRequested => !reserveManagedOnly;\n        public static bool BurstArgumentPresent => reserveOptIn;")
rep('PreparationBlockReason = reserveManagedOnly ? "ManagedOverride" : "DefaultOff";', 'PreparationBlockReason = "ManagedOverride";')
new[FILES[0]] = rt
for r in FILES:
    assert "#if UNITY_EDITOR" not in new[r] and "Navigation.CreateFlowField(goals, stopRadius);" not in new[r] if r == FILES[0] else True
OUT.mkdir(parents=True)
man = {"name": "P3 Burst default-on candidate", "created": dt.datetime.now().astimezone().isoformat(), "accepted": False,
       "base": "P12 nav-sharing candidate (unaccepted)", "removedEditorGates": counts, "files": {}}
for r in FILES:
    (OUT / "original").mkdir(exist_ok=True); (OUT / "candidate").mkdir(exist_ok=True)
    (OUT / "original" / Path(r).name).write_bytes(orig[r])
    nb = new[r].encode("utf-8"); (OUT / "candidate" / Path(r).name).write_bytes(nb)
    (P12 / r).write_bytes(nb)
    man["files"][r] = {"originalSha256": sha(orig[r]), "candidateSha256": sha(nb)}
(OUT / "candidate-manifest.json").write_text(json.dumps(man, indent=2), encoding="utf-8")
print(json.dumps(man, indent=1))
