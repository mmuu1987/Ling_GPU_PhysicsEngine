"""P3 Burst default-on candidate regression on P12 (2026-10-10, user-authorized). Adapted from p12_regression_20261009.py.

Serial, one Unity process at a time, isolated P12 only. Stages:
  1 nav-editmode     : EditMode MassEngine.Tests TerrainNavigationGridTests
  2 first-command    : PlayMode P9FirstCommandTests (3 tests)
  3 combined x4      : P9CombinedAcceptanceTests managed/optin x joint/repeat (repeat runs rendered GUI, as baseline)
No source edits. Restores P12 ProjectSettings, persistent TestResults.xml and (GUI) Editor prefs it touched.
Mother project (Assets/Packages/ProjectSettings stat), git HEAD/index and the 37 package are audited before/after.
"""
import datetime as dt, hashlib, json, os, re, subprocess, sys, time
import xml.etree.ElementTree as ET
from pathlib import Path

sys.stdout.reconfigure(encoding="utf-8")
ROOT = Path.cwd()
P = ROOT / "outputs" / "P12"
O = ROOT / "outputs" / "P3BurstDefault-20261010-01" / "regression-01"
U = Path(r"D:\soft\Unity6\6000.3.14f1\Editor\Unity.exe")
B37 = ROOT / "Builds" / "CaptureDefault-20261006-37"
LOCALLOW = Path(os.environ["USERPROFILE"]) / "AppData" / "LocalLow"
PREFS = Path(os.environ["APPDATA"]) / "Unity" / "Editor-5.x" / "Preferences"
GUI_PREFS = [PREFS / "GameViewSizes.asset", PREFS / "Layouts" / "current" / "default-6000.dwlt"]
env = os.environ.copy(); env["GIT_OPTIONAL_LOCKS"] = "0"


def sha(p):
    h = hashlib.sha256()
    with open(p, "rb") as f:
        for c in iter(lambda: f.read(1 << 20), b""): h.update(c)
    return h.hexdigest()


def save(p, d):
    p.parent.mkdir(parents=True, exist_ok=True)
    p.write_text(json.dumps(d, ensure_ascii=False, indent=2), encoding="utf-8")


def git(*a):
    return subprocess.check_output(["git", *a], cwd=ROOT, env=env).decode().strip()


def stat_tree(roots):
    out = {}
    for r in roots:
        for f in r.rglob("*"):
            if f.is_file():
                st = f.stat(); out[f.relative_to(ROOT).as_posix()] = [st.st_size, st.st_mtime_ns]
    return out


def hash_tree(root):
    return {f.relative_to(root).as_posix(): sha(f) for f in sorted(root.rglob("*")) if f.is_file()} if root.exists() else {}


def procs():
    q = subprocess.run(["powershell", "-NoProfile", "-Command",
        "@(Get-Process Unity,WarSandbox,P9WindowsValidation -ErrorAction SilentlyContinue).Count"], capture_output=True, text=True)
    return q.stdout.strip()


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


def mother_snapshot():
    return {"head": git("rev-parse", "HEAD"), "index": sha(ROOT / ".git" / "index"),
            "stats": stat_tree([ROOT / "Assets", ROOT / "Packages", ROOT / "ProjectSettings"]),
            "b37": hash_tree(B37)}


T = "MassEngine.Game.Tests.P9CombinedAcceptanceTests."
STAGES = [
    ("nav-editmode-default", dict(platform="EditMode", asm="MassEngine.Tests", filt="MassEngine.Tests.TerrainNavigationGridTests", gui=False, managed=False, timeout=1800)),
    ("burst-gate-editmode-default", dict(platform="EditMode", asm="MassEngine.Tests", filt="MassEngine.Tests.BurstDependencyRepairPolicyTests;MassEngine.Tests.BurstDependencyRepairRuntimeTests", gui=False, managed=False, timeout=1800)),
    ("burst-gate-editmode-managed", dict(platform="EditMode", asm="MassEngine.Tests", filt="MassEngine.Tests.BurstDependencyRepairPolicyTests;MassEngine.Tests.BurstDependencyRepairRuntimeTests", gui=False, managed=True, timeout=1800)),
    ("first-command-default", dict(platform="PlayMode", asm="Game.PlayModeTests", filt="MassEngine.Game.Tests.P9FirstCommandTests", gui=False, managed=False, timeout=2400)),
    ("default-joint", dict(platform="PlayMode", asm="Game.PlayModeTests", filt=T + "P9SingleSessionRadiusDeploymentPlansOrdersNaturalResultAndSceneSwitch", gui=False, managed=False, timeout=2400)),
    ("default-repeat", dict(platform="PlayMode", asm="Game.PlayModeTests", filt=T + "P9Repeated2048ScopedFeedbackAndResourceRelease", gui=True, managed=False, timeout=2400)),
    ("managed-joint", dict(platform="PlayMode", asm="Game.PlayModeTests", filt=T + "P9SingleSessionRadiusDeploymentPlansOrdersNaturalResultAndSceneSwitch", gui=False, managed=True, timeout=2400)),
    ("managed-repeat", dict(platform="PlayMode", asm="Game.PlayModeTests", filt=T + "P9Repeated2048ScopedFeedbackAndResourceRelease", gui=True, managed=True, timeout=2400)),
]


