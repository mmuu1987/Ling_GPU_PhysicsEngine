"""MotherMap1024-20261010-01 part b: rerun nav EditMode with the required isolated P8 dir under Logs/InteractionRefinement-20261007/P8
(first attempt used outputs/... and InteractionRefinementP8 exited 1 before tests), restore Unity auto-writes, audit efficiently, finalize journal."""
import datetime as dt, hashlib, json, os, re, shutil, subprocess, sys, time
import xml.etree.ElementTree as ET
from pathlib import Path
sys.stdout.reconfigure(encoding="utf-8")
ROOT = Path(r"E:\GitHub\Ling_GPU_PhysicsEngine\Ling_GPU_PhysicsEngine")
OUT = ROOT / "outputs" / "MotherMap1024-20261010-01"; CAND = ROOT / "outputs" / "Map1024-20261010-01"
UNITY = Path(r"D:\soft\Unity6\6000.3.14f1\Editor\Unity.exe"); B37 = ROOT / "Builds" / "CaptureDefault-20261006-37"
env = os.environ.copy(); env["GIT_OPTIONAL_LOCKS"] = "0"
def sha(p):
    h = hashlib.sha256()
    with Path(p).open("rb") as f:
        for c in iter(lambda: f.read(1 << 20), b""): h.update(c)
    return h.hexdigest()
def manifest(root):
    root = Path(root); return {p.relative_to(root).as_posix(): sha(p) for p in sorted(root.rglob("*")) if p.is_file()} if root.exists() else {}
def save(p, d): Path(p).write_text(json.dumps(d, ensure_ascii=False, indent=2, default=str) + "\n", encoding="utf-8")
def git(*a): return subprocess.run(["git", *a], cwd=ROOT, capture_output=True, text=True, encoding="utf-8", errors="replace", env=env).stdout
def procs():
    return subprocess.run(["powershell", "-NoProfile", "-Command", "@(Get-Process Unity,WarSandbox,P9WindowsValidation,P3BurstValidation,P3BurstDefaultValidation,P3ScaleValidation -ErrorAction SilentlyContinue).Count"], capture_output=True, text=True).stdout.strip()
J = json.loads((OUT / "journal.json").read_text(encoding="utf-8"))
def ev(t):
    print(t, flush=True); J["events"].append({"t": dt.datetime.now().astimezone().isoformat(), "e": t}); save(OUT / "journal.json", J)
LOCALLOW = Path(os.environ["USERPROFILE"]) / "AppData" / "LocalLow"
def testresults():
    d = {}
    if LOCALLOW.is_dir():
        for f in LOCALLOW.glob("*/*/TestResults.xml"):
            n = f.parent.parent.name + "/" + f.parent.name
            if any(k in n for k in ("Ling", "WarSandbox", "Mass", "P9")): d[n] = f
    return d
if procs() != "0": raise SystemExit("Unity/game process running; refusing")
J["errors"].append("02-nav-editmode attempt 1: --interaction-p8-output outside Logs/InteractionRefinement-20261007/P8 -> InteractionRefinementP8 Exit(1) before tests (harness path, not a code failure); part-a audit killed (O(n^2) on 17 MB git status)")
start_ns = int(dt.datetime.fromisoformat(J["started"]).timestamp() * 1e9)
tr_before = {k: f.read_bytes() for k, f in testresults().items()}
V = ROOT / "Logs" / "InteractionRefinement-20261007" / "P8" / "mothermap1024-01-nav-editmode"; V.mkdir(parents=True, exist_ok=False)
(V / "guide.json").write_text('{"version":1,"dismissed":true}', encoding="utf-8")
D = OUT / "02b-nav-editmode"; D.mkdir(exist_ok=False); log = D / "Editor.log"
full = [str(UNITY), "-batchmode", "-force-d3d11", "-projectPath", str(ROOT), "-runTests", "-testPlatform", "EditMode", "-assemblyNames", "MassEngine.Tests",
        "-testResults", str(V / "results.xml"), "-testFilter", "MassEngine.Tests.TerrainNavigationGridTests", "--interaction-p8-output=" + str(V),
        "--war-sandbox-guide-file=" + str(V / "guide.json"), "--war-sandbox-settings-file=" + str(V / "audio.json"),
        "--war-sandbox-review-global-file=" + str(V / "globals.json"), "-logFile", str(log)]
ev("UNITY_START 02b-nav-editmode " + json.dumps(full, ensure_ascii=False)); t0 = time.time()
with (D / "launcher.txt").open("w", encoding="utf-8") as f:
    p = subprocess.Popen(full, cwd=str(ROOT), stdout=f, stderr=subprocess.STDOUT); timed = False
    try: rc = p.wait(timeout=1800)
    except subprocess.TimeoutExpired:
        subprocess.run(["taskkill", "/PID", str(p.pid), "/T", "/F"], capture_output=True); rc = p.wait(60); timed = True
