from common_p1 import *
import shutil,subprocess,sys
src=P1/'preview-03/radius-evidence';docs=ROOT/'Docs/InteractionRefinement-20261007';dest=docs/'P1截图'
assert not dest.exists(),'Do not overwrite report images'
audit=json.loads((P1/'final-audit-02.json').read_text(encoding='utf-8'));assert audit['status']=='P1_AUTOMATED_PASS_PENDING_MANUAL_ACCEPTANCE'
shutil.copy2(P1/'final-audit-02.json',docs/'P1-AUDIT.json');dest.mkdir()
files=list(src.glob('*.png'));assert len(files)==20
for p in files:shutil.copy2(p,dest/p.name);assert sha(p)==sha(dest/p.name)
subprocess.run([sys.executable,'-X','utf8',str(ROOT/'Tools/InteractionRefinement/create_p1_report.py')],cwd=ROOT,check=True)
print('P1 REPORT PUBLISHED; 20 original screenshots; no source/build/save changes',flush=True)
