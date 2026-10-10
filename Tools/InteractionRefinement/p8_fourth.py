from common_p8 import *
import sys
for args in [['apply_p8.py','04-rendered-capture-fixture'],['run_p8.py','playmode','scoped-scene-03','scoped-scene','gui'],['run_p8.py','playmode','scoped-kernels-02','scoped-kernels']]:
 print('STEP',args,flush=True);r=subprocess.run([sys.executable,'-X','utf8',str(ROOT/'Tools/InteractionRefinement'/args[0])]+args[1:],cwd=ROOT)
 if r.returncode:sys.exit(r.returncode)
