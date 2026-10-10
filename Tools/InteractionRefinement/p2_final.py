from common_p2 import *
import sys,subprocess
for args in [['apply_p2.py','04-rejection-and-restored-reset'],['run_p2.py','playmode','preview-03','preview'],['run_p2.py','editmode','editmode-03'],['audit_p2.py','final-audit-01'],['export_p2.py','preview-03']]:
 print('STEP',args,flush=True)
 r=subprocess.run([sys.executable,'-X','utf8',str(ROOT/'Tools/InteractionRefinement'/args[0])]+args[1:],cwd=ROOT)
 if r.returncode:sys.exit(r.returncode)
