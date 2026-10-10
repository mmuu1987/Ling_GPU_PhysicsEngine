from common_p8 import *
import sys
sys.stdout.reconfigure(encoding="utf-8")
P9=LOG/"P9"
P9.mkdir(exist_ok=True)
receipt=P9/"initial-regression-20261008.json"
assert not receipt.exists(), "Do not overwrite evidence"
state={"status":"running","scope":"Existing suites only; not combined end-to-end qualification", "humanAcceptance":"pending", "build":"not authorized; 37 unchanged", "runs":[]}
save(receipt,state)
steps=[("editmode","p9-critical-01","critical"),("playmode","p9-radius-01","radius"),("playmode","p9-deployment-01","resize"),("playmode","p9-scoped-scene-01","scoped-scene","gui")]
for args in steps:
 print("P9 STEP",args,flush=True)
 r=subprocess.run([sys.executable,"-X","utf8",str(ROOT/"Tools/InteractionRefinement/run_p8.py"),*args],cwd=ROOT)
 state["runs"].append({"args":args,"exitCode":r.returncode,"evidence":str((P8/args[1]).relative_to(ROOT))})
 save(receipt,state)
 if r.returncode:
  state["status"]="blocked_or_failed_review_required";save(receipt,state);sys.exit(r.returncode)
state["status"]="existing_targeted_suites_passed_not_P9_acceptance"
save(receipt,state)
print(json.dumps(state,ensure_ascii=False,indent=2),flush=True)
