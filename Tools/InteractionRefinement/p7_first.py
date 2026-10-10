from common_p7 import *
import sys
for args in [['apply_p7.py','01-selection'],['run_p7.py','editmode','editmode-01'],['run_p7.py','playmode','selection-01','selection','gui']]:
 print('STEP',args,flush=True);r=subprocess.run([sys.executable,'-X','utf8',str(ROOT/'Tools/InteractionRefinement'/args[0])]+args[1:],cwd=ROOT)
 if r.returncode:sys.exit(r.returncode)