text = log.read_text(encoding="utf-8", errors="replace") if log.exists() else ""
s = {"label": "02b-nav-editmode", "exitCode": rc, "timedOut": timed, "wallSeconds": round(time.time() - t0, 1),
     "csErrors": sorted(set(re.findall(r"^.*error CS\d+.*$", text, re.M)))[:30], "isolated": "INTERACTION_P8_ISOLATED" in text,
     "exceptions": [l[:400] for l in text.splitlines() if "Exception" in l][:20]}
if (V / "results.xml").exists():
    r = ET.parse(V / "results.xml").getroot()
    s["tests"] = {k: r.get(k) for k in ("result", "total", "passed", "failed", "skipped", "duration")}
    s["failedCases"] = [t.get("fullname") for t in r.iter("test-case") if t.get("result") != "Passed"]
    shutil.copy2(V / "results.xml", OUT / "nav-editmode-results.xml")
J["stages"].append(s); J["navTests"] = s.get("tests")
ev("UNITY_EXIT 02b-nav-editmode rc=%s cs=%d tests=%s isolated=%s" % (rc, len(s["csErrors"]), json.dumps(s.get("tests")), s["isolated"]))
# restore
rest = {"projectSettingsRestored": []}
orig = manifest(OUT / "ProjectSettings-original"); now = manifest(ROOT / "ProjectSettings")
for rel in set(now) | set(orig):
    if now.get(rel) != orig.get(rel):
        dst = ROOT / "ProjectSettings" / rel
        if rel in orig: shutil.copy2(OUT / "ProjectSettings-original" / rel, dst)
        else: (OUT / "auto-generated").mkdir(exist_ok=True); shutil.copy2(dst, OUT / "auto-generated" / Path(rel).name); dst.unlink()
        rest["projectSettingsRestored"].append(rel)
rest["projectSettingsMatch"] = manifest(ROOT / "ProjectSettings") == orig
for k, f in testresults().items():
    if k not in tr_before: f.unlink(); rest.setdefault("testResultsRemoved", []).append(k)
    elif f.read_bytes() != tr_before[k]: f.write_bytes(tr_before[k]); rest.setdefault("testResultsRestored", []).append(k)
J["restorationB"] = rest
# audit
copied = J["copied"]
J["copiedStillIntact"] = all(sha(ROOT / r) == h for r, h in copied.items())
touched = []
for top in ("Assets", "Packages", "ProjectSettings"):
    for q in (ROOT / top).rglob("*"):
        if q.is_file() and q.stat().st_mtime_ns >= start_ns: touched.append(q.relative_to(ROOT).as_posix())
J["filesTouchedSinceStart"] = sorted(touched)
J["onlyExpectedTouched"] = set(touched) <= set(copied)
mb = json.loads((CAND / "mother-before.json").read_text(encoding="utf-8")).get("b37")
J["b37UnchangedVsMap1024Baseline"] = (manifest(B37) == mb) if mb else None
J["gitHeadUnchanged"] = git("rev-parse", "HEAD").strip() == J["gitHead"]
J["indexStagedNow"] = git("diff", "--cached", "--name-only").strip().splitlines()
sa = git("status", "--porcelain=v1", "--untracked-files=all"); (OUT / "git-status-after.txt").write_text(sa, encoding="utf-8")
before = set((OUT / "git-status-before.txt").read_text(encoding="utf-8").splitlines()); after = set(sa.splitlines())
new = sorted(after - before); gone = sorted(before - after)
J["gitStatusNewLines"] = new[:80]; J["gitStatusGoneLines"] = gone[:80]
exp = {c for c in copied}
J["gitStatusUnexpected"] = [l for l in new if not any(l[3:].strip('"') == c or c.startswith(l[3:].strip('"').rstrip("/") + "/") for c in exp)][:40] + ["GONE " + l for l in gone][:40]
ok_nav = bool(s.get("tests")) and str(s["tests"].get("result", "")).startswith("Passed") and s["tests"].get("total") == "23" and s["tests"].get("passed") == "23"
J["allPassed"] = bool(J.get("validation", {}).get("passed")) and ok_nav and rc == 0 and not any(x["csErrors"] for x in J["stages"]) and J["copiedStillIntact"] \
    and J["onlyExpectedTouched"] and J["b37UnchangedVsMap1024Baseline"] is not False and J["gitHeadUnchanged"] and not J["indexStagedNow"] and J.get("builderRemoved")
J["finished"] = dt.datetime.now().astimezone().isoformat(); save(OUT / "journal.json", J)
print("ALL_DONE allPassed=%s nav=%s intact=%s onlyExpected=%s b37=%s head=%s staged=%d restore=%s" % (J["allPassed"], json.dumps(s.get("tests")), J["copiedStillIntact"],
      J["onlyExpectedTouched"], J["b37UnchangedVsMap1024Baseline"], J["gitHeadUnchanged"], len(J["indexStagedNow"]), json.dumps(rest)), flush=True)
print("TOUCHED", json.dumps(J["filesTouchedSinceStart"][:40])); print("GITNEW", json.dumps(new[:40], ensure_ascii=False)); print("GITUNEXPECTED", json.dumps(J["gitStatusUnexpected"], ensure_ascii=False))
