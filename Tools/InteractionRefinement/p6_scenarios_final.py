from common_p6 import *
import sys
commands=[['apply_p6.py','12-rendered-moving-evidence'],['run_p6.py','playmode','rendered-load-02','local-render','gui'],['run_p6.py','playmode','rendered-load-03','local-render','gui','reverse'],['run_p6.py','playmode','scenarios-02','local-scenarios'],['run_p6.py','playmode','local-06','local'],['run_p6.py','playmode','legacy-kernels-03','legacy-kernels'],['run_p6.py','playmode','deployment-02','resize'],['run_p6.py','editmode','editmode-05']]
for args in commands:
 print('STEP',args,flush=True);r=subprocess.run([sys.executable,'-X','utf8',str(ROOT/'Tools/InteractionRefinement'/args[0])]+args[1:],cwd=ROOT)
 if r.returncode:sys.exit(r.returncode)
