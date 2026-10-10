from common_p2 import *
import sys,subprocess
for args in [['apply_p2.py','05-exact-saved-file-e2e'],['run_p2.py','playmode','preview-04','preview'],['run_p2.py','editmode','editmode-04'],['audit_p2_02.py','final-audit-02'],['export_p2.py','preview-04']]:
 print('STEP',args,flush=True)
 r=subprocess.run([sys.executable,'-X','utf8',str(ROOT/'Tools/InteractionRefinement'/args[0])]+args[1:],cwd=ROOT)
 if r.returncode:sys.exit(r.returncode)
