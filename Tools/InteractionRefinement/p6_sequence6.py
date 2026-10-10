from common_p6 import *
import sys
commands=[['run_p6.py','editmode','editmode-03'],['audit_p6.py','editmode-03','local-04','scene-03','legacy-kernels-02','deployment-01']]
for args in commands:
 print('STEP',args,flush=True);r=subprocess.run([sys.executable,'-X','utf8',str(ROOT/'Tools/InteractionRefinement'/args[0])]+args[1:],cwd=ROOT)
 if r.returncode:sys.exit(r.returncode)
