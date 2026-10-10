from common_p3 import *
import sys,subprocess
for args in [['run_p3.py','editmode','editmode-01'],['audit_p3.py'],['export_p3_v3.py','causal-01']]:
 print('STEP',args,flush=True)
 r=subprocess.run([sys.executable,'-X','utf8',str(ROOT/'Tools/InteractionRefinement'/args[0])]+args[1:],cwd=ROOT)
 if r.returncode:sys.exit(r.returncode)
