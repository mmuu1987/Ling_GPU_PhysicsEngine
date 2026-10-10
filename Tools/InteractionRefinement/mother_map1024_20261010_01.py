"""Merge the Map1024 candidate (forest-green22-1024) from outputs/P12 into the MOTHER working tree (user-approved 2026-10-10 11:21).
Phase A only: snapshot/backup/rollback -> verify+copy 13 added files + Catalog.asset -> serial Unity checks on mother:
  01-validate (temp Map1024Builder.Validate: catalog loads entry, 100k/200k TryValidate on 1024, 512 control rejects 200k)
  remove temp builder -> 02-nav-editmode (TerrainNavigationGridTests, also final 0-CS-error compile check).
No git add/commit here (phase B). No reset/checkout/clean. 37 package untouched (audited). Evidence: outputs/MotherMap1024-20261010-01."""
import datetime as dt, difflib, hashlib, json, os, re, shutil, subprocess, sys, time
import xml.etree.ElementTree as ET
from pathlib import Path

sys.stdout.reconfigure(encoding="utf-8")
ROOT = Path(r"E:\GitHub\Ling_GPU_PhysicsEngine\Ling_GPU_PhysicsEngine")
P12 = ROOT / "outputs" / "P12"
CAND = ROOT / "outputs" / "Map1024-20261010-01"
OUT = ROOT / "outputs" / "MotherMap1024-20261010-01"
UNITY = Path(r"D:\soft\Unity6\6000.3.14f1\Editor\Unity.exe")
B37 = ROOT / "Builds" / "CaptureDefault-20261006-37"
TOOLS = ROOT / "Tools" / "InteractionRefinement"
BUILDER_REL = "Assets/Game/Editor/Map1024Builder.cs"
BUILDER_GUID = "3f6a1d2e9b7c4e5fa1b2c3d4e5f60718"
CATALOG = "Assets/Game/Scenes/Catalog.asset"
MAP_DIR = "Assets/Game/Content/Battlefields/Map1024"
env = os.environ.copy(); env["GIT_OPTIONAL_LOCKS"] = "0"


def sha(p):
    h = hashlib.sha256()
    with Path(p).open("rb") as f:
        for c in iter(lambda: f.read(1 << 20), b""): h.update(c)
    return h.hexdigest()


def manifest(root):
    root = Path(root)
    return {p.relative_to(root).as_posix(): sha(p) for p in sorted(root.rglob("*")) if p.is_file()} if root.exists() else {}


def statmap(root):
    root = Path(root)
    return {p.relative_to(root).as_posix(): [p.stat().st_size, p.stat().st_mtime_ns] for p in root.rglob("*") if p.is_file()} if root.exists() else {}


def save(p, d):
    Path(p).parent.mkdir(parents=True, exist_ok=True)
    Path(p).write_text(json.dumps(d, ensure_ascii=False, indent=2, default=str) + "\n", encoding="utf-8")


def git(*a):
    return subprocess.run(["git", *a], cwd=ROOT, capture_output=True, text=True, encoding="utf-8", errors="replace", env=env).stdout


def procs():
    q = subprocess.run(["powershell", "-NoProfile", "-Command",
        "@(Get-Process Unity,WarSandbox,P9WindowsValidation,P3BurstValidation,P3BurstDefaultValidation,P3ScaleValidation -ErrorAction SilentlyContinue).Count"],
        capture_output=True, text=True)
    return q.stdout.strip()


LOCALLOW = Path(os.environ["USERPROFILE"]) / "AppData" / "LocalLow"


def userdata():
    d = {}
    if LOCALLOW.is_dir():
        for comp in LOCALLOW.iterdir():
            if comp.is_dir():
                for prod in comp.iterdir():
                    n = comp.name + "/" + prod.name
                    if prod.is_dir() and any(k in n for k in ("Ling", "WarSandbox", "Mass", "P9")):
                        for f in prod.rglob("*"):
                            if f.is_file(): d[n + "/" + f.relative_to(prod).as_posix()] = f
    return d