def run_stage(name, c, journal):
    V = P / "Logs" / "InteractionRefinement-20261007" / "P8" / ("p3bd01-" + name); V.mkdir(parents=True)  # isolation guard requires this root
    (V / "guide.json").write_text('{"version":1,"dismissed":true}', encoding="utf-8")
    args = [str(U), "-force-d3d11", "-projectPath", str(P), "-logFile", str(V / "Editor.log"), "-runTests",
            "-testPlatform", c["platform"], "-assemblyNames", c["asm"], "-testResults", str(V / "results.xml"),
            "-testFilter", c["filt"], "--interaction-p8-output=" + str(V),
            "--war-sandbox-guide-file=" + str(V / "guide.json"), "--war-sandbox-settings-file=" + str(V / "audio.json"),
            "--war-sandbox-review-global-file=" + str(V / "globals.json"), "--war-sandbox-menu",
            "-screen-width", "1280", "-screen-height", "720"]
    if not c["gui"]: args.insert(1, "-batchmode")
    else: args += ["--interaction-p8-owned-editor"]
    if c["managed"]: args += ["--war-sandbox-nav-managed"]
    settings_before = {f: f.read_bytes() for f in (P / "ProjectSettings").glob("*") if f.is_file()}
    prefs_before = {str(f): (f.read_bytes() if f.exists() else None) for f in GUI_PREFS} if c["gui"] else {}
    ud_before = {k: f.read_bytes() for k, f in userdata().items() if f.name == "TestResults.xml"}
    s = {"name": name, "args": args, "started": dt.datetime.now().astimezone().isoformat(), "gui": c["gui"], "managedOverride": c["managed"]}
    print("STAGE_START", name, flush=True)
    t0 = time.perf_counter()
    with (V / "launcher.log").open("w", encoding="utf-8") as log:
        child = subprocess.Popen(args, cwd=str(P), stdout=log, stderr=subprocess.STDOUT)
        try: s["exitCode"] = child.wait(timeout=c["timeout"])
        except subprocess.TimeoutExpired:
            subprocess.run(["taskkill", "/PID", str(child.pid), "/T", "/F"], capture_output=True); child.wait(60)
            s["exitCode"] = child.returncode; s["timedOut"] = True
    s["wallSeconds"] = round(time.perf_counter() - t0, 1)
    xml = V / "results.xml"
    if xml.exists():
        r = ET.parse(xml).getroot(); s["tests"] = {k: r.get(k) for k in ("result", "total", "passed", "failed", "skipped", "inconclusive", "duration")}
        s["cases"] = [{"name": t.get("fullname"), "result": t.get("result"), "duration": t.get("duration"),
                       "message": (t.findtext("failure/message") or t.findtext("reason/message") or "")[:600]} for t in r.iter("test-case")]
    text = (V / "Editor.log").read_text(encoding="utf-8", errors="replace") if (V / "Editor.log").exists() else ""
    s["compileErrors"] = sorted(set(re.findall(r"^.*error CS\d+.*$", text, re.M)))[:20]
    s["shaderErrors"] = sorted(set(re.findall(r"^.*Shader error.*$", text, re.M)))[:20]
    # restore what Unity may have auto-written
    s["restoredProjectSettings"] = []
    for f, data in settings_before.items():
        if f.is_file() and f.read_bytes() != data:
            (V / "auto-settings-generated").mkdir(exist_ok=True); (V / "auto-settings-generated" / f.name).write_bytes(f.read_bytes())
            f.write_bytes(data); s["restoredProjectSettings"].append(f.name)
    for f in (P / "ProjectSettings").glob("*"):
        if f.is_file() and f not in settings_before:
            (V / "auto-settings-generated").mkdir(exist_ok=True); (V / "auto-settings-generated" / f.name).write_bytes(f.read_bytes()); f.unlink()
            s["restoredProjectSettings"].append("removed-new:" + f.name)
    if c["gui"]:
        if procs() == "0":
            for f, data in prefs_before.items():
                p = Path(f)
                if data is None:
                    if p.exists(): p.unlink()
                elif not p.exists() or p.read_bytes() != data:
                    p.parent.mkdir(parents=True, exist_ok=True); p.write_bytes(data)
            s["guiPrefsRestored"] = True
        else: s["guiPrefsRestored"] = False
    s["restoredTestResults"] = []
    for k, f in userdata().items():
        if f.name != "TestResults.xml": continue
        before = ud_before.get(k)
        if before is None:
            (V / "persistent-generated").mkdir(exist_ok=True); (V / "persistent-generated" / (k.replace("/", "_"))).write_bytes(f.read_bytes()); f.unlink()
            s["restoredTestResults"].append("removed-new:" + k)
        elif f.read_bytes() != before:
            f.write_bytes(before); s["restoredTestResults"].append(k)
    t = s.get("tests", {})
    s["passed"] = (s.get("exitCode") == 0 and not s.get("timedOut") and not s["compileErrors"] and not s["shaderErrors"]
                   and t.get("result", "").startswith("Passed") and int(t.get("failed") or 0) == 0 and int(t.get("passed") or 0) > 0)
    s["finished"] = dt.datetime.now().astimezone().isoformat()
    save(V / "stage.json", s); save(O / (name + ".json"), s)
    try:
        rg = V / "runtime-gate.json"
        if rg.exists(): s["runtimeGate"] = json.loads(rg.read_text(encoding="utf-8"))
    except Exception as ex: s["runtimeGateError"] = repr(ex)
    print("STAGE_DONE", name, "passed=", s["passed"], "tests=", json.dumps(t), "wall=", s["wallSeconds"], flush=True)
    for cse in s.get("cases", []):
        print("   CASE", cse["result"], cse["name"], cse["duration"], cse["message"][:200], flush=True)
    return s


