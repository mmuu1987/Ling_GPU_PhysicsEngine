from pathlib import Path
import subprocess,json,sys
sys.stdout.reconfigure(encoding="utf-8")
R=Path(__file__).resolve().parents[2];O=R/"outputs/BurstPublishMerge-20261009-01";T=R/"outputs/B10"
p=json.loads((O/"preflight.json").read_text());q=json.loads((O/"publish.json").read_text())
args=[p["ghPath"],"pr","checks",str(q["pr"]["number"]),"--repo",p["repo"],"--watch","--interval","10","--fail-fast"]
print("WAIT FOR PR CHECKS; no bypass",flush=True)
try:
 r=subprocess.run(args,cwd=T,capture_output=True,text=True,encoding="utf-8",errors="replace",timeout=900)
 result={"exitCode":r.returncode,"stdout":r.stdout[-12000:],"stderr":r.stderr[-3000:]}
except subprocess.TimeoutExpired:
 result={"status":"pending_after_15_minutes"}
(O/"checks-watch.json").write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding="utf-8")
print(json.dumps(result,ensure_ascii=False,indent=2),flush=True)
v=subprocess.run([p["ghPath"],"pr","view",str(q["pr"]["number"]),"--repo",p["repo"],"--json","number,state,headRefOid,mergeable,mergeStateStatus,reviewDecision,statusCheckRollup,reviews,comments"],cwd=T,capture_output=True,text=True,encoding="utf-8",errors="replace",timeout=60)
assert v.returncode==0
(O/"review-after-checks.json").write_text(v.stdout,encoding="utf-8")
x=json.loads(v.stdout);print(json.dumps({k:x[k] for k in ["number","state","headRefOid","mergeable","mergeStateStatus","reviewDecision","statusCheckRollup"]},ensure_ascii=False,indent=2))
