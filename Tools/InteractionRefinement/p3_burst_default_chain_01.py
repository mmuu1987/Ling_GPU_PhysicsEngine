import json, subprocess, sys
from pathlib import Path
ROOT = Path.cwd()
r = subprocess.run([sys.executable, "Tools/InteractionRefinement/p3_burst_default_regression_01.py"])
j = json.loads((ROOT / "outputs/P3BurstDefault-20261010-01/regression-01/journal.json").read_text(encoding="utf-8"))
print("REGRESSION allPassed", j.get("allPassed"), flush=True)
if not (j.get("allPassed") and j.get("motherUnchanged") and j.get("package37Unchanged") and j.get("p12AssetsUnchanged")):
    print("STOP: regression not clean; Player stage skipped", flush=True); sys.exit(2)
r = subprocess.run([sys.executable, "Tools/InteractionRefinement/p3_burst_default_player_01.py"])
print("PLAYER exit", r.returncode, flush=True)
