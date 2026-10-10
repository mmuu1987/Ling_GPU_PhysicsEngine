from common_p7 import *
import sys
for args in [['apply_p7.py','02-preserve-fixture-buffers'],['run_p7.py','playmode','selection-02','selection','gui']]:
 print('STEP',args,flush=True);r=subprocess.run([sys.executable,'-X','utf8',str(ROOT/'Tools/InteractionRefinement'/args[0])]+args[1:],cwd=ROOT)
 if r.returncode:sys.exit(r.returncode)
