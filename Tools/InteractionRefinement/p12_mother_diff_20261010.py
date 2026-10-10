import hashlib, json, subprocess, sys
from pathlib import Path
sys.stdout.reconfigure(encoding="utf-8")
ROOT = Path.cwd(); P = ROOT / "outputs" / "P12"
def g(*a, cwd=ROOT):
    r = subprocess.run(["git", *a], cwd=cwd, capture_output=True, text=True, encoding="utf-8", errors="replace"); return (r.stdout + r.stderr).strip()
print("MOTHER HEAD", g("rev-parse", "--abbrev-ref", "HEAD"), g("rev-parse", "HEAD"))
print("MOTHER status\n", g("status", "--short")[:1500])
print("BRANCHES\n", g("branch", "-a", "-vv")[:2500])
print("WORKTREES\n", g("worktree", "list"))
print("P12 .git", (P / ".git").exists(), g("rev-parse", "--abbrev-ref", "HEAD", cwd=P) if (P / ".git").exists() else "")
if (P / ".git").exists(): print("P12 log\n", g("log", "--oneline", "-8", cwd=P)); print("P12 status\n", g("status", "--short", cwd=P)[:1500])
def h(f): return hashlib.sha256(f.read_bytes()).hexdigest()
diff = {"onlyP12": [], "onlyMother": [], "changed": []}
for top in ("Assets", "Packages", "ProjectSettings"):
    a = {f.relative_to(P).as_posix(): f for f in (P / top).rglob("*") if f.is_file()}
    b = {f.relative_to(ROOT).as_posix(): f for f in (ROOT / top).rglob("*") if f.is_file()}
    for k in sorted(a.keys() - b.keys()): diff["onlyP12"].append(k)
    for k in sorted(b.keys() - a.keys()): diff["onlyMother"].append(k)
    for k in sorted(a.keys() & b.keys()):
        if a[k].stat().st_size != b[k].stat().st_size or h(a[k]) != h(b[k]): diff["changed"].append(k)
for k, v in diff.items(): print(k, len(v)); [print("  ", x) for x in v[:80]]
