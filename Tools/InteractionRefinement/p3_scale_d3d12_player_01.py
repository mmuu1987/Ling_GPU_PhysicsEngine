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
OUT_PARENT = ROOT / "outputs" / "P3ScaleD3D12-20261010-01"
OUT = OUT_PARENT / "attempt-02"  # attempt-01 (harness bug: deployment editor not active) kept at OUT_PARENT top level
BUILD_DIR = OUT / "WindowsPlayer-01"
B37 = ROOT / "Builds" / "CaptureDefault-20261006-37"
PATCH_FILES = ["Assets/MassEngine/Terrain/TerrainNavigationRuntime.cs", "Assets/MassEngine/Terrain/TerrainNavigationGrid.cs"]
DEFINE_RE = re.compile(r"^#if UNITY_EDITOR(\r?)$", re.M)
UNITY = Path(r"D:\soft\Unity6\6000.3.14f1\Editor\Unity.exe")
CACHE = P12 / "Library" / "ShaderCache"
WARMUP_STATE = P12 / "Library" / "WarSandbox"
WARMUP_MARKER = WARMUP_STATE / "P9ShaderWarmup-D3D11.json"
HARNESS_REL = Path("Assets/Game/Scripts/P3ScaleFrameHarness.cs")
BUILDER_REL = Path("Assets/Game/Editor/P3ScaleBuild.cs")
HARNESS_GUID = "7b3d9f1e2a5c4e8f9a0b1c2d3e4f5a61"
BUILDER_GUID = "8c4e0a2f3b6d4f9a8b1c2d3e4f5a6b72"
EXE_NAME = "P3ScaleValidation"
RUNS = [("01-D3D11-50k", "d3d11", 50000), ("02-D3D12-50k", "d3d12", 50000),
        ("03-D3D11-100k", "d3d11", 100000), ("04-D3D12-100k", "d3d12", 100000),
        ("05-D3D11-200k", "d3d11", 200000), ("06-D3D12-200k", "d3d12", 200000)]
NO_PROGRESS_KILL_S = 300
ABSOLUTE_KILL_S = 900


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
            "playerBuildConfigLines": [line for line in text.splitlines() if "P9 PLAYER BUILD" in line],
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
            "--war-sandbox-menu", "--p3s-scale-validation", "--p3s-units=" + str(units),
            "--p3s-output=" + str(run_dir), "--interaction-p8-output=" + str(run_dir),
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
        keys = ("Direct3D", "D3D12", "D3D11", "GfxDevice", "Burst", "Exception", "P3S_", "out of memory", "Out of memory", "Crash", "crash", "TDR", "removed", "Renderer:", "Vendor:", "VRAM")
        row["notableLogLines"] = [x[:400] for x in lines if any(k in x for k in keys)][-80:]
        row["playerLogLines"] = len(lines)
    journal["runtimeRuns"].append(row)
    r = result or {}
    print_step("finished %s status=%s api=%s agents=%s median=%s p95=%s p99=%s over33=%s over50=%s fps=%s gpuPeakMB=%s nav=%s" % (
        label, status, r.get("graphicsApi"), r.get("agentCount"), r.get("medianMs"), r.get("p95Ms"), r.get("p99Ms"), r.get("over33"), r.get("over50"),
        r.get("avgFps"), mb(r.get("gfxDriverBytesPeak")), r.get("navBackend")), journal)
    time.sleep(8)
    return row


