from common_p6 import *
import sys
commands=[['apply_p6.py','05-trace-and-coverage'],['run_p6.py','playmode','local-03','local'],['run_p6.py','playmode','scene-01','local-scene'],['run_p6.py','playmode','legacy-kernels-01','legacy-kernels'],['run_p6.py','editmode','editmode-02']]
for args in commands:
 print('STEP',args,flush=True);r=subprocess.run([sys.executable,'-X','utf8',str(ROOT/'Tools/InteractionRefinement'/args[0])]+args[1:],cwd=ROOT)
 if r.returncode:sys.exit(r.returncode)
