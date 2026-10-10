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

ROOT = Path.cwd()
P12 = ROOT / "outputs" / "P12"
TRIAL = ROOT / "outputs" / "P9FirstCommand-20261009-01"  # candidate manifest lives here
P3OUT = ROOT / "outputs" / "P3PlayerBurst-20261009-01"
OUT = P3OUT / "validation-01"
BUILD_DIR = P3OUT / "WindowsPlayerBurst-01"
B37 = ROOT / "Builds" / "CaptureDefault-20261006-37"
PATCH_FILES = ["Assets/MassEngine/Terrain/TerrainNavigationRuntime.cs", "Assets/MassEngine/Terrain/TerrainNavigationGrid.cs"]
DEFINE_RE = re.compile(r"^#if UNITY_EDITOR(\r?)$", re.M)
UNITY = Path(r"D:\soft\Unity6\6000.3.14f1\Editor\Unity.exe")
CACHE = P12 / "Library" / "ShaderCache"
WARMUP_STATE = P12 / "Library" / "WarSandbox"
WARMUP_MARKER = WARMUP_STATE / "P9ShaderWarmup-D3D11.json"
HARNESS_REL = Path("Assets/Game/Scripts/P3PlayerBurstFrameHarness.cs")
BUILDER_REL = Path("Assets/Game/Editor/P3PlayerBurstBuild.cs")
HARNESS_GUID = "5d0e3b7a91c24f6e8a4b2c19f7e60d31"
BUILDER_GUID = "8b2f61c4d7a94e0f9c3e5a7b1d2f4069"


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
            "playerBuildConfigLines": [line for line in text.splitlines() if "P9 PLAYER BUILD:" in line or "P9 PLAYER BUILD RESULT:" in line],
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


