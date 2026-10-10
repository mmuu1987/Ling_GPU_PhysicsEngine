from common_p8 import *
import sys
for args in [['apply_p8.py','02-player-evidence'],['run_p8.py','playmode','scoped-scene-01','scoped-scene','gui'],['run_p8.py','playmode','scoped-kernels-02','scoped-kernels']]:
 print('STEP',args,flush=True);r=subprocess.run([sys.executable,'-X','utf8',str(ROOT/'Tools/InteractionRefinement'/args[0])]+args[1:],cwd=ROOT)
 if r.returncode:sys.exit(r.returncode)
