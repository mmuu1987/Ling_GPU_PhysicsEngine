from common_p8 import *
import sys
steps=[['apply_p8.py','05-terminal-inflight'],['run_p8.py','playmode','scoped-scene-04','scoped-scene','gui'],['run_p8.py','playmode','scoped-kernels-03','scoped-kernels'],['run_p8.py','playmode','selection-01','selection','gui'],['run_p8.py','playmode','local-01','local'],['run_p8.py','playmode','legacy-kernels-01','legacy-kernels'],['run_p8.py','playmode','deployment-01','resize'],['run_p8.py','playmode','scenarios-01','local-scenarios'],['run_p8.py','editmode','editmode-02']]
for args in steps:
 print('STEP',args,flush=True);r=subprocess.run([sys.executable,'-X','utf8',str(ROOT/'Tools/InteractionRefinement'/args[0])]+args[1:],cwd=ROOT)
 if r.returncode:sys.exit(r.returncode)
