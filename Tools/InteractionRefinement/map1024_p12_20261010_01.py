"""Map1024 candidate in isolated P12 (2026-10-10, user-approved 10:38).

Adds a 1024x1024 m test battlefield "forest-green22-1024" (derived from forest-green22) to outputs/P12 as a
PERSISTENT candidate change. Serial, one Unity process at a time. Mother Assets/ProjectSettings and the 37
package are never touched (audited). Evidence/backups/rollback: outputs/Map1024-20261010-01.
Stages: build-map (temp Editor builder) -> validate-map (EditMode/headless TryValidate 100k/200k + 512 control)
-> remove temp builder -> nav EditMode TerrainNavigationGridTests (also the final 0-CS-error compile check).
"""
import datetime as dt, hashlib, json, os, re, shutil, subprocess, sys, time
import xml.etree.ElementTree as ET
from pathlib import Path

sys.stdout.reconfigure(encoding="utf-8")
ROOT = Path(r"E:\GitHub\Ling_GPU_PhysicsEngine\Ling_GPU_PhysicsEngine")
P12 = ROOT / "outputs" / "P12"
OUT = ROOT / "outputs" / "Map1024-20261010-01"
UNITY = Path(r"D:\soft\Unity6\6000.3.14f1\Editor\Unity.exe")
B37 = ROOT / "Builds" / "CaptureDefault-20261006-37"
TOOLS = ROOT / "Tools" / "InteractionRefinement"
BUILDER_REL = Path("Assets/Game/Editor/Map1024Builder.cs")
BUILDER_GUID = "3f6a1d2e9b7c4e5fa1b2c3d4e5f60718"
MAP_DIR_REL = "Assets/Game/Content/Battlefields/Map1024"
MODIFIED = ["Assets/Game/Scenes/Catalog.asset"]
VLOG = P12 / "Logs" / "InteractionRefinement-20261007" / "P8"
env = os.environ.copy(); env["GIT_OPTIONAL_LOCKS"] = "0"


def sha(p):
    h = hashlib.sha256()
    with Path(p).open("rb") as f:
        for c in iter(lambda: f.read(1 << 20), b""): h.update(c)
    return h.hexdigest()


def manifest(root):
    root = Path(root)
    if not root.exists(): return {}
    return {p.relative_to(root).as_posix(): sha(p) for p in sorted(root.rglob("*")) if p.is_file()}


