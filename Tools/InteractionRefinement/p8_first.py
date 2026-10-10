from common_p8 import *
import sys
for args in [['apply_p8.py','01-scoped-commands'],['run_p8.py','editmode','editmode-01'],['run_p8.py','playmode','scoped-kernels-01','scoped-kernels']]:
 print('STEP',args,flush=True);r=subprocess.run([sys.executable,'-X','utf8',str(ROOT/'Tools/InteractionRefinement'/args[0])]+args[1:],cwd=ROOT)
 if r.returncode:sys.exit(r.returncode)
