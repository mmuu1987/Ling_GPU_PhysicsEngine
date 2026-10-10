"""P9 Player first-use latency run (2026-10-09, run 06).

Reuses the already-built Windows x64 Development Player from
outputs/P9FirstCommand-20261009-01/WindowsPlayerPrepared-05 (built from P12 by
validate_p9_windows_player_05 run). No Unity Editor, no rebuild, no project edits.

Fix vs -05 runs: -05 pre-created settings.json/guide.json as "{}". "{}" settings is
treated as invalid -> settings panel opens at startup -> TryEnterBattlefield rejected.
Here settings file is NOT created (fresh profile, no prompt) and guide file is a valid
dismissed record so the first-deployment intro does not block commands.
"""
import datetime as dt
import hashlib
import json
import os
import subprocess
import sys
import time
from pathlib import Path

ROOT = Path.cwd()
TRIAL = ROOT / "outputs" / "P9FirstCommand-20261009-01"
BUILD_DIR = TRIAL / "WindowsPlayerPrepared-05"
EXE = BUILD_DIR / "P9WindowsValidation.exe"
OUT = ROOT / "outputs" / "P9PlayerCold-20261009-01"
LOCALLOW = Path(os.environ.get("USERPROFILE", "")) / "AppData" / "LocalLow"
PROTECTED_37 = ROOT / "Builds" / "CaptureDefault-20261006-37"

RUNS = [
    ("d3d11-launch1", "Direct3D11", "-force-d3d11"),
    ("d3d11-launch2", "Direct3D11", "-force-d3d11"),
    ("d3d12-launch1", "Direct3D12", "-force-d3d12"),
    ("d3d12-launch2", "Direct3D12", "-force-d3d12"),
]


def sha(p):
    h = hashlib.sha256()
    with p.open("rb") as f:
        for c in iter(lambda: f.read(1 << 20), b""):
            h.update(c)
    return h.hexdigest()


def tree(root):
    if not root.exists():
        return {}
    out = {}
    for p in sorted(root.rglob("*")):
        if p.is_file():
            try:
                out[str(p.relative_to(root)).replace("\\", "/")] = sha(p)
            except OSError as ex:
                out[str(p.relative_to(root)).replace("\\", "/")] = "unreadable:" + repr(ex)
    return out


def user_data_snapshot():
    snap = {}
    if LOCALLOW.is_dir():
        for company in LOCALLOW.iterdir():
            if not company.is_dir():
                continue
            for product in company.iterdir():
                name = (company.name + "/" + product.name)
                if product.is_dir() and ("WarSandbox" in name or "Ling" in name or "P9" in name or "Mass" in name):
                    snap[name] = tree(product)
    return snap


def save(path, data):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def procs():
    return subprocess.check_output(["powershell", "-NoProfile", "-Command",
        "@(Get-Process Unity,WarSandbox,P9WindowsValidation -ErrorAction SilentlyContinue).Count"], text=True).strip()


def main():
    if OUT.exists():
        raise SystemExit("refusing to overwrite " + str(OUT))
    if not EXE.is_file():
        raise SystemExit("prebuilt player missing: " + str(EXE))
    if procs() != "0":
        raise SystemExit("Unity/game process already running; refusing")
    OUT.mkdir(parents=True)
    journal = {"name": "p9-player-cold-06", "started": dt.datetime.now().astimezone().isoformat(),
               "exe": str(EXE), "exeSha256": sha(EXE), "runs": [], "notes": [
                   "Prebuilt -05 Player reused; no Editor, no rebuild, no project edits.",
                   "GPU driver shader cache not cleared (global state); launch1 vs launch2 reported instead.",
                   "Earlier -05 launches of this exe never reached battle, so local-order kernels were never used by this exe before launch1.",
                   "Editor D3D11 runs used the same DXBC kernels; NVIDIA driver cache may already hold D3D11 objects."]}
    journal["userDataBefore"] = user_data_snapshot()
    journal["protected37Before"] = tree(PROTECTED_37)
    save(OUT / "journal.json", journal)
    for label, api, flag in RUNS:
        run_dir = OUT / label
        run_dir.mkdir(parents=True)
        (run_dir / "guide.json").write_text('{"version":1,"dismissed":true}\n', encoding="utf-8")
        (run_dir / "globals.json").write_text("{}\n", encoding="utf-8")
        log = run_dir / "Player.log"
        args = [str(EXE), flag, "-screen-width", "1280", "-screen-height", "720", "-screen-fullscreen", "0",
                "--war-sandbox-menu", "--p9-player-validation", "--p9-expected-api=" + api,
                "--p9-player-output=" + str(run_dir), "--interaction-p8-output=" + str(run_dir),
                "--war-sandbox-guide-file=" + str(run_dir / "guide.json"),
                "--war-sandbox-settings-file=" + str(run_dir / "settings.json"),
                "--war-sandbox-review-global-file=" + str(run_dir / "globals.json"),
                "-logFile", str(log)]
        print("PLAYER_START", label, flush=True)
        t0 = time.perf_counter()
        p = subprocess.Popen(args, cwd=str(BUILD_DIR))
        try:
            rc = p.wait(timeout=600)
        except subprocess.TimeoutExpired:
            subprocess.run(["taskkill", "/PID", str(p.pid), "/T", "/F"], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            rc = None
        wall = time.perf_counter() - t0
        rp = run_dir / "result.json"
        result = json.loads(rp.read_text(encoding="utf-8")) if rp.is_file() else None
        row = {"label": label, "api": api, "exitCode": rc, "wallSeconds": round(wall, 2),
               "passed": bool(result and result.get("passed") and rc == 0),
               "error": result.get("error") if result else "no result.json",
               "agentCount": result.get("agentCount") if result else None,
               "stages": result.get("stages") if result else None,
               "commands": result.get("commands") if result else None}
        if log.is_file():
            lines = log.read_text(encoding="utf-8", errors="replace").splitlines()
            row["logTail"] = [x for x in lines if "P9_PLAYER" in x or "Exception" in x or "Fatal" in x][-60:]
        journal["runs"].append(row)
        save(OUT / "journal.json", journal)
        print("PLAYER_DONE", label, "passed=", row["passed"], "error=", row["error"], flush=True)
        if row["commands"]:
            for c in row["commands"]:
                print("  CMD", json.dumps(c, ensure_ascii=False)[:400], flush=True)
        time.sleep(3)
    journal["userDataAfter"] = user_data_snapshot()
    journal["userDataUnchanged"] = journal["userDataAfter"] == journal["userDataBefore"]
    journal["protected37Unchanged"] = tree(PROTECTED_37) == journal["protected37Before"]
    journal["finished"] = dt.datetime.now().astimezone().isoformat()
    journal["noLeftoverProcess"] = procs() == "0"
    save(OUT / "journal.json", journal)
    print("ALL_DONE userDataUnchanged=", journal["userDataUnchanged"], "protected37Unchanged=", journal["protected37Unchanged"], flush=True)


if __name__ == "__main__":
    main()
