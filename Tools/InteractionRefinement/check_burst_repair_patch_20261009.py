from pathlib import Path
import os,json,zipfile,subprocess,hashlib,sys
sys.stdout.reconfigure(encoding="utf-8")
R=Path(__file__).resolve().parents[2];W=R/"outputs/BurstDependencyRepair-20261009-01";D=W/"patch-check"
assert not D.exists();D.mkdir()
m=json.loads((W/"repair-manifest.json").read_text(encoding="utf-8"))
with zipfile.ZipFile(W/"committed-source.zip") as z:
 for path,item in m["changes"].items():
  if item["oldSha"] is not None:
   data=z.read(path);assert hashlib.sha256(data).hexdigest()==item["oldSha"]
   p=D/path;p.parent.mkdir(parents=True,exist_ok=True);p.write_bytes(data)
env=os.environ.copy();env["GIT_CEILING_DIRECTORIES"]=str(R/"outputs")
for args in [["git","apply","--check",str(W/"dependency-repair.patch")],["git","apply",str(W/"dependency-repair.patch")]]:
 subprocess.run(args,cwd=D,env=env,check=True)
assert all(hashlib.sha256((D/p).read_bytes()).hexdigest()==v["newSha"] for p,v in m["changes"].items())
receipt={"passed":True,"files":len(m["changes"]),"scope":"git apply --check then apply in disposable non-repository directory from exact base blobs; all repaired bytes match validated manifest. No mother index or branch writes."}
(W/"patch-apply-check.json").write_text(json.dumps(receipt,indent=2))
print(json.dumps(receipt))
