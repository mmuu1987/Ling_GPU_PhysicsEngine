import datetime as dt, hashlib, json, os, subprocess, sys, time
from pathlib import Path
ROOT = Path.cwd()
P3OUT = ROOT / "outputs" / "P3PlayerBurst-20261009-01"
EXE = P3OUT / "WindowsPlayerBurst-01" / "P3BurstValidation.exe"
OUT = P3OUT / "validation-02-BAAB"
B37 = ROOT / "Builds" / "CaptureDefault-20261006-37"
def sha(p):
    h = hashlib.sha256()
    with p.open("rb") as f:
        for c in iter(lambda: f.read(1 << 20), b""): h.update(c)
    return h.hexdigest()
def git(*a):
    env = os.environ.copy(); env["GIT_OPTIONAL_LOCKS"] = "0"
    return subprocess.check_output(["git", *a], cwd=ROOT, env=env).decode().strip()
def snap():
    st = {}
    for r in ("Assets", "Packages", "ProjectSettings"):
        for p in (ROOT / r).rglob("*"):
            if p.is_file(): s = p.stat(); st[str(p.relative_to(ROOT))] = [s.st_size, s.st_mtime_ns]
    return {"head": git("rev-parse", "HEAD"), "index": sha(ROOT / ".git" / "index"), "stats": st,
            "b37": {str(p.relative_to(B37)): sha(p) for p in sorted(B37.rglob("*")) if p.is_file()},
            "exe": sha(EXE)}
def wj(d): (OUT / "journal.json").write_text(json.dumps(d, ensure_ascii=False, indent=2), encoding="utf-8")
if OUT.exists(): raise SystemExit("exists")
if not EXE.is_file(): raise SystemExit("exe missing")
ps = subprocess.check_output(["powershell", "-NoProfile", "-Command", "@(Get-Process Unity,WarSandbox,P9WindowsValidation,P3BurstValidation -ErrorAction SilentlyContinue).Count"], text=True).strip()
if ps != "0": raise SystemExit("process present " + ps)
OUT.mkdir(parents=True)
before = snap()
j = {"name": "p3-player-burst-baab-02", "started": dt.datetime.now().astimezone().isoformat(), "order": "BAAB", "runs": [], "gates": {}}
wj(j)
for label, burst in (("B3-burst", True), ("A3-managed", False), ("A4-managed", False), ("B4-burst", True)):
    rd = OUT / ("player-" + label); rd.mkdir()
    (rd / "guide.json").write_text('{"version":1,"dismissed":true}\n', encoding="utf-8")
    (rd / "globals.json").write_text("{}\n", encoding="utf-8")
    args = [str(EXE), "-force-d3d11", "-screen-width", "1920", "-screen-height", "1080", "-screen-fullscreen", "0",
            "--war-sandbox-menu", "--p3-player-frame-validation", "--p3-player-output=" + str(rd), "--interaction-p8-output=" + str(rd),
            "--war-sandbox-guide-file=" + str(rd / "guide.json"), "--war-sandbox-settings-file=" + str(rd / "settings.json"),
            "--war-sandbox-review-global-file=" + str(rd / "globals.json"), "-logFile", str(rd / "Player.log")]
    if burst: args.append("--war-sandbox-nav-burst")
    p = subprocess.Popen(args, cwd=str(EXE.parent))
    try: rc = p.wait(timeout=900)
    except subprocess.TimeoutExpired:
        subprocess.run(["taskkill", "/PID", str(p.pid), "/T", "/F"]); rc = None
    rp = rd / "result.json"
    r = json.loads(rp.read_text(encoding="utf-8")) if rp.is_file() else {}
    j["runs"].append({"label": label, "burst": burst, "exit": rc, "valid": bool(r.get("passed") and rc == 0), "result": r})
    print(label, rc, r.get("medianMs"), r.get("p99Ms"), r.get("over33"), r.get("over50"), flush=True)
    wj(j); time.sleep(5)
R = {x["label"]: x["result"] for x in j["runs"]}
for b, a in (("B3-burst", "A3-managed"), ("B4-burst", "A4-managed")):
    rb, ra = R[b], R[a]
    g = {"p99Ratio": rb["p99Ms"] / ra["p99Ms"], "medianRatio": rb["medianMs"] / ra["medianMs"]}
    g["p99"] = g["p99Ratio"] <= 0.80; g["median"] = g["medianRatio"] <= 1.05
    g["over33"] = rb["over33"] <= 0.5 * ra["over33"]; g["over50"] = rb["over50"] <= max(ra["over50"], 3)
    ns, ne = rb["navAtSampleStart"], rb["navAtSampleEnd"]
    g["burstPath"] = ns["preparationState"] == "Ready" and ne["preparationState"] == "Ready" and ne["burstSolves"] > ns["burstSolves"] and ne["managedSolves"] == ns["managedSolves"] and ne["managedNativeSolves"] == 0
    ms, me = ra["navAtSampleStart"], ra["navAtSampleEnd"]
    g["managedControl"] = me["burstSolves"] == 0 and me["managedSolves"] > ms["managedSolves"]
    g["passed"] = all(g[k] for k in ("p99", "median", "over33", "over50", "burstPath", "managedControl"))
    j["gates"][b + "/" + a] = g
j["allRunsValid"] = all(x["valid"] for x in j["runs"])
j["baabPassed"] = j["allRunsValid"] and all(g["passed"] for g in j["gates"].values())
after = snap()
j["audit"] = {"motherUnchanged": all(after[k] == before[k] for k in ("head", "index", "stats")), "package37Unchanged": after["b37"] == before["b37"], "exeUnchanged": after["exe"] == before["exe"]}
j["finished"] = dt.datetime.now().astimezone().isoformat()
wj(j)
print(json.dumps({k: j[k] for k in ("gates", "allRunsValid", "baabPassed", "audit")}, indent=1))
print("|".join("%s med=%.3f p95=%.2f p99=%.2f max=%.2f o33=%d o50=%d frames=%d" % (x["label"], x["result"]["medianMs"], x["result"]["p95Ms"], x["result"]["p99Ms"], x["result"]["maxMs"], x["result"]["over33"], x["result"]["over50"], x["result"]["frames"]) for x in j["runs"]))