J = {"name": "mother-map1024-20261010-01", "started": dt.datetime.now().astimezone().isoformat(), "events": [], "stages": [], "errors": []}


def ev(t):
    print(t, flush=True); J["events"].append({"t": dt.datetime.now().astimezone().isoformat(), "e": t}); save(OUT / "journal.json", J)


def unity(label, args, timeout):
    d = OUT / label; d.mkdir(parents=True, exist_ok=False)
    log = d / "Editor.log"
    full = [str(UNITY), "-batchmode", "-force-d3d11", "-projectPath", str(ROOT)] + args + ["-logFile", str(log)]
    while procs() != "0": time.sleep(5)
    ev("UNITY_START " + label + " " + json.dumps(full, ensure_ascii=False))
    t0 = time.time()
    with (d / "launcher.txt").open("w", encoding="utf-8") as f:
        p = subprocess.Popen(full, cwd=str(ROOT), stdout=f, stderr=subprocess.STDOUT)
        timed = False
        try: rc = p.wait(timeout=timeout)
        except subprocess.TimeoutExpired:
            subprocess.run(["taskkill", "/PID", str(p.pid), "/T", "/F"], capture_output=True); rc = p.wait(60); timed = True
    text = log.read_text(encoding="utf-8", errors="replace") if log.exists() else ""
    s = {"label": label, "exitCode": rc, "timedOut": timed, "wallSeconds": round(time.time() - t0, 1),
         "csErrors": sorted(set(re.findall(r"^.*error CS\d+.*$", text, re.M)))[:30],
         "map1024Lines": [l[:3000] for l in text.splitlines() if "MAP1024" in l][:20],
         "exceptions": [l[:400] for l in text.splitlines() if "Exception" in l][:20]}
    J["stages"].append(s); ev("UNITY_EXIT %s rc=%s timed=%s cs=%d wall=%s" % (label, rc, timed, len(s["csErrors"]), s["wallSeconds"]))
    return s, d


