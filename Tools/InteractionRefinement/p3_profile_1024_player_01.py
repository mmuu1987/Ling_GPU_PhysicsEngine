import datetime as dt
import hashlib
import json
import os
import re
from pathlib import Path
import shutil
import subprocess
import sys
import time
import uuid

ROOT = Path(r"E:\GitHub\Ling_GPU_PhysicsEngine\Ling_GPU_PhysicsEngine")
P12 = ROOT / "outputs" / "P12"
OUT = ROOT / "outputs" / "P3Profile1024-20261010-01"
MAP_DIR = P12 / "Assets" / "Game" / "Content" / "Battlefields" / "Map1024"
CATALOG = P12 / "Assets" / "Game" / "Scenes" / "Catalog.asset"
MAP_MANIFEST = ROOT / "outputs" / "Map1024-20261010-01" / "candidate-manifest.json"
BUILD_DIR = OUT / "WindowsPlayer-01"
B37 = ROOT / "Builds" / "CaptureDefault-20261006-37"
PATCH_FILES = ["Assets/MassEngine/Terrain/TerrainNavigationRuntime.cs", "Assets/MassEngine/Terrain/TerrainNavigationGrid.cs"]
DEFINE_RE = re.compile(r"^#if UNITY_EDITOR(\r?)$", re.M)
UNITY = Path(r"D:\soft\Unity6\6000.3.14f1\Editor\Unity.exe")
CACHE = P12 / "Library" / "ShaderCache"
WARMUP_STATE = P12 / "Library" / "WarSandbox"
WARMUP_MARKER = WARMUP_STATE / "P9ShaderWarmup-D3D11.json"
HARNESS_REL = Path("Assets/Game/Scripts/P3ProfileFrameHarness.cs")
BUILDER_REL = Path("Assets/Game/Editor/P3ProfileBuild.cs")
HARNESS_GUID = "3e7a1c5f9b2d4a6e8c0f1a3b5d7e9f12"
BUILDER_GUID = "4f8b2d6a0c3e4b7f9d1a2c4e6f8a0b23"
EXE_NAME = "P3Profile1024Validation"
RUNS = [("01-D3D11-100k-1024-profile", "d3d11", 100000), ("02-D3D11-200k-1024-profile", "d3d11", 200000)]
NO_PROGRESS_KILL_S = 300
ABSOLUTE_KILL_S = 1200


def sha(path):
    h = hashlib.sha256()
    with path.open("rb") as f:
        for chunk in iter(lambda: f.read(1024 * 1024), b""):
            h.update(chunk)
    return h.hexdigest()


def tree_manifest(root):
    if not root.exists():
        return {}
    return {str(p.relative_to(root)).replace("\\", "/"): {"sha256": sha(p), "bytes": p.stat().st_size}
            for p in sorted(root.rglob("*")) if p.is_file()}


def write_json(path, data):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def print_step(text, journal):
    print(text, flush=True)
    journal["events"].append({"time": dt.datetime.now().astimezone().isoformat(), "event": text})
    write_json(OUT / "journal.json", journal)


