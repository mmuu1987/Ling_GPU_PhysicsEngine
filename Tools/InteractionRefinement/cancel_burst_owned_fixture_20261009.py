from pathlib import Path
import json,subprocess,sys
sys.stdout.reconfigure(encoding="utf-8")
R=Path(__file__).resolve().parents[2]
W=R/"outputs/BurstDependencyRepair-20261009-01"
owner=json.loads((W/"validation-01/default/owner.json").read_text())
pid=owner["pid"]
q=subprocess.run(["powershell","-NoProfile","-Command",f"Get-CimInstance Win32_Process -Filter \"ProcessId = {pid}\" | Select-Object ProcessId,Name,CommandLine | ConvertTo-Json -Compress"],capture_output=True,text=True,encoding="utf-8",check=True)
print(q.stdout,flush=True)
a=json.loads(q.stdout)
assert a["ProcessId"]==pid and a["Name"].lower()=="unity.exe"
assert owner["project"].replace("\\","/").lower() in a["CommandLine"].replace("\\","/").lower()
(W/"validation-01/owned-cancellation.json").write_text(json.dumps({"pid":pid,"reason":"Pre-test review found fixture extraction included historical capture block. Cancel only this owned isolated Editor before repairing test; no product failure conclusion.","commandLine":a["CommandLine"]},indent=2))
r=subprocess.run(["taskkill","/PID",str(pid),"/T","/F"],capture_output=True,text=True)
print(r.stdout,r.stderr,r.returncode)