def main():
    if not P12.is_dir() or not UNITY.is_file():
        raise RuntimeError("P12 project or pinned Unity editor is missing")
    if P3OUT.exists():
        raise RuntimeError("Refusing to overwrite existing Player validation evidence or build output")
    if not CACHE.is_dir():
        raise RuntimeError("P12 Library/ShaderCache is missing; no cold-cache test attempted")
    if not (P12 / HARNESS_REL).parent.is_dir():
        raise RuntimeError("Existing Game/Scripts folder missing; refusing to add a new project folder")
    harness_path = P12 / HARNESS_REL
    builder_path = P12 / BUILDER_REL
    added = [harness_path, Path(str(harness_path) + ".meta"), builder_path, Path(str(builder_path) + ".meta")]
    if any(p.exists() for p in added):
        raise RuntimeError("Temporary Player-validation asset path already exists; refusing to overwrite")
    if not (TRIAL / "candidate-manifest.json").is_file():
        raise RuntimeError("Candidate manifest is missing")
    candidate_manifest_path = TRIAL / "candidate-manifest.json"
    candidate_manifest_sha_before = sha(candidate_manifest_path)
    candidate = json.loads(candidate_manifest_path.read_text(encoding="utf-8"))
    if candidate.get("accepted") is not False:
        raise RuntimeError("Candidate state changed; expected unaccepted isolated candidate")
    candidate_files = candidate.get("files", {})
    for rel, expected in candidate_files.items():
        actual = P12 / Path(rel)
        if not actual.is_file() or sha(actual) != expected:
            raise RuntimeError("Candidate manifest mismatch before Player validation: " + rel)
    ps = subprocess.check_output(["powershell", "-NoProfile", "-Command",
                                  "@(Get-Process Unity,WarSandbox,P9WindowsValidation,P3BurstValidation -ErrorAction SilentlyContinue).Count"], text=True).strip()
    if ps != "0":
        raise RuntimeError("Existing Unity/game process present; refusing parallel project access: " + ps)

    OUT.mkdir(parents=True)
    mother_before = mother_snapshot()
    journal = {"name": "p3-windows-x64-development-player-burst-aot-abba-01", "started": dt.datetime.now().astimezone().isoformat(),
               "status": "running", "candidateAccepted": candidate.get("accepted"), "candidateFilesBefore": candidate_files,
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
    original_player_settings = {
        "projectSettingsFiles": settings_before,
        "assetFiles": assets_before,
        "shaderCacheFiles": cache_before,
        "warmupStateExisted": warmup_existed,
        "warmupMarkerExisted": False,
        "userSettingsFiles": user_settings_before,
        "targetPreferenceFiles": target_pref_before,
        "candidateFiles": candidate_files,
    }
    write_json(OUT / "original-state-manifest.json", original_player_settings)
    source_dir = ROOT / "Tools" / "InteractionRefinement"
    harness_text = (source_dir / "P3PlayerBurstFrameHarness.cs.txt").read_text(encoding="utf-8")
    builder_text = (source_dir / "P3PlayerBurstBuild.cs.txt").read_text(encoding="utf-8")
    (OUT / "P3PlayerBurstFrameHarness.cs.txt").write_text(harness_text, encoding="utf-8")
    (OUT / "P3PlayerBurstBuild.cs.txt").write_text(builder_text, encoding="utf-8")
    patch_backup = {rel: (P12 / rel).read_bytes() for rel in PATCH_FILES}
    for rel, data in patch_backup.items():
        dst = OUT / "patch-original" / Path(rel).name; dst.parent.mkdir(parents=True, exist_ok=True); dst.write_bytes(data)
    patched = False
    original_target = None
    restore_target_needed = False
    target_args = []

    try:
        print_step("preflight passed; copied exact P12 ShaderCache and ProjectSettings baselines", journal)
        harness_path.parent.mkdir(parents=True, exist_ok=True)
        harness_path.write_text(harness_text, encoding="utf-8", newline="\r\n")
        builder_path.write_text(builder_text, encoding="utf-8", newline="\r\n")
        Path(str(harness_path) + ".meta").write_text("fileFormatVersion: 2\nguid: " + HARNESS_GUID + "\nMonoImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: -32000\n  icon: {instanceID: 0}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n", encoding="utf-8", newline="\r\n")
        Path(str(builder_path) + ".meta").write_text("fileFormatVersion: 2\nguid: " + BUILDER_GUID + "\nMonoImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n  icon: {instanceID: 0}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n", encoding="utf-8", newline="\r\n")
        journal["temporaryP12Files"] = [str(p.relative_to(P12)).replace("\\", "/") for p in added]
        print_step("installed temporary Player-only harness and Editor build entry", journal)
        patch_counts = {}
        for rel in PATCH_FILES:
            text = patch_backup[rel].decode("utf-8")
            if "UnityEditor" in text.replace("UNITY_EDITOR", ""):
                raise RuntimeError("UnityEditor API in patch target; refusing: " + rel)
            new, n = DEFINE_RE.subn(lambda m: "#if UNITY_EDITOR || WAR_SANDBOX_PLAYER_BURST" + m.group(1), text)
            if n == 0: raise RuntimeError("No UNITY_EDITOR gate in " + rel)
            patch_counts[rel] = n
            (P12 / rel).write_bytes(new.encode("utf-8"))
            (OUT / "patch-applied" / Path(rel).name).parent.mkdir(parents=True, exist_ok=True)
            (OUT / "patch-applied" / Path(rel).name).write_bytes(new.encode("utf-8"))
        patched = True
        journal["temporaryGatePatch"] = {"define": "WAR_SANDBOX_PLAYER_BURST", "replacedGates": patch_counts,
            "note": "#if UNITY_EDITOR -> #if UNITY_EDITOR || WAR_SANDBOX_PLAYER_BURST; Editor and normal Player builds unchanged; define only in this build"}
        print_step("applied temporary gate patch " + json.dumps(patch_counts), journal)

        context = run_unity("editor-build-target-before", ["-batchmode", "-force-d3d11", "-projectPath", str(P12),
                           "-executeMethod", "MassEngine.Game.Editor.P3PlayerBurstBuild.ReportContext", "-quit"], timeout=1800)
        journal["unityRuns"].append(context)
        context_match = re.search(r"P9 PLAYER CONTEXT: activeTarget=([A-Za-z0-9_]+)", "\\n".join(context["contextLines"]))
        if context["exitCode"] != 0 or context["scriptCompilationErrors"] or context_match is None or not context["catalogSceneLines"]:
            raise RuntimeError("Could not read the original active build target/catalog scenes from Unity")
        journal["catalogSceneExpansionPreflight"] = context["catalogSceneLines"]
        original_target = context_match.group(1)
        target_args = [] if original_target == "StandaloneWindows64" else ["-buildTarget", "StandaloneWindows64"]
        restore_target_needed = bool(target_args)
        journal["originalActiveBuildTarget"] = original_target
        journal["temporaryBuildTarget"] = "StandaloneWindows64" if restore_target_needed else None
        print_step("original active build target recorded: " + original_target + "; temporary Windows64 switch=" + str(restore_target_needed), journal)


        BUILD_DIR.mkdir(parents=True)
        player_exe = BUILD_DIR / "P3BurstValidation.exe"
        build_args = ["-batchmode", "-force-d3d11", "-projectPath", str(P12)] + target_args + ["-executeMethod",
                      "MassEngine.Game.Editor.P3PlayerBurstBuild.BuildDevelopmentPlayer",
                      "--p3-player-build-output=" + str(player_exe), "-quit"]
        build = run_unity("windows-x64-development-player-build", build_args, timeout=2400)
        journal["unityRuns"].append(build)
        build_summary_path = Path(str(player_exe) + ".build.txt")
        journal["buildSummary"] = build_summary_path.read_text(encoding="utf-8", errors="replace") if build_summary_path.exists() else None
        journal["cacheAfterPlayerBuild"] = tree_manifest(CACHE)
        if (build["exitCode"] != 0 or build["scriptCompilationErrors"] or not player_exe.is_file()
                or not journal["buildSummary"] or "result=Succeeded" not in journal["buildSummary"] or "errors=0" not in journal["buildSummary"]):
            raise RuntimeError("Windows x64 Development Player build did not complete successfully")
        burst_dlls = [str(p.relative_to(BUILD_DIR)) for p in BUILD_DIR.rglob("*burst*")]
        journal["burstArtifacts"] = burst_dlls
        # The builder records all PlayerSettings graphics backends in its Unity log.
        journal["graphicsApisBuildLog"] = build["playerBuildConfigLines"]
        print_step("Windows x64 Development Player built; settings/build logs retained", journal)

        for label, burst in (("A1-managed", False), ("B1-burst", True), ("B2-burst", True), ("A2-managed", False)):
            run_dir = OUT / ("player-" + label)
            run_dir.mkdir(parents=True)
            (run_dir / "guide.json").write_text('{"version":1,"dismissed":true}\n', encoding="utf-8")
            (run_dir / "globals.json").write_text("{}\n", encoding="utf-8")
            run_log = run_dir / "Player.log"
            args = [str(player_exe), "-force-d3d11", "-screen-width", "1920", "-screen-height", "1080", "-screen-fullscreen", "0",
                    "--war-sandbox-menu", "--p3-player-frame-validation",
                    "--p3-player-output=" + str(run_dir), "--interaction-p8-output=" + str(run_dir),
                    "--war-sandbox-guide-file=" + str(run_dir / "guide.json"),
                    "--war-sandbox-settings-file=" + str(run_dir / "settings.json"),
                    "--war-sandbox-review-global-file=" + str(run_dir / "globals.json"),
                    "-logFile", str(run_log)]
            if burst: args.append("--war-sandbox-nav-burst")
            print("PLAYER_START", label, json.dumps(args, ensure_ascii=False), flush=True)
            with run_log.open("w", encoding="utf-8", errors="replace") as f:
                proc = subprocess.Popen(args, cwd=str(BUILD_DIR), stdout=f, stderr=subprocess.STDOUT)
                try:
                    exit_code = proc.wait(timeout=900)
                except subprocess.TimeoutExpired:
                    subprocess.run(["taskkill", "/PID", str(proc.pid), "/T", "/F"], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
                    exit_code = None
            result_path = run_dir / "result.json"
            result = json.loads(result_path.read_text(encoding="utf-8")) if result_path.is_file() else None
            row = {"label": label, "burstArgument": burst, "exitCode": exit_code, "result": result,
                   "valid": bool(result and result.get("passed") and exit_code == 0)}
            if run_log.is_file():
                lines = run_log.read_text(encoding="utf-8", errors="replace").splitlines()
                row["notableLogLines"] = [x for x in lines if "Burst" in x or "burst" in x or "Exception" in x or "P3_PLAYER" in x][-60:]
            journal["runtimeRuns"].append(row)
            time.sleep(5)
            r = result or {}
            print_step("finished %s valid=%s median=%s p99=%s over33=%s over50=%s nav=%s" % (label, row["valid"], r.get("medianMs"), r.get("p99Ms"), r.get("over33"), r.get("over50"), json.dumps(r.get("navAtSampleEnd"))), journal)

        runs = {x["label"]: (x["result"] or {}) for x in journal["runtimeRuns"]}
        gates = {}
        for b, a in (("B1-burst", "A1-managed"), ("B2-burst", "A2-managed")):
            rb, ra = runs.get(b, {}), runs.get(a, {})
            if not rb or not ra: gates[b + "/" + a] = {"evaluated": False}; continue
            g = {"p99Ratio": ratio(rb["p99Ms"], ra["p99Ms"]), "medianRatio": ratio(rb["medianMs"], ra["medianMs"])}
            g["p99"] = g["p99Ratio"] is not None and g["p99Ratio"] <= 0.80
            g["median"] = g["medianRatio"] is not None and g["medianRatio"] <= 1.05
            g["over33"] = rb["over33"] <= 0.5 * ra["over33"]
            g["over50"] = rb["over50"] <= max(ra["over50"], 3)
            ns, ne = rb.get("navAtSampleStart", {}), rb.get("navAtSampleEnd", {})
            g["burstPath"] = (ns.get("preparationState") == "Ready" and ne.get("preparationState") == "Ready"
                              and ne.get("burstSolves", 0) > ns.get("burstSolves", 0) and ne.get("managedSolves") == ns.get("managedSolves")
                              and ne.get("managedNativeSolves", 1) == 0)
            ms, me = ra.get("navAtSampleStart", {}), ra.get("navAtSampleEnd", {})
            g["managedControl"] = me.get("burstSolves", 1) == 0 and me.get("managedSolves", 0) > ms.get("managedSolves", 0)
            g["passed"] = all(g[k] for k in ("p99", "median", "over33", "over50", "burstPath", "managedControl"))
            gates[b + "/" + a] = g
        journal["abbaGates"] = gates
        journal["allRunsValid"] = len(journal["runtimeRuns"]) == 4 and all(x["valid"] for x in journal["runtimeRuns"])
        journal["abbaPassed"] = journal["allRunsValid"] and all(g.get("passed") for g in gates.values())
        journal["status"] = "completed-measured"
        journal["performanceDecision"] = "Gate = Editor P3 Burst ABBA thresholds (P99<=0.80x, median<=1.05x, >33.33ms<=0.5x, >50ms<=max(A,3)) plus Burst-path/managed-control counters. Player default camera, not fixed macro camera."
    except Exception as ex:
        journal["status"] = "failed-or-blocked"
        journal["errors"].append(repr(ex))
        print("VALIDATION_ERROR", repr(ex), flush=True)
    finally:
        # Restore the caller's active target before removing the temporary method used to verify it.
        if restore_target_needed and original_target:
            try:
                restore_args = ["-batchmode", "-force-d3d11", "-projectPath", str(P12), "-buildTarget", original_target,
                                "-executeMethod", "MassEngine.Game.Editor.P3PlayerBurstBuild.ReportContext", "-quit"]
                target_restore = run_unity("restore-original-build-target", restore_args, timeout=1800)
                journal["restoration"]["targetRestoreRun"] = target_restore
                restore_match = re.search(r"P9 PLAYER CONTEXT: activeTarget=([A-Za-z0-9_]+)", "\\n".join(target_restore["contextLines"]))
                journal["restoration"]["activeBuildTargetMatchesOriginal"] = bool(target_restore["exitCode"] == 0 and restore_match and restore_match.group(1) == original_target)
            except Exception as ex:
                journal["restoration"]["activeBuildTargetRestoreError"] = repr(ex)
        try:
            generated_target_state = {}
            for rel, expected_hash in target_pref_before.items():
                target_file = library_root / rel
                actual_hash = sha(target_file) if target_file.is_file() else None
                generated_target_state[rel] = actual_hash
                if expected_hash is None:
                    if target_file.exists(): target_file.unlink()
                elif actual_hash != expected_hash:
                    target_file.parent.mkdir(parents=True, exist_ok=True)
                    shutil.copy2(target_pref_backup / rel, target_file)
            journal["restoration"]["targetPreferenceFilesMatchOriginal"] = all(
                (sha(library_root / rel) if (library_root / rel).is_file() else None) == expected
                for rel, expected in target_pref_before.items())
            journal["restoration"]["targetPreferenceFilesGeneratedDuringTest"] = generated_target_state
        except Exception as ex:
            journal["restoration"]["targetPreferenceRestoreError"] = repr(ex)
        # Unity and both Player runs have exited before restoring assets and cache state.
        try:
            for p in added:
                if p.exists(): p.unlink()
            journal["restoration"]["temporarySourcesRemoved"] = all(not p.exists() for p in added)
        except Exception as ex:
            journal["restoration"]["temporarySourceCleanupError"] = repr(ex)
        try:
            actual_warmup = tree_manifest(WARMUP_STATE)
            expected_warmup = tree_manifest(warmup_backup) if warmup_existed else {}
            if WARMUP_STATE.exists(): shutil.rmtree(WARMUP_STATE)
            if warmup_existed: shutil.copytree(warmup_backup, WARMUP_STATE)
            journal["restoration"]["warmupStateMatchesOriginal"] = tree_manifest(WARMUP_STATE) == expected_warmup if warmup_existed else not WARMUP_STATE.exists()
            journal["restoration"]["warmupStateGeneratedDuringTest"] = actual_warmup
        except Exception as ex:
            journal["restoration"]["warmupStateRestoreError"] = repr(ex)
        try:
            actual_cache = tree_manifest(CACHE)
            cache_restore = restore_tree(CACHE, cache_backup, cache_existed)
            journal["restoration"]["shaderCacheMatchesOriginal"] = cache_restore == cache_before
            journal["restoration"]["shaderCacheGeneratedDuringTest"] = actual_cache
        except Exception as ex:
            journal["restoration"]["shaderCacheRestoreError"] = repr(ex)
        try:
            journal["restoration"]["projectSettings"] = restore_project_settings(settings_before, settings_backup)
        except Exception as ex:
            journal["restoration"]["projectSettingsRestoreError"] = repr(ex)
        try:
            user_generated = tree_manifest(user_settings_root)
            user_restored = restore_tree(user_settings_root, user_settings_backup, user_settings_existed)
            journal["restoration"]["userSettingsMatchesOriginal"] = user_restored == user_settings_before
            journal["restoration"]["userSettingsGeneratedDuringTest"] = user_generated
        except Exception as ex:
            journal["restoration"]["userSettingsRestoreError"] = repr(ex)
        try:
            for rel, data in patch_backup.items():
                if (P12 / rel).read_bytes() != data: (P12 / rel).write_bytes(data)
            journal["restoration"]["gatePatchReverted"] = all((P12 / rel).read_bytes() == data for rel, data in patch_backup.items())
        except Exception as ex:
            journal["restoration"]["gatePatchRevertError"] = repr(ex)
        try:
            current_candidate = {}
            for rel in candidate_files:
                actual = P12 / Path(rel)
                current_candidate[rel] = sha(actual) if actual.is_file() else None
            journal["restoration"]["candidateSourcesUnchanged"] = current_candidate == candidate_files
            journal["restoration"]["candidateManifestUnchanged"] = sha(candidate_manifest_path) == candidate_manifest_sha_before
            journal["restoration"]["assetCleanup"] = cleanup_generated_assets(assets_before, OUT)
            journal["restoration"]["assetsMatchOriginal"] = journal["restoration"]["assetCleanup"]["assetsMatchOriginal"]
            journal["restoration"]["temporaryAssetsClean"] = not any(p.exists() for p in added)
        except Exception as ex:
            journal["restoration"]["integrityCheckError"] = repr(ex)
        try:
            ma = mother_snapshot()
            journal["restoration"]["motherUnchanged"] = ma["head"] == mother_before["head"] and ma["index"] == mother_before["index"] and ma["stats"] == mother_before["stats"]
            journal["restoration"]["package37Unchanged"] = ma["b37"] == mother_before["b37"]
        except Exception as ex:
            journal["restoration"]["motherCheckError"] = repr(ex)
        journal["finished"] = dt.datetime.now().astimezone().isoformat()
        # A build/runtime failure remains a failure even if cleanup succeeded.
        write_json(OUT / "journal.json", journal)
        print("VALIDATION_FINISHED", journal["status"], json.dumps(journal.get("restoration", {}), ensure_ascii=False), flush=True)

    return 0 if journal["status"] == "completed-measured" else 1


if __name__ == "__main__":
    sys.exit(main())


