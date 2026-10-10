from common_p6 import *
import sys
commands=[['apply_p6.py','06-scene-cost-and-regressions'],['run_p6.py','playmode','local-04','local'],['run_p6.py','playmode','scene-02','local-scene'],['run_p6.py','playmode','legacy-kernels-02','legacy-kernels'],['run_p6.py','playmode','deployment-01','resize'],['run_p6.py','playmode','critical-01','critical'],['run_p6.py','editmode','editmode-03'],['audit_p6.py','editmode-03','local-04','scene-02','legacy-kernels-02','deployment-01','critical-01']]
for args in commands:
 print('STEP',args,flush=True);r=subprocess.run([sys.executable,'-X','utf8',str(ROOT/'Tools/InteractionRefinement'/args[0])]+args[1:],cwd=ROOT)
 if r.returncode:sys.exit(r.returncode)