def main():
    if O.exists(): raise SystemExit("refusing to overwrite " + str(O))
    if not P.is_dir() or not U.is_file(): raise SystemExit("P12 or Unity missing")
    if procs() != "0": raise SystemExit("Unity/game process already running; refusing")
    O.mkdir(parents=True)
    journal = {"name": "p3-burst-default-candidate-regression-01", "started": dt.datetime.now().astimezone().isoformat(), "stages": []}
    journal["p12AssetsBefore"] = hash_tree(P / "Assets" / "MassEngine") | {"Game/" + k: v for k, v in hash_tree(P / "Assets" / "Game").items()}
    print("SNAPSHOT mother/37 ...", flush=True)
    mb = mother_snapshot(); save(O / "mother-before.json", mb)
    journal["userDataBefore"] = {k: sha(f) for k, f in userdata().items()}
    save(O / "journal.json", journal)
    for name, c in STAGES:
        while procs() != "0": time.sleep(5)
        s = run_stage(name, c, journal); journal["stages"].append({k: s.get(k) for k in ("name", "passed", "tests", "wallSeconds", "exitCode", "timedOut", "cases", "compileErrors", "runtimeGate", "managedOverride")})
        save(O / "journal.json", journal)
        if s.get("compileErrors"): print("ABORT: compile errors", flush=True); break
        time.sleep(5)
    ma = mother_snapshot()
    journal["motherUnchanged"] = ma["head"] == mb["head"] and ma["index"] == mb["index"] and ma["stats"] == mb["stats"]
    journal["package37Unchanged"] = ma["b37"] == mb["b37"]
    after = {"Game/" + k: v for k, v in hash_tree(P / "Assets" / "Game").items()}
    journal["p12AssetsUnchanged"] = (hash_tree(P / "Assets" / "MassEngine") | after) == journal["p12AssetsBefore"]
    journal["userDataAfter"] = {k: sha(f) for k, f in userdata().items()}
    journal["userDataUnchanged"] = journal["userDataAfter"] == journal["userDataBefore"]
    journal["allPassed"] = all(x["passed"] for x in journal["stages"]) and len(journal["stages"]) == len(STAGES)
    journal["finished"] = dt.datetime.now().astimezone().isoformat()
    del journal["p12AssetsBefore"]
    save(O / "journal.json", journal)
    print("ALL_DONE allPassed=", journal["allPassed"], "mother=", journal["motherUnchanged"], "b37=", journal["package37Unchanged"],
          "p12Assets=", journal["p12AssetsUnchanged"], "userData=", journal["userDataUnchanged"], flush=True)


if __name__ == "__main__":
    main()