def main():
    if OUT.exists(): raise SystemExit("refusing: output dir exists " + str(OUT))
    if not UNITY.is_file(): raise SystemExit("Unity missing")
    if procs() != "0": raise SystemExit("Unity/game process already running; refusing")
    cm = json.loads((CAND / "candidate-manifest.json").read_text(encoding="utf-8"))
    added = list(cm["addedFiles"])
    if len(added) != 13: raise SystemExit("expected 13 added files, got %d" % len(added))
    for rel in added + [MAP_DIR]:
        if (ROOT / rel).exists(): raise SystemExit("refusing: already in mother: " + rel)
    if (ROOT / BUILDER_REL).exists(): raise SystemExit("refusing: builder already in mother")
    # P12 candidate still intact?
    bad = [r for r in added if sha(P12 / r) != cm["addedFileSha256"][r]]
    if bad: raise SystemExit("P12 candidate changed since manifest: " + str(bad))
    if sha(P12 / CATALOG) != cm["modifiedFiles"][CATALOG]["candidateSha256"]: raise SystemExit("P12 Catalog changed since manifest")
    OUT.mkdir(parents=True)
    # ---- 1) record mother state, backup, rollback ----
    J["gitHead"] = git("rev-parse", "HEAD").strip(); J["gitBranch"] = git("rev-parse", "--abbrev-ref", "HEAD").strip()
    J["aheadBehindBefore"] = git("rev-list", "--left-right", "--count", "HEAD...@{u}").strip()
    J["upstream"] = git("rev-parse", "--abbrev-ref", "@{u}").strip()
    (OUT / "git-status-before.txt").write_text(git("status", "--porcelain=v1", "--untracked-files=all"), encoding="utf-8")
    J["stagedBefore"] = git("diff", "--cached", "--name-only").strip().splitlines()
    J["indexShaBefore"] = sha(ROOT / ".git" / "index")
    ev("mother HEAD=%s branch=%s upstream=%s ab=%s staged=%d" % (J["gitHead"], J["gitBranch"], J["upstream"], J["aheadBehindBefore"], len(J["stagedBefore"])))
    b37 = manifest(B37); J["b37Files"] = len(b37)
    for r in (CATALOG, CATALOG + ".meta"):
        dst = OUT / "backup" / r; dst.parent.mkdir(parents=True, exist_ok=True); shutil.copy2(ROOT / r, dst)
    J["motherCatalogSha"] = sha(ROOT / CATALOG)
    J["motherCatalogMetaSha"] = sha(ROOT / (CATALOG + ".meta"))
    J["p12CatalogMetaSha"] = sha(P12 / (CATALOG + ".meta"))
    assets_before = statmap(ROOT / "Assets")
    settings_before = manifest(ROOT / "ProjectSettings"); packages_before = manifest(ROOT / "Packages")
    shutil.copytree(ROOT / "ProjectSettings", OUT / "ProjectSettings-original")
    ud_before = {k: f.read_bytes() for k, f in userdata().items() if f.name == "TestResults.xml"}
    ud_hash_before = {k: sha(f) for k, f in userdata().items()}
    plan = {"added": added, "addedDirs": [MAP_DIR], "modified": [CATALOG], "catalogOriginalSha": J["motherCatalogSha"]}
    save(OUT / "plan.json", plan)
    (OUT / "rollback.py").write_text(ROLLBACK, encoding="utf-8")
    ev("backup + rollback.py written")
    # ---- 2) catalog check + copy ----
    orig_p12 = cm["modifiedFiles"][CATALOG]["originalSha256"]
    if J["motherCatalogSha"] == orig_p12:
        shutil.copy2(P12 / CATALOG, ROOT / CATALOG); J["catalogMode"] = "copied-P12-candidate (mother == P12 pre-candidate)"
    else:
        J["catalogMode"] = "applied-appended-entry (mother differs from P12 pre-candidate)"
        a = (CAND / "backup" / CATALOG).read_text(encoding="utf-8").splitlines(keepends=True)
        b = (P12 / CATALOG).read_text(encoding="utf-8").splitlines(keepends=True)
        sm = difflib.SequenceMatcher(None, a, b, autojunk=False)
        ops = [o for o in sm.get_opcodes() if o[0] != "equal"]
        if len(ops) != 1 or ops[0][0] != "insert": raise SystemExit("unexpected catalog diff shape " + str(ops))
        _, i1, _, j1, j2 = ops[0]; ins = b[j1:j2]; ctx_before = a[max(0, i1 - 3):i1]; ctx_after = a[i1:i1 + 1]
        with (ROOT / CATALOG).open("r", encoding="utf-8", newline="") as f: m = f.read().splitlines(keepends=True)
        st = lambda L: [x.rstrip("\r\n") for x in L]
        hits = [k for k in range(len(m)) if st(m[max(0, k - 3):k]) == st(ctx_before) and st(m[k:k + 1]) == st(ctx_after)]
        if len(hits) != 1: raise SystemExit("cannot uniquely locate insertion point in mother catalog: %d hits" % len(hits))
        if any("forest-green22-1024" in l for l in m): raise SystemExit("mother catalog already has forest-green22-1024")
        nl = "\r\n" if m[0].endswith("\r\n") else "\n"
        ins = [l.rstrip("\r\n") + nl for l in ins]
        with (ROOT / CATALOG).open("w", encoding="utf-8", newline="") as f: f.write("".join(m[:hits[0]] + ins + m[hits[0]:]))
        J["catalogMotherDiff"] = "".join(difflib.unified_diff((OUT / "backup" / CATALOG).read_text(encoding="utf-8").splitlines(True), (ROOT / CATALOG).read_text(encoding="utf-8").splitlines(True), "mother-original", "mother-new"))
    (OUT / "Catalog.asset.mother.diff").write_text("".join(difflib.unified_diff((OUT / "backup" / CATALOG).read_text(encoding="utf-8").splitlines(True), (ROOT / CATALOG).read_text(encoding="utf-8").splitlines(True), "mother-original", "mother-new")), encoding="utf-8")
    for rel in added:
        t = ROOT / rel; t.parent.mkdir(parents=True, exist_ok=True); shutil.copy2(P12 / rel, t)
    J["copied"] = {r: sha(ROOT / r) for r in added + [CATALOG]}
    J["copyVerified"] = all(J["copied"][r] == cm["addedFileSha256"][r] for r in added)
    J["catalogNewSha"] = J["copied"][CATALOG]
    J["catalogEqualsP12Candidate"] = J["catalogNewSha"] == cm["modifiedFiles"][CATALOG]["candidateSha256"]
    ev("COPIED %d files verified=%s catalogMode=%s catalogEqualsP12=%s" % (len(added), J["copyVerified"], J["catalogMode"], J["catalogEqualsP12Candidate"]))
    if not J["copyVerified"]: raise SystemExit("copy verification failed")
    # file facts for phase B
    facts = {}
    for rel in added:
        p = ROOT / rel; head = p.read_bytes()[:4000].decode("utf-8", "replace")
        facts[rel] = {"bytes": p.stat().st_size, "ignored": git("check-ignore", "-v", "--no-index", "--", rel).strip(),
                      "attr": git("check-attr", "filter", "--", rel).strip(), "unityClasses": sorted(set(re.findall(r"--- !u!(\d+)", head)))}
    J["fileFacts"] = facts; save(OUT / "journal.json", J)
    # ---- 3) serial Unity checks ----
    builder = (TOOLS / "Map1024Builder.cs.txt").read_text(encoding="utf-8")
    bpath = ROOT / BUILDER_REL; ok = False
    try:
        bpath.write_text(builder, encoding="utf-8", newline="\r\n")
        Path(str(bpath) + ".meta").write_text("fileFormatVersion: 2\nguid: " + BUILDER_GUID + "\nMonoImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n  icon: {instanceID: 0}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n", encoding="utf-8", newline="\r\n")
        ev("installed temporary builder (Validate only used) " + BUILDER_REL)
        s, d = unity("01-validate-map", ["-executeMethod", "MassEngine.Game.Editor.Map1024Builder.Validate", "--map1024-output=" + str(OUT / "validation"), "-quit"], 3600)
        vf = OUT / "validation" / "map1024-validation.json"
        J["validation"] = json.loads(vf.read_text(encoding="utf-8")) if vf.exists() else None
        if s["exitCode"] != 0 or s["csErrors"] or not J["validation"] or not J["validation"].get("passed"): raise RuntimeError("validate-map failed")
        ok = True
    except Exception as ex:
        J["errors"].append(repr(ex)); ev("ERROR " + repr(ex))
    finally:
        for p in (bpath, Path(str(bpath) + ".meta")):
            if p.exists(): p.unlink()
        J["builderRemoved"] = not bpath.exists() and not Path(str(bpath) + ".meta").exists()
        ev("temporary builder removed=" + str(J["builderRemoved"]))
    if ok:
        V = OUT / "02-nav-editmode-io"; V.mkdir(parents=True)
        (V / "guide.json").write_text('{"version":1,"dismissed":true}', encoding="utf-8")
        s, d = unity("02-nav-editmode", ["-runTests", "-testPlatform", "EditMode", "-assemblyNames", "MassEngine.Tests", "-testResults", str(V / "results.xml"),
                                         "-testFilter", "MassEngine.Tests.TerrainNavigationGridTests", "--interaction-p8-output=" + str(V),
                                         "--war-sandbox-guide-file=" + str(V / "guide.json"), "--war-sandbox-settings-file=" + str(V / "audio.json"),
                                         "--war-sandbox-review-global-file=" + str(V / "globals.json")], 1800)
        if (V / "results.xml").exists():
            r = ET.parse(V / "results.xml").getroot()
            s["tests"] = {k: r.get(k) for k in ("result", "total", "passed", "failed", "skipped", "duration")}
            s["failedCases"] = [t.get("fullname") for t in r.iter("test-case") if t.get("result") != "Passed"]
        J["navTests"] = s.get("tests"); save(OUT / "journal.json", J)
        ev("nav tests " + json.dumps(s.get("tests")) + " cs=" + str(len(s["csErrors"])))
    # ---- restore what Unity may have auto-written ----
    rest = {"projectSettingsRestored": []}
    now = manifest(ROOT / "ProjectSettings")
    for rel in set(now) | set(settings_before):
        if now.get(rel) != settings_before.get(rel):
            dst = ROOT / "ProjectSettings" / rel
            if rel in settings_before: shutil.copy2(OUT / "ProjectSettings-original" / rel, dst)
            else: (OUT / "auto-generated").mkdir(exist_ok=True); shutil.copy2(dst, OUT / "auto-generated" / Path(rel).name); dst.unlink()
            rest["projectSettingsRestored"].append(rel)
    for k, f in userdata().items():
        if f.name != "TestResults.xml": continue
        if k not in ud_before: f.unlink(); rest.setdefault("testResultsRemoved", []).append(k)
        elif f.read_bytes() != ud_before[k]: f.write_bytes(ud_before[k]); rest.setdefault("testResultsRestored", []).append(k)
    rest["userDataUnchanged"] = {k: sha(f) for k, f in userdata().items()} == ud_hash_before
    rest["projectSettingsMatch"] = manifest(ROOT / "ProjectSettings") == settings_before
    rest["packagesUnchanged"] = manifest(ROOT / "Packages") == packages_before
    J["restoration"] = rest
    # ---- audit ----
    assets_after = statmap(ROOT / "Assets")
    ad = {"added": sorted("Assets/" + k for k in set(assets_after) - set(assets_before)),
          "removed": sorted("Assets/" + k for k in set(assets_before) - set(assets_after)),
          "changed": sorted("Assets/" + k for k in set(assets_after) & set(assets_before) if assets_after[k] != assets_before[k])}
    ad["onlyExpected"] = sorted(ad["added"]) == sorted(added) and not ad["removed"] and ad["changed"] == [CATALOG]
    J["assetDiff"] = ad
    J["copiedStillIntact"] = all(sha(ROOT / r) == h for r, h in J["copied"].items())
    J["b37Unchanged"] = manifest(B37) == b37
    J["gitHeadUnchanged"] = git("rev-parse", "HEAD").strip() == J["gitHead"]
    J["indexUnchanged"] = sha(ROOT / ".git" / "index") == J["indexShaBefore"]
    sa = git("status", "--porcelain=v1", "--untracked-files=all"); (OUT / "git-status-after.txt").write_text(sa, encoding="utf-8")
    before = set((OUT / "git-status-before.txt").read_text(encoding="utf-8").splitlines())
    J["gitStatusNewLines"] = [l for l in sa.splitlines() if l not in before][:60]
    J["gitStatusGoneLines"] = [l for l in before if l not in set(sa.splitlines())][:60]
    J["allPassed"] = ok and bool(J.get("navTests")) and str(J["navTests"].get("result", "")).startswith("Passed") and ad["onlyExpected"] \
        and not any(s["csErrors"] for s in J["stages"]) and J["copiedStillIntact"] and J["b37Unchanged"] and J["gitHeadUnchanged"]
    J["finished"] = dt.datetime.now().astimezone().isoformat(); save(OUT / "journal.json", J)
    print("ALL_DONE allPassed=%s b37=%s head=%s index=%s intact=%s assetDiff=%s restore=%s" % (J["allPassed"], J["b37Unchanged"], J["gitHeadUnchanged"], J["indexUnchanged"],
          J["copiedStillIntact"], json.dumps({k: (v if k == "onlyExpected" else len(v)) for k, v in ad.items()}), json.dumps(rest)), flush=True)
    print("GITNEW " + json.dumps(J["gitStatusNewLines"], ensure_ascii=False)[:3000]); print("GITGONE " + json.dumps(J["gitStatusGoneLines"], ensure_ascii=False)[:2000])
    print("FACTS " + json.dumps(facts, ensure_ascii=False)[:4000])
    if J.get("validation"): print("VALIDATION " + json.dumps(J["validation"], ensure_ascii=False)[:3500], flush=True)