def run_unity(label, args, timeout=1800):
    log = OUT / (label + ".log")
    full = [str(UNITY)] + args
    if full[-1] == "-quit":
        full[-1:-1] = ["-logFile", str(log)]
    else:
        full.extend(["-logFile", str(log)])
    print("UNITY_START", label, json.dumps(full, ensure_ascii=False), flush=True)
    with log.open("w", encoding="utf-8", errors="replace") as f:
        p = subprocess.Popen(full, cwd=str(ROOT), stdout=f, stderr=subprocess.STDOUT)
        try:
            rc = p.wait(timeout=timeout)
        except subprocess.TimeoutExpired:
            subprocess.run(["taskkill", "/PID", str(p.pid), "/T", "/F"], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            raise RuntimeError(label + " timed out; killed only owned Unity process tree")
    text = log.read_text(encoding="utf-8", errors="replace") if log.exists() else ""
    print("UNITY_EXIT", label, rc, "logBytes", len(text), flush=True)
    return {"label": label, "exitCode": rc, "log": str(log), "logBytes": len(text),
            "scriptCompilationErrors": [line for line in text.splitlines() if "error CS" in line or "Script Compilation Error" in line],
            "playerBuildConfigLines": [line for line in text.splitlines() if "P9 PLAYER BUILD" in line or "P3P PLAYER BUILD" in line],
            "contextLines": [line for line in text.splitlines() if "P9 PLAYER CONTEXT:" in line],
            "catalogSceneLines": [line for line in text.splitlines() if "P9 PLAYER CATALOG SCENES:" in line],
            "tail": text.splitlines()[-80:]}


def restore_project_settings(original_manifest, settings_backup):
    root = P12 / "ProjectSettings"
    before = set(original_manifest)
    removed, restored = [], []
    if root.exists():
        for p in sorted(root.rglob("*"), reverse=True):
            if p.is_file():
                rel = str(p.relative_to(root)).replace("\\", "/")
                if rel not in before:
                    p.unlink(); removed.append(rel)
        for p in sorted(root.rglob("*"), reverse=True):
            if p.is_dir():
                try: p.rmdir()
                except OSError: pass
    for rel, info in original_manifest.items():
        dst = root / Path(rel)
        src = settings_backup / Path(rel)
        if not dst.exists() or sha(dst) != info["sha256"]:
            dst.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(src, dst)
            restored.append(rel)
    return {"removedNewFiles": removed, "restoredChangedFiles": restored, "matchesOriginal": tree_manifest(root) == original_manifest}


def restore_tree(root, backup, existed):
    if root.exists():
        shutil.rmtree(root)
    if existed:
        shutil.copytree(backup, root)
    return tree_manifest(root) if existed else {}


def cleanup_generated_assets(original_manifest, evidence_dir):
    root = P12 / "Assets"
    before_cleanup = tree_manifest(root)
    original_paths = set(original_manifest)
    current_paths = set(before_cleanup)
    added = sorted(current_paths - original_paths)
    removed = sorted(original_paths - current_paths)
    changed = sorted(p for p in (current_paths & original_paths) if before_cleanup[p] != original_manifest[p])
    known_build_outputs = {
        "Resources.meta",
        "Resources/PerformanceTestRunInfo.json",
        "Resources/PerformanceTestRunInfo.json.meta",
        "Resources/PerformanceTestRunSettings.json",
        "Resources/PerformanceTestRunSettings.json.meta",
    }
    removed_generated = []
    if set(added).issubset(known_build_outputs) and not removed and not changed:
        stash = evidence_dir / "generated-by-player-build"
        for rel in added:
            src = root / Path(rel)
            if not src.is_file():
                continue
            stash.mkdir(parents=True, exist_ok=True)
            dst = stash / Path(rel).name
            shutil.copy2(src, dst)
            removed_generated.append({"path": "Assets/" + rel, "sha256": sha(dst), "bytes": dst.stat().st_size})
            src.unlink()
        resource_dir = root / "Resources"
        try: resource_dir.rmdir()
        except OSError: pass
    after_cleanup = tree_manifest(root)
    return {
        "addedBeforeCleanup": added,
        "removedOriginalFilesBeforeCleanup": removed,
        "changedOriginalFilesBeforeCleanup": changed,
        "knownUnityBuildFilesRemoved": removed_generated,
        "assetsMatchOriginal": after_cleanup == original_manifest,
        "originalFileCount": len(original_manifest),
        "finalFileCount": len(after_cleanup),
    }


def git(*a):
    env = os.environ.copy(); env["GIT_OPTIONAL_LOCKS"] = "0"
    return subprocess.check_output(["git", *a], cwd=ROOT, env=env).decode().strip()


def mother_snapshot():
    stats = {}
    for r in ("Assets", "Packages", "ProjectSettings"):
        for p in (ROOT / r).rglob("*"):
            if p.is_file():
                st = p.stat(); stats[str(p.relative_to(ROOT)).replace("\\", "/")] = [st.st_size, st.st_mtime_ns]
    return {"head": git("rev-parse", "HEAD"), "index": sha(ROOT / ".git" / "index"), "stats": stats,
            "b37": {k: v["sha256"] for k, v in tree_manifest(B37).items()}}


def ratio(b, a):
    return b / a if a else None



def mb(x):
    return round(x / 1048576.0, 1) if isinstance(x, (int, float)) and x > 0 else None


def run_player(player_exe, label, api, units, journal):
    run_dir = OUT / "runs" / label
    run_dir.mkdir(parents=True)
    (run_dir / "guide.json").write_text('{"version":1,"dismissed":true}\n', encoding="utf-8")
    (run_dir / "globals.json").write_text("{}\n", encoding="utf-8")
    run_log = run_dir / "Player.log"
    stdout_log = run_dir / "stdout.txt"
    args = [str(player_exe), "-force-" + api, "-screen-width", "1920", "-screen-height", "1080", "-screen-fullscreen", "0",
            "--war-sandbox-menu", "--p3p-profile", "--p3p-units=" + str(units),
            "--p3p-output=" + str(run_dir), "--interaction-p8-output=" + str(run_dir),
            "--war-sandbox-guide-file=" + str(run_dir / "guide.json"),
            "--war-sandbox-settings-file=" + str(run_dir / "settings.json"),
            "--war-sandbox-review-global-file=" + str(run_dir / "globals.json"),
            "-logFile", str(run_log)]
    print("PLAYER_START", label, json.dumps(args, ensure_ascii=False), flush=True)
    t0 = time.time(); killed = None
    with stdout_log.open("w", encoding="utf-8", errors="replace") as f:
        proc = subprocess.Popen(args, cwd=str(BUILD_DIR), stdout=f, stderr=subprocess.STDOUT)
        last_progress = time.time(); last_mtime = None
        while True:
            try:
                exit_code = proc.wait(timeout=5)
                break
            except subprocess.TimeoutExpired:
                pass
            prog = run_dir / "progress.txt"
            m = prog.stat().st_mtime if prog.exists() else None
            if m != last_mtime:
                last_mtime = m; last_progress = time.time()
            now = time.time()
            if now - last_progress > NO_PROGRESS_KILL_S or now - t0 > ABSOLUTE_KILL_S:
                killed = "no-progress-%ds" % NO_PROGRESS_KILL_S if now - last_progress > NO_PROGRESS_KILL_S else "absolute-%ds" % ABSOLUTE_KILL_S
                subprocess.run(["taskkill", "/PID", str(proc.pid), "/T", "/F"], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
                try: exit_code = proc.wait(timeout=30)
                except subprocess.TimeoutExpired: exit_code = None
                break
    wall = time.time() - t0
    result_path = run_dir / "result.json"
    result = None
    if result_path.is_file():
        try: result = json.loads(result_path.read_text(encoding="utf-8"))
        except Exception as ex: result = {"parseError": repr(ex)}
    if killed: status = "FAILED(timeout/hang:" + killed + ")"
    elif result is None: status = "FAILED(crash/no-result exit=%s)" % exit_code
    elif result.get("passed") and exit_code == 0: status = "OK"
    else: status = "FAILED(" + str(result.get("error"))[:160] + ")"
    row = {"label": label, "api": api, "units": units, "exitCode": exit_code, "killed": killed, "wallSeconds": round(wall, 1),
           "status": status, "result": result}
    if run_log.is_file():
        lines = run_log.read_text(encoding="utf-8", errors="replace").splitlines()
        keys = ("Direct3D", "D3D12", "D3D11", "GfxDevice", "Burst", "Exception", "P3P_", "out of memory", "Out of memory", "Crash", "crash", "TDR", "removed", "Renderer:", "Vendor:", "VRAM")
        row["notableLogLines"] = [x[:400] for x in lines if any(k in x for k in keys)][-80:]
        row["playerLogLines"] = len(lines)
    journal["runtimeRuns"].append(row)
    r = result or {}
    full = r.get("full") or {}
    def med(k):
        v = full.get(k) or {}
        return v.get("median")
    print_step("finished %s status=%s api=%s agents=%s median=%s p95=%s ftCpuMain=%s ftCpuRender=%s ftGpu=%s presentWait=%s dispatches=%s nav=%s ablations=%s" % (
        label, status, r.get("graphicsApi"), r.get("agentCount"), r.get("medianMs"), r.get("p95Ms"), med("ftCpuMain"), med("ftCpuRender"), med("ftGpu"), med("ftPresentWait"),
        (full.get("dispatchesPerFrame") or {}).get("median"), r.get("navBackend"), [(x.get("mode"), round(x.get("medianMs") or 0, 2), round(((x.get("ftGpu") or {}).get("median") or 0), 2)) for x in (r.get("ablations") or [])]), journal)
    time.sleep(8)
    return row


def write_summary(journal):
    def f(v, n=2):
        return "-" if v is None else (("%." + str(n) + "f") % v if isinstance(v, float) else str(v))
    rows, md = [], ["# P3 1024 frame-time breakdown (Windows x64 Development Player, D3D11)", ""]
    for x in journal.get("runtimeRuns", []):
        r = x.get("result") or {}
        full = r.get("full") or {}
        def st(w, k):
            v = (w or {}).get(k) or {}
            return v.get("median"), v.get("p95")
        row = {"label": x["label"], "api": r.get("graphicsApi") or x["api"], "units": x["units"], "agents": r.get("agentCount"), "status": x["status"],
               "frameMedian": r.get("medianMs"), "frameP95": r.get("p95Ms"), "frames": r.get("frames"), "fps": r.get("avgFps"),
               "ftEnabled": r.get("frameTimingFeatureEnabled"), "cpuFrame": st(full, "ftCpuFrame"), "cpuMain": st(full, "ftCpuMain"), "cpuRender": st(full, "ftCpuRender"),
               "presentWait": st(full, "ftPresentWait"), "gpu": st(full, "ftGpu"), "dispatches": st(full, "dispatchesPerFrame"),
               "navBackend": r.get("navBackend"), "navSolveMsPerFrame": r.get("navSolveMsPerFrameInWindow"),
               "aliveStart": r.get("aliveAtSampleStart"), "aliveEnd": r.get("aliveAtEnd"),
               "ablations": [{"mode": a.get("mode"), "frameMedian": a.get("medianMs"), "frameP95": a.get("p95Ms"), "cpuMain": st(a, "ftCpuMain"), "cpuRender": st(a, "ftCpuRender"),
                              "gpu": st(a, "ftGpu"), "presentWait": st(a, "ftPresentWait"), "dispatches": st(a, "dispatchesPerFrame"), "frames": a.get("frames")} for a in (r.get("ablations") or [])],
               "topTimeMarkers": [m for m in (full.get("markers") or []) if m.get("unit") == "ms"][:45],
               "counters": [m for m in (full.get("markers") or []) if m.get("unit") != "ms"][:30],
               "dispatchLabels": (r.get("dispatchLabelsInFullWindow") or [])[:40]}
        rows.append(row)
        md += ["## %s — %s, %s agents, status %s" % (row["label"], row["api"], f(row["agents"]), row["status"]), "",
               "Frame (Stopwatch) median %s ms / P95 %s ms, %s frames, %s FPS; nav %s, nav solve ms/frame in window %s; alive %s→%s; FrameTimingManager enabled=%s" % (
                   f(row["frameMedian"]), f(row["frameP95"]), f(row["frames"]), f(row["fps"], 1), row["navBackend"], f(row["navSolveMsPerFrame"], 3), f(row["aliveStart"]), f(row["aliveEnd"]), row["ftEnabled"]), "",
               "| window | frame median | frame P95 | CPU main med/P95 | CPU render med/P95 | present wait med | GPU med/P95 | orchestrator dispatches/frame |", "|---|---|---|---|---|---|---|---|"]
        def wline(name, fm, fp, cm, cr, pw, g, d):
            return "| %s | %s | %s | %s / %s | %s / %s | %s | %s / %s | %s |" % (name, f(fm), f(fp), f(cm[0]), f(cm[1]), f(cr[0]), f(cr[1]), f(pw[0]), f(g[0]), f(g[1]), f(d[0], 1))
        md.append(wline("full (60 s)", row["frameMedian"], row["frameP95"], row["cpuMain"], row["cpuRender"], row["presentWait"], row["gpu"], row["dispatches"]))
        for a in row["ablations"]:
            md.append(wline(a["mode"] + " (20 s)", a["frameMedian"], a["frameP95"], a["cpuMain"], a["cpuRender"], a["presentWait"], a["gpu"], a["dispatches"]))
        md += ["", "Top time markers (full window, ms/frame summed over all threads; median / P95 / mean, occurrences/frame):", "", "| marker | category | median | P95 | mean | calls/frame |", "|---|---|---|---|---|---|"]
        for m in row["topTimeMarkers"]:
            md.append("| %s | %s | %s | %s | %s | %s |" % (m.get("name"), m.get("category"), f(m.get("median"), 3), f(m.get("p95"), 3), f(m.get("mean"), 3), f(m.get("meanCount"), 1)))
        md += ["", "Counters (full window, median):", "", "| counter | unit | median | P95 |", "|---|---|---|---|"]
        for m in row["counters"]:
            md.append("| %s | %s | %s | %s |" % (m.get("name"), m.get("unit"), f(m.get("median"), 1), f(m.get("p95"), 1)))
        md += ["", "Orchestrator dispatch labels (full window): " + "; ".join("%s=%.2f/f" % (d.get("label"), d.get("perFrame") or 0) for d in row["dispatchLabels"]), ""]
    write_json(OUT / "summary.json", {"rows": rows, "build": journal.get("buildInfo"), "restoration": journal.get("restoration")})
    (OUT / "summary.md").write_text("\n".join(md) + "\n", encoding="utf-8")


def main():
    if not P12.is_dir() or not UNITY.is_file():
        raise RuntimeError("P12 project or pinned Unity editor is missing")
    if OUT.exists():
        raise RuntimeError("Refusing: output dir exists " + str(OUT))
    if not MAP_DIR.is_dir() or "forest-green22-1024" not in CATALOG.read_text(encoding="utf-8") or not MAP_MANIFEST.is_file():
        raise RuntimeError("Map1024 candidate missing from P12 (run map1024_p12_20261010_01.py first)")
    cand = json.loads(MAP_MANIFEST.read_text(encoding="utf-8"))
    for rel, h in cand["addedFileSha256"].items():
        if sha(P12 / rel) != h: raise RuntimeError("Map1024 candidate file differs from its manifest: " + rel)
    for rel, info in cand["modifiedFiles"].items():
        if sha(P12 / rel) != info["candidateSha256"]: raise RuntimeError("Map1024 modified file differs from manifest: " + rel)
    if not CACHE.is_dir():
        raise RuntimeError("P12 Library/ShaderCache is missing")
    nav_text = (P12 / PATCH_FILES[0]).read_text(encoding="utf-8")
    if DEFINE_RE.search(nav_text) or "#if UNITY_EDITOR" in nav_text:
        raise RuntimeError("P12 TerrainNavigationRuntime.cs still has #if UNITY_EDITOR gating; stopping per instructions")
    if "Burst is the default in Editor and Player" not in nav_text:
        raise RuntimeError("P12 TerrainNavigationRuntime.cs lacks the Burst-default marker; stopping")
    harness_path = P12 / HARNESS_REL
    builder_path = P12 / BUILDER_REL
    added = [harness_path, Path(str(harness_path) + ".meta"), builder_path, Path(str(builder_path) + ".meta")]
    if any(p.exists() for p in added):
        raise RuntimeError("Temporary asset path already exists; refusing to overwrite")
    ps = subprocess.check_output(["powershell", "-NoProfile", "-Command",
                                  "@(Get-Process Unity,WarSandbox,P9WindowsValidation,P3BurstValidation,P3BurstDefaultValidation,P3ScaleValidation,P3Scale1024Validation," + EXE_NAME + " -ErrorAction SilentlyContinue).Count"], text=True).strip()
    if ps != "0":
        raise RuntimeError("Existing Unity/game process present; refusing parallel project access: " + ps)

    OUT.mkdir(parents=True)
    mother_before = mother_snapshot()
    nav_files = {}
    for rel in PATCH_FILES:
        a, b = P12 / rel, ROOT / rel
        nav_files[rel] = {"p12": sha(a) if a.is_file() else None, "mother": sha(b) if b.is_file() else None}
        nav_files[rel]["equal"] = nav_files[rel]["p12"] == nav_files[rel]["mother"]
    candidate_before = {"map": tree_manifest(MAP_DIR), "catalog": sha(CATALOG)}
    journal = {"name": "p3-profile-1024-frame-breakdown-player-01", "map": "forest-green22-1024", "started": dt.datetime.now().astimezone().isoformat(),
               "status": "running", "plan": [list(x) for x in RUNS], "navSourceFiles": nav_files,
               "events": [], "unityRuns": [], "runtimeRuns": [], "restoration": {}, "errors": []}
    write_json(OUT / "mother-before.json", mother_before)
    write_json(OUT / "journal.json", journal)
    cache_existed = CACHE.exists()
    cache_backup = OUT / "P12-ShaderCache-original"
    shutil.copytree(CACHE, cache_backup)
    cache_before = tree_manifest(CACHE)
    settings_root = P12 / "ProjectSettings"
    settings_backup = OUT / "ProjectSettings-original"
    shutil.copytree(settings_root, settings_backup)
    settings_before = tree_manifest(settings_root)
    user_settings_root = P12 / "UserSettings"
    user_settings_backup = OUT / "UserSettings-original"
    user_settings_existed = user_settings_root.exists()
    if user_settings_existed:
        shutil.copytree(user_settings_root, user_settings_backup)
    user_settings_before = tree_manifest(user_settings_root)
    library_root = P12 / "Library"
    target_pref_relatives = ["EditorUserBuildSettings.asset", "BuildProfileContext.asset"]
    target_pref_backup = OUT / "Library-target-settings-original"
    target_pref_before = {}
    for rel in target_pref_relatives:
        src = library_root / rel
        target_pref_before[rel] = sha(src) if src.is_file() else None
        if src.is_file():
            dst = target_pref_backup / rel
            dst.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(src, dst)
    assets_root = P12 / "Assets"
    assets_before = tree_manifest(assets_root)
    warmup_backup = OUT / "Library-WarSandbox-original"
    warmup_existed = WARMUP_STATE.exists()
    if warmup_existed:
        shutil.copytree(WARMUP_STATE, warmup_backup)
    write_json(OUT / "original-state-manifest.json", {
        "projectSettingsFiles": settings_before, "assetFiles": assets_before, "shaderCacheFiles": cache_before,
        "warmupStateExisted": warmup_existed, "userSettingsFiles": user_settings_before, "targetPreferenceFiles": target_pref_before})
    source_dir = ROOT / "Tools" / "InteractionRefinement"
    harness_text = (source_dir / "P3Profile1024FrameHarness.cs.txt").read_text(encoding="utf-8")
    builder_text = (source_dir / "P3ProfileBuild.cs.txt").read_text(encoding="utf-8")
    (OUT / "P3ProfileFrameHarness.cs.txt").write_text(harness_text, encoding="utf-8")
    (OUT / "P3ProfileBuild.cs.txt").write_text(builder_text, encoding="utf-8")
    original_target = None
    restore_target_needed = False
    target_args = []

    try:
        print_step("preflight passed (Burst default, no UNITY_EDITOR gating); baselines copied", journal)
        harness_path.write_text(harness_text, encoding="utf-8", newline="\r\n")
        builder_path.write_text(builder_text, encoding="utf-8", newline="\r\n")
        Path(str(harness_path) + ".meta").write_text("fileFormatVersion: 2\nguid: " + HARNESS_GUID + "\nMonoImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: -32000\n  icon: {instanceID: 0}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n", encoding="utf-8", newline="\r\n")
        Path(str(builder_path) + ".meta").write_text("fileFormatVersion: 2\nguid: " + BUILDER_GUID + "\nMonoImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n  icon: {instanceID: 0}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n", encoding="utf-8", newline="\r\n")
        journal["temporaryP12Files"] = [str(p.relative_to(P12)).replace("\\", "/") for p in added]
        print_step("installed temporary Player harness and Editor build entry", journal)

        context = run_unity("editor-build-target-before", ["-batchmode", "-force-d3d11", "-projectPath", str(P12),
                           "-executeMethod", "MassEngine.Game.Editor.P3ProfileBuild.ReportContext", "-quit"], timeout=1800)
        journal["unityRuns"].append(context)
        context_match = re.search(r"P9 PLAYER CONTEXT: activeTarget=([A-Za-z0-9_]+)", "\n".join(context["contextLines"]))
        if context["exitCode"] != 0 or context["scriptCompilationErrors"] or context_match is None or not context["catalogSceneLines"]:
            raise RuntimeError("Could not read the original active build target/catalog scenes from Unity")
        original_target = context_match.group(1)
        target_args = [] if original_target == "StandaloneWindows64" else ["-buildTarget", "StandaloneWindows64"]
        restore_target_needed = bool(target_args)
        journal["originalActiveBuildTarget"] = original_target
        journal["contextLines"] = context["contextLines"]
        print_step("original active build target: " + original_target + "; temporary Windows64 switch=" + str(restore_target_needed), journal)

        BUILD_DIR.mkdir(parents=True)
        player_exe = BUILD_DIR / (EXE_NAME + ".exe")
        build_args = ["-batchmode", "-force-d3d11", "-projectPath", str(P12)] + target_args + ["-executeMethod",
                      "MassEngine.Game.Editor.P3ProfileBuild.BuildDevelopmentPlayer",
                      "--p3s-player-build-output=" + str(player_exe), "-quit"]
        bstart = time.time()
        build = run_unity("windows-x64-development-player-build", build_args, timeout=3600)
        journal["unityRuns"].append(build)
        build_summary_path = Path(str(player_exe) + ".build.txt")
        journal["buildSummary"] = build_summary_path.read_text(encoding="utf-8", errors="replace") if build_summary_path.exists() else None
        journal["buildInfo"] = {"exe": str(player_exe), "wallSeconds": round(time.time() - bstart, 1),
                                "finished": dt.datetime.now().astimezone().isoformat(), "summary": journal["buildSummary"],
                                "configLines": build["playerBuildConfigLines"]}
        if (build["exitCode"] != 0 or build["scriptCompilationErrors"] or not player_exe.is_file()
                or not journal["buildSummary"] or "result=Succeeded" not in journal["buildSummary"] or "errors=0" not in journal["buildSummary"]):
            raise RuntimeError("Windows x64 Development Player build did not complete successfully")
        if "Direct3D12" not in journal["buildSummary"]:
            raise RuntimeError("Direct3D12 is not in the built Player's graphics API list")
        journal["buildInfo"]["burstArtifacts"] = [str(p.relative_to(BUILD_DIR)) for p in BUILD_DIR.rglob("*burst*")]
        print_step("Windows x64 Development Player built: " + journal["buildSummary"], journal)

        for label, api, units in RUNS:
            run_player(player_exe, label, api, units, journal)
            write_summary(journal)
        journal["status"] = "completed-measured"
    except Exception as ex:
        journal["status"] = "failed-or-blocked"
        journal["errors"].append(repr(ex))
        print("VALIDATION_ERROR", repr(ex), flush=True)
    finally:
        if restore_target_needed and original_target:
            try:
                restore_args = ["-batchmode", "-force-d3d11", "-projectPath", str(P12), "-buildTarget", original_target,
                                "-executeMethod", "MassEngine.Game.Editor.P3ProfileBuild.ReportContext", "-quit"]
                target_restore = run_unity("restore-original-build-target", restore_args, timeout=1800)
                journal["restoration"]["targetRestoreExit"] = target_restore["exitCode"]
                restore_match = re.search(r"P9 PLAYER CONTEXT: activeTarget=([A-Za-z0-9_]+)", "\n".join(target_restore["contextLines"]))
                journal["restoration"]["activeBuildTargetMatchesOriginal"] = bool(target_restore["exitCode"] == 0 and restore_match and restore_match.group(1) == original_target)
            except Exception as ex:
                journal["restoration"]["activeBuildTargetRestoreError"] = repr(ex)
        try:
            for rel, expected_hash in target_pref_before.items():
                target_file = library_root / rel
                actual_hash = sha(target_file) if target_file.is_file() else None
                if expected_hash is None:
                    if target_file.exists(): target_file.unlink()
                elif actual_hash != expected_hash:
                    target_file.parent.mkdir(parents=True, exist_ok=True)
                    shutil.copy2(target_pref_backup / rel, target_file)
            journal["restoration"]["targetPreferenceFilesMatchOriginal"] = all(
                (sha(library_root / rel) if (library_root / rel).is_file() else None) == expected
                for rel, expected in target_pref_before.items())
        except Exception as ex:
            journal["restoration"]["targetPreferenceRestoreError"] = repr(ex)
        try:
            for p in added:
                if p.exists(): p.unlink()
            journal["restoration"]["temporarySourcesRemoved"] = all(not p.exists() for p in added)
        except Exception as ex:
            journal["restoration"]["temporarySourceCleanupError"] = repr(ex)
        try:
            expected_warmup = tree_manifest(warmup_backup) if warmup_existed else {}
            if WARMUP_STATE.exists(): shutil.rmtree(WARMUP_STATE)
            if warmup_existed: shutil.copytree(warmup_backup, WARMUP_STATE)
            journal["restoration"]["warmupStateMatchesOriginal"] = tree_manifest(WARMUP_STATE) == expected_warmup if warmup_existed else not WARMUP_STATE.exists()
        except Exception as ex:
            journal["restoration"]["warmupStateRestoreError"] = repr(ex)
        try:
            cache_restore = restore_tree(CACHE, cache_backup, cache_existed)
            journal["restoration"]["shaderCacheMatchesOriginal"] = cache_restore == cache_before
        except Exception as ex:
            journal["restoration"]["shaderCacheRestoreError"] = repr(ex)
        try:
            journal["restoration"]["projectSettings"] = restore_project_settings(settings_before, settings_backup)
        except Exception as ex:
            journal["restoration"]["projectSettingsRestoreError"] = repr(ex)
        try:
            user_restored = restore_tree(user_settings_root, user_settings_backup, user_settings_existed)
            journal["restoration"]["userSettingsMatchesOriginal"] = user_restored == user_settings_before
        except Exception as ex:
            journal["restoration"]["userSettingsRestoreError"] = repr(ex)
        try:
            journal["restoration"]["assetCleanup"] = cleanup_generated_assets(assets_before, OUT)
            journal["restoration"]["assetsMatchOriginal"] = journal["restoration"]["assetCleanup"]["assetsMatchOriginal"]
            journal["restoration"]["temporaryAssetsClean"] = not any(p.exists() for p in added)
            journal["restoration"]["map1024CandidateUnchanged"] = {"map": tree_manifest(MAP_DIR), "catalog": sha(CATALOG)} == candidate_before
            journal["restoration"]["navSourcesUnchanged"] = all((sha(P12 / rel) if (P12 / rel).is_file() else None) == v["p12"] for rel, v in nav_files.items())
        except Exception as ex:
            journal["restoration"]["integrityCheckError"] = repr(ex)
        try:
            ma = mother_snapshot()
            journal["restoration"]["motherUnchanged"] = ma["head"] == mother_before["head"] and ma["index"] == mother_before["index"] and ma["stats"] == mother_before["stats"]
            journal["restoration"]["package37Unchanged"] = ma["b37"] == mother_before["b37"]
        except Exception as ex:
            journal["restoration"]["motherCheckError"] = repr(ex)
        def _bools(d, prefix=""):
            out = {}
            for k, v in (d or {}).items():
                if isinstance(v, bool): out[prefix + k] = v
                elif isinstance(v, dict) and k in ("projectSettings",): out[prefix + k + ".matchesOriginal"] = v.get("matchesOriginal")
            return out
        checks = _bools(journal["restoration"])
        journal["restoration"]["allChecksTrue"] = bool(checks) and all(v is True for v in checks.values()) and not any(k.endswith("Error") for k in journal["restoration"])
        write_json(OUT / "p12-restore-verification.json", {"checks": checks, "allChecksTrue": journal["restoration"]["allChecksTrue"], "restoration": journal["restoration"]})
        journal["finished"] = dt.datetime.now().astimezone().isoformat()
        write_json(OUT / "journal.json", journal)
        try: write_summary(journal)
        except Exception as ex: print("SUMMARY_ERROR", repr(ex), flush=True)
        print("VALIDATION_FINISHED", journal["status"], json.dumps(journal.get("restoration", {}), ensure_ascii=False)[:4000], flush=True)

    return 0 if journal["status"] == "completed-measured" else 1


if __name__ == "__main__":
    sys.exit(main())
