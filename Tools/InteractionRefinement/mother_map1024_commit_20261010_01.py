"""Phase B of MotherMap1024-20261010-01: local commit of ONLY the 14 Map1024 paths. No push. User-approved 2026-10-10 11:21."""
import hashlib, json, os, re, subprocess, sys
from pathlib import Path
sys.stdout.reconfigure(encoding="utf-8")
ROOT = Path(r"E:\GitHub\Ling_GPU_PhysicsEngine\Ling_GPU_PhysicsEngine"); O = ROOT / "outputs" / "MotherMap1024-20261010-01"
env = os.environ.copy(); env["GIT_OPTIONAL_LOCKS"] = "0"
def git(*a, check=True):
    r = subprocess.run(["git", *a], cwd=ROOT, capture_output=True, text=True, encoding="utf-8", errors="replace")
    if check and r.returncode: raise SystemExit("git " + " ".join(a[:3]) + " failed: " + r.stderr)
    return r.stdout.strip()
def sha(p): return hashlib.sha256(Path(p).read_bytes()).hexdigest()
j = json.loads((O / "journal.json").read_text(encoding="utf-8"))
assert j["allPassed"] and j["copyVerified"], "phase A not passed"
assert git("rev-parse", "HEAD") == j["gitHead"], "HEAD moved"
staged = git("diff", "--cached", "--name-only")
if staged: raise SystemExit("index already has staged changes, refusing:\n" + staged[:2000])
files = sorted(j["copied"])
assert len(files) == 14, len(files)
bad = [f for f in files if sha(ROOT / f) != j["copied"][f]]
if bad: raise SystemExit("files changed since merge: " + str(bad))
MESH_TEX = {"43", "28", "89", "117", "187"}  # Mesh, Texture2D, Cubemap, Texture3D, Texture2DArray
add, skipped = [], []
for f in files:
    p = ROOT / f; size = p.stat().st_size
    ign = git("check-ignore", "-v", "--no-index", "--", f, check=False)
    head = p.read_bytes()[:200000].decode("utf-8", "replace")
    cls = set(re.findall(r"--- !u!(\d+)", head))
    reason = []
    if size > 50 * 1024 * 1024: reason.append(">50MB")
    if ign: reason.append("gitignored: " + ign)
    if cls & MESH_TEX: reason.append("mesh/texture asset classes " + str(sorted(cls & MESH_TEX)))
    (skipped if reason else add).append({"path": f, "bytes": size, "reason": reason})
print("ADD", json.dumps(add, ensure_ascii=False)); print("SKIP", json.dumps(skipped, ensure_ascii=False))
paths = [a["path"] for a in add]
git("add", "--", *paths)
st = sorted(git("diff", "--cached", "--name-only").splitlines())
assert st == sorted(paths), ("staged mismatch", set(st) ^ set(paths))
print("STAGED_STAT", git("diff", "--cached", "--stat"))
msg = """Add 1024m test battlefield forest-green22-1024 (200k deploy)

- New Assets/Game/Content/Battlefields/Map1024: Green1024 scene (derived from forest-green22 512, XZ x2),
  Surface1024 257x257@4m, nav/flow 256x256@4m, Simulation/System configs, RosterPolicy1024 (max 200,000 units)
- Catalog.asset: append entry forest-green22-1024 only; 512 maps unchanged

Validation (mother working tree): 0 CS errors; nav EditMode TerrainNavigationGridTests 23/23;
catalog TryValidate ok; 100k and 200k (100k/side, density 0.7, aspect 2.2, gap 50) pass TryValidate on 1024;
512 control still rejects 200k. Test map only: high unit counts are not a smoothness guarantee.
Evidence: outputs/MotherMap1024-20261010-01"""
git("commit", "-m", msg)
c = git("rev-parse", "HEAD"); parent = git("rev-parse", "HEAD~1")
info = {"commit": c, "parent": parent, "branch": git("rev-parse", "--abbrev-ref", "HEAD"), "committed": add, "skipped": skipped,
        "aheadBehind": git("rev-list", "--left-right", "--count", "HEAD...@{u}", check=False), "statusLine": git("status", "-sb").splitlines()[0],
        "showFiles": git("show", "--name-only", "--format=", "HEAD").splitlines(), "stagedAfter": git("diff", "--cached", "--name-only").splitlines()}
(O / "commit.json").write_text(json.dumps(info, ensure_ascii=False, indent=2), encoding="utf-8")
print("COMMIT", json.dumps(info, ensure_ascii=False))