def write_summary(journal):
    rows = []
    for x in journal.get("runtimeRuns", []):
        r = x.get("result") or {}
        rows.append({"label": x["label"], "apiRequested": x["api"], "apiActual": r.get("graphicsApi"), "units": x["units"],
                     "agents": r.get("agentCount"), "aliveAtSampleStart": r.get("aliveAtSampleStart"), "aliveAtSampleEnd": r.get("aliveAtSampleEnd"),
                     "medianMs": r.get("medianMs"), "p95Ms": r.get("p95Ms"), "p99Ms": r.get("p99Ms"), "maxMs": r.get("maxMs"),
                     "over33": r.get("over33"), "over50": r.get("over50"), "frames": r.get("frames"), "avgFps": r.get("avgFps"),
                     "gfxDriverMBStart": mb(r.get("gfxDriverBytesAtSampleStart")), "gfxDriverMBPeak": mb(r.get("gfxDriverBytesPeak")), "gfxDriverMBEnd": mb(r.get("gfxDriverBytesEnd")),
                     "vramMB": r.get("graphicsMemorySizeMB"), "navBackend": r.get("navBackend"), "phaseStart": r.get("phaseAtSampleStart"), "phaseEnd": r.get("phaseAtSampleEnd"),
                     "deployment": r.get("deploymentNote"), "loadS": r.get("loadSeconds"), "applyToRunningS": r.get("applyToRunningSeconds"),
                     "status": x["status"], "exitCode": x["exitCode"], "wallS": x["wallSeconds"]})
    write_json(OUT / "summary.json", {"rows": rows, "build": journal.get("buildInfo"), "restoration": journal.get("restoration")})
    def f(v, n=2):
        return "-" if v is None else (("%." + str(n) + "f") % v if isinstance(v, float) else str(v))
    md = ["| run | API(actual) | units | agents | alive@start | median ms | P95 | P99 | >33ms | >50ms | avg FPS | GPU drv MB start/peak/end | nav | phase | deploy | status |",
          "|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|"]
    for r in rows:
        md.append("| %s | %s | %s | %s | %s | %s | %s | %s | %s | %s | %s | %s/%s/%s | %s | %s→%s | %s | %s |" % (
            r["label"], r["apiActual"] or r["apiRequested"], r["units"], f(r["agents"]), f(r["aliveAtSampleStart"]), f(r["medianMs"]), f(r["p95Ms"]), f(r["p99Ms"]),
            f(r["over33"]), f(r["over50"]), f(r["avgFps"], 1), f(r["gfxDriverMBStart"]), f(r["gfxDriverMBPeak"]), f(r["gfxDriverMBEnd"]), f(r["navBackend"]),
            f(r["phaseStart"]), f(r["phaseEnd"]), f(r["deployment"]), r["status"]))
    (OUT / "summary.md").write_text("\n".join(md) + "\n", encoding="utf-8")


def main():
    if not P12.is_dir() or not UNITY.is_file():
        raise RuntimeError("P12 project or pinned Unity editor is missing")
    if OUT.exists() or not (OUT_PARENT / "journal.json").is_file():
        raise RuntimeError("Refusing: attempt-02 dir exists or attempt-01 evidence missing " + str(OUT))
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
                                  "@(Get-Process Unity,WarSandbox,P9WindowsValidation,P3BurstValidation,P3BurstDefaultValidation," + EXE_NAME + " -ErrorAction SilentlyContinue).Count"], text=True).strip()
    if ps != "0":
        raise RuntimeError("Existing Unity/game process present; refusing parallel project access: " + ps)

    OUT.mkdir(parents=True)
    mother_before = mother_snapshot()
    nav_files = {}
    for rel in PATCH_FILES:
        a, b = P12 / rel, ROOT / rel
        nav_files[rel] = {"p12": sha(a) if a.is_file() else None, "mother": sha(b) if b.is_file() else None}
        nav_files[rel]["equal"] = nav_files[rel]["p12"] == nav_files[rel]["mother"]
    journal = {"name": "p3-scale-d3d11-vs-d3d12-player-01-attempt-02", "started": dt.datetime.now().astimezone().isoformat(),
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
    harness_text = (source_dir / "P3ScaleFrameHarness.cs.txt").read_text(encoding="utf-8")
    builder_text = (source_dir / "P3ScaleBuild.cs.txt").read_text(encoding="utf-8")
    (OUT / "P3ScaleFrameHarness.cs.txt").write_text(harness_text, encoding="utf-8")
    (OUT / "P3ScaleBuild.cs.txt").write_text(builder_text, encoding="utf-8")
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
                           "-executeMethod", "MassEngine.Game.Editor.P3ScaleBuild.ReportContext", "-quit"], timeout=1800)
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
                      "MassEngine.Game.Editor.P3ScaleBuild.BuildDevelopmentPlayer",
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
                                "-executeMethod", "MassEngine.Game.Editor.P3ScaleBuild.ReportContext", "-quit"]
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
            journal["restoration"]["navSourcesUnchanged"] = all((sha(P12 / rel) if (P12 / rel).is_file() else None) == v["p12"] for rel, v in nav_files.items())
        except Exception as ex:
            journal["restoration"]["integrityCheckError"] = repr(ex)
        try:
            ma = mother_snapshot()
            journal["restoration"]["motherUnchanged"] = ma["head"] == mother_before["head"] and ma["index"] == mother_before["index"] and ma["stats"] == mother_before["stats"]
            journal["restoration"]["package37Unchanged"] = ma["b37"] == mother_before["b37"]
        except Exception as ex:
            journal["restoration"]["motherCheckError"] = repr(ex)
        journal["finished"] = dt.datetime.now().astimezone().isoformat()
        write_json(OUT / "journal.json", journal)
        try: write_summary(journal)
        except Exception as ex: print("SUMMARY_ERROR", repr(ex), flush=True)
        print("VALIDATION_FINISHED", journal["status"], json.dumps(journal.get("restoration", {}), ensure_ascii=False)[:4000], flush=True)

    return 0 if journal["status"] == "completed-measured" else 1


if __name__ == "__main__":
    sys.exit(main())
