from pathlib import Path
import json,zipfile,hashlib,base64,sys
sys.stdout.reconfigure(encoding="utf-8")
R=Path(__file__).resolve().parents[2];W=R/"outputs/BurstDependencyRepair-20261009-01"
q=json.loads((W/"patch-apply-check.json").read_text());assert q["passed"]
f=R/"Docs/InteractionRefinement-20261007/P3-BURST-DEPENDENCY-REPAIR-20261009.md"
s=f.read_text(encoding="utf-8")
s=s.replace("## 可复现性与保护\n", "## 可复现性与保护\n\n- 最终补丁已通过 `git -c core.autocrlf=false apply --check`，并在仅含精确基线文件的一次性目录实际应用；11文件SHA-256全部与最终已验证源码一致。见 `patch-apply-check.json`。\n")
s=s.replace("## 交付物与下一步", "5. 打包时的首份手工拼接diff未正确表达无末尾换行，应用检查拒绝；改用Git原生diff，并以单命令core.autocrlf=false固定LF后通过实际应用及11文件逐字节对拍；未修改用户Git全局配置，保留1项末尾空行警告。原失败补丁留档，不进入最终修复包；不改变已验证源码。\n\n## 交付物与下一步")
f.write_text(s,encoding="utf-8");(W/"README.md").write_text(s,encoding="utf-8")
bundle=W/"BurstDependencyRepair-20261009.zip"
with zipfile.ZipFile(bundle,"w",zipfile.ZIP_DEFLATED) as z:
 for p in sorted((W/"repair-files").rglob("*")):
  if p.is_file():z.write(p,p.relative_to(W).as_posix())
 for path in ["README.md","repair-manifest.json","dependency-repair.patch","source-provenance.json","patch-apply-check.json","validation-03/summary.json"]:z.write(W/path,path)
raw=bundle.read_bytes();b64=base64.b64encode(raw).decode();(W/"bundle.base64.txt").write_text("\n".join(b64[i:i+120] for i in range(0,len(b64),120)),encoding="ascii")
r=json.loads((W/"delivery.json").read_text());r.update(bytes=len(raw),sha256=hashlib.sha256(raw).hexdigest(),patchApplyVerified=True)
(W/"delivery.json").write_text(json.dumps(r,indent=2),encoding="utf-8");print(json.dumps(r,ensure_ascii=False,indent=2))