def save(p, d):
    Path(p).parent.mkdir(parents=True, exist_ok=True)
    Path(p).write_text(json.dumps(d, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def git(*a): return subprocess.check_output(["git", *a], cwd=ROOT, env=env).decode().strip()


def mother_snapshot():
    stats = {}
    for r in ("Assets", "Packages", "ProjectSettings"):
        for p in (ROOT / r).rglob("*"):
            if p.is_file():
                st = p.stat(); stats[p.relative_to(ROOT).as_posix()] = [st.st_size, st.st_mtime_ns]
    return {"head": git("rev-parse", "HEAD"), "index": sha(ROOT / ".git" / "index"), "stats": stats, "b37": manifest(B37)}


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


J = {"name": "map1024-p12-candidate-01", "started": dt.datetime.now().astimezone().isoformat(), "events": [], "stages": [], "errors": []}


def ev(t):
    print(t, flush=True); J["events"].append({"t": dt.datetime.now().astimezone().isoformat(), "e": t}); save(OUT / "journal.json", J)


def unity(label, args, timeout):
    log = OUT / (label + ".log")
    full = [str(UNITY)] + args + ["-logFile", str(log)]
    ev("UNITY_START " + label + " " + json.dumps(full, ensure_ascii=False))
    t0 = time.time()
    with (OUT / (label + ".launcher.txt")).open("w", encoding="utf-8") as f:
        p = subprocess.Popen(full, cwd=str(P12), stdout=f, stderr=subprocess.STDOUT)
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
    return s


ROLLBACK = r'''"""Rollback of the Map1024 candidate in outputs/P12 (generated 2026-10-10).
Deletes every file added by map1024_p12_20261010_01.py and restores every modified P12 file from backup/.
Run with no Unity process open on P12:  python rollback.py   (add --dry-run to only print)."""
import hashlib, json, shutil, subprocess, sys
from pathlib import Path
HERE = Path(__file__).resolve().parent
P12 = Path(r"E:\GitHub\Ling_GPU_PhysicsEngine\Ling_GPU_PhysicsEngine\outputs\P12")
m = json.loads((HERE / "candidate-manifest.json").read_text(encoding="utf-8"))
dry = "--dry-run" in sys.argv
def sha(p):
    h = hashlib.sha256(); h.update(Path(p).read_bytes()); return h.hexdigest()
n = subprocess.run(["powershell","-NoProfile","-Command","@(Get-Process Unity -ErrorAction SilentlyContinue).Count"],capture_output=True,text=True).stdout.strip()
if n != "0" and not dry: sys.exit("Close Unity first (running Unity processes: %s)" % n)
for rel in m["addedFiles"]:
    p = P12 / rel
    print("delete", rel, "exists=" + str(p.exists()))
    if not dry and p.exists(): p.unlink()
for d in m.get("addedDirs", []):
    p = P12 / d
    if not dry and p.exists():
        for q in sorted(p.rglob("*"), reverse=True):
            if q.is_dir(): q.rmdir()
            else: sys.exit("unexpected extra file left in added dir: %s" % q)
        p.rmdir()
    print("rmdir", d)
for rel, info in m["modifiedFiles"].items():
    print("restore", rel)
    if not dry: shutil.copy2(HERE / "backup" / rel, P12 / rel)
if not dry:
    ok = all(sha(P12 / rel) == info["originalSha256"] for rel, info in m["modifiedFiles"].items()) and not any((P12 / r).exists() for r in m["addedFiles"])
    print("ROLLBACK_OK" if ok else "ROLLBACK_MISMATCH"); sys.exit(0 if ok else 1)
'''


def main():
    if OUT.exists(): raise SystemExit("refusing: output dir exists " + str(OUT))
    if (P12 / MAP_DIR_REL).exists() or (P12 / (MAP_DIR_REL + ".meta")).exists(): raise SystemExit("refusing: Map1024 dir already in P12")
    if (P12 / BUILDER_REL).exists(): raise SystemExit("refusing: builder already present")
    if not UNITY.is_file() or not P12.is_dir(): raise SystemExit("Unity or P12 missing")
    if procs() != "0": raise SystemExit("Unity/game process already running; refusing")
    OUT.mkdir(parents=True)
    ud_before = {k: f.read_bytes() for k, f in userdata().items() if f.name == "TestResults.xml"}
    ud_hash_before = {k: sha(f) for k, f in userdata().items()}
    ev("snapshot mother/37/P12 ...")
    mb = mother_snapshot(); save(OUT / "mother-before.json", {k: v for k, v in mb.items() if k != "stats"} | {"statCount": len(mb["stats"])})
    assets_before = manifest(P12 / "Assets")
    settings_before = manifest(P12 / "ProjectSettings"); packages_before = manifest(P12 / "Packages")
    shutil.copytree(P12 / "ProjectSettings", OUT / "ProjectSettings-original")
    us_existed = (P12 / "UserSettings").exists()
    if us_existed: shutil.copytree(P12 / "UserSettings", OUT / "UserSettings-original")
    us_before = manifest(P12 / "UserSettings")
    lib_prefs = {}
    for rel in ("EditorUserBuildSettings.asset", "BuildProfileContext.asset"):
        f = P12 / "Library" / rel
        lib_prefs[rel] = sha(f) if f.is_file() else None
        if f.is_file(): (OUT / "Library-target-settings-original").mkdir(exist_ok=True); shutil.copy2(f, OUT / "Library-target-settings-original" / rel)
    for rel in MODIFIED:
        for r in (rel, rel + ".meta"):
            dst = OUT / "backup" / r; dst.parent.mkdir(parents=True, exist_ok=True); shutil.copy2(P12 / r, dst)
    save(OUT / "original-state.json", {"modifiedOriginals": {r: sha(P12 / r) for r in MODIFIED}, "projectSettings": settings_before,
                                         "packages": packages_before, "userSettings": us_before, "libraryTargetPrefs": lib_prefs, "assetFileCount": len(assets_before)})
    builder = (TOOLS / "Map1024Builder.cs.txt").read_text(encoding="utf-8")
    (OUT / "Map1024Builder.cs.txt").write_text(builder, encoding="utf-8")
    bpath = P12 / BUILDER_REL
    ok = False
    try:
        bpath.write_text(builder, encoding="utf-8", newline="\r\n")
        Path(str(bpath) + ".meta").write_text("fileFormatVersion: 2\nguid: " + BUILDER_GUID + "\nMonoImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n  icon: {instanceID: 0}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n", encoding="utf-8", newline="\r\n")
        ev("installed temporary builder " + BUILDER_REL.as_posix())
        s = unity("01-build-map", ["-batchmode", "-force-d3d11", "-projectPath", str(P12), "-executeMethod", "MassEngine.Game.Editor.Map1024Builder.Build", "-quit"], 3600)
        if s["exitCode"] != 0 or s["csErrors"] or not any("MAP1024 BUILD OK" in l for l in s["map1024Lines"]):
            raise RuntimeError("build-map failed")
        vdir = OUT / "validation"
        s = unity("02-validate-map", ["-batchmode", "-force-d3d11", "-projectPath", str(P12), "-executeMethod", "MassEngine.Game.Editor.Map1024Builder.Validate",
                                      "--map1024-output=" + str(vdir), "-quit"], 3600)
        J["validation"] = json.loads((vdir / "map1024-validation.json").read_text(encoding="utf-8")) if (vdir / "map1024-validation.json").exists() else None
        if s["exitCode"] != 0 or s["csErrors"] or not J["validation"] or not J["validation"].get("passed"):
            raise RuntimeError("validate-map failed")
        ok = True
    except Exception as ex:
        J["errors"].append(repr(ex)); ev("ERROR " + repr(ex))
    finally:
        for p in (bpath, Path(str(bpath) + ".meta")):
            if p.exists(): p.unlink()
        J["builderRemoved"] = not bpath.exists() and not Path(str(bpath) + ".meta").exists()
        ev("temporary builder removed=" + str(J["builderRemoved"]))
    if ok:
        V = VLOG / "map1024-01-nav-editmode"; V.mkdir(parents=True)
        (V / "guide.json").write_text('{"version":1,"dismissed":true}', encoding="utf-8")
        s = unity("03-nav-editmode", ["-batchmode", "-force-d3d11", "-projectPath", str(P12), "-runTests", "-testPlatform", "EditMode",
                                      "-assemblyNames", "MassEngine.Tests", "-testResults", str(V / "results.xml"),
                                      "-testFilter", "MassEngine.Tests.TerrainNavigationGridTests", "--interaction-p8-output=" + str(V),
                                      "--war-sandbox-guide-file=" + str(V / "guide.json"), "--war-sandbox-settings-file=" + str(V / "audio.json"),
                                      "--war-sandbox-review-global-file=" + str(V / "globals.json")], 1800)
        if (V / "results.xml").exists():
            r = ET.parse(V / "results.xml").getroot()
            s["tests"] = {k: r.get(k) for k in ("result", "total", "passed", "failed", "skipped", "duration")}
            s["failedCases"] = [t.get("fullname") for t in r.iter("test-case") if t.get("result") != "Passed"]
            shutil.copy2(V / "results.xml", OUT / "nav-editmode-results.xml")
        J["navTests"] = s.get("tests"); save(OUT / "journal.json", J)
        ev("nav tests " + json.dumps(s.get("tests")) + " cs=" + str(len(s["csErrors"])))
    # ---- restore what Unity may have auto-written (never the candidate) ----
    rest = {"projectSettingsRestored": [], "userSettingsRestored": False}
    now = manifest(P12 / "ProjectSettings")
    for rel in set(now) | set(settings_before):
        if now.get(rel) != settings_before.get(rel):
            dst = P12 / "ProjectSettings" / rel
            if rel in settings_before: shutil.copy2(OUT / "ProjectSettings-original" / rel, dst)
            else: dst.unlink()
            rest["projectSettingsRestored"].append(rel)
    if manifest(P12 / "UserSettings") != us_before:
        if (P12 / "UserSettings").exists(): shutil.rmtree(P12 / "UserSettings")
        if us_existed: shutil.copytree(OUT / "UserSettings-original", P12 / "UserSettings")
        rest["userSettingsRestored"] = True
    for rel, h in lib_prefs.items():
        f = P12 / "Library" / rel
        if h and (not f.is_file() or sha(f) != h): shutil.copy2(OUT / "Library-target-settings-original" / rel, f); rest.setdefault("libraryPrefsRestored", []).append(rel)
    for k, f in userdata().items():
        if f.name != "TestResults.xml": continue
        if k not in ud_before: f.unlink(); rest.setdefault("testResultsRemoved", []).append(k)
        elif f.read_bytes() != ud_before[k]: f.write_bytes(ud_before[k]); rest.setdefault("testResultsRestored", []).append(k)
    rest["userDataUnchanged"] = {k: sha(f) for k, f in userdata().items()} == ud_hash_before
    rest["projectSettingsMatch"] = manifest(P12 / "ProjectSettings") == settings_before
    rest["packagesUnchanged"] = manifest(P12 / "Packages") == packages_before
    rest["userSettingsMatch"] = manifest(P12 / "UserSettings") == us_before
    J["restoration"] = rest
    # ---- candidate manifest ----
    after = manifest(P12 / "Assets")
    added = sorted("Assets/" + k for k in set(after) - set(assets_before))
    removed = sorted("Assets/" + k for k in set(assets_before) - set(after))
    changed = sorted("Assets/" + k for k in set(after) & set(assets_before) if after[k] != assets_before[k])
    expected_added = all(a.startswith(MAP_DIR_REL + "/") or a == MAP_DIR_REL + ".meta" for a in added)
    J["assetDiff"] = {"added": added, "removed": removed, "changed": changed,
                      "onlyExpected": expected_added and not removed and set(changed) <= set(MODIFIED)}
    cm = {"candidate": "forest-green22-1024 (1024x1024 m test battlefield) in outputs/P12",
          "addedDirs": [MAP_DIR_REL], "addedFiles": added,
          "addedFileSha256": {a: after[a[len("Assets/"):]] for a in added},
          "modifiedFiles": {r: {"originalSha256": sha(OUT / "backup" / r), "candidateSha256": sha(P12 / r) if (P12 / r).exists() else None} for r in MODIFIED},
          "temporaryFilesRemoved": [BUILDER_REL.as_posix(), BUILDER_REL.as_posix() + ".meta"]}
    save(OUT / "candidate-manifest.json", cm)
    (OUT / "rollback.py").write_text(ROLLBACK, encoding="utf-8")
    try:
        a = (OUT / "backup" / MODIFIED[0]).read_text(encoding="utf-8").splitlines(); b = (P12 / MODIFIED[0]).read_text(encoding="utf-8").splitlines()
        import difflib
        (OUT / "Catalog.asset.diff").write_text("\n".join(difflib.unified_diff(a, b, "original", "candidate", lineterm="")), encoding="utf-8")
    except Exception as ex: J["errors"].append("diff " + repr(ex))
    ma = mother_snapshot()
    J["motherUnchanged"] = ma["head"] == mb["head"] and ma["index"] == mb["index"] and ma["stats"] == mb["stats"]
    J["package37Unchanged"] = ma["b37"] == mb["b37"]
    J["allPassed"] = ok and bool(J.get("navTests")) and str(J["navTests"].get("result", "")).startswith("Passed") and J["assetDiff"]["onlyExpected"] \
        and not any(s["csErrors"] for s in J["stages"])
    J["finished"] = dt.datetime.now().astimezone().isoformat()
    save(OUT / "journal.json", J)
    print("ALL_DONE allPassed=%s mother=%s b37=%s assetDiff=%s restore=%s" % (J["allPassed"], J["motherUnchanged"], J["package37Unchanged"],
          json.dumps({k: (v if k == "onlyExpected" else len(v)) for k, v in J["assetDiff"].items()}), json.dumps(rest)), flush=True)
    if J.get("validation"): print("VALIDATION " + json.dumps(J["validation"], ensure_ascii=False)[:3500], flush=True)


if __name__ == "__main__":
    main()
