# -*- coding: utf-8 -*-
"""P3 lane ABBA run3 failure recovery. Restore-only; aborts on any unknown byte state."""
import hashlib, json, os, shutil, subprocess, sys

ROOT = r"E:\GitHub\Ling_GPU_PhysicsEngine\Ling_GPU_PhysicsEngine"
WORK = os.path.join(ROOT, r"Logs\InteractionRefinement-20261007\P3\lane-abba-20261008-03")
REPORT = os.path.join(WORK, "recovery-executed-01.json")
report = {"steps": [], "errors": [], "restored": [], "skippedIdentical": [],
          "jit": None, "lockfileRemoved": False, "aborted": False}

def sha256(p):
    h = hashlib.sha256()
    with open(p, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()

def fail(msg):
    report["errors"].append(msg)
    report["aborted"] = True
    with open(REPORT, "w", encoding="utf-8") as f:
        json.dump(report, f, indent=2, ensure_ascii=False)
    print("ABORT:", msg)
    sys.exit(1)

# 1. No project Unity/WarSandbox process may be running.
for image in ("Unity.exe", "WarSandbox.exe"):
    out = subprocess.run(["tasklist", "/FI", "IMAGENAME eq " + image, "/FO", "CSV"],
                         capture_output=True)
    text_out = (out.stdout or b"").decode("utf-8", errors="replace")
    if image.lower() in text_out.lower():
        fail("process still running: " + image)
report["steps"].append("no Unity.exe / WarSandbox.exe process")

# 2. Load registry; verify every current file is a KNOWN state (original or candidate).
with open(os.path.join(WORK, "registry.json"), "r", encoding="utf-8") as f:
    reg = json.load(f)["variants"]
orig_h, cand_h = reg["original"], reg["candidate"]
plan = []
for rel, oh in orig_h.items():
    wp = os.path.join(ROOT, rel.replace("/", os.sep))
    if not os.path.exists(wp):
        fail("workspace file missing: " + rel)
    cur = sha256(wp)
    if cur == oh:
        report["skippedIdentical"].append(rel)
    elif cur == cand_h[rel]:
        bak = os.path.join(WORK, "original", rel.replace("/", os.sep))
        if not os.path.exists(bak):
            fail("original backup missing: " + rel)
        if sha256(bak) != oh:
            fail("original backup hash mismatch: " + rel)
        plan.append((rel, bak, wp, oh))
    else:
        fail("UNKNOWN current bytes (neither original nor candidate), not touching: " + rel)
report["steps"].append("state audit done: %d to restore, %d already original" %
                       (len(plan), len(report["skippedIdentical"])))

# 3. Restore known candidate bytes back to original bytes.
for rel, bak, wp, oh in plan:
    shutil.copy2(bak, wp)
    if sha256(wp) != oh:
        fail("post-restore hash mismatch: " + rel)
    report["restored"].append(rel)

# 4. JIT cache: archive generated state, then restore escrow.
escrow = os.path.join(WORK, "original-JIT-escrow")
jit = os.path.join(ROOT, r"Library\BurstCache\JIT")
gen_arch = os.path.join(WORK, "generated-JIT-final")
if not os.path.isdir(escrow):
    fail("JIT escrow missing")
jinfo = {"escrowFiles": sum(len(fs) for _, _, fs in os.walk(escrow))}
if os.path.isdir(jit):
    jinfo["activeFilesBefore"] = sum(len(fs) for _, _, fs in os.walk(jit))
    if not os.path.isdir(gen_arch):
        shutil.copytree(jit, gen_arch)
        jinfo["generatedArchived"] = True
    else:
        jinfo["generatedArchived"] = "already-existed"
    shutil.rmtree(jit)
else:
    jinfo["activeFilesBefore"] = 0
shutil.copytree(escrow, jit)
jinfo["activeFilesAfter"] = sum(len(fs) for _, _, fs in os.walk(jit))
report["jit"] = jinfo
report["steps"].append("JIT restored from escrow")

# 5. Remove stale UnityLockfile (0-byte leftover; verified no Unity process above).
lock = os.path.join(ROOT, r"Temp\UnityLockfile")
if os.path.exists(lock):
    try:
        os.remove(lock)
        report["lockfileRemoved"] = True
    except OSError as e:
        fail("cannot remove UnityLockfile (maybe held): " + str(e))
report["steps"].append("stale lockfile handled")

report["result"] = "RESTORED"
with open(REPORT, "w", encoding="utf-8") as f:
    json.dump(report, f, indent=2, ensure_ascii=False)
print(json.dumps(report, indent=2, ensure_ascii=False))