ROLLBACK = r'''"""Rollback of MotherMap1024-20261010-01 (mother working tree + optional local commit).
  python rollback.py --dry-run      print only
  python rollback.py                delete the 13 added Map1024 files/dir, restore mother Catalog.asset from backup/
  python rollback.py --undo-commit  additionally, if HEAD is still the recorded Map1024 commit, move the branch back to its parent
                                    (git reset --soft <parent> then unstage only the 14 paths; nothing else touched). Never pushes.
Close Unity first."""
import hashlib, json, shutil, subprocess, sys
from pathlib import Path
HERE = Path(__file__).resolve().parent
ROOT = Path(r"E:\GitHub\Ling_GPU_PhysicsEngine\Ling_GPU_PhysicsEngine")
plan = json.loads((HERE / "plan.json").read_text(encoding="utf-8"))
dry = "--dry-run" in sys.argv
def git(*a): return subprocess.run(["git", *a], cwd=ROOT, capture_output=True, text=True).stdout.strip()
n = subprocess.run(["powershell","-NoProfile","-Command","@(Get-Process Unity -ErrorAction SilentlyContinue).Count"],capture_output=True,text=True).stdout.strip()
if n != "0" and not dry: sys.exit("Close Unity first (running Unity processes: %s)" % n)
paths = plan["added"] + plan["modified"]
if "--undo-commit" in sys.argv:
    c = HERE / "commit.json"
    if c.exists():
        info = json.loads(c.read_text(encoding="utf-8"))
        if git("rev-parse", "HEAD") == info["commit"]:
            print("undo commit", info["commit"], "->", info["parent"])
            if not dry:
                subprocess.check_call(["git", "reset", "--soft", info["parent"]], cwd=ROOT)
                subprocess.check_call(["git", "reset", "-q", info["parent"], "--"] + paths, cwd=ROOT)
        else: print("HEAD is not the recorded commit; not touching git")
for rel in plan["added"]:
    p = ROOT / rel; print("delete", rel, "exists=" + str(p.exists()))
    if not dry and p.exists(): p.unlink()
for d in plan["addedDirs"]:
    p = ROOT / d
    if not dry and p.exists():
        for q in sorted(p.rglob("*"), reverse=True):
            if q.is_dir(): q.rmdir()
            else: sys.exit("unexpected extra file in added dir: %s" % q)
        p.rmdir()
    print("rmdir", d)
for rel in plan["modified"]:
    print("restore", rel)
    if not dry: shutil.copy2(HERE / "backup" / rel, ROOT / rel)
if not dry:
    ok = hashlib.sha256((ROOT / plan["modified"][0]).read_bytes()).hexdigest() == plan["catalogOriginalSha"] and not any((ROOT / r).exists() for r in plan["added"])
    print("ROLLBACK_OK" if ok else "ROLLBACK_MISMATCH"); sys.exit(0 if ok else 1)
'''

if __name__ == "__main__":
    try: main()
    except SystemExit as e:
        if OUT.exists(): J["errors"].append("SystemExit " + str(e)); save(OUT / "journal.json", J)
        print("ABORT", e, flush=True); raise
