from common_p7 import *
import sys
commands=[['run_p7.py','playmode','local-01','local'],['run_p7.py','playmode','legacy-kernels-01','legacy-kernels'],['run_p7.py','playmode','deployment-01','resize'],['run_p7.py','playmode','scenarios-01','local-scenarios'],['run_p7.py','editmode','editmode-02'],['audit_p7.py','selection-03','local-01','legacy-kernels-01','deployment-01','scenarios-01','editmode-02']]
for args in commands:
 print('STEP',args,flush=True);r=subprocess.run([sys.executable,'-X','utf8',str(ROOT/'Tools/InteractionRefinement'/args[0])]+args[1:],cwd=ROOT)
 if r.returncode:sys.exit(r.returncode)
